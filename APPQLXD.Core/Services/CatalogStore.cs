using APPQLXD.Core.Calculations;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace APPQLXD.Core.Services;

public sealed class CatalogStore
{
    private readonly Func<AppDbContext> _factory;
    private IReadOnlyList<WarehouseRow>? _warehousesCache;
    private IReadOnlyList<WarehouseRow>? _catalogWarehousesCache;
    private IReadOnlyList<ConsumerRow>? _consumersCache;
    private IReadOnlyList<ItemRow>? _itemsCache;

    public int CatalogGeneration { get; private set; }

    public CatalogStore(Func<AppDbContext> factory) => _factory = factory;

    public void InvalidateCache() => BustCatalogSnapshots();

    private void BustCatalogSnapshots()
    {
        _warehousesCache = null;
        _catalogWarehousesCache = null;
        _consumersCache = null;
        _itemsCache = null;
        CatalogGeneration++;
    }

    public IReadOnlyList<GroupRow> GetGroups()
    {
        using var db = _factory();
        var scope = ReadWarehouseScope(db);
        return db.ItemGroups.AsNoTracking()
            .Where(x => x.Scope == scope)
            .OrderBy(x => x.Name)
            .Select(x => new GroupRow(x.Id, x.Name, x.Scope))
            .ToList();
    }

    public FuelResult SaveGroup(Guid? id, string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            return FuelResult.Fail("Tên nhóm mặt hàng không được trống.");
        using var db = _factory();
        var scope = ReadWarehouseScope(db);
        if (db.ItemGroups.Any(x => x.Name == name && x.Id != id))
            return FuelResult.Fail("Tên nhóm mặt hàng đã tồn tại.");
        ItemGroup row;
        if (id is null)
        {
            row = new ItemGroup { Id = Guid.NewGuid(), Name = name, Scope = scope };
            db.ItemGroups.Add(row);
        }
        else
        {
            row = db.ItemGroups.FirstOrDefault(x => x.Id == id.Value) ?? throw new FuelRuleException("Không tìm thấy nhóm mặt hàng.");
            if (row.Scope != scope)
                return FuelResult.Fail($"Nhóm thuộc {Labels.WarehouseScopeLabel(row.Scope)}. Đổi chế độ kho trong Cài đặt để sửa.");
            row.Name = name;
        }

        db.SaveChanges();
        BustCatalogSnapshots();
        return FuelResult.Success(row.Id, "Đã lưu nhóm mặt hàng.");
    }

    public FuelResult DeleteGroup(Guid id)
    {
        using var db = _factory();
        var row = db.ItemGroups.FirstOrDefault(x => x.Id == id);
        if (row is null)
            return FuelResult.Fail("Không tìm thấy nhóm mặt hàng.");
        var scope = ReadWarehouseScope(db);
        if (row.Scope != scope)
            return FuelResult.Fail($"Nhóm thuộc {Labels.WarehouseScopeLabel(row.Scope)}. Đổi chế độ kho trong Cài đặt để xóa.");
        if (db.Consumers.Any(x => x.DefaultGroupId == id))
            return FuelResult.Fail("Nhóm đang là nhiên liệu mặc định của đối tượng, không xóa.");
        var items = db.FuelItems.Where(x => x.GroupId == id).ToList();
        foreach (var item in items)
        {
            if (db.Documents.Any(x => x.ItemId == item.Id) || db.Lots.Any(x => x.ItemId == item.Id) || db.Consumers.Any(x => x.DefaultItemId == item.Id))
                return FuelResult.Fail($"Mặt hàng \"{item.Name}\" trong nhóm đã phát sinh chứng từ, lô hoặc đối tượng, không xóa nhóm.");
        }

        if (items.Count > 0)
            db.FuelItems.RemoveRange(items);
        db.ItemGroups.Remove(row);
        db.SaveChanges();
        BustCatalogSnapshots();
        return FuelResult.Success(id, items.Count == 0
            ? "Đã xóa nhóm mặt hàng."
            : $"Đã xóa nhóm và {items.Count} mặt hàng trong nhóm.");
    }

    public IReadOnlyList<UnitRow> GetUnits()
    {
        using var db = _factory();
        return db.MeasureUnits.AsNoTracking().OrderBy(x => x.Name).Select(x => new UnitRow(x.Id, x.Name)).ToList();
    }

    public FuelResult SaveUnit(Guid? id, string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            return FuelResult.Fail("Tên đơn vị tính không được trống.");
        using var db = _factory();
        if (db.MeasureUnits.Any(x => x.Name == name && x.Id != id))
            return FuelResult.Fail("Đơn vị tính đã tồn tại.");
        MeasureUnit row;
        if (id is null)
        {
            row = new MeasureUnit { Id = Guid.NewGuid(), Name = name };
            db.MeasureUnits.Add(row);
        }
        else
        {
            row = db.MeasureUnits.FirstOrDefault(x => x.Id == id.Value) ?? throw new FuelRuleException("Không tìm thấy đơn vị tính.");
            row.Name = name;
        }

        db.SaveChanges();
        return FuelResult.Success(row.Id, "Đã lưu đơn vị tính.");
    }

    public FuelResult DeleteUnit(Guid id)
    {
        using var db = _factory();
        if (db.FuelItems.Any(x => x.UnitId == id))
            return FuelResult.Fail("Đơn vị tính đang được mặt hàng sử dụng, không xóa.");
        var row = db.MeasureUnits.FirstOrDefault(x => x.Id == id);
        if (row is null)
            return FuelResult.Fail("Không tìm thấy đơn vị tính.");
        db.MeasureUnits.Remove(row);
        db.SaveChanges();
        return FuelResult.Success(id, "Đã xóa đơn vị tính.");
    }

    public IReadOnlyList<LotTypeRow> GetLotTypes(bool activeOnly = true)
    {
        using var db = _factory();
        var query = db.LotTypes.AsNoTracking().AsQueryable();
        if (activeOnly)
            query = query.Where(x => x.IsActive);
        return query.OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .Select(x => new LotTypeRow(x.Id, x.Code, x.Name, x.SortOrder, x.IsActive))
            .ToList();
    }

    public FuelResult SaveLotType(Guid? id, string code, string name, int? sortOrder = null)
    {
        code = code.Trim().ToUpperInvariant();
        name = name.Trim();
        if (code.Length == 0)
            return FuelResult.Fail("Mã loại lô không được trống.");
        if (name.Length == 0)
            name = code;
        using var db = _factory();
        if (db.LotTypes.Any(x => x.Code == code && x.Id != id))
            return FuelResult.Fail("Mã loại lô đã tồn tại.");
        LotType row;
        if (id is null)
        {
            var nextSort = sortOrder ?? (db.LotTypes.Any() ? db.LotTypes.Max(x => x.SortOrder) + 1 : 1);
            row = new LotType { Id = Guid.NewGuid(), Code = code, Name = name, SortOrder = nextSort, IsActive = true };
            db.LotTypes.Add(row);
        }
        else
        {
            row = db.LotTypes.FirstOrDefault(x => x.Id == id.Value) ?? throw new FuelRuleException("Không tìm thấy loại lô.");
            row.Code = code;
            row.Name = name;
            if (sortOrder is int sort)
                row.SortOrder = sort;
        }

        db.SaveChanges();
        return FuelResult.Success(row.Id, "Đã lưu loại lô.");
    }

