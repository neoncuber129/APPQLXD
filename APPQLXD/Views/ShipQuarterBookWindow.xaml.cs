using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using APPQLXD.Core;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Models;
using APPQLXD.Core.Persistence;
using APPQLXD.ViewModels;
using ToolTip = System.Windows.Controls.ToolTip;

namespace APPQLXD.Views;

public sealed class ShipQuarterBookPopupVm
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string StatusText { get; set; } = "";
    public string ShipName { get; init; } = "";
    public string ShipType { get; init; } = "";
    public string FuelUsed { get; init; } = "";
    public string FuelGroupName { get; init; } = "";
    public bool FuelIsGasoline { get; init; }
    public string FormId { get; init; } = "Số 3-04.3/XD-14";
    public string UnitNote { get; init; } = "Đơn vị tính: Nhiên liệu: Lít 15° C; Dầu mỡ: Kg.";
    public Guid ConsumerId { get; init; }
    public DateTime QuarterDate { get; init; }
    public decimal DefaultMainMachines { get; init; } = 1;
    public decimal DefaultAuxMachines { get; init; } = 1;
    public IReadOnlyDictionary<string, decimal> NormRates { get; init; } =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<ShipQuarterBookLineVm> Rows { get; init; } = [];
    public IReadOnlyList<ShipQuarterBookLineEdit> PendingLines { get; set; } = [];
    public IReadOnlyList<LotTypeRow> LotTypes { get; init; } = [];
    public IReadOnlyList<MissionTaskRow> MissionTasks { get; init; } = [];
    internal int NextAddOrder { get; set; }
}

public sealed class ShipQuarterBookLineVm : INotifyPropertyChanged
{
    private string _documentNumber = "";
    private DateTime? _documentDate;
    private string _description = "";
    private decimal _mainOpsCount;
    private decimal _hoursAtBerth;
    private decimal _hoursCx25;
    private decimal _hoursCx50;
    private decimal _hoursCx75;
    private decimal _hoursCx100;
    private decimal _auxOpsCount;
    private decimal _auxHours;
    private bool _manualFuelOut;
    private decimal _fuelOutManual;
    private bool _manualOilOut;
    private decimal _oilOut;
    private bool _pendingRecalculate;
    private decimal? _fuelIn;
    private decimal? _oilIn;
    private decimal? _fuelBalance;
    private decimal? _oilBalance;
    private string _fuelBalanceLotTip = "";
    private string _oilBalanceLotTip = "";
    private decimal? _rowTotal;
    private Guid _lotTypeId = SeedIds.LotTypeTx;
    private string _lotTypeCode = "TX";
    private Guid? _missionTaskId;
    private string _missionTaskName = "";

    public bool IsOpening { get; init; }
    public bool IsTransfer { get; init; }
    /// <summary>Thứ tự thêm trong phiên (0 = dòng đã có). Cùng ngày: dòng mới nằm dưới cùng.</summary>
    public int AddOrder { get; set; }
    public Guid? LineId { get; set; }
    public bool IsEditable => !IsOpening && !IsTransfer;
    public bool LotTypeLocked => IsOpening || IsTransfer;

    public bool PendingRecalculate
    {
        get => _pendingRecalculate;
        set { if (Set(ref _pendingRecalculate, value)) Notify(); }
    }

    public Guid LotTypeId
    {
        get => _lotTypeId;
        set
        {
            if (Set(ref _lotTypeId, value == Guid.Empty ? SeedIds.LotTypeTx : value))
                Notify();
        }
    }

    public string LotTypeCode
    {
        get => _lotTypeCode;
        set { if (Set(ref _lotTypeCode, string.IsNullOrWhiteSpace(value) ? "TX" : value.Trim())) Notify(); }
    }

    public Guid? MissionTaskId
    {
        get => _missionTaskId;
        set { if (Set(ref _missionTaskId, value)) Notify(); }
    }

    public string MissionTaskName
    {
        get => _missionTaskName;
        set { if (Set(ref _missionTaskName, value ?? "")) Notify(); }
    }

    public string DocumentNumber
    {
        get => _documentNumber;
        set { if (Set(ref _documentNumber, value ?? "")) Notify(); }
    }

    public DateTime? DocumentDate
    {
        get => _documentDate;
        set
        {
            if (Set(ref _documentDate, value))
            {
                Notify();
                DateChanged?.Invoke(this);
            }
        }
    }

    public string Description
    {
        get => _description;
        set { if (Set(ref _description, value ?? "")) Notify(); }
    }

    public decimal MainOpsCount
    {
        get => _mainOpsCount;
        set
        {
            if (Set(ref _mainOpsCount, value))
                OnHourOrMachineChanged();
        }
    }

    public decimal HoursAtBerth
    {
        get => _hoursAtBerth;
        set
        {
            if (Set(ref _hoursAtBerth, value))
                OnHourOrMachineChanged();
        }
    }

    public decimal HoursCx25
    {
        get => _hoursCx25;
        set
        {
            if (Set(ref _hoursCx25, value))
                OnHourOrMachineChanged();
        }
    }

    public decimal HoursCx50
    {
        get => _hoursCx50;
        set
        {
            if (Set(ref _hoursCx50, value))
                OnHourOrMachineChanged();
        }
    }

    public decimal HoursCx75
    {
        get => _hoursCx75;
        set
        {
            if (Set(ref _hoursCx75, value))
                OnHourOrMachineChanged();
        }
    }

    public decimal HoursCx100
    {
        get => _hoursCx100;
        set
        {
            if (Set(ref _hoursCx100, value))
                OnHourOrMachineChanged();
        }
    }

    public decimal AuxOpsCount
    {
        get => _auxOpsCount;
        set
        {
            if (Set(ref _auxOpsCount, value))
                OnHourOrMachineChanged();
        }
    }

    public decimal AuxHours
    {
        get => _auxHours;
        set
        {
            if (Set(ref _auxHours, value))
                OnHourOrMachineChanged();
        }
    }

    private void OnHourOrMachineChanged()
    {
        var wasPending = _pendingRecalculate;
        if (!_manualFuelOut)
            _pendingRecalculate = false;
        Notify();
        if (wasPending && !_manualFuelOut)
            RequestRender?.Invoke();
    }

    public bool ManualFuelOut
    {
        get => _manualFuelOut;
        set { if (Set(ref _manualFuelOut, value)) Notify(); }
    }

    public decimal FuelOutManual
    {
        get => _fuelOutManual;
        set { if (Set(ref _fuelOutManual, value)) Notify(); }
    }

    public bool ManualOilOut
    {
        get => _manualOilOut;
        set { if (Set(ref _manualOilOut, value)) Notify(); }
    }

    public decimal OilOut
    {
        get => _oilOut;
        set
        {
            if (Set(ref _oilOut, value))
                Notify();
        }
    }

    public void SetOilOutAuto(decimal value)
    {
        _oilOut = value;
        OnPropertyChanged(nameof(OilOut));
    }

    /// <summary>Một chế độ chung cho Xuất NL + dầu mỡ.</summary>
    public void SyncManualMode(bool manual)
    {
        _manualFuelOut = manual;
        _manualOilOut = manual;
        OnPropertyChanged(nameof(ManualFuelOut));
        OnPropertyChanged(nameof(ManualOilOut));
    }

