using System.Globalization;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace APPQLXD.Core.Services;

public sealed class FuelLedger
{
    private readonly Func<AppDbContext> _factory;

    public FuelLedger(Func<AppDbContext> factory) => _factory = factory;

    public ImportPreview PreviewImport(decimal unitPrice, decimal quantity, decimal? amount, decimal vcf) =>
        ImportLotSplitter.Split(unitPrice, quantity, amount, vcf);

    public FuelResult SaveOpening(OpeningRequest request) => InTx(db =>
    {
        var touched = new HashSet<(Guid, Guid)>();
        var id = WriteOpening(db, request, touched);
        EnsureNonNegative(db, touched);
        return FuelResult.Success(id, "Đã ghi nhận tồn đầu kỳ.");
    });

    public FuelResult SaveOpeningSheet(OpeningSheetRequest request) => InTx(db =>
    {
        if (request.Cells.Count == 0 && request.VoidIds.Count == 0)
            return FuelResult.Fail("Chưa có số lượng tồn đầu để lưu.");
        var touched = new HashSet<(Guid, Guid)>();
        foreach (var id in request.VoidIds.Distinct())
        {
            var doc = db.Documents.FirstOrDefault(x => x.Id == id)
                ?? throw new FuelRuleException("Không tìm thấy chứng từ.");
            if (doc.Kind != DocumentKind.Opening)
                return FuelResult.Fail("Bảng tồn đầu chỉ xóa được phiếu tồn đầu.");
            if (doc.Status != DocumentStatus.Active)
                continue;
            RemoveEffects(db, doc, touched);
            doc.Status = DocumentStatus.Voided;
            doc.UpdatedAt = DateTime.UtcNow;
        }

        Guid? first = null;
        foreach (var cell in request.Cells)
        {
            var id = WriteOpening(db, cell, touched);
            first ??= id;
        }

        EnsureNonNegative(db, touched);
        var message = request.VoidIds.Count == 0
            ? "Đã lưu bảng tồn đầu."
            : "Đã lưu bảng tồn đầu và hoàn tồn các ô đã xóa.";
        return FuelResult.Success(first ?? request.VoidIds[0], message);
    });

    public FuelResult SaveImport(ImportRequest request) => InTx(db =>
    {
        var touched = new HashSet<(Guid, Guid)>();
        var item = RequireItem(db, request.ItemId);
        var warehouse = RequireWarehouse(db, request.WarehouseId);
        var preview = ImportLotSplitter.Split(request.UnitPrice, request.InputQuantity, request.Amount, request.Vcf);
        if (!preview.Ok)
            throw new FuelRuleException(preview.Message);

        var doc = Begin(db, request.DocumentId, DocumentKind.Import, touched);
        var itemName = Or(request.ItemName, item.Name);
        var groupName = Or(request.GroupName, item.Group?.Name ?? "");
        var unitName = Or(request.UnitName, item.Unit?.Name ?? "");
        var quality = Or(request.QualityInfo, item.QualityInfo);
        var measurement = Or(request.MeasurementNote, item.MeasurementNote);
        var rule = Or(request.ConversionRule, item.ConversionRule);
        var vcf = QuantityMath.RoundVcf(request.Vcf);
        WriteHeader(doc, request.DocumentDate, item.Id, itemName, groupName, unitName, quality, request.Temperature ?? item.Temperature, measurement, rule, vcf,
            warehouse.Id, Or(request.WarehouseName, warehouse.Name), Or(request.WarehouseTypeName, Labels.Warehouse(warehouse.Type)));
        doc.UnitPrice = preview.Lines[0].UnitPrice;
        if (!QuantityMath.TryWholeMoney(request.UnitPrice, out var enteredPrice))
            throw new FuelRuleException("Đơn giá phải là số nguyên không âm.");
        doc.UnitPrice = enteredPrice;
        doc.InputQuantity = preview.TotalQuantity;
        doc.ActualQuantity = preview.TotalActual;
        doc.Amount = preview.TotalAmount;
        doc.WasSplit = preview.WasSplit;
        var lotType = ResolveLotType(db, request.LotTypeId);
        doc.LotTypeId = lotType.Id;
        doc.LotTypeCode = lotType.Code;
        foreach (var line in preview.Lines)
        {
            var lot = EnsureLot(db, itemName, line.UnitPrice, lotType.Id, item.Id, groupName, unitName, quality, doc.Temperature, measurement, rule, vcf);
            var docLine = NewLine(doc, line.LineNo, lot, warehouse.Id, line.Quantity, line.Actual, line.Amount);
            docLine.LotTypeId = lotType.Id;
            docLine.LotTypeCode = lotType.Code;
            AttachLine(db, doc, docLine);
        }

        ApplyFields(db, doc, DocumentFamily.Import, WithAutoImportFormNumber(db, doc, request.Fields), request.AddToSampleSetId);
        AddEffects(db, doc, touched);
        EnsureNonNegative(db, touched);
        return FuelResult.Success(doc.Id, preview.WasSplit ? "Đã lưu phiếu nhập và tách 2 lô." : "Đã lưu phiếu nhập.");
    });

    public FuelResult SaveSlip(SlipRequest request) => InTx(db =>
    {
        if (request.Lines.Count == 0)
            throw new FuelRuleException("Phiếu cần ít nhất một dòng hàng.");
        var warehouse = RequireWarehouse(db, request.WarehouseId);
        var kind = ResolveSlipKind(request);
        if (kind == DocumentKind.Auxiliary && warehouse.Type != WarehouseType.Auxiliary)
            return FuelResult.Fail("Chỉ tiêu thụ trên kho phụ.");
        Warehouse? destination = null;
        if (kind == DocumentKind.Transfer)
        {
            if (request.DestinationWarehouseId is not Guid destinationId)
                return FuelResult.Fail("Chọn kho nhận.");
            destination = RequireWarehouse(db, destinationId);
            if (destination.Id == warehouse.Id)
                return FuelResult.Fail("Kho nguồn và kho nhận phải khác nhau.");
            if (!SameWarehouseFamily(warehouse.Type, destination.Type))
                return FuelResult.Fail("Không điều chuyển giữa Kho XD và Kho PTKT-VTXD.");
        }
        else if (kind == DocumentKind.LotConvert)
        {
            // Đổi loại cùng kho: kho nhận = kho nguồn (ghi nhận trên chứng từ).
            destination = warehouse;
        }

        var touched = new HashSet<(Guid, Guid)>();
        var doc = Begin(db, request.DocumentId, kind, touched);
        var built = new List<(FuelDocumentLine Line, string ItemName, string Group, string Unit, string Quality, decimal? Temperature, string Rule, decimal Vcf, Guid? ItemId)>();
        var lineNo = 1;
        foreach (var input in request.Lines)
        {
            FuelItem? item = input.ItemId is Guid itemId ? RequireItem(db, itemId) : null;
            var itemName = Or(input.ItemName, item?.Name ?? "");
            if (itemName.Length == 0 && input.LotId is null)
                throw new FuelRuleException($"Dòng {lineNo} thiếu tên xăng dầu.");
            var groupName = Or(input.GroupName, item?.Group?.Name ?? "");
            var unitName = Or(input.UnitName, item?.Unit?.Name ?? "");
            var quality = Or(input.QualityGrade, item?.QualityInfo ?? "");
            var rule = Or(item?.ConversionRule ?? "", Labels.DefaultConversionRule);
            var vcf = QuantityMath.RoundVcf(input.Vcf);
            if (vcf <= 0)
                throw new FuelRuleException($"Dòng {lineNo}: hệ số VCF phải lớn hơn 0.");
            long price;
            Lot lot;
            LotType lotType;
            if (request.IsExport && input.LotId is Guid selectedLotId)
            {
                lot = RequireLot(db, selectedLotId);
                price = lot.UnitPrice;
                itemName = lot.ItemName;
                lotType = ResolveLotType(db, lot.LotTypeId);
            }
            else
            {
                if (!QuantityMath.TryWholeMoney(input.UnitPrice, out price) || price < 0)
                    throw new FuelRuleException($"Dòng {lineNo}: đơn giá phải là số nguyên không âm.");
                lotType = ResolveLotType(db, input.LotTypeId);
                if (request.IsExport)
                {
                    var key = QuantityMath.LotKey(itemName);
                    lot = db.Lots.Local.FirstOrDefault(x => x.ItemNameKey == key && x.UnitPrice == price && x.LotTypeId == lotType.Id)
                        ?? db.Lots.FirstOrDefault(x => x.ItemNameKey == key && x.UnitPrice == price && x.LotTypeId == lotType.Id)
                        ?? throw new FuelRuleException($"Không có lô {itemName} đơn giá {price:N0} loại {lotType.Code} để xuất.");
                }
                else
                    lot = EnsureLot(db, itemName, price, lotType.Id, item?.Id, groupName, unitName, quality, input.Temperature ?? item?.Temperature, item?.MeasurementNote ?? "", rule, vcf);
            }
            var observed = QuantityMath.Whole(input.ObservedQuantity);
            var actual = input.ActualQuantity is decimal given && given > 0
                ? QuantityMath.Whole(given)
                : QuantityMath.ActualImport(observed, vcf);
            if (actual <= 0)
                throw new FuelRuleException($"Dòng {lineNo}: thực tế phải lớn hơn 0.");
            if (observed <= 0)
                observed = actual;
            decimal amount = input.Amount is decimal givenAmount && QuantityMath.TryWholeMoney(givenAmount, out var money)
                ? money
                : decimal.Round(actual * price, 0, MidpointRounding.AwayFromZero);

            var line = NewLine(doc, lineNo, lot, warehouse.Id, observed, actual, amount);
            line.ItemId = item?.Id;
            line.ItemCode = Or(input.ItemCode, "");
            line.QualityGrade = quality;
            line.Temperature = input.Temperature;
            line.Density = input.Density;
            line.Vcf = vcf;
            line.LotTypeId = lot.LotTypeId;
            line.LotTypeCode = lotType.Code;
            if (kind == DocumentKind.Transfer)
            {
                var destType = ResolveLotType(db, input.DestinationLotTypeId ?? lot.LotTypeId);
                if (destType.Id != lot.LotTypeId)
                    throw new FuelRuleException(
                        $"Dòng {lineNo}: đổi loại lô khi điều chuyển chưa hoàn thiện. Hãy giữ cùng loại nguồn và đích.");
                line.DestinationLotTypeId = lot.LotTypeId;
                line.DestinationLotTypeCode = lotType.Code;
            }
            else if (kind == DocumentKind.LotConvert)
            {
                if (input.DestinationLotTypeId is not Guid destTypeId || destTypeId == Guid.Empty)
                    throw new FuelRuleException($"Dòng {lineNo}: chọn loại lô đích.");
                var destType = ResolveLotType(db, destTypeId);
                if (destType.Id == lot.LotTypeId)
                    throw new FuelRuleException($"Dòng {lineNo}: loại lô đích phải khác loại nguồn.");
                if (!IsTxSscdConvertPair(lotType, destType))
                    throw new FuelRuleException($"Dòng {lineNo}: chỉ được đổi giữa TX và SSCĐ.");
                line.DestinationLotTypeId = destType.Id;
                line.DestinationLotTypeCode = destType.Code;
            }
            built.Add((line, itemName, groupName, unitName, quality, input.Temperature ?? item?.Temperature, rule, vcf, item?.Id));
            lineNo++;
        }

        if (kind is DocumentKind.Transfer or DocumentKind.LotConvert)
        {
            var typeIds = built.Select(x => x.Line.LotTypeId ?? SeedIds.LotTypeTx).Distinct().ToList();
            if (typeIds.Count > 1)
                throw new FuelRuleException(
                    kind == DocumentKind.LotConvert
                        ? "Một phiếu đổi loại lô chỉ được một loại nguồn. Hãy tách thành nhiều phiếu."
                        : "Một phiếu điều chuyển chỉ được một loại lô (chức năng nhiều loại trên cùng phiếu chưa hoàn thiện). Hãy tách thành nhiều phiếu.");
        }

        decimal? vehicleDistance = null;
        decimal? vehicleNorm = null;
        Consumer? vehicle = null;
        Consumer? machine = null;
        if (kind == DocumentKind.Issue && request.ConsumerId is Guid machineId && machineId != Guid.Empty)
        {
            machine = db.Consumers.FirstOrDefault(x => x.Id == machineId)
                ?? throw new FuelRuleException("Chọn máy.");
            if (machine.Type != ConsumerType.Machine)
                throw new FuelRuleException("Xuất cho máy chỉ dùng đối tượng là máy.");
        }

        if (kind == DocumentKind.Consumption)
        {
            vehicle = db.Consumers.FirstOrDefault(x => x.Id == request.ConsumerId)
                ?? throw new FuelRuleException("Chọn phương tiện.");
            if (vehicle.Type != ConsumerType.Vehicle)
                throw new FuelRuleException("Xuất phương tiện chỉ dùng đối tượng là phương tiện.");
            if (request.ManualQuantity)
            {
                var vehicleLine = built[0].Line;
                if (vehicleLine.ActualQuantity <= 0)
                    throw new FuelRuleException("Thực xuất phải lớn hơn 0.");
                vehicleDistance = request.Distance is decimal d && d >= 0 ? QuantityMath.RoundQty(d) : null;
                vehicleNorm = request.Norm is decimal n && n > 0
                    ? QuantityMath.RoundNorm(n)
                    : vehicle.Norm is decimal vn && vn > 0 ? QuantityMath.RoundNorm(vn) : null;
                var display = QuantityMath.ExportDisplayQuantity(vehicleLine.ActualQuantity, vehicleLine.Vcf);
                vehicleLine.Quantity = display;
                vehicleLine.Amount = decimal.Round(vehicleLine.ActualQuantity * vehicleLine.UnitPrice, 0, MidpointRounding.AwayFromZero);
            }
            else
            {
                var resolved = ResolveConsumption(new ConsumptionRequest
                {
                    ConsumerType = ConsumerType.Vehicle,
                    Distance = request.Distance,
                    Norm = request.Norm
                }, vehicle);
                vehicleDistance = resolved.Distance;
                vehicleNorm = resolved.Norm;
                var vehicleLine = built[0].Line;
                var display = QuantityMath.ExportDisplayQuantity(resolved.Actual, vehicleLine.Vcf);
                vehicleLine.Quantity = display;
                vehicleLine.ActualQuantity = resolved.Actual;
                vehicleLine.Amount = decimal.Round(resolved.Actual * vehicleLine.UnitPrice, 0, MidpointRounding.AwayFromZero);
            }
        }

        Consumer? transferConsumer = null;
        if (kind == DocumentKind.Transfer)
        {
            var transferId = request.ConsumerId is Guid cid && cid != Guid.Empty
                ? cid
                : destination?.Id;
            if (transferId is Guid tid && tid != Guid.Empty)
            {
                transferConsumer = db.Consumers.FirstOrDefault(x => x.Id == tid);
                // Kho nhận không phải đối tượng → không bắt buộc ConsumerId.
                if (transferConsumer is null && request.ConsumerId is Guid)
                    throw new FuelRuleException("Đối tượng không tồn tại.");
            }

            if (transferConsumer?.Type == ConsumerType.Vehicle)
            {
                vehicleNorm = request.Norm is decimal n && n > 0
                    ? QuantityMath.RoundNorm(n)
                    : transferConsumer.Norm is decimal vn && vn > 0 ? QuantityMath.RoundNorm(vn) : null;
                // Chỉ tính lại thực xuất từ km×định mức khi Distance được gửi tường minh và không nhập tay.
                if (!request.ManualQuantity && request.Distance is decimal)
                {
                    var resolved = ResolveConsumption(new ConsumptionRequest
                    {
                        ConsumerType = ConsumerType.Vehicle,
                        Distance = request.Distance,
                        Norm = request.Norm
                    }, transferConsumer);
                    vehicleDistance = resolved.Distance;
                    vehicleNorm = resolved.Norm;
                    var transferLine = built[0].Line;
                    var display = QuantityMath.ExportDisplayQuantity(resolved.Actual, transferLine.Vcf);
                    transferLine.Quantity = display;
                    transferLine.ActualQuantity = resolved.Actual;
                    transferLine.Amount = decimal.Round(resolved.Actual * transferLine.UnitPrice, 0, MidpointRounding.AwayFromZero);
                }
                else
                {
                    var transferLine = built[0].Line;
                    if (transferLine.ActualQuantity <= 0)
                        throw new FuelRuleException("Thực xuất phải lớn hơn 0.");
                    vehicleDistance = request.Distance is decimal d && d >= 0
                        ? QuantityMath.RoundQty(d)
                        : TryParseKilometers(request.Kilometers);
                    var display = QuantityMath.ExportDisplayQuantity(transferLine.ActualQuantity, transferLine.Vcf);
                    transferLine.Quantity = display;
                    transferLine.Amount = decimal.Round(transferLine.ActualQuantity * transferLine.UnitPrice, 0, MidpointRounding.AwayFromZero);
                }
            }
        }

        var first = built[0];
        var summary = built.Count == 1 ? first.ItemName : $"{first.ItemName} (+{built.Count - 1})";
        WriteHeader(doc, request.DocumentDate, first.ItemId ?? Guid.Empty, summary, first.Group, first.Unit, first.Quality, first.Temperature, "", first.Rule, first.Vcf,
            warehouse.Id, Or(request.WarehouseName, warehouse.Name), Or(request.WarehouseTypeName, Labels.Warehouse(warehouse.Type)));
        if (first.ItemId is null)
            doc.ItemId = null;
        doc.UnitPrice = built[0].Line.UnitPrice;
        doc.LotTypeId = built[0].Line.LotTypeId;
        doc.LotTypeCode = built[0].Line.LotTypeCode;
        doc.InputQuantity = QuantityMath.Whole(built.Sum(x => x.Line.Quantity));
        doc.ActualQuantity = QuantityMath.Whole(built.Sum(x => x.Line.ActualQuantity));
        doc.Amount = built.Sum(x => x.Line.Amount);
        doc.WasSplit = false;
        doc.ManualQuantity = request.ManualQuantity
            && (vehicle is not null || transferConsumer?.Type == ConsumerType.Vehicle);
        doc.ConsumerName = Or(request.ReceiverPerson, "");
        if (vehicle is not null)
        {
            doc.ConsumerId = vehicle.Id;
            doc.ConsumerName = vehicle.Name;
            doc.ConsumerCode = vehicle.Code;
            doc.ConsumerTypeName = Labels.Consumer(ConsumerType.Vehicle);
            doc.Norm = vehicleNorm;
            doc.Distance = vehicleDistance;
        }
        else if (machine is not null)
        {
            doc.ConsumerId = machine.Id;
            doc.ConsumerName = machine.Name;
            doc.ConsumerCode = machine.Code;
            doc.ConsumerTypeName = Labels.Consumer(ConsumerType.Machine);
        }
        else if (transferConsumer is not null)
        {
            doc.ConsumerId = transferConsumer.Id;
            doc.ConsumerName = transferConsumer.Name;
            doc.ConsumerCode = transferConsumer.Code;
            doc.ConsumerTypeName = Labels.Consumer(transferConsumer.Type);
            doc.Norm = vehicleNorm;
            doc.Distance = vehicleDistance;
        }
        if (destination is not null)
        {
            doc.DestinationWarehouseId = destination.Id;
            doc.DestinationWarehouseName = Or(request.DestinationWarehouseName, destination.Name);
        }

        ApplySlipMeta(doc, request, doc.Amount.Value);
        if (doc.Distance is decimal savedKm
            && savedKm >= 0
            && string.IsNullOrWhiteSpace(doc.Kilometers))
            doc.Kilometers = savedKm.ToString(CultureInfo.InvariantCulture);
        foreach (var row in built)
            AttachLine(db, doc, row.Line);
        var fields = request.IsExport
            ? request.Fields
            : WithAutoImportFormNumber(db, doc, request.Fields);
        ApplyFields(db, doc, request.IsExport ? DocumentFamily.Export : DocumentFamily.Import, fields, request.AddToSampleSetId);
        AddEffects(db, doc, touched);
        EnsureNonNegative(db, touched);
        var message = kind switch
        {
            DocumentKind.Transfer => "Đã lưu phiếu điều chuyển.",
            DocumentKind.LotConvert => "Đã lưu phiếu đổi loại lô.",
            DocumentKind.Auxiliary => "Đã lưu tiêu thụ kho phụ.",
            DocumentKind.Consumption => "Đã lưu phiếu xuất phương tiện.",
            DocumentKind.Issue => "Đã lưu phiếu xuất.",
            _ => "Đã lưu phiếu nhập."
        };
        return FuelResult.Success(doc.Id, message);
    });

