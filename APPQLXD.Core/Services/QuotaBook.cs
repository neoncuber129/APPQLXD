using APPQLXD.Core.Calculations;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace APPQLXD.Core.Services;

/// <summary>Thống kê hạn mức theo nhiệm vụ / năm / quý / loại lô.</summary>
public sealed class QuotaBook
{
    private readonly Func<AppDbContext> _factory;

    public QuotaBook(Func<AppDbContext> factory) => _factory = factory;

    public QuotaSheet Build(int year, int quarter, NxtLotViewMode lotView = NxtLotViewMode.TxSscd)
    {
        if (year < 2000 || year > 2100)
            year = DateTime.Today.Year;
        if (quarter is < 1 or > 4)
            quarter = 1;
        if (lotView is not (NxtLotViewMode.TxSscd or NxtLotViewMode.Iuu))
            lotView = NxtLotViewMode.TxSscd;

        // Dữ liệu trong quý: từ ngày đầu quý đến ngày cuối quý.
        var qFromDate = new DateTime(year, (quarter - 1) * 3 + 1, 1);
        var qToDate = new DateTime(year, quarter * 3, DateTime.DaysInMonth(year, quarter * 3));
        // Lũy tích sử dụng: từ 01/01 đầu năm đến cuối quý đang chọn.
        var cumFromDate = new DateTime(year, 1, 1);
        var cumToDate = qToDate;

        using var db = _factory();
        MissionSeeder.Ensure(db);

        var groups = db.MissionGroups.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .ToList();
        var tasks = db.MissionTasks.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .ToList();
        var lotKey = (int)lotView;
        var limits = db.MissionYearLimits.AsNoTracking()
            .Where(x => x.Year == year && x.LotView == lotKey)
            .ToDictionary(x => x.TaskId, x => x);

        var qAgg = AggregateUsage(db, qFromDate, qToDate, lotView);
        var cumAgg = AggregateUsage(db, cumFromDate, cumToDate, lotView);

        // Dòng "Cộng tiêu thụ" sau nhóm không-hao-hụt cuối (nhóm hao hụt nằm phía dưới).
        var lastConsumptionGroupId = groups.LastOrDefault(g => !g.IsLossGroup)?.Id;

        var rows = new List<QuotaRow>();
        decimal sumItoIV_gasLim = 0, sumItoIV_dieLim = 0;
        decimal sumItoIV_qGasKm = 0, sumItoIV_qGasHr = 0, sumItoIV_qDieKm = 0, sumItoIV_qDieHr = 0;
        decimal sumItoIV_qGasVeh = 0, sumItoIV_qGasMach = 0, sumItoIV_qDieVeh = 0, sumItoIV_qDieMach = 0;
        decimal sumItoIV_cumGasKm = 0, sumItoIV_cumGasHr = 0, sumItoIV_cumDieKm = 0, sumItoIV_cumDieHr = 0;
        decimal sumItoIV_cumGasVeh = 0, sumItoIV_cumGasMach = 0, sumItoIV_cumDieVeh = 0, sumItoIV_cumDieMach = 0;

        decimal sumAll_gasLim = 0, sumAll_dieLim = 0;
        decimal sumAll_qGasKm = 0, sumAll_qGasHr = 0, sumAll_qDieKm = 0, sumAll_qDieHr = 0;
        decimal sumAll_qGasVeh = 0, sumAll_qGasMach = 0, sumAll_qDieVeh = 0, sumAll_qDieMach = 0;
        decimal sumAll_cumGasKm = 0, sumAll_cumGasHr = 0, sumAll_cumDieKm = 0, sumAll_cumDieHr = 0;
        decimal sumAll_cumGasVeh = 0, sumAll_cumGasMach = 0, sumAll_cumDieVeh = 0, sumAll_cumDieMach = 0;

        foreach (var group in groups)
        {
            var groupTasks = tasks.Where(t => t.GroupId == group.Id).ToList();
            decimal gGasLim = 0, gDieLim = 0;
            decimal gQGasKm = 0, gQGasHr = 0, gQDieKm = 0, gQDieHr = 0;
            decimal gQGasVeh = 0, gQGasMach = 0, gQDieVeh = 0, gQDieMach = 0;
            decimal gCumGasKm = 0, gCumGasHr = 0, gCumDieKm = 0, gCumDieHr = 0;
            decimal gCumGasVeh = 0, gCumGasMach = 0, gCumDieVeh = 0, gCumDieMach = 0;

            var groupRows = new List<QuotaRow>();
            var idx = 1;
            foreach (var task in groupTasks)
            {
                limits.TryGetValue(task.Id, out var lim);
                var gasLim = lim?.GasolineLimit ?? 0;
                var dieLim = lim?.DieselLimit ?? 0;

                qAgg.TryGetValue(task.Id, out var uQ);
                uQ ??= Usage.Empty;

                cumAgg.TryGetValue(task.Id, out var uCum);
                uCum ??= Usage.Empty;

                groupRows.Add(MakeTaskRow(
                    idx.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    task.Name,
                    task.Id,
                    gasLim, dieLim, uQ, uCum,
                    isHeader: false));

                gGasLim += gasLim; gDieLim += dieLim;
                gQGasKm += uQ.GasKm; gQGasHr += uQ.GasHours; gQDieKm += uQ.DieKm; gQDieHr += uQ.DieHours;
                gQGasVeh += uQ.GasVehicle; gQGasMach += uQ.GasMachine; gQDieVeh += uQ.DieVehicle; gQDieMach += uQ.DieMachine;

                gCumGasKm += uCum.GasKm; gCumGasHr += uCum.GasHours; gCumDieKm += uCum.DieKm; gCumDieHr += uCum.DieHours;
                gCumGasVeh += uCum.GasVehicle; gCumGasMach += uCum.GasMachine; gCumDieVeh += uCum.DieVehicle; gCumDieMach += uCum.DieMachine;
                idx++;
            }

            // Dòng nhóm (= cộng nhóm) đứng trên các nhiệm vụ con — như mẫu.
            rows.Add(MakeTaskRow(
                group.Code,
                group.Name,
                null,
                gGasLim, gDieLim,
                new Usage(gQGasKm, gQGasHr, gQDieKm, gQDieHr, gQGasVeh, gQGasMach, gQDieVeh, gQDieMach),
                new Usage(gCumGasKm, gCumGasHr, gCumDieKm, gCumDieHr, gCumGasVeh, gCumGasMach, gCumDieVeh, gCumDieMach),
                isHeader: true));
            rows.AddRange(groupRows);

            sumAll_gasLim += gGasLim; sumAll_dieLim += gDieLim;
            sumAll_qGasKm += gQGasKm; sumAll_qGasHr += gQGasHr; sumAll_qDieKm += gQDieKm; sumAll_qDieHr += gQDieHr;
            sumAll_qGasVeh += gQGasVeh; sumAll_qGasMach += gQGasMach; sumAll_qDieVeh += gQDieVeh; sumAll_qDieMach += gQDieMach;
            sumAll_cumGasKm += gCumGasKm; sumAll_cumGasHr += gCumGasHr; sumAll_cumDieKm += gCumDieKm; sumAll_cumDieHr += gCumDieHr;
            sumAll_cumGasVeh += gCumGasVeh; sumAll_cumGasMach += gCumGasMach; sumAll_cumDieVeh += gCumDieVeh; sumAll_cumDieMach += gCumDieMach;

            if (!group.IsLossGroup)
            {
                sumItoIV_gasLim += gGasLim; sumItoIV_dieLim += gDieLim;
                sumItoIV_qGasKm += gQGasKm; sumItoIV_qGasHr += gQGasHr; sumItoIV_qDieKm += gQDieKm; sumItoIV_qDieHr += gQDieHr;
                sumItoIV_qGasVeh += gQGasVeh; sumItoIV_qGasMach += gQGasMach; sumItoIV_qDieVeh += gQDieVeh; sumItoIV_qDieMach += gQDieMach;
                sumItoIV_cumGasKm += gCumGasKm; sumItoIV_cumGasHr += gCumGasHr; sumItoIV_cumDieKm += gCumDieKm; sumItoIV_cumDieHr += gCumDieHr;
                sumItoIV_cumGasVeh += gCumGasVeh; sumItoIV_cumGasMach += gCumGasMach; sumItoIV_cumDieVeh += gCumDieVeh; sumItoIV_cumDieMach += gCumDieMach;
            }

            if (group.Id == lastConsumptionGroupId)
            {
                rows.Add(MakeTaskRow(
                    "",
                    "Cộng tiêu thụ",
                    null,
                    sumItoIV_gasLim, sumItoIV_dieLim,
                    new Usage(sumItoIV_qGasKm, sumItoIV_qGasHr, sumItoIV_qDieKm, sumItoIV_qDieHr,
                        sumItoIV_qGasVeh, sumItoIV_qGasMach, sumItoIV_qDieVeh, sumItoIV_qDieMach),
                    new Usage(sumItoIV_cumGasKm, sumItoIV_cumGasHr, sumItoIV_cumDieKm, sumItoIV_cumDieHr,
                        sumItoIV_cumGasVeh, sumItoIV_cumGasMach, sumItoIV_cumDieVeh, sumItoIV_cumDieMach),
                    isHeader: true));
            }
        }

        rows.Add(MakeTaskRow(
            "",
            "Tổng cộng",
            null,
            sumAll_gasLim, sumAll_dieLim,
            new Usage(sumAll_qGasKm, sumAll_qGasHr, sumAll_qDieKm, sumAll_qDieHr,
                sumAll_qGasVeh, sumAll_qGasMach, sumAll_qDieVeh, sumAll_qDieMach),
            new Usage(sumAll_cumGasKm, sumAll_cumGasHr, sumAll_cumDieKm, sumAll_cumDieHr,
                sumAll_cumGasVeh, sumAll_cumGasMach, sumAll_cumDieVeh, sumAll_cumDieMach),
            isHeader: true));

        return new QuotaSheet(year, quarter, lotView, qFromDate, qToDate, rows, cumFromDate);
    }