    public FuelResult DeleteLotType(Guid id)
    {
        using var db = _factory();
        var row = db.LotTypes.FirstOrDefault(x => x.Id == id);
        if (row is null)
            return FuelResult.Fail("Không tìm thấy loại lô.");
        if (id == SeedIds.LotTypeTx || id == SeedIds.LotTypeSscd || id == SeedIds.LotTypeIuu)
            return FuelResult.Fail("Không xóa loại lô mặc định TX, SSCĐ, IUU.");
        if (db.Lots.Any(x => x.LotTypeId == id) || db.Documents.Any(x => x.LotTypeId == id) || db.DocumentLines.Any(x => x.LotTypeId == id || x.DestinationLotTypeId == id))
            return FuelResult.Fail("Loại lô đang được sử dụng, không xóa.");
        db.LotTypes.Remove(row);
        db.SaveChanges();
        return FuelResult.Success(id, "Đã xóa loại lô.");
    }

    public IReadOnlyList<ItemRow> GetItems()
    {
        if (_itemsCache is not null)
            return _itemsCache;
        using var db = _factory();
        var scope = ReadWarehouseScope(db);
        _itemsCache = db.FuelItems.AsNoTracking().Include(x => x.Group).Include(x => x.Unit)
            .Where(x => x.Group != null && x.Group.Scope == scope)
            .OrderBy(x => x.Name)
            .Select(x => new ItemRow(
                x.Id, x.GroupId, x.Group!.Name, x.UnitId, x.Unit!.Name, x.Name,
                x.Code, x.Density, x.QualityInfo, x.Temperature, x.MeasurementNote, x.Vcf, x.ConversionRule))
            .ToList();
        return _itemsCache;
    }

    public ItemRow? GetItem(Guid id)
    {
        using var db = _factory();
        return db.FuelItems.AsNoTracking().Include(x => x.Group).Include(x => x.Unit)
            .Where(x => x.Id == id)
            .Select(x => new ItemRow(
                x.Id, x.GroupId, x.Group!.Name, x.UnitId, x.Unit!.Name, x.Name,
                x.Code, x.Density, x.QualityInfo, x.Temperature, x.MeasurementNote, x.Vcf, x.ConversionRule))
            .FirstOrDefault();
    }

    public FuelResult SaveItem(ItemEdit edit)
    {
        var name = edit.Name.Trim();
        if (name.Length == 0)
            return FuelResult.Fail("Tên mặt hàng không được trống.");
        var vcf = QuantityMath.RoundVcf(edit.Vcf);
        if (vcf <= 0)
            return FuelResult.Fail("VCF phải lớn hơn 0.");
        using var db = _factory();
        var scope = ReadWarehouseScope(db);
        var group = db.ItemGroups.FirstOrDefault(x => x.Id == edit.GroupId);
        if (group is null)
            return FuelResult.Fail("Nhóm mặt hàng không tồn tại.");
        if (group.Scope != scope)
            return FuelResult.Fail($"Nhóm \"{group.Name}\" thuộc {Labels.WarehouseScopeLabel(group.Scope)}. Đổi chế độ kho trong Cài đặt.");
        if (!db.MeasureUnits.Any(x => x.Id == edit.UnitId))
            return FuelResult.Fail("Đơn vị tính không tồn tại.");
        if (db.FuelItems.Any(x => x.Name == name && x.Id != edit.Id))
            return FuelResult.Fail("Tên mặt hàng đã tồn tại.");

        FuelItem row;
        if (edit.Id is null)
        {
            row = new FuelItem { Id = Guid.NewGuid() };
            db.FuelItems.Add(row);
        }
        else
        {
            row = db.FuelItems.FirstOrDefault(x => x.Id == edit.Id.Value) ?? throw new FuelRuleException("Không tìm thấy mặt hàng.");
        }

        row.GroupId = edit.GroupId;
        row.UnitId = edit.UnitId;
        row.Name = name;
        row.Code = edit.Code.Trim();
        row.Density = edit.Density;
        row.QualityInfo = edit.QualityInfo.Trim();
        row.Temperature = edit.Temperature;
        row.MeasurementNote = edit.MeasurementNote.Trim();
        row.Vcf = vcf;
        row.ConversionRule = string.IsNullOrWhiteSpace(edit.ConversionRule) ? Labels.DefaultConversionRule : edit.ConversionRule.Trim();
        db.SaveChanges();
        BustCatalogSnapshots();
        return FuelResult.Success(row.Id, "Đã lưu mặt hàng.");
    }

    public FuelResult DeleteItem(Guid id)
    {
        using var db = _factory();
        if (db.Documents.Any(x => x.ItemId == id) || db.Lots.Any(x => x.ItemId == id) || db.Consumers.Any(x => x.DefaultItemId == id))
            return FuelResult.Fail("Mặt hàng đã phát sinh chứng từ, lô hoặc đối tượng, không xóa.");
        var row = db.FuelItems.FirstOrDefault(x => x.Id == id);
        if (row is null)
            return FuelResult.Fail("Không tìm thấy mặt hàng.");
        db.FuelItems.Remove(row);
        db.SaveChanges();
        BustCatalogSnapshots();
        return FuelResult.Success(id, "Đã xóa mặt hàng.");
    }

    public IReadOnlyList<WarehouseRow> GetWarehouses()
    {
        if (_warehousesCache is not null)
            return _warehousesCache;
        using var db = _factory();
        var scope = ReadWarehouseScope(db);
        _warehousesCache = MapWarehouses(db, x => ScopeAllows(scope, x.Type));
        return _warehousesCache;
    }

    /// <summary>Danh mục kho độc lập: luôn trả Kho XD + Kho PTKT-VTXD (không lọc theo chế độ làm việc).</summary>
    public IReadOnlyList<WarehouseRow> GetCatalogWarehouses()
    {
        if (_catalogWarehousesCache is not null)
            return _catalogWarehousesCache;
        using var db = _factory();
        _catalogWarehousesCache = MapWarehouses(db, x => x.Type is WarehouseType.Main or WarehouseType.Ptkt);
        return _catalogWarehousesCache;
    }

    private static IReadOnlyList<WarehouseRow> MapWarehouses(AppDbContext db, Func<Warehouse, bool> allow)
    {
        var consumerTypes = db.Consumers.AsNoTracking().ToDictionary(x => x.Id, x => x.Type);
        return db.Warehouses.AsNoTracking().OrderBy(x => x.Name).AsEnumerable()
            .Where(allow)
            .Select(x =>
            {
                var isConsumer = consumerTypes.TryGetValue(x.Id, out var consumerType);
                return new WarehouseRow(
                    x.Id, x.Code, x.Name, x.Type, Labels.Warehouse(x.Type),
                    x.DefaultImportSampleSetId, x.DefaultExportSampleSetId,
                    isConsumer, isConsumer ? Labels.Consumer(consumerType) : null);
            })
            .ToList();
    }

