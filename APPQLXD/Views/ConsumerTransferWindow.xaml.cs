using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Models;
using ToolTip = System.Windows.Controls.ToolTip;
using APPQLXD.Core.Persistence;
using APPQLXD.ViewModels;

namespace APPQLXD.Views;

public sealed class ConsumerTransferPopupVm
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string StatusText { get; init; } = "";
    public string ConsumerName { get; init; } = "";
    public string PlateNumber { get; init; } = "";
    public string FuelUsed { get; init; } = "";
    public string FormId { get; init; } = "Số 3-04.1/XD-14";
    public string UnitNote { get; init; } = "Đơn vị tính: Nhiên liệu: Lít 15° C; Dầu mỡ: Kg.";
    public Guid ConsumerId { get; init; }
    public DateTime QuarterDate { get; init; }
    public ObservableCollection<ConsumerTransferBookLineVm> Rows { get; init; } = [];
    public IReadOnlyList<LotTypeRow> LotTypes { get; init; } = [];
    public IReadOnlyList<MissionTaskRow> MissionTasks { get; init; } = [];
    public IReadOnlyList<ConsumerQuarterBookLineEdit> PendingLines { get; set; } = [];
    internal int NextAddOrder { get; set; }
}

public sealed class ConsumerTransferBookLineVm : INotifyPropertyChanged
{
    private decimal? _kilometers;
    private decimal? _machineHours;
    private decimal? _normQuantity;
    private decimal? _actualQuantity;
    private decimal? _fuelOut;
    private decimal? _oilOut;
    private bool _manualFuelOut;
    private bool _manualOilOut;
    private decimal? _fuelBalance;
    private decimal? _oilBalance;
    private string _fuelBalanceLotTip = "";
    private string _oilBalanceLotTip = "";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;
    public event Action<ConsumerTransferBookLineVm>? DateChanged;

