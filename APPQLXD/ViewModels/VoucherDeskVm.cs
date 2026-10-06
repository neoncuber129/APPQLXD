using System.Collections.ObjectModel;
using System.Globalization;
using APPQLXD.Core;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Services;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Core.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace APPQLXD.ViewModels;

public partial class VoucherLineVm : ObservableObject
{
    private bool _suppress;
    private bool _fromActual;

    public Guid? SnapshotItemId { get; set; }
    public string SnapshotName { get; set; } = "";
    public string FuelDisplayName => !string.IsNullOrWhiteSpace(Item?.Name) ? Item.Name : SnapshotName;
    public bool FuelTouched { get; set; }
    public Guid? AutoItemId { get; set; }
    public bool PriceFromAmount { get; set; }
    public bool QuantityFromVehicle { get; set; }
    public event Action? Changed;
    public event Action<VoucherLineVm>? LotChosen;

    [ObservableProperty] private LotOption? _lot;
    [ObservableProperty] private LotTypeRow? _lotType;
    [ObservableProperty] private LotTypeRow? _destinationLotType;
    [ObservableProperty] private ItemRow? _item;
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _quality = "1";
    [ObservableProperty] private string _observed = "";
    [ObservableProperty] private string _temperature = "";
    [ObservableProperty] private string _density = "";
    [ObservableProperty] private string _vcf = "";
    [ObservableProperty] private string _actual = "";
    [ObservableProperty] private string _price = "";
    [ObservableProperty] private string _amount = "";
    [ObservableProperty] private string _splitNote = "";
    [ObservableProperty] private string _splitWarning = "";
    [ObservableProperty] private string _splitMethod = "";
    [ObservableProperty] private bool _needsSplit;
    [ObservableProperty] private bool _splitChosen;
    [ObservableProperty] private bool _splitLocked = true;
    [ObservableProperty] private bool _floorAvailable;
    [ObservableProperty] private bool _ceilAvailable;
    [ObservableProperty] private bool _floorChosen;
    [ObservableProperty] private bool _ceilChosen;
    [ObservableProperty] private bool _manualChosen;
    [ObservableProperty] private string _floorCaption = "";
    [ObservableProperty] private string _ceilCaption = "";
    [ObservableProperty] private string _remainingText = "";
    [ObservableProperty] private bool _remainingShort;
    [ObservableProperty] private int _index = 1;

    public ObservableCollection<SplitPartVm> SplitParts { get; } = [];

    partial void OnLotChanged(LotOption? value)
    {
        if (_suppress)
            return;
        if (value is not null)
        {
            _suppress = true;
            Price = Numbers.Money(value.UnitPrice);
            _suppress = false;
        }

        FuelTouched = true;
        LotChosen?.Invoke(this);
    }

    public void SelectLot(LotOption? lot)
    {
        _suppress = true;
        Lot = lot;
        _suppress = false;
    }

    public void SelectItem(ItemRow? item)
    {
        _suppress = true;
        Item = item;
        if (item is not null && string.IsNullOrWhiteSpace(Code))
            Code = item.Code;
        _suppress = false;
        NotifyFuelDisplay();
    }

    public void NotifyFuelDisplay() => OnPropertyChanged(nameof(FuelDisplayName));

    public void FillFromLot(ItemRow? item, decimal firstVcf)
    {
        _suppress = true;
        if (item is not null)
        {
            Item = item;
            Code = item.Code;
            if (!string.IsNullOrWhiteSpace(item.QualityInfo))
                Quality = item.QualityInfo;
            if (item.Temperature is decimal temperature)
                Temperature = Numbers.Decimal(temperature);
            if (item.Density is decimal density)
                Density = Numbers.Factor(density);
        }

        var vcf = firstVcf > 0 ? firstVcf : item?.Vcf ?? 0;
        if (vcf > 0)
            Vcf = Numbers.Factor(vcf);
        _suppress = false;
        if (!QuantityFromVehicle)
            LinkQuantities(_fromActual);
        else
            Changed?.Invoke();
    }

    public void ShowWhole(decimal observed, decimal actual)
    {
        _suppress = true;
        Observed = Numbers.Qty(observed);
        Actual = Numbers.Qty(actual);
        _suppress = false;
    }

    public void SetVehicleQuantity(decimal actual, decimal vcf)
    {
        actual = QuantityMath.Whole(actual);
        _suppress = true;
        _fromActual = true;
        Actual = Numbers.Qty(actual);
        if (vcf > 0)
            Observed = Numbers.Qty(QuantityMath.ExportDisplayQuantity(actual, vcf));
        if (Numbers.Try(Price, out var price))
            Amount = Numbers.Money(decimal.Round(actual * price, 0, MidpointRounding.AwayFromZero));
        _suppress = false;
    }

    partial void OnItemChanged(ItemRow? value)
    {
        OnPropertyChanged(nameof(FuelDisplayName));
        if (_suppress || value is null)
            return;
        FuelTouched = true;
        if (value.Id != SnapshotItemId)
        {
            SnapshotItemId = null;
            SnapshotName = "";
            OnPropertyChanged(nameof(FuelDisplayName));
        }

        Code = value.Code;
        if (!string.IsNullOrWhiteSpace(value.QualityInfo))
            Quality = value.QualityInfo;
        if (value.Temperature is decimal temperature)
            Temperature = Numbers.Decimal(temperature);
        if (value.Density is decimal density)
            Density = Numbers.Factor(density);
        Vcf = Numbers.Factor(value.Vcf);
        if (!QuantityFromVehicle)
            LinkQuantities(_fromActual);
        else
            Changed?.Invoke();
    }

    partial void OnObservedChanged(string value)
    {
        if (_suppress)
            return;
        _fromActual = false;
        LinkQuantities(fromActual: false);
    }

    partial void OnVcfChanged(string value)
    {
        if (_suppress)
            return;
        LinkQuantities(_fromActual);
    }

    partial void OnPriceChanged(string value)
    {
        if (!_suppress && !PriceFromAmount)
        {
            _suppress = true;
            if (Numbers.Try(Actual, out var actual) && Numbers.Try(Price, out var price))
                Amount = Numbers.Money(decimal.Round(actual * price, 0, MidpointRounding.AwayFromZero));
            _suppress = false;
            Changed?.Invoke();
        }
    }

    partial void OnAmountChanged(string value)
    {
        if (!_suppress && PriceFromAmount)
            DerivePrice();
    }

    partial void OnSplitMethodChanged(string value)
    {
        if (!_suppress && PriceFromAmount)
            DerivePrice(keepChoice: true);
    }

    [RelayCommand]
    private void ChooseSplit(string? method)
    {
        if (string.IsNullOrWhiteSpace(method) || method == SplitMethod)
            return;
        SplitMethod = method;
    }

    partial void OnActualChanged(string value)
    {
        if (_suppress)
            return;
        _fromActual = true;
        LinkQuantities(fromActual: true);
    }

    public void Apply(LineRow row, IEnumerable<ItemRow> items)
    {
        _suppress = true;
        SnapshotItemId = row.ItemId;
        SnapshotName = row.ItemName?.Trim() ?? "";
        Item = items.FirstOrDefault(x => row.ItemId is Guid id && id != Guid.Empty && x.Id == id)
            ?? items.FirstOrDefault(x => string.Equals(x.Name, row.ItemName, StringComparison.CurrentCultureIgnoreCase));
        if (Item is null && !string.IsNullOrWhiteSpace(row.ItemName))
        {
            // Mặt hàng có thể ngoài phạm vi danh mục hiện tại — vẫn giữ tên trên phiếu.
            SnapshotName = row.ItemName.Trim();
        }

        Code = string.IsNullOrWhiteSpace(row.ItemCode) ? Item?.Code ?? "" : row.ItemCode;
        Quality = string.IsNullOrWhiteSpace(row.QualityGrade) ? "1" : row.QualityGrade;
        Observed = Numbers.Qty(row.Quantity);
        Temperature = row.Temperature is decimal temperature ? Numbers.Decimal(temperature) : "";
        Density = row.Density is decimal density ? Numbers.Factor(density) : "";
        var vcf = row.Vcf > 0 ? row.Vcf : Item?.Vcf ?? 0;
        Vcf = Numbers.Factor(vcf);
        Actual = Numbers.Qty(row.ActualQuantity);
        Price = Numbers.Money(row.UnitPrice);
        Amount = Numbers.Money(row.Amount);
        // Giữ Actual đã ghi sổ làm nguồn sự thật — đổi VCF chỉ suy ra Xuất quan sát, không làm lệch tồn.
        _fromActual = true;
        _suppress = false;
        OnPropertyChanged(nameof(FuelDisplayName));
        if (PriceFromAmount)
            DerivePrice();
    }

    private void LinkQuantities(bool fromActual)
    {
        if (_suppress)
            return;

        if (!Numbers.Try(Vcf, out var vcf) || vcf <= 0)
        {
            if (!PriceFromAmount && Numbers.Try(Actual, out var actualVal) && Numbers.Try(Price, out var priceVal))
            {
                _suppress = true;
                Amount = Numbers.Money(decimal.Round(actualVal * priceVal, 0, MidpointRounding.AwayFromZero));
                _suppress = false;
            }
            Changed?.Invoke();
            return;
        }

        _suppress = true;
        try
        {
            if (fromActual)
            {
                if (Numbers.Try(Actual, out var actual))
                {
                    var obs = QuantityMath.ExportDisplayQuantity(actual, vcf);
                    Observed = Numbers.Qty(obs);
                }
            }
            else
            {
                if (Numbers.Try(Observed, out var qty))
                {
                    var actual = QuantityMath.ActualImport(qty, vcf);
                    Actual = Numbers.Qty(actual);
                }
            }

            if (!PriceFromAmount && Numbers.Try(Actual, out var priced) && Numbers.Try(Price, out var price))
                Amount = Numbers.Money(decimal.Round(priced * price, 0, MidpointRounding.AwayFromZero));
        }
        finally
        {
            _suppress = false;
        }

        if (PriceFromAmount)
            DerivePrice();
        else
            Changed?.Invoke();
    }

    private void DerivePrice(bool keepChoice = false)
    {
        if (_suppress)
            return;
        if (!keepChoice && SplitMethod.Length > 0)
        {
            _suppress = true;
            SplitMethod = "";
            _suppress = false;
        }

        if (!Numbers.Try(Amount, out var amount) || !TryActual(out var actual))
        {
            ClearSplitOffer();
            Changed?.Invoke();
            return;
        }

        var floor = PriceByActual(amount, actual, ceil: false);
        var ceil = PriceByActual(amount, actual, ceil: true);
        if (floor.Ok && !floor.WasSplit)
        {
            _suppress = true;
            Price = Numbers.Money(floor.Lines[0].UnitPrice);
            _suppress = false;
            ClearSplitOffer();
            Changed?.Invoke();
            return;
        }

        NeedsSplit = true;
        FloorAvailable = floor.Ok && floor.WasSplit;
        CeilAvailable = ceil.Ok && ceil.WasSplit;
        FloorCaption = DescribeSplit(floor);
        CeilCaption = DescribeSplit(ceil);
        MarkChoice();

        if (SplitMethod.Length == 0)
        {
            SplitLocked = true;
            SplitParts.Clear();
            _suppress = true;
            Price = "";
            _suppress = false;
            SplitNote = "Chọn cặp tách";
            SplitWarning = "Chưa chọn cặp tách.";
            Changed?.Invoke();
            return;
        }

        if (SplitMethod == "Tự nhập")
        {
            SplitLocked = false;
            if (SplitParts.Count != 2 || SplitParts.Any(x => !x.CanEdit))
            {
                SplitParts.Clear();
                var first = new SplitPartVm(this);
                var second = new SplitPartVm(this);
                first.Load(1, "", "", "", true);
                second.Load(2, "", "", "", true);
                SplitParts.Add(first);
                SplitParts.Add(second);
            }

            RefreshManual();
            return;
        }

        SplitLocked = true;
        var preview = SplitMethod == "Làm tròn lên" ? ceil : floor;
        _suppress = true;
        if (!preview.Ok || !preview.WasSplit)
        {
            Price = "";
            SplitNote = preview.Message;
            SplitWarning = preview.Message;
            SplitParts.Clear();
        }
        else
        {
            Price = string.Join(" / ", preview.Lines.Select(x => Numbers.Money(x.UnitPrice)));
            SplitNote = SplitMethod;
            SplitWarning = SplitMethod == "Làm tròn lên"
                ? "Đã chọn làm tròn lên."
                : "Đã chọn phần nguyên.";
            FillParts(preview, canEdit: false);
        }

        _suppress = false;
        Changed?.Invoke();
    }

    private void ClearSplitOffer()
    {
        NeedsSplit = false;
        SplitChosen = false;
        FloorChosen = false;
        CeilChosen = false;
        ManualChosen = false;
        FloorAvailable = false;
        CeilAvailable = false;
        FloorCaption = "";
        CeilCaption = "";
        SplitNote = "";
        SplitWarning = "";
        SplitParts.Clear();
    }

    private void MarkChoice()
    {
        FloorChosen = SplitMethod == "Phần nguyên";
        CeilChosen = SplitMethod == "Làm tròn lên";
        ManualChosen = SplitMethod == "Tự nhập";
        SplitChosen = FloorChosen || CeilChosen || ManualChosen;
    }

