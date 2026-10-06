using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using APPQLXD.Controls;
using APPQLXD.Core;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Core.Persistence;
using APPQLXD.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace APPQLXD.ViewModels;

public partial class OpeningCellVm : ObservableObject
{
    public Guid WarehouseId { get; init; }
    public Guid? DocumentId { get; set; }
    public DocumentKind Kind { get; set; }
    public decimal SavedActual { get; set; }
    public string SavedLotKey { get; set; } = "";
    public long SavedPrice { get; set; }
    public Guid SavedLotTypeId { get; set; }
    public bool UseShipNorm { get; set; }
    public decimal? ShipNorm { get; set; }
    public Guid? ConsumerId { get; set; }
    public string ConsumerName { get; set; } = "";
    public string ConsumerCode { get; set; } = "";
    [ObservableProperty] private string _quantity = "";
    [ObservableProperty] private string _operating = "";
    [ObservableProperty] private string _stockBefore = "";
    [ObservableProperty] private string _onHandText = "—";
    [ObservableProperty] private string _stockAfterText = "—";
    public decimal? OnHand { get; private set; }
    public decimal? SavedVcf { get; set; }
    private bool _syncing;
    /// <summary>Tắt cascade RefreshAfter khi đang nạp hàng loạt bảng quý.</summary>
    public bool SuppressRefresh { get; set; }

    partial void OnQuantityChanged(string value)
    {
        if (Numbers.Try(value, out var qty) && qty > QuantityMath.MaxEditQty)
        {
            Quantity = Numbers.Qty(QuantityMath.MaxEditQty);
            return;
        }

        if (!SuppressRefresh)
        {
            RefreshAfter();
            QuantityEdited?.Invoke();
        }
    }

    public event Action? QuantityEdited;

    partial void OnOperatingChanged(string value)
    {
        if (_syncing || !UseShipNorm || SuppressRefresh)
            return;
        RecalcFromOperating();
    }

    public void SetShipNorm(decimal? rate)
    {
        ShipNorm = rate is > 0 ? rate : null;
        UseShipNorm = ConsumerId is not null;
        if (SuppressRefresh)
            return;
        if (!string.IsNullOrWhiteSpace(Operating))
            RecalcFromOperating();
        else
            RefreshAfter();
    }

    private void RecalcFromOperating()
    {
        if (_syncing)
            return;
        _syncing = true;
        try
        {
            if (ShipNorm is decimal norm && norm > 0 && Numbers.Try(Operating, out var operating) && operating >= 0)
                Quantity = Numbers.Qty(QuantityMath.ShipActual(operating, norm));
            else if (string.IsNullOrWhiteSpace(Operating) || ShipNorm is not > 0)
                Quantity = string.IsNullOrWhiteSpace(Operating) ? "" : Quantity;
        }
        finally
        {
            _syncing = false;
            if (!SuppressRefresh)
                RefreshAfter();
        }
    }

    public void SetOnHand(decimal? value)
    {
        OnHand = value;
        OnHandText = value is decimal qty ? Numbers.Qty(qty) : "—";
        if (!SuppressRefresh)
            RefreshAfter();
    }

    public void RefreshAfter()
    {
        if (SuppressRefresh)
            return;
        if (OnHand is not decimal before)
        {
            StockAfterText = "—";
            return;
        }

        if (!Numbers.Try(Quantity, out var used))
        {
            StockAfterText = Numbers.Qty(before);
            return;
        }

        StockAfterText = Numbers.Qty(before - used);
    }
}

public sealed class QuarterRow
{
    public int Year { get; init; }
    public int Quarter { get; init; }
    public DateTime EndDate { get; init; }
    public int Slips { get; init; }
    public string Total { get; init; } = "";
    public string Label => $"Quý {Quarter}/{Year}";
}

public partial class ConsumptionBookListItemVm : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    public Guid WarehouseId { get; init; }
    public string Kind { get; init; } = "";
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Status { get; init; } = "";
    public bool IsShip { get; init; }
    public bool HasData { get; init; }
}

public partial class OpeningLotRowVm : ObservableObject
{
    private bool _loading;

    public ObservableCollection<OpeningCellVm> Cells { get; } = [];
    [ObservableProperty] private ItemRow? _item;
    [ObservableProperty] private string _price = "";
    [ObservableProperty] private LotTypeRow? _lotType;
    [ObservableProperty] private bool _lotTypeLocked;
    public Guid LotTypeId => LotType?.Id ?? SeedIds.LotTypeTx;
    /// <summary>Nhóm lớn (NL) chưa chia loại lô — cột Loại lô để trống.</summary>
    public string LotTypeCode => IsGroupHeader || IsGroupTotal ? "" : (LotType?.Code ?? "TX");
    public Guid? SnapshotItemId { get; set; }
    public string SnapshotName { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string UnitName { get; set; } = "";
    public string QualityInfo { get; set; } = "";
    public string MeasurementNote { get; set; } = "";
    public decimal? Temperature { get; set; }
    public decimal CatalogVcf { get; set; }

    public void LoadItem(ItemRow? item, Guid? snapshotItemId, string snapshotName, string group, string unit, string quality, string measurement, decimal? temperature)
    {
        _loading = true;
        SnapshotItemId = snapshotItemId;
        SnapshotName = snapshotName;
        GroupName = group;
        UnitName = unit;
        QualityInfo = quality;
        MeasurementNote = measurement;
        Temperature = temperature;
        if (item is not null)
            CatalogVcf = item.Vcf;
        Item = item;
        _loading = false;
    }

    partial void OnItemChanged(ItemRow? value)
    {
        if (_loading || value is null)
            return;
        CatalogVcf = value.Vcf;
        if (value.Id != SnapshotItemId || string.IsNullOrEmpty(SnapshotName))
        {
            SnapshotItemId = value.Id;
            SnapshotName = "";
            GroupName = value.GroupName;
            UnitName = value.UnitName;
            QualityInfo = value.QualityInfo;
            MeasurementNote = value.MeasurementNote;
            Temperature = value.Temperature;
            foreach (var cell in Cells)
                cell.SavedVcf = null;
        }

        IdentityChanged?.Invoke(this);
        GroupChanged?.Invoke(this);
    }

    partial void OnPriceChanged(string value)
    {
        if (!_loading)
            IdentityChanged?.Invoke(this);
    }

    partial void OnLotTypeChanged(LotTypeRow? value)
    {
        if (!_loading && !IsSheetHeader && !LotTypeLocked)
            IdentityChanged?.Invoke(this);
    }

    public event Action<OpeningLotRowVm>? IdentityChanged;
    public event Action<OpeningLotRowVm>? GroupChanged;

    public int SheetOrder { get; set; }
    /// <summary>Dòng vừa thêm: giữ trên cùng đến khi Lưu hoặc Thêm dòng khác.</summary>
    public bool IsDraft { get; set; }
    public bool IsGroupTotal { get; set; }
    public bool IsGroupHeader { get; set; }
    public bool IsLotTypeHeader { get; set; }
    public bool IsSheetHeader => IsGroupHeader || IsLotTypeHeader || IsGroupTotal;
    public string RowBackground { get; set; } = "#FFFFFF";
    public string RowForeground { get; set; } = "#1B2836";
    public FontWeight RowFontWeight { get; set; } = FontWeights.Normal;
    public string LotName => SnapshotItemId == Item?.Id && SnapshotName.Length > 0 ? SnapshotName : Item?.Name ?? "";
    public string ItemDisplayName =>
        IsGroupHeader ? FuelGroupOrder.Label(GroupName)
        : IsLotTypeHeader ? $"  {(string.IsNullOrWhiteSpace(LotTypeCode) ? "TX" : LotTypeCode)}"
        : IsGroupTotal ? "Cộng"
        : LotName;

    public decimal TotalQty { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string TotalQtyText => TotalQty == 0 ? "" : Numbers.Qty(TotalQty);
    public string TotalAmountText => TotalAmount == 0 ? "" : Numbers.Money(TotalAmount);

    public void RecalcTotals()
    {
        if (IsSheetHeader)
            return;
        decimal qty = 0;
        foreach (var cell in Cells)
        {
            if (Numbers.Try(cell.Quantity, out var cellQty) && cellQty > 0)
                qty = QuantityMath.Whole(qty + QuantityMath.Whole(cellQty));
        }

        SetTotals(qty, AmountOf(qty, Price));
    }

    public void SetTotals(decimal qty, decimal amount)
    {
        TotalQty = qty;
        TotalAmount = amount;
        OnPropertyChanged(nameof(TotalQty));
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(TotalQtyText));
        OnPropertyChanged(nameof(TotalAmountText));
    }

    public static decimal AmountOf(decimal qty, string priceText)
    {
        if (qty <= 0)
            return 0;
        if (!Numbers.Try(priceText, out var price)
            || !QuantityMath.TryWholeMoney(price, out var whole)
            || !QuantityMath.TryAmount(whole, qty, out var amount))
            return 0;
        return amount;
    }

    public SheetGroupKey SheetGroup =>
        IsDraft
            ? new SheetGroupKey(-1, "")
            : IsGroupHeader || IsLotTypeHeader || IsGroupTotal
            ? new SheetGroupKey(FuelGroupOrder.Rank(GroupName), FuelGroupOrder.Label(GroupName))
            : Item is null && string.IsNullOrWhiteSpace(GroupName)
                ? new SheetGroupKey(100, "")
                : new SheetGroupKey(FuelGroupOrder.Rank(GroupName), FuelGroupOrder.Label(GroupName));

    public LotTypeGroupKey LotTypeGroup =>
        IsDraft || IsGroupTotal || IsGroupHeader
            ? new LotTypeGroupKey(-1, "")
            : IsLotTypeHeader
                ? new LotTypeGroupKey(LotTypeGroupKey.RankOf(LotTypeCode), string.IsNullOrWhiteSpace(LotTypeCode) ? "TX" : LotTypeCode)
                : new LotTypeGroupKey(LotTypeGroupKey.RankOf(LotTypeCode), string.IsNullOrWhiteSpace(LotTypeCode) ? "TX" : LotTypeCode);
}

public sealed class SheetGroupKey : IComparable
{
    public SheetGroupKey(int rank, string name)
    {
        Rank = rank;
        Name = name;
    }

    public int Rank { get; }
    public string Name { get; }
    public string Display => Name;

    public int CompareTo(object? obj)
    {
        if (obj is not SheetGroupKey other)
            return 1;
        var byRank = Rank.CompareTo(other.Rank);
        return byRank != 0 ? byRank : string.Compare(Name, other.Name, StringComparison.CurrentCultureIgnoreCase);
    }

    public override bool Equals(object? obj) =>
        obj is SheetGroupKey other && Rank == other.Rank && string.Equals(Name, other.Name, StringComparison.CurrentCultureIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(Rank, Name.ToUpperInvariant());

    public override string ToString() => Name;
}

public sealed class LotTypeGroupKey : IComparable
{
    public LotTypeGroupKey(int rank, string code)
    {
        Rank = rank;
        Code = code;
    }

    public int Rank { get; }
    public string Code { get; }
    public string Display => Code;

    public static int RankOf(string? code) => (code ?? "").Trim().ToUpperInvariant() switch
    {
        "TX" => 0,
        "SSCĐ" or "SSCD" => 1,
        "IUU" => 2,
        _ => 10
    };

    public int CompareTo(object? obj)
    {
        if (obj is not LotTypeGroupKey other)
            return 1;
        var byRank = Rank.CompareTo(other.Rank);
        return byRank != 0 ? byRank : string.Compare(Code, other.Code, StringComparison.CurrentCultureIgnoreCase);
    }

    public override bool Equals(object? obj) =>
        obj is LotTypeGroupKey other && Rank == other.Rank && string.Equals(Code, other.Code, StringComparison.CurrentCultureIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(Rank, Code.ToUpperInvariant());

    public override string ToString() => Code;
}

public partial class OpeningVm : PageVm, IDocumentEditor
{
    private static readonly (string Header, string SubHeader, string Row, string Ink)[] Palette =
    [
        ("#1A4F7A", "#3D7AAB", "#E8F0F8", "#1B2836"), // Dầu — xanh đậm / xanh vừa
        ("#7A4A0C", "#A56B1E", "#FFF6E8", "#5C3A0A"), // Xăng — nâu đậm / nâu vừa
        ("#1F5A40", "#3D8A62", "#E8F5EE", "#1B3D2E"), // Nhớt
        ("#5A2A5A", "#7A4A7A", "#F5EAF5", "#3D2140"), // Mỡ
        ("#345A70", "#5A7A94", "#EEF2F5", "#1B2836"), // Khác
        ("#7A2E24", "#9A4A40", "#F8EBE8", "#4A1F18"),
    ];

    private readonly List<Guid> _pendingVoids = [];

    private int _sheetOrder;
    private bool _rebuildQueued;
    private bool _applying;
    private bool _saving;
    private int _loadGeneration;
    private bool _openingReady;
    private DateTime _openingLoadedDate;
    private int _openingCatalogGeneration = int.MinValue;

    public OpeningVm(FuelSystem system) : base(system)
    {
        SheetRows = CollectionViewSource.GetDefaultView(Rows);
    }

    public ObservableCollection<WarehouseRow> Warehouses { get; } = [];
    public ObservableCollection<ItemRow> Items { get; } = [];
    public ObservableCollection<LotTypeRow> LotTypes { get; } = [];
    public ObservableCollection<OpeningLotRowVm> Rows { get; } = [];
    public ICollectionView SheetRows { get; }
    [ObservableProperty] private OpeningLotRowVm? _selected;
    [ObservableProperty] private DateTime _documentDate = DateTime.Today;
    [ObservableProperty] private string _grandTotalAmountText = "0";
    public event Action? SheetReady;

    public override void Refresh() => _ = RefreshAsync();

    public override async Task RefreshAsync(IProgress<DemoProgress>? progress = null)
    {
        var generation = System.CatalogGeneration;
        if (_openingReady
            && Rows.Count > 0
            && _openingLoadedDate.Date == DocumentDate.Date
            && _openingCatalogGeneration == generation)
            return;
        await ReloadSheetAsync();
    }

    public void EditDocument(Guid id)
    {
        _loadGeneration++;
        _openingReady = false;
        ApplyOpening(ReadOpening());
        Selected = Rows.FirstOrDefault(x => !x.IsSheetHeader && x.Cells.Any(cell => cell.DocumentId == id));
        if (Selected is null)
            Fail("Không thấy phiếu tồn đầu này trong bảng. Phiếu đã hủy không hiện ở đây.");
        else
            Ok("Đã chọn dòng lô tương ứng trong bảng.");
    }

    private async Task ReloadSheetAsync()
    {
        var generation = ++_loadGeneration;
        var snapshot = await Task.Run(ReadOpening);
        if (generation != _loadGeneration)
            return;
        ApplyOpening(snapshot);
    }

    private OpeningSnapshot ReadOpening() => new(
        System.GetWarehouses(),
        System.GetItems(),
        System.ListSheetHeaders(DocumentKind.Opening));

