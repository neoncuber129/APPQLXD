using System.Windows;
using APPQLXD.Core.Export;

namespace APPQLXD.Views;

public partial class PlaceholderListWindow : Window
{
    private readonly IReadOnlyList<PlaceholderRow> _rows;

    public PlaceholderListWindow(ExportDocumentKind kind, IReadOnlyList<PlaceholderInfo> placeholders)
    {
        InitializeComponent();
        TitleText.Text = kind switch
        {
            ExportDocumentKind.PhieuNhap => "Placeholder — Phiếu nhập",
            ExportDocumentKind.PhieuXuat => "Placeholder — Phiếu xuất",
            ExportDocumentKind.SoTtMayXe => "Placeholder — Sổ TT máy / phương tiện",
            ExportDocumentKind.SoTtTau => "Placeholder — Sổ TT tàu",
            ExportDocumentKind.SoNxt => "Placeholder — Sổ NXT",
            ExportDocumentKind.SoNxtTong => "Placeholder — NXT tổng",
            _ => "Placeholder"
        };
        _rows = placeholders.Select(p => new PlaceholderRow(p.Token, p.Description, p.IsRowField ? "Có" : "")).ToList();
        Grid.ItemsSource = _rows;
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void CopySelectedClick(object sender, RoutedEventArgs e)
    {
        var selected = Grid.SelectedItems.Cast<PlaceholderRow>().Select(x => x.Token).ToList();
        if (selected.Count == 0 && Grid.SelectedItem is PlaceholderRow one)
            selected.Add(one.Token);
        if (selected.Count == 0)
        {
            MessageBox.Show("Chọn ít nhất một dòng.", "Copy", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Clipboard.SetText(string.Join(Environment.NewLine, selected));
    }

    private void CopyAllClick(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(string.Join(Environment.NewLine, _rows.Select(x => x.Token)));

    private sealed record PlaceholderRow(string Token, string Description, string RowLabel);
}
