using System.Globalization;

namespace APPQLXD.Core.Calculations;

/// <summary>
/// Nhập giờ hoạt động tàu dạng <c>x.y'</c>: x = giờ, y = phút (0–59).
/// Lưu/tính bằng số giờ thập phân (ví dụ 2.30' → 2,5 giờ).
/// </summary>
public static class ShipHourFormat
{
    /// <summary>Parse "2.30'" / "2.30" / "2,30" / "2" → số giờ thập phân.</summary>
    public static bool TryParse(string? text, out decimal hours)
    {
        hours = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim().TrimEnd('\'').Trim();
        if (s.Length == 0)
            return false;

        var sep = s.IndexOfAny(['.', ',']);
        if (sep < 0)
        {
            if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var whole) || whole < 0)
                return false;
            hours = whole;
            return true;
        }

        var left = s[..sep].Trim();
        var right = s[(sep + 1)..].Trim();
        if (left.Length == 0)
            left = "0";
        if (!int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var h) || h < 0)
            return false;
        if (right.Length == 0)
        {
            hours = h;
            return true;
        }

        if (!int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || minutes is < 0 or > 59)
            return false;

        hours = QuantityMath.RoundQty(h + minutes / 60m);
        return true;
    }

    /// <summary>Hiển thị số giờ thập phân thành <c>H.MM'</c> (phút luôn 2 chữ số).</summary>
    public static string Format(decimal? hours)
    {
        if (hours is null)
            return "";
        var value = QuantityMath.RoundQty(hours.Value);
        if (value <= 0)
            return "";

        var totalMinutes = (int)Math.Round(value * 60m, MidpointRounding.AwayFromZero);
        if (totalMinutes <= 0)
            return "";

        var h = totalMinutes / 60;
        var m = totalMinutes % 60;
        // Giờ chẵn: chỉ hiện số giờ, không kèm .00'
        if (m == 0)
            return h.ToString(CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{h}.{m:D2}'");
    }

    public static string Format(decimal hours) => Format((decimal?)hours);
}