    private void ApplyOpening(OpeningSnapshot snapshot)
    {
        _applying = true;
        try
        {
            _pendingVoids.Clear();
            Warehouses.Clear();
            foreach (var row in snapshot.Warehouses)
                Warehouses.Add(row);
            Items.Clear();
            foreach (var row in snapshot.Items)
                Items.Add(row);
            ReloadLotTypes();
            Selected = null;
            var grouped = new Dictionary<(string Key, long Price, Guid LotTypeId), OpeningLotRowVm>();
            var duplicate = 0;
            DateTime? date = null;
            foreach (var doc in snapshot.Documents)
            {
                date = date is null || doc.DocumentDate > date ? doc.DocumentDate : date;
                var typeId = doc.LotTypeId ?? SeedIds.LotTypeTx;
                var key = (QuantityMath.LotKey(doc.ItemName), doc.UnitPrice, typeId);
                if (!grouped.TryGetValue(key, out var lot))
                {
                    lot = CreateRow();
                    lot.Price = Numbers.Money(doc.UnitPrice);
                    lot.LotType = LotTypeUi.Pick(LotTypes, typeId);
                    lot.LoadItem(
                        Items.FirstOrDefault(x => doc.ItemId is Guid id && x.Id == id) ?? Items.FirstOrDefault(x => x.Name == doc.ItemName),
                        doc.ItemId,
                        doc.ItemName,
                        doc.GroupName,
                        doc.UnitName,
                        doc.QualityInfo,
                        doc.MeasurementNote,
                        doc.Temperature);
                    grouped.Add(key, lot);
                }

                var cell = lot.Cells.FirstOrDefault(x => x.WarehouseId == doc.WarehouseId);
                if (cell is null)
                    continue;
                if (cell.DocumentId is null)
                {
                    cell.DocumentId = doc.Id;
                    cell.Quantity = Numbers.Qty(doc.ActualQuantity);
                }
                else
                    duplicate++;
            }

            foreach (var lot in grouped.Values)
                lot.RecalcTotals();
            RebuildSheetStructure(grouped.Values.ToList(), force: true);
            if (date is DateTime known)
                DocumentDate = known;
            _openingReady = true;
            _openingLoadedDate = DocumentDate;
            _openingCatalogGeneration = System.CatalogGeneration;
            if (duplicate > 0)
                Ok($"Có {duplicate} phiếu tồn đầu trùng lô và kho. Ô đang hiện phiếu mới nhất.");
            SheetReady?.Invoke();
        }
        finally
        {
            _applying = false;
        }
    }

    public void AddLotFromDialog(ItemRow item, string priceText, LotTypeRow lotType)
    {
        if (!Numbers.Try(priceText, out var price) || !QuantityMath.TryWholeMoney(price, out var whole) || whole < 0)
        {
            Fail("Đơn giá phải là số nguyên không âm.");
            return;
        }

        var key = (QuantityMath.LotKey(item.Name), whole, lotType.Id);
        if (Rows.Any(x => !x.IsSheetHeader
                          && QuantityMath.LotKey(x.LotName) == key.Item1
                          && Numbers.Try(x.Price, out var existing)
                          && QuantityMath.TryWholeMoney(existing, out var existingWhole)
                          && existingWhole == whole
                          && x.LotTypeId == lotType.Id))
        {
            Fail($"Đã có dòng {item.Name} | {Numbers.Money(price)} | {lotType.Code}. Chọn dòng đó để nhập tồn.");
            return;
        }

        var row = CreateRow();
        row.Price = Numbers.Money(whole);
        row.LotType = LotTypeUi.Pick(LotTypes, lotType.Id) ?? lotType;
        row.LoadItem(item, item.Id, item.Name, item.GroupName, item.UnitName, item.QualityInfo, item.MeasurementNote, item.Temperature);
        row.RecalcTotals();
        var data = Rows.Where(x => !x.IsSheetHeader).ToList();
        data.Add(row);
        RebuildSheetStructure(data);
        Selected = row;
        Ok($"Đã thêm lô {item.Name} ({lotType.Code}). Nhập số lượng theo kho rồi Lưu bảng.");
    }

    [RelayCommand]
    private void AddLot()
    {
        // Popup do OpeningView mở; giữ command để binding cũ không lỗi.
    }

    [RelayCommand]
    private void RemoveLot(OpeningLotRowVm? row)
    {
        row ??= Selected;
        if (row is null || row.IsSheetHeader)
            return;
        var ids = row.Cells.Where(x => x.DocumentId is Guid).Select(x => x.DocumentId!.Value).ToList();
        if (ids.Count > 0 && !GroupsVm.Confirm("Xóa dòng này? Các phiếu tồn đầu của dòng sẽ được hoàn khi lưu bảng."))
            return;
        _pendingVoids.AddRange(ids);
        var data = Rows.Where(x => !x.IsSheetHeader && !ReferenceEquals(x, row)).ToList();
        RebuildSheetStructure(data);
        Selected = data.LastOrDefault();
    }

    [RelayCommand]
    private void Save()
    {
        if (_saving)
            return;
        if (Warehouses.Count == 0)
        {
            Fail("Chưa có kho.");
            return;
        }

        var cells = new List<OpeningRequest>();
        var voids = new List<Guid>(_pendingVoids);
        var seen = new HashSet<(string Key, long Price, Guid LotTypeId)>();
        foreach (var row in Rows)
        {
            if (row.IsSheetHeader)
                continue;
            var filled = new List<(OpeningCellVm Cell, decimal Quantity)>();
            var cleared = new List<Guid>();
            foreach (var cell in row.Cells)
            {
                if (string.IsNullOrWhiteSpace(cell.Quantity))
                {
                    if (cell.DocumentId is Guid id)
                        cleared.Add(id);
                    continue;
                }

                if (!Numbers.Try(cell.Quantity, out var quantity))
                {
                    Fail("Số lượng tồn đầu phải lớn hơn 0. Để trống ô nếu kho không có lô này.");
                    return;
                }

                quantity = QuantityMath.ClampEditQty(QuantityMath.Whole(quantity));
                cell.Quantity = Numbers.Qty(quantity);
                if (quantity <= 0)
                {
                    Fail("Số lượng tồn đầu phải lớn hơn 0. Để trống ô nếu kho không có lô này.");
                    return;
                }

                filled.Add((cell, quantity));
            }

            if (filled.Count == 0 && cleared.Count == 0)
                continue;
            if (row.Item is null)
            {
                Fail("Chọn mặt hàng cho từng dòng có số lượng.");
                return;
            }

            if (!Numbers.Try(row.Price, out var price) || !QuantityMath.TryWholeMoney(price, out var whole) || whole < 0)
            {
                Fail($"Đơn giá của {row.Item.Name} phải là số nguyên không âm.");
                return;
            }

            var name = row.LotName;
            if (name.Length == 0)
            {
                Fail("Dòng lô thiếu tên mặt hàng.");
                return;
            }

            var key = (QuantityMath.LotKey(name), whole, row.LotTypeId);
            if (!seen.Add(key))
            {
                Fail($"Hai dòng cùng mặt hàng, đơn giá và loại lô ({name}, {Numbers.Money(price)}, {row.LotTypeCode}). Gộp vào một dòng.");
                return;
            }

            voids.AddRange(cleared);
            foreach (var (cell, quantity) in filled)
            {
                var warehouse = Warehouses.First(x => x.Id == cell.WarehouseId);
                cells.Add(new OpeningRequest
                {
                    DocumentId = cell.DocumentId,
                    DocumentDate = DocumentDate,
                    WarehouseId = warehouse.Id,
                    WarehouseName = warehouse.Name,
                    WarehouseTypeName = warehouse.TypeName,
                    ItemId = row.Item.Id,
                    ItemName = name,
                    GroupName = row.GroupName,
                    UnitName = row.UnitName,
                    QualityInfo = row.QualityInfo,
                    Temperature = row.Temperature,
                    MeasurementNote = row.MeasurementNote,
                    UnitPrice = price,
                    LotTypeId = row.LotTypeId,
                    ActualQuantity = quantity
                });
            }
        }

        if (cells.Count == 0 && voids.Count == 0)
        {
            Fail("Nhập số lượng tồn đầu của ít nhất một lô.");
            return;
        }

        if (voids.Count > 0 && !GroupsVm.Confirm("Lưu bảng sẽ hoàn tồn các ô hoặc dòng đã xóa. Tiếp tục?"))
            return;

        if (System.HasActiveImportOrIssue())
        {
            if (!GroupsVm.Confirm(
                    "Đã có phiếu nhập/xuất. Sửa tồn đầu kỳ có thể làm lệch sổ và tồn kho. Bạn có chắc muốn tiếp tục?"))
                return;
            if (!GroupsVm.Confirm(
                    "Xác nhận lần 2: vẫn lưu thay đổi tồn đầu kỳ khi đã có phiếu nhập/xuất?"))
                return;
        }

        _saving = true;
        try
        {
            Show(System.SaveOpeningSheet(new OpeningSheetRequest
            {
                DocumentDate = DocumentDate,
                Cells = cells,
                VoidIds = voids.Distinct().ToList()
            }));
            if (!BannerIsError)
            {
                _openingReady = false;
                Refresh();
            }
        }
        finally
        {
            _saving = false;
        }
    }

    private sealed record OpeningSnapshot(
        IReadOnlyList<WarehouseRow> Warehouses,
        IReadOnlyList<ItemRow> Items,
        IReadOnlyList<SheetHeader> Documents);

    private OpeningLotRowVm CreateRow()
    {
        var row = new OpeningLotRowVm
        {
            SheetOrder = ++_sheetOrder,
            LotType = LotTypeUi.Pick(LotTypes, SeedIds.LotTypeTx)
        };
        row.GroupChanged += OnRowIdentityChanged;
        row.IdentityChanged += OnRowIdentityChanged;
        foreach (var warehouse in Warehouses)
        {
            var cell = new OpeningCellVm { WarehouseId = warehouse.Id };
            cell.QuantityEdited += () => OnRowQuantityEdited(row);
            row.Cells.Add(cell);
        }

        return row;
    }

    private void OnRowIdentityChanged(OpeningLotRowVm row)
    {
        if (_applying || row.IsSheetHeader)
            return;
        row.RecalcTotals();
        QueueRebuildStructure();
    }

    private void OnRowQuantityEdited(OpeningLotRowVm row)
    {
        if (_applying || row.IsSheetHeader)
            return;
        row.RecalcTotals();
        UpdateHeaderTotals();
        RefreshGrandTotal();
    }

    private void ReloadLotTypes()
    {
        LotTypes.Clear();
        foreach (var row in System.GetLotTypes())
            LotTypes.Add(row);
    }

    private void QueueRebuildStructure()
    {
        if (_applying || _rebuildQueued)
            return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            RebuildSheetStructure(Rows.Where(x => !x.IsSheetHeader).ToList());
            return;
        }

        _rebuildQueued = true;
        dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _rebuildQueued = false;
            if (_applying)
                return;
            RebuildSheetStructure(Rows.Where(x => !x.IsSheetHeader).ToList());
        });
    }

    private void RebuildSheetStructure(IReadOnlyList<OpeningLotRowVm> dataRows, bool force = false)
    {
        if (_applying && !force)
            return;

        var selected = Selected is { IsSheetHeader: false } ? Selected : null;
        var ordered = dataRows
            .OrderBy(x => x.SheetGroup.Rank)
            .ThenBy(x => x.SheetGroup.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.LotTypeGroup.Rank)
            .ThenBy(x => x.LotTypeGroup.Code, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.LotName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.SheetOrder)
            .ToList();

        Rows.Clear();
        foreach (var fuelGroup in ordered.GroupBy(x => x.SheetGroup))
        {
            var fuelMembers = fuelGroup.ToList();
            var color = ColorIndexOf(fuelGroup.Key.Rank);
            var groupQty = fuelMembers.Sum(x => x.TotalQty);
            var groupAmount = QuantityMath.RoundMoney(fuelMembers.Sum(x => x.TotalAmount));
            Rows.Add(CreateGroupHeader(fuelGroup.Key.Name, color, groupQty, groupAmount));

            foreach (var typeGroup in fuelMembers
                         .GroupBy(x => x.LotTypeGroup)
                         .OrderBy(g => g.Key.Rank)
                         .ThenBy(g => g.Key.Code, StringComparer.CurrentCultureIgnoreCase))
            {
                var typeMembers = typeGroup.ToList();
                var typeQty = typeMembers.Sum(x => x.TotalQty);
                var typeAmount = QuantityMath.RoundMoney(typeMembers.Sum(x => x.TotalAmount));
                Rows.Add(CreateLotTypeHeader(fuelGroup.Key.Name, typeGroup.Key.Code, color, typeQty, typeAmount));
                foreach (var row in typeMembers)
                {
                    ApplyTone(row, color, header: false, lotTypeHeader: false);
                    Rows.Add(row);
                }
            }
        }

        Selected = selected is not null && Rows.Contains(selected) ? selected : Rows.FirstOrDefault(x => !x.IsSheetHeader);
        RefreshGrandTotal();
    }

    private void RefreshGrandTotal()
    {
        var amount = QuantityMath.RoundMoney(Rows.Where(x => !x.IsSheetHeader).Sum(x => x.TotalAmount));
        GrandTotalAmountText = Numbers.Money(amount);
    }

    private void UpdateHeaderTotals()
    {
        var data = Rows.Where(x => !x.IsSheetHeader).ToList();
        OpeningLotRowVm? groupHeader = null;
        OpeningLotRowVm? typeHeader = null;
        var groupMembers = new List<OpeningLotRowVm>();
        var typeMembers = new List<OpeningLotRowVm>();

        void FlushType()
        {
            if (typeHeader is null)
                return;
            typeHeader.SetTotals(
                typeMembers.Sum(x => x.TotalQty),
                QuantityMath.RoundMoney(typeMembers.Sum(x => x.TotalAmount)));
            typeMembers.Clear();
            typeHeader = null;
        }

        void FlushGroup()
        {
            FlushType();
            if (groupHeader is null)
                return;
            groupHeader.SetTotals(
                groupMembers.Sum(x => x.TotalQty),
                QuantityMath.RoundMoney(groupMembers.Sum(x => x.TotalAmount)));
            groupMembers.Clear();
            groupHeader = null;
        }

        foreach (var row in Rows)
        {
            if (row.IsGroupHeader)
            {
                FlushGroup();
                groupHeader = row;
                continue;
            }

            if (row.IsLotTypeHeader)
            {
                FlushType();
                typeHeader = row;
                continue;
            }

            if (row.IsSheetHeader)
                continue;
            groupMembers.Add(row);
            typeMembers.Add(row);
        }

        FlushGroup();
        RefreshGrandTotal();
    }

    private OpeningLotRowVm CreateGroupHeader(string groupName, int colorIndex, decimal qty, decimal amount)
    {
        var row = new OpeningLotRowVm
        {
            IsGroupHeader = true,
            GroupName = groupName,
            SheetOrder = int.MinValue
        };
        foreach (var warehouse in Warehouses)
            row.Cells.Add(new OpeningCellVm { WarehouseId = warehouse.Id, SuppressRefresh = true });
        row.SetTotals(qty, amount);
        ApplyTone(row, colorIndex, header: true, lotTypeHeader: false);
        return row;
    }