    private static string DescribeSplit(ImportPreview preview)
    {
        if (!preview.Ok || preview.Lines.Count == 0)
            return preview.Message;
        return string.Join(Environment.NewLine, preview.Lines.Select(line =>
            $"{Numbers.Qty(line.Actual)} × {Numbers.Money(line.UnitPrice)} = {Numbers.Money(line.Amount)}"));
    }

    private void FillParts(ImportPreview preview, bool canEdit)
    {
        SplitParts.Clear();
        foreach (var line in preview.Lines)
        {
            var part = new SplitPartVm(this);
            part.Load(
                SplitParts.Count + 1,
                Numbers.Money(line.UnitPrice),
                Numbers.Qty(line.Actual),
                Numbers.Money(line.Amount),
                canEdit);
            SplitParts.Add(part);
        }
    }

    public void RefreshManual()
    {
        if (!Numbers.Try(Amount, out var targetAmount) || !TryActual(out var targetActual))
        {
            SplitWarning = "Nhập thực nhập và thành tiền của dòng hàng.";
            Changed?.Invoke();
            return;
        }

        decimal sumActual = 0;
        decimal sumAmount = 0;
        var complete = true;
        foreach (var part in SplitParts)
        {
            if (!Numbers.Try(part.Price, out var price) || !QuantityMath.IsWholeMoney(price) || price < 0
                || !Numbers.Try(part.Actual, out var partActual) || partActual <= 0)
            {
                complete = false;
                continue;
            }

            var qty = QuantityMath.RoundQty(partActual);
            var raw = price * qty;
            if (!QuantityMath.IsWholeMoney(raw))
            {
                complete = false;
                part.SetAmount("");
                continue;
            }

            part.SetAmount(Numbers.Money(raw));
            sumActual += qty;
            sumAmount += raw;
        }

        _suppress = true;
        Price = string.Join(" / ", SplitParts.Select(x => x.Price));
        _suppress = false;
        if (!complete)
            SplitWarning = "Mỗi lô cần đơn giá nguyên và thực nhập lớn hơn 0. Đơn giá × thực nhập phải ra số nguyên.";
        else if (QuantityMath.RoundQty(sumActual) != QuantityMath.RoundQty(targetActual))
            SplitWarning = $"Tổng thực nhập {Numbers.Qty(sumActual)} phải bằng {Numbers.Qty(targetActual)}.";
        else if (sumAmount != targetAmount)
            SplitWarning = $"Tổng thành tiền {Numbers.Money(sumAmount)} phải bằng {Numbers.Money(targetAmount)}.";
        else
            SplitWarning = "Hai lô khớp tổng thực nhập và tổng thành tiền.";
        SplitNote = "Tự nhập hai lô";
        Changed?.Invoke();
    }

    public bool TryReadSplit(decimal money, decimal actual, out ImportPreview preview, out string error)
    {
        var lines = new List<SplitLine>();
        decimal sumActual = 0;
        decimal sumAmount = 0;
        foreach (var part in SplitParts)
        {
            if (!Numbers.Try(part.Price, out var price) || !QuantityMath.TryWholeMoney(price, out var whole) || whole < 0)
            {
                error = "Đơn giá mỗi lô phải là số nguyên không âm.";
                preview = ImportPreview.Fail(error);
                return false;
            }

            if (!Numbers.Try(part.Actual, out var partActual) || partActual <= 0)
            {
                error = "Thực nhập mỗi lô phải lớn hơn 0.";
                preview = ImportPreview.Fail(error);
                return false;
            }

            var qty = QuantityMath.RoundQty(partActual);
            var raw = whole * qty;
            if (!QuantityMath.IsWholeMoney(raw))
            {
                error = "Đơn giá × thực nhập của mỗi lô phải ra số nguyên.";
                preview = ImportPreview.Fail(error);
                return false;
            }

            lines.Add(new SplitLine(lines.Count + 1, whole, qty, raw, qty));
            sumActual += qty;
            sumAmount += raw;
        }

        if (lines.Count < 2)
        {
            error = "Tách lô cần đủ hai lô.";
            preview = ImportPreview.Fail(error);
            return false;
        }

        if (QuantityMath.RoundQty(sumActual) != QuantityMath.RoundQty(actual))
        {
            error = "Tổng thực nhập hai lô phải bằng thực nhập của dòng.";
            preview = ImportPreview.Fail(error);
            return false;
        }

        if (sumAmount != money)
        {
            error = "Tổng thành tiền hai lô phải bằng thành tiền của dòng.";
            preview = ImportPreview.Fail(error);
            return false;
        }

        error = "";
        preview = ImportPreview.Success(lines);
        return true;
    }

    private bool TryActual(out decimal actual)
    {
        if (Numbers.Try(Actual, out actual) && actual > 0)
        {
            actual = QuantityMath.Whole(actual);
            return actual > 0;
        }
        if (Numbers.Try(Observed, out var qty) && Numbers.Try(Vcf, out var vcf) && qty > 0 && vcf > 0)
        {
            actual = QuantityMath.ActualImport(qty, vcf);
            return actual > 0;
        }

        actual = 0;
        return false;
    }

    public static ImportPreview PriceByActual(decimal amount, decimal actual, bool ceil = false)
    {
        if (actual <= 0)
            return ImportPreview.Fail("Thực nhập phải lớn hơn 0.");
        var basePrice = ceil ? decimal.Ceiling(amount / actual) : decimal.Floor(amount / actual);
        return ImportLotSplitter.Split(basePrice, actual, amount, 1m);
    }

    public void LoadBalanced(VoucherLineVm source, decimal observed, decimal actual, long price, decimal amount)
    {
        _suppress = true;
        _fromActual = true;
        SnapshotItemId = source.SnapshotItemId;
        SnapshotName = source.SnapshotName;
        PriceFromAmount = source.PriceFromAmount;
        Item = source.Item;
        LotType = source.LotType;
        DestinationLotType = source.DestinationLotType;
        Code = source.Code;
        Quality = source.Quality;
        Temperature = source.Temperature;
        Density = source.Density;
        Vcf = source.Vcf;
        Observed = Numbers.Qty(QuantityMath.Whole(observed));
        Actual = Numbers.Qty(QuantityMath.Whole(actual));
        Price = Numbers.Money(price);
        Amount = Numbers.Money(amount);
        NeedsSplit = false;
        SplitChosen = false;
        SplitMethod = "";
        SplitNote = "";
        SplitWarning = "";
        SplitParts.Clear();
        _suppress = false;
    }
}

public partial class SplitPartVm : ObservableObject
{
    private readonly VoucherLineVm _owner;
    private bool _loading;

    public SplitPartVm(VoucherLineVm owner) => _owner = owner;

    [ObservableProperty] private int _no;
    [ObservableProperty] private string _price = "";
    [ObservableProperty] private string _actual = "";
    [ObservableProperty] private string _amount = "";
    [ObservableProperty] private bool _canEdit;

    public bool IsLocked => !CanEdit;

    partial void OnCanEditChanged(bool value) => OnPropertyChanged(nameof(IsLocked));

    public void Load(int no, string price, string actual, string amount, bool canEdit)
    {
        _loading = true;
        No = no;
        Price = price;
        Actual = actual;
        Amount = amount;
        CanEdit = canEdit;
        _loading = false;
    }

    public void SetAmount(string value)
    {
        _loading = true;
        Amount = value;
        _loading = false;
    }

    partial void OnPriceChanged(string value)
    {
        if (!_loading && CanEdit)
            _owner.RefreshManual();
    }

    partial void OnActualChanged(string value)
    {
        if (!_loading && CanEdit)
            _owner.RefreshManual();
    }
}

public partial class VoucherDeskVm : PageVm, IDocumentEditor
{
    private Guid? _editingId;
    private bool _listStale = true;
    private string? _listCacheKey;
    private readonly Dictionary<Guid, decimal> _postedActualByLot = [];
    private bool _loading;

    public VoucherDeskVm(FuelSystem system, bool exportDesk) : base(system)
    {
        ExportDesk = exportDesk;
        IsExport = exportDesk;
        ImportFields = new FieldFormVm(system, DocumentFamily.Import);
        ExportFields = new FieldFormVm(system, DocumentFamily.Export);
    }

    public bool ExportDesk { get; }

    public ObservableCollection<OptionRow> Warehouses { get; } = [];
    public ObservableCollection<OptionRow> WarehouseChoices { get; } = [];
    public ObservableCollection<OptionRow> Destinations { get; } = [];
    public ObservableCollection<LotOption> Lots { get; } = [];
    public ObservableCollection<LotTypeRow> LotTypes { get; } = [];
    public IEnumerable<LotTypeRow> ConvertLotTypeChoices =>
        LotTypes.Where(x => IsTxOrSscdLotType(x.Id));
    public IEnumerable<LotOption> ConvertSourceLots =>
        Lots.Where(x => IsTxOrSscdLotType(x.LotTypeId));
    public ObservableCollection<ConsumerRow> Targets { get; } = [];
    private bool _keepEntry;
    public ObservableCollection<string> NatureOptions { get; } = [];
    public ObservableCollection<string> SenderOptions { get; } = [];
    public ObservableCollection<string> ReceiverUnitOptions { get; } = [];
    public ObservableCollection<string> ReceiverPersonOptions { get; } = [];
    public ObservableCollection<string> PlateOptions { get; } = [];
    public ObservableCollection<string> MissionOptions { get; } = [];
    public ObservableCollection<MissionTaskRow> MissionTasks { get; } = [];
    public ObservableCollection<string> OriginPlaceOptions { get; } = [];
    public ObservableCollection<string> DestinationPlaceOptions { get; } = [];
    public ObservableCollection<string> DelivererOptions { get; } = [];
    public ObservableCollection<string> OrganizationOptions { get; } = [];
    public ObservableCollection<string> UnitOptions { get; } = [];
    public ObservableCollection<string> ContractOptions { get; } = [];
    public ObservableCollection<string> CarrierOptions { get; } = [];
    public ObservableCollection<string> IntroOptions { get; } = [];
    public ObservableCollection<string> PriceUntilOptions { get; } = [];
    public ObservableCollection<string> KilometersOptions { get; } = [];
    public ObservableCollection<string> CalibrationOptions { get; } = [];
    public ObservableCollection<string> ReceivedVolumeOptions { get; } = [];
    public ObservableCollection<string> PackageOptions { get; } = [];
    public ObservableCollection<string> NoteOptions { get; } = [];
    public ObservableCollection<string> CodeOptions { get; } = [];
    public ObservableCollection<string> QualityOptions { get; } = [];
    public ObservableCollection<string> SignDelivererOptions { get; } = [];
    public ObservableCollection<string> SignReceiverOptions { get; } = [];
    public ObservableCollection<string> SignFinanceOptions { get; } = [];
    public ObservableCollection<string> SignWriterOptions { get; } = [];
    public ObservableCollection<string> SignChiefOptions { get; } = [];
    public ObservableCollection<string> SignCommanderOptions { get; } = [];
    public ObservableCollection<ItemRow> Items { get; } = [];
    public ObservableCollection<VoucherLineVm> Lines { get; } = [];
    [ObservableProperty] private ObservableCollection<VoucherListItemVm> _importDocuments = [];
    [ObservableProperty] private ObservableCollection<VoucherListItemVm> _exportDocuments = [];
    private bool _formOptionsReady;
    private int _catalogGeneration = -1;
    public FieldFormVm ImportFields { get; }
    public FieldFormVm ExportFields { get; }
    public FieldFormVm Fields => IsExport ? ExportFields : ImportFields;

