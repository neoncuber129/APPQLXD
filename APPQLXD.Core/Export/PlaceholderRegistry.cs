using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;

namespace APPQLXD.Core.Export;

public enum ExportDocumentKind
{
    PhieuNhap,
    PhieuXuat,
    SoTtMayXe,
    SoTtTau,
    SoNxt,
    SoNxtTong
}

public sealed record PlaceholderInfo(
    string Token,
    string Description,
    bool IsRowField,
    Guid? FieldDefinitionId = null);

/// <summary>Danh mục placeholder cố định (mã ngắn) + trường động theo FieldDefinition.Id.</summary>
public sealed class PlaceholderRegistry
{
    private readonly Dictionary<Guid, string> _fieldTokens = new();

    public IReadOnlyList<PlaceholderInfo> List(ExportDocumentKind kind, IEnumerable<FieldRow>? dynamicFields = null)
    {
        var list = new List<PlaceholderInfo>(FixedFor(kind));
        if (kind is ExportDocumentKind.PhieuNhap or ExportDocumentKind.PhieuXuat)
        {
            var family = kind == ExportDocumentKind.PhieuNhap ? DocumentFamily.Import : DocumentFamily.Export;
            foreach (var field in dynamicFields ?? [])
            {
                if (field.Family != family) continue;
                var token = TokenForField(field.Id, field.Name);
                list.Add(new PlaceholderInfo(token, field.Name, false, field.Id));
            }
        }

        return list
            .GroupBy(x => x.Token, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.IsRowField)
            .ThenBy(x => x.Token, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string TokenForField(Guid fieldId, string fieldName)
    {
        if (_fieldTokens.TryGetValue(fieldId, out var existing))
            return existing;

        var baseSlug = PlaceholderSlug.FromVietnamese(fieldName, 12);
        var token = "$F_" + baseSlug;
        var used = new HashSet<string>(_fieldTokens.Values, StringComparer.OrdinalIgnoreCase);
        if (used.Contains(token))
            token = "$F_" + baseSlug + fieldId.ToString("N")[..4].ToUpperInvariant();

        _fieldTokens[fieldId] = token;
        return token;
    }

    public static IReadOnlyList<PlaceholderInfo> FixedFor(ExportDocumentKind kind) => kind switch
    {
        ExportDocumentKind.PhieuNhap => SlipFixed("nhập"),
        ExportDocumentKind.PhieuXuat => SlipFixed("xuất"),
        ExportDocumentKind.SoTtMayXe => BookMayXeFixed(),
        ExportDocumentKind.SoTtTau => BookTauFixed(),
        ExportDocumentKind.SoNxt => NxtFixed(),
        ExportDocumentKind.SoNxtTong => NxtTongFixed(),
        _ => []
    };

    private static PlaceholderInfo[] SlipFixed(string tinhChatHint) =>
    [
        P("$SoPhieu", "Số phiếu"),
        P("$Ngay", "Ngày"),
        P("$CQ", "Cơ quan"),
        P("$DV", "Đơn vị"),
        P("$DVGiao", "ĐV giao"),
        P("$DVNhan", "ĐV nhận"),
        P("$TC", $"Tính chất {tinhChatHint}"),
        P("$HD", "HĐ / lệnh"),
        P("$DVVC", "ĐV vận chuyển"),
        P("$GiaDen", "Giá đến ngày"),
        P("$NGiao", "Người giao"),
        P("$NNhan", "Người nhận"),
        P("$GiayGT", "Giấy GT / CMT"),
        P("$SoXe", "Số xe"),
        P("$SoKm", "Số km"),
        P("$DtKd", "DT kiểm định"),
        P("$DtNhan", "DT nhận hàng"),
        P("$BaoBi", "SL bao bì"),
        P("$NV", "Nhiệm vụ"),
        P("$NoiDi", "Nơi đi"),
        P("$NoiDen", "Nơi đến"),
        P("$GC", "Ghi chú"),
        P("$Kho", "Kho"),
        P("$KDen", "Kho đích"),
        P("$DT", "Đối tượng"),
        P("$SKhoan", "Số khoản"),
        P("$TongSL", "Tổng SL"),
        P("$TongThuc", "Tổng thực"),
        P("$TongTien", "Tổng tiền"),
        P("$BC", "Bằng chữ"),
        P("$CK_NG", "CK người giao"),
        P("$CK_NN", "CK người nhận"),
        P("$CK_TC", "CK tài chính"),
        P("$CK_NV", "CK người viết"),
        P("$CK_TB", "CK trưởng ban"),
        P("$CK_CH", "CK chỉ huy"),
        R("$STT", "STT"),
        R("$Ten", "Tên hàng"),
        R("$MaSo", "Mã số"),
        R("$CL", "CL"),
        R("$SL", "SL"),
        R("$Nhiet", "Nhiệt"),
        R("$TD", "Tỉ trọng"),
        R("$VCF", "VCF"),
        R("$Thuc", "Thực NX"),
        R("$Gia", "Đơn giá"),
        R("$TTien", "Thành tiền"),
        R("$LLo", "Loại lô")
    ];

    private static PlaceholderInfo[] BookMayXeFixed() =>
    [
        P("$Nam", "Năm"),
        P("$Quy", "Quý"),
        P("$DT", "Máy / xe"),
        P("$BienSo", "Biển số"),
        P("$NL", "Nhiên liệu"),
        P("$MS", "Mẫu số"),
        P("$DV", "Đơn vị"),
        P("$TXang", "Tổng xăng ĐC"),
        P("$TDau", "Tổng dầu ĐC"),
        P("$O_DGiai", "Tồn quý trước — diễn giải"),
        P("$O_XTon", "Tồn quý trước — NL tồn"),
        P("$O_DTon", "Tồn quý trước — dầu mỡ tồn"),
        P("$C_DGiai", "Cộng — diễn giải"),
        P("$C_SoKm", "Cộng — km"),
        P("$C_GMay", "Cộng — giờ máy"),
        P("$C_DM", "Cộng — ĐM"),
        P("$C_TT", "Cộng — thực tế"),
        P("$C_Vuot", "Cộng — vượt"),
        P("$C_Thieu", "Cộng — thiếu"),
        P("$C_XNhap", "Cộng — xăng nhập"),
        P("$C_XXuat", "Cộng — xăng xuất"),
        P("$C_DNhap", "Cộng — dầu nhập"),
        P("$C_DXuat", "Cộng — dầu xuất"),
        P("$T_DGiai", "Tồn cuối (mang sang) — diễn giải"),
        P("$T_XTon", "Tồn cuối (mang sang) — NL tồn"),
        P("$T_DTon", "Tồn cuối (mang sang) — dầu mỡ tồn"),
        R("$STT", "STT"),
        R("$SoPhieu", "Số phiếu"),
        R("$Ngay", "Ngày"),
        R("$DGiai", "Diễn giải"),
        R("$NoiDi", "Nơi đi"),
        R("$NoiDen", "Nơi đến"),
        R("$SoKm", "Km"),
        R("$GMay", "Giờ máy"),
        R("$DM", "Định mức"),
        R("$TT", "Thực tế"),
        R("$Vuot", "Vượt"),
        R("$Thieu", "Thiếu"),
        R("$XNhap", "Xăng nhập"),
        R("$XXuat", "Xăng xuất"),
        R("$XTon", "Xăng tồn"),
        R("$DNhap", "Dầu nhập"),
        R("$DXuat", "Dầu xuất"),
        R("$DTon", "Dầu tồn"),
        R("$LLo", "Loại lô")
    ];

    private static PlaceholderInfo[] BookTauFixed() =>
    [
        P("$Nam", "Năm"),
        P("$Quy", "Quý"),
        P("$DT", "Tên tàu"),
        P("$LTau", "Loại tàu"),
        P("$NL", "Nhiên liệu"),
        P("$MS", "Mẫu số"),
        P("$DV", "Đơn vị"),
        P("$O_DGiai", "Tồn quý trước — diễn giải"),
        P("$O_XTon", "Tồn quý trước — NL tồn"),
        P("$O_DTon", "Tồn quý trước — dầu mỡ tồn"),
        P("$C_DGiai", "Cộng — diễn giải"),
        P("$C_MChinh", "Cộng — máy chính"),
        P("$C_GNeo", "Cộng — giờ neo"),
        P("$C_Cx25", "Cộng — CX 25%"),
        P("$C_Cx50", "Cộng — CX 50%"),
        P("$C_Cx75", "Cộng — CX 75%"),
        P("$C_Cx100", "Cộng — CX 100%"),
        P("$C_TongGio", "Cộng — tổng giờ×máy chính"),
        P("$C_MPhu", "Cộng — máy phụ"),
        P("$C_GPhu", "Cộng — giờ phụ"),
        P("$C_XDung", "Cộng giờ HĐ — tiêu thụ xăng"),
        P("$C_DDung", "Cộng giờ HĐ — tiêu thụ Diesel"),
        P("$C_XNhap", "Cộng — xăng nhập"),
        P("$C_XXuat", "Cộng — xăng xuất"),
        P("$C_DNhap", "Cộng — dầu nhập"),
        P("$C_DXuat", "Cộng — dầu xuất"),
        P("$T_DGiai", "Tồn cuối (mang sang) — diễn giải"),
        P("$T_XTon", "Tồn cuối (mang sang) — NL tồn"),
        P("$T_DTon", "Tồn cuối (mang sang) — dầu mỡ tồn"),
        R("$STT", "STT"),
        R("$SoPhieu", "Số phiếu"),
        R("$Ngay", "Ngày"),
        R("$DGiai", "Diễn giải"),
        R("$MChinh", "Máy chính"),
        R("$GNeo", "Giờ neo"),
        R("$Cx25", "CX 25%"),
        R("$Cx50", "CX 50%"),
        R("$Cx75", "CX 75%"),
        R("$Cx100", "CX 100%"),
        R("$TongGio", "Tổng giờ×máy chính"),
        R("$MPhu", "Máy phụ"),
        R("$GPhu", "Giờ phụ"),
        R("$XDung", "Cộng giờ HĐ — tiêu thụ xăng (dòng)"),
        R("$DDung", "Cộng giờ HĐ — tiêu thụ Diesel (dòng)"),
        R("$XNhap", "Xăng nhập"),
        R("$XXuat", "Xăng xuất"),
        R("$XTon", "Xăng tồn"),
        R("$DNhap", "Dầu nhập"),
        R("$DXuat", "Dầu xuất"),
        R("$DTon", "Dầu tồn"),
        R("$TDong", "Tổng dòng"),
        R("$LLo", "Loại lô")
    ];

    private static PlaceholderInfo[] NxtFixed() =>
    [
        P("$Nam", "Năm"),
        P("$Quy", "Quý"),
        P("$Kho", "Kho"),
        R("$STT", "STT"),
        R("$SoPhieu", "Số CT"),
        R("$Ngay", "Ngày"),
        R("$DGiai", "Diễn giải"),
        R("$SoKm", "Km"),
        R("$NV", "Nhiệm vụ"),
        R("$Nhap", "Nhập"),
        R("$Xuat", "Xuất"),
        R("$Ton", "Tồn")
    ];

    private static PlaceholderInfo[] NxtTongFixed() =>
    [
        P("$Nam", "Năm"),
        P("$Quy", "Quý"),
        P("$Kho", "Kho"),
        R("$STT", "STT"),
        R("$Nhom", "Nhóm"),
        R("$Ten", "Tên hàng"),
        R("$Gia", "Đơn giá"),
        R("$LLo", "Loại lô"),
        R("$TonDau", "Tồn đầu"),
        R("$Nhap", "Nhập"),
        R("$Xuat", "Xuất"),
        R("$TCuoi", "Tồn cuối"),
        R("$GC", "Ghi chú")
    ];

    private static PlaceholderInfo P(string token, string desc) => new(token, desc, false);
    private static PlaceholderInfo R(string token, string desc) => new(token, desc, true);
}