    public bool IsOpening { get; init; }
    public bool IsManualRow { get; init; }
    /// <summary>Thứ tự thêm trong phiên (0 = dòng đã có). Cùng ngày: dòng mới nằm dưới cùng.</summary>
    public int AddOrder { get; set; }
    public Guid? LineId { get; set; }
    public Guid? TransferDocumentId { get; init; }
    private string _documentNumber = "";
    private DateTime? _documentDate;
    private string _description = "";
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
            if (!Set(ref _documentDate, value))
                return;
            OnPropertyChanged(nameof(DocumentDate));
            DateChanged?.Invoke(this);
        }
    }
    public string Description
    {
        get => _description;
        set { if (Set(ref _description, value ?? "")) Notify(); }
    }
    public string Origin { get; init; } = "";
    public string Destination { get; init; } = "";
    public decimal? FuelIn { get; init; }
    public decimal? OilIn { get; init; }
    public Guid LotTypeId { get; set; } = SeedIds.LotTypeTx;
    public string LotTypeCode { get; set; } = "TX";
    public Guid? MissionTaskId { get; set; }
    public string MissionTaskName { get; set; } = "";
    public bool IsEditable => !IsOpening;

    public decimal? Kilometers
    {
        get => _kilometers;
        set { if (Set(ref _kilometers, value)) Notify(); }
    }

    public decimal? MachineHours
    {
        get => _machineHours;
        set { if (Set(ref _machineHours, value)) Notify(); }
    }

    public decimal? NormQuantity
    {
        get => _normQuantity;
        set { if (Set(ref _normQuantity, value)) Notify(); }
    }

    public decimal? ActualQuantity
    {
        get => _actualQuantity;
        set
        {
            if (!Set(ref _actualQuantity, value))
                return;
            if (IsEditable && IsOutOverride(value, FuelIn))
                _manualFuelOut = true;
            if (value is decimal actual)
                _fuelOut = actual;
            Notify();
        }
    }

    public decimal? FuelOut
    {
        get => _fuelOut;
        set
        {
            if (!Set(ref _fuelOut, value))
                return;
            if (IsEditable && IsOutOverride(value, FuelIn))
                _manualFuelOut = true;
            if (value is decimal fuel)
                _actualQuantity = fuel;
            Notify();
        }
    }

    public decimal? OilOut
    {
        get => _oilOut;
        set
        {
            if (!Set(ref _oilOut, value))
                return;
            if (IsEditable && IsOutOverride(value, OilIn))
                _manualOilOut = true;
            Notify();
        }
    }

    public bool ManualFuelOut
    {
        get => _manualFuelOut;
        set { if (Set(ref _manualFuelOut, value)) Notify(); }
    }

    public bool ManualOilOut
    {
        get => _manualOilOut;
        set { if (Set(ref _manualOilOut, value)) Notify(); }
    }

    public decimal? FuelBalance
    {
        get => _fuelBalance;
        set { if (Set(ref _fuelBalance, value)) OnPropertyChanged(); }
    }

    public string FuelBalanceLotTip
    {
        get => _fuelBalanceLotTip;
        set { if (Set(ref _fuelBalanceLotTip, value ?? "")) OnPropertyChanged(); }
    }

    public string OilBalanceLotTip
    {
        get => _oilBalanceLotTip;
        set { if (Set(ref _oilBalanceLotTip, value ?? "")) OnPropertyChanged(); }
    }

    public decimal? OilBalance
    {
        get => _oilBalance;
        set { if (Set(ref _oilBalance, value)) OnPropertyChanged(); }
    }

    public decimal? OverQuantity
    {
        get
        {
            if (NormQuantity is not decimal n || ActualQuantity is not decimal a)
                return null;
            var diff = QuantityMath.Whole(a - n);
            return diff > 0 ? diff : null;
        }
    }

    public decimal? UnderQuantity
    {
        get
        {
            if (NormQuantity is not decimal n || ActualQuantity is not decimal a)
                return null;
            var diff = QuantityMath.Whole(a - n);
            return diff < 0 ? QuantityMath.Whole(-diff) : null;
        }
    }

    /// <summary>Xuất khác Nhập (null coi = 0) → ghi đè tay, không để ĐC ghi đè khi mở lại.</summary>
    public static bool IsOutOverride(decimal? outflow, decimal? inflow) =>
        QuantityMath.Whole(outflow ?? 0) != QuantityMath.Whole(inflow ?? 0);

    private void Notify()
    {
        OnPropertyChanged(nameof(DocumentNumber));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Kilometers));
        OnPropertyChanged(nameof(MachineHours));
        OnPropertyChanged(nameof(NormQuantity));
        OnPropertyChanged(nameof(ActualQuantity));
        OnPropertyChanged(nameof(FuelOut));
        OnPropertyChanged(nameof(OilOut));
        OnPropertyChanged(nameof(ManualFuelOut));
        OnPropertyChanged(nameof(ManualOilOut));
        OnPropertyChanged(nameof(OverQuantity));
        OnPropertyChanged(nameof(UnderQuantity));
        Changed?.Invoke();
    }

    private bool Set<T>(ref T field, T value)
    {
        if (Equals(field, value))
            return false;
        field = value;
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class ConsumerTransferWindow : Window
{
    private const double RowHeight = 24;
    private static readonly double[] ColWidths =
    [
        78, 88, 156, 160, 72, 100, 100, 78, 78, 78, 78, 62, 62, 78, 78, 78, 78, 78, 78, 44
    ];
    private static readonly Brush Head = Brush("#E8EEF5");
    private static readonly Brush SubHead = Brush("#F3F6FA");
    private static readonly Brush Line = Brush("#CBD5E1");
    private static readonly Brush TextBrush = Brush("#0F172A");
    private static readonly Brush OpeningBg = Brush("#F8FAFC");
    private static readonly Brush ManualBg = Brush("#FFFBEB");
    private bool _suppress;
    private bool _sizedToContent;

    public bool Saved { get; private set; }

    public ConsumerTransferWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RenderBook();
        Loaded += (_, _) =>
        {
            RenderBook();
            SizeToBookContent();
        };
    }

    public static bool? Show(ConsumerTransferPopupVm model)
    {
        var window = new ConsumerTransferWindow { DataContext = model };
        if (Application.Current?.MainWindow is Window owner && owner.IsLoaded)
            window.Owner = owner;
        var result = window.ShowDialog();
        if (result == true && window.Saved)
            return true;
        return false;
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
        if (DataContext is not ConsumerTransferPopupVm model)
            return;
        try
        {
            RecalcBalances();
            var book = new ConsumerTransferBook(
                model.ConsumerName,
                model.PlateNumber,
                model.FuelUsed,
                model.FormId,
                model.UnitNote,
                model.Rows.Where(x => !x.IsOpening).Sum(x => x.FuelIn ?? 0),
                model.Rows.Where(x => !x.IsOpening).Sum(x => x.OilIn ?? 0),
                model.Rows.Select(x => new ConsumerTransferBookRow(
                    x.IsOpening, x.TransferDocumentId, x.DocumentNumber, x.DocumentDate, x.Description,
                    x.Origin, x.Destination, x.Kilometers, x.MachineHours, x.NormQuantity, x.ActualQuantity,
                    x.OverQuantity, x.UnderQuantity, x.FuelIn, x.FuelOut, x.FuelBalance, x.OilIn, x.OilOut, x.OilBalance,
                    x.ManualFuelOut, x.ManualOilOut, x.LineId, x.LotTypeId, x.LotTypeCode, x.IsManualRow,
                    x.FuelBalanceLotTip, x.OilBalanceLotTip)).ToList());

            var q = (model.QuarterDate.Month - 1) / 3 + 1;
            var bytes = Services.ExportUi.CreateService().ExportConsumerBookWord(book, model.QuarterDate.Year, q);
            Services.ExportUi.SaveBytes(bytes, $"so_tt_may_xe_{model.ConsumerName}_Q{q}_{model.QuarterDate.Year}.docx",
                "Word (*.docx)|*.docx", "docx");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Xuất Word", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConsumerTransferPopupVm model)
            return;
        RecalcBalances();
        model.PendingLines = model.Rows
            .Where(x => x.IsEditable)
            .Select(x =>
            {
                var manualFuel = x.ManualFuelOut || x.IsManualRow
                    || ConsumerTransferBookLineVm.IsOutOverride(x.FuelOut, x.FuelIn);
                var manualOil = x.ManualOilOut || x.IsManualRow
                    || ConsumerTransferBookLineVm.IsOutOverride(x.OilOut, x.OilIn);
                return new ConsumerQuarterBookLineEdit
                {
                    LineId = x.LineId,
                    TransferDocumentId = x.TransferDocumentId,
                    DocumentNumber = x.DocumentNumber,
                    DocumentDate = x.DocumentDate,
                    Description = x.Description,
                    Kilometers = x.Kilometers,
                    MachineHours = x.MachineHours,
                    NormQuantity = x.NormQuantity,
                    ActualQuantity = x.ActualQuantity,
                    ManualFuelOut = manualFuel,
                    FuelOut = x.FuelOut ?? 0,
                    ManualOilOut = manualOil,
                    OilOut = x.OilOut ?? 0,
                    LotTypeId = x.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : x.LotTypeId,
                    LotTypeCode = string.IsNullOrWhiteSpace(x.LotTypeCode) ? "TX" : x.LotTypeCode,
                    MissionTaskId = x.MissionTaskId
                };
            })
            .ToList();
        Saved = true;
        DialogResult = true;
    }

    private void CloseClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void RenderBook()
    {
        if (BookHost is null)
            return;
        _suppress = true;
        try
        {
            BookHost.Children.Clear();
            if (DataContext is not ConsumerTransferPopupVm model)
                return;

            foreach (var row in model.Rows)
            {
                row.Changed -= OnRowChanged;
                row.DateChanged -= OnEditableDateChanged;
            }

            foreach (var row in model.Rows.Where(x => x.IsEditable))
            {
                row.Changed += OnRowChanged;
                if (row.IsManualRow)
                    row.DateChanged += OnEditableDateChanged;
            }

            RecalcBalances();
            BookHost.Children.Add(BuildHeader());
            foreach (var row in model.Rows)
                BookHost.Children.Add(BuildRow(model, row));
            BookHost.Children.Add(BuildTotalsRow(model));
        }
        finally
        {
            _suppress = false;
        }
    }

    private void OnRowChanged()
    {
        if (_suppress)
            return;
        RecalcBalances();
        RebuildDataCells();
    }

    private void RebuildDataCells()
    {
        if (BookHost is null || DataContext is not ConsumerTransferPopupVm model)
            return;
        if (BookHost.Children.Count != model.Rows.Count + 2)
            return;
        for (var i = 0; i < model.Rows.Count; i++)
        {
            if (BookHost.Children[i + 1] is not Grid grid)
                continue;
            var row = model.Rows[i];
            SetCellText(grid, 11, FormatQty(row.OverQuantity));
            SetCellText(grid, 12, FormatQty(row.UnderQuantity));
            SetBalanceCell(grid, 15, FormatQty(row.FuelBalance), LotTip("Nhiên liệu", row.FuelBalanceLotTip));
            SetBalanceCell(grid, 18, FormatQty(row.OilBalance), LotTip("Dầu mỡ", row.OilBalanceLotTip));
        }

        if (BookHost.Children.Count == model.Rows.Count + 2)
        {
            BookHost.Children.RemoveAt(BookHost.Children.Count - 1);
            BookHost.Children.Add(BuildTotalsRow(model));
        }
    }

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

    private void RecalcBalances()
    {
        if (DataContext is not ConsumerTransferPopupVm model)
            return;
        decimal fuel = 0;
        decimal oil = 0;
        var fuelLots = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var oilLots = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in model.Rows)
        {
            if (row.IsOpening)
            {
                fuel = row.FuelBalance ?? 0;
                oil = row.OilBalance ?? 0;
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

            var fuelIn = row.FuelIn ?? 0;
            var oilIn = row.OilIn ?? 0;
            var fuelOut = row.FuelOut ?? 0;
            var oilOut = row.OilOut ?? 0;
            fuel = QuantityMath.Whole(fuel + fuelIn - fuelOut);
            oil = QuantityMath.Whole(oil + oilIn - oilOut);
            LotBalanceTips.Add(fuelLots, row.LotTypeCode, fuelIn - fuelOut);
            LotBalanceTips.Add(oilLots, row.LotTypeCode, oilIn - oilOut);
            row.FuelBalance = fuel == 0 && fuelIn == 0 && fuelOut == 0 ? null : fuel;
            row.OilBalance = oil == 0 && oilIn == 0 && oilOut == 0 ? null : oil;
            row.FuelBalanceLotTip = LotBalanceTips.Format(fuelLots);
            row.OilBalanceLotTip = LotBalanceTips.Format(oilLots);
        }
    }

    private static Grid BuildHeader()
    {
        var grid = new Grid { Height = RowHeight * 2 };
        foreach (var width in ColWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });

        AddMerge(grid, 0, 0, 2, 1, "Chứng từ", Head);
        AddCell(grid, 0, 1, "Số", SubHead, true);
        AddCell(grid, 1, 1, "Ngày", SubHead, true);
        AddSpan(grid, 2, "Diễn giải", Head);
        AddSpan(grid, 3, "Nhiệm vụ", Head);
        AddSpan(grid, 4, "Loại lô", Head);
        AddSpan(grid, 5, "Nơi đi", Head);
        AddSpan(grid, 6, "Nơi đến", Head);
        AddSpan(grid, 7, "Km hoạt động", Head);
        AddSpan(grid, 8, "Giờ máy hoạt động", Head);
        AddMerge(grid, 9, 0, 2, 1, "Nhiên liệu tiêu thụ theo", Head);
        AddCell(grid, 9, 1, "định mức", SubHead, true);
        AddCell(grid, 10, 1, "thực chi", SubHead, true);
        AddMerge(grid, 11, 0, 2, 1, "So sánh Đ.mức với thực chi", Head);
        AddCell(grid, 11, 1, "Quá", SubHead, true);
        AddCell(grid, 12, 1, "Rút", SubHead, true);
        AddMerge(grid, 13, 0, 3, 1, "Nhiên liệu tiêu thụ", Head);
        AddCell(grid, 13, 1, "Nhập", SubHead, true);
        AddCell(grid, 14, 1, "Xuất", SubHead, true);
        AddCell(grid, 15, 1, "Tồn", SubHead, true);
        AddMerge(grid, 16, 0, 3, 1, "Dầu mỡ", Head);
        AddCell(grid, 16, 1, "Nhập", SubHead, true);
        AddCell(grid, 17, 1, "Xuất", SubHead, true);
        AddCell(grid, 18, 1, "Tồn", SubHead, true);
        AddSpan(grid, 19, "", Head);
        return grid;
    }

    private Grid BuildTotalsRow(ConsumerTransferPopupVm model)
    {
        decimal km = 0, hours = 0, norm = 0, actual = 0, over = 0, under = 0;
        decimal fuelIn = 0, fuelOut = 0, oilIn = 0, oilOut = 0;
        foreach (var row in model.Rows)
        {
            if (row.IsOpening)
                continue;
            km = QuantityMath.RoundQty(km + (row.Kilometers ?? 0));
            hours = QuantityMath.RoundQty(hours + (row.MachineHours ?? 0));
            norm = QuantityMath.Whole(norm + (row.NormQuantity ?? 0));
            actual = QuantityMath.Whole(actual + (row.ActualQuantity ?? 0));
            over = QuantityMath.Whole(over + (row.OverQuantity ?? 0));
            under = QuantityMath.Whole(under + (row.UnderQuantity ?? 0));
            fuelIn = QuantityMath.Whole(fuelIn + (row.FuelIn ?? 0));
            fuelOut = QuantityMath.Whole(fuelOut + (row.FuelOut ?? 0));
            oilIn = QuantityMath.Whole(oilIn + (row.OilIn ?? 0));
            oilOut = QuantityMath.Whole(oilOut + (row.OilOut ?? 0));
        }

        var grid = new Grid { Height = RowHeight, Background = Head };
        foreach (var width in ColWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        AddData(grid, 0, "", false, FontWeights.SemiBold);
        AddData(grid, 1, "", false, FontWeights.SemiBold);
        AddData(grid, 2, "Cộng", false, FontWeights.SemiBold);
        AddData(grid, 3, "", false, FontWeights.SemiBold);
        AddData(grid, 4, "", false, FontWeights.SemiBold);
        AddData(grid, 5, "", false, FontWeights.SemiBold);
        AddData(grid, 6, "", false, FontWeights.SemiBold);
        AddData(grid, 7, FormatQty(NullZero(km)), true, FontWeights.SemiBold);
        AddData(grid, 8, FormatQty(NullZero(hours)), true, FontWeights.SemiBold);
        AddData(grid, 9, FormatQty(NullZero(norm)), true, FontWeights.SemiBold);
        AddData(grid, 10, FormatQty(NullZero(actual)), true, FontWeights.SemiBold);
        AddData(grid, 11, FormatQty(NullZero(over)), true, FontWeights.SemiBold);
        AddData(grid, 12, FormatQty(NullZero(under)), true, FontWeights.SemiBold);
        AddData(grid, 13, FormatQty(NullZero(fuelIn)), true, FontWeights.SemiBold);
        AddData(grid, 14, FormatQty(NullZero(fuelOut)), true, FontWeights.SemiBold);
        AddData(grid, 15, "", true, FontWeights.SemiBold);
        AddData(grid, 16, FormatQty(NullZero(oilIn)), true, FontWeights.SemiBold);
        AddData(grid, 17, FormatQty(NullZero(oilOut)), true, FontWeights.SemiBold);
        AddData(grid, 18, "", true, FontWeights.SemiBold);
        AddData(grid, 19, "", true, FontWeights.SemiBold);
        return grid;
    }

    private Grid BuildRow(ConsumerTransferPopupVm model, ConsumerTransferBookLineVm row)
    {
        var bg = row.IsOpening ? OpeningBg : row.IsManualRow || row.ManualFuelOut || row.ManualOilOut ? ManualBg : Brushes.White;
        var grid = new Grid { Height = RowHeight, Background = bg };
        foreach (var width in ColWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

        var weight = row.IsOpening ? FontWeights.SemiBold : FontWeights.Normal;
        if (row.IsManualRow)
        {
            AddTextEdit(grid, 0, row.DocumentNumber, v => row.DocumentNumber = v);
            AddDateEdit(grid, 1, row.DocumentDate, v => row.DocumentDate = v);
            AddTextEdit(grid, 2, row.Description, v => row.Description = v);
            AddMissionCell(grid, 3, model, row);
            AddLotTypeCell(grid, 4, model, row);
        }
        else if (row.IsEditable)
        {
            AddData(grid, 0, row.DocumentNumber, false, weight);
            AddData(grid, 1, FormatDate(row.DocumentDate), false, weight);
            AddData(grid, 2, row.Description, false, weight);
            AddMissionCell(grid, 3, model, row);
            AddLotTypeCell(grid, 4, model, row);
        }
        else
        {
            AddData(grid, 0, row.DocumentNumber, false, weight);
            AddData(grid, 1, FormatDate(row.DocumentDate), false, weight);
            AddData(grid, 2, row.Description, false, weight);
            AddData(grid, 3, row.IsOpening ? "" : row.MissionTaskName, false, weight);
            AddData(grid, 4, row.IsOpening ? "" : row.LotTypeCode, true, weight);
        }

        AddData(grid, 5, row.Origin, false, weight);
        AddData(grid, 6, row.Destination, false, weight);

        if (row.IsEditable)
        {
            AddEdit(grid, 7, row.Kilometers, v => row.Kilometers = v);
            AddEdit(grid, 8, row.MachineHours, v => row.MachineHours = v);
            AddEdit(grid, 9, row.NormQuantity, v => row.NormQuantity = v);
            AddEdit(grid, 10, row.ActualQuantity, v => row.ActualQuantity = v);
            AddData(grid, 11, FormatQty(row.OverQuantity), true, weight);
            AddData(grid, 12, FormatQty(row.UnderQuantity), true, weight);
            // Nhập chỉ từ ĐC — dòng tay để trống, không sửa.
            AddData(grid, 13, FormatQty(row.FuelIn), true, weight);
            AddEdit(grid, 14, row.FuelOut, v =>
            {
                row.FuelOut = v;
                if (row.IsManualRow || ConsumerTransferBookLineVm.IsOutOverride(v, row.FuelIn))
                    row.ManualFuelOut = true;
            });
            AddData(grid, 15, FormatQty(row.FuelBalance), true, weight, LotTip("Nhiên liệu", row.FuelBalanceLotTip));
            AddData(grid, 16, FormatQty(row.OilIn), true, weight);
            AddEdit(grid, 17, row.OilOut, v =>
            {
                row.OilOut = v;
                if (row.IsManualRow || ConsumerTransferBookLineVm.IsOutOverride(v, row.OilIn))
                    row.ManualOilOut = true;
            });
            AddData(grid, 18, FormatQty(row.OilBalance), true, weight, LotTip("Dầu mỡ", row.OilBalanceLotTip));
            if (row.IsManualRow)
                AddRowActions(grid, 19, row);
            else
                AddData(grid, 19, "", true, weight);
        }
        else
        {
            AddData(grid, 7, FormatQty(row.Kilometers), true, weight);
            AddData(grid, 8, FormatQty(row.MachineHours), true, weight);
            AddData(grid, 9, FormatQty(row.NormQuantity), true, weight);
            AddData(grid, 10, FormatQty(row.ActualQuantity), true, weight);
            AddData(grid, 11, FormatQty(row.OverQuantity), true, weight);
            AddData(grid, 12, FormatQty(row.UnderQuantity), true, weight);
            AddData(grid, 13, FormatQty(row.FuelIn), true, weight);
            AddData(grid, 14, FormatQty(row.FuelOut), true, weight);
            AddData(grid, 15, FormatQty(row.FuelBalance), true, weight, LotTip("Nhiên liệu", row.FuelBalanceLotTip));
            AddData(grid, 16, FormatQty(row.OilIn), true, weight);
            AddData(grid, 17, FormatQty(row.OilOut), true, weight);
            AddData(grid, 18, FormatQty(row.OilBalance), true, weight, LotTip("Dầu mỡ", row.OilBalanceLotTip));
            AddData(grid, 19, "", true, weight);
        }

        return grid;
    }

    private void AddMissionCell(Grid grid, int column, ConsumerTransferPopupVm model, ConsumerTransferBookLineVm row)
    {
        if (model.MissionTasks.Count == 0)
        {
            AddData(grid, column, row.MissionTaskName, false, FontWeights.Normal);
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

    private void AddLotTypeCell(Grid grid, int column, ConsumerTransferPopupVm model, ConsumerTransferBookLineVm row)
    {
        if (!row.IsManualRow || model.LotTypes.Count == 0)
        {
            AddData(grid, column, row.IsOpening ? "" : row.LotTypeCode, true, FontWeights.Normal);
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

    private void AddRowClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConsumerTransferPopupVm model)
            return;
        var row = new ConsumerTransferBookLineVm
        {
            IsManualRow = true,
            DocumentDate = LastWorkingDate(model),
            DocumentNumber = "",
            Description = "Tiêu thụ",
            ManualFuelOut = true,
            ManualOilOut = true,
            LotTypeId = SeedIds.LotTypeTx,
            LotTypeCode = "TX",
            AddOrder = ++model.NextAddOrder
        };
        row.Changed += OnRowChanged;
        row.DateChanged += OnEditableDateChanged;
        model.Rows.Add(row);
        SortAndRender();
    }

    private void OnEditableDateChanged(ConsumerTransferBookLineVm row)
    {
        if (DataContext is ConsumerTransferPopupVm model)
            row.AddOrder = ++model.NextAddOrder;
        SortAndRender();
    }

    private void SortAndRender()
    {
        SortRowsChronologically();
        RenderBook();
    }

    private static DateTime LastWorkingDate(ConsumerTransferPopupVm model)
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
        if (DataContext is not ConsumerTransferPopupVm model)
            return;
        var opening = model.Rows.Where(x => x.IsOpening).ToList();
        var rest = model.Rows
            .Where(x => !x.IsOpening)
            .OrderBy(x => x.DocumentDate?.Date ?? DateTime.MaxValue)
            .ThenBy(x => x.IsManualRow ? 1 : 0) // cùng ngày: phiếu ĐC trước
            .ThenBy(x => x.AddOrder) // dòng thêm / vừa đổi ngày ở dưới cùng ngày đó
            .ThenBy(x => x.DocumentNumber, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        model.Rows.Clear();
        foreach (var row in opening.Concat(rest))
            model.Rows.Add(row);
    }

    private void AddRowActions(Grid grid, int column, ConsumerTransferBookLineVm row)
    {
        var btn = new Button
        {
            Content = "✕",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(0),
            Width = 16,
            Height = 18,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Xóa dòng tiêu thụ",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brush("#B91C1C"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        btn.Click += (_, _) => DeleteRow(row);
        var cell = new Border
        {
            Width = ColWidths[column],
            Height = RowHeight,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = btn
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private void DeleteRow(ConsumerTransferBookLineVm row)
    {
        if (DataContext is not ConsumerTransferPopupVm model || !row.IsManualRow)
            return;
        row.Changed -= OnRowChanged;
        row.DateChanged -= OnEditableDateChanged;
        model.Rows.Remove(row);
        RenderBook();
    }

    private void AddTextEdit(Grid grid, int column, string value, Action<string> set)
    {
        var box = new TextBox
        {
            Text = value ?? "",
            FontSize = 11,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 0, 4, 0),
            MaxWidth = ColWidths[column] - 2,
            TextWrapping = TextWrapping.NoWrap
        };
        box.LostFocus += (_, _) =>
        {
            set(box.Text?.Trim() ?? "");
            box.Text = box.Text?.Trim() ?? "";
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
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
            Width = ColWidths[column] - 2,
            Padding = new Thickness(2, 0, 2, 0)
        };
        picker.SelectedDateChanged += (_, _) => set(picker.SelectedDate?.Date);
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

    private void AddEdit(Grid grid, int column, decimal? value, Action<decimal?> set)
    {
        var box = new TextBox
        {
            Text = FormatQty(value),
            FontSize = 11,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Padding = new Thickness(4, 0, 6, 0),
            MaxWidth = ColWidths[column] - 2,
            MaxLength = 24,
            TextWrapping = TextWrapping.NoWrap
        };
        Controls.NumericFormat.SetKind(box, NumericKind.Decimal);
        box.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                set(null);
                box.Text = "";
                return;
            }

            if (Numbers.Try(box.Text, out var parsed))
            {
                parsed = QuantityMath.ClampEditQty(QuantityMath.RoundQty(parsed));
                set(parsed);
                box.Text = FormatQty(parsed);
            }
            else
                box.Text = FormatQty(value);
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

    private static void AddSpan(Grid grid, int column, string text, Brush background)
    {
        var cell = Box(text, ColWidths[column], RowHeight * 2, background, true);
        Grid.SetColumn(cell, column);
        Grid.SetRowSpan(cell, 2);
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

    private static void AddCell(Grid grid, int column, int row, string text, Brush background, bool center)
    {
        var cell = Box(text, ColWidths[column], RowHeight, background, center);
        Grid.SetColumn(cell, column);
        Grid.SetRow(cell, row);
        grid.Children.Add(cell);
    }

    private static string? LotTip(string title, string tip) =>
        string.IsNullOrWhiteSpace(tip) ? null : $"Số lượng tổng theo loại lô — {title}\n{tip}";

    private static void AddData(Grid grid, int column, string text, bool right, FontWeight weight, string? toolTip = null)
    {
        var cell = Box(text, ColWidths[column], RowHeight, Brushes.Transparent, !right);
        if (cell.Child is TextBlock block)
        {
            block.FontWeight = weight;
            block.TextAlignment = right ? TextAlignment.Right : TextAlignment.Left;
            block.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            if (right)
                block.Margin = new Thickness(4, 0, 6, 0);
        }

        if (!string.IsNullOrWhiteSpace(toolTip))
            cell.ToolTip = new ToolTip
            {
                Content = new TextBlock
                {
                    Text = toolTip,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 280
                }
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
                FontSize = 12,
                Foreground = TextBrush,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(4, 0, 4, 0)
            }
        };
    }

    private static decimal? NullZero(decimal value) => value == 0 ? null : value;

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

    private static SolidColorBrush Brush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
