namespace APPQLXD.Core.Domain;

public sealed class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class ItemGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Nhóm thuộc danh mục Kho XD hoặc Kho PTKT-VTXD — lọc theo chế độ làm việc.</summary>
    public WarehouseScope Scope { get; set; } = WarehouseScope.Xd;
}

public sealed class MeasureUnit
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class LotType
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class LotOrigin
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class FuelItem
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid UnitId { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public decimal? Density { get; set; }
    public string QualityInfo { get; set; } = "";
    public decimal? Temperature { get; set; }
    public string MeasurementNote { get; set; } = "";
    public decimal Vcf { get; set; }
    public string ConversionRule { get; set; } = "";
    public ItemGroup? Group { get; set; }
    public MeasureUnit? Unit { get; set; }
}

public sealed class Warehouse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public WarehouseType Type { get; set; }
    public Guid? DefaultImportSampleSetId { get; set; }
    public Guid? DefaultExportSampleSetId { get; set; }
}

public sealed class Consumer
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public ConsumerType Type { get; set; }
    public Guid? DefaultGroupId { get; set; }
    public Guid? DefaultItemId { get; set; }
    public decimal? Norm { get; set; }
    /// <summary>Máy/xe: khi mở tiêu thụ quý, điền ô trống từ tổng phiếu điều chuyển trong quý.</summary>
    public bool RollTransfersIntoQuarter { get; set; } = true;
    public Guid? DefaultImportSampleSetId { get; set; }
    public Guid? DefaultExportSampleSetId { get; set; }
    /// <summary>Tàu: loại tàu (hiển thị trên sổ tiêu thụ quý).</summary>
    public string ShipType { get; set; } = "";
    /// <summary>Tàu: số máy chính hoạt động (mặc định cho sổ tiêu thụ quý).</summary>
    public decimal MainMachineCount { get; set; }
    /// <summary>Tàu: số máy phụ hoạt động (mặc định cho sổ tiêu thụ quý).</summary>
    public decimal AuxMachineCount { get; set; }
    public List<ConsumerNormFactor> NormFactors { get; set; } = [];
}

public sealed class ConsumerNormFactor
{
    public Guid Id { get; set; }
    public Guid ConsumerId { get; set; }
    public Guid? GroupId { get; set; }
    public string Name { get; set; } = "";
    public decimal Value { get; set; }
    public int SortOrder { get; set; }
    public Consumer? Consumer { get; set; }
}