    private OpeningLotRowVm CreateLotTypeHeader(string groupName, string lotTypeCode, int colorIndex, decimal qty, decimal amount)
    {
        var code = string.IsNullOrWhiteSpace(lotTypeCode) ? "TX" : lotTypeCode;
        var row = new OpeningLotRowVm
        {
            IsLotTypeHeader = true,
            GroupName = groupName,
            LotType = LotTypes.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase))
                      ?? LotTypeUi.Pick(LotTypes, SeedIds.LotTypeTx),
            SheetOrder = int.MinValue + 1
        };
        foreach (var warehouse in Warehouses)
            row.Cells.Add(new OpeningCellVm { WarehouseId = warehouse.Id, SuppressRefresh = true });
        row.SetTotals(qty, amount);
        ApplyTone(row, colorIndex, header: false, lotTypeHeader: true);
        return row;
    }

    private static void ApplyTone(OpeningLotRowVm row, int colorIndex, bool header, bool lotTypeHeader)
    {
        var tone = Palette[ColorIndexOf(colorIndex)];
        if (header)
        {
            // Nền đậm hơn + chữ trắng — dễ đọc hơn tông cũ.
            row.RowBackground = tone.Header;
            row.RowForeground = "#FFFFFF";
            row.RowFontWeight = FontWeights.SemiBold;
        }
        else if (lotTypeHeader)
        {
            row.RowBackground = tone.SubHeader;
            row.RowForeground = "#FFFFFF";
            row.RowFontWeight = FontWeights.SemiBold;
        }
        else
        {
            row.RowBackground = tone.Row;
            row.RowForeground = tone.Ink;
            row.RowFontWeight = FontWeights.Normal;
        }
    }

    private static int ColorIndexOf(int colorIndex)
    {
        if (colorIndex is >= 0 and <= 3)
            return colorIndex;
        return 4 + (Math.Abs(colorIndex) % (Palette.Length - 4));
    }
}

file static class SheetGroups
{
    public static void Apply(ListCollectionView view, IEnumerable<OpeningLotRowVm> rows, bool nestLotTypes = false)
    {
        if (view.IsAddingNew)
            view.CommitNew();
        if (view.IsEditingItem)
            view.CommitEdit();

        var fuel = new PropertyGroupDescription(nameof(OpeningLotRowVm.SheetGroup));
        foreach (var key in rows.Select(x => x.SheetGroup).Distinct().OrderBy(x => x.Rank).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            // Rank < 0 = nhóm draft (không tiêu đề); Name rỗng = bỏ.
            if (key.Rank < 0 || key.Name.Length == 0)
                continue;
            fuel.GroupNames.Add(key);
        }

        view.GroupDescriptions.Clear();
        view.GroupDescriptions.Add(fuel);
        if (!nestLotTypes)
            return;

        // Không gắn GroupNames cho loại lô: WPF sẽ tạo header rỗng TX/SSCĐ/IUU
        // dưới mọi nhóm nhiên liệu → thêm dòng bị lag. Thứ tự nhờ CustomSort.
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OpeningLotRowVm.LotTypeGroup)));
    }
}

file sealed class OpeningSheetSort : System.Collections.IComparer
{
    public static readonly OpeningSheetSort Instance = new();

    public int Compare(object? x, object? y)
    {
        if (x is SheetGroupKey leftKey && y is SheetGroupKey rightKey)
            return leftKey.CompareTo(rightKey);
        if (x is LotTypeGroupKey leftType && y is LotTypeGroupKey rightType)
            return leftType.CompareTo(rightType);
        if (x is not OpeningLotRowVm left || y is not OpeningLotRowVm right)
            return 0;
        if (left.IsDraft != right.IsDraft)
            return left.IsDraft ? -1 : 1;
        var byGroup = left.SheetGroup.CompareTo(right.SheetGroup);
        if (byGroup != 0)
            return byGroup;
        var byLotType = left.LotTypeGroup.CompareTo(right.LotTypeGroup);
        if (byLotType != 0)
            return byLotType;
        if (left.IsGroupTotal != right.IsGroupTotal)
            return left.IsGroupTotal ? 1 : -1;
        var byName = string.Compare(left.LotName, right.LotName, StringComparison.CurrentCultureIgnoreCase);
        if (byName != 0)
            return byName;
        var byPrice = ComparePrice(left.Price, right.Price);
        return byPrice != 0 ? byPrice : left.SheetOrder.CompareTo(right.SheetOrder);
    }

    private static int ComparePrice(string left, string right)
    {
        var leftOk = Numbers.Try(left, out var leftPrice);
        var rightOk = Numbers.Try(right, out var rightPrice);
        if (leftOk && rightOk)
            return leftPrice.CompareTo(rightPrice);
        if (leftOk != rightOk)
            return leftOk ? -1 : 1;
        return string.Compare(left, right, StringComparison.CurrentCultureIgnoreCase);
    }
}

public partial class ImportVm : PageVm, IDocumentEditor
{
    public ImportVm(FuelSystem system) : base(system) => Fields = new FieldFormVm(system, DocumentFamily.Import);
    public FieldFormVm Fields { get; }
    public ObservableCollection<OptionRow> Warehouses { get; } = [];
    public ObservableCollection<OptionRow> Items { get; } = [];
    public ObservableCollection<DocumentRow> Documents { get; } = [];
    public ObservableCollection<SplitLine> PreviewLines { get; } = [];
    [ObservableProperty] private OptionRow? _warehouse;
    [ObservableProperty] private OptionRow? _item;
    [ObservableProperty] private DocumentRow? _selected;
    [ObservableProperty] private DateTime _documentDate = DateTime.Today;
    [ObservableProperty] private string _price = "";
    [ObservableProperty] private string _quantity = "";
    [ObservableProperty] private string _vcf = "";
    [ObservableProperty] private string _amount = "";
    [ObservableProperty] private string _info = "";
    [ObservableProperty] private string _preview = "Nhập đơn giá, số lượng và VCF để tính thực nhập.";
    private Guid? _editingId;
    private bool _suppress;
    private string _itemName = "", _group = "", _unit = "", _quality = "", _measure = "", _rule = "";
    private decimal? _temperature;

    partial void OnWarehouseChanged(OptionRow? value)
    {
        if (_suppress || value is null) return;
        var wh = System.GetWarehouses().FirstOrDefault(x => x.Id == value.Id);
        Fields.ApplySample(null);
    }

    partial void OnItemChanged(OptionRow? value)
    {
        if (_suppress || value is null) return;
        var item = System.GetItem(value.Id);
        if (item is null) return;
        CopyItem(item);
        Vcf = item.Vcf.ToString(CultureInfo.CurrentCulture);
        RefreshPreview();
    }

    partial void OnPriceChanged(string value) => RefreshPreview();
    partial void OnQuantityChanged(string value) => RefreshPreview();
    partial void OnVcfChanged(string value) => RefreshPreview();
    partial void OnAmountChanged(string value) => RefreshPreview();

    public override void Refresh()
    {
        var wh = Warehouse?.Id;
        var item = Item?.Id;
        var sample = Fields.SelectedSample?.Id;
        Warehouses.Clear();
        Items.Clear();
        foreach (var row in System.GetWarehouses()) Warehouses.Add(new OptionRow(row.Id, row.Name));
        foreach (var row in System.GetItems()) Items.Add(new OptionRow(row.Id, row.Name));
        Documents.Clear();
        foreach (var row in System.ListDocuments(DocumentKind.Import)) Documents.Add(row);
        Fields.ReloadSets(sample);
        _suppress = true;
        Warehouse = Warehouses.FirstOrDefault(x => x.Id == wh) ?? Warehouses.FirstOrDefault();
        Item = Items.FirstOrDefault(x => x.Id == item);
        _suppress = false;
        if (_editingId is null && Fields.Rows.Count == 0)
            Fields.ApplySample(null);
    }

    [RelayCommand]
    private void New()
    {
        _editingId = null; Selected = null; Price = ""; Quantity = ""; Amount = ""; DocumentDate = DateTime.Today; Banner = "";
        if (Item is not null) OnItemChanged(Item);
        if (Warehouse is not null) OnWarehouseChanged(Warehouse);
    }

    [RelayCommand]
    private void Save()
    {
        if (Warehouse is null || Item is null) { Fail("Chọn kho và mặt hàng."); return; }
        if (!Numbers.Try(Price, out var price) || !Numbers.Try(Quantity, out var quantity) || !Numbers.Try(Vcf, out var vcf))
        { Fail("Đơn giá, số lượng hoặc VCF không hợp lệ."); return; }
        quantity = QuantityMath.Whole(quantity);
        Quantity = Numbers.Qty(quantity);
        if (quantity <= 0) { Fail("Số lượng phải lớn hơn 0."); return; }
        decimal? amount = null;
        if (!string.IsNullOrWhiteSpace(Amount))
        {
            if (!Numbers.Try(Amount, out var parsed)) { Fail("Thành tiền không hợp lệ."); return; }
            amount = parsed;
        }

        var wh = System.GetWarehouses().First(x => x.Id == Warehouse.Id);
        Show(System.SaveImport(new ImportRequest
        {
            DocumentId = _editingId,
            DocumentDate = DocumentDate,
            WarehouseId = wh.Id,
            WarehouseName = wh.Name,
            WarehouseTypeName = wh.TypeName,
            ItemId = Item.Id,
            ItemName = _itemName,
            GroupName = _group,
            UnitName = _unit,
            QualityInfo = _quality,
            Temperature = _temperature,
            MeasurementNote = _measure,
            ConversionRule = _rule,
            Vcf = vcf,
            UnitPrice = price,
            InputQuantity = quantity,
            Amount = amount,
            Fields = Fields.Inputs(),
            AddToSampleSetId = Fields.SampleToUpdate
        }));
        if (!BannerIsError) { _editingId = null; Refresh(); }
    }

    [RelayCommand] private void EditSelected() { if (Selected is not null) EditDocument(Selected.Id); }

    [RelayCommand]
    private void VoidSelected()
    {
        var id = Selected?.Id ?? _editingId;
        if (id is null) return;
        if (!GroupsVm.Confirm("Hủy chứng từ và hoàn tồn?")) return;
        Show(System.Void(id.Value));
        if (!BannerIsError) { _editingId = null; Refresh(); }
    }

    public void EditDocument(Guid id)
    {
        var doc = System.GetDocument(id);
        if (doc is null || doc.Kind != DocumentKind.Import) return;
        Refresh();
        _editingId = doc.Id;
        _suppress = true;
        DocumentDate = doc.DocumentDate;
        Warehouse = Warehouses.FirstOrDefault(x => x.Id == doc.WarehouseId);
        Item = Items.FirstOrDefault(x => x.Id == doc.ItemId);
        _suppress = false;
        _itemName = doc.ItemName;
        _group = doc.GroupName;
        _unit = doc.UnitName;
        _quality = doc.QualityInfo;
        _temperature = doc.Temperature;
        _measure = doc.MeasurementNote;
        _rule = doc.ConversionRule;
        Price = Numbers.Money(doc.UnitPrice);
        Quantity = Numbers.Qty(doc.InputQuantity);
        Vcf = doc.Vcf.ToString(CultureInfo.CurrentCulture);
        Amount = doc.Amount is decimal amount ? Numbers.Money(amount) : "";
        Info = $"{doc.ItemName} · {doc.UnitName} · {doc.QualityInfo} · snapshot";
        Fields.ApplySample(null);
        Fields.LoadSnapshot(doc.Fields);
        RefreshPreview();
        Ok($"Đang sửa {doc.Number}. VCF và tên mặt hàng lấy từ snapshot, không tính lại từ danh mục.");
    }

    private void CopyItem(ItemRow item)
    {
        _itemName = item.Name;
        _group = item.GroupName;
        _unit = item.UnitName;
        _quality = item.QualityInfo;
        _temperature = item.Temperature;
        _measure = item.MeasurementNote;
        _rule = item.ConversionRule;
        Info = $"{item.Name} · {item.UnitName} · {item.QualityInfo}";
    }

    private void RefreshPreview()
    {
        PreviewLines.Clear();
        if (!Numbers.Try(Price, out var price) || !Numbers.Try(Quantity, out var quantity) || !Numbers.Try(Vcf, out var vcf))
        {
            Preview = "Nhập đơn giá, số lượng và VCF để tính thực nhập.";
            return;
        }

        var wholeQuantity = QuantityMath.Whole(quantity);
        if (wholeQuantity != quantity && !Numbers.EndsWithSeparator(Quantity))
        {
            Quantity = Numbers.Qty(wholeQuantity);
            return;
        }

        decimal? amount = null;
        if (!string.IsNullOrWhiteSpace(Amount))
        {
            if (!Numbers.Try(Amount, out var parsed)) { Preview = "Thành tiền chưa hợp lệ."; return; }
            amount = parsed;
        }

        var preview = System.PreviewImport(price, quantity, amount, vcf);
        if (!preview.Ok) { Preview = preview.Message; return; }
        foreach (var line in preview.Lines) PreviewLines.Add(line);
        Preview = preview.WasSplit
            ? $"Tách {preview.Lines.Count} lô. Thực nhập {Numbers.Qty(preview.TotalActual)}. Thành tiền {Numbers.Money(preview.TotalAmount)}."
            : $"Một lô. Thực nhập {Numbers.Qty(preview.TotalActual)} = số lượng nhập × VCF.";
    }
}

public partial class TransferVm : PageVm, IDocumentEditor
{
    public TransferVm(FuelSystem system) : base(system) => Fields = new FieldFormVm(system, DocumentFamily.Export);
    public FieldFormVm Fields { get; }
    public ObservableCollection<OptionRow> Warehouses { get; } = [];
    public ObservableCollection<LotOption> Lots { get; } = [];
    public ObservableCollection<DocumentRow> Documents { get; } = [];
    [ObservableProperty] private OptionRow? _source;
    [ObservableProperty] private OptionRow? _destination;
    [ObservableProperty] private LotOption? _lot;
    [ObservableProperty] private DocumentRow? _selected;
    [ObservableProperty] private DateTime _documentDate = DateTime.Today;
    [ObservableProperty] private string _actual = "";
    [ObservableProperty] private string _vcf = "";
    [ObservableProperty] private string _display = "";
    [ObservableProperty] private string _normText = "";
    [ObservableProperty] private string _kilometers = "";
    [ObservableProperty] private string _originPlace = "";
    [ObservableProperty] private string _destinationPlace = "";
    [ObservableProperty] private bool _showVehicleCalc;
    private Guid? _editingId;
    private bool _suppress;
    private ConsumerRow? _transferConsumer;

    partial void OnSourceChanged(OptionRow? value)
    {
        if (_suppress) return;
        ReloadLots(null);
        if (value is null) return;
        var wh = System.GetWarehouses().FirstOrDefault(x => x.Id == value.Id);
        if (_editingId is null) Fields.ApplySample(wh?.DefaultExportSampleSetId);
        RefreshTransferConsumer();
    }

    partial void OnDestinationChanged(OptionRow? value)
    {
        if (_suppress) return;
        RefreshTransferConsumer();
    }

    partial void OnLotChanged(LotOption? value) { if (!_suppress) PrefillVcf(value); }
    partial void OnActualChanged(string value) => RefreshDisplay();
    partial void OnVcfChanged(string value) => RefreshDisplay();
    partial void OnNormTextChanged(string value) { if (!_suppress) ApplyVehicleQuantity(); }
    partial void OnKilometersChanged(string value) { if (!_suppress) ApplyVehicleQuantity(); }