    [ObservableProperty] private bool _isFormOpen;
    [ObservableProperty] private bool _isExport;
    [ObservableProperty] private ExportSlipMode _exportMode = ExportSlipMode.Transfer;
    [ObservableProperty] private OptionRow? _warehouse;
    [ObservableProperty] private OptionRow? _destination;
    [ObservableProperty] private LotTypeRow? _convertTargetLotType;
    [ObservableProperty] private LotOption? _convertSourceLot;
    [ObservableProperty] private string _convertQuantity = "";
    [ObservableProperty] private ConsumerRow? _target;
    [ObservableProperty] private string _normText = "";
    [ObservableProperty] private VoucherListItemVm? _selectedImport;
    [ObservableProperty] private VoucherListItemVm? _selectedExport;
    [ObservableProperty] private VoucherLineVm? _selectedLine;
    [ObservableProperty] private DateTime _documentDate = DateTime.Today;
    [ObservableProperty] private bool _manualQuantity;
    [ObservableProperty] private string _formNumber = "";
    [ObservableProperty] private string _organizationName = "";
    [ObservableProperty] private string _unitTitle = "";
    [ObservableProperty] private string _senderUnit = "";
    [ObservableProperty] private string _receiverUnit = "";
    [ObservableProperty] private string _nature = "";
    [ObservableProperty] private string _contractOrOrder = "";
    [ObservableProperty] private string _carrierUnit = "";
    [ObservableProperty] private string _priceValidUntil = "";
    [ObservableProperty] private string _delivererName = "";
    [ObservableProperty] private string _introDocument = "";
    [ObservableProperty] private string _vehiclePlate = "";
    [ObservableProperty] private string _calibrationVolume = "";
    [ObservableProperty] private string _receivedVolume = "";
    [ObservableProperty] private string _packageCount = "";
    [ObservableProperty] private string _receiverPerson = "";
    [ObservableProperty] private string _kilometers = "";
    [ObservableProperty] private string _mission = "";
    [ObservableProperty] private MissionTaskRow? _missionTask;
    [ObservableProperty] private string _originPlace = "";
    [ObservableProperty] private string _destinationPlace = "";
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string _signerReceiver = "";
    [ObservableProperty] private string _signerDeliverer = "";
    [ObservableProperty] private string _signerFinance = "";
    [ObservableProperty] private string _signerWriter = "";
    [ObservableProperty] private string _signerChief = "";
    [ObservableProperty] private string _signerCommander = "";
    [ObservableProperty] private string _lineCountText = "";
    [ObservableProperty] private string _quantityText = "";
    [ObservableProperty] private string _amountWords = "";
    [ObservableProperty] private string _totalObservedText = "";
    [ObservableProperty] private string _totalActualText = "";
    [ObservableProperty] private string _totalAmountText = "";
    [ObservableProperty] private bool _hasSplit;
    [ObservableProperty] private bool _extraFieldsOpen;
    /// <summary>Sau khi lưu thành công: giữ form mở và chuẩn bị phiếu mới.</summary>
    [ObservableProperty] private bool _continueAfterSave;
    [ObservableProperty] private bool _invalidWarehouse;
    [ObservableProperty] private bool _invalidDestination;
    [ObservableProperty] private bool _invalidTarget;
    [ObservableProperty] private bool _invalidFormNumber;
    [ObservableProperty] private bool _invalidReceiverPerson;
    [ObservableProperty] private bool _invalidConvertSourceLot;
    [ObservableProperty] private bool _invalidConvertTargetLotType;
    [ObservableProperty] private bool _invalidConvertQuantity;
    public string ExtraHeader => ExportDesk ? "Trường đã tạo" : "Trường bổ sung";

    public string FormTitle => IsExport ? "PHIẾU XUẤT XĂNG DẦU" : "PHIẾU NHẬP XĂNG DẦU";
    public string ListTitle => ExportDesk ? "Phiếu xuất" : "Phiếu nhập";
    public string CreateText => ExportDesk ? "Tạo phiếu xuất" : "Tạo phiếu nhập";
    public string[] ListQuarters { get; } = ["Tất cả", "Quý 1", "Quý 2", "Quý 3", "Quý 4"];
    [ObservableProperty] private string _listYear = DateTime.Today.Year.ToString();
    [ObservableProperty] private string _listQuarter = "Quý " + ((DateTime.Today.Month - 1) / 3 + 1);

    partial void OnListYearChanged(string value)
    {
        if (_loading) return;
        InvalidateList();
        ReloadList();
    }

    partial void OnListQuarterChanged(string value)
    {
        if (_loading) return;
        InvalidateList();
        ReloadList();
    }

    public string ListHint => ExportDesk
        ? "Điều chuyển sang máy/xe/tàu chỉ chuyển tồn, không phải tiêu thụ. Tiêu thụ các kho ngoài kho chính nhập 1 lần/quý tại tab Tiêu thụ quý. Tích nhiều phiếu để xóa cùng lúc."
        : "Tạo, xem, sửa hoặc xóa phiếu. Xóa phiếu hoàn tồn rồi xóa hẳn. Tích nhiều phiếu để xóa cùng lúc.";
    public bool ShowList => !IsFormOpen;
    public bool IsIssueMode => ExportMode == ExportSlipMode.Issue;
    public bool IsMachineMode => ExportMode == ExportSlipMode.Issue;
    public bool IsRetailMode => ExportMode == ExportSlipMode.Retail;
    public bool IsTransferMode => ExportMode == ExportSlipMode.Transfer;
    public bool IsLotConvertMode => ExportMode == ExportSlipMode.LotConvert;
    public bool IsVehicleMode => ExportMode == ExportSlipMode.Vehicle;
    public bool IsConsumerMode => IsVehicleMode || IsMachineMode;
    public bool ShowVehicleCalc => IsVehicleMode || (IsTransferMode && TransferConsumer?.Type == ConsumerType.Vehicle);
    public bool ShowManualQuantityToggle => ShowVehicleCalc;
    public bool IsShipDestination =>
        (IsTransferMode && (TransferConsumer?.Type == ConsumerType.Ship || (Destination is not null && System.GetConsumers().FirstOrDefault(x => x.Id == Destination.Id)?.Type == ConsumerType.Ship)))
        || (IsConsumerMode && Target?.Type == ConsumerType.Ship)
        || (!IsConsumerMode && !IsTransferMode && Destination is not null && System.GetConsumers().FirstOrDefault(x => x.Id == Destination.Id)?.Type == ConsumerType.Ship);
    public bool IsMissionTaskVisible => !IsShipDestination;
    /// <summary>Phiếu điều chuyển: khóa nhập liệu đến khi đã chọn kho nhận. Đổi loại lô luôn mở.</summary>
    public bool TransferDetailsEnabled => IsLotConvertMode || !IsTransferMode || Destination is not null;
    public ConsumerRow? TransferConsumer { get; private set; }
    public string WarehouseLabel => ExportDesk ? "Kho nguồn" : "Kho ghi sổ";
    public string ModeHint => ExportMode switch
    {
        ExportSlipMode.Transfer => Destination is null
            ? "Chọn kho nhận trước. Sau đó mới nhập các dữ liệu khác trên phiếu điều chuyển."
            : TransferConsumer?.Type == ConsumerType.Vehicle
            ? ManualQuantity
                ? "Điều chuyển sang phương tiện: nhập thực xuất thủ công. Trừ kho nguồn, cộng kho nhận. Không tính là tiêu thụ."
                : "Điều chuyển sang phương tiện: thực xuất = quãng đường × định mức (làm tròn lên). Bật «Nhập tay» cạnh số km nếu cần nhập thực xuất thủ công."
            : TransferConsumer is not null
                ? "Điều chuyển sang đối tượng: nhập thực xuất (hoặc xuất theo VCF). Trừ kho nguồn, cộng kho nhận. Không tính là tiêu thụ."
                : "Điều chuyển giảm tồn kho nguồn và tăng tồn kho nhận bằng thực xuất. Chọn đối tượng ở kho nhận để dùng định mức/nhiên liệu mặc định. Không tính là tiêu thụ.",
        ExportSlipMode.LotConvert =>
            "Chọn kho, lô nguồn, loại đích (TX ↔ SSCĐ) và số lượng. Chỉ đổi nhãn loại lô trong cùng kho.",
        ExportSlipMode.Vehicle => ManualQuantity
            ? "Nhập thực xuất thủ công trên dòng đầu. Các dòng sau: nhập xuất hoặc thực xuất thì ô còn lại và thành tiền tự tính theo VCF."
            : "Dòng đầu lấy thực xuất bằng quãng đường × định mức, làm tròn lên. Bật «Nhập tay» cạnh số km nếu cần nhập thực xuất thủ công.",
        ExportSlipMode.Retail => "Tự điền toàn bộ phiếu. Nhập xuất hoặc thực xuất thì ô còn lại được tính theo VCF.",
        _ => "Chọn máy. Nhập xuất hoặc thực xuất thì ô còn lại và thành tiền tự tính theo VCF. Thực xuất trừ tồn kho nguồn."
    };
    public bool ImportPriceLocked => !ExportDesk;
    public string QtyHeader => IsExport ? "Xuất" : "Nhập";
    public string ActualHeader => IsExport ? "Thực xuất" : "Thực nhập";
    public string ContractLabel => IsExport ? "Theo lệnh (KH)" : "Theo hợp đồng số";
    public string SenderLabel => IsExport ? "Đơn vị giao" : "Đơn vị giao hàng";
    public string ReceiverLabel => IsExport ? "Đơn vị nhận" : "Đơn vị nhận hàng";

    partial void OnIsFormOpenChanged(bool value) => OnPropertyChanged(nameof(ShowList));

    partial void OnExportModeChanged(ExportSlipMode value)
    {
        OnPropertyChanged(nameof(IsIssueMode));
        OnPropertyChanged(nameof(IsMachineMode));
        OnPropertyChanged(nameof(IsRetailMode));
        OnPropertyChanged(nameof(IsTransferMode));
        OnPropertyChanged(nameof(IsLotConvertMode));
        OnPropertyChanged(nameof(IsVehicleMode));
        OnPropertyChanged(nameof(IsConsumerMode));
        OnPropertyChanged(nameof(ShowVehicleCalc));
        OnPropertyChanged(nameof(ShowManualQuantityToggle));
        OnPropertyChanged(nameof(TransferDetailsEnabled));
        OnPropertyChanged(nameof(IsShipDestination));
        OnPropertyChanged(nameof(IsMissionTaskVisible));
        OnPropertyChanged(nameof(ConvertLotTypeChoices));
        OnPropertyChanged(nameof(ConvertSourceLots));
        OnPropertyChanged(nameof(WarehouseLabel));
        OnPropertyChanged(nameof(ModeHint));
        if (IsShipDestination)
            MissionTask = null;
        if (_loading || _keepEntry)
            return;
        ResetSlipEntry();
        ApplyWarehouseChoices();
        RefreshTransferConsumer();
        if (value == ExportSlipMode.LotConvert)
        {
            if (string.IsNullOrWhiteSpace(Nature))
                Nature = "Đổi loại lô";
            ConvertSourceLot = null;
            ConvertQuantity = "";
            EnsureConvertTargetDefault();
            OnPropertyChanged(nameof(ConvertSourceLots));
        }
    }

    partial void OnConvertSourceLotChanged(LotOption? value)
    {
        InvalidConvertSourceLot = false;
        if (_loading || !IsLotConvertMode)
            return;
        EnsureOneConvertLine();
        var line = Lines[0];
        line.SelectLot(value);
        if (value is not null)
        {
            var item = Items.FirstOrDefault(x => x.Id == value.ItemId);
            line.Price = Numbers.Money(value.UnitPrice);
            line.FillFromLot(item, value.FirstVcf);
            line.LotType = FindLotType(value.LotTypeId);
            if (ConvertTargetLotType is null || ConvertTargetLotType.Id == value.LotTypeId)
                EnsureConvertTargetDefault(preferOppositeOf: value.LotTypeId);
            line.DestinationLotType = ConvertTargetLotType;
            if (string.IsNullOrWhiteSpace(line.Vcf) || line.Vcf == "0")
                line.Vcf = "1";
        }
        else
        {
            line.LotType = null;
            line.DestinationLotType = ConvertTargetLotType;
        }
    }

    partial void OnConvertQuantityChanged(string value)
    {
        InvalidConvertQuantity = false;
        if (_loading || !IsLotConvertMode)
            return;
        EnsureOneConvertLine();
        Lines[0].Actual = value;
        Lines[0].Observed = value;
        if (string.IsNullOrWhiteSpace(Lines[0].Vcf) || Lines[0].Vcf == "0")
            Lines[0].Vcf = "1";
    }

    partial void OnConvertTargetLotTypeChanged(LotTypeRow? value)
    {
        InvalidConvertTargetLotType = false;
        if (_loading || !IsLotConvertMode || Lines.Count == 0)
            return;
        Lines[0].DestinationLotType = value;
    }

    partial void OnFormNumberChanged(string value)
    {
        InvalidFormNumber = false;
        foreach (var row in Fields.Rows.Where(x => IsInvoiceFieldName(x.Name)))
            row.HasError = false;
    }

    partial void OnReceiverPersonChanged(string value)
    {
        InvalidReceiverPerson = false;
        foreach (var row in Fields.Rows.Where(x => IsReceiverFieldName(x.Name)))
            row.HasError = false;
    }

    private void EnsureOneConvertLine()
    {
        while (Lines.Count > 1)
        {
            var extra = Lines[^1];
            extra.Changed -= OnLineChanged;
            extra.LotChosen -= ApplyChosenLot;
            Lines.RemoveAt(Lines.Count - 1);
        }

        if (Lines.Count == 0)
            AddLine();
    }

    partial void OnManualQuantityChanged(bool value)
    {
        OnPropertyChanged(nameof(ModeHint));
        if (_loading)
            return;
        BindConsumerLines();
        if (!value && ShowVehicleCalc)
            ApplyVehicleQuantity();
        else
            RecalcTotals();
    }

    partial void OnTargetChanged(ConsumerRow? value)
    {
        InvalidTarget = false;
        OnPropertyChanged(nameof(IsShipDestination));
        OnPropertyChanged(nameof(IsMissionTaskVisible));
        if (IsShipDestination)
            MissionTask = null;
        if (_loading || value is null)
            return;
        var vehicle = value.Type == ConsumerType.Vehicle;
        var next = vehicle ? ExportSlipMode.Vehicle : ExportSlipMode.Issue;
        if (ExportMode != next)
        {
            _keepEntry = true;
            ExportMode = next;
            _keepEntry = false;
        }

        if (_editingId is null)
            ResetEntryLines();
        else
            BindConsumerLines();
        ApplyObjectSample();
        NormText = "";
        if (vehicle && value.Norm is decimal norm)
            NormText = Numbers.Factor(norm);
        if (string.IsNullOrWhiteSpace(VehiclePlate))
            VehiclePlate = value.Name;
        ApplyDefaultFuel(replace: true);
        if (vehicle && !ManualQuantity)
            ApplyVehicleQuantity();
    }

