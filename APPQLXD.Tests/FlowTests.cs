using APPQLXD.Core;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Core.Persistence;
using APPQLXD.Core.Services;

namespace APPQLXD.Tests;

public sealed class CalculationTests
{
    [Fact]
    public void Import_actual_is_quantity_times_vcf()
    {
        Assert.Equal(99m, QuantityMath.ActualImport(100m, 0.99m));
        Assert.Equal(10m, QuantityMath.ActualImport(10.5m, 0.99m));
        Assert.Equal(12m, QuantityMath.ActualImport(12.5m, 0.99m));
    }

    [Fact]
    public void Rounding_uses_away_from_zero()
    {
        Assert.Equal(1.1112m, QuantityMath.RoundQty(1.11115m));
        Assert.Equal(1.2345m, QuantityMath.RoundQty(1.23445m));
        Assert.Equal(0.987655m, QuantityMath.RoundVcf(0.9876545m));
        Assert.Equal(1m, QuantityMath.ActualImport(1.11115m, 1m));
    }

    [Fact]
    public void Export_display_is_actual_divided_by_vcf()
    {
        Assert.Equal(25m, QuantityMath.ExportDisplayQuantity(25m, 0.99m));
        Assert.Equal(100m, QuantityMath.ExportDisplayQuantity(99m, 0.99m));
        Assert.Equal(101m, QuantityMath.ExportDisplayQuantity(100m, 0.99m));
    }

    [Fact]
    public void Money_words_match_the_paper_vouchers()
    {
        Assert.Equal("Ba triệu, chín trăm bảy mươi sáu ngàn, tám trăm bốn mươi tám đồng.", MoneyWords.ToDong(3_976_848m));
        Assert.Equal("Chín trăm sáu mươi bảy triệu, năm trăm chín mươi lăm ngàn, bảy trăm sáu mươi đồng.", MoneyWords.ToDong(967_595_760m));
    }

    [Fact]
    public void Exact_range_covers_hundred_thousand_billions()
    {
        // Hàng trăm ngàn tỉ = 100.000 × 10^9 = 10^14
        Assert.Equal(100_000_000_000_000m, QuantityMath.MaxExactValue);
        Assert.True(QuantityMath.IsWithinExactRange(QuantityMath.MaxExactValue));
        Assert.False(QuantityMath.IsWithinExactRange(QuantityMath.MaxExactValue + 1m));

        var qty = 100_000_000_000_000m;
        Assert.Equal(qty, QuantityMath.ActualImport(qty, 1m));
        Assert.Equal(99_000_000_000_000m, QuantityMath.ActualImport(qty, 0.99m));

        Assert.True(QuantityMath.TryAmount(25_000L, 4_000_000_000m, out var amount));
        Assert.Equal(100_000_000_000_000m, amount);
        Assert.False(QuantityMath.TryAmount(25_001L, 4_000_000_000m, out _));

        Assert.Contains("ngàn tỷ", MoneyWords.ToDong(100_000_000_000_000m), StringComparison.Ordinal);
        Assert.Equal(QuantityMath.MaxExactValue, QuantityMath.ClampEditQty(QuantityMath.MaxExactValue + 1m));
    }

    [Fact]
    public void Ship_hour_format_parses_hours_and_minutes()
    {
        Assert.True(ShipHourFormat.TryParse("2.30'", out var h1));
        Assert.Equal(2.5m, h1);
        Assert.True(ShipHourFormat.TryParse("1,15", out var h2));
        Assert.Equal(1.25m, h2);
        Assert.True(ShipHourFormat.TryParse("0.45'", out var h3));
        Assert.Equal(0.75m, h3);
        Assert.True(ShipHourFormat.TryParse("3", out var h4));
        Assert.Equal(3m, h4);
        Assert.False(ShipHourFormat.TryParse("1.60'", out _));
        Assert.Equal("2.30'", ShipHourFormat.Format(2.5m));
        Assert.Equal("1.15'", ShipHourFormat.Format(1.25m));
        Assert.Equal("3", ShipHourFormat.Format(3m));
        Assert.Equal("2", ShipHourFormat.Format(2.0m));
        // 2.30' × định mức 10 → ceil(25) = 25
        Assert.Equal(25m, QuantityMath.VehicleActual(h1, 10m));
    }

    [Fact]
    public void Vehicle_actual_is_distance_times_norm()
    {
        Assert.Equal(25m, QuantityMath.VehicleActual(100m, 0.25m));
        Assert.Equal(14m, QuantityMath.VehicleActual(40m, 0.36m));
        Assert.Equal(0m, QuantityMath.VehicleActual(1m, 0.01m));
    }

    [Fact]
    public void Ship_effective_norm_is_sum_of_conversion_rates()
    {
        Assert.Equal(3.5m, QuantityMath.ShipEffectiveNorm([2m, 1.5m]));
        Assert.Equal(2m, QuantityMath.ShipEffectiveNorm([2m, 0m, -1m]));
        Assert.Equal(0m, QuantityMath.ShipEffectiveNorm([]));
        Assert.Equal(8m, QuantityMath.ShipActual(4m, 2m));
    }

    [Fact]
    public void Equal_amount_stays_one_lot()
    {
        var preview = ImportLotSplitter.Split(20000m, 100m, 2_000_000m, 1m);
        Assert.True(preview.Ok);
        Assert.Single(preview.Lines);
        Assert.Equal(100m, preview.TotalActual);
    }

    [Fact]
    public void Unequal_amount_splits_into_two_integer_prices()
    {
        var up = ImportLotSplitter.Split(20000m, 100m, 2_000_050m, 1m);
        Assert.True(up.Ok);
        Assert.Equal(2, up.Lines.Count);
        Assert.Equal(2_000_050m, up.TotalAmount);
        Assert.Equal(100m, up.TotalQuantity);
        Assert.Equal(100m, up.TotalActual);
        Assert.All(up.Lines, line => Assert.True(line.UnitPrice == decimal.Truncate(line.UnitPrice)));
        Assert.Contains(up.Lines, line => line.UnitPrice == 20000);
        Assert.Contains(up.Lines, line => line.UnitPrice == 20001);

        var down = ImportLotSplitter.Split(20000m, 100m, 1_999_950m, 1m);
        Assert.True(down.Ok);
        Assert.Equal(1_999_950m, down.TotalAmount);
        Assert.Contains(down.Lines, line => line.UnitPrice == 19999);
    }

    [Fact]
    public void Fractional_price_is_rejected()
    {
        var preview = ImportLotSplitter.Split(20000.5m, 10m, null, 1m);
        Assert.False(preview.Ok);
    }

    [Fact]
    public void Floor_pairs_list_integer_prices_with_the_second_quantity_increasing()
    {
        Assert.Empty(ImportLotSplitter.FloorPairs(100m, 2_000_000m));

        var pairs = ImportLotSplitter.FloorPairs(100m, 2_000_050m);
        Assert.NotEmpty(pairs);
        Assert.Equal(pairs.OrderBy(x => x.Quantity2).ToArray(), pairs);
        Assert.Contains(pairs, x => x.Quantity1 == 50m && x.Price1 == 20_000 && x.Quantity2 == 50m && x.Price2 == 20_001);
        Assert.All(pairs, pair =>
        {
            Assert.True(pair.Matches(100m, 2_000_050m));
            Assert.Equal(100m, pair.Quantity1 + pair.Quantity2);
            Assert.Equal(2_000_050m, pair.Amount1 + pair.Amount2);
            Assert.Equal(pair.Price1 * pair.Quantity1, pair.Amount1);
            Assert.Equal(pair.Price2 * pair.Quantity2, pair.Amount2);
            Assert.Equal(20_000, pair.Price1);
            Assert.True(pair.Price2 > pair.Price1);
            Assert.True(pair.Quantity2 > 0);
            Assert.Equal(decimal.Truncate(pair.Quantity1), pair.Quantity1);
            Assert.Equal(decimal.Truncate(pair.Quantity2), pair.Quantity2);
            Assert.Equal(decimal.Truncate(pair.Amount1), pair.Amount1);
            Assert.Equal(decimal.Truncate(pair.Amount2), pair.Amount2);
        });
    }

    [Fact]
    public void Floor_pairs_never_rebuild_amount_from_average_unit_price()
    {
        // Thành tiền người dùng nhập phải được giữ nguyên khi tách — không lấy round(đơn_giá_trung_bình × thực_nhập).
        var samples = new (decimal Actual, decimal Amount)[]
        {
            (100m, 2_000_050m),
            (7m, 100_003m),
            (123m, 2_456_789m),
            (50m, 1_000_001m),
            (999m, 19_999_999m),
            (1m, 15_001m),
            (250m, 5_125_075m)
        };

        foreach (var (actual, amount) in samples)
        {
            var pairs = ImportLotSplitter.FloorPairs(actual, amount);
            foreach (var pair in pairs)
            {
                Assert.True(pair.Matches(actual, amount), $"actual={actual} amount={amount} pair={pair}");
                Assert.Equal(amount, pair.Amount1 + pair.Amount2);
                Assert.Equal(actual, pair.Quantity1 + pair.Quantity2);
            }
        }
    }

    [Fact]
    public void Floor_pairs_reject_a_fractional_actual()
    {
        Assert.Empty(ImportLotSplitter.FloorPairs(10.5m, 100_000m));
    }
}

