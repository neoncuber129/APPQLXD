using System.Globalization;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Core.Persistence;

namespace APPQLXD.Core.Services;

public static class DemoActivity
{
    private static readonly HashSet<string> QuantityNames = new(StringComparer.Ordinal)
    {
        "Số lượng", "Thực nhập", "Thực xuất", "Số lượng bao bì"
    };

    private static readonly string[] FuelGroups = ["Xăng", "Dầu", "Nhớt", "Mỡ"];
    private const string PtktGroupName = "PTKT - VTXD";
    private static readonly DateTime QuarterEnd = new(2026, 9, 30);
    private static readonly (Guid Id, string Code, decimal Share)[] LotTypeMix =
    [
        (SeedIds.LotTypeTx, "TX", 0.50m),
        (SeedIds.LotTypeSscd, "SSCĐ", 0.30m),
        (SeedIds.LotTypeIuu, "IUU", 0.20m)
    ];

    private static readonly Guid[] DemoMissionTasks =
    [
        SeedIds.MissionTaskCombat,
        SeedIds.MissionTaskTraining,
        SeedIds.MissionTaskComms,
        SeedIds.MissionTaskPolicy,
        SeedIds.MissionTaskPropaganda,
        SeedIds.MissionTaskParty,
        SeedIds.MissionTaskSupply,
        SeedIds.MissionTaskBarracks,
        SeedIds.MissionTaskWeapons,
        SeedIds.MissionTaskVehicles,
        SeedIds.MissionTaskLossRegular,
        SeedIds.MissionTaskLossReady
    ];

    public static FuelResult Load(FuelSystem system, IProgress<DemoProgress>? progress = null)
    {
        if (system.ListDocuments().Any(x => x.FormNumber.StartsWith("DEMO-", StringComparison.Ordinal)))
            return FuelResult.Success(Guid.Empty, "Dữ liệu thử đã có.");

        var previous = system.GetWarehouseScope();
        try
        {
            // Hai họ kho tách biệt hoàn toàn: queue + chạy xong XD rồi mới sang PTKT (đúng scope khi ghi phiếu).
            system.SetWarehouseScope(WarehouseScope.Xd);
            var xdRun = new DemoRun();
            var xd = QueueXd(system, xdRun);
            if (!xd.Ok)
                return xd;
            var xdSaved = xdRun.Execute(progress, "Kho XD");
            if (!xdSaved.Ok)
                return xdSaved;

            system.SetWarehouseScope(WarehouseScope.Ptkt);
            var ptktRun = new DemoRun();
            var ptkt = QueuePtkt(system, ptktRun);
            if (!ptkt.Ok)
                return ptkt;
            var ptktSaved = ptktRun.Execute(progress, "Kho PTKT-VTXD");
            if (!ptktSaved.Ok)
                return ptktSaved;

            var total = xdRun.StepCount + ptktRun.StepCount;
            return FuelResult.Success(Guid.Empty,
                $"Đã tạo {total} bước dữ liệu thử tách biệt (XD {xdRun.StepCount} + PTKT {ptktRun.StepCount}). Không trộn danh mục/kho hai họ; không tạo phiếu xuất máy/phương tiện.");
        }
        finally
        {
            system.SetWarehouseScope(previous);
        }
    }

