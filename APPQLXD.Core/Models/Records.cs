using APPQLXD.Core.Calculations;
using APPQLXD.Core.Domain;

namespace APPQLXD.Core.Models;

public sealed record FuelResult(bool Ok, string Message, Guid? Id = null)
{
    public static FuelResult Success(Guid id, string message = "Đã lưu.") => new(true, message, id);
    public static FuelResult Success(string message = "Thành công.") => new(true, message, null);
    public static FuelResult Fail(string message) => new(false, message);
}

public sealed record DemoProgress(string Phase, int PhaseDone, int PhaseTotal, int Done, int Total);

public sealed record RebuildResult(bool Ok, string Message, int DocumentCount, IReadOnlyList<string> Warnings);

public sealed class FieldInput
{
    public Guid FieldId { get; init; }
    public string Value { get; init; } = "";
}

public sealed class ItemEdit
{
    public Guid? Id { get; init; }
    public Guid GroupId { get; init; }
    public Guid UnitId { get; init; }
    public string Name { get; init; } = "";
    public string Code { get; init; } = "";
    public decimal? Density { get; init; }
    public string QualityInfo { get; init; } = "";
    public decimal? Temperature { get; init; }
    public string MeasurementNote { get; init; } = "";
    public decimal Vcf { get; init; }
    public string ConversionRule { get; init; } = "";
}

