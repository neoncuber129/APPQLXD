using System.Windows;
using APPQLXD.Core;
using APPQLXD.Core.Models;

namespace APPQLXD.Views;

public partial class DemoProgressWindow : Window
{
    private bool _finished;

    public DemoProgressWindow()
    {
        InitializeComponent();
    }

    public FuelResult? Result { get; private set; }

    public void Start(Func<IProgress<DemoProgress>, FuelResult> work)
    {
        Loaded += async (_, _) =>
        {
            try
            {
                var progress = new Progress<DemoProgress>(Report);
                Result = await Task.Run(() => work(progress));
            }
            catch (Exception ex)
            {
                Result = FuelResult.Fail(ex.Message);
            }

            _finished = true;
            DialogResult = true;
        };
    }

    private void Report(DemoProgress progress)
    {
        PhaseText.Text = progress.Phase;
        PhaseBar.Maximum = Math.Max(1, progress.PhaseTotal);
        PhaseBar.Value = progress.PhaseDone;
        PhaseCount.Text = $"{progress.PhaseDone:N0} / {progress.PhaseTotal:N0}";
        TotalBar.Maximum = Math.Max(1, progress.Total);
        TotalBar.Value = progress.Done;
        TotalCount.Text = $"{progress.Done:N0} / {progress.Total:N0}";
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_finished)
            e.Cancel = true;
    }
}
