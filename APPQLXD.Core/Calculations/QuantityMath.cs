namespace APPQLXD.Core.Calculations;

public static class QuantityMath
{
    public const int QuantityDecimals = 4;
    public const int VcfDecimals = 6;
    public const int NormDecimals = 6;

    /// <summary>
    /// Trần chính xác nghiệp vụ: hàng trăm ngàn tỉ (100.000 × 10^9 = 10^14).
    /// Số lượng / thành tiền nguyên trong phạm vi này phải tính đúng tuyệt đối (decimal, không double).
    /// </summary>
    public const decimal MaxExactValue = 100_000_000_000_000m;

    /// <summary>Alias trần ô nhập / kết quả tính — trùng chuẩn MaxExactValue.</summary>
    public const decimal MaxEditQty = MaxExactValue;

    public static decimal ClampEditQty(decimal value)
    {
        if (value < 0)
            return 0;
        return value > MaxExactValue ? MaxExactValue : value;
    }

    public static bool IsWithinExactRange(decimal value) =>
        value >= 0 && value <= MaxExactValue;

    public static decimal RoundQty(decimal value) =>
        Math.Round(value, QuantityDecimals, MidpointRounding.AwayFromZero);

    public static decimal RoundVcf(decimal value) =>
        Math.Round(value, VcfDecimals, MidpointRounding.AwayFromZero);

    public static decimal RoundNorm(decimal value) =>
        Math.Round(value, NormDecimals, MidpointRounding.AwayFromZero);

    public static decimal RoundMoney(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero);

    public static bool IsWholeMoney(decimal value) => value == decimal.Truncate(value);

    public static bool TryWholeMoney(decimal value, out long whole)
    {
        if (!IsWholeMoney(value) || value < long.MinValue || value > long.MaxValue)
        {
            whole = 0;
            return false;
        }

        whole = (long)value;
        return true;
    }

    /// <summary>Thành tiền = đơn giá × số lượng (đồng nguyên). Trả false nếu vượt chuẩn / không nguyên.</summary>
    public static bool TryAmount(long unitPrice, decimal quantity, out decimal amount)
    {
        amount = 0;
        if (unitPrice < 0 || quantity < 0)
            return false;
        try
        {
            var product = RoundMoney(unitPrice * Whole(quantity));
            if (!IsWholeMoney(product) || product > MaxExactValue)
                return false;
            amount = product;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static string LotKey(string itemName) => itemName.Trim().ToUpperInvariant();

    public static decimal Whole(decimal value) =>
        Math.Round(RoundQty(value), 0, MidpointRounding.AwayFromZero);

    public static decimal ActualImport(decimal inputQuantity, decimal vcf)
    {
        try
        {
            var factor = RoundVcf(vcf);
            if (factor <= 0)
                return 0;
            return ClampEditQty(Math.Round(inputQuantity * factor, 0, MidpointRounding.AwayFromZero));
        }
        catch (OverflowException)
        {
            return MaxExactValue;
        }
    }

    public static decimal ExportDisplayQuantity(decimal actualQuantity, decimal vcf)
    {
        var factor = RoundVcf(vcf);
        if (factor <= 0)
            throw new FuelRuleException("VCF phải lớn hơn 0.");
        return ClampEditQty(Math.Round(actualQuantity / factor, 0, MidpointRounding.AwayFromZero));
    }

    public static decimal VehicleActual(decimal distance, decimal norm)
    {
        try
        {
            var raw = RoundQty(ClampEditQty(RoundQty(distance)) * RoundNorm(norm));
            if (raw <= 0)
                return 0;
            return ClampEditQty(Math.Round(raw, 0, MidpointRounding.AwayFromZero));
        }
        catch (OverflowException)
        {
            return MaxExactValue;
        }
    }

    /// <summary>Định mức tàu = tổng tỷ lệ quy đổi (lít khi vận hành = 1).</summary>
    public static decimal ShipEffectiveNorm(IEnumerable<decimal> rates)
    {
        decimal total = 0;
        foreach (var rate in rates)
        {
            var value = RoundNorm(rate);
            if (value > 0)
                total = RoundNorm(total + value);
        }

        return total;
    }

    public static decimal ShipActual(decimal operatingQuantity, decimal effectiveNorm) =>
        VehicleActual(operatingQuantity, effectiveNorm);
}

public sealed class FuelRuleException : Exception
{
    public FuelRuleException(string message) : base(message) { }
}
