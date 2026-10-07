using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using APPQLXD.ViewModels;

namespace APPQLXD.Views;

public partial class QuotaView
{
    private static readonly double[] Cols =
    [
        40, 160,
        56, 56, 56,
        48, 48, 48, 48,
        48, 48, 52, 48, 48, 52,
        56,
        52, 52, 56,
        52, 52, 56,
        52, 52, 56
    ];

    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(0x1B, 0x28, 0x36));
    private static readonly Brush HeadBg = new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF4));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x1B, 0x28, 0x36));
    private const double HeadH = 26;
    private const double RowH = 28;

    private QuotaVm? _vm;
    private bool _wired;

    public QuotaView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_wired)
            return;
        _wired = true;
        DataContextChanged += (_, _) => Attach();
        Attach();
    }

    private void Attach()
    {
        if (_vm is not null)
            _vm.PropertyChanged -= VmChanged;

        _vm = DataContext as QuotaVm;
        if (_vm is null)
            return;
        _vm.PropertyChanged += VmChanged;
        Rebuild();
    }

    private void VmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuotaVm.SheetRevision))
            Rebuild();
    }

    private void Rebuild()
    {
        SheetHost.Children.Clear();
        if (_vm is null)
            return;

        var table = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var w in Cols)
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

        for (var i = 0; i < 3; i++)
            table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeadH) });
        foreach (var _ in _vm.Rows)
            table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowH) });

        BuildHeader(table);

        var rowIndex = 3;
        foreach (var row in _vm.Rows)
        {
            PaintDataRow(table, rowIndex, row);
            rowIndex++;
        }

        SheetHost.Children.Add(table);
    }

    private static void BuildHeader(Grid table)
    {
        // Hàng 1
        HeadSpan(table, 0, 0, 1, 3, "Số TT");
        HeadSpan(table, 1, 0, 1, 3, "Nhiệm vụ");
        HeadSpan(table, 2, 0, 3, 1, "Hạn mức được phép sử dụng");
        HeadSpan(table, 5, 0, 4, 1, "Hoạt động của xe, máy, tàu");
        HeadSpan(table, 9, 0, 6, 1, "Nhiên liệu tiêu thụ");
        HeadSpan(table, 15, 0, 1, 3, "Cộng\nNLTT");
        HeadSpan(table, 16, 0, 3, 1, "Lũy tích sử dụng");
        HeadSpan(table, 19, 0, 6, 1, "So sánh giữa hạn mức và sử dụng");

        // Hàng 2
        HeadSpan(table, 2, 1, 1, 2, "Xăng");
        HeadSpan(table, 3, 1, 1, 2, "Điêzel");
        HeadSpan(table, 4, 1, 1, 2, "Cộng");
        HeadSpan(table, 5, 1, 2, 1, "Sử dụng xăng");
        HeadSpan(table, 7, 1, 2, 1, "Sử dụng điêzel");
        HeadSpan(table, 9, 1, 3, 1, "Xăng");
        HeadSpan(table, 12, 1, 3, 1, "Điêzel");
        HeadSpan(table, 16, 1, 1, 2, "Xăng");
        HeadSpan(table, 17, 1, 1, 2, "Điêzel");
        HeadSpan(table, 18, 1, 1, 2, "Tổng");
        HeadSpan(table, 19, 1, 3, 1, "Còn");
        HeadSpan(table, 22, 1, 3, 1, "Quá");

        // Hàng 3
        HeadCell(table, 5, 2, "Km");
        HeadCell(table, 6, 2, "Giờ");
        HeadCell(table, 7, 2, "Km");
        HeadCell(table, 8, 2, "Giờ");
        HeadCell(table, 9, 2, "Xe");
        HeadCell(table, 10, 2, "Máy");
        HeadCell(table, 11, 2, "Cộng");
        HeadCell(table, 12, 2, "Xe");
        HeadCell(table, 13, 2, "Máy");
        HeadCell(table, 14, 2, "Cộng");
        HeadCell(table, 19, 2, "Xăng");
        HeadCell(table, 20, 2, "Điêzel");
        HeadCell(table, 21, 2, "Tổng");
        HeadCell(table, 22, 2, "Xăng");
        HeadCell(table, 23, 2, "Điêzel");
        HeadCell(table, 24, 2, "Tổng");
    }

    private static void PaintDataRow(Grid table, int row, QuotaRowVm vm)
    {
        var ink = vm.IsHeader ? Red : Ink;
        var weight = vm.IsHeader ? FontWeights.SemiBold : FontWeights.Normal;
        Cell(table, 0, row, vm.Stt, ink, weight);
        Cell(table, 1, row, vm.Name, ink, weight, TextAlignment.Left);

        if (vm.CanEditLimit)
        {
            EditCell(table, 2, row, vm, nameof(QuotaRowVm.GasolineLimitText));
            EditCell(table, 3, row, vm, nameof(QuotaRowVm.DieselLimitText));
        }
        else
        {
            Cell(table, 2, row, vm.GasolineLimitText, ink, weight);
            Cell(table, 3, row, vm.DieselLimitText, ink, weight);
        }

        Cell(table, 4, row, vm.LimitTotal, ink, weight);
        Cell(table, 5, row, vm.GasolineKm, ink, weight);
        Cell(table, 6, row, vm.GasolineHours, ink, weight);
        Cell(table, 7, row, vm.DieselKm, ink, weight);
        Cell(table, 8, row, vm.DieselHours, ink, weight);
        Cell(table, 9, row, vm.GasolineVehicle, ink, weight);
        Cell(table, 10, row, vm.GasolineMachine, ink, weight);
        Cell(table, 11, row, vm.GasolineFuelTotal, ink, weight);
        Cell(table, 12, row, vm.DieselVehicle, ink, weight);
        Cell(table, 13, row, vm.DieselMachine, ink, weight);
        Cell(table, 14, row, vm.DieselFuelTotal, ink, weight);
        Cell(table, 15, row, vm.FuelTotal, ink, weight);
        Cell(table, 16, row, vm.CumGasoline, ink, weight);
        Cell(table, 17, row, vm.CumDiesel, ink, weight);
        Cell(table, 18, row, vm.CumTotal, ink, weight);
        Cell(table, 19, row, vm.RemainGasoline, ink, weight);
        Cell(table, 20, row, vm.RemainDiesel, ink, weight);
        Cell(table, 21, row, vm.RemainTotal, ink, weight);
        Cell(table, 22, row, vm.ExcessGasoline, ink, weight);
        Cell(table, 23, row, vm.ExcessDiesel, ink, weight);
        Cell(table, 24, row, vm.ExcessTotal, ink, weight);
    }

    private static void HeadSpan(Grid table, int col, int row, int colSpan, int rowSpan, string text)
    {
        var border = new Border
        {
            Background = HeadBg,
            BorderBrush = Line,
            BorderThickness = new Thickness(0.5),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Ink
            }
        };
        Grid.SetColumn(border, col);
        Grid.SetRow(border, row);
        if (colSpan > 1) Grid.SetColumnSpan(border, colSpan);
        if (rowSpan > 1) Grid.SetRowSpan(border, rowSpan);
        table.Children.Add(border);
    }

    private static void HeadCell(Grid table, int col, int row, string text) =>
        HeadSpan(table, col, row, 1, 1, text);

    private static void Cell(
        Grid table, int col, int row, string text, Brush ink, FontWeight weight,
        TextAlignment align = TextAlignment.Center)
    {
        var border = new Border
        {
            BorderBrush = Line,
            BorderThickness = new Thickness(0.5),
            Background = Brushes.White,
            Child = new TextBlock
            {
                Text = text ?? "",
                FontSize = 11,
                FontWeight = weight,
                Foreground = ink,
                TextAlignment = align,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(align == TextAlignment.Left ? 4 : 2, 1, 2, 1)
            }
        };
        Grid.SetColumn(border, col);
        Grid.SetRow(border, row);
        table.Children.Add(border);
    }

    private static void EditCell(Grid table, int col, int row, QuotaRowVm vm, string path)
    {
        var box = new TextBox
        {
            FontSize = 11,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 0, 2, 0)
        };
        box.SetBinding(TextBox.TextProperty, new Binding(path)
        {
            Source = vm,
            UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
            Mode = BindingMode.TwoWay
        });
        var border = new Border
        {
            BorderBrush = Line,
            BorderThickness = new Thickness(0.5),
            Background = Brushes.White,
            Child = box
        };
        Grid.SetColumn(border, col);
        Grid.SetRow(border, row);
        table.Children.Add(border);
    }
}
