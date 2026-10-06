using System.Windows;
using APPQLXD.ViewModels;

namespace APPQLXD.Views;

public partial class NxtWarehouseWindow : Window
{
    public NxtWarehouseWindow() => InitializeComponent();

    public static bool Show(System.Collections.Generic.IEnumerable<NxtWarehouseVm> warehouses)
    {
        var window = new NxtWarehouseWindow { DataContext = warehouses };
        if (Application.Current?.MainWindow is Window owner && owner.IsLoaded)
            window.Owner = owner;
        return window.ShowDialog() == true;
    }

    private void OkClick(object sender, RoutedEventArgs e) => DialogResult = true;
}