    public decimal? FuelIn
    {
        get => _fuelIn;
        set => Set(ref _fuelIn, value);
    }

    public decimal? OilIn
    {
        get => _oilIn;
        set => Set(ref _oilIn, value);
    }

    public decimal? FuelBalance
    {
        get => _fuelBalance;
        set => Set(ref _fuelBalance, value);
    }

    public string FuelBalanceLotTip
    {
        get => _fuelBalanceLotTip;
        set => Set(ref _fuelBalanceLotTip, value ?? "");
    }

    public decimal? OilBalance
    {
        get => _oilBalance;
        set => Set(ref _oilBalance, value);
    }

    public string OilBalanceLotTip
    {
        get => _oilBalanceLotTip;
        set => Set(ref _oilBalanceLotTip, value ?? "");
    }

    public decimal? RowTotal
    {
        get => _rowTotal;
        set => Set(ref _rowTotal, value);
    }

    public decimal MainHoursTotal
    {
        get
        {
            var sum = QuantityMath.RoundQty(HoursAtBerth + HoursCx25 + HoursCx50 + HoursCx75 + HoursCx100);
            var machines = QuantityMath.RoundQty(MainOpsCount);
            return QuantityMath.RoundQty(sum * machines);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;
    public event Action<ShipQuarterBookLineVm>? DateChanged;
    public event Action? RequestRender;

    private void Notify()
    {
        OnPropertyChanged(nameof(MainHoursTotal));
        Changed?.Invoke();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class ShipQuarterBookWindow : Window
{
    private const double RowHeight = 34;
    private static decimal MaxEditQty => QuantityMath.MaxEditQty;
    private static readonly double[] ColWidths =
    [
        72, 132, 134, 160, 64, 54, 54, 54, 54, 54, 54, 58, 54, 58, 66, 66, 66, 66, 66, 58, 58, 58, 80
    ];
    private static readonly Brush Head = Brush("#E8EEF5");
    private static readonly Brush SubHead = Brush("#F3F6FA");
    private static readonly Brush Line = Brush("#CBD5E1");
    private static readonly Brush TextBrush = Brush("#0F172A");
    private static readonly Brush OpeningBg = Brush("#F8FAFC");
    private static readonly Brush LotTxBg = Brush("#E8F0F8");      // TX — xanh nhạt
    private static readonly Brush LotSscdBg = Brush("#FFF6E8");    // SSCĐ — vàng nhạt
    private static readonly Brush LotIuuBg = Brush("#F5EAF5");     // IUU — tím nhạt
    private static readonly Brush LotOtherBg = Brush("#EEF2F5");
    private static readonly Brush ManualBg = Brush("#FFF7ED");
    private static readonly Brush LockedBg = Brush("#F1F5F9");

    public bool Saved { get; private set; }
    private bool _suppressQtyCommit;
    private bool _sizedToContent;

    public ShipQuarterBookWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RenderBook();
        Loaded += (_, _) =>
        {
            RenderBook();
            SizeToBookContent();
        };
    }

    public static bool? Show(ShipQuarterBookPopupVm model)
    {
        var window = new ShipQuarterBookWindow { DataContext = model };
        if (Application.Current?.MainWindow is Window owner && owner.IsLoaded)
            window.Owner = owner;
        return window.ShowDialog();
    }

    private void SizeToBookContent()
    {
        if (_sizedToContent || BookHost is null || BookHost.Children.Count == 0)
            return;
        UpdateLayout();
        if (BookHost.Parent is not ScrollViewer sv)
            return;

        var tableW = 0d;
        foreach (var w in ColWidths)
            tableW += w;
        var tableH = 0d;
        foreach (UIElement child in BookHost.Children)
        {
            if (child is FrameworkElement fe)
                tableH += fe.Height > 0 ? fe.Height : RowHeight;
        }

        var frameW = Math.Max(40, ActualWidth - sv.ViewportWidth);
        var frameH = Math.Max(120, ActualHeight - sv.ViewportHeight);
        // Thanh cuộn dọc/ngang khi bị giới hạn bởi màn hình.
        const double scrollPad = 20;

        var work = SystemParameters.WorkArea;
        var maxW = Math.Max(MinWidth, work.Width - 24);
        var maxH = Math.Max(MinHeight, work.Height - 24);
        var targetW = Math.Clamp(tableW + frameW + scrollPad, MinWidth, maxW);
        var targetH = Math.Clamp(tableH + frameH + scrollPad, MinHeight, maxH);

        Width = targetW;
        Height = targetH;
        if (Owner is { IsLoaded: true } owner)
        {
            Left = owner.Left + Math.Max(0, (owner.ActualWidth - Width) / 2);
            Top = owner.Top + Math.Max(0, (owner.ActualHeight - Height) / 2);
        }
        else
        {
            Left = work.Left + Math.Max(0, (work.Width - Width) / 2);
            Top = work.Top + Math.Max(0, (work.Height - Height) / 2);
        }

        Left = Math.Min(Math.Max(Left, work.Left), work.Right - Width);
        Top = Math.Min(Math.Max(Top, work.Top), work.Bottom - Height);
        _sizedToContent = true;
    }

    private void ExportWordClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShipQuarterBookPopupVm model)
            return;
        try
        {
            RecalcBalances();
            var year = model.QuarterDate.Year;
            var quarter = (model.QuarterDate.Month - 1) / 3 + 1;
            var book = new ShipQuarterBookDto(
                null,
                model.ConsumerId,
                model.ShipName,
                model.ShipType,
                model.FuelUsed,
                model.FuelGroupName,
                model.FuelIsGasoline,
                model.FormId,
                model.UnitNote,
                year,
                quarter,
                model.DefaultMainMachines,
                model.DefaultAuxMachines,
                model.NormRates,
                model.Rows.Select(x =>
                {
                    decimal? fuelOut = null;
                    decimal? gas = null;
                    decimal? diesel = null;
                    if (!x.IsOpening)
                    {
                        if (x.IsTransfer)
                            fuelOut = null;
                        else
                        {
                            var resolved = ResolveFuelOut(x, model.NormRates);
                            fuelOut = resolved > 0 ? resolved : null;
                            if (fuelOut is > 0)
                            {
                                if (model.FuelIsGasoline) gas = fuelOut;
                                else diesel = fuelOut;
                            }
                        }
                    }

                    return new ShipQuarterBookRowDto(
                        x.IsOpening, x.IsTransfer, x.LineId, x.DocumentNumber, x.DocumentDate, x.Description,
                        NullZero(x.MainOpsCount), NullZero(x.HoursAtBerth), NullZero(x.HoursCx25),
                        NullZero(x.HoursCx50), NullZero(x.HoursCx75), NullZero(x.HoursCx100),
                        NullZero(x.MainHoursTotal), NullZero(x.AuxOpsCount), NullZero(x.AuxHours),
                        gas, diesel,
                        x.ManualFuelOut, x.ManualOilOut,
                        x.FuelIn, fuelOut, x.FuelBalance,
                        x.OilIn, NullZero(x.OilOut), x.OilBalance, x.RowTotal, x.LotTypeId, x.LotTypeCode,
                        x.FuelBalanceLotTip, x.OilBalanceLotTip);
                }).ToList());

            var bytes = Services.ExportUi.CreateService().ExportShipBookWord(book);
            Services.ExportUi.SaveBytes(bytes, $"so_tt_tau_{model.ShipName}_Q{quarter}_{year}.docx",
                "Word (*.docx)|*.docx", "docx");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Xuất Word", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void AddRowClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShipQuarterBookPopupVm model)
            return;
        var row = new ShipQuarterBookLineVm
        {
            DocumentDate = LastWorkingDate(model),
            Description = "",
            MainOpsCount = model.DefaultMainMachines,
            AuxOpsCount = model.DefaultAuxMachines,
            LotTypeId = SeedIds.LotTypeTx,
            LotTypeCode = "TX",
            AddOrder = ++model.NextAddOrder
        };
        row.Changed += OnRowChanged;
        row.DateChanged += OnEditableDateChanged;
        model.Rows.Add(row);
        SortAndRender();
    }

    private void OnRowChanged()
    {
        RecalcBalances();
    }

    private void OnEditableDateChanged(ShipQuarterBookLineVm row)
    {
        if (DataContext is ShipQuarterBookPopupVm model)
            row.AddOrder = ++model.NextAddOrder;
        SortAndRender();
    }

    private void SortAndRender()
    {
        SortRowsChronologically();
        RenderBook();
    }

    private static DateTime LastWorkingDate(ShipQuarterBookPopupVm model)
    {
        for (var i = model.Rows.Count - 1; i >= 0; i--)
        {
            var r = model.Rows[i];
            if (!r.IsOpening && r.DocumentDate is DateTime d)
                return d.Date;
        }

        return model.QuarterDate.Date;
    }

    private void SortRowsChronologically()
    {
        if (DataContext is not ShipQuarterBookPopupVm model)
            return;
        var opening = model.Rows.Where(x => x.IsOpening).ToList();
        var rest = model.Rows
            .Where(x => !x.IsOpening)
            .OrderBy(x => x.DocumentDate?.Date ?? DateTime.MaxValue)
            .ThenBy(x => x.IsTransfer ? 0 : 1) // cùng ngày: phiếu ĐC trước
            .ThenBy(x => x.AddOrder) // dòng thêm / vừa đổi ngày ở dưới cùng ngày đó
            .ThenBy(x => x.DocumentNumber, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        model.Rows.Clear();
        foreach (var row in opening.Concat(rest))
            model.Rows.Add(row);
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShipQuarterBookPopupVm model)
            return;
        RecalcBalances();
        model.PendingLines = model.Rows
            .Where(x => x.IsEditable)
            .Select(x => new ShipQuarterBookLineEdit
            {
                LineId = x.LineId,
                DocumentNumber = x.DocumentNumber,
                DocumentDate = x.DocumentDate,
                Description = x.Description,
                MainOpsCount = x.MainOpsCount,
                HoursAtBerth = x.HoursAtBerth,
                HoursCx25 = x.HoursCx25,
                HoursCx50 = x.HoursCx50,
                HoursCx75 = x.HoursCx75,
                HoursCx100 = x.HoursCx100,
                AuxOpsCount = x.AuxOpsCount,
                AuxHours = x.AuxHours,
                ManualFuelOut = x.ManualFuelOut || x.ManualOilOut || x.PendingRecalculate,
                FuelOutManual = (x.ManualFuelOut || x.ManualOilOut || x.PendingRecalculate) ? x.FuelOutManual : null,
                ManualOilOut = x.ManualFuelOut || x.ManualOilOut || x.PendingRecalculate,
                OilOut = x.OilOut,
                LotTypeId = x.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : x.LotTypeId,
                LotTypeCode = string.IsNullOrWhiteSpace(x.LotTypeCode) ? "TX" : x.LotTypeCode,
                MissionTaskId = x.MissionTaskId
            })
            .ToList();
        Saved = true;
        DialogResult = true;
    }

    private void RenderBook()
    {
        if (BookHost is null)
            return;
        Keyboard.ClearFocus();
        _suppressQtyCommit = true;
        try
        {
            BookHost.Children.Clear();
            if (DataContext is not ShipQuarterBookPopupVm model)
                return;

            foreach (var row in model.Rows)
            {
                row.Changed -= OnRowChanged;
                row.DateChanged -= OnEditableDateChanged;
                row.RequestRender -= RenderBook;
            }

            foreach (var row in model.Rows.Where(x => x.IsEditable))
            {
                // Đồng bộ cờ cũ: một chế độ cho cả Xuất NL và dầu mỡ.
                var manual = row.ManualFuelOut || row.ManualOilOut;
                if (row.ManualFuelOut != manual || row.ManualOilOut != manual)
                    row.SyncManualMode(manual);

                if (!manual && !row.PendingRecalculate)
                {
                    var formulaFuel = CalculateFormulaFuelOut(row, model.NormRates);
                    var formulaOil = AutoOilOut(formulaFuel);
                    var currentFuel = row.FuelOutManual > 0 ? row.FuelOutManual : formulaFuel;
                    var currentOil = row.OilOut > 0 ? row.OilOut : formulaOil;
                    if (currentFuel != formulaFuel || currentOil != formulaOil)
                    {
                        row.PendingRecalculate = true;
                    }
                }

                row.Changed += OnRowChanged;
                row.DateChanged += OnEditableDateChanged;
                row.RequestRender += RenderBook;
            }

            RecalcBalances();
            BookHost.Children.Add(BuildHeader());
            foreach (var row in model.Rows)
                BookHost.Children.Add(BuildRow(model, row));
            BookHost.Children.Add(BuildTotalsRow(model));
        }
        finally
        {
            _suppressQtyCommit = false;
        }
    }

    private void RecalcBalances()
    {
        if (DataContext is not ShipQuarterBookPopupVm model)
            return;
        try
        {
            decimal fuel = 0;
            decimal oil = 0;
            var fuelLots = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var oilLots = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in model.Rows)
            {
                if (row.IsOpening)
                {
                    fuel = QuantityMath.Whole(row.FuelBalance ?? 0);
                    oil = QuantityMath.RoundQty(row.OilBalance ?? 0);
                    fuelLots = LotBalanceTips.Parse(row.FuelBalanceLotTip);
                    oilLots = LotBalanceTips.Parse(row.OilBalanceLotTip);
                    if (fuelLots.Count == 0 && fuel != 0)
                        LotBalanceTips.Add(fuelLots, "TX", fuel);
                    if (oilLots.Count == 0 && oil != 0)
                        LotBalanceTips.Add(oilLots, "TX", oil);
                    row.FuelBalanceLotTip = LotBalanceTips.Format(fuelLots);
                    row.OilBalanceLotTip = LotBalanceTips.Format(oilLots);
                    continue;
                }

                if (row.IsTransfer)
                {
                    var fuelIn = row.FuelIn ?? 0;
                    var oilIn = row.OilIn ?? 0;
                    fuel = AddQty(fuel, fuelIn);
                    oil = AddQty(oil, oilIn);
                    ApplyTransferLotTips(fuelLots, oilLots, row);
                    row.FuelBalance = fuel;
                    row.OilBalance = oil;
                    row.FuelBalanceLotTip = LotBalanceTips.Format(fuelLots);
                    row.OilBalanceLotTip = LotBalanceTips.Format(oilLots);
                    continue;
                }

                var fuelOut = ResolveFuelOut(row, model.NormRates);
                if (!row.ManualOilOut && !row.PendingRecalculate)
                    row.SetOilOutAuto(AutoOilOut(fuelOut));
                var oilOut = ClampQty(QuantityMath.Whole(row.OilOut > 0 ? row.OilOut : AutoOilOut(fuelOut)));
                fuel = QuantityMath.Whole(fuel - fuelOut);
                oil = QuantityMath.Whole(oil - oilOut);
                LotBalanceTips.Add(fuelLots, row.LotTypeCode, -fuelOut);
                LotBalanceTips.Add(oilLots, row.LotTypeCode, -oilOut);
                row.FuelBalance = fuel;
                row.OilBalance = oil;
                row.FuelBalanceLotTip = LotBalanceTips.Format(fuelLots);
                row.OilBalanceLotTip = LotBalanceTips.Format(oilLots);
            }

            RebuildDataCells();
            if (BookHost is not null && BookHost.Children.Count == model.Rows.Count + 2)
            {
                BookHost.Children.RemoveAt(BookHost.Children.Count - 1);
                BookHost.Children.Add(BuildTotalsRow(model));
            }
        }
        catch (OverflowException)
        {
            // Giữ số liệu đã clamp — tránh crash khi người dùng nhập số quá lớn.
        }
    }

    private static void ApplyTransferLotTips(
        Dictionary<string, decimal> fuelLots,
        Dictionary<string, decimal> oilLots,
        ShipQuarterBookLineVm row)
    {
        var codes = (row.LotTypeCode ?? "TX")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (codes.Length == 0)
            codes = ["TX"];
        var fuelIn = row.FuelIn ?? 0;
        var oilIn = row.OilIn ?? 0;
        // ĐC có thể ghi nhiều mã (TX,SSCĐ): gán toàn bộ vào mã đầu, các mã còn lại hiện 0 trong tip nếu đã có từ trước.
        LotBalanceTips.Add(fuelLots, codes[0], fuelIn);
        LotBalanceTips.Add(oilLots, codes[0], oilIn);
    }

    private static decimal AutoOilOut(decimal fuelOut)
    {
        var fuel = QuantityMath.Whole(fuelOut);
        if (fuel <= 0)
            return 0;
        // 4% × NL, làm tròn lên thành lít nguyên (không thập phân).
        try
        {
            var raw = MulQty(fuel, ShipNormSlots.OilPerFuelRatio);
            return raw <= 0 ? 0 : ClampQty(decimal.Ceiling(raw));
        }
        catch (OverflowException)
        {
            return MaxEditQty;
        }
    }

    private static decimal OilLiters(decimal value)
    {
        var qty = QuantityMath.RoundQty(value);
        return qty <= 0 ? 0 : decimal.Ceiling(qty);
    }

    private static decimal? NullZero(decimal value) => value == 0 ? null : value;

    private void RebuildDataCells()
    {
        if (BookHost is null || DataContext is not ShipQuarterBookPopupVm model)
            return;
        // Header + rows: skip header (index 0), refresh balance/fuel-out display cells by re-render when few rows.
        // Header + data rows + totals footer
        if (BookHost.Children.Count != model.Rows.Count + 2)
            return;
        for (var i = 0; i < model.Rows.Count; i++)
        {
            if (BookHost.Children[i + 1] is not Grid grid)
                continue;
            var row = model.Rows[i];
            var fuelOut = row.IsEditable ? ResolveFuelOut(row, model.NormRates) : (decimal?)null;
            var gas = model.FuelIsGasoline && fuelOut is > 0 ? fuelOut : null;
            var diesel = !model.FuelIsGasoline && fuelOut is > 0 ? fuelOut : null;
            SetCellText(grid, 14, FormatQty(gas));
            SetCellText(grid, 15, FormatQty(diesel));
            if (row.IsEditable)
            {
                SetCellText(grid, 11, FormatHour(row.MainHoursTotal == 0 ? null : row.MainHoursTotal));
                if (!row.ManualFuelOut)
                    SetCellText(grid, 17, FormatQty(fuelOut));
                if (!row.ManualOilOut)
                {
                    var oil = row.PendingRecalculate ? row.OilOut : AutoOilOut(fuelOut ?? 0);
                    SetCellText(grid, 20, FormatQty(oil == 0 ? null : oil));
                }
                SetBalanceCell(grid, 18, FormatQty(row.FuelBalance), LotTip("Nhiên liệu", row.FuelBalanceLotTip));
                SetBalanceCell(grid, 21, FormatQty(row.OilBalance), LotTip("Dầu mỡ", row.OilBalanceLotTip));
            }
            else
            {
                SetBalanceCell(grid, 18, FormatQty(row.FuelBalance), LotTip("Nhiên liệu", row.FuelBalanceLotTip));
                SetBalanceCell(grid, 21, FormatQty(row.OilBalance), LotTip("Dầu mỡ", row.OilBalanceLotTip));
            }
        }
    }

    private static string? LotTip(string title, string tip) =>
        string.IsNullOrWhiteSpace(tip) ? null : $"Số lượng tổng theo loại lô — {title}\n{tip}";

    private static void SetBalanceCell(Grid grid, int column, string text, string? toolTip)
    {
        SetCellText(grid, column, text);
        foreach (UIElement child in grid.Children)
        {
            if (Grid.GetColumn(child) != column || child is not Border border)
                continue;
            ApplyLotTip(border, toolTip);
            break;
        }
    }

    private static void SetCellText(Grid grid, int column, string text)
    {
        foreach (UIElement child in grid.Children)
        {
            if (Grid.GetColumn(child) != column || child is not Border border)
                continue;
            if (border.Child is TextBlock block)
                block.Text = text;
            else if (border.Child is TextBox box)
                box.Text = text;
            else if (border.Child is StackPanel panel)
            {
                foreach (UIElement pChild in panel.Children)
                {
                    if (pChild is TextBlock pBlock)
                        pBlock.Text = text;
                }
            }
            else if (border.Child is DatePicker)
            {
                // Ngày giữ SelectedDate trên DatePicker — không ghi đè bằng text tồn.
            }
            break;
        }
    }

    private static void ApplyLotTip(FrameworkElement element, string? toolTip)
    {
        if (string.IsNullOrWhiteSpace(toolTip))
        {
            element.ToolTip = null;
            return;
        }

        element.ToolTip = new ToolTip
        {
            Content = new TextBlock
            {
                Text = toolTip,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 280
            }
        };
    }

    public static decimal CalculateFormulaFuelOut(ShipQuarterBookLineVm row, IReadOnlyDictionary<string, decimal> rates)
    {
        try
        {
            var mainMachines = QuantityMath.RoundQty(row.MainOpsCount);
            decimal total = 0;
            total = AddQty(total, SlotFuel(row.HoursAtBerth, mainMachines, rates, ShipNormSlots.AtBerth));
            total = AddQty(total, SlotFuel(row.HoursCx25, mainMachines, rates, ShipNormSlots.Cx25));
            total = AddQty(total, SlotFuel(row.HoursCx50, mainMachines, rates, ShipNormSlots.Cx50));
            total = AddQty(total, SlotFuel(row.HoursCx75, mainMachines, rates, ShipNormSlots.Cx75));
            total = AddQty(total, SlotFuel(row.HoursCx100, mainMachines, rates, ShipNormSlots.Cx100));
            var auxMachines = QuantityMath.RoundQty(row.AuxOpsCount);
            total = AddQty(total, SlotFuel(row.AuxHours, auxMachines, rates, ShipNormSlots.Aux));
            return ClampQty(QuantityMath.Whole(Math.Round(total, 0, MidpointRounding.AwayFromZero)));
        }
        catch (OverflowException)
        {
            return MaxEditQty;
        }
    }

    private static decimal ResolveFuelOut(ShipQuarterBookLineVm row, IReadOnlyDictionary<string, decimal> rates)
    {
        if (row.ManualFuelOut || row.PendingRecalculate)
            return ClampQty(QuantityMath.Whole(row.FuelOutManual));
        return CalculateFormulaFuelOut(row, rates);
    }

    private static decimal SlotFuel(decimal hours, decimal machines, IReadOnlyDictionary<string, decimal> rates, string key)
    {
        hours = QuantityMath.RoundQty(hours);
        machines = QuantityMath.RoundQty(machines);
        if (hours <= 0 || machines <= 0)
            return 0;
        rates.TryGetValue(key, out var rate);
        if (rate <= 0)
            return 0;
        return MulQty(MulQty(hours, machines), QuantityMath.RoundNorm(rate));
    }

    private static decimal ClampQty(decimal value) => QuantityMath.ClampEditQty(value);

    private static decimal AddQty(decimal left, decimal right)
    {
        try
        {
            return ClampQty(left + right);
        }
        catch (OverflowException)
        {
            return MaxEditQty;
        }
    }

    private static decimal MulQty(decimal left, decimal right)
    {
        if (left == 0 || right == 0)
            return 0;
        try
        {
            return ClampQty(left * right);
        }
        catch (OverflowException)
        {
            return MaxEditQty;
        }
    }

    private static Grid BuildHeader()
    {
        var grid = new Grid { Height = RowHeight * 3 };
        foreach (var width in ColWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        for (var i = 0; i < 3; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });

        AddMerge(grid, 0, 0, 2, 1, "Chứng từ", Head);
        AddCell(grid, 0, 1, "Số", SubHead, true, 2);
        AddCell(grid, 1, 1, "Ngày", SubHead, true, 2);

        AddSpan(grid, 2, "Diễn giải (tính chất xuất)", Head, 3);
        AddSpan(grid, 3, "Nhiệm vụ", Head, 3);
        AddSpan(grid, 4, "Loại lô", Head, 3);

        AddMerge(grid, 5, 0, 7, 1, "Hoạt động máy chính", Head);
        AddCell(grid, 5, 1, "Số máy", SubHead, true, 2);
        AddMerge(grid, 6, 1, 5, 1, "Số giờ hoạt động", SubHead);
        AddCell(grid, 6, 2, "Tại bến", SubHead, true);
        AddCell(grid, 7, 2, "25%", SubHead, true);
        AddCell(grid, 8, 2, "50%", SubHead, true);
        AddCell(grid, 9, 2, "75%", SubHead, true);
        AddCell(grid, 10, 2, "100%", SubHead, true);
        AddCell(grid, 11, 1, "Cộng giờ×máy", SubHead, true, 2);

        AddMerge(grid, 12, 0, 2, 1, "Máy phụ", Head);
        AddCell(grid, 12, 1, "Số máy", SubHead, true, 2);
        AddCell(grid, 13, 1, "Giờ HĐ", SubHead, true, 2);

        AddMerge(grid, 14, 0, 2, 1, "Nhiên liệu tiêu thụ", Head);
        AddCell(grid, 14, 1, "Xăng", SubHead, true, 2);
        AddCell(grid, 15, 1, "Diesel", SubHead, true, 2);

        AddMerge(grid, 16, 0, 3, 1, "Nhiên liệu", Head);
        AddCell(grid, 16, 1, "Nhập", SubHead, true, 2);
        AddCell(grid, 17, 1, "Xuất", SubHead, true, 2);
        AddCell(grid, 18, 1, "Tồn", SubHead, true, 2);

        AddMerge(grid, 19, 0, 3, 1, "Dầu mỡ", Head);
        AddCell(grid, 19, 1, "Nhập", SubHead, true, 2);
        AddCell(grid, 20, 1, "Xuất", SubHead, true, 2);
        AddCell(grid, 21, 1, "Tồn", SubHead, true, 2);
        AddSpan(grid, 22, "", Head, 3);
        return grid;
    }

    private Grid BuildTotalsRow(ShipQuarterBookPopupVm model)
    {
        decimal berth = 0, cx25 = 0, cx50 = 0, cx75 = 0, cx100 = 0, mainHours = 0;
        decimal auxHours = 0, gas = 0, diesel = 0;
        decimal fuelIn = 0, fuelOut = 0, oilIn = 0, oilOut = 0;
        foreach (var row in model.Rows)
        {
            if (row.IsOpening)
                continue;
            fuelIn = QuantityMath.Whole(AddQty(fuelIn, row.FuelIn ?? 0));
            oilIn = QuantityMath.RoundQty(AddQty(oilIn, row.OilIn ?? 0));
            if (!row.IsEditable)
                continue;
            berth = QuantityMath.RoundQty(AddQty(berth, row.HoursAtBerth));
            cx25 = QuantityMath.RoundQty(AddQty(cx25, row.HoursCx25));
            cx50 = QuantityMath.RoundQty(AddQty(cx50, row.HoursCx50));
            cx75 = QuantityMath.RoundQty(AddQty(cx75, row.HoursCx75));
            cx100 = QuantityMath.RoundQty(AddQty(cx100, row.HoursCx100));
            mainHours = QuantityMath.RoundQty(AddQty(mainHours, row.MainHoursTotal));
            auxHours = QuantityMath.RoundQty(AddQty(auxHours, row.AuxHours));
            var fo = ResolveFuelOut(row, model.NormRates);
            fuelOut = QuantityMath.Whole(AddQty(fuelOut, fo));
            var oo = row.ManualOilOut || row.PendingRecalculate ? row.OilOut : (row.OilOut > 0 ? row.OilOut : AutoOilOut(fo));
            oilOut = QuantityMath.Whole(AddQty(oilOut, oo));
            if (model.FuelIsGasoline)
                gas = QuantityMath.Whole(AddQty(gas, fo));
            else
                diesel = QuantityMath.Whole(AddQty(diesel, fo));
        }

        var grid = new Grid { Height = RowHeight, Background = Head };
        foreach (var width in ColWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        AddData(grid, 0, "", false);
        AddData(grid, 1, "", false);
        AddData(grid, 2, "Cộng", false);
        AddData(grid, 3, "", false);
        AddData(grid, 4, "", false);
        AddData(grid, 5, "", true);
        AddData(grid, 6, FormatHour(NullZero(berth)), true);
        AddData(grid, 7, FormatHour(NullZero(cx25)), true);
        AddData(grid, 8, FormatHour(NullZero(cx50)), true);
        AddData(grid, 9, FormatHour(NullZero(cx75)), true);
        AddData(grid, 10, FormatHour(NullZero(cx100)), true);
        AddData(grid, 11, FormatHour(NullZero(mainHours)), true);
        AddData(grid, 12, "", true);
        AddData(grid, 13, FormatHour(NullZero(auxHours)), true);
        AddData(grid, 14, FormatQty(NullZero(gas)), true);
        AddData(grid, 15, FormatQty(NullZero(diesel)), true);
        AddData(grid, 16, FormatQty(NullZero(fuelIn)), true);
        AddData(grid, 17, FormatQty(NullZero(fuelOut)), true);
        AddData(grid, 18, "", true);
        AddData(grid, 19, FormatQty(NullZero(oilIn)), true);
        AddData(grid, 20, FormatQty(NullZero(oilOut)), true);
        AddData(grid, 21, "", true);
        AddData(grid, 22, "", true);
        return grid;
    }

    private Grid BuildRow(ShipQuarterBookPopupVm model, ShipQuarterBookLineVm row)
    {
        var bg = row.IsOpening ? OpeningBg : LotTypeBackground(row.LotTypeCode);
        var grid = new Grid { Height = RowHeight, Background = bg };
        foreach (var width in ColWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

        var fuelOut = row.IsEditable ? ResolveFuelOut(row, model.NormRates) : (decimal?)null;
        var gas = model.FuelIsGasoline && fuelOut is > 0 ? fuelOut : null;
        var diesel = !model.FuelIsGasoline && fuelOut is > 0 ? fuelOut : null;

        if (!row.IsEditable)
        {
            AddData(grid, 0, row.DocumentNumber, false);
            AddData(grid, 1, FormatDate(row.DocumentDate), false);
            AddData(grid, 2, row.Description, false);
            AddData(grid, 3, row.IsOpening ? "" : row.MissionTaskName, false);
            AddData(grid, 4, row.IsOpening ? "" : row.LotTypeCode, true);
            for (var i = 5; i <= 15; i++)
                AddData(grid, i, "", true);
            AddData(grid, 16, FormatQty(row.FuelIn), true);
            AddData(grid, 17, "", true);
            AddData(grid, 18, FormatQty(row.FuelBalance), true, LotTip("Nhiên liệu", row.FuelBalanceLotTip));
            AddData(grid, 19, FormatQty(row.OilIn), true);
            AddData(grid, 20, "", true);
            AddData(grid, 21, FormatQty(row.OilBalance), true, LotTip("Dầu mỡ", row.OilBalanceLotTip));
            AddData(grid, 22, "", true);
            return grid;
        }

        AddEdit(grid, 0, row.DocumentNumber, false, text => row.DocumentNumber = text);
        AddDateEdit(grid, 1, row.DocumentDate, d => row.DocumentDate = d);
        AddEdit(grid, 2, row.Description, false, text => row.Description = text);
        AddMissionCell(grid, 3, model, row);
        AddLotTypeCell(grid, 4, model, row);
        AddLockedData(grid, 5, FormatQty(row.MainOpsCount == 0 ? null : row.MainOpsCount), true);
        AddHourEdit(grid, 6, row.HoursAtBerth, v => row.HoursAtBerth = v);
        AddHourEdit(grid, 7, row.HoursCx25, v => row.HoursCx25 = v);
        AddHourEdit(grid, 8, row.HoursCx50, v => row.HoursCx50 = v);
        AddHourEdit(grid, 9, row.HoursCx75, v => row.HoursCx75 = v);
        AddHourEdit(grid, 10, row.HoursCx100, v => row.HoursCx100 = v);
        AddData(grid, 11, FormatHour(row.MainHoursTotal == 0 ? null : row.MainHoursTotal), true);
        AddLockedData(grid, 12, FormatQty(row.AuxOpsCount == 0 ? null : row.AuxOpsCount), true);
        AddHourEdit(grid, 13, row.AuxHours, v => row.AuxHours = v);
        AddData(grid, 14, FormatQty(gas), true);
        AddData(grid, 15, FormatQty(diesel), true);
        AddData(grid, 16, "", true);
        // Một chế độ chung: tự tính = khóa cả Xuất NL + dầu mỡ; thủ công = mở cả hai.
        var manual = row.ManualFuelOut;
        if (manual)
        {
            var currentOil = row.OilOut > 0 ? row.OilOut : AutoOilOut(row.FuelOutManual);
            if (row.OilOut == 0 && currentOil > 0)
                row.OilOut = currentOil;
            AddQtyEdit(grid, 17, row.FuelOutManual, v => row.FuelOutManual = v, ManualBg);
            AddQtyEdit(grid, 20, row.OilOut, v => row.OilOut = OilLiters(v), ManualBg);
        }
        else
        {
            var formulaFuel = CalculateFormulaFuelOut(row, model.NormRates);
            var formulaOil = AutoOilOut(formulaFuel);
            var currentOil = (!row.PendingRecalculate) ? AutoOilOut(fuelOut ?? 0) : (row.OilOut > 0 ? row.OilOut : AutoOilOut(fuelOut ?? 0));
            if (!row.PendingRecalculate && row.OilOut != currentOil)
                row.SetOilOutAuto(currentOil);

            var fuelDiffers = row.PendingRecalculate && (fuelOut != formulaFuel);
            var oilDiffers = row.PendingRecalculate && (currentOil != formulaOil);

            if (fuelDiffers)
                AddLockedDataWithRecalc(grid, 17, FormatQty(fuelOut), row, model, formulaFuel, "lít NL");
            else
                AddLockedData(grid, 17, FormatQty(fuelOut), true);

            if (oilDiffers)
                AddLockedDataWithRecalc(grid, 20, FormatQty(currentOil == 0 ? null : currentOil), row, model, formulaOil, "lít dầu");
            else
                AddLockedData(grid, 20, FormatQty(currentOil == 0 ? null : currentOil), true);
        }

        AddData(grid, 18, FormatQty(row.FuelBalance), true, LotTip("Nhiên liệu", row.FuelBalanceLotTip));
        AddData(grid, 19, "", true);
        AddData(grid, 21, FormatQty(row.OilBalance), true, LotTip("Dầu mỡ", row.OilBalanceLotTip));
        AddRowActions(grid, 22, row);

        grid.PreviewMouseRightButtonUp += (_, e) =>
        {
            Keyboard.ClearFocus();
            ToggleRowManual(row, model);
            e.Handled = true;
        };
        grid.ToolTip = manual
            ? "Chế độ thủ công — chuột phải dòng để về tự tính (Xuất NL + dầu mỡ)."
            : (row.PendingRecalculate
                ? "Chế độ tự động (đang giữ số liệu thủ công chưa khớp công thức) — bấm 🔄 để tính lại hoặc nhập mới ở các ô giờ/máy."
                : "Chế độ tự tính (khóa Xuất NL + dầu mỡ) — chuột phải dòng để nhập tay.");

        return grid;
    }

    private void AddMissionCell(Grid grid, int column, ShipQuarterBookPopupVm model, ShipQuarterBookLineVm row)
    {
        if (model.MissionTasks.Count == 0)
        {
            AddData(grid, column, row.MissionTaskName, false);
            return;
        }

        var combo = new Controls.SafeComboBox
        {
            ItemsSource = model.MissionTasks,
            DisplayMemberPath = nameof(MissionTaskRow.Display),
            IsEditable = false,
            SelectedItem = model.MissionTasks.FirstOrDefault(x => x.Id == row.MissionTaskId)
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not MissionTaskRow selected)
            {
                row.MissionTaskId = null;
                row.MissionTaskName = "";
                return;
            }

            row.MissionTaskId = selected.Id;
            row.MissionTaskName = selected.Display;
        };
        var host = new Border
        {
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Width = ColWidths[column],
            Child = combo
        };
        Grid.SetColumn(host, column);
        grid.Children.Add(host);
    }

    private static Brush LotTypeBackground(string? code) =>
        (code ?? "").Trim().ToUpperInvariant() switch
        {
            "TX" => LotTxBg,
            "SSCĐ" or "SSCD" => LotSscdBg,
            "IUU" => LotIuuBg,
            _ => LotOtherBg
        };

    private void ToggleRowManual(ShipQuarterBookLineVm row, ShipQuarterBookPopupVm model)
    {
        if (row.ManualFuelOut)
        {
            var formulaFuel = CalculateFormulaFuelOut(row, model.NormRates);
            var formulaOil = AutoOilOut(formulaFuel);
            var currentOil = row.OilOut > 0 ? row.OilOut : AutoOilOut(row.FuelOutManual);
            row.OilOut = currentOil;
            row.SyncManualMode(false);
            if (row.FuelOutManual != formulaFuel || currentOil != formulaOil)
            {
                row.PendingRecalculate = true;
            }
            else
            {
                row.PendingRecalculate = false;
                row.SetOilOutAuto(formulaOil);
            }
        }
        else
        {
            var fuel = ResolveFuelOut(row, model.NormRates);
            var oil = row.OilOut > 0 ? row.OilOut : AutoOilOut(fuel);
            row.FuelOutManual = fuel;
            row.OilOut = oil;
            row.PendingRecalculate = false;
            row.SyncManualMode(true);
        }

        RenderBook();
    }

    private void ApplyFormulaRecalc(ShipQuarterBookLineVm row, ShipQuarterBookPopupVm model)
    {
        var formulaFuel = CalculateFormulaFuelOut(row, model.NormRates);
        var formulaOil = AutoOilOut(formulaFuel);
        row.PendingRecalculate = false;
        row.FuelOutManual = formulaFuel;
        row.SetOilOutAuto(formulaOil);
        RecalcBalances();
        RenderBook();
    }

    private void AddLotTypeCell(Grid grid, int column, ShipQuarterBookPopupVm model, ShipQuarterBookLineVm row)
    {
        if (row.LotTypeLocked || model.LotTypes.Count == 0)
        {
            AddData(grid, column, row.IsOpening ? "" : row.LotTypeCode, true);
            return;
        }

        var combo = new Controls.SafeComboBox
        {
            ItemsSource = model.LotTypes,
            DisplayMemberPath = nameof(LotTypeRow.Code),
            IsEditable = false,
            IsTextSearchEnabled = false,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 11,
            SelectedItem = model.LotTypes.FirstOrDefault(x => x.Id == row.LotTypeId)
                ?? model.LotTypes.FirstOrDefault(x => x.Id == SeedIds.LotTypeTx)
                ?? model.LotTypes.FirstOrDefault()
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not LotTypeRow selected)
                return;
            row.LotTypeId = selected.Id;
            row.LotTypeCode = selected.Code;
            grid.Background = LotTypeBackground(selected.Code);
            RecalcBalances();
        };
        var cell = new Border
        {
            Width = ColWidths[column],
            Height = RowHeight,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 1, 1),
            ClipToBounds = true,
            Child = combo
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private void AddRowActions(Grid grid, int column, ShipQuarterBookLineVm row)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 1, 0)
        };
        if (row.ManualFuelOut)
            panel.Children.Add(ModeBadge("Tay", "#C2410C", "Đang thủ công"));