    /// <summary>Dữ liệu mẫu Kho XD: tồn/nhập/xuất lẻ/điều chuyển/tiêu thụ quý. Không tạo phiếu xuất máy/xe.</summary>
    private static FuelResult QueueXd(FuelSystem system, DemoRun run)
    {
        var main = system.GetWarehouses().FirstOrDefault(x => x.Type == WarehouseType.Main);
        if (main is null)
            return FuelResult.Fail("Chưa có Kho XD để tạo dữ liệu thử.");

        var items = PickItems(system);
        foreach (var group in FuelGroups)
        {
            if (items.All(x => x.GroupName != group))
                return FuelResult.Fail($"Danh mục chưa có mặt hàng nhóm {group}.");
        }
        if (items.Any(x => x.GroupName.Contains("PTKT", StringComparison.OrdinalIgnoreCase)))
            return FuelResult.Fail("Dữ liệu mẫu Kho XD không được dùng mặt hàng nhóm PTKT-VTXD.");

        // Trong scope XD không được thấy kho PTKT.
        if (system.GetWarehouses().Any(x => x.Type == WarehouseType.Ptkt))
            return FuelResult.Fail("Chế độ Kho XD vẫn thấy Kho PTKT-VTXD — kiểm tra phạm vi làm việc.");

        var destinations = EnsureXdDestinations(system, items);
        if (!destinations.Ok)
            return destinations.Error!;

        var aux = system.GetWarehouses()
            .Where(x => x.Type == WarehouseType.Auxiliary)
            .OrderBy(x => x.Name)
            .ToList();
        if (aux.Count < 3)
            return FuelResult.Fail("Cần ít nhất 3 vị trí kho phụ (đối tượng) trong danh mục Kho XD.");

        // Cần lại sau Ensure (vừa có thể tạo thêm đối tượng).
        aux = system.GetWarehouses()
            .Where(x => x.Type == WarehouseType.Auxiliary)
            .OrderBy(x => x.Name)
            .ToList();

        var priceOf = BuildPrices(items);
        long Price(ItemRow item) => priceOf[item.Id];
        var import = new SampleBook(system, DocumentFamily.Import);
        var export = new SampleBook(system, DocumentFamily.Export);
        var byGroup = FuelGroups.Select(name => items.First(x => x.GroupName == name)).ToList();
        var gas = byGroup[0];
        var diesel = byGroup[1];
        var oil = byGroup[2];
        var grease = byGroup[3];
        var consumersById = system.GetConsumers().ToDictionary(x => x.Id);
        ItemRow FuelFor(WarehouseRow dest) => FuelItemFor(dest, gas, diesel, consumersById);

        // Chỉ Main + Auxiliary — không đưa Kho PTKT vào phiếu XD.
        var xdPlaces = system.GetWarehouses()
            .Where(x => x.Type is WarehouseType.Main or WarehouseType.Auxiliary)
            .OrderBy(x => x.Type == WarehouseType.Main ? 0 : 1)
            .ThenBy(x => x.Name)
            .ToList();
        foreach (var warehouse in xdPlaces)
        {
            var cells = new List<OpeningRequest>();
            var fuelOnly = warehouse.IsConsumerLocation ? FuelFor(warehouse) : null;
            foreach (var item in items)
            {
                // Đối tượng máy/xe/tàu: tồn đầu chỉ 1 loại NL (xăng XOR dầu) + nhớt/mỡ.
                if (fuelOnly is not null
                    && item.GroupName is "Xăng" or "Dầu"
                    && item.Id != fuelOnly.Id)
                    continue;

                var total = warehouse.Id == main.Id ? MainQty(item.GroupName) : AuxQty(item.GroupName);
                cells.AddRange(OpeningByLotTypes(warehouse, item, Price(item), total));
            }

            var place = warehouse.Name;
            run.Add("Tồn đầu XD", () => system.SaveOpeningSheet(new OpeningSheetRequest
            {
                DocumentDate = new DateTime(2026, 1, 1),
                Cells = cells
            }) is var saved && !saved.Ok ? FuelResult.Fail($"{place}: {saved.Message}") : saved);
        }

        // Nhập: đủ TX / SSCĐ / IUU + tách lô + nhiều dòng.
        foreach (var (item, form, month, typeId) in new[]
                 {
                     (gas, "DEMO-PN-01", 2, SeedIds.LotTypeTx),
                     (diesel, "DEMO-PN-DAU", 3, SeedIds.LotTypeSscd),
                     (oil, "DEMO-PN-NHOT", 4, SeedIds.LotTypeIuu),
                     (grease, "DEMO-PN-MO", 5, SeedIds.LotTypeTx),
                     (diesel, "DEMO-PN-TX", 6, SeedIds.LotTypeTx),
                     (gas, "DEMO-PN-SSCD", 6, SeedIds.LotTypeSscd),
                     (diesel, "DEMO-PN-IUU", 7, SeedIds.LotTypeIuu)
                 })
        {
            var observed = InQty(item.GroupName);
            var actual = QuantityMath.ActualImport(observed, Vcf(item));
            var captured = item;
            var capturedPrice = Price(item);
            var capturedForm = form;
            var capturedType = typeId;
            var date = new DateTime(2026, month, 12);
            run.Add("Phiếu nhập XD", () => system.SaveSlip(ImportSlip(main, captured, capturedPrice, capturedForm, date, import,
                [Line(captured, null, observed, actual, capturedPrice, capturedType)])));
        }

        var splitItem = items.FirstOrDefault(x => x.Vcf == 1m) ?? gas;
        var splitPrice = Price(splitItem);
        var splitObservedA = 4_000m;
        var splitObservedB = 2_500m;
        run.Add("Phiếu nhập XD", () => system.SaveSlip(ImportSlip(main, splitItem, splitPrice, "DEMO-PN-TACH", new DateTime(2026, 8, 12), import,
        [
            Line(splitItem, null, splitObservedA, QuantityMath.ActualImport(splitObservedA, Vcf(splitItem)), splitPrice, SeedIds.LotTypeTx),
            Line(splitItem, null, splitObservedB, QuantityMath.ActualImport(splitObservedB, Vcf(splitItem)), splitPrice + 1, SeedIds.LotTypeSscd)
        ])));

        run.Add("Phiếu nhập XD", () => system.SaveSlip(ImportSlip(main, gas, Price(gas), "DEMO-PN-NHIEU", new DateTime(2026, 8, 18), import,
            byGroup.Select((item, i) =>
            {
                var observed = 1_000m;
                var typeId = LotTypeMix[i % LotTypeMix.Length].Id;
                return Line(item, null, observed, QuantityMath.ActualImport(observed, Vcf(item)), Price(item), typeId);
            }).ToList())));

        // Xuất lẻ Q3: khoảng 10 phiếu/quý — chỉ Retail (ưu tiên TX, vài phiếu loại khác).
        var retailPlans = new (ItemRow Item, string Form, DateTime Date, decimal Observed, decimal? Actual, long? LotPrice, Guid LotTypeId)[]
        {
            (gas, "DEMO-PX-01", new DateTime(2026, 7, 8), OutQty(gas.GroupName), null, null, SeedIds.LotTypeTx),
            (diesel, "DEMO-PX-DAU", new DateTime(2026, 7, 18), OutQty(diesel.GroupName), null, null, SeedIds.LotTypeSscd),
            (oil, "DEMO-PX-NHOT", new DateTime(2026, 7, 28), OutQty(oil.GroupName), null, null, SeedIds.LotTypeIuu),
            (grease, "DEMO-PX-MO", new DateTime(2026, 8, 5), OutQty(grease.GroupName), null, null, SeedIds.LotTypeTx),
            (gas, "DEMO-PX-LE", new DateTime(2026, 8, 12), 1_000m, QuantityMath.ActualImport(1_000m, Vcf(gas)), null, SeedIds.LotTypeTx),
            (diesel, "DEMO-PX-05", new DateTime(2026, 8, 20), OutQty(diesel.GroupName), null, null, SeedIds.LotTypeTx),
            (gas, "DEMO-PX-06", new DateTime(2026, 8, 27), OutQty(gas.GroupName), null, null, SeedIds.LotTypeSscd),
            (splitItem, "DEMO-PX-LO", new DateTime(2026, 9, 4), 200m, 200m, splitPrice + 1, SeedIds.LotTypeSscd),
            (diesel, "DEMO-PX-08", new DateTime(2026, 9, 12), OutQty(diesel.GroupName), null, null, SeedIds.LotTypeIuu),
            (gas, "DEMO-PX-NHIEU", new DateTime(2026, 9, 18), 0m, null, null, SeedIds.LotTypeTx) // multi-line handled below
        };
        foreach (var plan in retailPlans)
        {
            if (plan.Form == "DEMO-PX-NHIEU")
            {
                run.Add("Xuất lẻ XD", () =>
                {
                    var lines = new List<SlipLineInput>();
                    var typeIndex = 0;
                    foreach (var item in byGroup.Take(2))
                    {
                        var typeId = LotTypeMix[typeIndex++ % LotTypeMix.Length].Id;
                        var lot = FindLot(system, main.Id, item.Name, Price(item), typeId);
                        if (lot is null)
                            return FuelResult.Fail($"Không thấy lô {item.Name} loại {LotTypeCode(typeId)} để xuất nhiều dòng.");
                        lines.Add(Line(item, lot.LotId, 500m, 500m, lot.UnitPrice, lot.LotTypeId));
                    }

                    return system.SaveSlip(ExportSlip(main, ExportSlipMode.Retail, plan.Form, plan.Date, export, lines));
                });
                continue;
            }

            var captured = plan.Item;
            var form = plan.Form;
            var date = plan.Date;
            var observed = plan.Observed;
            var actual = plan.Actual ?? observed;
            var lotPrice = plan.LotPrice ?? Price(captured);
            var lotTypeId = plan.LotTypeId;
            run.Add("Xuất lẻ XD", () =>
            {
                var lot = FindLot(system, main.Id, captured.Name, lotPrice, lotTypeId)
                    ?? FindLot(system, main.Id, captured.Name, lotPrice);
                return lot is null
                    ? FuelResult.Fail($"Không thấy lô {captured.Name} tại Kho XD.")
                    : system.SaveSlip(ExportSlip(main, ExportSlipMode.Retail, form, date, export,
                        [Line(captured, lot.LotId, observed, actual, lot.UnitPrice, lot.LotTypeId)]));
            });
        }

        // Nhiều điều chuyển Q3 — mỗi đối tượng chỉ 1 loại NL (xăng XOR dầu); mix TX/SSCĐ/IUU.
        var machines = aux.Where(x => x.ConsumerTypeName == "Máy").OrderBy(x => x.Name).ToList();
        var vehicles = aux.Where(x => x.ConsumerTypeName == "Phương tiện").OrderBy(x => x.Name).ToList();
        var ships = aux.Where(x => x.ConsumerTypeName == "Tàu").OrderBy(x => x.Name).ToList();
        var machineWithDc = machines[0];
        var vehicleWithDc = vehicles[0];
        var machineNoDc = machines[1];
        var vehicleNoDc = vehicles[1];
        var shipNoDc = ships[0];
        var shipWithDc = ships[1];
        _ = (machineNoDc, vehicleNoDc, shipNoDc);
        var machineFuel = FuelFor(machineWithDc);
        var vehicleFuel = FuelFor(vehicleWithDc);
        var shipFuel = FuelFor(shipWithDc!);

        var transferPlans = new List<(WarehouseRow Dest, ItemRow Item, long Price, string Form, DateTime Date, decimal Qty, Guid LotTypeId, Guid MissionTaskId)>();
        var missionSeq = 0;
        Guid NextMission() => DemoMissionTasks[missionSeq++ % DemoMissionTasks.Length];
        void PlanDc(WarehouseRow dest, ItemRow item, string form, DateTime date, decimal qty, Guid lotTypeId) =>
            transferPlans.Add((dest, item, Price(item), form, date, qty, lotTypeId, NextMission()));

        PlanDc(machineWithDc, machineFuel, "DEMO-DC-MAY", new DateTime(2026, 7, 15), 800m, SeedIds.LotTypeTx);
        PlanDc(machineWithDc, machineFuel, "DEMO-DC-MAY-SSCD", new DateTime(2026, 7, 20), 500m, SeedIds.LotTypeSscd);
        PlanDc(machineWithDc, machineFuel, "DEMO-DC-MAY-IUU", new DateTime(2026, 7, 25), 400m, SeedIds.LotTypeIuu);
        PlanDc(vehicleWithDc, vehicleFuel, "DEMO-DC-XE", new DateTime(2026, 8, 10), 800m, SeedIds.LotTypeTx);
        PlanDc(vehicleWithDc, vehicleFuel, "DEMO-DC-XE-SSCD", new DateTime(2026, 8, 15), 500m, SeedIds.LotTypeSscd);
        PlanDc(vehicleWithDc, vehicleFuel, "DEMO-DC-XE-IUU", new DateTime(2026, 8, 18), 300m, SeedIds.LotTypeIuu);
        PlanDc(shipWithDc!, shipFuel, "DEMO-DC-TAU", new DateTime(2026, 9, 5), 600m, SeedIds.LotTypeTx);
        PlanDc(shipWithDc!, shipFuel, "DEMO-DC-TAU-SSCD", new DateTime(2026, 9, 8), 400m, SeedIds.LotTypeSscd);
        PlanDc(shipWithDc!, shipFuel, "DEMO-DC-TAU-IUU", new DateTime(2026, 9, 12), 250m, SeedIds.LotTypeIuu);

        var dcSeq = 1;
        var q3Days = new[]
        {
            new DateTime(2026, 7, 5), new DateTime(2026, 7, 12), new DateTime(2026, 7, 22), new DateTime(2026, 7, 29),
            new DateTime(2026, 8, 3), new DateTime(2026, 8, 14), new DateTime(2026, 8, 21), new DateTime(2026, 8, 28),
            new DateTime(2026, 9, 2), new DateTime(2026, 9, 9), new DateTime(2026, 9, 16), new DateTime(2026, 9, 23)
        };
        var dcTargets = new List<(WarehouseRow Dest, ItemRow Item, decimal Qty, Guid LotTypeId)>
        {
            (machineWithDc, machineFuel, 500m, SeedIds.LotTypeTx),
            (machineWithDc, machineFuel, 700m, SeedIds.LotTypeSscd),
            (machineWithDc, machineFuel, 400m, SeedIds.LotTypeIuu),
            (vehicleWithDc, vehicleFuel, 500m, SeedIds.LotTypeTx),
            (vehicleWithDc, vehicleFuel, 600m, SeedIds.LotTypeSscd),
            (vehicleWithDc, vehicleFuel, 300m, SeedIds.LotTypeIuu),
            (machineWithDc, machineFuel, 450m, SeedIds.LotTypeTx),
            (vehicleWithDc, vehicleFuel, 550m, SeedIds.LotTypeSscd),
            (machineWithDc, oil, 100m, SeedIds.LotTypeTx),
            (vehicleWithDc, grease, 80m, SeedIds.LotTypeTx),
            (machineWithDc, machineFuel, 350m, SeedIds.LotTypeIuu),
            (vehicleWithDc, vehicleFuel, 420m, SeedIds.LotTypeTx),
            (machineWithDc, machineFuel, 280m, SeedIds.LotTypeSscd),
            (vehicleWithDc, vehicleFuel, 380m, SeedIds.LotTypeIuu),
            (machineWithDc, machineFuel, 320m, SeedIds.LotTypeTx),
            (vehicleWithDc, vehicleFuel, 410m, SeedIds.LotTypeSscd),
            (shipWithDc!, shipFuel, 250m, SeedIds.LotTypeTx),
            (shipWithDc!, shipFuel, 300m, SeedIds.LotTypeSscd),
            (shipWithDc!, shipFuel, 200m, SeedIds.LotTypeIuu),
            (shipWithDc!, shipFuel, 180m, SeedIds.LotTypeIuu)
        };

        foreach (var extra in machines.Skip(2).Take(2))
            dcTargets.Add((extra, FuelFor(extra), 200m, SeedIds.LotTypeTx));
        foreach (var extra in vehicles.Skip(2).Take(2))
            dcTargets.Add((extra, FuelFor(extra), 200m, SeedIds.LotTypeSscd));

        for (var i = 0; i < dcTargets.Count; i++)
        {
            var (dest, item, qty, typeId) = dcTargets[i];
            var date = q3Days[i % q3Days.Length];
            PlanDc(dest, item, $"DEMO-DC-{dcSeq:0000}", date, qty, typeId);
            dcSeq++;
        }

        var transferIndex = 0;
        foreach (var plan in transferPlans)
            QueueTransfer(run, system, main, plan.Dest, plan.Item, plan.Price, plan.Form, plan.Date, plan.Qty, plan.LotTypeId, plan.MissionTaskId, export, transferIndex++);

        var destsWithDc = transferPlans.Select(x => x.Dest.Id).ToHashSet();

        // Tiêu thụ quý qua sổ (không SaveConsumptionSheet).
        foreach (var warehouse in aux.Where(x =>
                     (x.ConsumerTypeName is "Máy" or "Phương tiện") && destsWithDc.Contains(x.Id)))
        {
            var captured = warehouse;
            run.Add("Sổ tiêu thụ máy/xe", () => SaveDemoConsumerBook(system, captured));
        }

        foreach (var ship in ships)
        {
            var captured = ship;
            var hasDc = destsWithDc.Contains(ship.Id);
            run.Add("Sổ tiêu thụ tàu", () => SaveDemoShipBook(system, captured, hasDc));
        }

        return FuelResult.Success(Guid.Empty);
    }

