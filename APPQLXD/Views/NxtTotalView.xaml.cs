using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using APPQLXD.ViewModels;

namespace APPQLXD.Views;

public partial class NxtTotalView : UserControl
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.OrdinalIgnoreCase);

    public NxtTotalView()
    {
        InitializeComponent();
    }

    private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is not NxtTotalRowVm row)
            return;
        e.Row.Background = BrushOf(row.RowBackground);
        e.Row.Foreground = BrushOf(row.RowForeground);
        e.Row.FontWeight = row.FontWeight;
    }

    private static SolidColorBrush BrushOf(string hex)
    {
        if (Brushes.TryGetValue(hex, out var cached))
            return cached;
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        Brushes[hex] = brush;
        return brush;
    }
}
