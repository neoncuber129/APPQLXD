using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using APPQLXD.ViewModels;

namespace APPQLXD.Views;

public partial class OpeningView : UserControl
{
    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);
    private OpeningVm? _vm;

    public OpeningView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
        Loaded += (_, _) => Rebuild();
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        CommitGrid();
        if (DataContext is OpeningVm vm)
            vm.SaveCommand.Execute(null);
    }

    private void AddClick(object sender, RoutedEventArgs e)
    {
        CommitGrid();
        if (_vm is null)
            return;
        var dialog = new OpeningLotWindow(_vm.Items, _vm.LotTypes, _vm.LotOrigins)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true || dialog.SelectedItem is null || dialog.SelectedLotType is null || dialog.SelectedLotOrigin is null)
            return;
        _vm.AddLotFromDialog(dialog.SelectedItem, dialog.PriceText, dialog.SelectedLotType, dialog.SelectedLotOrigin);
    }

    private void RemoveClick(object sender, RoutedEventArgs e)
    {
        CommitGrid();
        if (DataContext is OpeningVm vm)
            vm.RemoveLotCommand.Execute(null);
    }

    private void CommitGrid()
    {
        Sheet.CommitEdit(DataGridEditingUnit.Cell, true);
        Sheet.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void Hook()
    {
        if (_vm is not null)
            _vm.SheetReady -= Rebuild;
        _vm = DataContext as OpeningVm;
        if (_vm is not null)
            _vm.SheetReady += Rebuild;
        Rebuild();
    }

    private bool _rebuildQueued;

    private void Rebuild()
    {
        if (_vm is null || Sheet is null || _rebuildQueued)
            return;
        _rebuildQueued = true;
        Dispatcher.BeginInvoke(FillColumns, System.Windows.Threading.DispatcherPriority.Loaded);
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

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is OpeningLotRowVm { IsSheetHeader: true })
            e.Cancel = true;
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

    private void FillColumns()
    {
        _rebuildQueued = false;
        if (_vm is null || Sheet is null)
            return;

        var centerHeader = new Style(typeof(DataGridColumnHeader));
        centerHeader.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        centerHeader.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        Sheet.ColumnHeaderStyle = centerHeader;

        var centerCell = TextStyle(TextAlignment.Center);
        var rightCell = TextStyle(TextAlignment.Right);
        var moneyEdit = EditStyle(HorizontalAlignment.Center, NumericKind.Money);
        var qtyEdit = EditStyle(HorizontalAlignment.Center, NumericKind.Qty);

        Sheet.Columns.Clear();
        Sheet.Columns.Add(Controls.SheetColumns.OpeningItemColumn(_vm.Items));
        Sheet.Columns.Add(new DataGridTextColumn
        {
            Header = "Đơn giá",
            Binding = new Binding(nameof(OpeningLotRowVm.Price)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = 110,
            ElementStyle = centerCell,
            EditingElementStyle = moneyEdit
        });
        Sheet.Columns.Add(Controls.SheetColumns.LotTypeColumn(_vm.LotTypes));
        Sheet.Columns.Add(Controls.SheetColumns.LotOriginColumn(_vm.LotOrigins));
        Sheet.Columns.Add(new DataGridTextColumn
        {
            Header = "Tổng tồn",
            Binding = new Binding(nameof(OpeningLotRowVm.TotalQtyText)),
            Width = 100,
            IsReadOnly = true,
            ElementStyle = centerCell
        });
        Sheet.Columns.Add(new DataGridTextColumn
        {
            Header = "Tổng thành tiền",
            Binding = new Binding(nameof(OpeningLotRowVm.TotalAmountText)),
            Width = 130,
            IsReadOnly = true,
            ElementStyle = rightCell
        });
        for (var i = 0; i < _vm.Warehouses.Count; i++)
        {
            Sheet.Columns.Add(new DataGridTextColumn
            {
                Header = _vm.Warehouses[i].Name,
                Binding = new Binding($"Cells[{i}].Quantity") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 140,
                ElementStyle = centerCell,
                EditingElementStyle = qtyEdit
            });
        }
    }

    private static Style TextStyle(TextAlignment align)
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, align));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(4, 0, 4, 0)));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new Binding("(TextElement.Foreground)")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1)
        }));
        return style;
    }

    private static Style EditStyle(HorizontalAlignment align, NumericKind kind)
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(TextBox.MaxLengthProperty, 24));
        style.Setters.Add(new Setter(TextBox.TextWrappingProperty, TextWrapping.NoWrap));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, align));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(Controls.NumericFormat.KindProperty, kind));
        return style;
    }
}