    /// <summary>Dữ liệu mẫu Kho PTKT: chỉ kho PTKT + mặt hàng nhóm PTKT - VTXD. Không dùng danh mục/kho XD.</summary>
    private static FuelResult QueuePtkt(FuelSystem system, DemoRun run)
    {
        // Trong scope Ptkt, GetWarehouses chỉ còn kho PTKT.
        var ptkt = system.GetWarehouses().FirstOrDefault(x => x.Type == WarehouseType.Ptkt)
            ?? system.GetCatalogWarehouses().FirstOrDefault(x => x.Type == WarehouseType.Ptkt);
        if (ptkt is null)
            return FuelResult.Fail("Chưa có Kho PTKT-VTXD để tạo dữ liệu thử.");
        if (ptkt.Type != WarehouseType.Ptkt)
            return FuelResult.Fail("Kho PTKT-VTXD không hợp lệ.");

        var items = PickPtktItems(system);
        if (items.Count == 0)
            return FuelResult.Fail("Danh mục chưa có mặt hàng nhóm PTKT - VTXD.");
        if (items.Any(x => x.GroupName != PtktGroupName))
            return FuelResult.Fail("Dữ liệu mẫu PTKT chỉ được dùng mặt hàng nhóm PTKT - VTXD.");

        // Không lẫn kho XD / đối tượng XD.
        if (system.GetWarehouses().Any(x => x.Type is WarehouseType.Main or WarehouseType.Auxiliary))
            return FuelResult.Fail("Chế độ Kho PTKT-VTXD vẫn thấy kho XD — kiểm tra phạm vi làm việc.");

        var priceOf = BuildPrices(items);
        long Price(ItemRow item) => priceOf[item.Id];
        var import = new SampleBook(system, DocumentFamily.Import);
        var export = new SampleBook(system, DocumentFamily.Export);

        run.Add("Tồn đầu PTKT", () => system.SaveOpeningSheet(new OpeningSheetRequest
        {
            DocumentDate = new DateTime(2026, 1, 1),
            Cells = items.SelectMany(item => OpeningByLotTypes(ptkt, item, Price(item), PtktQty())).ToList()
        }) is var opened && !opened.Ok ? FuelResult.Fail($"{ptkt.Name}: {opened.Message}") : opened);

        var importSeq = 1;
        foreach (var item in items.Take(4))
        {
            var observed = 20m;
            var actual = QuantityMath.ActualImport(observed, Vcf(item));
            var form = $"DEMO-PTKT-PN-{importSeq:00}";
            var date = new DateTime(2026, 3 + importSeq, 10);
            var captured = item;
            var capturedPrice = Price(item);
            var capturedForm = form;
            var capturedDate = date;
            importSeq++;
            run.Add("Phiếu nhập PTKT", () => system.SaveSlip(ImportSlip(ptkt, captured, capturedPrice, capturedForm, capturedDate, import,
                [Line(captured, null, observed, actual, capturedPrice)])));
        }

        run.Add("Phiếu nhập PTKT", () => system.SaveSlip(ImportSlip(ptkt, items[0], Price(items[0]), "DEMO-PTKT-PN-NHIEU", new DateTime(2026, 8, 20), import,
            items.Take(Math.Min(3, items.Count)).Select(item =>
            {
                var observed = 5m;
                return Line(item, null, observed, QuantityMath.ActualImport(observed, Vcf(item)), Price(item));
            }).ToList())));

        var exportSeq = 1;
        foreach (var item in items.Take(2))
        {
            var outQty = 2m;
            var form = $"DEMO-PTKT-PX-{exportSeq:00}";
            var captured = item;
            var capturedForm = form;
            exportSeq++;
            run.Add("Xuất lẻ PTKT", () =>
            {
                var lot = FindLot(system, ptkt.Id, captured.Name, Price(captured));
                return lot is null
                    ? FuelResult.Fail($"Không thấy lô {captured.Name} tại Kho PTKT-VTXD.")
                    : system.SaveSlip(ExportSlip(ptkt, ExportSlipMode.Retail, capturedForm, new DateTime(2026, 9, 5), export,
                        [Line(captured, lot.LotId, outQty, outQty, lot.UnitPrice)]));
            });
        }

        var retail = items[0];
        run.Add("Xuất lẻ PTKT", () =>
        {
            var lot = FindLot(system, ptkt.Id, retail.Name, Price(retail));
            return lot is null
                ? FuelResult.Fail($"Không thấy lô {retail.Name} để xuất lẻ tại Kho PTKT-VTXD.")
                : system.SaveSlip(ExportSlip(ptkt, ExportSlipMode.Retail, "DEMO-PTKT-PX-LE", new DateTime(2026, 9, 12), export,
                    [Line(retail, lot.LotId, 1m, 1m, lot.UnitPrice)]));
        });

        run.Add("Xuất lẻ PTKT", () =>
        {
            var lines = new List<SlipLineInput>();
            foreach (var item in items.Take(2))
            {
                var lot = FindLot(system, ptkt.Id, item.Name, Price(item));
                if (lot is null)
                    return FuelResult.Fail($"Không thấy lô {item.Name} tại Kho PTKT-VTXD để xuất nhiều dòng.");
                lines.Add(Line(item, lot.LotId, 1m, 1m, lot.UnitPrice));
            }

            return system.SaveSlip(ExportSlip(ptkt, ExportSlipMode.Retail, "DEMO-PTKT-PX-NHIEU", new DateTime(2026, 9, 14), export, lines));
        });

        return FuelResult.Success(Guid.Empty);
    }