    public override void Refresh()
    {
        var source = Source?.Id;
        var dest = Destination?.Id;
        var sample = Fields.SelectedSample?.Id;
        Warehouses.Clear();
        foreach (var row in System.GetWarehouses())
            Warehouses.Add(new OptionRow(row.Id, $"{row.Name} ({row.ConsumerTypeName ?? row.TypeName})"));
        Documents.Clear();
        foreach (var row in System.ListDocuments(DocumentKind.Transfer)) Documents.Add(row);
        Fields.ReloadSets(sample);
        _suppress = true;
        Source = Warehouses.FirstOrDefault(x => x.Id == source) ?? Warehouses.FirstOrDefault();
        Destination = Warehouses.FirstOrDefault(x => x.Id == dest) ?? Warehouses.Skip(1).FirstOrDefault();
        _suppress = false;
        ReloadLots(Lot?.LotId);
        RefreshTransferConsumer();
    }

    [RelayCommand]
    private void New()
    {
        _editingId = null; Selected = null; Actual = ""; DocumentDate = DateTime.Today; Kilometers = ""; OriginPlace = ""; DestinationPlace = ""; Banner = "";
        RefreshTransferConsumer();
    }

    [RelayCommand]
    private void Save()
    {
        if (Source is null || Destination is null || Lot is null) { Fail("Chọn kho nguồn, kho nhận và lô."); return; }
        if (ShowVehicleCalc)
        {
            if (!Numbers.Try(NormText, out var norm) || norm <= 0) { Fail("Định mức phải lớn hơn 0."); return; }
            if (!Numbers.Try(Kilometers, out var distance) || distance < 0) { Fail("Nhập quãng đường."); return; }
            ApplyVehicleQuantity();
        }

        if (!Numbers.Try(Actual, out var actual) || !Numbers.Try(Vcf, out var vcf)) { Fail("Thực xuất hoặc VCF không hợp lệ."); return; }
        actual = QuantityMath.Whole(actual);
        Actual = Numbers.Qty(actual);
        if (actual <= 0) { Fail("Thực xuất phải lớn hơn 0."); return; }
        var source = System.GetWarehouses().First(x => x.Id == Source.Id);
        var dest = System.GetWarehouses().First(x => x.Id == Destination.Id);
        Show(System.SaveTransfer(new TransferRequest
        {
            DocumentId = _editingId,
            DocumentDate = DocumentDate,
            SourceWarehouseId = source.Id,
            SourceWarehouseName = source.Name,
            SourceWarehouseTypeName = source.TypeName,
            DestinationWarehouseId = dest.Id,
            DestinationWarehouseName = dest.Name,
            OriginPlace = OriginPlace,
            DestinationPlace = DestinationPlace,
            LotId = Lot.LotId,
            ActualQuantity = actual,
            Vcf = vcf,
            Fields = Fields.Inputs(),
            AddToSampleSetId = Fields.SampleToUpdate
        }));
        if (!BannerIsError) { _editingId = null; Refresh(); }
    }

    [RelayCommand] private void EditSelected() { if (Selected is not null) EditDocument(Selected.Id); }

    [RelayCommand]
    private void VoidSelected()
    {
        var id = Selected?.Id ?? _editingId;
        if (id is null) return;
        if (!GroupsVm.Confirm("Hủy chứng từ và hoàn tồn?")) return;
        Show(System.Void(id.Value));
        if (!BannerIsError) { _editingId = null; Refresh(); }
    }

    public void EditDocument(Guid id)
    {
        var doc = System.GetDocument(id);
        if (doc is null || doc.Kind != DocumentKind.Transfer) return;
        Refresh();
        _editingId = doc.Id;
        _suppress = true;
        DocumentDate = doc.DocumentDate;
        Source = Warehouses.FirstOrDefault(x => x.Id == doc.WarehouseId);
        Destination = Warehouses.FirstOrDefault(x => x.Id == doc.DestinationWarehouseId);
        ReloadLots(doc.Lines.FirstOrDefault()?.LotId);
        Lot = Lots.FirstOrDefault(x => x.LotId == doc.Lines.FirstOrDefault()?.LotId);
        Actual = Numbers.Qty(doc.ActualQuantity);
        Vcf = doc.Vcf.ToString(CultureInfo.CurrentCulture);
        Kilometers = doc.Distance is decimal distance ? Numbers.Decimal(distance) : "";
        NormText = doc.Norm is decimal norm ? Numbers.Factor(norm) : "";
        OriginPlace = doc.Slip.OriginPlace;
        DestinationPlace = doc.Slip.DestinationPlace;
        _suppress = false;
        Fields.ApplySample(null);
        Fields.LoadSnapshot(doc.Fields);
        RefreshTransferConsumer(applyDefaults: false);
        RefreshDisplay();
        Ok($"Đang sửa {doc.Number}. Tên lô và VCF lấy từ snapshot.");
    }

    private void ReloadLots(Guid? include)
    {
        var keep = include ?? Lot?.LotId;
        Lots.Clear();
        if (Source is null) return;
        foreach (var lot in System.GetLots(Source.Id, keep)) Lots.Add(lot);
        _suppress = true;
        Lot = Lots.FirstOrDefault(x => x.LotId == keep) ?? Lots.FirstOrDefault();
        _suppress = false;
    }

    private void PrefillVcf(LotOption? lot)
    {
        if (lot?.ItemId is Guid itemId && System.GetItem(itemId) is { } item)
            Vcf = item.Vcf.ToString(CultureInfo.CurrentCulture);
        else if (lot is not null)
            Vcf = lot.FirstVcf.ToString(CultureInfo.CurrentCulture);
        ApplyVehicleQuantity();
        RefreshDisplay();
    }

    private void RefreshTransferConsumer(bool applyDefaults = true)
    {
        ConsumerRow? next = null;
        if (Destination is not null)
            next = System.GetConsumers().FirstOrDefault(x => x.Id == Destination.Id);
        if (next is null && Source is not null)
            next = System.GetConsumers().FirstOrDefault(x => x.Id == Source.Id);
        _transferConsumer = next;
        ShowVehicleCalc = next?.Type == ConsumerType.Vehicle;
        if (!applyDefaults || _suppress)
            return;
        if (next?.Type == ConsumerType.Vehicle && next.Norm is decimal norm && string.IsNullOrWhiteSpace(NormText))
            NormText = Numbers.Factor(norm);
        if (next is not null && _editingId is null)
        {
            var wh = System.GetWarehouses().FirstOrDefault(x => x.Id == next.Id);
            if (wh?.DefaultExportSampleSetId is Guid sample)
                Fields.ApplySample(sample);
        }

        ApplyVehicleQuantity();
    }

    private void ApplyVehicleQuantity()
    {
        if (!ShowVehicleCalc)
            return;
        if (!Numbers.Try(Kilometers, out var distance) || distance < 0
            || !Numbers.Try(NormText, out var norm) || norm <= 0
            || !Numbers.Try(Vcf, out var vcf) || vcf <= 0)
            return;
        Actual = Numbers.Qty(QuantityMath.VehicleActual(distance, norm));
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        if (!Numbers.Try(Actual, out var actual) || !Numbers.Try(Vcf, out var vcf) || vcf <= 0)
        {
            Display = "";
            return;
        }

        var whole = QuantityMath.Whole(actual);
        if (whole != actual && !Numbers.EndsWithSeparator(Actual))
        {
            Actual = Numbers.Qty(whole);
            return;
        }

        try { Display = Numbers.Qty(QuantityMath.ExportDisplayQuantity(whole, vcf)); }
        catch (FuelRuleException ex) { Display = ex.Message; }
    }
}

public partial class ConsumptionVm : PageVm, IDocumentEditor
{
    private static readonly (string Header, string SubHeader, string Row, string Ink)[] Palette =
    [
        ("#1A4F7A", "#3D7AAB", "#E8F0F8", "#1B2836"),
        ("#7A4A0C", "#A56B1E", "#FFF6E8", "#5C3A0A"),
        ("#1F5A40", "#3D8A62", "#E8F5EE", "#1B3D2E"),
        ("#5A2A5A", "#7A4A7A", "#F5EAF5", "#3D2140"),
        ("#345A70", "#5A7A94", "#EEF2F5", "#1B2836"),
        ("#7A2E24", "#9A4A40", "#F8EBE8", "#4A1F18"),
    ];

    private readonly List<Guid> _pendingVoids = [];
    private readonly Dictionary<Guid, DocumentKind> _pendingKinds = [];
    private readonly Dictionary<(string Key, long Price, Guid LotTypeId, Guid Warehouse), decimal> _stock = [];
    private List<WarehouseRow> _allWarehouses = [];
    private Dictionary<Guid, ConsumerRow> _ships = [];
    private bool _suppress;
    private bool _datePinned;
    private bool _sheetDirty = true;
    private int _loadGeneration;
    private int _sheetOrder;
    private bool _regroupQueued;
    private bool _applying;
    private bool _bookListDirty = true;
    private int _bookListLoadedYear;
    private int _bookListLoadedQuarter;
    private int _bookListGeneration;
    private int _seenCatalogGeneration = int.MinValue;

    public static readonly string[] ConsumerGroups = ["Máy", "Phương tiện", "Tàu"];

    public ConsumptionVm(FuelSystem system) : base(system)
    {
        DraftYear = DateTime.Today.Year;
        BookListYear = DateTime.Today.Year;
        BookListQuarter = BookListQuarters[(DateTime.Today.Month - 1) / 3];
        SheetRows = new ListCollectionView(Rows);
        // Thứ tự dòng do RebuildConsumptionStructure xếp (nhóm NL → loại lô → dòng), không CustomSort.
    }

    public ObservableCollection<WarehouseRow> Warehouses { get; } = [];
    public ObservableCollection<ItemRow> Items { get; } = [];
    public ObservableCollection<LotTypeRow> LotTypes { get; } = [];
    public ObservableCollection<OpeningLotRowVm> Rows { get; } = [];
    public ListCollectionView SheetRows { get; }
    public ObservableCollection<QuarterRow> Quarters { get; } = [];
    public ObservableCollection<ConsumptionBookListItemVm> BookListItems { get; } = [];
    public string[] BookListQuarters { get; } = ["Quý 1", "Quý 2", "Quý 3", "Quý 4"];
    [ObservableProperty] private OpeningLotRowVm? _selected;
    [ObservableProperty] private QuarterRow? _selectedQuarter;
    [ObservableProperty] private DateTime _documentDate = DateTime.Today;
    [ObservableProperty] private bool _isSheetOpen;
    [ObservableProperty] private int _draftYear;
    [ObservableProperty] private int _bookListYear;
    [ObservableProperty] private string _bookListQuarter = "Quý 1";
    [ObservableProperty] private string _consumerGroup = "Máy";
    public bool ShowList => !IsSheetOpen;
    public bool IsGroupMachine => ConsumerGroup == "Máy";
    public bool IsGroupVehicle => ConsumerGroup == "Phương tiện";
    public bool IsGroupShip => ConsumerGroup == "Tàu";

    /// <summary>Chỉ số ô trong dòng theo kho (ổn định theo toàn bộ kho, không theo nhóm đang lọc).</summary>
    public int CellIndex(Guid warehouseId)
    {
        for (var i = 0; i < _allWarehouses.Count; i++)
        {
            if (_allWarehouses[i].Id == warehouseId)
                return i;
        }

        return -1;
    }

    partial void OnConsumerGroupChanged(string value)
    {
        OnPropertyChanged(nameof(IsGroupMachine));
        OnPropertyChanged(nameof(IsGroupVehicle));
        OnPropertyChanged(nameof(IsGroupShip));
        if (!IsSheetOpen || _applying)
            return;
        ApplyWarehouseFilter();
        SheetReady?.Invoke();
    }

    [RelayCommand]
    private void SelectConsumerGroup(string? group)
    {
        if (string.IsNullOrWhiteSpace(group) || group == ConsumerGroup)
            return;
        if (!ConsumerGroups.Contains(group))
            return;
        ConsumerGroup = group;
    }

    public async Task ShowConsumerTransfers(WarehouseRow warehouse)
    {
        if (!warehouse.IsConsumerLocation)
            return;
        if (warehouse.ConsumerTypeName is not ("Máy" or "Phương tiện"))
            return;

        Ok("Đang mở sổ...");
        ConsumerTransferBook book;
        try
        {
            var date = DocumentDate;
            var id = warehouse.Id;
            book = await Task.Run(() => System.GetConsumerQuarterBook(date, id));
        }
        catch (Exception ex)
        {
            ErrorLog.Record(ex, "Sổ máy/xe");
            Fail("Không mở được sổ tiêu thụ.");
            return;
        }

        var rows = new ObservableCollection<ConsumerTransferBookLineVm>();
        var missionNames = System.GetMissionTasks(activeOnly: false)
            .ToDictionary(x => x.Id, x => x.Display);
        foreach (var row in book.Rows)
        {
            rows.Add(new ConsumerTransferBookLineVm
            {
                IsOpening = row.IsOpening,
                IsManualRow = row.IsManualRow,
                LineId = row.LineId,
                TransferDocumentId = row.DocumentId,
                DocumentNumber = row.DocumentNumber,
                DocumentDate = row.DocumentDate,
                Description = row.Description,
                Origin = row.Origin,
                Destination = row.Destination,
                FuelIn = row.FuelIn,
                OilIn = row.OilIn,
                Kilometers = row.Kilometers,
                MachineHours = row.MachineHours,
                NormQuantity = row.NormQuantity,
                ActualQuantity = row.ActualQuantity,
                FuelOut = row.FuelOut,
                OilOut = row.OilOut,
                FuelBalance = row.FuelBalance,
                OilBalance = row.OilBalance,
                FuelBalanceLotTip = row.FuelBalanceLotTip,
                OilBalanceLotTip = row.OilBalanceLotTip,
                ManualFuelOut = row.ManualFuelOut,
                ManualOilOut = row.ManualOilOut,
                LotTypeId = row.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : row.LotTypeId,
                LotTypeCode = string.IsNullOrWhiteSpace(row.LotTypeCode) ? "TX" : row.LotTypeCode,
                MissionTaskId = row.MissionTaskId,
                MissionTaskName = row.MissionTaskId is Guid mid && missionNames.TryGetValue(mid, out var label)
                    ? label
                    : row.MissionTaskName
            });
        }

        var transferCount = book.Rows.Count(x => !x.IsOpening && !x.IsManualRow);
        var popup = new ConsumerTransferPopupVm
        {
            Title = warehouse.Name,
            Subtitle = $"{QuarterTitle} — sổ tiêu thụ theo phiếu điều chuyển đến {warehouse.ConsumerTypeName.ToLowerInvariant()}.",
            StatusText = transferCount > 0
                ? $"Đã nạp {transferCount} phiếu ĐC. Có thể Thêm dòng tiêu thụ tay (chỉ Xuất). Lưu sổ trừ tồn."
                : "Chưa có phiếu ĐC. Có thể Thêm dòng tiêu thụ tay (chỉ Xuất NL/DM).",
            ConsumerName = book.ConsumerName,
            PlateNumber = book.PlateNumber,
            FuelUsed = book.FuelUsed,
            FormId = book.FormId,
            UnitNote = book.UnitNote,
            ConsumerId = warehouse.Id,
            QuarterDate = DocumentDate,
            Rows = rows,
            LotTypes = LotTypes.ToList(),
            MissionTasks = System.GetMissionTasks().ToList()
        };

        if (ConsumerTransferWindow.Show(popup) != true)
            return;

        var saved = System.SaveConsumerQuarterBook(new ConsumerQuarterBookSaveRequest
        {
            ConsumerId = popup.ConsumerId,
            QuarterDate = popup.QuarterDate,
            Lines = popup.PendingLines
        });
        if (!saved.Ok)
        {
            Fail(saved.Message);
            return;
        }

        Ok(saved.Message);
        InvalidateBookList();
        _sheetDirty = true;
        _ = ReloadSheetAsync(DocumentDate);
    }

