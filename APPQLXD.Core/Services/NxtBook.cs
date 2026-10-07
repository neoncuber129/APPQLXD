using System.Globalization;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace APPQLXD.Core.Services;

public sealed class NxtBook(Func<AppDbContext> factory)
{
    public NxtSheet Build(int year, int quarter, IReadOnlyCollection<Guid> warehouseIds, NxtLotViewMode lotView = NxtLotViewMode.TxSscd)
    {
        if (warehouseIds.Count == 0 || quarter is < 1 or > 4 || year is < 1900 or > 9999)
            return new NxtSheet([], []);

        var start = new DateTime(year, (quarter - 1) * 3 + 1, 1);
        var end = start.AddMonths(3).AddDays(-1);
        var selected = warehouseIds.ToHashSet();
        var (documents, items, lots, _) = Load(end, selected);

        var fuels = new Dictionary<string, FuelCol>(StringComparer.Ordinal);
        var opening = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var slips = new List<SlipEffect>();

        foreach (var doc in documents)
        {
            // Đổi loại lô cùng kho: sổ NXT (TX+SSCĐ xem chung) bỏ qua — net lượng không đổi.
            if (doc.Kind == DocumentKind.LotConvert)
                continue;

            var effect = Effects(doc, selected, items, lots, fuels, FuelKeyMode.Item, lotView);
            if (effect.Count == 0)
                continue;
            if (doc.Kind == DocumentKind.Opening)
            {
                if (doc.DocumentDate.Date <= end)
                    Add(opening, effect);
                continue;
            }

            if (doc.DocumentDate.Date < start)
                Add(opening, effect);
            else if (doc.DocumentDate.Date <= end)
                slips.Add(new SlipEffect(doc, effect));
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, qty) in opening)
        {
            if (qty != 0)
                used.Add(key);
        }

        foreach (var slip in slips)
        {
            foreach (var (key, bucket) in slip.Quantities)
            {
                if (bucket.In != 0 || bucket.Out != 0)
                    used.Add(key);
            }
        }

