using System.Windows;
using System.Windows.Controls;
using APPQLXD.Core;
using APPQLXD.Core.Domain;
using APPQLXD.ViewModels;

namespace APPQLXD.Views;

public static class SlipEditWindow
{
    private static Window? _open;

    public static void Open(FuelSystem system, DocumentKind kind, Guid id, Action saved)
    {
        _open?.Close();
        PageVm page = kind switch
        {
            DocumentKind.Import => new VoucherDeskVm(system, exportDesk: false),
            DocumentKind.Issue or DocumentKind.Transfer or DocumentKind.LotConvert => new VoucherDeskVm(system, exportDesk: true),
            DocumentKind.Consumption when system.GetDocument(id)?.Distance is not null => new VoucherDeskVm(system, exportDesk: true),
            DocumentKind.Consumption or DocumentKind.Auxiliary => new ConsumptionVm(system),
            DocumentKind.Opening => new OpeningVm(system),
            _ => throw new InvalidOperationException("Không mở được phiếu này.")
        };

        if (page is VoucherDeskVm desk)
            desk.PrepareEdit();
        else
            page.Refresh();
        if (page is IDocumentEditor editor)
            editor.EditDocument(id);

        var work = SystemParameters.WorkArea;
        // Bảng phiếu (cột MinWidth) ~1150px + padding/chrome → cần ~1280; lấy phần lớn màn hình.
        var width = Math.Max(1280, Math.Min(work.Width * 0.92, work.Width - 40));
        var height = Math.Max(720, Math.Min(work.Height * 0.9, work.Height - 40));
        var window = new Window
        {
            Title = "Sửa phiếu",
            Width = width,
            Height = height,
            MinWidth = Math.Min(1280, work.Width - 40),
            MinHeight = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            Content = new ScrollViewer
            {
                // Không Auto ngang: đo vô hạn làm cột * thành Auto → lệch header/dòng so với tab.
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new ContentControl
                {
                    Content = page,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch
                }
            }
        };
        var refreshed = false;
        void RefreshBook()
        {
            if (refreshed)
                return;
            refreshed = true;
            saved();
        }

        var closing = false;
        void ClosePopup()
        {
            if (closing)
                return;
            closing = true;
            window.Content = null;
            window.Close();
        }

        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PageVm.Banner) && page.LastSavedId is not null && !page.BannerIsError)
            {
                RefreshBook();
                ClosePopup();
            }

            if (LeftTheEditor(page, e.PropertyName))
                ClosePopup();
        };
        window.Closed += (_, _) =>
        {
            if (_open == window)
                _open = null;
            window.Dispatcher.BeginInvoke(RefreshBook, System.Windows.Threading.DispatcherPriority.Background);
        };
        _open = window;
        window.Show();
    }

    private static bool LeftTheEditor(PageVm page, string? property) =>
        page switch
        {
            VoucherDeskVm desk when property == nameof(VoucherDeskVm.IsFormOpen) => !desk.IsFormOpen,
            ConsumptionVm sheet when property == nameof(ConsumptionVm.IsSheetOpen) => !sheet.IsSheetOpen,
            _ => false
        };
}
