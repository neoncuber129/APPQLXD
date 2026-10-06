using APPQLXD.Core.Calculations;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Core.Persistence;
using APPQLXD.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace APPQLXD.Core;

public sealed class FuelSystem
{
    private static readonly HashSet<string> KnownTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "ItemGroups", "MeasureUnits", "LotTypes", "FuelItems", "Warehouses", "Consumers", "ConsumerNormFactors",
        "FieldDefinitions", "SampleSets", "SampleValues", "AppSettings", "Lots", "StockBalances",
        "StockMovements", "Documents", "DocumentLines", "DocumentFields", "ShipQuarterBooks", "ShipQuarterBookLines",
        "ConsumerQuarterBooks", "ConsumerQuarterBookLines"
    };

    private readonly CatalogStore _catalog;
    private readonly FuelLedger _ledger;
    private readonly ShipQuarterBooks _shipBooks;
    private readonly string _connectionString;
    private readonly bool _seed;

    public FuelSystem(string databasePath, bool seed = true)
    {
        DatabasePath = databasePath;
        _seed = seed;
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, DefaultTimeout = 5 }.ToString();
        PrepareDatabase();
        AppDbContext Factory() => new(_connectionString);
        _catalog = new CatalogStore(Factory);
        _ledger = new FuelLedger(Factory);
        _shipBooks = new ShipQuarterBooks(Factory);
        _catalog.EnsureItemGroupScopes();
    }

    public string DatabasePath { get; }

    private static readonly string[] ActivityTables =
    [
        "ShipQuarterBookLines", "ShipQuarterBooks",
        "ConsumerQuarterBookLines", "ConsumerQuarterBooks",
        "StockMovements", "StockBalances", "DocumentFields", "DocumentLines", "Documents", "Lots"
    ];

    private static readonly string[] CatalogTables =
    [
        "MeasureUnits", "ItemGroups", "FieldDefinitions", "Warehouses", "SampleSets", "FuelItems", "Consumers", "ConsumerNormFactors", "SampleValues", "AppSettings"
    ];

    public FuelResult Backup(string destinationPath)
    {
        try
        {
            CopyDatabase(destinationPath);
            return FuelResult.Success(Guid.Empty, "Đã sao lưu toàn bộ dữ liệu.");
        }
        catch (Exception ex)
        {
            return FuelResult.Fail("Không sao lưu được: " + ex.Message);
        }
    }

    public FuelResult BackupCatalog(string destinationPath)
    {
        try
        {
            CopyDatabase(destinationPath);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destinationPath }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            foreach (var table in ActivityTables)
            {
                command.CommandText = $"DELETE FROM \"{table}\";";
                command.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();
            return FuelResult.Success(Guid.Empty, "Đã sao lưu danh mục.");
        }
        catch (Exception ex)
        {
            SqliteConnection.ClearAllPools();
            return FuelResult.Fail("Không sao lưu danh mục được: " + ex.Message);
        }
    }

    public FuelResult ClearActivity()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            using var db = new AppDbContext(_connectionString);
            db.Database.OpenConnection();
            db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
            using var tx = db.Database.BeginTransaction();
            foreach (var table in ActivityTables)
                db.Database.ExecuteSqlRaw($"DELETE FROM \"{table}\";");
            tx.Commit();
            _catalog.InvalidateCache();
            return FuelResult.Success(Guid.Empty, "Đã xoá chứng từ và tồn kho. Danh mục được giữ.");
        }
        catch (Exception ex)
        {
            return FuelResult.Fail("Không xoá chứng từ được: " + ex.Message);
        }
    }

    public FuelResult Restore(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            return FuelResult.Fail("Không thấy tệp sao lưu.");
        var source = Path.GetFullPath(sourcePath);
        var target = Path.GetFullPath(DatabasePath);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            return FuelResult.Fail("Hãy chọn tệp sao lưu khác cơ sở dữ liệu đang mở.");
        if (!LooksLikeSqlite(source))
            return FuelResult.Fail("Tệp không phải cơ sở dữ liệu SQLite.");
        if (!IsAppDatabase(source, out var reason))
            return FuelResult.Fail(reason);

        var safety = target + ".before-restore";
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(target))
                File.Copy(target, safety, true);
            File.Copy(source, target, true);
            DeleteSidecars(target);
            var added = PrepareDatabase();
            _catalog.InvalidateCache();
            var message = added > 0
                ? $"Đã khôi phục dữ liệu. Đã bổ sung {added} bảng hoặc cột còn thiếu."
                : "Đã khôi phục dữ liệu.";
            return FuelResult.Success(Guid.Empty, message);
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(safety))
                {
                    SqliteConnection.ClearAllPools();
                    File.Copy(safety, target, true);
                    DeleteSidecars(target);
                    PrepareDatabase();
                }
            }
            catch
            {
                /* giữ nguyên thông báo lỗi khôi phục */
            }

            return FuelResult.Fail("Không khôi phục được: " + ex.Message);
        }
    }

    public FuelResult RestoreCatalog(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            return FuelResult.Fail("Không thấy tệp sao lưu danh mục.");
        var source = Path.GetFullPath(sourcePath);
        var target = Path.GetFullPath(DatabasePath);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            return FuelResult.Fail("Hãy chọn tệp sao lưu khác cơ sở dữ liệu đang mở.");
        if (!LooksLikeSqlite(source))
            return FuelResult.Fail("Tệp không phải cơ sở dữ liệu SQLite.");
        if (!IsAppDatabase(source, out var reason))
            return FuelResult.Fail(reason);

        try
        {
            SqliteConnection.ClearAllPools();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "ATTACH '" + source.Replace("'", "''") + "' AS cat;";
            command.ExecuteNonQuery();
            command.CommandText = "SELECT 1 FROM cat.sqlite_master WHERE type = 'table' AND name = 'ItemGroups' LIMIT 1;";
            if (command.ExecuteScalar() is null)
            {
                command.CommandText = "DETACH cat;";
                command.ExecuteNonQuery();
                return FuelResult.Fail("Tệp không có danh mục.");
            }

            if (MissingCatalogIds(command, "Warehouses", """
                SELECT WarehouseId AS Id FROM StockBalances
                UNION SELECT WarehouseId FROM Documents WHERE WarehouseId IS NOT NULL
                UNION SELECT DestinationWarehouseId FROM Documents WHERE DestinationWarehouseId IS NOT NULL
                UNION SELECT WarehouseId FROM DocumentLines
                """))
            {
                command.CommandText = "DETACH cat;";
                command.ExecuteNonQuery();
                return FuelResult.Fail("Danh mục sao lưu thiếu kho đang có chứng từ hoặc tồn. Hãy xoá chứng từ trước, hoặc khôi phục toàn bộ.");
            }

            if (MissingCatalogIds(command, "FuelItems", """
                SELECT ItemId AS Id FROM Lots WHERE ItemId IS NOT NULL
                UNION SELECT ItemId FROM Documents WHERE ItemId IS NOT NULL
                UNION SELECT ItemId FROM DocumentLines WHERE ItemId IS NOT NULL
                """))
            {
                command.CommandText = "DETACH cat;";
                command.ExecuteNonQuery();
                return FuelResult.Fail("Danh mục sao lưu thiếu mặt hàng đang có chứng từ hoặc lô. Hãy xoá chứng từ trước, hoặc khôi phục toàn bộ.");
            }

            command.CommandText = "PRAGMA foreign_keys = OFF;";
            command.ExecuteNonQuery();
            using var tx = connection.BeginTransaction();
            command.Transaction = tx;
            foreach (var table in CatalogTables.Reverse())
            {
                if (!CatalogTableExists(command, table))
                    continue;
                command.CommandText = $"DELETE FROM \"{table}\";";
                command.ExecuteNonQuery();
            }

            foreach (var table in CatalogTables)
            {
                if (!CatalogTableExists(command, table))
                    continue;
                command.CommandText = $"INSERT INTO \"{table}\" SELECT * FROM cat.\"{table}\";";
                command.ExecuteNonQuery();
            }

            tx.Commit();
            command.Transaction = null;
            command.Parameters.Clear();
            command.CommandText = "DETACH cat;";
            command.ExecuteNonQuery();
            PrepareDatabase();
            _catalog.InvalidateCache();
            return FuelResult.Success(Guid.Empty, "Đã khôi phục danh mục. Chứng từ và tồn được giữ.");
        }
        catch (Exception ex)
        {
            return FuelResult.Fail("Không khôi phục danh mục được: " + ex.Message);
        }
    }

    private static bool CatalogTableExists(SqliteCommand command, string table)
    {
        command.Parameters.Clear();
        command.CommandText = "SELECT 1 FROM cat.sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", table);
        var exists = command.ExecuteScalar() is not null;
        command.Parameters.Clear();
        return exists;
    }

    private static bool MissingCatalogIds(SqliteCommand command, string catalogTable, string referencedIds)
    {
        command.Parameters.Clear();
        command.CommandText = $"SELECT COUNT(*) FROM ({referencedIds}) AS used WHERE used.Id NOT IN (SELECT Id FROM cat.\"{catalogTable}\");";
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private void CopyDatabase(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        Checkpoint();
        File.Copy(DatabasePath, destinationPath, true);
        DeleteSidecars(destinationPath);
    }

    private int PrepareDatabase()
    {
        using var db = new AppDbContext(_connectionString);
        db.Database.EnsureCreated();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode=WAL;";
            command.ExecuteScalar();
        }

        var added = SchemaPatch.Apply(db);
        MissionSeeder.Ensure(db);
        if (_seed && !db.ItemGroups.Any())
        {
            using var tx = db.Database.BeginTransaction();
            DataSeeder.Seed(db);
            ExcelSampleData.Ensure(db);
            tx.Commit();
        }
        new CatalogStore(() => new AppDbContext(_connectionString)).EnsureItemGroupScopes();
        return added;
    }

    private void Checkpoint()
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        command.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }

    private static bool LooksLikeSqlite(string path)
    {
        Span<byte> header = stackalloc byte[16];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return stream.Read(header) == header.Length && header.SequenceEqual("SQLite format 3\0"u8);
    }

    private static bool IsAppDatabase(string path, out string reason)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
            using var reader = command.ExecuteReader();
            var names = new List<string>();
            while (reader.Read())
                names.Add(reader.GetString(0));
            if (names.Count == 0 || names.Any(KnownTables.Contains))
            {
                reason = "";
                return true;
            }
        }
        catch (Exception ex)
        {
            reason = "Không đọc được tệp sao lưu: " + ex.Message;
            return false;
        }

        reason = "Tệp không phải cơ sở dữ liệu của ứng dụng.";
        return false;
    }

    public FuelResult LoadDemoActivity(IProgress<DemoProgress>? progress = null) => DemoActivity.Load(this, progress);

    public FuelResult ClearAll()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            using var db = new AppDbContext(_connectionString);
            db.Database.OpenConnection();
            db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
            using var tx = db.Database.BeginTransaction();
            foreach (var table in AllDataTables)
                db.Database.ExecuteSqlRaw($"DELETE FROM \"{table}\";");
            tx.Commit();
            _catalog.InvalidateCache();
            return FuelResult.Success(Guid.Empty, "Đã xoá toàn bộ chứng từ, tồn kho và danh mục.");
        }
        catch (Exception ex)
        {
            return FuelResult.Fail("Không xoá toàn bộ được: " + ex.Message);
        }
    }

    /// <summary>Nạp toàn bộ danh mục mẫu từ Excel (mặt hàng, kho, phương tiện xe/máy/tàu, phiếu mẫu, hạn mức).</summary>
    public FuelResult LoadSampleCatalog()
    {
        try
        {
            using var db = new AppDbContext(_connectionString);
            using var tx = db.Database.BeginTransaction();
            DataSeeder.Seed(db);
            ExcelSampleData.Ensure(db);
            MissionSeeder.Ensure(db);
            tx.Commit();
            _catalog.InvalidateCache();
            _catalog.EnsureItemGroupScopes();
            return FuelResult.Success(Guid.Empty, "Đã tạo danh mục mẫu từ Excel thành công.");
        }
        catch (Exception ex)
        {
            return FuelResult.Fail("Không tạo được danh mục mẫu: " + ex.Message);
        }
    }

    /// <summary>Giữ tên cũ cho tương thích; giờ chỉ xoá sạch, không nạp Excel.</summary>
    public FuelResult ResetFromExcel() => ClearAll();

    private static readonly string[] AllDataTables =
    [
        "ShipQuarterBookLines", "ShipQuarterBooks",
        "ConsumerQuarterBookLines", "ConsumerQuarterBooks",
        "StockMovements", "StockBalances",
        "DocumentFields", "DocumentLines", "Documents", "Lots",
        "SampleValues", "ConsumerNormFactors", "Consumers", "Warehouses",
        "FuelItems", "SampleSets", "FieldDefinitions", "ItemGroups", "MeasureUnits",
        "MissionYearLimits", "AppSettings"
    ];

    private static void DeleteSidecars(string databasePath)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public static FuelSystem CreateDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appqlxd.db");
        BringLocalDatabase(path);
        return new FuelSystem(path, seed: false);
    }

    private static void BringLocalDatabase(string path)
    {
        if (File.Exists(path))
            return;
        var previous = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "APPQLXD", "appqlxd.db");
        if (!File.Exists(previous))
            return;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = previous }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
            File.Copy(previous, path);
        }
        catch (IOException)
        {
        }
        catch (SqliteException)
        {
        }
    }

    public IReadOnlyList<GroupRow> GetGroups() => _catalog.GetGroups();
    public FuelResult SaveGroup(Guid? id, string name) => _catalog.SaveGroup(id, name);
    public FuelResult DeleteGroup(Guid id) => _catalog.DeleteGroup(id);
    public IReadOnlyList<UnitRow> GetUnits() => _catalog.GetUnits();
    public FuelResult SaveUnit(Guid? id, string name) => _catalog.SaveUnit(id, name);
    public FuelResult DeleteUnit(Guid id) => _catalog.DeleteUnit(id);
    public IReadOnlyList<LotTypeRow> GetLotTypes(bool activeOnly = true) => _catalog.GetLotTypes(activeOnly);
    public FuelResult SaveLotType(Guid? id, string code, string name, int? sortOrder = null) => _catalog.SaveLotType(id, code, name, sortOrder);
    public FuelResult DeleteLotType(Guid id) => _catalog.DeleteLotType(id);

    public IReadOnlyList<MissionGroupRow> GetMissionGroups() => _catalog.GetMissionGroups();
    public IReadOnlyList<MissionTaskRow> GetMissionTasks(Guid? groupId = null, bool activeOnly = true) =>
        _catalog.GetMissionTasks(groupId, activeOnly);
    public FuelResult SaveMissionGroup(Guid? id, string code, string name, bool isLossGroup, int? sortOrder = null) =>
        _catalog.SaveMissionGroup(id, code, name, isLossGroup, sortOrder);
    public FuelResult DeleteMissionGroup(Guid id) => _catalog.DeleteMissionGroup(id);
    public FuelResult SaveMissionTask(Guid? id, Guid groupId, string name, int? sortOrder = null, bool isActive = true) =>
        _catalog.SaveMissionTask(id, groupId, name, sortOrder, isActive);
    public FuelResult DeleteMissionTask(Guid id) => _catalog.DeleteMissionTask(id);
    public IReadOnlyList<MissionYearLimitRow> GetMissionYearLimits(int year, NxtLotViewMode? lotView = null) =>
        _catalog.GetMissionYearLimits(year, lotView);
    public FuelResult SaveMissionYearLimit(Guid taskId, int year, NxtLotViewMode lotView, decimal gasolineLimit, decimal dieselLimit) =>
        _catalog.SaveMissionYearLimit(taskId, year, lotView, gasolineLimit, dieselLimit);
    public IReadOnlyList<ItemRow> GetItems() => _catalog.GetItems();
    public ItemRow? GetItem(Guid id) => _catalog.GetItem(id);
    public FuelResult SaveItem(ItemEdit edit) => _catalog.SaveItem(edit);
    public FuelResult DeleteItem(Guid id) => _catalog.DeleteItem(id);
    public int CatalogGeneration => _catalog.CatalogGeneration;
    public IReadOnlyList<WarehouseRow> GetWarehouses() => _catalog.GetWarehouses();
    public IReadOnlyList<WarehouseRow> GetCatalogWarehouses() => _catalog.GetCatalogWarehouses();
    public FuelResult SaveWarehouse(WarehouseEdit edit) => _catalog.SaveWarehouse(edit);
    public FuelResult DeleteWarehouse(Guid id) => _catalog.DeleteWarehouse(id);
    public IReadOnlyList<ConsumerRow> GetConsumers() => _catalog.GetConsumers();
    public FuelResult SaveConsumer(ConsumerEdit edit) => _catalog.SaveConsumer(edit);
    public FuelResult RememberPreferredFuel(Guid consumerId, Guid itemId) => _catalog.RememberPreferredFuel(consumerId, itemId);
    public FuelResult DeleteConsumer(Guid id) => _catalog.DeleteConsumer(id);
    public bool GetExtraFieldsOnSlip() => _catalog.GetExtraFieldsOnSlip();
    public void SetExtraFieldsOnSlip(bool enabled) => _catalog.SetExtraFieldsOnSlip(enabled);
    public WarehouseScope GetWarehouseScope() => _catalog.GetWarehouseScope();
    public void SetWarehouseScope(WarehouseScope scope) => _catalog.SetWarehouseScope(scope);
    public IReadOnlyList<FieldRow> GetFields(DocumentFamily? family = null) => _catalog.GetFields(family);
    public FuelResult SaveField(FieldEdit edit) => _catalog.SaveField(edit);
    public FuelResult DeleteField(Guid id) => _catalog.DeleteField(id);
    public FuelResult EnsureOwnedExportSample(bool warehouse, Guid ownerId) => _catalog.EnsureOwnedExportSample(warehouse, ownerId);
    public FuelResult EnsureCatalogSample(DocumentFamily family) => _catalog.EnsureCatalogSample(family);
    public IReadOnlyList<SampleSetRow> GetSampleSets(DocumentFamily? family = null) => _catalog.GetSampleSets(family);
    public FuelResult SaveSampleSet(Guid? id, string name, DocumentFamily family) => _catalog.SaveSampleSet(id, name, family);
    public FuelResult DeleteSampleSet(Guid id) => _catalog.DeleteSampleSet(id);
    public IReadOnlyList<SampleValueRow> GetSampleValues(Guid sampleSetId) => _catalog.GetSampleValues(sampleSetId);
    public IReadOnlyDictionary<string, string> GetOwnedExportDefaults(Guid consumerId) => _catalog.GetOwnedExportDefaults(consumerId);
    public IReadOnlyList<SampleValueRow> GetSharedExportValues() => _catalog.GetSharedExportValues();
    public FuelResult SaveOwnedSample(Guid setId, IReadOnlyList<(Guid FieldId, string Value)> fields) => _catalog.SaveOwnedSample(setId, fields);
    public FuelResult AddSampleValue(Guid sampleSetId, Guid fieldId, string value) => _catalog.AddSampleValue(sampleSetId, fieldId, value);
    public FuelResult UpdateSampleValue(Guid id, string value) => _catalog.UpdateSampleValue(id, value);
    public FuelResult DeleteSampleValue(Guid id) => _catalog.DeleteSampleValue(id);
    public IReadOnlyList<string> GetFieldOptions(Guid fieldId) => _catalog.GetFieldOptions(fieldId);
    public IReadOnlyDictionary<Guid, IReadOnlyList<string>> GetFieldOptionMap(DocumentFamily family) => _catalog.GetFieldOptionMap(family);
    public IReadOnlyList<FieldFormRow> BuildFieldForm(DocumentFamily family, Guid? sampleSetId) => _catalog.BuildFieldForm(family, sampleSetId);

    public ImportPreview PreviewImport(decimal unitPrice, decimal quantity, decimal? amount, decimal vcf) =>
        _ledger.PreviewImport(unitPrice, quantity, amount, vcf);

    public FuelResult SaveOpening(OpeningRequest request) => _ledger.SaveOpening(request);
    public FuelResult SaveOpeningSheet(OpeningSheetRequest request) => _ledger.SaveOpeningSheet(request);
    public FuelResult SaveImport(ImportRequest request) => _ledger.SaveImport(request);
    public FuelResult SaveSlip(SlipRequest request) => _ledger.SaveSlip(request);
    public FuelResult SaveTransfer(TransferRequest request) => _ledger.SaveTransfer(request);
    public FuelResult SaveConsumption(ConsumptionRequest request) => _ledger.SaveConsumption(request);
    public FuelResult SaveConsumptionSheet(ConsumptionSheetRequest request) => _ledger.SaveConsumptionSheet(request);
    public FuelResult SaveAuxiliary(AuxiliaryRequest request) => _ledger.SaveAuxiliary(request);
    public FuelResult SaveAuxiliarySheet(AuxiliarySheetRequest request) => _ledger.SaveAuxiliarySheet(request);
    public FuelResult Void(Guid documentId) => _ledger.Void(documentId);
    public FuelResult DeleteSlip(Guid documentId) => _ledger.DeleteSlip(documentId);
    public RebuildResult RebuildStock() => _ledger.Rebuild();
    public DocumentDetail? GetDocument(Guid id) => _ledger.GetDocument(id);
    public IReadOnlyList<DocumentRow> ListDocuments(DocumentKind? kind = null) => _ledger.ListDocuments(kind);

    public bool HasActiveImportOrIssue() => _ledger.HasActiveImportOrIssue();
    public IReadOnlyList<DocumentRow> ListDesk(bool export, int? year, int quarter) => _ledger.ListDesk(export, year, quarter);

    public HashSet<Guid> ListLocationsWithQuarterBookActivity(DateTime quarterDate) =>
        _ledger.ListLocationsWithQuarterBookActivity(quarterDate);

    public IReadOnlyList<DocumentDetail> ListDocumentHeaders(params DocumentKind[] kinds) => _ledger.ListDocumentHeaders(kinds);
    public IReadOnlyList<SheetHeader> ListSheetHeaders(params DocumentKind[] kinds) => _ledger.ListSheetHeaders(kinds);

    public IReadOnlyList<SheetHeader> ListSheetHeadersInRange(
        DocumentKind kind,
        DateTime fromInclusive,
        DateTime toInclusive,
        bool nullDistanceOnly = false) =>
        _ledger.ListSheetHeadersInRange(kind, fromInclusive, toInclusive, nullDistanceOnly);
    public IReadOnlyList<InboundTransferSum> SumInboundTransfers(DateTime quarterDate, Guid? destinationWarehouseId = null) =>
        _ledger.SumInboundTransfers(quarterDate, destinationWarehouseId);

    public IReadOnlyList<InboundTransferSlipRow> ListInboundTransfers(DateTime quarterDate, Guid destinationWarehouseId) =>
        _ledger.ListInboundTransfers(quarterDate, destinationWarehouseId);

    public ConsumerTransferBook BuildConsumerTransferBook(DateTime quarterDate, Guid destinationWarehouseId) =>
        _ledger.BuildConsumerTransferBook(quarterDate, destinationWarehouseId);

    public ConsumerTransferBook GetConsumerQuarterBook(DateTime quarterDate, Guid destinationWarehouseId) =>
        _ledger.GetConsumerQuarterBook(quarterDate, destinationWarehouseId);

    public FuelResult SaveConsumerQuarterBook(ConsumerQuarterBookSaveRequest request) =>
        _ledger.SaveConsumerQuarterBook(request);

    public ShipQuarterBookDto GetShipQuarterBook(DateTime quarterDate, Guid shipWarehouseId) =>
        _shipBooks.GetBook(quarterDate, shipWarehouseId);

    public FuelResult SaveShipQuarterBook(ShipQuarterBookSaveRequest request) =>
        _shipBooks.SaveBook(request);

    public IReadOnlyList<QuarterRollupSuggestion> SuggestQuarterRollups(DateTime quarterDate)
    {
        var enabled = GetConsumers()
            .Where(x => (x.Type is ConsumerType.Machine or ConsumerType.Vehicle) && x.RollTransfersIntoQuarter)
            .Select(x => x.Id)
            .ToHashSet();
        if (enabled.Count == 0)
            return [];

        var date = quarterDate == default ? DateTime.Today : quarterDate.Date;
        var start = new DateTime(date.Year, ((date.Month - 1) / 3) * 3 + 1, 1);
        var end = start.AddMonths(3);
        var existing = ListSheetHeaders(DocumentKind.Consumption)
            .Where(x => x.Distance is null
                && x.WarehouseId is Guid warehouseId
                && enabled.Contains(warehouseId)
                && x.DocumentDate.Date >= start
                && x.DocumentDate.Date < end)
            .GroupBy(x => (x.WarehouseId!.Value, QuantityMath.LotKey(x.ItemName), x.UnitPrice, x.LotTypeId ?? Persistence.SeedIds.LotTypeTx))
            .ToDictionary(g => g.Key, g => g.First().ActualQuantity);

        return SumInboundTransfers(quarterDate)
            .Where(x => enabled.Contains(x.DestinationWarehouseId) && x.ActualQuantity > 0)
            .Select(x =>
            {
                var key = (x.DestinationWarehouseId, x.ItemNameKey, x.UnitPrice, x.LotTypeId);
                decimal? saved = existing.TryGetValue(key, out var qty) ? qty : null;
                return new QuarterRollupSuggestion(
                    x.DestinationWarehouseId,
                    x.ItemNameKey,
                    x.UnitPrice,
                    x.ActualQuantity,
                    saved,
                    x.LotTypeId,
                    x.LotTypeCode.Length > 0 ? x.LotTypeCode : "TX");
            })
            .Where(x => x.NeedsPrefill || x.NeedsUpdate)
            .ToList();
    }

    public IReadOnlyList<StockRow> ListStock(StockFilter filter) => _ledger.ListStock(filter);
    public NxtSheet GetNxt(int year, int quarter, IReadOnlyCollection<Guid> warehouseIds, NxtLotViewMode lotView = NxtLotViewMode.TxSscd) =>
        new NxtBook(() => new AppDbContext(_connectionString)).Build(year, quarter, warehouseIds, lotView);

    public NxtTotalSheet GetNxtTotal(int year, int quarter, IReadOnlyCollection<Guid> warehouseIds, NxtLotViewMode lotView = NxtLotViewMode.All) =>
        new NxtBook(() => new AppDbContext(_connectionString)).BuildTotal(year, quarter, warehouseIds, lotView);

    public NxtTotalSheet GetNxtWarehouseTotal(int year, int quarter, Guid warehouseId, NxtLotViewMode lotView = NxtLotViewMode.All) =>
        new NxtBook(() => new AppDbContext(_connectionString)).BuildWarehouseTotal(year, quarter, warehouseId, lotView);

    public QuotaSheet GetQuotaSheet(int year, int quarter, NxtLotViewMode lotView = NxtLotViewMode.TxSscd) =>
        new QuotaBook(() => new AppDbContext(_connectionString)).Build(year, quarter, lotView);

    public IReadOnlyList<MovementRow> ListMovements(Guid? lotId = null, Guid? warehouseId = null) => _ledger.ListMovements(lotId, warehouseId);
    public IReadOnlyList<LotOption> GetLots(Guid warehouseId, Guid? includeLotId = null) => _ledger.GetLots(warehouseId, includeLotId);
    public DashboardSummary GetDashboard() => _ledger.GetDashboard();

    internal void DebugSetBalance(Guid lotId, Guid warehouseId, decimal quantity) =>
        _ledger.DebugSetBalance(lotId, warehouseId, quantity);
}