    public FuelResult SaveWarehouse(WarehouseEdit edit)
    {
        var name = edit.Name.Trim();
        var code = edit.Code.Trim().ToUpperInvariant();
        if (name.Length == 0 || code.Length == 0)
            return FuelResult.Fail("Mã kho và tên kho không được trống.");
        using var db = _factory();
        if (edit.Id is Guid existingId && db.Consumers.Any(x => x.Id == existingId))
            return FuelResult.Fail("Đây là vị trí tồn của đối tượng. Sửa ở danh mục đối tượng.");
        if (db.Warehouses.Any(x => x.Code == code && x.Id != edit.Id))
            return FuelResult.Fail("Mã kho đã tồn tại.");
        if (db.Warehouses.Any(x => x.Name == name && x.Id != edit.Id))
            return FuelResult.Fail("Tên kho đã tồn tại.");
        if (db.Consumers.Any(x => x.Code == code && x.Id != edit.Id))
            return FuelResult.Fail("Mã đã dùng cho đối tượng.");
        if (db.Consumers.Any(x => x.Name == name && x.Id != edit.Id))
            return FuelResult.Fail("Tên đã dùng cho đối tượng.");
        var importSample = edit.Type == WarehouseType.Auxiliary ? null : edit.DefaultImportSampleSetId;
        if (!SampleMatches(db, importSample, DocumentFamily.Import))
            return FuelResult.Fail("Bộ dữ liệu mẫu nhập không đúng loại phiếu nhập.");
        if (!SampleMatches(db, edit.DefaultExportSampleSetId, DocumentFamily.Export))
            return FuelResult.Fail("Bộ dữ liệu mẫu xuất không đúng loại phiếu xuất.");
        if (edit.Type is not (WarehouseType.Main or WarehouseType.Ptkt or WarehouseType.Auxiliary))
            return FuelResult.Fail("Loại kho không hợp lệ.");

        Warehouse row;
        if (edit.Id is null)
        {
            row = new Warehouse { Id = Guid.NewGuid() };
            db.Warehouses.Add(row);
        }
        else
        {
            row = db.Warehouses.FirstOrDefault(x => x.Id == edit.Id.Value) ?? throw new FuelRuleException("Không tìm thấy kho.");
        }

        row.Code = code;
        row.Name = name;
        row.Type = edit.Type;
        row.DefaultImportSampleSetId = importSample;
        row.DefaultExportSampleSetId = edit.DefaultExportSampleSetId;
        db.SaveChanges();
        BustCatalogSnapshots();
        return FuelResult.Success(row.Id, "Đã lưu kho.");
    }

    public FuelResult DeleteWarehouse(Guid id)
    {
        using var db = _factory();
        if (db.Consumers.Any(x => x.Id == id))
            return FuelResult.Fail("Đây là vị trí tồn của đối tượng. Xóa ở danh mục đối tượng.");
        if (db.Documents.Any(x => x.WarehouseId == id || x.DestinationWarehouseId == id)
            || db.StockBalances.Any(x => x.WarehouseId == id))
            return FuelResult.Fail("Kho đã phát sinh chứng từ hoặc tồn, không xóa.");
        var row = db.Warehouses.FirstOrDefault(x => x.Id == id);
        if (row is null)
            return FuelResult.Fail("Không tìm thấy kho.");
        db.Warehouses.Remove(row);
        db.SaveChanges();
        BustCatalogSnapshots();
        return FuelResult.Success(id, "Đã xóa kho.");
    }

