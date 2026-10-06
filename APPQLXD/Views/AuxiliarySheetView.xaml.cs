using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using APPQLXD.ViewModels;

namespace APPQLXD.Views;

public partial class AuxiliarySheetView : UserControl
{
    private AuxiliarySheetVm? _vm;

    public AuxiliarySheetView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
        Loaded += (_, _) => Rebuild();
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        CommitGrid();
        if (DataContext is AuxiliarySheetVm vm)
            vm.SaveCommand.Execute(null);
    }

    private void AddClick(object sender, RoutedEventArgs e)
    {
        CommitGrid();
        if (DataContext is AuxiliarySheetVm vm)
            vm.AddLotCommand.Execute(null);
    }

    private void RemoveClick(object sender, RoutedEventArgs e)
    {
        CommitGrid();
        if (DataContext is AuxiliarySheetVm vm)
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
        _vm = DataContext as AuxiliarySheetVm;
        if (_vm is not null)
            _vm.SheetReady += Rebuild;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_vm is null || Sheet is null)
            return;
        var moneyEdit = EditStyle(NumericKind.Money);
        var qtyEdit = EditStyle(NumericKind.Qty);

        Sheet.Columns.Clear();
        Sheet.Columns.Add(Controls.SheetColumns.ItemColumn(_vm.Items));
        Sheet.Columns.Add(new DataGridTextColumn
        {
            Header = "Đơn giá",
            Binding = new Binding(nameof(OpeningLotRowVm.Price)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = 120,
            EditingElementStyle = moneyEdit
        });
        Sheet.Columns.Add(Controls.SheetColumns.LotTypeColumn(_vm.LotTypes));
        for (var i = 0; i < _vm.Warehouses.Count; i++)
        {
            Sheet.Columns.Add(new DataGridTextColumn
            {
                Header = _vm.Warehouses[i].Name,
                Binding = new Binding($"Cells[{i}].Quantity") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 160,
                EditingElementStyle = qtyEdit
            });
        }
    }

    private static Style EditStyle(NumericKind kind)
    {
        var edit = new Style(typeof(TextBox));
        edit.Setters.Add(new Setter(TextBox.MaxLengthProperty, 24));
        edit.Setters.Add(new Setter(TextBox.TextWrappingProperty, TextWrapping.NoWrap));
        edit.Setters.Add(new Setter(Controls.NumericFormat.KindProperty, kind));
        return edit;
    }
}
