using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using APPQLXD.Views;

namespace APPQLXD;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        ApplyVietnamCulture();
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("vi-VN")));
        var icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((_, args) =>
        {
            if (args.Source is Window window && window.Icon is null)
                window.Icon = icon;
        }));
        EventManager.RegisterClassHandler(typeof(DatePicker), FrameworkElement.LoadedEvent, new RoutedEventHandler((_, args) =>
        {
            if (args.Source is not DatePicker picker)
                return;
            picker.Language = XmlLanguage.GetLanguage("vi-VN");
            picker.SelectedDateFormat = DatePickerFormat.Short;
        }));
        base.OnStartup(e);
        Dispatcher.UnhandledExceptionFilter += (_, args) => args.RequestCatch = true;
        DispatcherUnhandledException += OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobservedTask;

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var splash = new StartupSplashWindow();
        MainWindow = splash;
        splash.Show();
        splash.Report("Đang khởi động...", 0, 4);
        await Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            splash.Report("Đang chuẩn bị mẫu xuất Word...", 1, 4);
            await Task.Run(() => Core.Export.SampleTemplateBuilder.EnsureDefaults(AppContext.BaseDirectory));

            splash.Report("Đang mở cơ sở dữ liệu...", 2, 4);
            var system = await Task.Run(Core.FuelSystem.CreateDefault);

            splash.Report("Đang khởi tạo giao diện...", 3, 4);
            await Dispatcher.Yield(DispatcherPriority.Background);
            ApplyVietnamCulture();
            var mainVm = new ViewModels.MainVm(system);

            splash.Report("Sắp xong...", 4, 4);
            await Dispatcher.Yield(DispatcherPriority.Render);

            var window = new MainWindow { DataContext = mainVm };
            MainWindow = window;
            window.Show();
            splash.Close();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception ex)
        {
            ErrorLog.Record(ex, "Khởi động");
            try { splash.Close(); } catch { /* ignore */ }
            ShowStartupLog();
            Shutdown(1);
        }
    }

    private static void ApplyVietnamCulture()
    {
        var vietnam = (CultureInfo)CultureInfo.GetCultureInfo("vi-VN").Clone();
        vietnam.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
        vietnam.DateTimeFormat.LongDatePattern = "dd MMMM yyyy";
        vietnam.DateTimeFormat.FullDateTimePattern = "dd MMMM yyyy HH:mm:ss";
        vietnam.DateTimeFormat.DateSeparator = "/";
        CultureInfo.DefaultThreadCurrentCulture = vietnam;
        CultureInfo.DefaultThreadCurrentUICulture = vietnam;
        Thread.CurrentThread.CurrentCulture = vietnam;
        Thread.CurrentThread.CurrentUICulture = vietnam;
    }

    private static void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try
        {
            ErrorLog.Record(e.Exception, "Giao diện");
        }
        catch
        {
            /* nhật ký không được làm ứng dụng thoát */
        }
    }

    private static void OnDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            ErrorLog.Record(ex, "Tiến trình");
    }

    private static void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ErrorLog.Record(e.Exception, "Tác vụ");
        e.SetObserved();
    }

    private static void ShowStartupLog()
    {
        var text = new System.Windows.Controls.TextBox
        {
            Text = string.Join(Environment.NewLine + Environment.NewLine, ErrorLog.Snapshot().Select(x => x.Detail)),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12
        };
        var window = new Window
        {
            Title = "Nhật ký lỗi",
            Width = 760,
            Height = 480,
            Content = text,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        window.ShowDialog();
    }
}
