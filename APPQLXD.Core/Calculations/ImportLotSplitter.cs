namespace APPQLXD.Core.Calculations;

public sealed record SplitLine(int LineNo, long UnitPrice, decimal Quantity, decimal Amount, decimal Actual);

public readonly record struct FloorSplitPair(decimal Quantity1, long Price1, decimal Quantity2, long Price2)
{
    public decimal Amount1 => Price1 * Quantity1;
    public decimal Amount2 => Price2 * Quantity2;

    /// <summary>
    /// Cặp hợp lệ phải giữ đúng thực nhập và thành tiền người dùng đã nhập.
    /// Thành tiền mỗi lô = đơn giá × thực nhập (không làm tròn / không tính ngược tổng).
    /// </summary>
    public bool Matches(decimal actual, decimal amount) =>
        Quantity1 > 0
        && Quantity2 > 0
        && Price1 >= 0
        && Price2 > Price1
        && QuantityMath.RoundQty(Quantity1 + Quantity2) == QuantityMath.RoundQty(actual)
        && Amount1 + Amount2 == amount
        && QuantityMath.IsWholeMoney(Amount1)
        && QuantityMath.IsWholeMoney(Amount2);
}

public sealed record ImportPreview(bool Ok, string Message, IReadOnlyList<SplitLine> Lines)
{
    public static ImportPreview Success(IReadOnlyList<SplitLine> lines) => new(true, "OK", lines);
    public static ImportPreview Fail(string message) => new(false, message, []);
    public decimal TotalActual => Lines.Sum(l => l.Actual);
    public decimal TotalQuantity => Lines.Sum(l => l.Quantity);
    public decimal TotalAmount => Lines.Sum(l => l.Amount);
    public bool WasSplit => Lines.Count > 1;
}

public static class ImportLotSplitter
{
    public static ImportPreview Split(decimal unitPrice, decimal quantity, decimal? amount, decimal vcf)
    {
        if (!QuantityMath.TryWholeMoney(unitPrice, out var price) || price < 0)
            return ImportPreview.Fail("Đơn giá phải là số nguyên không âm.");

        var qty = QuantityMath.Whole(quantity);
        var factor = QuantityMath.RoundVcf(vcf);
        if (qty <= 0)
            return ImportPreview.Fail("Số lượng nhập phải lớn hơn 0.");
        if (factor <= 0)
            return ImportPreview.Fail("VCF phải lớn hơn 0.");

        var product = price * qty;
        var totalActual = QuantityMath.ActualImport(qty, factor);
        if (amount is null)
            return ImportPreview.Success([Line(1, price, qty, product, totalActual)]);

        if (!QuantityMath.IsWholeMoney(amount.Value) || amount.Value < 0)
            return ImportPreview.Fail("Thành tiền phải là số nguyên không âm (VND).");

        var target = amount.Value;
        if (product == target)
            return ImportPreview.Success([Line(1, price, qty, target, totalActual)]);

        var diff = target - product;
        var abs = Math.Abs(diff);
        var kCeiling = decimal.Ceiling(abs / qty);
        if (kCeiling < 1 || kCeiling > int.MaxValue)
            return ImportPreview.Fail("Chênh lệch thành tiền chưa tách được với đơn giá nguyên. Cần xác nhận quy tắc điều chỉnh.");

        var k = (int)kCeiling;
        var sign = diff > 0 ? 1L : -1L;
        var price2 = price + sign * k;
        if (price2 < 0)
            return ImportPreview.Fail("Không tách được lô với đơn giá nguyên không âm. Cần xác nhận quy tắc điều chỉnh.");

        var qty2 = abs / k;
        var qty1 = qty - qty2;
        if (qty1 < 0 || qty2 < 0)
            return ImportPreview.Fail("Không tách được số lượng hai lô. Cần xác nhận quy tắc điều chỉnh.");

        var lines = new List<SplitLine>();
        if (qty1 > 0)
            lines.Add(Line(lines.Count + 1, price, qty1, price * qty1, QuantityMath.Whole(qty1 * factor)));
        if (qty2 > 0)
            lines.Add(Line(lines.Count + 1, price2, qty2, price2 * qty2, QuantityMath.Whole(qty2 * factor)));
        if (lines.Count == 0)
            return ImportPreview.Fail("Không tạo được dòng lô.");

        // Không được cộng dồn phần dư vào thành tiền một lô: thành tiền mỗi lô phải = đơn giá × số lượng.
        // Nếu tổng lệch, cặp tách không hợp lệ — tránh tính ngược làm sai tổng người dùng đã nhập.
        if (lines.Sum(l => l.Amount) != target || lines.Sum(l => l.Quantity) != qty)
            return ImportPreview.Fail("Tách lô không bảo toàn số lượng hoặc thành tiền. Cần xác nhận quy tắc điều chỉnh.");

        var actualDrift = totalActual - lines.Sum(l => l.Actual);
        if (actualDrift != 0)
        {
            var index = 0;
            for (var i = 1; i < lines.Count; i++)
            {
                if (lines[i].Actual > lines[index].Actual)
                    index = i;
            }

            var host = lines[index];
            var adjusted = QuantityMath.Whole(host.Actual + actualDrift);
            if (adjusted < 0)
                return ImportPreview.Fail("Làm tròn thực nhập sau khi tách lô bị âm. Cần xác nhận quy tắc làm tròn.");
            lines[index] = host with { Actual = adjusted };
        }

        if (lines.Sum(l => l.Amount) != target || lines.Sum(l => l.Quantity) != qty)
            return ImportPreview.Fail("Tách lô không bảo toàn số lượng hoặc thành tiền. Cần xác nhận quy tắc điều chỉnh.");

        return ImportPreview.Success(lines);
    }

