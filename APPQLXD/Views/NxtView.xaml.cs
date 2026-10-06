using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Models;
using APPQLXD.ViewModels;

namespace APPQLXD.Views;

public partial class NxtView : UserControl
{
    private const double QtyWidth = 76;
    private const double RowHeight = 30;
    private const int BufferRows = 4;
    private static readonly Brush Line = Freeze(new SolidColorBrush(Color.FromRgb(0xD5, 0xDD, 0xE6)));
    private static readonly Brush Head = Freeze(new SolidColorBrush(Color.FromRgb(0x1B, 0x28, 0x36)));
    private static readonly Brush HeadText = Brushes.White;
    private static readonly Brush GroupHead = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x5A, 0x12)));
    private static readonly Brush SubHead = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x4A, 0x5C)));
    private static readonly Brush GroupSub = Freeze(new SolidColorBrush(Color.FromRgb(0xA8, 0x74, 0x2A)));
    private static readonly Brush Paper = Brushes.White;
    private static readonly Brush Summary = Freeze(new SolidColorBrush(Color.FromRgb(0xEE, 0xF3, 0xF8)));
    private static readonly Brush GroupPaper = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xF6, 0xE8)));
    private static readonly Brush Ink = Freeze(new SolidColorBrush(Color.FromRgb(0x1B, 0x28, 0x36)));
    private static readonly double[] LeftWidths = [100, 52, 52, 96, 220, 70, 160];
    private static readonly double LeftWidth = LeftWidths.Sum();

    private readonly List<RowSlot> _slots = [];
    private readonly Border _leftLead = new();
    private readonly StackPanel _leftRows = new();
    private readonly Border _leftTail = new();
    private readonly Border _fuelLead = new();
    private readonly StackPanel _fuelRows = new();
    private readonly Border _fuelTail = new();
    private bool _hostsReady;
    private IReadOnlyList<NxtRow> _rows = [];
    private NxtSheet? _sheet;
    private int _windowStart = -1;
    private int _slotColumns = -1;
    private string _headerKey = "";
    private INxtBoard? _vm;

    public NxtView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch();
    }

    private void Watch()
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVm;
        _vm = DataContext as INxtBoard;
        if (_vm is not null)
            _vm.PropertyChanged += OnVm;
        Draw();
    }

    private void OnVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(INxtBoard.Sheet))
            Draw();
    }

    private bool _syncing;
    private bool _binding;

    private void FuelScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_binding || _syncing)
            return;
        _syncing = true;
        if (e.HorizontalChange != 0)
            HeadScroll.ScrollToHorizontalOffset(FuelScroll.HorizontalOffset);
        if (e.VerticalChange != 0)
            LeftScroll.ScrollToVerticalOffset(FuelScroll.VerticalOffset);
        PlaceBars();
        _syncing = false;
        if (e.VerticalChange != 0 || e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
            BindWindow();
    }

    private void FuelScrollChanged(object sender, SizeChangedEventArgs e)
    {
        if (_binding)
            return;
        PlaceBars();
        BindWindow();
    }

    private void LeftScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_binding || _syncing || e.VerticalChange == 0)
            return;
        _syncing = true;
        FuelScroll.ScrollToVerticalOffset(LeftScroll.VerticalOffset);
        PlaceBars();
        _syncing = false;
    }

    private void BarChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || _placing)
            return;
        _syncing = true;
        if (ReferenceEquals(sender, VBar))
        {
            FuelScroll.ScrollToVerticalOffset(e.NewValue);
            LeftScroll.ScrollToVerticalOffset(e.NewValue);
        }
        else
        {
            FuelScroll.ScrollToHorizontalOffset(e.NewValue);
            HeadScroll.ScrollToHorizontalOffset(e.NewValue);
        }

        _syncing = false;
        if (ReferenceEquals(sender, VBar))
            BindWindow();
    }

    private bool _placing;

    private void PlaceBars()
    {
        if (_placing)
            return;
        _placing = true;
        var vertical = FuelScroll.ScrollableHeight > 0.5;
        var horizontal = FuelScroll.ScrollableWidth > 0.5;
        var vWidth = vertical ? SystemParameters.VerticalScrollBarWidth : 0;
        var hHeight = horizontal ? SystemParameters.HorizontalScrollBarHeight : 0;
        if (VBarColumn.Width.GridUnitType != GridUnitType.Pixel || Math.Abs(VBarColumn.Width.Value - vWidth) > 0.1)
            VBarColumn.Width = new GridLength(vWidth);
        if (HBarRow.Height.GridUnitType != GridUnitType.Pixel || Math.Abs(HBarRow.Height.Value - hHeight) > 0.1)
            HBarRow.Height = new GridLength(hHeight);
        SetBar(VBar, Math.Max(0, FuelScroll.ViewportHeight), Math.Max(0, FuelScroll.ScrollableHeight), FuelScroll.VerticalOffset);
        SetBar(HBar, Math.Max(0, FuelScroll.ViewportWidth), Math.Max(0, FuelScroll.ScrollableWidth), FuelScroll.HorizontalOffset);
        _placing = false;
    }

    private static void SetBar(ScrollBar bar, double viewport, double maximum, double value)
    {
        if (Math.Abs(bar.ViewportSize - viewport) > 0.5)
            bar.ViewportSize = viewport;
        if (Math.Abs(bar.Maximum - maximum) > 0.5)
            bar.Maximum = maximum;
        var clamped = Math.Min(bar.Maximum, value);
        if (Math.Abs(bar.Value - clamped) > 0.5)
            bar.Value = clamped;
    }

    private void LeftWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        FuelScroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent
        });
    }

    private void Draw()
    {
        DrawOverlay.Visibility = Visibility.Collapsed;
        _windowStart = -1;
        if (DataContext is not INxtBoard vm)
        {
            _sheet = null;
            _rows = [];
            _headerKey = "";
            LeftHead.Children.Clear();
            FuelHead.Children.Clear();
            ClearSlots();
            SetSpacers(0, 0);
            return;
        }

        var keepScroll = vm.TakePreserveScroll();
        var vertical = keepScroll ? FuelScroll.VerticalOffset : 0;
        var horizontal = keepScroll ? FuelScroll.HorizontalOffset : 0;
        _sheet = vm.Sheet;
        _rows = _sheet.Rows.ToList();
        EnsureHosts();
        var headerKey = string.Join('\0', _sheet.Columns.Select(c => c.Title + (c.IsGroupTotal ? "G" : "")));
        if (headerKey != _headerKey || LeftHead.Children.Count == 0 || FuelHead.Children.Count == 0)
        {
            LeftHead.Children.Clear();
            FuelHead.Children.Clear();
            LeftHead.Children.Add(LeftHeader());
            FuelHead.Children.Add(FuelHeader(_sheet));
            _headerKey = headerKey;
        }

        LeftBody.Width = LeftWidth;
        FuelBody.Width = Math.Max(1, _sheet.Columns.Count) * QtyWidth * 3;
        ApplyScroll(vertical, horizontal);
        BindWindow();
        PlaceBars();
        if (!keepScroll)
            return;
        Dispatcher.BeginInvoke(() =>
        {
            ApplyScroll(vertical, horizontal);
            BindWindow();
            PlaceBars();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ApplyScroll(double vertical, double horizontal)
    {
        _syncing = true;
        FuelScroll.ScrollToVerticalOffset(vertical);
        LeftScroll.ScrollToVerticalOffset(vertical);
        FuelScroll.ScrollToHorizontalOffset(horizontal);
        HeadScroll.ScrollToHorizontalOffset(horizontal);
        _syncing = false;
    }

    private void BindWindow()
    {
        if (_binding)
            return;
        if (_sheet is null || _rows.Count == 0)
        {
            ClearSlots();
            _windowStart = -1;
            return;
        }

        _binding = true;
        try
        {
            var offset = FuelScroll.VerticalOffset;
            var capacity = WindowCapacity();
            var start = FuelScroll.ScrollableHeight <= 0.5
                ? 0
                : (int)Math.Floor(offset / RowHeight) - BufferRows;
            if (start < 0)
                start = 0;
            var maxStart = Math.Max(0, _rows.Count - capacity);
            if (start > maxStart)
                start = maxStart;
            if (_slotColumns != _sheet.Columns.Count || _slots.Count < capacity)
            {
                _slotColumns = _sheet.Columns.Count;
                RebuildSlots(capacity);
            }

            if (start != _windowStart)
            {
                _windowStart = start;
                var shown = 0;
                for (var i = 0; i < _slots.Count; i++)
                {
                    var index = start + i;
                    var slot = _slots[i];
                    if (index >= _rows.Count)
                    {
                        slot.Left.Visibility = Visibility.Collapsed;
                        slot.Fuel.Visibility = Visibility.Collapsed;
                        continue;
                    }

                    BindSlot(slot, index);
                    shown++;
                }

                SetSpacers(start * RowHeight, Math.Max(0, _rows.Count - start - shown) * RowHeight);
            }

            if (Math.Abs(FuelScroll.VerticalOffset - offset) > 0.5)
            {
                _syncing = true;
                FuelScroll.ScrollToVerticalOffset(offset);
                LeftScroll.ScrollToVerticalOffset(offset);
                _syncing = false;
            }
        }
        finally
        {
            _binding = false;
        }
    }

    private int WindowCapacity()
    {
        var viewport = FuelScroll.ViewportHeight;
        if (viewport < 1)
            viewport = 24 * RowHeight;
        var capacity = (int)Math.Ceiling(viewport / RowHeight) + BufferRows * 2;
        return Math.Max(1, Math.Min(capacity, Math.Max(1, _rows.Count)));
    }

    private void EnsureHosts()
    {
        if (_hostsReady)
            return;
        LeftBody.Children.Add(_leftLead);
        LeftBody.Children.Add(_leftRows);
        LeftBody.Children.Add(_leftTail);
        FuelBody.Children.Add(_fuelLead);
        FuelBody.Children.Add(_fuelRows);
        FuelBody.Children.Add(_fuelTail);
        _hostsReady = true;
    }

    private void SetSpacers(double lead, double tail)
    {
        _leftLead.Height = lead;
        _fuelLead.Height = lead;
        _leftTail.Height = tail;
        _fuelTail.Height = tail;
    }

    private void RebuildSlots(int capacity)
    {
        ClearSlots();
        EnsureHosts();
        var columns = _sheet?.Columns.Count ?? 0;
        for (var i = 0; i < capacity; i++)
        {
            var slot = CreateSlot(columns);
            _slots.Add(slot);
            _leftRows.Children.Add(slot.Left);
            _fuelRows.Children.Add(slot.Fuel);
        }

        _windowStart = -1;
    }

    private void ClearSlots()
    {
        foreach (var slot in _slots)
        {
            _leftRows.Children.Remove(slot.Left);
            _fuelRows.Children.Remove(slot.Fuel);
        }

        _slots.Clear();
    }

    private RowSlot CreateSlot(int columns)
    {
        var left = new Grid { Height = RowHeight };
        left.MouseLeftButtonUp += OpenRow;
        foreach (var width in LeftWidths)
            left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        var leftCells = new CellView[LeftWidths.Length];
        for (var i = 0; i < LeftWidths.Length; i++)
        {
            var cell = BodyCell(LeftWidths[i], right: false);
            Grid.SetColumn(cell.Box, i);
            left.Children.Add(cell.Box);
            leftCells[i] = cell;
        }

        var fuel = HeaderGrid(columns);
        fuel.Height = RowHeight;
        fuel.MouseLeftButtonUp += OpenRow;
        var fuelCells = new CellView[columns * 3];
        for (var i = 0; i < fuelCells.Length; i++)
        {
            var cell = BodyCell(QtyWidth, right: true);
            Grid.SetColumn(cell.Box, i);
            fuel.Children.Add(cell.Box);
            fuelCells[i] = cell;
        }

        return new RowSlot(left, fuel, leftCells, fuelCells);
    }

    private void BindSlot(RowSlot slot, int index)
    {
        var row = _rows[index];
        var sheet = _sheet!;
        var summary = row.Kind != NxtRowKind.Slip;
        var paper = summary ? Summary : Paper;
        var weight = summary ? FontWeights.SemiBold : FontWeights.Normal;
        var hand = row.Kind == NxtRowKind.Slip;
        slot.Left.Tag = row;
        slot.Fuel.Tag = row;
        slot.Left.Background = paper;
        slot.Left.Cursor = hand ? Cursors.Hand : null;
        slot.Fuel.Cursor = slot.Left.Cursor;
        slot.Left.Visibility = Visibility.Visible;
        slot.Fuel.Visibility = Visibility.Visible;
        slot.Left.VerticalAlignment = VerticalAlignment.Top;
        slot.Fuel.VerticalAlignment = VerticalAlignment.Top;
        var number = row.Kind == NxtRowKind.Slip ? row.Number : "";
        var values = new[]
        {
            row.VoucherColumn == NxtVoucherColumn.N ? number : "",
            row.VoucherColumn == NxtVoucherColumn.Xx ? number : "",
            row.VoucherColumn == NxtVoucherColumn.Xd ? number : "",
            row.Date?.ToString("dd/MM/yyyy") ?? "",
            row.Description,
            row.Kilometers is decimal km ? Format(km) : "",
            row.Mission
        };
        for (var i = 0; i < values.Length; i++)
            Paint(slot.LeftCells[i], values[i], paper, weight);
        for (var i = 0; i < sheet.Columns.Count; i++)
        {
            var cell = i < row.Cells.Count ? row.Cells[i] : new NxtCell(null, null, null);
            var cellPaper = sheet.Columns[i].IsGroupTotal ? GroupPaper : paper;
            var cellWeight = summary || sheet.Columns[i].IsGroupTotal ? FontWeights.SemiBold : FontWeights.Normal;
            Paint(slot.FuelCells[i * 3], FormatQty(cell.In), cellPaper, cellWeight);
            Paint(slot.FuelCells[i * 3 + 1], FormatQty(cell.Out), cellPaper, cellWeight);
            Paint(slot.FuelCells[i * 3 + 2], FormatQty(cell.Balance), cellPaper, cellWeight);
        }
    }

    private static Grid LeftHeader()
    {
        var grid = new Grid { Height = RowHeight * 2 };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
        foreach (var width in LeftWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

        var document = Box("Chứng từ", LeftWidths[0] + LeftWidths[1] + LeftWidths[2] + LeftWidths[3], RowHeight, Head, HeadText, FontWeights.SemiBold, false, true);
        Grid.SetColumn(document, 0);
        Grid.SetColumnSpan(document, 4);
        grid.Children.Add(document);
        AddLeftHead(grid, 0, 1, "N", LeftWidths[0]);
        AddLeftHead(grid, 1, 1, "XX", LeftWidths[1]);
        AddLeftHead(grid, 2, 1, "XD", LeftWidths[2]);
        AddLeftHead(grid, 3, 1, "Ngày", LeftWidths[3]);

        AddLeftSpan(grid, 4, "Diễn giải");
        AddLeftSpan(grid, 5, "Số km");
        AddLeftSpan(grid, 6, "Nhiệm vụ");
        return grid;
    }

    private static void AddLeftHead(Grid grid, int column, int row, string text, double width)
    {
        var cell = Box(text, width, RowHeight, SubHead, HeadText, FontWeights.SemiBold, false, true);
        Grid.SetColumn(cell, column);
        Grid.SetRow(cell, row);
        grid.Children.Add(cell);
    }

    private static void AddLeftSpan(Grid grid, int column, string text)
    {
        var cell = Box(text, LeftWidths[column], RowHeight * 2, Head, HeadText, FontWeights.SemiBold, false, true);
        Grid.SetColumn(cell, column);
        Grid.SetRowSpan(cell, 2);
        grid.Children.Add(cell);
    }

    private static Grid FuelHeader(NxtSheet sheet)
    {
        var grid = HeaderGrid(sheet.Columns.Count);
        grid.Height = RowHeight * 2;
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
        for (var i = 0; i < sheet.Columns.Count; i++)
        {
            var column = sheet.Columns[i];
            var title = Box(column.Title, QtyWidth * 3, RowHeight, column.IsGroupTotal ? GroupHead : Head, HeadText, FontWeights.SemiBold, true);
            Grid.SetColumn(title, i * 3);
            Grid.SetColumnSpan(title, 3);
            grid.Children.Add(title);
            var sub = column.IsGroupTotal ? GroupSub : SubHead;
            AddSub(grid, i, 0, "Nhập", sub);
            AddSub(grid, i, 1, "Xuất", sub);
            AddSub(grid, i, 2, "Tồn", sub);
        }

        return grid;
    }

    private void OpenRow(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: NxtRow row })
            return;
        if (row.Kind != NxtRowKind.Slip)
            return;
        if (DataContext is not INxtBoard vm)
            return;
        e.Handled = true;
        vm.Edit(row);
    }

    private static Grid HeaderGrid(int groups)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        for (var i = 0; i < groups * 3; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(QtyWidth) });
        return grid;
    }

    private static void AddSub(Grid grid, int group, int offset, string text, Brush background)
    {
        var cell = Box(text, QtyWidth, RowHeight, background, HeadText, FontWeights.SemiBold, true);
        Grid.SetColumn(cell, group * 3 + offset);
        Grid.SetRow(cell, 1);
        grid.Children.Add(cell);
    }

    private static string FormatQty(decimal? value) =>
        value is decimal number ? Format(number) : "";

    private static string Format(decimal value) =>
        Numbers.Qty(QuantityMath.Whole(value));

    private static void Paint(CellView cell, string text, Brush background, FontWeight weight)
    {
        cell.Box.Background = background;
        cell.Text.Text = text;
        cell.Text.FontWeight = weight;
        cell.Text.ToolTip = text.Length == 0 ? null : text;
    }

    private static CellView BodyCell(double width, bool right)
    {
        var text = new TextBlock
        {
            Foreground = Ink,
            Padding = new Thickness(6, 0, 6, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
        var box = new Border
        {
            Width = width,
            Height = RowHeight,
            Background = Paper,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = text
        };
        return new CellView(box, text);
    }

    private static Border Box(string text, double width, double height, Brush background, Brush foreground, FontWeight weight, bool right, bool center = false)
    {
        return new Border
        {
            Width = width,
            Height = height,
            Background = background,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontWeight = weight,
                Padding = new Thickness(6, 0, 6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = center ? HorizontalAlignment.Center : right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                ToolTip = text.Length == 0 ? null : text
            }
        };
    }

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private sealed record CellView(Border Box, TextBlock Text);

    private sealed record RowSlot(Grid Left, Grid Fuel, CellView[] LeftCells, CellView[] FuelCells);
}
