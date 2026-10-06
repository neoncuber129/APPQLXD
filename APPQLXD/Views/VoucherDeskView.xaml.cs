using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using APPQLXD.Core.Calculations;
using APPQLXD.ViewModels;

namespace APPQLXD.Views;

public partial class VoucherDeskView : UserControl
{
    private bool _splitOpen;
    private VoucherLineVm? _skippedLine;
    private string _skippedAmount = "";
    private string _skippedActual = "";
    private VoucherDeskVm? _desk;

    public VoucherDeskView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WatchDesk();
        Loaded += (_, _) =>
        {
            WatchDesk();
            ShowForm(force: true);
        };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                ShowForm(force: true);
        };
        Unloaded += (_, _) =>
        {
            if (_desk is not null)
                _desk.PropertyChanged -= OnDeskChanged;
            _desk = null;
        };
    }

    private void WatchDesk() => Watch(DataContext as VoucherDeskVm);

    private void Watch(VoucherDeskVm? desk)
    {
        if (ReferenceEquals(_desk, desk))
        {
            ShowForm(force: true);
            return;
        }

        if (_desk is not null)
            _desk.PropertyChanged -= OnDeskChanged;
        _desk = desk;
        if (_desk is not null)
            _desk.PropertyChanged += OnDeskChanged;
        ShowForm(force: true);
    }

    private void OnDeskChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VoucherDeskVm.IsFormOpen))
            ShowForm(force: true);
    }

    /// <summary>
    /// Gắn lại SlipForm vào FormHost. Sau khi PageHost detach/attach view cache,
    /// ContentControl dễ còn template/content cũ nhưng không vẽ gì (màn hình trắng).
    /// </summary>
    private void ShowForm(bool force = false)
    {
        if (!IsLoaded)
            return;

        if (_desk is { IsFormOpen: true })
        {
            var needRebuild = force
                || FormHost.ContentTemplate is null
                || !ReferenceEquals(FormHost.Content, _desk);
            if (!needRebuild)
                return;
            try
            {
                var template = (DataTemplate)FindResource("SlipForm");
                FormHost.ClearValue(ContentControl.ContentProperty);
                FormHost.ClearValue(ContentControl.ContentTemplateProperty);
                FormHost.ContentTemplate = template;
                FormHost.Content = _desk;
            }
            catch (ResourceReferenceKeyNotFoundException)
            {
                // Resources chưa sẵn — Loaded sẽ gọi lại.
            }

            return;
        }

        FormHost.ClearValue(ContentControl.ContentProperty);
        FormHost.ClearValue(ContentControl.ContentTemplateProperty);
    }

    private void LinePressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is VoucherLineVm line && DataContext is VoucherDeskVm desk)
            desk.Pick(line);
    }

    private void MoneyLostFocus(object sender, RoutedEventArgs e) =>
        TryOpenSplit(sender as FrameworkElement, honorSkip: true);

    private void MoneyKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        TryOpenSplit(sender as FrameworkElement, honorSkip: false);
        e.Handled = true;
    }

    private void SplitNoteClick(object sender, MouseButtonEventArgs e)
    {
        TryOpenSplit(sender as FrameworkElement, honorSkip: false);
        e.Handled = true;
    }

    private void TryOpenSplit(FrameworkElement? element, bool honorSkip)
    {
        if (_splitOpen || element?.DataContext is not VoucherLineVm line)
            return;
        if (DataContext is not VoucherDeskVm desk || !line.PriceFromAmount || !line.NeedsSplit)
            return;
        if (honorSkip && ReferenceEquals(_skippedLine, line) && _skippedAmount == line.Amount && _skippedActual == line.Actual)
            return;
        if (!Numbers.Try(line.Amount, out var amount) || !Numbers.Try(line.Actual, out var actual) || actual <= 0)
            return;

        _splitOpen = true;
        try
        {
            var dialog = new SplitPriceWindow(actual, amount, ImportLotSplitter.FloorPairs(actual, amount));
            var owner = Window.GetWindow(this);
            if (owner is not null)
                dialog.Owner = owner;
            if (dialog.ShowDialog() == true && dialog.Selected is FloorSplitPair pair)
            {
                if (!pair.Matches(actual, amount))
                    return;
                desk.ApplyFloorPair(line, pair);
            }
            else
            {
                _skippedLine = line;
                _skippedAmount = line.Amount;
                _skippedActual = line.Actual;
            }
        }
        finally
        {
            _splitOpen = false;
        }
    }
}
