using APPQLXD.Core;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Services;
using Labels = APPQLXD.Core.Labels;

namespace APPQLXD.Core.Persistence;

/// <summary>
/// Danh mục và dữ liệu mẫu lấy từ phiếu nhập/xuất 2026 và sổ NXT quý 3.
/// </summary>
public static class ExcelSampleData
{
    public const string ImportSetName = "Hải đoàn BP 18 - nhập";
    public const string ExportSetName = "Hải đoàn BP 18 - xuất";

    public static void Ensure(AppDbContext db)
    {
        EnsurePaperFields(db);
        EnsurePtktWarehouse(db);
        if (db.SampleSets.Any(x => x.Name == ExportSetName))
        {
            var exportSetId = db.SampleSets.First(x => x.Name == ExportSetName).Id;
            SeedFullPaperSamples(db);
            PrepareSeedShip(db);
            AlignCatalog(db);
            EnsureVehicles(db, exportSetId);
            EnsureMachines(db, exportSetId);
            FillShipNormDefaults(db);
            SeedMissionYearLimits(db, DateTime.Today.Year);
            TrimExtraConsumers(db);
            EnsureConsumerLocations(db);
            db.SaveChanges();
            return;
        }

        var now = new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc);
        var liter = Unit(db, "Lít");
        var groupFuel = Group(db, "Xăng");
        var groupDiesel = Group(db, "Dầu");
        var groupGrease = Group(db, "Mỡ");
        var groupLube = Group(db, "Nhớt");
        var groupKit = Group(db, "PTKT - VTXD");

        AddItem(db, "Xăng RON 95", groupFuel, liter, 0.973500m, 0.735m, 36);
        AddItem(db, "Dầu DO 0,05S", groupDiesel, liter, 0.982000m, 0.835m, 36);
        TouchFuel(db, "Xăng RON 95", 0.973500m, 0.735m, 36);
        TouchFuel(db, "Dầu DO 0,05S", 0.982000m, 0.835m, 36);

        AddItem(db, "Xăng RON 92", groupFuel, liter, 1m, null, null);
        AddItem(db, "Xăng E10 Ron 95", groupFuel, liter, 1m, null, null);
        AddItem(db, "Dầu mỡ", groupGrease, liter, 1m, null, null);
        AddItem(db, "Total Rubia Tir 6400", groupLube, liter, 1m, null, 36);
        foreach (var name in Lubes)
            AddItem(db, name, groupLube, liter, 1m, null, null);
        foreach (var name in Greases)
            AddItem(db, name, groupGrease, liter, 1m, null, null);

        var piece = Unit(db, "Cái");
        var column = Unit(db, "Cột");
        var bottle = Unit(db, "Bình");
        AddItem(db, "Cột tra NL ĐT TATSUNO", groupKit, column, 1m, null, null);
        AddItem(db, "Bơm quả nén D-100", groupKit, piece, 1m, null, null);
        AddItem(db, "Chăn ami ăng", groupKit, piece, 1m, null, null);
        AddItem(db, "Bình bột cứu hỏa 4kg-MQZL-4", groupKit, bottle, 1m, null, null);
        AddItem(db, "Bình bột cứu hỏa 8kg-MQZL-8", groupKit, bottle, 1m, null, null);
        AddItem(db, "Bình bột cứu hỏa 35kg-MQZL-35", groupKit, bottle, 1m, null, null);
        AddItem(db, "Bình cứu hỏa MFTZ-8", groupKit, piece, 1m, null, null);

        var main = db.Warehouses.Local.FirstOrDefault(x => x.Id == SeedIds.WhMain)
            ?? db.Warehouses.FirstOrDefault(x => x.Id == SeedIds.WhMain)
            ?? db.Warehouses.FirstOrDefault(x => x.Type == WarehouseType.Main);
        if (main is null)
        {
            main = new Warehouse
            {
                Id = Guid.NewGuid(),
                Code = "HDBP18",
                Name = "Kho Hải đoàn Biên phòng 18",
                Type = WarehouseType.Main
            };
            db.Warehouses.Add(main);
        }
        else if (main.Name == "Kho tổng")
        {
            main.Code = "HDBP18";
            main.Name = "Kho Hải đoàn Biên phòng 18";
        }

        if (!db.Warehouses.Any(x => x.Id == SeedIds.WhPtkt)
            && !db.Warehouses.Local.Any(x => x.Id == SeedIds.WhPtkt)
            && !db.Warehouses.Any(x => x.Type == WarehouseType.Ptkt)
            && !db.Warehouses.Local.Any(x => x.Type == WarehouseType.Ptkt))
        {
            EnsurePtktWarehouse(db);
        }

        var warehouse = main ?? throw new InvalidOperationException("Không tạo được kho chính.");

        var importNature = Field(db, DocumentFamily.Import, "Tính chất nhập", 10);
        var importSender = Field(db, DocumentFamily.Import, "Đơn vị giao hàng", 11);
        var importReceiverUnit = Field(db, DocumentFamily.Import, "Đơn vị nhận hàng", 12);
        var importReceiver = Field(db, DocumentFamily.Import, "Người nhận", 13);
        var importMission = Field(db, DocumentFamily.Import, "Nhiệm vụ", 14);
        var exportNature = Field(db, DocumentFamily.Export, "Tính chất xuất", 10);
        var exportSender = Field(db, DocumentFamily.Export, "Đơn vị giao", 11);
        var exportReceiverUnit = Field(db, DocumentFamily.Export, "Đơn vị nhận", 12);
        var exportPlate = Field(db, DocumentFamily.Export, "Số xe", 13);
        var exportMission = Field(db, DocumentFamily.Export, "Nhiệm vụ", 14);
        EnsureRequired(db, DocumentFamily.Import, "Số hóa đơn", 1);
        var delivererId = FindField(db, DocumentFamily.Import, "Người giao hàng")?.Id
            ?? Field(db, DocumentFamily.Import, "Người giao hàng", 23);
        var exportReceiver = Field(db, DocumentFamily.Export, "Người nhận", 8);

        var importSet = new SampleSet { Id = Guid.NewGuid(), Name = ImportSetName, Family = DocumentFamily.Import };
        var exportSet = new SampleSet { Id = Guid.NewGuid(), Name = ExportSetName, Family = DocumentFamily.Export };
        db.SampleSets.AddRange(importSet, exportSet);

