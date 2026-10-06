using System.Globalization;
using APPQLXD.Core.Calculations;

namespace APPQLXD.Core.Export;

/// <summary>
/// Định dạng số xuất Word/Excel khớp UI (vi-VN): số lượng N0, tiền N0,
/// nhiệt độ 0.####, tỉ trọng/VCF 0.######.
/// </summary>
public static class ExportNumberFormat
{
    public static readonly CultureInfo Vietnam = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>Số lượng / km / giờ trên sổ — như Numbers.Qty.</summary>
    public static string Qty(decimal? value) =>
        value is null ? "" : Qty(value.Value);

    public static string Qty(decimal value) =>
        QuantityMath.Whole(value).ToString("N0", Vietnam);

    /// <summary>Tiền / đơn giá — như Numbers.Money.</summary>
    public static string Money(decimal value) =>
        decimal.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", Vietnam);

    public static string Money(long value) =>
        value.ToString("N0", Vietnam);

    /// <summary>Nhiệt độ — như Numbers.Decimal.</summary>
    public static string Decimal(decimal? value) =>
        value is null ? "" : Decimal(value.Value);

    public static string Decimal(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero).ToString("0.####", Vietnam);

    /// <summary>Tỉ trọng / VCF — như Numbers.Factor.</summary>
    public static string Factor(decimal? value) =>
        value is null ? "" : Factor(value.Value);

    public static string Factor(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero).ToString("0.######", Vietnam);
}
