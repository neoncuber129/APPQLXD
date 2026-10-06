using APPQLXD.Core;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Core.Persistence;

namespace APPQLXD.Tests;

public sealed class LedgerAccuracyTests
{
    private const string Ron = "Xăng RON 95";
    private const string Diesel = "Dầu DO 0,05S";

    [Fact]
    public void Import_joins_an_existing_lot_and_a_different_amount_splits_without_losing_quantity()
    {
        using var app = TestApp.Create();
        var book = new Shadow();
        var system = app.System;

        var opening = SaveOpening(system, book, SeedIds.WhMain, SeedIds.ItemRon95, Ron, 15_000m, 40m, new DateTime(2026, 6, 10));
        var joined = SaveImport(system, book, SeedIds.WhMain, SeedIds.ItemRon95, "  xăng ron 95  ", 15_000m, 25m, 1m, null, new DateTime(2026, 8, 2), "HD-GOP");

        Assert.False(system.GetDocument(joined)!.WasSplit);
        Assert.Equal(65m, book.Quantity(Ron, 15_000m, SeedIds.WhMain));
        Assert.Single(LotIds(system, Ron, 15_000m));

        var split = SaveImport(system, book, SeedIds.WhMain, SeedIds.ItemRon95, Ron, 15_000m, 10m, 0.99m, 150_004m, new DateTime(2026, 8, 3), "HD-TACH");
        var splitDoc = system.GetDocument(split)!;
        Assert.True(splitDoc.WasSplit);
        Assert.Equal(2, splitDoc.Lines.Count);
        Assert.Equal(10m, splitDoc.Lines.Sum(x => x.Quantity));
        Assert.Equal(150_004m, splitDoc.Lines.Sum(x => x.Amount));
        Assert.Equal(QuantityMath.ActualImport(10m, 0.99m), splitDoc.Lines.Sum(x => x.ActualQuantity));
        Assert.Equal(10m, QuantityMath.ActualImport(10m, 0.99m));
        Assert.Equal(2, LotIds(system, Ron, 15_000m).Concat(LotIds(system, Ron, 15_001m)).Distinct().Count());

        var edited = SaveImport(system, book, SeedIds.WhMain, SeedIds.ItemRon95, Ron, 15_000m, 10m, 0.99m, null, new DateTime(2026, 8, 3), "HD-TACH", split);
        var after = system.GetDocument(edited)!;
        Assert.Equal(split, edited);
        Assert.False(after.WasSplit);
        Assert.Single(after.Lines);
        Assert.Equal(0m, book.Quantity(Ron, 15_001m, SeedIds.WhMain));
        Assert.Equal(system.ListMovements().Count(x => x.DocumentId == split), after.Lines.Count);

        var onHand = book.Quantity(Ron, 15_000m, SeedIds.WhMain);
        SaveIssue(system, book, SeedIds.WhMain, Ron, 15_000m, onHand - 4m, new DateTime(2026, 8, 20));
        var tooSmall = system.SaveOpening(new OpeningRequest
        {
            DocumentId = opening,
            DocumentDate = new DateTime(2026, 6, 10),
            WarehouseId = SeedIds.WhMain,
            ItemId = SeedIds.ItemRon95,
            ItemName = Ron,
            UnitPrice = 15_000m,
            ActualQuantity = 1m
        });
        Assert.False(tooSmall.Ok);
        Assert.Equal(40m, system.GetDocument(opening)!.ActualQuantity);
        AssertAgree(system, book);
    }

