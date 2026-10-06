namespace APPQLXD.Core;

/// <summary>Định mức tàu cố định theo sổ tiêu thụ quý 3-04.3/XD-14 (L/giờ).</summary>
public static class ShipNormSlots
{
    public const string AtBerth = "Tại bến";
    public const string Cx25 = "25% CX";
    public const string Cx50 = "50% CX";
    public const string Cx75 = "75% CX";
    public const string Cx100 = "100% CX";
    public const string Aux = "Máy phụ";

    public static readonly string[] Labels =
    [
        AtBerth,
        Cx25,
        Cx50,
        Cx75,
        Cx100,
        Aux
    ];

    public static bool IsKnown(string? name)
    {
        var key = name?.Trim() ?? "";
        return Labels.Any(x => x.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    public static int SortOrder(string? name)
    {
        var key = name?.Trim() ?? "";
        for (var i = 0; i < Labels.Length; i++)
        {
            if (Labels[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                return i + 1;
        }

        return 99;
    }

    public static bool IsFuelGroup(string? groupName)
    {
        var name = groupName?.Trim() ?? "";
        return name.Equals("Xăng", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Dầu", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGreaseGroup(string? groupName)
    {
        var name = groupName?.Trim() ?? "";
        return name.Equals("Nhớt", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Mỡ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Tỷ lệ dầu mỡ / nhiên liệu tiêu thụ trên sổ tàu.</summary>
    public const decimal OilPerFuelRatio = 0.04m;
}