    public FuelResult SaveTransfer(TransferRequest request) => InTx(db =>
    {
        if (request.SourceWarehouseId == request.DestinationWarehouseId)
            return FuelResult.Fail("Kho nguồn và kho nhận phải khác nhau.");
        var source = RequireWarehouse(db, request.SourceWarehouseId);
        var destination = RequireWarehouse(db, request.DestinationWarehouseId);
        if (!SameWarehouseFamily(source.Type, destination.Type))
            return FuelResult.Fail("Không điều chuyển giữa Kho XD và Kho PTKT-VTXD.");
        var lot = RequireLot(db, request.LotId);
        var sourceType = ResolveLotType(db, lot.LotTypeId);
        var destType = ResolveLotType(db, request.DestinationLotTypeId ?? lot.LotTypeId);
        if (destType.Id != sourceType.Id)
            return FuelResult.Fail("Đổi loại lô khi điều chuyển chưa hoàn thiện. Hãy giữ cùng loại nguồn và đích.");
        var actual = QuantityMath.Whole(request.ActualQuantity);
        if (actual <= 0)
            throw new FuelRuleException("Thực xuất phải lớn hơn 0.");
        var vcf = QuantityMath.RoundVcf(request.Vcf);
        var display = QuantityMath.ExportDisplayQuantity(actual, vcf);
        var touched = new HashSet<(Guid, Guid)>();
        var doc = Begin(db, request.DocumentId, DocumentKind.Transfer, touched);
        WriteLotHeader(doc, request.DocumentDate, lot, vcf, source.Id, Or(request.SourceWarehouseName, source.Name), Or(request.SourceWarehouseTypeName, Labels.Warehouse(source.Type)));
        doc.DestinationWarehouseId = destination.Id;
        doc.DestinationWarehouseName = Or(request.DestinationWarehouseName, destination.Name);
        doc.OriginPlace = Or(request.OriginPlace, "");
        doc.DestinationPlace = Or(request.DestinationPlace, "");
        doc.UnitPrice = lot.UnitPrice;
        doc.LotTypeId = sourceType.Id;
        doc.LotTypeCode = sourceType.Code;
        doc.InputQuantity = display;
        doc.ActualQuantity = actual;
        doc.Amount = null;
        doc.WasSplit = false;
        var line = NewLine(doc, 1, lot, source.Id, display, actual, 0);
        line.LotTypeId = sourceType.Id;
        line.LotTypeCode = sourceType.Code;
        line.DestinationLotTypeId = sourceType.Id;
        line.DestinationLotTypeCode = sourceType.Code;
        AttachLine(db, doc, line);
        ApplyFields(db, doc, DocumentFamily.Export, request.Fields, request.AddToSampleSetId);
        AddEffects(db, doc, touched);
        EnsureNonNegative(db, touched);
        return FuelResult.Success(doc.Id, "Đã lưu phiếu điều chuyển.");
    });

    public FuelResult SaveConsumption(ConsumptionRequest request) => InTx(db =>
    {
        var consumer = db.Consumers.FirstOrDefault(x => x.Id == request.ConsumerId)
            ?? throw new FuelRuleException("Đối tượng tiêu thụ không tồn tại.");
        var warehouse = RequireWarehouse(db, request.WarehouseId);
        var lot = RequireLot(db, request.LotId);
        var (actual, distance, norm) = ResolveConsumption(request, consumer);
        var vcf = QuantityMath.RoundVcf(request.Vcf);
        var display = QuantityMath.ExportDisplayQuantity(actual, vcf);
        var touched = new HashSet<(Guid, Guid)>();
        var doc = Begin(db, request.DocumentId, DocumentKind.Consumption, touched);
        WriteLotHeader(doc, request.DocumentDate, lot, vcf, warehouse.Id, Or(request.WarehouseName, warehouse.Name), Or(request.WarehouseTypeName, Labels.Warehouse(warehouse.Type)));
        doc.ConsumerId = consumer.Id;
        doc.ConsumerName = Or(request.ConsumerName, consumer.Name);
        doc.ConsumerCode = Or(request.ConsumerCode, consumer.Code);
        doc.ConsumerTypeName = Labels.Consumer(request.ConsumerType);
        doc.Norm = norm;
        doc.Distance = distance;
        doc.UnitPrice = lot.UnitPrice;
        doc.LotTypeId = lot.LotTypeId;
        doc.LotTypeCode = ResolveLotType(db, lot.LotTypeId).Code;
        doc.InputQuantity = display;
        doc.ActualQuantity = actual;
        doc.Amount = null;
        var consumeLine = NewLine(doc, 1, lot, warehouse.Id, display, actual, 0);
        consumeLine.LotTypeId = lot.LotTypeId;
        consumeLine.LotTypeCode = doc.LotTypeCode;
        AttachLine(db, doc, consumeLine);
        ApplyFields(db, doc, DocumentFamily.Export, request.Fields, request.AddToSampleSetId);
        AddEffects(db, doc, touched);
        EnsureNonNegative(db, touched);
        return FuelResult.Success(doc.Id, "Đã lưu phiếu xuất tiêu thụ.");
    });

    public FuelResult SaveConsumptionSheet(ConsumptionSheetRequest request) => InTx(db =>
    {
        if (request.Cells.Count == 0 && request.VoidIds.Count == 0)
            return FuelResult.Fail("Chưa có lượng tiêu thụ để lưu.");
        var touched = new HashSet<(Guid, Guid)>();
        foreach (var id in request.VoidIds.Distinct())
        {
            var doc = db.Documents.FirstOrDefault(x => x.Id == id)
                ?? throw new FuelRuleException("Không tìm thấy chứng từ.");
            if (doc.Kind != DocumentKind.Consumption)
                return FuelResult.Fail("Bảng xuất tiêu thụ chỉ xóa được phiếu xuất tiêu thụ.");
            if (doc.Status != DocumentStatus.Active)
                continue;
            RemoveEffects(db, doc, touched);
            doc.Status = DocumentStatus.Voided;
            doc.UpdatedAt = DateTime.UtcNow;
        }

        Guid? first = null;
        foreach (var cell in request.Cells)
        {
            var id = WriteConsumption(db, cell, touched);
            first ??= id;
        }

        EnsureNonNegative(db, touched);
        var message = request.VoidIds.Count == 0
            ? "Đã lưu bảng xuất tiêu thụ."
            : "Đã lưu bảng xuất tiêu thụ và hoàn tồn các ô đã xóa.";
        return FuelResult.Success(first ?? request.VoidIds[0], message);
    });

    public FuelResult SaveAuxiliary(AuxiliaryRequest request) => InTx(db =>
    {
        var warehouse = RequireWarehouse(db, request.WarehouseId);
        if (warehouse.Type != WarehouseType.Auxiliary)
            return FuelResult.Fail("Chỉ tiêu thụ trên kho phụ.");
        var lot = RequireLot(db, request.LotId);
        var actual = QuantityMath.Whole(request.ActualQuantity);
        if (actual <= 0)
            throw new FuelRuleException("Thực xuất phải lớn hơn 0.");
        var vcf = QuantityMath.RoundVcf(request.Vcf);
        var display = QuantityMath.ExportDisplayQuantity(actual, vcf);
        var touched = new HashSet<(Guid, Guid)>();
        var doc = Begin(db, request.DocumentId, DocumentKind.Auxiliary, touched);
        WriteLotHeader(doc, request.DocumentDate, lot, vcf, warehouse.Id, Or(request.WarehouseName, warehouse.Name), Or(request.WarehouseTypeName, Labels.Warehouse(warehouse.Type)));
        doc.UnitPrice = lot.UnitPrice;
        doc.LotTypeId = lot.LotTypeId;
        doc.LotTypeCode = ResolveLotType(db, lot.LotTypeId).Code;
        doc.InputQuantity = display;
        doc.ActualQuantity = actual;
        var auxLine = NewLine(doc, 1, lot, warehouse.Id, display, actual, 0);
        auxLine.LotTypeId = lot.LotTypeId;
        auxLine.LotTypeCode = doc.LotTypeCode;
        AttachLine(db, doc, auxLine);
        ApplyFields(db, doc, DocumentFamily.Export, request.Fields, request.AddToSampleSetId);
        AddEffects(db, doc, touched);
        EnsureNonNegative(db, touched);
        return FuelResult.Success(doc.Id, "Đã lưu tiêu thụ kho phụ.");
    });

