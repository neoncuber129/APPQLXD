using APPQLXD.Core.Domain;

namespace APPQLXD.Core;

public static class Labels
{
    public const string OpeningRule = "Tồn đầu kỳ là số tồn thực tế, không nhân VCF.";
    public const string DefaultConversionRule = "Thực nhập = Số lượng nhập × VCF. Giá trị xuất = Thực xuất / VCF. Tồn dùng thực nhập và thực xuất.";

    public static string Warehouse(WarehouseType type) => type switch
    {
        WarehouseType.Main => "Kho XD",
        WarehouseType.Ptkt => "Kho PTKT-VTXD",
        WarehouseType.Auxiliary => "Kho phụ",
        _ => type.ToString()
    };

    public static string WarehouseScopeLabel(WarehouseScope scope) => scope switch
    {
        WarehouseScope.Ptkt => "Kho PTKT-VTXD",
        _ => "Kho XD"
    };

    public static string Consumer(ConsumerType type) => type switch
    {
        ConsumerType.Machine => "Máy",
        ConsumerType.Vehicle => "Phương tiện",
        ConsumerType.Ship => "Tàu",
        ConsumerType.Other => "Đối tượng khác",
        _ => type.ToString()
    };

    public static string Kind(DocumentKind kind) => kind switch
    {
        DocumentKind.Opening => "Tồn đầu kỳ",
        DocumentKind.Import => "Phiếu nhập",
        DocumentKind.Transfer => "Điều chuyển kho",
        DocumentKind.Consumption => "Xuất tiêu thụ",
        DocumentKind.Auxiliary => "Tiêu thụ kho phụ",
        DocumentKind.Issue => "Phiếu xuất",
        DocumentKind.LotConvert => "Đổi loại lô",
        _ => kind.ToString()
    };

    public static string Status(DocumentStatus status) => status switch
    {
        DocumentStatus.Active => "Hiệu lực",
        DocumentStatus.Voided => "Đã hủy",
        _ => status.ToString()
    };

    public static string Family(DocumentFamily family) => family switch
    {
        DocumentFamily.Import => "Phiếu nhập",
        DocumentFamily.Export => "Phiếu xuất",
        _ => family.ToString()
    };

    public static string DataType(FieldDataType type) => type switch
    {
        FieldDataType.Text => "Chữ",
        FieldDataType.Number => "Số",
        FieldDataType.Date => "Ngày",
        FieldDataType.Boolean => "Có/Không",
        _ => type.ToString()
    };

    public static DocumentFamily FamilyOf(DocumentKind kind) => kind switch
    {
        DocumentKind.Import => DocumentFamily.Import,
        DocumentKind.Transfer or DocumentKind.Consumption or DocumentKind.Auxiliary or DocumentKind.Issue or DocumentKind.LotConvert => DocumentFamily.Export,
        _ => DocumentFamily.Import
    };
}
