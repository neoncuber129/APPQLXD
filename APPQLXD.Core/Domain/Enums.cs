namespace APPQLXD.Core.Domain;

public enum WarehouseType
{
    Main = 1,
    Auxiliary = 2,
    Ptkt = 3
}

/// <summary>Chế độ làm việc tạm thời: chỉ Kho XD hoặc chỉ Kho PTKT-VTXD.</summary>
public enum WarehouseScope
{
    Xd = 1,
    Ptkt = 2
}

/// <summary>Chế độ xem sổ NXT theo loại lô.</summary>
public enum NxtLotViewMode
{
    /// <summary>TX và SSCĐ.</summary>
    TxSscd = 0,
    /// <summary>Chỉ IUU.</summary>
    Iuu = 1,
    /// <summary>Tất cả loại lô (như sổ cũ).</summary>
    All = 2
}

public enum ConsumerType
{
    Machine = 1,
    Vehicle = 2,
    Other = 3,
    Ship = 4
}

public enum DocumentFamily
{
    Import = 1,
    Export = 2
}

public enum DocumentKind
{
    Opening = 1,
    Import = 2,
    Transfer = 3,
    Consumption = 4,
    Auxiliary = 5,
    Issue = 6,
    /// <summary>Đổi loại lô cùng kho (TX ↔ SSCĐ).</summary>
    LotConvert = 7
}

public enum ExportSlipMode
{
    Issue = 0,
    Transfer = 1,
    Auxiliary = 2,
    Vehicle = 3,
    Retail = 4,
    /// <summary>Đổi loại lô cùng kho.</summary>
    LotConvert = 5
}

public enum DocumentStatus
{
    Active = 1,
    Voided = 2
}

public enum FieldDataType
{
    Text = 1,
    Number = 2,
    Date = 3,
    Boolean = 4
}