    private static void QueueTransfer(
        DemoRun run,
        FuelSystem system,
        WarehouseRow main,
        WarehouseRow destination,
        ItemRow item,
        long price,
        string form,
        DateTime date,
        decimal quantity,
        Guid lotTypeId,
        Guid missionTaskId,
        SampleBook export,
        int transferIndex)
    {
        run.Add("Điều chuyển XD", () =>
        {
            var lot = FindLot(system, main.Id, item.Name, price, lotTypeId)
                ?? FindLot(system, main.Id, item.Name, price);
            if (lot is null)
                return FuelResult.Fail($"Không thấy lô {item.Name} loại {LotTypeCode(lotTypeId)} để điều chuyển tới {destination.Name}.");

            // Phương tiện: luôn gắn số km mẫu trên phiếu ĐC.
            decimal? distance = null;
            if (destination.ConsumerTypeName == "Phương tiện")
                distance = QuantityMath.RoundQty(30m + transferIndex % 9 * 15m);

            return system.SaveSlip(TransferSlip(main, destination, item, lot, form, quantity, date, export, missionTaskId, distance));
        });
    }

    private static FuelResult SaveDemoConsumerBook(FuelSystem system, WarehouseRow warehouse)
    {
        var book = system.GetConsumerQuarterBook(QuarterEnd, warehouse.Id);
        var isVehicle = warehouse.ConsumerTypeName == "Phương tiện";
        var isMachine = warehouse.ConsumerTypeName == "Máy";
        var missionIndex = 0;
        var lineIndex = 0;
        var lines = book.Rows
            .Where(x => !x.IsOpening)
            .Select(row =>
            {
                var i = lineIndex++;
                decimal? km = row.Kilometers;
                decimal? hours = row.MachineHours;
                if (isVehicle && (km is null or 0))
                    km = QuantityMath.RoundQty(35m + i * 12m);
                if (isMachine && (hours is null or 0))
                    hours = QuantityMath.RoundQty(2m + i * 0.75m);

                return new ConsumerQuarterBookLineEdit
                {
                    LineId = row.LineId,
                    TransferDocumentId = row.DocumentId,
                    DocumentNumber = row.DocumentNumber,
                    DocumentDate = row.DocumentDate,
                    Description = row.Description,
                    Kilometers = km,
                    MachineHours = hours,
                    NormQuantity = row.NormQuantity,
                    ActualQuantity = row.ActualQuantity,
                    ManualFuelOut = false,
                    FuelOut = QuantityMath.Whole(row.FuelOut ?? row.FuelIn ?? 0),
                    ManualOilOut = false,
                    OilOut = QuantityMath.Whole(row.OilOut ?? row.OilIn ?? 0),
                    LotTypeId = row.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : row.LotTypeId,
                    LotTypeCode = string.IsNullOrWhiteSpace(row.LotTypeCode) ? "TX" : row.LotTypeCode,
                    MissionTaskId = row.MissionTaskId ?? DemoMissionTasks[missionIndex++ % DemoMissionTasks.Length]
                };
            })
            .ToList();
        if (lines.Count == 0)
            return FuelResult.Fail($"Sổ {warehouse.Name}: không có dòng điều chuyển để lưu.");
        return system.SaveConsumerQuarterBook(new ConsumerQuarterBookSaveRequest
        {
            ConsumerId = warehouse.Id,
            QuarterDate = QuarterEnd,
            Lines = lines
        });
    }