    public FuelResult SaveAuxiliarySheet(AuxiliarySheetRequest request) => InTx(db =>
    {
        if (request.Cells.Count == 0 && request.VoidIds.Count == 0)
            return FuelResult.Fail("Chưa có lượng tiêu thụ để lưu.");
        var touched = new HashSet<(Guid, Guid)>();
        foreach (var id in request.VoidIds.Distinct())
        {
            var existing = db.Documents.FirstOrDefault(x => x.Id == id)
                ?? throw new FuelRuleException("Không tìm thấy chứng từ.");
            if (existing.Kind != DocumentKind.Auxiliary)
                return FuelResult.Fail("Bảng tiêu thụ kho phụ chỉ xóa được phiếu tiêu thụ kho phụ.");
            if (existing.Status != DocumentStatus.Active)
                continue;
            RemoveEffects(db, existing, touched);
            existing.Status = DocumentStatus.Voided;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        Guid? first = null;
        foreach (var cell in request.Cells)
        {
            var id = WriteAuxiliaryCell(db, cell, touched);
            first ??= id;
        }

        EnsureNonNegative(db, touched);
        var message = request.VoidIds.Count == 0
            ? "Đã lưu bảng tiêu thụ kho phụ."
            : "Đã lưu bảng tiêu thụ kho phụ và hoàn tồn các ô đã xóa.";
        return FuelResult.Success(first ?? request.VoidIds[0], message);
    });

    public FuelResult Void(Guid documentId) => InTx(db =>
    {
        var doc = db.Documents.FirstOrDefault(x => x.Id == documentId)
            ?? throw new FuelRuleException("Không tìm thấy chứng từ.");
        if (doc.Status != DocumentStatus.Active)
            return FuelResult.Fail("Chứng từ đã hủy.");
        var touched = new HashSet<(Guid, Guid)>();
        RemoveEffects(db, doc, touched);
        EnsureNonNegative(db, touched);
        doc.Status = DocumentStatus.Voided;
        doc.UpdatedAt = DateTime.UtcNow;
        return FuelResult.Success(doc.Id, "Đã hủy chứng từ và hoàn tồn.");
    });

    public FuelResult DeleteSlip(Guid documentId) => InTx(db =>
    {
        var doc = db.Documents.Include(x => x.Lines).Include(x => x.Fields).FirstOrDefault(x => x.Id == documentId)
            ?? throw new FuelRuleException("Không tìm thấy chứng từ.");
        if (doc.Kind is not (DocumentKind.Import or DocumentKind.Issue))
            return FuelResult.Fail("Chỉ xóa được phiếu nhập hoặc phiếu xuất.");
        if (doc.Status == DocumentStatus.Active)
        {
            var touched = new HashSet<(Guid, Guid)>();
            RemoveEffects(db, doc, touched);
            EnsureNonNegative(db, touched);
        }
        else
        {
            var leftover = db.StockMovements.Where(x => x.DocumentId == doc.Id).ToList();
            if (leftover.Count > 0)
                db.StockMovements.RemoveRange(leftover);
        }

        db.Documents.Remove(doc);
        return FuelResult.Success(documentId, "Đã xóa phiếu và hoàn tồn.");
    });

    public RebuildResult Rebuild()
    {
        using var db = _factory();
        using var tx = db.Database.BeginTransaction();
        var docs = db.Documents.Include(x => x.Lines)
            .Where(x => x.Status == DocumentStatus.Active)
            .OrderBy(x => x.Sequence)
            .ToList();
        db.StockMovements.RemoveRange(db.StockMovements.ToList());
        foreach (var balance in db.StockBalances.ToList())
            balance.Quantity = 0;
        var touched = new HashSet<(Guid, Guid)>();
        foreach (var doc in docs)
            AddEffects(db, doc, touched);
        db.SaveChanges();
        tx.Commit();
        var warnings = db.StockBalances.AsEnumerable()
            .Where(x => x.Quantity < 0)
            .Select(x => $"Tồn âm sau đối soát tại kho {x.WarehouseId}, lô {x.LotId}: {Whole(x.Quantity)}.")
            .ToList();
        return new RebuildResult(true, "Đã đối soát tồn từ chứng từ còn hiệu lực.", docs.Count, warnings);
    }

    public DocumentDetail? GetDocument(Guid id)
    {
        using var db = _factory();
        var doc = db.Documents.AsNoTracking().Include(x => x.Lines).Include(x => x.Fields).FirstOrDefault(x => x.Id == id);
        return doc is null ? null : MapDetail(doc);
    }

    public IReadOnlyList<DocumentRow> ListDocuments(DocumentKind? kind)
    {
        using var db = _factory();
        var query = db.Documents.AsNoTracking().AsQueryable();
        if (kind is DocumentKind value)
            query = query.Where(x => x.Kind == value);
        return ProjectRows(query.OrderByDescending(x => x.Sequence));
    }

    /// <summary>Có phiếu nhập hoặc phiếu xuất (Issue) đang hiệu lực — dùng cảnh báo khi sửa tồn đầu kỳ.</summary>
    public bool HasActiveImportOrIssue()
    {
        using var db = _factory();
        return db.Documents.AsNoTracking().Any(x =>
            x.Status == DocumentStatus.Active
            && (x.Kind == DocumentKind.Import || x.Kind == DocumentKind.Issue));
    }

    public IReadOnlyList<DocumentRow> ListDesk(bool export, int? year, int quarter)
    {
        using var db = _factory();
        var query = db.Documents.AsNoTracking().AsQueryable();
        query = export
            ? query.Where(x => x.Kind == DocumentKind.Issue || x.Kind == DocumentKind.Transfer || x.Kind == DocumentKind.LotConvert || (x.Kind == DocumentKind.Consumption && x.Distance != null))
            : query.Where(x => x.Kind == DocumentKind.Import);
        if (year is int value)
        {
            var start = quarter is >= 1 and <= 4
                ? new DateTime(value, (quarter - 1) * 3 + 1, 1)
                : new DateTime(value, 1, 1);
            var end = quarter is >= 1 and <= 4 ? start.AddMonths(3) : start.AddYears(1);
            query = query.Where(x => x.DocumentDate >= start && x.DocumentDate < end);
        }
        else if (quarter is >= 1 and <= 4)
        {
            var startMonth = (quarter - 1) * 3 + 1;
            query = query.Where(x => x.DocumentDate.Month >= startMonth && x.DocumentDate.Month <= startMonth + 2);
        }

        return ProjectRows(query.OrderByDescending(x => x.DocumentDate).ThenByDescending(x => x.Sequence));
    }

    /// <summary>
    /// Kho đối tượng có hoạt động sổ trong quý (phiếu ĐC đến hoặc dòng sổ đã lưu) — 1–2 truy vấn, không dựng cả sổ.
    /// </summary>
    public HashSet<Guid> ListLocationsWithQuarterBookActivity(DateTime quarterDate)
    {
        using var db = _factory();
        var date = quarterDate == default ? DateTime.Today : quarterDate.Date;
        var start = new DateTime(date.Year, ((date.Month - 1) / 3) * 3 + 1, 1);
        var end = start.AddMonths(3);
        var year = start.Year;
        var quarter = (start.Month - 1) / 3 + 1;

        var fromTransfers = db.Documents.AsNoTracking()
            .Where(x => x.Status == DocumentStatus.Active
                && x.Kind == DocumentKind.Transfer
                && x.DestinationWarehouseId != null
                && x.DocumentDate >= start
                && x.DocumentDate < end)
            .Select(x => x.DestinationWarehouseId!.Value)
            .Distinct()
            .ToList();

        var fromConsumerBooks = db.ConsumerQuarterBooks.AsNoTracking()
            .Where(x => x.Year == year && x.Quarter == quarter && x.Lines.Any())
            .Select(x => x.ConsumerId)
            .ToList();

        var fromShipBooks = db.ShipQuarterBooks.AsNoTracking()
            .Where(x => x.Year == year && x.Quarter == quarter && x.Lines.Any())
            .Select(x => x.ConsumerId)
            .ToList();

        var set = new HashSet<Guid>(fromTransfers.Count + fromConsumerBooks.Count + fromShipBooks.Count);
        foreach (var id in fromTransfers)
            set.Add(id);
        foreach (var id in fromConsumerBooks)
            set.Add(id);
        foreach (var id in fromShipBooks)
            set.Add(id);
        return set;
    }

    public IReadOnlyList<SheetHeader> ListSheetHeaders(params DocumentKind[] kinds)
    {
        using var db = _factory();
        var query = db.Documents.AsNoTracking().Where(x => x.Status == DocumentStatus.Active);
        if (kinds.Length > 0)
            query = query.Where(x => kinds.Contains(x.Kind));
        return ProjectSheetHeaders(query);
    }

    /// <summary>Headers theo khoảng ngày (và tùy chọn chỉ tiêu thụ quý: Distance == null).</summary>
    public IReadOnlyList<SheetHeader> ListSheetHeadersInRange(
        DocumentKind kind,
        DateTime fromInclusive,
        DateTime toInclusive,
        bool nullDistanceOnly = false)
    {
        using var db = _factory();
        var from = fromInclusive.Date;
        var to = toInclusive.Date;
        var query = db.Documents.AsNoTracking()
            .Where(x => x.Status == DocumentStatus.Active
                && x.Kind == kind
                && x.DocumentDate >= from
                && x.DocumentDate <= to);
        if (nullDistanceOnly)
            query = query.Where(x => x.Distance == null);
        return ProjectSheetHeaders(query);
    }

    private static IReadOnlyList<SheetHeader> ProjectSheetHeaders(IQueryable<FuelDocument> query) =>
        query.OrderByDescending(x => x.Sequence).Select(doc => new SheetHeader(
            doc.Id, doc.Kind, doc.Status, doc.DocumentDate, doc.ItemId, doc.ItemName, doc.GroupName, doc.UnitName,
            doc.QualityInfo, doc.Temperature, doc.MeasurementNote, doc.Vcf, doc.WarehouseId, doc.UnitPrice,
            doc.LotTypeId, doc.LotTypeCode ?? "",
            doc.ActualQuantity, doc.Distance, doc.ConsumerId, doc.Norm, doc.OperatingQuantity)).ToList();

    /// <summary>
    /// Tổng thực xuất các phiếu điều chuyển đến kho đích trong quý (nhóm theo lô).
    /// </summary>
    public IReadOnlyList<InboundTransferSum> SumInboundTransfers(DateTime quarterDate, Guid? destinationWarehouseId = null)
    {
        using var db = _factory();
        var date = quarterDate == default ? DateTime.Today : quarterDate.Date;
        var start = new DateTime(date.Year, ((date.Month - 1) / 3) * 3 + 1, 1);
        var end = start.AddMonths(3);
        var query =
            from doc in db.Documents.AsNoTracking()
            where doc.Status == DocumentStatus.Active
                && doc.Kind == DocumentKind.Transfer
                && doc.DestinationWarehouseId != null
                && doc.DocumentDate >= start
                && doc.DocumentDate < end
            join line in db.DocumentLines.AsNoTracking() on doc.Id equals line.DocumentId
            select new
            {
                DestinationId = doc.DestinationWarehouseId!.Value,
                line.ItemName,
                line.UnitPrice,
                LotTypeId = line.DestinationLotTypeId ?? line.LotTypeId ?? SeedIds.LotTypeTx,
                LotTypeCode = line.DestinationLotTypeCode.Length > 0
                    ? line.DestinationLotTypeCode
                    : (line.LotTypeCode.Length > 0 ? line.LotTypeCode : "TX"),
                line.ActualQuantity
            };
        if (destinationWarehouseId is Guid destinationId)
            query = query.Where(x => x.DestinationId == destinationId);

        return query.AsEnumerable()
            .GroupBy(x => (x.DestinationId, Key: QuantityMath.LotKey(x.ItemName), x.UnitPrice, x.LotTypeId, x.LotTypeCode))
            .Select(g => new InboundTransferSum(
                g.Key.DestinationId,
                g.Key.Key,
                g.Key.UnitPrice,
                g.Key.LotTypeId,
                g.Key.LotTypeCode,
                QuantityMath.Whole(g.Sum(x => x.ActualQuantity))))
            .OrderBy(x => x.DestinationWarehouseId)
            .ThenBy(x => x.ItemNameKey)
            .ThenBy(x => x.UnitPrice)
            .ThenBy(x => x.LotTypeCode)
            .ToList();
    }

    /// <summary>Danh sách phiếu điều chuyển đến một kho đích trong quý (từng dòng hàng).</summary>
    public IReadOnlyList<InboundTransferSlipRow> ListInboundTransfers(DateTime quarterDate, Guid destinationWarehouseId)
    {
        using var db = _factory();
        var date = quarterDate == default ? DateTime.Today : quarterDate.Date;
        var start = new DateTime(date.Year, ((date.Month - 1) / 3) * 3 + 1, 1);
        var end = start.AddMonths(3);
        var rows = (
            from doc in db.Documents.AsNoTracking()
            where doc.Status == DocumentStatus.Active
                && doc.Kind == DocumentKind.Transfer
                && doc.DestinationWarehouseId == destinationWarehouseId
                && doc.DocumentDate >= start
                && doc.DocumentDate < end
            join line in db.DocumentLines.AsNoTracking() on doc.Id equals line.DocumentId
            orderby doc.DocumentDate, doc.Sequence, line.LineNo
            select new { doc, line }
        ).AsEnumerable()
            .Select(x => new InboundTransferSlipRow(
                x.doc.Id,
                x.doc.DocumentDate,
                x.doc.FormNumber,
                string.IsNullOrWhiteSpace(x.doc.FormNumber) ? x.doc.Number : x.doc.FormNumber,
                x.line.ItemName,
                x.line.UnitPrice,
                x.line.DestinationLotTypeId ?? x.line.LotTypeId ?? SeedIds.LotTypeTx,
                x.line.DestinationLotTypeCode.Length > 0
                    ? x.line.DestinationLotTypeCode
                    : (x.line.LotTypeCode.Length > 0 ? x.line.LotTypeCode : "TX"),
                QuantityMath.Whole(x.line.ActualQuantity)))
            .ToList();
        return rows;
    }

    /// <summary>
    /// Sổ tiêu thụ máy/xe: mang sang + mỗi phiếu ĐC đến kho đích trong quý.
    /// ĐC bao nhiêu thì tiêu thụ bấy nhiêu (Nhập = Xuất); nhiên liệu = Xăng/Dầu, dầu mỡ = nhóm còn lại.
    /// </summary>
    public ConsumerTransferBook BuildConsumerTransferBook(DateTime quarterDate, Guid destinationWarehouseId)
    {
        using var db = _factory();
        var date = quarterDate == default ? DateTime.Today : quarterDate.Date;
        var start = new DateTime(date.Year, ((date.Month - 1) / 3) * 3 + 1, 1);
        var end = start.AddMonths(3);
        var quarterEnd = end.AddDays(-1);

        var warehouse = db.Warehouses.AsNoTracking().FirstOrDefault(x => x.Id == destinationWarehouseId);
        var consumer = db.Consumers.AsNoTracking().FirstOrDefault(x => x.Id == destinationWarehouseId);

        var consumerName = consumer?.Name ?? warehouse?.Name ?? "";
        var fuelUsed = "";
        if (consumer is not null)
        {
            var groupName = consumer.DefaultGroupId is Guid gid
                ? db.ItemGroups.AsNoTracking().FirstOrDefault(x => x.Id == gid)?.Name?.Trim() ?? ""
                : "";
            var itemName = consumer.DefaultItemId is Guid iid
                ? db.FuelItems.AsNoTracking().FirstOrDefault(x => x.Id == iid)?.Name?.Trim() ?? ""
                : "";
            fuelUsed = groupName.Length > 0 && itemName.Length > 0
                ? $"{groupName} — {itemName}"
                : itemName.Length > 0 ? itemName : groupName;
        }

        var items = db.FuelItems.AsNoTracking().Include(x => x.Group).ToDictionary(x => x.Id);
        var lots = db.Lots.AsNoTracking().ToDictionary(x => x.Id);

        // Chỉ dòng chạm kho đích (WarehouseId / DestinationWarehouseId) — signed qty lọc tiếp theo Kind.
        var flat = (
            from doc in db.Documents.AsNoTracking()
            where doc.Status == DocumentStatus.Active && doc.DocumentDate < end
            join line in db.DocumentLines.AsNoTracking() on doc.Id equals line.DocumentId
            where line.WarehouseId == destinationWarehouseId
                || doc.DestinationWarehouseId == destinationWarehouseId
            select new
            {
                doc.Id,
                doc.Kind,
                doc.DocumentDate,
                doc.Sequence,
                doc.Number,
                doc.FormNumber,
                doc.Nature,
                doc.Mission,
                doc.MissionTaskId,
                doc.Note,
                doc.Kilometers,
                doc.Distance,
                doc.Norm,
                doc.VehiclePlate,
                doc.WarehouseName,
                doc.DestinationWarehouseId,
                doc.DestinationWarehouseName,
                doc.OriginPlace,
                doc.DestinationPlace,
                line.LotId,
                line.ItemId,
                line.ItemName,
                line.WarehouseId,
                line.ActualQuantity,
                line.LotTypeId,
                line.LotTypeCode,
                line.DestinationLotTypeId,
                line.DestinationLotTypeCode
            }).ToList();

        decimal fuelOpening = 0;
        decimal oilOpening = 0;
        var fuelOpenLots = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var oilOpenLots = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in flat)
        {
            var signed = SignedQtyForWarehouse(row.Kind, row.WarehouseId, row.DestinationWarehouseId, destinationWarehouseId, row.ActualQuantity);
            if (signed == 0)
                continue;
            var isOpeningDoc = row.Kind == DocumentKind.Opening
                ? row.DocumentDate.Date <= quarterEnd
                : row.DocumentDate.Date < start;
            if (!isOpeningDoc)
                continue;
            var groupName = ResolveGroupName(row.ItemId, row.LotId, row.ItemName, items, lots);
            var typeCode = LotTypeCodeForWarehouseEffect(
                row.Kind, row.WarehouseId, row.DestinationWarehouseId, destinationWarehouseId,
                row.LotTypeCode, row.DestinationLotTypeCode);
            if (IsFuelGroup(groupName))
            {
                fuelOpening = QuantityMath.Whole(fuelOpening + signed);
                LotBalanceTips.Add(fuelOpenLots, typeCode, signed);
            }
            else if (IsGreaseGroupName(groupName))
            {
                oilOpening = QuantityMath.Whole(oilOpening + signed);
                LotBalanceTips.Add(oilOpenLots, typeCode, signed);
            }
        }

        var transfers = flat
            .Where(x => x.Kind == DocumentKind.Transfer
                && x.DestinationWarehouseId == destinationWarehouseId
                && x.DocumentDate.Date >= start
                && x.DocumentDate.Date < end)
            .GroupBy(x => x.Id)
            .OrderBy(g => g.First().DocumentDate)
            .ThenBy(g => g.First().Sequence)
            .ToList();

        var plate = transfers
            .Select(g => g.First().VehiclePlate?.Trim() ?? "")
            .FirstOrDefault(x => x.Length > 0) ?? "";
        if (plate.Length == 0)
        {
            plate = consumer?.Type == ConsumerType.Vehicle
                ? consumer.Name
                : consumer?.Code ?? warehouse?.Code ?? "";
        }

        var rows = new List<ConsumerTransferBookRow>
        {
            new(
                IsOpening: true,
                DocumentId: null,
                DocumentNumber: "",
                DocumentDate: null,
                Description: "Quý trước mang sang",
                Origin: "",
                Destination: "",
                Kilometers: null,
                MachineHours: null,
                NormQuantity: null,
                ActualQuantity: null,
                OverQuantity: null,
                UnderQuantity: null,
                FuelIn: null,
                FuelOut: null,
                FuelBalance: fuelOpening == 0 ? null : fuelOpening,
                OilIn: null,
                OilOut: null,
                OilBalance: oilOpening == 0 ? null : oilOpening,
                FuelBalanceLotTip: LotBalanceTips.Format(fuelOpenLots),
                OilBalanceLotTip: LotBalanceTips.Format(oilOpenLots))
        };

        decimal fuelBalance = fuelOpening;
        decimal oilBalance = oilOpening;
        decimal fuelTransferTotal = 0;
        decimal oilTransferTotal = 0;

        foreach (var group in transfers)
        {
            var head = group.First();
            decimal? kilometers = head.Distance;
            if (kilometers is null && decimal.TryParse(head.Kilometers?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var kmParsed))
                kilometers = QuantityMath.RoundQty(kmParsed);

            var description = FirstNonEmpty(head.Mission, head.Nature, head.Note);
            var number = string.IsNullOrWhiteSpace(head.FormNumber) ? head.Number : head.FormNumber;
            var origin = head.OriginPlace?.Trim() ?? "";
            var destination = head.DestinationPlace?.Trim() ?? "";

            // Mỗi dòng sổ = một loại lô (không gộp TX,SSCĐ trên cùng dòng).
            var byLotType = group
                .Select(line =>
                {
                    var typeId = line.DestinationLotTypeId ?? line.LotTypeId ?? SeedIds.LotTypeTx;
                    var typeCode = !string.IsNullOrWhiteSpace(line.DestinationLotTypeCode)
                        ? line.DestinationLotTypeCode.Trim()
                        : (!string.IsNullOrWhiteSpace(line.LotTypeCode) ? line.LotTypeCode.Trim() : "TX");
                    return (line, typeId, typeCode);
                })
                .GroupBy(x => x.typeId)
                .OrderBy(g =>
                {
                    var code = (g.Select(x => x.typeCode).FirstOrDefault(c => c.Length > 0) ?? "TX").Trim().ToUpperInvariant();
                    return code switch { "TX" => 0, "SSCĐ" or "SSCD" => 1, "IUU" => 2, _ => 50 };
                })
                .ThenBy(g => g.Select(x => x.typeCode).FirstOrDefault() ?? "TX", StringComparer.CurrentCultureIgnoreCase);

            foreach (var typeGroup in byLotType)
            {
                decimal fuelQty = 0;
                decimal oilQty = 0;
                var typeCode = "TX";
                foreach (var (line, _, code) in typeGroup)
                {
                    if (typeCode == "TX" && code.Length > 0)
                        typeCode = code;
                    var qty = QuantityMath.Whole(line.ActualQuantity);
                    if (qty <= 0)
                        continue;
                    var groupName = ResolveGroupName(line.ItemId, line.LotId, line.ItemName, items, lots);
                    if (IsFuelGroup(groupName))
                        fuelQty = QuantityMath.Whole(fuelQty + qty);
                    else if (IsGreaseGroupName(groupName))
                        oilQty = QuantityMath.Whole(oilQty + qty);
                }

                if (fuelQty <= 0 && oilQty <= 0)
                    continue;

                fuelTransferTotal = QuantityMath.Whole(fuelTransferTotal + fuelQty);
                oilTransferTotal = QuantityMath.Whole(oilTransferTotal + oilQty);
                fuelBalance = QuantityMath.Whole(fuelBalance);
                oilBalance = QuantityMath.Whole(oilBalance);

                decimal? actualQty = fuelQty > 0 ? fuelQty : null;
                decimal? normQty = actualQty;
                var lotTypeId = typeGroup.Key;

                rows.Add(new ConsumerTransferBookRow(
                    IsOpening: false,
                    DocumentId: head.Id,
                    DocumentNumber: number ?? "",
                    DocumentDate: head.DocumentDate.Date,
                    Description: description,
                    Origin: origin,
                    Destination: destination,
                    Kilometers: kilometers,
                    MachineHours: null,
                    NormQuantity: normQty,
                    ActualQuantity: actualQty,
                    OverQuantity: null,
                    UnderQuantity: null,
                    FuelIn: fuelQty > 0 ? fuelQty : null,
                    FuelOut: fuelQty > 0 ? fuelQty : null,
                    FuelBalance: fuelBalance == 0 && fuelQty == 0 ? null : fuelBalance,
                    OilIn: oilQty > 0 ? oilQty : null,
                    OilOut: oilQty > 0 ? oilQty : null,
                    OilBalance: oilBalance == 0 && oilQty == 0 ? null : oilBalance,
                    LotTypeId: lotTypeId,
                    LotTypeCode: typeCode,
                    MissionTaskId: head.MissionTaskId));
            }
        }

        // Opening with zero balances still shown; blank both balance cells when truly zero.
        if (fuelOpening == 0 && oilOpening == 0)
        {
            rows[0] = rows[0] with { FuelBalance = null, OilBalance = null };
        }

        return new ConsumerTransferBook(
            ConsumerName: consumerName,
            PlateNumber: plate,
            FuelUsed: fuelUsed,
            FormId: "Số 3-04.1/XD-14",
            UnitNote: "Đơn vị tính: Nhiên liệu: Lít 15° C; Dầu mỡ: Kg.",
            FuelTransferTotal: fuelTransferTotal,
            OilTransferTotal: oilTransferTotal,
            Rows: rows);
    }