    private static QuotaRow MakeTaskRow(
        string stt,
        string name,
        Guid? taskId,
        decimal gasLim,
        decimal dieLim,
        Usage uQ,
        Usage uCum,
        bool isHeader)
    {
        var limTotal = QuantityMath.Whole(gasLim + dieLim);
        var qGasFuel = QuantityMath.Whole(uQ.GasVehicle + uQ.GasMachine);
        var qDieFuel = QuantityMath.Whole(uQ.DieVehicle + uQ.DieMachine);
        var qFuelTotal = QuantityMath.Whole(qGasFuel + qDieFuel);

        var cumGas = QuantityMath.Whole(uCum.GasVehicle + uCum.GasMachine);
        var cumDie = QuantityMath.Whole(uCum.DieVehicle + uCum.DieMachine);
        var cumTotal = QuantityMath.Whole(cumGas + cumDie);

        var remainGas = gasLim > cumGas ? QuantityMath.Whole(gasLim - cumGas) : 0;
        var remainDie = dieLim > cumDie ? QuantityMath.Whole(dieLim - cumDie) : 0;
        var remainTotal = limTotal > cumTotal ? QuantityMath.Whole(limTotal - cumTotal) : 0;

        var excessGas = cumGas > gasLim ? QuantityMath.Whole(cumGas - gasLim) : 0;
        var excessDie = cumDie > dieLim ? QuantityMath.Whole(cumDie - dieLim) : 0;
        var excessTotal = cumTotal > limTotal ? QuantityMath.Whole(cumTotal - limTotal) : 0;

        return new QuotaRow(
            Stt: stt,
            Name: name,
            TaskId: taskId,
            IsHeader: isHeader,
            GasolineLimit: NullZ(gasLim),
            DieselLimit: NullZ(dieLim),
            LimitTotal: NullZ(limTotal),
            GasolineKm: NullZ(uQ.GasKm),
            GasolineHours: NullZ(uQ.GasHours),
            DieselKm: NullZ(uQ.DieKm),
            DieselHours: NullZ(uQ.DieHours),
            GasolineVehicle: NullZ(uQ.GasVehicle),
            GasolineMachine: NullZ(uQ.GasMachine),
            GasolineFuelTotal: NullZ(qGasFuel),
            DieselVehicle: NullZ(uQ.DieVehicle),
            DieselMachine: NullZ(uQ.DieMachine),
            DieselFuelTotal: NullZ(qDieFuel),
            FuelTotal: NullZ(qFuelTotal),
            CumGasoline: NullZ(cumGas),
            CumDiesel: NullZ(cumDie),
            CumTotal: NullZ(cumTotal),
            RemainGasoline: NullZ(remainGas),
            RemainDiesel: NullZ(remainDie),
            RemainTotal: NullZ(remainTotal),
            ExcessGasoline: NullZ(excessGas),
            ExcessDiesel: NullZ(excessDie),
            ExcessTotal: NullZ(excessTotal));
    }

