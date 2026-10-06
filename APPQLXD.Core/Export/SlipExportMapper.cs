using System.Globalization;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;

namespace APPQLXD.Core.Export;

public static class SlipExportMapper
{
    public static WordFillRequest Map(DocumentDetail doc, PlaceholderRegistry? registry = null)
    {
        registry ??= new PlaceholderRegistry();
        var slip = doc.Slip;
        var scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["$SoPhieu"] = FirstNonEmpty(slip.FormNumber, doc.Number),
            ["$Ngay"] = doc.DocumentDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["$CQ"] = slip.OrganizationName,
            ["$DV"] = slip.UnitTitle,
            ["$DVGiao"] = FirstNonEmpty(slip.SenderUnit, doc.WarehouseName),
            ["$DVNhan"] = FirstNonEmpty(slip.ReceiverUnit, doc.DestinationWarehouseName, doc.ConsumerName),
            ["$TC"] = slip.Nature,
            ["$HD"] = slip.ContractOrOrder,
            ["$DVVC"] = slip.CarrierUnit,
            ["$GiaDen"] = slip.PriceValidUntil,
            ["$NGiao"] = slip.DelivererName,
            ["$NNhan"] = slip.ReceiverPerson,
            ["$GiayGT"] = slip.IntroDocument,
            ["$SoXe"] = slip.VehiclePlate,
            ["$SoKm"] = FirstNonEmpty(slip.Kilometers, ExportNumberFormat.Qty(doc.Distance)),
            ["$DtKd"] = slip.CalibrationVolume,
            ["$DtNhan"] = slip.ReceivedVolume,
            ["$BaoBi"] = slip.PackageCount,
            ["$NV"] = slip.Mission,
            ["$NoiDi"] = slip.OriginPlace,
            ["$NoiDen"] = slip.DestinationPlace,
            ["$GC"] = slip.Note,
            ["$Kho"] = doc.WarehouseName,
            ["$KDen"] = doc.DestinationWarehouseName,
            ["$DT"] = doc.ConsumerName,
            ["$SKhoan"] = doc.Lines.Count.ToString(CultureInfo.InvariantCulture),
            ["$TongSL"] = ExportNumberFormat.Qty(doc.Lines.Sum(x => x.Quantity)),
            ["$TongThuc"] = ExportNumberFormat.Qty(doc.Lines.Sum(x => x.ActualQuantity)),
            ["$TongTien"] = ExportNumberFormat.Money(doc.Lines.Sum(x => x.Amount)),
            ["$BC"] = slip.AmountInWords,
            ["$CK_NG"] = slip.SignerDeliverer,
            ["$CK_NN"] = slip.SignerReceiver,
            ["$CK_TC"] = slip.SignerFinance,
            ["$CK_NV"] = slip.SignerWriter,
            ["$CK_TB"] = slip.SignerChief,
            ["$CK_CH"] = slip.SignerCommander
        };

        foreach (var field in doc.Fields)
        {
            var token = "$F_" + PlaceholderSlug.FromVietnamese(field.Name, 12);
            if (!scalars.ContainsKey(token))
                scalars[token] = field.Value ?? "";
            MapKnownFieldAlias(scalars, field.Name, field.Value);
        }

        var rows = new List<IReadOnlyDictionary<string, string>>();
        var i = 1;
        foreach (var line in doc.Lines)
        {
            rows.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["$STT"] = i.ToString(CultureInfo.InvariantCulture),
                ["$Ten"] = line.ItemName,
                ["$MaSo"] = line.ItemCode,
                ["$CL"] = line.QualityGrade,
                ["$SL"] = ExportNumberFormat.Qty(line.Quantity),
                ["$Nhiet"] = ExportNumberFormat.Decimal(line.Temperature),
                ["$TD"] = ExportNumberFormat.Factor(line.Density),
                ["$VCF"] = ExportNumberFormat.Factor(line.Vcf),
                ["$Thuc"] = ExportNumberFormat.Qty(line.ActualQuantity),
                ["$Gia"] = ExportNumberFormat.Money(line.UnitPrice),
                ["$TTien"] = ExportNumberFormat.Money(line.Amount),
                ["$LLo"] = line.LotTypeCode
            });
            i++;
        }

        _ = registry;
        return new WordFillRequest
        {
            Scalars = PlaceholderAliases.Expand(scalars),
            Rows = PlaceholderAliases.ExpandRows(rows),
            RowExpandRule = WordRowExpandRule.BySttOnly
        };
    }

    public static string TemplateFileName(DocumentDetail doc) =>
        doc.Kind == DocumentKind.Import ? TemplateNames.PhieuNhap : TemplateNames.PhieuXuat;

    private static void MapKnownFieldAlias(Dictionary<string, string> scalars, string name, string? value)
    {
        var v = value ?? "";
        switch (name)
        {
            case "Cơ quan" when string.IsNullOrWhiteSpace(scalars.GetValueOrDefault("$CQ")): scalars["$CQ"] = v; break;
            case "Đơn vị" when string.IsNullOrWhiteSpace(scalars.GetValueOrDefault("$DV")): scalars["$DV"] = v; break;
            case "Ghi chú" when string.IsNullOrWhiteSpace(scalars.GetValueOrDefault("$GC")): scalars["$GC"] = v; break;
            case "Nhiệm vụ" when string.IsNullOrWhiteSpace(scalars.GetValueOrDefault("$NV")): scalars["$NV"] = v; break;
            case "Số xe" when string.IsNullOrWhiteSpace(scalars.GetValueOrDefault("$SoXe")): scalars["$SoXe"] = v; break;
            case "Số km" when string.IsNullOrWhiteSpace(scalars.GetValueOrDefault("$SoKm")): scalars["$SoKm"] = v; break;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v!;
        return "";
    }
}