    /// <summary>Sổ máy/xe: dòng ĐC auto + override đã lưu (nếu có).</summary>
    public ConsumerTransferBook GetConsumerQuarterBook(DateTime quarterDate, Guid destinationWarehouseId)
    {
        var book = BuildConsumerTransferBook(quarterDate, destinationWarehouseId);
        using var db = _factory();
        var date = quarterDate == default ? DateTime.Today : quarterDate.Date;
        var year = date.Year;
        var quarter = (date.Month - 1) / 3 + 1;
        var saved = db.ConsumerQuarterBooks.AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefault(x => x.ConsumerId == destinationWarehouseId && x.Year == year && x.Quarter == quarter);
        if (saved is null || saved.Lines.Count == 0)
            return book;

        var byTransferType = saved.Lines
            .Where(x => x.TransferDocumentId is Guid)
            .GroupBy(x => (
                DocId: x.TransferDocumentId!.Value,
                TypeId: x.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : x.LotTypeId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.SortOrder).First());
        var merged = book.Rows.Select(row =>
        {
            if (row.IsOpening || row.DocumentId is not Guid docId)
                return row;
            var typeId = row.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : row.LotTypeId;
            if (!byTransferType.TryGetValue((docId, typeId), out var line))
                return row;
            var defaultFuelOut = row.FuelOut ?? 0;
            var defaultOilOut = row.OilOut ?? 0;
            // Xuất đã lưu khác mặc định ĐC (kể cả khi cờ Manual cũ bị sót) → giữ số đã lưu.
            var manualFuel = line.ManualFuelOut
                || QuantityMath.Whole(line.FuelOut) != QuantityMath.Whole(defaultFuelOut);
            var manualOil = line.ManualOilOut
                || QuantityMath.Whole(line.OilOut) != QuantityMath.Whole(defaultOilOut);
            var fuelOut = manualFuel ? line.FuelOut : defaultFuelOut;
            var oilOut = manualOil ? line.OilOut : defaultOilOut;
            var actual = line.ActualQuantity ?? (fuelOut > 0 ? fuelOut : row.ActualQuantity);
            var norm = line.NormQuantity ?? row.NormQuantity;
            decimal? over = null;
            decimal? under = null;
            if (norm is decimal n && actual is decimal a)
            {
                var diff = QuantityMath.Whole(a - n);
                if (diff > 0) over = diff;
                else if (diff < 0) under = QuantityMath.Whole(-diff);
            }

            return row with
            {
                LineId = line.Id,
                Kilometers = line.Kilometers ?? row.Kilometers,
                MachineHours = line.MachineHours ?? row.MachineHours,
                NormQuantity = norm,
                ActualQuantity = actual,
                OverQuantity = over,
                UnderQuantity = under,
                FuelOut = fuelOut > 0 ? fuelOut : null,
                OilOut = oilOut > 0 ? oilOut : null,
                ManualFuelOut = manualFuel,
                ManualOilOut = manualOil,
                Description = string.IsNullOrWhiteSpace(line.Description) ? row.Description : line.Description,
                LotTypeId = line.LotTypeId == Guid.Empty ? row.LotTypeId : line.LotTypeId,
                LotTypeCode = string.IsNullOrWhiteSpace(line.LotTypeCode) ? row.LotTypeCode : line.LotTypeCode,
                MissionTaskId = line.MissionTaskId ?? row.MissionTaskId
            };
        }).ToList();

        // Dòng tiêu thụ thêm tay (không gắn phiếu ĐC) — Nhập trống, chỉ Xuất.
        foreach (var line in saved.Lines
                     .Where(x => x.TransferDocumentId is null)
                     .OrderBy(x => x.DocumentDate ?? DateTime.MaxValue)
                     .ThenBy(x => x.SortOrder))
        {
            var fuelOut = line.FuelOut;
            var oilOut = line.OilOut;
            var actual = line.ActualQuantity ?? (fuelOut > 0 ? fuelOut : null);
            var norm = line.NormQuantity;
            decimal? over = null;
            decimal? under = null;
            if (norm is decimal n && actual is decimal a)
            {
                var diff = QuantityMath.Whole(a - n);
                if (diff > 0) over = diff;
                else if (diff < 0) under = QuantityMath.Whole(-diff);
            }

            merged.Add(new ConsumerTransferBookRow(
                IsOpening: false,
                DocumentId: null,
                DocumentNumber: line.DocumentNumber,
                DocumentDate: line.DocumentDate,
                Description: line.Description,
                Origin: "",
                Destination: "",
                Kilometers: line.Kilometers,
                MachineHours: line.MachineHours,
                NormQuantity: norm,
                ActualQuantity: actual,
                OverQuantity: over,
                UnderQuantity: under,
                FuelIn: null,
                FuelOut: fuelOut > 0 ? fuelOut : null,
                FuelBalance: null,
                OilIn: null,
                OilOut: oilOut > 0 ? oilOut : null,
                OilBalance: null,
                ManualFuelOut: line.ManualFuelOut || fuelOut > 0,
                ManualOilOut: line.ManualOilOut || oilOut > 0,
                LineId: line.Id,
                LotTypeId: line.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : line.LotTypeId,
                LotTypeCode: string.IsNullOrWhiteSpace(line.LotTypeCode) ? "TX" : line.LotTypeCode,
                IsManualRow: true,
                MissionTaskId: line.MissionTaskId));
        }

        if (merged.Count > 1)
        {
            var opening = merged[0];
            // Cùng ngày: phiếu nhập (ĐC) trước, rồi tiêu thụ tay.
            var rest = merged.Skip(1)
                .OrderBy(x => x.DocumentDate?.Date ?? DateTime.MaxValue)
                .ThenBy(x => x.IsManualRow ? 1 : 0)
                .ThenBy(x => x.DocumentNumber, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            merged = [opening, .. rest];
        }

        // Recalc balances after override outs + dòng tay (+ tip tồn theo loại lô).
        decimal fuelBal = merged[0].FuelBalance ?? 0;
        decimal oilBal = merged[0].OilBalance ?? 0;
        var fuelLots = LotBalanceTips.Parse(merged[0].FuelBalanceLotTip);
        var oilLots = LotBalanceTips.Parse(merged[0].OilBalanceLotTip);
        if (fuelLots.Count == 0 && fuelBal != 0)
            LotBalanceTips.Add(fuelLots, "TX", fuelBal);
        if (oilLots.Count == 0 && oilBal != 0)
            LotBalanceTips.Add(oilLots, "TX", oilBal);
        merged[0] = merged[0] with
        {
            FuelBalanceLotTip = LotBalanceTips.Format(fuelLots),
            OilBalanceLotTip = LotBalanceTips.Format(oilLots)
        };
        for (var i = 1; i < merged.Count; i++)
        {
            var row = merged[i];
            var fuelIn = row.FuelIn ?? 0;
            var oilIn = row.OilIn ?? 0;
            var fuelOut = row.FuelOut ?? 0;
            var oilOut = row.OilOut ?? 0;
            fuelBal = QuantityMath.Whole(fuelBal + fuelIn - fuelOut);
            oilBal = QuantityMath.Whole(oilBal + oilIn - oilOut);
            LotBalanceTips.Add(fuelLots, row.LotTypeCode, fuelIn - fuelOut);
            LotBalanceTips.Add(oilLots, row.LotTypeCode, oilIn - oilOut);
            merged[i] = row with
            {
                FuelBalance = fuelBal == 0 && fuelIn == 0 && fuelOut == 0 ? null : fuelBal,
                OilBalance = oilBal == 0 && oilIn == 0 && oilOut == 0 ? null : oilBal,
                FuelBalanceLotTip = LotBalanceTips.Format(fuelLots),
                OilBalanceLotTip = LotBalanceTips.Format(oilLots)
            };
        }

        return book with { Rows = merged };
    }

    public FuelResult SaveConsumerQuarterBook(ConsumerQuarterBookSaveRequest request) => InTx(db =>
    {
        var consumer = db.Consumers.FirstOrDefault(x => x.Id == request.ConsumerId)
            ?? throw new FuelRuleException("Không tìm thấy máy/phương tiện.");
        if (consumer.Type is not (ConsumerType.Machine or ConsumerType.Vehicle))
            return FuelResult.Fail("Chỉ lưu sổ tiêu thụ cho máy hoặc phương tiện.");

        var date = request.QuarterDate == default ? DateTime.Today : request.QuarterDate.Date;
        var year = date.Year;
        var quarter = (date.Month - 1) / 3 + 1;
        var start = new DateTime(year, (quarter - 1) * 3 + 1, 1);
        var end = start.AddMonths(3);

        var book = db.ConsumerQuarterBooks
            .Include(x => x.Lines)
            .FirstOrDefault(x => x.ConsumerId == consumer.Id && x.Year == year && x.Quarter == quarter);
        if (book is null)
        {
            book = new ConsumerQuarterBook
            {
                Id = Guid.NewGuid(),
                ConsumerId = consumer.Id,
                Year = year,
                Quarter = quarter
            };
            db.ConsumerQuarterBooks.Add(book);
        }

        book.UpdatedAt = DateTime.UtcNow;
        db.ConsumerQuarterBookLines.RemoveRange(book.Lines);
        book.Lines.Clear();

        var order = 1;

        // Mặc định Xuất ĐC (= Nhập) để nhận diện dòng đã sửa dầu mỡ/NL dù cờ Manual bị sót.
        var itemsById = db.FuelItems.AsNoTracking().Include(i => i.Group).ToDictionary(i => i.Id);
        var lotsById = db.Lots.AsNoTracking().ToDictionary(l => l.Id);
        var defaultOutByTransfer = (
            from doc in db.Documents.AsNoTracking()
            where doc.Status == DocumentStatus.Active
                && doc.Kind == DocumentKind.Transfer
                && doc.DestinationWarehouseId == consumer.Id
                && doc.DocumentDate >= start
                && doc.DocumentDate < end
            join line in db.DocumentLines.AsNoTracking() on doc.Id equals line.DocumentId
            select new
            {
                doc.Id,
                line.ItemId,
                line.LotId,
                line.ItemName,
                line.ActualQuantity,
                LotTypeId = line.DestinationLotTypeId ?? line.LotTypeId ?? SeedIds.LotTypeTx
            }
        ).AsEnumerable()
            .GroupBy(x => (x.Id, x.LotTypeId))
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    decimal fuel = 0, oil = 0;
                    foreach (var line in g)
                    {
                        var qty = QuantityMath.Whole(line.ActualQuantity);
                        if (qty <= 0)
                            continue;
                        var groupName = ResolveGroupName(line.ItemId, line.LotId, line.ItemName, itemsById, lotsById);
                        if (IsFuelGroup(groupName))
                            fuel = QuantityMath.Whole(fuel + qty);
                        else if (IsGreaseGroupName(groupName))
                            oil = QuantityMath.Whole(oil + qty);
                    }

                    return (Fuel: fuel, Oil: oil);
                });

        foreach (var edit in request.Lines)
        {
            var fuelOut = QuantityMath.Whole(edit.FuelOut);
            var oilOut = QuantityMath.Whole(edit.OilOut);
            var manualFuel = edit.ManualFuelOut || edit.TransferDocumentId is null;
            var manualOil = edit.ManualOilOut || edit.TransferDocumentId is null;
            var editTypeId = edit.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : edit.LotTypeId;
            if (edit.TransferDocumentId is Guid tid && defaultOutByTransfer.TryGetValue((tid, editTypeId), out var def))
            {
                if (fuelOut != def.Fuel)
                    manualFuel = true;
                if (oilOut != def.Oil)
                    manualOil = true;
            }

            var line = new ConsumerQuarterBookLine
            {
                Id = edit.LineId is Guid id && id != Guid.Empty ? id : Guid.NewGuid(),
                BookId = book.Id,
                SortOrder = order++,
                TransferDocumentId = edit.TransferDocumentId,
                DocumentNumber = edit.DocumentNumber?.Trim() ?? "",
                DocumentDate = edit.DocumentDate?.Date,
                Description = edit.Description?.Trim() ?? "",
                MissionTaskId = edit.MissionTaskId,
                Kilometers = edit.Kilometers is decimal km ? QuantityMath.RoundQty(km) : null,
                MachineHours = edit.MachineHours is decimal mh ? QuantityMath.RoundQty(mh) : null,
                NormQuantity = edit.NormQuantity is decimal nq ? QuantityMath.Whole(nq) : null,
                ActualQuantity = edit.ActualQuantity is decimal aq ? QuantityMath.Whole(aq) : null,
                ManualFuelOut = manualFuel,
                FuelOut = fuelOut,
                ManualOilOut = manualOil,
                OilOut = oilOut,
                LotTypeId = edit.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : edit.LotTypeId,
                LotTypeCode = string.IsNullOrWhiteSpace(edit.LotTypeCode) ? "TX" : edit.LotTypeCode.Trim()
            };
            book.Lines.Add(line);
            db.ConsumerQuarterBookLines.Add(line);

            // Đồng bộ nhiệm vụ ngược về phiếu xuất ĐC.
            if (edit.TransferDocumentId is Guid transferId && edit.MissionTaskId is Guid missionId)
            {
                var doc = db.Documents.FirstOrDefault(x => x.Id == transferId);
                if (doc is not null)
                    doc.MissionTaskId = missionId;
            }
            else if (edit.TransferDocumentId is Guid clearId && edit.MissionTaskId is null)
            {
                var doc = db.Documents.FirstOrDefault(x => x.Id == clearId);
                if (doc is not null)
                    doc.MissionTaskId = null;
            }
        }

        var touched = new HashSet<(Guid, Guid)>();
        VoidQuarterConsumption(db, consumer.Id, start, end, touched);
        // Dọn chuyển động mồ côi (phiếu tiêu thụ đã xóa nhưng còn dòng tồn) — lệch mang sang quý sau.
        PurgeOrphanConsumptionMovements(db, consumer.Id, touched);

        // Luôn trừ FIFO theo loại lô: lô có ngày phiếu nhập cũ nhất trước → mới nhất.
        var byType = request.Lines
            .GroupBy(x => x.LotTypeId == Guid.Empty ? SeedIds.LotTypeTx : x.LotTypeId)
            .Select(g => (
                TypeId: g.Key,
                Fuel: QuantityMath.Whole(g.Sum(x => QuantityMath.Whole(x.FuelOut))),
                Oil: QuantityMath.Whole(g.Sum(x => QuantityMath.Whole(x.OilOut)))))
            .Where(x => x.Fuel > 0 || x.Oil > 0)
            .ToList();
        foreach (var part in byType)
        {
            SyncMachineGroupConsumption(db, consumer, start, end, part.Fuel, grease: false, part.TypeId, touched);
            SyncMachineGroupConsumption(db, consumer, start, end, part.Oil, grease: true, part.TypeId, touched);
        }

        EnsureNonNegative(db, touched);
        return FuelResult.Success(book.Id, "Đã lưu sổ tiêu thụ và trừ tồn. Quý sau mang sang theo tồn cuối quý này.");
    });

    private static void VoidQuarterConsumption(
        AppDbContext db,
        Guid warehouseId,
        DateTime start,
        DateTime end,
        HashSet<(Guid, Guid)> touched)
    {
        var existing = db.Documents
            .Include(x => x.Lines)
            .Include(x => x.Fields)
            .Where(x => x.Status == DocumentStatus.Active
                && x.Kind == DocumentKind.Consumption
                && x.Distance == null
                && x.WarehouseId == warehouseId
                && x.DocumentDate >= start
                && x.DocumentDate < end)
            .ToList();
        foreach (var doc in existing)
        {
            RemoveEffects(db, doc, touched);
            db.DocumentFields.RemoveRange(doc.Fields);
            db.DocumentLines.RemoveRange(doc.Lines);
            db.Documents.Remove(doc);
        }
    }

    /// <summary>
    /// Xóa StockMovements còn sót khi phiếu Consumption đã mất
    /// (Begin tái dùng phiếu đang Deleted trong cùng transaction → orphan, mang sang quý sau lệch).
    /// </summary>
    private static void PurgeOrphanConsumptionMovements(
        AppDbContext db,
        Guid warehouseId,
        HashSet<(Guid, Guid)> touched)
    {
        var knownDocIds = db.Documents.AsNoTracking().Select(x => x.Id).ToHashSet();
        foreach (var entry in db.ChangeTracker.Entries<FuelDocument>())
        {
            if (entry.State == EntityState.Deleted)
                knownDocIds.Remove(entry.Entity.Id);
            else if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Unchanged)
                knownDocIds.Add(entry.Entity.Id);
        }

        var alreadyRemoved = db.ChangeTracker.Entries<StockMovement>()
            .Where(x => x.State == EntityState.Deleted)
            .Select(x => x.Entity.Id)
            .ToHashSet();

        var orphans = db.StockMovements
            .Where(x => x.WarehouseId == warehouseId && x.Reason == "Xuất tiêu thụ")
            .AsEnumerable()
            .Where(x => !alreadyRemoved.Contains(x.Id) && !knownDocIds.Contains(x.DocumentId))
            .ToList();
        foreach (var move in orphans)
        {
            Adjust(db, move.LotId, move.WarehouseId, -move.SignedQuantity, touched);
            db.StockMovements.Remove(move);
        }
    }

    private static void SyncMachineGroupConsumption(
        AppDbContext db,
        Consumer consumer,
        DateTime start,
        DateTime end,
        decimal targetOut,
        bool grease,
        Guid lotTypeId,
        HashSet<(Guid, Guid)> touched)
    {
        if (targetOut <= 0)
            return;

        var typeId = lotTypeId == Guid.Empty ? SeedIds.LotTypeTx : lotTypeId;
        var lotMeta = db.Lots.AsNoTracking().Where(x => x.LotTypeId == typeId).ToDictionary(x => x.Id);
        // Tracked balances — thấy Adjust sau Void trong cùng transaction (không AsNoTracking).
        var stocks = db.StockBalances
            .Where(b => b.WarehouseId == consumer.Id)
            .ToList()
            .Where(b => QuantityMath.RoundQty(b.Quantity) > 0 && lotMeta.ContainsKey(b.LotId))
            .Select(b =>
            {
                var lot = lotMeta[b.LotId];
                var g = lot.GroupName?.Trim() ?? "";
                if (lot.ItemId is Guid itemId)
                {
                    var item = db.FuelItems.AsNoTracking().Include(i => i.Group).FirstOrDefault(i => i.Id == itemId);
                    if (item?.Group?.Name is string gn && gn.Length > 0)
                        g = gn;
                }

                var match = grease ? IsGreaseGroupName(g) : IsFuelGroup(g);
                if (!match)
                    return null;
                var first = FirstImportKey(db, lot.Id);
                return new { bal = b, lot, FirstAt = first.At, FirstSeq = first.Seq };
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderBy(x => x.FirstAt)
            .ThenBy(x => x.FirstSeq)
            .ThenBy(x => x.lot.UnitPrice)
            .ThenBy(x => x.lot.ItemName)
            .ToList();

        var remain = targetOut;
        var quarterEnd = end.AddDays(-1);
        foreach (var row in stocks)
        {
            if (remain <= 0)
                break;
            var available = QuantityMath.RoundQty(row.bal.Quantity);
            if (available <= 0)
                continue;
            var take = available < remain ? available : remain;
            var lot = db.Lots.First(x => x.Id == row.lot.Id);
            var vcf = lot.FirstVcf <= 0 ? 1m : lot.FirstVcf;
            var wh = db.Warehouses.AsNoTracking().FirstOrDefault(x => x.Id == consumer.Id);
            WriteConsumption(db, new ConsumptionCellRequest
            {
                DocumentDate = quarterEnd,
                ConsumerId = consumer.Id,
                ConsumerType = consumer.Type,
                ConsumerName = consumer.Name,
                ConsumerCode = consumer.Code,
                WarehouseId = consumer.Id,
                WarehouseName = wh?.Name ?? consumer.Name,
                WarehouseTypeName = wh is null ? Labels.Warehouse(WarehouseType.Auxiliary) : Labels.Warehouse(wh.Type),
                ItemName = lot.ItemName,
                UnitPrice = lot.UnitPrice,
                LotTypeId = lot.LotTypeId,
                ActualQuantity = take,
                Vcf = vcf
            }, touched);
            remain = QuantityMath.RoundQty(remain - take);
        }

        if (remain > 0)
        {
            var typeCode = db.LotTypes.AsNoTracking().FirstOrDefault(x => x.Id == typeId)?.Code ?? "TX";
            throw new FuelRuleException(grease
                ? $"Không đủ tồn dầu mỡ loại {typeCode} để trừ tiêu thụ (thiếu {remain:0.####})."
                : $"Không đủ tồn nhiên liệu loại {typeCode} để trừ tiêu thụ (thiếu {remain:0.####}).");
        }
    }

    /// <summary>
    /// Ngày phiếu nhập/mở đầu gốc của lô (không dùng ngày ĐC xuống tàu/máy) — FIFO cũ → mới.
    /// </summary>
    private static (DateTime At, long Seq) FirstImportKey(AppDbContext db, Guid lotId) =>
        FirstImportKey(db, lotId, []);

    private static (DateTime At, long Seq) FirstImportKey(AppDbContext db, Guid lotId, HashSet<Guid> visited)
    {
        if (!visited.Add(lotId))
            return (DateTime.MaxValue, long.MaxValue);

        var origin = (
            from m in db.StockMovements.AsNoTracking()
            where m.LotId == lotId && m.SignedQuantity > 0
            join d in db.Documents.AsNoTracking() on m.DocumentId equals d.Id
            where d.Status == DocumentStatus.Active
                && (d.Kind == DocumentKind.Import || d.Kind == DocumentKind.Opening)
            orderby d.DocumentDate, d.Sequence
            select new { d.DocumentDate, d.Sequence }
        ).FirstOrDefault();
        if (origin is not null)
            return (origin.DocumentDate.Date, origin.Sequence);

        // Lô sinh từ ĐC đổi loại / đổi loại: truy ngày nhập của lô nguồn.
        var sourceLotIds = (
            from m in db.StockMovements.AsNoTracking()
            where m.LotId == lotId && m.SignedQuantity > 0
            join d in db.Documents.AsNoTracking() on m.DocumentId equals d.Id
            where d.Status == DocumentStatus.Active
                && (d.Kind == DocumentKind.Transfer || d.Kind == DocumentKind.LotConvert)
            join line in db.DocumentLines.AsNoTracking() on d.Id equals line.DocumentId
            where line.LotId != lotId
            select line.LotId
        ).Distinct().ToList();

        (DateTime At, long Seq)? best = null;
        foreach (var sourceLotId in sourceLotIds)
        {
            var key = FirstImportKey(db, sourceLotId, visited);
            if (best is null || key.At < best.Value.At || (key.At == best.Value.At && key.Seq < best.Value.Seq))
                best = key;
        }

        return best ?? (DateTime.MaxValue, long.MaxValue);
    }

    private static bool IsGreaseGroupName(string groupName)
    {
        var name = groupName.Trim();
        return name.Equals("Nhớt", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Mỡ", StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var trimmed = value?.Trim() ?? "";
            if (trimmed.Length > 0)
                return trimmed;
        }

        return "";
    }

    private static bool IsFuelGroup(string groupName)
    {
        var name = groupName.Trim();
        return name.Equals("Xăng", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Dầu", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveGroupName(
        Guid? itemId,
        Guid lotId,
        string? itemName,
        Dictionary<Guid, FuelItem> items,
        Dictionary<Guid, Lot> lots)
    {
        if (lots.TryGetValue(lotId, out var lot))
        {
            itemId ??= lot.ItemId;
            var fromLot = lot.GroupName?.Trim() ?? "";
            if (itemId is Guid id && items.TryGetValue(id, out var item))
                return item.Group?.Name?.Trim() ?? fromLot;
            return fromLot;
        }

        if (itemId is Guid itemKey && items.TryGetValue(itemKey, out var byItem))
            return byItem.Group?.Name?.Trim() ?? "";
        return "";
    }

    private static decimal SignedQtyForWarehouse(
        DocumentKind kind,
        Guid lineWarehouseId,
        Guid? destinationWarehouseId,
        Guid targetWarehouseId,
        decimal actualQuantity)
    {
        var qty = QuantityMath.Whole(actualQuantity);
        if (qty == 0)
            return 0;
        switch (kind)
        {
            case DocumentKind.Opening:
            case DocumentKind.Import:
                return lineWarehouseId == targetWarehouseId ? qty : 0;
            case DocumentKind.Issue:
            case DocumentKind.Consumption:
            case DocumentKind.Auxiliary:
                return lineWarehouseId == targetWarehouseId ? -qty : 0;
            case DocumentKind.Transfer:
                if (lineWarehouseId == targetWarehouseId)
                    return -qty;
                if (destinationWarehouseId == targetWarehouseId)
                    return qty;
                return 0;
            case DocumentKind.LotConvert:
                // Đổi loại cùng kho: không tính là nhập ĐC máy/tàu.
                return 0;
            default:
                return 0;
        }
    }

    private static string LotTypeCodeForWarehouseEffect(
        DocumentKind kind,
        Guid lineWarehouseId,
        Guid? destinationWarehouseId,
        Guid targetWarehouseId,
        string? lotTypeCode,
        string? destinationLotTypeCode)
    {
        var source = string.IsNullOrWhiteSpace(lotTypeCode) ? "TX" : lotTypeCode.Trim();
        var dest = string.IsNullOrWhiteSpace(destinationLotTypeCode) ? source : destinationLotTypeCode.Trim();
        if (kind == DocumentKind.Transfer && destinationWarehouseId == targetWarehouseId && lineWarehouseId != targetWarehouseId)
            return dest;
        return source;
    }

    public IReadOnlyList<DocumentDetail> ListDocumentHeaders(params DocumentKind[] kinds)
    {
        using var db = _factory();
        var query = db.Documents.AsNoTracking().AsQueryable();
        if (kinds.Length > 0)
            query = query.Where(x => kinds.Contains(x.Kind));
        return query.OrderByDescending(x => x.Sequence).AsEnumerable().Select(MapDetail).ToList();
    }

    public IReadOnlyList<StockRow> ListStock(StockFilter filter)
    {
        using var db = _factory();
        long? price = null;
        if (filter.UnitPrice is decimal unitPrice && QuantityMath.TryWholeMoney(unitPrice, out var whole))
            price = whole;

        var balances = db.StockBalances.AsNoTracking().AsQueryable();
        if (!filter.IncludeZero)
            balances = balances.Where(x => x.Quantity != 0);
        if (filter.WarehouseId is Guid singleWh)
            balances = balances.Where(x => x.WarehouseId == singleWh);
        else if (filter.WarehouseIds is { Count: > 0 } ids)
        {
            var idList = ids as List<Guid> ?? ids.ToList();
            balances = balances.Where(x => idList.Contains(x.WarehouseId));
        }

        if (price is long expectedPrice)
        {
            var expected = expectedPrice;
            balances =
                from balance in balances
                join lot in db.Lots.AsNoTracking() on balance.LotId equals lot.Id
                where lot.UnitPrice == expected
                select balance;
        }

        var rows = (
            from balance in balances
            join lot in db.Lots.AsNoTracking() on balance.LotId equals lot.Id
            join warehouse in db.Warehouses.AsNoTracking() on balance.WarehouseId equals warehouse.Id
            join lotType in db.LotTypes.AsNoTracking() on lot.LotTypeId equals lotType.Id into types
            from lotType in types.DefaultIfEmpty()
            join item in db.FuelItems.AsNoTracking() on lot.ItemId equals item.Id into items
            from item in items.DefaultIfEmpty()
            join groupRow in db.ItemGroups.AsNoTracking() on item.GroupId equals groupRow.Id into groups
            from groupRow in groups.DefaultIfEmpty()
            select new
            {
                balance.LotId,
                balance.WarehouseId,
                balance.Quantity,
                WarehouseName = warehouse.Name,
                warehouse.Type,
                GroupName = groupRow != null ? groupRow.Name : lot.GroupName,
                lot.ItemName,
                lot.ItemNameKey,
                lot.UnitName,
                lot.UnitPrice,
                lot.LotTypeId,
                LotTypeCode = lotType != null ? lotType.Code : ""
            }).ToList();

        var itemName = filter.ItemName?.Trim();
        var groupName = filter.GroupName?.Trim();
        var itemKey = string.IsNullOrWhiteSpace(itemName) ? null : QuantityMath.LotKey(itemName);
        return rows.Where(x =>
            {
                if (!string.IsNullOrWhiteSpace(itemName)
                    && x.ItemName.Contains(itemName, StringComparison.OrdinalIgnoreCase) == false
                    && (itemKey is null || x.ItemNameKey.Contains(itemKey, StringComparison.Ordinal) == false))
                    return false;
                if (!string.IsNullOrWhiteSpace(groupName)
                    && x.GroupName.Contains(groupName, StringComparison.OrdinalIgnoreCase) == false)
                    return false;
                return true;
            })
            .OrderBy(x => x.WarehouseName).ThenBy(x => x.GroupName).ThenBy(x => x.ItemName).ThenBy(x => x.UnitPrice).ThenBy(x => x.LotTypeCode)
            .Select(x => new StockRow(x.LotId, x.WarehouseId, x.WarehouseName, Labels.Warehouse(x.Type), x.GroupName, x.ItemName, x.UnitName, x.UnitPrice, x.LotTypeId, x.LotTypeCode.Length > 0 ? x.LotTypeCode : "TX", x.Quantity))
            .ToList();
    }

    public IReadOnlyList<MovementRow> ListMovements(Guid? lotId, Guid? warehouseId)
    {
        using var db = _factory();
        var moves = db.StockMovements.AsNoTracking().AsQueryable();
        if (lotId is Guid lid)
            moves = moves.Where(x => x.LotId == lid);
        if (warehouseId is Guid wid)
            moves = moves.Where(x => x.WarehouseId == wid);

        return (
            from move in moves
            join doc in db.Documents.AsNoTracking() on move.DocumentId equals doc.Id
            join lot in db.Lots.AsNoTracking() on move.LotId equals lot.Id
            join lotType in db.LotTypes.AsNoTracking() on lot.LotTypeId equals lotType.Id into types
            from lotType in types.DefaultIfEmpty()
            join warehouse in db.Warehouses.AsNoTracking() on move.WarehouseId equals warehouse.Id
            orderby move.OccurredAt
            select new MovementRow(
                move.DocumentId,
                doc.Number,
                move.OccurredAt,
                move.Reason,
                warehouse.Name,
                lot.ItemName,
                lot.UnitPrice,
                lot.LotTypeId,
                lotType != null && lotType.Code.Length > 0 ? lotType.Code : "TX",
                move.SignedQuantity)).ToList();
    }

    public IReadOnlyList<LotOption> GetLots(Guid warehouseId, Guid? includeLotId)
    {
        using var db = _factory();
        var rows = (
            from balance in db.StockBalances.AsNoTracking()
            join lot in db.Lots.AsNoTracking() on balance.LotId equals lot.Id
            join lotType in db.LotTypes.AsNoTracking() on lot.LotTypeId equals lotType.Id into types
            from lotType in types.DefaultIfEmpty()
            where balance.WarehouseId == warehouseId
            select new { lot.Id, lot.ItemId, lot.ItemName, lot.GroupName, lot.UnitPrice, lot.LotTypeId, LotTypeCode = lotType != null ? lotType.Code : "", balance.Quantity, lot.FirstVcf }).ToList()
            .Select(x => new LotOption(x.Id, x.ItemId, x.ItemName, x.UnitPrice, x.LotTypeId, x.LotTypeCode.Length > 0 ? x.LotTypeCode : "TX", x.Quantity, x.FirstVcf, "", x.GroupName))
            .ToList();

        if (includeLotId is Guid extra && rows.All(x => x.LotId != extra))
        {
            var lot = (
                from l in db.Lots.AsNoTracking()
                join lotType in db.LotTypes.AsNoTracking() on l.LotTypeId equals lotType.Id into types
                from lotType in types.DefaultIfEmpty()
                where l.Id == extra
                select new { Lot = l, Code = lotType != null ? lotType.Code : "" }
            ).FirstOrDefault();
            if (lot is not null)
                rows.Add(new LotOption(lot.Lot.Id, lot.Lot.ItemId, lot.Lot.ItemName, lot.Lot.UnitPrice, lot.Lot.LotTypeId, lot.Code.Length > 0 ? lot.Code : "TX", 0, lot.Lot.FirstVcf, "", lot.Lot.GroupName));
        }

        return rows.Where(x => x.Quantity > 0 || x.LotId == includeLotId)
            .OrderBy(x => x.ItemName).ThenBy(x => x.UnitPrice).ThenBy(x => x.LotTypeCode)
            .Select(x => x with { Display = $"{x.ItemName} | giá {Whole(x.UnitPrice)} | {x.LotTypeCode} | tồn {Whole(x.Quantity)}" })
            .ToList();
    }

    public DashboardSummary GetDashboard()
    {
        using var db = _factory();
        var stock = db.StockBalances.AsNoTracking().Sum(x => x.Quantity);
        var recent = db.Documents.AsNoTracking().OrderByDescending(x => x.Sequence).Take(8).AsEnumerable().Select(MapRow).ToList();
        return new DashboardSummary(db.FuelItems.Count(), db.Warehouses.Count(), db.Consumers.Count(), stock, recent);
    }

    internal void DebugSetBalance(Guid lotId, Guid warehouseId, decimal quantity)
    {
        using var db = _factory();
        var balance = db.StockBalances.First(x => x.LotId == lotId && x.WarehouseId == warehouseId);
        balance.Quantity = quantity;
        db.SaveChanges();
    }

    private FuelResult InTx(Func<AppDbContext, FuelResult> action)
    {
        using var db = _factory();
        using var tx = db.Database.BeginTransaction();
        try
        {
            var result = action(db);
            if (!result.Ok)
            {
                tx.Rollback();
                return result;
            }

            db.SaveChanges();
            tx.Commit();
            return result;
        }
        catch (FuelRuleException ex)
        {
            tx.Rollback();
            return FuelResult.Fail(ex.Message);
        }
    }

    private static Guid WriteOpening(AppDbContext db, OpeningRequest request, HashSet<(Guid, Guid)> touched)
    {
        var item = RequireItem(db, request.ItemId);
        var warehouse = RequireWarehouse(db, request.WarehouseId);
        if (!QuantityMath.TryWholeMoney(request.UnitPrice, out var price) || price < 0)
            throw new FuelRuleException("Đơn giá phải là số nguyên không âm.");
        var actual = QuantityMath.Whole(request.ActualQuantity);
        if (actual <= 0)
            throw new FuelRuleException("Số lượng tồn đầu phải lớn hơn 0.");
        var itemName = Or(request.ItemName, item.Name);
        var lotType = ResolveLotType(db, request.LotTypeId);
        var doc = Begin(db, request.DocumentId ?? ExistingOpeningSlip(db, warehouse.Id, itemName, price, lotType.Id), DocumentKind.Opening, touched);
        var groupName = Or(request.GroupName, item.Group?.Name ?? "");
        var unitName = Or(request.UnitName, item.Unit?.Name ?? "");
        var quality = Or(request.QualityInfo, item.QualityInfo);
        var measurement = Or(request.MeasurementNote, item.MeasurementNote);
        var lot = EnsureLot(db, itemName, price, lotType.Id, item.Id, groupName, unitName, quality, request.Temperature ?? item.Temperature, measurement, Labels.OpeningRule, 0);
        WriteHeader(doc, request.DocumentDate, item.Id, itemName, groupName, unitName, quality, request.Temperature ?? item.Temperature, measurement, Labels.OpeningRule, 0,
            warehouse.Id, Or(request.WarehouseName, warehouse.Name), Or(request.WarehouseTypeName, Labels.Warehouse(warehouse.Type)));
        doc.UnitPrice = price;
        doc.LotTypeId = lotType.Id;
        doc.LotTypeCode = lotType.Code;
        doc.InputQuantity = actual;
        doc.ActualQuantity = actual;
        doc.Amount = null;
        doc.WasSplit = false;
        var line = NewLine(doc, 1, lot, warehouse.Id, actual, actual, 0);
        line.LotTypeId = lotType.Id;
        line.LotTypeCode = lotType.Code;
        AttachLine(db, doc, line);
        AddEffects(db, doc, touched);
        return doc.Id;
    }

    private static Guid? ExistingOpeningSlip(AppDbContext db, Guid warehouseId, string itemName, long price, Guid lotTypeId)
    {
        var key = QuantityMath.LotKey(itemName);
        return db.Documents
            .Where(x => x.Status == DocumentStatus.Active
                && x.Kind == DocumentKind.Opening
                && x.WarehouseId == warehouseId
                && x.UnitPrice == price
                && (x.LotTypeId == null || x.LotTypeId == lotTypeId))
            .OrderByDescending(x => x.Sequence)
            .AsEnumerable()
            .Where(x => QuantityMath.LotKey(x.ItemName) == key
                && (x.LotTypeId ?? SeedIds.LotTypeTx) == lotTypeId)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefault();
    }

    private static Guid WriteConsumption(AppDbContext db, ConsumptionCellRequest request, HashSet<(Guid, Guid)> touched)
    {
        Consumer? consumer = null;
        if (request.ConsumerId is Guid consumerId && consumerId != Guid.Empty)
        {
            consumer = db.Consumers.FirstOrDefault(x => x.Id == consumerId)
                ?? throw new FuelRuleException("Đối tượng tiêu thụ không tồn tại.");
        }

        var warehouse = RequireWarehouse(db, request.WarehouseId);
        if (!QuantityMath.TryWholeMoney(request.UnitPrice, out var price) || price < 0)
            throw new FuelRuleException("Đơn giá phải là số nguyên không âm.");
        var lotType = ResolveLotType(db, request.LotTypeId);
        var key = QuantityMath.LotKey(request.ItemName);
        var lot = db.Lots.Local.FirstOrDefault(x => x.ItemNameKey == key && x.UnitPrice == price && x.LotTypeId == lotType.Id)
            ?? db.Lots.FirstOrDefault(x => x.ItemNameKey == key && x.UnitPrice == price && x.LotTypeId == lotType.Id)
            ?? throw new FuelRuleException($"Không có lô {request.ItemName.Trim()} đơn giá {price:N0} loại {lotType.Code} để xuất tiêu thụ.");

        decimal? operating = null;
        decimal? shipNorm = null;
        var actual = QuantityMath.Whole(request.ActualQuantity);
        // Tạm thời tiêu thụ quý của tàu dùng lượng người dùng nhập (ActualQuantity).
        // Định mức tỷ lệ quy đổi chưa áp dụng để tính — không validate bắt buộc.
        if (consumer?.Type == ConsumerType.Ship)
        {
            if (request.OperatingQuantity is decimal run && run >= 0)
                operating = QuantityMath.RoundQty(run);
            if (request.Norm is decimal norm && norm > 0)
                shipNorm = QuantityMath.RoundNorm(norm);
        }

        if (actual <= 0)
            throw new FuelRuleException("Lượng tiêu thụ phải lớn hơn 0.");
        var vcf = QuantityMath.RoundVcf(request.Vcf);
        var display = QuantityMath.ExportDisplayQuantity(actual, vcf);
        var doc = Begin(db, request.DocumentId ?? ExistingQuarterSlip(db, lot, warehouse.Id, request.DocumentDate), DocumentKind.Consumption, touched);
        WriteLotHeader(doc, request.DocumentDate, lot, vcf, warehouse.Id, Or(request.WarehouseName, warehouse.Name), Or(request.WarehouseTypeName, Labels.Warehouse(warehouse.Type)));
        if (consumer is not null)
        {
            doc.ConsumerId = consumer.Id;
            doc.ConsumerName = Or(request.ConsumerName, consumer.Name);
            doc.ConsumerCode = Or(request.ConsumerCode, consumer.Code);
            doc.ConsumerTypeName = Labels.Consumer(consumer.Type);
        }

        doc.Norm = shipNorm;
        doc.Distance = null;
        doc.OperatingQuantity = operating;
        doc.UnitPrice = lot.UnitPrice;
        doc.LotTypeId = lot.LotTypeId;
        doc.LotTypeCode = lotType.Code;
        doc.InputQuantity = display;
        doc.ActualQuantity = actual;
        doc.Amount = null;
        var line = NewLine(doc, 1, lot, warehouse.Id, display, actual, 0);
        line.Vcf = vcf;
        line.LotTypeId = lot.LotTypeId;
        line.LotTypeCode = lotType.Code;
        AttachLine(db, doc, line);
        if (request.Fields.Count > 0 || request.AddToSampleSetId is not null)
            ApplyFields(db, doc, DocumentFamily.Export, request.Fields, request.AddToSampleSetId);
        AddEffects(db, doc, touched);
        return doc.Id;
    }

    private static Guid? ExistingQuarterSlip(AppDbContext db, Lot lot, Guid warehouseId, DateTime documentDate)
    {
        var date = documentDate == default ? DateTime.Today : documentDate.Date;
        var start = new DateTime(date.Year, ((date.Month - 1) / 3) * 3 + 1, 1);
        var end = start.AddMonths(3);
        var deletedIds = db.ChangeTracker.Entries<FuelDocument>()
            .Where(x => x.State == EntityState.Deleted)
            .Select(x => x.Entity.Id)
            .ToHashSet();
        return db.Documents
            .Where(x => x.Status == DocumentStatus.Active
                && x.Kind == DocumentKind.Consumption
                && x.Distance == null
                && x.WarehouseId == warehouseId
                && x.UnitPrice == lot.UnitPrice
                && (x.LotTypeId == null || x.LotTypeId == lot.LotTypeId)
                && x.DocumentDate >= start
                && x.DocumentDate < end)
            .OrderByDescending(x => x.Sequence)
            .AsEnumerable()
            .Where(x => !deletedIds.Contains(x.Id)
                && QuantityMath.LotKey(x.ItemName) == lot.ItemNameKey
                && (x.LotTypeId ?? SeedIds.LotTypeTx) == lot.LotTypeId)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefault();
    }

    private static Guid WriteAuxiliaryCell(AppDbContext db, AuxiliaryCellRequest request, HashSet<(Guid, Guid)> touched)
    {
        var warehouse = RequireWarehouse(db, request.WarehouseId);
        if (warehouse.Type != WarehouseType.Auxiliary)
            throw new FuelRuleException("Chỉ tiêu thụ trên kho phụ.");
        if (!QuantityMath.TryWholeMoney(request.UnitPrice, out var price) || price < 0)
            throw new FuelRuleException("Đơn giá phải là số nguyên không âm.");
        var lotType = ResolveLotType(db, request.LotTypeId);
        var key = QuantityMath.LotKey(request.ItemName);
        var lot = db.Lots.Local.FirstOrDefault(x => x.ItemNameKey == key && x.UnitPrice == price && x.LotTypeId == lotType.Id)
            ?? db.Lots.FirstOrDefault(x => x.ItemNameKey == key && x.UnitPrice == price && x.LotTypeId == lotType.Id)
            ?? throw new FuelRuleException($"Không có lô {request.ItemName.Trim()} đơn giá {price:N0} loại {lotType.Code} để tiêu thụ.");
        var actual = QuantityMath.Whole(request.ActualQuantity);
        if (actual <= 0)
            throw new FuelRuleException("Lượng tiêu thụ phải lớn hơn 0.");
        var vcf = QuantityMath.RoundVcf(request.Vcf);
        var display = QuantityMath.ExportDisplayQuantity(actual, vcf);
        var doc = Begin(db, request.DocumentId, DocumentKind.Auxiliary, touched);
        WriteLotHeader(doc, request.DocumentDate, lot, vcf, warehouse.Id, Or(request.WarehouseName, warehouse.Name), Or(request.WarehouseTypeName, Labels.Warehouse(warehouse.Type)));
        doc.UnitPrice = lot.UnitPrice;
        doc.LotTypeId = lot.LotTypeId;
        doc.LotTypeCode = lotType.Code;
        doc.InputQuantity = display;
        doc.ActualQuantity = actual;
        doc.Amount = null;
        var line = NewLine(doc, 1, lot, warehouse.Id, display, actual, 0);
        line.Vcf = vcf;
        line.LotTypeId = lot.LotTypeId;
        line.LotTypeCode = lotType.Code;
        AttachLine(db, doc, line);
        AddEffects(db, doc, touched);
        return doc.Id;
    }

    private static FuelDocument Begin(AppDbContext db, Guid? documentId, DocumentKind kind, HashSet<(Guid, Guid)> touched)
    {
        if (documentId is null)
        {
            var created = new FuelDocument
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                Status = DocumentStatus.Active,
                CreatedAt = DateTime.UtcNow,
                Sequence = NextSequence(db),
                Number = NextNumber(db, Prefix(kind))
            };
            db.Documents.Add(created);
            return created;
        }

        var existing = db.Documents.Local.FirstOrDefault(x => x.Id == documentId)
            ?? db.Documents.Include(x => x.Lines).Include(x => x.Fields).FirstOrDefault(x => x.Id == documentId);
        // Phiếu đang xóa trong cùng transaction (chưa SaveChanges) — SQL vẫn thấy, không tái sử dụng.
        if (existing is not null && db.Entry(existing).State == EntityState.Deleted)
            existing = null;
        if (existing is null)
        {
            var created = new FuelDocument
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                Status = DocumentStatus.Active,
                CreatedAt = DateTime.UtcNow,
                Sequence = NextSequence(db),
                Number = NextNumber(db, Prefix(kind))
            };
            db.Documents.Add(created);
            return created;
        }

        if (existing.Status != DocumentStatus.Active)
            throw new FuelRuleException("Chứng từ đã hủy, không sửa.");
        if (existing.Kind != kind)
            throw new FuelRuleException("Không đổi loại chứng từ.");
        if (db.Entry(existing).Collection(x => x.Lines).IsLoaded == false)
            db.Entry(existing).Collection(x => x.Lines).Load();
        if (db.Entry(existing).Collection(x => x.Fields).IsLoaded == false)
            db.Entry(existing).Collection(x => x.Fields).Load();
        RemoveEffects(db, existing, touched);
        foreach (var line in existing.Lines.ToList())
            existing.Lines.Remove(line);
        foreach (var field in existing.Fields.ToList())
            existing.Fields.Remove(field);
        existing.UpdatedAt = DateTime.UtcNow;
        return existing;
    }

    private static void WriteHeader(FuelDocument doc, DateTime date, Guid itemId, string itemName, string groupName, string unitName, string quality, decimal? temperature, string measurement, string rule, decimal vcf, Guid warehouseId, string warehouseName, string warehouseType)
    {
        doc.DocumentDate = date == default ? DateTime.Today : date.Date;
        doc.ItemId = itemId;
        doc.ItemName = itemName;
        doc.GroupName = groupName;
        doc.UnitName = unitName;
        doc.QualityInfo = quality;
        doc.Temperature = temperature;
        doc.MeasurementNote = measurement;
        doc.ConversionRule = rule;
        doc.Vcf = vcf;
        doc.WarehouseId = warehouseId;
        doc.WarehouseName = warehouseName;
        doc.WarehouseTypeName = warehouseType;
    }

    private static void WriteLotHeader(FuelDocument doc, DateTime date, Lot lot, decimal vcf, Guid warehouseId, string warehouseName, string warehouseType)
    {
        WriteHeader(doc, date, lot.ItemId ?? Guid.Empty, lot.ItemName, lot.GroupName, lot.UnitName, lot.QualityInfo, lot.Temperature, lot.MeasurementNote, lot.ConversionRule, vcf, warehouseId, warehouseName, warehouseType);
        if (lot.ItemId is null)
            doc.ItemId = null;
        doc.LotTypeId = lot.LotTypeId;
        if (doc.LotTypeCode.Length == 0)
            doc.LotTypeCode = lot.LotType?.Code ?? "";
    }

    private static (decimal Actual, decimal? Distance, decimal? Norm) ResolveConsumption(ConsumptionRequest request, Consumer consumer)
    {
        if (request.ConsumerType == ConsumerType.Vehicle)
        {
            if (request.Distance is null || request.Distance < 0)
                throw new FuelRuleException("Quãng đường phải lớn hơn hoặc bằng 0.");
            var distance = QuantityMath.RoundQty(request.Distance.Value);
            var norm = QuantityMath.RoundNorm(request.Norm ?? consumer.Norm ?? 0);
            if (norm <= 0)
                throw new FuelRuleException("Định mức phải lớn hơn 0.");
            var actual = QuantityMath.VehicleActual(distance, norm);
            if (actual <= 0)
                throw new FuelRuleException("Thực xuất phải lớn hơn 0.");
            return (actual, distance, norm);
        }

        var direct = QuantityMath.Whole(request.DirectActual ?? 0);
        if (direct <= 0)
            throw new FuelRuleException("Thực xuất phải lớn hơn 0.");
        return (direct, null, null);
    }

    /// <summary>Phiếu nhập: số phiếu trống → dùng số hệ thống; đồng bộ sang trường «Số hóa đơn» nếu còn trống.</summary>
    private static IReadOnlyList<FieldInput> WithAutoImportFormNumber(
        AppDbContext db, FuelDocument doc, IReadOnlyList<FieldInput> inputs)
    {
        if (string.IsNullOrWhiteSpace(doc.FormNumber))
            doc.FormNumber = doc.Number;

        var invoice = db.FieldDefinitions.Local
                .FirstOrDefault(IsImportInvoiceField)
            ?? db.FieldDefinitions.AsEnumerable().FirstOrDefault(IsImportInvoiceField);
        if (invoice is null || string.IsNullOrWhiteSpace(doc.FormNumber))
            return inputs;

        var list = inputs.ToList();
        var existing = list.FirstOrDefault(x => x.FieldId == invoice.Id);
        if (existing is not null && !string.IsNullOrWhiteSpace(existing.Value))
            return list;

        list.RemoveAll(x => x.FieldId == invoice.Id);
        list.Add(new FieldInput { FieldId = invoice.Id, Value = doc.FormNumber.Trim() });
        return list;
    }

    private static bool IsImportInvoiceField(FieldDefinition field) =>
        field.Family == DocumentFamily.Import
        && field.IsVisible
        && (field.Id == SeedIds.FieldInvoice
            || field.Name.Contains("hóa đơn", StringComparison.OrdinalIgnoreCase)
            || field.Name.Contains("hoa don", StringComparison.OrdinalIgnoreCase));

    private static void ApplyFields(AppDbContext db, FuelDocument doc, DocumentFamily family, IReadOnlyList<FieldInput> inputs, Guid? addToSampleSetId)
    {
        var defs = db.FieldDefinitions.Where(x => x.Family == family).ToList();
        foreach (var required in defs.Where(x => x.IsVisible && x.IsRequired))
        {
            var given = inputs.FirstOrDefault(x => x.FieldId == required.Id)?.Value?.Trim() ?? "";
            if (given.Length == 0)
                throw new FuelRuleException($"Trường bắt buộc: {required.Name}.");
        }

        foreach (var input in inputs)
        {
            var def = defs.FirstOrDefault(x => x.Id == input.FieldId);
            if (def is null || !def.IsVisible)
                continue;
            var value = input.Value?.Trim() ?? "";
            if (value.Length == 0)
                continue;
            if (!CatalogStore.ValueMatches(def.DataType, value))
                throw new FuelRuleException($"Trường {def.Name} không đúng kiểu dữ liệu.");
            var snapshot = new FuelDocumentField
            {
                Id = Guid.NewGuid(),
                DocumentId = doc.Id,
                FieldDefinitionId = def.Id,
                Name = def.Name,
                DataType = Labels.DataType(def.DataType),
                IsRequired = def.IsRequired,
                Value = value
            };
            db.DocumentFields.Add(snapshot);
            if (!doc.Fields.Contains(snapshot))
                doc.Fields.Add(snapshot);
        }

        if (addToSampleSetId is not Guid setId)
            return;
        var set = db.SampleSets.FirstOrDefault(x => x.Id == setId)
            ?? throw new FuelRuleException("Không tìm thấy bộ dữ liệu mẫu.");
        if (set.Family != family)
            throw new FuelRuleException("Bộ dữ liệu mẫu không cùng loại phiếu.");
        foreach (var field in doc.Fields)
        {
            if (field.FieldDefinitionId is not Guid fieldId)
                continue;
            if (!SlipFieldCatalog.AllowsSampleValues(field.Name))
                continue;
            if (db.SampleValues.Local.Any(x => x.SampleSetId == setId && x.FieldDefinitionId == fieldId && x.Value == field.Value)
                || db.SampleValues.Any(x => x.SampleSetId == setId && x.FieldDefinitionId == fieldId && x.Value == field.Value))
                continue;
            db.SampleValues.Add(new SampleValue
            {
                Id = Guid.NewGuid(),
                SampleSetId = setId,
                FieldDefinitionId = fieldId,
                Value = field.Value,
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    private static Lot EnsureLot(AppDbContext db, string itemName, long price, Guid lotTypeId, Guid? itemId, string group, string unit, string quality, decimal? temperature, string measurement, string rule, decimal firstVcf)
    {
        var key = QuantityMath.LotKey(itemName);
        if (key.Length == 0)
            throw new FuelRuleException("Thiếu tên mặt hàng để xác định lô.");
        var type = ResolveLotType(db, lotTypeId);
        var lot = db.Lots.Local.FirstOrDefault(x => x.ItemNameKey == key && x.UnitPrice == price && x.LotTypeId == type.Id)
            ?? db.Lots.FirstOrDefault(x => x.ItemNameKey == key && x.UnitPrice == price && x.LotTypeId == type.Id);
        if (lot is not null)
            return lot;
        lot = new Lot
        {
            Id = Guid.NewGuid(),
            ItemName = itemName.Trim(),
            ItemNameKey = key,
            UnitPrice = price,
            LotTypeId = type.Id,
            ItemId = itemId,
            GroupName = group,
            UnitName = unit,
            QualityInfo = quality,
            Temperature = temperature,
            MeasurementNote = measurement,
            ConversionRule = rule,
            FirstVcf = firstVcf
        };
        db.Lots.Add(lot);
        return lot;
    }

    private static LotType ResolveLotType(AppDbContext db, Guid? lotTypeId)
    {
        var id = lotTypeId is Guid given && given != Guid.Empty ? given : SeedIds.LotTypeTx;
        var type = db.LotTypes.Local.FirstOrDefault(x => x.Id == id)
            ?? db.LotTypes.FirstOrDefault(x => x.Id == id);
        if (type is not null)
            return type;
        if (id != SeedIds.LotTypeTx)
        {
            type = db.LotTypes.Local.FirstOrDefault(x => x.Id == SeedIds.LotTypeTx)
                ?? db.LotTypes.FirstOrDefault(x => x.Id == SeedIds.LotTypeTx);
            if (type is not null)
                return type;
        }

        throw new FuelRuleException("Chưa có danh mục loại lô. Hãy mở lại ứng dụng để khởi tạo TX, SSCĐ, IUU.");
    }

    private static void AttachLine(AppDbContext db, FuelDocument doc, FuelDocumentLine line)
    {
        db.DocumentLines.Add(line);
        if (!doc.Lines.Contains(line))
            doc.Lines.Add(line);
    }

    private static FuelDocumentLine NewLine(FuelDocument doc, int lineNo, Lot lot, Guid warehouseId, decimal quantity, decimal actual, decimal amount) =>
        new()
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            LineNo = lineNo,
            LotId = lot.Id,
            WarehouseId = warehouseId,
            ItemName = lot.ItemName,
            UnitPrice = lot.UnitPrice,
            LotTypeId = lot.LotTypeId,
            Quantity = quantity,
            ActualQuantity = actual,
            Amount = amount
        };

    private static void AddEffects(AppDbContext db, FuelDocument doc, HashSet<(Guid, Guid)> touched)
    {
        switch (doc.Kind)
        {
            case DocumentKind.Opening:
                foreach (var line in doc.Lines)
                    Post(db, doc, line.LotId, line.WarehouseId, line.ActualQuantity, "Tồn đầu kỳ", touched);
                break;
            case DocumentKind.Import:
                foreach (var line in doc.Lines)
                    Post(db, doc, line.LotId, line.WarehouseId, line.ActualQuantity, "Thực nhập", touched);
                break;
            case DocumentKind.Transfer:
                if (doc.DestinationWarehouseId is not Guid destinationId)
                    throw new FuelRuleException("Phiếu điều chuyển thiếu kho nhận.");
                foreach (var line in doc.Lines)
                {
                    Post(db, doc, line.LotId, line.WarehouseId, -line.ActualQuantity, "Điều chuyển đi", touched);
                    var sourceLot = RequireLot(db, line.LotId);
                    var destTypeId = line.DestinationLotTypeId ?? sourceLot.LotTypeId;
                    Guid destinationLotId;
                    if (destTypeId == sourceLot.LotTypeId)
                        destinationLotId = sourceLot.Id;
                    else
                    {
                        var destLot = EnsureLot(
                            db,
                            sourceLot.ItemName,
                            sourceLot.UnitPrice,
                            destTypeId,
                            sourceLot.ItemId,
                            sourceLot.GroupName,
                            sourceLot.UnitName,
                            sourceLot.QualityInfo,
                            sourceLot.Temperature,
                            sourceLot.MeasurementNote,
                            sourceLot.ConversionRule,
                            sourceLot.FirstVcf);
                        destinationLotId = destLot.Id;
                        line.DestinationLotTypeId = destTypeId;
                        if (line.DestinationLotTypeCode.Length == 0)
                            line.DestinationLotTypeCode = ResolveLotType(db, destTypeId).Code;
                    }

                    Post(db, doc, destinationLotId, destinationId, line.ActualQuantity, "Điều chuyển đến", touched);
                }
                break;
            case DocumentKind.LotConvert:
                foreach (var line in doc.Lines)
                {
                    Post(db, doc, line.LotId, line.WarehouseId, -line.ActualQuantity, "Đổi loại đi", touched);
                    var sourceLot = RequireLot(db, line.LotId);
                    var destTypeId = line.DestinationLotTypeId
                        ?? throw new FuelRuleException("Phiếu đổi loại lô thiếu loại đích.");
                    if (destTypeId == sourceLot.LotTypeId)
                        throw new FuelRuleException("Loại lô đích phải khác loại nguồn.");
                    var destLot = EnsureLot(
                        db,
                        sourceLot.ItemName,
                        sourceLot.UnitPrice,
                        destTypeId,
                        sourceLot.ItemId,
                        sourceLot.GroupName,
                        sourceLot.UnitName,
                        sourceLot.QualityInfo,
                        sourceLot.Temperature,
                        sourceLot.MeasurementNote,
                        sourceLot.ConversionRule,
                        sourceLot.FirstVcf);
                    line.DestinationLotTypeId = destTypeId;
                    if (line.DestinationLotTypeCode.Length == 0)
                        line.DestinationLotTypeCode = ResolveLotType(db, destTypeId).Code;
                    Post(db, doc, destLot.Id, line.WarehouseId, line.ActualQuantity, "Đổi loại đến", touched);
                }
                break;
            case DocumentKind.Consumption:
                foreach (var line in doc.Lines)
                    Post(db, doc, line.LotId, line.WarehouseId, -line.ActualQuantity, "Xuất tiêu thụ", touched);
                break;
            case DocumentKind.Auxiliary:
                foreach (var line in doc.Lines)
                    Post(db, doc, line.LotId, line.WarehouseId, -line.ActualQuantity, "Tiêu thụ kho phụ", touched);
                break;
            case DocumentKind.Issue:
                foreach (var line in doc.Lines)
                    Post(db, doc, line.LotId, line.WarehouseId, -line.ActualQuantity, "Thực xuất", touched);
                break;
        }
    }

    private static void Post(AppDbContext db, FuelDocument doc, Guid lotId, Guid warehouseId, decimal signed, string reason, HashSet<(Guid, Guid)> touched)
    {
        var qty = QuantityMath.Whole(signed);
        if (qty == 0)
            return;
        db.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            LotId = lotId,
            WarehouseId = warehouseId,
            SignedQuantity = qty,
            Reason = reason,
            OccurredAt = doc.DocumentDate.Date
        });
        Adjust(db, lotId, warehouseId, qty, touched);
    }

    private static void RemoveEffects(AppDbContext db, FuelDocument doc, HashSet<(Guid, Guid)> touched)
    {
        var moves = db.StockMovements.Where(x => x.DocumentId == doc.Id).ToList();
        foreach (var move in moves)
        {
            Adjust(db, move.LotId, move.WarehouseId, -move.SignedQuantity, touched);
            db.StockMovements.Remove(move);
        }
    }

    private static void Adjust(AppDbContext db, Guid lotId, Guid warehouseId, decimal delta, HashSet<(Guid, Guid)> touched)
    {
        var balance = db.StockBalances.FirstOrDefault(x => x.LotId == lotId && x.WarehouseId == warehouseId);
        if (balance is null)
        {
            balance = new StockBalance { Id = Guid.NewGuid(), LotId = lotId, WarehouseId = warehouseId, Quantity = 0 };
            db.StockBalances.Add(balance);
        }

        balance.Quantity = QuantityMath.Whole(balance.Quantity + delta);
        touched.Add((lotId, warehouseId));
    }

    private static void EnsureNonNegative(AppDbContext db, HashSet<(Guid Lot, Guid Warehouse)> touched)
    {
        foreach (var key in touched)
        {
            var balance = db.StockBalances.FirstOrDefault(x => x.LotId == key.Lot && x.WarehouseId == key.Warehouse);
            if (balance is not null && balance.Quantity < 0)
                throw new FuelRuleException($"Không đủ tồn lô. Tồn sau khi ghi nhận sẽ là {Whole(balance.Quantity)}, không được âm.");
        }
    }

    private static FuelItem RequireItem(AppDbContext db, Guid id) =>
        db.FuelItems.Include(x => x.Group).Include(x => x.Unit).FirstOrDefault(x => x.Id == id)
        ?? throw new FuelRuleException("Mặt hàng không tồn tại.");

    private static Warehouse RequireWarehouse(AppDbContext db, Guid id) =>
        db.Warehouses.FirstOrDefault(x => x.Id == id) ?? throw new FuelRuleException("Kho không tồn tại.");

    /// <summary>Kho XD (+ phụ/đối tượng) độc lập với Kho PTKT-VTXD — không trộn trên cùng phiếu điều chuyển.</summary>
    private static bool SameWarehouseFamily(WarehouseType a, WarehouseType b) =>
        (a == WarehouseType.Ptkt) == (b == WarehouseType.Ptkt);

    private static Lot RequireLot(AppDbContext db, Guid id) =>
        db.Lots.FirstOrDefault(x => x.Id == id) ?? throw new FuelRuleException("Lô không tồn tại.");

    private static decimal? TryParseKilometers(string? text)
    {
        var value = text?.Trim() ?? "";
        if (value.Length == 0)
            return null;
        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out var km)
            || decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out km))
        {
            if (km >= 0)
                return QuantityMath.RoundQty(km);
        }

        return null;
    }

    private static string Or(string given, string fallback) =>
        string.IsNullOrWhiteSpace(given) ? fallback : given.Trim();

    private static DocumentKind ResolveSlipKind(SlipRequest request)
    {
        if (!request.IsExport)
            return DocumentKind.Import;
        return request.ExportMode switch
        {
            ExportSlipMode.Transfer => DocumentKind.Transfer,
            ExportSlipMode.LotConvert => DocumentKind.LotConvert,
            ExportSlipMode.Auxiliary => DocumentKind.Auxiliary,
            ExportSlipMode.Vehicle => DocumentKind.Consumption,
            ExportSlipMode.Retail => DocumentKind.Issue,
            _ => DocumentKind.Issue
        };
    }

    private static string Prefix(DocumentKind kind) => kind switch
    {
        DocumentKind.Opening => "TD",
        DocumentKind.Import => "PN",
        DocumentKind.Transfer => "DC",
        DocumentKind.LotConvert => "DL",
        DocumentKind.Consumption => "XT",
        DocumentKind.Auxiliary => "KP",
        DocumentKind.Issue => "PX",
        _ => "CT"
    };

    private static bool IsTxSscdConvertPair(LotType source, LotType destination)
    {
        static bool IsTxOrSscd(LotType type)
        {
            if (type.Id == SeedIds.LotTypeTx || type.Id == SeedIds.LotTypeSscd)
                return true;
            var code = (type.Code ?? "").Trim().ToUpperInvariant();
            return code is "TX" or "SSCĐ" or "SSCD";
        }

        return source.Id != destination.Id && IsTxOrSscd(source) && IsTxOrSscd(destination);
    }

    private static long NextSequence(AppDbContext db)
    {
        var stored = db.Documents.Select(x => (long?)x.Sequence).Max() ?? 0;
        var local = db.Documents.Local.Count == 0 ? 0 : db.Documents.Local.Max(x => x.Sequence);
        return Math.Max(stored, local) + 1;
    }

    private static string NextNumber(AppDbContext db, string prefix)
    {
        var max = 0L;
        var numbers = db.Documents.Local.Select(x => x.Number)
            .Concat(db.Documents.Where(x => x.Number.StartsWith(prefix)).Select(x => x.Number));
        foreach (var number in numbers)
        {
            if (number is not null && number.StartsWith(prefix) && number.Length > prefix.Length && long.TryParse(number[prefix.Length..], out var value) && value > max)
                max = value;
        }

        return prefix + (max + 1).ToString("D6");
    }

    private static void ApplySlipMeta(FuelDocument doc, SlipRequest request, decimal amount)
    {
        doc.FormNumber = Or(request.FormNumber, "");
        doc.OrganizationName = Or(request.OrganizationName, "");
        doc.UnitTitle = Or(request.UnitTitle, "");
        doc.SenderUnit = Or(request.SenderUnit, "");
        doc.ReceiverUnit = Or(request.ReceiverUnit, "");
        doc.Nature = Or(request.Nature, "");
        doc.ContractOrOrder = Or(request.ContractOrOrder, "");
        doc.CarrierUnit = Or(request.CarrierUnit, "");
        doc.PriceValidUntil = Or(request.PriceValidUntil, "");
        doc.DelivererName = Or(request.DelivererName, "");
        doc.IntroDocument = Or(request.IntroDocument, "");
        doc.VehiclePlate = Or(request.VehiclePlate, "");
        doc.CalibrationVolume = Or(request.CalibrationVolume, "");
        doc.ReceivedVolume = Or(request.ReceivedVolume, "");
        doc.PackageCount = Or(request.PackageCount, "");
        doc.ReceiverPerson = Or(request.ReceiverPerson, "");
        doc.Kilometers = Or(request.Kilometers, "");
        doc.Mission = Or(request.Mission, "");
        doc.MissionTaskId = request.MissionTaskId;
        doc.OriginPlace = Or(request.OriginPlace, "");
        doc.DestinationPlace = Or(request.DestinationPlace, "");
        doc.Note = Or(request.Note, "");
        doc.SignerReceiver = Or(request.SignerReceiver, "");
        doc.SignerDeliverer = Or(request.SignerDeliverer, "");
        doc.SignerFinance = Or(request.SignerFinance, "");
        doc.SignerWriter = Or(request.SignerWriter, "");
        doc.SignerChief = Or(request.SignerChief, "");
        doc.SignerCommander = Or(request.SignerCommander, "");
        doc.AmountInWords = string.IsNullOrWhiteSpace(request.AmountInWords)
            ? MoneyWords.ToDong(amount)
            : request.AmountInWords.Trim();
    }

    private static IReadOnlyList<DocumentRow> ProjectRows(IQueryable<FuelDocument> query) =>
        query.Select(doc => new DocumentRow(
            doc.Id, doc.Number, doc.Kind, "", doc.Status, "", doc.DocumentDate,
            doc.WarehouseName, doc.DestinationWarehouseName, doc.LotTypeCode ?? "", doc.ItemName, doc.ConsumerName, doc.UnitPrice,
            doc.InputQuantity, doc.ActualQuantity, doc.Vcf, doc.Amount, doc.WasSplit,
            doc.FormNumber, doc.Nature, doc.ReceiverPerson, doc.VehiclePlate, doc.Kilometers, doc.Mission,
            doc.Number, doc.Distance)).AsEnumerable().Select(FinishRow).ToList();

    private static DocumentRow FinishRow(DocumentRow row) => row with
    {
        KindName = Labels.Kind(row.Kind),
        StatusName = Labels.Status(row.Status),
        DisplayNumber = string.IsNullOrWhiteSpace(row.FormNumber) ? row.Number : row.FormNumber
    };

    private static DocumentRow MapRow(FuelDocument doc) => new(
        doc.Id, doc.Number, doc.Kind, Labels.Kind(doc.Kind), doc.Status, Labels.Status(doc.Status), doc.DocumentDate,
        doc.WarehouseName, doc.DestinationWarehouseName, doc.LotTypeCode ?? "", doc.ItemName, doc.ConsumerName, doc.UnitPrice,
        doc.InputQuantity, doc.ActualQuantity, doc.Vcf, doc.Amount, doc.WasSplit,
        doc.FormNumber, doc.Nature, doc.ReceiverPerson, doc.VehiclePlate, doc.Kilometers, doc.Mission,
        string.IsNullOrWhiteSpace(doc.FormNumber) ? doc.Number : doc.FormNumber, doc.Distance);

    private static DocumentDetail MapDetail(FuelDocument doc) => new(
        doc.Id, doc.Number, doc.Kind, Labels.Kind(doc.Kind), doc.Status, Labels.Status(doc.Status),
        doc.DocumentDate, doc.CreatedAt, doc.UpdatedAt,
        doc.ItemId, doc.ItemName, doc.GroupName, doc.UnitName, doc.QualityInfo, doc.Temperature, doc.MeasurementNote, doc.ConversionRule, doc.Vcf,
        doc.WarehouseId, doc.WarehouseName, doc.WarehouseTypeName,
        doc.DestinationWarehouseId, doc.DestinationWarehouseName,
        doc.ConsumerId, doc.ConsumerName, doc.ConsumerCode, doc.ConsumerTypeName, doc.Norm, doc.Distance, doc.OperatingQuantity, doc.ManualQuantity,
        doc.UnitPrice, doc.LotTypeId, doc.LotTypeCode ?? "", doc.InputQuantity, doc.ActualQuantity, doc.Amount, doc.WasSplit,
        doc.Lines.OrderBy(x => x.LineNo).Select(x => new LineRow(x.LineNo, x.LotId, x.WarehouseId, x.ItemName, x.UnitPrice, x.LotTypeId, x.LotTypeCode ?? "", x.DestinationLotTypeId, x.DestinationLotTypeCode ?? "", x.Quantity, x.ActualQuantity, x.Amount, x.ItemId, x.ItemCode, x.QualityGrade, x.Temperature, x.Density, x.Vcf)).ToList(),
        doc.Fields.OrderBy(x => x.Name).Select(x => new FieldSnapshotRow(x.Name, x.DataType, x.IsRequired, x.Value)).ToList(),
        new SlipInfo(doc.FormNumber, doc.OrganizationName, doc.UnitTitle, doc.SenderUnit, doc.ReceiverUnit, doc.Nature, doc.ContractOrOrder, doc.CarrierUnit, doc.PriceValidUntil, doc.DelivererName, doc.IntroDocument, doc.VehiclePlate, doc.CalibrationVolume, doc.ReceivedVolume, doc.PackageCount, doc.ReceiverPerson, doc.Kilometers, doc.Mission, doc.OriginPlace, doc.DestinationPlace, doc.Note, doc.SignerReceiver, doc.SignerDeliverer, doc.SignerFinance, doc.SignerWriter, doc.SignerChief, doc.SignerCommander, doc.AmountInWords, doc.MissionTaskId));

    private static string Whole(decimal value) =>
        decimal.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
}