    private static FuelResult SaveDemoShipBook(FuelSystem system, WarehouseRow ship, bool hasDc)
    {
        var dto = system.GetShipQuarterBook(QuarterEnd, ship.Id);
        var edits = new List<ShipQuarterBookLineEdit>();
        var code = string.IsNullOrWhiteSpace(ship.Code) ? ship.Name : ship.Code;
        var missionIndex = 0;
        Guid NextMission() => DemoMissionTasks[missionIndex++ % DemoMissionTasks.Length];
        var fuelGroup = string.IsNullOrWhiteSpace(dto.FuelGroupName) ? "Dầu" : dto.FuelGroupName;
        var n = 1;

        // Dòng mẫu có giờ hoạt động (tự tính NL theo định mức).
        void AddHourSample(Guid typeId, string typeCode, int variant)
        {
            edits.Add(new ShipQuarterBookLineEdit
            {
                DocumentNumber = $"DEMO-GD-{code}-{typeCode}-{n++}",
                DocumentDate = QuarterEnd.AddDays(-(variant + 1) * 3),
                Description = $"Chạy thử {typeCode}",
                MainOpsCount = 1,
                HoursAtBerth = QuantityMath.RoundQty(1.5m + variant),
                HoursCx25 = QuantityMath.RoundQty(0.5m + variant * 0.25m),
                HoursCx50 = QuantityMath.RoundQty(2m + variant * 0.5m),
                HoursCx75 = QuantityMath.RoundQty(1m + variant * 0.25m),
                HoursCx100 = QuantityMath.RoundQty(3m + variant),
                AuxOpsCount = 1,
                AuxHours = QuantityMath.RoundQty(1m + variant * 0.5m),
                ManualFuelOut = false,
                LotTypeId = typeId,
                LotTypeCode = typeCode,
                MissionTaskId = NextMission()
            });
        }

        if (hasDc)
        {
            var byType = dto.Rows
                .Where(x => x.IsTransfer)
                .GroupBy(x => x.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : x.LotTypeId)
                .Select(g => (
                    TypeId: g.Key,
                    Code: g.Select(x => x.LotTypeCode).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? LotTypeCode(g.Key),
                    Fuel: QuantityMath.Whole(g.Sum(x => x.FuelIn ?? 0))))
                .Where(x => x.Fuel > 0)
                .ToList();
            var variant = 0;
            foreach (var part in byType)
            {
                AddHourSample(part.TypeId, part.Code, variant++);
                var take = QuantityMath.Whole(part.Fuel * 0.7m);
                if (take <= 0)
                    take = part.Fuel;
                edits.Add(new ShipQuarterBookLineEdit
                {
                    DocumentNumber = $"DEMO-TT-{code}-{part.Code}-{n++}",
                    DocumentDate = QuarterEnd,
                    Description = $"Tiêu thụ thử {part.Code}",
                    ManualFuelOut = true,
                    FuelOutManual = take,
                    LotTypeId = part.TypeId,
                    LotTypeCode = part.Code,
                    MissionTaskId = NextMission()
                });
            }
        }
        else
        {
            // Tàu không ĐC: dòng có giờ + dòng nhập tay từ tồn đầu.
            var variant = 0;
            foreach (var (typeId, typeCode, share) in LotTypeMix)
            {
                AddHourSample(typeId, typeCode, variant++);
                var take = QuantityMath.Whole(AuxQty(fuelGroup) * share * 0.5m);
                if (take <= 0)
                    continue;
                edits.Add(new ShipQuarterBookLineEdit
                {
                    DocumentNumber = $"DEMO-TT-{code}-{typeCode}-{n++}",
                    DocumentDate = QuarterEnd,
                    Description = $"Tiêu thụ thử {typeCode}",
                    ManualFuelOut = true,
                    FuelOutManual = take,
                    LotTypeId = typeId,
                    LotTypeCode = typeCode,
                    MissionTaskId = NextMission()
                });
            }
        }

        if (edits.Count == 0)
            return FuelResult.Fail($"Sổ tàu {ship.Name}: chưa có dòng tiêu thụ.");

        return system.SaveShipQuarterBook(new ShipQuarterBookSaveRequest
        {
            ConsumerId = ship.Id,
            QuarterDate = QuarterEnd,
            FuelGroupName = dto.FuelGroupName,
            Lines = edits
        });
    }

    private sealed record XdDestinations(FuelResult? Error, WarehouseRow? Machine, WarehouseRow? Vehicle, WarehouseRow? Ship)
    {
        public bool Ok => Error is null || Error.Ok;
        public static XdDestinations Fail(string message) => new(FuelResult.Fail(message), null, null, null);
        public static XdDestinations OkResult(WarehouseRow machine, WarehouseRow vehicle, WarehouseRow ship) =>
            new(null, machine, vehicle, ship);
    }

