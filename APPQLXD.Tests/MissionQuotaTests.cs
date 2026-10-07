using APPQLXD.Core;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Export;
using APPQLXD.Core.Models;
using APPQLXD.Core.Persistence;

namespace APPQLXD.Tests;

public sealed class MissionQuotaTests
{
    [Fact]
    public void Mission_catalog_is_seeded()
    {
        using var app = TestApp.Create();
        var groups = app.System.GetMissionGroups();
        Assert.Contains(groups, x => x.Id == SeedIds.MissionGroupStaff);
        Assert.Contains(groups, x => x.IsLossGroup);
        var tasks = app.System.GetMissionTasks();
        Assert.Contains(tasks, x => x.Id == SeedIds.MissionTaskCombat);
        Assert.Contains(tasks, x => x.Display.Contains("Tác chiến", StringComparison.Ordinal));
    }

    [Fact]
    public void Transfer_slip_keeps_mission_task_and_syncs_to_consumer_book()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(100m, 20000m)).Ok);
        var lot = app.System.GetLots(SeedIds.WhMain).First(x => x.UnitPrice == 20000).LotId;
        var vehicle = app.System.GetConsumers().Single(x => x.Id == SeedIds.Vehicle);
        var main = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhMain);
        var date = new DateTime(2026, 3, 10);
        var saved = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            ConsumerId = vehicle.Id,
            Distance = 40m,
            Norm = 0.25m,
            DestinationWarehouseId = vehicle.Id,
            DestinationWarehouseName = vehicle.Name,
            DocumentDate = date,
            FormNumber = "DC-NV-01",
            WarehouseId = main.Id,
            WarehouseName = main.Name,
            WarehouseTypeName = main.TypeName,
            Mission = "Diễn giải tay",
            MissionTaskId = SeedIds.MissionTaskCombat,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Lái xe" }],
            Lines =
            [
                new SlipLineInput
                {
                    LotId = lot,
                    ItemName = "Xăng RON 95",
                    ObservedQuantity = 1m,
                    ActualQuantity = 1m,
                    Vcf = 1m,
                    UnitPrice = 20000m
                }
            ]
        });
        Assert.True(saved.Ok, saved.Message);
        var doc = app.System.GetDocument(saved.Id!.Value)!;
        Assert.Equal(SeedIds.MissionTaskCombat, doc.Slip.MissionTaskId);
        Assert.Equal("Diễn giải tay", doc.Slip.Mission);

        var book = app.System.GetConsumerQuarterBook(date, vehicle.Id);
        var line = Assert.Single(book.Rows, x => x.DocumentId == doc.Id);
        Assert.Equal(SeedIds.MissionTaskCombat, line.MissionTaskId);

        var updated = app.System.SaveConsumerQuarterBook(new ConsumerQuarterBookSaveRequest
        {
            ConsumerId = vehicle.Id,
            QuarterDate = date,
            Lines =
            [
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = doc.Id,
                    DocumentNumber = line.DocumentNumber,
                    DocumentDate = line.DocumentDate,
                    Description = line.Description,
                    Kilometers = line.Kilometers,
                    MachineHours = line.MachineHours,
                    NormQuantity = line.NormQuantity,
                    ActualQuantity = line.ActualQuantity,
                    ManualFuelOut = line.ManualFuelOut,
                    FuelOut = line.FuelOut ?? 0,
                    ManualOilOut = line.ManualOilOut,
                    OilOut = line.OilOut ?? 0,
                    LotTypeId = line.LotTypeId,
                    LotTypeCode = line.LotTypeCode,
                    MissionTaskId = SeedIds.MissionTaskTraining
                }
            ]
        });
        Assert.True(updated.Ok, updated.Message);
        var again = app.System.GetDocument(doc.Id)!;
        Assert.Equal(SeedIds.MissionTaskTraining, again.Slip.MissionTaskId);
    }

    [Fact]
    public void Mission_year_limits_are_seeded_for_quota_sheet()
    {
        using var app = TestApp.Create();
        var sheet = app.System.GetQuotaSheet(2026, 3);
        Assert.Contains(sheet.Rows, x => x.TaskId == SeedIds.MissionTaskCombat && x.GasolineLimit > 0);
        Assert.Contains(sheet.Rows, x => x.TaskId == SeedIds.MissionTaskLossRegular && x.DieselLimit > 0);
    }

    [Fact]
    public void Quota_sheet_uses_year_limits_and_lot_view()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveMissionYearLimit(SeedIds.MissionTaskCombat, 2026, NxtLotViewMode.TxSscd, 1000m, 2000m).Ok);
        Assert.True(app.System.SaveMissionYearLimit(SeedIds.MissionTaskCombat, 2026, NxtLotViewMode.Iuu, 100m, 200m).Ok);
        var sheet = app.System.GetQuotaSheet(2026, 1, NxtLotViewMode.TxSscd);
        var combat = Assert.Single(sheet.Rows, x => x.TaskId == SeedIds.MissionTaskCombat);
        Assert.Equal(1000m, combat.GasolineLimit);
        Assert.Equal(2000m, combat.DieselLimit);
        Assert.Contains(sheet.Rows, x => x.IsHeader && x.Stt == "I" && x.Name == "Khối tham mưu");
        Assert.DoesNotContain(sheet.Rows, x => x.Name.StartsWith("Cộng I", StringComparison.Ordinal));
        var groupIdx = sheet.Rows.ToList().FindIndex(x => x.IsHeader && x.Stt == "I");
        var taskIdx = sheet.Rows.ToList().FindIndex(x => x.TaskId == SeedIds.MissionTaskCombat);
        Assert.True(groupIdx >= 0 && taskIdx > groupIdx);
        var congIdx = sheet.Rows.ToList().FindIndex(x => x.IsHeader && x.Name == "Cộng tiêu thụ");
        var lossIdx = sheet.Rows.ToList().FindIndex(x => x.IsHeader && x.Stt == "V");
        var totalIdx = sheet.Rows.ToList().FindIndex(x => x.IsHeader && x.Name == "Tổng cộng");
        Assert.True(congIdx > 0 && lossIdx > congIdx && totalIdx > lossIdx);

        var iuu = app.System.GetQuotaSheet(2026, 1, NxtLotViewMode.Iuu);
        Assert.Equal(NxtLotViewMode.Iuu, iuu.LotView);
        var combatIuu = Assert.Single(iuu.Rows, x => x.TaskId == SeedIds.MissionTaskCombat);
        Assert.Equal(100m, combatIuu.GasolineLimit);
        Assert.Equal(200m, combatIuu.DieselLimit);
    }

    [Fact]
    public void Quota_excel_export_has_rows_and_xlsx_signature()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveMissionYearLimit(SeedIds.MissionTaskCombat, 2026, NxtLotViewMode.TxSscd, 1000m, 2000m).Ok);
        var sheet = app.System.GetQuotaSheet(2026, 1, NxtLotViewMode.TxSscd);
        var bytes = QuotaExcelWriter.Write(sheet);
        Assert.True(bytes.Length > 100);
        Assert.Equal(0x50, bytes[0]); // P
        Assert.Equal(0x4B, bytes[1]); // K — zip/xlsx
        Assert.Contains(sheet.Rows, x => x.TaskId == SeedIds.MissionTaskCombat);
    }

    [Fact]
    public void Quota_classifies_ship_fuel_by_default_group_not_book_label()
    {
        using var app = TestApp.Create();
        var ship = app.System.GetConsumers().Single(x => x.Id == SeedIds.Ship);
        Assert.True(app.System.SaveConsumer(new ConsumerEdit
        {
            Id = ship.Id,
            Code = ship.Code,
            Name = ship.Name,
            Type = ConsumerType.Ship,
            DefaultGroupId = SeedIds.GroupDiesel,
            DefaultItemId = ship.DefaultItemId,
            DefaultExportSampleSetId = ship.DefaultExportSampleSetId,
            ShipType = ship.ShipType,
            MainMachineCount = ship.MainMachineCount,
            AuxMachineCount = ship.AuxMachineCount,
            NormFactors = ship.NormFactors.Select(f => new ConsumerNormFactorEdit
            {
                Id = f.Id,
                GroupId = SeedIds.GroupDiesel,
                Name = f.Name,
                Value = f.Value,
                SortOrder = f.SortOrder
            }).ToList()
        }).Ok);

        var date = new DateTime(2026, 3, 15);
        // Nhãn request cố ý Xăng — sổ/hạn mức vẫn theo DefaultGroupId = Dầu.
        // Manual 0L để không cần tồn khi trừ XT.
        Assert.True(app.System.SaveShipQuarterBook(new ShipQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Ship,
            QuarterDate = date,
            FuelGroupName = "Xăng",
            Lines =
            [
                new ShipQuarterBookLineEdit
                {
                    DocumentNumber = "TT-HM-01",
                    DocumentDate = date,
                    Description = "Tuần tra",
                    MainOpsCount = 1m,
                    HoursAtBerth = 2m,
                    ManualFuelOut = true,
                    FuelOutManual = 0m,
                    MissionTaskId = SeedIds.MissionTaskCombat,
                    LotTypeCode = "TX",
                    LotTypeId = SeedIds.LotTypeTx
                }
            ]
        }).Ok);

        var book = app.System.GetShipQuarterBook(date, SeedIds.Ship);
        Assert.Equal("Dầu", book.FuelGroupName);
        Assert.False(book.FuelIsGasoline);

        var sheet = app.System.GetQuotaSheet(2026, 1, NxtLotViewMode.TxSscd);
        var combat = Assert.Single(sheet.Rows, x => x.TaskId == SeedIds.MissionTaskCombat);
        Assert.True((combat.DieselHours ?? 0) > 0);
        Assert.Equal(0m, combat.GasolineHours ?? 0);
    }

    [Fact]
    public void Ship_quarter_book_and_quota_line_calculations_match_rules()
    {
        using var app = TestApp.Create();
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [ShipNormSlots.AtBerth] = 10m,
            [ShipNormSlots.Cx25] = 20m,
            [ShipNormSlots.Cx50] = 30m,
            [ShipNormSlots.Cx75] = 40m,
            [ShipNormSlots.Cx100] = 50m,
            [ShipNormSlots.Aux] = 15m
        };

        // 1. Máy chính: 2 máy x (1h bến + 2h 25% + 0h + 0h + 1h 100%) = 2 x 4h = 8h.
        // 2. NL máy chính: 2 máy x (1*10 + 2*20 + 0 + 0 + 1*50) = 2 x 100 = 200L.
        // 3. Máy phụ: 3 máy x 4h = 12h máy phụ; NL máy phụ = 3 x 4h x 15 = 180L.
        // Tổng NL = 200 + 180 = 380L.
        var line = new ShipQuarterBookLine
        {
            MainOpsCount = 2m,
            HoursAtBerth = 1m,
            HoursCx25 = 2m,
            HoursCx50 = 0m,
            HoursCx75 = 0m,
            HoursCx100 = 1m,
            AuxOpsCount = 3m,
            AuxHours = 4m
        };

        var fuelOut = APPQLXD.Core.Services.ShipQuarterBooks.ResolveFuelOut(line, rates);
        Assert.Equal(380m, fuelOut);

        // Trường hợp máy phụ: số máy = 0, giờ = 4 -> NL máy phụ = 0
        var lineZeroAuxMachines = new ShipQuarterBookLine
        {
            MainOpsCount = 1m,
            HoursAtBerth = 1m,
            AuxOpsCount = 0m,
            AuxHours = 4m
        };
        Assert.Equal(10m, APPQLXD.Core.Services.ShipQuarterBooks.ResolveFuelOut(lineZeroAuxMachines, rates));

        // Trường hợp máy phụ: số máy = 2, giờ = 0 -> NL máy phụ = 0
        var lineZeroAuxHours = new ShipQuarterBookLine
        {
            MainOpsCount = 1m,
            HoursAtBerth = 1m,
            AuxOpsCount = 2m,
            AuxHours = 0m
        };
        Assert.Equal(10m, APPQLXD.Core.Services.ShipQuarterBooks.ResolveFuelOut(lineZeroAuxHours, rates));

        // 4. Hạn mức: cấu hình tàu trong danh mục có 2 máy chính, 3 máy phụ.
        // Tổng giờ = (2 máy chính * 4h = 8h) + (3 máy phụ * 4h = 12h) = 20h.
        var ship = app.System.GetConsumers().Single(x => x.Id == SeedIds.Ship);
        Assert.True(app.System.SaveConsumer(new ConsumerEdit
        {
            Id = ship.Id,
            Code = ship.Code,
            Name = ship.Name,
            Type = ship.Type,
            DefaultGroupId = ship.DefaultGroupId,
            ShipType = ship.ShipType,
            MainMachineCount = 2m,
            AuxMachineCount = 3m,
            NormFactors = ship.NormFactors.Select(f => new ConsumerNormFactorEdit
            {
                Id = f.Id,
                GroupId = f.GroupId ?? Guid.Empty,
                Name = f.Name,
                Value = f.Value,
                SortOrder = f.SortOrder
            }).ToList()
        }).Ok);

        var date = new DateTime(2026, 2, 20);
        Assert.True(app.System.SaveShipQuarterBook(new ShipQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Ship,
            QuarterDate = date,
            Lines =
            [
                new ShipQuarterBookLineEdit
                {
                    DocumentNumber = "TT-CALC-01",
                    DocumentDate = date,
                    Description = "Test tính toán",
                    MainOpsCount = 2m,
                    HoursAtBerth = 1m,
                    HoursCx25 = 2m,
                    HoursCx100 = 1m,
                    AuxOpsCount = 3m,
                    AuxHours = 4m,
                    ManualFuelOut = true,
                    FuelOutManual = 0m,
                    MissionTaskId = SeedIds.MissionTaskCombat,
                    LotTypeCode = "TX",
                    LotTypeId = SeedIds.LotTypeTx
                }
            ]
        }).Ok);

        var sheet = app.System.GetQuotaSheet(2026, 1, NxtLotViewMode.TxSscd);
        var combat = Assert.Single(sheet.Rows, x => x.TaskId == SeedIds.MissionTaskCombat);
        // (1+2+1)*2 + 4*3 = 8 + 12 = 20h
        Assert.Equal(20m, combat.GasolineHours ?? 0);
        Assert.Equal(0m, combat.DieselHours ?? 0);
    }

    [Fact]
    public void Quota_combines_machine_decimal_hours_and_ship_hours_correctly()
    {
        using var app = TestApp.Create();
        var date = new DateTime(2026, 2, 15);

        Assert.True(app.System.SaveImport(Import(1000m, 20000m)).Ok);
        var lot = app.System.GetLots(SeedIds.WhMain).First(x => x.UnitPrice == 20000).LotId;
        var main = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhMain);

        var savedTransfer = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            ConsumerId = SeedIds.Machine,
            DestinationWarehouseId = SeedIds.Machine,
            DocumentDate = new DateTime(2026, 1, 10),
            FormNumber = "DC-01",
            WarehouseId = main.Id,
            WarehouseName = main.Name,
            WarehouseTypeName = main.TypeName,
            MissionTaskId = SeedIds.MissionTaskCombat,
            Lines =
            [
                new SlipLineInput
                {
                    LotId = lot,
                    ItemName = "Xăng RON 95",
                    ObservedQuantity = 100m,
                    ActualQuantity = 100m,
                    Vcf = 1m,
                    UnitPrice = 20000m
                }
            ]
        });
        Assert.True(savedTransfer.Ok);

        var machineBook = app.System.GetConsumerQuarterBook(date, SeedIds.Machine);
        var transferRow = Assert.Single(machineBook.Rows, x => !x.IsOpening);

        // 1. Máy tiêu thụ: 2.5 giờ (số thập phân) cho nhiệm vụ Combat
        Assert.True(app.System.SaveConsumerQuarterBook(new ConsumerQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Machine,
            QuarterDate = date,
            Lines =
            [
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = transferRow.DocumentId,
                    DocumentNumber = transferRow.DocumentNumber,
                    DocumentDate = date,
                    Description = "Chạy máy phát",
                    MachineHours = 2.5m,
                    ActualQuantity = 50m,
                    FuelOut = 50m,
                    MissionTaskId = SeedIds.MissionTaskCombat,
                    LotTypeCode = "TX",
                    LotTypeId = SeedIds.LotTypeTx
                }
            ]
        }).Ok);

        // 2. Tàu tiêu thụ: 1.15' = 1.25 giờ trên máy chính (1 máy) + 0.30' = 0.5 giờ trên máy phụ (1 máy) -> Tàu = 1.75 giờ
        Assert.True(app.System.SaveShipQuarterBook(new ShipQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Ship,
            QuarterDate = date,
            Lines =
            [
                new ShipQuarterBookLineEdit
                {
                    DocumentNumber = "TT-TAU-01",
                    DocumentDate = date,
                    Description = "Tuần tra",
                    MainOpsCount = 1m,
                    HoursAtBerth = 1.25m, // Tương đương 1.15'
                    AuxOpsCount = 1m,
                    AuxHours = 0.5m,      // Tương đương 0.30'
                    ManualFuelOut = true,
                    FuelOutManual = 0m,
                    MissionTaskId = SeedIds.MissionTaskCombat,
                    LotTypeCode = "TX",
                    LotTypeId = SeedIds.LotTypeTx
                }
            ]
        }).Ok);

        var sheet = app.System.GetQuotaSheet(2026, 1, NxtLotViewMode.TxSscd);
        var combat = Assert.Single(sheet.Rows, x => x.TaskId == SeedIds.MissionTaskCombat);

        // Tàu (Xăng): 1.15' (1.25h) + 0.30' (0.5h) = 1.75h vào cột Giờ Xăng
        Assert.Equal(1.75m, combat.GasolineHours ?? 0);
        // Máy (Điêzel): 2.5h (thập phân) vào cột Giờ Điêzel
        Assert.Equal(2.5m, combat.DieselHours ?? 0);
    }

    [Fact]
    public void Quota_quarter_vs_cumulative_and_comparison_columns()
    {
        using var app = TestApp.Create();
        // Hạn mức năm: Xăng = 100L, Điêzel = 200L, Tổng = 300L
        Assert.True(app.System.SaveMissionYearLimit(SeedIds.MissionTaskCombat, 2026, NxtLotViewMode.TxSscd, 100m, 200m).Ok);

        Assert.True(app.System.SaveImport(Import(1000m, 20000m)).Ok);
        var lot = app.System.GetLots(SeedIds.WhMain).First(x => x.UnitPrice == 20000).LotId;
        var vehicle = app.System.GetConsumers().Single(x => x.Id == SeedIds.Vehicle);
        var main = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhMain);

        // Q1: Xuất 40L xe (Xăng) vào 15/02/2026
        var q1Date = new DateTime(2026, 2, 15);
        var s1 = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            ConsumerId = vehicle.Id,
            DestinationWarehouseId = vehicle.Id,
            DocumentDate = q1Date,
            FormNumber = "DC-Q1",
            WarehouseId = main.Id,
            WarehouseName = main.Name,
            WarehouseTypeName = main.TypeName,
            MissionTaskId = SeedIds.MissionTaskCombat,
            Lines = [new SlipLineInput { LotId = lot, ItemName = "Xăng RON 95", ObservedQuantity = 40m, ActualQuantity = 40m, Vcf = 1m, UnitPrice = 20000m }]
        });
        Assert.True(s1.Ok);

        var vBookQ1 = app.System.GetConsumerQuarterBook(q1Date, SeedIds.Vehicle);
        var tr1 = Assert.Single(vBookQ1.Rows, x => !x.IsOpening);
        Assert.True(app.System.SaveConsumerQuarterBook(new ConsumerQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Vehicle,
            QuarterDate = q1Date,
            Lines =
            [
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = tr1.DocumentId,
                    DocumentNumber = tr1.DocumentNumber,
                    DocumentDate = q1Date,
                    Description = "Nhiệm vụ Q1",
                    Kilometers = 100m,
                    ActualQuantity = 40m,
                    FuelOut = 40m,
                    MissionTaskId = SeedIds.MissionTaskCombat,
                    LotTypeCode = "TX",
                    LotTypeId = SeedIds.LotTypeTx
                }
            ]
        }).Ok);

        // Q2: Xuất 70L xe (Xăng) vào 15/05/2026
        var q2Date = new DateTime(2026, 5, 15);
        var s2 = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            ConsumerId = vehicle.Id,
            DestinationWarehouseId = vehicle.Id,
            DocumentDate = q2Date,
            FormNumber = "DC-Q2",
            WarehouseId = main.Id,
            WarehouseName = main.Name,
            WarehouseTypeName = main.TypeName,
            MissionTaskId = SeedIds.MissionTaskCombat,
            Lines = [new SlipLineInput { LotId = lot, ItemName = "Xăng RON 95", ObservedQuantity = 70m, ActualQuantity = 70m, Vcf = 1m, UnitPrice = 20000m }]
        });
        Assert.True(s2.Ok);

        var vBookQ2 = app.System.GetConsumerQuarterBook(q2Date, SeedIds.Vehicle);
        var tr2 = Assert.Single(vBookQ2.Rows, x => !x.IsOpening);
        Assert.True(app.System.SaveConsumerQuarterBook(new ConsumerQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Vehicle,
            QuarterDate = q2Date,
            Lines =
            [
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = tr2.DocumentId,
                    DocumentNumber = tr2.DocumentNumber,
                    DocumentDate = q2Date,
                    Description = "Nhiệm vụ Q2",
                    Kilometers = 150m,
                    ActualQuantity = 70m,
                    FuelOut = 70m,
                    MissionTaskId = SeedIds.MissionTaskCombat,
                    LotTypeCode = "TX",
                    LotTypeId = SeedIds.LotTypeTx
                }
            ]
        }).Ok);

        // Kiểm tra Quý 2:
        // - Dữ liệu trong quý (trước ô Lũy tích): chỉ tính Q2 = 70L
        // - Lũy tích (từ 01/01 đến hết Q2): 40 + 70 = 110L
        // - Hạn mức Xăng = 100L -> Quá Xăng = 10L, Còn Xăng = 0
        // - Hạn mức Điêzel = 200L, Lũy tích Điêzel = 0 -> Còn Điêzel = 200L
        // - Tổng hạn mức = 300L, Tổng lũy tích = 110L -> Còn Tổng = 190L
        var sheetQ2 = app.System.GetQuotaSheet(2026, 2, NxtLotViewMode.TxSscd);
        var row = Assert.Single(sheetQ2.Rows, x => x.TaskId == SeedIds.MissionTaskCombat);

        Assert.Equal(70m, row.GasolineVehicle ?? 0);
        Assert.Equal(70m, row.GasolineFuelTotal ?? 0);
        Assert.Equal(70m, row.FuelTotal ?? 0);

        Assert.Equal(110m, row.CumGasoline ?? 0);
        Assert.Equal(0m, row.CumDiesel ?? 0);
        Assert.Equal(110m, row.CumTotal ?? 0);

        Assert.Equal(0m, row.RemainGasoline ?? 0);
        Assert.Equal(200m, row.RemainDiesel ?? 0);
        Assert.Equal(190m, row.RemainTotal ?? 0);

        Assert.Equal(10m, row.ExcessGasoline ?? 0);
        Assert.Equal(0m, row.ExcessDiesel ?? 0);
        Assert.Equal(0m, row.ExcessTotal ?? 0);

        // Kiểm tra ClearMissionYearLimits
        Assert.True(app.System.ClearMissionYearLimits(2026, NxtLotViewMode.TxSscd).Ok);
        var sheetCleared = app.System.GetQuotaSheet(2026, 2, NxtLotViewMode.TxSscd);
        var rowCleared = Assert.Single(sheetCleared.Rows, x => x.TaskId == SeedIds.MissionTaskCombat);
        Assert.Null(rowCleared.GasolineLimit);
        Assert.Null(rowCleared.DieselLimit);
        Assert.Null(rowCleared.LimitTotal);
    }

    private static ImportRequest Import(decimal qty, decimal price) => new()
    {
        DocumentDate = new DateTime(2026, 1, 5),
        WarehouseId = SeedIds.WhMain,
        ItemId = SeedIds.ItemRon95,
        Vcf = 1m,
        UnitPrice = price,
        InputQuantity = qty,
        Fields = [new FieldInput { FieldId = SeedIds.FieldInvoice, Value = "HD-NV" }]
    };
}

