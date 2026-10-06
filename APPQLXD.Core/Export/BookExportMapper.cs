using System.Globalization;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Models;

namespace APPQLXD.Core.Export;

public static class BookExportMapper
{
    public static WordFillRequest MapConsumer(ConsumerTransferBook book, int year, int quarter)
    {
        decimal km = 0, hours = 0, norm = 0, actual = 0, over = 0, under = 0;
        decimal fuelIn = 0, fuelOut = 0, oilIn = 0, oilOut = 0;
        foreach (var r in book.Rows)
        {
            if (r.IsOpening) continue;
            km = QuantityMath.RoundQty(km + (r.Kilometers ?? 0));
            hours = QuantityMath.RoundQty(hours + (r.MachineHours ?? 0));
            norm = QuantityMath.Whole(norm + (r.NormQuantity ?? 0));
            actual = QuantityMath.Whole(actual + (r.ActualQuantity ?? 0));
            over = QuantityMath.Whole(over + (r.OverQuantity ?? 0));
            under = QuantityMath.Whole(under + (r.UnderQuantity ?? 0));
            fuelIn = QuantityMath.Whole(fuelIn + (r.FuelIn ?? 0));
            fuelOut = QuantityMath.Whole(fuelOut + (r.FuelOut ?? 0));
            oilIn = QuantityMath.Whole(oilIn + (r.OilIn ?? 0));
            oilOut = QuantityMath.Whole(oilOut + (r.OilOut ?? 0));
        }

        var opening = book.Rows.FirstOrDefault(r => r.IsOpening);
        var activity = book.Rows.Where(r => !r.IsOpening).ToList();
        // Không phát sinh trong quý: tồn mang sang = tồn quý trước.
        var closingFuel = activity.Count > 0 ? activity[^1].FuelBalance : opening?.FuelBalance;
        var closingOil = activity.Count > 0 ? activity[^1].OilBalance : opening?.OilBalance;
        var scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["$Nam"] = year.ToString(CultureInfo.InvariantCulture),
            ["$Quy"] = quarter.ToString(CultureInfo.InvariantCulture),
            ["$DT"] = book.ConsumerName,
            ["$BienSo"] = book.PlateNumber,
            ["$NL"] = book.FuelUsed,
            ["$MS"] = book.FormId,
            ["$DV"] = book.UnitNote,
            ["$TXang"] = ExportNumberFormat.Qty(book.FuelTransferTotal),
            ["$TDau"] = ExportNumberFormat.Qty(book.OilTransferTotal),
            // Dòng "Tồn quý trước chuyển sang" cố định trên mẫu Word
            ["$O_DGiai"] = string.IsNullOrWhiteSpace(opening?.Description)
                ? "Tồn quý trước chuyển sang"
                : opening!.Description,
            ["$O_XTon"] = ExportNumberFormat.Qty(opening?.FuelBalance),
            ["$O_DTon"] = ExportNumberFormat.Qty(opening?.OilBalance),
            ["$C_DGiai"] = "Cộng",
            ["$C_SoKm"] = ExportNumberFormat.Qty(km),
            ["$C_GMay"] = ExportNumberFormat.Qty(hours),
            ["$C_DM"] = ExportNumberFormat.Qty(norm),
            ["$C_TT"] = ExportNumberFormat.Qty(actual),
            ["$C_Vuot"] = ExportNumberFormat.Qty(over),
            ["$C_Thieu"] = ExportNumberFormat.Qty(under),
            ["$C_XNhap"] = ExportNumberFormat.Qty(fuelIn),
            ["$C_XXuat"] = ExportNumberFormat.Qty(fuelOut),
            ["$C_DNhap"] = ExportNumberFormat.Qty(oilIn),
            ["$C_DXuat"] = ExportNumberFormat.Qty(oilOut),
            // Dòng cuối "Tồn mang sang quý sau" / tồn cuối
            ["$T_DGiai"] = "Tồn mang sang quý sau",
            ["$T_XTon"] = ExportNumberFormat.Qty(closingFuel),
            ["$T_DTon"] = ExportNumberFormat.Qty(closingOil)
        };