    public async Task ShowShipQuarterBook(WarehouseRow warehouse)
    {
        if (!warehouse.IsConsumerLocation || warehouse.ConsumerTypeName != "Tàu")
            return;

        Ok("Đang mở sổ tàu...");
        ShipQuarterBookDto book;
        try
        {
            var date = DocumentDate;
            var id = warehouse.Id;
            book = await Task.Run(() => System.GetShipQuarterBook(date, id));
        }
        catch (Exception ex)
        {
            ErrorLog.Record(ex, "Sổ tàu");
            Fail("Không mở được sổ tàu.");
            return;
        }

        var rows = new ObservableCollection<ShipQuarterBookLineVm>();
        var missionNames = System.GetMissionTasks(activeOnly: false)
            .ToDictionary(x => x.Id, x => x.Display);
        foreach (var row in book.Rows)
        {
            var vm = new ShipQuarterBookLineVm
            {
                IsOpening = row.IsOpening,
                IsTransfer = row.IsTransfer,
                LineId = row.LineId,
                DocumentNumber = row.DocumentNumber,
                DocumentDate = row.DocumentDate,
                Description = row.Description,
                MainOpsCount = row.MainOpsCount ?? book.MainMachineCount,
                HoursAtBerth = row.HoursAtBerth ?? 0,
                HoursCx25 = row.HoursCx25 ?? 0,
                HoursCx50 = row.HoursCx50 ?? 0,
                HoursCx75 = row.HoursCx75 ?? 0,
                HoursCx100 = row.HoursCx100 ?? 0,
                AuxOpsCount = row.AuxOpsCount ?? book.AuxMachineCount,
                AuxHours = row.AuxHours ?? 0,
                ManualFuelOut = row.ManualFuelOut,
                FuelOutManual = row.ManualFuelOut ? (row.FuelOut ?? 0) : 0,
                ManualOilOut = row.ManualOilOut,
                OilOut = row.OilOut ?? 0,
                FuelIn = row.FuelIn,
                OilIn = row.OilIn,
                FuelBalance = row.FuelBalance,
                OilBalance = row.OilBalance,
                FuelBalanceLotTip = row.FuelBalanceLotTip,
                OilBalanceLotTip = row.OilBalanceLotTip,
                RowTotal = row.RowTotal,
                LotTypeId = row.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : row.LotTypeId,
                LotTypeCode = string.IsNullOrWhiteSpace(row.LotTypeCode) ? "TX" : row.LotTypeCode,
                MissionTaskId = row.MissionTaskId,
                MissionTaskName = row.MissionTaskId is Guid mid && missionNames.TryGetValue(mid, out var label)
                    ? label
                    : row.MissionTaskName
            };
            rows.Add(vm);
        }

        var transferCount = book.Rows.Count(x => x.IsTransfer);
        var popup = new ShipQuarterBookPopupVm
        {
            Title = book.ShipName,
            Subtitle = $"{QuarterTitle} — sổ tiêu thụ tàu (mẫu 3-04.3/XD-14).",
            StatusText = transferCount > 0
                ? $"Đã nạp {transferCount} phiếu ĐC (sắp theo ngày). Dầu mỡ = 4% Xuất NL (sửa tay được). Cột Tổng = NL+DM."
                : "Chưa có phiếu ĐC trong quý. Dầu mỡ = 4% Xuất NL (sửa tay được). Cột Tổng = NL+DM.",
            ShipName = book.ShipName,
            ShipType = book.ShipType,
            FuelUsed = book.FuelUsed,
            FuelGroupName = book.FuelGroupName,
            FuelIsGasoline = book.FuelIsGasoline,
            FormId = book.FormId,
            UnitNote = book.UnitNote,
            ConsumerId = book.ConsumerId,
            QuarterDate = DocumentDate,
            DefaultMainMachines = book.MainMachineCount,
            DefaultAuxMachines = book.AuxMachineCount,
            NormRates = book.NormRates,
            Rows = rows,
            LotTypes = LotTypes.ToList(),
            MissionTasks = System.GetMissionTasks().ToList()
        };

        if (ShipQuarterBookWindow.Show(popup) != true)
            return;

        var saved = System.SaveShipQuarterBook(new ShipQuarterBookSaveRequest
        {
            ConsumerId = popup.ConsumerId,
            QuarterDate = popup.QuarterDate,
            FuelGroupName = popup.FuelGroupName,
            Lines = popup.PendingLines
        });
        if (!saved.Ok)
        {
            Fail(saved.Message);
            return;
        }

        Ok(saved.Message);
        InvalidateBookList();
        _sheetDirty = true;
        _ = ReloadSheetAsync(DocumentDate);
    }

    private int ApplyTransferSums(IEnumerable<InboundTransferSum> sums)
    {
        var itemsByLotKey = Items
            .GroupBy(x => QuantityMath.LotKey(x.Name))
            .ToDictionary(g => g.Key, g => g.First());
        var applied = 0;
        foreach (var sum in sums)
        {
            if (sum.ActualQuantity <= 0)
                continue;
            var lot = Rows.FirstOrDefault(x =>
                !x.IsSheetHeader
                && QuantityMath.LotKey(x.LotName) == sum.ItemNameKey
                && Numbers.Try(x.Price, out var price)
                && QuantityMath.TryWholeMoney(price, out var whole)
                && whole == sum.UnitPrice
                && x.LotTypeId == sum.LotTypeId);
            if (lot is null)
            {
                itemsByLotKey.TryGetValue(sum.ItemNameKey, out var item);
                lot = CreateRow();
                lot.Price = Numbers.Money(sum.UnitPrice);
                lot.LotType = LotTypeUi.Pick(LotTypes, sum.LotTypeId);
                lot.LotTypeLocked = true;
                lot.LoadItem(item, item?.Id, item?.Name ?? sum.ItemNameKey, item?.GroupName ?? "", item?.UnitName ?? "",
                    item?.QualityInfo ?? "", item?.MeasurementNote ?? "", item?.Temperature);
                Rows.Add(lot);
            }

            var cell = lot.Cells.FirstOrDefault(x => x.WarehouseId == sum.DestinationWarehouseId);
            if (cell is null)
                continue;
            cell.Quantity = Numbers.Qty(sum.ActualQuantity);
            applied++;
        }

        if (applied > 0)
            RefreshGroupTotals();
        return applied;
    }

    public string QuarterTitle => $"Quý {QuarterOf(DocumentDate)}/{DocumentDate.Year} — ngày {DocumentDate:dd/MM/yyyy}";
    public event Action? SheetReady;

    partial void OnIsSheetOpenChanged(bool value) => OnPropertyChanged(nameof(ShowList));