        var btn = new Button
        {
            Content = "✕",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            Width = 18,
            Height = RowHeight - 6,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Xóa dòng tiêu thụ",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brush("#B91C1C"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        btn.Click += (_, _) => DeleteRow(row);
        panel.Children.Add(btn);

        var cell = new Border
        {
            Width = ColWidths[column],
            Height = RowHeight,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = panel
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static Border ModeBadge(string text, string hex, string tip) =>
        new()
        {
            Background = Brush(hex),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(1, 0, 1, 0),
            ToolTip = tip,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

    private void DeleteRow(ShipQuarterBookLineVm row)
    {
        if (DataContext is not ShipQuarterBookPopupVm model || !row.IsEditable)
            return;
        row.Changed -= OnRowChanged;
        row.DateChanged -= OnEditableDateChanged;
        model.Rows.Remove(row);
        RenderBook();
    }

    private static void AddSpan(Grid grid, int column, string text, Brush background, int rowSpan)
    {
        var cell = Box(text, ColWidths[column], RowHeight * rowSpan, background, true);
        Grid.SetColumn(cell, column);
        Grid.SetRowSpan(cell, rowSpan);
        grid.Children.Add(cell);
    }

    private static void AddMerge(Grid grid, int column, int row, int span, int rowSpan, string text, Brush background)
    {
        var width = 0d;
        for (var i = 0; i < span; i++)
            width += ColWidths[column + i];
        var cell = Box(text, width, RowHeight * rowSpan, background, true);
        Grid.SetColumn(cell, column);
        Grid.SetRow(cell, row);
        Grid.SetColumnSpan(cell, span);
        if (rowSpan > 1)
            Grid.SetRowSpan(cell, rowSpan);
        grid.Children.Add(cell);
    }

    private static void AddCell(Grid grid, int column, int row, string text, Brush background, bool center, int rowSpan = 1)
    {
        var cell = Box(text, ColWidths[column], RowHeight * rowSpan, background, center);
        Grid.SetColumn(cell, column);
        Grid.SetRow(cell, row);
        if (rowSpan > 1)
            Grid.SetRowSpan(cell, rowSpan);
        grid.Children.Add(cell);
    }

    private static void AddData(Grid grid, int column, string text, bool right, string? toolTip = null)
    {
        var cell = Box(text, ColWidths[column], RowHeight, Brushes.Transparent, !right);
        if (cell.Child is TextBlock block)
        {
            block.FontWeight = FontWeights.Normal;
            block.TextAlignment = right ? TextAlignment.Right : TextAlignment.Left;
            block.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            if (right)
                block.Margin = new Thickness(2, 0, 4, 0);
        }

        ApplyLotTip(cell, toolTip);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static void AddLockedData(Grid grid, int column, string text, bool right)
    {
        var cell = Box(text, ColWidths[column], RowHeight, LockedBg, !right);
        if (cell.Child is TextBlock block)
        {
            block.FontWeight = FontWeights.Normal;
            block.Foreground = Brush("#64748B");
            block.TextAlignment = right ? TextAlignment.Right : TextAlignment.Left;
            block.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            if (right)
                block.Margin = new Thickness(2, 0, 4, 0);
        }

        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private void AddLockedDataWithRecalc(
        Grid grid, int column, string text, ShipQuarterBookLineVm row, ShipQuarterBookPopupVm model, decimal formulaQty, string unit)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0)
        };

        var btnRecalc = new Button
        {
            Content = "🔄",
            FontSize = 10,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 2, 0),
            Width = 16,
            Height = RowHeight - 8,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = $"Dữ liệu chưa khớp công thức (Công thức: {FormatQty(formulaQty)} {unit}). Nhấp để tính lại theo công thức.",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brush("#D97706"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        btnRecalc.Click += (_, e) =>
        {
            e.Handled = true;
            ApplyFormulaRecalc(row, model);
        };

        var block = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = Brush("#64748B"),
            FontWeight = FontWeights.Normal,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(btnRecalc);
        panel.Children.Add(block);

        var cell = new Border
        {
            Width = ColWidths[column],
            Height = RowHeight,
            Background = LockedBg,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 1, 1),
            ClipToBounds = true,
            Child = panel
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private void AddEdit(Grid grid, int column, string value, bool right, Action<string> set)
    {
        var box = new TextBox
        {
            Text = value,
            FontSize = 11,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 0, 2, 0),
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
            MaxWidth = ColWidths[column] - 2,
            TextWrapping = TextWrapping.NoWrap
        };
        var isDetached = false;
        box.Unloaded += (_, _) => isDetached = true;

        box.TextChanged += (_, _) =>
        {
            if (_suppressQtyCommit || isDetached)
                return;
            set(box.Text);
        };
        box.LostFocus += (_, _) =>
        {
            if (_suppressQtyCommit || isDetached)
                return;
            set(box.Text);
        };
        box.KeyUp += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                if (_suppressQtyCommit || isDetached)
                    return;
                set(box.Text);
            }
        };
        var cell = new Border
        {
            Width = ColWidths[column],
            Height = RowHeight,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 1, 1),
            ClipToBounds = true,
            Child = box
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private void AddDateEdit(Grid grid, int column, DateTime? value, Action<DateTime?> set)
    {
        var picker = new DatePicker
        {
            SelectedDate = value,
            FontSize = 11,
            MinHeight = 0,
            Height = RowHeight - 2,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            SelectedDateFormat = DatePickerFormat.Short,
            MaxWidth = ColWidths[column] - 2
        };
        var current = value?.Date;
        picker.SelectedDateChanged += (_, _) =>
        {
            var next = picker.SelectedDate?.Date;
            if (next == current)
                return;
            current = next;
            set(next);
        };
        var cell = new Border
        {
            Width = ColWidths[column],
            Height = RowHeight,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 1, 1),
            ClipToBounds = true,
            Child = picker
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private void AddQtyEdit(Grid grid, int column, decimal value, Action<decimal> set, Brush? background = null)
    {
        var box = new TextBox
        {
            Text = FormatQty(value == 0 ? null : value),
            FontSize = 11,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 0, 4, 0),
            TextAlignment = TextAlignment.Right,
            MaxWidth = ColWidths[column] - 2,
            MaxLength = 24,
            TextWrapping = TextWrapping.NoWrap
        };
        Controls.NumericFormat.SetKind(box, NumericKind.Decimal);

        var isDetached = false;
        box.Unloaded += (_, _) => isDetached = true;

        void Commit()
        {
            if (_suppressQtyCommit || isDetached)
                return;
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                set(0);
                box.Text = "";
                return;
            }

            if (!Numbers.Try(box.Text, out var qty))
                return;
            qty = ClampQty(QuantityMath.RoundQty(qty));
            set(qty);
            box.Text = FormatQty(qty == 0 ? null : qty);
        }

        box.TextChanged += (_, _) =>
        {
            if (_suppressQtyCommit || isDetached)
                return;
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                set(0);
                return;
            }
            if (Numbers.Try(box.Text, out var qty))
                set(ClampQty(QuantityMath.RoundQty(qty)));
        };
        box.LostFocus += (_, _) => Commit();
        box.KeyUp += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                Commit();
        };
        var cell = new Border
        {
            Width = ColWidths[column],
            Height = RowHeight,
            Background = background ?? Brushes.Transparent,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 1, 1),
            ClipToBounds = true,
            Child = box
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    /// <summary>Ô giờ hoạt động: nhập <c>x.y'</c> (x giờ, y phút) → lưu số giờ thập phân.</summary>
    private void AddHourEdit(Grid grid, int column, decimal value, Action<decimal> set)
    {
        var box = new TextBox
        {
            Text = FormatHour(value == 0 ? null : value),
            FontSize = 11,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 0, 2, 0),
            TextAlignment = TextAlignment.Right,
            MaxWidth = ColWidths[column] - 2,
            MaxLength = 12,
            TextWrapping = TextWrapping.NoWrap,
            ToolTip = "Nhập giờ.phút' — ví dụ 2.30' = 2 giờ 30 phút"
        };

