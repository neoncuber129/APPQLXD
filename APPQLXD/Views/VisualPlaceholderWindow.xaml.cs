using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using APPQLXD.Core.Export;

namespace APPQLXD.Views;

public partial class VisualPlaceholderWindow : Window
{
    private readonly IReadOnlyList<PlaceholderInfo> _placeholders;

    public VisualPlaceholderWindow(ExportDocumentKind kind, IReadOnlyList<PlaceholderInfo> placeholders)
    {
        InitializeComponent();
        _placeholders = placeholders;
        var title = kind switch
        {
            ExportDocumentKind.PhieuNhap => "Placeholder — Phiếu nhập",
            ExportDocumentKind.PhieuXuat => "Placeholder — Phiếu xuất",
            ExportDocumentKind.SoTtMayXe => "Placeholder — Sổ TT máy/xe",
            ExportDocumentKind.SoTtTau => "Placeholder — Sổ TT tàu",
            ExportDocumentKind.SoNxt => "Placeholder — Sổ NXT",
            ExportDocumentKind.SoNxtTong => "Placeholder — NXT tổng",
            _ => "Placeholder"
        };
        Title = title;
        TitleText.Text = title;
        ListGrid.ItemsSource = placeholders
            .Select(p => new ListRow(p.Token, p.Description, p.IsRowField ? "Có" : ""))
            .ToList();
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void CopyAllClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(string.Join(Environment.NewLine, _placeholders.Select(x => x.Token)));
        StatusText.Text = $"Đã copy {_placeholders.Count} mã.";
    }

    private void ListGridPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindListRow(e.OriginalSource as DependencyObject) is not { } row)
            return;
        Clipboard.SetText(row.Token);
        StatusText.Text = $"Đã copy {row.Token}";
        e.Handled = true;
    }

    private static ListRow? FindListRow(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is DataGridRow { Item: ListRow row })
                return row;
            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private sealed record ListRow(string Token, string Description, string RowLabel);
}