        var columns = Columns(fuels, used);
        var itemIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].ItemKey is string key)
                itemIndex[key] = i;
        }

        IndexMembers(columns, itemIndex);

        var rows = new List<NxtRow>();
        var running = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var key in used)
            running[key] = opening.GetValueOrDefault(key);

        rows.Add(Row(NxtRowKind.Opening, null, null, "", null, "Mang sang", null, "", Cells(columns, itemIndex, running, null)));

        var periodByItem = new Dictionary<string, (decimal In, decimal Out)>(StringComparer.Ordinal);
        // Cùng ngày: gom tất cả phiếu nhập trước, rồi mới các phiếu xuất/điều chuyển/khác.
        foreach (var slip in slips
            .OrderBy(x => x.Document.DocumentDate.Date)
            .ThenBy(x => x.Document.Kind == DocumentKind.Import ? 0 : 1)
            .ThenBy(x => x.Document.Number, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Document.Id))
        {
            foreach (var (key, bucket) in slip.Quantities)
            {
                var now = periodByItem.GetValueOrDefault(key);
                periodByItem[key] = (QuantityMath.Whole(now.In + bucket.In), QuantityMath.Whole(now.Out + bucket.Out));
                running[key] = QuantityMath.Whole(running.GetValueOrDefault(key) + bucket.In - bucket.Out);
            }

            rows.Add(Row(
                NxtRowKind.Slip,
                slip.Document.Id,
                slip.Document.Kind,
                slip.Document.Number,
                slip.Document.DocumentDate.Date,
                Describe(slip.Document),
                ResolveKilometers(slip.Document),
                MissionOf(slip.Document),
                Cells(columns, itemIndex, running, slip.Quantities),
                VoucherColumnOf(slip.Document.Kind, slip.Quantities, fuels)));
        }

        // Một dòng tổng: Nhập/Xuất phát sinh trong kỳ + Tồn cuối (trước đây tách «Cộng phát sinh» / «Tồn cuối kỳ»).
        rows.Add(Row(NxtRowKind.Period, null, null, "", null, "Cộng mang sang", null, "",
            CongMangSangCells(columns, itemIndex, periodByItem, running)));
        return new NxtSheet(columns.Select(x => new NxtColumn(x.Title, x.IsGroupTotal)).ToList(), rows);
    }

    /// <summary>
    /// NXT tổng kho lớn (mọi kho trong phạm vi): mỗi dòng một mặt hàng + đơn giá, theo nhóm.
    /// Cột tồn đầu / nhập / xuất / tồn sau. Điều chuyển nội bộ không ghi nhập–xuất.
    /// Đổi loại lô: không đưa vào nhập/xuất; tồn sau điều chỉnh và ghi chú tăng/giảm.
    /// </summary>
    public NxtTotalSheet BuildTotal(
        int year,
        int quarter,
        IReadOnlyCollection<Guid> warehouseIds,
        NxtLotViewMode lotView = NxtLotViewMode.All,
        bool includeTransfers = false) =>
        BuildTotalCore(year, quarter, warehouseIds, lotView, includeTransfers);

    /// <summary>
    /// NXT từng kho: cùng dạng bảng NXT tổng; nhập = PN + ĐC vào;
    /// xuất = ĐC ra + tiêu thụ + xuất lẻ (+ tiêu thụ kho phụ); đổi loại chỉ điều chỉnh tồn sau.
    /// </summary>
    public NxtTotalSheet BuildWarehouseTotal(
        int year,
        int quarter,
        Guid warehouseId,
        NxtLotViewMode lotView = NxtLotViewMode.All) =>
        BuildTotalCore(year, quarter, [warehouseId], lotView, includeTransfers: true);

    public enum NxtWhCategory
    {
        Main = 0,
        Machine = 1,
        Vehicle = 2,
        Ship = 3
    }

    private NxtTotalSheet BuildTotalCore(
        int year,
        int quarter,
        IReadOnlyCollection<Guid> warehouseIds,
        NxtLotViewMode lotView,
        bool includeTransfers)
    {
        if (warehouseIds.Count == 0 || quarter is < 1 or > 4 || year is < 1900 or > 9999)
            return new NxtTotalSheet([]);

        var start = new DateTime(year, (quarter - 1) * 3 + 1, 1);
        var end = start.AddMonths(3).AddDays(-1);
        var selected = warehouseIds.ToHashSet();
        var (documents, items, lots, consumers) = Load(end, selected);

        var fuels = new Dictionary<string, FuelCol>(StringComparer.Ordinal);
        var opening = new Dictionary<(string Key, NxtWhCategory Cat), decimal>();
        var periodIn = new Dictionary<(string Key, NxtWhCategory Cat), decimal>();
        var periodOut = new Dictionary<(string Key, NxtWhCategory Cat), decimal>();
        var totalIn = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var totalOut = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var convertAdjust = new Dictionary<(string Key, NxtWhCategory Cat), decimal>();
        var convertNoteQty = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var convertNoteCodes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        static void AddTotal(Dictionary<string, decimal> map, string key, decimal qty)
        {
            qty = QuantityMath.Whole(qty);
            if (qty == 0) return;
            map[key] = QuantityMath.Whole(map.GetValueOrDefault(key) + qty);
        }

        foreach (var doc in documents)
        {
            var isOpeningDoc = doc.Kind == DocumentKind.Opening;
            var isBeforeStart = doc.DocumentDate.Date < start;
            var isWithinQuarter = doc.DocumentDate.Date >= start && doc.DocumentDate.Date <= end;

            foreach (var line in doc.Lines)
            {
                var fuel = Resolve(line, items, lots, FuelKeyMode.ItemPrice);
                if (!MatchesLotView(fuel.LotTypeCode, lotView))
                    continue;
                fuels.TryAdd(fuel.Key, fuel);

                var srcSelected = selected.Contains(line.WarehouseId);
                var srcCat = GetWhCategory(line.WarehouseId, consumers);
                var qty = QuantityMath.Whole(line.ActualQuantity);

                if (isOpeningDoc)
                {
                    if (doc.DocumentDate.Date <= end && srcSelected)
                    {
                        AddCat(opening, fuel.Key, srcCat, qty);
                    }
                    continue;
                }

                if (isBeforeStart)
                {
                    switch (doc.Kind)
                    {
                        case DocumentKind.Import:
                            if (srcSelected) AddCat(opening, fuel.Key, srcCat, qty);
                            break;
                        case DocumentKind.Issue:
                        case DocumentKind.Consumption:
                        case DocumentKind.Auxiliary:
                            if (srcSelected) AddCat(opening, fuel.Key, srcCat, -qty);
                            break;
                        case DocumentKind.Transfer:
                            if (srcSelected) AddCat(opening, fuel.Key, srcCat, -qty);
                            if (doc.DestinationWarehouseId is Guid destWhId && selected.Contains(destWhId))
                            {
                                var destCat = GetWhCategory(destWhId, consumers);
                                var destFuel = ResolveDestination(line, fuel, items, lots, FuelKeyMode.ItemPrice);
                                if (MatchesLotView(destFuel.LotTypeCode, lotView))
                                {
                                    fuels.TryAdd(destFuel.Key, destFuel);
                                    AddCat(opening, destFuel.Key, destCat, qty);
                                }
                            }
                            break;
                        case DocumentKind.LotConvert:
                            if (srcSelected)
                            {
                                AddCat(opening, fuel.Key, srcCat, -qty);
                                var destFuel = ResolveDestination(line, fuel, items, lots, FuelKeyMode.ItemPrice);
                                if (MatchesLotView(destFuel.LotTypeCode, lotView))
                                {
                                    fuels.TryAdd(destFuel.Key, destFuel);
                                    AddCat(opening, destFuel.Key, srcCat, qty);
                                }
                            }
                            break;
                    }
                    continue;
                }

                if (!isWithinQuarter)
                    continue;

                // Trong quý:
                switch (doc.Kind)
                {
                    case DocumentKind.Import:
                        if (srcSelected)
                        {
                            AddCat(periodIn, fuel.Key, srcCat, qty);
                            AddTotal(totalIn, fuel.Key, qty);
                        }
                        break;
                    case DocumentKind.Issue:
                    case DocumentKind.Consumption:
                    case DocumentKind.Auxiliary:
                        if (srcSelected)
                        {
                            AddCat(periodOut, fuel.Key, srcCat, qty);
                            AddTotal(totalOut, fuel.Key, qty);
                        }
                        break;
                    case DocumentKind.Transfer:
                    {
                        var dstWhId = doc.DestinationWarehouseId;
                        var dstSelected = dstWhId is Guid dId && selected.Contains(dId);
                        FuelCol? destFuel = null;
                        if (dstSelected)
                        {
                            var df = ResolveDestination(line, fuel, items, lots, FuelKeyMode.ItemPrice);
                            if (MatchesLotView(df.LotTypeCode, lotView))
                            {
                                destFuel = df;
                                fuels.TryAdd(destFuel.Key, destFuel);
                                var destCat = GetWhCategory(dstWhId!.Value, consumers);
                                AddCat(periodIn, destFuel.Key, destCat, qty);
                            }
                        }
                        if (srcSelected)
                        {
                            AddCat(periodOut, fuel.Key, srcCat, qty);
                        }

                        // Cột Tổng (toàn đơn vị):
                        if (srcSelected && !dstSelected)
                        {
                            AddTotal(totalOut, fuel.Key, qty);
                        }
                        else if (!srcSelected && dstSelected && destFuel != null)
                        {
                            AddTotal(totalIn, destFuel.Key, qty);
                        }
                        else if (srcSelected && dstSelected && destFuel != null && !string.Equals(destFuel.Key, fuel.Key, StringComparison.Ordinal))
                        {
                            // Đổi loại lô qua ĐC nội bộ
                            AddTotal(totalOut, fuel.Key, qty);
                            AddTotal(totalIn, destFuel.Key, qty);
                        }
                        break;
                    }
                    case DocumentKind.LotConvert:
                        if (srcSelected)
                        {
                            var destFuel = ResolveDestination(line, fuel, items, lots, FuelKeyMode.ItemPrice);
                            fuels.TryAdd(destFuel.Key, destFuel);
                            AddCat(convertAdjust, fuel.Key, srcCat, -qty);
                            AddCat(convertAdjust, destFuel.Key, srcCat, qty);
                            AccrueConvertNote(convertNoteQty, convertNoteCodes, fuel.Key, -qty, destFuel.LotTypeCode);
                            AccrueConvertNote(convertNoteQty, convertNoteCodes, destFuel.Key, qty, fuel.LotTypeCode);
                        }
                        break;
                }
            }
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ((k, _), qty) in opening) if (qty != 0) keys.Add(k);
        foreach (var ((k, _), qty) in periodIn) if (qty != 0) keys.Add(k);
        foreach (var ((k, _), qty) in periodOut) if (qty != 0) keys.Add(k);
        foreach (var ((k, _), qty) in convertAdjust) if (qty != 0) keys.Add(k);

        var rows = keys
            .Select(key =>
            {
                var fuel = fuels[key];
                var openMain = opening.GetValueOrDefault((key, NxtWhCategory.Main));
                var openMach = opening.GetValueOrDefault((key, NxtWhCategory.Machine));
                var openVeh = opening.GetValueOrDefault((key, NxtWhCategory.Vehicle));
                var openShip = opening.GetValueOrDefault((key, NxtWhCategory.Ship));
                var openTotal = QuantityMath.Whole(openMain + openMach + openVeh + openShip);

                var inMain = periodIn.GetValueOrDefault((key, NxtWhCategory.Main));
                var inMach = periodIn.GetValueOrDefault((key, NxtWhCategory.Machine));
                var inVeh = periodIn.GetValueOrDefault((key, NxtWhCategory.Vehicle));
                var inShip = periodIn.GetValueOrDefault((key, NxtWhCategory.Ship));
                var inTotal = totalIn.GetValueOrDefault(key);

                var outMain = periodOut.GetValueOrDefault((key, NxtWhCategory.Main));
                var outMach = periodOut.GetValueOrDefault((key, NxtWhCategory.Machine));
                var outVeh = periodOut.GetValueOrDefault((key, NxtWhCategory.Vehicle));
                var outShip = periodOut.GetValueOrDefault((key, NxtWhCategory.Ship));
                var outTotal = totalOut.GetValueOrDefault(key);

                var adjMain = convertAdjust.GetValueOrDefault((key, NxtWhCategory.Main));
                var adjMach = convertAdjust.GetValueOrDefault((key, NxtWhCategory.Machine));
                var adjVeh = convertAdjust.GetValueOrDefault((key, NxtWhCategory.Vehicle));
                var adjShip = convertAdjust.GetValueOrDefault((key, NxtWhCategory.Ship));

                var closeMain = QuantityMath.Whole(openMain + inMain - outMain + adjMain);
                var closeMach = QuantityMath.Whole(openMach + inMach - outMach + adjMach);
                var closeVeh = QuantityMath.Whole(openVeh + inVeh - outVeh + adjVeh);
                var closeShip = QuantityMath.Whole(openShip + inShip - outShip + adjShip);
                var closeTotal = QuantityMath.Whole(closeMain + closeMach + closeVeh + closeShip);

                return new NxtTotalRow(
                    fuel.GroupName,
                    fuel.Name,
                    fuel.UnitPrice,
                    fuel.LotTypeId,
                    fuel.LotTypeCode,
                    openMain, openMach, openVeh, openShip, openTotal,
                    inMain, inMach, inVeh, inShip, inTotal,
                    outMain, outMach, outVeh, outShip, outTotal,
                    closeMain, closeMach, closeVeh, closeShip, closeTotal,
                    Note: FormatConvertNote(
                        convertNoteQty.GetValueOrDefault(key),
                        convertNoteCodes.TryGetValue(key, out var codes) ? codes : null));
            })
            .OrderBy(x => GroupRank(x.GroupName))
            .ThenBy(x => x.GroupName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.ItemName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.UnitPrice)
            .ThenBy(x => x.LotTypeCode, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new NxtTotalSheet(rows);
    }

    private static NxtWhCategory GetWhCategory(Guid warehouseId, Dictionary<Guid, ConsumerType> consumers)
    {
        if (consumers.TryGetValue(warehouseId, out var type))
        {
            return type switch
            {
                ConsumerType.Machine => NxtWhCategory.Machine,
                ConsumerType.Vehicle => NxtWhCategory.Vehicle,
                ConsumerType.Ship => NxtWhCategory.Ship,
                _ => NxtWhCategory.Main
            };
        }
        return NxtWhCategory.Main;
    }

    private static void AddCat(
        Dictionary<(string Key, NxtWhCategory Cat), decimal> map,
        string key,
        NxtWhCategory cat,
        decimal qty)
    {
        qty = QuantityMath.Whole(qty);
        if (qty == 0) return;
        map[(key, cat)] = QuantityMath.Whole(map.GetValueOrDefault((key, cat)) + qty);
    }

    private (List<NxtDoc> Documents, Dictionary<Guid, FuelItem> Items, Dictionary<Guid, Lot> Lots, Dictionary<Guid, ConsumerType> Consumers) Load(
        DateTime end,
        HashSet<Guid> warehouseIds)
    {
        using var db = factory();
        var items = db.FuelItems.AsNoTracking().Include(x => x.Group).ToDictionary(x => x.Id);
        var lots = db.Lots.AsNoTracking().Include(x => x.LotType).ToDictionary(x => x.Id);
        var consumers = db.Consumers.AsNoTracking().ToDictionary(x => x.Id, x => x.Type);
        var endExclusive = end.AddDays(1);
        var selected = warehouseIds.ToList();
        var flat = (
            from doc in db.Documents.AsNoTracking()
            where doc.Status == DocumentStatus.Active && doc.DocumentDate < endExclusive
            join line in db.DocumentLines.AsNoTracking() on doc.Id equals line.DocumentId
            where selected.Contains(line.WarehouseId)
                || (doc.DestinationWarehouseId != null && selected.Contains(doc.DestinationWarehouseId.Value))
            select new
            {
                doc.Id,
                doc.Kind,
                doc.DocumentDate,
                doc.Number,
                doc.Nature,
                doc.Mission,
                doc.Distance,
                doc.Kilometers,
                doc.DestinationWarehouseId,
                line.LotId,
                line.ItemId,
                line.ItemName,
                line.UnitPrice,
                line.WarehouseId,
                line.ActualQuantity,
                line.LotTypeId,
                line.LotTypeCode,
                line.DestinationLotTypeId,
                line.DestinationLotTypeCode
            }).ToList();
        var documents = flat.GroupBy(x => x.Id).Select(group =>
        {
            var head = group.First();
            return new NxtDoc(
                head.Id, head.Kind, head.DocumentDate, head.Number, head.Nature, head.Mission, head.Distance, head.Kilometers, head.DestinationWarehouseId,
                group.Select(x => new NxtLine(
                    x.LotId, x.ItemId, x.ItemName, x.UnitPrice, x.WarehouseId, x.ActualQuantity,
                    x.LotTypeId, x.LotTypeCode ?? "", x.DestinationLotTypeId, x.DestinationLotTypeCode ?? "")).ToList());
        }).ToList();
        return (documents, items, lots, consumers);
    }

    private static List<Col> Columns(Dictionary<string, FuelCol> fuels, HashSet<string> used)
    {
        var visible = fuels.Values.Where(x => used.Contains(x.Key))
            .OrderBy(x => GroupRank(x.GroupName))
            .ThenBy(x => x.GroupName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var columns = new List<Col>();
        foreach (var group in visible.GroupBy(x => x.GroupName))
        {
            var members = group.ToList();
            foreach (var item in members)
                columns.Add(new Col(item.Key, item.Name, false));
            if (members.Count >= 2)
                columns.Add(new Col(null, string.IsNullOrWhiteSpace(group.Key) ? "Khác" : group.Key, true) { Members = members.Select(x => x.Key).ToList() });
        }

        return columns;
    }

    private static NxtRow Row(
        NxtRowKind kind,
        Guid? id,
        DocumentKind? documentKind,
        string number,
        DateTime? date,
        string description,
        decimal? kilometers,
        string mission,
        IReadOnlyList<NxtCell> cells,
        NxtVoucherColumn voucherColumn = NxtVoucherColumn.None) =>
        new(kind, id, documentKind, number, date, description, kilometers, mission, cells, voucherColumn);

    private static NxtVoucherColumn VoucherColumnOf(
        DocumentKind kind,
        Dictionary<string, Bucket> quantities,
        Dictionary<string, FuelCol> fuels)
    {
        if (kind == DocumentKind.Import)
            return NxtVoucherColumn.N;

        // XX: phiếu xuất (và các phiếu trừ/chuyển) có chứa xăng.
        var hasGasoline = quantities.Keys.Any(key =>
            fuels.TryGetValue(key, out var fuel)
            && fuel.GroupName.Equals("Xăng", StringComparison.OrdinalIgnoreCase));
        return hasGasoline ? NxtVoucherColumn.Xx : NxtVoucherColumn.Xd;
    }

    private static List<NxtCell> Cells(List<Col> columns, Dictionary<string, int> itemIndex, Dictionary<string, decimal> balance, Dictionary<string, Bucket>? movement)
    {
        var cells = columns.Select(_ => new NxtCell(null, null, null)).ToList();
        foreach (var (key, index) in itemIndex)
        {
            decimal? inn = null;
            decimal? outQty = null;
            if (movement is not null && movement.TryGetValue(key, out var bucket))
            {
                if (bucket.In != 0)
                    inn = bucket.In;
                if (bucket.Out != 0)
                    outQty = bucket.Out;
            }

            cells[index] = new NxtCell(inn, outQty, balance.GetValueOrDefault(key));
        }

        FillTotals(columns, cells, blankBalance: false);
        return cells;
    }

    private static List<NxtCell> CongMangSangCells(
        List<Col> columns,
        Dictionary<string, int> itemIndex,
        Dictionary<string, (decimal In, decimal Out)> period,
        Dictionary<string, decimal> closing)
    {
        var cells = columns.Select(_ => new NxtCell(0, 0, 0)).ToList();
        foreach (var (key, index) in itemIndex)
        {
            var value = period.GetValueOrDefault(key);
            cells[index] = new NxtCell(value.In, value.Out, closing.GetValueOrDefault(key));
        }

        FillTotals(columns, cells, blankBalance: false);
        return cells;
    }

    private static void FillTotals(List<Col> columns, List<NxtCell> cells, bool blankBalance)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (!columns[i].IsGroupTotal)
                continue;
            decimal? inn = null;
            decimal? outQty = null;
            decimal? balance = blankBalance ? null : 0;
            foreach (var index in columns[i].MemberIndexes)
            {
                var cell = cells[index];
                inn = Add(inn, cell.In);
                outQty = Add(outQty, cell.Out);
                if (!blankBalance)
                    balance = QuantityMath.Whole((balance ?? 0) + (cell.Balance ?? 0));
            }

            if (blankBalance)
            {
                inn ??= 0;
                outQty ??= 0;
            }

            cells[i] = new NxtCell(inn, outQty, balance);
        }
    }

    private static decimal? Add(decimal? left, decimal? right)
    {
        if (left is null && right is null)
            return null;
        return QuantityMath.Whole((left ?? 0) + (right ?? 0));
    }

    private static Dictionary<string, Bucket> Effects(
        NxtDoc doc,
        HashSet<Guid> selected,
        Dictionary<Guid, FuelItem> items,
        Dictionary<Guid, Lot> lots,
        Dictionary<string, FuelCol> fuels,
        FuelKeyMode keyMode,
        NxtLotViewMode lotView)
    {
        var map = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        foreach (var line in doc.Lines)
        {
            var fuel = Resolve(line, items, lots, keyMode);
            if (!MatchesLotView(fuel.LotTypeCode, lotView))
                continue;
            fuels.TryAdd(fuel.Key, fuel);
            switch (doc.Kind)
            {
                case DocumentKind.Opening:
                case DocumentKind.Import:
                    Add(map, selected, fuel, line.WarehouseId, line.ActualQuantity);
                    break;
                case DocumentKind.Issue:
                case DocumentKind.Consumption:
                case DocumentKind.Auxiliary:
                    Add(map, selected, fuel, line.WarehouseId, -line.ActualQuantity);
                    break;
                case DocumentKind.Transfer:
                    // ĐC ra kho nguồn; ĐC vào kho đích (đúng loại lô đích nếu đổi loại).
                    Add(map, selected, fuel, line.WarehouseId, -line.ActualQuantity);
                    if (doc.DestinationWarehouseId is Guid destination)
                    {
                        var destFuel = ResolveDestination(line, fuel, items, lots, keyMode);
                        if (!MatchesLotView(destFuel.LotTypeCode, lotView))
                            break;
                        fuels.TryAdd(destFuel.Key, destFuel);
                        Add(map, selected, destFuel, destination, line.ActualQuantity);
                    }
                    break;
                case DocumentKind.LotConvert:
                {
                    Add(map, selected, fuel, line.WarehouseId, -line.ActualQuantity);
                    var destFuel = ResolveDestination(line, fuel, items, lots, keyMode);
                    if (!MatchesLotView(destFuel.LotTypeCode, lotView))
                        break;
                    fuels.TryAdd(destFuel.Key, destFuel);
                    Add(map, selected, destFuel, line.WarehouseId, line.ActualQuantity);
                    break;
                }
            }
        }

        return map;
    }

    private static void RememberLotConvertNotes(
        NxtDoc doc,
        Dictionary<string, Bucket> effect,
        Dictionary<Guid, FuelItem> items,
        Dictionary<Guid, Lot> lots,
        Dictionary<string, decimal> noteQty,
        Dictionary<string, HashSet<string>> noteCodes)
    {
        foreach (var line in doc.Lines)
        {
            var source = Resolve(line, items, lots, FuelKeyMode.ItemPrice);
            var dest = ResolveDestination(line, source, items, lots, FuelKeyMode.ItemPrice);
            var qty = QuantityMath.Whole(line.ActualQuantity);
            if (qty <= 0)
                continue;
            if (effect.TryGetValue(source.Key, out var srcBucket) && srcBucket.Out != 0)
                AccrueConvertNote(noteQty, noteCodes, source.Key, -qty, dest.LotTypeCode);
            if (effect.TryGetValue(dest.Key, out var destBucket) && destBucket.In != 0)
                AccrueConvertNote(noteQty, noteCodes, dest.Key, qty, source.LotTypeCode);
        }
    }

    private static void AccrueConvertNote(
        Dictionary<string, decimal> noteQty,
        Dictionary<string, HashSet<string>> noteCodes,
        string key,
        decimal signedQty,
        string otherLotTypeCode)
    {
        noteQty[key] = QuantityMath.Whole(noteQty.GetValueOrDefault(key) + signedQty);
        var code = string.IsNullOrWhiteSpace(otherLotTypeCode) ? "?" : otherLotTypeCode.Trim();
        if (!noteCodes.TryGetValue(key, out var codes))
        {
            codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            noteCodes[key] = codes;
        }

        codes.Add(code);
    }

    private static string FormatConvertNote(decimal signedNet, IReadOnlyCollection<string>? otherCodes)
    {
        if (signedNet == 0)
            return "";
        var abs = QuantityMath.Whole(Math.Abs(signedNet));
        var qtyText = abs.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"));
        var via = otherCodes is { Count: > 0 }
            ? " " + string.Join("/", otherCodes.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
            : "";
        return signedNet < 0
            ? $"Tồn sau giảm {qtyText} do đổi →{via}"
            : $"Tồn sau tăng {qtyText} do đổi ←{via}";
    }

    private static FuelCol ResolveDestination(
        NxtLine line,
        FuelCol source,
        Dictionary<Guid, FuelItem> items,
        Dictionary<Guid, Lot> lots,
        FuelKeyMode keyMode)
    {
        var destTypeId = line.DestinationLotTypeId is Guid id && id != Guid.Empty ? id : source.LotTypeId;
        var destCode = !string.IsNullOrWhiteSpace(line.DestinationLotTypeCode)
            ? line.DestinationLotTypeCode.Trim()
            : source.LotTypeCode;
        if (destTypeId == source.LotTypeId && string.Equals(destCode, source.LotTypeCode, StringComparison.OrdinalIgnoreCase))
            return source;

        // Tạo key cùng mặt hàng/đơn giá nhưng loại đích.
        var fake = new NxtLine(
            line.LotId,
            line.ItemId,
            line.ItemName,
            line.UnitPrice,
            line.WarehouseId,
            line.ActualQuantity,
            destTypeId,
            destCode,
            destTypeId,
            destCode);
        // Resolve ưu tiên LotType trên line khi có — override qua lots nếu cùng LotId sẽ sai.
        return ResolveWithLotType(fake, items, lots, keyMode, destTypeId, destCode);
    }

    private static FuelCol ResolveWithLotType(
        NxtLine line,
        Dictionary<Guid, FuelItem> items,
        Dictionary<Guid, Lot> lots,
        FuelKeyMode keyMode,
        Guid lotTypeId,
        string lotTypeCode)
    {
        Guid? itemId = line.ItemId;
        var name = line.ItemName?.Trim() ?? "";
        var group = "";
        Guid groupId = Guid.Empty;
        long price = line.UnitPrice;
        if (lots.TryGetValue(line.LotId, out var lot))
        {
            itemId ??= lot.ItemId;
            if (name.Length == 0)
                name = lot.ItemName.Trim();
            group = lot.GroupName?.Trim() ?? "";
            if (price == 0)
                price = lot.UnitPrice;
        }

        if (itemId is Guid id && items.TryGetValue(id, out var item))
        {
            name = item.Name.Trim();
            groupId = item.GroupId;
            group = item.Group?.Name?.Trim() ?? group;
            if (keyMode == FuelKeyMode.Item)
                return new FuelCol(id.ToString(), name, group, groupId, 0, lotTypeId, lotTypeCode);
            if (keyMode == FuelKeyMode.ItemPrice)
                return new FuelCol(id.ToString("N") + ":" + price + ":" + lotTypeId.ToString("N"), name, group, groupId, price, lotTypeId, lotTypeCode);
        }

        if (name.Length == 0)
            name = "Không tên";

        if (keyMode == FuelKeyMode.ItemPrice)
            return new FuelCol("n:" + name.ToUpperInvariant() + ":" + price + ":" + lotTypeId.ToString("N"), name, group, groupId, price, lotTypeId, lotTypeCode);

        return new FuelCol("n:" + name.ToUpperInvariant(), name, group, groupId, 0, lotTypeId, lotTypeCode);
    }

    private static bool MatchesLotView(string lotTypeCode, NxtLotViewMode mode)
    {
        if (mode == NxtLotViewMode.All)
            return true;
        var code = (lotTypeCode ?? "TX").Trim().ToUpperInvariant();
        return mode switch
        {
            NxtLotViewMode.Iuu => code is "IUU",
            _ => code is "TX" or "SSCĐ" or "SSCD"
        };
    }

    private static void Add(Dictionary<string, Bucket> map, HashSet<Guid> selected, FuelCol fuel, Guid warehouseId, decimal signed)
    {
        if (!selected.Contains(warehouseId))
            return;
        signed = QuantityMath.Whole(signed);
        if (signed == 0)
            return;
        if (!map.TryGetValue(fuel.Key, out var bucket))
        {
            bucket = new Bucket();
            map[fuel.Key] = bucket;
        }

        if (signed > 0)
            bucket.In = QuantityMath.Whole(bucket.In + signed);
        else
            bucket.Out = QuantityMath.Whole(bucket.Out - signed);
    }

    private static void Add(Dictionary<string, decimal> opening, Dictionary<string, Bucket> effect)
    {
        foreach (var (key, bucket) in effect)
            opening[key] = QuantityMath.Whole(opening.GetValueOrDefault(key) + bucket.In - bucket.Out);
    }

    private static FuelCol Resolve(NxtLine line, Dictionary<Guid, FuelItem> items, Dictionary<Guid, Lot> lots, FuelKeyMode keyMode)
    {
        Guid? itemId = line.ItemId;
        var name = line.ItemName?.Trim() ?? "";
        var group = "";
        Guid groupId = Guid.Empty;
        long price = line.UnitPrice;
        var lotTypeId = SeedIds.LotTypeTx;
        var lotTypeCode = "TX";
        if (lots.TryGetValue(line.LotId, out var lot))
        {
            itemId ??= lot.ItemId;
            if (name.Length == 0)
                name = lot.ItemName.Trim();
            group = lot.GroupName?.Trim() ?? "";
            if (price == 0)
                price = lot.UnitPrice;
            lotTypeId = lot.LotTypeId;
            if (lot.LotType is not null && lot.LotType.Code.Length > 0)
                lotTypeCode = lot.LotType.Code;
        }

        if (line.LotTypeId is Guid lineTypeId && lineTypeId != Guid.Empty)
            lotTypeId = lineTypeId;
        if (!string.IsNullOrWhiteSpace(line.LotTypeCode))
            lotTypeCode = line.LotTypeCode.Trim();

        if (itemId is Guid id && items.TryGetValue(id, out var item))
        {
            name = item.Name.Trim();
            groupId = item.GroupId;
            group = item.Group?.Name?.Trim() ?? group;
            if (keyMode == FuelKeyMode.Item)
                return new FuelCol(id.ToString(), name, group, groupId, 0, lotTypeId, lotTypeCode);
            if (keyMode == FuelKeyMode.ItemPrice)
                return new FuelCol(id.ToString("N") + ":" + price + ":" + lotTypeId.ToString("N"), name, group, groupId, price, lotTypeId, lotTypeCode);
        }

        if (name.Length == 0)
            name = "Không tên";

        if (keyMode == FuelKeyMode.Lot)
        {
            var key = line.LotId != Guid.Empty
                ? "l:" + line.LotId.ToString("N")
                : "n:" + name.ToUpperInvariant() + ":" + price + ":" + lotTypeId.ToString("N");
            var title = price > 0 ? $"{name} ({price:N0}) [{lotTypeCode}]" : $"{name} [{lotTypeCode}]";
            return new FuelCol(key, title, group, groupId, price, lotTypeId, lotTypeCode);
        }

        if (keyMode == FuelKeyMode.ItemPrice)
            return new FuelCol("n:" + name.ToUpperInvariant() + ":" + price + ":" + lotTypeId.ToString("N"), name, group, groupId, price, lotTypeId, lotTypeCode);

        return new FuelCol("n:" + name.ToUpperInvariant(), name, group, groupId, 0, lotTypeId, lotTypeCode);
    }

    private static void IndexMembers(List<Col> columns, Dictionary<string, int> itemIndex)
    {
        foreach (var column in columns)
        {
            if (!column.IsGroupTotal)
                continue;
            column.MemberIndexes = column.Members
                .Select(key => itemIndex.TryGetValue(key, out var index) ? index : -1)
                .Where(index => index >= 0)
                .ToArray();
        }
    }

    private static string Describe(NxtDoc doc) => doc.Nature?.Trim() ?? "";

    private static string MissionOf(NxtDoc doc) => doc.Mission?.Trim() ?? "";

    /// <summary>Ưu tiên Distance (ĐC/xuất xe); fallback chuỗi Kilometers trên mặt phiếu.</summary>
    private static decimal? ResolveKilometers(NxtDoc doc)
    {
        if (doc.Distance is decimal distance && distance >= 0)
            return QuantityMath.RoundQty(distance);
        var text = doc.Kilometers?.Trim() ?? "";
        if (text.Length == 0)
            return null;
        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var km)
            || decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out km))
        {
            if (km >= 0)
                return QuantityMath.RoundQty(km);
        }

        return null;
    }

    private static int GroupRank(string name) => name switch
    {
        "Dầu" => 0,
        "Xăng" => 1,
        "Nhớt" => 2,
        "Mỡ" => 3,
        _ => 9
    };

    private enum FuelKeyMode
    {
        Item,
        ItemPrice,
        Lot
    }

    private sealed class Bucket
    {
        public decimal In;
        public decimal Out;
    }

    private sealed record FuelCol(string Key, string Name, string GroupName, Guid GroupId, long UnitPrice, Guid LotTypeId = default, string LotTypeCode = "TX");

    private sealed class Col
    {
        public Col(string? itemKey, string title, bool isGroupTotal)
        {
            ItemKey = itemKey;
            Title = title;
            IsGroupTotal = isGroupTotal;
        }

        public string? ItemKey { get; }
        public string Title { get; }
        public bool IsGroupTotal { get; }
        public List<string> Members { get; init; } = [];
        public int[] MemberIndexes { get; set; } = [];
    }

    private sealed record NxtLine(
        Guid LotId,
        Guid? ItemId,
        string? ItemName,
        long UnitPrice,
        Guid WarehouseId,
        decimal ActualQuantity,
        Guid? LotTypeId = null,
        string LotTypeCode = "",
        Guid? DestinationLotTypeId = null,
        string DestinationLotTypeCode = "");

    private sealed record NxtDoc(
        Guid Id,
        DocumentKind Kind,
        DateTime DocumentDate,
        string Number,
        string? Nature,
        string? Mission,
        decimal? Distance,
        string? Kilometers,
        Guid? DestinationWarehouseId,
        IReadOnlyList<NxtLine> Lines);

    private sealed record SlipEffect(NxtDoc Document, Dictionary<string, Bucket> Quantities);
}