    /// <summary>Bảo đảm ≥2 Máy, ≥2 Phương tiện, ≥2 Tàu — mỗi đối tượng 1 loại NL (xăng XOR dầu).</summary>
    private static XdDestinations EnsureXdDestinations(FuelSystem system, IReadOnlyList<ItemRow> items)
    {
        var gas = items.FirstOrDefault(x => x.GroupName == "Xăng") ?? items[0];
        var diesel = items.FirstOrDefault(x => x.GroupName == "Dầu") ?? gas;

        FuelResult EnsureMachine(int index)
        {
            var fuel = index % 2 == 0 ? diesel : gas;
            return system.SaveConsumer(new ConsumerEdit
            {
                Code = $"MAY-DEMO-{index}",
                Name = $"Máy thử demo {index}",
                Type = ConsumerType.Machine,
                DefaultGroupId = fuel.GroupId,
                DefaultItemId = fuel.Id,
                RollTransfersIntoQuarter = true
            });
        }

        FuelResult EnsureVehicle(int index)
        {
            var fuel = index % 2 == 0 ? gas : diesel;
            return system.SaveConsumer(new ConsumerEdit
            {
                Code = $"XE-DEMO-{index}",
                Name = $"Xe thử demo {index}",
                Type = ConsumerType.Vehicle,
                Norm = 0.25m,
                DefaultGroupId = fuel.GroupId,
                DefaultItemId = fuel.Id,
                RollTransfersIntoQuarter = true
            });
        }

        FuelResult EnsureShip(int index)
        {
            var fuel = index % 2 == 0 ? diesel : gas;
            return system.SaveConsumer(new ConsumerEdit
            {
                Code = $"TAU-DEMO-{index}",
                Name = $"Tàu thử demo {index}",
                Type = ConsumerType.Ship,
                DefaultGroupId = fuel.GroupId,
                DefaultItemId = fuel.Id,
                ShipType = $"Loại demo {index}",
                NormFactors =
                [
                    new ConsumerNormFactorEdit
                    {
                        GroupId = fuel.GroupId,
                        Name = "Chạy máy chính",
                        Value = 1m,
                        SortOrder = 0
                    }
                ]
            });
        }

        while (system.GetConsumers().Count(x => x.Type == ConsumerType.Machine) < 2)
        {
            var n = system.GetConsumers().Count(x => x.Type == ConsumerType.Machine) + 1;
            var saved = EnsureMachine(n);
            if (!saved.Ok)
                return XdDestinations.Fail(saved.Message);
        }

        while (system.GetConsumers().Count(x => x.Type == ConsumerType.Vehicle && x.Norm is > 0) < 2)
        {
            var n = system.GetConsumers().Count(x => x.Type == ConsumerType.Vehicle) + 1;
            var saved = EnsureVehicle(n);
            if (!saved.Ok)
                return XdDestinations.Fail(saved.Message);
        }

        while (system.GetConsumers().Count(x => x.Type == ConsumerType.Ship) < 2)
        {
            var n = system.GetConsumers().Count(x => x.Type == ConsumerType.Ship) + 1;
            var saved = EnsureShip(n);
            if (!saved.Ok)
                return XdDestinations.Fail(saved.Message);
        }

        var warehouses = system.GetWarehouses().Where(x => x.Type == WarehouseType.Auxiliary).ToList();
        var machine = warehouses.FirstOrDefault(x => x.ConsumerTypeName == "Máy");
        var vehicle = warehouses.FirstOrDefault(x => x.ConsumerTypeName == "Phương tiện");
        var ship = warehouses.FirstOrDefault(x => x.ConsumerTypeName == "Tàu");
        if (machine is null || vehicle is null || ship is null)
            return XdDestinations.Fail("Không tạo được vị trí kho phụ Máy / Phương tiện / Tàu cho dữ liệu thử.");
        if (warehouses.Count(x => x.ConsumerTypeName == "Máy") < 2
            || warehouses.Count(x => x.ConsumerTypeName == "Phương tiện") < 2
            || warehouses.Count(x => x.ConsumerTypeName == "Tàu") < 2)
            return XdDestinations.Fail("Cần ít nhất 2 vị trí mỗi loại Máy / Phương tiện / Tàu.");

        return XdDestinations.OkResult(machine, vehicle, ship);
    }

    private static ItemRow FuelItemFor(
        WarehouseRow dest,
        ItemRow gas,
        ItemRow diesel,
        IReadOnlyDictionary<Guid, ConsumerRow> consumers)
    {
        if (consumers.TryGetValue(dest.Id, out var consumer))
        {
            if (consumer.DefaultItemId is Guid itemId)
            {
                if (gas.Id == itemId)
                    return gas;
                if (diesel.Id == itemId)
                    return diesel;
            }

            if (string.Equals(consumer.DefaultGroupName, "Xăng", StringComparison.OrdinalIgnoreCase))
                return gas;
            if (string.Equals(consumer.DefaultGroupName, "Dầu", StringComparison.OrdinalIgnoreCase))
                return diesel;
            if (consumer.DefaultGroupId == gas.GroupId)
                return gas;
            if (consumer.DefaultGroupId == diesel.GroupId)
                return diesel;
        }

        // Fallback theo loại đối tượng khi chưa gán NL mặc định.
        return dest.ConsumerTypeName == "Phương tiện" ? gas : diesel;
    }

    private static Dictionary<Guid, long> BuildPrices(IReadOnlyList<ItemRow> items)
    {
        var priceOf = new Dictionary<Guid, long>();
        foreach (var group in items.GroupBy(x => x.GroupName))
        {
            var index = 0;
            foreach (var item in group)
                priceOf[item.Id] = PriceFor(item, index++);
        }

        return priceOf;
    }

