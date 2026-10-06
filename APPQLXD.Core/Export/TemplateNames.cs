using APPQLXD.Core.Domain;

namespace APPQLXD.Core.Export;

public static class TemplateNames
{
    public const string FolderName = "templateword";
    public const string FolderXd = "kho_xd";
    public const string FolderVt = "kho_vt";

    public const string PhieuNhap = "phieu_nhap.docx";
    public const string PhieuXuat = "phieu_xuat.docx";
    public const string SoTtMayXe = "so_tt_may_xe.docx";
    public const string SoTtTau = "so_tt_tau.docx";
    public const string SoNxt = "so_nxt.docx";
    public const string SoNxtTong = "so_nxt_tong.docx";

    public static string ScopeFolderName(WarehouseScope scope) =>
        scope == WarehouseScope.Ptkt ? FolderVt : FolderXd;

    public static string ResolveRoot(string? baseDirectory = null) =>
        Path.Combine(baseDirectory ?? AppContext.BaseDirectory, FolderName);

    /// <summary>Thư mục mẫu theo kho: templateword/kho_xd hoặc templateword/kho_vt.</summary>
    public static string ResolveFolder(string? baseDirectory = null, WarehouseScope scope = WarehouseScope.Xd) =>
        Path.Combine(ResolveRoot(baseDirectory), ScopeFolderName(scope));

    public static string ResolvePath(string fileName, string? baseDirectory = null, WarehouseScope scope = WarehouseScope.Xd) =>
        Path.Combine(ResolveFolder(baseDirectory, scope), fileName);

    public static string ForKind(ExportDocumentKind kind) => kind switch
    {
        ExportDocumentKind.PhieuNhap => PhieuNhap,
        ExportDocumentKind.PhieuXuat => PhieuXuat,
        ExportDocumentKind.SoTtMayXe => SoTtMayXe,
        ExportDocumentKind.SoTtTau => SoTtTau,
        ExportDocumentKind.SoNxt => SoNxt,
        ExportDocumentKind.SoNxtTong => SoNxtTong,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
