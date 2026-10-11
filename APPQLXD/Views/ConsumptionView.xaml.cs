using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using APPQLXD.Core;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.ViewModels;

namespace APPQLXD.Views;

public partial class ConsumptionView : UserControl
{
    private const double BandHeight = 26;
    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SolidColorBrush Line = Freeze(Color.FromRgb(0xD5, 0xDE, 0xE8));
    private static readonly SolidColorBrush Edge = Freeze(Color.FromRgb(0x9B, 0xB0, 0xC3));
    private static readonly SolidColorBrush EvenBand = Freeze(Color.FromRgb(0x1E, 0x4D, 0x7B));
    private static readonly SolidColorBrush OddBand = Freeze(Color.FromRgb(0x1F, 0x6A, 0x57));
    private static readonly SolidColorBrush EvenSub = Freeze(Color.FromRgb(0xD7, 0xE5, 0xF2));
    private static readonly SolidColorBrush OddSub = Freeze(Color.FromRgb(0xD4, 0xED, 0xE4));
    private static readonly SolidColorBrush EvenRead = Freeze(Color.FromRgb(0xE7, 0xF0, 0xF8));
    private static readonly SolidColorBrush OddRead = Freeze(Color.FromRgb(0xE5, 0xF4, 0xEE));

    private ConsumptionVm? _vm;
    private readonly List<Border> _bands = [];
    private ScrollViewer? _gridScroll;
    private bool _rebuilding;
    private bool _placing;
    private Guid[]? _builtWarehouseIds;
    private string? _builtGroup;