    public IReadOnlyList<ConsumerRow> GetConsumers()
    {
        if (_consumersCache is not null)
            return _consumersCache;
        using var db = _factory();
        var items = db.FuelItems.AsNoTracking().ToDictionary(x => x.Id);
        var groups = db.ItemGroups.AsNoTracking().ToDictionary(x => x.Id, x => x.Name);
        var factors = db.ConsumerNormFactors.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .AsEnumerable()
            .GroupBy(x => x.ConsumerId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ConsumerNormFactorRow>)g
                .Select(f => new ConsumerNormFactorRow(f.Id, f.GroupId, f.Name, f.Value, f.SortOrder)).ToList());
        _consumersCache = db.Consumers.AsNoTracking().OrderBy(x => x.Code).AsEnumerable().Select(x =>
        {
            var preferred = x.DefaultItemId is Guid itemId && items.TryGetValue(itemId, out var item) ? item : null;
            var groupId = x.DefaultGroupId ?? preferred?.GroupId;
            var factorRows = factors.TryGetValue(x.Id, out var list) ? list : Array.Empty<ConsumerNormFactorRow>();
            var effective = x.Type == ConsumerType.Ship
                ? QuantityMath.ShipEffectiveNorm(factorRows.Select(f => f.Value))
                : x.Norm;
            return new ConsumerRow(
            x.Id, x.Code, x.Name, x.Type, Labels.Consumer(x.Type),
            groupId,
            groupId is Guid gid && groups.TryGetValue(gid, out var groupName) ? groupName : null,
            preferred?.Id,
            preferred?.Name,
            x.Norm,
            effective is > 0 ? effective : null,
            factorRows,
            x.DefaultImportSampleSetId, x.DefaultExportSampleSetId,
            (x.Type is ConsumerType.Machine or ConsumerType.Vehicle) && x.RollTransfersIntoQuarter,
            x.Type == ConsumerType.Ship ? x.MainMachineCount : 0,
            x.Type == ConsumerType.Ship ? x.AuxMachineCount : 0,
            x.Type == ConsumerType.Ship ? x.ShipType : "");
        }).ToList();
        return _consumersCache;
    }

    public FuelResult SaveConsumer(ConsumerEdit edit)
    {
        var name = edit.Name.Trim();
        var code = edit.Code.Trim().ToUpperInvariant();
        if (name.Length == 0 || code.Length == 0)
            return FuelResult.Fail("Mã và tên đối tượng không được trống.");
        var norm = edit.Norm is null ? (decimal?)null : QuantityMath.RoundNorm(edit.Norm.Value);
        if (edit.Type == ConsumerType.Vehicle)
        {
            if (norm is null || norm <= 0)
                return FuelResult.Fail("Phương tiện phải có định mức lớn hơn 0.");
        }
        else if (edit.Type == ConsumerType.Ship)
        {
            if (edit.DefaultGroupId is null || edit.DefaultGroupId == Guid.Empty)
                return FuelResult.Fail("Chọn nhóm nhiên liệu mặc định (Xăng hoặc Dầu) cho tàu.");
            if (edit.NormFactors.Count != ShipNormSlots.Labels.Length)
                return FuelResult.Fail("Tàu cần đủ 6 bậc định mức: Tại bến, 25/50/75/100% CX, Máy phụ.");
            if (QuantityMath.RoundQty(edit.MainMachineCount) < 0 || QuantityMath.RoundQty(edit.AuxMachineCount) < 0)
                return FuelResult.Fail("Số máy hoạt động không được âm.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var factor in edit.NormFactors)
            {
                if (!ShipNormSlots.IsKnown(factor.Name))
                    return FuelResult.Fail("Nhãn định mức tàu phải là một trong 6 bậc cố định.");
                if (!seen.Add(factor.Name.Trim()))
                    return FuelResult.Fail("Mỗi bậc định mức tàu chỉ được nhập một lần.");
                if (factor.GroupId == Guid.Empty)
                    return FuelResult.Fail("Chọn nhóm nhiên liệu cho mỗi dòng định mức.");
                if (QuantityMath.RoundNorm(factor.Value) < 0)
                    return FuelResult.Fail("Định mức (L/giờ) không được âm.");
            }
        }
        else if (norm is < 0)
        {
            return FuelResult.Fail("Định mức không được âm.");
        }

        using var db = _factory();
        if (db.Consumers.Any(x => x.Code == code && x.Id != edit.Id))
            return FuelResult.Fail("Mã đối tượng đã tồn tại.");
        if (edit.DefaultGroupId is Guid groupId && !db.ItemGroups.Any(x => x.Id == groupId))
            return FuelResult.Fail("Nhóm nhiên liệu mặc định không tồn tại.");
        if (edit.DefaultItemId is Guid itemId && !db.FuelItems.Any(x => x.Id == itemId))
            return FuelResult.Fail("Nhiên liệu mặc định không tồn tại.");
        if (edit.Type == ConsumerType.Ship)
        {
            var defaultGroupName = db.ItemGroups.AsNoTracking()
                .Where(x => x.Id == edit.DefaultGroupId)
                .Select(x => x.Name)
                .FirstOrDefault();
            if (!ShipNormSlots.IsFuelGroup(defaultGroupName))
                return FuelResult.Fail("Nhóm nhiên liệu mặc định của tàu phải là Xăng hoặc Dầu.");
            foreach (var factor in edit.NormFactors)
            {
                var factorGroup = db.ItemGroups.AsNoTracking().FirstOrDefault(x => x.Id == factor.GroupId);
                if (factorGroup is null)
                    return FuelResult.Fail("Nhóm nhiên liệu định mức không tồn tại.");
                if (!ShipNormSlots.IsFuelGroup(factorGroup.Name))
                    return FuelResult.Fail("Định mức tàu chỉ gắn nhóm Xăng hoặc Dầu.");
            }
        }
        if (!SampleMatches(db, edit.DefaultImportSampleSetId, DocumentFamily.Import))
            return FuelResult.Fail("Bộ dữ liệu mẫu nhập không đúng loại.");
        if (!SampleMatches(db, edit.DefaultExportSampleSetId, DocumentFamily.Export))
            return FuelResult.Fail("Bộ dữ liệu mẫu xuất không đúng loại.");

        Consumer row;
        if (edit.Id is null)
        {
            row = new Consumer { Id = Guid.NewGuid() };
            db.Consumers.Add(row);
        }
        else
        {
            row = db.Consumers.FirstOrDefault(x => x.Id == edit.Id.Value) ?? throw new FuelRuleException("Không tìm thấy đối tượng.");
        }

        if (db.Warehouses.Any(x => x.Code == code && x.Id != row.Id))
            return FuelResult.Fail("Mã đã dùng cho kho.");
        if (db.Warehouses.Any(x => x.Name == name && x.Id != row.Id))
            return FuelResult.Fail("Tên đã dùng cho kho.");

        var preferred = edit.DefaultItemId;
        var group = edit.DefaultGroupId;
        if (preferred is Guid preferredId)
        {
            var preferredItem = db.FuelItems.First(x => x.Id == preferredId);
            if (group is Guid chosen && preferredItem.GroupId != chosen)
                preferred = null;
            else
                group ??= preferredItem.GroupId;
        }

        row.Code = code;
        row.Name = name;
        row.Type = edit.Type;
        row.DefaultGroupId = group;
        row.DefaultItemId = preferred;
        row.Norm = edit.Type == ConsumerType.Vehicle ? norm : null;
        row.RollTransfersIntoQuarter = edit.Type is ConsumerType.Machine or ConsumerType.Vehicle
            && edit.RollTransfersIntoQuarter;
        row.ShipType = edit.Type == ConsumerType.Ship ? edit.ShipType.Trim() : "";
        row.MainMachineCount = edit.Type == ConsumerType.Ship ? QuantityMath.RoundQty(edit.MainMachineCount) : 0;
        row.AuxMachineCount = edit.Type == ConsumerType.Ship ? QuantityMath.RoundQty(edit.AuxMachineCount) : 0;
        row.DefaultImportSampleSetId = edit.DefaultImportSampleSetId;
        row.DefaultExportSampleSetId = edit.DefaultExportSampleSetId;
        SyncNormFactors(db, row, edit.Type == ConsumerType.Ship ? edit.NormFactors : []);
        SyncConsumerWarehouse(db, row);
        db.SaveChanges();
        BustCatalogSnapshots();
        return FuelResult.Success(row.Id, "Đã lưu đối tượng tiêu thụ.");
    }

    public FuelResult RememberPreferredFuel(Guid consumerId, Guid itemId)
    {
        using var db = _factory();
        var consumer = db.Consumers.FirstOrDefault(x => x.Id == consumerId);
        var item = db.FuelItems.FirstOrDefault(x => x.Id == itemId);
        if (consumer is null || item is null)
            return FuelResult.Fail("Không tìm thấy đối tượng hoặc nhiên liệu.");
        var groupId = consumer.DefaultGroupId;
        if (groupId is null && consumer.DefaultItemId is Guid currentId)
            groupId = db.FuelItems.FirstOrDefault(x => x.Id == currentId)?.GroupId;
        if (groupId is null)
            return FuelResult.Fail("Đối tượng chưa có nhóm nhiên liệu mặc định.");
        if (item.GroupId != groupId)
            return FuelResult.Fail("Nhiên liệu không thuộc nhóm mặc định của đối tượng.");
        consumer.DefaultGroupId = groupId;
        if (consumer.DefaultItemId == item.Id)
            return FuelResult.Success(item.Id, "Đã ưu tiên nhiên liệu này.");
        consumer.DefaultItemId = item.Id;
        db.SaveChanges();
        BustCatalogSnapshots();
        return FuelResult.Success(item.Id, "Lần sau sẽ ưu tiên nhiên liệu này.");
    }

    public FuelResult DeleteConsumer(Guid id)
    {
        using var db = _factory();
        if (db.Documents.Any(x => x.ConsumerId == id || x.WarehouseId == id || x.DestinationWarehouseId == id)
            || db.StockBalances.Any(x => x.WarehouseId == id))
            return FuelResult.Fail("Đối tượng đã có chứng từ hoặc tồn, không xóa.");
        var row = db.Consumers.FirstOrDefault(x => x.Id == id);
        if (row is null)
            return FuelResult.Fail("Không tìm thấy đối tượng.");
        var location = db.Warehouses.FirstOrDefault(x => x.Id == id);
        if (location is not null)
            db.Warehouses.Remove(location);
        db.Consumers.Remove(row);
        db.SaveChanges();
        BustCatalogSnapshots();
        return FuelResult.Success(id, "Đã xóa đối tượng.");
    }

    public int EnsureConsumerStockLocations()
    {
        using var db = _factory();
        var added = 0;
        foreach (var consumer in db.Consumers.ToList())
        {
            if (SyncConsumerWarehouse(db, consumer))
                added++;
        }

        if (added > 0)
        {
            db.SaveChanges();
            BustCatalogSnapshots();
        }

        return added;
    }

    /// <summary>
    /// Đồng bộ vị trí tồn kho phụ gắn với đối tượng (cùng Id). Trả về true nếu đã thêm mới.
    /// </summary>
    internal static bool SyncConsumerWarehouse(AppDbContext db, Consumer consumer)
    {
        var row = db.Warehouses.Local.FirstOrDefault(x => x.Id == consumer.Id)
            ?? db.Warehouses.FirstOrDefault(x => x.Id == consumer.Id);
        if (row is null)
        {
            db.Warehouses.Add(new Warehouse
            {
                Id = consumer.Id,
                Code = consumer.Code,
                Name = consumer.Name,
                Type = WarehouseType.Auxiliary,
                DefaultImportSampleSetId = null,
                DefaultExportSampleSetId = consumer.DefaultExportSampleSetId
            });
            return true;
        }

        row.Code = consumer.Code;
        row.Name = consumer.Name;
        row.Type = WarehouseType.Auxiliary;
        row.DefaultImportSampleSetId = null;
        row.DefaultExportSampleSetId = consumer.DefaultExportSampleSetId;
        return false;
    }

    private static void SyncNormFactors(AppDbContext db, Consumer consumer, IReadOnlyList<ConsumerNormFactorEdit> edits)
    {
        var existing = db.ConsumerNormFactors.Where(x => x.ConsumerId == consumer.Id).ToList();
        foreach (var row in existing)
            db.ConsumerNormFactors.Remove(row);
        var groups = db.ItemGroups.ToDictionary(x => x.Id, x => x.Name);
        var order = 0;
        foreach (var edit in edits)
        {
            order++;
            var label = edit.Name.Trim();
            if (label.Length == 0)
                label = groups.TryGetValue(edit.GroupId, out var groupName) ? groupName : $"Định mức {order}";
            db.ConsumerNormFactors.Add(new ConsumerNormFactor
            {
                Id = edit.Id ?? Guid.NewGuid(),
                ConsumerId = consumer.Id,
                GroupId = edit.GroupId,
                Name = label,
                Value = QuantityMath.RoundNorm(edit.Value),
                SortOrder = edit.SortOrder > 0 ? edit.SortOrder : order
            });
        }
    }

    public bool GetExtraFieldsOnSlip()
    {
        using var db = _factory();
        return db.AppSettings.AsNoTracking().FirstOrDefault(x => x.Key == ExtraFieldsKey)?.Value == "1";
    }

    public void SetExtraFieldsOnSlip(bool enabled)
    {
        using var db = _factory();
        var row = db.AppSettings.FirstOrDefault(x => x.Key == ExtraFieldsKey);
        var value = enabled ? "1" : "0";
        if (row is null)
            db.AppSettings.Add(new AppSetting { Key = ExtraFieldsKey, Value = value });
        else if (row.Value == value)
            return;
        else
            row.Value = value;
        db.SaveChanges();
    }

    public void EnsureItemGroupScopes()
    {
        using var db = _factory();
        var changed = false;
        foreach (var group in db.ItemGroups)
        {
            var want = group.Name.Contains("PTKT", StringComparison.OrdinalIgnoreCase)
                ? WarehouseScope.Ptkt
                : group.Scope;
            // Chỉ gán PTKT theo tên; nhóm khác giữ Scope hiện có (mặc định XD).
            if (want == WarehouseScope.Ptkt && group.Scope != WarehouseScope.Ptkt)
            {
                group.Scope = WarehouseScope.Ptkt;
                changed = true;
            }
        }

        if (changed)
        {
            db.SaveChanges();
            BustCatalogSnapshots();
        }
    }

    public WarehouseScope GetWarehouseScope()
    {
        using var db = _factory();
        return ReadWarehouseScope(db);
    }

    public void SetWarehouseScope(WarehouseScope scope)
    {
        using var db = _factory();
        var value = scope == WarehouseScope.Ptkt ? "PTKT" : "XD";
        var row = db.AppSettings.FirstOrDefault(x => x.Key == WarehouseScopeKey);
        if (row is null)
            db.AppSettings.Add(new AppSetting { Key = WarehouseScopeKey, Value = value });
        else if (row.Value == value)
            return;
        else
            row.Value = value;
        db.SaveChanges();
        BustCatalogSnapshots();
    }

    private static WarehouseScope ReadWarehouseScope(AppDbContext db)
    {
        var value = db.AppSettings.AsNoTracking().FirstOrDefault(x => x.Key == WarehouseScopeKey)?.Value;
        return string.Equals(value, "PTKT", StringComparison.OrdinalIgnoreCase)
            ? WarehouseScope.Ptkt
            : WarehouseScope.Xd;
    }

    private static bool ScopeAllows(WarehouseScope scope, WarehouseType type) =>
        scope == WarehouseScope.Ptkt ? type == WarehouseType.Ptkt : type != WarehouseType.Ptkt;

    private const string ExtraFieldsKey = "ExtraFieldsOnSlip";
    private const string WarehouseScopeKey = "WarehouseScope";

    public IReadOnlyList<FieldRow> GetFields(DocumentFamily? family)
    {
        using var db = _factory();
        var query = db.FieldDefinitions.AsNoTracking().AsQueryable();
        if (family is DocumentFamily value)
            query = query.Where(x => x.Family == value);
        return query.OrderBy(x => x.Family).ThenBy(x => x.SortOrder).ThenBy(x => x.Name)
            .AsEnumerable()
            .Select(x => new FieldRow(x.Id, x.Family, Labels.Family(x.Family), x.Name, x.DataType, Labels.DataType(x.DataType), x.IsRequired, x.IsVisible, x.SortOrder))
            .ToList();
    }

    public FuelResult SaveField(FieldEdit edit)
    {
        var name = edit.Name.Trim();
        if (name.Length == 0)
            return FuelResult.Fail("Tên trường không được trống.");
        using var db = _factory();
        if (db.FieldDefinitions.Any(x => x.Family == edit.Family && x.Name == name && x.Id != edit.Id))
            return FuelResult.Fail("Tên trường đã tồn tại trong loại phiếu này.");
        FieldDefinition row;
        if (edit.Id is null)
        {
            row = new FieldDefinition { Id = Guid.NewGuid() };
            db.FieldDefinitions.Add(row);
            row.SortOrder = edit.SortOrder > 0 ? edit.SortOrder : db.FieldDefinitions.Where(x => x.Family == edit.Family).Select(x => (int?)x.SortOrder).Max() ?? 0 + 1;
        }
        else
        {
            row = db.FieldDefinitions.FirstOrDefault(x => x.Id == edit.Id.Value) ?? throw new FuelRuleException("Không tìm thấy trường dữ liệu.");
            row.SortOrder = edit.SortOrder;
        }

        row.Family = edit.Family;
        row.Name = name;
        row.DataType = edit.DataType;
        row.IsRequired = edit.IsRequired;
        row.IsVisible = edit.IsVisible;
        if (edit.Id is not null)
            row.SortOrder = edit.SortOrder;
        db.SaveChanges();
        return FuelResult.Success(row.Id, "Đã lưu trường dữ liệu.");
    }

    public FuelResult DeleteField(Guid id)
    {
        using var db = _factory();
        if (db.SampleValues.Any(x => x.FieldDefinitionId == id))
            return FuelResult.Fail("Trường đang có dữ liệu mẫu, không xóa.");
        var row = db.FieldDefinitions.FirstOrDefault(x => x.Id == id);
        if (row is null)
            return FuelResult.Fail("Không tìm thấy trường dữ liệu.");
        db.FieldDefinitions.Remove(row);
        db.SaveChanges();
        return FuelResult.Success(id, "Đã xóa trường dữ liệu.");
    }

    public FuelResult EnsureOwnedExportSample(bool warehouse, Guid ownerId)
    {
        using var db = _factory();
        if (warehouse)
        {
            if (db.Consumers.Any(x => x.Id == ownerId))
                return FuelResult.Fail("Đây là vị trí tồn của đối tượng. Sửa mẫu ở danh mục đối tượng.");
            var row = db.Warehouses.FirstOrDefault(x => x.Id == ownerId);
            if (row is null)
                return FuelResult.Fail("Không tìm thấy kho.");
            row.DefaultExportSampleSetId = OwnExportSet(db, row.DefaultExportSampleSetId, ownerId, $"Phiếu xuất {row.Name}");
            db.SaveChanges();
            BustCatalogSnapshots();
            return FuelResult.Success(row.DefaultExportSampleSetId.Value, "Đã mở phiếu mẫu.");
        }

        var consumer = db.Consumers.FirstOrDefault(x => x.Id == ownerId);
        if (consumer is null)
            return FuelResult.Fail("Không tìm thấy đối tượng.");
        consumer.DefaultExportSampleSetId = OwnExportSet(db, consumer.DefaultExportSampleSetId, ownerId, $"Phiếu xuất {consumer.Name}");
        SyncConsumerWarehouse(db, consumer);
        db.SaveChanges();
        BustCatalogSnapshots();
        return FuelResult.Success(consumer.DefaultExportSampleSetId.Value, "Đã mở phiếu mẫu.");
    }

    private static Guid OwnExportSet(AppDbContext db, Guid? current, Guid ownerId, string name)
    {
        if (current is Guid setId && !ExportSetShared(db, setId, ownerId))
            return setId;
        var set = new SampleSet { Id = Guid.NewGuid(), Name = name, Family = DocumentFamily.Export };
        db.SampleSets.Add(set);
        return set.Id;
    }

    private static bool ExportSetShared(AppDbContext db, Guid setId, Guid ownerId)
    {
        var otherWarehouse = db.Warehouses.Any(x =>
            x.DefaultExportSampleSetId == setId
            && x.Id != ownerId
            && !db.Consumers.Any(c => c.Id == x.Id));
        var otherConsumer = db.Consumers.Any(x => x.DefaultExportSampleSetId == setId && x.Id != ownerId);
        return otherWarehouse || otherConsumer;
    }

    public FuelResult EnsureCatalogSample(DocumentFamily family)
    {
        using var db = _factory();
        var sets = db.SampleSets.Where(x => x.Family == family).ToList();
        var shared = sets
            .Select(set => (set, refs: SampleRefs(db, set.Id)))
            .Where(x => x.refs != 1)
            .OrderByDescending(x => x.refs)
            .ThenBy(x => x.set.Name)
            .Select(x => x.set)
            .FirstOrDefault();
        if (shared is not null)
            return FuelResult.Success(shared.Id, shared.Name);

        var created = new SampleSet
        {
            Id = Guid.NewGuid(),
            Name = family == DocumentFamily.Export ? "Phiếu xuất" : "Phiếu nhập",
            Family = family
        };
        db.SampleSets.Add(created);
        db.SaveChanges();
        return FuelResult.Success(created.Id, created.Name);
    }

    private static int SampleRefs(AppDbContext db, Guid setId) =>
        db.Warehouses.Count(x =>
            (x.DefaultImportSampleSetId == setId || x.DefaultExportSampleSetId == setId)
            && !db.Consumers.Any(c => c.Id == x.Id))
        + db.Consumers.Count(x => x.DefaultImportSampleSetId == setId || x.DefaultExportSampleSetId == setId);

    public IReadOnlyList<SampleSetRow> GetSampleSets(DocumentFamily? family)
    {
        using var db = _factory();
        var query = db.SampleSets.AsNoTracking().AsQueryable();
        if (family is DocumentFamily value)
            query = query.Where(x => x.Family == value);
        return query.OrderBy(x => x.Name).AsEnumerable()
            .Select(x => new SampleSetRow(x.Id, x.Name, x.Family, Labels.Family(x.Family)))
            .ToList();
    }

    public FuelResult SaveSampleSet(Guid? id, string name, DocumentFamily family)
    {
        name = name.Trim();
        if (name.Length == 0)
            return FuelResult.Fail("Tên bộ dữ liệu mẫu không được trống.");
        using var db = _factory();
        SampleSet row;
        if (id is null)
        {
            row = new SampleSet { Id = Guid.NewGuid(), Name = name, Family = family };
            db.SampleSets.Add(row);
        }
        else
        {
            row = db.SampleSets.FirstOrDefault(x => x.Id == id.Value) ?? throw new FuelRuleException("Không tìm thấy bộ dữ liệu mẫu.");
            if (row.Family != family && db.SampleValues.Any(x => x.SampleSetId == row.Id))
                return FuelResult.Fail("Không đổi loại phiếu khi bộ mẫu đã có giá trị.");
            row.Name = name;
            row.Family = family;
        }

        db.SaveChanges();
        return FuelResult.Success(row.Id, "Đã lưu bộ dữ liệu mẫu.");
    }

    public FuelResult DeleteSampleSet(Guid id)
    {
        using var db = _factory();
        if (db.Warehouses.Any(x => x.DefaultImportSampleSetId == id || x.DefaultExportSampleSetId == id)
            || db.Consumers.Any(x => x.DefaultImportSampleSetId == id || x.DefaultExportSampleSetId == id))
            return FuelResult.Fail("Bộ dữ liệu mẫu đang là mặc định của kho hoặc đối tượng, không xóa.");
        var row = db.SampleSets.Include(x => x.Values).FirstOrDefault(x => x.Id == id);
        if (row is null)
            return FuelResult.Fail("Không tìm thấy bộ dữ liệu mẫu.");
        db.SampleSets.Remove(row);
        db.SaveChanges();
        return FuelResult.Success(id, "Đã xóa bộ dữ liệu mẫu.");
    }

    public IReadOnlyList<SampleValueRow> GetSampleValues(Guid sampleSetId)
    {
        using var db = _factory();
        return db.SampleValues.AsNoTracking().Include(x => x.Field)
            .Where(x => x.SampleSetId == sampleSetId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new SampleValueRow(x.Id, x.SampleSetId, x.FieldDefinitionId, x.Field!.Name, x.Value))
            .ToList();
    }

    public IReadOnlyDictionary<string, string> GetOwnedExportDefaults(Guid consumerId)
    {
        using var db = _factory();
        var consumer = db.Consumers.AsNoTracking().FirstOrDefault(x => x.Id == consumerId);
        if (consumer?.DefaultExportSampleSetId is not Guid setId || SampleRefs(db, setId) != 1)
            return new Dictionary<string, string>();
        return db.SampleValues.AsNoTracking().Include(x => x.Field)
            .Where(x => x.SampleSetId == setId)
            .AsEnumerable()
            .GroupBy(x => x.Field!.Name)
            .Select(group => (
                group.Key,
                values: group.Select(x => x.Value.Trim()).Where(x => x.Length > 0).Distinct().ToList()))
            .Where(x => x.values.Count == 1)
            .ToDictionary(x => x.Key, x => x.values[0]);
    }

    public IReadOnlyList<SampleValueRow> GetSharedExportValues()
    {
        using var db = _factory();
        var shared = db.SampleSets.Where(x => x.Family == DocumentFamily.Export).ToList()
            .Where(set => SampleRefs(db, set.Id) != 1)
            .Select(set => set.Id)
            .ToList();
        if (shared.Count == 0)
            return [];
        return db.SampleValues.AsNoTracking().Include(x => x.Field)
            .Where(x => shared.Contains(x.SampleSetId))
            .OrderBy(x => x.CreatedAt)
            .Select(x => new SampleValueRow(x.Id, x.SampleSetId, x.FieldDefinitionId, x.Field!.Name, x.Value))
            .ToList();
    }

    public FuelResult SaveOwnedSample(Guid setId, IReadOnlyList<(Guid FieldId, string Value)> fields)
    {
        using var db = _factory();
        var set = db.SampleSets.Include(x => x.Values).FirstOrDefault(x => x.Id == setId);
        if (set is null)
            return FuelResult.Fail("Không tìm thấy phiếu mẫu.");
        if (set.Family != DocumentFamily.Export)
            return FuelResult.Fail("Phiếu mẫu này không phải phiếu xuất.");

        var defs = db.FieldDefinitions.Where(x => x.Family == DocumentFamily.Export).ToDictionary(x => x.Id);
        var incoming = new Dictionary<Guid, string>();
        var touched = new HashSet<Guid>();
        foreach (var (fieldId, raw) in fields)
        {
            if (!defs.TryGetValue(fieldId, out var def))
                return FuelResult.Fail("Trường không thuộc phiếu xuất.");
            if (!SlipFieldCatalog.AllowsSampleValues(def.Name))
                continue;
            touched.Add(fieldId);
            var value = raw.Trim();
            if (value.Length == 0)
                continue;
            if (!ValueMatches(def.DataType, value))
                return FuelResult.Fail($"\"{def.Name}\" không đúng kiểu {Labels.DataType(def.DataType)}.");
            incoming[fieldId] = value;
        }

        foreach (var group in set.Values.GroupBy(x => x.FieldDefinitionId).ToList())
        {
            if (!touched.Contains(group.Key))
                continue;
            var rows = group.ToList();
            if (!incoming.TryGetValue(group.Key, out var value))
            {
                db.SampleValues.RemoveRange(rows);
                continue;
            }

            rows[0].Value = value;
            if (rows.Count > 1)
                db.SampleValues.RemoveRange(rows.Skip(1));
            incoming.Remove(group.Key);
        }

        foreach (var (fieldId, value) in incoming)
        {
            db.SampleValues.Add(new SampleValue
            {
                Id = Guid.NewGuid(),
                SampleSetId = setId,
                FieldDefinitionId = fieldId,
                Value = value,
                CreatedAt = DateTime.UtcNow
            });
        }

        db.SaveChanges();
        return FuelResult.Success(setId, "Đã lưu phiếu mẫu.");
    }

    public FuelResult AddSampleValue(Guid sampleSetId, Guid fieldId, string value)
    {
        value = value.Trim();
        if (value.Length == 0)
            return FuelResult.Fail("Giá trị mẫu không được trống.");
        using var db = _factory();
        var set = db.SampleSets.FirstOrDefault(x => x.Id == sampleSetId);
        var field = db.FieldDefinitions.FirstOrDefault(x => x.Id == fieldId);
        if (set is null || field is null)
            return FuelResult.Fail("Bộ mẫu hoặc trường dữ liệu không tồn tại.");
        if (!SlipFieldCatalog.AllowsSampleValues(field.Name))
            return FuelResult.Fail($"Trường \"{field.Name}\" không lưu dữ liệu mẫu.");
        if (set.Family != field.Family)
            return FuelResult.Fail("Trường không thuộc loại phiếu của bộ mẫu.");
        if (!ValueMatches(field.DataType, value))
            return FuelResult.Fail($"Giá trị không đúng kiểu {Labels.DataType(field.DataType)}.");
        if (db.SampleValues.Any(x => x.SampleSetId == sampleSetId && x.FieldDefinitionId == fieldId && x.Value == value))
            return FuelResult.Success(sampleSetId, "Giá trị đã có trong dữ liệu mẫu.");
        var row = new SampleValue
        {
            Id = Guid.NewGuid(),
            SampleSetId = sampleSetId,
            FieldDefinitionId = fieldId,
            Value = value,
            CreatedAt = DateTime.UtcNow
        };
        db.SampleValues.Add(row);
        db.SaveChanges();
        return FuelResult.Success(row.Id, "Đã thêm giá trị vào dữ liệu mẫu.");
    }

    public FuelResult UpdateSampleValue(Guid id, string value)
    {
        value = value.Trim();
        if (value.Length == 0)
            return FuelResult.Fail("Giá trị mẫu không được trống.");
        using var db = _factory();
        var row = db.SampleValues.Include(x => x.Field).FirstOrDefault(x => x.Id == id);
        if (row is null || row.Field is null)
            return FuelResult.Fail("Không tìm thấy giá trị mẫu.");
        if (row.Value == value)
            return FuelResult.Success(id, "Giá trị không đổi.");
        if (!ValueMatches(row.Field.DataType, value))
            return FuelResult.Fail($"Giá trị không đúng kiểu {Labels.DataType(row.Field.DataType)}.");
        if (db.SampleValues.Any(x => x.Id != id && x.SampleSetId == row.SampleSetId && x.FieldDefinitionId == row.FieldDefinitionId && x.Value == value))
            return FuelResult.Fail("Giá trị này đã có trong trường.");
        row.Value = value;
        db.SaveChanges();
        return FuelResult.Success(id, "Đã sửa giá trị mẫu.");
    }

    public FuelResult DeleteSampleValue(Guid id)
    {
        using var db = _factory();
        var row = db.SampleValues.FirstOrDefault(x => x.Id == id);
        if (row is null)
            return FuelResult.Fail("Không tìm thấy giá trị mẫu.");
        db.SampleValues.Remove(row);
        db.SaveChanges();
        return FuelResult.Success(id, "Đã xóa giá trị mẫu.");
    }

    public IReadOnlyList<string> GetFieldOptions(Guid fieldId)
    {
        using var db = _factory();
        return db.SampleValues.AsNoTracking()
            .Where(x => x.FieldDefinitionId == fieldId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Value)
            .AsEnumerable()
            .Distinct()
            .ToList();
    }

    public IReadOnlyDictionary<Guid, IReadOnlyList<string>> GetFieldOptionMap(DocumentFamily family)
    {
        using var db = _factory();
        return (
            from value in db.SampleValues.AsNoTracking()
            join field in db.FieldDefinitions.AsNoTracking() on value.FieldDefinitionId equals field.Id
            where field.Family == family
            orderby value.CreatedAt
            select new { value.FieldDefinitionId, value.Value })
            .AsEnumerable()
            .GroupBy(x => x.FieldDefinitionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(x => x.Value).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList());
    }

    public IReadOnlyList<FieldFormRow> BuildFieldForm(DocumentFamily family, Guid? sampleSetId)
    {
        using var db = _factory();
        var fields = db.FieldDefinitions.AsNoTracking()
            .Where(x => x.Family == family && x.IsVisible)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .AsEnumerable()
            .Where(x => !SlipFieldCatalog.HideFromExtraForm(x.Name))
            .ToList();
        var ids = fields.Select(x => x.Id).ToHashSet();
        var options = db.SampleValues.AsNoTracking()
            .Where(x => ids.Contains(x.FieldDefinitionId))
            .OrderBy(x => x.CreatedAt)
            .ToList()
            .GroupBy(x => x.FieldDefinitionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Value).Distinct().ToList());
        var selected = sampleSetId is null
            ? new Dictionary<Guid, string>()
            : db.SampleValues.AsNoTracking()
                .Where(x => x.SampleSetId == sampleSetId)
                .OrderBy(x => x.CreatedAt)
                .AsEnumerable()
                .GroupBy(x => x.FieldDefinitionId)
                .ToDictionary(g => g.Key, g => g.First().Value);

        return fields.Select(f => new FieldFormRow(
            f.Id,
            f.Name,
            f.DataType,
            Labels.DataType(f.DataType),
            f.IsRequired,
            selected.TryGetValue(f.Id, out var value) ? value : "",
            options.TryGetValue(f.Id, out var list) ? list : [])).ToList();
    }

    internal static bool ValueMatches(FieldDataType type, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;
        return type switch
        {
            FieldDataType.Text => true,
            FieldDataType.Number => decimal.TryParse(value, out _) || decimal.TryParse(value.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _),
            FieldDataType.Date => DateTime.TryParse(value, out _),
            FieldDataType.Boolean => value.Trim().ToLowerInvariant() is "true" or "false" or "có" or "không" or "co" or "khong" or "1" or "0" or "yes" or "no",
            _ => true
        };
    }

    private static bool SampleMatches(AppDbContext db, Guid? sampleSetId, DocumentFamily family)
    {
        if (sampleSetId is null)
            return true;
        var set = db.SampleSets.FirstOrDefault(x => x.Id == sampleSetId);
        return set is not null && set.Family == family;
    }

    public IReadOnlyList<MissionGroupRow> GetMissionGroups()
    {
        using var db = _factory();
        MissionSeeder.Ensure(db);
        return db.MissionGroups.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .Select(g => new MissionGroupRow(
                g.Id, g.Code, g.Name, g.SortOrder, g.IsLossGroup,
                db.MissionTasks.Count(t => t.GroupId == g.Id)))
            .ToList();
    }

    public IReadOnlyList<MissionTaskRow> GetMissionTasks(Guid? groupId = null, bool activeOnly = true)
    {
        using var db = _factory();
        MissionSeeder.Ensure(db);
        var q = from t in db.MissionTasks.AsNoTracking()
                join g in db.MissionGroups.AsNoTracking() on t.GroupId equals g.Id
                select new { t, g };
        if (groupId is Guid gid)
            q = q.Where(x => x.t.GroupId == gid);
        if (activeOnly)
            q = q.Where(x => x.t.IsActive);
        return q.OrderBy(x => x.g.SortOrder).ThenBy(x => x.t.SortOrder).ThenBy(x => x.t.Name)
            .Select(x => new MissionTaskRow(x.t.Id, x.t.GroupId, x.g.Code, x.g.Name, x.t.Name, x.t.SortOrder, x.t.IsActive))
            .ToList();
    }

    public FuelResult SaveMissionGroup(Guid? id, string code, string name, bool isLossGroup, int? sortOrder = null)
    {
        code = code.Trim().ToUpperInvariant();
        name = name.Trim();
        if (code.Length == 0)
            return FuelResult.Fail("Mã nhóm nhiệm vụ không được trống.");
        if (name.Length == 0)
            return FuelResult.Fail("Tên nhóm nhiệm vụ không được trống.");
        using var db = _factory();
        if (db.MissionGroups.Any(x => x.Code == code && x.Id != id))
            return FuelResult.Fail("Mã nhóm nhiệm vụ đã tồn tại.");
        MissionGroup row;
        if (id is null)
        {
            var next = sortOrder ?? (db.MissionGroups.Any() ? db.MissionGroups.Max(x => x.SortOrder) + 1 : 1);
            row = new MissionGroup { Id = Guid.NewGuid(), Code = code, Name = name, SortOrder = next, IsLossGroup = isLossGroup };
            db.MissionGroups.Add(row);
        }
        else
        {
            row = db.MissionGroups.FirstOrDefault(x => x.Id == id.Value)
                ?? throw new FuelRuleException("Không tìm thấy nhóm nhiệm vụ.");
            row.Code = code;
            row.Name = name;
            row.IsLossGroup = isLossGroup;
            if (sortOrder is int s)
                row.SortOrder = s;
        }

        db.SaveChanges();
        BustCatalogSnapshots();
        return FuelResult.Success(row.Id, "Đã lưu nhóm nhiệm vụ.");
    }

    public FuelResult DeleteMissionGroup(Guid id)
    {
        using var db = _factory();
        var row = db.MissionGroups.FirstOrDefault(x => x.Id == id);
        if (row is null)
            return FuelResult.Fail("Không tìm thấy nhóm nhiệm vụ.");
        if (db.MissionTasks.Any(x => x.GroupId == id))
            return FuelResult.Fail("Nhóm còn nhiệm vụ con, không xóa.");
        db.MissionGroups.Remove(row);
        db.SaveChanges();
        BustCatalogSnapshots();
        return FuelResult.Success(id, "Đã xóa nhóm nhiệm vụ.");
    }

    public FuelResult SaveMissionTask(Guid? id, Guid groupId, string name, int? sortOrder = null, bool isActive = true)
    {
        name = name.Trim();
        if (name.Length == 0)
            return FuelResult.Fail("Tên nhiệm vụ không được trống.");
        using var db = _factory();
        if (!db.MissionGroups.Any(x => x.Id == groupId))
            return FuelResult.Fail("Không tìm thấy nhóm nhiệm vụ.");
        if (db.MissionTasks.Any(x => x.GroupId == groupId && x.Name == name && x.Id != id))
            return FuelResult.Fail("Tên nhiệm vụ đã có trong nhóm.");
        MissionTask row;
        if (id is null)
        {
            var next = sortOrder ?? (db.MissionTasks.Any(x => x.GroupId == groupId)
                ? db.MissionTasks.Where(x => x.GroupId == groupId).Max(x => x.SortOrder) + 1
                : 1);
            row = new MissionTask
            {
                Id = Guid.NewGuid(),
                GroupId = groupId,
                Name = name,
                SortOrder = next,
                IsActive = isActive
            };
            db.MissionTasks.Add(row);
        }
        else
        {
            row = db.MissionTasks.FirstOrDefault(x => x.Id == id.Value)
                ?? throw new FuelRuleException("Không tìm thấy nhiệm vụ.");
            row.GroupId = groupId;
            row.Name = name;
            row.IsActive = isActive;
            if (sortOrder is int s)
                row.SortOrder = s;
        }

        db.SaveChanges();
        BustCatalogSnapshots();
        return FuelResult.Success(row.Id, "Đã lưu nhiệm vụ.");
    }

    public FuelResult DeleteMissionTask(Guid id)
    {
        using var db = _factory();
        var row = db.MissionTasks.FirstOrDefault(x => x.Id == id);
        if (row is null)
            return FuelResult.Fail("Không tìm thấy nhiệm vụ.");
        if (db.Documents.Any(x => x.MissionTaskId == id)
            || db.ConsumerQuarterBookLines.Any(x => x.MissionTaskId == id)
            || db.ShipQuarterBookLines.Any(x => x.MissionTaskId == id))
            return FuelResult.Fail("Nhiệm vụ đang được dùng trên phiếu hoặc sổ tiêu thụ, không xóa.");
        db.MissionYearLimits.RemoveRange(db.MissionYearLimits.Where(x => x.TaskId == id));
        db.MissionTasks.Remove(row);
        db.SaveChanges();
        BustCatalogSnapshots();
        return FuelResult.Success(id, "Đã xóa nhiệm vụ.");
    }

    public IReadOnlyList<MissionYearLimitRow> GetMissionYearLimits(int year, NxtLotViewMode? lotView = null)
    {
        using var db = _factory();
        var q = db.MissionYearLimits.AsNoTracking().Where(x => x.Year == year);
        if (lotView is NxtLotViewMode mode && mode is NxtLotViewMode.TxSscd or NxtLotViewMode.Iuu)
            q = q.Where(x => x.LotView == (int)mode);
        return q
            .Select(x => new MissionYearLimitRow(x.TaskId, x.Year, (NxtLotViewMode)x.LotView, x.GasolineLimit, x.DieselLimit))
            .ToList();
    }

    public FuelResult SaveMissionYearLimit(Guid taskId, int year, NxtLotViewMode lotView, decimal gasolineLimit, decimal dieselLimit)
    {
        if (year < 2000 || year > 2100)
            return FuelResult.Fail("Năm không hợp lệ.");
        if (lotView is not (NxtLotViewMode.TxSscd or NxtLotViewMode.Iuu))
            return FuelResult.Fail("Hạn mức chỉ nhập riêng cho TX + SSCĐ hoặc IUU.");
        gasolineLimit = QuantityMath.ClampEditQty(QuantityMath.Whole(gasolineLimit));
        dieselLimit = QuantityMath.ClampEditQty(QuantityMath.Whole(dieselLimit));
        using var db = _factory();
        if (!db.MissionTasks.Any(x => x.Id == taskId))
            return FuelResult.Fail("Không tìm thấy nhiệm vụ.");
        var view = (int)lotView;
        var row = db.MissionYearLimits.FirstOrDefault(x => x.TaskId == taskId && x.Year == year && x.LotView == view);
        if (row is null)
        {
            row = new MissionYearLimit
            {
                Id = Guid.NewGuid(),
                TaskId = taskId,
                Year = year,
                LotView = view,
                GasolineLimit = gasolineLimit,
                DieselLimit = dieselLimit
            };
            db.MissionYearLimits.Add(row);
        }
        else
        {
            row.GasolineLimit = gasolineLimit;
            row.DieselLimit = dieselLimit;
        }

        db.SaveChanges();
        return FuelResult.Success(row.Id, "Đã lưu hạn mức.");
    }
}