    partial void OnDocumentDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(QuarterTitle));
        if (_suppress || !_datePinned || !IsSheetOpen)
            return;
        _sheetDirty = true;
        _ = ReloadSheetAsync(DocumentDate);
    }

    public override void Refresh() => _ = SoftRefreshAsync();

    public override Task RefreshAsync(IProgress<DemoProgress>? progress = null) => SoftRefreshAsync();

    /// <summary>
    /// Đổi tab: giữ bảng quý nếu đang mở (chỉ reload khi dirty); danh sách thì soft-cache sổ.
    /// Đổi kho XD/PTKT (CatalogGeneration) luôn nạp lại.
    /// </summary>
    private async Task SoftRefreshAsync()
    {
        var generation = System.CatalogGeneration;
        if (_seenCatalogGeneration != generation)
        {
            _seenCatalogGeneration = generation;
            _sheetDirty = true;
            _bookListDirty = true;
        }

        if (IsSheetOpen)
        {
            if (!_sheetDirty)
                return;
            await ReloadSheetAsync(DocumentDate);
            return;
        }

        await ReloadQuartersAsync();
        await ReloadBookListIfNeededAsync();
    }

    partial void OnBookListYearChanged(int value)
    {
        if (_suppress || IsSheetOpen) return;
        _bookListDirty = true;
        _ = ReloadBookListIfNeededAsync(force: true);
    }

    partial void OnBookListQuarterChanged(string value)
    {
        if (_suppress || IsSheetOpen) return;
        _bookListDirty = true;
        _ = ReloadBookListIfNeededAsync(force: true);
    }

    private Task ReloadBookListIfNeededAsync(bool force = false)
    {
        var q = Array.IndexOf(BookListQuarters, BookListQuarter) + 1;
        if (q < 1) q = 1;
        if (!force
            && !_bookListDirty
            && BookListItems.Count > 0
            && _bookListLoadedYear == BookListYear
            && _bookListLoadedQuarter == q)
            return Task.CompletedTask;
        return ReloadBookListAsync();
    }

    private async Task ReloadBookListAsync()
    {
        if (BookListYear < 2000 || BookListYear > 2100)
        {
            BookListItems.Clear();
            return;
        }

        var q = Array.IndexOf(BookListQuarters, BookListQuarter) + 1;
        if (q < 1) q = 1;
        var year = BookListYear;
        var quarterDate = new DateTime(year, q * 3, DateTime.DaysInMonth(year, q * 3));
        var generation = ++_bookListGeneration;

        try
        {
            var snapshot = await Task.Run(() =>
            {
                var warehouses = System.GetWarehouses()
                    .Where(x => x.IsConsumerLocation && x.ConsumerTypeName is "Máy" or "Phương tiện" or "Tàu")
                    .OrderBy(x => x.ConsumerTypeName)
                    .ThenBy(x => x.Name)
                    .ToList();
                var withData = System.ListLocationsWithQuarterBookActivity(quarterDate);
                return (warehouses, withData);
            });

            if (generation != _bookListGeneration)
                return;

            BookListItems.Clear();
            foreach (var wh in snapshot.warehouses)
            {
                var isShip = wh.ConsumerTypeName == "Tàu";
                var hasData = snapshot.withData.Contains(wh.Id);
                BookListItems.Add(new ConsumptionBookListItemVm
                {
                    WarehouseId = wh.Id,
                    Kind = wh.ConsumerTypeName ?? "",
                    Code = wh.Code,
                    Name = wh.Name,
                    Status = hasData ? "Đã có sổ" : "Trống",
                    IsShip = isShip,
                    HasData = hasData
                });
            }

            _bookListDirty = false;
            _bookListLoadedYear = year;
            _bookListLoadedQuarter = q;
        }
        catch (Exception ex)
        {
            ErrorLog.Record(ex, "Danh sách sổ tiêu thụ");
            Fail("Không mở được danh sách sổ tiêu thụ.");
        }
    }

    private void InvalidateBookList() => _bookListDirty = true;

    [RelayCommand]
    private void SelectAllBooks()
    {
        foreach (var row in BookListItems)
            row.IsSelected = true;
    }

    [RelayCommand]
    private void ClearBookSelection()
    {
        foreach (var row in BookListItems)
            row.IsSelected = false;
    }

    [RelayCommand]
    private async Task OpenBookFromList(ConsumptionBookListItemVm? item)
    {
        if (item is null) return;
        var wh = System.GetWarehouses().FirstOrDefault(x => x.Id == item.WarehouseId);
        if (wh is null) return;
        var q = Array.IndexOf(BookListQuarters, BookListQuarter) + 1;
        if (q < 1) q = 1;
        DocumentDate = new DateTime(BookListYear, q * 3, DateTime.DaysInMonth(BookListYear, q * 3));
        if (item.IsShip)
            await ShowShipQuarterBook(wh);
        else
            await ShowConsumerTransfers(wh);
        InvalidateBookList();
        await ReloadBookListIfNeededAsync(force: true);
    }

    [RelayCommand]
    private async Task ExportSelectedBooks()
    {
        try
        {
            var picks = BookListItems.Where(x => x.IsSelected).ToList();
            if (picks.Count == 0)
            {
                Fail("Tick chọn ít nhất một sổ để xuất Word.");
                return;
            }

            var q = Array.IndexOf(BookListQuarters, BookListQuarter) + 1;
            if (q < 1) q = 1;
            var quarterDate = new DateTime(BookListYear, q * 3, DateTime.DaysInMonth(BookListYear, q * 3));
            var year = BookListYear;

            var mode = picks.Count == 1
                ? Core.Export.MultiExportMode.IndividualFiles
                : Services.ExportUi.AskMultiMode(picks.Count);
            if (mode is null) return;

            var svc = Services.ExportUi.CreateService();
            var artifacts = await Services.ExportUi.RunBusyAsync(
                $"Đang xuất {picks.Count} sổ Word...",
                progress =>
                {
                    var total = picks.Count;
                    var items = new List<Core.Export.ExportService.BookExportItem>(total);
                    for (var i = 0; i < picks.Count; i++)
                    {
                        var pick = picks[i];
                        progress.Report(new Core.Models.DemoProgress(
                            $"Đang đọc sổ {i + 1}/{total}: {pick.Name}", i, total + 1, i, total + 1));
                        if (pick.IsShip)
                        {
                            var book = System.GetShipQuarterBook(quarterDate, pick.WarehouseId);
                            items.Add(new Core.Export.ExportService.BookExportItem(pick.Name, true, null, book, year, q));
                        }
                        else
                        {
                            var book = System.GetConsumerQuarterBook(quarterDate, pick.WarehouseId);
                            items.Add(new Core.Export.ExportService.BookExportItem(pick.Name, false, book, null, year, q));
                        }
                    }

                    progress.Report(new Core.Models.DemoProgress(
                        "Đang tạo file Word...", total, total + 1, total, total + 1));
                    return svc.ExportBooks(items, mode.Value, progress);
                });
            if (artifacts is null || artifacts.Count == 0)
                return;
            Services.ExportUi.SaveArtifacts(artifacts, artifacts[0].FileName);
            Ok($"Đã xuất {picks.Count} sổ Word.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    /// <summary>Nút Quay lại / ép về danh sách 4 quý.</summary>
    private async Task ResetToListAsync()
    {
        IsSheetOpen = false;
        _datePinned = false;
        Banner = "";
        _pendingVoids.Clear();
        _pendingKinds.Clear();
        Rows.Clear();
        Warehouses.Clear();
        _allWarehouses = [];
        Items.Clear();
        Selected = null;
        // Không SheetReady khi đóng sheet — giữ cột/bands đã dựng cho lần mở sau.
        await ReloadQuartersAsync();
    }

    partial void OnDraftYearChanged(int value)
    {
        if (_suppress || IsSheetOpen)
            return;
        BookListYear = value;
        _ = ReloadQuartersAsync();
    }

    [RelayCommand]
    private void OpenQuarter(QuarterRow? row)
    {
        row ??= SelectedQuarter;
        if (row is null)
        {
            Fail("Chọn một quý trong danh sách.");
            return;
        }

        Open(row.EndDate);
    }

    [RelayCommand]
    private void Back() => _ = ResetToListAsync();

    private void Open(DateTime end)
    {
        _suppress = true;
        DocumentDate = end;
        DraftYear = end.Year;
        _datePinned = true;
        _suppress = false;
        IsSheetOpen = true;
        Banner = "";
        _sheetDirty = true;
        _ = ReloadSheetAsync(end);
    }

    [RelayCommand]
    private void AddLot()
    {
        Ok("Bảng ngoài chỉ xem. Bấm tên đối tượng để mở sổ tiêu thụ và thêm dòng.");
    }

    [RelayCommand]
    private void RemoveLot(OpeningLotRowVm? row)
    {
        Ok("Bảng ngoài chỉ xem. Bấm tên đối tượng để mở sổ tiêu thụ và sửa.");
    }

    [RelayCommand]
    private void Save()
    {
        Ok("Bảng ngoài chỉ xem. Bấm tên đối tượng để mở sổ tiêu thụ và Lưu sổ.");
    }

    public void EditDocument(Guid id)
    {
        var doc = System.GetDocument(id);
        if (doc is null)
            return;
        if (doc.Kind == DocumentKind.Consumption && doc.Distance is not null)
            return;
        if (doc.Kind == DocumentKind.Auxiliary)
        {
            var end = QuarterEnd(doc.DocumentDate.Year, QuarterOf(doc.DocumentDate));
            Open(end);
            Fail("Đây là phiếu tiêu thụ kho phụ cũ. Từ nay chỉ nhập tiêu thụ 1 lần/quý tại tab này (ô tương ứng). Lưu sẽ ghi tiêu thụ quý.");
            return;
        }

        if (doc.Kind != DocumentKind.Consumption)
            return;
        var quarterEnd = QuarterEnd(doc.DocumentDate.Year, QuarterOf(doc.DocumentDate));
        _suppress = true;
        DraftYear = quarterEnd.Year;
        DocumentDate = quarterEnd;
        _datePinned = true;
        _suppress = false;
        IsSheetOpen = true;
        Banner = "";
        _ = OpenDocumentAsync(quarterEnd, id);
    }

    private async Task OpenDocumentAsync(DateTime end, Guid documentId)
    {
        try
        {
            var generation = ++_loadGeneration;
            var snapshot = await Task.Run(() => ReadConsumptionSheet(end));
            if (generation != _loadGeneration)
                return;
            ApplyConsumptionSheet(snapshot);
            Selected = Rows.FirstOrDefault(x => x.Cells.Any(cell => cell.DocumentId == documentId));
            if (Selected is null)
                Fail("Không thấy phiếu này trong bảng quý. Phiếu đã hủy hoặc không thuộc kho phụ.");
            else
                Ok($"Đã mở {QuarterTitle}.");
        }
        catch (Exception ex)
        {
            ErrorLog.Record(ex, "Tiêu thụ quý");
            Fail("Không mở được bảng quý.");
        }
    }

    private async Task ReloadQuartersAsync()
    {
        try
        {
            var generation = ++_loadGeneration;
            var snapshot = await Task.Run(ReadQuarters);
            if (generation != _loadGeneration)
                return;
            ApplyQuarters(snapshot);
        }
        catch (Exception ex)
        {
            ErrorLog.Record(ex, "Tiêu thụ quý");
            Fail("Không mở được danh sách quý.");
        }
    }

    private async Task ReloadSheetAsync(DateTime end)
    {
        try
        {
            var generation = ++_loadGeneration;
            var snapshot = await Task.Run(() => ReadConsumptionSheet(end));
            if (generation != _loadGeneration)
                return;
            ApplyConsumptionSheet(snapshot);
        }
        catch (Exception ex)
        {
            ErrorLog.Record(ex, "Tiêu thụ quý");
            Fail("Không mở được bảng quý.");
        }
    }

    private QuarterSnapshot ReadQuarters()
    {
        var year = DraftYear is >= 2000 and <= 2100 ? DraftYear : DateTime.Today.Year;
        var from = new DateTime(year, 1, 1);
        var to = new DateTime(year, 12, 31);
        return new(
            System.GetWarehouses().Where(x => x.Type == WarehouseType.Auxiliary).Select(x => x.Id).ToHashSet(),
            System.ListSheetHeadersInRange(DocumentKind.Consumption, from, to, nullDistanceOnly: true).ToList());
    }

    private ConsumptionSnapshot ReadConsumptionSheet(DateTime end)
    {
        var start = new DateTime(end.Year, end.Month - 2, 1);
        var warehouses = OrderConsumptionWarehouses(System.GetWarehouses().Where(x => x.Type == WarehouseType.Auxiliary));
        var warehouseIds = warehouses.Select(x => x.Id).ToList();
        var ships = System.GetConsumers()
            .Where(x => x.Type == ConsumerType.Ship)
            .ToDictionary(x => x.Id);
        return new ConsumptionSnapshot(
            warehouses,
            System.GetItems(),
            System.ListStock(new StockFilter(null, null, null, null, true, warehouseIds)),
            System.ListSheetHeadersInRange(DocumentKind.Consumption, start, end.Date, nullDistanceOnly: true).ToList(),
            ships);
    }

    private static List<WarehouseRow> OrderConsumptionWarehouses(IEnumerable<WarehouseRow> rows)
    {
        static int Rank(WarehouseRow row)
        {
            if (!row.IsConsumerLocation)
                return 100;
            return row.ConsumerTypeName switch
            {
                "Máy" => 0,
                "Phương tiện" => 1,
                "Tàu" => 2,
                "Đối tượng khác" => 3,
                _ => 50
            };
        }

        return rows.OrderBy(Rank).ThenBy(x => x.Name).ToList();
    }

    private void ApplyQuarters(QuarterSnapshot snapshot)
    {
        var year = DraftYear is >= 2000 and <= 2100 ? DraftYear : DateTime.Today.Year;
        if (year != DraftYear)
        {
            _suppress = true;
            DraftYear = year;
            _suppress = false;
        }

        var groups = new Dictionary<int, (int Count, decimal Total)>();
        foreach (var doc in snapshot.Documents)
        {
            if (doc.WarehouseId is not Guid warehouseId || !snapshot.AuxiliaryIds.Contains(warehouseId))
                continue;
            if (doc.DocumentDate.Year != year)
                continue;
            var quarter = QuarterOf(doc.DocumentDate);
            groups.TryGetValue(quarter, out var current);
            groups[quarter] = (current.Count + 1, current.Total + doc.ActualQuantity);
        }

        Quarters.Clear();
        for (var q = 1; q <= 4; q++)
        {
            groups.TryGetValue(q, out var current);
            Quarters.Add(new QuarterRow
            {
                Year = year,
                Quarter = q,
                EndDate = QuarterEnd(year, q),
                Slips = current.Count,
                Total = Numbers.Qty(current.Total)
            });
        }

        var prefer = year == DateTime.Today.Year ? QuarterOf(DateTime.Today) : 1;
        SelectedQuarter = Quarters.FirstOrDefault(x => x.Quarter == prefer) ?? Quarters.FirstOrDefault();
        Ok($"Năm {year}: luôn có đủ 4 quý. Bấm Mở để vào bảng tiêu thụ (kể cả quý chưa có phiếu).");
    }

    private void ApplyConsumptionSheet(ConsumptionSnapshot snapshot)
    {
        _applying = true;
        try
        {
            ApplyConsumptionSheetCore(snapshot);
        }
        finally
        {
            _applying = false;
        }
    }

    private void ApplyConsumptionSheetCore(ConsumptionSnapshot snapshot)
    {
        _pendingVoids.Clear();
        _pendingKinds.Clear();
        _ships = snapshot.Ships as Dictionary<Guid, ConsumerRow>
            ?? snapshot.Ships.ToDictionary(x => x.Key, x => x.Value);
        _allWarehouses = snapshot.Warehouses.ToList();
        if (!_allWarehouses.Any(MatchesConsumerGroup))
        {
            var first = ConsumerGroups.FirstOrDefault(g =>
                _allWarehouses.Any(w => w.IsConsumerLocation && w.ConsumerTypeName == g));
            if (first is not null)
                ConsumerGroup = first;
        }

        ApplyWarehouseFilter();
        Items.Clear();
        foreach (var row in snapshot.Items)
            Items.Add(row);
        ReloadLotTypes();

        var itemsById = snapshot.Items.ToDictionary(x => x.Id);
        var itemsByName = snapshot.Items
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var itemsByLotKey = snapshot.Items
            .GroupBy(x => QuantityMath.LotKey(x.Name))
            .ToDictionary(g => g.Key, g => g.First());

        _stock.Clear();
        Selected = null;
        var warehouseIds = _allWarehouses.Select(x => x.Id).ToHashSet();
        var grouped = new Dictionary<(string Key, long Price, Guid LotTypeId), OpeningLotRowVm>();
        foreach (var stock in snapshot.Stock)
        {
            if (!warehouseIds.Contains(stock.WarehouseId))
                continue;
            var typeId = stock.LotTypeId;
            var key = (QuantityMath.LotKey(stock.ItemName), stock.UnitPrice, typeId);
            // Cộng tồn theo từng kho (không gộp liên kho).
            var stockKey = (key.Item1, key.Item2, key.Item3, stock.WarehouseId);
            _stock.TryGetValue(stockKey, out var existing);
            _stock[stockKey] = QuantityMath.RoundQty(existing + stock.Quantity);
            if (grouped.ContainsKey(key))
                continue;
            itemsByName.TryGetValue(stock.ItemName, out var item);
            var lot = CreateRow();
            SuppressCells(lot, true);
            lot.Price = Numbers.Money(stock.UnitPrice);
            lot.LotType = LotTypeUi.Pick(LotTypes, typeId);
            lot.LoadItem(item, item?.Id, stock.ItemName, stock.GroupName, stock.UnitName, item?.QualityInfo ?? "", item?.MeasurementNote ?? "", item?.Temperature);
            grouped.Add(key, lot);
        }

        var duplicate = 0;
        foreach (var doc in snapshot.Documents)
        {
            if (doc.WarehouseId is not Guid warehouseId || !warehouseIds.Contains(warehouseId))
                continue;
            var typeId = doc.LotTypeId ?? SeedIds.LotTypeTx;
            var key = (QuantityMath.LotKey(doc.ItemName), doc.UnitPrice, typeId);
            if (!grouped.TryGetValue(key, out var lot))
            {
                ItemRow? item = null;
                if (doc.ItemId is Guid itemId)
                    itemsById.TryGetValue(itemId, out item);
                item ??= itemsByName.GetValueOrDefault(doc.ItemName);
                lot = CreateRow();
                SuppressCells(lot, true);
                lot.Price = Numbers.Money(doc.UnitPrice);
                lot.LotType = LotTypeUi.Pick(LotTypes, typeId);
                lot.LoadItem(item, doc.ItemId, doc.ItemName, doc.GroupName, doc.UnitName, doc.QualityInfo, doc.MeasurementNote, doc.Temperature);
                grouped.Add(key, lot);
            }

            var cell = lot.Cells.FirstOrDefault(x => x.WarehouseId == warehouseId);
            if (cell is null)
                continue;
            if (cell.DocumentId is null)
                AssignCell(cell, doc, key.Item1);
            else
                duplicate++;
        }

        SheetRows.GroupDescriptions.Clear();
        var dataRows = grouped.Values.ToList();
        if (dataRows.Count == 0)
        {
            var blank = CreateRow();
            SuppressCells(blank, false);
            dataRows.Add(blank);
        }
        else
        {
            foreach (var lot in dataRows)
                SuppressCells(lot, false);
        }

        RebuildConsumptionStructure(dataRows, force: true);
        _sheetDirty = false;
        if (_allWarehouses.Count == 0)
            Fail("Chưa có kho phụ trong danh mục.");
        else if (Warehouses.Count == 0)
            Ok($"Nhóm {ConsumerGroup}: chưa có đối tượng. Chọn nhóm khác hoặc thêm trong danh mục.");
        else if (duplicate > 0)
            Ok($"Có {duplicate} phiếu trùng lô và kho trong quý này. Ô đang hiện phiếu gặp trước.");
        else
            Ok("");
        SheetReady?.Invoke();
    }

    private void RefreshGroupTotals() =>
        RebuildConsumptionStructure(Rows.Where(x => !x.IsSheetHeader).ToList());

    /// <summary>Giống NXT tổng / tồn đầu: nhóm NL → loại lô (TX/SSCĐ/IUU) kèm tổng từng cột kho.</summary>
    private void RebuildConsumptionStructure(IReadOnlyList<OpeningLotRowVm> dataRows, bool force = false)
    {
        if (_applying && !force)
            return;

        var selected = Selected is { IsSheetHeader: false } ? Selected : null;
        var ordered = dataRows
            .OrderBy(x => x.SheetGroup.Rank)
            .ThenBy(x => x.SheetGroup.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.LotTypeGroup.Rank)
            .ThenBy(x => x.LotTypeGroup.Code, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.LotName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.SheetOrder)
            .ToList();

        Rows.Clear();
        SheetRows.GroupDescriptions.Clear();
        foreach (var fuelGroup in ordered.GroupBy(x => x.SheetGroup))
        {
            var fuelMembers = fuelGroup.ToList();
            if (fuelGroup.Key.Name.Length == 0 && fuelMembers.All(x => x.IsDraft || string.IsNullOrWhiteSpace(x.LotName)))
            {
                foreach (var row in fuelMembers)
                    Rows.Add(row);
                continue;
            }

            var color = ColorIndexOf(fuelGroup.Key.Rank);
            Rows.Add(CreateConsumptionHeader(
                isGroupHeader: true,
                isLotTypeHeader: false,
                groupName: fuelGroup.Key.Name,
                lotTypeCode: "",
                colorIndex: color,
                members: fuelMembers));

            foreach (var typeGroup in fuelMembers
                         .GroupBy(x => x.LotTypeGroup)
                         .OrderBy(g => g.Key.Rank)
                         .ThenBy(g => g.Key.Code, StringComparer.CurrentCultureIgnoreCase))
            {
                var typeMembers = typeGroup.ToList();
                Rows.Add(CreateConsumptionHeader(
                    isGroupHeader: false,
                    isLotTypeHeader: true,
                    groupName: fuelGroup.Key.Name,
                    lotTypeCode: typeGroup.Key.Code,
                    colorIndex: color,
                    members: typeMembers));
                foreach (var row in typeMembers)
                {
                    ApplyConsumptionTone(row, color, header: false, lotTypeHeader: false);
                    Rows.Add(row);
                }
            }
        }

        Selected = selected is not null && Rows.Contains(selected)
            ? selected
            : Rows.FirstOrDefault(x => !x.IsSheetHeader);
        SheetRows.Refresh();
    }

    private OpeningLotRowVm CreateConsumptionHeader(
        bool isGroupHeader,
        bool isLotTypeHeader,
        string groupName,
        string lotTypeCode,
        int colorIndex,
        IEnumerable<OpeningLotRowVm> members)
    {
        var memberList = members.ToList();
        var row = new OpeningLotRowVm
        {
            IsGroupHeader = isGroupHeader,
            IsLotTypeHeader = isLotTypeHeader,
            GroupName = groupName,
            SheetOrder = isGroupHeader ? int.MinValue : int.MinValue + 1
        };
        if (isLotTypeHeader)
        {
            var code = string.IsNullOrWhiteSpace(lotTypeCode) ? "TX" : lotTypeCode;
            row.LotType = LotTypes.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase))
                          ?? LotTypeUi.Pick(LotTypes, SeedIds.LotTypeTx);
        }

        foreach (var warehouse in _allWarehouses)
        {
            var cell = new OpeningCellVm { WarehouseId = warehouse.Id, SuppressRefresh = true };
            decimal sum = 0;
            foreach (var member in memberList)
            {
                var source = member.Cells.FirstOrDefault(x => x.WarehouseId == warehouse.Id);
                if (source is not null && Numbers.Try(source.Quantity, out var qty))
                    sum = QuantityMath.Whole(sum + qty);
            }

            cell.Quantity = sum == 0 ? "" : Numbers.Qty(sum);
            cell.SuppressRefresh = false;
            row.Cells.Add(cell);
        }

        ApplyConsumptionTone(row, colorIndex, header: isGroupHeader, lotTypeHeader: isLotTypeHeader);
        return row;
    }

    private static void ApplyConsumptionTone(OpeningLotRowVm row, int colorIndex, bool header, bool lotTypeHeader)
    {
        var tone = Palette[ColorIndexOf(colorIndex)];
        if (header)
        {
            row.RowBackground = tone.Header;
            row.RowForeground = "#FFFFFF";
            row.RowFontWeight = FontWeights.SemiBold;
        }
        else if (lotTypeHeader)
        {
            row.RowBackground = tone.SubHeader;
            row.RowForeground = "#FFFFFF";
            row.RowFontWeight = FontWeights.SemiBold;
        }
        else
        {
            row.RowBackground = tone.Row;
            row.RowForeground = tone.Ink;
            row.RowFontWeight = FontWeights.Normal;
        }
    }

    private static int ColorIndexOf(int colorIndex)
    {
        if (colorIndex is >= 0 and <= 3)
            return colorIndex;
        return 4 + (Math.Abs(colorIndex) % (Palette.Length - 4));
    }

    private void OnCellQuantityEdited()
    {
        if (_applying)
            return;
        RefreshGroupTotals();
    }

    private void ApplyWarehouseFilter()
    {
        Warehouses.Clear();
        foreach (var row in _allWarehouses.Where(MatchesConsumerGroup))
            Warehouses.Add(row);
    }

    private bool MatchesConsumerGroup(WarehouseRow row) =>
        row.IsConsumerLocation && row.ConsumerTypeName == ConsumerGroup;

    private static void SuppressCells(OpeningLotRowVm lot, bool suppress)
    {
        foreach (var cell in lot.Cells)
            cell.SuppressRefresh = suppress;
    }

    private sealed record QuarterSnapshot(HashSet<Guid> AuxiliaryIds, IReadOnlyList<SheetHeader> Documents);

    private sealed record ConsumptionSnapshot(
        IReadOnlyList<WarehouseRow> Warehouses,
        IReadOnlyList<ItemRow> Items,
        IReadOnlyList<StockRow> Stock,
        IReadOnlyList<SheetHeader> Documents,
        IReadOnlyDictionary<Guid, ConsumerRow> Ships);

    private void AssignCell(OpeningCellVm cell, SheetHeader doc, string lotKey)
    {
        cell.DocumentId = doc.Id;
        cell.Kind = doc.Kind;
        cell.SavedActual = doc.ActualQuantity;
        cell.SavedLotKey = lotKey;
        cell.SavedPrice = doc.UnitPrice;
        cell.SavedLotTypeId = doc.LotTypeId ?? SeedIds.LotTypeTx;
        cell.Quantity = Numbers.Qty(doc.ActualQuantity);
        cell.SavedVcf = doc.Vcf;
        if (cell.UseShipNorm && doc.OperatingQuantity is decimal operating)
            cell.Operating = Numbers.Qty(operating);
    }

    private void ApplyStock(OpeningLotRowVm row)
    {
        Numbers.Try(row.Price, out var price);
        long whole = 0;
        var named = row.LotName.Length > 0 && QuantityMath.TryWholeMoney(price, out whole);
        var key = named ? QuantityMath.LotKey(row.LotName) : "";
        ApplyShipNorms(row);
        foreach (var cell in row.Cells)
        {
            if (!named)
            {
                cell.SetOnHand(null);
                cell.StockBefore = "Tồn —";
                continue;
            }

            // Tồn trước/sau theo đúng kho của cột (không cộng dồn liên kho).
            _stock.TryGetValue((key, whole, row.LotTypeId, cell.WarehouseId), out var current);
            var before = current;
            if (cell.DocumentId is not null && cell.SavedLotKey == key && cell.SavedPrice == whole && cell.SavedLotTypeId == row.LotTypeId)
                before = QuantityMath.RoundQty(before + cell.SavedActual);
            cell.SetOnHand(before);
            cell.StockBefore = $"Tồn {Numbers.Qty(before)}";
        }
    }

    private void ApplyShipNorms(OpeningLotRowVm row)
    {
        foreach (var cell in row.Cells)
        {
            if (cell.ConsumerId is not Guid shipId || !_ships.TryGetValue(shipId, out var ship))
                continue;
            cell.ConsumerId = ship.Id;
            cell.ConsumerName = ship.Name;
            cell.ConsumerCode = ship.Code;
            // Định mức tàu chưa dùng để tính tiêu thụ quý — nhập thực xuất trực tiếp.
            cell.UseShipNorm = false;
            cell.ShipNorm = null;
        }
    }

    private void ReloadLotTypes()
    {
        LotTypes.Clear();
        foreach (var row in System.GetLotTypes())
            LotTypes.Add(row);
    }

    private OpeningLotRowVm CreateRow()
    {
        var row = new OpeningLotRowVm
        {
            SheetOrder = ++_sheetOrder,
            LotType = LotTypeUi.Pick(LotTypes, SeedIds.LotTypeTx)
        };
        row.IdentityChanged += _ =>
        {
            if (!_applying)
            {
                ApplyStock(row);
                RefreshGroupTotals();
            }
        };
        row.GroupChanged += _ =>
        {
            QueueRegroup();
            if (!_applying)
                RefreshGroupTotals();
        };
        foreach (var warehouse in _allWarehouses)
        {
            var cell = new OpeningCellVm { WarehouseId = warehouse.Id, SuppressRefresh = _applying };
            cell.QuantityEdited += OnCellQuantityEdited;
            if (warehouse.IsConsumerLocation)
            {
                cell.ConsumerId = warehouse.Id;
                cell.ConsumerName = warehouse.Name;
                cell.ConsumerCode = warehouse.Code;
                cell.UseShipNorm = false;
                if (warehouse.ConsumerTypeName == Labels.Consumer(ConsumerType.Ship)
                    && _ships.TryGetValue(warehouse.Id, out var ship))
                {
                    cell.ConsumerName = ship.Name;
                    cell.ConsumerCode = ship.Code;
                }
            }

            row.Cells.Add(cell);
        }

        ApplyShipNorms(row);
        return row;
    }

    private void ApplyGroupOrder() =>
        RebuildConsumptionStructure(Rows.Where(x => !x.IsSheetHeader).ToList());

    private void QueueRegroup()
    {
        if (_applying || _regroupQueued)
            return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            ApplyGroupOrder();
            return;
        }

        _regroupQueued = true;
        dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _regroupQueued = false;
            if (_applying)
                return;
            ApplyGroupOrder();
        });
    }

    private static int QuarterOf(DateTime date) => (date.Month - 1) / 3 + 1;

    private static DateTime QuarterEnd(int year, int quarter)
    {
        var month = quarter * 3;
        return new DateTime(year, month, DateTime.DaysInMonth(year, month));
    }
}