        var stamp = now;
        Add(db, importSet.Id, importNature, ImportNatures, ref stamp);
        Add(db, importSet.Id, importSender, Senders, ref stamp);
        Add(db, importSet.Id, importReceiverUnit, ImportReceiverUnits, ref stamp);
        Add(db, importSet.Id, importReceiver, People, ref stamp);
        Add(db, importSet.Id, delivererId, Deliverers, ref stamp);
        Add(db, importSet.Id, importMission, Missions, ref stamp);

        Add(db, exportSet.Id, exportNature, ExportNatures, ref stamp);
        Add(db, exportSet.Id, exportSender, ["Kho Hải đoàn Biên phòng 18"], ref stamp);
        Add(db, exportSet.Id, exportReceiverUnit, ReceiverUnits, ref stamp);
        Add(db, exportSet.Id, exportReceiver, People, ref stamp);
        Add(db, exportSet.Id, exportPlate, Plates, ref stamp);
        Add(db, exportSet.Id, exportMission, Missions, ref stamp);

        warehouse.DefaultImportSampleSetId = null;
        warehouse.DefaultExportSampleSetId = exportSet.Id;
        var ptkt = db.Warehouses.Local.FirstOrDefault(x => x.Id == SeedIds.WhPtkt)
            ?? db.Warehouses.FirstOrDefault(x => x.Id == SeedIds.WhPtkt)
            ?? db.Warehouses.Local.FirstOrDefault(x => x.Type == WarehouseType.Ptkt)
            ?? db.Warehouses.FirstOrDefault(x => x.Type == WarehouseType.Ptkt);
        if (ptkt is not null)
        {
            ptkt.Type = WarehouseType.Ptkt;
            ptkt.DefaultImportSampleSetId = null;
            ptkt.DefaultExportSampleSetId = exportSet.Id;
        }

        foreach (var (plate, fuel, perHundredKm) in Vehicles)
            EnsureVehicle(db, exportSet.Id, plate, fuel, perHundredKm);

