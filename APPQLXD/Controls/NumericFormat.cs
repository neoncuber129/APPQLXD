using System.Windows;
using System.Windows.Controls;
using APPQLXD.ViewModels;

namespace APPQLXD.Controls;

/// <summary>Định dạng số Việt Nam theo thời gian thực khi người dùng gõ vào TextBox.</summary>
public static class NumericFormat
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind",
        typeof(NumericKind),
        typeof(NumericFormat),
        new PropertyMetadata(NumericKind.None, OnKindChanged));

    public static void SetKind(DependencyObject element, NumericKind value) =>
        element.SetValue(KindProperty, value);

    public static NumericKind GetKind(DependencyObject element) =>
        (NumericKind)element.GetValue(KindProperty);

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
            return;

        box.TextChanged -= OnTextChanged;
        if (e.NewValue is NumericKind kind && kind != NumericKind.None)
        {
            box.TextChanged += OnTextChanged;
            Apply(box, kind);
        }
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box)
            return;
        var kind = GetKind(box);
        if (kind == NumericKind.None)
            return;
        Apply(box, kind);
    }

    private static void Apply(TextBox box, NumericKind kind)
    {
        var text = box.Text ?? "";
        var caret = box.CaretIndex;
        var digitsBefore = CountSignificantBefore(text, caret, kind);
        var formatted = Numbers.FormatLive(text, kind);
        if (formatted == text)
            return;

        box.TextChanged -= OnTextChanged;
        try
        {
            box.Text = formatted;
            box.CaretIndex = CaretFromSignificant(formatted, digitsBefore, kind);
        }
        finally
        {
            box.TextChanged += OnTextChanged;
        }
    }

    private static int CountSignificantBefore(string text, int caret, NumericKind kind)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var count = 0;
        var seenComma = false;
        for (var i = 0; i < caret; i++)
        {
            var ch = text[i];
            if (char.IsDigit(ch))
                count++;
            else if (ch == ',' && AllowsFraction(kind) && !seenComma)
            {
                seenComma = true;
                count++;
            }
        }

        return count;
    }

    private static int CaretFromSignificant(string text, int significant, NumericKind kind)
    {
        if (significant <= 0)
            return 0;
        var count = 0;
        var seenComma = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsDigit(ch))
                count++;
            else if (ch == ',' && AllowsFraction(kind) && !seenComma)
            {
                seenComma = true;
                count++;
            }

            if (count >= significant)
                return i + 1;
        }

        return text.Length;
    }

    private static bool AllowsFraction(NumericKind kind) =>
        kind is NumericKind.Decimal or NumericKind.Factor;
}
