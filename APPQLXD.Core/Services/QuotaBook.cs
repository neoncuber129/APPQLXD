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

        var fromDate = new DateTime(year, 1, 1);
        var toDate = new DateTime(year, quarter * 3, DateTime.DaysInMonth(year, quarter * 3));

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

        var agg = AggregateUsage(db, fromDate, toDate, lotView);
        // Dòng "Cộng tiêu thụ" sau nhóm không-hao-hụt cuối (nhóm hao hụt nằm phía dưới).
        var lastConsumptionGroupId = groups.LastOrDefault(g => !g.IsLossGroup)?.Id;

        var rows = new List<QuotaRow>();
        decimal sumItoIV_gasLim = 0, sumItoIV_dieLim = 0;
        decimal sumItoIV_gasKm = 0, sumItoIV_gasHr = 0, sumItoIV_dieKm = 0, sumItoIV_dieHr = 0;
        decimal sumItoIV_gasVeh = 0, sumItoIV_gasMach = 0, sumItoIV_dieVeh = 0, sumItoIV_dieMach = 0;
        decimal sumAll_gasLim = 0, sumAll_dieLim = 0;
        decimal sumAll_gasKm = 0, sumAll_gasHr = 0, sumAll_dieKm = 0, sumAll_dieHr = 0;
        decimal sumAll_gasVeh = 0, sumAll_gasMach = 0, sumAll_dieVeh = 0, sumAll_dieMach = 0;

        foreach (var group in groups)
        {
            var groupTasks = tasks.Where(t => t.GroupId == group.Id).ToList();
            decimal gGasLim = 0, gDieLim = 0;
            decimal gGasKm = 0, gGasHr = 0, gDieKm = 0, gDieHr = 0;
            decimal gGasVeh = 0, gGasMach = 0, gDieVeh = 0, gDieMach = 0;

            var groupRows = new List<QuotaRow>();
            var idx = 1;
            foreach (var task in groupTasks)
            {
                limits.TryGetValue(task.Id, out var lim);
                var gasLim = lim?.GasolineLimit ?? 0;
                var dieLim = lim?.DieselLimit ?? 0;
                agg.TryGetValue(task.Id, out var u);
                u ??= Usage.Empty;

                groupRows.Add(MakeTaskRow(
                    idx.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    task.Name,
                    task.Id,
                    gasLim, dieLim, u,
                    isHeader: false));

                gGasLim += gasLim; gDieLim += dieLim;
                gGasKm += u.GasKm; gGasHr += u.GasHours; gDieKm += u.DieKm; gDieHr += u.DieHours;
                gGasVeh += u.GasVehicle; gGasMach += u.GasMachine; gDieVeh += u.DieVehicle; gDieMach += u.DieMachine;
                idx++;
            }

            // Dòng nhóm (= cộng nhóm) đứng trên các nhiệm vụ con — như mẫu.
            rows.Add(MakeTaskRow(
                group.Code,
                group.Name,
                null,
                gGasLim, gDieLim,
                new Usage(gGasKm, gGasHr, gDieKm, gDieHr, gGasVeh, gGasMach, gDieVeh, gDieMach),
                isHeader: true));
            rows.AddRange(groupRows);

            sumAll_gasLim += gGasLim; sumAll_dieLim += gDieLim;
            sumAll_gasKm += gGasKm; sumAll_gasHr += gGasHr; sumAll_dieKm += gDieKm; sumAll_dieHr += gDieHr;
            sumAll_gasVeh += gGasVeh; sumAll_gasMach += gGasMach; sumAll_dieVeh += gDieVeh; sumAll_dieMach += gDieMach;

            if (!group.IsLossGroup)
            {
                sumItoIV_gasLim += gGasLim; sumItoIV_dieLim += gDieLim;
                sumItoIV_gasKm += gGasKm; sumItoIV_gasHr += gGasHr; sumItoIV_dieKm += gDieKm; sumItoIV_dieHr += gDieHr;
                sumItoIV_gasVeh += gGasVeh; sumItoIV_gasMach += gGasMach; sumItoIV_dieVeh += gDieVeh; sumItoIV_dieMach += gDieMach;
            }

            if (group.Id == lastConsumptionGroupId)
            {
                rows.Add(MakeTaskRow(
                    "",
                    "Cộng tiêu thụ",
                    null,
                    sumItoIV_gasLim, sumItoIV_dieLim,
                    new Usage(sumItoIV_gasKm, sumItoIV_gasHr, sumItoIV_dieKm, sumItoIV_dieHr,
                        sumItoIV_gasVeh, sumItoIV_gasMach, sumItoIV_dieVeh, sumItoIV_dieMach),
                    isHeader: true));
            }
        }

        rows.Add(MakeTaskRow(
            "",
            "Tổng cộng",
            null,
            sumAll_gasLim, sumAll_dieLim,
            new Usage(sumAll_gasKm, sumAll_gasHr, sumAll_dieKm, sumAll_dieHr,
                sumAll_gasVeh, sumAll_gasMach, sumAll_dieVeh, sumAll_dieMach),
            isHeader: true));

        return new QuotaSheet(year, quarter, lotView, fromDate, toDate, rows);
    }

    private static QuotaRow MakeTaskRow(
        string stt,
        string name,
        Guid? taskId,
        decimal gasLim,
        decimal dieLim,
        Usage u,
        bool isHeader)
    {
        var limTotal = QuantityMath.Whole(gasLim + dieLim);
        var gasFuel = QuantityMath.Whole(u.GasVehicle + u.GasMachine);
        var dieFuel = QuantityMath.Whole(u.DieVehicle + u.DieMachine);
        var fuelTotal = QuantityMath.Whole(gasFuel + dieFuel);
        var remain = limTotal > fuelTotal ? QuantityMath.Whole(limTotal - fuelTotal) : 0;
        var over = fuelTotal > limTotal ? QuantityMath.Whole(fuelTotal - limTotal) : 0;
        return new QuotaRow(
            Stt: stt,
            Name: name,
            TaskId: taskId,
            IsHeader: isHeader,
            GasolineLimit: NullZ(gasLim),
            DieselLimit: NullZ(dieLim),
            LimitTotal: NullZ(limTotal),
            GasolineKm: NullZ(u.GasKm),
            GasolineHours: NullZ(u.GasHours),
            DieselKm: NullZ(u.DieKm),
            DieselHours: NullZ(u.DieHours),
            GasolineVehicle: NullZ(u.GasVehicle),
            GasolineMachine: NullZ(u.GasMachine),
            GasolineFuelTotal: NullZ(gasFuel),
            DieselVehicle: NullZ(u.DieVehicle),
            DieselMachine: NullZ(u.DieMachine),
            DieselFuelTotal: NullZ(dieFuel),
            FuelTotal: NullZ(fuelTotal),
            Cumulative: null,
            Remaining: NullZ(remain),
            Excess: NullZ(over));
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
    IReadOnlyList<QuotaRow> Rows);

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
    decimal? Cumulative,
    decimal? Remaining,
    decimal? Excess);