    partial void OnNormTextChanged(string value)
    {
        if (!_loading && !ManualQuantity)
            ApplyVehicleQuantity();
    }

    partial void OnKilometersChanged(string value)
    {
        if (!_loading && ShowVehicleCalc && !ManualQuantity)
            ApplyVehicleQuantity();
    }

    partial void OnDestinationChanged(OptionRow? value)
    {
        InvalidDestination = false;
        OnPropertyChanged(nameof(TransferDetailsEnabled));
        OnPropertyChanged(nameof(IsShipDestination));
        OnPropertyChanged(nameof(IsMissionTaskVisible));
        OnPropertyChanged(nameof(ModeHint));
        if (IsShipDestination)
            MissionTask = null;
        if (_loading || !IsTransferMode)
            return;
        RefreshTransferConsumer(applyDefaults: _editingId is null);
    }

    private void ClearInputErrors()
    {
        InvalidWarehouse = false;
        InvalidDestination = false;
        InvalidTarget = false;
        InvalidFormNumber = false;
        InvalidReceiverPerson = false;
        InvalidConvertSourceLot = false;
        InvalidConvertTargetLotType = false;
        InvalidConvertQuantity = false;
        Fields.ClearErrors();
    }

    private static bool IsInvoiceFieldName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && (name.Contains("hóa đơn", StringComparison.OrdinalIgnoreCase)
            || name.Contains("hoa don", StringComparison.OrdinalIgnoreCase));

    private static bool IsReceiverFieldName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Contains("Người nhận", StringComparison.OrdinalIgnoreCase);

    /// <summary>Đánh dấu trường bắt buộc còn trống (giấy + panel bổ sung). Trả true nếu còn thiếu.</summary>
    private bool MarkAndReportMissingRequired()
    {
        var family = ExportDesk ? DocumentFamily.Export : DocumentFamily.Import;
        var inputs = PaperFieldInputs();
        var missing = new List<string>();
        foreach (var def in System.GetFields(family).Where(x => x.IsVisible && x.IsRequired))
        {
            var given = inputs.FirstOrDefault(x => x.FieldId == def.Id)?.Value?.Trim() ?? "";
            if (given.Length > 0)
                continue;
            // Phiếu nhập: số phiếu trống → hệ thống tự cấp khi lưu.
            if (!ExportDesk && IsInvoiceFieldName(def.Name) && string.IsNullOrWhiteSpace(FormNumber))
                continue;
            missing.Add(def.Name);
            Fields.MarkErrorByName(def.Name);
            if (IsInvoiceFieldName(def.Name))
                InvalidFormNumber = true;
            if (IsReceiverFieldName(def.Name))
                InvalidReceiverPerson = true;
        }

        if (missing.Count == 0)
            return false;

        if (Fields.Rows.Any(x => x.HasError) || Fields.PanelVisible)
            ExtraFieldsOpen = true;
        Fail(missing.Count == 1
            ? $"Trường bắt buộc: {missing[0]}."
            : $"Thiếu trường bắt buộc: {string.Join(", ", missing)}.");
        return true;
    }

    partial void OnWarehouseChanged(OptionRow? value)
    {
        InvalidWarehouse = false;
        if (_loading)
            return;
        FillWarehouseSide();
        ReloadLots();
        if (IsLotConvertMode)
            ConvertSourceLot = null;
        if (IsTransferMode)
            RefreshTransferConsumer(applyDefaults: _editingId is null);
        else
            RefreshExportFields();
        ApplyDefaultFuel();
    }