    [Fact]
    public void Large_crud_matches_stock_movements_and_the_nxt_book()
    {
        using var app = TestApp.Create();
        var book = new Shadow();
        var system = app.System;

        SaveOpening(system, book, SeedIds.WhMain, SeedIds.ItemRon95, Ron, 15_000m, 3_000m, new DateTime(2026, 6, 10));
        SaveOpening(system, book, SeedIds.WhMain, SeedIds.ItemRon95, Ron, 18_000m, 1_000m, new DateTime(2026, 6, 10));
        SaveOpening(system, book, SeedIds.WhMain, SeedIds.ItemDo, Diesel, 20_000m, 2_000m, new DateTime(2026, 6, 10));
        SaveOpening(system, book, SeedIds.WhAux, SeedIds.ItemRon95, Ron, 15_000m, 400m, new DateTime(2026, 6, 10));
        Assert.Single(LotIds(system, Ron, 15_000m));

        var imports = new List<Guid>();
        for (var i = 0; i < 48; i++)
        {
            var diesel = i % 3 == 0;
            var item = diesel ? SeedIds.ItemDo : SeedIds.ItemRon95;
            var name = diesel ? Diesel : Ron;
            var price = diesel ? 20_000m : i % 2 == 0 ? 15_000m : 18_000m;
            var warehouse = i % 5 == 0 ? SeedIds.WhAux : SeedIds.WhMain;
            var qty = 4m + i % 19;
            var vcf = i % 7 == 0 ? 0.9735m : 1m;
            decimal? amount = i % 11 == 0 ? price * qty + (i % 4) : null;
            imports.Add(SaveImport(system, book, warehouse, item, name, price, qty, vcf, amount, new DateTime(2026, 8, 1 + i % 27), "HD-" + i));
        }

        var issues = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            var price = i % 2 == 0 ? 15_000m : 18_000m;
            var qty = 2m + i % 6;
            if (book.Quantity(Ron, price, SeedIds.WhMain) < qty + 30m)
                continue;
            issues.Add(SaveIssue(system, book, SeedIds.WhMain, Ron, price, qty, new DateTime(2026, 8, 20)));
        }

        var transfers = new List<Guid>();
        for (var i = 0; i < 8; i++)
        {
            if (book.Quantity(Ron, 15_000m, SeedIds.WhMain) < 12m)
                break;
            transfers.Add(SaveTransfer(system, book, Ron, 15_000m, 5m, new DateTime(2026, 8, 22)));
        }

        var grown = imports[3];
        var grownDoc = system.GetDocument(grown)!;
        SaveImport(system, book, grownDoc.WarehouseId!.Value, grownDoc.ItemId!.Value, grownDoc.ItemName, grownDoc.UnitPrice, grownDoc.InputQuantity + 7m, grownDoc.Vcf, null, grownDoc.DocumentDate, "HD-SUA", grown);