public sealed class WarehouseEdit
{
    public Guid? Id { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public WarehouseType Type { get; init; }
    public Guid? DefaultImportSampleSetId { get; init; }
    public Guid? DefaultExportSampleSetId { get; init; }
}

public sealed class ConsumerEdit
{
    public Guid? Id { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public ConsumerType Type { get; init; }
    public Guid? DefaultGroupId { get; init; }
    public Guid? DefaultItemId { get; init; }
    public decimal? Norm { get; init; }
    public bool RollTransfersIntoQuarter { get; init; } = true;
    public IReadOnlyList<ConsumerNormFactorEdit> NormFactors { get; init; } = [];
    public Guid? DefaultImportSampleSetId { get; init; }
    public Guid? DefaultExportSampleSetId { get; init; }
    /// <summary>Tàu: loại tàu (sổ tiêu thụ quý / xuất Word).</summary>
    public string ShipType { get; init; } = "";
    /// <summary>Tàu: số máy chính mặc định.</summary>
    public decimal MainMachineCount { get; init; }
    /// <summary>Tàu: số máy phụ mặc định.</summary>
    public decimal AuxMachineCount { get; init; }
}

public sealed class ConsumerNormFactorEdit
{
    public Guid? Id { get; init; }
    public Guid GroupId { get; init; }
    public string Name { get; init; } = "";
    public decimal Value { get; init; }
    public int SortOrder { get; init; }
}

public sealed class FieldEdit
{
    public Guid? Id { get; init; }
    public DocumentFamily Family { get; init; }
    public string Name { get; init; } = "";
    public FieldDataType DataType { get; init; }
    public bool IsRequired { get; init; }
    public bool IsVisible { get; init; } = true;
    public int SortOrder { get; init; }
}

public sealed class OpeningRequest
{
    public Guid? DocumentId { get; init; }
    public DateTime DocumentDate { get; init; }
    public Guid WarehouseId { get; init; }
    public string WarehouseName { get; init; } = "";
    public string WarehouseTypeName { get; init; } = "";
    public Guid ItemId { get; init; }
    public string ItemName { get; init; } = "";
    public string GroupName { get; init; } = "";
    public string UnitName { get; init; } = "";
    public string QualityInfo { get; init; } = "";
    public decimal? Temperature { get; init; }
    public string MeasurementNote { get; init; } = "";
    public decimal UnitPrice { get; init; }
    public Guid? LotTypeId { get; init; }
    public string Origin { get; init; } = "Tự mua";
    public decimal ActualQuantity { get; init; }
}

public sealed class OpeningSheetRequest
{
    public DateTime DocumentDate { get; init; }
    public IReadOnlyList<OpeningRequest> Cells { get; init; } = [];
    public IReadOnlyList<Guid> VoidIds { get; init; } = [];
}

public sealed class ImportRequest
{
    public Guid? DocumentId { get; init; }
    public DateTime DocumentDate { get; init; }
    public Guid WarehouseId { get; init; }
    public string WarehouseName { get; init; } = "";
    public string WarehouseTypeName { get; init; } = "";
    public Guid ItemId { get; init; }
    public string ItemName { get; init; } = "";
    public string GroupName { get; init; } = "";
    public string UnitName { get; init; } = "";
    public string QualityInfo { get; init; } = "";
    public decimal? Temperature { get; init; }
    public string MeasurementNote { get; init; } = "";
    public string ConversionRule { get; init; } = "";
    public decimal Vcf { get; init; }
    public decimal UnitPrice { get; init; }
    public Guid? LotTypeId { get; init; }
    public string Origin { get; init; } = "Tự mua";
    public decimal InputQuantity { get; init; }
    public decimal? Amount { get; init; }
    public IReadOnlyList<FieldInput> Fields { get; init; } = [];
    public Guid? AddToSampleSetId { get; init; }
}

public sealed class TransferRequest
{
    public Guid? DocumentId { get; init; }
    public DateTime DocumentDate { get; init; }
    public Guid SourceWarehouseId { get; init; }
    public string SourceWarehouseName { get; init; } = "";
    public string SourceWarehouseTypeName { get; init; } = "";
    public Guid DestinationWarehouseId { get; init; }
    public string DestinationWarehouseName { get; init; } = "";
    public string OriginPlace { get; init; } = "";
    public string DestinationPlace { get; init; } = "";
    public Guid LotId { get; init; }
    public Guid? DestinationLotTypeId { get; init; }
    public decimal ActualQuantity { get; init; }
    public decimal Vcf { get; init; }
    public IReadOnlyList<FieldInput> Fields { get; init; } = [];
    public Guid? AddToSampleSetId { get; init; }
}

public sealed class ConsumptionRequest
{
    public Guid? DocumentId { get; init; }
    public DateTime DocumentDate { get; init; }
    public Guid ConsumerId { get; init; }
    public ConsumerType ConsumerType { get; init; }
    public string ConsumerName { get; init; } = "";
    public string ConsumerCode { get; init; } = "";
    public Guid WarehouseId { get; init; }
    public string WarehouseName { get; init; } = "";
    public string WarehouseTypeName { get; init; } = "";
    public Guid LotId { get; init; }
    public decimal? DirectActual { get; init; }
    public decimal? Distance { get; init; }
    public decimal? Norm { get; init; }
    public decimal Vcf { get; init; }
    public IReadOnlyList<FieldInput> Fields { get; init; } = [];
    public Guid? AddToSampleSetId { get; init; }
}

public sealed class ConsumptionCellRequest
{
    public Guid? DocumentId { get; init; }
    public DateTime DocumentDate { get; init; }
    public Guid? ConsumerId { get; init; }
    public ConsumerType ConsumerType { get; init; }
    public string ConsumerName { get; init; } = "";
    public string ConsumerCode { get; init; } = "";
    public Guid WarehouseId { get; init; }
    public string WarehouseName { get; init; } = "";
    public string WarehouseTypeName { get; init; } = "";
    public string ItemName { get; init; } = "";
    public decimal UnitPrice { get; init; }
    public Guid? LotTypeId { get; init; }
    public string Origin { get; init; } = "Tự mua";
    public decimal ActualQuantity { get; init; }
    public decimal? OperatingQuantity { get; init; }
    public decimal? Norm { get; init; }
    public decimal Vcf { get; init; }
    public IReadOnlyList<FieldInput> Fields { get; init; } = [];
    public Guid? AddToSampleSetId { get; init; }
}

public sealed class ConsumptionSheetRequest
{
    public DateTime DocumentDate { get; init; }
    public IReadOnlyList<ConsumptionCellRequest> Cells { get; init; } = [];
    public IReadOnlyList<Guid> VoidIds { get; init; } = [];
}

public sealed class AuxiliaryCellRequest
{
    public Guid? DocumentId { get; init; }
    public DateTime DocumentDate { get; init; }
    public Guid WarehouseId { get; init; }
    public string WarehouseName { get; init; } = "";
    public string WarehouseTypeName { get; init; } = "";
    public string ItemName { get; init; } = "";
    public decimal UnitPrice { get; init; }
    public Guid? LotTypeId { get; init; }
    public string Origin { get; init; } = "Tự mua";
    public decimal ActualQuantity { get; init; }
    public decimal Vcf { get; init; }
}

public sealed class AuxiliarySheetRequest
{
    public DateTime DocumentDate { get; init; }
    public IReadOnlyList<AuxiliaryCellRequest> Cells { get; init; } = [];
    public IReadOnlyList<Guid> VoidIds { get; init; } = [];
}

public sealed class AuxiliaryRequest
{
    public Guid? DocumentId { get; init; }
    public DateTime DocumentDate { get; init; }
    public Guid WarehouseId { get; init; }
    public string WarehouseName { get; init; } = "";
    public string WarehouseTypeName { get; init; } = "";
    public Guid LotId { get; init; }
    public decimal ActualQuantity { get; init; }
    public decimal Vcf { get; init; }
    public IReadOnlyList<FieldInput> Fields { get; init; } = [];
    public Guid? AddToSampleSetId { get; init; }
}

public sealed record GroupRow(Guid Id, string Name, WarehouseScope Scope = WarehouseScope.Xd);
public sealed record UnitRow(Guid Id, string Name);
public sealed record LotTypeRow(Guid Id, string Code, string Name, int SortOrder, bool IsActive);
public sealed record LotOriginRow(Guid Id, string Name, int SortOrder, bool IsActive);

public sealed record MissionGroupRow(Guid Id, string Code, string Name, int SortOrder, bool IsLossGroup, int TaskCount);
public sealed record MissionTaskRow(Guid Id, Guid GroupId, string GroupCode, string GroupName, string Name, int SortOrder, bool IsActive)
{
    public string Display => Name;
}

public sealed record MissionYearLimitRow(Guid TaskId, int Year, NxtLotViewMode LotView, decimal GasolineLimit, decimal DieselLimit);

public sealed record ItemRow(
    Guid Id,
    Guid GroupId,
    string GroupName,
    Guid UnitId,
    string UnitName,
    string Name,
    string Code,
    decimal? Density,
    string QualityInfo,
    decimal? Temperature,
    string MeasurementNote,
    decimal Vcf,
    string ConversionRule);

public sealed record WarehouseRow(
    Guid Id,
    string Code,
    string Name,
    WarehouseType Type,
    string TypeName,
    Guid? DefaultImportSampleSetId,
    Guid? DefaultExportSampleSetId,
    bool IsConsumerLocation = false,
    string? ConsumerTypeName = null);

public sealed record ConsumerRow(
    Guid Id,
    string Code,
    string Name,
    ConsumerType Type,
    string TypeName,
    Guid? DefaultGroupId,
    string? DefaultGroupName,
    Guid? DefaultItemId,
    string? DefaultItemName,
    decimal? Norm,
    decimal? EffectiveNorm,
    IReadOnlyList<ConsumerNormFactorRow> NormFactors,
    Guid? DefaultImportSampleSetId,
    Guid? DefaultExportSampleSetId,
    bool RollTransfersIntoQuarter = true,
    decimal MainMachineCount = 0,
    decimal AuxMachineCount = 0,
    string ShipType = "");

public sealed record InboundTransferSum(
    Guid DestinationWarehouseId,
    string ItemNameKey,
    long UnitPrice,
    Guid LotTypeId,
    string LotTypeCode,
    decimal ActualQuantity);

/// <summary>Một dòng phiếu điều chuyển đến máy/xe trong quý (để hiện popup).</summary>
public sealed record InboundTransferSlipRow(
    Guid DocumentId,
    DateTime DocumentDate,
    string FormNumber,
    string DisplayNumber,
    string ItemName,
    long UnitPrice,
    Guid LotTypeId,
    string LotTypeCode,
    decimal ActualQuantity);

/// <summary>Sổ tiêu thụ máy/xe theo phiếu điều chuyển trong quý (mẫu 3-04.1/XD-14).</summary>
public sealed record ConsumerTransferBook(
    string ConsumerName,
    string PlateNumber,
    string FuelUsed,
    string FormId,
    string UnitNote,
    decimal FuelTransferTotal,
    decimal OilTransferTotal,
    IReadOnlyList<ConsumerTransferBookRow> Rows);

public sealed record ConsumerTransferBookRow(
    bool IsOpening,
    Guid? DocumentId,
    string DocumentNumber,
    DateTime? DocumentDate,
    string Description,
    string Origin,
    string Destination,
    decimal? Kilometers,
    decimal? MachineHours,
    decimal? NormQuantity,
    decimal? ActualQuantity,
    decimal? OverQuantity,
    decimal? UnderQuantity,
    decimal? FuelIn,
    decimal? FuelOut,
    decimal? FuelBalance,
    decimal? OilIn,
    decimal? OilOut,
    decimal? OilBalance,
    bool ManualFuelOut = false,
    bool ManualOilOut = false,
    Guid? LineId = null,
    Guid LotTypeId = default,
    string LotTypeCode = "TX",
    bool IsManualRow = false,
    string FuelBalanceLotTip = "",
    string OilBalanceLotTip = "",
    Guid? MissionTaskId = null,
    string MissionTaskName = "");

public sealed class ConsumerQuarterBookSaveRequest
{
    public Guid ConsumerId { get; init; }
    public DateTime QuarterDate { get; init; }
    public IReadOnlyList<ConsumerQuarterBookLineEdit> Lines { get; init; } = [];
}

public sealed class ConsumerQuarterBookLineEdit
{
    public Guid? LineId { get; init; }
    public Guid? TransferDocumentId { get; init; }
    public string DocumentNumber { get; init; } = "";
    public DateTime? DocumentDate { get; init; }
    public string Description { get; init; } = "";
    public decimal? Kilometers { get; init; }
    public decimal? MachineHours { get; init; }
    public decimal? NormQuantity { get; init; }
    public decimal? ActualQuantity { get; init; }
    public bool ManualFuelOut { get; init; }
    public decimal FuelOut { get; init; }
    public bool ManualOilOut { get; init; }
    public decimal OilOut { get; init; }
    public Guid LotTypeId { get; init; }
    public string LotTypeCode { get; init; } = "TX";
    public Guid? MissionTaskId { get; init; }
}

/// <summary>Sổ tiêu thụ quý tàu (mẫu 3-04.3/XD-14).</summary>
public sealed record ShipQuarterBookDto(
    Guid? BookId,
    Guid ConsumerId,
    string ShipName,
    string ShipType,
    string FuelUsed,
    string FuelGroupName,
    bool FuelIsGasoline,
    string FormId,
    string UnitNote,
    int Year,
    int Quarter,
    decimal MainMachineCount,
    decimal AuxMachineCount,
    IReadOnlyDictionary<string, decimal> NormRates,
    IReadOnlyList<ShipQuarterBookRowDto> Rows);

public sealed record ShipQuarterBookRowDto(
    bool IsOpening,
    bool IsTransfer,
    Guid? LineId,
    string DocumentNumber,
    DateTime? DocumentDate,
    string Description,
    decimal? MainOpsCount,
    decimal? HoursAtBerth,
    decimal? HoursCx25,
    decimal? HoursCx50,
    decimal? HoursCx75,
    decimal? HoursCx100,
    decimal? MainHoursTotal,
    decimal? AuxOpsCount,
    decimal? AuxHours,
    decimal? GasolineUse,
    decimal? DieselUse,
    bool ManualFuelOut,
    bool ManualOilOut,
    decimal? FuelIn,
    decimal? FuelOut,
    decimal? FuelBalance,
    decimal? OilIn,
    decimal? OilOut,
    decimal? OilBalance,
    decimal? RowTotal,
    Guid LotTypeId = default,
    string LotTypeCode = "TX",
    string FuelBalanceLotTip = "",
    string OilBalanceLotTip = "",
    Guid? MissionTaskId = null,
    string MissionTaskName = "");

public sealed class ShipQuarterBookSaveRequest
{
    public Guid ConsumerId { get; init; }
    public DateTime QuarterDate { get; init; }
    public string FuelGroupName { get; init; } = "";
    public IReadOnlyList<ShipQuarterBookLineEdit> Lines { get; init; } = [];
}

public sealed class ShipQuarterBookLineEdit
{
    public Guid? LineId { get; init; }
    public string DocumentNumber { get; init; } = "";
    public DateTime? DocumentDate { get; init; }
    public string Description { get; init; } = "";
    public decimal MainOpsCount { get; init; }
    public decimal HoursAtBerth { get; init; }
    public decimal HoursCx25 { get; init; }
    public decimal HoursCx50 { get; init; }
    public decimal HoursCx75 { get; init; }
    public decimal HoursCx100 { get; init; }
    public decimal AuxOpsCount { get; init; }
    public decimal AuxHours { get; init; }
    public bool ManualFuelOut { get; init; }
    public decimal? FuelOutManual { get; init; }
    public bool ManualOilOut { get; init; }
    public decimal OilOut { get; init; }
    public Guid LotTypeId { get; init; }
    public string LotTypeCode { get; init; } = "TX";
    public Guid? MissionTaskId { get; init; }
}

/// <summary>
/// Gợi ý tiêu thụ quý từ tổng điều chuyển.
/// SavedQuantity null = ô chưa lưu (điền sẵn); có giá trị và nhỏ hơn ActualQuantity = đề nghị cập nhật.
/// </summary>
public sealed record QuarterRollupSuggestion(
    Guid WarehouseId,
    string ItemNameKey,
    long UnitPrice,
    decimal ActualQuantity,
    decimal? SavedQuantity = null,
    Guid LotTypeId = default,
    string LotTypeCode = "TX")
{
    public bool NeedsUpdate => SavedQuantity is decimal saved && ActualQuantity > saved;
    public bool NeedsPrefill => SavedQuantity is null && ActualQuantity > 0;
}

public sealed record ConsumerNormFactorRow(Guid Id, Guid? GroupId, string Name, decimal Value, int SortOrder);

public sealed record FieldRow(
    Guid Id,
    DocumentFamily Family,
    string FamilyName,
    string Name,
    FieldDataType DataType,
    string DataTypeName,
    bool IsRequired,
    bool IsVisible,
    int SortOrder);

public sealed record SampleSetRow(Guid Id, string Name, DocumentFamily Family, string FamilyName);
public sealed record SampleValueRow(Guid Id, Guid SampleSetId, Guid FieldId, string FieldName, string Value);

public sealed record FieldFormRow(
    Guid FieldId,
    string Name,
    FieldDataType DataType,
    string DataTypeName,
    bool IsRequired,
    string Value,
    IReadOnlyList<string> Options);

public sealed record LotOption(
    Guid LotId,
    Guid? ItemId,
    string ItemName,
    long UnitPrice,
    Guid LotTypeId,
    string LotTypeCode,
    decimal Quantity,
    decimal FirstVcf,
    string Display,
    string GroupName = "",
    string Origin = "Tự mua");

public sealed record StockFilter(
    Guid? WarehouseId,
    string? GroupName,
    string? ItemName,
    decimal? UnitPrice,
    bool IncludeZero = false,
    IReadOnlyCollection<Guid>? WarehouseIds = null);

public sealed record StockRow(
    Guid LotId,
    Guid WarehouseId,
    string WarehouseName,
    string WarehouseTypeName,
    string GroupName,
    string ItemName,
    string UnitName,
    long UnitPrice,
    Guid LotTypeId,
    string LotTypeCode,
    decimal Quantity,
    string Origin = "Tự mua");

public enum NxtRowKind
{
    Opening,
    Slip,
    Period,
    Closing
}

/// <summary>Cột số chứng từ trên sổ NXT: N = nhập; XX = xuất có xăng; XD = phiếu còn lại.</summary>
public enum NxtVoucherColumn
{
    None,
    N,
    Xx,
    Xd
}

public sealed record NxtColumn(string Title, bool IsGroupTotal);

public sealed record NxtCell(decimal? In, decimal? Out, decimal? Balance);

public sealed record NxtRow(
    NxtRowKind Kind,
    Guid? DocumentId,
    DocumentKind? DocumentKind,
    string Number,
    DateTime? Date,
    string Description,
    decimal? Kilometers,
    string Mission,
    IReadOnlyList<NxtCell> Cells,
    NxtVoucherColumn VoucherColumn = NxtVoucherColumn.None);

public sealed record NxtSheet(IReadOnlyList<NxtColumn> Columns, IReadOnlyList<NxtRow> Rows);

/// <summary>Một dòng NXT tổng: mặt hàng + đơn giá + loại lô tách theo Kho hải đoàn, Máy, Phương tiện, Tàu và Tổng.</summary>
public sealed record NxtTotalRow(
    string GroupName,
    string ItemName,
    long UnitPrice,
    Guid LotTypeId,
    string LotTypeCode,
    // Tồn đầu
    decimal OpeningMain,
    decimal OpeningMachine,
    decimal OpeningVehicle,
    decimal OpeningShip,
    decimal OpeningTotal,
    // Nhập
    decimal InMain,
    decimal InMachine,
    decimal InVehicle,
    decimal InShip,
    decimal InTotal,
    // Xuất
    decimal OutMain,
    decimal OutMachine,
    decimal OutVehicle,
    decimal OutShip,
    decimal OutTotal,
    // Tồn sau
    decimal ClosingMain,
    decimal ClosingMachine,
    decimal ClosingVehicle,
    decimal ClosingShip,
    decimal ClosingTotal,
    bool IsGroupTotal = false,
    string Note = "")
{
    public NxtTotalRow(
        string groupName,
        string itemName,
        long unitPrice,
        Guid lotTypeId,
        string lotTypeCode,
        decimal opening,
        decimal @in,
        decimal @out,
        decimal closing,
        bool isGroupTotal = false,
        string note = "")
        : this(groupName, itemName, unitPrice, lotTypeId, lotTypeCode,
            opening, 0, 0, 0, opening,
            @in, 0, 0, 0, @in,
            @out, 0, 0, 0, @out,
            closing, 0, 0, 0, closing,
            isGroupTotal, note)
    {
    }

    public decimal Opening => OpeningTotal;
    public decimal In => InTotal;
    public decimal Out => OutTotal;
    public decimal Closing => ClosingTotal;
}

public sealed record NxtTotalSheet(IReadOnlyList<NxtTotalRow> Rows);

public sealed record MovementRow(
    Guid DocumentId,
    string DocumentNumber,
    DateTime OccurredAt,
    string Reason,
    string WarehouseName,
    string ItemName,
    long UnitPrice,
    Guid LotTypeId,
    string LotTypeCode,
    decimal SignedQuantity,
    string Origin = "Tự mua");

public sealed record SheetHeader(
    Guid Id,
    DocumentKind Kind,
    DocumentStatus Status,
    DateTime DocumentDate,
    Guid? ItemId,
    string ItemName,
    string GroupName,
    string UnitName,
    string QualityInfo,
    decimal? Temperature,
    string MeasurementNote,
    decimal Vcf,
    Guid? WarehouseId,
    long UnitPrice,
    Guid? LotTypeId,
    string LotTypeCode,
    string Origin,
    decimal ActualQuantity,
    decimal? Distance,
    Guid? ConsumerId = null,
    decimal? Norm = null,
    decimal? OperatingQuantity = null);

public sealed record DocumentRow(
    Guid Id,
    string Number,
    DocumentKind Kind,
    string KindName,
    DocumentStatus Status,
    string StatusName,
    DateTime DocumentDate,
    string WarehouseName,
    string DestinationWarehouseName,
    string LotTypeCode,
    string ItemName,
    string ConsumerName,
    long UnitPrice,
    decimal InputQuantity,
    decimal ActualQuantity,
    decimal Vcf,
    decimal? Amount,
    bool WasSplit,
    string FormNumber,
    string Nature,
    string ReceiverPerson,
    string VehiclePlate,
    string Kilometers,
    string Mission,
    string DisplayNumber,
    decimal? Distance,
    string Origin = "Tự mua");

public sealed record LineRow(
    int LineNo,
    Guid LotId,
    Guid WarehouseId,
    string ItemName,
    long UnitPrice,
    Guid? LotTypeId,
    string LotTypeCode,
    Guid? DestinationLotTypeId,
    string DestinationLotTypeCode,
    decimal Quantity,
    decimal ActualQuantity,
    decimal Amount,
    Guid? ItemId,
    string ItemCode,
    string QualityGrade,
    decimal? Temperature,
    decimal? Density,
    decimal Vcf,
    string Origin = "Tự mua");

public sealed record FieldSnapshotRow(string Name, string DataType, bool IsRequired, string Value);

public sealed record DocumentDetail(
    Guid Id,
    string Number,
    DocumentKind Kind,
    string KindName,
    DocumentStatus Status,
    string StatusName,
    DateTime DocumentDate,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid? ItemId,
    string ItemName,
    string GroupName,
    string UnitName,
    string QualityInfo,
    decimal? Temperature,
    string MeasurementNote,
    string ConversionRule,
    decimal Vcf,
    Guid? WarehouseId,
    string WarehouseName,
    string WarehouseTypeName,
    Guid? DestinationWarehouseId,
    string DestinationWarehouseName,
    Guid? ConsumerId,
    string ConsumerName,
    string ConsumerCode,
    string ConsumerTypeName,
    decimal? Norm,
    decimal? Distance,
    decimal? OperatingQuantity,
    bool ManualQuantity,
    long UnitPrice,
    Guid? LotTypeId,
    string LotTypeCode,
    decimal InputQuantity,
    decimal ActualQuantity,
    decimal? Amount,
    bool WasSplit,
    IReadOnlyList<LineRow> Lines,
    IReadOnlyList<FieldSnapshotRow> Fields,
    SlipInfo Slip,
    string Origin = "Tự mua");

public sealed record SlipInfo(
    string FormNumber,
    string OrganizationName,
    string UnitTitle,
    string SenderUnit,
    string ReceiverUnit,
    string Nature,
    string ContractOrOrder,
    string CarrierUnit,
    string PriceValidUntil,
    string DelivererName,
    string IntroDocument,
    string VehiclePlate,
    string CalibrationVolume,
    string ReceivedVolume,
    string PackageCount,
    string ReceiverPerson,
    string Kilometers,
    string Mission,
    string OriginPlace,
    string DestinationPlace,
    string Note,
    string SignerReceiver,
    string SignerDeliverer,
    string SignerFinance,
    string SignerWriter,
    string SignerChief,
    string SignerCommander,
    string AmountInWords,
    Guid? MissionTaskId = null);

public sealed class SlipLineInput
{
    public Guid? LotId { get; init; }
    public Guid? ItemId { get; init; }
    public string ItemName { get; init; } = "";
    public string GroupName { get; init; } = "";
    public string UnitName { get; init; } = "";
    public string ItemCode { get; init; } = "";
    public string QualityGrade { get; init; } = "";
    public decimal ObservedQuantity { get; init; }
    public decimal? Temperature { get; init; }
    public decimal? Density { get; init; }
    public decimal Vcf { get; init; }
    public decimal? ActualQuantity { get; init; }
    public decimal UnitPrice { get; init; }
    public Guid? LotTypeId { get; init; }
    public string Origin { get; init; } = "Tự mua";
    /// <summary>Điều chuyển: loại lô tại kho nhận (null = giữ loại nguồn).</summary>
    public Guid? DestinationLotTypeId { get; init; }
    public decimal? Amount { get; init; }
}

public sealed class SlipRequest
{
    public Guid? DocumentId { get; init; }
    public bool IsExport { get; init; }
    public ExportSlipMode ExportMode { get; init; }
    public Guid? ConsumerId { get; init; }
    public decimal? Distance { get; init; }
    public decimal? Norm { get; init; }
    /// <summary>Xe: bỏ qua km × định mức, dùng thực xuất người dùng nhập.</summary>
    public bool ManualQuantity { get; init; }
    public Guid? DestinationWarehouseId { get; init; }
    public string DestinationWarehouseName { get; init; } = "";
    public DateTime DocumentDate { get; init; }
    public string FormNumber { get; init; } = "";
    public Guid WarehouseId { get; init; }
    public string WarehouseName { get; init; } = "";
    public string WarehouseTypeName { get; init; } = "";
    public string OrganizationName { get; init; } = "";
    public string UnitTitle { get; init; } = "";
    public string SenderUnit { get; init; } = "";
    public string ReceiverUnit { get; init; } = "";
    public string Nature { get; init; } = "";
    public string ContractOrOrder { get; init; } = "";
    public string CarrierUnit { get; init; } = "";
    public string PriceValidUntil { get; init; } = "";
    public string DelivererName { get; init; } = "";
    public string IntroDocument { get; init; } = "";
    public string VehiclePlate { get; init; } = "";
    public string CalibrationVolume { get; init; } = "";
    public string ReceivedVolume { get; init; } = "";
    public string PackageCount { get; init; } = "";
    public string ReceiverPerson { get; init; } = "";
    public string Kilometers { get; init; } = "";
    public string Mission { get; init; } = "";
    public Guid? MissionTaskId { get; init; }
    public string OriginPlace { get; init; } = "";
    public string DestinationPlace { get; init; } = "";
    public string Note { get; init; } = "";
    public string SignerReceiver { get; init; } = "";
    public string SignerDeliverer { get; init; } = "";
    public string SignerFinance { get; init; } = "";
    public string SignerWriter { get; init; } = "";
    public string SignerChief { get; init; } = "";
    public string SignerCommander { get; init; } = "";
    public string AmountInWords { get; init; } = "";
    public IReadOnlyList<SlipLineInput> Lines { get; init; } = [];
    public IReadOnlyList<FieldInput> Fields { get; init; } = [];
    public Guid? AddToSampleSetId { get; init; }
}

public sealed record DashboardSummary(
    int ItemCount,
    int WarehouseCount,
    int ConsumerCount,
    decimal TotalStock,
    IReadOnlyList<DocumentRow> RecentDocuments);
