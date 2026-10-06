using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using APPQLXD.Core.Calculations;
using APPQLXD.ViewModels;

namespace APPQLXD;

public sealed class BoolVisConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

public sealed class BoolVisInverseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

public sealed class IgnoreNullConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value ?? Binding.DoNothing;
}

/// <summary>Tổng tồn + thành tiền của CollectionViewGroup (đệ quy subgroup loại lô).</summary>
public sealed class OpeningGroupTotalsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not IEnumerable items)
            return "";

        Sum(items, out var qty, out var amount);
        if (qty == 0 && amount == 0)
            return "";
        return $"Tồn {Numbers.Qty(qty)}   TT {Numbers.Money(amount)}";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static void Sum(IEnumerable items, out decimal qty, out decimal amount)
    {
        qty = 0;
        amount = 0;
        foreach (var item in items)
        {
            switch (item)
            {
                case OpeningLotRowVm row when !row.IsGroupTotal:
                    qty = QuantityMath.Whole(qty + row.TotalQty);
                    amount = QuantityMath.RoundMoney(amount + row.TotalAmount);
                    break;
                case CollectionViewGroup group:
                    Sum(group.Items, out var nestedQty, out var nestedAmount);
                    qty = QuantityMath.Whole(qty + nestedQty);
                    amount = QuantityMath.RoundMoney(amount + nestedAmount);
                    break;
            }
        }
    }
}