        var observedOnly = system.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Issue,
            DocumentDate = new DateTime(2026, 8, 25),
            WarehouseId = SeedIds.WhMain,
            FormNumber = "VCF-1",
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }],
            Lines = [new SlipLineInput { ItemId = SeedIds.ItemRon95, ItemName = Ron, ObservedQuantity = 20m, Vcf = 0.99m, UnitPrice = 15_000m }]
        });
        Assert.True(observedOnly.Ok, observedOnly.Message);
        var posted = system.GetDocument(observedOnly.Id!.Value)!;
        Assert.Equal(QuantityMath.ActualImport(20m, 0.99m), posted.ActualQuantity);
        book.Replace(posted.Id, [new Move(Ron, 15_000m, SeedIds.WhMain, -posted.ActualQuantity)]);

        var beforeReject = book.Snapshot();
        var rejected = system.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Issue,
            DocumentDate = new DateTime(2026, 8, 26),
            WarehouseId = SeedIds.WhMain,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }],
            Lines = [new SlipLineInput { ItemId = SeedIds.ItemRon95, ItemName = Ron, ObservedQuantity = 9_000m, ActualQuantity = 9_000m, Vcf = 1m, UnitPrice = 15_000m }]
        });
        Assert.False(rejected.Ok);
        Assert.Equal(beforeReject, book.Snapshot());

        if (issues.Count > 0)
        {
            Assert.True(system.DeleteSlip(issues[0]).Ok);
            book.Remove(issues[0]);
        }

        if (transfers.Count > 0)
        {
            Assert.True(system.Void(transfers[0]).Ok);
            book.Remove(transfers[0]);
            Assert.Equal(DocumentStatus.Voided, system.GetDocument(transfers[0])!.Status);
            Assert.DoesNotContain(system.GetNxt(2026, 3, [SeedIds.WhMain, SeedIds.WhAux]).Rows, x => x.DocumentId == transfers[0]);
        }

        var auxLot = LotIds(system, Ron, 15_000m).Single();
        var firstConsume = system.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            Cells =
            [
                new ConsumptionCellRequest
                {
                    DocumentDate = new DateTime(2026, 9, 30),
                    WarehouseId = SeedIds.WhAux,
                    ItemName = Ron,
                    UnitPrice = 15_000m,
                    ActualQuantity = 6m,
                    Vcf = 1m
                }
            ]
        });
        Assert.True(firstConsume.Ok, firstConsume.Message);
        book.Replace(firstConsume.Id!.Value, [new Move(Ron, 15_000m, SeedIds.WhAux, -6m)]);
        var secondConsume = system.SaveConsumptionSheet(new ConsumptionSheetRequest
        {
            DocumentDate = new DateTime(2026, 9, 30),
            Cells =
            [
                new ConsumptionCellRequest
                {
                    DocumentDate = new DateTime(2026, 9, 30),
                    WarehouseId = SeedIds.WhAux,
                    ItemName = Ron,
                    UnitPrice = 15_000m,
                    ActualQuantity = 2m,
                    Vcf = 1m
                }
            ]
        });
        Assert.True(secondConsume.Ok, secondConsume.Message);
        Assert.Equal(firstConsume.Id, secondConsume.Id);
        book.Replace(firstConsume.Id!.Value, [new Move(Ron, 15_000m, SeedIds.WhAux, -2m)]);
        var auxName = system.GetWarehouses().Single(w => w.Id == SeedIds.WhAux).Name;
        Assert.Single(system.ListDocuments(DocumentKind.Consumption), x => x.Status == DocumentStatus.Active && x.WarehouseName == auxName && x.UnitPrice == 15_000m);

        var aux = system.SaveAuxiliary(new AuxiliaryRequest
        {
            DocumentDate = new DateTime(2026, 9, 12),
            WarehouseId = SeedIds.WhAux,
            LotId = auxLot,
            ActualQuantity = 3m,
            Vcf = 0.99m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Tàu" }]
        });
        Assert.True(aux.Ok, aux.Message);
        book.Replace(aux.Id!.Value, [new Move(Ron, 15_000m, SeedIds.WhAux, -3m)]);
        var mainBlocked = system.SaveAuxiliary(new AuxiliaryRequest
        {
            DocumentDate = new DateTime(2026, 9, 12),
            WarehouseId = SeedIds.WhMain,
            LotId = auxLot,
            ActualQuantity = 1m,
            Vcf = 1m,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Kho chính" }]
        });
        Assert.False(mainBlocked.Ok);

        var distance = 12.5m;
        var norm = 0.2m;
        var vehicleActual = QuantityMath.VehicleActual(distance, norm);
        Assert.True(book.Quantity(Ron, 15_000m, SeedIds.WhMain) > vehicleActual);
        var vehicle = system.SaveSlip(new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Vehicle,
            ConsumerId = SeedIds.Vehicle,
            Distance = distance,
            Norm = norm,
            DocumentDate = new DateTime(2026, 9, 18),
            WarehouseId = SeedIds.WhMain,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Lái xe" }],
            Lines = [new SlipLineInput { LotId = auxLot, ObservedQuantity = 1m, ActualQuantity = 1m, Vcf = 1m, UnitPrice = 15_000m }]
        });
        Assert.True(vehicle.Ok, vehicle.Message);
        Assert.Equal(vehicleActual, system.GetDocument(vehicle.Id!.Value)!.ActualQuantity);
        book.Replace(vehicle.Id!.Value, [new Move(Ron, 15_000m, SeedIds.WhMain, -vehicleActual)]);

        var rebuilt = system.RebuildStock();
        Assert.True(rebuilt.Ok, rebuilt.Message);
        Assert.Empty(rebuilt.Warnings);

        AssertAgree(system, book);
        AssertNxt(system, book);
    }

    private static Guid SaveOpening(FuelSystem system, Shadow book, Guid warehouse, Guid item, string name, decimal price, decimal actual, DateTime date, Guid? id = null)
    {
        var saved = system.SaveOpening(new OpeningRequest
        {
            DocumentId = id,
            DocumentDate = date,
            WarehouseId = warehouse,
            ItemId = item,
            ItemName = name,
            UnitPrice = price,
            ActualQuantity = actual
        });
        Assert.True(saved.Ok, saved.Message);
        book.Replace(saved.Id!.Value, [new Move(name, price, warehouse, QuantityMath.Whole(actual))]);
        var doc = system.GetDocument(saved.Id!.Value)!;
        Assert.Equal(0m, doc.Vcf);
        Assert.Equal(QuantityMath.Whole(actual), doc.ActualQuantity);
        return saved.Id!.Value;
    }

    private static Guid SaveImport(FuelSystem system, Shadow book, Guid warehouse, Guid item, string name, decimal price, decimal qty, decimal vcf, decimal? amount, DateTime date, string invoice, Guid? id = null)
    {
        var preview = ImportLotSplitter.Split(price, qty, amount, vcf);
        Assert.True(preview.Ok, preview.Message);
        Assert.Equal(QuantityMath.Whole(qty), preview.TotalQuantity);
        Assert.Equal(QuantityMath.ActualImport(qty, vcf), preview.TotalActual);
        if (amount is decimal target)
            Assert.Equal(target, preview.TotalAmount);

        var saved = system.SaveImport(new ImportRequest
        {
            DocumentId = id,
            DocumentDate = date,
            WarehouseId = warehouse,
            ItemId = item,
            ItemName = name,
            UnitPrice = price,
            InputQuantity = qty,
            Amount = amount,
            Vcf = vcf,
            Fields = [new FieldInput { FieldId = SeedIds.FieldInvoice, Value = invoice }]
        });
        Assert.True(saved.Ok, saved.Message);
        book.Replace(saved.Id!.Value, preview.Lines.Select(x => new Move(name, x.UnitPrice, warehouse, x.Actual)).ToList());
        return saved.Id!.Value;
    }

    private static Guid SaveIssue(FuelSystem system, Shadow book, Guid warehouse, string name, decimal price, decimal actual, DateTime date)
    {
        var saved = SaveSlip(system, new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Issue,
            DocumentDate = date,
            WarehouseId = warehouse,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }],
            Lines = [new SlipLineInput { ItemId = SeedIds.ItemRon95, ItemName = name, ObservedQuantity = actual, ActualQuantity = actual, Vcf = 1m, UnitPrice = price }]
        });
        book.Replace(saved, [new Move(name, price, warehouse, -QuantityMath.Whole(actual))]);
        return saved;
    }

    private static Guid SaveTransfer(FuelSystem system, Shadow book, string name, decimal price, decimal actual, DateTime date)
    {
        var lot = LotIds(system, name, price).Single();
        var saved = SaveSlip(system, new SlipRequest
        {
            IsExport = true,
            ExportMode = ExportSlipMode.Transfer,
            DocumentDate = date,
            WarehouseId = SeedIds.WhMain,
            DestinationWarehouseId = SeedIds.WhAux,
            Fields = [new FieldInput { FieldId = SeedIds.FieldReceiver, Value = "Thủ kho" }],
            Lines = [new SlipLineInput { LotId = lot, ObservedQuantity = actual, ActualQuantity = actual, Vcf = 1m, UnitPrice = price }]
        });
        var whole = QuantityMath.Whole(actual);
        book.Replace(saved,
        [
            new Move(name, price, SeedIds.WhMain, -whole),
            new Move(name, price, SeedIds.WhAux, whole)
        ]);
        return saved;
    }

    private static Guid SaveSlip(FuelSystem system, SlipRequest request)
    {
        var saved = system.SaveSlip(request);
        Assert.True(saved.Ok, saved.Message);
        return saved.Id!.Value;
    }

    private static List<Guid> LotIds(FuelSystem system, string name, decimal price) =>
        system.ListStock(new StockFilter(null, null, null, price, true))
            .Where(x => QuantityMath.LotKey(x.ItemName) == QuantityMath.LotKey(name))
            .Select(x => x.LotId)
            .Distinct()
            .ToList();

    private static void AssertAgree(FuelSystem system, Shadow book)
    {
        var stock = system.ListStock(new StockFilter(null, null, null, null, true));
        foreach (var row in stock)
        {
            var expected = book.Quantity(row.ItemName, row.UnitPrice, row.WarehouseId);
            Assert.True(row.Quantity == expected, $"{row.WarehouseName} {row.ItemName} giá {row.UnitPrice}: tồn {row.Quantity}, sổ đối chiếu {expected}.");
        }

        foreach (var row in book.Rows.Where(x => x.Quantity != 0))
            Assert.Contains(stock, x => QuantityMath.LotKey(x.ItemName) == row.Key && x.UnitPrice == row.Price && x.WarehouseId == row.Warehouse && x.Quantity == row.Quantity);

        var moves = system.ListMovements();
        Assert.Equal(stock.Sum(x => x.Quantity), moves.Sum(x => x.SignedQuantity));
        foreach (var row in stock)
        {
            var signed = moves.Where(x => x.WarehouseName == row.WarehouseName && x.UnitPrice == row.UnitPrice && QuantityMath.LotKey(x.ItemName) == QuantityMath.LotKey(row.ItemName)).Sum(x => x.SignedQuantity);
            Assert.Equal(row.Quantity, signed);
        }

        Assert.All(stock, x => Assert.True(x.Quantity >= 0));
        var lotKeys = stock.Select(x => (QuantityMath.LotKey(x.ItemName), x.UnitPrice, x.LotId)).Distinct().ToList();
        Assert.Equal(lotKeys.Select(x => (x.Item1, x.Item2)).Distinct().Count(), lotKeys.Select(x => x.LotId).Distinct().Count());
    }

    private static void AssertNxt(FuelSystem system, Shadow book)
    {
        var warehouses = new[] { SeedIds.WhMain, SeedIds.WhAux };
        var sheet = system.GetNxt(2026, 3, warehouses);
        var opening = sheet.Rows.Single(x => x.Kind == NxtRowKind.Opening);
        var cong = sheet.Rows.Single(x => x.Kind == NxtRowKind.Period);
        Assert.Equal("Cộng mang sang", cong.Description);
        Assert.DoesNotContain(sheet.Rows, x => x.Kind == NxtRowKind.Closing);
        foreach (var name in new[] { Ron, Diesel })
        {
            var open = Cell(sheet, opening, name).Balance ?? 0;
            var inn = Cell(sheet, cong, name).In ?? 0;
            var outQty = Cell(sheet, cong, name).Out ?? 0;
            var close = Cell(sheet, cong, name).Balance ?? 0;
            Assert.Equal(QuantityMath.Whole(open + inn - outQty), close);
            var stock = book.Rows.Where(x => x.Key == QuantityMath.LotKey(name) && warehouses.Contains(x.Warehouse)).Sum(x => x.Quantity);
            Assert.Equal(stock, close);
        }
    }

    private static NxtCell Cell(NxtSheet sheet, NxtRow row, string title)
    {
        var index = sheet.Columns.ToList().FindIndex(x => x.Title == title);
        Assert.True(index >= 0, title);
        return row.Cells[index];
    }

    private sealed record Move(string Name, decimal Price, Guid Warehouse, decimal Signed);

    private sealed class Shadow
    {
        private readonly Dictionary<(string Key, long Price, Guid Warehouse), decimal> _qty = [];
        private readonly Dictionary<Guid, List<Move>> _docs = [];

        public IReadOnlyList<(string Key, long Price, Guid Warehouse, decimal Quantity)> Rows =>
            _qty.Select(x => (x.Key.Key, x.Key.Price, x.Key.Warehouse, x.Value)).ToList();

        public decimal Quantity(string name, decimal price, Guid warehouse) =>
            _qty.GetValueOrDefault((QuantityMath.LotKey(name), WholePrice(price), warehouse));

        public Dictionary<(string Key, long Price, Guid Warehouse), decimal> Snapshot() => new(_qty);

        public void Replace(Guid id, IReadOnlyList<Move> moves)
        {
            Remove(id);
            _docs[id] = moves.ToList();
            foreach (var move in moves)
                Add(move.Name, move.Price, move.Warehouse, move.Signed);
        }

        public void Remove(Guid id)
        {
            if (!_docs.Remove(id, out var moves))
                return;
            foreach (var move in moves)
                Add(move.Name, move.Price, move.Warehouse, -move.Signed);
        }

        private void Add(string name, decimal price, Guid warehouse, decimal signed)
        {
            var key = (QuantityMath.LotKey(name), WholePrice(price), warehouse);
            _qty[key] = QuantityMath.Whole(_qty.GetValueOrDefault(key) + signed);
        }

        private static long WholePrice(decimal price)
        {
            Assert.True(QuantityMath.TryWholeMoney(price, out var whole));
            return whole;
        }
    }
}