    private static SplitLine Line(int no, long price, decimal qty, decimal amount, decimal actual) =>
        new(no, price, qty, amount, actual);

    public static IReadOnlyList<FloorSplitPair> FloorPairs(decimal actual, decimal amount)
    {
        var qty = QuantityMath.RoundQty(actual);
        if (qty <= 0 || qty != decimal.Truncate(qty) || !QuantityMath.TryWholeMoney(amount, out var money) || money < 0)
            return [];

        const decimal scale = 10_000m;
        var qtyUnitsDec = qty * scale;
        if (qtyUnitsDec != decimal.Truncate(qtyUnitsDec) || qtyUnitsDec <= 0 || qtyUnitsDec > long.MaxValue)
            return [];

        var qtyUnits = (long)qtyUnitsDec;
        var price1 = decimal.Floor(money / qty);
        if (price1 < 0 || price1 > long.MaxValue)
            return [];

        var floorPrice = (long)price1;
        var remainder = money * scale - floorPrice * (decimal)qtyUnits;
        if (remainder <= 0 || remainder != decimal.Truncate(remainder) || remainder >= qtyUnits || remainder > long.MaxValue)
            return [];

        var rem = (long)remainder;
        var pairs = new List<FloorSplitPair>();
        foreach (var secondUnits in Divisors(rem))
        {
            if (secondUnits <= 0 || secondUnits >= qtyUnits || rem % secondUnits != 0)
                continue;

            var step = rem / secondUnits;
            if (step <= 0 || floorPrice > long.MaxValue - step)
                continue;

            var secondPrice = floorPrice + step;
            if (floorPrice < 0 || secondPrice < 0)
                continue;

            var quantity1 = QuantityMath.RoundQty((qtyUnits - secondUnits) / scale);
            var quantity2 = QuantityMath.RoundQty(secondUnits / scale);
            if (quantity1 <= 0 || quantity2 <= 0 || quantity1 + quantity2 != qty)
                continue;
            if (quantity1 != decimal.Truncate(quantity1) || quantity2 != decimal.Truncate(quantity2))
                continue;

            var pair = new FloorSplitPair(quantity1, floorPrice, quantity2, secondPrice);
            if (!pair.Matches(qty, money))
                continue;

            pairs.Add(pair);
        }

        pairs.Sort((left, right) => left.Quantity2.CompareTo(right.Quantity2));
        return pairs;
    }

    private static List<long> Divisors(long value)
    {
        var small = new List<long>();
        var large = new List<long>();
        var limit = IntegerSqrt(value);
        for (long factor = 1; factor <= limit; factor++)
        {
            if (value % factor != 0)
                continue;
            small.Add(factor);
            var other = value / factor;
            if (other != factor)
                large.Add(other);
        }

        large.Reverse();
        small.AddRange(large);
        return small;
    }

    private static long IntegerSqrt(long value)
    {
        if (value < 2)
            return value;
        long low = 1;
        long high = Math.Min(value, 3_037_000_499L);
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (mid <= value / mid)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return high;
    }
}