        // Không đưa dòng tồn đầu vào bảng nhân bản — mẫu Word thường đã có hàng cố định.
        // Quý không phát sinh: không thêm dòng dữ liệu (engine bỏ hàng mẫu trống).
        var rows = new List<IReadOnlyDictionary<string, string>>();
        var i = 1;
        foreach (var r in activity)
        {
            rows.Add(MapConsumerRow(i.ToString(CultureInfo.InvariantCulture), r));
            i++;
        }

        // Dòng Cộng: engine chèn sau các dòng dữ liệu từ $C_* (giữ trong Rows để tương thích test/mapper).
        rows.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["$STT"] = "",
            ["$SoPhieu"] = "",
            ["$Ngay"] = "",
            ["$DGiai"] = "Cộng",
            ["$NoiDi"] = "",
            ["$NoiDen"] = "",
            ["$SoKm"] = scalars["$C_SoKm"],
            ["$GMay"] = scalars["$C_GMay"],
            ["$DM"] = scalars["$C_DM"],
            ["$TT"] = scalars["$C_TT"],
            ["$Vuot"] = scalars["$C_Vuot"],
            ["$Thieu"] = scalars["$C_Thieu"],
            ["$XNhap"] = scalars["$C_XNhap"],
            ["$XXuat"] = scalars["$C_XXuat"],
            ["$XTon"] = "",
            ["$DNhap"] = scalars["$C_DNhap"],
            ["$DXuat"] = scalars["$C_DXuat"],
            ["$DTon"] = "",
            ["$LLo"] = ""
        });

        return new WordFillRequest
        {
            Scalars = PlaceholderAliases.Expand(scalars),
            Rows = PlaceholderAliases.ExpandRows(rows),
            RowExpandRule = WordRowExpandRule.BySttOrSoPhieu
        };
    }

    public static WordFillRequest MapShip(ShipQuarterBookDto book)
    {
        decimal mainOps = 0, berth = 0, cx25 = 0, cx50 = 0, cx75 = 0, cx100 = 0, mainHours = 0;
        decimal auxOps = 0, auxHours = 0, gas = 0, diesel = 0;
        decimal fuelIn = 0, fuelOut = 0, oilIn = 0, oilOut = 0;

        foreach (var r in book.Rows)
        {
            if (r.IsOpening) continue;
            fuelIn = QuantityMath.Whole(fuelIn + (r.FuelIn ?? 0));
            oilIn = QuantityMath.RoundQty(oilIn + (r.OilIn ?? 0));
            if (r.IsTransfer) continue;
            mainOps = QuantityMath.RoundQty(mainOps + (r.MainOpsCount ?? 0));
            berth = QuantityMath.RoundQty(berth + (r.HoursAtBerth ?? 0));
            cx25 = QuantityMath.RoundQty(cx25 + (r.HoursCx25 ?? 0));
            cx50 = QuantityMath.RoundQty(cx50 + (r.HoursCx50 ?? 0));
            cx75 = QuantityMath.RoundQty(cx75 + (r.HoursCx75 ?? 0));
            cx100 = QuantityMath.RoundQty(cx100 + (r.HoursCx100 ?? 0));
            mainHours = QuantityMath.RoundQty(mainHours + (r.MainHoursTotal ?? 0));
            auxOps = QuantityMath.RoundQty(auxOps + (r.AuxOpsCount ?? 0));
            auxHours = QuantityMath.RoundQty(auxHours + (r.AuxHours ?? 0));
            fuelOut = QuantityMath.Whole(fuelOut + (r.FuelOut ?? 0));
            oilOut = QuantityMath.Whole(oilOut + (r.OilOut ?? 0));
            if (book.FuelIsGasoline)
                gas = QuantityMath.Whole(gas + (r.FuelOut ?? r.GasolineUse ?? 0));
            else
                diesel = QuantityMath.Whole(diesel + (r.FuelOut ?? r.DieselUse ?? 0));
        }

        var opening = book.Rows.FirstOrDefault(r => r.IsOpening);
        var activity = book.Rows.Where(r => !r.IsOpening).ToList();
        // Không phát sinh trong quý: tồn mang sang = tồn quý trước.
        var closingFuel = activity.Count > 0 ? activity[^1].FuelBalance : opening?.FuelBalance;
        var closingOil = activity.Count > 0 ? activity[^1].OilBalance : opening?.OilBalance;
        var scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["$Nam"] = book.Year.ToString(CultureInfo.InvariantCulture),
            ["$Quy"] = book.Quarter.ToString(CultureInfo.InvariantCulture),
            ["$DT"] = book.ShipName,
            ["$LTau"] = book.ShipType,
            ["$NL"] = book.FuelUsed,
            ["$MS"] = book.FormId,
            ["$DV"] = book.UnitNote,
            // Dòng "Tồn quý trước chuyển sang" cố định trên mẫu Word
            ["$O_DGiai"] = string.IsNullOrWhiteSpace(opening?.Description)
                ? "Tồn quý trước chuyển sang"
                : opening!.Description,
            ["$O_XTon"] = ExportNumberFormat.Qty(opening?.FuelBalance),
            ["$O_DTon"] = ExportNumberFormat.Qty(opening?.OilBalance),
            ["$C_DGiai"] = "Cộng",
            ["$C_MChinh"] = "",
            ["$C_GNeo"] = ShipHourFormat.Format(berth),
            ["$C_Cx25"] = ShipHourFormat.Format(cx25),
            ["$C_Cx50"] = ShipHourFormat.Format(cx50),
            ["$C_Cx75"] = ShipHourFormat.Format(cx75),
            ["$C_Cx100"] = ShipHourFormat.Format(cx100),
            ["$C_TongGio"] = ShipHourFormat.Format(mainHours),
            ["$C_MPhu"] = "",
            ["$C_GPhu"] = ShipHourFormat.Format(auxHours),
            ["$C_XDung"] = ExportNumberFormat.Qty(gas),
            ["$C_DDung"] = ExportNumberFormat.Qty(diesel),
            ["$C_XNhap"] = ExportNumberFormat.Qty(fuelIn),
            ["$C_XXuat"] = ExportNumberFormat.Qty(fuelOut),
            ["$C_DNhap"] = ExportNumberFormat.Qty(oilIn),
            ["$C_DXuat"] = ExportNumberFormat.Qty(oilOut),
            // Dòng cuối "Tồn mang sang quý sau" / tồn cuối
            ["$T_DGiai"] = "Tồn mang sang quý sau",
            ["$T_XTon"] = ExportNumberFormat.Qty(closingFuel),
            ["$T_DTon"] = ExportNumberFormat.Qty(closingOil)
        };

        // Quý không phát sinh: không thêm dòng dữ liệu (engine bỏ hàng mẫu trống).
        var rows = new List<IReadOnlyDictionary<string, string>>();
        var i = 1;
        foreach (var r in activity)
        {
            rows.Add(MapShipRow(i.ToString(CultureInfo.InvariantCulture), r));
            i++;
        }

        rows.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["$STT"] = "",
            ["$SoPhieu"] = "",
            ["$Ngay"] = "",
            ["$DGiai"] = "Cộng",
            ["$MChinh"] = scalars["$C_MChinh"],
            ["$GNeo"] = scalars["$C_GNeo"],
            ["$Cx25"] = scalars["$C_Cx25"],
            ["$Cx50"] = scalars["$C_Cx50"],
            ["$Cx75"] = scalars["$C_Cx75"],
            ["$Cx100"] = scalars["$C_Cx100"],
            ["$TongGio"] = scalars["$C_TongGio"],
            ["$MPhu"] = scalars["$C_MPhu"],
            ["$GPhu"] = scalars["$C_GPhu"],
            ["$XDung"] = scalars["$C_XDung"],
            ["$DDung"] = scalars["$C_DDung"],
            ["$XNhap"] = scalars["$C_XNhap"],
            ["$XXuat"] = scalars["$C_XXuat"],
            ["$XTon"] = "",
            ["$DNhap"] = scalars["$C_DNhap"],
            ["$DXuat"] = scalars["$C_DXuat"],
            ["$DTon"] = "",
            ["$TDong"] = "",
            ["$LLo"] = ""
        });

        return new WordFillRequest
        {
            Scalars = PlaceholderAliases.Expand(scalars),
            Rows = PlaceholderAliases.ExpandRows(rows),
            RowExpandRule = WordRowExpandRule.BySttOrSoPhieu
        };
    }

    private static Dictionary<string, string> MapConsumerRow(string stt, ConsumerTransferBookRow r) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["$STT"] = stt,
            ["$SoPhieu"] = r.DocumentNumber,
            ["$Ngay"] = r.DocumentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
            ["$DGiai"] = r.Description,
            ["$NoiDi"] = r.Origin,
            ["$NoiDen"] = r.Destination,
            ["$SoKm"] = ExportNumberFormat.Qty(r.Kilometers),
            ["$GMay"] = ExportNumberFormat.Qty(r.MachineHours),
            ["$DM"] = ExportNumberFormat.Qty(r.NormQuantity),
            ["$TT"] = ExportNumberFormat.Qty(r.ActualQuantity),
            ["$Vuot"] = ExportNumberFormat.Qty(r.OverQuantity),
            ["$Thieu"] = ExportNumberFormat.Qty(r.UnderQuantity),
            ["$XNhap"] = ExportNumberFormat.Qty(r.FuelIn),
            ["$XXuat"] = ExportNumberFormat.Qty(r.FuelOut),
            ["$XTon"] = ExportNumberFormat.Qty(r.FuelBalance),
            ["$DNhap"] = ExportNumberFormat.Qty(r.OilIn),
            ["$DXuat"] = ExportNumberFormat.Qty(r.OilOut),
            ["$DTon"] = ExportNumberFormat.Qty(r.OilBalance),
            ["$LLo"] = r.LotTypeCode
        };

    private static Dictionary<string, string> MapShipRow(string stt, ShipQuarterBookRowDto r) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["$STT"] = stt,
            ["$SoPhieu"] = r.DocumentNumber,
            ["$Ngay"] = r.DocumentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
            ["$DGiai"] = r.Description,
            ["$MChinh"] = ExportNumberFormat.Qty(r.MainOpsCount),
            ["$GNeo"] = ShipHourFormat.Format(r.HoursAtBerth),
            ["$Cx25"] = ShipHourFormat.Format(r.HoursCx25),
            ["$Cx50"] = ShipHourFormat.Format(r.HoursCx50),
            ["$Cx75"] = ShipHourFormat.Format(r.HoursCx75),
            ["$Cx100"] = ShipHourFormat.Format(r.HoursCx100),
            ["$TongGio"] = ShipHourFormat.Format(r.MainHoursTotal),
            ["$MPhu"] = ExportNumberFormat.Qty(r.AuxOpsCount),
            ["$GPhu"] = ShipHourFormat.Format(r.AuxHours),
            ["$XDung"] = ExportNumberFormat.Qty(r.GasolineUse),
            ["$DDung"] = ExportNumberFormat.Qty(r.DieselUse),
            ["$XNhap"] = ExportNumberFormat.Qty(r.FuelIn),
            ["$XXuat"] = ExportNumberFormat.Qty(r.FuelOut),
            ["$XTon"] = ExportNumberFormat.Qty(r.FuelBalance),
            ["$DNhap"] = ExportNumberFormat.Qty(r.OilIn),
            ["$DXuat"] = ExportNumberFormat.Qty(r.OilOut),
            ["$DTon"] = ExportNumberFormat.Qty(r.OilBalance),
            ["$TDong"] = ExportNumberFormat.Qty(r.RowTotal),
            ["$LLo"] = r.LotTypeCode
        };
}
