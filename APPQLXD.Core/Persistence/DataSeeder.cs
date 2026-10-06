using APPQLXD.Core;
using APPQLXD.Core.Domain;
using Labels = APPQLXD.Core.Labels;

namespace APPQLXD.Core.Persistence;

public static class DataSeeder
{
    public static void Seed(AppDbContext db)
    {
        var now = DateTime.UtcNow;

        if (!db.ItemGroups.Any(x => x.Id == SeedIds.GroupFuel))
            db.ItemGroups.Add(new ItemGroup { Id = SeedIds.GroupFuel, Name = "Xăng" });
        if (!db.ItemGroups.Any(x => x.Id == SeedIds.GroupDiesel))
            db.ItemGroups.Add(new ItemGroup { Id = SeedIds.GroupDiesel, Name = "Dầu" });

        if (!db.MeasureUnits.Any(x => x.Id == SeedIds.UnitLiter))
            db.MeasureUnits.Add(new MeasureUnit { Id = SeedIds.UnitLiter, Name = "Lít" });

        if (!db.FuelItems.Any(x => x.Id == SeedIds.ItemRon95))
        {
            db.FuelItems.Add(new FuelItem
            {
                Id = SeedIds.ItemRon95,
                GroupId = SeedIds.GroupFuel,
                UnitId = SeedIds.UnitLiter,
                Name = "Xăng RON 95",
                QualityInfo = "1",
                Density = 0.735m,
                Temperature = 36,
                MeasurementNote = "Phiếu nhập 2026: nhiệt độ 36°C, tỉ trọng 0,735, VCF 0,9735",
                Vcf = 0.973500m,
                ConversionRule = Labels.DefaultConversionRule
            });
        }
        if (!db.FuelItems.Any(x => x.Id == SeedIds.ItemDo))
        {
            db.FuelItems.Add(new FuelItem
            {
                Id = SeedIds.ItemDo,
                GroupId = SeedIds.GroupDiesel,
                UnitId = SeedIds.UnitLiter,
                Name = "Dầu DO 0,05S",
                QualityInfo = "1",
                Density = 0.835m,
                Temperature = 36,
                MeasurementNote = "Phiếu nhập 2026: nhiệt độ 36°C, tỉ trọng 0,835, VCF 0,982",
                Vcf = 0.982000m,
                ConversionRule = Labels.DefaultConversionRule
            });
        }

        var fields = new[]
        {
            new FieldDefinition
            {
                Id = SeedIds.FieldInvoice,
                Family = DocumentFamily.Import,
                Name = "Số hóa đơn",
                DataType = FieldDataType.Text,
                IsRequired = true,
                IsVisible = true,
                SortOrder = 1
            },
            new FieldDefinition
            {
                Id = SeedIds.FieldDeliverer,
                Family = DocumentFamily.Import,
                Name = "Người giao",
                DataType = FieldDataType.Text,
                IsRequired = false,
                IsVisible = true,
                SortOrder = 2
            },
            new FieldDefinition
            {
                Id = SeedIds.FieldHidden,
                Family = DocumentFamily.Import,
                Name = "Ghi chú nội bộ",
                DataType = FieldDataType.Text,
                IsRequired = true,
                IsVisible = false,
                SortOrder = 3
            },
            new FieldDefinition
            {
                Id = SeedIds.FieldReceiver,
                Family = DocumentFamily.Export,
                Name = "Người nhận",
                DataType = FieldDataType.Text,
                IsRequired = false,
                IsVisible = true,
                SortOrder = 1
            },
            new FieldDefinition
            {
                Id = SeedIds.FieldPurpose,
                Family = DocumentFamily.Export,
                Name = "Mục đích",
                DataType = FieldDataType.Text,
                IsRequired = false,
                IsVisible = true,
                SortOrder = 2
            }
        };
        foreach (var f in fields)
        {
            if (!db.FieldDefinitions.Any(x => x.Id == f.Id))
                db.FieldDefinitions.Add(f);
        }

        if (!db.SampleSets.Any(x => x.Id == SeedIds.SampleImport))
            db.SampleSets.Add(new SampleSet { Id = SeedIds.SampleImport, Name = "Mẫu nhập kho tổng", Family = DocumentFamily.Import });
        if (!db.SampleSets.Any(x => x.Id == SeedIds.SampleExport))
            db.SampleSets.Add(new SampleSet { Id = SeedIds.SampleExport, Name = "Mẫu xuất vận hành", Family = DocumentFamily.Export });

        if (!db.SampleValues.Any(x => x.SampleSetId == SeedIds.SampleImport && x.FieldDefinitionId == SeedIds.FieldInvoice))
            db.SampleValues.Add(new SampleValue { Id = Guid.NewGuid(), SampleSetId = SeedIds.SampleImport, FieldDefinitionId = SeedIds.FieldInvoice, Value = "HD-MAU", CreatedAt = now });
        if (!db.SampleValues.Any(x => x.SampleSetId == SeedIds.SampleImport && x.FieldDefinitionId == SeedIds.FieldDeliverer))
            db.SampleValues.Add(new SampleValue { Id = Guid.NewGuid(), SampleSetId = SeedIds.SampleImport, FieldDefinitionId = SeedIds.FieldDeliverer, Value = "Nguyễn Văn A", CreatedAt = now });
        if (!db.SampleValues.Any(x => x.SampleSetId == SeedIds.SampleExport && x.FieldDefinitionId == SeedIds.FieldReceiver))
            db.SampleValues.Add(new SampleValue { Id = Guid.NewGuid(), SampleSetId = SeedIds.SampleExport, FieldDefinitionId = SeedIds.FieldReceiver, Value = "Tổ máy", CreatedAt = now });
        if (!db.SampleValues.Any(x => x.SampleSetId == SeedIds.SampleExport && x.FieldDefinitionId == SeedIds.FieldPurpose))
            db.SampleValues.Add(new SampleValue { Id = Guid.NewGuid(), SampleSetId = SeedIds.SampleExport, FieldDefinitionId = SeedIds.FieldPurpose, Value = "Vận hành", CreatedAt = now });

        var warehouses = new[]
        {
            new Warehouse
            {
                Id = SeedIds.WhMain,
                Code = "HDBP18",
                Name = "Kho Hải đoàn Biên phòng 18",
                Type = WarehouseType.Main,
                DefaultImportSampleSetId = null,
                DefaultExportSampleSetId = SeedIds.SampleExport
            },
            new Warehouse
            {
                Id = SeedIds.WhPtkt,
                Code = "PTKT",
                Name = "Kho PTKT-VTXD",
                Type = WarehouseType.Ptkt,
                DefaultImportSampleSetId = null,
                DefaultExportSampleSetId = SeedIds.SampleExport
            },
            new Warehouse
            {
                Id = SeedIds.WhAux,
                Code = "KHOPHU",
                Name = "Kho phụ bãi",
                Type = WarehouseType.Auxiliary
            }
        };
        foreach (var w in warehouses)
        {
            if (!db.Warehouses.Any(x => x.Id == w.Id))
                db.Warehouses.Add(w);
        }

        var consumers = new[]
        {
            new Consumer
            {
                Id = SeedIds.Machine,
                Code = "MáyQY",
                Name = "Máy QY",
                Type = ConsumerType.Machine,
                DefaultGroupId = SeedIds.GroupDiesel,
                DefaultItemId = SeedIds.ItemDo,
                RollTransfersIntoQuarter = true,
                DefaultExportSampleSetId = SeedIds.SampleExport
            },
            new Consumer
            {
                Id = SeedIds.Vehicle,
                Code = "QB3475",
                Name = "QB 34-75",
                Type = ConsumerType.Vehicle,
                DefaultGroupId = SeedIds.GroupFuel,
                DefaultItemId = SeedIds.ItemRon95,
                Norm = 0.180000m,
                DefaultExportSampleSetId = SeedIds.SampleExport
            },
            new Consumer
            {
                Id = SeedIds.Ship,
                Code = "27-05-01",
                Name = "Tàu BP 27-05-01",
                Type = ConsumerType.Ship,
                DefaultGroupId = SeedIds.GroupFuel,
                DefaultItemId = SeedIds.ItemRon95,
                DefaultExportSampleSetId = SeedIds.SampleExport,
                ShipType = "Tàu tuần tra",
                MainMachineCount = 1m,
                AuxMachineCount = 1m
            }
        };
        foreach (var c in consumers)
        {
            if (!db.Consumers.Any(x => x.Id == c.Id))
                db.Consumers.Add(c);
        }

        var factors = new[]
        {
            new ConsumerNormFactor
            {
                Id = Guid.Parse("b1b1b1b1-b1b1-b1b1-b1b1-b1b1b1b1b1b1"),
                ConsumerId = SeedIds.Ship,
                GroupId = SeedIds.GroupFuel,
                Name = ShipNormSlots.AtBerth,
                Value = 1m,
                SortOrder = 1
            },
            new ConsumerNormFactor
            {
                Id = Guid.Parse("b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2"),
                ConsumerId = SeedIds.Ship,
                GroupId = SeedIds.GroupFuel,
                Name = ShipNormSlots.Cx25,
                Value = 2m,
                SortOrder = 2
            },
            new ConsumerNormFactor
            {
                Id = Guid.Parse("b3b3b3b3-b3b3-b3b3-b3b3-b3b3b3b3b3b3"),
                ConsumerId = SeedIds.Ship,
                GroupId = SeedIds.GroupFuel,
                Name = ShipNormSlots.Cx50,
                Value = 3m,
                SortOrder = 3
            },
            new ConsumerNormFactor
            {
                Id = Guid.Parse("b4b4b4b4-b4b4-b4b4-b4b4-b4b4b4b4b4b4"),
                ConsumerId = SeedIds.Ship,
                GroupId = SeedIds.GroupFuel,
                Name = ShipNormSlots.Cx75,
                Value = 4m,
                SortOrder = 4
            },
            new ConsumerNormFactor
            {
                Id = Guid.Parse("b5b5b5b5-b5b5-b5b5-b5b5-b5b5b5b5b5b5"),
                ConsumerId = SeedIds.Ship,
                GroupId = SeedIds.GroupFuel,
                Name = ShipNormSlots.Cx100,
                Value = 5m,
                SortOrder = 5
            },
            new ConsumerNormFactor
            {
                Id = Guid.Parse("b6b6b6b6-b6b6-b6b6-b6b6-b6b6b6b6b6b6"),
                ConsumerId = SeedIds.Ship,
                GroupId = SeedIds.GroupFuel,
                Name = ShipNormSlots.Aux,
                Value = 1.5m,
                SortOrder = 6
            }
        };
        foreach (var factor in factors)
        {
            if (!db.ConsumerNormFactors.Any(x => x.Id == factor.Id))
                db.ConsumerNormFactors.Add(factor);
        }

        foreach (var consumer in db.Consumers.Local.ToList())
            Services.CatalogStore.SyncConsumerWarehouse(db, consumer);

        db.SaveChanges();
    }
}