    private static decimal? NullZ(decimal v) => v == 0 ? null : QuantityMath.RoundQty(v);

    private static Dictionary<Guid, Usage> AggregateUsage(
        AppDbContext db,
        DateTime fromDate,
        DateTime toDate,
        NxtLotViewMode lotView)
    {
        var result = new Dictionary<Guid, Usage>();

        // Consumer books (máy / xe)
        var consumerLines = (
            from line in db.ConsumerQuarterBookLines.AsNoTracking()
            join book in db.ConsumerQuarterBooks.AsNoTracking() on line.BookId equals book.Id
            join wh in db.Warehouses.AsNoTracking() on book.ConsumerId equals wh.Id
            join c in db.Consumers.AsNoTracking() on book.ConsumerId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            where line.MissionTaskId != null
                  && line.DocumentDate != null
                  && line.DocumentDate >= fromDate
                  && line.DocumentDate <= toDate
            select new
            {
                TaskId = line.MissionTaskId!.Value,
                line.LotTypeCode,
                line.Kilometers,
                line.MachineHours,
                line.FuelOut,
                Type = c != null ? c.Type : ConsumerType.Machine,
                DefaultGroupId = (Guid?)(c != null ? c.DefaultGroupId : null)
            }).ToList();

        var groupNames = db.ItemGroups.AsNoTracking().ToDictionary(x => x.Id, x => x.Name);

        foreach (var line in consumerLines)
        {
            if (!MatchesLot(line.LotTypeCode, lotView))
                continue;
            var isGas = IsGasolineConsumer(line.DefaultGroupId, groupNames);
            var isVehicle = line.Type == ConsumerType.Vehicle;
            Add(result, line.TaskId, isGas, isVehicle,
                line.Kilometers ?? 0, line.MachineHours ?? 0, line.FuelOut);
        }

        // Ship books — phân loại Xăng/Dầu theo NL mặc định / tồn thực tế (tàu chỉ dùng 1 loại).
        var shipLines = (
            from line in db.ShipQuarterBookLines.AsNoTracking()
            join book in db.ShipQuarterBooks.AsNoTracking() on line.BookId equals book.Id
            join c in db.Consumers.AsNoTracking() on book.ConsumerId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            where line.MissionTaskId != null
                  && line.DocumentDate != null
                  && line.DocumentDate >= fromDate
                  && line.DocumentDate <= toDate
            select new
            {
                TaskId = line.MissionTaskId!.Value,
                line.LotTypeCode,
                book.FuelGroupName,
                book.ConsumerId,
                DefaultGroupId = (Guid?)(c != null ? c.DefaultGroupId : null),
                Line = line
            }).ToList();

        var shipIds = shipLines.Select(x => x.ConsumerId).Distinct().ToList();
        var ratesByShip = LoadShipNormRates(db, shipIds);
        var stockFuelByShip = LoadStockFuelGroupByWarehouse(db, shipIds);

        foreach (var row in shipLines)
        {
            if (!MatchesLot(row.LotTypeCode, lotView))
                continue;
            var line = row.Line;
            var mainHours = QuantityMath.RoundQty(
                (line.HoursAtBerth + line.HoursCx25 + line.HoursCx50 + line.HoursCx75 + line.HoursCx100) * line.MainOpsCount);
            var auxHours = QuantityMath.RoundQty(line.AuxHours * line.AuxOpsCount);
            var hours = QuantityMath.RoundQty(mainHours + auxHours);
            ratesByShip.TryGetValue(row.ConsumerId, out var rates);
            rates ??= new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var fuel = ShipQuarterBooks.ResolveFuelOut(line, rates);
            var fuelGroup = ResolveShipFuelGroup(
                row.DefaultGroupId, row.FuelGroupName, groupNames, stockFuelByShip, row.ConsumerId);
            var isGas = fuelGroup.Equals("Xăng", StringComparison.OrdinalIgnoreCase);
            Add(result, row.TaskId, isGas, isVehicle: false, km: 0, hours, fuel);
        }

        return result;
    }

