namespace APPQLXD.Core.Calculations;

public static class MoneyWords
{
    private static readonly string[] Digits = ["không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín"];
    // Đủ đến ~10^18 (long): hỗ trợ đọc thành tiền tới trên hàng trăm ngàn tỉ.
    private static readonly string[] Scales = ["", "ngàn", "triệu", "tỷ", "ngàn tỷ", "triệu tỷ", "tỷ tỷ"];

    public static string ToDong(decimal amount)
    {
        var rounded = decimal.Round(amount, 0, MidpointRounding.AwayFromZero);
        if (rounded < 0)
            rounded = -rounded;
        if (rounded == 0)
            return "Không đồng.";
        if (rounded > long.MaxValue)
            return rounded.ToString("N0") + " đồng.";

        var value = (long)rounded;
        var groups = new List<int>();
        var rest = value;
        while (rest > 0)
        {
            groups.Add((int)(rest % 1000));
            rest /= 1000;
        }

        var parts = new List<string>();
        var higher = false;
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i] == 0)
                continue;
            var text = ReadBlock(groups[i], higher);
            if (i < Scales.Length && Scales[i].Length > 0)
                text += " " + Scales[i];
            parts.Add(text);
            higher = true;
        }

        var sentence = string.Join(", ", parts);
        return char.ToUpper(sentence[0]) + sentence[1..] + " đồng.";
    }

    private static string ReadBlock(int n, bool full)
    {
        var tram = n / 100;
        var chuc = n / 10 % 10;
        var don = n % 10;
        var parts = new List<string>();
        if (tram > 0 || (full && n > 0))
            parts.Add(Digits[tram] + " trăm");
        if (chuc == 0 && don > 0 && (tram > 0 || full))
            parts.Add("lẻ");
        if (chuc == 1)
            parts.Add("mười");
        else if (chuc > 1)
            parts.Add(Digits[chuc] + " mươi");
        if (don > 0)
        {
            if (don == 5 && chuc >= 1)
                parts.Add("lăm");
            else if (don == 1 && chuc > 1)
                parts.Add("mốt");
            else
                parts.Add(Digits[don]);
        }

        return string.Join(' ', parts);
    }
}
