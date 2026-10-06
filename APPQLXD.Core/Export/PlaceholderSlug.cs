using System.Globalization;
using System.Text;

namespace APPQLXD.Core.Export;

public static class PlaceholderSlug
{
    public static string FromVietnamese(string? text, int maxLen = 24)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "X";

        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
                continue;
            if (ch is 'đ' or 'Đ')
            {
                sb.Append('D');
                continue;
            }
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch is '-' or '_' or '/' or '\\' or '.' or ',' or ';' or ':')
                sb.Append(' ');
        }

        var ascii = sb.ToString().Normalize(NormalizationForm.FormC);
        if (ascii.Length == 0)
            return "X";

        var parts = ascii.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var pascal = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            pascal.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
                pascal.Append(part[1..].ToLowerInvariant());
        }

        var result = pascal.Length > 0 ? pascal.ToString() : "X";
        if (result.Length > maxLen)
            result = result[..maxLen];
        return result;
    }

    public static string Token(string code) =>
        code.StartsWith('$') ? code : "$" + code;
}
