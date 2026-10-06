using System.Globalization;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace APPQLXD.Core.Services;

public sealed class ShipQuarterBooks(Func<AppDbContext> factory)
{
    public ShipQuarterBookDto GetBook(DateTime quarterDate, Guid shipWarehouseId)
    {
        using var db = factory();
        var (year, quarter, start, end) = QuarterBounds(quarterDate);
        var consumer = db.Consumers.AsNoTracking().FirstOrDefault(x => x.Id == shipWarehouseId)
            ?? throw new FuelRuleException("Không tìm thấy tàu.");
        if (consumer.Type != ConsumerType.Ship)
            throw new FuelRuleException("Đối tượng không phải tàu.");

        var factors = db.ConsumerNormFactors.AsNoTracking()
            .Where(x => x.ConsumerId == shipWarehouseId)
            .OrderBy(x => x.SortOrder)
            .ToList();
        var rates = ShipNormSlots.Labels.ToDictionary(
            label => label,
            label => factors.FirstOrDefault(x => x.Name.Equals(label, StringComparison.OrdinalIgnoreCase))?.Value ?? 0m,
            StringComparer.OrdinalIgnoreCase);

        var groups = db.ItemGroups.AsNoTracking().ToDictionary(x => x.Id, x => x.Name);
        var fuelGroupName = ResolveFuelGroupName(db, consumer, groups, shipWarehouseId, start, end);
        var fuelIsGasoline = fuelGroupName.Equals("Xăng", StringComparison.OrdinalIgnoreCase);

        var items = db.FuelItems.AsNoTracking().Include(x => x.Group).ToDictionary(x => x.Id);
        var lots = db.Lots.AsNoTracking().ToDictionary(x => x.Id);
        var (fuelOpening, oilOpening, fuelOpenLots, oilOpenLots) =
            OpeningBalances(db, shipWarehouseId, start, end, fuelGroupName, items, lots);

        var book = db.ShipQuarterBooks.AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefault(x => x.ConsumerId == shipWarehouseId && x.Year == year && x.Quarter == quarter);

        var fuelLots = new Dictionary<string, decimal>(fuelOpenLots, StringComparer.OrdinalIgnoreCase);
        var oilLots = new Dictionary<string, decimal>(oilOpenLots, StringComparer.OrdinalIgnoreCase);
        var rows = new List<ShipQuarterBookRowDto>
        {
            OpeningRow(fuelOpening, oilOpening, LotBalanceTips.Format(fuelLots), LotBalanceTips.Format(oilLots))
        };

        var chron = new List<(DateTime Date, int Tie, bool IsTransfer, decimal FuelIn, decimal OilIn, string LotCode, ShipQuarterBookRowDto? Transfer, ShipQuarterBookLine? Line)>();
        var transferIndex = 0;
        foreach (var transfer in InboundTransfers(db, shipWarehouseId, start, end, items, lots, fuelGroupName))
        {
            chron.Add((
                transfer.Row.DocumentDate ?? DateTime.MaxValue,
                transferIndex++,
                true,
                transfer.FuelIn,
                transfer.OilIn,
                transfer.Row.LotTypeCode,
                transfer.Row,
                null));
        }

        var saved = book?.Lines.OrderBy(x => x.SortOrder).ThenBy(x => x.DocumentDate).ToList() ?? [];
        var lineIndex = 0;
        foreach (var line in saved)
        {
            chron.Add((
                line.DocumentDate ?? DateTime.MaxValue,
                1_000_000 + lineIndex++,
                false,
                0,
                0,
                string.IsNullOrWhiteSpace(line.LotTypeCode) ? "TX" : line.LotTypeCode,
                null,
                line));
        }

        decimal fuelBal = fuelOpening;
        decimal oilBal = oilOpening;
        // Cùng ngày: tất cả phiếu nhập (ĐC) trước, rồi tiêu thụ.
        foreach (var item in chron.OrderBy(x => x.Date.Date).ThenBy(x => x.IsTransfer ? 0 : 1).ThenBy(x => x.Tie))
        {
            if (item.IsTransfer && item.Transfer is not null)
            {
                fuelBal = QuantityMath.Whole(fuelBal + item.FuelIn);
                oilBal = QuantityMath.RoundQty(oilBal + item.OilIn);
                // ĐC có thể nhiều loại lô (TX,SSCĐ) — phân bổ tip theo mã ghép nếu có.
                ApplyShipTransferLotTips(fuelLots, oilLots, item.Transfer);
                rows.Add(item.Transfer with
                {
                    FuelBalance = fuelBal == 0 && item.FuelIn == 0 ? null : fuelBal,
                    OilBalance = oilBal == 0 && item.OilIn == 0 ? null : oilBal,
                    RowTotal = NullIfZero(QuantityMath.RoundQty(item.FuelIn + item.OilIn)),
                    FuelBalanceLotTip = LotBalanceTips.Format(fuelLots),
                    OilBalanceLotTip = LotBalanceTips.Format(oilLots)
                });
                continue;
            }

            if (item.Line is null)
                continue;
            var fuelOut = ResolveFuelOut(item.Line, rates);
            var oilOut = ResolveOilOut(item.Line, fuelOut);
            fuelBal = QuantityMath.Whole(fuelBal - fuelOut);
            oilBal = QuantityMath.RoundQty(oilBal - oilOut);
            LotBalanceTips.Add(fuelLots, item.LotCode, -fuelOut);
            LotBalanceTips.Add(oilLots, item.LotCode, -oilOut);
            rows.Add(ConsumptionRow(item.Line, consumer.MainMachineCount, consumer.AuxMachineCount, rates, fuelIsGasoline, fuelOut, oilOut, fuelBal, oilBal,
                LotBalanceTips.Format(fuelLots), LotBalanceTips.Format(oilLots)));
        }

        return new ShipQuarterBookDto(
            BookId: book?.Id,
            ConsumerId: consumer.Id,
            ShipName: consumer.Name,
            ShipType: string.IsNullOrWhiteSpace(consumer.ShipType) ? consumer.Code : consumer.ShipType.Trim(),
            FuelUsed: fuelGroupName,
            FuelGroupName: fuelGroupName,
            FuelIsGasoline: fuelIsGasoline,
            FormId: "Số 3-04.3/XD-14",
            UnitNote: "Đơn vị tính: Nhiên liệu: Lít 15° C; Dầu mỡ: Kg.",
            Year: year,
            Quarter: quarter,
            MainMachineCount: QuantityMath.RoundQty(consumer.MainMachineCount),
            AuxMachineCount: QuantityMath.RoundQty(consumer.AuxMachineCount),
            NormRates: rates,
            Rows: rows);
    }

