using APPQLXD.Core.Domain;

namespace APPQLXD.Core.Persistence;

/// <summary>
/// Trường trên mặt phiếu nhập/xuất. Số phiếu và ngày không nằm trong danh sách này.
/// </summary>
public static class SlipFieldCatalog
{
    public static readonly (string Name, int Sort)[] Shared =
    [
        ("Cơ quan", 20),
        ("Đơn vị", 21),
        ("Đơn vị vận chuyển", 30),
        ("Giấy giới thiệu và CMT", 31),
        ("Có giá đến ngày", 32),
        ("Số xe", 33),
        ("Số km", 34),
        ("Dung tích kiểm định", 35),
        ("Dung tích nhận hàng", 36),
        ("Số lượng bao bì", 37),
        ("Nhiệm vụ", 38),
        ("Ghi chú", 40),
        ("Mã số", 50),
        ("Chất lượng", 51),
        ("Số lượng", 52),
        ("Nhiệt độ", 53),
        ("Tỉ trọng", 54),
        ("Hệ số VCF", 55),
        ("Đơn giá", 57),
        ("Thành tiền", 58),
        ("Chữ ký người giao", 60),
        ("Chữ ký người nhận", 61),
        ("Chữ ký tài chính", 62),
        ("Chữ ký người viết phiếu", 63),
        ("Chữ ký trưởng ban HC-KT", 64),
        ("Chữ ký chỉ huy đơn vị", 65)
    ];

    public static readonly (string Name, int Sort)[] ImportOnly =
    [
        ("Tính chất nhập", 10),
        ("Đơn vị giao hàng", 11),
        ("Đơn vị nhận hàng", 12),
        ("Người nhận", 13),
        ("Người giao hàng", 23),
        ("Theo hợp đồng số", 24),
        ("Thực nhập", 56)
    ];

    public static readonly (string Name, int Sort)[] ExportOnly =
    [
        ("Tính chất xuất", 10),
        ("Đơn vị giao", 11),
        ("Đơn vị nhận", 12),
        ("Theo lệnh (KH)", 24),
        ("Thực xuất", 56)
    ];

    private static readonly HashSet<string> ExtraFormNames = BuildExtraFormNames();

    public static IEnumerable<(DocumentFamily Family, string Name, int Sort)> All()
    {
        foreach (var family in new[] { DocumentFamily.Import, DocumentFamily.Export })
        {
            foreach (var field in Shared)
                yield return (family, field.Name, field.Sort);
            foreach (var field in family == DocumentFamily.Import ? ImportOnly : ExportOnly)
                yield return (family, field.Name, field.Sort);
        }
    }

    public static bool HideFromExtraForm(string name) => ExtraFormNames.Contains(name);

    public static readonly string[] ImportPaper =
    [
        "Cơ quan", "Đơn vị",
        "Đơn vị nhận hàng", "Đơn vị giao hàng", "Tính chất nhập", "Theo hợp đồng số", "Đơn vị vận chuyển",
        "Có giá đến ngày", "Người giao hàng", "Giấy giới thiệu và CMT", "Số xe", "Số km", "Nhiệm vụ", "Dung tích kiểm định",
        "Ghi chú",
        "Chữ ký người giao", "Chữ ký người nhận", "Chữ ký tài chính", "Chữ ký người viết phiếu",
        "Chữ ký trưởng ban HC-KT", "Chữ ký chỉ huy đơn vị"
    ];

    public static readonly string[] ExportPaper =
    [
        "Cơ quan", "Đơn vị",
        "Đơn vị giao", "Đơn vị nhận", "Tính chất xuất", "Theo lệnh (KH)", "Người nhận", "Giấy giới thiệu và CMT",
        "Có giá đến ngày", "Đơn vị vận chuyển", "Số xe", "Số km", "Nhiệm vụ", "Dung tích kiểm định", "Dung tích nhận hàng", "Số lượng bao bì",
        "Ghi chú",
        "Chữ ký người nhận", "Chữ ký người giao", "Chữ ký tài chính", "Chữ ký người viết phiếu",
        "Chữ ký trưởng ban HC-KT", "Chữ ký chỉ huy đơn vị"
    ];

    private static readonly HashSet<string> PaperNames = ImportPaper.Concat(ExportPaper).ToHashSet();

    public static bool IsPaperPriority(string name) => PaperNames.Contains(name);

    public static bool IsDocumentKey(string name) =>
        name is "Số phiếu" or "Ngày" or "Ngày chứng từ";

    /// <summary>Trường nhập tay mỗi lần — không seed / không ghi vào bộ dữ liệu mẫu.</summary>
    public static bool AllowsSampleValues(string? name)
    {
        var key = name?.Trim() ?? "";
        return key is not ("Có giá đến ngày" or "Số km");
    }

    private static HashSet<string> BuildExtraFormNames()
    {
        var names = Shared.Select(x => x.Name)
            .Concat(ImportOnly.Select(x => x.Name))
            .Concat(ExportOnly.Select(x => x.Name))
            .ToHashSet();
        names.Remove("Người nhận");
        return names;
    }
}
