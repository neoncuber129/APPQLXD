using System.Globalization;
using System.Text;

namespace APPQLXD.Core.Calculations;

/// <summary>Gợi ý tồn theo loại lô (tooltip) trên sổ tiêu thụ quý.</summary>
public static class LotBalanceTips
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public static void Add(Dictionary<string, decimal> map, string? lotTypeCode, decimal delta)
    {
        if (delta == 0)
            return;
        var code = string.IsNullOrWhiteSpace(lotTypeCode) ? "TX" : lotTypeCode.Trim();
        map[code] = QuantityMath.Whole(map.GetValueOrDefault(code) + delta);
        if (map[code] == 0)
            map.Remove(code);
    }

    public static string Format(IReadOnlyDictionary<string, decimal> map)
    {
        if (map.Count == 0)
            return "";
        var sb = new StringBuilder();
        foreach (var (code, qty) in map.OrderBy(x => Rank(x.Key)).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (qty == 0)
                continue;
            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(code).Append(": ").Append(qty.ToString("N0", Vi));
        }

        return sb.ToString();
    }

    public static Dictionary<string, decimal> Parse(string? tip)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(tip))
            return map;
        foreach (var line in tip.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = line.IndexOf(':');
            if (idx <= 0)
                continue;
            var code = line[..idx].Trim();
            var qtyText = line[(idx + 1)..].Trim();
            if (code.Length == 0)
                continue;
            if (decimal.TryParse(qtyText, NumberStyles.Number, Vi, out var qty)
                || decimal.TryParse(qtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out qty))
                map[code] = QuantityMath.Whole(qty);
        }

        return map;
    }

    public static int Rank(string code) =>
        code.Trim().ToUpperInvariant() switch
        {
            "TX" => 0,
            "SSCĐ" or "SSCD" => 1,
            "IUU" => 2,
            _ => 50
        };
}