public sealed class FieldDefinition
{
    public Guid Id { get; set; }
    public DocumentFamily Family { get; set; }
    public string Name { get; set; } = "";
    public FieldDataType DataType { get; set; }
    public bool IsRequired { get; set; }
    public bool IsVisible { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class SampleSet
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DocumentFamily Family { get; set; }
    public List<SampleValue> Values { get; set; } = [];
}

public sealed class SampleValue
{
    public Guid Id { get; set; }
    public Guid SampleSetId { get; set; }
    public Guid FieldDefinitionId { get; set; }
    public string Value { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public SampleSet? SampleSet { get; set; }
    public FieldDefinition? Field { get; set; }
}

public sealed class Lot
{
    public Guid Id { get; set; }
    public string ItemName { get; set; } = "";
    public string ItemNameKey { get; set; } = "";
    public long UnitPrice { get; set; }
    public Guid LotTypeId { get; set; }
    public Guid? ItemId { get; set; }
    public string GroupName { get; set; } = "";
    public string UnitName { get; set; } = "";
    public string QualityInfo { get; set; } = "";
    public decimal? Temperature { get; set; }
    public string MeasurementNote { get; set; } = "";
    public string ConversionRule { get; set; } = "";
    public decimal FirstVcf { get; set; }
    public string Origin { get; set; } = "Tự mua";
    public LotType? LotType { get; set; }
}

public sealed class StockBalance
{
    public Guid Id { get; set; }
    public Guid LotId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public Lot? Lot { get; set; }
    public Warehouse? Warehouse { get; set; }
}

public sealed class StockMovement
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid LotId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal SignedQuantity { get; set; }
    public string Reason { get; set; } = "";
    public DateTime OccurredAt { get; set; }
}

public sealed class FuelDocument
{
    public Guid Id { get; set; }
    public long Sequence { get; set; }
    public string Number { get; set; } = "";
    public DocumentKind Kind { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Active;
    public DateTime DocumentDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public Guid? ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string UnitName { get; set; } = "";
    public string QualityInfo { get; set; } = "";
    public decimal? Temperature { get; set; }
    public string MeasurementNote { get; set; } = "";
    public string ConversionRule { get; set; } = "";
    public decimal Vcf { get; set; }

    public Guid? WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public string WarehouseTypeName { get; set; } = "";

    public Guid? DestinationWarehouseId { get; set; }
    public string DestinationWarehouseName { get; set; } = "";

    public Guid? ConsumerId { get; set; }
    public string ConsumerName { get; set; } = "";
    public string ConsumerCode { get; set; } = "";
    public string ConsumerTypeName { get; set; } = "";
    public decimal? Norm { get; set; }
    public decimal? Distance { get; set; }
    public decimal? OperatingQuantity { get; set; }
    /// <summary>Xe: nhập thực xuất thủ công thay vì km × định mức.</summary>
    public bool ManualQuantity { get; set; }

    public long UnitPrice { get; set; }
    public Guid? LotTypeId { get; set; }
    public string LotTypeCode { get; set; } = "";
    public string Origin { get; set; } = "Tự mua";
    public decimal InputQuantity { get; set; }
    public decimal ActualQuantity { get; set; }
    public decimal? Amount { get; set; }
    public bool WasSplit { get; set; }

    public string FormNumber { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string UnitTitle { get; set; } = "";
    public string SenderUnit { get; set; } = "";
    public string ReceiverUnit { get; set; } = "";
    public string Nature { get; set; } = "";
    public string ContractOrOrder { get; set; } = "";
    public string CarrierUnit { get; set; } = "";
    public string PriceValidUntil { get; set; } = "";
    public string DelivererName { get; set; } = "";
    public string IntroDocument { get; set; } = "";
    public string VehiclePlate { get; set; } = "";
    public string CalibrationVolume { get; set; } = "";
    public string ReceivedVolume { get; set; } = "";
    public string PackageCount { get; set; } = "";
    public string ReceiverPerson { get; set; } = "";
    public string Kilometers { get; set; } = "";
    public string Mission { get; set; } = "";
    /// <summary>Nhiệm vụ danh mục (phiếu xuất máy/xe). Null = chưa chọn.</summary>
    public Guid? MissionTaskId { get; set; }
    /// <summary>Nơi đi trên phiếu điều chuyển (không bắt buộc).</summary>
    public string OriginPlace { get; set; } = "";
    /// <summary>Nơi đến trên phiếu điều chuyển (không bắt buộc).</summary>
    public string DestinationPlace { get; set; } = "";
    public string Note { get; set; } = "";
    public string SignerReceiver { get; set; } = "";
    public string SignerDeliverer { get; set; } = "";
    public string SignerFinance { get; set; } = "";
    public string SignerWriter { get; set; } = "";
    public string SignerChief { get; set; } = "";
    public string SignerCommander { get; set; } = "";
    public string AmountInWords { get; set; } = "";

    public List<FuelDocumentLine> Lines { get; set; } = [];
    public List<FuelDocumentField> Fields { get; set; } = [];
}

public sealed class FuelDocumentLine
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int LineNo { get; set; }
    public Guid LotId { get; set; }
    public Guid WarehouseId { get; set; }
    public string ItemName { get; set; } = "";
    public long UnitPrice { get; set; }
    public Guid? LotTypeId { get; set; }
    public string LotTypeCode { get; set; } = "";
    public string Origin { get; set; } = "Tự mua";
    /// <summary>Điều chuyển: loại lô tại kho nhận (null = giữ nguyên loại nguồn).</summary>
    public Guid? DestinationLotTypeId { get; set; }
    public string DestinationLotTypeCode { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal ActualQuantity { get; set; }
    public decimal Amount { get; set; }
    public Guid? ItemId { get; set; }
    public string ItemCode { get; set; } = "";
    public string QualityGrade { get; set; } = "";
    public decimal? Temperature { get; set; }
    public decimal? Density { get; set; }
    public decimal Vcf { get; set; }
    public FuelDocument? Document { get; set; }
}

public sealed class FuelDocumentField
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? FieldDefinitionId { get; set; }
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool IsRequired { get; set; }
    public string Value { get; set; } = "";
    public FuelDocument? Document { get; set; }
}

/// <summary>Sổ tiêu thụ quý của một tàu (một bản ghi / quý / tàu).</summary>
public sealed class ShipQuarterBook
{
    public Guid Id { get; set; }
    public Guid ConsumerId { get; set; }
    public int Year { get; set; }
    public int Quarter { get; set; }
    public string FuelGroupName { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public List<ShipQuarterBookLine> Lines { get; set; } = [];
}

public sealed class ShipQuarterBookLine
{
    public Guid Id { get; set; }
    public Guid BookId { get; set; }
    public int SortOrder { get; set; }
    public string DocumentNumber { get; set; } = "";
    public DateTime? DocumentDate { get; set; }
    public string Description { get; set; } = "";
    public Guid? MissionTaskId { get; set; }
    public decimal MainOpsCount { get; set; }
    public decimal HoursAtBerth { get; set; }
    public decimal HoursCx25 { get; set; }
    public decimal HoursCx50 { get; set; }
    public decimal HoursCx75 { get; set; }
    public decimal HoursCx100 { get; set; }
    public decimal AuxOpsCount { get; set; }
    public decimal AuxHours { get; set; }
    public bool ManualFuelOut { get; set; }
    public decimal? FuelOutManual { get; set; }
    public bool ManualOilOut { get; set; }
    public decimal OilOut { get; set; }
    public Guid LotTypeId { get; set; }
    public string LotTypeCode { get; set; } = "TX";
    public ShipQuarterBook? Book { get; set; }
}

/// <summary>Sổ tiêu thụ quý máy/xe (một bản ghi / quý / đối tượng).</summary>
public sealed class ConsumerQuarterBook
{
    public Guid Id { get; set; }
    public Guid ConsumerId { get; set; }
    public int Year { get; set; }
    public int Quarter { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ConsumerQuarterBookLine> Lines { get; set; } = [];
}

public sealed class ConsumerQuarterBookLine
{
    public Guid Id { get; set; }
    public Guid BookId { get; set; }
    public int SortOrder { get; set; }
    public Guid? TransferDocumentId { get; set; }
    public string DocumentNumber { get; set; } = "";
    public DateTime? DocumentDate { get; set; }
    public string Description { get; set; } = "";
    public Guid? MissionTaskId { get; set; }
    public decimal? Kilometers { get; set; }
    public decimal? MachineHours { get; set; }
    public decimal? NormQuantity { get; set; }
    public decimal? ActualQuantity { get; set; }
    public bool ManualFuelOut { get; set; }
    public decimal FuelOut { get; set; }
    public bool ManualOilOut { get; set; }
    public decimal OilOut { get; set; }
    public Guid LotTypeId { get; set; }
    public string LotTypeCode { get; set; } = "TX";
    public ConsumerQuarterBook? Book { get; set; }
}

/// <summary>Nhóm nhiệm vụ (I. Khối tham mưu…).</summary>
public sealed class MissionGroup
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    /// <summary>True = nhóm Hao hụt (không vào hàng Cộng tiêu thụ I–IV).</summary>
    public bool IsLossGroup { get; set; }
    public List<MissionTask> Tasks { get; set; } = [];
}

/// <summary>Nhiệm vụ con trong nhóm.</summary>
public sealed class MissionTask
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public MissionGroup? Group { get; set; }
}

/// <summary>Hạn mức NL được phép theo nhiệm vụ + năm + bảng loại lô (TX+SSCĐ hoặc IUU).</summary>
public sealed class MissionYearLimit
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public int Year { get; set; }
    /// <summary><see cref="NxtLotViewMode.TxSscd"/> hoặc <see cref="NxtLotViewMode.Iuu"/>.</summary>
    public int LotView { get; set; }
    public decimal GasolineLimit { get; set; }
    public decimal DieselLimit { get; set; }
    public MissionTask? Task { get; set; }
}