    partial void OnIsExportChanged(bool value)
    {
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(ContractLabel));
        OnPropertyChanged(nameof(SenderLabel));
        OnPropertyChanged(nameof(ReceiverLabel));
        OnPropertyChanged(nameof(Fields));
        OnPropertyChanged(nameof(QtyHeader));
        OnPropertyChanged(nameof(ActualHeader));
        OnPropertyChanged(nameof(ExtraHeader));
    }

    private DocumentRow? ActiveRow => (ExportDesk ? SelectedExport : SelectedImport)?.Row;

    private ObservableCollection<VoucherListItemVm> ActiveDocuments =>
        ExportDesk ? ExportDocuments : ImportDocuments;

    private IReadOnlyList<DocumentRow> ChosenRows()
    {
        var checkedRows = ActiveDocuments.Where(x => x.IsSelected).Select(x => x.Row).ToList();
        if (checkedRows.Count > 0)
            return checkedRows;
        return ActiveRow is null ? [] : [ActiveRow];
    }

    public override void Refresh()
    {
        // Đổi kho XD/PTKT: luôn đồng bộ danh mục form (kể cả khi form đang đóng).
        EnsureDesk();
        ReloadListIfNeeded();
    }

    public override async Task RefreshAsync(IProgress<DemoProgress>? progress = null)
    {
        EnsureDesk();
        await ReloadListIfNeededAsync(progress);
    }

    public void PrepareEdit()
    {
        EnsureDesk();
        IsFormOpen = true;
    }

    private void LoadCatalogs()
    {
        var warehouseId = Warehouse?.Id;
        var destinationId = Destination?.Id;
        _loading = true;
        Warehouses.Clear();
        Destinations.Clear();
        foreach (var row in System.GetWarehouses())
        {
            var label = $"{row.Name} ({row.ConsumerTypeName ?? row.TypeName})";
            Warehouses.Add(new OptionRow(row.Id, row.Name));
            Destinations.Add(new OptionRow(row.Id, label));
        }

        Destination = Destinations.FirstOrDefault(x => x.Id == destinationId);
        Items.Clear();
        foreach (var row in System.GetItems())
            Items.Add(row);
        LotTypes.Clear();
        foreach (var row in System.GetLotTypes())
            LotTypes.Add(row);
        Targets.Clear();
        foreach (var row in System.GetConsumers().OrderBy(x => x.Type).ThenBy(x => x.Name))
        {
            if (row.Type is ConsumerType.Machine or ConsumerType.Vehicle)
                Targets.Add(row);
        }
        var keepTask = MissionTask?.Id;
        MissionTasks.Clear();
        foreach (var row in System.GetMissionTasks())
            MissionTasks.Add(row);
        MissionTask = MissionTasks.FirstOrDefault(x => x.Id == keepTask);
        var extraFields = System.GetExtraFieldsOnSlip();
        ImportFields.SetExtraFieldsEnabled(extraFields);
        ExportFields.SetExtraFieldsEnabled(extraFields);
        ImportFields.ReloadSets();
        ExportFields.ReloadSets();
        if (ImportFields.Rows.Count == 0)
            ImportFields.ApplySample(null);
        if (ExportFields.Rows.Count == 0)
            ExportFields.ApplySample(null);
        _loading = false;
        ApplyWarehouseChoices(warehouseId);
    }

    private void EnsureDesk()
    {
        var generation = System.CatalogGeneration;
        if (_formOptionsReady && _catalogGeneration == generation)
            return;
        LoadCatalogs();
        FillSampleOptions();
        _formOptionsReady = true;
        _catalogGeneration = generation;
    }

    private void FillSampleOptions()
    {
        var family = ExportDesk ? DocumentFamily.Export : DocumentFamily.Import;
        var fields = System.GetFields(family);
        var options = System.GetFieldOptionMap(family);
        void Fill(ObservableCollection<string> target, string name)
        {
            target.Clear();
            var field = fields.FirstOrDefault(x => x.Name == name);
            if (field is null || !options.TryGetValue(field.Id, out var values))
                return;
            foreach (var value in values)
                target.Add(value);
        }

        Fill(OrganizationOptions, "Cơ quan");
        Fill(UnitOptions, "Đơn vị");
        Fill(NatureOptions, ExportDesk ? "Tính chất xuất" : "Tính chất nhập");
        Fill(SenderOptions, ExportDesk ? "Đơn vị giao" : "Đơn vị giao hàng");
        Fill(ReceiverUnitOptions, ExportDesk ? "Đơn vị nhận" : "Đơn vị nhận hàng");
        Fill(ContractOptions, ExportDesk ? "Theo lệnh (KH)" : "Theo hợp đồng số");
        Fill(CarrierOptions, "Đơn vị vận chuyển");
        Fill(ReceiverPersonOptions, "Người nhận");
        Fill(IntroOptions, "Giấy giới thiệu và CMT");
        Fill(PriceUntilOptions, "Có giá đến ngày");
        Fill(PlateOptions, "Số xe");
        Fill(KilometersOptions, "Số km");
        Fill(CalibrationOptions, "Dung tích kiểm định");
        Fill(ReceivedVolumeOptions, "Dung tích nhận hàng");
        Fill(PackageOptions, "Số lượng bao bì");
        Fill(MissionOptions, "Nhiệm vụ");
        Fill(OriginPlaceOptions, "Nơi đi");
        Fill(DestinationPlaceOptions, "Nơi đến");
        Fill(NoteOptions, "Ghi chú");
        Fill(CodeOptions, "Mã số");
        Fill(QualityOptions, "Chất lượng");
        Fill(SignDelivererOptions, "Chữ ký người giao");
        Fill(SignReceiverOptions, "Chữ ký người nhận");
        Fill(SignFinanceOptions, "Chữ ký tài chính");
        Fill(SignWriterOptions, "Chữ ký người viết phiếu");
        Fill(SignChiefOptions, "Chữ ký trưởng ban HC-KT");
        Fill(SignCommanderOptions, "Chữ ký chỉ huy đơn vị");

        var delivererFields = fields;
        var delivererOptions = options;
        if (family != DocumentFamily.Import)
        {
            delivererFields = System.GetFields(DocumentFamily.Import);
            delivererOptions = System.GetFieldOptionMap(DocumentFamily.Import);
        }

        var deliverer = delivererFields.FirstOrDefault(x => x.Name == "Người giao hàng")
            ?? delivererFields.FirstOrDefault(x => x.Name == "Người giao");
        DelivererOptions.Clear();
        if (deliverer is not null && delivererOptions.TryGetValue(deliverer.Id, out var delivererValues))
        {
            foreach (var value in delivererValues)
                DelivererOptions.Add(value);
        }
    }

    private List<FieldInput> PaperFieldInputs()
    {
        var inputs = Fields.Inputs().ToList();
        var family = ExportDesk ? DocumentFamily.Export : DocumentFamily.Import;
        AppendFieldMatch(inputs, family, IsInvoiceFieldName, FormNumber);
        AppendFieldMatch(inputs, family, IsReceiverFieldName, ReceiverPerson);
        return inputs;
    }

    private void AppendField(List<FieldInput> inputs, DocumentFamily family, string name, string value) =>
        AppendFieldMatch(inputs, family, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase), value);

    private void AppendFieldMatch(List<FieldInput> inputs, DocumentFamily family, Func<string, bool> nameMatch, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        var field = System.GetFields(family).FirstOrDefault(x => x.IsVisible && nameMatch(x.Name));
        if (field is null || inputs.Any(x => x.FieldId == field.Id && !string.IsNullOrWhiteSpace(x.Value)))
            return;
        inputs.Add(new FieldInput { FieldId = field.Id, Value = value.Trim() });
    }

    public void EditDocument(Guid id)
    {
        var doc = System.GetDocument(id);
        if (doc is null)
            return;
        if (ExportDesk && doc.Kind == DocumentKind.Consumption && doc.Distance is null)
        {
            Fail("Phiếu tiêu thụ quý mở ở tab Tiêu thụ quý.");
            return;
        }

        if (ExportDesk && doc.Kind == DocumentKind.Auxiliary)
        {
            Fail("Tiêu thụ kho phụ mở ở tab Tiêu thụ quý.");
            return;
        }

        var allowed = ExportDesk
            ? doc.Kind is DocumentKind.Issue or DocumentKind.Transfer or DocumentKind.LotConvert or DocumentKind.Consumption
            : doc.Kind == DocumentKind.Import;
        if (!allowed)
        {
            Fail(ExportDesk ? "Phiếu này không mở trên tab phiếu xuất." : "Chỉ sửa phiếu nhập trên tab này.");
            return;
        }

        Load(doc);
    }

    [RelayCommand]
    private void Create() => NewSlip();

    [RelayCommand]
    private void ChooseExportMode(string? mode)
    {
        var next = mode switch
        {
            "Transfer" => ExportSlipMode.Transfer,
            "LotConvert" => ExportSlipMode.LotConvert,
            "Retail" => ExportSlipMode.Retail,
            "Consumer" => ExportMode is ExportSlipMode.Issue or ExportSlipMode.Vehicle
                ? ExportMode
                : ExportSlipMode.Vehicle,
            _ => ExportSlipMode.Transfer
        };
        if (_editingId is not null && next != ExportMode)
        {
            Fail("Không đổi loại phiếu đã lưu. Hãy tạo phiếu mới.");
            return;
        }

        ExportMode = next;
    }

    [RelayCommand]
    private void NewImport() => NewSlip();

    [RelayCommand]
    private void NewExport() => NewSlip();

    [RelayCommand]
    private void AddLine(VoucherLineVm? after = null)
    {
        var line = MakeLine();
        line.LotType = DefaultLotType();
        line.Changed += OnLineChanged;
        line.LotChosen += ApplyChosenLot;
        var index = after is null ? Lines.Count : Lines.IndexOf(after) + 1;
        if (index < 0)
            index = Lines.Count;
        Lines.Insert(index, line);
        SelectedLine = line;
        BindConsumerLines();
        Renumber();
        RecalcTotals();
    }

    public void ApplyFloorPair(VoucherLineVm source, FloorSplitPair pair)
    {
        var index = Lines.IndexOf(source);
        if (index < 0)
            return;
        if (!Numbers.Try(source.Actual, out var actual) || !Numbers.Try(source.Amount, out var amount))
        {
            Fail("Cần thực nhập và thành tiền của dòng trước khi tách lô.");
            return;
        }

        actual = QuantityMath.Whole(actual);
        if (!QuantityMath.IsWholeMoney(amount) || !pair.Matches(actual, amount))
        {
            Fail("Cặp tách phải khớp đúng thực nhập và thành tiền đã nhập. Không tính ngược tổng từ đơn giá.");
            return;
        }

        Numbers.Try(source.Observed, out var observed);
        decimal observed1;
        decimal observed2;
        if (observed > 0 && actual > 0)
        {
            observed1 = QuantityMath.Whole(observed * pair.Quantity1 / actual);
            observed2 = QuantityMath.Whole(observed - observed1);
            if (observed2 < 0)
                observed2 = 0;
        }
        else
        {
            observed1 = pair.Quantity1;
            observed2 = pair.Quantity2;
        }

        // Thành tiền lấy từ cặp đã kiểm Matches (đơn giá × thực nhập từng lô), không làm tròn lại tổng.
        var first = MakeLine();
        var second = MakeLine();
        first.LoadBalanced(source, observed1, pair.Quantity1, pair.Price1, pair.Amount1);
        second.LoadBalanced(source, observed2, pair.Quantity2, pair.Price2, pair.Amount2);
        source.Changed -= OnLineChanged;
        source.LotChosen -= ApplyChosenLot;
        Lines.RemoveAt(index);
        Lines.Insert(index, first);
        Lines.Insert(index + 1, second);
        first.Changed += OnLineChanged;
        second.Changed += OnLineChanged;
        first.LotChosen += ApplyChosenLot;
        second.LotChosen += ApplyChosenLot;
        SelectedLine = first;
        Renumber();
        RecalcTotals();
        Banner = "";
    }

    public void Pick(VoucherLineVm line) => SelectedLine = line;

    [RelayCommand]
    private void RemoveLine(VoucherLineVm? line)
    {
        line ??= SelectedLine ?? Lines.LastOrDefault();
        if (line is null)
            return;
        line.Changed -= OnLineChanged;
        line.LotChosen -= ApplyChosenLot;
        Lines.Remove(line);
        if (Lines.Count == 0)
            AddLine(null);
        else
            BindConsumerLines();
        SelectedLine = Lines.LastOrDefault();
        Renumber();
        if (IsVehicleMode && !ManualQuantity)
            ApplyVehicleQuantity();
        else
            RecalcTotals();
    }

    [RelayCommand]
    private void CloseForm()
    {
        IsFormOpen = false;
        Banner = "";
    }

    [RelayCommand]
    private void ResetSlip()
    {
        var warehouse = Warehouse;
        var mode = ExportMode;
        var date = DocumentDate;
        _editingId = null;
        _postedActualByLot.Clear();
        _loading = true;
        Target = null;
        NormText = "";
        Kilometers = "";
        ManualQuantity = false;
        Destination = null;
        ConvertTargetLotType = null;
        ConvertSourceLot = null;
        ConvertQuantity = "";
        FormNumber = "";
        ClearPaper();
        _loading = false;
        DocumentDate = date;
        if (ExportMode != mode)
        {
            _keepEntry = true;
            ExportMode = mode;
            _keepEntry = false;
        }

        Warehouse = warehouse;
        foreach (var line in Lines)
        {
            line.Changed -= OnLineChanged;
            line.LotChosen -= ApplyChosenLot;
        }

        Lines.Clear();
        AddLine();
        if (ExportDesk && !ManualQuantity)
            ApplyVehicleQuantity();
        Fields.ApplySample(null);
        FillWarehouseSide();
        Renumber();
        Ok("Đã xóa trắng phiếu.");
    }

    [RelayCommand]
    private void ViewSelected() => OpenSelected();

    [RelayCommand]
    private void EditSelected() => OpenSelected();

    [RelayCommand]
    private void SelectAllDocuments()
    {
        foreach (var row in ActiveDocuments)
            row.IsSelected = true;
    }

    [RelayCommand]
    private void ClearDocumentSelection()
    {
        foreach (var row in ActiveDocuments)
            row.IsSelected = false;
    }

    private void OpenSelected()
    {
        if (ActiveRow is null)
        {
            Fail(ExportDesk ? "Chọn một phiếu xuất trong danh sách." : "Chọn một phiếu nhập trong danh sách.");
            return;
        }

        EditDocument(ActiveRow.Id);
    }

    [RelayCommand]
    private async Task ExportWord()
    {
        try
        {
            var ids = new List<Guid>();
            if (IsFormOpen && _editingId is Guid editing)
                ids.Add(editing);
            else
                ids.AddRange(ChosenRows().Select(x => x.Id));

            if (ids.Count == 0)
            {
                Fail(ExportDesk ? "Chọn phiếu xuất để xuất Word." : "Chọn phiếu nhập để xuất Word.");
                return;
            }

            var docs = new List<Core.Models.DocumentDetail>();
            foreach (var id in ids)
            {
                var doc = System.GetDocument(id);
                if (doc is null)
                {
                    Fail("Không đọc được phiếu.");
                    return;
                }
                docs.Add(doc);
            }

            var mode = docs.Count == 1
                ? Core.Export.MultiExportMode.IndividualFiles
                : Services.ExportUi.AskMultiMode(docs.Count);
            if (mode is null)
                return;

            var svc = Services.ExportUi.CreateService();
            var artifacts = await Services.ExportUi.RunBusyAsync(
                $"Đang xuất {docs.Count} phiếu Word...",
                progress => svc.ExportSlips(docs, mode.Value, progress));
            if (artifacts is null || artifacts.Count == 0)
                return;
            Services.ExportUi.SaveArtifacts(artifacts, artifacts[0].FileName);
            Ok($"Đã xuất {docs.Count} phiếu Word.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        var picks = ChosenRows();
        if (picks.Count == 0)
        {
            Fail(ExportDesk ? "Chọn phiếu xuất trong danh sách." : "Chọn phiếu nhập trong danh sách.");
            return;
        }

        var voidKind = picks.Any(x => x.Kind is DocumentKind.Transfer or DocumentKind.LotConvert or DocumentKind.Auxiliary or DocumentKind.Consumption);
        var label = picks.Count == 1
            ? picks[0].DisplayNumber
            : $"{picks.Count} phiếu";
        if (voidKind)
        {
            if (!GroupsVm.Confirm($"Hủy {label}? Tồn được hoàn, phiếu chuyển sang đã hủy."))
                return;
        }
        else
        {
            if (!GroupsVm.Confirm($"Xóa {label}? Tồn kho sẽ được hoàn và phiếu bị xóa hẳn."))
                return;
        }

        var editingHit = false;
        foreach (var row in picks)
        {
            if (row.Kind is DocumentKind.Transfer or DocumentKind.LotConvert or DocumentKind.Auxiliary or DocumentKind.Consumption)
                Show(System.Void(row.Id));
            else
                Show(System.DeleteSlip(row.Id));
            if (BannerIsError)
                break;
            if (row.Id == _editingId)
                editingHit = true;
        }

        if (editingHit)
        {
            _editingId = null;
            IsFormOpen = false;
        }

        ReloadList();
    }

    [RelayCommand]
    private void Save()
    {
        ClearInputErrors();
        if (Warehouse is null)
        {
            InvalidWarehouse = true;
            Fail("Chọn kho.");
            return;
        }

        var warehouse = System.GetWarehouses().FirstOrDefault(x => x.Id == Warehouse.Id);
        if (warehouse is null)
        {
            InvalidWarehouse = true;
            Fail("Kho không còn trong danh mục.");
            return;
        }

        ConsumerRow? vehicle = null;
        decimal? tripDistance = null;
        decimal? tripNorm = null;
        if (ExportDesk && ExportMode == ExportSlipMode.Issue)
        {
            if (Target is null || Target.Type != ConsumerType.Machine)
            {
                InvalidTarget = true;
                Fail("Chọn máy.");
                return;
            }

            vehicle = Target;
        }

        if (ExportDesk && ExportMode == ExportSlipMode.Vehicle)
        {
            vehicle = Target;
            if (vehicle is null || vehicle.Type != ConsumerType.Vehicle)
            {
                InvalidTarget = true;
                Fail("Chọn phương tiện.");
                return;
            }

            if (ManualQuantity)
            {
                if (Numbers.Try(NormText, out var optionalNorm) && optionalNorm > 0)
                    tripNorm = optionalNorm;
                if (Numbers.Try(Kilometers, out var optionalDistance) && optionalDistance >= 0)
                    tripDistance = optionalDistance;
            }
            else
            {
                if (!Numbers.Try(NormText, out var parsedNorm) || parsedNorm <= 0)
                {
                    Fail("Định mức phải lớn hơn 0.");
                    return;
                }

                if (!Numbers.Try(Kilometers, out var parsedDistance) || parsedDistance < 0)
                {
                    Fail("Nhập quãng đường.");
                    return;
                }

                tripDistance = parsedDistance;
                tripNorm = parsedNorm;
                ApplyVehicleQuantity();
            }
        }

        OptionRow? destination = null;
        if (ExportDesk && ExportMode == ExportSlipMode.Transfer)
        {
            destination = Destination;
            if (destination is null)
            {
                InvalidDestination = true;
                Fail("Chọn kho nhận.");
                return;
            }

            if (destination.Id == warehouse.Id)
            {
                InvalidDestination = true;
                Fail("Kho nguồn và kho nhận phải khác nhau.");
                return;
            }

            RefreshTransferConsumer(applyDefaults: false);
            if (TransferConsumer is { Type: ConsumerType.Vehicle } transferVehicle)
            {
                vehicle = transferVehicle;
                if (ManualQuantity)
                {
                    if (Numbers.Try(NormText, out var optionalNorm) && optionalNorm > 0)
                        tripNorm = optionalNorm;
                    if (Numbers.Try(Kilometers, out var optionalDistance) && optionalDistance >= 0)
                        tripDistance = optionalDistance;
                }
                else
                {
                    if (!Numbers.Try(NormText, out var parsedNorm) || parsedNorm <= 0)
                    {
                        Fail("Định mức phải lớn hơn 0.");
                        return;
                    }

                    if (!Numbers.Try(Kilometers, out var parsedDistance) || parsedDistance < 0)
                    {
                        Fail("Nhập quãng đường.");
                        return;
                    }

                    tripDistance = parsedDistance;
                    tripNorm = parsedNorm;
                    ApplyVehicleQuantity();
                }
            }
            else if (TransferConsumer is not null)
                vehicle = TransferConsumer;

            // Đổi loại / đa loại trên 1 phiếu ĐC: chặn (chức năng chưa hoàn thiện).
            Guid? transferTypeId = null;
            foreach (var line in Lines)
            {
                var blank = line.Item is null && line.Lot is null
                    && string.IsNullOrWhiteSpace(line.Observed)
                    && string.IsNullOrWhiteSpace(line.Actual)
                    && string.IsNullOrWhiteSpace(PriceFromAmountBlank(line));
                if (blank || line.Lot is null)
                    continue;
                var sourceTypeId = line.Lot.LotTypeId;
                var destTypeId = line.DestinationLotType?.Id ?? sourceTypeId;
                if (destTypeId != sourceTypeId)
                {
                    Fail("Đổi loại lô khi điều chuyển chưa hoàn thiện. Dùng chế độ «Đổi loại lô».");
                    return;
                }

                if (transferTypeId is null)
                    transferTypeId = sourceTypeId;
                else if (transferTypeId != sourceTypeId)
                {
                    Fail("Một phiếu điều chuyển chỉ được một loại lô (chức năng nhiều loại trên cùng phiếu chưa hoàn thiện). Hãy tách thành nhiều phiếu.");
                    return;
                }

                line.DestinationLotType = line.LotType ?? FindLotType(sourceTypeId);
            }
        }
        else if (ExportDesk && ExportMode == ExportSlipMode.LotConvert)
        {
            destination = Warehouse;
            if (ConvertTargetLotType is null)
            {
                InvalidConvertTargetLotType = true;
                Fail("Chọn loại lô đích (TX hoặc SSCĐ).");
                return;
            }

            if (ConvertSourceLot is null)
            {
                InvalidConvertSourceLot = true;
                Fail("Chọn lô nguồn.");
                return;
            }

            if (!Numbers.Try(ConvertQuantity, out var convertQty) || convertQty <= 0)
            {
                InvalidConvertQuantity = true;
                Fail("Nhập số lượng lớn hơn 0.");
                return;
            }

            if (ConvertSourceLot.LotTypeId == ConvertTargetLotType.Id)
            {
                InvalidConvertTargetLotType = true;
                Fail("Loại lô đích phải khác loại của lô nguồn.");
                return;
            }

            if (!IsTxOrSscdLotType(ConvertSourceLot.LotTypeId) || !IsTxOrSscdLotType(ConvertTargetLotType.Id))
            {
                InvalidConvertTargetLotType = true;
                Fail("Chỉ đổi giữa TX và SSCĐ.");
                return;
            }

            EnsureOneConvertLine();
            OnConvertSourceLotChanged(ConvertSourceLot);
            OnConvertQuantityChanged(Numbers.Qty(QuantityMath.Whole(convertQty)));
            OnConvertTargetLotTypeChanged(ConvertTargetLotType);
        }

        var lines = new List<SlipLineInput>();
        var index = 1;
        foreach (var line in Lines)
        {
            var blank = line.Item is null && line.Lot is null
                && string.IsNullOrWhiteSpace(line.Observed)
                && string.IsNullOrWhiteSpace(line.Actual)
                && string.IsNullOrWhiteSpace(PriceFromAmountBlank(line));
            if (blank)
                continue;
            if (ExportDesk && line.Lot is null)
            {
                Fail($"Dòng {index}: chọn lô.");
                return;
            }

            if (line.Item is null)
            {
                Fail($"Dòng {index}: chọn tên xăng dầu.");
                return;
            }

            if (!Numbers.Try(line.Observed, out var observed))
            {
                if (IsLotConvertMode && Numbers.Try(line.Actual, out var convertActual))
                    observed = convertActual;
                else
                {
                    Fail($"Dòng {index}: số lượng phải lớn hơn 0.");
                    return;
                }
            }

            if (!Numbers.Try(line.Vcf, out var vcf) || vcf <= 0)
            {
                Fail($"Dòng {index}: hệ số VCF phải lớn hơn 0.");
                return;
            }

            if (!Numbers.Try(line.Actual, out var actual))
            {
                Fail($"Dòng {index}: số thực tế phải lớn hơn 0.");
                return;
            }

            observed = QuantityMath.Whole(observed);
            actual = QuantityMath.Whole(actual);
            line.ShowWhole(observed, actual);
            if (observed <= 0)
            {
                Fail($"Dòng {index}: số lượng phải lớn hơn 0.");
                return;
            }

            if (actual <= 0)
            {
                Fail($"Dòng {index}: số thực tế phải lớn hơn 0.");
                return;
            }

            decimal price = 0;
            ImportPreview? split = null;
            if (!ExportDesk)
            {
                if (!Numbers.Try(line.Amount, out var money) || !QuantityMath.IsWholeMoney(money) || money < 0)
                {
                    Fail($"Dòng {index}: nhập thành tiền là số nguyên không âm.");
                    return;
                }

                if (line.NeedsSplit)
                {
                    if (string.IsNullOrEmpty(line.SplitMethod))
                    {
                        Fail($"Dòng {index}: chọn cặp tách đơn giá trước khi lưu.");
                        return;
                    }

                    if (!line.TryReadSplit(money, actual, out var chosen, out var splitError))
                    {
                        Fail($"Dòng {index}: {splitError}");
                        return;
                    }

                    split = chosen;
                }
                else if (Numbers.Try(line.Price, out var linePrice)
                    && QuantityMath.TryWholeMoney(linePrice, out var wholePrice)
                    && wholePrice >= 0
                    && wholePrice * actual == money)
                {
                    // Dòng đã tách/chọn cặp: giữ đúng thành tiền người dùng nhập, không tính ngược lại.
                    split = ImportPreview.Success([new SplitLine(1, wholePrice, actual, money, actual)]);
                }
                else
                {
                    split = VoucherLineVm.PriceByActual(money, actual);
                    if (!split.Ok)
                    {
                        Fail($"Dòng {index}: {split.Message}");
                        return;
                    }

                    if (split.WasSplit)
                    {
                        Fail($"Dòng {index}: chọn cặp tách đơn giá trước khi lưu.");
                        return;
                    }

                    if (split.TotalAmount != money || split.TotalActual != actual)
                    {
                        Fail($"Dòng {index}: tổng thành tiền/thực nhập sau tách phải bằng số đã nhập.");
                        return;
                    }
                }
            }
            else if (line.Lot is not null)
                price = line.Lot.UnitPrice;
            else if (!Numbers.Try(line.Price, out price))
            {
                Fail($"Dòng {index}: đơn giá không hợp lệ.");
                return;
            }

            decimal? temperature = null;
            if (!string.IsNullOrWhiteSpace(line.Temperature))
            {
                if (!Numbers.Try(line.Temperature, out var parsed))
                {
                    Fail($"Dòng {index}: nhiệt độ không hợp lệ.");
                    return;
                }

                temperature = parsed;
            }

            decimal? density = null;
            if (!string.IsNullOrWhiteSpace(line.Density))
            {
                if (!Numbers.Try(line.Density, out var parsed))
                {
                    Fail($"Dòng {index}: tỉ trọng không hợp lệ.");
                    return;
                }

                density = parsed;
            }

            decimal? amount = Numbers.Try(line.Amount, out var parsedAmount) ? parsedAmount : null;
            var name = line.Lot?.ItemName
                ?? (line.Item.Id == line.SnapshotItemId && !string.IsNullOrEmpty(line.SnapshotName) ? line.SnapshotName : line.Item.Name);
            if (split is not null)
            {
                var observedLeft = observed;
                for (var partIndex = 0; partIndex < split.Lines.Count; partIndex++)
                {
                    var part = split.Lines[partIndex];
                    var observedPart = partIndex == split.Lines.Count - 1
                        ? QuantityMath.Whole(observedLeft)
                        : QuantityMath.Whole(observed * part.Actual / actual);
                    if (observedPart < 0)
                        observedPart = 0;
                    observedLeft -= observedPart;
                    lines.Add(new SlipLineInput
                    {
                        ItemId = line.Item.Id,
                        ItemName = name,
                        GroupName = line.Item.GroupName,
                        UnitName = line.Item.UnitName,
                        ItemCode = line.Code,
                        QualityGrade = line.Quality,
                        ObservedQuantity = observedPart,
                        Temperature = temperature,
                        Density = density,
                        Vcf = vcf,
                        ActualQuantity = part.Actual,
                        UnitPrice = part.UnitPrice,
                        Amount = part.Amount,
                        LotTypeId = line.LotType?.Id ?? DefaultLotType()?.Id
                    });
                }
            }
            else
            {
                lines.Add(new SlipLineInput
                {
                    LotId = line.Lot?.LotId,
                    ItemId = line.Item.Id,
                    ItemName = name,
                    GroupName = line.Item.GroupName,
                    UnitName = line.Item.UnitName,
                    ItemCode = line.Code,
                    QualityGrade = line.Quality,
                    ObservedQuantity = observed,
                    Temperature = temperature,
                    Density = density,
                    Vcf = vcf,
                    ActualQuantity = actual,
                    UnitPrice = price,
                    Amount = amount,
                    LotTypeId = ExportDesk ? line.Lot?.LotTypeId ?? line.LotType?.Id : line.LotType?.Id ?? DefaultLotType()?.Id,
                    DestinationLotTypeId = ExportDesk && IsTransferMode
                        ? line.Lot?.LotTypeId ?? line.LotType?.Id
                        : ExportDesk && IsLotConvertMode
                            ? ConvertTargetLotType?.Id ?? line.DestinationLotType?.Id
                            : null
                });
            }

            index++;
        }

        if (lines.Count == 0)
        {
            Fail("Thêm ít nhất một dòng hàng.");
            return;
        }

        foreach (var field in Fields.Rows)
        {
            if (!string.IsNullOrWhiteSpace(field.Value))
                continue;
            var fieldName = field.Name ?? "";
            if (IsInvoiceFieldName(fieldName) && !string.IsNullOrWhiteSpace(FormNumber))
                field.Value = FormNumber.Trim();
            if (IsReceiverFieldName(fieldName) && !string.IsNullOrWhiteSpace(ReceiverPerson))
                field.Value = ReceiverPerson.Trim();
        }

        if (MarkAndReportMissingRequired())
            return;

        var result = System.SaveSlip(new SlipRequest
        {
            DocumentId = _editingId,
            IsExport = IsExport,
            ExportMode = ExportDesk ? ExportMode : ExportSlipMode.Issue,
            ConsumerId = vehicle?.Id,
            Distance = tripDistance,
            Norm = tripNorm,
            ManualQuantity = ManualQuantity && ShowVehicleCalc,
            DestinationWarehouseId = destination?.Id,
            DestinationWarehouseName = destination?.Label ?? "",
            DocumentDate = DocumentDate,
            FormNumber = FormNumber,
            WarehouseId = warehouse.Id,
            WarehouseName = warehouse.Name,
            WarehouseTypeName = warehouse.TypeName,
            OrganizationName = OrganizationName,
            UnitTitle = UnitTitle,
            SenderUnit = SenderUnit,
            ReceiverUnit = ReceiverUnit,
            Nature = Nature,
            ContractOrOrder = ContractOrOrder,
            CarrierUnit = CarrierUnit,
            PriceValidUntil = PriceValidUntil,
            DelivererName = DelivererName,
            IntroDocument = IntroDocument,
            VehiclePlate = VehiclePlate,
            CalibrationVolume = CalibrationVolume,
            ReceivedVolume = ReceivedVolume,
            PackageCount = PackageCount,
            ReceiverPerson = ReceiverPerson,
            Kilometers = Kilometers,
            Mission = Mission,
            MissionTaskId = IsExport ? MissionTask?.Id : null,
            OriginPlace = OriginPlace,
            DestinationPlace = DestinationPlace,
            Note = Note,
            SignerReceiver = SignerReceiver,
            SignerDeliverer = SignerDeliverer,
            SignerFinance = SignerFinance,
            SignerWriter = SignerWriter,
            SignerChief = SignerChief,
            SignerCommander = SignerCommander,
            AmountInWords = AmountWords,
            Lines = lines,
            Fields = PaperFieldInputs(),
            AddToSampleSetId = Fields.SampleToUpdate
        });
        Show(result);
        if (!result.Ok
            && result.Message is string saveError
            && saveError.StartsWith("Trường bắt buộc:", StringComparison.OrdinalIgnoreCase))
        {
            var name = saveError["Trường bắt buộc:".Length..].Trim().TrimEnd('.');
            Fields.MarkErrorByName(name);
            if (IsInvoiceFieldName(name))
                InvalidFormNumber = true;
            if (IsReceiverFieldName(name))
                InvalidReceiverPerson = true;
            if (Fields.Rows.Any(x => x.HasError) || Fields.PanelVisible)
                ExtraFieldsOpen = true;
        }

        if (result.Ok && IsExport && vehicle is not null)
            RememberManualFuel(vehicle.Id);
        if (!result.Ok || result.Id is not Guid id)
            return;
        _editingId = null;
        ReloadList();
        if (IsExport)
            SelectedExport = ExportDocuments.FirstOrDefault(x => x.Id == id);
        else
            SelectedImport = ImportDocuments.FirstOrDefault(x => x.Id == id);

        if (ContinueAfterSave)
        {
            PrepareNextSlipAfterSave(result.Message);
            return;
        }

        IsFormOpen = false;
    }

    /// <summary>Giữ form mở: xóa trắng dòng/số phiếu, giữ kho–chế độ–ngày để nhập phiếu tiếp.</summary>
    private void PrepareNextSlipAfterSave(string savedMessage)
    {
        var warehouse = Warehouse;
        var mode = ExportMode;
        var date = DocumentDate;
        var keepContinue = ContinueAfterSave;
        _postedActualByLot.Clear();
        ClearInputErrors();
        _loading = true;
        Target = null;
        NormText = "";
        Kilometers = "";
        ManualQuantity = false;
        Destination = null;
        ConvertTargetLotType = null;
        ConvertSourceLot = null;
        ConvertQuantity = "";
        FormNumber = "";
        ClearPaper();
        _loading = false;
        ContinueAfterSave = keepContinue;
        DocumentDate = date;
        if (ExportMode != mode)
        {
            _keepEntry = true;
            ExportMode = mode;
            _keepEntry = false;
        }

        Warehouse = warehouse;
        foreach (var line in Lines)
        {
            line.Changed -= OnLineChanged;
            line.LotChosen -= ApplyChosenLot;
        }

        Lines.Clear();
        AddLine();
        if (IsLotConvertMode)
            EnsureConvertTargetDefault();
        if (ExportDesk && !ManualQuantity)
            ApplyVehicleQuantity();
        Fields.ApplySample(null);
        FillWarehouseSide();
        if (IsTransferMode)
            RefreshTransferConsumer(applyDefaults: true);
        else if (ExportDesk)
            RefreshExportFields();
        Renumber();
        IsFormOpen = true;
        Ok(string.IsNullOrWhiteSpace(savedMessage)
            ? "Đã lưu. Tiếp tục thêm phiếu mới."
            : $"{savedMessage} Tiếp tục thêm phiếu mới.");
    }

    private void NewSlip()
    {
        EnsureDesk();
        ClearInputErrors();
        _editingId = null;
        _postedActualByLot.Clear();
        IsExport = ExportDesk;
        ExportMode = ExportDesk ? ExportSlipMode.Transfer : ExportSlipMode.Issue;
        Target = null;
        NormText = "";
        ManualQuantity = false;
        Warehouse = null;
        ApplyWarehouseChoices();
        DocumentDate = DateTime.Today;
        FormNumber = "";
        OrganizationName = "";
        UnitTitle = "";
        SenderUnit = "";
        ReceiverUnit = "";
        Nature = "";
        ContractOrOrder = "";
        CarrierUnit = "";
        PriceValidUntil = "";
        DelivererName = "";
        IntroDocument = "";
        VehiclePlate = "";
        CalibrationVolume = "";
        ReceivedVolume = "";
        PackageCount = "";
        ReceiverPerson = "";
        Kilometers = "";
        Mission = "";
        MissionTask = null;
        OriginPlace = "";
        DestinationPlace = "";
        Note = "";
        SignerReceiver = "";
        SignerDeliverer = "";
        SignerFinance = "";
        SignerWriter = "";
        SignerChief = "";
        SignerCommander = "";
        foreach (var line in Lines)
        {
            line.Changed -= OnLineChanged;
            line.LotChosen -= ApplyChosenLot;
        }

        Lines.Clear();
        AddLine();
        if (ExportDesk && !ManualQuantity)
            ApplyVehicleQuantity();
        Fields.ApplySample(null);
        FillWarehouseSide();
        Banner = "";
        Renumber();
        IsFormOpen = true;
    }

    private void Load(DocumentDetail doc)
    {
        EnsureDesk();
        _loading = true;
        _editingId = doc.Id;
        _postedActualByLot.Clear();
        foreach (var row in doc.Lines)
        {
            _postedActualByLot.TryGetValue(row.LotId, out var posted);
            _postedActualByLot[row.LotId] = posted + row.ActualQuantity;
        }
        IsExport = ExportDesk;
        ExportMode = doc.Kind switch
        {
            DocumentKind.Transfer => ExportSlipMode.Transfer,
            DocumentKind.LotConvert => ExportSlipMode.LotConvert,
            DocumentKind.Consumption => ExportSlipMode.Vehicle,
            DocumentKind.Issue when doc.ConsumerId is Guid && doc.ConsumerTypeName == Labels.Consumer(ConsumerType.Machine) => ExportSlipMode.Issue,
            DocumentKind.Issue => ExportSlipMode.Retail,
            _ => ExportSlipMode.Issue
        };
        ApplyWarehouseChoices(doc.WarehouseId);
        Destination = Destinations.FirstOrDefault(x => x.Id == doc.DestinationWarehouseId);
        if (ExportMode == ExportSlipMode.LotConvert)
        {
            var destTypeId = doc.Lines
                .Select(x => x.DestinationLotTypeId)
                .FirstOrDefault(x => x is Guid id && id != Guid.Empty);
            ConvertTargetLotType = destTypeId is Guid id ? FindLotType(id) : null;
            var first = doc.Lines.FirstOrDefault();
            _loading = true;
            ConvertQuantity = first is null ? "" : Numbers.Qty(first.ActualQuantity);
            ConvertSourceLot = first is null
                ? null
                : Lots.FirstOrDefault(x => x.LotId == first.LotId);
            _loading = false;
            EnsureOneConvertLine();
            if (ConvertSourceLot is not null)
                OnConvertSourceLotChanged(ConvertSourceLot);
            if (!string.IsNullOrWhiteSpace(ConvertQuantity))
                OnConvertQuantityChanged(ConvertQuantity);
        }
        else
        {
            ConvertTargetLotType = null;
            ConvertSourceLot = null;
            ConvertQuantity = "";
        }
        DocumentDate = doc.DocumentDate;
        FormNumber = doc.Slip.FormNumber;
        OrganizationName = doc.Slip.OrganizationName;
        UnitTitle = doc.Slip.UnitTitle;
        SenderUnit = doc.Slip.SenderUnit;
        ReceiverUnit = doc.Slip.ReceiverUnit;
        Nature = doc.Slip.Nature;
        ContractOrOrder = doc.Slip.ContractOrOrder;
        CarrierUnit = doc.Slip.CarrierUnit;
        PriceValidUntil = doc.Slip.PriceValidUntil;
        DelivererName = doc.Slip.DelivererName;
        IntroDocument = doc.Slip.IntroDocument;
        VehiclePlate = doc.Slip.VehiclePlate;
        CalibrationVolume = doc.Slip.CalibrationVolume;
        ReceivedVolume = doc.Slip.ReceivedVolume;
        PackageCount = doc.Slip.PackageCount;
        ReceiverPerson = doc.Slip.ReceiverPerson;
        Kilometers = doc.Distance is decimal distance ? Numbers.Decimal(distance) : doc.Slip.Kilometers;
        NormText = doc.Norm is decimal norm ? Numbers.Factor(norm) : "";
        ManualQuantity = doc.ManualQuantity;
        Target = Targets.FirstOrDefault(x => x.Id == doc.ConsumerId);
        Mission = doc.Slip.Mission;
        MissionTask = MissionTasks.FirstOrDefault(x => x.Id == doc.Slip.MissionTaskId);
        OriginPlace = doc.Slip.OriginPlace;
        DestinationPlace = doc.Slip.DestinationPlace;
        Note = doc.Slip.Note;
        SignerReceiver = doc.Slip.SignerReceiver;
        SignerDeliverer = doc.Slip.SignerDeliverer;
        SignerFinance = doc.Slip.SignerFinance;
        SignerWriter = doc.Slip.SignerWriter;
        SignerChief = doc.Slip.SignerChief;
        SignerCommander = doc.Slip.SignerCommander;
        foreach (var line in Lines)
        {
            line.Changed -= OnLineChanged;
            line.LotChosen -= ApplyChosenLot;
        }

        Lines.Clear();
        ReloadLots(doc.Lines.Select(x => x.LotId));
        foreach (var row in doc.Lines)
        {
            var line = MakeLine();
            var fallbackName = string.IsNullOrWhiteSpace(row.ItemName) ? doc.ItemName : row.ItemName;
            line.Apply(row with { ItemName = fallbackName }, Items);
            line.SelectLot(Lots.FirstOrDefault(x => x.LotId == row.LotId));
            if (line.Item is null && line.Lot?.ItemId is Guid lotItemId)
            {
                var fromLot = Items.FirstOrDefault(x => x.Id == lotItemId);
                if (fromLot is not null)
                {
                    line.SelectItem(fromLot);
                    if (string.IsNullOrWhiteSpace(line.SnapshotName))
                        line.SnapshotName = fromLot.Name;
                }
                else if (string.IsNullOrWhiteSpace(line.SnapshotName) && !string.IsNullOrWhiteSpace(line.Lot.ItemName))
                {
                    line.SnapshotName = line.Lot.ItemName;
                    line.NotifyFuelDisplay();
                }
            }
            else
                line.NotifyFuelDisplay();

            line.LotType = FindLotType(row.LotTypeId);
            line.DestinationLotType = FindLotType(row.DestinationLotTypeId ?? row.LotTypeId);
            line.Changed += OnLineChanged;
            line.LotChosen += ApplyChosenLot;
            Lines.Add(line);
        }

        if (Lines.Count == 0)
            AddLine();
        BindConsumerLines();
        Fields.ApplySample(null);
        Fields.LoadSnapshot(doc.Fields);
        _loading = false;
        RefreshTransferConsumer(applyDefaults: false);
        Renumber();
        RecalcTotals();
        IsFormOpen = true;
        Ok($"Đang sửa {Display(doc)}.");
    }

    private static string Display(DocumentDetail doc) =>
        string.IsNullOrWhiteSpace(doc.Slip.FormNumber) ? doc.Number : doc.Slip.FormNumber;

    private void FillWarehouseSide()
    {
        if (Warehouse is null)
            return;
        if (!IsExport && string.IsNullOrWhiteSpace(ReceiverUnit))
            ReceiverUnit = Warehouse.Label;
        if (IsExport && string.IsNullOrWhiteSpace(SenderUnit))
            SenderUnit = Warehouse.Label;
    }

    private void RecalcTotals()
    {
        decimal observed = 0;
        decimal actual = 0;
        decimal amount = 0;
        var count = 0;
        foreach (var line in Lines)
        {
            if (Numbers.Try(line.Observed, out var lineObserved))
                observed += lineObserved;
            if (Numbers.Try(line.Actual, out var lineActual))
                actual += lineActual;
            if (Numbers.Try(line.Amount, out var lineAmount))
                amount += lineAmount;
            if (line.Item is not null || Numbers.Try(line.Observed, out _))
                count++;
        }

        LineCountText = $"Tổng cộng: {count} khoản";
        QuantityText = $"Số lượng: {Numbers.Qty(actual)} lít, kg.";
        TotalObservedText = Numbers.Qty(observed);
        TotalActualText = Numbers.Qty(actual);
        TotalAmountText = Numbers.Money(amount);
        AmountWords = MoneyWords.ToDong(amount);
        HasSplit = Lines.Any(x => x.NeedsSplit);
        RefreshRemainings();
    }

    private void RefreshRemainings()
    {
        var used = new Dictionary<Guid, decimal>();
        if (ExportDesk)
        {
            foreach (var line in Lines)
            {
                if (line.Lot is null || !Numbers.Try(line.Actual, out var actual) || actual <= 0)
                    continue;
                used.TryGetValue(line.Lot.LotId, out var sum);
                used[line.Lot.LotId] = sum + actual;
            }
        }

        foreach (var line in Lines)
        {
            if (!ExportDesk || line.Lot is not LotOption lot || !used.TryGetValue(lot.LotId, out var taken))
            {
                line.RemainingText = "";
                line.RemainingShort = false;
                continue;
            }

            _postedActualByLot.TryGetValue(lot.LotId, out var posted);
            var left = lot.Quantity + posted - taken;
            line.RemainingText = $"còn lại {Numbers.Qty(left)}";
            line.RemainingShort = left < 0;
        }
    }

    private VoucherLineVm MakeLine() => new()
    {
        PriceFromAmount = !ExportDesk,
        QuantityFromVehicle = ExportDesk && ShowVehicleCalc && !ManualQuantity && Lines.Count == 0
    };

    private void BindConsumerLines()
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            var vehicleHead = ExportDesk && ShowVehicleCalc && !ManualQuantity && i == 0;
            Lines[i].QuantityFromVehicle = vehicleHead;
        }
    }

    private static string PriceFromAmountBlank(VoucherLineVm line) =>
        line.PriceFromAmount ? line.Amount : line.Price;

    private void Renumber()
    {
        for (var i = 0; i < Lines.Count; i++)
            Lines[i].Index = i + 1;
    }

    private void InvalidateList() => _listStale = true;

    private string ListCacheKey() =>
        $"{ListYear.Trim()}|{ListQuarter}|{(ExportDesk ? "X" : "N")}|g{System.CatalogGeneration}";

    private void ReloadListIfNeeded()
    {
        var key = ListCacheKey();
        if (!_listStale && key == _listCacheKey && ActiveDocuments.Count > 0)
            return;
        ReloadList();
    }

    private async Task ReloadListIfNeededAsync(IProgress<DemoProgress>? progress)
    {
        var key = ListCacheKey();
        if (!_listStale && key == _listCacheKey && ActiveDocuments.Count > 0)
            return;

        progress?.Report(new DemoProgress("Đang đọc danh sách phiếu", 0, 1, 0, 1));
        if (!TryParseListFilter(out var year, out var quarter))
        {
            Publish([]);
            return;
        }

        var export = ExportDesk;
        var rows = await Task.Run(() => System.ListDesk(export, year, quarter));
        progress?.Report(new DemoProgress("Đang đọc danh sách phiếu", 1, 1, 1, 1));
        Publish(rows);
        _listStale = false;
        _listCacheKey = key;
    }

    private void ReloadList()
    {
        if (!TryParseListFilter(out var year, out var quarter))
        {
            Publish([]);
            return;
        }

        Publish(System.ListDesk(ExportDesk, year, quarter));
        _listStale = false;
        _listCacheKey = ListCacheKey();
    }

    private bool TryParseListFilter(out int? year, out int quarter)
    {
        year = null;
        quarter = ListQuarter switch
        {
            "Quý 1" => 1,
            "Quý 2" => 2,
            "Quý 3" => 3,
            "Quý 4" => 4,
            _ => 0
        };
        var yearText = ListYear.Trim();
        if (yearText.Length == 0)
            return true;
        if (!int.TryParse(yearText, out var parsed) || parsed is < 1900 or > 2100)
            return false;
        year = parsed;
        return true;
    }

    private void Publish(IReadOnlyList<DocumentRow> rows)
    {
        var keep = ExportDesk ? SelectedExport?.Id : SelectedImport?.Id;
        var list = new ObservableCollection<VoucherListItemVm>(
            rows.OrderByDescending(x => x.DocumentDate).ThenByDescending(x => x.DisplayNumber)
                .Select(x => new VoucherListItemVm(x)));
        var selected = keep is Guid id ? list.FirstOrDefault(x => x.Id == id) : null;
        if (ExportDesk)
        {
            ExportDocuments = list;
            SelectedExport = selected;
        }
        else
        {
            ImportDocuments = list;
            SelectedImport = selected;
        }
    }

    private void ApplyWarehouseChoices(Guid? prefer = null)
    {
        var pickingNew = prefer is null && Warehouse is null;
        prefer ??= Warehouse?.Id;
        if (pickingNew && ExportDesk)
            prefer = System.GetWarehouses().FirstOrDefault(x => x.Type == WarehouseType.Main)?.Id ?? prefer;
        WarehouseChoices.Clear();
        foreach (var row in System.GetWarehouses())
            WarehouseChoices.Add(new OptionRow(row.Id, row.Name));
        var loading = _loading;
        _loading = true;
        Warehouse = WarehouseChoices.FirstOrDefault(x => x.Id == prefer) ?? WarehouseChoices.FirstOrDefault();
        _loading = loading;
        if (!loading)
        {
            ReloadLots();
            RefreshExportFields();
        }
    }

    private void RefreshExportFields()
    {
        if (!ExportDesk || _loading || _editingId is not null || IsRetailMode)
            return;
        var selected = IsConsumerMode ? Target : IsTransferMode ? TransferConsumer : null;
        if (selected is null)
            return;
        var values = System.GetOwnedExportDefaults(selected.Id);
        var sampleId = values.Count == 0
            ? null
            : System.GetConsumers().FirstOrDefault(x => x.Id == selected.Id)?.DefaultExportSampleSetId;
        ApplySampleToPaper(values);
        ExportFields.ShowValues(sampleId, values);
        ExtraFieldsOpen = ExportFields.PanelVisible;
    }

    private void ApplySampleToPaper(IReadOnlyDictionary<string, string> values)
    {
        void Put(string name, Action<string> set)
        {
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                set(value);
        }

        Put("Cơ quan", value => OrganizationName = value);
        Put("Đơn vị", value => UnitTitle = value);
        Put("Đơn vị giao", value => SenderUnit = value);
        Put("Đơn vị nhận", value => ReceiverUnit = value);
        Put("Tính chất xuất", value => Nature = value);
        Put("Theo lệnh (KH)", value => ContractOrOrder = value);
        Put("Người nhận", value => ReceiverPerson = value);
        Put("Giấy giới thiệu và CMT", value => IntroDocument = value);
        Put("Có giá đến ngày", value => PriceValidUntil = value);
        Put("Đơn vị vận chuyển", value => CarrierUnit = value);
        Put("Số xe", value => VehiclePlate = value);
        Put("Số km", value => Kilometers = value);
        Put("Dung tích kiểm định", value => CalibrationVolume = value);
        Put("Dung tích nhận hàng", value => ReceivedVolume = value);
        Put("Số lượng bao bì", value => PackageCount = value);
        Put("Nhiệm vụ", value => Mission = value);
        Put("Nơi đi", value => OriginPlace = value);
        Put("Nơi đến", value => DestinationPlace = value);
        Put("Ghi chú", value => Note = value);
        Put("Chữ ký người nhận", value => SignerReceiver = value);
        Put("Chữ ký người giao", value => SignerDeliverer = value);
        Put("Chữ ký tài chính", value => SignerFinance = value);
        Put("Chữ ký người viết phiếu", value => SignerWriter = value);
        Put("Chữ ký trưởng ban HC-KT", value => SignerChief = value);
        Put("Chữ ký chỉ huy đơn vị", value => SignerCommander = value);
    }

    private void ReloadLots(IEnumerable<Guid>? include = null)
    {
        Lots.Clear();
        if (!ExportDesk || Warehouse is null)
            return;
        var seen = new HashSet<Guid>();
        void AddRange(IEnumerable<LotOption> rows)
        {
            foreach (var row in rows)
            {
                if (seen.Add(row.LotId))
                    Lots.Add(row);
            }
        }

        AddRange(System.GetLots(Warehouse.Id));
        if (include is not null)
        {
            foreach (var id in include.Distinct())
                AddRange(System.GetLots(Warehouse.Id, id));
        }

        OnPropertyChanged(nameof(ConvertSourceLots));
    }

    private void ResetEntryLines()
    {
        foreach (var line in Lines)
        {
            line.Changed -= OnLineChanged;
            line.LotChosen -= ApplyChosenLot;
        }

        Lines.Clear();
        AddLine();
    }

    private void ApplyObjectSample()
    {
        var loading = _loading;
        _loading = true;
        ClearPaper();
        _loading = loading;
        RefreshExportFields();
    }

    private void ResetSlipEntry()
    {
        ClearInputErrors();
        var loading = _loading;
        _loading = true;
        Target = null;
        NormText = "";
        Kilometers = "";
        ManualQuantity = false;
        Destination = null;
        ConvertTargetLotType = null;
        ConvertSourceLot = null;
        ConvertQuantity = "";
        ClearPaper();
        foreach (var line in Lines)
        {
            line.Changed -= OnLineChanged;
            line.LotChosen -= ApplyChosenLot;
        }

        Lines.Clear();
        _loading = loading;
        AddLine();
        if (IsRetailMode)
        {
            ExportFields.ApplySample(null);
            ExtraFieldsOpen = ExportFields.PanelVisible;
        }
        else
            RefreshExportFields();
    }

    private void ClearPaper()
    {
        OrganizationName = "";
        UnitTitle = "";
        SenderUnit = "";
        ReceiverUnit = "";
        Nature = "";
        ContractOrOrder = "";
        CarrierUnit = "";
        PriceValidUntil = "";
        DelivererName = "";
        IntroDocument = "";
        VehiclePlate = "";
        CalibrationVolume = "";
        ReceivedVolume = "";
        PackageCount = "";
        ReceiverPerson = "";
        Mission = "";
        MissionTask = null;
        Note = "";
        SignerReceiver = "";
        SignerDeliverer = "";
        SignerFinance = "";
        SignerWriter = "";
        SignerChief = "";
        SignerCommander = "";
    }

    private void ApplyDefaultFuel(bool replace = false)
    {
        if (!ExportDesk || _editingId is not null || _loading)
            return;
        if (!IsConsumerMode && !(IsTransferMode && TransferConsumer is not null))
            return;
        var selected = IsConsumerMode ? Target : TransferConsumer;
        if (selected is null || Warehouse is null)
            return;
        var line = Lines.Count == 1 ? Lines[0] : null;
        if (line is null || (line.FuelTouched && !replace))
            return;
        var consumer = System.GetConsumers().FirstOrDefault(x => x.Id == selected.Id);
        if (consumer?.DefaultGroupId is not Guid groupId)
            return;
        var chosen = ExportFuelChoice.Pick(Lots, Items, groupId, consumer.DefaultItemId);
        if (chosen is null)
            return;
        var item = Items.FirstOrDefault(x => x.Id == chosen.ItemId);
        line.AutoItemId = chosen.ItemId;
        line.SelectLot(chosen);
        line.Price = Numbers.Money(chosen.UnitPrice);
        line.FillFromLot(item, chosen.FirstVcf);
        line.FuelTouched = false;
    }

    private void RememberManualFuel(Guid consumerId)
    {
        var line = Lines.FirstOrDefault();
        if (line is null || !line.FuelTouched || line.Item is not ItemRow item)
            return;
        if (line.AutoItemId == item.Id)
            return;
        var consumer = System.GetConsumers().FirstOrDefault(x => x.Id == consumerId);
        if (consumer?.DefaultGroupId is not Guid groupId || item.GroupId != groupId)
            return;
        if (consumer.DefaultItemId == item.Id)
            return;
        System.RememberPreferredFuel(consumerId, item.Id);
    }

    private void ApplyChosenLot(VoucherLineVm line)
    {
        var lot = line.Lot;
        if (lot is null || _loading)
            return;
        var item = Items.FirstOrDefault(x => lot.ItemId is Guid id && x.Id == id)
            ?? Items.FirstOrDefault(x => x.Name == lot.ItemName);
        line.FillFromLot(item, lot.FirstVcf);
        line.LotType = FindLotType(lot.LotTypeId);
        if (IsTransferMode)
            line.DestinationLotType = FindLotType(lot.LotTypeId);
        if (IsLotConvertMode)
        {
            line.DestinationLotType = ConvertTargetLotType;
            if (ConvertTargetLotType is null || ConvertTargetLotType.Id == lot.LotTypeId)
                EnsureConvertTargetDefault(preferOppositeOf: lot.LotTypeId);
            line.DestinationLotType = ConvertTargetLotType;
        }
        if (ShowVehicleCalc && !ManualQuantity)
            ApplyVehicleQuantity();
    }

    private LotTypeRow? DefaultLotType() =>
        LotTypes.FirstOrDefault(x => x.Id == SeedIds.LotTypeTx) ?? LotTypes.FirstOrDefault();

    private LotTypeRow? FindLotType(Guid? id)
    {
        var typeId = id is Guid given && given != Guid.Empty ? given : SeedIds.LotTypeTx;
        return LotTypes.FirstOrDefault(x => x.Id == typeId) ?? DefaultLotType();
    }

    private void EnsureConvertTargetDefault(Guid? preferOppositeOf = null)
    {
        OnPropertyChanged(nameof(ConvertLotTypeChoices));
        var sourceId = preferOppositeOf
            ?? Lines.Select(x => x.Lot?.LotTypeId).FirstOrDefault(x => x is Guid id && id != Guid.Empty);
        if (sourceId is Guid source)
        {
            var opposite = OppositeTxSscd(source);
            if (opposite is not null)
            {
                ConvertTargetLotType = opposite;
                return;
            }
        }

        ConvertTargetLotType ??= ConvertLotTypeChoices.FirstOrDefault(x => x.Id == SeedIds.LotTypeSscd)
            ?? ConvertLotTypeChoices.FirstOrDefault();
    }

    private LotTypeRow? OppositeTxSscd(Guid sourceTypeId)
    {
        if (sourceTypeId == SeedIds.LotTypeTx)
            return LotTypes.FirstOrDefault(x => x.Id == SeedIds.LotTypeSscd);
        if (sourceTypeId == SeedIds.LotTypeSscd)
            return LotTypes.FirstOrDefault(x => x.Id == SeedIds.LotTypeTx);
        var code = LotTypes.FirstOrDefault(x => x.Id == sourceTypeId)?.Code?.Trim().ToUpperInvariant() ?? "";
        if (code is "TX")
            return LotTypes.FirstOrDefault(x =>
            {
                var c = x.Code?.Trim().ToUpperInvariant() ?? "";
                return c is "SSCĐ" or "SSCD";
            });
        if (code is "SSCĐ" or "SSCD")
            return LotTypes.FirstOrDefault(x => string.Equals(x.Code?.Trim(), "TX", StringComparison.OrdinalIgnoreCase));
        return null;
    }

    private static bool IsTxOrSscdLotType(Guid typeId)
    {
        if (typeId == SeedIds.LotTypeTx || typeId == SeedIds.LotTypeSscd)
            return true;
        return false;
    }

    private void OnLineChanged()
    {
        if (ShowVehicleCalc && !ManualQuantity)
            ApplyVehicleQuantity();
        else
            RecalcTotals();
    }

    private void ApplyVehicleQuantity()
    {
        if (!ShowVehicleCalc || ManualQuantity)
            return;
        var line = Lines.FirstOrDefault();
        if (line is not null
            && Numbers.Try(Kilometers, out var distance) && distance >= 0
            && Numbers.Try(NormText, out var norm) && norm > 0
            && Numbers.Try(line.Vcf, out var vcf) && vcf > 0)
        {
            line.QuantityFromVehicle = true;
            line.SetVehicleQuantity(QuantityMath.VehicleActual(distance, norm), vcf);
        }

        RecalcTotals();
    }

    private void RefreshTransferConsumer(bool applyDefaults = true)
    {
        var previous = TransferConsumer?.Id;
        ConsumerRow? next = null;
        if (IsTransferMode)
        {
            if (Destination is not null)
                next = System.GetConsumers().FirstOrDefault(x => x.Id == Destination.Id);
            if (next is null && Warehouse is not null)
                next = System.GetConsumers().FirstOrDefault(x => x.Id == Warehouse.Id);
        }

        TransferConsumer = next;
        OnPropertyChanged(nameof(TransferConsumer));
        OnPropertyChanged(nameof(ShowVehicleCalc));
        OnPropertyChanged(nameof(ShowManualQuantityToggle));
        OnPropertyChanged(nameof(IsShipDestination));
        OnPropertyChanged(nameof(IsMissionTaskVisible));
        OnPropertyChanged(nameof(ModeHint));
        if (IsShipDestination)
            MissionTask = null;
        BindConsumerLines();
        if (!applyDefaults || _loading)
            return;
        if (next?.Id == previous && previous is not null)
        {
            if (ShowVehicleCalc && !ManualQuantity)
                ApplyVehicleQuantity();
            return;
        }

        NormText = "";
        if (next?.Type == ConsumerType.Vehicle && next.Norm is decimal norm)
            NormText = Numbers.Factor(norm);
        if (next is not null && string.IsNullOrWhiteSpace(VehiclePlate))
            VehiclePlate = next.Name;
        if (next is not null)
        {
            ApplyObjectSample();
            ApplyDefaultFuel(replace: true);
        }

        if (ShowVehicleCalc && !ManualQuantity)
            ApplyVehicleQuantity();
    }

}

public partial class VoucherListItemVm : ObservableObject
{
    public VoucherListItemVm(DocumentRow row) => Row = row;

    public DocumentRow Row { get; }
    public Guid Id => Row.Id;

    [ObservableProperty] private bool _isSelected;
}