    public FuelResult SaveBook(ShipQuarterBookSaveRequest request)
    {
        try
        {
            using var db = factory();
            using var tx = db.Database.BeginTransaction();
            var (year, quarter, start, end) = QuarterBounds(request.QuarterDate);
            var consumer = db.Consumers.FirstOrDefault(x => x.Id == request.ConsumerId)
                ?? throw new FuelRuleException("Không tìm thấy tàu.");
            if (consumer.Type != ConsumerType.Ship)
                return FuelResult.Fail("Đối tượng không phải tàu.");

            var groups = db.ItemGroups.ToDictionary(x => x.Id, x => x.Name);
            // Tàu chỉ dùng 1 loại NL: lấy theo DefaultGroupId / tồn thực tế, không tin nhãn tay trên request.
            var fuelGroupName = ResolveFuelGroupName(db, consumer, groups, consumer.Id, start, end);
            if (!ShipNormSlots.IsFuelGroup(fuelGroupName))
                return FuelResult.Fail("Nhóm nhiên liệu tàu phải là Xăng hoặc Dầu.");

            var factors = db.ConsumerNormFactors.Where(x => x.ConsumerId == consumer.Id).ToList();
            var rates = ShipNormSlots.Labels.ToDictionary(
                label => label,
                label => factors.FirstOrDefault(x => x.Name.Equals(label, StringComparison.OrdinalIgnoreCase))?.Value ?? 0m,
                StringComparer.OrdinalIgnoreCase);

            var book = db.ShipQuarterBooks
                .Include(x => x.Lines)
                .FirstOrDefault(x => x.ConsumerId == consumer.Id && x.Year == year && x.Quarter == quarter);
            if (book is null)
            {
                book = new ShipQuarterBook
                {
                    Id = Guid.NewGuid(),
                    ConsumerId = consumer.Id,
                    Year = year,
                    Quarter = quarter
                };
                db.ShipQuarterBooks.Add(book);
            }

            book.FuelGroupName = fuelGroupName;
            book.UpdatedAt = DateTime.UtcNow;
            db.ShipQuarterBookLines.RemoveRange(book.Lines);
            book.Lines.Clear();

            var orderedEdits = request.Lines
                .OrderBy(x => x.DocumentDate ?? DateTime.MaxValue)
                .ThenBy(x => x.DocumentNumber ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
            var mainMachines = QuantityMath.RoundQty(consumer.MainMachineCount);
            var auxMachines = QuantityMath.RoundQty(consumer.AuxMachineCount);
            var order = 1;
            foreach (var edit in orderedEdits)
            {
                var line = new ShipQuarterBookLine
                {
                    Id = edit.LineId is Guid id && id != Guid.Empty ? id : Guid.NewGuid(),
                    BookId = book.Id,
                    SortOrder = order++,
                    DocumentNumber = edit.DocumentNumber?.Trim() ?? "",
                    DocumentDate = edit.DocumentDate?.Date,
                    Description = edit.Description?.Trim() ?? "",
                    MissionTaskId = edit.MissionTaskId,
                    MainOpsCount = mainMachines,
                    HoursAtBerth = QuantityMath.RoundQty(edit.HoursAtBerth),
                    HoursCx25 = QuantityMath.RoundQty(edit.HoursCx25),
                    HoursCx50 = QuantityMath.RoundQty(edit.HoursCx50),
                    HoursCx75 = QuantityMath.RoundQty(edit.HoursCx75),
                    HoursCx100 = QuantityMath.RoundQty(edit.HoursCx100),
                    AuxOpsCount = auxMachines,
                    AuxHours = QuantityMath.RoundQty(edit.AuxHours),
                    ManualFuelOut = edit.ManualFuelOut,
                    FuelOutManual = edit.FuelOutManual is decimal m ? QuantityMath.Whole(m) : null,
                    ManualOilOut = edit.ManualOilOut,
                    OilOut = 0,
                    LotTypeId = edit.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : edit.LotTypeId,
                    LotTypeCode = string.IsNullOrWhiteSpace(edit.LotTypeCode) ? "TX" : edit.LotTypeCode.Trim()
                };
                var fuelOut = ResolveFuelOut(line, rates);
                line.OilOut = edit.ManualOilOut
                    ? OilLiters(edit.OilOut)
                    : AutoOilOut(fuelOut);
                book.Lines.Add(line);
                db.ShipQuarterBookLines.Add(line);
            }

            var touched = new HashSet<(Guid, Guid)>();
            var byType = orderedEdits
                .Select(edit =>
                {
                    var typeId = edit.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : edit.LotTypeId;
                    var temp = new ShipQuarterBookLine
                    {
                        ManualFuelOut = edit.ManualFuelOut,
                        FuelOutManual = edit.FuelOutManual is decimal m ? QuantityMath.Whole(m) : null,
                        HoursAtBerth = QuantityMath.RoundQty(edit.HoursAtBerth),
                        HoursCx25 = QuantityMath.RoundQty(edit.HoursCx25),
                        HoursCx50 = QuantityMath.RoundQty(edit.HoursCx50),
                        HoursCx75 = QuantityMath.RoundQty(edit.HoursCx75),
                        HoursCx100 = QuantityMath.RoundQty(edit.HoursCx100),
                        MainOpsCount = mainMachines,
                        AuxOpsCount = auxMachines,
                        AuxHours = QuantityMath.RoundQty(edit.AuxHours),
                        ManualOilOut = edit.ManualOilOut,
                        OilOut = edit.ManualOilOut ? OilLiters(edit.OilOut) : 0
                    };
                    var fuel = ResolveFuelOut(temp, rates);
                    if (!edit.ManualOilOut)
                        temp.OilOut = AutoOilOut(fuel);
                    return (TypeId: typeId, Fuel: fuel, Oil: temp.OilOut);
                })
                .GroupBy(x => x.TypeId)
                .Select(g => (TypeId: g.Key, Fuel: QuantityMath.Whole(g.Sum(x => x.Fuel)), Oil: QuantityMath.Whole(g.Sum(x => x.Oil))))
                .ToList();

            // Xóa mọi XT quý của tàu (không lọc nhóm NL) — tránh sót phiếu khi đổi Xăng↔Dầu.
            ClearAllQuarterConsumption(db, consumer, start, end, touched);
            foreach (var part in byType)
            {
                DeductGroupFifo(db, consumer, start, end, fuelGroupName, part.Fuel, grease: false, part.TypeId, touched);
                DeductGroupFifo(db, consumer, start, end, fuelGroupName, part.Oil, grease: true, part.TypeId, touched);
            }
            EnsureNonNegative(db, touched);
            db.SaveChanges();
            tx.Commit();
            return FuelResult.Success(book.Id, "Đã lưu sổ tiêu thụ tàu và trừ tồn theo nhóm. Quý sau mang sang theo tồn cuối quý này.");
        }
        catch (FuelRuleException ex)
        {
            return FuelResult.Fail(ex.Message);
        }
    }

    private static void ClearAllQuarterConsumption(
        AppDbContext db,
        Consumer consumer,
        DateTime start,
        DateTime end,
        HashSet<(Guid, Guid)> touched)
    {
        var existing = db.Documents
            .Include(x => x.Lines)
            .Include(x => x.Fields)
            .Where(x => x.Status == DocumentStatus.Active
                && x.Kind == DocumentKind.Consumption
                && x.ConsumerId == consumer.Id
                && x.Distance == null
                && x.DocumentDate >= start
                && x.DocumentDate < end)
            .ToList();
        foreach (var doc in existing)
        {
            RemoveEffects(db, doc, touched);
            db.DocumentFields.RemoveRange(doc.Fields);
            db.DocumentLines.RemoveRange(doc.Lines);
            db.Documents.Remove(doc);
        }
    }

    private static void DeductGroupFifo(
        AppDbContext db,
        Consumer consumer,
        DateTime start,
        DateTime end,
        string fuelGroupName,
        decimal targetOut,
        bool grease,
        Guid lotTypeId,
        HashSet<(Guid, Guid)> touched)
    {
        if (targetOut <= 0)
            return;

        var typeId = lotTypeId == Guid.Empty ? SeedIds.LotTypeTx : lotTypeId;
        var lotMeta = db.Lots.AsNoTracking().Where(x => x.LotTypeId == typeId).ToDictionary(x => x.Id);
        // Tracked balances — thấy Adjust sau Clear trong cùng transaction.
        var stocks = db.StockBalances
            .Where(b => b.WarehouseId == consumer.Id)
            .ToList()
            .Where(b => QuantityMath.RoundQty(b.Quantity) > 0 && lotMeta.ContainsKey(b.LotId))
            .Select(b =>
            {
                var lot = lotMeta[b.LotId];
                var g = lot.GroupName?.Trim() ?? "";
                if (lot.ItemId is Guid itemId)
                {
                    var item = db.FuelItems.AsNoTracking().Include(i => i.Group).FirstOrDefault(i => i.Id == itemId);
                    if (item?.Group?.Name is string gn && gn.Length > 0)
                        g = gn;
                }

                var match = grease
                    ? ShipNormSlots.IsGreaseGroup(g)
                    : g.Equals(fuelGroupName, StringComparison.OrdinalIgnoreCase);
                if (!match)
                    return null;
                var first = FirstImportKey(db, lot.Id);
                return new { bal = b, lot, FirstAt = first.At, FirstSeq = first.Seq };
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderBy(x => x.FirstAt)
            .ThenBy(x => x.FirstSeq)
            .ThenBy(x => x.lot.UnitPrice)
            .ThenBy(x => x.lot.ItemName)
            .ToList();

        var remain = targetOut;
        var quarterEnd = end.AddDays(-1);
        foreach (var row in stocks)
        {
            if (remain <= 0)
                break;
            var available = QuantityMath.RoundQty(row.bal.Quantity);
            if (available <= 0)
                continue;
            var take = available < remain ? available : remain;
            WriteGroupConsumption(db, consumer, db.Lots.First(x => x.Id == row.lot.Id), take, quarterEnd, touched);
            remain = QuantityMath.RoundQty(remain - take);
        }

        if (remain > 0)
        {
            var typeCode = db.LotTypes.AsNoTracking().FirstOrDefault(x => x.Id == typeId)?.Code ?? "TX";
            throw new FuelRuleException(grease
                ? $"Không đủ tồn mỡ/nhớt loại {typeCode} trên tàu để trừ {NumbersOrQty(targetOut)} (thiếu {NumbersOrQty(remain)})."
                : $"Không đủ tồn {fuelGroupName} loại {typeCode} trên tàu để trừ {NumbersOrQty(targetOut)} (thiếu {NumbersOrQty(remain)}).");
        }
    }

    /// <summary>
    /// Ngày phiếu nhập/mở đầu gốc của lô (không dùng ngày ĐC xuống tàu) — FIFO cũ → mới.
    /// </summary>
    private static (DateTime At, long Seq) FirstImportKey(AppDbContext db, Guid lotId) =>
        FirstImportKey(db, lotId, []);

    private static (DateTime At, long Seq) FirstImportKey(AppDbContext db, Guid lotId, HashSet<Guid> visited)
    {
        if (!visited.Add(lotId))
            return (DateTime.MaxValue, long.MaxValue);

        var origin = (
            from m in db.StockMovements.AsNoTracking()
            where m.LotId == lotId && m.SignedQuantity > 0
            join d in db.Documents.AsNoTracking() on m.DocumentId equals d.Id
            where d.Status == DocumentStatus.Active
                && (d.Kind == DocumentKind.Import || d.Kind == DocumentKind.Opening)
            orderby d.DocumentDate, d.Sequence
            select new { d.DocumentDate, d.Sequence }
        ).FirstOrDefault();
        if (origin is not null)
            return (origin.DocumentDate.Date, origin.Sequence);

        // Lô sinh từ ĐC đổi loại / đổi loại: truy ngày nhập của lô nguồn.
        var sourceLotIds = (
            from m in db.StockMovements.AsNoTracking()
            where m.LotId == lotId && m.SignedQuantity > 0
            join d in db.Documents.AsNoTracking() on m.DocumentId equals d.Id
            where d.Status == DocumentStatus.Active
                && (d.Kind == DocumentKind.Transfer || d.Kind == DocumentKind.LotConvert)
            join line in db.DocumentLines.AsNoTracking() on d.Id equals line.DocumentId
            where line.LotId != lotId
            select line.LotId
        ).Distinct().ToList();

        (DateTime At, long Seq)? best = null;
        foreach (var sourceLotId in sourceLotIds)
        {
            var key = FirstImportKey(db, sourceLotId, visited);
            if (best is null || key.At < best.Value.At || (key.At == best.Value.At && key.Seq < best.Value.Seq))
                best = key;
        }

        return best ?? (DateTime.MaxValue, long.MaxValue);
    }

    private static string NumbersOrQty(decimal value)
    {
        var qty = QuantityMath.RoundQty(value);
        return qty == QuantityMath.Whole(qty)
            ? qty.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))
            : qty.ToString("0.####", CultureInfo.GetCultureInfo("vi-VN"));
    }
    private static void WriteGroupConsumption(
        AppDbContext db,
        Consumer consumer,
        Lot lot,
        decimal actual,
        DateTime documentDate,
        HashSet<(Guid, Guid)> touched)
    {
        actual = QuantityMath.RoundQty(actual);
        if (actual <= 0)
            return;
        var warehouse = db.Warehouses.First(x => x.Id == consumer.Id);
        var doc = new FuelDocument
        {
            Id = Guid.NewGuid(),
            Sequence = NextSequence(db),
            Number = "TT" + (db.Documents.AsEnumerable().Select(x => x.Sequence).DefaultIfEmpty(0).Max() + 1).ToString("D6"),
            Kind = DocumentKind.Consumption,
            Status = DocumentStatus.Active,
            DocumentDate = documentDate,
            CreatedAt = DateTime.UtcNow
        };
        // Number via Begin pattern — reuse simpler assignment after max sequence.
        doc.Number = "TT" + doc.Sequence.ToString("D6");
        db.Documents.Add(doc);

        doc.ItemId = lot.ItemId;
        doc.ItemName = lot.ItemName;
        var resolvedGroup = lot.GroupName?.Trim() ?? "";
        if (lot.ItemId is Guid itemId)
        {
            var item = db.FuelItems.AsNoTracking().Include(i => i.Group).FirstOrDefault(i => i.Id == itemId);
            if (item?.Group?.Name is string gn && gn.Length > 0)
                resolvedGroup = gn.Trim();
        }

        doc.GroupName = resolvedGroup;
        doc.UnitName = lot.UnitName;
        doc.Vcf = lot.FirstVcf <= 0 ? 1m : lot.FirstVcf;
        doc.WarehouseId = warehouse.Id;
        doc.WarehouseName = warehouse.Name;
        doc.WarehouseTypeName = Labels.Warehouse(warehouse.Type);
        doc.ConsumerId = consumer.Id;
        doc.ConsumerName = consumer.Name;
        doc.ConsumerCode = consumer.Code;
        doc.ConsumerTypeName = Labels.Consumer(ConsumerType.Ship);
        doc.Distance = null;
        doc.UnitPrice = lot.UnitPrice;
        var typeId = lot.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : lot.LotTypeId;
        var typeCode = lot.LotType?.Code
            ?? db.LotTypes.Local.FirstOrDefault(x => x.Id == typeId)?.Code
            ?? db.LotTypes.AsNoTracking().FirstOrDefault(x => x.Id == typeId)?.Code
            ?? "TX";
        doc.LotTypeId = typeId;
        doc.LotTypeCode = typeCode;
        doc.InputQuantity = QuantityMath.ExportDisplayQuantity(actual, doc.Vcf);
        doc.ActualQuantity = actual;
        doc.Amount = null;
        var line = new FuelDocumentLine
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            LineNo = 1,
            LotId = lot.Id,
            WarehouseId = warehouse.Id,
            ItemName = lot.ItemName,
            UnitPrice = lot.UnitPrice,
            Quantity = doc.InputQuantity,
            ActualQuantity = actual,
            Amount = 0,
            ItemId = lot.ItemId,
            Vcf = doc.Vcf,
            LotTypeId = typeId,
            LotTypeCode = typeCode
        };
        db.DocumentLines.Add(line);
        if (!doc.Lines.Contains(line))
            doc.Lines.Add(line);
        AddEffects(db, doc, touched);
    }

