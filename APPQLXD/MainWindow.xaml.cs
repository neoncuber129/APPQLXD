using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using APPQLXD.Controls;

namespace APPQLXD;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ErrorEntry> _log = [];

    public MainWindow()
    {
        InitializeComponent();
        LogGrid.ItemsSource = _log;
        foreach (var entry in ErrorLog.Snapshot())
            _log.Add(entry);
        if (_log.Count > 0)
            ShowLog();
        ErrorLog.EntryAdded += OnEntryAdded;
        Closed += (_, _) => ErrorLog.EntryAdded -= OnEntryAdded;
    }

    private void TabsPreviewClick(object sender, MouseButtonEventArgs e) => DropDownCloser.Close(PageHost);

    private void OnEntryAdded(ErrorEntry entry)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Append(entry));
            return;
        }

        Dispatcher.BeginInvoke(() => Append(entry));
    }

    private void Append(ErrorEntry entry)
    {
        _log.Add(entry);
        ShowLog();
        try
        {
            LogGrid.UpdateLayout();
            LogGrid.ScrollIntoView(entry);
            LogGrid.SelectedItem = entry;
        }
        catch
        {
            /* bảng log không được gây thêm lỗi */
        }
    }

    private void ShowLog()
    {
        LogPanel.Visibility = Visibility.Visible;
        LogTitle.Text = _log.Count == 1 ? "Nhật ký lỗi — 1 dòng" : $"Nhật ký lỗi — {_log.Count} dòng";
    }

    private void ClearLogClick(object sender, RoutedEventArgs e)
    {
        ErrorLog.Clear();
        _log.Clear();
        LogPanel.Visibility = Visibility.Collapsed;
    }
}
