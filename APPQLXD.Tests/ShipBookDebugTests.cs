using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Core.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace APPQLXD.Tests;

public sealed class ShipBookDebugTests(ITestOutputHelper output)
{
    [Fact]
    public void Debug_fifo()
    {
        using var app = TestApp.Create();
        var ship = app.System.GetConsumers().Single(x => x.Id == SeedIds.Ship);
        output.WriteLine($"ship={ship.Name} group={ship.DefaultGroupName}");
        foreach (var f in ship.NormFactors.OrderBy(x => x.SortOrder))
            output.WriteLine($"norm {f.Name}={f.Value}");

        var o1 = app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 6, 10),
            WarehouseId = SeedIds.Ship,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 18000m,
            ActualQuantity = 5m
        });
        output.WriteLine($"o1={o1.Ok} {o1.Message}");
        var o2 = app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 6, 20),
            WarehouseId = SeedIds.Ship,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 22000m,
            ActualQuantity = 8m
        });
        output.WriteLine($"o2={o2.Ok} {o2.Message}");
        foreach (var s in app.System.ListStock(new StockFilter(SeedIds.Ship, null, null, null, true)))
            output.WriteLine($"stock {s.ItemName} p={s.UnitPrice} q={s.Quantity} g={s.GroupName}");

        var book = app.System.GetShipQuarterBook(new DateTime(2026, 9, 30), SeedIds.Ship);
        output.WriteLine($"fuelGroup={book.FuelGroupName} opening={book.Rows[0].FuelBalance}");

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
                    ManualFuelOut = true,
                    FuelOutManual = 9m
                }
            ]
        });
        output.WriteLine($"save={saved.Ok} {saved.Message}");
        foreach (var s in app.System.ListStock(new StockFilter(SeedIds.Ship, null, null, null, true)))
            output.WriteLine($"after {s.ItemName} p={s.UnitPrice} q={s.Quantity}");
    }

    [Theory]
    [InlineData(0, 0)]      // 0L -> 0L dầu
    [InlineData(1, 1)]      // 1L * 0.04 = 0.04L -> làm tròn lên = 1L
    [InlineData(10, 1)]     // 10L * 0.04 = 0.40L -> làm tròn lên = 1L
    [InlineData(25, 1)]     // 25L * 0.04 = 1.00L -> làm tròn lên = 1L
    [InlineData(26, 2)]     // 26L * 0.04 = 1.04L -> làm tròn lên = 2L
    [InlineData(50, 2)]     // 50L * 0.04 = 2.00L -> làm tròn lên = 2L
    [InlineData(51, 3)]     // 51L * 0.04 = 2.04L -> làm tròn lên = 3L
    [InlineData(100, 4)]    // 100L * 0.04 = 4.00L -> làm tròn lên = 4L
    [InlineData(101, 5)]    // 101L * 0.04 = 4.04L -> làm tròn lên = 5L
    public void Ship_quarter_book_oil_always_rounds_up_from_fuel(decimal fuelOut, decimal expectedOilOut)
    {
        using var app = TestApp.Create();
        var date = new DateTime(2026, 9, 30);
        var oilGroup = app.System.GetGroups().FirstOrDefault(x => x.Name == "Nhớt");
        var groupId = oilGroup?.Id ?? app.System.SaveGroup(null, "Nhớt").Id!.Value;
        var itemId = app.System.SaveItem(new ItemEdit
        {
            GroupId = groupId,
            UnitId = SeedIds.UnitLiter,
            Name = "Dầu nhờn test",
            Density = 0.9m,
            Temperature = 30,
            Vcf = 1m
        }).Id!.Value;

        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 7, 1),
            WarehouseId = SeedIds.Ship,
            ItemId = SeedIds.ItemRon95,
            UnitPrice = 20000m,
            ActualQuantity = 500m
        }).Ok);

        Assert.True(app.System.SaveOpening(new OpeningRequest
        {
            DocumentDate = new DateTime(2026, 7, 1),
            WarehouseId = SeedIds.Ship,
            ItemId = itemId,
            UnitPrice = 50000m,
            ActualQuantity = 100m
        }).Ok);

        var saved = app.System.SaveShipQuarterBook(new ShipQuarterBookSaveRequest
        {
            ConsumerId = SeedIds.Ship,
            QuarterDate = date,
            FuelGroupName = "Xăng",
            Lines =
            [
                new ShipQuarterBookLineEdit
                {
                    DocumentNumber = "TT-TEST-OIL",
                    DocumentDate = new DateTime(2026, 9, 15),
                    ManualFuelOut = true,
                    FuelOutManual = fuelOut,
                    ManualOilOut = false
                }
            ]
        });
        Assert.True(saved.Ok, saved.Message);

        var book = app.System.GetShipQuarterBook(date, SeedIds.Ship);
        var line = book.Rows[^1];
        Assert.Equal(expectedOilOut, line.OilOut ?? 0);
    }
}