    private static long NextSequence(AppDbContext db)
    {
        var max = db.Documents.Select(x => (long?)x.Sequence).Max() ?? 0;
        var local = db.Documents.Local.Select(x => x.Sequence).DefaultIfEmpty(0).Max();
        return Math.Max(max, local) + 1;
    }

    private static void AddEffects(AppDbContext db, FuelDocument doc, HashSet<(Guid, Guid)> touched)
    {
        foreach (var line in doc.Lines)
            Post(db, doc, line.LotId, line.WarehouseId, -line.ActualQuantity, "Xuất tiêu thụ tàu", touched);
    }

    private static void RemoveEffects(AppDbContext db, FuelDocument doc, HashSet<(Guid, Guid)> touched)
    {
        var moves = db.StockMovements.Where(x => x.DocumentId == doc.Id).ToList();
        foreach (var move in moves)
        {
            Adjust(db, move.LotId, move.WarehouseId, -move.SignedQuantity, touched);
            db.StockMovements.Remove(move);
        }
    }

    private static void Post(AppDbContext db, FuelDocument doc, Guid lotId, Guid warehouseId, decimal signed, string reason, HashSet<(Guid, Guid)> touched)
    {
        var qty = QuantityMath.RoundQty(signed);
        if (qty == 0)
            return;
        db.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            LotId = lotId,
            WarehouseId = warehouseId,
            SignedQuantity = qty,
            Reason = reason,
            OccurredAt = doc.DocumentDate
        });
        Adjust(db, lotId, warehouseId, qty, touched);
    }

    private static void Adjust(AppDbContext db, Guid lotId, Guid warehouseId, decimal delta, HashSet<(Guid, Guid)> touched)
    {
        var balance = db.StockBalances.FirstOrDefault(x => x.LotId == lotId && x.WarehouseId == warehouseId);
        if (balance is null)
        {
            balance = new StockBalance { Id = Guid.NewGuid(), LotId = lotId, WarehouseId = warehouseId, Quantity = 0 };
            db.StockBalances.Add(balance);
        }

        balance.Quantity = QuantityMath.RoundQty(balance.Quantity + delta);
        touched.Add((lotId, warehouseId));
    }

    private static void EnsureNonNegative(AppDbContext db, HashSet<(Guid LotId, Guid WarehouseId)> touched)
    {
        foreach (var (lotId, warehouseId) in touched)
        {
            var bal = db.StockBalances.FirstOrDefault(x => x.LotId == lotId && x.WarehouseId == warehouseId);
            if (bal is not null && bal.Quantity < 0)
                throw new FuelRuleException("Tồn kho không đủ sau khi trừ tiêu thụ tàu.");
        }
    }

    private static (int Year, int Quarter, DateTime Start, DateTime End) QuarterBounds(DateTime quarterDate)
    {
        var date = quarterDate == default ? DateTime.Today : quarterDate.Date;
        var start = new DateTime(date.Year, ((date.Month - 1) / 3) * 3 + 1, 1);
        var end = start.AddMonths(3);
        var quarter = (start.Month - 1) / 3 + 1;
        return (start.Year, quarter, start, end);
    }

    internal static string ResolveFuelGroupName(
        AppDbContext db,
        Consumer consumer,
        Dictionary<Guid, string> groups,
        Guid warehouseId,
        DateTime start,
        DateTime end)
    {
        if (consumer.DefaultGroupId is Guid gid && groups.TryGetValue(gid, out var name) && ShipNormSlots.IsFuelGroup(name))
            return name;

        var stocks = (
            from bal in db.StockBalances.AsNoTracking()
            where bal.WarehouseId == warehouseId && bal.Quantity != 0
            join lot in db.Lots.AsNoTracking() on bal.LotId equals lot.Id
            select lot.GroupName).AsEnumerable()
            .Select(x => x?.Trim() ?? "")
            .Where(ShipNormSlots.IsFuelGroup)
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(stocks))
            return stocks;

        return "Dầu";
    }

    private static (decimal Fuel, decimal Oil, Dictionary<string, decimal> FuelLots, Dictionary<string, decimal> OilLots) OpeningBalances(
        AppDbContext db,
        Guid warehouseId,
        DateTime start,
        DateTime end,
        string fuelGroupName,
        Dictionary<Guid, FuelItem> items,
        Dictionary<Guid, Lot> lots)
    {
        var quarterEnd = end.AddDays(-1);
        var flat = (
            from doc in db.Documents.AsNoTracking()
            where doc.Status == DocumentStatus.Active && doc.DocumentDate < end
            join line in db.DocumentLines.AsNoTracking() on doc.Id equals line.DocumentId
            where line.WarehouseId == warehouseId || doc.DestinationWarehouseId == warehouseId
            select new { doc, line }).ToList();

        decimal fuel = 0;
        decimal oil = 0;
        var fuelLots = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var oilLots = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in flat)
        {
            var signed = SignedQty(row.doc.Kind, row.line.WarehouseId, row.doc.DestinationWarehouseId, warehouseId, row.line.ActualQuantity);
            if (signed == 0)
                continue;
            var isOpeningDoc = row.doc.Kind == DocumentKind.Opening
                ? row.doc.DocumentDate.Date <= quarterEnd
                : row.doc.DocumentDate.Date < start;
            if (!isOpeningDoc)
                continue;
            var group = ResolveGroup(row.line.ItemId, row.line.LotId, items, lots);
            _ = fuelGroupName;
            var typeCode = LotTypeCodeForEffect(
                row.doc.Kind, row.line.WarehouseId, row.doc.DestinationWarehouseId, warehouseId,
                row.line.LotTypeCode, row.line.DestinationLotTypeCode);
            if (ShipNormSlots.IsFuelGroup(group))
            {
                fuel = QuantityMath.Whole(fuel + signed);
                LotBalanceTips.Add(fuelLots, typeCode, signed);
            }
            else if (ShipNormSlots.IsGreaseGroup(group))
            {
                oil = QuantityMath.RoundQty(oil + signed);
                LotBalanceTips.Add(oilLots, typeCode, signed);
            }
        }

        return (fuel, oil, fuelLots, oilLots);
    }

    private static string LotTypeCodeForEffect(
        DocumentKind kind,
        Guid lineWarehouseId,
        Guid? destinationWarehouseId,
        Guid targetWarehouseId,
        string? lotTypeCode,
        string? destinationLotTypeCode)
    {
        var source = string.IsNullOrWhiteSpace(lotTypeCode) ? "TX" : lotTypeCode.Trim();
        var dest = string.IsNullOrWhiteSpace(destinationLotTypeCode) ? source : destinationLotTypeCode.Trim();
        if (kind == DocumentKind.Transfer && destinationWarehouseId == targetWarehouseId && lineWarehouseId != targetWarehouseId)
            return dest;
        return source;
    }

    private static void ApplyShipTransferLotTips(
        Dictionary<string, decimal> fuelLots,
        Dictionary<string, decimal> oilLots,
        ShipQuarterBookRowDto transfer)
    {
        var codes = (transfer.LotTypeCode ?? "TX")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var code = codes.Length > 0 ? codes[0] : "TX";
        // ĐC gộp nhiều loại lô trên một dòng: tip gắn mã đầu; dòng tách loại (máy/xe) chính xác hơn.
        LotBalanceTips.Add(fuelLots, code, transfer.FuelIn ?? 0);
        LotBalanceTips.Add(oilLots, code, transfer.OilIn ?? 0);
        for (var i = 1; i < codes.Length; i++)
        {
            LotBalanceTips.Add(fuelLots, codes[i], 0);
            LotBalanceTips.Add(oilLots, codes[i], 0);
        }
    }

    private static List<(decimal FuelIn, decimal OilIn, ShipQuarterBookRowDto Row)> InboundTransfers(
        AppDbContext db,
        Guid warehouseId,
        DateTime start,
        DateTime end,
        Dictionary<Guid, FuelItem> items,
        Dictionary<Guid, Lot> lots,
        string fuelGroupName)
    {
        var docs = (
            from doc in db.Documents.AsNoTracking()
            where doc.Status == DocumentStatus.Active
                && doc.Kind == DocumentKind.Transfer
                && doc.DocumentDate >= start
                && doc.DocumentDate < end
                && (doc.DestinationWarehouseId == warehouseId
                    || (doc.DestinationWarehouseId == null && doc.ConsumerId == warehouseId))
            join line in db.DocumentLines.AsNoTracking() on doc.Id equals line.DocumentId
            select new { doc, line }).AsEnumerable()
            .GroupBy(x => x.doc.Id)
            .OrderBy(g => g.First().doc.DocumentDate.Date)
            .ThenBy(g => g.First().doc.Sequence)
            .ToList();

        var list = new List<(decimal, decimal, ShipQuarterBookRowDto)>();
        foreach (var group in docs)
        {
            var head = group.First().doc;
            decimal fuel = 0;
            decimal oil = 0;
            var typeCodes = new List<string>();
            var typeIds = new List<Guid>();
            foreach (var row in group)
            {
                var qty = QuantityMath.RoundQty(row.line.ActualQuantity);
                if (qty <= 0)
                    continue;
                var g = ResolveGroup(row.line.ItemId, row.line.LotId, items, lots);
                if (g.Equals(fuelGroupName, StringComparison.OrdinalIgnoreCase))
                    fuel = QuantityMath.Whole(fuel + qty);
                else if (ShipNormSlots.IsGreaseGroup(g))
                    oil = QuantityMath.RoundQty(oil + qty);
                else if (ShipNormSlots.IsFuelGroup(g))
                    // ĐC nhóm Xăng/Dầu khác nhiên liệu mặc định tàu — vẫn ghi Nhập để dòng sổ không trống.
                    fuel = QuantityMath.Whole(fuel + qty);

                var typeId = row.line.DestinationLotTypeId ?? row.line.LotTypeId ?? SeedIds.LotTypeTx;
                var typeCode = !string.IsNullOrWhiteSpace(row.line.DestinationLotTypeCode)
                    ? row.line.DestinationLotTypeCode
                    : (!string.IsNullOrWhiteSpace(row.line.LotTypeCode) ? row.line.LotTypeCode : "TX");
                if (!typeIds.Contains(typeId))
                {
                    typeIds.Add(typeId);
                    typeCodes.Add(typeCode);
                }
            }

            var number = string.IsNullOrWhiteSpace(head.FormNumber) ? head.Number : head.FormNumber;
            // Diễn giải trên sổ tàu = tính chất xuất.
            var desc = FirstNonEmpty(head.Nature, head.Mission, head.Note, "Điều chuyển");
            var lotTypeId = typeIds.Count > 0 ? typeIds[0] : SeedIds.LotTypeTx;
            var lotTypeCode = typeCodes.Count > 0 ? string.Join(",", typeCodes) : "TX";
            var transferRow = new ShipQuarterBookRowDto(
                IsOpening: false,
                IsTransfer: true,
                LineId: null,
                DocumentNumber: number ?? "",
                DocumentDate: head.DocumentDate.Date,
                Description: desc,
                MainOpsCount: null,
                HoursAtBerth: null,
                HoursCx25: null,
                HoursCx50: null,
                HoursCx75: null,
                HoursCx100: null,
                MainHoursTotal: null,
                AuxOpsCount: null,
                AuxHours: null,
                GasolineUse: null,
                DieselUse: null,
                ManualFuelOut: false,
                ManualOilOut: false,
                FuelIn: fuel > 0 ? fuel : null,
                FuelOut: null,
                FuelBalance: null,
                OilIn: oil > 0 ? oil : null,
                OilOut: null,
                OilBalance: null,
                RowTotal: NullIfZero(QuantityMath.RoundQty(fuel + oil)),
                LotTypeId: lotTypeId,
                LotTypeCode: lotTypeCode);
            list.Add((fuel, oil, transferRow));
        }

        return list;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var text = value?.Trim() ?? "";
            if (text.Length > 0)
                return text;
        }

        return "";
    }

    private static ShipQuarterBookRowDto OpeningRow(decimal fuel, decimal oil, string fuelTip, string oilTip) =>
        new(true, false, null, "", null, "Tồn …… chuyển sang",
            null, null, null, null, null, null, null, null, null, null, null, false, false,
            null, null, fuel == 0 ? null : fuel,
            null, null, oil == 0 ? null : oil,
            NullIfZero(QuantityMath.RoundQty(fuel + oil)),
            FuelBalanceLotTip: fuelTip,
            OilBalanceLotTip: oilTip);

    private static ShipQuarterBookRowDto ConsumptionRow(
        ShipQuarterBookLine line,
        decimal defaultMainMachines,
        decimal defaultAuxMachines,
        IReadOnlyDictionary<string, decimal> rates,
        bool fuelIsGasoline,
        decimal fuelOut,
        decimal oilOut,
        decimal fuelBal,
        decimal oilBal,
        string fuelTip,
        string oilTip)
    {
        var machines = line.MainOpsCount > 0 ? line.MainOpsCount : defaultMainMachines;
        var auxMachines = line.AuxOpsCount > 0 ? line.AuxOpsCount : defaultAuxMachines;
        var sumHours = QuantityMath.RoundQty(
            line.HoursAtBerth + line.HoursCx25 + line.HoursCx50 + line.HoursCx75 + line.HoursCx100);
        var mainTotal = QuantityMath.RoundQty(sumHours * machines);
        return new ShipQuarterBookRowDto(
            IsOpening: false,
            IsTransfer: false,
            LineId: line.Id,
            DocumentNumber: line.DocumentNumber,
            DocumentDate: line.DocumentDate,
            Description: line.Description,
            MainOpsCount: NullIfZero(machines),
            HoursAtBerth: NullIfZero(line.HoursAtBerth),
            HoursCx25: NullIfZero(line.HoursCx25),
            HoursCx50: NullIfZero(line.HoursCx50),
            HoursCx75: NullIfZero(line.HoursCx75),
            HoursCx100: NullIfZero(line.HoursCx100),
            MainHoursTotal: mainTotal > 0 ? mainTotal : null,
            AuxOpsCount: NullIfZero(auxMachines),
            AuxHours: NullIfZero(line.AuxHours),
            GasolineUse: fuelIsGasoline && fuelOut > 0 ? fuelOut : null,
            DieselUse: !fuelIsGasoline && fuelOut > 0 ? fuelOut : null,
            ManualFuelOut: line.ManualFuelOut,
            ManualOilOut: line.ManualOilOut,
            FuelIn: null,
            FuelOut: fuelOut > 0 ? fuelOut : null,
            FuelBalance: fuelBal,
            OilIn: null,
            OilOut: oilOut > 0 ? oilOut : null,
            OilBalance: oilBal,
            RowTotal: NullIfZero(QuantityMath.RoundQty(fuelOut + oilOut)),
            LotTypeId: line.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : line.LotTypeId,
            LotTypeCode: string.IsNullOrWhiteSpace(line.LotTypeCode) ? "TX" : line.LotTypeCode,
            FuelBalanceLotTip: fuelTip,
            OilBalanceLotTip: oilTip,
            MissionTaskId: line.MissionTaskId);
    }

    internal static decimal ResolveFuelOut(ShipQuarterBookLine line, IReadOnlyDictionary<string, decimal> rates)
    {
        if (line.ManualFuelOut && line.FuelOutManual is decimal manual)
            return QuantityMath.Whole(manual);

        var mainMachines = QuantityMath.RoundQty(line.MainOpsCount);
        decimal total = 0;
        total += SlotFuel(line.HoursAtBerth, mainMachines, rates, ShipNormSlots.AtBerth);
        total += SlotFuel(line.HoursCx25, mainMachines, rates, ShipNormSlots.Cx25);
        total += SlotFuel(line.HoursCx50, mainMachines, rates, ShipNormSlots.Cx50);
        total += SlotFuel(line.HoursCx75, mainMachines, rates, ShipNormSlots.Cx75);
        total += SlotFuel(line.HoursCx100, mainMachines, rates, ShipNormSlots.Cx100);

        var auxMachines = QuantityMath.RoundQty(line.AuxOpsCount);
        total += SlotFuel(line.AuxHours, auxMachines, rates, ShipNormSlots.Aux);
        return QuantityMath.Whole(Math.Round(total, 0, MidpointRounding.AwayFromZero));
    }

    private static decimal SlotFuel(decimal hours, decimal machines, IReadOnlyDictionary<string, decimal> rates, string key)
    {
        hours = QuantityMath.RoundQty(hours);
        machines = QuantityMath.RoundQty(machines);
        if (hours <= 0 || machines <= 0)
            return 0;
        rates.TryGetValue(key, out var rate);
        if (rate <= 0)
            return 0;
        return QuantityMath.RoundQty(hours * machines * QuantityMath.RoundNorm(rate));
    }

    private static decimal AutoOilOut(decimal fuelOut)
    {
        var fuel = QuantityMath.Whole(fuelOut);
        if (fuel <= 0)
            return 0;
        // 4% × NL, làm tròn lên thành lít nguyên (không thập phân).
        var raw = fuel * ShipNormSlots.OilPerFuelRatio;
        return raw <= 0 ? 0 : decimal.Ceiling(raw);
    }

    private static decimal ResolveOilOut(ShipQuarterBookLine line, decimal fuelOut) =>
        line.ManualOilOut ? OilLiters(line.OilOut) : AutoOilOut(fuelOut);

    /// <summary>Dầu mỡ luôn lít nguyên, làm tròn lên.</summary>
    private static decimal OilLiters(decimal value)
    {
        var qty = QuantityMath.RoundQty(value);
        return qty <= 0 ? 0 : decimal.Ceiling(qty);
    }

    private static decimal? NullIfZero(decimal value) => value == 0 ? null : value;

    private static string ResolveGroup(Guid? itemId, Guid lotId, Dictionary<Guid, FuelItem> items, Dictionary<Guid, Lot> lots)
    {
        if (lots.TryGetValue(lotId, out var lot))
        {
            itemId ??= lot.ItemId;
            var fromLot = lot.GroupName?.Trim() ?? "";
            if (itemId is Guid id && items.TryGetValue(id, out var item))
                return item.Group?.Name?.Trim() ?? fromLot;
            return fromLot;
        }

        if (itemId is Guid itemKey && items.TryGetValue(itemKey, out var byItem))
            return byItem.Group?.Name?.Trim() ?? "";
        return "";
    }

    private static decimal SignedQty(DocumentKind kind, Guid lineWarehouseId, Guid? destinationWarehouseId, Guid target, decimal actual)
    {
        var qty = QuantityMath.RoundQty(actual);
        if (qty == 0)
            return 0;
        return kind switch
        {
            DocumentKind.Opening or DocumentKind.Import => lineWarehouseId == target ? qty : 0,
            DocumentKind.Issue or DocumentKind.Consumption or DocumentKind.Auxiliary => lineWarehouseId == target ? -qty : 0,
            DocumentKind.Transfer when lineWarehouseId == target => -qty,
            DocumentKind.Transfer when destinationWarehouseId == target => qty,
            _ => 0
        };
    }
}