        EnsureMachines(db, exportSet.Id);
        SeedFullPaperSamples(db);
        PrepareSeedShip(db);
        AlignCatalog(db);
        FillShipNormDefaults(db);
        SeedMissionYearLimits(db, 2026);
        TrimExtraConsumers(db);
        EnsureConsumerLocations(db);
        db.SaveChanges();
    }

    private static readonly string[] AuxiliaryShips =
    [
        "Tàu BP 27-19-01", "Tàu BP 27-01-01", "Tàu BP 27-05-01"
    ];

    /// <summary>Mỗi tàu chỉ 1 loại NL (xăng hoặc dầu).</summary>
    private static readonly (string Name, string Fuel)[] ShipFuels =
    [
        ("Tàu BP 27-19-01", "Dầu DO 0,05S"),
        ("Tàu BP 27-01-01", "Dầu DO 0,05S"),
        ("Tàu BP 27-05-01", "Xăng RON 95")
    ];

    private static readonly string[] AutoAuxiliaryNames =
    [
        "Kho phụ bãi", "Kho phụ tàu", "Kho phụ doanh trại", "Kho phụ quân y", "Kho phụ kỹ thuật"
    ];

    private static void EnsurePtktWarehouse(AppDbContext db)
    {
        var existing = db.Warehouses.Local.FirstOrDefault(x => x.Id == SeedIds.WhPtkt)
            ?? db.Warehouses.FirstOrDefault(x => x.Id == SeedIds.WhPtkt)
            ?? db.Warehouses.Local.FirstOrDefault(x => x.Type == WarehouseType.Ptkt)
            ?? db.Warehouses.FirstOrDefault(x => x.Type == WarehouseType.Ptkt);
        if (existing is not null)
        {
            existing.Type = WarehouseType.Ptkt;
            if (string.IsNullOrWhiteSpace(existing.Code))
                existing.Code = "PTKT";
            if (string.IsNullOrWhiteSpace(existing.Name))
                existing.Name = "Kho PTKT-VTXD";
            return;
        }

        db.Warehouses.Add(new Warehouse
        {
            Id = SeedIds.WhPtkt,
            Code = "PTKT",
            Name = "Kho PTKT-VTXD",
            Type = WarehouseType.Ptkt
        });
    }

    private static void AlignCatalog(AppDbContext db)
    {
        var dau = Group(db, "Dầu");
        var xang = Group(db, "Xăng");
        var nhot = Group(db, "Nhớt");
        var mo = Group(db, "Mỡ");
        var lubeNames = Lubes.Append("Total Rubia Tir 6400").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var greaseNames = Greases.Append("Dầu mỡ").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = db.FuelItems.ToList();
        foreach (var item in items)
        {
            var groupId = FuelGroupId(item.Name, dau, xang, nhot, mo, lubeNames, greaseNames);
            if (groupId is Guid id && item.GroupId != id)
                item.GroupId = id;
        }

        var consumers = db.Consumers.ToList();
        foreach (var consumer in consumers)
        {
            if (consumer.DefaultGroupId is not Guid current)
                continue;
            var group = db.ItemGroups.FirstOrDefault(x => x.Id == current);
            if (group is null || group.Name is "Dầu" or "Xăng" or "Nhớt" or "Mỡ" or "PTKT - VTXD")
                continue;
            if (consumer.DefaultItemId is not Guid itemId)
                continue;
            var item = items.FirstOrDefault(x => x.Id == itemId);
            if (item is null)
                continue;
            var groupId = FuelGroupId(item.Name, dau, xang, nhot, mo, lubeNames, greaseNames);
            if (groupId is Guid id)
                consumer.DefaultGroupId = id;
        }

        foreach (var group in db.ItemGroups.Where(x => x.Name == "Dầu mỡ").ToList())
        {
            if (items.Any(x => x.GroupId == group.Id) || consumers.Any(x => x.DefaultGroupId == group.Id))
                continue;
            db.ItemGroups.Remove(group);
        }

        var reuse = db.Warehouses.ToList().FirstOrDefault(x => x.Name == "Kho phụ bãi" && !WarehouseUsed(db, x.Id));
        if (reuse is not null && db.Warehouses.Any(x => x.Name == AuxiliaryShips[0]))
            reuse = null;
        for (var i = 0; i < AuxiliaryShips.Length; i++)
            EnsureShip(db, AuxiliaryShips[i], i == 0 ? reuse : null);

        foreach (var row in db.Warehouses.Where(x => x.Type == WarehouseType.Auxiliary).ToList())
        {
            row.DefaultImportSampleSetId = null;
            if (consumers.Any(c => c.Id == row.Id))
                continue;
            if (AuxiliaryShips.Contains(row.Name) || !AutoAuxiliaryNames.Contains(row.Name))
                continue;
            if (WarehouseUsed(db, row.Id))
                continue;
            db.Warehouses.Remove(row);
        }
    }

    private static void EnsureConsumerLocations(AppDbContext db)
    {
        var map = db.Consumers.ToDictionary(x => x.Id);
        foreach (var local in db.Consumers.Local.ToList())
            map[local.Id] = local;
        foreach (var consumer in map.Values.ToList())
            CatalogStore.SyncConsumerWarehouse(db, consumer);
    }

    private static Guid? FuelGroupId(
        string name,
        Guid dau,
        Guid xang,
        Guid nhot,
        Guid mo,
        HashSet<string> lubeNames,
        HashSet<string> greaseNames)
    {
        if (greaseNames.Contains(name) || name.Contains("Mỡ", StringComparison.OrdinalIgnoreCase))
            return mo;
        if (lubeNames.Contains(name) || name.Contains("Nhớt", StringComparison.OrdinalIgnoreCase))
            return nhot;
        if (name.Contains("Xăng", StringComparison.OrdinalIgnoreCase))
            return xang;
        if (name.Contains("Dầu", StringComparison.OrdinalIgnoreCase))
            return dau;
        return null;
    }

    private static void EnsureVehicles(AppDbContext db, Guid exportSetId)
    {
        foreach (var (plate, fuel, perHundredKm) in Vehicles)
            EnsureVehicle(db, exportSetId, plate, fuel, perHundredKm);
    }

    private static void EnsureVehicle(AppDbContext db, Guid exportSetId, string plate, string fuel, decimal? perHundredKm)
    {
        var code = plate.Replace(" ", "", StringComparison.Ordinal);
        var item = db.FuelItems.Local.FirstOrDefault(x => x.Name == fuel)
            ?? db.FuelItems.FirstOrDefault(x => x.Name == fuel);
        var norm = perHundredKm is null ? (decimal?)null : perHundredKm.Value / 100m;
        var row = db.Consumers.Local.FirstOrDefault(x => x.Code == code || x.Name == plate)
            ?? db.Consumers.FirstOrDefault(x => x.Code == code || x.Name == plate);
        if (row is null && plate == Vehicles[0].Plate)
        {
            row = db.Consumers.Local.FirstOrDefault(x => x.Id == SeedIds.Vehicle)
                ?? db.Consumers.FirstOrDefault(x => x.Id == SeedIds.Vehicle);
        }

        if (row is null)
        {
            db.Consumers.Add(new Consumer
            {
                Id = plate == Vehicles[0].Plate ? SeedIds.Vehicle : Guid.NewGuid(),
                Code = code,
                Name = plate,
                Type = ConsumerType.Vehicle,
                DefaultGroupId = item?.GroupId,
                DefaultItemId = item?.Id,
                Norm = norm,
                DefaultExportSampleSetId = exportSetId
            });
            return;
        }

        row.Type = ConsumerType.Vehicle;
        row.Code = code;
        row.Name = plate;
        row.Norm = norm;
        if (item is not null)
        {
            row.DefaultGroupId = item.GroupId;
            row.DefaultItemId = item.Id;
        }

        if (row.DefaultExportSampleSetId is null)
            row.DefaultExportSampleSetId = exportSetId;
    }

    private static void EnsureMachines(AppDbContext db, Guid exportSetId)
    {
        RenameLegacyMachine(db, "Máyxăng", "Máy xăng", "MáyQY", "Máy QY");
        RenameLegacyMachine(db, "Máydầu", "Máy dầu", "MáyDT", "Máy DT");
        RenameLegacyMachine(db, "MáyXD", "Máy XD", "MáyTT", "Máy TT");
        RenameLegacyMachine(db, "MP-01", "Máy phát điện", "MáyQY", "Máy QY");

        for (var i = 0; i < Machines.Length; i++)
        {
            var (name, fuel) = Machines[i];
            var code = name.Replace(" ", "", StringComparison.Ordinal);
            var row = db.Consumers.Local.FirstOrDefault(x => x.Code == code || x.Name == name)
                ?? db.Consumers.FirstOrDefault(x => x.Code == code || x.Name == name);
            if (row is null && i == 0)
            {
                row = db.Consumers.Local.FirstOrDefault(x => x.Id == SeedIds.Machine)
                    ?? db.Consumers.FirstOrDefault(x => x.Id == SeedIds.Machine);
            }

            var item = db.FuelItems.Local.FirstOrDefault(x => x.Name == fuel)
                ?? db.FuelItems.FirstOrDefault(x => x.Name == fuel);
            if (row is null)
            {
                db.Consumers.Add(new Consumer
                {
                    Id = i == 0 ? SeedIds.Machine : Guid.NewGuid(),
                    Code = code,
                    Name = name,
                    Type = ConsumerType.Machine,
                    DefaultGroupId = item?.GroupId,
                    DefaultItemId = item?.Id,
                    Norm = null,
                    RollTransfersIntoQuarter = true,
                    DefaultExportSampleSetId = exportSetId
                });
                continue;
            }

            row.Type = ConsumerType.Machine;
            row.Code = code;
            row.Name = name;
            row.Norm = null;
            row.RollTransfersIntoQuarter = true;
            if (item is not null)
            {
                row.DefaultGroupId = item.GroupId;
                row.DefaultItemId = item.Id;
            }

            if (row.DefaultExportSampleSetId is null)
                row.DefaultExportSampleSetId = exportSetId;
        }

        RemoveUnusedMachine(db, "MáyXD", "Máy XD");
        RemoveUnusedMachine(db, "MáyCY", "Máy CY");

        foreach (var sample in db.SampleValues.Local.Where(x => x.Value is "Nổ máy XD" or "Nổ máy CY")
                     .Concat(db.SampleValues.Where(x => x.Value == "Nổ máy XD" || x.Value == "Nổ máy CY")).ToList())
            sample.Value = sample.Value.Contains("CY", StringComparison.Ordinal) ? "Nổ máy DT" : "Nổ máy TT";

        var natureField = db.FieldDefinitions.FirstOrDefault(x =>
            x.Family == DocumentFamily.Export && (x.Name == "Tính chất xuất" || x.Name == "Tính chất"));
        if (natureField is not null)
        {
            var stamp = DateTime.UtcNow;
            foreach (var value in new[] { "Nổ máy QY", "Nổ máy DT", "Nổ máy TT" })
            {
                if (db.SampleValues.Any(x => x.SampleSetId == exportSetId && x.FieldDefinitionId == natureField.Id && x.Value == value)
                    || db.SampleValues.Local.Any(x => x.SampleSetId == exportSetId && x.FieldDefinitionId == natureField.Id && x.Value == value))
                    continue;
                db.SampleValues.Add(new SampleValue
                {
                    Id = Guid.NewGuid(),
                    SampleSetId = exportSetId,
                    FieldDefinitionId = natureField.Id,
                    Value = value,
                    CreatedAt = stamp
                });
                stamp = stamp.AddMilliseconds(1);
            }
        }
    }

    private static void RemoveUnusedMachine(AppDbContext db, string code, string name)
    {
        var row = db.Consumers.FirstOrDefault(x => x.Code == code || x.Name == name)
            ?? db.Consumers.Local.FirstOrDefault(x => x.Code == code || x.Name == name);
        if (row is null)
            return;
        if (WarehouseUsed(db, row.Id)
            || db.Documents.Any(x => x.ConsumerId == row.Id || x.WarehouseId == row.Id || x.DestinationWarehouseId == row.Id))
            return;
        var warehouse = db.Warehouses.FirstOrDefault(x => x.Id == row.Id);
        if (warehouse is not null)
            db.Warehouses.Remove(warehouse);
        db.Consumers.Remove(row);
    }

    private static void RenameLegacyMachine(AppDbContext db, string oldCode, string oldName, string newCode, string newName)
    {
        if (db.Consumers.Any(x => x.Code == newCode || x.Name == newName)
            || db.Consumers.Local.Any(x => x.Code == newCode || x.Name == newName))
            return;
        var row = db.Consumers.FirstOrDefault(x => x.Code == oldCode || x.Name == oldName)
            ?? db.Consumers.Local.FirstOrDefault(x => x.Code == oldCode || x.Name == oldName);
        if (row is null)
            return;
        row.Code = newCode;
        row.Name = newName;
        row.Type = ConsumerType.Machine;
        var warehouse = db.Warehouses.FirstOrDefault(x => x.Id == row.Id);
        if (warehouse is not null)
            warehouse.Name = newName;
    }

    private static void EnsureShip(AppDbContext db, string name, Warehouse? reuse)
    {
        var row = db.Warehouses.FirstOrDefault(x => x.Name == name);
        if (row is null && reuse is not null)
        {
            reuse.Name = name;
            reuse.Type = WarehouseType.Auxiliary;
            reuse.Code = UniqueCode(db, reuse.Id, ShipCode(name));
            EnsureShipConsumer(db, reuse);
            return;
        }

        if (row is null)
        {
            row = new Warehouse
            {
                Id = Guid.NewGuid(),
                Name = name,
                Type = WarehouseType.Auxiliary,
                Code = UniqueCode(db, Guid.Empty, ShipCode(name))
            };
            db.Warehouses.Add(row);
            EnsureShipConsumer(db, row);
            return;
        }

        row.Type = WarehouseType.Auxiliary;
        EnsureShipConsumer(db, row);
    }

    private static void EnsureShipConsumer(AppDbContext db, Warehouse warehouse)
    {
        var fuelName = ShipFuels.FirstOrDefault(x => x.Name.Equals(warehouse.Name, StringComparison.OrdinalIgnoreCase)).Fuel;
        if (string.IsNullOrWhiteSpace(fuelName))
            fuelName = "Dầu DO 0,05S";
        var item = db.FuelItems.Local.FirstOrDefault(x => x.Name == fuelName)
            ?? db.FuelItems.FirstOrDefault(x => x.Name == fuelName);
        var groupId = item?.GroupId
            ?? db.ItemGroups.Local.FirstOrDefault(x => x.Name == (fuelName.Contains("Xăng", StringComparison.OrdinalIgnoreCase) ? "Xăng" : "Dầu"))?.Id
            ?? db.ItemGroups.FirstOrDefault(x => x.Name == (fuelName.Contains("Xăng", StringComparison.OrdinalIgnoreCase) ? "Xăng" : "Dầu"))?.Id;

        var consumer = db.Consumers.Local.FirstOrDefault(x => x.Id == warehouse.Id)
            ?? db.Consumers.FirstOrDefault(x => x.Id == warehouse.Id);
        if (consumer is null)
        {
            consumer = new Consumer
            {
                Id = warehouse.Id,
                Code = warehouse.Code,
                Name = warehouse.Name,
                Type = ConsumerType.Ship,
                DefaultGroupId = groupId,
                DefaultItemId = item?.Id,
                DefaultExportSampleSetId = warehouse.DefaultExportSampleSetId,
                MainMachineCount = 1,
                AuxMachineCount = 1,
                ShipType = "Tàu tuần tra"
            };
            db.Consumers.Add(consumer);
        }
        else
        {
            consumer.Code = warehouse.Code;
            consumer.Name = warehouse.Name;
            consumer.Type = ConsumerType.Ship;
            consumer.DefaultGroupId = groupId ?? consumer.DefaultGroupId;
            consumer.DefaultItemId = item?.Id ?? consumer.DefaultItemId;
            if (consumer.MainMachineCount <= 0)
                consumer.MainMachineCount = 1;
            if (consumer.AuxMachineCount < 0)
                consumer.AuxMachineCount = 0;
            if (string.IsNullOrWhiteSpace(consumer.ShipType))
                consumer.ShipType = "Tàu tuần tra";
        }

        EnsureShipNormSlots(db, warehouse.Id, consumer.DefaultGroupId ?? groupId);
    }

    private static void EnsureShipNormSlots(AppDbContext db, Guid consumerId, Guid? groupId)
    {
        if (groupId is null)
            return;
        var existing = db.ConsumerNormFactors.Local.Where(x => x.ConsumerId == consumerId)
            .Concat(db.ConsumerNormFactors.Where(x => x.ConsumerId == consumerId))
            .GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        if (existing.Count > 0 && !existing.Keys.Any(ShipNormSlots.IsKnown))
        {
            foreach (var old in existing.Values.ToList())
                db.ConsumerNormFactors.Remove(old);
            existing.Clear();
        }

        var order = 1;
        foreach (var label in ShipNormSlots.Labels)
        {
            if (existing.TryGetValue(label, out var row))
            {
                row.GroupId = groupId.Value;
                row.SortOrder = order;
                if (row.Value <= 0)
                    row.Value = DefaultShipNorm(label);
                order++;
                continue;
            }

            db.ConsumerNormFactors.Add(new ConsumerNormFactor
            {
                Id = Guid.NewGuid(),
                ConsumerId = consumerId,
                GroupId = groupId.Value,
                Name = label,
                Value = DefaultShipNorm(label),
                SortOrder = order
            });
            order++;
        }
    }

    private static decimal DefaultShipNorm(string label) => label switch
    {
        ShipNormSlots.AtBerth => 1m,
        ShipNormSlots.Cx25 => 2m,
        ShipNormSlots.Cx50 => 3m,
        ShipNormSlots.Cx75 => 4m,
        ShipNormSlots.Cx100 => 5m,
        ShipNormSlots.Aux => 1.5m,
        _ => 0m
    };

    private static void FillShipNormDefaults(AppDbContext db)
    {
        var diesel = db.ItemGroups.Local.FirstOrDefault(x => x.Name == "Dầu")
            ?? db.ItemGroups.FirstOrDefault(x => x.Name == "Dầu")
            ?? db.ItemGroups.FirstOrDefault(x => x.Name == "Xăng");
        foreach (var ship in db.Consumers.Local.Where(x => x.Type == ConsumerType.Ship)
                     .Concat(db.Consumers.Where(x => x.Type == ConsumerType.Ship)).GroupBy(x => x.Id).Select(g => g.First()))
        {
            if (ship.MainMachineCount <= 0)
                ship.MainMachineCount = 1;
            if (ship.AuxMachineCount < 0)
                ship.AuxMachineCount = 1;
            if (string.IsNullOrWhiteSpace(ship.ShipType))
                ship.ShipType = "Tàu tuần tra";
            EnsureShipNormSlots(db, ship.Id, ship.DefaultGroupId ?? diesel?.Id);
            if (ship.DefaultExportSampleSetId is null)
            {
                var export = db.SampleSets.Local.FirstOrDefault(x => x.Name == ExportSetName)
                    ?? db.SampleSets.FirstOrDefault(x => x.Name == ExportSetName);
                if (export is not null)
                    ship.DefaultExportSampleSetId = export.Id;
            }
        }
    }

    private static void SeedMissionYearLimits(AppDbContext db, int year)
    {
        MissionSeeder.Ensure(db);
        var tasks = db.MissionTasks.Local.Where(x => x.IsActive).Concat(db.MissionTasks.Where(x => x.IsActive))
            .GroupBy(x => x.Id).Select(g => g.First())
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .ToList();
        if (tasks.Count == 0)
            return;

        var existing = db.MissionYearLimits.Where(x => x.Year == year)
            .AsEnumerable()
            .Select(x => (x.TaskId, x.LotView))
            .Concat(db.MissionYearLimits.Local.Where(x => x.Year == year).Select(x => (x.TaskId, x.LotView)))
            .ToHashSet();
        var i = 0;
        foreach (var task in tasks)
        {
            // Hạn mức mẫu khác nhau theo nhiệm vụ và theo bảng TX+SSCĐ / IUU.
            var gasTx = 2_000m + i * 350m;
            var dieTx = 3_000m + i * 500m;
            var gasIuu = 400m + i * 80m;
            var dieIuu = 600m + i * 100m;
            if (task.GroupId == SeedIds.MissionGroupLoss)
            {
                gasTx = 200m + i * 50m;
                dieTx = 300m + i * 80m;
                gasIuu = 40m + i * 10m;
                dieIuu = 60m + i * 15m;
            }

            void Add(NxtLotViewMode view, decimal gas, decimal diesel)
            {
                var key = (task.Id, (int)view);
                if (!existing.Add(key))
                    return;
                db.MissionYearLimits.Add(new MissionYearLimit
                {
                    Id = Guid.NewGuid(),
                    TaskId = task.Id,
                    Year = year,
                    LotView = (int)view,
                    GasolineLimit = gas,
                    DieselLimit = diesel
                });
            }

            Add(NxtLotViewMode.TxSscd, gasTx, dieTx);
            Add(NxtLotViewMode.Iuu, gasIuu, dieIuu);
            i++;
        }
    }

    private static void PrepareSeedShip(AppDbContext db)
    {
        var seedShip = db.Consumers.Local.FirstOrDefault(x => x.Id == SeedIds.Ship)
            ?? db.Consumers.FirstOrDefault(x => x.Id == SeedIds.Ship);
        if (seedShip is null)
            return;
        var target = AuxiliaryShips[^1];
        seedShip.Name = target;
        seedShip.Code = ShipCode(target);
        seedShip.Type = ConsumerType.Ship;
        if (string.IsNullOrWhiteSpace(seedShip.ShipType))
            seedShip.ShipType = "Tàu tuần tra";
        if (seedShip.MainMachineCount <= 0)
            seedShip.MainMachineCount = 1;
        if (seedShip.AuxMachineCount < 0)
            seedShip.AuxMachineCount = 1;
        var wh = db.Warehouses.Local.FirstOrDefault(x => x.Id == SeedIds.Ship)
            ?? db.Warehouses.FirstOrDefault(x => x.Id == SeedIds.Ship);
        if (wh is not null)
        {
            wh.Name = target;
            wh.Type = WarehouseType.Auxiliary;
            wh.Code = UniqueCode(db, wh.Id, ShipCode(target));
        }
    }

    private static void TrimExtraConsumers(AppDbContext db)
    {
        var keepVehicles = Vehicles
            .Select(x => x.Plate.Replace(" ", "", StringComparison.Ordinal))
            .Concat(Vehicles.Select(x => x.Plate))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keepMachines = Machines
            .Select(x => x.Name.Replace(" ", "", StringComparison.Ordinal))
            .Concat(Machines.Select(x => x.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keepShips = AuxiliaryShips.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in db.Consumers.Local.Concat(db.Consumers).GroupBy(x => x.Id).Select(g => g.First()).ToList())
        {
            var keep = row.Type switch
            {
                ConsumerType.Vehicle => keepVehicles.Contains(row.Code) || keepVehicles.Contains(row.Name) || row.Id == SeedIds.Vehicle,
                ConsumerType.Machine => keepMachines.Contains(row.Code) || keepMachines.Contains(row.Name) || row.Id == SeedIds.Machine,
                ConsumerType.Ship => keepShips.Contains(row.Name) || row.Id == SeedIds.Ship,
                ConsumerType.Other => true,
                _ => true
            };
            if (keep)
                continue;
            if (WarehouseUsed(db, row.Id)
                || db.Documents.Any(x => x.ConsumerId == row.Id || x.WarehouseId == row.Id || x.DestinationWarehouseId == row.Id)
                || db.ShipQuarterBooks.Any(x => x.ConsumerId == row.Id)
                || db.ConsumerQuarterBooks.Any(x => x.ConsumerId == row.Id))
                continue;
            var warehouse = db.Warehouses.FirstOrDefault(x => x.Id == row.Id);
            if (warehouse is not null)
                db.Warehouses.Remove(warehouse);
            foreach (var factor in db.ConsumerNormFactors.Where(x => x.ConsumerId == row.Id).ToList())
                db.ConsumerNormFactors.Remove(factor);
            db.Consumers.Remove(row);
        }
    }

    private static string ShipCode(string name) =>
        name.Replace("Tàu BP ", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

    private static string UniqueCode(AppDbContext db, Guid ownerId, string code)
    {
        var candidate = code;
        var suffix = 2;
        while (db.Warehouses.Any(x => x.Code == candidate && x.Id != ownerId))
            candidate = code + "-" + suffix++;
        return candidate;
    }

    private static bool WarehouseUsed(AppDbContext db, Guid id) =>
        db.Documents.Any(x => x.WarehouseId == id || x.DestinationWarehouseId == id)
        || db.DocumentLines.Any(x => x.WarehouseId == id)
        || db.StockBalances.Any(x => x.WarehouseId == id)
        || db.StockMovements.Any(x => x.WarehouseId == id);

    private static void EnsurePaperFields(AppDbContext db)
    {
        var deliverer = db.FieldDefinitions.FirstOrDefault(x => x.Id == SeedIds.FieldDeliverer);
        if (deliverer is not null
            && deliverer.Name != "Người giao hàng"
            && !db.FieldDefinitions.Any(x => x.Family == deliverer.Family && x.Name == "Người giao hàng"))
            deliverer.Name = "Người giao hàng";

        foreach (var (family, name, sort) in SlipFieldCatalog.All())
            Field(db, family, name, sort);

        ClearExportDefaultRequired(db);
        ClearDefaultImportSamples(db);
        ClearNonSampleFieldValues(db);
    }

    /// <summary>Gỡ mẫu đã lưu cho trường không dùng dữ liệu mẫu (Có giá đến ngày, Số km).</summary>
    private static void ClearNonSampleFieldValues(AppDbContext db)
    {
        var fieldIds = db.FieldDefinitions.Local
            .Where(x => !SlipFieldCatalog.AllowsSampleValues(x.Name))
            .Select(x => x.Id)
            .Concat(db.FieldDefinitions.AsEnumerable().Where(x => !SlipFieldCatalog.AllowsSampleValues(x.Name)).Select(x => x.Id))
            .Distinct()
            .ToList();
        if (fieldIds.Count == 0)
            return;

        var local = db.SampleValues.Local.Where(x => fieldIds.Contains(x.FieldDefinitionId)).ToList();
        if (local.Count > 0)
            db.SampleValues.RemoveRange(local);
        var stored = db.SampleValues.Where(x => fieldIds.Contains(x.FieldDefinitionId)).ToList();
        if (stored.Count > 0)
            db.SampleValues.RemoveRange(stored);
    }

    /// <summary>Một lần: bỏ bắt buộc mặc định «Người nhận» phiếu xuất (có thể bật lại trong danh mục).</summary>
    private static void ClearExportDefaultRequired(AppDbContext db)
    {
        const string key = "Patch.ExportReceiverOptional";
        if (db.AppSettings.Any(x => x.Key == key))
            return;

        foreach (var row in db.FieldDefinitions.Where(x =>
                     x.Family == DocumentFamily.Export && x.Name == "Người nhận" && x.IsRequired))
            row.IsRequired = false;

        db.AppSettings.Add(new AppSetting { Key = key, Value = "1" });
    }

    /// <summary>Một lần: gỡ mẫu nhập mặc định — đã dùng Phiếu mẫu.</summary>
    private static void ClearDefaultImportSamples(AppDbContext db)
    {
        const string key = "Patch.ClearDefaultImportSample";
        if (db.AppSettings.Any(x => x.Key == key))
            return;

        foreach (var row in db.Warehouses.Where(x => x.DefaultImportSampleSetId != null))
            row.DefaultImportSampleSetId = null;
        foreach (var row in db.Consumers.Where(x => x.DefaultImportSampleSetId != null))
            row.DefaultImportSampleSetId = null;

        db.AppSettings.Add(new AppSetting { Key = key, Value = "1" });
    }

    private static void SeedFullPaperSamples(AppDbContext db)
    {
        var stamp = DateTime.UtcNow;
        foreach (var (setName, family) in new[] { (ImportSetName, DocumentFamily.Import), (ExportSetName, DocumentFamily.Export) })
        {
            var set = db.SampleSets.Local.FirstOrDefault(x => x.Name == setName && x.Family == family)
                ?? db.SampleSets.FirstOrDefault(x => x.Name == setName && x.Family == family);
            if (set is null)
                continue;

            AddMissing(db, set.Id, family, "Cơ quan", ["BỘ ĐỘI BIÊN PHÒNG"], ref stamp);
            AddMissing(db, set.Id, family, "Đơn vị", ["HẢI ĐOÀN BIÊN PHÒNG 18"], ref stamp);
            AddMissing(db, set.Id, family, "Đơn vị vận chuyển", ["Xe kho", "Tàu cấp"], ref stamp);
            AddMissing(db, set.Id, family, "Giấy giới thiệu và CMT", ["Giấy GT số 01", "CMT 001"], ref stamp);
            AddMissing(db, set.Id, family, "Số xe", Plates, ref stamp);
            AddMissing(db, set.Id, family, "Dung tích kiểm định", ["1000", "5000"], ref stamp);
            AddMissing(db, set.Id, family, "Dung tích nhận hàng", ["1000", "5000"], ref stamp);
            AddMissing(db, set.Id, family, "Số lượng bao bì", ["1", "2", "4"], ref stamp);
            AddMissing(db, set.Id, family, "Nhiệm vụ", Missions, ref stamp);
            AddMissing(db, set.Id, family, "Ghi chú", ["Theo lệnh", "Bổ sung tồn"], ref stamp);
            AddMissing(db, set.Id, family, "Mã số", ["NL-01", "NL-02"], ref stamp);
            AddMissing(db, set.Id, family, "Chất lượng", ["1", "2"], ref stamp);
            AddMissing(db, set.Id, family, "Chữ ký người giao", Deliverers, ref stamp);
            AddMissing(db, set.Id, family, "Chữ ký người nhận", People.Take(6), ref stamp);
            AddMissing(db, set.Id, family, "Chữ ký tài chính", ["Tài chính kho"], ref stamp);
            AddMissing(db, set.Id, family, "Chữ ký người viết phiếu", ["Thủ kho"], ref stamp);
            AddMissing(db, set.Id, family, "Chữ ký trưởng ban HC-KT", ["Trưởng ban HC-KT"], ref stamp);
            AddMissing(db, set.Id, family, "Chữ ký chỉ huy đơn vị", ["Chỉ huy đơn vị"], ref stamp);

            if (family == DocumentFamily.Import)
            {
                AddMissing(db, set.Id, family, "Tính chất nhập", ImportNatures, ref stamp);
                AddMissing(db, set.Id, family, "Đơn vị giao hàng", Senders, ref stamp);
                AddMissing(db, set.Id, family, "Đơn vị nhận hàng", ImportReceiverUnits, ref stamp);
                AddMissing(db, set.Id, family, "Người nhận", People.Take(8), ref stamp);
                AddMissing(db, set.Id, family, "Người giao hàng", Deliverers, ref stamp);
                AddMissing(db, set.Id, family, "Theo hợp đồng số", ["HĐ-2026/01", "HĐ-2026/02"], ref stamp);
            }
            else
            {
                AddMissing(db, set.Id, family, "Tính chất xuất", ExportNatures, ref stamp);
                AddMissing(db, set.Id, family, "Đơn vị giao", ["Kho Hải đoàn Biên phòng 18"], ref stamp);
                AddMissing(db, set.Id, family, "Đơn vị nhận", ReceiverUnits, ref stamp);
                AddMissing(db, set.Id, family, "Người nhận", People.Take(8), ref stamp);
                AddMissing(db, set.Id, family, "Theo lệnh (KH)", ["Lệnh 01", "Lệnh 02", "KH quý"], ref stamp);
            }
        }

        // Phiếu mẫu gắn SeedIds (dùng khi chưa chạy Excel set).
        SeedLegacySampleSet(db, SeedIds.SampleImport, DocumentFamily.Import, ref stamp);
        SeedLegacySampleSet(db, SeedIds.SampleExport, DocumentFamily.Export, ref stamp);
    }

    private static void SeedLegacySampleSet(AppDbContext db, Guid setId, DocumentFamily family, ref DateTime stamp)
    {
        if (!db.SampleSets.Any(x => x.Id == setId) && !db.SampleSets.Local.Any(x => x.Id == setId))
            return;
        AddMissing(db, setId, family, "Cơ quan", ["BỘ ĐỘI BIÊN PHÒNG"], ref stamp);
        AddMissing(db, setId, family, "Đơn vị", ["HẢI ĐOÀN BIÊN PHÒNG 18"], ref stamp);
        AddMissing(db, setId, family, "Nhiệm vụ", Missions.Take(6), ref stamp);
        AddMissing(db, setId, family, "Số xe", Plates, ref stamp);
        AddMissing(db, setId, family, "Chất lượng", ["1"], ref stamp);
        if (family == DocumentFamily.Import)
        {
            AddMissing(db, setId, family, "Tính chất nhập", ImportNatures.Take(3), ref stamp);
            AddMissing(db, setId, family, "Người giao hàng", Deliverers, ref stamp);
        }
        else
        {
            AddMissing(db, setId, family, "Tính chất xuất", ExportNatures.Take(8), ref stamp);
            AddMissing(db, setId, family, "Người nhận", ["Tổ máy", "Lái xe", "Thuyền trưởng"], ref stamp);
        }
    }

    private static void SeedHeaderSamples(AppDbContext db) => SeedFullPaperSamples(db);

    private static void TouchFuel(AppDbContext db, string name, decimal vcf, decimal density, decimal temperature)
    {
        var item = db.FuelItems.FirstOrDefault(x => x.Name == name);
        if (item is null)
            return;
        item.Vcf = vcf;
        item.Density = density;
        item.Temperature = temperature;
        item.QualityInfo = "1";
    }

    private static void AddItem(AppDbContext db, string name, Guid groupId, Guid unitId, decimal vcf, decimal? density, decimal? temperature)
    {
        if (db.FuelItems.Any(x => x.Name == name))
            return;
        db.FuelItems.Add(new FuelItem
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            UnitId = unitId,
            Name = name,
            QualityInfo = "1",
            Density = density,
            Temperature = temperature,
            Vcf = vcf,
            ConversionRule = Labels.DefaultConversionRule
        });
    }

    private static Guid Group(AppDbContext db, string name)
    {
        var scope = name.Contains("PTKT", StringComparison.OrdinalIgnoreCase)
            ? WarehouseScope.Ptkt
            : WarehouseScope.Xd;
        var row = db.ItemGroups.Local.FirstOrDefault(x => x.Name == name)
            ?? db.ItemGroups.FirstOrDefault(x => x.Name == name);
        if (row is not null)
        {
            if (row.Scope != scope)
                row.Scope = scope;
            return row.Id;
        }

        row = new ItemGroup { Id = Guid.NewGuid(), Name = name, Scope = scope };
        db.ItemGroups.Add(row);
        return row.Id;
    }

    private static Guid Unit(AppDbContext db, string name)
    {
        var row = db.MeasureUnits.FirstOrDefault(x => x.Name == name);
        if (row is not null)
            return row.Id;
        row = new MeasureUnit { Id = Guid.NewGuid(), Name = name };
        db.MeasureUnits.Add(row);
        return row.Id;
    }

    private static FieldDefinition? FindField(AppDbContext db, DocumentFamily family, string name) =>
        db.FieldDefinitions.Local.FirstOrDefault(x => x.Family == family && x.Name == name)
        ?? db.FieldDefinitions.FirstOrDefault(x => x.Family == family && x.Name == name);

    private static Guid EnsureRequired(AppDbContext db, DocumentFamily family, string name, int sort)
    {
        var row = FindField(db, family, name);
        if (row is not null)
            return row.Id;
        row = new FieldDefinition
        {
            Id = Guid.NewGuid(),
            Family = family,
            Name = name,
            DataType = FieldDataType.Text,
            IsRequired = true,
            IsVisible = true,
            SortOrder = sort
        };
        db.FieldDefinitions.Add(row);
        return row.Id;
    }

    private static Guid Field(AppDbContext db, DocumentFamily family, string name, int sort)
    {
        var row = FindField(db, family, name);
        if (row is not null)
            return row.Id;
        row = new FieldDefinition
        {
            Id = Guid.NewGuid(),
            Family = family,
            Name = name,
            DataType = FieldDataType.Text,
            IsRequired = false,
            IsVisible = true,
            SortOrder = sort
        };
        db.FieldDefinitions.Add(row);
        return row.Id;
    }

    private static void Add(AppDbContext db, Guid setId, Guid fieldId, IEnumerable<string> values, ref DateTime stamp)
    {
        foreach (var value in values.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct())
        {
            stamp = stamp.AddSeconds(1);
            db.SampleValues.Add(new SampleValue
            {
                Id = Guid.NewGuid(),
                SampleSetId = setId,
                FieldDefinitionId = fieldId,
                Value = value,
                CreatedAt = stamp
            });
        }
    }

    private static void AddMissing(AppDbContext db, Guid setId, DocumentFamily family, string fieldName, IEnumerable<string> values, ref DateTime stamp)
    {
        if (!SlipFieldCatalog.AllowsSampleValues(fieldName))
            return;
        var fieldId = FindField(db, family, fieldName)?.Id;
        if (fieldId is null)
            return;
        var existing = db.SampleValues.Local
            .Where(x => x.SampleSetId == setId && x.FieldDefinitionId == fieldId)
            .Select(x => x.Value)
            .ToHashSet();
        foreach (var value in db.SampleValues
            .Where(x => x.SampleSetId == setId && x.FieldDefinitionId == fieldId)
            .Select(x => x.Value))
            existing.Add(value);
        foreach (var value in values.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct())
        {
            if (!existing.Add(value))
                continue;
            stamp = stamp.AddSeconds(1);
            db.SampleValues.Add(new SampleValue
            {
                Id = Guid.NewGuid(),
                SampleSetId = setId,
                FieldDefinitionId = fieldId.Value,
                Value = value,
                CreatedAt = stamp
            });
        }
    }

    private static readonly string[] Lubes =
    [
        "Đinh tự AY", "Hydraulic VG 32", "Vanellus Monogarde 40", "MiLPC 01", "MiLPC 03", "MiLPC 04",
        "Rimula R4X", "Shell Omala G100", "Mobil Gard", "Niwa HG32(PC05)", "Fluid DOT 3",
        "Total Caprano TDI", "Total Quazt VNM"
    ];

    private static readonly string[] Greases = ["M1-13", "Solidol", "Opal", "Grease GL3", "PP 95/5"];

    private static readonly string[] ImportNatures =
    [
        "Mua đấu thầu kinh phí IUU", "Nhập hiện vật", "Nhập hiện vật PTKT-VTXD",
        "Mua xăng lẻ", "Mua dầu lẻ", "Mua công ty"
    ];

    private static readonly string[] ExportNatures =
    [
        "Phục vụ chỉ huy (CH)", "Phục vụ chỉ huy (CT)", "Phục vụ chỉ huy (HC)",
        "Liên hệ công tác (TM)", "Liên hệ công tác (CT)", "Liên hệ công tác (HC)",
        "Liên hệ công tác (KT)", "Liên hệ công tác (TS)",
        "Cấp Biên đội", "Cấp tàu 19-01", "Cấp tàu 01-01", "Cấp tàu 05-01",
        "Cấp bổ sung tồn tàu", "Cấp ứng làm NV IUU", "Cấp bổ sung NV IUU", "Thi KNN",
        "Nổ máy QY", "Nổ máy DT", "Nổ máy TT",
        "Bảo dưỡng kỹ thuật", "Hao hụt định mức", "Cưa cây, cắt cỏ", "Nhiệm vụ khác", "Nhập hiện vật"
    ];

    private static readonly string[] People =
    [
        "Trần Văn Thanh", "U1CN Trần Lý Vĩ", "U2CN Trần Lý Vĩ", "Nguyễn Đình Quân", "Nguyễn Văn Nhâm",
        "Nguyễn Công Hiển", "Bùi Văn Sơn", "T2 Bùi Văn Sơn", "Bùi Văn Long", "Trần Ngọc Thạch",
        "Nguyễn Bá Ngọc", "Nguyễn Bảo Hà", "Phan Anh Minh", "Đinh Bạt Anh", "Nguyễn Huy Hoàng",
        "Phạm Kim Lịch", "Đỗ Văn Hải", "T1CN Đỗ Văn Hải", "Trương Đình Vũ", "Bùi Phước Luýt",
        "Nguyễn Thành Trung", "Nguyễn Thái Học", "Nguyễn Văn Kiên", "Nguyễn Thế Nam", "Mai Văn Tùng",
        "Vũ Tiến Thành", "Nguyễn Văn Sáng", "Đoàn Văn Hùng", "Trương Xuân Dụng", "Phạm Văn Thứ",
        "Trần Hữu Hoàng", "U1CN Lê Tài"
    ];

    private static readonly string[] Deliverers = ["Nguyễn Anh Toàn", "Phạm Văn Thứ", "T1CN Trương Xuân Dụng"];

    private static readonly string[] Senders = ["Công ty CP DK QT TPP", "Phân kho 101 Phía Nam", "Hải đoàn Biên phòng 18"];

    private static readonly string[] ImportReceiverUnits = ["Kho Hải đoàn Biên phòng 18", "Hải đoàn Biên phòng 18", "Hải đoàn 18"];

    private static readonly string[] ReceiverUnits =
    [
        "Tàu BP 27-19-01", "Tàu BP 27-01-01", "Tàu BP 27-05-01",
        "Máy QY", "Máy DT", "Máy TT",
        "Quân y", "Doanh trại", "Cơ yếu", "Thông tin", "Thủ kho", "Lái xe", "Biên đội"
    ];

    private static readonly string[] Plates =
    [
        "QB 34-75", "QB 41-27", "QB 38-09",
        "Máy QY", "Máy DT", "Máy TT"
    ];

    private static readonly string[] Missions =
    [
        "TC, CH", "Cơ yếu", "HL CĐ", "Thông tin", "Chính trị", "Trinh sát", "Ma tuý", "Hậu cần",
        "Kỹ thuật", "KT tàu", "Hao hụt", "Bảo vệ DK", "Đi đà", "HL KT", "A80", "IUU", "HL K3", "Thi KNN"
    ];

    private static readonly (string Plate, string Fuel, decimal? PerHundredKm)[] Vehicles =
    [
        ("QB 34-75", "Xăng RON 92", 18m),
        ("QB 41-27", "Dầu DO 0,05S", 14m),
        ("QB 38-09", "Dầu DO 0,05S", 36m)
    ];

    /// <summary>Mỗi máy chỉ 1 loại NL (xăng hoặc dầu).</summary>
    private static readonly (string Name, string Fuel)[] Machines =
    [
        ("Máy QY", "Dầu DO 0,05S"),
        ("Máy DT", "Dầu DO 0,05S"),
        ("Máy TT", "Xăng RON 95")
    ];
}