public sealed class FlowTests
{
    [Fact]
    public void Opening_stock_is_actual_and_ignores_vcf()
    {
        using var app = TestApp.Create();
        var saved = app.System.SaveOpening(Open(7m, 15000m));
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal(7m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhMain));
        var doc = app.System.GetDocument(saved.Id!.Value)!;
        Assert.Equal(0m, doc.Vcf);
        Assert.Equal(7m, doc.ActualQuantity);
        Assert.Equal("Xăng RON 95", doc.ItemName);
    }

    [Fact]
    public void Opening_sheet_posts_each_warehouse_and_rolls_back_together()
    {
        using var app = TestApp.Create();
        var main = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhMain);
        var aux = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhAux);
        var saved = app.System.SaveOpeningSheet(new OpeningSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 1),
            Cells = [OpeningCell(main, 10m), OpeningCell(aux, 4m)]
        });
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal(10m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhMain));
        Assert.Equal(4m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhAux));
        var active = app.System.ListDocuments(DocumentKind.Opening).Where(x => x.Status == DocumentStatus.Active).ToList();
        Assert.Equal(2, active.Count);
        Assert.Equal(2, active.Select(x => x.Number).Distinct().Count());

        var mainDoc = active.Single(x => x.WarehouseName == main.Name);
        var lot = app.System.GetLots(SeedIds.WhMain).Single(x => x.UnitPrice == 15000).LotId;
        Assert.True(app.System.SaveConsumption(ConsumeMachine(lot, 9m)).Ok);
        var blocked = app.System.SaveOpeningSheet(new OpeningSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 1),
            Cells = [OpeningCell(aux, 4m)],
            VoidIds = [mainDoc.Id]
        });
        Assert.False(blocked.Ok);
        Assert.Equal(1m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhMain));
        Assert.Equal(DocumentStatus.Active, app.System.GetDocument(mainDoc.Id)!.Status);
    }

    [Fact]
    public void Opening_sheet_second_save_updates_the_same_slip()
    {
        using var app = TestApp.Create();
        var aux = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhAux);
        var saved = app.System.SaveOpeningSheet(new OpeningSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 1),
            Cells = [OpeningCell(aux, 4m)]
        });
        Assert.True(saved.Ok, saved.Message);

        var again = app.System.SaveOpeningSheet(new OpeningSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 1),
            Cells = [OpeningCell(aux, 6m)]
        });
        Assert.True(again.Ok, again.Message);
        Assert.Equal(saved.Id, again.Id);
        var active = app.System.ListDocuments(DocumentKind.Opening).Where(x => x.Status == DocumentStatus.Active).ToList();
        Assert.Single(active);
        Assert.Equal(6m, app.System.GetDocument(saved.Id!.Value)!.ActualQuantity);
        Assert.Equal(6m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhAux));
    }

    [Fact]
    public void Consumption_sheet_posts_typed_actual_and_rolls_back_together()
    {
        using var app = TestApp.Create();
        var main = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhMain);
        var aux = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhAux);
        Assert.True(app.System.SaveOpeningSheet(new OpeningSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 1),
            Cells = [OpeningCell(main, 10m), OpeningCell(aux, 4m)]
        }).Ok);

        var saved = app.System.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            Cells = [ConsumeCell(main, 3m), ConsumeCell(aux, 1m)]
        });
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal(7m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhMain));
        Assert.Equal(3m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhAux));
        var active = app.System.ListDocuments(DocumentKind.Consumption).Where(x => x.Status == DocumentStatus.Active).ToList();
        Assert.Equal(2, active.Count);
        var mainDoc = app.System.GetDocument(active.Single(x => x.WarehouseName == main.Name).Id)!;
        var auxDoc = app.System.GetDocument(active.Single(x => x.WarehouseName == aux.Name).Id)!;
        Assert.Equal(3m, mainDoc.ActualQuantity);
        Assert.Equal(QuantityMath.ExportDisplayQuantity(3m, 0.99m), mainDoc.InputQuantity);
        Assert.Equal(0.99m, mainDoc.Vcf);
        Assert.Null(mainDoc.Distance);
        Assert.Null(mainDoc.Norm);
        Assert.Equal(new DateTime(2026, 9, 30), mainDoc.DocumentDate.Date);

        var blocked = app.System.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            Cells = [ConsumeCell(main, 100m, mainDoc.Id), ConsumeCell(aux, 1m, auxDoc.Id)]
        });
        Assert.False(blocked.Ok);
        Assert.Equal(7m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhMain));
        Assert.Equal(3m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhAux));
        Assert.Equal(DocumentStatus.Active, app.System.GetDocument(mainDoc.Id)!.Status);

        var edited = app.System.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            Cells = [ConsumeCell(main, 5m, mainDoc.Id), ConsumeCell(aux, 1m, auxDoc.Id)]
        });
        Assert.True(edited.Ok, edited.Message);
        Assert.Equal(5m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhMain));

        var cleared = app.System.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            VoidIds = [mainDoc.Id]
        });
        Assert.True(cleared.Ok, cleared.Message);
        Assert.Equal(10m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhMain));
        Assert.Equal(3m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhAux));
        Assert.Equal(DocumentStatus.Voided, app.System.GetDocument(mainDoc.Id)!.Status);
    }

    [Fact]
    public void Consumption_sheet_second_save_updates_the_same_slip()
    {
        using var app = TestApp.Create();
        var aux = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhAux);
        Assert.True(app.System.SaveOpeningSheet(new OpeningSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 1),
            Cells = [OpeningCell(aux, 4m)]
        }).Ok);

        var saved = app.System.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            Cells = [ConsumeCell(aux, 1m)]
        });
        Assert.True(saved.Ok, saved.Message);

        var again = app.System.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            Cells = [ConsumeCell(aux, 2m)]
        });
        Assert.True(again.Ok, again.Message);
        Assert.Equal(saved.Id, again.Id);
        var active = app.System.ListDocuments(DocumentKind.Consumption).Where(x => x.Status == DocumentStatus.Active).ToList();
        Assert.Single(active);
        Assert.Equal(2m, app.System.GetDocument(saved.Id!.Value)!.ActualQuantity);
        Assert.Equal(2m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhAux));
    }

    [Fact]
    public void Consumption_sheet_saves_without_consumer_or_extra_fields()
    {
        using var app = TestApp.Create();
        var aux = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhAux);
        Assert.True(app.System.SaveOpeningSheet(new OpeningSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 1),
            Cells = [OpeningCell(aux, 4m)]
        }).Ok);

        var saved = app.System.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            Cells =
            [
                new ConsumptionCellRequest
                {
                    DocumentDate = new DateTime(2026, 9, 30),
                    WarehouseId = aux.Id,
                    WarehouseName = aux.Name,
                    WarehouseTypeName = aux.TypeName,
                    ItemName = "Xăng RON 95",
                    UnitPrice = 15000m,
                    ActualQuantity = 1m,
                    Vcf = 1m
                }
            ]
        });
        Assert.True(saved.Ok, saved.Message);
        var doc = app.System.GetDocument(saved.Id!.Value)!;
        Assert.Null(doc.ConsumerId);
        Assert.Empty(doc.Fields);
        Assert.Equal(3m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhAux));
    }

    [Fact]
    public void Consumption_sheet_uses_typed_quantity_for_a_vehicle()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveOpening(Open(10m, 15000m)).Ok);
        var warehouse = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhMain);
        var saved = app.System.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            Cells =
            [
                new ConsumptionCellRequest
                {
                    DocumentDate = new DateTime(2026, 9, 30),
                    ConsumerId = SeedIds.Vehicle,
                    ConsumerType = ConsumerType.Vehicle,
                    WarehouseId = warehouse.Id,
                    WarehouseName = warehouse.Name,
                    WarehouseTypeName = warehouse.TypeName,
                    ItemName = "Xăng RON 95",
                    UnitPrice = 15000m,
                    ActualQuantity = 2m,
                    Vcf = 0.99m,
                    Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Tổ máy" }]
                }
            ]
        });
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal(8m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhMain));
        var doc = app.System.GetDocument(saved.Id!.Value)!;
        Assert.Equal(2m, doc.ActualQuantity);
        Assert.Null(doc.Distance);
        Assert.Null(doc.Norm);

        var missing = app.System.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            Cells =
            [
                new ConsumptionCellRequest
                {
                    DocumentDate = new DateTime(2026, 9, 30),
                    ConsumerId = SeedIds.Vehicle,
                    ConsumerType = ConsumerType.Vehicle,
                    WarehouseId = warehouse.Id,
                    ItemName = "Không có lô này",
                    UnitPrice = 15000m,
                    ActualQuantity = 1m,
                    Vcf = 0.99m,
                    Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Tổ máy" }]
                }
            ]
        });
        Assert.False(missing.Ok);
        Assert.Equal(8m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhMain));
    }

    [Fact]
    public void Import_increases_stock_by_actual_not_display_quantity()
    {
        using var app = TestApp.Create();
        var saved = app.System.SaveImport(Import(100m, 20000m, vcf: 0.99m));
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal(99m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        var doc = app.System.GetDocument(saved.Id!.Value)!;
        Assert.Equal(100m, doc.InputQuantity);
        Assert.Equal(99m, doc.ActualQuantity);
        Assert.Equal(0.99m, doc.Vcf);
    }

    [Fact]
    public void Same_name_and_price_share_one_lot_different_price_does_not()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(10m, 20000m, vcf: 1m)).Ok);
        Assert.True(app.System.SaveImport(Import(15m, 20000m, vcf: 1m)).Ok);
        Assert.True(app.System.SaveImport(Import(4m, 21000m, vcf: 1m)).Ok);
        var rows = app.System.ListStock(new StockFilter(SeedIds.WhMain, null, "Xăng RON 95", null));
        Assert.Equal(2, rows.Count);
        Assert.Equal(25m, rows.Single(x => x.UnitPrice == 20000).Quantity);
        Assert.Equal(4m, rows.Single(x => x.UnitPrice == 21000).Quantity);
    }

    [Fact]
    public void Partial_export_leaves_remainder_and_over_export_is_rejected()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(1000m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        var exported = app.System.SaveConsumption(ConsumeMachine(lot, 300m));
        Assert.True(exported.Ok, exported.Message);
        Assert.Equal(700m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));

        var rejected = app.System.SaveConsumption(ConsumeMachine(lot, 701m));
        Assert.False(rejected.Ok);
        Assert.Contains("Không đủ tồn", rejected.Message);
        Assert.Equal(700m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
    }

    [Fact]
    public void Does_not_spill_into_another_lot()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(5m, 10000m, vcf: 1m)).Ok);
        Assert.True(app.System.SaveImport(Import(100m, 20000m, vcf: 1m)).Ok);
        var expensive = app.System.GetLots(SeedIds.WhMain).Single(x => x.UnitPrice == 20000);
        var rejected = app.System.SaveConsumption(ConsumeMachine(expensive.LotId, 101m));
        Assert.False(rejected.Ok);
        Assert.Equal(5m, Stock(app.System, "Xăng RON 95", 10000m, SeedIds.WhMain));
        Assert.Equal(100m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
    }

    [Fact]
    public void Transfer_moves_actual_quantity_and_keeps_total()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(50m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        var moved = app.System.SaveTransfer(Transfer(lot, 20m));
        Assert.True(moved.Ok, moved.Message);
        Assert.Equal(30m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        Assert.Equal(20m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhAux));
        Assert.Equal(50m, app.System.ListStock(new StockFilter(null, null, "Xăng RON 95", null)).Sum(x => x.Quantity));

        var same = app.System.SaveTransfer(Transfer(lot, 1m, SeedIds.WhMain, SeedIds.WhMain));
        Assert.False(same.Ok);
        var tooMuch = app.System.SaveTransfer(Transfer(lot, 31m));
        Assert.False(tooMuch.Ok);
        Assert.Equal(30m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
    }

    [Fact]
    public void Consumer_has_auxiliary_stock_location_for_transfer_and_quarterly_consumption()
    {
        using var app = TestApp.Create();
        var machine = app.System.GetConsumers().Single(x => x.Id == SeedIds.Machine);
        var location = app.System.GetWarehouses().Single(x => x.Id == SeedIds.Machine);
        Assert.True(location.IsConsumerLocation);
        Assert.Equal(WarehouseType.Auxiliary, location.Type);
        Assert.Equal(machine.Code, location.Code);
        Assert.Equal(machine.Name, location.Name);
        Assert.Equal("Máy", location.ConsumerTypeName);
        Assert.Null(location.DefaultImportSampleSetId);

        Assert.False(app.System.DeleteWarehouse(SeedIds.Machine).Ok);
        Assert.False(app.System.SaveWarehouse(new WarehouseEdit
        {
            Id = SeedIds.Machine,
            Code = machine.Code,
            Name = machine.Name,
            Type = WarehouseType.Auxiliary
        }).Ok);

        Assert.True(app.System.SaveImport(Import(40m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        var moved = app.System.SaveTransfer(Transfer(lot, 12m, SeedIds.WhMain, SeedIds.Machine));
        Assert.True(moved.Ok, moved.Message);
        Assert.Equal(28m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        Assert.Equal(12m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Machine));

        var consumed = app.System.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            Cells =
            [
                new ConsumptionCellRequest
                {
                    DocumentDate = new DateTime(2026, 9, 30),
                    WarehouseId = location.Id,
                    WarehouseName = location.Name,
                    WarehouseTypeName = location.TypeName,
                    ItemName = "Xăng RON 95",
                    UnitPrice = 20000m,
                    ActualQuantity = 3m,
                    Vcf = 1m
                }
            ]
        });
        Assert.True(consumed.Ok, consumed.Message);
        Assert.Equal(9m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Machine));

        var renamed = app.System.SaveConsumer(new ConsumerEdit
        {
            Id = SeedIds.Machine,
            Code = machine.Code,
            Name = "Máy phát điện số 1",
            Type = ConsumerType.Machine,
            DefaultGroupId = machine.DefaultGroupId,
            DefaultItemId = machine.DefaultItemId,
            DefaultExportSampleSetId = machine.DefaultExportSampleSetId
        });
        Assert.True(renamed.Ok, renamed.Message);
        Assert.Equal("Máy phát điện số 1", app.System.GetWarehouses().Single(x => x.Id == SeedIds.Machine).Name);
    }

    [Fact]
    public void Machine_and_vehicle_default_to_rolling_transfers_into_quarter()
    {
        using var app = TestApp.Create();
        var machine = app.System.GetConsumers().Single(x => x.Id == SeedIds.Machine);
        var vehicle = app.System.GetConsumers().Single(x => x.Id == SeedIds.Vehicle);
        Assert.True(machine.RollTransfersIntoQuarter);
        Assert.True(vehicle.RollTransfersIntoQuarter);

        Assert.True(app.System.SaveConsumer(new ConsumerEdit
        {
            Id = machine.Id,
            Code = machine.Code,
            Name = machine.Name,
            Type = ConsumerType.Machine,
            DefaultGroupId = machine.DefaultGroupId,
            DefaultItemId = machine.DefaultItemId,
            DefaultExportSampleSetId = machine.DefaultExportSampleSetId,
            RollTransfersIntoQuarter = false
        }).Ok);
        Assert.False(app.System.GetConsumers().Single(x => x.Id == SeedIds.Machine).RollTransfersIntoQuarter);
    }

    [Fact]
    public void Suggest_quarter_rollups_sums_transfers_to_machine_and_skips_when_disabled_or_saved()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(100m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        Assert.True(app.System.SaveTransfer(Transfer(lot, 10m, SeedIds.WhMain, SeedIds.Machine)).Ok);
        Assert.True(app.System.SaveTransfer(Transfer(lot, 7m, SeedIds.WhMain, SeedIds.Machine)).Ok);
        Assert.True(app.System.SaveTransfer(Transfer(lot, 5m, SeedIds.WhMain, SeedIds.WhAux)).Ok);

        var sums = app.System.SumInboundTransfers(new DateTime(2026, 9, 30), SeedIds.Machine);
        Assert.Equal(17m, Assert.Single(sums).ActualQuantity);

        var suggested = app.System.SuggestQuarterRollups(new DateTime(2026, 9, 30));
        Assert.Contains(suggested, x => x.WarehouseId == SeedIds.Machine && x.ActualQuantity == 17m);
        Assert.DoesNotContain(suggested, x => x.WarehouseId == SeedIds.WhAux);

        var machine = app.System.GetConsumers().Single(x => x.Id == SeedIds.Machine);
        Assert.True(app.System.SaveConsumer(new ConsumerEdit
        {
            Id = machine.Id,
            Code = machine.Code,
            Name = machine.Name,
            Type = ConsumerType.Machine,
            DefaultGroupId = machine.DefaultGroupId,
            DefaultItemId = machine.DefaultItemId,
            DefaultExportSampleSetId = machine.DefaultExportSampleSetId,
            RollTransfersIntoQuarter = false
        }).Ok);
        Assert.DoesNotContain(app.System.SuggestQuarterRollups(new DateTime(2026, 9, 30)), x => x.WarehouseId == SeedIds.Machine);

        Assert.True(app.System.SaveConsumer(new ConsumerEdit
        {
            Id = machine.Id,
            Code = machine.Code,
            Name = machine.Name,
            Type = ConsumerType.Machine,
            DefaultGroupId = machine.DefaultGroupId,
            DefaultItemId = machine.DefaultItemId,
            DefaultExportSampleSetId = machine.DefaultExportSampleSetId,
            RollTransfersIntoQuarter = true
        }).Ok);
        Assert.True(app.System.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            Cells =
            [
                new ConsumptionCellRequest
                {
                    DocumentDate = new DateTime(2026, 9, 30),
                    WarehouseId = SeedIds.Machine,
                    ItemName = "Xăng RON 95",
                    UnitPrice = 20000m,
                    ActualQuantity = 4m,
                    Vcf = 1m
                }
            ]
        }).Ok);
        var afterSave = Assert.Single(app.System.SuggestQuarterRollups(new DateTime(2026, 9, 30)), x => x.WarehouseId == SeedIds.Machine);
        Assert.True(afterSave.NeedsUpdate);
        Assert.Equal(4m, afterSave.SavedQuantity);
        Assert.Equal(17m, afterSave.ActualQuantity);
        Assert.Equal(13m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Machine));

        Assert.True(app.System.SaveTransfer(Transfer(lot, 3m, SeedIds.WhMain, SeedIds.Machine)).Ok);
        var afterMore = Assert.Single(app.System.SuggestQuarterRollups(new DateTime(2026, 9, 30)), x => x.WarehouseId == SeedIds.Machine);
        Assert.Equal(20m, afterMore.ActualQuantity);
        Assert.Equal(4m, afterMore.SavedQuantity);
        Assert.True(afterMore.NeedsUpdate);

        var slips = app.System.ListInboundTransfers(new DateTime(2026, 9, 30), SeedIds.Machine);
        Assert.Equal(3, slips.Count);
        Assert.Equal(20m, slips.Sum(x => x.ActualQuantity));
    }

    [Fact]
    public void Consumer_transfer_book_treats_transfer_as_consumption_and_splits_fuel_vs_oil()
    {
        using var app = TestApp.Create();
        var oilGroup = app.System.GetGroups().Single(x => x.Name == "Nhớt");
        var unit = app.System.GetUnits().Single(x => x.Name == "Lít");
        var oilItem = app.System.SaveItem(new ItemEdit
        {
            GroupId = oilGroup.Id,
            UnitId = unit.Id,
            Name = "Nhớt sổ máy test",
            Vcf = 1m
        });
        Assert.True(oilItem.Ok, oilItem.Message);

        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 6, 15),
            WarehouseId = SeedIds.Machine,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 20000m,
            ActualQuantity = 20m
        }).Ok);
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 6, 15),
            WarehouseId = SeedIds.Machine,
            ItemId = oilItem.Id!.Value,
            ItemName = "Nhớt sổ máy test",
            GroupName = "Nhớt",
            UnitName = "Lít",
            UnitPrice = 50000m,
            ActualQuantity = 5m
        }).Ok);

        Assert.True(app.System.SaveImport(Import(100m, 20000m, vcf: 1m)).Ok);
        Assert.True(app.System.SaveImport(new ImportRequest
        {
            DocumentDate = new DateTime(2026, 9, 2),
            WarehouseId = SeedIds.WhMain,
            ItemId = oilItem.Id!.Value,
            ItemName = "Nhớt sổ máy test",
            GroupName = "Nhớt",
            UnitName = "Lít",
            Vcf = 1m,
            UnitPrice = 50000m,
            InputQuantity = 50m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldInvoice, Value = "HD-OIL" }]
        }).Ok);

        var fuelLot = app.System.GetLots(SeedIds.WhMain).Single(x => x.ItemName == "Xăng RON 95" && x.UnitPrice == 20000m).LotId;
        var oilLot = app.System.GetLots(SeedIds.WhMain).Single(x => x.ItemName == "Nhớt sổ máy test").LotId;
        var transferred = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            DocumentDate = new DateTime(2026, 9, 10),
            WarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.Machine,
            Mission = "Nhiệm vụ HCKT",
            Nature = "Điều chuyển",
            OriginPlace = "HD",
            DestinationPlace = "HCM",
            Lines =
            [
                new SlipLineInput
                {
                    LotId = fuelLot,
                    ItemId = SeedIds.ItemRon95,
                    ItemName = "Xăng RON 95",
                    ActualQuantity = 54m,
                    ObservedQuantity = 54m,
                    UnitPrice = 20000m,
                    Vcf = 1m
                },
                new SlipLineInput
                {
                    LotId = oilLot,
                    ItemId = oilItem.Id,
                    ItemName = "Nhớt sổ máy test",
                    GroupName = "Nhớt",
                    ActualQuantity = 2m,
                    ObservedQuantity = 2m,
                    UnitPrice = 50000m,
                    Vcf = 1m
                }
            ],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Tổ máy" }]
        });
        Assert.True(transferred.Ok, transferred.Message);

        var machine = app.System.GetConsumers().Single(x => x.Id == SeedIds.Machine);
        var book = app.System.BuildConsumerTransferBook(new DateTime(2026, 9, 30), SeedIds.Machine);
        Assert.Equal(machine.Name, book.ConsumerName);
        Assert.Equal(2, book.Rows.Count);

        var opening = book.Rows[0];
        Assert.True(opening.IsOpening);
        Assert.Equal("Quý trước mang sang", opening.Description);
        Assert.Equal(20m, opening.FuelBalance);
        Assert.Equal(5m, opening.OilBalance);
        Assert.Null(opening.FuelIn);
        Assert.Null(opening.FuelOut);
        Assert.Null(opening.OilIn);
        Assert.Null(opening.OilOut);

        var slip = book.Rows[1];
        Assert.False(slip.IsOpening);
        Assert.Equal("Nhiệm vụ HCKT", slip.Description);
        Assert.Equal("HD", slip.Origin);
        Assert.Equal("HCM", slip.Destination);
        Assert.Equal(54m, slip.FuelIn);
        Assert.Equal(54m, slip.FuelOut);
        Assert.Equal(20m, slip.FuelBalance);
        Assert.Equal(2m, slip.OilIn);
        Assert.Equal(2m, slip.OilOut);
        Assert.Equal(5m, slip.OilBalance);
        Assert.Equal(54m, slip.ActualQuantity);
        Assert.Equal(54m, slip.NormQuantity);
        Assert.Null(slip.OverQuantity);
        Assert.Null(slip.UnderQuantity);
        Assert.Equal(54m, book.FuelTransferTotal);
        Assert.Equal(2m, book.OilTransferTotal);
    }

    [Fact]
    public void Consumer_quarter_book_save_without_edits_writes_consumption_from_transfers()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(100m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        Assert.True(app.System.SaveTransfer(Transfer(lot, 12m, SeedIds.WhMain, SeedIds.Machine)).Ok);

        var book = app.System.GetConsumerQuarterBook(new DateTime(2026, 9, 30), SeedIds.Machine);
        var transferRow = Assert.Single(book.Rows, x => !x.IsOpening);
        var saved = app.System.SaveConsumerQuarterBook(new ConsumerQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Machine,
            QuarterDate = new DateTime(2026, 9, 30),
            Lines =
            [
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = transferRow.DocumentId,
                    DocumentNumber = transferRow.DocumentNumber,
                    DocumentDate = transferRow.DocumentDate,
                    Description = transferRow.Description,
                    Kilometers = transferRow.Kilometers,
                    MachineHours = transferRow.MachineHours,
                    NormQuantity = transferRow.NormQuantity,
                    ActualQuantity = transferRow.ActualQuantity,
                    ManualFuelOut = false,
                    FuelOut = transferRow.FuelOut ?? 0,
                    ManualOilOut = false,
                    OilOut = transferRow.OilOut ?? 0
                }
            ]
        });
        Assert.True(saved.Ok, saved.Message);

        var consumption = app.System.ListDocuments(DocumentKind.Consumption)
            .Where(x => x.Status == DocumentStatus.Active && x.WarehouseName != null)
            .ToList();
        Assert.Contains(consumption, x => x.ActualQuantity == 12m);

        var again = app.System.GetConsumerQuarterBook(new DateTime(2026, 9, 30), SeedIds.Machine);
        Assert.Contains(again.Rows, x => !x.IsOpening && x.FuelOut == 12m);
    }

    [Fact]
    public void Consumer_quarter_book_manual_row_consumes_without_in_qty()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(100m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        Assert.True(app.System.SaveTransfer(Transfer(lot, 20m, SeedIds.WhMain, SeedIds.Machine)).Ok);

        var book = app.System.GetConsumerQuarterBook(new DateTime(2026, 9, 30), SeedIds.Machine);
        var transferRow = Assert.Single(book.Rows, x => !x.IsOpening);
        // ĐC: Xuất = 0 (giữ tồn từ ĐC); thêm dòng tay tiêu thụ 8.
        var saved = app.System.SaveConsumerQuarterBook(new ConsumerQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Machine,
            QuarterDate = new DateTime(2026, 9, 30),
            Lines =
            [
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = transferRow.DocumentId,
                    DocumentNumber = transferRow.DocumentNumber,
                    DocumentDate = transferRow.DocumentDate,
                    Description = transferRow.Description,
                    ManualFuelOut = true,
                    FuelOut = 0m,
                    ManualOilOut = true,
                    OilOut = 0m
                },
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = null,
                    DocumentNumber = "TT-MAY-01",
                    DocumentDate = new DateTime(2026, 9, 20),
                    Description = "Tiêu thụ",
                    ManualFuelOut = true,
                    FuelOut = 8m,
                    ManualOilOut = true,
                    OilOut = 0m,
                    LotTypeId = SeedIds.LotTypeTx,
                    LotTypeCode = "TX"
                }
            ]
        });
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal(12m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Machine));

        var again = app.System.GetConsumerQuarterBook(new DateTime(2026, 9, 30), SeedIds.Machine);
        var manual = Assert.Single(again.Rows, x => x.IsManualRow);
        Assert.Null(manual.FuelIn);
        Assert.Equal(8m, manual.FuelOut);
        Assert.Equal("TT-MAY-01", manual.DocumentNumber);
        var dc = Assert.Single(again.Rows, x => !x.IsOpening && !x.IsManualRow);
        Assert.Equal(20m, dc.FuelIn);
        Assert.True(dc.FuelOut is null or 0);
    }

    [Fact]
    public void Consumer_quarter_book_resave_with_manual_after_default_sees_restored_stock()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(100m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        Assert.True(app.System.SaveTransfer(Transfer(lot, 40m, SeedIds.WhMain, SeedIds.Machine)).Ok);

        var book = app.System.GetConsumerQuarterBook(new DateTime(2026, 9, 30), SeedIds.Machine);
        var transferRow = Assert.Single(book.Rows, x => !x.IsOpening);
        Assert.True(app.System.SaveConsumerQuarterBook(new ConsumerQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Machine,
            QuarterDate = new DateTime(2026, 9, 30),
            Lines =
            [
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = transferRow.DocumentId,
                    DocumentNumber = transferRow.DocumentNumber,
                    DocumentDate = transferRow.DocumentDate,
                    Description = transferRow.Description,
                    FuelOut = transferRow.FuelOut ?? 0,
                    OilOut = transferRow.OilOut ?? 0,
                    LotTypeId = transferRow.LotTypeId,
                    LotTypeCode = transferRow.LotTypeCode
                }
            ]
        }).Ok);
        Assert.Equal(0m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Machine));

        // Lưu lại: ĐC Xuất=0 + dòng tay 15 — phải thấy tồn đã hoàn trong cùng TX (không AsNoTracking lệch).
        var again = app.System.SaveConsumerQuarterBook(new ConsumerQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Machine,
            QuarterDate = new DateTime(2026, 9, 30),
            Lines =
            [
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = transferRow.DocumentId,
                    DocumentNumber = transferRow.DocumentNumber,
                    DocumentDate = transferRow.DocumentDate,
                    Description = transferRow.Description,
                    ManualFuelOut = true,
                    FuelOut = 0m,
                    ManualOilOut = true,
                    OilOut = 0m,
                    LotTypeId = transferRow.LotTypeId,
                    LotTypeCode = transferRow.LotTypeCode
                },
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = null,
                    DocumentNumber = "TT-RESAVE",
                    DocumentDate = new DateTime(2026, 9, 20),
                    Description = "Tay sau lần lưu 1",
                    ManualFuelOut = true,
                    FuelOut = 15m,
                    ManualOilOut = true,
                    OilOut = 0m,
                    LotTypeId = SeedIds.LotTypeTx,
                    LotTypeCode = "TX"
                }
            ]
        });
        Assert.True(again.Ok, again.Message);
        Assert.Equal(25m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Machine));
    }

    [Fact]
    public void Consumer_transfer_book_one_row_per_lot_type()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(50m, 20000m, vcf: 1m, lotTypeId: SeedIds.LotTypeTx)).Ok);
        Assert.True(app.System.SaveImport(Import(50m, 20000m, vcf: 1m, lotTypeId: SeedIds.LotTypeSscd)).Ok);
        var tx = app.System.GetLots(SeedIds.WhMain).Single(x => x.LotTypeId == SeedIds.LotTypeTx && x.UnitPrice == 20000).LotId;
        var sscd = app.System.GetLots(SeedIds.WhMain).Single(x => x.LotTypeId == SeedIds.LotTypeSscd && x.UnitPrice == 20000).LotId;
        Assert.True(app.System.SaveTransfer(new TransferRequest
        {
            DocumentDate = new DateTime(2026, 9, 10),
            SourceWarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.Machine,
            LotId = tx,
            ActualQuantity = 10m,
            Vcf = 1m,
            DestinationLotTypeId = SeedIds.LotTypeTx,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Máy" }]
        }).Ok);
        Assert.True(app.System.SaveTransfer(new TransferRequest
        {
            DocumentDate = new DateTime(2026, 9, 11),
            SourceWarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.Machine,
            LotId = sscd,
            ActualQuantity = 7m,
            Vcf = 1m,
            DestinationLotTypeId = SeedIds.LotTypeSscd,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Máy" }]
        }).Ok);

        var book = app.System.BuildConsumerTransferBook(new DateTime(2026, 9, 30), SeedIds.Machine);
        var slips = book.Rows.Where(x => !x.IsOpening).ToList();
        Assert.Equal(2, slips.Count);
        Assert.All(slips, x => Assert.DoesNotContain(",", x.LotTypeCode ?? ""));
        Assert.Contains(slips, x => x.LotTypeId == SeedIds.LotTypeTx && x.FuelIn == 10m && x.LotTypeCode is "TX");
        Assert.Contains(slips, x => x.LotTypeId == SeedIds.LotTypeSscd && x.FuelIn == 7m);
    }

    [Fact]
    public void Consumer_quarter_book_oil_out_persists_and_feeds_next_quarter_opening()
    {
        using var app = TestApp.Create();
        var oilGroup = app.System.GetGroups().Single(x => x.Name == "Nhớt");
        var unit = app.System.GetUnits().Single(x => x.Name == "Lít");
        var oilItem = app.System.SaveItem(new ItemEdit
        {
            GroupId = oilGroup.Id,
            UnitId = unit.Id,
            Name = "Nhớt mất khi mở lại",
            Vcf = 1m
        });
        Assert.True(oilItem.Ok, oilItem.Message);

        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 7, 1),
            WarehouseId = SeedIds.Machine,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 20000m,
            ActualQuantity = 10m
        }).Ok);
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 7, 1),
            WarehouseId = SeedIds.Machine,
            ItemId = oilItem.Id!.Value,
            ItemName = "Nhớt mất khi mở lại",
            GroupName = "Nhớt",
            UnitName = "Lít",
            UnitPrice = 50000m,
            ActualQuantity = 8m
        }).Ok);

        Assert.True(app.System.SaveImport(Import(50m, 20000m, vcf: 1m)).Ok);
        var fuelLot = OnlyLot(app.System);
        Assert.True(app.System.SaveTransfer(Transfer(fuelLot, 10m, SeedIds.WhMain, SeedIds.Machine)).Ok);

        var q3 = app.System.GetConsumerQuarterBook(new DateTime(2026, 9, 30), SeedIds.Machine);
        Assert.Equal(8m, q3.Rows[0].OilBalance);
        var transferRow = Assert.Single(q3.Rows, x => !x.IsOpening);
        Assert.Null(transferRow.OilIn);

        // Cố ý không gửi ManualOilOut (giống lỗi UI cũ khi OilIn trống) nhưng có Xuất DM.
        var saved = app.System.SaveConsumerQuarterBook(new ConsumerQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Machine,
            QuarterDate = new DateTime(2026, 9, 30),
            Lines =
            [
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = transferRow.DocumentId,
                    DocumentNumber = transferRow.DocumentNumber,
                    DocumentDate = transferRow.DocumentDate,
                    Description = transferRow.Description,
                    ManualFuelOut = false,
                    FuelOut = transferRow.FuelOut ?? 0,
                    ManualOilOut = false,
                    OilOut = 3m
                }
            ]
        });
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal(5m, Stock(app.System, "Nhớt mất khi mở lại", 50000m, SeedIds.Machine));

        var reopened = app.System.GetConsumerQuarterBook(new DateTime(2026, 9, 30), SeedIds.Machine);
        var row = Assert.Single(reopened.Rows, x => !x.IsOpening);
        Assert.Equal(3m, row.OilOut);
        Assert.True(row.ManualOilOut);
        Assert.Equal(5m, row.OilBalance);

        var q4 = app.System.GetConsumerQuarterBook(new DateTime(2026, 12, 31), SeedIds.Machine);
        Assert.Equal(5m, q4.Rows[0].OilBalance);
        Assert.Equal(10m, q4.Rows[0].FuelBalance);
    }

    [Fact]
    public void Consumer_quarter_book_resave_keeps_oil_consumption_for_next_opening()
    {
        using var app = TestApp.Create();
        var oilGroup = app.System.GetGroups().Single(x => x.Name == "Nhớt");
        var unit = app.System.GetUnits().Single(x => x.Name == "Lít");
        var oilItem = app.System.SaveItem(new ItemEdit
        {
            GroupId = oilGroup.Id,
            UnitId = unit.Id,
            Name = "Nhớt orphan mang sang",
            Vcf = 1m
        });
        Assert.True(oilItem.Ok, oilItem.Message);

        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 7, 1),
            WarehouseId = SeedIds.Machine,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 20000m,
            ActualQuantity = 40m
        }).Ok);
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 7, 1),
            WarehouseId = SeedIds.Machine,
            ItemId = oilItem.Id!.Value,
            ItemName = "Nhớt orphan mang sang",
            GroupName = "Nhớt",
            UnitName = "Lít",
            UnitPrice = 50000m,
            ActualQuantity = 20m
        }).Ok);

        Assert.True(app.System.SaveImport(Import(50m, 20000m, vcf: 1m)).Ok);
        Assert.True(app.System.SaveImport(new ImportRequest
        {
            DocumentDate = new DateTime(2026, 8, 2),
            WarehouseId = SeedIds.WhMain,
            ItemId = oilItem.Id!.Value,
            ItemName = "Nhớt orphan mang sang",
            GroupName = "Nhớt",
            UnitName = "Lít",
            Vcf = 1m,
            UnitPrice = 50000m,
            InputQuantity = 30m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldInvoice, Value = "HD-ORPHAN-OIL" }]
        }).Ok);

        var fuelLot = app.System.GetLots(SeedIds.WhMain).Single(x => x.ItemName == "Xăng RON 95" && x.UnitPrice == 20000m).LotId;
        var oilLot = app.System.GetLots(SeedIds.WhMain).Single(x => x.ItemName == "Nhớt orphan mang sang").LotId;
        Assert.True(app.System.SaveTransfer(new TransferRequest
        {
            DocumentDate = new DateTime(2026, 9, 4),
            SourceWarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.Machine,
            LotId = fuelLot,
            ActualQuantity = 10m,
            Vcf = 1m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }]
        }).Ok);
        Assert.True(app.System.SaveTransfer(new TransferRequest
        {
            DocumentDate = new DateTime(2026, 9, 5),
            SourceWarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.Machine,
            LotId = oilLot,
            ActualQuantity = 5m,
            Vcf = 1m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }]
        }).Ok);

        var q3 = app.System.GetConsumerQuarterBook(new DateTime(2026, 9, 30), SeedIds.Machine);
        var fuelRow = Assert.Single(q3.Rows, x => !x.IsOpening && (x.FuelIn ?? 0) > 0);
        var oilRow = Assert.Single(q3.Rows, x => !x.IsOpening && (x.OilIn ?? 0) > 0);

        ConsumerQuarterBookSaveRequest BookSave(decimal fuelOut, decimal oilOnFuelRow, decimal oilOnOilRow) => new()
        {
            ConsumerId = SeedIds.Machine,
            QuarterDate = new DateTime(2026, 9, 30),
            Lines =
            [
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = fuelRow.DocumentId,
                    DocumentNumber = fuelRow.DocumentNumber,
                    DocumentDate = fuelRow.DocumentDate,
                    Description = fuelRow.Description,
                    ManualFuelOut = true,
                    FuelOut = fuelOut,
                    ManualOilOut = true,
                    OilOut = oilOnFuelRow
                },
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = oilRow.DocumentId,
                    DocumentNumber = oilRow.DocumentNumber,
                    DocumentDate = oilRow.DocumentDate,
                    Description = oilRow.Description,
                    ManualFuelOut = true,
                    FuelOut = 0m,
                    ManualOilOut = oilOnOilRow != (oilRow.OilIn ?? 0),
                    OilOut = oilOnOilRow
                }
            ]
        };

        var first = app.System.SaveConsumerQuarterBook(BookSave(8m, 3m, 5m));
        Assert.True(first.Ok, first.Message);
        Assert.Equal(42m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Machine));
        Assert.Equal(17m, Stock(app.System, "Nhớt orphan mang sang", 50000m, SeedIds.Machine));

        var second = app.System.SaveConsumerQuarterBook(BookSave(8m, 3m, 5m));
        Assert.True(second.Ok, second.Message);
        Assert.Equal(42m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Machine));
        Assert.Equal(17m, Stock(app.System, "Nhớt orphan mang sang", 50000m, SeedIds.Machine));

        var oilConsumption = app.System.ListDocuments(DocumentKind.Consumption)
            .Where(x => x.Status == DocumentStatus.Active && x.ItemName == "Nhớt orphan mang sang")
            .Sum(x => x.ActualQuantity);
        Assert.Equal(8m, oilConsumption);

        var q4 = app.System.GetConsumerQuarterBook(new DateTime(2026, 12, 31), SeedIds.Machine);
        Assert.Equal(17m, q4.Rows[0].OilBalance);
        Assert.Equal(42m, q4.Rows[0].FuelBalance);
    }

    [Fact]
    public void Consumer_quarter_book_save_feeds_next_quarter_opening_balance()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 7, 1),
            WarehouseId = SeedIds.Machine,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 20000m,
            ActualQuantity = 20m
        }).Ok, "opening on machine");
        Assert.True(app.System.SaveImport(Import(100m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        Assert.True(app.System.SaveTransfer(Transfer(lot, 12m, SeedIds.WhMain, SeedIds.Machine)).Ok);

        var q3 = app.System.GetConsumerQuarterBook(new DateTime(2026, 9, 30), SeedIds.Machine);
        Assert.Equal(20m, q3.Rows[0].FuelBalance);
        var transferRow = Assert.Single(q3.Rows, x => !x.IsOpening);
        Assert.True(app.System.SaveConsumerQuarterBook(new ConsumerQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Machine,
            QuarterDate = new DateTime(2026, 9, 30),
            Lines =
            [
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = transferRow.DocumentId,
                    DocumentNumber = transferRow.DocumentNumber,
                    DocumentDate = transferRow.DocumentDate,
                    Description = transferRow.Description,
                    Kilometers = transferRow.Kilometers,
                    MachineHours = transferRow.MachineHours,
                    NormQuantity = transferRow.NormQuantity,
                    ActualQuantity = transferRow.ActualQuantity,
                    ManualFuelOut = false,
                    FuelOut = transferRow.FuelOut ?? 0,
                    ManualOilOut = false,
                    OilOut = transferRow.OilOut ?? 0
                }
            ]
        }).Ok);

        // ĐC 12 rồi tiêu thụ 12 → tồn cuối Q3 = 20; Q4 mang sang = 20.
        Assert.Equal(20m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Machine));
        var q4 = app.System.GetConsumerQuarterBook(new DateTime(2026, 12, 31), SeedIds.Machine);
        Assert.Equal(20m, q4.Rows[0].FuelBalance);
    }

    [Fact]
    public void Transfer_to_vehicle_uses_distance_times_norm()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(100m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        var vehicle = app.System.GetConsumers().Single(x => x.Id == SeedIds.Vehicle);
        var main = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhMain);
        var result = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            ConsumerId = vehicle.Id,
            Distance = 40m,
            Norm = 0.25m,
            DestinationWarehouseId = vehicle.Id,
            DestinationWarehouseName = vehicle.Name,
            DocumentDate = new DateTime(2026, 9, 4),
            FormNumber = "DC-XE-01",
            WarehouseId = main.Id,
            WarehouseName = main.Name,
            WarehouseTypeName = main.TypeName,
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
        Assert.True(result.Ok, result.Message);
        var doc = app.System.GetDocument(result.Id!.Value)!;
        Assert.Equal(10m, doc.ActualQuantity);
        Assert.Equal(0.25m, doc.Norm);
        Assert.Equal(40m, doc.Distance);
        Assert.Equal(vehicle.Id, doc.ConsumerId);
        Assert.Equal(90m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        Assert.Equal(10m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Vehicle));
    }

    [Fact]
    public void Transfer_to_vehicle_manual_quantity_skips_distance_times_norm()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(100m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        var vehicle = app.System.GetConsumers().Single(x => x.Id == SeedIds.Vehicle);
        var main = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhMain);
        var result = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            ConsumerId = vehicle.Id,
            Distance = 40m,
            Norm = 0.25m,
            ManualQuantity = true,
            DestinationWarehouseId = vehicle.Id,
            DestinationWarehouseName = vehicle.Name,
            DocumentDate = new DateTime(2026, 9, 4),
            FormNumber = "DC-XE-MAN",
            WarehouseId = main.Id,
            WarehouseName = main.Name,
            WarehouseTypeName = main.TypeName,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Lái xe" }],
            Lines =
            [
                new SlipLineInput
                {
                    LotId = lot,
                    ItemName = "Xăng RON 95",
                    ObservedQuantity = 7m,
                    ActualQuantity = 7m,
                    Vcf = 1m,
                    UnitPrice = 20000m
                }
            ]
        });
        Assert.True(result.Ok, result.Message);
        var doc = app.System.GetDocument(result.Id!.Value)!;
        Assert.True(doc.ManualQuantity);
        Assert.Equal(7m, doc.ActualQuantity);
        Assert.Equal(0.25m, doc.Norm);
        Assert.Equal(40m, doc.Distance);
        Assert.Equal(93m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        Assert.Equal(7m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Vehicle));
    }

    [Fact]
    public void Ship_has_fixed_power_slot_norms_and_quarterly_consumption_still_works()
    {
        using var app = TestApp.Create();
        var ship = app.System.GetConsumers().Single(x => x.Id == SeedIds.Ship);
        Assert.Equal(ConsumerType.Ship, ship.Type);
        Assert.Equal("Tàu", ship.TypeName);
        Assert.Equal(16.5m, ship.EffectiveNorm);
        Assert.Equal(ShipNormSlots.Labels.Length, ship.NormFactors.Count);
        Assert.Equal(ShipNormSlots.Labels, ship.NormFactors.OrderBy(x => x.SortOrder).Select(x => x.Name).ToArray());
        Assert.All(ship.NormFactors, x => Assert.Equal(SeedIds.GroupFuel, x.GroupId));
        var location = app.System.GetWarehouses().Single(x => x.Id == SeedIds.Ship);
        Assert.True(location.IsConsumerLocation);
        Assert.Equal("Tàu", location.ConsumerTypeName);

        Assert.True(app.System.SaveImport(Import(100m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        var main = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhMain);
        var transfer = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            ConsumerId = ship.Id,
            Distance = 40m,
            Norm = 3.5m,
            DestinationWarehouseId = ship.Id,
            DestinationWarehouseName = ship.Name,
            DocumentDate = new DateTime(2026, 9, 4),
            FormNumber = "DC-TAU-01",
            WarehouseId = main.Id,
            WarehouseName = main.Name,
            WarehouseTypeName = main.TypeName,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thuyền trưởng" }],
            Lines =
            [
                new SlipLineInput
                {
                    LotId = lot,
                    ItemName = "Xăng RON 95",
                    ObservedQuantity = 20m,
                    ActualQuantity = 20m,
                    Vcf = 1m,
                    UnitPrice = 20000m
                }
            ]
        });
        Assert.True(transfer.Ok, transfer.Message);
        var transferDoc = app.System.GetDocument(transfer.Id!.Value)!;
        Assert.Equal(20m, transferDoc.ActualQuantity);
        Assert.Null(transferDoc.Norm);
        Assert.Null(transferDoc.Distance);
        Assert.Equal(20m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Ship));

        var missing = app.System.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            Cells =
            [
                new ConsumptionCellRequest
                {
                    DocumentDate = new DateTime(2026, 9, 30),
                    WarehouseId = location.Id,
                    WarehouseName = location.Name,
                    WarehouseTypeName = location.TypeName,
                    ConsumerId = ship.Id,
                    ConsumerType = ConsumerType.Ship,
                    ConsumerName = ship.Name,
                    ConsumerCode = ship.Code,
                    ItemName = "Xăng RON 95",
                    UnitPrice = 20000m,
                    ActualQuantity = 1m,
                    Vcf = 1m
                }
            ]
        });
        Assert.True(missing.Ok, missing.Message);
        Assert.Equal(1m, app.System.GetDocument(missing.Id!.Value)!.ActualQuantity);
        Assert.Equal(19m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Ship));

        var consumed = app.System.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            Cells =
            [
                new ConsumptionCellRequest
                {
                    DocumentDate = new DateTime(2026, 9, 30),
                    DocumentId = missing.Id,
                    WarehouseId = location.Id,
                    WarehouseName = location.Name,
                    WarehouseTypeName = location.TypeName,
                    ConsumerId = ship.Id,
                    ConsumerType = ConsumerType.Ship,
                    ConsumerName = ship.Name,
                    ConsumerCode = ship.Code,
                    ItemName = "Xăng RON 95",
                    UnitPrice = 20000m,
                    ActualQuantity = 8m,
                    Vcf = 1m
                }
            ]
        });
        Assert.True(consumed.Ok, consumed.Message);
        var doc = app.System.GetDocument(consumed.Id!.Value)!;
        Assert.Equal(8m, doc.ActualQuantity);
        Assert.Null(doc.Norm);
        Assert.Null(doc.OperatingQuantity);
        Assert.Null(doc.Distance);
        Assert.Equal(12m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Ship));

        var factorsChanged = app.System.SaveConsumer(new ConsumerEdit
        {
            Id = ship.Id,
            Code = ship.Code,
            Name = ship.Name,
            Type = ConsumerType.Ship,
            DefaultGroupId = SeedIds.GroupFuel,
            DefaultItemId = ship.DefaultItemId,
            DefaultExportSampleSetId = ship.DefaultExportSampleSetId,
            NormFactors = ShipNormSlots.Labels.Select((label, i) => new ConsumerNormFactorEdit
            {
                GroupId = SeedIds.GroupFuel,
                Name = label,
                Value = i == 0 ? 5m : i == 5 ? 1m : 2m,
                SortOrder = i + 1
            }).ToList()
        });
        Assert.True(factorsChanged.Ok, factorsChanged.Message);
        Assert.Equal(8m, app.System.GetDocument(consumed.Id!.Value)!.ActualQuantity);
        Assert.Null(app.System.GetDocument(consumed.Id!.Value)!.Norm);
        var updated = app.System.GetConsumers().Single(x => x.Id == SeedIds.Ship);
        Assert.Equal(14m, updated.EffectiveNorm);
        Assert.Equal(6, updated.NormFactors.Count);
        Assert.Equal(ShipNormSlots.Labels, updated.NormFactors.OrderBy(x => x.SortOrder).Select(x => x.Name).ToArray());
    }

    [Fact]
    public void Ship_quarter_book_builds_transfers_as_inbound_and_saves_fifo_by_group()
    {
        using var app = TestApp.Create();
        var oilGroup = app.System.GetGroups().Single(x => x.Name == "Nhớt");
        var unit = app.System.GetUnits().Single(x => x.Name == "Lít");
        var oilItem = app.System.SaveItem(new ItemEdit
        {
            GroupId = oilGroup.Id,
            UnitId = unit.Id,
            Name = "Nhớt sổ tàu test",
            Vcf = 1m
        });
        Assert.True(oilItem.Ok, oilItem.Message);

        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 6, 15),
            WarehouseId = SeedIds.Ship,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 20000m,
            ActualQuantity = 10m
        }).Ok);
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 6, 15),
            WarehouseId = SeedIds.Ship,
            ItemId = oilItem.Id!.Value,
            ItemName = "Nhớt sổ tàu test",
            GroupName = "Nhớt",
            UnitName = "Lít",
            UnitPrice = 50000m,
            ActualQuantity = 4m
        }).Ok);

        Assert.True(app.System.SaveImport(Import(100m, 20000m, vcf: 1m)).Ok);
        Assert.True(app.System.SaveImport(new ImportRequest
        {
            DocumentDate = new DateTime(2026, 9, 2),
            WarehouseId = SeedIds.WhMain,
            ItemId = oilItem.Id!.Value,
            ItemName = "Nhớt sổ tàu test",
            GroupName = "Nhớt",
            UnitName = "Lít",
            Vcf = 1m,
            UnitPrice = 50000m,
            InputQuantity = 20m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldInvoice, Value = "HD-OIL-TAU" }]
        }).Ok);

        var fuelLot = app.System.GetLots(SeedIds.WhMain).Single(x => x.ItemName == "Xăng RON 95" && x.UnitPrice == 20000m).LotId;
        var oilLot = app.System.GetLots(SeedIds.WhMain).Single(x => x.ItemName == "Nhớt sổ tàu test").LotId;
        var transferred = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            DocumentDate = new DateTime(2026, 9, 10),
            WarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.Ship,
            Mission = "Cấp dầu tàu",
            Nature = "Điều chuyển",
            FormNumber = "DC-TAU-Q3",
            Lines =
            [
                new SlipLineInput
                {
                    LotId = fuelLot,
                    ItemId = SeedIds.ItemRon95,
                    ItemName = "Xăng RON 95",
                    ActualQuantity = 30m,
                    ObservedQuantity = 30m,
                    UnitPrice = 20000m,
                    Vcf = 1m
                },
                new SlipLineInput
                {
                    LotId = oilLot,
                    ItemId = oilItem.Id,
                    ItemName = "Nhớt sổ tàu test",
                    GroupName = "Nhớt",
                    ActualQuantity = 3m,
                    ObservedQuantity = 3m,
                    UnitPrice = 50000m,
                    Vcf = 1m
                }
            ],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thuyền trưởng" }]
        });
        Assert.True(transferred.Ok, transferred.Message);
        Assert.Equal(40m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Ship));
        Assert.Equal(7m, Stock(app.System, "Nhớt sổ tàu test", 50000m, SeedIds.Ship));

        var book = app.System.GetShipQuarterBook(new DateTime(2026, 9, 30), SeedIds.Ship);
        Assert.Equal("Tàu BP 27-05-01", book.ShipName);
        Assert.Equal("Xăng", book.FuelGroupName);
        Assert.True(book.FuelIsGasoline);
        Assert.Equal(2, book.Rows.Count);
        Assert.True(book.Rows[0].IsOpening);
        Assert.Equal(10m, book.Rows[0].FuelBalance);
        Assert.Equal(4m, book.Rows[0].OilBalance);
        Assert.True(book.Rows[1].IsTransfer);
        Assert.Equal("Điều chuyển", book.Rows[1].Description);
        Assert.Equal(30m, book.Rows[1].FuelIn);
        Assert.Equal(3m, book.Rows[1].OilIn);
        Assert.Equal(40m, book.Rows[1].FuelBalance);
        Assert.Equal(7m, book.Rows[1].OilBalance);
        Assert.Null(book.Rows[1].FuelOut);

        // 2h tại bến×1 + 1h 100%×5 + 2h máy phụ×1.5 = 10; dầu mỡ tay = 2
        var saved = app.System.SaveShipQuarterBook(new ShipQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Ship,
            QuarterDate = new DateTime(2026, 9, 30),
            FuelGroupName = "Xăng",
            Lines =
            [
                new ShipQuarterBookLineEdit
                {
                    DocumentNumber = "TT-TAU-01",
                    DocumentDate = new DateTime(2026, 9, 20),
                    Description = "Hành trình tuần tra",
                    MainOpsCount = 1m,
                    HoursAtBerth = 2m,
                    HoursCx100 = 1m,
                    AuxOpsCount = 1m,
                    AuxHours = 2m,
                    ManualOilOut = true,
                    OilOut = 2m
                }
            ]
        });
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal(30m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Ship));
        Assert.Equal(5m, Stock(app.System, "Nhớt sổ tàu test", 50000m, SeedIds.Ship));

        var after = app.System.GetShipQuarterBook(new DateTime(2026, 9, 30), SeedIds.Ship);
        Assert.Equal(3, after.Rows.Count);
        var use = after.Rows[2];
        Assert.False(use.IsTransfer);
        Assert.Equal(10m, use.FuelOut);
        Assert.Equal(3m, use.MainHoursTotal); // (2+1)×1 máy
        Assert.Equal(10m, use.GasolineUse);
        Assert.Null(use.DieselUse);
        Assert.Equal(2m, use.OilOut);
        Assert.Equal(30m, use.FuelBalance);
        Assert.Equal(5m, use.OilBalance);

        // 2 máy × (2h×1 + 1h×5) = 14; máy phụ 2×(2h×1.5)=6 → Xuất 20; dầu mỡ tự = Ceiling(20×0.04)=1
        var shipConsumer = app.System.GetConsumers().Single(x => x.Id == SeedIds.Ship);
        Assert.True(app.System.SaveConsumer(new ConsumerEdit
        {
            Id = shipConsumer.Id,
            Code = shipConsumer.Code,
            Name = shipConsumer.Name,
            Type = shipConsumer.Type,
            DefaultGroupId = shipConsumer.DefaultGroupId,
            ShipType = shipConsumer.ShipType,
            MainMachineCount = 2m,
            AuxMachineCount = 2m,
            NormFactors = shipConsumer.NormFactors.Select(f => new ConsumerNormFactorEdit
            {
                Id = f.Id,
                GroupId = f.GroupId ?? Guid.Empty,
                Name = f.Name,
                Value = f.Value,
                SortOrder = f.SortOrder
            }).ToList()
        }).Ok);

        var withMachines = app.System.SaveShipQuarterBook(new ShipQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Ship,
            QuarterDate = new DateTime(2026, 9, 30),
            FuelGroupName = "Xăng",
            Lines =
            [
                new ShipQuarterBookLineEdit
                {
                    DocumentNumber = "TT-TAU-SL",
                    DocumentDate = new DateTime(2026, 9, 21),
                    Description = "Hai máy chính",
                    MainOpsCount = 2m,
                    HoursAtBerth = 2m,
                    HoursCx100 = 1m,
                    AuxOpsCount = 2m,
                    AuxHours = 2m
                }
            ]
        });
        Assert.True(withMachines.Ok, withMachines.Message);
        Assert.Equal(20m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Ship));
        Assert.Equal(6m, Stock(app.System, "Nhớt sổ tàu test", 50000m, SeedIds.Ship)); // 7-1
        var machineBook = app.System.GetShipQuarterBook(new DateTime(2026, 9, 30), SeedIds.Ship);
        Assert.Equal(20m, machineBook.Rows[^1].FuelOut);
        Assert.Equal(1m, machineBook.Rows[^1].OilOut);
        Assert.False(machineBook.Rows[^1].ManualOilOut);
        Assert.Equal(6m, machineBook.Rows[^1].MainHoursTotal);
        Assert.Equal(2m, machineBook.Rows[^1].MainOpsCount);

        var manual = app.System.SaveShipQuarterBook(new ShipQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Ship,
            QuarterDate = new DateTime(2026, 9, 30),
            FuelGroupName = "Xăng",
            Lines =
            [
                new ShipQuarterBookLineEdit
                {
                    DocumentNumber = "TT-TAU-02",
                    DocumentDate = new DateTime(2026, 9, 25),
                    Description = "Xuất tay",
                    HoursAtBerth = 100m,
                    ManualFuelOut = true,
                    FuelOutManual = 7m,
                    ManualOilOut = true,
                    OilOut = 1m
                }
            ]
        });
        Assert.True(manual.Ok, manual.Message);
        Assert.Equal(33m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Ship));
        Assert.Equal(6m, Stock(app.System, "Nhớt sổ tàu test", 50000m, SeedIds.Ship)); // 7-1

        var manualBook = app.System.GetShipQuarterBook(new DateTime(2026, 9, 30), SeedIds.Ship);
        Assert.Equal(7m, manualBook.Rows[^1].FuelOut);
        Assert.True(manualBook.Rows[^1].ManualFuelOut);
        Assert.Equal(2m, manualBook.MainMachineCount);
    }

    [Fact]
    public void Ship_quarter_book_earlier_save_updates_all_later_quarter_openings()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 7, 1),
            WarehouseId = SeedIds.Ship,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 20000m,
            ActualQuantity = 50m
        }).Ok);

        var q3Date = new DateTime(2026, 9, 30);
        var q4Date = new DateTime(2026, 12, 31);
        var q1Next = new DateTime(2027, 3, 31);

        Assert.Equal(50m, app.System.GetShipQuarterBook(q3Date, SeedIds.Ship).Rows[0].FuelBalance);
        Assert.Equal(50m, app.System.GetShipQuarterBook(q4Date, SeedIds.Ship).Rows[0].FuelBalance);
        Assert.Equal(50m, app.System.GetShipQuarterBook(q1Next, SeedIds.Ship).Rows[0].FuelBalance);

        Assert.True(app.System.SaveShipQuarterBook(new ShipQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Ship,
            QuarterDate = q3Date,
            FuelGroupName = "Xăng",
            Lines =
            [
                new ShipQuarterBookLineEdit
                {
                    DocumentNumber = "TT-Q3-A",
                    DocumentDate = new DateTime(2026, 9, 10),
                    Description = "Tiêu thụ Q3",
                    ManualFuelOut = true,
                    FuelOutManual = 10m,
                    ManualOilOut = true,
                    OilOut = 0m
                }
            ]
        }).Ok);

        Assert.Equal(40m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Ship));
        Assert.Equal(40m, app.System.GetShipQuarterBook(q4Date, SeedIds.Ship).Rows[0].FuelBalance);
        Assert.Equal(40m, app.System.GetShipQuarterBook(q1Next, SeedIds.Ship).Rows[0].FuelBalance);

        // Đổi Q3 → mọi quý sau phải theo tồn mới (không chỉ quý kế tiếp).
        Assert.True(app.System.SaveShipQuarterBook(new ShipQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Ship,
            QuarterDate = q3Date,
            FuelGroupName = "Xăng",
            Lines =
            [
                new ShipQuarterBookLineEdit
                {
                    DocumentNumber = "TT-Q3-B",
                    DocumentDate = new DateTime(2026, 9, 10),
                    Description = "Sửa Q3",
                    ManualFuelOut = true,
                    FuelOutManual = 18m,
                    ManualOilOut = true,
                    OilOut = 0m
                }
            ]
        }).Ok);

        Assert.Equal(32m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Ship));
        Assert.Equal(32m, app.System.GetShipQuarterBook(q4Date, SeedIds.Ship).Rows[0].FuelBalance);
        Assert.Equal(32m, app.System.GetShipQuarterBook(q1Next, SeedIds.Ship).Rows[0].FuelBalance);

        // Xóa hết dòng Q3 (CRUD xóa) → mang sang các quý sau về lại 50.
        Assert.True(app.System.SaveShipQuarterBook(new ShipQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Ship,
            QuarterDate = q3Date,
            FuelGroupName = "Xăng",
            Lines = []
        }).Ok);
        Assert.Equal(50m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Ship));
        Assert.Equal(50m, app.System.GetShipQuarterBook(q4Date, SeedIds.Ship).Rows[0].FuelBalance);
        Assert.Equal(50m, app.System.GetShipQuarterBook(q1Next, SeedIds.Ship).Rows[0].FuelBalance);
    }

    [Fact]
    public void Ship_quarter_book_carry_forward_keeps_both_fuel_and_oil()
    {
        using var app = TestApp.Create();
        var oilGroup = app.System.GetGroups().Single(x => x.Name == "Nhớt");
        var unit = app.System.GetUnits().Single(x => x.Name == "Lít");
        var oilItem = app.System.SaveItem(new ItemEdit
        {
            Code = "OIL-CF",
            Name = "Nhớt mang sang",
            GroupId = oilGroup.Id,
            UnitId = unit.Id,
            Vcf = 1m
        });
        Assert.True(oilItem.Ok, oilItem.Message);

        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 7, 1),
            WarehouseId = SeedIds.Ship,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 20000m,
            ActualQuantity = 40m
        }).Ok);
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 7, 1),
            WarehouseId = SeedIds.Ship,
            ItemId = oilItem.Id!.Value,
            ItemName = "Nhớt mang sang",
            GroupName = "Nhớt",
            UnitName = "Lít",
            UnitPrice = 50000m,
            ActualQuantity = 10m
        }).Ok);

        var q3 = new DateTime(2026, 9, 30);
        var q4 = new DateTime(2026, 12, 31);
        Assert.True(app.System.SaveShipQuarterBook(new ShipQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Ship,
            QuarterDate = q3,
            FuelGroupName = "Xăng",
            Lines =
            [
                new ShipQuarterBookLineEdit
                {
                    DocumentNumber = "TT-CF-1",
                    DocumentDate = new DateTime(2026, 9, 12),
                    Description = "Tiêu thụ NL+DM",
                    ManualFuelOut = true,
                    FuelOutManual = 12m,
                    ManualOilOut = true,
                    OilOut = 3m
                }
            ]
        }).Ok);

        Assert.Equal(28m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Ship));
        Assert.Equal(7m, Stock(app.System, "Nhớt mang sang", 50000m, SeedIds.Ship));

        var open4 = app.System.GetShipQuarterBook(q4, SeedIds.Ship).Rows[0];
        Assert.Equal(28m, open4.FuelBalance);
        Assert.Equal(7m, open4.OilBalance);

        // Sửa lại quý trước — cả hai cột mang sang quý sau phải đổi đúng.
        Assert.True(app.System.SaveShipQuarterBook(new ShipQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Ship,
            QuarterDate = q3,
            FuelGroupName = "Xăng",
            Lines =
            [
                new ShipQuarterBookLineEdit
                {
                    DocumentNumber = "TT-CF-2",
                    DocumentDate = new DateTime(2026, 9, 12),
                    Description = "Sửa NL+DM",
                    ManualFuelOut = true,
                    FuelOutManual = 8m,
                    ManualOilOut = true,
                    OilOut = 5m
                }
            ]
        }).Ok);

        Assert.Equal(32m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.Ship));
        Assert.Equal(5m, Stock(app.System, "Nhớt mang sang", 50000m, SeedIds.Ship));
        open4 = app.System.GetShipQuarterBook(q4, SeedIds.Ship).Rows[0];
        Assert.Equal(32m, open4.FuelBalance);
        Assert.Equal(5m, open4.OilBalance);
    }

    [Fact]
    public void Ship_quarter_book_fifo_splits_across_lots_same_fuel_group()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 6, 10),
            WarehouseId = SeedIds.Ship,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 18000m,
            ActualQuantity = 5m
        }).Ok);
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 6, 20),
            WarehouseId = SeedIds.Ship,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 22000m,
            ActualQuantity = 8m
        }).Ok);

        var saved = app.System.SaveShipQuarterBook(new ShipQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Ship,
            QuarterDate = new DateTime(2026, 9, 30),
            FuelGroupName = "Xăng",
            Lines =
            [
                new ShipQuarterBookLineEdit
                {
                    DocumentNumber = "TT-FIFO",
                    DocumentDate = new DateTime(2026, 9, 15),
                    Description = "Trừ FIFO",
                    ManualFuelOut = true,
                    FuelOutManual = 9m,
                    ManualOilOut = true,
                    OilOut = 0m
                }
            ]
        });
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal(0m, Stock(app.System, "Xăng RON 95", 18000m, SeedIds.Ship));
        Assert.Equal(4m, Stock(app.System, "Xăng RON 95", 22000m, SeedIds.Ship));
    }

    [Fact]
    public void Consumer_quarter_book_auto_deduct_fifo_oldest_inbound_first()
    {
        using var app = TestApp.Create();
        // Hai lô cùng loại trên máy: phiếu nhập (mở đầu) cũ trước, mới sau.
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 6, 10),
            WarehouseId = SeedIds.Machine,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 18000m,
            ActualQuantity = 5m
        }).Ok);
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 6, 20),
            WarehouseId = SeedIds.Machine,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 22000m,
            ActualQuantity = 8m
        }).Ok);

        var saved = app.System.SaveConsumerQuarterBook(new ConsumerQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Machine,
            QuarterDate = new DateTime(2026, 9, 30),
            Lines =
            [
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = null,
                    DocumentNumber = "TT-FIFO-MAY",
                    DocumentDate = new DateTime(2026, 9, 15),
                    Description = "Trừ FIFO tự động",
                    ManualFuelOut = true,
                    FuelOut = 9m,
                    ManualOilOut = true,
                    OilOut = 0m,
                    LotTypeId = SeedIds.LotTypeTx,
                    LotTypeCode = "TX"
                }
            ]
        });
        Assert.True(saved.Ok, saved.Message);
        // Trừ hết lô 10/6 trước, rồi mới đến lô 20/6.
        Assert.Equal(0m, Stock(app.System, "Xăng RON 95", 18000m, SeedIds.Machine));
        Assert.Equal(4m, Stock(app.System, "Xăng RON 95", 22000m, SeedIds.Machine));
    }

    [Fact]
    public void Consumer_quarter_book_fifo_uses_import_date_not_transfer_date()
    {
        using var app = TestApp.Create();
        // PN cũ (10/6) nhưng ĐC xuống máy muộn; PN mới (20/6) nhưng ĐC sớm hơn.
        Assert.True(app.System.SaveImport(new ImportRequest
        {
            DocumentDate = new DateTime(2026, 6, 10),
            WarehouseId = SeedIds.WhMain,
            ItemId = SeedIds.ItemRon95,
            Vcf = 1m,
            UnitPrice = 18000m,
            InputQuantity = 5m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldInvoice, Value = "HD-OLD" }]
        }).Ok);
        Assert.True(app.System.SaveImport(new ImportRequest
        {
            DocumentDate = new DateTime(2026, 6, 20),
            WarehouseId = SeedIds.WhMain,
            ItemId = SeedIds.ItemRon95,
            Vcf = 1m,
            UnitPrice = 22000m,
            InputQuantity = 8m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldInvoice, Value = "HD-NEW" }]
        }).Ok);
        var oldLot = app.System.GetLots(SeedIds.WhMain).Single(x => x.UnitPrice == 18000).LotId;
        var newLot = app.System.GetLots(SeedIds.WhMain).Single(x => x.UnitPrice == 22000).LotId;

        Assert.True(app.System.SaveTransfer(new TransferRequest
        {
            DocumentDate = new DateTime(2026, 9, 5),
            SourceWarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.Machine,
            LotId = newLot,
            ActualQuantity = 8m,
            Vcf = 1m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Máy" }]
        }).Ok);
        Assert.True(app.System.SaveTransfer(new TransferRequest
        {
            DocumentDate = new DateTime(2026, 9, 25),
            SourceWarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.Machine,
            LotId = oldLot,
            ActualQuantity = 5m,
            Vcf = 1m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Máy" }]
        }).Ok);

        var saved = app.System.SaveConsumerQuarterBook(new ConsumerQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Machine,
            QuarterDate = new DateTime(2026, 9, 30),
            Lines =
            [
                new ConsumerQuarterBookLineEdit
                {
                    TransferDocumentId = null,
                    DocumentNumber = "TT-FIFO-PN",
                    DocumentDate = new DateTime(2026, 9, 28),
                    Description = "FIFO theo ngày PN",
                    ManualFuelOut = true,
                    FuelOut = 9m,
                    ManualOilOut = true,
                    OilOut = 0m,
                    LotTypeId = SeedIds.LotTypeTx,
                    LotTypeCode = "TX"
                }
            ]
        });
        Assert.True(saved.Ok, saved.Message);
        // Theo ngày PN (10/6 rồi 20/6), không theo ngày ĐC (25/9 vs 5/9).
        Assert.Equal(0m, Stock(app.System, "Xăng RON 95", 18000m, SeedIds.Machine));
        Assert.Equal(4m, Stock(app.System, "Xăng RON 95", 22000m, SeedIds.Machine));
    }

    [Fact]
    public void Consumption_formulas_follow_consumer_type()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(100m, 20000m, vcf: 0.99m)).Ok);
        var lot = OnlyLot(app.System);

        var machine = app.System.SaveConsumption(ConsumeMachine(lot, 10m));
        Assert.True(machine.Ok, machine.Message);
        Assert.Equal(10m, app.System.GetDocument(machine.Id!.Value)!.ActualQuantity);
        Assert.Equal(QuantityMath.ExportDisplayQuantity(10m, 0.99m), app.System.GetDocument(machine.Id!.Value)!.InputQuantity);

        var vehicle = app.System.SaveConsumption(ConsumeVehicle(lot, 100m, 0.25m));
        Assert.True(vehicle.Ok, vehicle.Message);
        var vehicleDoc = app.System.GetDocument(vehicle.Id!.Value)!;
        Assert.Equal(25m, vehicleDoc.ActualQuantity);
        Assert.Equal(0.25m, vehicleDoc.Norm);
        Assert.Equal(100m, vehicleDoc.Distance);

        var created = app.System.SaveConsumer(new ConsumerEdit
        {
            Id = null,
            Code = "KH-01",
            Name = "Công trình A",
            Type = ConsumerType.Other,
            DefaultGroupId = SeedIds.GroupFuel,
            DefaultItemId = SeedIds.ItemRon95,
            DefaultExportSampleSetId = SeedIds.SampleExport
        });
        var other = app.System.SaveConsumption(ConsumeOther(lot, 4m, created.Id!.Value));
        Assert.True(other.Ok, other.Message);
        Assert.Equal(99m - 10m - 25m - 4m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
    }

    [Fact]
    public void Auxiliary_consumption_only_hits_auxiliary_warehouse()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(40m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        Assert.True(app.System.SaveTransfer(Transfer(lot, 15m)).Ok);
        var rejected = app.System.SaveAuxiliary(Aux(lot, 1m, SeedIds.WhMain));
        Assert.False(rejected.Ok);
        var saved = app.System.SaveAuxiliary(Aux(lot, 5m, SeedIds.WhAux));
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal(10m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhAux));
        Assert.Equal(25m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        var doc = app.System.GetDocument(saved.Id!.Value)!;
        Assert.Equal(5m, doc.ActualQuantity);
        Assert.Equal(QuantityMath.ExportDisplayQuantity(5m, 0.99m), doc.InputQuantity);
    }

    [Fact]
    public void Catalog_changes_do_not_rewrite_saved_documents_or_stock()
    {
        using var app = TestApp.Create();
        var saved = app.System.SaveImport(Import(100m, 20000m, vcf: 0.99m, invoice: "HD-GOC"));
        Assert.True(saved.Ok, saved.Message);
        var before = app.System.GetDocument(saved.Id!.Value)!;

        var item = app.System.GetItem(SeedIds.ItemRon95)!;
        Assert.True(app.System.SaveItem(new ItemEdit
        {
            Id = item.Id,
            GroupId = item.GroupId,
            UnitId = item.UnitId,
            Name = "Xăng đổi tên",
            QualityInfo = "Đã đổi",
            Temperature = 15,
            MeasurementNote = "Đo mới",
            Vcf = 0.5m,
            ConversionRule = "Quy tắc mới"
        }).Ok);

        var warehouse = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhMain);
        Assert.True(app.System.SaveWarehouse(new WarehouseEdit
        {
            Id = warehouse.Id,
            Code = warehouse.Code,
            Name = "Kho đã đổi",
            Type = warehouse.Type,
            DefaultImportSampleSetId = warehouse.DefaultImportSampleSetId,
            DefaultExportSampleSetId = warehouse.DefaultExportSampleSetId
        }).Ok);

        var values = app.System.GetSampleValues(SeedIds.SampleImport);
        foreach (var value in values)
            app.System.DeleteSampleValue(value.Id);
        app.System.AddSampleValue(SeedIds.SampleImport, SeedIds.FieldInvoice, "HD-MOI");

        var after = app.System.GetDocument(saved.Id!.Value)!;
        Assert.Equal(before.ItemName, after.ItemName);
        Assert.Equal(before.Vcf, after.Vcf);
        Assert.Equal(before.ActualQuantity, after.ActualQuantity);
        Assert.Equal(before.QualityInfo, after.QualityInfo);
        Assert.Equal(before.WarehouseName, after.WarehouseName);
        Assert.Equal("HD-GOC", after.Fields.Single(x => x.Name == "Số hóa đơn").Value);
        Assert.Equal(99m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        Assert.Equal(0m, Stock(app.System, "Xăng đổi tên", 20000m, SeedIds.WhMain));
    }

    [Fact]
    public void Vehicle_norm_snapshot_survives_catalog_change()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(50m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        var saved = app.System.SaveConsumption(ConsumeVehicle(lot, 10m, 0.25m));
        Assert.True(saved.Ok, saved.Message);
        var consumer = app.System.GetConsumers().Single(x => x.Id == SeedIds.Vehicle);
        Assert.True(app.System.SaveConsumer(new ConsumerEdit
        {
            Id = consumer.Id,
            Code = consumer.Code,
            Name = consumer.Name,
            Type = consumer.Type,
            DefaultItemId = consumer.DefaultItemId,
            Norm = 0.9m,
            DefaultExportSampleSetId = consumer.DefaultExportSampleSetId
        }).Ok);

        var doc = app.System.GetDocument(saved.Id!.Value)!;
        Assert.Equal(0.25m, doc.Norm);
        Assert.Equal(3m, doc.ActualQuantity);
        Assert.Equal(47m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
    }

    [Fact]
    public void Edit_reapplies_stock_once_and_rolls_back_when_invalid()
    {
        using var app = TestApp.Create();
        var imported = app.System.SaveImport(Import(100m, 20000m, vcf: 1m));
        Assert.True(imported.Ok, imported.Message);
        var lot = OnlyLot(app.System);
        Assert.True(app.System.SaveConsumption(ConsumeMachine(lot, 40m)).Ok);
        Assert.Equal(60m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));

        var tooSmall = app.System.SaveImport(Import(30m, 20000m, vcf: 1m, documentId: imported.Id));
        Assert.False(tooSmall.Ok);
        Assert.Equal(60m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        Assert.Equal(100m, app.System.GetDocument(imported.Id!.Value)!.ActualQuantity);

        var edited = app.System.SaveImport(Import(80m, 20000m, vcf: 1m, documentId: imported.Id));
        Assert.True(edited.Ok, edited.Message);
        Assert.Equal(40m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        Assert.Equal(80m, app.System.GetDocument(imported.Id!.Value)!.ActualQuantity);
        Assert.Equal(40m, app.System.ListMovements(lot, SeedIds.WhMain).Sum(x => x.SignedQuantity));
    }

    [Fact]
    public void Void_reverses_stock_and_cannot_void_twice_or_drive_stock_negative()
    {
        using var app = TestApp.Create();
        var imported = app.System.SaveImport(Import(20m, 20000m, vcf: 1m));
        var lot = OnlyLot(app.System);
        var exported = app.System.SaveConsumption(ConsumeMachine(lot, 5m));
        var blocked = app.System.Void(imported.Id!.Value);
        Assert.False(blocked.Ok);
        Assert.Equal(15m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));

        var voidedExport = app.System.Void(exported.Id!.Value);
        Assert.True(voidedExport.Ok, voidedExport.Message);
        Assert.Equal(20m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        Assert.False(app.System.Void(exported.Id!.Value).Ok);

        Assert.True(app.System.Void(imported.Id!.Value).Ok);
        Assert.Equal(0m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        Assert.Equal(DocumentStatus.Voided, app.System.GetDocument(imported.Id!.Value)!.Status);
    }

    [Fact]
    public void Delete_slip_removes_import_and_restores_stock()
    {
        using var app = TestApp.Create();
        var imported = app.System.SaveImport(Import(20m, 20000m, vcf: 1m));
        Assert.True(imported.Ok, imported.Message);
        Assert.Equal(20m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));

        var deleted = app.System.DeleteSlip(imported.Id!.Value);
        Assert.True(deleted.Ok, deleted.Message);
        Assert.Null(app.System.GetDocument(imported.Id!.Value));
        Assert.Equal(0m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        Assert.DoesNotContain(app.System.ListDocuments(DocumentKind.Import), x => x.Id == imported.Id);
        Assert.False(app.System.DeleteSlip(imported.Id!.Value).Ok);
    }

    [Fact]
    public void Delete_slip_refuses_negative_stock_and_other_document_kinds()
    {
        using var app = TestApp.Create();
        var imported = app.System.SaveImport(Import(20m, 20000m, vcf: 1m));
        var lot = OnlyLot(app.System);
        var consumed = app.System.SaveConsumption(ConsumeMachine(lot, 5m));
        Assert.True(consumed.Ok, consumed.Message);

        var blocked = app.System.DeleteSlip(imported.Id!.Value);
        Assert.False(blocked.Ok);
        Assert.Equal(15m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        Assert.NotNull(app.System.GetDocument(imported.Id!.Value));

        var wrongKind = app.System.DeleteSlip(consumed.Id!.Value);
        Assert.False(wrongKind.Ok);
        Assert.NotNull(app.System.GetDocument(consumed.Id!.Value));
        Assert.Equal(15m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
    }

    [Fact]
    public void Rebuild_restores_stock_from_active_documents_without_doubling()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveOpening(Open(3m, 20000m)).Ok);
        Assert.True(app.System.SaveImport(Import(10m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        Assert.True(app.System.SaveConsumption(ConsumeMachine(lot, 4m)).Ok);
        Assert.Equal(9m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));

        app.System.DebugSetBalance(lot, SeedIds.WhMain, 1m);
        Assert.Equal(1m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        var rebuilt = app.System.RebuildStock();
        Assert.True(rebuilt.Ok);
        Assert.Empty(rebuilt.Warnings);
        Assert.Equal(9m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        Assert.Equal(9m, app.System.ListMovements(lot, SeedIds.WhMain).Sum(x => x.SignedQuantity));
    }

    [Fact]
    public void Hidden_field_is_not_required_and_empty_invoice_uses_document_number()
    {
        using var app = TestApp.Create();
        var auto = app.System.SaveImport(Import(1m, 20000m, vcf: 1m, invoice: ""));
        Assert.True(auto.Ok, auto.Message);
        var autoDoc = app.System.GetDocument(auto.Id!.Value)!;
        Assert.Equal(autoDoc.Number, autoDoc.Slip.FormNumber);
        Assert.Equal(autoDoc.Number, autoDoc.Fields.Single(x => x.Name == "Số hóa đơn").Value);

        var saved = app.System.SaveImport(Import(1m, 20000m, vcf: 1m, invoice: "HD-1"));
        Assert.True(saved.Ok, saved.Message);
        var form = app.System.BuildFieldForm(DocumentFamily.Import, SeedIds.SampleImport);
        Assert.DoesNotContain(form, x => x.Name == "Ghi chú nội bộ");
        Assert.Equal("HD-MAU", form.Single(x => x.Name == "Số hóa đơn").Value);
        Assert.False(app.System.GetFields(DocumentFamily.Export).Single(x => x.Name == "Người nhận").IsRequired);
    }

    [Fact]
    public void Manual_value_can_join_sample_data_without_changing_the_saved_document()
    {
        using var app = TestApp.Create();
        var saved = app.System.SaveImport(Import(2m, 20000m, vcf: 1m, invoice: "HD-TAY", addSample: SeedIds.SampleImport));
        Assert.True(saved.Ok, saved.Message);
        Assert.Contains("HD-TAY", app.System.GetFieldOptions(SeedIds.FieldInvoice));
        var sample = app.System.GetSampleValues(SeedIds.SampleImport).Single(x => x.Value == "HD-TAY");
        Assert.True(app.System.DeleteSampleValue(sample.Id).Ok);
        Assert.Equal("HD-TAY", app.System.GetDocument(saved.Id!.Value)!.Fields.Single(x => x.Name == "Số hóa đơn").Value);
    }

    [Fact]
    public void Import_adjustment_posts_two_lots_and_reloads_the_same_amounts()
    {
        using var app = TestApp.Create();
        var saved = app.System.SaveImport(Import(100m, 20000m, vcf: 1m, amount: 2_000_050m));
        Assert.True(saved.Ok, saved.Message);
        var doc = app.System.GetDocument(saved.Id!.Value)!;
        Assert.True(doc.WasSplit);
        Assert.Equal(2, doc.Lines.Count);
        Assert.Equal(2_000_050m, doc.Lines.Sum(x => x.Amount));
        Assert.Equal(100m, doc.Lines.Sum(x => x.ActualQuantity));
        Assert.Equal(50m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        Assert.Equal(50m, Stock(app.System, "Xăng RON 95", 20001m, SeedIds.WhMain));
    }

    [Fact]
    public void Stock_formula_matches_opening_plus_actual_in_minus_actual_out()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveOpening(Open(5m, 18000m)).Ok);
        Assert.True(app.System.SaveImport(Import(10m, 18000m, vcf: 0.5m)).Ok);
        var lot = app.System.GetLots(SeedIds.WhMain).Single(x => x.UnitPrice == 18000).LotId;
        Assert.True(app.System.SaveTransfer(Transfer(lot, 2m)).Ok);
        Assert.True(app.System.SaveAuxiliary(Aux(lot, 1m, SeedIds.WhAux)).Ok);
        var main = Stock(app.System, "Xăng RON 95", 18000m, SeedIds.WhMain);
        var aux = Stock(app.System, "Xăng RON 95", 18000m, SeedIds.WhAux);
        var actualIn = QuantityMath.ActualImport(10m, 0.5m);
        Assert.Equal(5m + actualIn - 2m, main);
        Assert.Equal(2m - 1m, aux);
    }

    [Fact]
    public void Paper_slip_posts_actual_quantity_and_keeps_the_form_header()
    {
        using var app = TestApp.Create();
        var saved = app.System.SaveSlip(new SlipRequest
        {
            DocumentDate = new DateTime(2026, 5, 14),
            FormNumber = "1-02/XD-14",
            WarehouseId = SeedIds.WhMain,
            OrganizationName = "BỘ ĐỘI BIÊN PHÒNG",
            UnitTitle = "HẢI ĐOÀN BIÊN PHÒNG 18",
            ReceiverUnit = "Kho Hải đoàn Biên phòng 18",
            SenderUnit = "Công ty CP DK QT TPP",
            Nature = "Mua đấu thầu kinh phí IUU",
            ContractOrOrder = "05/HĐMB/HĐBP18-TPP",
            Kilometers = "400",
            Mission = "Cấp tàu",
            Fields = [new FieldInput { FieldId = SeedIds.FieldInvoice, Value = "HD-1" }],
            Lines =
            [
                new SlipLineInput
                {
                    ItemId = SeedIds.ItemDo,
                    ObservedQuantity = 29.531568228105908m,
                    Vcf = 0.982m,
                    ActualQuantity = 29m,
                    UnitPrice = 29240m,
                    Temperature = 36,
                    Density = 0.835m,
                    QualityGrade = "1"
                },
                new SlipLineInput
                {
                    ItemId = SeedIds.ItemRon95,
                    ObservedQuantity = 100m,
                    Vcf = 0.99m,
                    UnitPrice = 20000m,
                    QualityGrade = "1"
                }
            ]
        });
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal(29m, Stock(app.System, "Dầu DO 0,05S", 29240m, SeedIds.WhMain));
        Assert.Equal(99m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        var doc = app.System.GetDocument(saved.Id!.Value)!;
        Assert.Equal("1-02/XD-14", doc.Slip.FormNumber);
        Assert.Equal(29m * 29240m + 99m * 20000m, doc.Amount);
        Assert.Equal("400", doc.Slip.Kilometers);
        Assert.Equal(36m, doc.Lines[0].Temperature);
        Assert.Equal(0.835m, doc.Lines[0].Density);

        var exported = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            DocumentDate = new DateTime(2026, 6, 30),
            FormNumber = "1-03/XD-14",
            WarehouseId = SeedIds.WhMain,
            Nature = "Hao hụt định mức",
            ReceiverPerson = "Trương Xuân Dụng",
            VehiclePlate = "QB 38-09",
            Kilometers = "400",
            Mission = "Hao hụt định mức",
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Trương Xuân Dụng" }],
            Lines = [new SlipLineInput { ItemId = SeedIds.ItemRon95, ObservedQuantity = 10m, Vcf = 0.99m, UnitPrice = 20000m, ActualQuantity = 10m }]
        });
        Assert.True(exported.Ok, exported.Message);
        Assert.Equal(89m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        var issue = app.System.GetDocument(exported.Id!.Value)!;
        Assert.Equal(DocumentKind.Issue, issue.Kind);
        Assert.Equal("QB 38-09", issue.Slip.VehiclePlate);
        Assert.Equal("Phiếu xuất", app.System.ListDocuments(DocumentKind.Issue).Single().KindName);
    }

    [Fact]
    public void Paper_transfer_moves_stock_and_auxiliary_only_hits_auxiliary_warehouse()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(50m, 20000m, vcf: 1m)).Ok);
        var moved = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            DestinationWarehouseId = SeedIds.WhAux,
            DocumentDate = new DateTime(2026, 6, 30),
            FormNumber = "DC-1",
            WarehouseId = SeedIds.WhMain,
            ReceiverPerson = "Thủ kho",
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }],
            Lines = [new SlipLineInput { ItemId = SeedIds.ItemRon95, ItemName = "Xăng RON 95", ObservedQuantity = 20m, Vcf = 1m, UnitPrice = 20000m, ActualQuantity = 20m }]
        });
        Assert.True(moved.Ok, moved.Message);
        Assert.Equal(30m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        Assert.Equal(20m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhAux));
        var transfer = app.System.GetDocument(moved.Id!.Value)!;
        Assert.Equal(DocumentKind.Transfer, transfer.Kind);
        Assert.Equal(SeedIds.WhAux, transfer.DestinationWarehouseId);
        Assert.Equal("DC-1", transfer.Slip.FormNumber);

        var same = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            DestinationWarehouseId = SeedIds.WhMain,
            WarehouseId = SeedIds.WhMain,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }],
            Lines = [new SlipLineInput { ItemName = "Xăng RON 95", ObservedQuantity = 1m, Vcf = 1m, UnitPrice = 20000m, ActualQuantity = 1m }]
        });
        Assert.False(same.Ok);

        var wrongWarehouse = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Auxiliary,
            WarehouseId = SeedIds.WhMain,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }],
            Lines = [new SlipLineInput { ItemName = "Xăng RON 95", ObservedQuantity = 1m, Vcf = 1m, UnitPrice = 20000m, ActualQuantity = 1m }]
        });
        Assert.False(wrongWarehouse.Ok);
        Assert.Equal(30m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));

        var aux = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Auxiliary,
            WarehouseId = SeedIds.WhAux,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }],
            Lines = [new SlipLineInput { ItemName = "Xăng RON 95", ObservedQuantity = 5m, Vcf = 1m, UnitPrice = 20000m, ActualQuantity = 5m }]
        });
        Assert.True(aux.Ok, aux.Message);
        Assert.Equal(15m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhAux));
        Assert.Equal(30m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
        Assert.Equal(DocumentKind.Auxiliary, app.System.GetDocument(aux.Id!.Value)!.Kind);
    }

    [Fact]
    public void Excel_sample_lists_are_loaded_into_catalog_and_field_options()
    {
        using var app = TestApp.Create();
        Assert.NotNull(app.System.GetItems().Single(x => x.Name == "Xăng RON 92"));
        Assert.Equal(0.973500m, app.System.GetItem(SeedIds.ItemRon95)!.Vcf);
        Assert.Equal(0.982000m, app.System.GetItem(SeedIds.ItemDo)!.Vcf);
        Assert.Equal("Kho Hải đoàn Biên phòng 18", app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhMain).Name);
        var nature = app.System.GetFields(DocumentFamily.Export).Single(x => x.Name == "Tính chất xuất");
        Assert.Contains("Hao hụt định mức", app.System.GetFieldOptions(nature.Id));
        Assert.Contains("Cấp tàu 01-01", app.System.GetFieldOptions(nature.Id));
        var mission = app.System.GetFields(DocumentFamily.Export).Single(x => x.Name == "Nhiệm vụ");
        Assert.Contains("IUU", app.System.GetFieldOptions(mission.Id));
        var plate = app.System.GetConsumers().Single(x => x.Code == "QB38-09");
        Assert.Equal(0.36m, plate.Norm);
        Assert.Equal("Dầu DO 0,05S", plate.DefaultItemName);
        Assert.Contains("Có giá đến ngày", app.System.GetFields(DocumentFamily.Export).Select(x => x.Name));
        Assert.Empty(app.System.GetFieldOptions(
            app.System.GetFields(DocumentFamily.Export).Single(x => x.Name == "Có giá đến ngày").Id));
    }

    [Fact]
    public void Paper_fields_are_managed_as_samples_except_slip_number_and_date()
    {
        using var app = TestApp.Create();
        foreach (var family in new[] { DocumentFamily.Import, DocumentFamily.Export })
        {
            var fields = app.System.GetFields(family);
            Assert.Contains(fields, x => x.Name == "Cơ quan" && x.IsVisible && !x.IsRequired);
            Assert.Contains(fields, x => x.Name == "Giấy giới thiệu và CMT" && x.IsVisible);
            Assert.Contains(fields, x => x.Name == "Chữ ký người giao" && x.IsVisible && !x.IsRequired);
            Assert.Contains(fields, x => x.Name == "Ghi chú" && x.IsVisible);
            Assert.Contains(fields, x => x.Name == "Số lượng" && x.IsVisible && !x.IsRequired);
            Assert.DoesNotContain(fields, x => x.Name is "Số phiếu" or "Ngày" or "Ngày chứng từ");
        }

        Assert.Equal("Người giao hàng", app.System.GetFields(DocumentFamily.Import).Single(x => x.Id == SeedIds.FieldDeliverer).Name);
        Assert.Contains(app.System.GetFields(DocumentFamily.Import), x => x.Name == "Thực nhập" && !x.IsRequired);
        Assert.Contains(app.System.GetFields(DocumentFamily.Export), x => x.Name == "Thực xuất" && !x.IsRequired);
        Assert.Contains("BỘ ĐỘI BIÊN PHÒNG", app.System.GetFieldOptions(app.System.GetFields(DocumentFamily.Import).Single(x => x.Name == "Cơ quan").Id));

        var form = app.System.BuildFieldForm(DocumentFamily.Import, SeedIds.SampleImport);
        Assert.Contains(form, x => x.Name == "Số hóa đơn");
        Assert.DoesNotContain(form, x => x.Name == "Cơ quan");
        Assert.DoesNotContain(form, x => x.Name == "Ghi chú nội bộ");
    }

    [Fact]
    public void Backup_restore_replaces_data_and_repairs_a_short_schema()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveGroup(null, "Nhóm sao lưu").Ok);
        var backup = Path.Combine(Path.GetDirectoryName(app.System.DatabasePath)!, "backup.db");
        var saved = app.System.Backup(backup);
        Assert.True(saved.Ok, saved.Message);
        Assert.True(app.System.SaveGroup(null, "Nhóm sau sao lưu").Ok);

        var restored = app.System.Restore(backup);
        Assert.True(restored.Ok, restored.Message);
        var names = app.System.GetGroups().Select(x => x.Name).ToList();
        Assert.Contains("Nhóm sao lưu", names);
        Assert.DoesNotContain("Nhóm sau sao lưu", names);

        var opened = app.System.SaveOpening(Open(4m, 15000m));
        Assert.True(opened.Ok, opened.Message);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = app.System.DatabasePath }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE FuelItems DROP COLUMN Code";
            command.ExecuteNonQuery();
            command.CommandText = "DROP TABLE DocumentFields";
            command.ExecuteNonQuery();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var adapted = new FuelSystem(app.System.DatabasePath, seed: false);
        Assert.Contains(adapted.GetItems(), x => x.Name == "Xăng RON 95");
        var document = adapted.GetDocument(opened.Id!.Value);
        Assert.NotNull(document);
        Assert.Empty(document.Fields);
    }

    [Fact]
    public void Demo_activity_posts_opening_imports_each_export_and_quarter_consumption()
    {
        using var app = TestApp.Create();
        var result = app.System.LoadDemoActivity();
        Assert.True(result.Ok, result.Message);
        Assert.True(app.System.GetWarehouses().Count(x => x.Type == WarehouseType.Auxiliary) >= 3);
        Assert.Contains(app.System.GetWarehouses(), x => x.ConsumerTypeName == "Máy");
        Assert.Contains(app.System.GetWarehouses(), x => x.ConsumerTypeName == "Phương tiện");
        Assert.Contains(app.System.GetWarehouses(), x => x.ConsumerTypeName == "Tàu");
        Assert.True(app.System.ListDocuments(DocumentKind.Opening).Count >= 3);
        Assert.True(app.System.ListDocuments(DocumentKind.Import).Count >= 3);
        Assert.True(app.System.ListDocuments(DocumentKind.Issue).Count >= 3);
        Assert.True(app.System.ListDocuments(DocumentKind.Transfer).Count >= 15);
        Assert.True(app.System.ListDocuments(DocumentKind.Consumption).Count(x =>
            app.System.GetDocument(x.Id)?.Distance is null) >= 2);
        var consumption = app.System.ListDocuments(DocumentKind.Consumption)
            .Select(x => app.System.GetDocument(x.Id)!)
            .ToList();
        Assert.DoesNotContain(consumption, x => x.Distance is not null);
        Assert.True(consumption.Count(x => x.Distance is null && x.DocumentDate == new DateTime(2026, 9, 30)) >= 2);
        Assert.DoesNotContain(consumption.Where(x => x.Distance is null), x => x.WarehouseTypeName == "Kho XD" && x.DocumentDate == new DateTime(2026, 9, 30));
        var warehousesById = app.System.GetWarehouses().ToDictionary(x => x.Id);
        Assert.Contains(consumption, x => x.WarehouseId is Guid id && warehousesById.TryGetValue(id, out var w) && w.ConsumerTypeName == "Máy");
        Assert.Contains(consumption, x => x.WarehouseId is Guid id && warehousesById.TryGetValue(id, out var w) && w.ConsumerTypeName == "Phương tiện");
        Assert.Contains(consumption, x => x.WarehouseId is Guid id && warehousesById.TryGetValue(id, out var w) && w.ConsumerTypeName == "Tàu");

        var xdRetail = app.System.ListDocuments(DocumentKind.Issue)
            .Where(x => x.FormNumber.StartsWith("DEMO-PX", StringComparison.Ordinal)
                && !x.FormNumber.StartsWith("DEMO-PTKT-", StringComparison.Ordinal))
            .ToList();
        Assert.InRange(xdRetail.Count, 8, 12);

        var transferRows = app.System.ListDocuments(DocumentKind.Transfer)
            .Where(x => x.FormNumber.StartsWith("DEMO-", StringComparison.Ordinal))
            .ToList();
        Assert.True(transferRows.Count >= 15);
        var transferDetails = transferRows.Select(x => app.System.GetDocument(x.Id)!).ToList();
        var transferByDest = transferDetails
            .Where(x => x.DestinationWarehouseId is Guid)
            .GroupBy(x => x.DestinationWarehouseId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.ActualQuantity));

        // Máy/Xe có ĐC: tiêu thụ quý = tổng ĐC; Máy/Xe không ĐC: không có tiêu thụ quý.
        var machines = app.System.GetWarehouses().Where(x => x.ConsumerTypeName == "Máy").OrderBy(x => x.Name).ToList();
        var vehicles = app.System.GetWarehouses().Where(x => x.ConsumerTypeName == "Phương tiện").OrderBy(x => x.Name).ToList();
        var ships = app.System.GetWarehouses().Where(x => x.ConsumerTypeName == "Tàu").OrderBy(x => x.Name).ToList();
        Assert.True(machines.Count >= 2);
        Assert.True(vehicles.Count >= 2);
        Assert.True(ships.Count >= 2);
        var machineNoDc = machines[1];
        var vehicleNoDc = vehicles[1];
        var shipNoDc = ships[0];
        Assert.DoesNotContain(consumption, x => x.WarehouseId == machineNoDc.Id);
        Assert.DoesNotContain(consumption, x => x.WarehouseId == vehicleNoDc.Id);
        Assert.False(transferByDest.ContainsKey(machineNoDc.Id));
        Assert.False(transferByDest.ContainsKey(vehicleNoDc.Id));
        Assert.False(transferByDest.ContainsKey(shipNoDc.Id));
        Assert.Contains(consumption, x => x.WarehouseId == shipNoDc.Id); // tàu không ĐC vẫn tiêu thụ từ tồn

        foreach (var ship in ships)
        {
            var book = app.System.GetShipQuarterBook(new DateTime(2026, 9, 30), ship.Id);
            Assert.True(book.BookId is Guid id && id != Guid.Empty, $"Thiếu sổ tàu {ship.Name}");
        }

        var lotTypeIds = new HashSet<Guid>
        {
            SeedIds.LotTypeTx,
            SeedIds.LotTypeSscd,
            SeedIds.LotTypeIuu
        };
        var importTypes = app.System.ListDocuments(DocumentKind.Import)
            .Where(x => x.FormNumber.StartsWith("DEMO-PN", StringComparison.Ordinal))
            .Select(x => app.System.GetDocument(x.Id)!)
            .SelectMany(x => x.Lines.Select(l => l.LotTypeId ?? x.LotTypeId ?? SeedIds.LotTypeTx))
            .ToHashSet();
        Assert.True(lotTypeIds.IsSubsetOf(importTypes), "Demo nhập phải có đủ TX/SSCĐ/IUU");
        var transferTypes = transferDetails
            .SelectMany(x => x.Lines.Select(l => l.LotTypeId ?? x.LotTypeId ?? SeedIds.LotTypeTx))
            .ToHashSet();
        Assert.True(lotTypeIds.IsSubsetOf(transferTypes), "Demo ĐC phải có đủ TX/SSCĐ/IUU");
        var consumptionTypes = consumption
            .Where(x => x.Distance is null)
            .Select(x => x.LotTypeId ?? SeedIds.LotTypeTx)
            .ToHashSet();
        Assert.True(lotTypeIds.IsSubsetOf(consumptionTypes), "Demo tiêu thụ phải có đủ TX/SSCĐ/IUU");

        foreach (var row in consumption.Where(x => x.WarehouseId is Guid id && warehousesById.TryGetValue(id, out var w) && w.ConsumerTypeName is "Máy" or "Phương tiện"))
        {
            Assert.True(transferByDest.TryGetValue(row.WarehouseId!.Value, out var totalDc));
            var destConsumption = consumption.Where(x => x.WarehouseId == row.WarehouseId).Sum(x => x.ActualQuantity);
            Assert.Equal(totalDc, destConsumption);
        }

        var stock = app.System.ListStock(new StockFilter(null, null, null, null, true));
        foreach (var group in new[] { "Xăng", "Dầu", "Nhớt", "Mỡ" })
            Assert.Contains(stock, x => x.GroupName == group && x.Quantity >= 1000);
        Assert.Contains(app.System.ListDocuments(DocumentKind.Import), x => x.FormNumber == "DEMO-PN-01");
        Assert.Contains(app.System.ListDocuments(DocumentKind.Import), x => x.FormNumber == "DEMO-PN-TACH");
        Assert.Contains(app.System.ListDocuments(DocumentKind.Import), x => x.FormNumber == "DEMO-PN-NHIEU");
        Assert.DoesNotContain(app.System.ListDocuments(DocumentKind.Issue), x => x.FormNumber == "DEMO-PX-MAY");
        Assert.DoesNotContain(app.System.ListDocuments(), x => x.FormNumber.StartsWith("DEMO-PT-", StringComparison.Ordinal));
        Assert.Contains(app.System.ListDocuments(DocumentKind.Issue), x => x.FormNumber == "DEMO-PX-LE");
        Assert.Contains(app.System.ListDocuments(DocumentKind.Issue), x => x.FormNumber == "DEMO-PX-NHIEU");
        Assert.All(app.System.ListDocuments(DocumentKind.Issue).Where(x => x.FormNumber.StartsWith("DEMO-", StringComparison.Ordinal)), row =>
        {
            var detail = app.System.GetDocument(row.Id)!;
            Assert.True(string.IsNullOrWhiteSpace(detail.ConsumerTypeName)
                || detail.ConsumerTypeName is not ("Máy" or "Phương tiện"));
        });
        Assert.DoesNotContain(app.System.ListDocuments(DocumentKind.Auxiliary), x => x.FormNumber.StartsWith("DEMO-", StringComparison.Ordinal));
        var split = app.System.GetDocument(app.System.ListDocuments().Single(x => x.FormNumber == "DEMO-PN-TACH").Id)!;
        Assert.Equal(2, split.Lines.Count);
        Assert.Equal(2, split.Lines.Select(x => x.UnitPrice).Distinct().Count());
        foreach (var form in new[] { "DEMO-PN-01", "DEMO-PX-01", "DEMO-PN-TACH", "DEMO-PX-LE" })
        {
            var detail = app.System.GetDocument(app.System.ListDocuments().Single(x => x.FormNumber == form).Id)!;
            Assert.All(detail.Lines, line =>
            {
                Assert.Equal(decimal.Truncate(line.Quantity), line.Quantity);
                Assert.Equal(decimal.Truncate(line.ActualQuantity), line.ActualQuantity);
            });
            var family = detail.Kind == DocumentKind.Import ? DocumentFamily.Import : DocumentFamily.Export;
            var visible = app.System.GetFields(family).Where(x => x.IsVisible).Select(x => x.Name).ToHashSet();
            var filled = detail.Fields.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Name).ToHashSet();
            Assert.True(visible.IsSubsetOf(filled), form);
        }

        Assert.All(app.System.ListDocuments(), row =>
        {
            Assert.Equal(decimal.Truncate(row.InputQuantity), row.InputQuantity);
            Assert.Equal(decimal.Truncate(row.ActualQuantity), row.ActualQuantity);
        });

        Assert.Contains(transferRows, x => x.FormNumber == "DEMO-DC-MAY");
        Assert.Contains(transferRows, x => x.FormNumber == "DEMO-DC-XE");
        Assert.Contains(transferRows, x => x.FormNumber == "DEMO-DC-TAU");
        Assert.All(transferRows, row =>
        {
            var detail = app.System.GetDocument(row.Id)!;
            Assert.True(detail.DocumentDate >= new DateTime(2026, 7, 1) && detail.DocumentDate <= new DateTime(2026, 9, 30));
            Assert.NotNull(detail.DestinationWarehouseId);
            Assert.NotEqual(SeedIds.WhPtkt, detail.WarehouseId);
            Assert.NotEqual(SeedIds.WhPtkt, detail.DestinationWarehouseId);
            Assert.NotEqual("Kho PTKT-VTXD", detail.WarehouseTypeName);
            Assert.True(warehousesById.TryGetValue(detail.DestinationWarehouseId!.Value, out var dest));
            Assert.Equal(WarehouseType.Auxiliary, dest.Type);
        });
        Assert.Equal("Máy", warehousesById[app.System.GetDocument(transferRows.Single(x => x.FormNumber == "DEMO-DC-MAY").Id)!.DestinationWarehouseId!.Value].ConsumerTypeName);
        Assert.Equal("Phương tiện", warehousesById[app.System.GetDocument(transferRows.Single(x => x.FormNumber == "DEMO-DC-XE").Id)!.DestinationWarehouseId!.Value].ConsumerTypeName);
        Assert.Equal("Tàu", warehousesById[app.System.GetDocument(transferRows.Single(x => x.FormNumber == "DEMO-DC-TAU").Id)!.DestinationWarehouseId!.Value].ConsumerTypeName);

        Assert.Contains(app.System.ListDocuments(DocumentKind.Import), x => x.FormNumber == "DEMO-PTKT-PN-01");
        Assert.Contains(app.System.ListDocuments(DocumentKind.Issue), x => x.FormNumber == "DEMO-PTKT-PX-LE");
        Assert.DoesNotContain(app.System.ListDocuments(DocumentKind.Transfer), x => x.FormNumber.StartsWith("DEMO-PTKT-", StringComparison.Ordinal));
        Assert.DoesNotContain(app.System.ListDocuments(DocumentKind.Consumption), x => x.FormNumber.StartsWith("DEMO-PTKT-", StringComparison.Ordinal));
        Assert.Equal(WarehouseScope.Xd, app.System.GetWarehouseScope());
        app.System.SetWarehouseScope(WarehouseScope.Ptkt);
        var ptktNames = app.System.GetItems().Where(x => x.GroupName == "PTKT - VTXD").Select(x => x.Name).ToHashSet();
        Assert.NotEmpty(ptktNames);
        app.System.SetWarehouseScope(WarehouseScope.Xd);
        var xdItemNames = app.System.GetItems()
            .Where(x => x.GroupName is "Xăng" or "Dầu" or "Nhớt" or "Mỡ")
            .Select(x => x.Name)
            .ToHashSet();
        Assert.All(app.System.ListDocuments().Where(x => x.FormNumber.StartsWith("DEMO-", StringComparison.Ordinal)
            && !x.FormNumber.StartsWith("DEMO-PTKT-", StringComparison.Ordinal)), row =>
        {
            var detail = app.System.GetDocument(row.Id)!;
            Assert.NotEqual("Kho PTKT-VTXD", detail.WarehouseTypeName);
            Assert.NotEqual(SeedIds.WhPtkt, detail.WarehouseId);
            Assert.False(detail.GroupName.Contains("PTKT", StringComparison.OrdinalIgnoreCase), row.FormNumber);
            Assert.All(detail.Lines, line => Assert.Contains(line.ItemName, xdItemNames));
        });
        Assert.All(app.System.ListDocuments().Where(x => x.FormNumber.StartsWith("DEMO-PTKT-", StringComparison.Ordinal)), row =>
        {
            var detail = app.System.GetDocument(row.Id)!;
            Assert.Equal("Kho PTKT-VTXD", detail.WarehouseTypeName);
            Assert.Equal(SeedIds.WhPtkt, detail.WarehouseId);
            Assert.Null(detail.DestinationWarehouseId);
            Assert.All(detail.Lines, line => Assert.Contains(line.ItemName, ptktNames));
            Assert.DoesNotContain(detail.Lines, line => xdItemNames.Contains(line.ItemName));
            Assert.True(row.Kind is DocumentKind.Opening or DocumentKind.Import or DocumentKind.Issue);
        });
        var catalog = app.System.GetCatalogWarehouses();
        Assert.Contains(catalog, x => x.Id == SeedIds.WhMain && x.Type == WarehouseType.Main);
        Assert.Contains(catalog, x => x.Id == SeedIds.WhPtkt && x.Type == WarehouseType.Ptkt);
        Assert.DoesNotContain(app.System.GetWarehouses(), x => x.Type == WarehouseType.Ptkt);
        Assert.Equal("Dữ liệu thử đã có.", app.System.LoadDemoActivity().Message);
    }

    [Fact]
    public void Clear_all_wipes_activity_and_catalog_without_excel_reload()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveOpening(Open(4m, 15000m)).Ok);
        Assert.Contains(app.System.GetItems(), x => x.Name == "Xăng RON 95");
        var result = app.System.ClearAll();
        Assert.True(result.Ok, result.Message);
        Assert.Empty(app.System.ListDocuments());
        Assert.Empty(app.System.ListStock(new StockFilter(null, null, null, null, true)));
        Assert.Empty(app.System.GetItems());
        Assert.Empty(app.System.GetGroups());
        Assert.Empty(app.System.GetWarehouses());
        Assert.Empty(app.System.GetConsumers());
        Assert.Empty(app.System.GetSampleSets());

        // Reopening database in default app mode (seed: false) must keep it clean
        var reopened = new FuelSystem(app.System.DatabasePath, seed: false);
        Assert.Empty(reopened.GetItems());
        Assert.Empty(reopened.GetGroups());
        Assert.Empty(reopened.GetWarehouses());
        Assert.Empty(reopened.GetConsumers());

        // Explicitly loading sample catalog repopulates it
        var loadRes = reopened.LoadSampleCatalog();
        Assert.True(loadRes.Ok, loadRes.Message);
        Assert.Contains(reopened.GetItems(), x => x.Name == "Xăng RON 95");
        Assert.Contains(reopened.GetConsumers(), x => x.Name == "Máy QY");
        Assert.DoesNotContain(reopened.GetConsumers(), x => x.Name == "Công trình A");
    }

    [Fact]
    public void Clear_activity_keeps_catalog_and_catalog_backup_restores_only_catalog()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveOpening(Open(4m, 15000m)).Ok);
        Assert.True(app.System.SaveGroup(null, "Nhóm giữ").Ok);
        var cleared = app.System.ClearActivity();
        Assert.True(cleared.Ok, cleared.Message);
        Assert.Empty(app.System.ListDocuments());
        Assert.Empty(app.System.ListStock(new StockFilter(null, null, null, null, true)));
        Assert.Contains(app.System.GetGroups(), x => x.Name == "Nhóm giữ");
        Assert.Contains(app.System.GetItems(), x => x.Name == "Xăng RON 95");

        var path = Path.Combine(Path.GetDirectoryName(app.System.DatabasePath)!, "catalog.db");
        Assert.True(app.System.BackupCatalog(path).Ok);
        Assert.True(app.System.SaveGroup(null, "Nhóm sau").Ok);
        Assert.True(app.System.SaveOpening(Open(2m, 15000m)).Ok);
        var restored = app.System.RestoreCatalog(path);
        Assert.True(restored.Ok, restored.Message);
        Assert.Contains(app.System.GetGroups(), x => x.Name == "Nhóm giữ");
        Assert.DoesNotContain(app.System.GetGroups(), x => x.Name == "Nhóm sau");
        Assert.NotEmpty(app.System.ListDocuments());
    }

    [Fact]
    public void Nxt_book_keeps_opening_running_balance_and_group_totals()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 6, 1),
            WarehouseId = SeedIds.WhMain,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 15000m,
            ActualQuantity = 10m
        }).Ok);
        var group = app.System.GetGroups().Single(x => x.Name == "Xăng");
        var unit = app.System.GetUnits().Single(x => x.Name == "Lít");
        var added = app.System.SaveItem(new ItemEdit { GroupId = group.Id, UnitId = unit.Id, Name = "Xăng E10", Vcf = 1m });
        Assert.True(added.Ok, added.Message);

        var imported = app.System.SaveSlip(new SlipRequest
        {
            IsExport = false,
            DocumentDate = new DateTime(2026, 7, 10),
            WarehouseId = SeedIds.WhMain,
            Nature = "Mua đấu thầu",
            Mission = "Nhập hiện vật",
            Lines = [new SlipLineInput { ItemId = SeedIds.ItemRon95, ItemName = "Xăng RON 95", ObservedQuantity = 4m, ActualQuantity = 4m, UnitPrice = 15000m, Vcf = 1m }],
            Fields = [new FieldInput { FieldId = SeedIds.FieldInvoice, Value = "HD-NXT" }]
        });
        Assert.True(imported.Ok, imported.Message);
        Assert.True(app.System.SaveImport(new ImportRequest
        {
            DocumentDate = new DateTime(2026, 7, 16),
            WarehouseId = SeedIds.WhMain,
            ItemId = added.Id!.Value,
            ItemName = "Xăng E10",
            Vcf = 1m,
            UnitPrice = 16000m,
            InputQuantity = 6m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldInvoice, Value = "HD-E10" }]
        }).Ok);
        var diesel = app.System.SaveImport(new ImportRequest
        {
            DocumentDate = new DateTime(2026, 7, 18),
            WarehouseId = SeedIds.WhMain,
            ItemId = SeedIds.ItemDo,
            ItemName = "Dầu DO 0,05S",
            Vcf = 1m,
            UnitPrice = 12000m,
            InputQuantity = 3m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldInvoice, Value = "HD-DO" }]
        });
        Assert.True(diesel.Ok, diesel.Message);
        var lot = app.System.GetLots(SeedIds.WhMain).Single(x => x.ItemName == "Xăng RON 95").LotId;
        var vehicle = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Vehicle,
            ConsumerId = SeedIds.Vehicle,
            Distance = 12.5m,
            Norm = 0.1m,
            DocumentDate = new DateTime(2026, 7, 20),
            WarehouseId = SeedIds.WhMain,
            Nature = "Xuất xe",
            Mission = "Tuần tra",
            Lines = [new SlipLineInput { LotId = lot, Vcf = 1m, ObservedQuantity = 1m, ActualQuantity = 1m, UnitPrice = 1m }],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Lái xe" }]
        });
        Assert.True(vehicle.Ok, vehicle.Message);

        var sheet = app.System.GetNxt(2026, 3, [SeedIds.WhMain]);
        Assert.Equal(["Dầu DO 0,05S", "Xăng E10", "Xăng RON 95", "Xăng"], sheet.Columns.Select(x => x.Title));
        Assert.Equal([false, false, false, true], sheet.Columns.Select(x => x.IsGroupTotal));
        Assert.DoesNotContain(sheet.Columns, x => x.Title == "Dầu");
        Assert.Empty(app.System.GetNxt(2026, 3, []).Rows);

        var opening = sheet.Rows[0];
        Assert.Equal(NxtRowKind.Opening, opening.Kind);
        Assert.Null(opening.DocumentId);
        Assert.Equal(10m, At(sheet, opening, "Xăng RON 95").Balance);
        Assert.Null(At(sheet, opening, "Xăng RON 95").In);
        Assert.Equal(10m, At(sheet, opening, "Xăng").Balance);

        var slips = sheet.Rows.Where(x => x.Kind == NxtRowKind.Slip).ToList();
        Assert.Equal(4, slips.Count);
        Assert.All(slips.Where(x => x.DocumentKind == DocumentKind.Import), x => Assert.Equal(NxtVoucherColumn.N, x.VoucherColumn));
        var buy = slips[0];
        Assert.Equal("Mua đấu thầu", buy.Description);
        Assert.Equal(NxtVoucherColumn.N, buy.VoucherColumn);
        Assert.Equal("Nhập hiện vật", buy.Mission);
        Assert.Equal(new DateTime(2026, 7, 10), buy.Date);
        Assert.Equal(4m, At(sheet, buy, "Xăng RON 95").In);
        Assert.Equal(14m, At(sheet, buy, "Xăng RON 95").Balance);
        Assert.Equal(4m, At(sheet, buy, "Xăng").In);
        Assert.Equal(14m, At(sheet, buy, "Xăng").Balance);

        var trip = slips.Single(x => x.Description == "Xuất xe");
        Assert.Equal(NxtVoucherColumn.Xx, trip.VoucherColumn); // xuất có xăng → XX
        Assert.Equal(12.5m, trip.Kilometers);
        Assert.Equal("Tuần tra", trip.Mission);
        Assert.Equal(1m, At(sheet, trip, "Xăng RON 95").Out);
        Assert.Equal(13m, At(sheet, trip, "Xăng RON 95").Balance);
        Assert.Equal(19m, At(sheet, trip, "Xăng").Balance);

        var cong = sheet.Rows.Single(x => x.Kind == NxtRowKind.Period);
        Assert.Equal("Cộng mang sang", cong.Description);
        Assert.DoesNotContain(sheet.Rows, x => x.Kind == NxtRowKind.Closing);
        Assert.Equal(4m, At(sheet, cong, "Xăng RON 95").In);
        Assert.Equal(1m, At(sheet, cong, "Xăng RON 95").Out);
        Assert.Equal(13m, At(sheet, cong, "Xăng RON 95").Balance);
        Assert.Equal(10m, At(sheet, cong, "Xăng").In);
        Assert.Equal(1m, At(sheet, cong, "Xăng").Out);
        Assert.Equal(6m, At(sheet, cong, "Xăng E10").Balance);
        Assert.Equal(19m, At(sheet, cong, "Xăng").Balance);
        Assert.Equal(3m, At(sheet, cong, "Dầu DO 0,05S").Balance);

        Assert.True(app.System.Void(diesel.Id!.Value).Ok);
        var afterVoid = app.System.GetNxt(2026, 3, [SeedIds.WhMain]);
        Assert.DoesNotContain(afterVoid.Columns, x => x.Title == "Dầu DO 0,05S");
        Assert.DoesNotContain(afterVoid.Rows, x => x.DocumentId == diesel.Id);
    }

    [Fact]
    public void ListLocationsWithQuarterBookActivity_detects_inbound_transfers()
    {
        using var app = TestApp.Create();
        Assert.DoesNotContain(SeedIds.Machine, app.System.ListLocationsWithQuarterBookActivity(new DateTime(2026, 9, 30)));

        Assert.True(app.System.SaveImport(Import(50m, 20000m, vcf: 1m)).Ok);
        var lot = app.System.GetLots(SeedIds.WhMain).Single(x => x.UnitPrice == 20000m).LotId;
        Assert.True(app.System.SaveTransfer(new TransferRequest
        {
            DocumentDate = new DateTime(2026, 8, 10),
            SourceWarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.Machine,
            LotId = lot,
            ActualQuantity = 10m,
            Vcf = 1m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Máy" }]
        }).Ok);

        Assert.Contains(SeedIds.Machine, app.System.ListLocationsWithQuarterBookActivity(new DateTime(2026, 9, 30)));
        Assert.DoesNotContain(SeedIds.Machine, app.System.ListLocationsWithQuarterBookActivity(new DateTime(2026, 12, 31)));
    }

    [Fact]
    public void Nxt_book_counts_opening_slips_in_the_balance_without_a_row()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 8, 1),
            WarehouseId = SeedIds.WhMain,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 15000m,
            ActualQuantity = 8m
        }).Ok);

        var sheet = app.System.GetNxt(2026, 3, [SeedIds.WhMain]);
        Assert.DoesNotContain(sheet.Rows, x => x.Kind == NxtRowKind.Slip);
        Assert.Equal(8m, At(sheet, sheet.Rows[0], "Xăng RON 95").Balance);
        Assert.Equal(8m, At(sheet, sheet.Rows[^1], "Xăng RON 95").Balance);
    }

    [Fact]
    public void Nxt_book_combines_ticked_warehouses_and_nets_an_internal_transfer()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 6, 1),
            WarehouseId = SeedIds.WhMain,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 15000m,
            ActualQuantity = 10m
        }).Ok);
        var lot = OnlyLot(app.System);
        var moved = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            DocumentDate = new DateTime(2026, 8, 4),
            WarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.WhAux,
            Nature = "Điều nội bộ",
            Lines = [new SlipLineInput { LotId = lot, ObservedQuantity = 3m, ActualQuantity = 3m, UnitPrice = 15000m, Vcf = 1m }],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }]
        });
        Assert.True(moved.Ok, moved.Message);

        var both = app.System.GetNxt(2026, 3, [SeedIds.WhMain, SeedIds.WhAux]);
        var slip = Assert.Single(both.Rows, x => x.Kind == NxtRowKind.Slip);
        Assert.Equal("Điều nội bộ", slip.Description);
        Assert.Equal(3m, At(both, slip, "Xăng RON 95").Out);
        Assert.Equal(3m, At(both, slip, "Xăng RON 95").In);
        Assert.Equal(10m, At(both, slip, "Xăng RON 95").Balance);
        Assert.Equal(10m, At(both, both.Rows[^1], "Xăng RON 95").Balance);

        var source = app.System.GetNxt(2026, 3, [SeedIds.WhMain]);
        var sourceSlip = Assert.Single(source.Rows, x => x.Kind == NxtRowKind.Slip);
        Assert.Equal(3m, At(source, sourceSlip, "Xăng RON 95").Out);
        Assert.Null(At(source, sourceSlip, "Xăng RON 95").In);
        Assert.Equal(7m, At(source, sourceSlip, "Xăng RON 95").Balance);

        var destination = app.System.GetNxt(2026, 3, [SeedIds.WhAux]);
        var destinationSlip = Assert.Single(destination.Rows, x => x.Kind == NxtRowKind.Slip);
        Assert.Equal(3m, At(destination, destinationSlip, "Xăng RON 95").In);
        Assert.Null(At(destination, destinationSlip, "Xăng RON 95").Out);
        Assert.Equal(3m, At(destination, destination.Rows[^1], "Xăng RON 95").Balance);
    }

    [Fact]
    public void Nxt_shows_kilometers_from_vehicle_transfer_distance_or_slip_text()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(50m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        var vehicle = app.System.GetConsumers().Single(x => x.Id == SeedIds.Vehicle);
        var main = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhMain);

        var byDistance = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            ConsumerId = vehicle.Id,
            Distance = 35m,
            Norm = 0.25m,
            ManualQuantity = true,
            DestinationWarehouseId = vehicle.Id,
            DestinationWarehouseName = vehicle.Name,
            DocumentDate = new DateTime(2026, 8, 5),
            FormNumber = "DC-KM-01",
            WarehouseId = main.Id,
            WarehouseName = main.Name,
            WarehouseTypeName = main.TypeName,
            Nature = "ĐC xe có Distance",
            Lines = [new SlipLineInput { LotId = lot, ObservedQuantity = 5m, ActualQuantity = 5m, UnitPrice = 20000m, Vcf = 1m }],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Lái xe" }]
        });
        Assert.True(byDistance.Ok, byDistance.Message);

        var byText = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            DestinationWarehouseId = vehicle.Id,
            DestinationWarehouseName = vehicle.Name,
            DocumentDate = new DateTime(2026, 8, 12),
            FormNumber = "DC-KM-02",
            WarehouseId = main.Id,
            WarehouseName = main.Name,
            WarehouseTypeName = main.TypeName,
            Nature = "ĐC xe có Số km",
            Kilometers = "48",
            ManualQuantity = true,
            Lines = [new SlipLineInput { LotId = lot, ObservedQuantity = 4m, ActualQuantity = 4m, UnitPrice = 20000m, Vcf = 1m }],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Lái xe" }]
        });
        Assert.True(byText.Ok, byText.Message);
        Assert.Equal(48m, app.System.GetDocument(byText.Id!.Value)!.Distance);

        var sheet = app.System.GetNxt(2026, 3, [SeedIds.WhMain]);
        var rowDistance = Assert.Single(sheet.Rows, x => x.Kind == NxtRowKind.Slip && x.Description == "ĐC xe có Distance");
        var rowText = Assert.Single(sheet.Rows, x => x.Kind == NxtRowKind.Slip && x.Description == "ĐC xe có Số km");
        Assert.Equal(35m, rowDistance.Kilometers);
        Assert.Equal(48m, rowText.Kilometers);
    }

    [Fact]
    public void Nxt_total_lists_items_by_group_and_ignores_internal_transfer()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 6, 1),
            WarehouseId = SeedIds.WhMain,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 15000m,
            ActualQuantity = 10m
        }).Ok);
        var lot = OnlyLot(app.System);
        Assert.True(app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            DocumentDate = new DateTime(2026, 8, 4),
            WarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.WhAux,
            Nature = "Điều nội bộ",
            Lines = [new SlipLineInput { LotId = lot, ObservedQuantity = 3m, ActualQuantity = 3m, UnitPrice = 15000m, Vcf = 1m }],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }]
        }).Ok);
        Assert.True(app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Retail,
            DocumentDate = new DateTime(2026, 8, 10),
            WarehouseId = SeedIds.WhMain,
            Nature = "Xuất lẻ",
            Lines = [new SlipLineInput { LotId = lot, ObservedQuantity = 2m, ActualQuantity = 2m, UnitPrice = 15000m, Vcf = 1m }],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }]
        }).Ok);
        var bought = app.System.SaveImport(new ImportRequest
        {
            DocumentDate = new DateTime(2026, 8, 12),
            WarehouseId = SeedIds.WhMain,
            ItemId = SeedIds.ItemRon95,
            ItemName = "Xăng RON 95",
            Vcf = 1m,
            UnitPrice = 15000m,
            InputQuantity = 5m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldInvoice, Value = "HD-1" }]
        });
        Assert.True(bought.Ok, bought.Message);

        var ids = app.System.GetWarehouses().Select(x => x.Id).ToList();
        var total = app.System.GetNxtTotal(2026, 3, ids);
        var row = Assert.Single(total.Rows, x => x.ItemName == "Xăng RON 95");
        Assert.Equal("Xăng", row.GroupName);
        Assert.Equal(15000L, row.UnitPrice);
        Assert.Equal(10m, row.Opening);
        Assert.Equal(5m, row.In);
        Assert.Equal(2m, row.Out);
        Assert.Equal(13m, row.Closing);
        Assert.DoesNotContain(total.Rows, x => x.IsGroupTotal);

        // NXT từng kho: ĐC 3L Main→Aux tính xuất ở Main, nhập ở Aux.
        var main = app.System.GetNxtWarehouseTotal(2026, 3, SeedIds.WhMain);
        var mainRow = Assert.Single(main.Rows, x => x.ItemName == "Xăng RON 95");
        Assert.Equal(10m, mainRow.Opening);
        Assert.Equal(5m, mainRow.In);
        Assert.Equal(5m, mainRow.Out); // 3 ĐC + 2 bán lẻ
        Assert.Equal(10m, mainRow.Closing);

        var aux = app.System.GetNxtWarehouseTotal(2026, 3, SeedIds.WhAux);
        var auxRow = Assert.Single(aux.Rows, x => x.ItemName == "Xăng RON 95");
        Assert.Equal(0m, auxRow.Opening);
        Assert.Equal(3m, auxRow.In);
        Assert.Equal(0m, auxRow.Out);
        Assert.Equal(3m, auxRow.Closing);
    }

    [Fact]
    public void Nxt_warehouse_counts_transfer_in_out_consumption_and_retail()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(40m, 20000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        Assert.True(app.System.SaveTransfer(Transfer(lot, 15m, SeedIds.WhMain, SeedIds.Machine)).Ok);
        Assert.True(app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Retail,
            DocumentDate = new DateTime(2026, 9, 10),
            WarehouseId = SeedIds.WhMain,
            Nature = "Xuất lẻ",
            Lines = [new SlipLineInput { LotId = lot, ObservedQuantity = 4m, ActualQuantity = 4m, UnitPrice = 20000m, Vcf = 1m }],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }]
        }).Ok);
        Assert.True(app.System.SaveConsumption(new ConsumptionRequest
        {
            DocumentDate = new DateTime(2026, 9, 12),
            ConsumerId = SeedIds.Machine,
            ConsumerType = ConsumerType.Machine,
            WarehouseId = SeedIds.Machine,
            LotId = app.System.GetLots(SeedIds.Machine).Single().LotId,
            DirectActual = 6m,
            Vcf = 1m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Máy" }]
        }).Ok);

        var main = Assert.Single(app.System.GetNxtWarehouseTotal(2026, 3, SeedIds.WhMain).Rows);
        Assert.Equal(40m, main.Opening + main.In); // PN trong quý → In (không Opening)
        Assert.Equal(40m, main.In);
        Assert.Equal(19m, main.Out); // 15 ĐC ra + 4 xuất lẻ
        Assert.Equal(21m, main.Closing);

        var machine = Assert.Single(app.System.GetNxtWarehouseTotal(2026, 3, SeedIds.Machine).Rows);
        Assert.Equal(15m, machine.In); // ĐC vào
        Assert.Equal(6m, machine.Out); // tiêu thụ
        Assert.Equal(9m, machine.Closing);
    }

    [Fact]
    public void Nxt_warehouse_closing_matches_live_stock_per_lot()
    {
        using var app = TestApp.Create();
        // Hai lô khác đơn giá trên cùng kho + ĐC một phần sang máy.
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 6, 1),
            WarehouseId = SeedIds.WhMain,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 18000m,
            ActualQuantity = 12m
        }).Ok);
        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 6, 1),
            WarehouseId = SeedIds.WhMain,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 22000m,
            ActualQuantity = 8m
        }).Ok);
        var cheap = app.System.GetLots(SeedIds.WhMain).Single(x => x.UnitPrice == 18000).LotId;
        Assert.True(app.System.SaveTransfer(new TransferRequest
        {
            DocumentDate = new DateTime(2026, 8, 15),
            SourceWarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.Machine,
            LotId = cheap,
            ActualQuantity = 5m,
            Vcf = 1m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Máy" }]
        }).Ok);
        Assert.True(app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Retail,
            DocumentDate = new DateTime(2026, 9, 1),
            WarehouseId = SeedIds.WhMain,
            Nature = "Xuất lẻ",
            Lines =
            [
                new SlipLineInput
                {
                    LotId = app.System.GetLots(SeedIds.WhMain).Single(x => x.UnitPrice == 22000).LotId,
                    ObservedQuantity = 3m,
                    ActualQuantity = 3m,
                    UnitPrice = 22000m,
                    Vcf = 1m
                }
            ],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }]
        }).Ok);

        var sheet = app.System.GetNxtWarehouseTotal(2026, 3, SeedIds.WhMain);
        Assert.Equal(2, sheet.Rows.Count);
        foreach (var row in sheet.Rows)
        {
            var stock = Stock(app.System, row.ItemName, row.UnitPrice, SeedIds.WhMain);
            Assert.Equal(stock, row.Closing);
            Assert.Equal(QuantityMath.Whole(row.Opening + row.In - row.Out), row.Closing);
        }

        var cheapRow = Assert.Single(sheet.Rows, x => x.UnitPrice == 18000);
        Assert.Equal(12m, cheapRow.Opening);
        Assert.Equal(0m, cheapRow.In);
        Assert.Equal(5m, cheapRow.Out);
        Assert.Equal(7m, cheapRow.Closing);

        var dearRow = Assert.Single(sheet.Rows, x => x.UnitPrice == 22000);
        Assert.Equal(8m, dearRow.Opening);
        Assert.Equal(0m, dearRow.In);
        Assert.Equal(3m, dearRow.Out);
        Assert.Equal(5m, dearRow.Closing);

        var machineSheet = app.System.GetNxtWarehouseTotal(2026, 3, SeedIds.Machine);
        var machineRow = Assert.Single(machineSheet.Rows);
        Assert.Equal(0m, machineRow.Opening);
        Assert.Equal(5m, machineRow.In);
        Assert.Equal(0m, machineRow.Out);
        Assert.Equal(5m, machineRow.Closing);
        Assert.Equal(Stock(app.System, machineRow.ItemName, machineRow.UnitPrice, SeedIds.Machine), machineRow.Closing);
    }

    private static NxtCell At(NxtSheet sheet, NxtRow row, string title)
    {
        var index = sheet.Columns.ToList().FindIndex(x => x.Title == title);
        Assert.True(index >= 0, title);
        return row.Cells[index];
    }

    private static decimal Stock(FuelSystem system, string item, decimal price, Guid warehouse) =>
        system.ListStock(new StockFilter(warehouse, null, item, price, true)).Sum(x => x.Quantity);

    private static Guid OnlyLot(FuelSystem system) =>
        Assert.Single(system.GetLots(SeedIds.WhMain)).LotId;

    private static OpeningRequest Open(decimal actual, decimal price) => new()
    {
        DocumentDate = new DateTime(2026, 9, 1),
        WarehouseId = SeedIds.WhMain,
        ItemId = SeedIds.ItemRon95,
        UnitPrice = price,
        ActualQuantity = actual
    };

    [Fact]
    public void Vehicle_slip_posts_distance_times_norm_on_the_chosen_lot()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(10m, 15000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        var saved = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Vehicle,
            ConsumerId = SeedIds.Vehicle,
            Distance = 10m,
            Norm = 0.25m,
            DocumentDate = new DateTime(2026, 6, 30),
            WarehouseId = SeedIds.WhMain,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Lái xe" }],
            Lines = [new SlipLineInput { LotId = lot, Vcf = 1m, ObservedQuantity = 1m, ActualQuantity = 1m, UnitPrice = 1m }]
        });
        Assert.True(saved.Ok, saved.Message);
        var doc = app.System.GetDocument(saved.Id!.Value)!;
        Assert.Equal(DocumentKind.Consumption, doc.Kind);
        Assert.Equal(3m, doc.ActualQuantity);
        Assert.Equal(0.25m, doc.Norm);
        Assert.Equal(10m, doc.Distance);
        Assert.Equal(7m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhMain));
    }

    [Fact]
    public void Vehicle_slip_keeps_manual_lines_after_the_norm_line()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(30m, 15000m, vcf: 1m)).Ok);
        var lot = OnlyLot(app.System);
        var saved = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Vehicle,
            ConsumerId = SeedIds.Vehicle,
            Distance = 40m,
            Norm = 0.25m,
            DocumentDate = new DateTime(2026, 6, 30),
            WarehouseId = SeedIds.WhMain,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Lái xe" }],
            Lines =
            [
                new SlipLineInput { LotId = lot, Vcf = 1m, ObservedQuantity = 1m, ActualQuantity = 1m, UnitPrice = 1m },
                new SlipLineInput { LotId = lot, Vcf = 0.8m, ObservedQuantity = 12m, ActualQuantity = 10m, UnitPrice = 1m }
            ]
        });
        Assert.True(saved.Ok, saved.Message);
        var doc = app.System.GetDocument(saved.Id!.Value)!;
        Assert.Equal(2, doc.Lines.Count);
        Assert.Equal(10m, doc.Lines[0].ActualQuantity);
        Assert.Equal(10m, doc.Lines[0].Quantity);
        Assert.Equal(150_000m, doc.Lines[0].Amount);
        Assert.Equal(10m, doc.Lines[1].ActualQuantity);
        Assert.Equal(12m, doc.Lines[1].Quantity);
        Assert.Equal(150_000m, doc.Lines[1].Amount);
        Assert.Equal(20m, doc.ActualQuantity);
        Assert.Equal(300_000m, doc.Amount);
        Assert.Equal(10m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhMain));
    }

    [Fact]
    public void Auxiliary_sheet_posts_on_auxiliary_warehouses_and_rolls_back()
    {
        using var app = TestApp.Create();
        var main = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhMain);
        var aux = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhAux);
        Assert.True(app.System.SaveOpeningSheet(new OpeningSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 1),
            Cells = [OpeningCell(main, 10m), OpeningCell(aux, 4m)]
        }).Ok);

        var rejected = app.System.SaveAuxiliarySheet(new AuxiliarySheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 6),
            Cells =
            [
                new AuxiliaryCellRequest
                {
                    DocumentDate = new DateTime(2026, 9, 6),
                    WarehouseId = main.Id,
                    WarehouseName = main.Name,
                    WarehouseTypeName = main.TypeName,
                    ItemName = "Xăng RON 95",
                    UnitPrice = 15000m,
                    ActualQuantity = 1m,
                    Vcf = 0.99m
                }
            ]
        });
        Assert.False(rejected.Ok);
        Assert.Equal(10m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhMain));
        Assert.Equal(4m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhAux));

        var saved = app.System.SaveAuxiliarySheet(new AuxiliarySheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 6),
            Cells =
            [
                new AuxiliaryCellRequest
                {
                    DocumentDate = new DateTime(2026, 9, 6),
                    WarehouseId = aux.Id,
                    WarehouseName = aux.Name,
                    WarehouseTypeName = aux.TypeName,
                    ItemName = "Xăng RON 95",
                    UnitPrice = 15000m,
                    ActualQuantity = 1.5m,
                    Vcf = 0.99m
                }
            ]
        });
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal(2m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhAux));
        Assert.Equal(DocumentKind.Auxiliary, app.System.GetDocument(saved.Id!.Value)!.Kind);

        var cleared = app.System.SaveAuxiliarySheet(new AuxiliarySheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 6),
            VoidIds = [saved.Id!.Value]
        });
        Assert.True(cleared.Ok, cleared.Message);
        Assert.Equal(4m, Stock(app.System, "Xăng RON 95", 15000m, SeedIds.WhAux));
    }

    private static ConsumptionCellRequest ConsumeCell(WarehouseRow warehouse, decimal actual, Guid? documentId = null) => new()
    {
        DocumentId = documentId,
        DocumentDate = new DateTime(2026, 9, 30),
        ConsumerId = SeedIds.Machine,
        ConsumerType = ConsumerType.Machine,
        WarehouseId = warehouse.Id,
        WarehouseName = warehouse.Name,
        WarehouseTypeName = warehouse.TypeName,
        ItemName = "Xăng RON 95",
        UnitPrice = 15000m,
        ActualQuantity = actual,
        Vcf = 0.99m,
        Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Tổ máy" }]
    };

    private static OpeningRequest OpeningCell(WarehouseRow warehouse, decimal actual) => new()
    {
        DocumentDate = new DateTime(2026, 9, 1),
        WarehouseId = warehouse.Id,
        WarehouseName = warehouse.Name,
        WarehouseTypeName = warehouse.TypeName,
        ItemId = SeedIds.ItemRon95,
        ItemName = "Xăng RON 95",
        UnitPrice = 15000m,
        ActualQuantity = actual
    };

    private static ImportRequest Import(decimal qty, decimal price, decimal vcf, decimal? amount = null, string invoice = "HD-1", Guid? documentId = null, Guid? addSample = null, Guid? lotTypeId = null) => new()
    {
        DocumentId = documentId,
        DocumentDate = new DateTime(2026, 9, 2),
        WarehouseId = SeedIds.WhMain,
        ItemId = SeedIds.ItemRon95,
        Vcf = vcf,
        UnitPrice = price,
        LotTypeId = lotTypeId,
        InputQuantity = qty,
        Amount = amount,
        AddToSampleSetId = addSample,
        Fields = invoice.Length == 0 ? [] : [new FieldInput { FieldId = SeedIds.FieldInvoice, Value = invoice }]
    };

    private static ConsumptionRequest ConsumeMachine(Guid lot, decimal actual) => new()
    {
        DocumentDate = new DateTime(2026, 9, 3),
        ConsumerId = SeedIds.Machine,
        ConsumerType = ConsumerType.Machine,
        WarehouseId = SeedIds.WhMain,
        LotId = lot,
        DirectActual = actual,
        Vcf = 0.99m,
        Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Tổ máy" }]
    };

    private static ConsumptionRequest ConsumeVehicle(Guid lot, decimal distance, decimal norm) => new()
    {
        DocumentDate = new DateTime(2026, 9, 3),
        ConsumerId = SeedIds.Vehicle,
        ConsumerType = ConsumerType.Vehicle,
        WarehouseId = SeedIds.WhMain,
        LotId = lot,
        Distance = distance,
        Norm = norm,
        Vcf = 0.99m,
        Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Lái xe" }]
    };

    private static ConsumptionRequest ConsumeOther(Guid lot, decimal actual, Guid? consumerId = null) => new()
    {
        DocumentDate = new DateTime(2026, 9, 3),
        ConsumerId = consumerId ?? SeedIds.Other,
        ConsumerType = ConsumerType.Other,
        WarehouseId = SeedIds.WhMain,
        LotId = lot,
        DirectActual = actual,
        Vcf = 0.99m,
        Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Công trình" }]
    };

    private static TransferRequest Transfer(Guid lot, decimal actual, Guid? source = null, Guid? destination = null) => new()
    {
        DocumentDate = new DateTime(2026, 9, 4),
        SourceWarehouseId = source ?? SeedIds.WhMain,
        DestinationWarehouseId = destination ?? SeedIds.WhAux,
        LotId = lot,
        ActualQuantity = actual,
        Vcf = 0.99m,
        Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }]
    };

    private static AuxiliaryRequest Aux(Guid lot, decimal actual, Guid warehouse) => new()
    {
        DocumentDate = new DateTime(2026, 9, 5),
        WarehouseId = warehouse,
        LotId = lot,
        ActualQuantity = actual,
        Vcf = 0.99m,
        Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Bãi" }]
    };

    [Fact]
    public void Owned_export_sample_stays_private_to_one_object()
    {
        using var app = TestApp.Create();
        var warehouse = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhMain);
        var otherId = app.System.GetWarehouses().FirstOrDefault(x => x.Id != SeedIds.WhMain && !x.IsConsumerLocation)?.Id;
        if (otherId is null)
        {
            var createdAux = app.System.SaveWarehouse(new WarehouseEdit
            {
                Code = "PHU-TEST",
                Name = "Kho phụ kiểm thử",
                Type = WarehouseType.Auxiliary
            });
            Assert.True(createdAux.Ok, createdAux.Message);
            otherId = createdAux.Id;
        }

        var other = app.System.GetWarehouses().Single(x => x.Id == otherId);
        var created = app.System.EnsureOwnedExportSample(true, warehouse.Id);
        Assert.True(created.Ok, created.Message);
        var again = app.System.EnsureOwnedExportSample(true, warehouse.Id);
        Assert.Equal(created.Id, again.Id);
        Assert.Equal(created.Id, app.System.GetWarehouses().Single(x => x.Id == warehouse.Id).DefaultExportSampleSetId);
        Assert.NotEqual(created.Id, app.System.GetWarehouses().Single(x => x.Id == other.Id).DefaultExportSampleSetId);

        var field = app.System.GetFields(DocumentFamily.Export).First(x => x.Name == "Đơn vị nhận");
        Assert.True(app.System.AddSampleValue(created.Id!.Value, field.Id, "Đội 1").Ok);
        var values = app.System.GetSampleValues(created.Id.Value);
        Assert.Contains(values, x => x.FieldName == "Đơn vị nhận" && x.Value == "Đội 1");
    }

    [Fact]
    public void Owned_sample_saves_one_value_per_field_and_offers_shared_export_choices()
    {
        using var app = TestApp.Create();
        var warehouse = app.System.GetWarehouses().Single(x => x.Id == SeedIds.WhMain);
        var created = app.System.EnsureOwnedExportSample(true, warehouse.Id);
        Assert.True(created.Ok, created.Message);
        var setId = created.Id!.Value;
        var field = app.System.GetFields(DocumentFamily.Export).First(x => x.Name == "Cơ quan");

        var choices = app.System.GetSharedExportValues();
        Assert.Contains(choices, x => x.FieldId == field.Id && x.Value == "BỘ ĐỘI BIÊN PHÒNG");

        var saved = app.System.SaveOwnedSample(setId, [(field.Id, "Đơn vị A")]);
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal("Đơn vị A", app.System.GetSampleValues(setId).Single(x => x.FieldId == field.Id).Value);
        Assert.DoesNotContain(app.System.GetSharedExportValues(), x => x.Value == "Đơn vị A");

        var vehicle = app.System.GetConsumers().Single(x => x.Id == SeedIds.Vehicle);
        Assert.Empty(app.System.GetOwnedExportDefaults(vehicle.Id));
        var owned = app.System.EnsureOwnedExportSample(false, vehicle.Id);
        Assert.True(owned.Ok, owned.Message);
        var receiver = app.System.GetFields(DocumentFamily.Export).First(x => x.Name == "Người nhận");
        Assert.True(app.System.SaveOwnedSample(owned.Id!.Value, [(receiver.Id, "Lái xe A")]).Ok);
        var defaults = app.System.GetOwnedExportDefaults(vehicle.Id);
        Assert.Equal("Lái xe A", defaults["Người nhận"]);
        Assert.DoesNotContain(defaults, x => x.Value == "U1CN Lê Tài" || x.Value == "Biên đội");

        var cleared = app.System.SaveOwnedSample(setId, [(field.Id, "  ")]);
        Assert.True(cleared.Ok, cleared.Message);
        Assert.DoesNotContain(app.System.GetSampleValues(setId), x => x.FieldId == field.Id);
    }

    [Fact]
    public void Extra_fields_on_slips_stay_hidden_until_turned_on()
    {
        using var app = TestApp.Create();
        Assert.False(app.System.GetExtraFieldsOnSlip());
        app.System.SetExtraFieldsOnSlip(true);
        Assert.True(app.System.GetExtraFieldsOnSlip());
        app.System.SetExtraFieldsOnSlip(false);
        Assert.False(app.System.GetExtraFieldsOnSlip());
    }

    [Fact]
    public void Warehouse_scope_defaults_to_xd_and_filters_warehouses_and_items()
    {
        using var app = TestApp.Create();
        Assert.Equal(WarehouseScope.Xd, app.System.GetWarehouseScope());
        var xd = app.System.GetWarehouses();
        Assert.Contains(xd, x => x.Id == SeedIds.WhMain);
        Assert.DoesNotContain(xd, x => x.Id == SeedIds.WhPtkt);
        Assert.Contains(app.System.GetGroups(), x => x.Name == "Xăng");
        Assert.DoesNotContain(app.System.GetGroups(), x => x.Name == "PTKT - VTXD");
        Assert.Contains(app.System.GetItems(), x => x.Name == "Xăng RON 95");
        Assert.DoesNotContain(app.System.GetItems(), x => x.Name == "Cột tra NL ĐT TATSUNO");

        var catalog = app.System.GetCatalogWarehouses();
        Assert.Contains(catalog, x => x.Id == SeedIds.WhMain && x.TypeName == "Kho XD");
        Assert.Contains(catalog, x => x.Id == SeedIds.WhPtkt && x.TypeName == "Kho PTKT-VTXD");

        app.System.SetWarehouseScope(WarehouseScope.Ptkt);
        Assert.Equal(WarehouseScope.Ptkt, app.System.GetWarehouseScope());
        var ptkt = app.System.GetWarehouses();
        Assert.Contains(ptkt, x => x.Id == SeedIds.WhPtkt);
        Assert.DoesNotContain(ptkt, x => x.Id == SeedIds.WhMain);
        Assert.DoesNotContain(ptkt, x => x.IsConsumerLocation);
        Assert.Contains(app.System.GetGroups(), x => x.Name == "PTKT - VTXD");
        Assert.DoesNotContain(app.System.GetGroups(), x => x.Name == "Xăng");
        Assert.Contains(app.System.GetItems(), x => x.Name == "Cột tra NL ĐT TATSUNO");
        Assert.DoesNotContain(app.System.GetItems(), x => x.Name == "Xăng RON 95");

        app.System.SetWarehouseScope(WarehouseScope.Xd);
        Assert.Equal(WarehouseScope.Xd, app.System.GetWarehouseScope());
    }

    [Fact]
    public void Transfer_rejects_cross_family_between_xd_and_ptkt()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveOpening(Open(100m, 20000m)).Ok);
        var lot = app.System.GetLots(SeedIds.WhMain).Single(x => x.ItemName == "Xăng RON 95" && x.UnitPrice == 20000);
        var blocked = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            DocumentDate = new DateTime(2026, 9, 1),
            FormNumber = "XD-PTKT-BLOCK",
            WarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.WhPtkt,
            DestinationWarehouseName = "Kho PTKT",
            Lines =
            [
                new SlipLineInput
                {
                    LotId = lot.LotId,
                    ItemName = lot.ItemName,
                    ObservedQuantity = 10m,
                    ActualQuantity = 10m,
                    UnitPrice = lot.UnitPrice,
                    Amount = 200_000m,
                    Vcf = 1m
                }
            ]
        });
        Assert.False(blocked.Ok);
        Assert.Contains("PTKT", blocked.Message);
    }

    [Fact]
    public void Export_lot_prefers_the_remembered_name_inside_the_group()
    {
        var group = Guid.NewGuid();
        var otherGroup = Guid.NewGuid();
        var preferred = Guid.NewGuid();
        var other = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        ItemRow Item(Guid id, Guid groupId, string name) =>
            new(id, groupId, "Xăng", Guid.NewGuid(), "lít", name, "MS", null, "", null, "", 1m, "");
        LotOption Lot(Guid itemId, string name, decimal qty) =>
            new(Guid.NewGuid(), itemId, name, 10000, SeedIds.LotTypeTx, "TX", qty, 1m, "");
        var items = new[]
        {
            Item(preferred, group, "RON 95"),
            Item(other, group, "E5"),
            Item(outsider, otherGroup, "DO")
        };
        var lots = new[]
        {
            Lot(other, "E5", 50),
            Lot(preferred, "RON 95", 10),
            Lot(outsider, "DO", 100)
        };

        Assert.Equal(preferred, ExportFuelChoice.Pick(lots, items, group, preferred)!.ItemId);
        Assert.Equal(other, ExportFuelChoice.Pick(lots, items, group, null)!.ItemId);
        Assert.Equal(outsider, ExportFuelChoice.Pick(lots, items, otherGroup, null)!.ItemId);
    }

    [Fact]
    public void Saving_a_manual_fuel_name_is_preferred_next_time()
    {
        using var app = TestApp.Create();
        var saved = app.System.SaveItem(new ItemEdit
        {
            GroupId = SeedIds.GroupFuel,
            UnitId = SeedIds.UnitLiter,
            Name = "Xăng E5",
            Vcf = 1m,
            ConversionRule = "tạm"
        });
        Assert.True(saved.Ok, saved.Message);
        var remembered = app.System.RememberPreferredFuel(SeedIds.Vehicle, saved.Id!.Value);
        Assert.True(remembered.Ok, remembered.Message);
        Assert.Equal(saved.Id, app.System.GetConsumers().Single(x => x.Id == SeedIds.Vehicle).DefaultItemId);

        var outside = app.System.SaveGroup(null, "Nhóm nhớ");
        Assert.True(outside.Ok, outside.Message);
        var diesel = app.System.SaveItem(new ItemEdit
        {
            GroupId = outside.Id!.Value,
            UnitId = SeedIds.UnitLiter,
            Name = "Dầu DO",
            Vcf = 1m,
            ConversionRule = "tạm"
        });
        Assert.False(app.System.RememberPreferredFuel(SeedIds.Vehicle, diesel.Id!.Value).Ok);
        Assert.Equal(saved.Id, app.System.GetConsumers().Single(x => x.Id == SeedIds.Vehicle).DefaultItemId);
    }

    [Fact]
    public void Deleting_a_group_removes_its_unused_items()
    {
        using var app = TestApp.Create();
        var created = app.System.SaveGroup(null, "Nhóm tạm");
        Assert.True(created.Ok, created.Message);
        var groupId = created.Id!.Value;
        var item = app.System.SaveItem(new ItemEdit
        {
            GroupId = groupId,
            UnitId = SeedIds.UnitLiter,
            Name = "Hàng tạm",
            Vcf = 1m,
            ConversionRule = "tạm"
        });
        Assert.True(item.Ok, item.Message);

        var removed = app.System.DeleteGroup(groupId);
        Assert.True(removed.Ok, removed.Message);
        Assert.DoesNotContain(app.System.GetGroups(), x => x.Id == groupId);
        Assert.DoesNotContain(app.System.GetItems(), x => x.Id == item.Id);

        var blocked = app.System.DeleteGroup(SeedIds.GroupFuel);
        Assert.False(blocked.Ok);
        Assert.Contains(app.System.GetGroups(), x => x.Id == SeedIds.GroupFuel);
    }

    [Fact]
    public void Machine_export_keeps_the_machine_and_retail_export_does_not()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(40m, 20000m, vcf: 1m)).Ok);
        var machine = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Issue,
            ConsumerId = SeedIds.Machine,
            DocumentDate = new DateTime(2026, 9, 1),
            FormNumber = "MAY-1",
            WarehouseId = SeedIds.WhMain,
            ReceiverPerson = "Máy bơm",
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Máy bơm" }],
            Lines = [new SlipLineInput { ItemId = SeedIds.ItemRon95, ItemName = "Xăng RON 95", ObservedQuantity = 10m, Vcf = 1m, UnitPrice = 20000m, ActualQuantity = 10m }]
        });
        Assert.True(machine.Ok, machine.Message);
        var saved = app.System.GetDocument(machine.Id!.Value)!;
        Assert.Equal(DocumentKind.Issue, saved.Kind);
        Assert.Equal(SeedIds.Machine, saved.ConsumerId);
        Assert.Equal("Máy", saved.ConsumerTypeName);
        Assert.Equal(30m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));

        var retail = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Retail,
            DocumentDate = new DateTime(2026, 9, 2),
            FormNumber = "LE-1",
            WarehouseId = SeedIds.WhMain,
            ReceiverPerson = "Khách lẻ",
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Khách lẻ" }],
            Lines = [new SlipLineInput { ItemId = SeedIds.ItemRon95, ItemName = "Xăng RON 95", ObservedQuantity = 5m, Vcf = 1m, UnitPrice = 20000m, ActualQuantity = 5m }]
        });
        Assert.True(retail.Ok, retail.Message);
        var loose = app.System.GetDocument(retail.Id!.Value)!;
        Assert.Equal(DocumentKind.Issue, loose.Kind);
        Assert.Null(loose.ConsumerId);
        Assert.Equal(25m, Stock(app.System, "Xăng RON 95", 20000m, SeedIds.WhMain));
    }

    [Fact]
    public void Auxiliary_warehouses_are_the_ships_and_fuel_uses_four_groups()
    {
        using var app = TestApp.Create();
        var warehouses = app.System.GetWarehouses();
        Assert.DoesNotContain(warehouses, x => x.Name == "Kho phụ bãi");
        Assert.Equal(SeedIds.WhAux, warehouses.Single(x => x.Name == "Tàu BP 27-19-01").Id);
        foreach (var name in new[] { "Tàu BP 27-01-01", "Tàu BP 27-05-01" })
            Assert.Contains(warehouses, x => x.Name == name && x.Type == WarehouseType.Auxiliary);
        Assert.Equal(3, warehouses.Count(x => x.Type == WarehouseType.Auxiliary && x.Name.StartsWith("Tàu BP", StringComparison.Ordinal)));
        Assert.All(warehouses.Where(x => x.Type == WarehouseType.Auxiliary), x => Assert.Null(x.DefaultImportSampleSetId));

        var consumers = app.System.GetConsumers();
        Assert.Equal(3, consumers.Count(x => x.Type == ConsumerType.Vehicle));
        Assert.Equal(3, consumers.Count(x => x.Type == ConsumerType.Machine));
        Assert.Equal(3, consumers.Count(x => x.Type == ConsumerType.Ship));
        Assert.All(consumers.Where(x => x.Type == ConsumerType.Vehicle), x => Assert.True(x.Norm is > 0));
        Assert.All(consumers.Where(x => x.Type == ConsumerType.Ship), x =>
        {
            Assert.Equal(ShipNormSlots.Labels.Length, x.NormFactors.Count);
            Assert.All(x.NormFactors, f => Assert.True(f.Value > 0));
        });

        var groups = app.System.GetGroups().Select(x => x.Name).ToList();
        Assert.Contains("Dầu", groups);
        Assert.Contains("Xăng", groups);
        Assert.Contains("Nhớt", groups);
        Assert.Contains("Mỡ", groups);
        Assert.DoesNotContain("Dầu mỡ", groups);
        Assert.Equal("Mỡ", app.System.GetItems().Single(x => x.Name == "Dầu mỡ").GroupName);
        Assert.Equal("Nhớt", app.System.GetItems().Single(x => x.Name == "Rimula R4X").GroupName);
        Assert.Equal("Xăng", app.System.GetItems().Single(x => x.Name == "Xăng RON 92").GroupName);
        Assert.Equal("Dầu", app.System.GetItems().Single(x => x.Name == "Dầu DO 0,05S").GroupName);
    }

    [Fact]
    public void Seed_creates_default_lot_types()
    {
        using var app = TestApp.Create();
        var types = app.System.GetLotTypes();
        Assert.Contains(types, x => x.Id == SeedIds.LotTypeTx && x.Code == "TX");
        Assert.Contains(types, x => x.Id == SeedIds.LotTypeSscd && x.Code == "SSCĐ");
        Assert.Contains(types, x => x.Id == SeedIds.LotTypeIuu && x.Code == "IUU");
    }

    [Fact]
    public void Same_name_price_different_lot_type_are_separate_lots()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(10m, 20000m, vcf: 1m, lotTypeId: SeedIds.LotTypeTx)).Ok);
        Assert.True(app.System.SaveImport(Import(7m, 20000m, vcf: 1m, lotTypeId: SeedIds.LotTypeSscd)).Ok);
        var lots = app.System.GetLots(SeedIds.WhMain).Where(x => x.ItemName == "Xăng RON 95" && x.UnitPrice == 20000).ToList();
        Assert.Equal(2, lots.Count);
        Assert.Equal(10m, lots.Single(x => x.LotTypeId == SeedIds.LotTypeTx).Quantity);
        Assert.Equal(7m, lots.Single(x => x.LotTypeId == SeedIds.LotTypeSscd).Quantity);
    }

    [Fact]
    public void Consumption_sheet_only_deducts_the_selected_lot_type()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(20m, 15000m, vcf: 1m, lotTypeId: SeedIds.LotTypeTx)).Ok);
        Assert.True(app.System.SaveImport(Import(20m, 15000m, vcf: 1m, lotTypeId: SeedIds.LotTypeSscd)).Ok);
        var machine = app.System.GetWarehouses().First(x => x.Id == SeedIds.Machine);
        Assert.True(app.System.SaveTransfer(Transfer(
            app.System.GetLots(SeedIds.WhMain).Single(x => x.LotTypeId == SeedIds.LotTypeTx).LotId,
            8m, SeedIds.WhMain, SeedIds.Machine)).Ok);
        Assert.True(app.System.SaveTransfer(Transfer(
            app.System.GetLots(SeedIds.WhMain).Single(x => x.LotTypeId == SeedIds.LotTypeSscd).LotId,
            5m, SeedIds.WhMain, SeedIds.Machine)).Ok);

        var saved = app.System.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            Cells =
            [
                new ConsumptionCellRequest
                {
                    DocumentDate = new DateTime(2026, 9, 30),
                    ConsumerId = SeedIds.Machine,
                    ConsumerType = ConsumerType.Machine,
                    WarehouseId = machine.Id,
                    WarehouseName = machine.Name,
                    WarehouseTypeName = machine.TypeName,
                    ItemName = "Xăng RON 95",
                    UnitPrice = 15000m,
                    LotTypeId = SeedIds.LotTypeSscd,
                    ActualQuantity = 5m,
                    Vcf = 1m
                }
            ]
        });
        Assert.True(saved.Ok, saved.Message);
        Assert.Equal(8m, StockByType(app.System, "Xăng RON 95", 15000m, SeedIds.LotTypeTx, SeedIds.Machine));
        Assert.Equal(0m, StockByType(app.System, "Xăng RON 95", 15000m, SeedIds.LotTypeSscd, SeedIds.Machine));
        Assert.Equal(12m, StockByType(app.System, "Xăng RON 95", 15000m, SeedIds.LotTypeTx, SeedIds.WhMain));
        Assert.Equal(15m, StockByType(app.System, "Xăng RON 95", 15000m, SeedIds.LotTypeSscd, SeedIds.WhMain));
    }

    [Fact]
    public void Transfer_change_destination_lot_type_is_blocked_until_feature_ready()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(30m, 18000m, vcf: 1m, lotTypeId: SeedIds.LotTypeTx)).Ok);
        var sourceLot = app.System.GetLots(SeedIds.WhMain).Single(x => x.LotTypeId == SeedIds.LotTypeTx && x.UnitPrice == 18000);
        var moved = app.System.SaveTransfer(new TransferRequest
        {
            DocumentDate = new DateTime(2026, 9, 10),
            SourceWarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.WhAux,
            LotId = sourceLot.LotId,
            DestinationLotTypeId = SeedIds.LotTypeIuu,
            ActualQuantity = 10m,
            Vcf = 1m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Kho phụ" }]
        });
        Assert.False(moved.Ok);
        Assert.Contains("chưa hoàn thiện", moved.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(30m, StockByType(app.System, "Xăng RON 95", 18000m, SeedIds.LotTypeTx, SeedIds.WhMain));
        Assert.Equal(0m, StockByType(app.System, "Xăng RON 95", 18000m, SeedIds.LotTypeIuu, SeedIds.WhAux));
    }

    [Fact]
    public void Lot_convert_moves_stock_tx_to_sscd_same_warehouse()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(30m, 18000m, vcf: 1m, lotTypeId: SeedIds.LotTypeTx)).Ok);
        var sourceLot = app.System.GetLots(SeedIds.WhMain).Single(x => x.LotTypeId == SeedIds.LotTypeTx && x.UnitPrice == 18000);
        var converted = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.LotConvert,
            DocumentDate = new DateTime(2026, 9, 12),
            WarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.WhMain,
            Nature = "Đổi loại lô",
            Lines =
            [
                new SlipLineInput
                {
                    LotId = sourceLot.LotId,
                    ObservedQuantity = 10m,
                    ActualQuantity = 10m,
                    UnitPrice = 18000m,
                    Vcf = 1m,
                    DestinationLotTypeId = SeedIds.LotTypeSscd
                }
            ],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }]
        });
        Assert.True(converted.Ok, converted.Message);
        Assert.Equal(20m, StockByType(app.System, "Xăng RON 95", 18000m, SeedIds.LotTypeTx, SeedIds.WhMain));
        Assert.Equal(10m, StockByType(app.System, "Xăng RON 95", 18000m, SeedIds.LotTypeSscd, SeedIds.WhMain));

        var nxt = app.System.GetNxt(2026, 3, [SeedIds.WhMain]);
        Assert.DoesNotContain(nxt.Rows, x => x.Kind == NxtRowKind.Slip && x.DocumentKind == DocumentKind.LotConvert);

        var total = app.System.GetNxtTotal(2026, 3, app.System.GetWarehouses().Select(x => x.Id).ToList(), NxtLotViewMode.All);
        var tx = Assert.Single(total.Rows, x => x.ItemName == "Xăng RON 95" && x.LotTypeId == SeedIds.LotTypeTx && !x.IsGroupTotal);
        var sscd = Assert.Single(total.Rows, x => x.ItemName == "Xăng RON 95" && x.LotTypeId == SeedIds.LotTypeSscd && !x.IsGroupTotal);
        Assert.Equal(0m, tx.Out);
        Assert.Equal(0m, sscd.In);
        Assert.Equal(20m, tx.Closing);
        Assert.Equal(10m, sscd.Closing);
        Assert.Contains("Tồn sau giảm", tx.Note, StringComparison.Ordinal);
        Assert.Contains("SSCĐ", tx.Note, StringComparison.Ordinal);
        Assert.Contains("Tồn sau tăng", sscd.Note, StringComparison.Ordinal);
        Assert.Contains("TX", sscd.Note, StringComparison.Ordinal);

        // NXT từng kho: cùng ghi chú đổi loại lô như NXT tổng.
        var wh = app.System.GetNxtWarehouseTotal(2026, 3, SeedIds.WhMain, NxtLotViewMode.All);
        var whTx = Assert.Single(wh.Rows, x => x.ItemName == "Xăng RON 95" && x.LotTypeId == SeedIds.LotTypeTx);
        var whSscd = Assert.Single(wh.Rows, x => x.ItemName == "Xăng RON 95" && x.LotTypeId == SeedIds.LotTypeSscd);
        Assert.Equal(0m, whTx.Out);
        Assert.Equal(0m, whSscd.In);
        Assert.Equal(20m, whTx.Closing);
        Assert.Equal(10m, whSscd.Closing);
        Assert.Contains("Tồn sau giảm", whTx.Note, StringComparison.Ordinal);
        Assert.Contains("SSCĐ", whTx.Note, StringComparison.Ordinal);
        Assert.Contains("Tồn sau tăng", whSscd.Note, StringComparison.Ordinal);
        Assert.Contains("TX", whSscd.Note, StringComparison.Ordinal);

        Assert.True(app.System.Void(converted.Id!.Value).Ok);
        Assert.Equal(30m, StockByType(app.System, "Xăng RON 95", 18000m, SeedIds.LotTypeTx, SeedIds.WhMain));
        Assert.Equal(0m, StockByType(app.System, "Xăng RON 95", 18000m, SeedIds.LotTypeSscd, SeedIds.WhMain));
    }

    [Fact]
    public void Lot_convert_rejects_iuu_and_same_type()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(10m, 18000m, vcf: 1m, lotTypeId: SeedIds.LotTypeTx)).Ok);
        var sourceLot = app.System.GetLots(SeedIds.WhMain).Single(x => x.LotTypeId == SeedIds.LotTypeTx && x.UnitPrice == 18000);
        var toIuu = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.LotConvert,
            DocumentDate = new DateTime(2026, 9, 12),
            WarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.WhMain,
            Lines =
            [
                new SlipLineInput
                {
                    LotId = sourceLot.LotId,
                    ObservedQuantity = 5m,
                    ActualQuantity = 5m,
                    UnitPrice = 18000m,
                    Vcf = 1m,
                    DestinationLotTypeId = SeedIds.LotTypeIuu
                }
            ],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }]
        });
        Assert.False(toIuu.Ok);
        Assert.Contains("TX và SSCĐ", toIuu.Message, StringComparison.OrdinalIgnoreCase);

        var same = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.LotConvert,
            DocumentDate = new DateTime(2026, 9, 12),
            WarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.WhMain,
            Lines =
            [
                new SlipLineInput
                {
                    LotId = sourceLot.LotId,
                    ObservedQuantity = 5m,
                    ActualQuantity = 5m,
                    UnitPrice = 18000m,
                    Vcf = 1m,
                    DestinationLotTypeId = SeedIds.LotTypeTx
                }
            ],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }]
        });
        Assert.False(same.Ok);
    }

    [Fact]
    public void Default_import_without_lot_type_uses_tx()
    {
        using var app = TestApp.Create();
        Assert.True(app.System.SaveImport(Import(5m, 12000m, vcf: 1m)).Ok);
        var lot = Assert.Single(app.System.GetLots(SeedIds.WhMain), x => x.UnitPrice == 12000);
        Assert.Equal(SeedIds.LotTypeTx, lot.LotTypeId);
        Assert.Equal("TX", lot.LotTypeCode);
    }

    [Fact]
    public void LotOrigin_catalog_contains_default_values()
    {
        using var app = TestApp.Create();
        var origins = app.System.GetLotOrigins();
        Assert.Contains(origins, x => x.Name == "Tự mua");
        Assert.Contains(origins, x => x.Name == "Trên cấp");
    }

    [Fact]
    public void Import_with_different_origins_creates_separate_lots_and_tracks_stock()
    {
        using var app = TestApp.Create();
        var wh = SeedIds.WhMain;
        var date = new DateTime(2026, 10, 1);

        // 1. Nhập lô Tự mua
        var import1 = app.System.SaveSlip(new SlipRequest
        {
            IsExport = false,
            DocumentDate = date,
            WarehouseId = wh,
            Lines =
            [
                new SlipLineInput
                {
                    ItemName = "Xăng Ron 95-III",
                    ObservedQuantity = 100m,
                    ActualQuantity = 100m,
                    UnitPrice = 15000m,
                    Amount = 1500000m,
                    Vcf = 1m,
                    LotTypeId = SeedIds.LotTypeTx,
                    Origin = "Tự mua"
                }
            ],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }]
        });
        Assert.True(import1.Ok);

        // 2. Nhập lô Trên cấp cùng mặt hàng, cùng đơn giá, cùng loại lô TX
        var import2 = app.System.SaveSlip(new SlipRequest
        {
            IsExport = false,
            DocumentDate = date,
            WarehouseId = wh,
            Lines =
            [
                new SlipLineInput
                {
                    ItemName = "Xăng Ron 95-III",
                    ObservedQuantity = 200m,
                    ActualQuantity = 200m,
                    UnitPrice = 15000m,
                    Amount = 3000000m,
                    Vcf = 1m,
                    LotTypeId = SeedIds.LotTypeTx,
                    Origin = "Trên cấp"
                }
            ],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }]
        });
        Assert.True(import2.Ok);

        // 3. Kiểm tra danh sách lô: có 2 lô riêng biệt với 2 nguồn gốc khác nhau
        var lots = app.System.GetLots(wh).Where(x => x.ItemName == "Xăng Ron 95-III" && x.UnitPrice == 15000m).ToList();
        Assert.Equal(2, lots.Count);
        var lotTuMua = Assert.Single(lots, x => x.Origin == "Tự mua");
        var lotTrenCap = Assert.Single(lots, x => x.Origin == "Trên cấp");
        Assert.Equal(100m, lotTuMua.Quantity);
        Assert.Equal(200m, lotTrenCap.Quantity);

        // 4. Xuất từ lô Trên cấp 40 lít
        var export = app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Retail,
            DocumentDate = date.AddDays(1),
            WarehouseId = wh,
            Lines =
            [
                new SlipLineInput
                {
                    LotId = lotTrenCap.LotId,
                    ItemName = "Xăng Ron 95-III",
                    ObservedQuantity = 40m,
                    ActualQuantity = 40m,
                    UnitPrice = 15000m,
                    Amount = 600000m,
                    Vcf = 1m,
                    LotTypeId = SeedIds.LotTypeTx,
                    Origin = "Trên cấp"
                }
            ],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Người nhận" }]
        });
        Assert.True(export.Ok);

        // 5. Kiểm tra tồn sau xuất: Tự mua vẫn 100, Trên cấp còn 160
        var lotsAfter = app.System.GetLots(wh).Where(x => x.ItemName == "Xăng Ron 95-III" && x.UnitPrice == 15000m).ToList();
        var lotTuMuaAfter = Assert.Single(lotsAfter, x => x.Origin == "Tự mua");
        var lotTrenCapAfter = Assert.Single(lotsAfter, x => x.Origin == "Trên cấp");
        Assert.Equal(100m, lotTuMuaAfter.Quantity);
        Assert.Equal(160m, lotTrenCapAfter.Quantity);

        // 6. Kiểm tra danh sách phiếu trên VoucherDesk có chứa trường Origin
        var deskImports = app.System.ListDesk(export: false, 2026, 4);
        Assert.Contains(deskImports, x => x.Origin == "Tự mua");
        Assert.Contains(deskImports, x => x.Origin == "Trên cấp");

        var deskExports = app.System.ListDesk(export: true, 2026, 4);
        Assert.Contains(deskExports, x => x.Origin == "Trên cấp");
    }

    [Fact]
    public void Opening_sheet_with_different_origins_creates_separate_lots()
    {
        using var app = TestApp.Create();
        var wh = SeedIds.WhMain;
        var date = new DateTime(2026, 1, 1);
        var item = app.System.GetItems().First();

        var result = app.System.SaveOpeningSheet(new OpeningSheetRequest
        {
            DocumentDate = date,
            Cells =
            [
                new OpeningRequest
                {
                    DocumentDate = date,
                    WarehouseId = wh,
                    ItemId = item.Id,
                    ItemName = item.Name,
                    UnitPrice = 20000m,
                    LotTypeId = SeedIds.LotTypeTx,
                    Origin = "Tự mua",
                    ActualQuantity = 50m
                },
                new OpeningRequest
                {
                    DocumentDate = date,
                    WarehouseId = wh,
                    ItemId = item.Id,
                    ItemName = item.Name,
                    UnitPrice = 20000m,
                    LotTypeId = SeedIds.LotTypeTx,
                    Origin = "Trên cấp",
                    ActualQuantity = 75m
                }
            ]
        });
        Assert.True(result.Ok);

        var lots = app.System.GetLots(wh).Where(x => x.ItemId == item.Id && x.UnitPrice == 20000m).ToList();
        Assert.Equal(2, lots.Count);
        var lot1 = Assert.Single(lots, x => x.Origin == "Tự mua");
        var lot2 = Assert.Single(lots, x => x.Origin == "Trên cấp");
        Assert.Equal(50m, lot1.Quantity);
        Assert.Equal(75m, lot2.Quantity);

        var headers = app.System.ListSheetHeaders(DocumentKind.Opening);
        Assert.Contains(headers, x => x.Origin == "Tự mua" && x.UnitPrice == 20000m);
        Assert.Contains(headers, x => x.Origin == "Trên cấp" && x.UnitPrice == 20000m);
    }

    [Fact]
    public void Nxt_total_breaks_down_by_main_machine_vehicle_ship_and_total()
    {
        using var app = TestApp.Create();
        var item = app.System.GetItems().First(x => x.Name == "Dầu DO 0,05S");

        // 1. Setup consumers for each category
        var machineWh = app.System.GetWarehouses().First(x => x.ConsumerTypeName == "Máy").Id;
        var vehicleWh = app.System.GetWarehouses().First(x => x.ConsumerTypeName == "Phương tiện").Id;
        var shipWh = app.System.GetWarehouses().First(x => x.ConsumerTypeName == "Tàu").Id;
        var mainWh = SeedIds.WhMain;

        // 2. Set Opening Stock: Main=100, Machine=20, Vehicle=30, Ship=50 -> Total Opening = 200
        var date = new DateTime(2026, 1, 1);
        Assert.True(app.System.SaveOpeningSheet(new OpeningSheetRequest
        {
            DocumentDate = date,
            Cells =
            [
                new OpeningRequest { DocumentDate = date, WarehouseId = mainWh, ItemId = item.Id, UnitPrice = 18000m, ActualQuantity = 100m, LotTypeId = SeedIds.LotTypeTx },
                new OpeningRequest { DocumentDate = date, WarehouseId = machineWh, ItemId = item.Id, UnitPrice = 18000m, ActualQuantity = 20m, LotTypeId = SeedIds.LotTypeTx },
                new OpeningRequest { DocumentDate = date, WarehouseId = vehicleWh, ItemId = item.Id, UnitPrice = 18000m, ActualQuantity = 30m, LotTypeId = SeedIds.LotTypeTx },
                new OpeningRequest { DocumentDate = date, WarehouseId = shipWh, ItemId = item.Id, UnitPrice = 18000m, ActualQuantity = 50m, LotTypeId = SeedIds.LotTypeTx },
            ]
        }).Ok);

        // 3. External Import to Main in Q3: 50 -> InMain += 50
        Assert.True(app.System.SaveImport(new ImportRequest
        {
            DocumentDate = new DateTime(2026, 7, 5),
            WarehouseId = mainWh,
            ItemId = item.Id,
            ItemName = item.Name,
            Vcf = 1m,
            UnitPrice = 18000m,
            InputQuantity = 50m,
            LotTypeId = SeedIds.LotTypeTx,
            Fields = [new FieldInput { FieldId = SeedIds.FieldInvoice, Value = "HD-IMPORT-Q3" }]
        }).Ok);

        // 4. Transfer in Q3: Main -> Ship 25 -> OutMain += 25, InShip += 25
        var mainLot = app.System.GetLots(mainWh).First(x => x.ItemId == item.Id && x.UnitPrice == 18000m).LotId;
        Assert.True(app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            DocumentDate = new DateTime(2026, 7, 10),
            WarehouseId = mainWh,
            DestinationWarehouseId = shipWh,
            Nature = "Cấp dầu tàu",
            Lines = [new SlipLineInput { LotId = mainLot, ObservedQuantity = 25m, ActualQuantity = 25m, UnitPrice = 18000m, Vcf = 1m }],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thuyền trưởng" }]
        }).Ok);

        // 5. Consumption in Q3: Vehicle consumes 10 -> OutVehicle += 10
        var vehLot = app.System.GetLots(vehicleWh).First(x => x.ItemId == item.Id && x.UnitPrice == 18000m).LotId;
        Assert.True(app.System.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Retail,
            DocumentDate = new DateTime(2026, 8, 15),
            WarehouseId = vehicleWh,
            Nature = "Xe chạy",
            Lines = [new SlipLineInput { LotId = vehLot, ObservedQuantity = 10m, ActualQuantity = 10m, UnitPrice = 18000m, Vcf = 1m }],
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Lái xe" }]
        }).Ok);

        // 6. Check GetNxtTotal
        var allWhIds = app.System.GetWarehouses().Select(x => x.Id).ToList();
        var totalSheet = app.System.GetNxtTotal(2026, 3, allWhIds);
        var row = Assert.Single(totalSheet.Rows, x => x.ItemName == item.Name && x.UnitPrice == 18000L);

        // Verify Tồn đầu
        Assert.Equal(100m, row.OpeningMain);
        Assert.Equal(20m, row.OpeningMachine);
        Assert.Equal(30m, row.OpeningVehicle);
        Assert.Equal(50m, row.OpeningShip);
        Assert.Equal(200m, row.OpeningTotal);

        // Verify Nhập
        Assert.Equal(50m, row.InMain);
        Assert.Equal(0m, row.InMachine);
        Assert.Equal(0m, row.InVehicle);
        Assert.Equal(25m, row.InShip); // Inbound transfer
        Assert.Equal(50m, row.InTotal); // External import only

        // Verify Xuất
        Assert.Equal(25m, row.OutMain); // Outbound transfer
        Assert.Equal(0m, row.OutMachine);
        Assert.Equal(10m, row.OutVehicle); // Consumption
        Assert.Equal(0m, row.OutShip);
        Assert.Equal(10m, row.OutTotal); // External export only

        // Verify Tồn sau
        // Main: 100 + 50 - 25 = 125
        Assert.Equal(125m, row.ClosingMain);
        // Machine: 20 + 0 - 0 = 20
        Assert.Equal(20m, row.ClosingMachine);
        // Vehicle: 30 + 0 - 10 = 20
        Assert.Equal(20m, row.ClosingVehicle);
        // Ship: 50 + 25 - 0 = 75
        Assert.Equal(75m, row.ClosingShip);
        // Total: 200 + 50 - 10 = 240 (also 125 + 20 + 20 + 75 = 240)
        Assert.Equal(240m, row.ClosingTotal);
    }

    private static decimal StockByType(FuelSystem system, string item, decimal price, Guid lotTypeId, Guid warehouse) =>
        system.ListStock(new StockFilter(warehouse, null, item, price, true))
            .Where(x => x.LotTypeId == lotTypeId)
            .Sum(x => x.Quantity);
}

internal sealed class TestApp : IDisposable
{
    public FuelSystem System { get; }
    private readonly string _dir;

    private TestApp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "appqlxd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        System = new FuelSystem(Path.Combine(_dir, "t.db"));
    }

    public static TestApp Create() => new();

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { /* tệp tạm */ }
    }
}