public partial class AuxiliarySheetVm : PageVm, IDocumentEditor
{
    private readonly List<Guid> _pendingVoids = [];
    private bool _datePinned;
    private int _loadGeneration;

    public AuxiliarySheetVm(FuelSystem system) : base(system) { }

    public ObservableCollection<WarehouseRow> Warehouses { get; } = [];
    public ObservableCollection<ItemRow> Items { get; } = [];
    public ObservableCollection<LotTypeRow> LotTypes { get; } = [];
    public ObservableCollection<OpeningLotRowVm> Rows { get; } = [];
    [ObservableProperty] private OpeningLotRowVm? _selected;
    [ObservableProperty] private DateTime _documentDate = DateTime.Today;
    public event Action? SheetReady;

    partial void OnDocumentDateChanged(DateTime value)
    {
        if (!_datePinned)
            return;
        _ = ReloadSheetAsync(DocumentDate);
    }

    public override void Refresh()
    {
        if (!_datePinned)
            _datePinned = true;
        _ = ReloadSheetAsync(DocumentDate);
    }

    public override Task RefreshAsync(IProgress<DemoProgress>? progress = null)
    {
        if (!_datePinned)
            _datePinned = true;
        return ReloadSheetAsync(DocumentDate);
    }

    [RelayCommand]
    private void AddLot()
    {
        var row = CreateRow();
        Rows.Add(row);
        Selected = row;
    }

    [RelayCommand]
    private void RemoveLot(OpeningLotRowVm? row)
    {
        row ??= Selected;
        if (row is null)
            return;
        var ids = row.Cells.Where(x => x.DocumentId is Guid).Select(x => x.DocumentId!.Value).ToList();
        if (ids.Count > 0 && !GroupsVm.Confirm("Xóa dòng này? Các phiếu tiêu thụ kho phụ của dòng sẽ được hoàn khi lưu bảng."))
            return;
        _pendingVoids.AddRange(ids);
        Rows.Remove(row);
        Selected = Rows.LastOrDefault();
    }

    [RelayCommand]
    private void Save()
    {
        if (Warehouses.Count == 0)
        {
            Fail("Chưa có kho phụ.");
            return;
        }

        var cells = new List<AuxiliaryCellRequest>();
        var voids = new List<Guid>(_pendingVoids);
        var seen = new HashSet<(string Key, long Price, Guid LotTypeId)>();
        foreach (var row in Rows)
        {
            var filled = new List<(OpeningCellVm Cell, decimal Quantity, decimal Vcf)>();
            var cleared = new List<Guid>();
            foreach (var cell in row.Cells)
            {
                if (string.IsNullOrWhiteSpace(cell.Quantity))
                {
                    if (cell.DocumentId is Guid id)
                        cleared.Add(id);
                    continue;
                }

                if (!Numbers.Try(cell.Quantity, out var quantity))
                {
                    Fail("Lượng tiêu thụ phải lớn hơn 0. Để trống ô nếu lô không tiêu thụ ở kho đó.");
                    return;
                }

                quantity = QuantityMath.ClampEditQty(QuantityMath.Whole(quantity));
                cell.Quantity = Numbers.Qty(quantity);
                if (quantity <= 0)
                {
                    Fail("Lượng tiêu thụ phải lớn hơn 0. Để trống ô nếu lô không tiêu thụ ở kho đó.");
                    return;
                }

                var vcf = cell.SavedVcf is decimal saved && saved > 0 && row.Item?.Id == row.SnapshotItemId
                    ? saved
                    : row.CatalogVcf;
                if (vcf <= 0)
                {
                    Fail($"Lô {row.LotName} chưa có VCF lớn hơn 0.");
                    return;
                }

                filled.Add((cell, quantity, vcf));
            }

            if (filled.Count == 0 && cleared.Count == 0)
                continue;
            if (row.Item is null)
            {
                Fail("Chọn mặt hàng cho từng dòng có lượng tiêu thụ.");
                return;
            }

            if (!Numbers.Try(row.Price, out var price) || !QuantityMath.TryWholeMoney(price, out var whole) || whole < 0)
            {
                Fail($"Đơn giá của {row.Item.Name} phải là số nguyên không âm.");
                return;
            }

            var name = row.LotName;
            if (name.Length == 0)
            {
                Fail("Dòng lô thiếu tên mặt hàng.");
                return;
            }

            var key = (QuantityMath.LotKey(name), whole, row.LotTypeId);
            if (!seen.Add(key))
            {
                Fail($"Hai dòng cùng mặt hàng, đơn giá và loại lô ({name}, {Numbers.Money(price)}, {row.LotTypeCode}). Gộp vào một dòng.");
                return;
            }

            voids.AddRange(cleared);
            foreach (var (cell, quantity, vcf) in filled)
            {
                var warehouse = Warehouses.First(x => x.Id == cell.WarehouseId);
                cells.Add(new AuxiliaryCellRequest
                {
                    DocumentId = cell.DocumentId,
                    DocumentDate = DocumentDate,
                    WarehouseId = warehouse.Id,
                    WarehouseName = warehouse.Name,
                    WarehouseTypeName = warehouse.TypeName,
                    ItemName = name,
                    UnitPrice = price,
                    LotTypeId = row.LotTypeId,
                    ActualQuantity = quantity,
                    Vcf = vcf
                });
            }
        }

        if (cells.Count == 0 && voids.Count == 0)
        {
            Fail("Nhập lượng tiêu thụ của ít nhất một lô.");
            return;
        }

        if (cells.Any(c => Warehouses.FirstOrDefault(w => w.Id == c.WarehouseId)?.IsConsumerLocation == true)
            && !GroupsVm.Confirm("Có ô ghi trên kho máy/xe/tàu. Tiêu thụ kho phụ sẽ trừ tồn riêng với sổ quý (XT) và có thể trừ kép. Tiếp tục?"))
            return;

        if (voids.Count > 0 && !GroupsVm.Confirm("Lưu bảng sẽ hoàn tồn các ô hoặc dòng đã xóa. Tiếp tục?"))
            return;

        Show(System.SaveAuxiliarySheet(new AuxiliarySheetRequest
        {
            DocumentDate = DocumentDate,
            Cells = cells,
            VoidIds = voids.Distinct().ToList()
        }));
        if (!BannerIsError)
            Refresh();
    }

    public void EditDocument(Guid id)
    {
        var doc = System.GetDocument(id);
        if (doc is null || doc.Kind != DocumentKind.Auxiliary)
            return;
        _datePinned = false;
        DocumentDate = doc.DocumentDate.Date;
        _datePinned = true;
        _loadGeneration++;
        ApplyAuxiliary(ReadAuxiliary(DocumentDate));
        Selected = Rows.FirstOrDefault(x => x.Cells.Any(cell => cell.DocumentId == id));
        if (Selected is null)
            Fail("Không thấy phiếu tiêu thụ kho phụ này trong bảng. Phiếu đã hủy không hiện ở đây.");
        else
            Ok($"Đã chọn dòng lô tương ứng ngày {DocumentDate:dd/MM/yyyy}.");
    }

    private async Task ReloadSheetAsync(DateTime date)
    {
        var generation = ++_loadGeneration;
        var snapshot = await Task.Run(() => ReadAuxiliary(date));
        if (generation != _loadGeneration)
            return;
        ApplyAuxiliary(snapshot);
    }

    private AuxiliarySnapshot ReadAuxiliary(DateTime date) => new(
        System.GetWarehouses().Where(x => x.Type == WarehouseType.Auxiliary).ToList(),
        System.GetItems(),
        System.ListStock(new StockFilter(null, null, null, null, true)),
        System.ListSheetHeaders(DocumentKind.Auxiliary).Where(x => x.DocumentDate.Date == date.Date).ToList());