    /// <summary>Xăng nếu DefaultGroupId / tồn NL / FuelGroupName trên sổ là Xăng; còn lại Dầu.</summary>
    private static string ResolveShipFuelGroup(
        Guid? defaultGroupId,
        string? bookFuelGroupName,
        Dictionary<Guid, string> groups,
        Dictionary<Guid, string> stockFuelByWarehouse,
        Guid warehouseId)
    {
        if (defaultGroupId is Guid gid && groups.TryGetValue(gid, out var fromDefault)
            && ShipNormSlots.IsFuelGroup(fromDefault))
            return fromDefault.Trim();

        if (stockFuelByWarehouse.TryGetValue(warehouseId, out var fromStock)
            && ShipNormSlots.IsFuelGroup(fromStock))
            return fromStock;

        var fromBook = bookFuelGroupName?.Trim() ?? "";
        if (ShipNormSlots.IsFuelGroup(fromBook))
            return fromBook;

        return "Dầu";
    }

    private static Dictionary<Guid, Dictionary<string, decimal>> LoadShipNormRates(
        AppDbContext db, List<Guid> shipIds)
    {
        if (shipIds.Count == 0)
            return new();

        var factors = db.ConsumerNormFactors.AsNoTracking()
            .Where(x => shipIds.Contains(x.ConsumerId))
            .ToList();

        return factors
            .GroupBy(x => x.ConsumerId)
            .ToDictionary(
                g => g.Key,
                g => ShipNormSlots.Labels.ToDictionary(
                    label => label,
                    label => g.FirstOrDefault(x => x.Name.Equals(label, StringComparison.OrdinalIgnoreCase))?.Value ?? 0m,
                    StringComparer.OrdinalIgnoreCase));
    }