        var isDetached = false;
        box.Unloaded += (_, _) => isDetached = true;

        void Commit()
        {
            if (_suppressQtyCommit || isDetached)
                return;
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                set(0);
                box.Text = "";
                return;
            }

            if (!ShipHourFormat.TryParse(box.Text, out var hours))
                return;
            hours = ClampQty(QuantityMath.RoundQty(hours));
            set(hours);
            box.Text = FormatHour(hours == 0 ? null : hours);
        }

        box.TextChanged += (_, _) =>
        {
            if (_suppressQtyCommit || isDetached)
                return;
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                set(0);
                return;
            }
            if (ShipHourFormat.TryParse(box.Text, out var hours))
                set(ClampQty(QuantityMath.RoundQty(hours)));
        };
        box.LostFocus += (_, _) => Commit();
        box.KeyUp += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                Commit();
        };
        var cell = new Border
        {
            Width = ColWidths[column],
            Height = RowHeight,
            Background = Brushes.Transparent,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 1, 1),
            ClipToBounds = true,
            Child = box
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static Border Box(string text, double width, double height, Brush background, bool center)
    {
        return new Border
        {
            Width = width,
            Height = height,
            Background = background,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 1, 1),
            ClipToBounds = true,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = TextBrush,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(2, 0, 2, 0)
            }
        };
    }

    private static string FormatDate(DateTime? date) =>
        date is DateTime value ? value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "";

    private static string FormatQty(decimal? value)
    {
        if (value is null)
            return "";
        var qty = QuantityMath.RoundQty(value.Value);
        if (qty == 0)
            return "";
        return qty == QuantityMath.Whole(qty) ? Numbers.Qty(qty) : Numbers.Decimal(qty);
    }

    private static string FormatHour(decimal? value) => ShipHourFormat.Format(value);

    private static SolidColorBrush Brush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