    private static List<ItemRow> PickItems(FuelSystem system) =>
        system.GetItems()
            .Where(x => FuelGroups.Contains(x.GroupName)
                && !x.GroupName.Contains("PTKT", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => Array.IndexOf(FuelGroups, x.GroupName))
            .ThenBy(x => x.Name)
            .ToList();

    private static List<ItemRow> PickPtktItems(FuelSystem system) =>
        system.GetItems()
            .Where(x => x.GroupName == PtktGroupName)
            .OrderBy(x => x.Name)
            .ToList();

    private static long PriceFor(ItemRow item, int indexInGroup) => item.GroupName switch
    {
        "Xăng" => 25_000 + indexInGroup * 100,
        "Dầu" => 22_000 + indexInGroup * 100,
        "Nhớt" => 90_000 + indexInGroup * 100,
        "Mỡ" => 50_000 + indexInGroup * 100,
        PtktGroupName => 500_000 + indexInGroup * 10_000,
        _ => 18_000 + indexInGroup * 100
    };

    private static decimal PtktQty() => 50m;

    private static decimal MainQty(string group) => group switch
    {
        "Xăng" or "Dầu" => 100_000m,
        "Nhớt" => 20_000m,
        "Mỡ" => 10_000m,
        _ => 5_000m
    };

    private static decimal AuxQty(string group) => group switch
    {
        "Xăng" or "Dầu" => 8_000m,
        "Nhớt" => 1_500m,
        "Mỡ" => 800m,
        _ => 500m
    };

    private static decimal InQty(string group) => group switch
    {
        "Xăng" or "Dầu" => 5_000m,
        "Nhớt" => 500m,
        "Mỡ" => 300m,
        _ => 200m
    };

    private static decimal OutQty(string group) => group switch
    {
        "Xăng" or "Dầu" => 800m,
        "Nhớt" => 80m,
        "Mỡ" => 40m,
        _ => 20m
    };

    private static OpeningRequest Opening(WarehouseRow warehouse, ItemRow item, long price, decimal actual, Guid lotTypeId) => new()
    {
        DocumentDate = new DateTime(2026, 7, 1),
        WarehouseId = warehouse.Id,
        WarehouseName = warehouse.Name,
        WarehouseTypeName = warehouse.TypeName,
        ItemId = item.Id,
        ItemName = item.Name,
        GroupName = item.GroupName,
        UnitName = item.UnitName,
        QualityInfo = item.QualityInfo,
        Temperature = item.Temperature,
        UnitPrice = price,
        ActualQuantity = QuantityMath.Whole(actual),
        LotTypeId = lotTypeId
    };

    private static List<OpeningRequest> OpeningByLotTypes(WarehouseRow warehouse, ItemRow item, long price, decimal total)
    {
        var cells = new List<OpeningRequest>();
        foreach (var (typeId, _, qty) in SplitByLotType(total))
            cells.Add(Opening(warehouse, item, price, qty, typeId));
        return cells;
    }

    private static List<(Guid TypeId, string Code, decimal Qty)> SplitByLotType(decimal total)
    {
        total = QuantityMath.Whole(total);
        var parts = new List<(Guid, string, decimal)>();
        decimal used = 0;
        for (var i = 0; i < LotTypeMix.Length; i++)
        {
            var (id, code, share) = LotTypeMix[i];
            var qty = i == LotTypeMix.Length - 1
                ? QuantityMath.Whole(total - used)
                : QuantityMath.Whole(total * share);
            if (qty < 0)
                qty = 0;
            used = QuantityMath.Whole(used + qty);
            if (qty > 0)
                parts.Add((id, code, qty));
        }

        return parts;
    }

    private static string LotTypeCode(Guid lotTypeId)
    {
        foreach (var (id, code, _) in LotTypeMix)
        {
            if (id == lotTypeId)
                return code;
        }

        return lotTypeId == SeedIds.LotTypeSscd ? "SSCĐ"
            : lotTypeId == SeedIds.LotTypeIuu ? "IUU"
            : "TX";
    }

    private static SlipRequest ImportSlip(WarehouseRow warehouse, ItemRow item, long price, string form, DateTime date, SampleBook book, IReadOnlyList<SlipLineInput> lines) =>
        Paper(warehouse, false, ExportSlipMode.Issue, form, date, book.Next(lines, false), lines);

    private static SlipRequest ExportSlip(WarehouseRow warehouse, ExportSlipMode mode, string form, DateTime date, SampleBook book, IReadOnlyList<SlipLineInput> lines) =>
        Paper(warehouse, true, mode, form, date, book.Next(lines, true), lines);

    private static SlipRequest TransferSlip(
        WarehouseRow source,
        WarehouseRow destination,
        ItemRow item,
        LotOption lot,
        string form,
        decimal quantity,
        DateTime date,
        SampleBook book,
        Guid? missionTaskId = null,
        decimal? distanceOverride = null)
    {
        var lines = new[]
        {
            Line(item, lot.LotId, quantity, quantity, lot.UnitPrice, lot.LotTypeId, lot.LotTypeId)
        };
        var pack = book.Next(lines, true);
        if (pack.Text.ContainsKey("Đơn vị nhận"))
            pack.Text["Đơn vị nhận"] = destination.Name;

        decimal? distance = distanceOverride;
        if (distance is null)
        {
            var kilometers = pack.Text.TryGetValue("Số km", out var kmText) ? kmText.Trim() : "";
            if (kilometers.Length > 0
                && (decimal.TryParse(kilometers, NumberStyles.Any, CultureInfo.CurrentCulture, out var km)
                    || decimal.TryParse(kilometers, NumberStyles.Any, CultureInfo.InvariantCulture, out km))
                && km >= 0)
                distance = QuantityMath.RoundQty(km);
        }

        if (distance is decimal d && d >= 0)
            pack.Text["Số km"] = d.ToString(CultureInfo.InvariantCulture);

        // Phương tiện: giữ ManualQuantity để số lượng ĐC không bị tính lại từ km×định mức.
        var isVehicle = destination.ConsumerTypeName == "Phương tiện";
        return Paper(source, true, ExportSlipMode.Transfer, form, date, pack, lines,
            destinationId: destination.Id,
            destinationName: destination.Name,
            consumerId: destination.IsConsumerLocation ? destination.Id : null,
            distance: isVehicle ? distance : null,
            manualQuantity: destination.IsConsumerLocation,
            missionTaskId: missionTaskId);
    }

    private static SlipRequest Paper(
        WarehouseRow warehouse,
        bool export,
        ExportSlipMode mode,
        string form,
        DateTime date,
        SamplePack pack,
        IReadOnlyList<SlipLineInput> lines,
        Guid? destinationId = null,
        string destinationName = "",
        Guid? consumerId = null,
        decimal? distance = null,
        bool manualQuantity = false,
        Guid? missionTaskId = null)
    {
        string Text(params string[] names)
        {
            foreach (var name in names)
            {
                if (pack.Text.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        var amount = lines.Sum(x => x.Amount ?? 0);
        return new SlipRequest
        {
            IsExport = export,
            ExportMode = mode,
            ConsumerId = consumerId,
            Distance = distance,
            ManualQuantity = manualQuantity,
            DocumentDate = date,
            FormNumber = form,
            WarehouseId = warehouse.Id,
            WarehouseName = warehouse.Name,
            WarehouseTypeName = warehouse.TypeName,
            OrganizationName = Text("Cơ quan"),
            UnitTitle = Text("Đơn vị"),
            SenderUnit = Text("Đơn vị giao hàng", "Đơn vị giao"),
            ReceiverUnit = Text("Đơn vị nhận hàng", "Đơn vị nhận"),
            Nature = Text("Tính chất nhập", "Tính chất xuất"),
            ContractOrOrder = Text("Theo hợp đồng số", "Theo lệnh (KH)"),
            CarrierUnit = Text("Đơn vị vận chuyển"),
            PriceValidUntil = Text("Có giá đến ngày"),
            DelivererName = Text("Người giao hàng"),
            IntroDocument = Text("Giấy giới thiệu và CMT"),
            VehiclePlate = Text("Số xe"),
            CalibrationVolume = Text("Dung tích kiểm định"),
            ReceivedVolume = Text("Dung tích nhận hàng"),
            PackageCount = Text("Số lượng bao bì"),
            ReceiverPerson = Text("Người nhận"),
            Kilometers = Text("Số km"),
            Mission = Text("Nhiệm vụ"),
            MissionTaskId = missionTaskId,
            Note = Text("Ghi chú"),
            SignerReceiver = Text("Chữ ký người nhận"),
            SignerDeliverer = Text("Chữ ký người giao"),
            SignerFinance = Text("Chữ ký tài chính"),
            SignerWriter = Text("Chữ ký người viết phiếu"),
            SignerChief = Text("Chữ ký trưởng ban HC-KT"),
            SignerCommander = Text("Chữ ký chỉ huy đơn vị"),
            AmountInWords = MoneyWords.ToDong(amount),
            DestinationWarehouseId = destinationId,
            DestinationWarehouseName = destinationName,
            Fields = pack.Fields(),
            Lines = lines
        };
    }

    private static SlipLineInput Line(
        ItemRow item,
        Guid? lotId,
        decimal observed,
        decimal actual,
        decimal price,
        Guid? lotTypeId = null,
        Guid? destinationLotTypeId = null)
    {
        observed = QuantityMath.Whole(observed);
        actual = QuantityMath.Whole(actual);
        if (actual <= 0)
            actual = QuantityMath.ActualImport(observed, Vcf(item));
        var typeId = lotTypeId is Guid id && id != Guid.Empty ? id : SeedIds.LotTypeTx;
        return new SlipLineInput
        {
            LotId = lotId,
            ItemId = item.Id,
            ItemName = item.Name,
            GroupName = item.GroupName,
            UnitName = item.UnitName,
            ItemCode = item.Code,
            QualityGrade = string.IsNullOrWhiteSpace(item.QualityInfo) ? "1" : item.QualityInfo,
            Temperature = item.Temperature,
            Density = item.Density,
            ObservedQuantity = observed,
            ActualQuantity = actual,
            Vcf = Vcf(item),
            UnitPrice = price,
            Amount = QuantityMath.RoundMoney(price * actual),
            LotTypeId = typeId,
            DestinationLotTypeId = destinationLotTypeId ?? typeId
        };
    }

    private static decimal Vcf(ItemRow item) => item.Vcf > 0 ? item.Vcf : 1m;

    private static LotOption? FindLot(FuelSystem system, Guid warehouseId, string itemName, long price, Guid? lotTypeId = null)
    {
        var lots = system.GetLots(warehouseId).Where(x => x.ItemName == itemName && x.UnitPrice == price);
        if (lotTypeId is Guid typeId && typeId != Guid.Empty)
            lots = lots.Where(x => x.LotTypeId == typeId);
        return lots.OrderByDescending(x => x.Quantity).FirstOrDefault();
    }

    private static string WholeText(decimal value) =>
        QuantityMath.Whole(value).ToString("0", CultureInfo.InvariantCulture);

    private static bool TryNumber(string text, out decimal value)
    {
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out value))
            return true;
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private sealed class DemoRun
    {
        private readonly List<(string Phase, Func<FuelResult> Work)> _steps = [];

        public int StepCount => _steps.Count;

        public void Add(string phase, Func<FuelResult> work) => _steps.Add((phase, work));

        public FuelResult Execute(IProgress<DemoProgress>? progress, string familyLabel)
        {
            var totals = _steps.GroupBy(x => x.Phase).ToDictionary(x => x.Key, x => x.Count());
            var phaseDone = new Dictionary<string, int>();
            for (var i = 0; i < _steps.Count; i++)
            {
                var (phase, work) = _steps[i];
                var done = phaseDone.GetValueOrDefault(phase);
                progress?.Report(new DemoProgress($"{familyLabel}: {phase}", done, totals[phase], i, _steps.Count));
                var result = work();
                if (!result.Ok)
                    return FuelResult.Fail($"{familyLabel} — {phase}: {result.Message}");
                phaseDone[phase] = done + 1;
            }

            progress?.Report(new DemoProgress($"{familyLabel}: Xong", 1, 1, _steps.Count, _steps.Count));
            return FuelResult.Success(Guid.Empty);
        }
    }

    private sealed class SampleBook
    {
        private readonly IReadOnlyList<FieldRow> _fields;
        private readonly Dictionary<string, List<string>> _samples;
        private int _variant;

        public SampleBook(FuelSystem system, DocumentFamily family)
        {
            _fields = system.GetFields(family).Where(x => x.IsVisible).ToList();
            _samples = [];
            var setName = family == DocumentFamily.Import ? ExcelSampleData.ImportSetName : ExcelSampleData.ExportSetName;
            var set = system.GetSampleSets(family).FirstOrDefault(x => x.Name == setName)
                ?? system.GetSampleSets(family).FirstOrDefault();
            if (set is null)
                return;
            foreach (var row in system.GetSampleValues(set.Id))
            {
                var value = row.Value.Trim();
                if (value.Length == 0)
                    continue;
                if (!_samples.TryGetValue(row.FieldName, out var list))
                {
                    list = [];
                    _samples[row.FieldName] = list;
                }

                if (!list.Contains(value))
                    list.Add(value);
            }
        }

        public SamplePack Next(IReadOnlyList<SlipLineInput> lines, bool export)
        {
            var variant = _variant++;
            var text = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in _fields)
                text[field.Name] = Value(field, variant);

            var observed = lines.Sum(x => QuantityMath.Whole(x.ObservedQuantity));
            var actual = lines.Sum(x => QuantityMath.Whole(x.ActualQuantity ?? 0));
            PutQuantity(text, "Số lượng", observed);
            PutQuantity(text, export ? "Thực xuất" : "Thực nhập", actual);
            if (lines.Count == 1)
            {
                PutQuantity(text, "Đơn giá", lines[0].UnitPrice);
                PutQuantity(text, "Thành tiền", lines[0].Amount ?? 0);
            }

            return new SamplePack(_fields, text);
        }

        public IReadOnlyList<FieldInput> NextFields() => Next([], true).Fields();

        private string Value(FieldRow field, int variant)
        {
            if (_samples.TryGetValue(field.Name, out var options) && options.Count > 0)
            {
                var sample = options[variant % options.Count];
                if (!QuantityNames.Contains(field.Name))
                    return sample;
                if (TryNumber(sample, out var number))
                {
                    var whole = QuantityMath.Whole(number);
                    if (whole > 0)
                        return WholeText(whole);
                }
            }

            return Fallback(field, variant);
        }

        private static void PutQuantity(Dictionary<string, string> text, string name, decimal value)
        {
            if (!text.ContainsKey(name))
                return;
            var whole = QuantityMath.Whole(value);
            if (whole > 0)
                text[name] = WholeText(whole);
        }

        private static string Fallback(FieldRow field, int variant)
        {
            if (field.DataType == FieldDataType.Number || QuantityNames.Contains(field.Name))
                return (100 + variant).ToString(CultureInfo.InvariantCulture);
            if (field.DataType == FieldDataType.Date)
                return "31/12/2026";
            if (field.DataType == FieldDataType.Boolean)
                return "Có";
            return field.Name switch
            {
                "Số hóa đơn" => $"HD-DEMO-{variant + 1:00}",
                "Theo hợp đồng số" => "05/HĐMB/HĐBP18-TPP",
                "Theo lệnh (KH)" => "KH-DEMO-01",
                "Dung tích kiểm định" => "200",
                "Dung tích nhận hàng" => "200",
                "Mã số" => "MS-01",
                "Chất lượng" => "1",
                "Ghi chú" => "Dữ liệu thử",
                "Giấy giới thiệu và CMT" => "GT-DEMO",
                "Đơn vị vận chuyển" => "Hải đoàn Biên phòng 18",
                "Nhiệt độ" => "36",
                "Tỉ trọng" => "0,835",
                "Hệ số VCF" => "1",
                _ when field.Name.StartsWith("Chữ ký", StringComparison.Ordinal) => "Nguyễn Văn A",
                _ => "Dữ liệu thử"
            };
        }
    }

    private sealed class SamplePack
    {
        private readonly IReadOnlyList<FieldRow> _fields;

        public SamplePack(IReadOnlyList<FieldRow> fields, Dictionary<string, string> text)
        {
            _fields = fields;
            Text = text;
        }

        public Dictionary<string, string> Text { get; }

        public List<FieldInput> Fields() => _fields
            .Where(x => Text.TryGetValue(x.Name, out var value) && !string.IsNullOrWhiteSpace(value))
            .Select(x => new FieldInput { FieldId = x.Id, Value = Text[x.Name].Trim() })
            .ToList();
    }
}
