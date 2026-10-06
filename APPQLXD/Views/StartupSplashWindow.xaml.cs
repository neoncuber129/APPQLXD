using System.Windows;

namespace APPQLXD.Views;

public partial class StartupSplashWindow : Window
{
    public StartupSplashWindow()
    {
        InitializeComponent();
    }

    public void Report(string status, int done, int total)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Report(status, done, total));
            return;
        }

        StatusText.Text = status;
        var max = Math.Max(1, total);
        var value = Math.Clamp(done, 0, max);
        Bar.Maximum = max;
        Bar.Value = value;
        CountText.Text = $"{value} / {max}";
        // Cho phép vẽ ngay trước bước nặng tiếp theo.
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
    }
}