    public ConsumptionView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
        Loaded += (_, _) =>
        {
            Rebuild(force: false);
            ScheduleAlign();
        };
        Sheet.SizeChanged += (_, _) => AlignBands();
        SizeChanged += (_, _) => AlignBands();
        Sheet.BeginningEdit += (_, e) =>
        {
            e.Cancel = true;
        };
    }

    private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is not OpeningLotRowVm row)
            return;
        e.Row.Background = BrushOf(row.RowBackground);
        var fg = BrushOf(row.RowForeground);
        e.Row.Foreground = fg;
        TextElement.SetForeground(e.Row, fg);
        e.Row.FontWeight = row.RowFontWeight;
    }

    private static SolidColorBrush BrushOf(string hex)
    {
        if (BrushCache.TryGetValue(hex, out var cached))
            return cached;
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        BrushCache[hex] = brush;
        return brush;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        CommitGrid();
        if (DataContext is ConsumptionVm vm)
            vm.SaveCommand.Execute(null);
    }

    private void AddClick(object sender, RoutedEventArgs e)
    {
        CommitGrid();
        if (DataContext is ConsumptionVm vm)
            vm.AddLotCommand.Execute(null);
    }

    private void RemoveClick(object sender, RoutedEventArgs e)
    {
        CommitGrid();
        if (DataContext is ConsumptionVm vm)
            vm.RemoveLotCommand.Execute(null);
    }

    private void OpenQuarterClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ConsumptionVm vm)
            vm.OpenQuarterCommand.Execute(vm.SelectedQuarter);
    }

    private void OpenBookClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src
            && (FindAncestor<CheckBox>(src) is not null || FindAncestor<Button>(src) is not null))
            return;
        if (sender is not DataGrid grid || grid.SelectedItem is not ConsumptionBookListItemVm item)
            return;
        if (DataContext is ConsumptionVm vm)
            vm.OpenBookFromListCommand.Execute(item);
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match)
                return match;
            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    private void CommitGrid()
    {
        Sheet.CommitEdit(DataGridEditingUnit.Cell, true);
        Sheet.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void Hook()
    {
        if (_vm is not null)
            _vm.SheetReady -= OnSheetReady;
        _vm = DataContext as ConsumptionVm;
        if (_vm is not null)
            _vm.SheetReady += OnSheetReady;
        Rebuild(force: false);
    }

    private void OnSheetReady() => Rebuild(force: false);

    private void Rebuild(bool force)
    {
        if (_vm is null || Sheet is null)
            return;

        // Sheet đang ẩn (danh sách 4 quý): giữ cột/bands đã dựng.
        if (!_vm.IsSheetOpen)
            return;

        var ids = _vm.Warehouses.Select(x => x.Id).ToArray();
        var group = _vm.ConsumerGroup;
        if (!force
            && _builtWarehouseIds is not null
            && _builtWarehouseIds.AsSpan().SequenceEqual(ids)
            && _builtGroup == group
            && Sheet.Columns.Count > 0)
        {
            ScheduleAlign();
            return;
        }

        _rebuilding = true;
        if (_gridScroll is not null)
            _gridScroll.ScrollChanged -= GridScrolled;
        _gridScroll = null;
        try
        {
            Sheet.Columns.Clear();
            Sheet.Columns.Add(ConsumptionItemColumn());
            var lotType = Controls.SheetColumns.LotTypeColumn(_vm.LotTypes);
            lotType.IsReadOnly = true;
            lotType.CellEditingTemplate = null;
            StyleIdentityHeader(lotType);
            Sheet.Columns.Add(lotType);
            var price = new DataGridTextColumn
            {
                Header = "Đơn giá",
                Binding = new Binding(nameof(OpeningLotRowVm.Price)) { Mode = BindingMode.OneWay },
                Width = 120,
                IsReadOnly = true
            };
            StyleIdentityHeader(price);
            Sheet.Columns.Add(price);
            for (var i = 0; i < _vm.Warehouses.Count; i++)
            {
                var warehouse = _vm.Warehouses[i];
                var cellIndex = _vm.CellIndex(warehouse.Id);
                if (cellIndex < 0)
                    continue;
                AddWarehouseColumns(cellIndex, i, warehouse);
            }

            BuildBands();
            _builtWarehouseIds = ids;
            _builtGroup = group;
        }
        finally
        {
            _rebuilding = false;
        }

        ScheduleAlign();
    }

    private void ScheduleAlign() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, AlignBands);

    private void AddWarehouseColumns(int cellIndex, int visibleIndex, WarehouseRow warehouse)
    {
        var even = visibleIndex % 2 == 0;
        Sheet.Columns.Add(EntryColumn(cellIndex, 140, even, edge: true));
    }

    private DataGridTemplateColumn ConsumptionItemColumn()
    {
        var display = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(OpeningLotRowVm.ItemDisplayName)));
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 0, 0));
        text.SetBinding(TextBlock.FontWeightProperty, new Binding(nameof(OpeningLotRowVm.RowFontWeight)));
        text.SetBinding(TextBlock.ForegroundProperty, new Binding("(TextElement.Foreground)")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1)
        });
        var centerWhenHeader = new Style(typeof(TextBlock));
        var headerAlign = new DataTrigger { Binding = new Binding(nameof(OpeningLotRowVm.IsSheetHeader)), Value = true };
        headerAlign.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));
        headerAlign.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
        headerAlign.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(0)));
        centerWhenHeader.Triggers.Add(headerAlign);
        text.SetValue(FrameworkElement.StyleProperty, centerWhenHeader);
        display.VisualTree = text;

        var column = new DataGridTemplateColumn
        {
            Header = "Mặt hàng",
            Width = 240,
            IsReadOnly = true,
            CellTemplate = display
        };
        StyleIdentityHeader(column);
        return column;
    }

    private void BuildBands()
    {
        BandRow.Children.Clear();
        _bands.Clear();
        if (_vm is null)
            return;
        const int span = 1;
        for (var i = 0; i < _vm.Warehouses.Count; i++)
        {
            var warehouse = _vm.Warehouses[i];
            var name = warehouse.Name;
            var clickable = warehouse.IsConsumerLocation
                && warehouse.ConsumerTypeName is "Máy" or "Phương tiện" or "Tàu";
            var label = new TextBlock
            {
                Text = name,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0),
                ToolTip = clickable
                    ? warehouse.ConsumerTypeName == "Tàu"
                        ? $"{name} — bấm để mở sổ tiêu thụ tàu"
                        : $"{name} — bấm để xem phiếu điều chuyển trong quý"
                    : name,
                IsHitTestVisible = false
            };

            var band = new Border
            {
                Width = GroupWidth(i, span),
                Height = BandHeight,
                Background = i % 2 == 0 ? EvenBand : OddBand,
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = label,
                Cursor = clickable ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                ToolTip = clickable
                    ? warehouse.ConsumerTypeName == "Tàu"
                        ? $"{name} — bấm để mở sổ tiêu thụ tàu"
                        : $"{name} — bấm để xem phiếu điều chuyển trong quý"
                    : name
            };
            if (clickable)
            {
                var captured = warehouse;
                band.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    if (captured.ConsumerTypeName == "Tàu")
                        _ = _vm.ShowShipQuarterBook(captured);
                    else
                        _ = _vm.ShowConsumerTransfers(captured);
                };
            }

            _bands.Add(band);
            BandRow.Children.Add(band);
        }
    }

    private void AlignBands()
    {
        if (_rebuilding || _placing || _vm is null || _bands.Count != _vm.Warehouses.Count)
            return;
        _placing = true;
        try
        {
            HookScroll();
            var frozen = FrozenWidth();
            if (Math.Abs(FrozenBand.Width - frozen) > 0.5)
                FrozenBand.Width = frozen;
            const int span = 1;
            for (var i = 0; i < _bands.Count; i++)
            {
                var width = GroupWidth(i, span);
                if (width > 0 && Math.Abs(_bands[i].Width - width) > 0.5)
                    _bands[i].Width = width;
            }

            var inset = RightInset();
            var margin = BandScroll.Margin;
            if (Math.Abs(margin.Right - inset) > 0.5)
                BandScroll.Margin = new Thickness(Sheet.BorderThickness.Left, 0, inset, 0);
            if (_gridScroll is not null && Math.Abs(BandScroll.HorizontalOffset - _gridScroll.HorizontalOffset) > 0.5)
                BandScroll.ScrollToHorizontalOffset(_gridScroll.HorizontalOffset);
        }
        finally
        {
            _placing = false;
        }
    }

    private void HookScroll()
    {
        if (_gridScroll is not null)
            return;
        var viewer = FindScroll(Sheet);
        if (viewer is null)
            return;
        _gridScroll = viewer;
        _gridScroll.ScrollChanged += GridScrolled;
    }

    private void GridScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (_gridScroll is null)
            return;
        if (e.HorizontalChange != 0
            && Math.Abs(BandScroll.HorizontalOffset - _gridScroll.HorizontalOffset) > 0.5)
            BandScroll.ScrollToHorizontalOffset(_gridScroll.HorizontalOffset);
        if (e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)
            AlignBands();
    }

    private double FrozenWidth()
    {
        double width = Sheet.BorderThickness.Left;
        var count = Math.Min(3, Sheet.Columns.Count);
        for (var i = 0; i < count; i++)
            width += ColumnWidth(Sheet.Columns[i], i switch { 0 => 240, 1 => 88, _ => 120 });
        return width;
    }

    private double GroupWidth(int index, int span)
    {
        double width = 0;
        for (var k = 0; k < span; k++)
        {
            var column = 3 + index * span + k;
            if (column >= Sheet.Columns.Count)
                break;
            var fallback = span == 1 ? 140 : k == 1 ? 88 : 84;
            width += ColumnWidth(Sheet.Columns[column], fallback);
        }

        return width;
    }

    private static double ColumnWidth(DataGridColumn column, double fallback)
    {
        var actual = column.ActualWidth;
        if (actual > 1)
            return actual;
        var length = column.Width;
        return length.IsAbsolute && length.Value > 1 ? length.Value : fallback;
    }

    private double RightInset()
    {
        var bar = _gridScroll is { ComputedVerticalScrollBarVisibility: Visibility.Visible }
            ? SystemParameters.VerticalScrollBarWidth
            : 0;
        return Sheet.BorderThickness.Right + bar;
    }

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is ScrollViewer viewer)
                return viewer;
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
                queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }

        return null;
    }

    private static void StyleIdentityHeader(DataGridColumn column)
    {
        var style = new Style(typeof(DataGridColumnHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xF4, 0xF7, 0xFA))));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x1B, 0x28, 0x36))));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        column.HeaderStyle = style;
    }

    private static DataGridTextColumn ReadColumn(string role, string path, double width, bool even, bool edge)
    {
        var text = new Style(typeof(TextBlock));
        text.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));
        text.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        text.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x1B, 0x28, 0x36))));
        return new DataGridTextColumn
        {
            Header = RoleHeader(role, even, edge),
            Binding = new Binding(path),
            IsReadOnly = true,
            Width = width,
            ElementStyle = text,
            CellStyle = CellStyle(even ? EvenRead : OddRead, edge),
            HeaderStyle = HeaderChrome(even)
        };
    }

    private static DataGridTemplateColumn EntryColumn(int index, double width, bool even, bool edge)
    {
        var path = $"Cells[{index}].Quantity";
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(path));
        text.SetBinding(TextBlock.FontWeightProperty, new Binding(nameof(OpeningLotRowVm.RowFontWeight)));
        text.SetBinding(TextBlock.ForegroundProperty, new Binding("(TextElement.Foreground)")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1)
        });
        text.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.PaddingProperty, new Thickness(2, 0, 2, 0));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        return new DataGridTemplateColumn
        {
            Header = RoleHeader("Tiêu thụ", even, edge),
            IsReadOnly = true,
            CellTemplate = new DataTemplate { VisualTree = text },
            Width = width,
            CellStyle = TotalAwareCellStyle(even ? EvenRead : OddRead, edge),
            HeaderStyle = HeaderChrome(even)
        };
    }

    private static Style TotalAwareCellStyle(Brush background, bool edge)
    {
        var style = CellStyle(background, edge);
        var trigger = new DataTrigger
        {
            Binding = new Binding(nameof(OpeningLotRowVm.IsSheetHeader)),
            Value = true
        };
        // Nền/ chữ do LoadingRow (màu nhóm/loại lô); ô không đè màu.
        trigger.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Triggers.Add(trigger);
        return style;
    }

    private static Border RoleHeader(string role, bool even, bool edge)
    {
        return new Border
        {
            Background = even ? EvenSub : OddSub,
            BorderBrush = edge ? Edge : Line,
            BorderThickness = new Thickness(0, 0, edge ? 2 : 1, 0),
            Child = new TextBlock
            {
                Text = role,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x28, 0x36)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static Style CellStyle(Brush background, bool edge)
    {
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(Control.BackgroundProperty, background));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x1B, 0x28, 0x36))));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, edge ? Edge : Line));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, edge ? 2 : 1, 0)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, background));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x1B, 0x28, 0x36))));
        style.Triggers.Add(selected);
        return style;
    }

    private static Style HeaderChrome(bool even)
    {
        var style = new Style(typeof(DataGridColumnHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty, even ? EvenSub : OddSub));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        return style;
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