    private void ApplyAuxiliary(AuxiliarySnapshot snapshot)
    {
        _pendingVoids.Clear();
        Warehouses.Clear();
        foreach (var row in snapshot.Warehouses)
            Warehouses.Add(row);
        Items.Clear();
        foreach (var row in snapshot.Items)
            Items.Add(row);
        ReloadLotTypes();
        Rows.Clear();
        var warehouseIds = snapshot.Warehouses.Select(x => x.Id).ToHashSet();
        var grouped = new Dictionary<(string Key, long Price, Guid LotTypeId), OpeningLotRowVm>();
        foreach (var stock in snapshot.Stock)
        {
            if (!warehouseIds.Contains(stock.WarehouseId))
                continue;
            var typeId = stock.LotTypeId;
            var key = (QuantityMath.LotKey(stock.ItemName), stock.UnitPrice, typeId);
            if (grouped.ContainsKey(key))
                continue;
            var item = Items.FirstOrDefault(x => x.Name == stock.ItemName);
            var lot = CreateRow();
            lot.Price = Numbers.Money(stock.UnitPrice);
            lot.LotType = LotTypeUi.Pick(LotTypes, typeId);
            lot.LoadItem(item, item?.Id, stock.ItemName, stock.GroupName, stock.UnitName, item?.QualityInfo ?? "", item?.MeasurementNote ?? "", item?.Temperature);
            grouped.Add(key, lot);
        }

        var duplicate = 0;
        foreach (var doc in snapshot.Documents)
        {
            var typeId = doc.LotTypeId ?? SeedIds.LotTypeTx;
            var key = (QuantityMath.LotKey(doc.ItemName), doc.UnitPrice, typeId);
            if (!grouped.TryGetValue(key, out var lot))
            {
                var item = Items.FirstOrDefault(x => doc.ItemId is Guid itemId && x.Id == itemId)
                    ?? Items.FirstOrDefault(x => x.Name == doc.ItemName);
                lot = CreateRow();
                lot.Price = Numbers.Money(doc.UnitPrice);
                lot.LotType = LotTypeUi.Pick(LotTypes, typeId);
                lot.LoadItem(item, doc.ItemId, doc.ItemName, doc.GroupName, doc.UnitName, doc.QualityInfo, doc.MeasurementNote, doc.Temperature);
                if (doc.Vcf > 0)
                    lot.CatalogVcf = doc.Vcf;
                grouped.Add(key, lot);
            }

            var cell = lot.Cells.FirstOrDefault(x => x.WarehouseId == doc.WarehouseId);
            if (cell is null)
                continue;
            if (cell.DocumentId is null)
            {
                cell.DocumentId = doc.Id;
                cell.Quantity = Numbers.Qty(doc.ActualQuantity);
                cell.SavedVcf = doc.Vcf;
                cell.SavedLotTypeId = typeId;
            }
            else
                duplicate++;
        }

        foreach (var lot in grouped.Values)
            Rows.Add(lot);
        if (Rows.Count == 0)
            Rows.Add(CreateRow());
        if (duplicate > 0)
            Ok($"Có {duplicate} phiếu tiêu thụ kho phụ trùng lô và kho trong ngày này. Ô đang hiện phiếu mới nhất.");
        else if (Warehouses.Count == 0)
            Fail("Chưa có kho phụ trong danh mục.");
        SheetReady?.Invoke();
    }

    private void ReloadLotTypes()
    {
        LotTypes.Clear();
        foreach (var row in System.GetLotTypes())
            LotTypes.Add(row);
    }

    private sealed record AuxiliarySnapshot(
        IReadOnlyList<WarehouseRow> Warehouses,
        IReadOnlyList<ItemRow> Items,
        IReadOnlyList<StockRow> Stock,
        IReadOnlyList<SheetHeader> Documents);

    private OpeningLotRowVm CreateRow()
    {
        var row = new OpeningLotRowVm { LotType = LotTypeUi.Pick(LotTypes, SeedIds.LotTypeTx) };
        foreach (var warehouse in Warehouses)
            row.Cells.Add(new OpeningCellVm { WarehouseId = warehouse.Id });
        return row;
    }
}

file static class LotTypeUi
{
    public static LotTypeRow? Pick(IEnumerable<LotTypeRow> types, Guid? id)
    {
        var typeId = id is Guid given && given != Guid.Empty ? given : SeedIds.LotTypeTx;
        return types.FirstOrDefault(x => x.Id == typeId)
            ?? types.FirstOrDefault(x => x.Id == SeedIds.LotTypeTx)
            ?? types.FirstOrDefault();
    }
}

public partial class AuxiliaryVm : PageVm, IDocumentEditor
{
    public AuxiliaryVm(FuelSystem system) : base(system) => Fields = new FieldFormVm(system, DocumentFamily.Export);
    public FieldFormVm Fields { get; }
    public ObservableCollection<OptionRow> Warehouses { get; } = [];
    public ObservableCollection<LotOption> Lots { get; } = [];
    public ObservableCollection<DocumentRow> Documents { get; } = [];
    [ObservableProperty] private OptionRow? _warehouse;
    [ObservableProperty] private LotOption? _lot;
    [ObservableProperty] private DocumentRow? _selected;
    [ObservableProperty] private DateTime _documentDate = DateTime.Today;
    [ObservableProperty] private string _actual = "";
    [ObservableProperty] private string _vcf = "";
    [ObservableProperty] private string _display = "";
    private Guid? _editingId;
    private bool _suppress;

    partial void OnWarehouseChanged(OptionRow? value)
    {
        if (_suppress) return;
        ReloadLots(null);
        if (value is null || _editingId is not null) return;
        Fields.ApplySample(System.GetWarehouses().FirstOrDefault(x => x.Id == value.Id)?.DefaultExportSampleSetId);
    }

    partial void OnLotChanged(LotOption? value)
    {
        if (_suppress || value?.ItemId is not Guid itemId) return;
        if (System.GetItem(itemId) is { } item) Vcf = item.Vcf.ToString(CultureInfo.CurrentCulture);
        RefreshDisplay();
    }

    partial void OnActualChanged(string value) => RefreshDisplay();
    partial void OnVcfChanged(string value) => RefreshDisplay();

    public override void Refresh()
    {
        var wh = Warehouse?.Id;
        var sample = Fields.SelectedSample?.Id;
        Warehouses.Clear();
        foreach (var row in System.GetWarehouses().Where(x => x.Type == WarehouseType.Auxiliary))
            Warehouses.Add(new OptionRow(row.Id, row.Name));
        Documents.Clear();
        foreach (var row in System.ListDocuments(DocumentKind.Auxiliary)) Documents.Add(row);
        Fields.ReloadSets(sample);
        _suppress = true;
        Warehouse = Warehouses.FirstOrDefault(x => x.Id == wh) ?? Warehouses.FirstOrDefault();
        _suppress = false;
        ReloadLots(Lot?.LotId);
    }

    [RelayCommand] private void New() { _editingId = null; Selected = null; Actual = ""; DocumentDate = DateTime.Today; Banner = ""; }

    [RelayCommand]
    private void Save()
    {
        if (Warehouse is null || Lot is null) { Fail("Chọn kho phụ và lô."); return; }
        if (!Numbers.Try(Actual, out var actual) || !Numbers.Try(Vcf, out var vcf)) { Fail("Thực xuất hoặc VCF không hợp lệ."); return; }
        actual = QuantityMath.Whole(actual);
        Actual = Numbers.Qty(actual);
        if (actual <= 0) { Fail("Thực xuất phải lớn hơn 0."); return; }
        var wh = System.GetWarehouses().First(x => x.Id == Warehouse.Id);
        if (wh.IsConsumerLocation
            && !GroupsVm.Confirm("Kho này là máy/xe/tàu. Tiêu thụ kho phụ sẽ trừ tồn riêng với sổ quý (XT) và có thể trừ kép. Tiếp tục?"))
            return;
        Show(System.SaveAuxiliary(new AuxiliaryRequest
        {
            DocumentId = _editingId,
            DocumentDate = DocumentDate,
            WarehouseId = wh.Id,
            WarehouseName = wh.Name,
            WarehouseTypeName = wh.TypeName,
            LotId = Lot.LotId,
            ActualQuantity = actual,
            Vcf = vcf,
            Fields = Fields.Inputs(),
            AddToSampleSetId = Fields.SampleToUpdate
        }));
        if (!BannerIsError) { _editingId = null; Refresh(); }
    }

    [RelayCommand] private void EditSelected() { if (Selected is not null) EditDocument(Selected.Id); }

    [RelayCommand]
    private void VoidSelected()
    {
        var id = Selected?.Id ?? _editingId;
        if (id is null) return;
        if (!GroupsVm.Confirm("Hủy chứng từ và hoàn tồn?")) return;
        Show(System.Void(id.Value));
        if (!BannerIsError) { _editingId = null; Refresh(); }
    }

    public void EditDocument(Guid id)
    {
        var doc = System.GetDocument(id);
        if (doc is null || doc.Kind != DocumentKind.Auxiliary) return;
        Refresh();
        _editingId = doc.Id;
        _suppress = true;
        DocumentDate = doc.DocumentDate;
        Warehouse = Warehouses.FirstOrDefault(x => x.Id == doc.WarehouseId);
        ReloadLots(doc.Lines.FirstOrDefault()?.LotId);
        Lot = Lots.FirstOrDefault(x => x.LotId == doc.Lines.FirstOrDefault()?.LotId);
        Actual = Numbers.Qty(doc.ActualQuantity);
        Vcf = doc.Vcf.ToString(CultureInfo.CurrentCulture);
        _suppress = false;
        Fields.ApplySample(null);
        Fields.LoadSnapshot(doc.Fields);
        RefreshDisplay();
        Ok($"Đang sửa {doc.Number}. Snapshot được giữ nguyên khi danh mục đổi.");
    }

    private void ReloadLots(Guid? include)
    {
        var keep = include ?? Lot?.LotId;
        Lots.Clear();
        if (Warehouse is null) return;
        foreach (var lot in System.GetLots(Warehouse.Id, keep)) Lots.Add(lot);
        _suppress = true;
        Lot = Lots.FirstOrDefault(x => x.LotId == keep) ?? Lots.FirstOrDefault();
        _suppress = false;
    }

    private void RefreshDisplay()
    {
        if (!Numbers.Try(Actual, out var actual) || !Numbers.Try(Vcf, out var vcf) || vcf <= 0) { Display = ""; return; }
        var whole = QuantityMath.Whole(actual);
        if (whole != actual && !Numbers.EndsWithSeparator(Actual))
        {
            Actual = Numbers.Qty(whole);
            return;
        }

        try { Display = Numbers.Qty(QuantityMath.ExportDisplayQuantity(whole, vcf)); }
        catch (FuelRuleException ex) { Display = ex.Message; }
    }
}

public partial class StockVm : PageVm
{
    public StockVm(FuelSystem system) : base(system) { }
    public ObservableCollection<OptionRow> Warehouses { get; } = [];
    public ObservableCollection<OptionRow> Groups { get; } = [];
    public ObservableCollection<StockRow> Rows { get; } = [];
    public ObservableCollection<MovementRow> Movements { get; } = [];
    [ObservableProperty] private OptionRow? _warehouse;
    [ObservableProperty] private OptionRow? _group;
    [ObservableProperty] private string _item = "";
    [ObservableProperty] private string _price = "";
    [ObservableProperty] private StockRow? _selected;
    [ObservableProperty] private bool _includeZero;

    partial void OnSelectedChanged(StockRow? value)
    {
        Movements.Clear();
        if (value is null) return;
        foreach (var row in System.ListMovements(value.LotId, value.WarehouseId))
            Movements.Add(row);
    }

    public override void Refresh()
    {
        var wh = Warehouse?.Id;
        var group = Group?.Id;
        Warehouses.Clear();
        Groups.Clear();
        Warehouses.Add(new OptionRow(Guid.Empty, "Tất cả kho"));
        Groups.Add(new OptionRow(Guid.Empty, "Tất cả nhóm"));
        foreach (var row in System.GetWarehouses()) Warehouses.Add(new OptionRow(row.Id, row.Name));
        foreach (var row in System.GetGroups()) Groups.Add(new OptionRow(row.Id, row.Name));
        Warehouse = Warehouses.FirstOrDefault(x => x.Id == wh) ?? Warehouses.FirstOrDefault();
        Group = Groups.FirstOrDefault(x => x.Id == group) ?? Groups.FirstOrDefault();
        Search();
    }

    [RelayCommand]
    private void Search()
    {
        decimal? price = Numbers.Try(Price, out var parsed) ? parsed : null;
        var rows = System.ListStock(new StockFilter(
            Warehouse is null || Warehouse.Id == Guid.Empty ? null : Warehouse.Id,
            Group is null || Group.Id == Guid.Empty ? null : Group.Label,
            string.IsNullOrWhiteSpace(Item) ? null : Item,
            price,
            IncludeZero));
        Rows.Clear();
        foreach (var row in rows) Rows.Add(row);
        Selected = null;
        Movements.Clear();
    }

    [RelayCommand]
    private void Rebuild()
    {
        var result = System.RebuildStock();
        if (result.Warnings.Count == 0) Ok(result.Message);
        else Fail(result.Message + " " + string.Join(" ", result.Warnings));
        Search();
    }
}

public partial class HistoryVm : PageVm
{
    private readonly Action<DocumentKind, Guid> _edit;
    public HistoryVm(FuelSystem system, Action<DocumentKind, Guid> edit) : base(system) => _edit = edit;
    public ObservableCollection<KindFilter> Filters { get; } =
    [
        new(null, "Tất cả"),
        new(DocumentKind.Opening, "Tồn đầu kỳ"),
        new(DocumentKind.Import, "Phiếu nhập"),
        new(DocumentKind.Transfer, "Điều chuyển kho"),
        new(DocumentKind.LotConvert, "Đổi loại lô"),
        new(DocumentKind.Consumption, "Xuất tiêu thụ"),
        new(DocumentKind.Auxiliary, "Tiêu thụ kho phụ"),
        new(DocumentKind.Issue, "Phiếu xuất")
    ];
    public ObservableCollection<DocumentRow> Rows { get; } = [];
    [ObservableProperty] private KindFilter? _filter;
    [ObservableProperty] private DocumentRow? _selected;
    [ObservableProperty] private DocumentDetail? _detail;
    [ObservableProperty] private bool _hasDetail;

    partial void OnFilterChanged(KindFilter? value) => Load();
    partial void OnSelectedChanged(DocumentRow? value)
    {
        Detail = value is null ? null : System.GetDocument(value.Id);
        HasDetail = Detail is not null;
    }

    public override void Refresh()
    {
        Filter ??= Filters[0];
        Load();
    }

    private void Load()
    {
        var keep = Selected?.Id;
        Rows.Clear();
        foreach (var row in System.ListDocuments(Filter?.Kind)) Rows.Add(row);
        Selected = Rows.FirstOrDefault(x => x.Id == keep);
    }

    [RelayCommand]
    private void EditSelected()
    {
        if (Detail is null) return;
        if (Detail.Status != DocumentStatus.Active) { Fail("Chứng từ đã hủy, không sửa."); return; }
        _edit(Detail.Kind, Detail.Id);
    }

    [RelayCommand]
    private void VoidSelected()
    {
        if (Detail is null) return;
        if (!GroupsVm.Confirm("Hủy chứng từ và hoàn tồn?")) return;
        Show(System.Void(Detail.Id));
        if (!BannerIsError) Refresh();
    }
}
