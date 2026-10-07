using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using APPQLXD.ViewModels;

namespace APPQLXD.Views;

public partial class NxtTotalView : UserControl
{
    private static readonly double[] Cols =
    [
        160, 65, 50,
        65, 60, 60, 60, 70, // Tồn đầu
        65, 60, 60, 60, 70, // Nhập
        65, 60, 60, 60, 70, // Xuất
        65, 60, 60, 60, 70, // Tồn sau
        140                 // Ghi chú
    ];

    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(0x1B, 0x28, 0x36));
    private static readonly Brush HeadBg = new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF4));
    private static readonly Brush HeadSubBg = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
    private static readonly Brush HeadInk = new SolidColorBrush(Color.FromRgb(0x1B, 0x28, 0x36));
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.OrdinalIgnoreCase);

    private const double HeadH = 26;
    private const double HeadSubH = 24;
    private const double RowH = 28;

    private NxtTotalVm? _vm;
    private bool _wired;

    public NxtTotalView() => InitializeComponent();

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

        _vm = DataContext as NxtTotalVm;
        if (_vm is null)
            return;
        _vm.PropertyChanged += VmChanged;
        Rebuild();
    }

    private void VmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NxtTotalVm.SheetRevision) or nameof(NxtTotalVm.Rows))
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

        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeadH) });
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeadSubH) });

        foreach (var _ in _vm.Rows)
            table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowH) });

        BuildHeader(table);

        var rowIndex = 2;
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
        HeadSpan(table, 0, 0, 1, 2, "Lô", HeadBg);
        HeadSpan(table, 1, 0, 1, 2, "Đơn giá", HeadBg);
        HeadSpan(table, 2, 0, 1, 2, "Loại lô", HeadBg);
        HeadSpan(table, 3, 0, 5, 1, "Tồn đầu", HeadBg);
        HeadSpan(table, 8, 0, 5, 1, "Nhập", HeadBg);
        HeadSpan(table, 13, 0, 5, 1, "Xuất", HeadBg);
        HeadSpan(table, 18, 0, 5, 1, "Tồn sau", HeadBg);
        HeadSpan(table, 23, 0, 1, 2, "Ghi chú", HeadBg);

        // Hàng 2
        for (var block = 0; block < 4; block++)
        {
            var c = 3 + block * 5;
            HeadCell(table, c + 0, 1, "Kho HĐ", HeadSubBg);
            HeadCell(table, c + 1, 1, "Máy", HeadSubBg);
            HeadCell(table, c + 2, 1, "PT", HeadSubBg);
            HeadCell(table, c + 3, 1, "Tàu", HeadSubBg);
            HeadCell(table, c + 4, 1, "Tổng", HeadSubBg);
        }
    }

    private static void PaintDataRow(Grid table, int row, NxtTotalRowVm vm)
    {
        var bg = BrushOf(vm.RowBackground);
        var ink = BrushOf(vm.RowForeground);
        var weight = vm.FontWeight;

        Cell(table, 0, row, vm.LotText, ink, bg, weight, TextAlignment.Left);
        Cell(table, 1, row, vm.UnitPriceText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 2, row, vm.LotTypeText, ink, bg, weight, TextAlignment.Center);

        // Tồn đầu (3..7)
        Cell(table, 3, row, vm.OpeningMainText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 4, row, vm.OpeningMachineText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 5, row, vm.OpeningVehicleText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 6, row, vm.OpeningShipText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 7, row, vm.OpeningTotalText, ink, bg, FontWeights.SemiBold, TextAlignment.Right);

        // Nhập (8..12)
        Cell(table, 8, row, vm.InMainText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 9, row, vm.InMachineText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 10, row, vm.InVehicleText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 11, row, vm.InShipText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 12, row, vm.InTotalText, ink, bg, FontWeights.SemiBold, TextAlignment.Right);

        // Xuất (13..17)
        Cell(table, 13, row, vm.OutMainText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 14, row, vm.OutMachineText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 15, row, vm.OutVehicleText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 16, row, vm.OutShipText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 17, row, vm.OutTotalText, ink, bg, FontWeights.SemiBold, TextAlignment.Right);

        // Tồn sau (18..22)
        Cell(table, 18, row, vm.ClosingMainText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 19, row, vm.ClosingMachineText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 20, row, vm.ClosingVehicleText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 21, row, vm.ClosingShipText, ink, bg, weight, TextAlignment.Right);
        Cell(table, 22, row, vm.ClosingTotalText, ink, bg, FontWeights.SemiBold, TextAlignment.Right);

        Cell(table, 23, row, vm.Note, ink, bg, weight, TextAlignment.Left);
    }

    private static void HeadSpan(Grid table, int col, int row, int colSpan, int rowSpan, string text, Brush bg)
    {
        var border = new Border
        {
            Background = bg,
            BorderBrush = Line,
            BorderThickness = new Thickness(0.5),
            Child = new TextBlock
            {
                Text = text,
                Foreground = HeadInk,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2)
            }
        };
        Grid.SetColumn(border, col);
        Grid.SetRow(border, row);
        if (colSpan > 1) Grid.SetColumnSpan(border, colSpan);
        if (rowSpan > 1) Grid.SetRowSpan(border, rowSpan);
        table.Children.Add(border);
    }

    private static void HeadCell(Grid table, int col, int row, string text, Brush bg) =>
        HeadSpan(table, col, row, 1, 1, text, bg);

    private static void Cell(
        Grid table,
        int col,
        int row,
        string text,
        Brush ink,
        Brush bg,
        FontWeight weight,
        TextAlignment align = TextAlignment.Center)
    {
        var border = new Border
        {
            Background = bg,
            BorderBrush = Line,
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(3, 1, 3, 1),
            Child = new TextBlock
            {
                Text = text,
                Foreground = ink,
                FontWeight = weight,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = align,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };
        Grid.SetColumn(border, col);
        Grid.SetRow(border, row);
        table.Children.Add(border);
    }

    private static SolidColorBrush BrushOf(string hex)
    {
        if (Brushes.TryGetValue(hex, out var cached))
            return cached;
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        Brushes[hex] = brush;
        return brush;
    }
}
