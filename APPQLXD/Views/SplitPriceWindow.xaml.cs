using System.Windows;
using System.Windows.Controls;
using APPQLXD.Core.Calculations;
using APPQLXD.ViewModels;

namespace APPQLXD.Views;

public partial class SplitPriceWindow : Window
{
    private const int PageSize = 12;
    private readonly IReadOnlyList<FloorSplitPair> _pairs;
    private int _page;

    public FloorSplitPair? Selected { get; private set; }

    public SplitPriceWindow(decimal actual, decimal amount, IReadOnlyList<FloorSplitPair> pairs)
    {
        InitializeComponent();
        _pairs = pairs;
        var floor = pairs.Count > 0 ? pairs[0].Price1 : decimal.Floor(actual <= 0 ? 0 : amount / actual);
        Summary.Text = $"Thực nhập {Numbers.Qty(actual)} · Thành tiền {Numbers.Money(amount)} · Đơn giá phần nguyên {Numbers.Money(floor)} · {pairs.Count} cách";
        if (pairs.Count == 0)
        {
            EmptyText.Visibility = Visibility.Visible;
            PrevButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            PageText.Text = "Trang 0 / 0";
            return;
        }

        ShowPage();
    }

    private int PageCount => Math.Max(1, (_pairs.Count + PageSize - 1) / PageSize);

    private void ShowPage()
    {
        PageList.Children.Clear();
        var start = _page * PageSize;
        var end = Math.Min(start + PageSize, _pairs.Count);
        for (var i = start; i < end; i++)
        {
            var pair = _pairs[i];
            var button = new Button
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1B, 0x28, 0x36)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Content = $"{Numbers.Qty(pair.Quantity1)} × {Numbers.Money(pair.Price1)} = {Numbers.Money(pair.Amount1)} ; {Numbers.Qty(pair.Quantity2)} × {Numbers.Money(pair.Price2)} = {Numbers.Money(pair.Amount2)}",
                Tag = pair
            };
            button.Click += ChooseClick;
            PageList.Children.Add(button);
        }

        PageText.Text = $"Trang {_page + 1} / {PageCount}";
        PrevButton.IsEnabled = _page > 0;
        NextButton.IsEnabled = _page + 1 < PageCount;
    }

    private void ChooseClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FloorSplitPair pair })
        {
            Selected = pair;
            DialogResult = true;
        }
    }

    private void PrevClick(object sender, RoutedEventArgs e)
    {
        if (_page == 0)
            return;
        _page--;
        ShowPage();
    }

    private void NextClick(object sender, RoutedEventArgs e)
    {
        if (_page + 1 >= PageCount)
            return;
        _page++;
        ShowPage();
    }

    private void CloseClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