    private static Dictionary<Guid, string> LoadStockFuelGroupByWarehouse(
        AppDbContext db, List<Guid> warehouseIds)
    {
        if (warehouseIds.Count == 0)
            return new();

        return (
            from bal in db.StockBalances.AsNoTracking()
            where warehouseIds.Contains(bal.WarehouseId) && bal.Quantity != 0
            join lot in db.Lots.AsNoTracking() on bal.LotId equals lot.Id
            select new { bal.WarehouseId, lot.GroupName }).AsEnumerable()
            .Select(x => new { x.WarehouseId, Name = x.GroupName?.Trim() ?? "" })
            .Where(x => ShipNormSlots.IsFuelGroup(x.Name))
            .GroupBy(x => x.WarehouseId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x.Count())
                    .Select(x => x.Key)
                    .First());
    }

    private static bool IsGasolineConsumer(Guid? defaultGroupId, Dictionary<Guid, string> groups)
    {
        if (defaultGroupId is Guid id && groups.TryGetValue(id, out var name))
            return name.Equals("Xăng", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static bool MatchesLot(string? code, NxtLotViewMode mode)
    {
        if (mode == NxtLotViewMode.All)
            return true;
        var c = (code ?? "TX").Trim().ToUpperInvariant();
        return mode switch
        {
            NxtLotViewMode.Iuu => c is "IUU",
            NxtLotViewMode.TxSscd => c is "TX" or "SSCĐ" or "SSCD",
            _ => true
        };
    }

    private static void Add(
        Dictionary<Guid, Usage> map,
        Guid taskId,
        bool isGas,
        bool isVehicle,
        decimal km,
        decimal hours,
        decimal fuel)
    {
        map.TryGetValue(taskId, out var u);
        u ??= Usage.Empty;
        km = QuantityMath.RoundQty(km);
        hours = QuantityMath.RoundQty(hours);
        fuel = QuantityMath.Whole(fuel);
        if (isGas)
        {
            u = u with
            {
                GasKm = u.GasKm + km,
                GasHours = u.GasHours + hours,
                GasVehicle = u.GasVehicle + (isVehicle ? fuel : 0),
                GasMachine = u.GasMachine + (isVehicle ? 0 : fuel)
            };
        }
        else
        {
            u = u with
            {
                DieKm = u.DieKm + km,
                DieHours = u.DieHours + hours,
                DieVehicle = u.DieVehicle + (isVehicle ? fuel : 0),
                DieMachine = u.DieMachine + (isVehicle ? 0 : fuel)
            };
        }

        map[taskId] = u;
    }

    private sealed record Usage(
        decimal GasKm,
        decimal GasHours,
        decimal DieKm,
        decimal DieHours,
        decimal GasVehicle,
        decimal GasMachine,
        decimal DieVehicle,
        decimal DieMachine)
    {
        public static Usage Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
    }
}

public sealed record QuotaSheet(
    int Year,
    int Quarter,
    NxtLotViewMode LotView,
    DateTime FromDate,
    DateTime ToDate,
    IReadOnlyList<QuotaRow> Rows,
    DateTime? CumFromDate = null);

public sealed record QuotaRow(
    string Stt,
    string Name,
    Guid? TaskId,
    bool IsHeader,
    decimal? GasolineLimit,
    decimal? DieselLimit,
    decimal? LimitTotal,
    decimal? GasolineKm,
    decimal? GasolineHours,
    decimal? DieselKm,
    decimal? DieselHours,
    decimal? GasolineVehicle,
    decimal? GasolineMachine,
    decimal? GasolineFuelTotal,
    decimal? DieselVehicle,
    decimal? DieselMachine,
    decimal? DieselFuelTotal,
    decimal? FuelTotal,
    decimal? CumGasoline,
    decimal? CumDiesel,
    decimal? CumTotal,
    decimal? RemainGasoline,
    decimal? RemainDiesel,
    decimal? RemainTotal,
    decimal? ExcessGasoline,
    decimal? ExcessDiesel,
    decimal? ExcessTotal)
{
    // Backwards compatibility properties
    public decimal? Cumulative => CumTotal;
    public decimal? Remaining => RemainTotal;
    public decimal? Excess => ExcessTotal;
}

