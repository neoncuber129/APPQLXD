using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using APPQLXD.ViewModels;

namespace APPQLXD.Views;

public partial class CatalogHostView : UserControl
{
    private CatalogHostVm? _vm;

    public CatalogHostView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
        Loaded += (_, _) => ScrollEnd();
    }

    private void Hook()
    {
        if (_vm is not null)
            _vm.ColumnsBuilt -= ScrollEnd;
        _vm = DataContext as CatalogHostVm;
        if (_vm is not null)
            _vm.ColumnsBuilt += ScrollEnd;
    }

    private void PaperClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || DataContext is not CatalogHostVm vm) return;
        vm.Fields.PaperOnly = box.IsChecked == true;
    }

    private void SamplePress(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button button || button.DataContext is not FinderEntry entry)
            return;
        var list = FindParent<ListBox>(button);
        if (list?.DataContext is not FinderColumnVm column || DataContext is not CatalogHostVm vm)
            return;
        vm.OpenExportSample(column, entry);
    }

    private static T? FindParent<T>(DependencyObject start) where T : DependencyObject
    {
        for (var node = start; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is T match)
                return match;
        }

        return null;
    }

    private void EntrySelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (sender is not ListBox box || box.DataContext is not FinderColumnVm column) return;
        if (DataContext is CatalogHostVm vm)
            vm.Open(column);
    }

    private void DraftKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box || box.DataContext is not SampleFieldCardVm card)
            return;
        if (FindSamples(box) is SamplesVm vm)
            vm.AddDraftCommand.Execute(card);
        e.Handled = true;
    }

    private static SamplesVm? FindSamples(DependencyObject start)
    {
        for (var node = start; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ContentControl control && control.Content is SamplesVm samples)
                return samples;
        }

        return null;
    }

    private void ScrollEnd()
    {
        Dispatcher.BeginInvoke(() =>
        {
            Browser.UpdateLayout();
            Browser.ScrollToHorizontalOffset(Browser.ScrollableWidth);
        }, DispatcherPriority.Background);
    }
}
