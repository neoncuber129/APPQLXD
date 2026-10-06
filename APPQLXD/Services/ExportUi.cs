using System.Diagnostics;
using System.IO;
using System.Windows;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Export;
using APPQLXD.Core.Models;
using APPQLXD.ViewModels;
using Microsoft.Win32;

namespace APPQLXD.Services;

public static class ExportUi
{
    public static ExportService CreateService() =>
        new(AppContext.BaseDirectory, CurrentWarehouseScope());

    public static ExportService CreateService(WarehouseScope scope) =>
        new(AppContext.BaseDirectory, scope);

    /// <summary>Đọc phạm vi kho trên UI thread — MainWindow không truy cập từ Task.Run.</summary>
    public static WarehouseScope CurrentWarehouseScope()
    {
        var app = Application.Current;
        if (app is null)
            return WarehouseScope.Xd;
        if (app.Dispatcher.CheckAccess())
            return ReadWarehouseScope(app);
        return app.Dispatcher.Invoke(() => ReadWarehouseScope(app));
    }

    private static WarehouseScope ReadWarehouseScope(Application app)
    {
        if (app.MainWindow?.DataContext is MainVm main)
            return main.IsPtktScope ? WarehouseScope.Ptkt : WarehouseScope.Xd;
        return WarehouseScope.Xd;
    }

    /// <summary>Chạy công việc xuất trên nền, hiện thanh tiến trình overlay MainWindow.</summary>
    public static async Task<T?> RunBusyAsync<T>(string title, Func<IProgress<DemoProgress>, T> work)
    {
        if (Application.Current?.MainWindow?.DataContext is MainVm main)
        {
            T? result = default;
            Exception? error = null;
            await main.Track(title, async progress =>
            {
                try
                {
                    result = await Task.Run(() => work(progress));
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            if (error is not null)
                throw error;
            return result;
        }

        return await Task.Run(() => work(new Progress<DemoProgress>(_ => { })));
    }

    public static MultiExportMode? AskMultiMode(int count)
    {
        if (count <= 1)
            return MultiExportMode.IndividualFiles;

        var window = new Window
        {
            Title = "Xuất nhiều bản",
            Width = 420,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current.MainWindow
        };

        MultiExportMode? chosen = null;
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = $"Đã chọn {count} bản. Chọn cách xuất:",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        void AddBtn(string label, MultiExportMode mode)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = label,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(12, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            btn.Click += (_, _) =>
            {
                chosen = mode;
                window.DialogResult = true;
            };
            panel.Children.Add(btn);
        }

        AddBtn("Một file Word (ngắt trang mỗi bản)", MultiExportMode.SingleFileWithPageBreaks);
        AddBtn("File nén (.zip)", MultiExportMode.Zip);
        AddBtn("Từng file riêng", MultiExportMode.IndividualFiles);

        var cancel = new System.Windows.Controls.Button
        {
            Content = "Hủy",
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        cancel.Click += (_, _) => window.DialogResult = false;
        panel.Children.Add(cancel);

        window.Content = panel;
        return window.ShowDialog() == true ? chosen : null;
    }

    public static bool SaveArtifacts(IReadOnlyList<ExportArtifact> artifacts, string defaultFileName)
    {
        if (artifacts.Count == 0)
            return false;

        if (artifacts.Count == 1)
        {
            var a = artifacts[0];
            var ext = Path.GetExtension(a.FileName).ToLowerInvariant();
            var filter = ext switch
            {
                ".xlsx" => "Excel (*.xlsx)|*.xlsx",
                ".zip" => "Zip (*.zip)|*.zip",
                _ => "Word (*.docx)|*.docx"
            };
            var dialog = new SaveFileDialog
            {
                Title = "Lưu file xuất",
                Filter = filter,
                FileName = string.IsNullOrWhiteSpace(defaultFileName) ? a.FileName : defaultFileName,
                AddExtension = true,
                DefaultExt = ext.TrimStart('.')
            };
            if (dialog.ShowDialog() != true)
                return false;
            File.WriteAllBytes(dialog.FileName, a.Bytes);
            MessageBox.Show($"Đã lưu:\n{dialog.FileName}", "Xuất file", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        var folderDialog = new OpenFolderDialog
        {
            Title = "Chọn thư mục lưu các file"
        };
        if (folderDialog.ShowDialog() != true)
            return false;

        Directory.CreateDirectory(folderDialog.FolderName);
        foreach (var a in artifacts)
            File.WriteAllBytes(Path.Combine(folderDialog.FolderName, a.FileName), a.Bytes);

        MessageBox.Show($"Đã lưu {artifacts.Count} file vào:\n{folderDialog.FolderName}", "Xuất file",
            MessageBoxButton.OK, MessageBoxImage.Information);
        return true;
    }

    public static void SaveBytes(byte[] bytes, string defaultFileName, string filter, string defaultExt)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Lưu file xuất",
            Filter = filter,
            FileName = defaultFileName,
            AddExtension = true,
            DefaultExt = defaultExt
        };
        if (dialog.ShowDialog() != true)
            return;
        File.WriteAllBytes(dialog.FileName, bytes);
        MessageBox.Show($"Đã lưu:\n{dialog.FileName}", "Xuất file", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static void OpenTemplateFolder()
    {
        var svc = CreateService();
        Directory.CreateDirectory(svc.TemplateRoot);
        Directory.CreateDirectory(svc.TemplateFolder);
        // Mở thư mục gốc để thấy cả kho_xd và kho_vt.
        Process.Start(new ProcessStartInfo
        {
            FileName = svc.TemplateRoot,
            UseShellExecute = true
        });
    }

    public static void ShowPlaceholders(ExportDocumentKind kind, IReadOnlyList<Core.Models.FieldRow>? dynamicFields = null)
    {
        var svc = CreateService();
        var list = svc.Registry.List(kind, dynamicFields);
        var win = new Views.VisualPlaceholderWindow(kind, list)
        {
            Owner = Application.Current.MainWindow
        };
        win.ShowDialog();
    }
}
