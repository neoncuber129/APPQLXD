using System.Globalization;
using System.Text;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace APPQLXD.Core.Export;

public static class NxtExportMapper
{
    public static WordFillRequest MapNxt(NxtSheet sheet, int year, int quarter, string warehouseLabel)
    {
        var scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["$Nam"] = year.ToString(CultureInfo.InvariantCulture),
            ["$Quy"] = quarter.ToString(CultureInfo.InvariantCulture),
            ["$Kho"] = warehouseLabel
        };

        // Flatten: one Word row per NXT row; cells joined for multi-column sheets.
        var rows = new List<IReadOnlyDictionary<string, string>>();
        var i = 1;
        foreach (var r in sheet.Rows)
        {
            var nhap = JoinCells(r.Cells, c => c.In);
            var xuat = JoinCells(r.Cells, c => c.Out);
            var ton = JoinCells(r.Cells, c => c.Balance);
            rows.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["$STT"] = i.ToString(CultureInfo.InvariantCulture),
                ["$SoPhieu"] = r.Number,
                ["$Ngay"] = r.Date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
                ["$DGiai"] = r.Description,
                ["$SoKm"] = ExportNumberFormat.Qty(r.Kilometers),
                ["$NV"] = r.Mission,
                ["$Nhap"] = nhap,
                ["$Xuat"] = xuat,
                ["$Ton"] = ton
            });
            i++;
        }

        return new WordFillRequest
        {
            Scalars = PlaceholderAliases.Expand(scalars),
            Rows = PlaceholderAliases.ExpandRows(rows),
            RowExpandRule = WordRowExpandRule.BySttOnly
        };
    }

    public static WordFillRequest MapNxtTotal(NxtTotalSheet sheet, int year, int quarter, string warehouseLabel)
    {
        var scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["$Nam"] = year.ToString(CultureInfo.InvariantCulture),
            ["$Quy"] = quarter.ToString(CultureInfo.InvariantCulture),
            ["$Kho"] = warehouseLabel
        };

        var rows = new List<IReadOnlyDictionary<string, string>>();
        var i = 1;
        foreach (var r in sheet.Rows)
        {
            rows.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["$STT"] = i.ToString(CultureInfo.InvariantCulture),
                ["$Nhom"] = r.GroupName,
                ["$Ten"] = r.IsGroupTotal ? $"Tổng {r.GroupName}" : r.ItemName,
                ["$Gia"] = r.IsGroupTotal ? "" : ExportNumberFormat.Money(r.UnitPrice),
                ["$LLo"] = r.LotTypeCode,

                ["$TonDau_Kho"] = ExportNumberFormat.Qty(r.OpeningMain),
                ["$TonDau_May"] = ExportNumberFormat.Qty(r.OpeningMachine),
                ["$TonDau_Xe"] = ExportNumberFormat.Qty(r.OpeningVehicle),
                ["$TonDau_Tau"] = ExportNumberFormat.Qty(r.OpeningShip),
                ["$TonDau"] = ExportNumberFormat.Qty(r.OpeningTotal),

                ["$Nhap_Kho"] = ExportNumberFormat.Qty(r.InMain),
                ["$Nhap_May"] = ExportNumberFormat.Qty(r.InMachine),
                ["$Nhap_Xe"] = ExportNumberFormat.Qty(r.InVehicle),
                ["$Nhap_Tau"] = ExportNumberFormat.Qty(r.InShip),
                ["$Nhap"] = ExportNumberFormat.Qty(r.InTotal),

                ["$Xuat_Kho"] = ExportNumberFormat.Qty(r.OutMain),
                ["$Xuat_May"] = ExportNumberFormat.Qty(r.OutMachine),
                ["$Xuat_Xe"] = ExportNumberFormat.Qty(r.OutVehicle),
                ["$Xuat_Tau"] = ExportNumberFormat.Qty(r.OutShip),
                ["$Xuat"] = ExportNumberFormat.Qty(r.OutTotal),

                ["$TCuoi_Kho"] = ExportNumberFormat.Qty(r.ClosingMain),
                ["$TCuoi_May"] = ExportNumberFormat.Qty(r.ClosingMachine),
                ["$TCuoi_Xe"] = ExportNumberFormat.Qty(r.ClosingVehicle),
                ["$TCuoi_Tau"] = ExportNumberFormat.Qty(r.ClosingShip),
                ["$TCuoi"] = ExportNumberFormat.Qty(r.ClosingTotal),

                ["$TonCuoi_Kho"] = ExportNumberFormat.Qty(r.ClosingMain),
                ["$TonCuoi_May"] = ExportNumberFormat.Qty(r.ClosingMachine),
                ["$TonCuoi_Xe"] = ExportNumberFormat.Qty(r.ClosingVehicle),
                ["$TonCuoi_Tau"] = ExportNumberFormat.Qty(r.ClosingShip),
                ["$TonCuoi"] = ExportNumberFormat.Qty(r.ClosingTotal),

                ["$GC"] = r.Note
            });
            i++;
        }

        return new WordFillRequest
        {
            Scalars = PlaceholderAliases.Expand(scalars),
            Rows = PlaceholderAliases.ExpandRows(rows),
            RowExpandRule = WordRowExpandRule.BySttOnly
        };
    }

    private static string JoinCells(IReadOnlyList<NxtCell> cells, Func<NxtCell, decimal?> pick)
    {
        if (cells.Count == 0) return "";
        if (cells.Count == 1) return ExportNumberFormat.Qty(pick(cells[0]));
        var sb = new StringBuilder();
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0) sb.Append(" | ");
            sb.Append(ExportNumberFormat.Qty(pick(cells[i])));
        }
        return sb.ToString();
    }
}

/// <summary>Xuất Excel khớp bố cục / định dạng đang hiển thị trên sổ NXT và NXT tổng.</summary>
public static class NxtExcelWriter
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    // Palette khớp NxtTotalRowVm trên UI
    private static readonly (string Header, string Sub, string Row)[] Palette =
    [
        ("2E5A88", "4A7AA8", "E8F0F8"),
        ("8A5A12", "B07A2E", "FFF6E8"),
        ("2F6B4F", "4A8B6A", "E8F5EE"),
        ("6B3A6B", "8B5A8B", "F5EAF5"),
        ("4A6678", "6A86A0", "EEF2F5"),
        ("8B3A2F", "AB5A4F", "F8EBE8"),
    ];

    public static byte[] WriteNxt(NxtSheet sheet, int year, int quarter, string warehouseLabel)
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook, true))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();
            var styles = wbPart.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = BuildNxtStylesheet(sheet.Columns.Count);
            styles.Stylesheet.Save();

            var wsPart = wbPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var merges = new MergeCells();
            var colCount = 7 + sheet.Columns.Count * 3;

            // Row 1 title
            sheetData.AppendChild(StyledRow(1, StyleTitle, Abs(0, colCount - 1, $"NXT năm {year} quý {quarter} — {warehouseLabel}")));
            merges.AppendChild(new MergeCell { Reference = $"A1:{Col(colCount - 1)}1" });

            // Row 2–3 headers giống UI: Chứng từ | N XX XD Ngày | Diễn giải | Số km | Nhiệm vụ | (fuel × Nhập/Xuất/Tồn)
            var top = new List<(int Col, string Text, uint Style)>();
            top.Add((0, "Chứng từ", StyleHead));
            top.Add((4, "Diễn giải", StyleHead));
            top.Add((5, "Số km", StyleHead));
            top.Add((6, "Nhiệm vụ", StyleHead));
            for (var i = 0; i < sheet.Columns.Count; i++)
            {
                var style = sheet.Columns[i].IsGroupTotal ? StyleGroupHead : StyleHead;
                top.Add((7 + i * 3, sheet.Columns[i].Title, style));
            }
            sheetData.AppendChild(StyledRow(2, StyleHead, FillSparse(colCount, top)));

            var sub = new (int Col, string Text, uint Style)[]
            {
                (0, "N", StyleSubHead), (1, "XX", StyleSubHead), (2, "XD", StyleSubHead), (3, "Ngày", StyleSubHead)
            };
            var subList = sub.ToList();
            for (var i = 0; i < sheet.Columns.Count; i++)
            {
                var style = sheet.Columns[i].IsGroupTotal ? StyleGroupSub : StyleSubHead;
                var baseCol = 7 + i * 3;
                subList.Add((baseCol, "Nhập", style));
                subList.Add((baseCol + 1, "Xuất", style));
                subList.Add((baseCol + 2, "Tồn", style));
            }
            sheetData.AppendChild(StyledRow(3, StyleSubHead, FillSparse(colCount, subList)));

            merges.AppendChild(new MergeCell { Reference = "A2:D2" });
            merges.AppendChild(new MergeCell { Reference = "E2:E3" });
            merges.AppendChild(new MergeCell { Reference = "F2:F3" });
            merges.AppendChild(new MergeCell { Reference = "G2:G3" });
            for (var i = 0; i < sheet.Columns.Count; i++)
            {
                var a = Col(7 + i * 3);
                var b = Col(7 + i * 3 + 2);
                merges.AppendChild(new MergeCell { Reference = $"{a}2:{b}2" });
            }

            uint rowIndex = 4;
            foreach (var r in sheet.Rows)
            {
                var summary = r.Kind != NxtRowKind.Slip;
                var number = r.Kind == NxtRowKind.Slip ? r.Number : "";
                var values = new string[colCount];
                values[0] = r.VoucherColumn == NxtVoucherColumn.N ? number : "";
                values[1] = r.VoucherColumn == NxtVoucherColumn.Xx ? number : "";
                values[2] = r.VoucherColumn == NxtVoucherColumn.Xd ? number : "";
                values[3] = r.Date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
                values[4] = r.Description ?? "";
                values[5] = Qty(r.Kilometers);
                values[6] = r.Mission ?? "";
                for (var i = 0; i < sheet.Columns.Count; i++)
                {
                    var cell = i < r.Cells.Count ? r.Cells[i] : new NxtCell(null, null, null);
                    values[7 + i * 3] = Qty(cell.In);
                    values[7 + i * 3 + 1] = Qty(cell.Out);
                    values[7 + i * 3 + 2] = Qty(cell.Balance);
                }

                var style = summary ? StyleSummary : StyleBody;
                sheetData.AppendChild(StyledRow(rowIndex, style, values.Select((t, i) =>
                {
                    var colStyle = style;
                    if (i >= 7)
                    {
                        var fuel = (i - 7) / 3;
                        if (fuel < sheet.Columns.Count && sheet.Columns[fuel].IsGroupTotal)
                            colStyle = StyleGroupBody;
                    }
                    return (t, colStyle);
                }).ToArray()));
                rowIndex++;
            }

            var worksheet = new Worksheet();
            worksheet.AppendChild(ColumnsFrom(colCount, sheet));
            worksheet.AppendChild(sheetData);
            if (merges.ChildElements.Count > 0)
            {
                merges.Count = (uint)merges.ChildElements.Count;
                worksheet.AppendChild(merges);
            }
            wsPart.Worksheet = worksheet;

            wbPart.Workbook.AppendChild(new Sheets(
                new Sheet { Id = wbPart.GetIdOfPart(wsPart), SheetId = 1, Name = "NXT" }));
            wbPart.Workbook.Save();
        }

        return ms.ToArray();
    }

    public static byte[] WriteNxtTotal(NxtTotalSheet sheet, int year, int quarter, string warehouseLabel)
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook, true))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();
            var styles = wbPart.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = BuildTotalStylesheet();
            styles.Stylesheet.Save();

            var wsPart = wbPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            sheetData.AppendChild(StyledRow(1, StyleTitle, Abs(0, 23, $"NXT tổng năm {year} quý {quarter} — {warehouseLabel}")));
            var merges = new MergeCells();
            merges.AppendChild(new MergeCell { Reference = "A1:X1" });
            merges.AppendChild(new MergeCell { Reference = "A2:A3" });
            merges.AppendChild(new MergeCell { Reference = "B2:B3" });
            merges.AppendChild(new MergeCell { Reference = "C2:C3" });
            merges.AppendChild(new MergeCell { Reference = "D2:H2" });
            merges.AppendChild(new MergeCell { Reference = "I2:M2" });
            merges.AppendChild(new MergeCell { Reference = "N2:R2" });
            merges.AppendChild(new MergeCell { Reference = "S2:W2" });
            merges.AppendChild(new MergeCell { Reference = "X2:X3" });

            // Hàng 2: Tiêu đề tầng 1
            sheetData.AppendChild(StyledRow(2, StyleHead,
                ("Lô", StyleHead), ("Đơn giá", StyleHead), ("Loại lô", StyleHead),
                ("Tồn đầu", StyleHead), ("", StyleHead), ("", StyleHead), ("", StyleHead), ("", StyleHead),
                ("Nhập", StyleHead), ("", StyleHead), ("", StyleHead), ("", StyleHead), ("", StyleHead),
                ("Xuất", StyleHead), ("", StyleHead), ("", StyleHead), ("", StyleHead), ("", StyleHead),
                ("Tồn sau", StyleHead), ("", StyleHead), ("", StyleHead), ("", StyleHead), ("", StyleHead),
                ("Ghi chú", StyleHead)));

            // Hàng 3: Tiêu đề tầng 2
            sheetData.AppendChild(StyledRow(3, StyleHead,
                ("", StyleHead), ("", StyleHead), ("", StyleHead),
                ("Kho HĐ", StyleHead), ("Máy", StyleHead), ("PT", StyleHead), ("Tàu", StyleHead), ("Tổng", StyleHead),
                ("Kho HĐ", StyleHead), ("Máy", StyleHead), ("PT", StyleHead), ("Tàu", StyleHead), ("Tổng", StyleHead),
                ("Kho HĐ", StyleHead), ("Máy", StyleHead), ("PT", StyleHead), ("Tàu", StyleHead), ("Tổng", StyleHead),
                ("Kho HĐ", StyleHead), ("Máy", StyleHead), ("PT", StyleHead), ("Tàu", StyleHead), ("Tổng", StyleHead),
                ("", StyleHead)));

            uint rowIndex = 4;
            foreach (var group in sheet.Rows.GroupBy(x => x.GroupName, StringComparer.Ordinal))
            {
                var members = group.ToList();
                if (members.Count == 0) continue;
                var color = ColorIndex(GroupRank(group.Key));
                var label = string.IsNullOrWhiteSpace(group.Key) ? "Khác" : group.Key.Trim();

                sheetData.AppendChild(StyledRow(rowIndex++, TotalStyle(color, header: true),
                    (label, TotalStyle(color, header: true)),
                    ("", TotalStyle(color, header: true)),
                    ("", TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.OpeningMain)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.OpeningMachine)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.OpeningVehicle)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.OpeningShip)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.OpeningTotal)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.InMain)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.InMachine)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.InVehicle)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.InShip)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.InTotal)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.OutMain)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.OutMachine)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.OutVehicle)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.OutShip)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.OutTotal)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.ClosingMain)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.ClosingMachine)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.ClosingVehicle)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.ClosingShip)), TotalStyle(color, header: true)),
                    (Qty(members.Sum(x => x.ClosingTotal)), TotalStyle(color, header: true)),
                    ("", TotalStyle(color, header: true))));

                foreach (var typeGroup in members
                             .GroupBy(x => string.IsNullOrWhiteSpace(x.LotTypeCode) ? "TX" : x.LotTypeCode)
                             .OrderBy(g => LotTypeRank(g.Key))
                             .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
                {
                    var typeMembers = typeGroup.ToList();
                    sheetData.AppendChild(StyledRow(rowIndex++, TotalStyle(color, sub: true),
                        ($"  {typeGroup.Key}", TotalStyle(color, sub: true)),
                        ("", TotalStyle(color, sub: true)),
                        (typeGroup.Key, TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.OpeningMain)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.OpeningMachine)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.OpeningVehicle)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.OpeningShip)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.OpeningTotal)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.InMain)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.InMachine)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.InVehicle)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.InShip)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.InTotal)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.OutMain)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.OutMachine)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.OutVehicle)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.OutShip)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.OutTotal)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.ClosingMain)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.ClosingMachine)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.ClosingVehicle)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.ClosingShip)), TotalStyle(color, sub: true)),
                        (Qty(typeMembers.Sum(x => x.ClosingTotal)), TotalStyle(color, sub: true)),
                        ("", TotalStyle(color, sub: true))));

                    foreach (var r in typeMembers)
                    {
                        sheetData.AppendChild(StyledRow(rowIndex++, TotalStyle(color, item: true),
                            (r.ItemName, TotalStyle(color, item: true)),
                            (r.UnitPrice > 0 ? r.UnitPrice.ToString("N0", Vi) : "", TotalStyle(color, item: true)),
                            (r.LotTypeCode, TotalStyle(color, item: true)),
                            (Qty(r.OpeningMain), TotalStyle(color, item: true)),
                            (Qty(r.OpeningMachine), TotalStyle(color, item: true)),
                            (Qty(r.OpeningVehicle), TotalStyle(color, item: true)),
                            (Qty(r.OpeningShip), TotalStyle(color, item: true)),
                            (Qty(r.OpeningTotal), TotalStyle(color, item: true)),
                            (Qty(r.InMain), TotalStyle(color, item: true)),
                            (Qty(r.InMachine), TotalStyle(color, item: true)),
                            (Qty(r.InVehicle), TotalStyle(color, item: true)),
                            (Qty(r.InShip), TotalStyle(color, item: true)),
                            (Qty(r.InTotal), TotalStyle(color, item: true)),
                            (Qty(r.OutMain), TotalStyle(color, item: true)),
                            (Qty(r.OutMachine), TotalStyle(color, item: true)),
                            (Qty(r.OutVehicle), TotalStyle(color, item: true)),
                            (Qty(r.OutShip), TotalStyle(color, item: true)),
                            (Qty(r.OutTotal), TotalStyle(color, item: true)),
                            (Qty(r.ClosingMain), TotalStyle(color, item: true)),
                            (Qty(r.ClosingMachine), TotalStyle(color, item: true)),
                            (Qty(r.ClosingVehicle), TotalStyle(color, item: true)),
                            (Qty(r.ClosingShip), TotalStyle(color, item: true)),
                            (Qty(r.ClosingTotal), TotalStyle(color, item: true)),
                            (r.Note ?? "", TotalStyle(color, item: true))));
                    }
                }
            }

            var worksheet = new Worksheet();
            var colsList = new List<OpenXmlElement>
            {
                ColWidth(1, 26),
                ColWidth(2, 12),
                ColWidth(3, 10)
            };
            for (uint col = 4; col <= 23; col++)
                colsList.Add(ColWidth(col, 11));
            colsList.Add(ColWidth(24, 20));

            worksheet.AppendChild(new Columns(colsList));
            worksheet.AppendChild(sheetData);
            merges.Count = (uint)merges.ChildElements.Count;
            worksheet.AppendChild(merges);
            wsPart.Worksheet = worksheet;

            wbPart.Workbook.AppendChild(new Sheets(
                new Sheet { Id = wbPart.GetIdOfPart(wsPart), SheetId = 1, Name = "NXT tổng" }));
            wbPart.Workbook.Save();
        }

        return ms.ToArray();
    }

    private const uint StyleTitle = 1;
    private const uint StyleHead = 2;
    private const uint StyleSubHead = 3;
    private const uint StyleBody = 4;
    private const uint StyleSummary = 5;
    private const uint StyleGroupHead = 6;
    private const uint StyleGroupSub = 7;
    private const uint StyleGroupBody = 8;
    // Total styles after title(1)+colHead(2): 3 + color*3 + (0 header / 1 sub / 2 item)
    private static uint TotalStyle(int color, bool header = false, bool sub = false, bool item = false)
    {
        _ = item;
        var tone = (uint)(3 + color * 3);
        if (header) return tone;
        if (sub) return tone + 1;
        return tone + 2;
    }

    private static Columns ColumnsFrom(int colCount, NxtSheet sheet)
    {
        var cols = new Columns();
        double[] left = [12, 8, 8, 12, 28, 10, 20];
        for (uint i = 0; i < left.Length; i++)
            cols.AppendChild(ColWidth(i + 1, left[i]));
        for (uint i = 7; i < colCount; i++)
            cols.AppendChild(ColWidth(i + 1, 11));
        return cols;
    }

    private static Column ColWidth(uint index, double width) =>
        new() { Min = index, Max = index, Width = width, CustomWidth = true };

    private static (string Text, uint Style)[] Abs(int from, int to, string text)
    {
        var arr = new (string, uint)[to + 1];
        for (var i = 0; i <= to; i++)
            arr[i] = (i == from ? text : "", StyleTitle);
        return arr;
    }

    private static (string Text, uint Style)[] FillSparse(int colCount, IEnumerable<(int Col, string Text, uint Style)> cells)
    {
        var arr = new (string, uint)[colCount];
        for (var i = 0; i < colCount; i++)
            arr[i] = ("", StyleHead);
        foreach (var (col, text, style) in cells)
            if (col >= 0 && col < colCount)
                arr[col] = (text, style);
        return arr;
    }

    private static Row StyledRow(uint index, uint defaultStyle, params (string Text, uint Style)[] values)
    {
        var row = new Row { RowIndex = index };
        for (var i = 0; i < values.Length; i++)
        {
            var (text, style) = values[i];
            row.AppendChild(new Cell
            {
                DataType = CellValues.InlineString,
                StyleIndex = style == 0 ? defaultStyle : style,
                InlineString = new InlineString(new Text { Text = text ?? "" }),
                CellReference = Col(i) + index
            });
        }
        return row;
    }

    private static string Col(int zeroBased)
    {
        var n = zeroBased;
        var sb = new StringBuilder();
        do
        {
            sb.Insert(0, (char)('A' + n % 26));
            n = n / 26 - 1;
        } while (n >= 0);
        return sb.ToString();
    }

    private static string Qty(decimal? value) =>
        value is decimal n ? QuantityMath.Whole(n).ToString("N0", Vi) : "";

    private static string Qty(decimal value) =>
        QuantityMath.Whole(value).ToString("N0", Vi);

    private static int GroupRank(string? name) => name switch
    {
        "Dầu" => 0,
        "Xăng" => 1,
        "Nhớt" => 2,
        "Mỡ" => 3,
        _ => 4
    };

    private static int LotTypeRank(string code) => code.Trim().ToUpperInvariant() switch
    {
        "TX" => 0,
        "SSCĐ" or "SSCD" => 1,
        "IUU" => 2,
        _ => 10
    };

    private static int ColorIndex(int rank)
    {
        if (rank is >= 0 and <= 3) return rank;
        return 4 + Math.Abs(rank) % (Palette.Length - 4);
    }

    private static Stylesheet BuildNxtStylesheet(int fuelColumns)
    {
        _ = fuelColumns;
        var fonts = new Fonts(
            new Font(), // 0 default
            new Font(new Bold(), new FontSize { Val = 14 }), // 1 title
            new Font(new Bold(), new FontSize { Val = 11 }), // 2 head
            new Font(new Bold(), new FontSize { Val = 10 }), // 3 sub
            new Font(new FontSize { Val = 10 }), // 4 body
            new Font(new Bold(), new FontSize { Val = 10 }), // 5 summary
            new Font(new Bold(), new Color { Rgb = "FF1B2836" }, new FontSize { Val = 11 }), // 6 group head
            new Font(new Bold(), new FontSize { Val = 10 }), // 7 group sub
            new Font(new Bold(), new FontSize { Val = 10 })); // 8 group body
        fonts.Count = (uint)fonts.ChildElements.Count;

        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            Solid("FFE8EEF5"), // 2 head
            Solid("FFF3F6FA"), // 3 sub
            Solid("FFFFFFFF"), // 4 body
            Solid("FFF8FAFC"), // 5 summary
            Solid("FFF5E6C8"), // 6 group head gold-ish
            Solid("FFFAF0DC"), // 7 group sub
            Solid("FFFFF8EE")); // 8 group body
        fills.Count = (uint)fills.ChildElements.Count;

        var borders = new Borders(
            new Border(),
            ThinBorder());
        borders.Count = (uint)borders.ChildElements.Count;

        var cellFormats = new CellFormats(
            new CellFormat(), // 0
            Cf(1, 0, 0, center: true), // 1 title
            Cf(2, 2, 1, center: true), // 2 head
            Cf(3, 3, 1, center: true), // 3 sub
            Cf(4, 4, 1, center: true), // 4 body
            Cf(5, 5, 1, center: true), // 5 summary
            Cf(6, 6, 1, center: true), // 6 group head
            Cf(7, 7, 1, center: true), // 7 group sub
            Cf(8, 8, 1, center: true)); // 8 group body
        cellFormats.Count = (uint)cellFormats.ChildElements.Count;

        return new Stylesheet(fonts, fills, borders, cellFormats);
    }

    private static Stylesheet BuildTotalStylesheet()
    {
        // fonts: 0 default, 1 title/col-head bold, 2 white bold, 3 item
        var fonts = new Fonts(
            new Font(),
            new Font(new Bold(), new FontSize { Val = 14 }),
            new Font(new Bold(), new Color { Rgb = "FFFFFFFF" }, new FontSize { Val = 11 }),
            new Font(new FontSize { Val = 10 }));
        fonts.Count = 4;

        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            Solid("FFE8EEF5"));
        foreach (var p in Palette)
        {
            fills.AppendChild(Solid("FF" + p.Header));
            fills.AppendChild(Solid("FF" + p.Sub));
            fills.AppendChild(Solid("FF" + p.Row));
        }
        fills.Count = (uint)fills.ChildElements.Count;

        var borders = new Borders(new Border(), ThinBorder());
        borders.Count = 2;

        var cellFormats = new CellFormats(
            new CellFormat(),
            Cf(1, 0, 0, center: true), // 1 title
            Cf(1, 2, 1, center: true)); // 2 column head
        for (var c = 0; c < Palette.Length; c++)
        {
            var fillHeader = (uint)(3 + c * 3);
            cellFormats.AppendChild(Cf(2, fillHeader, 1, center: true));
            cellFormats.AppendChild(Cf(2, fillHeader + 1, 1, center: true));
            cellFormats.AppendChild(Cf(3, fillHeader + 2, 1, center: true));
        }
        cellFormats.Count = (uint)cellFormats.ChildElements.Count;
        return new Stylesheet(fonts, fills, borders, cellFormats);
    }

    private static Fill Solid(string rgb) =>
        new(new PatternFill(
            new ForegroundColor { Rgb = rgb },
            new BackgroundColor { Indexed = 64 })
        { PatternType = PatternValues.Solid });

    private static Border ThinBorder() => new(
        new LeftBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFCBD5E1" } },
        new RightBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFCBD5E1" } },
        new TopBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFCBD5E1" } },
        new BottomBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFCBD5E1" } },
        new DiagonalBorder());

    private static CellFormat Cf(uint fontId, uint fillId, uint borderId, bool center)
    {
        var cf = new CellFormat
        {
            FontId = fontId,
            FillId = fillId,
            BorderId = borderId,
            ApplyFont = true,
            ApplyFill = true,
            ApplyBorder = true,
            ApplyAlignment = true,
            Alignment = new Alignment
            {
                Horizontal = center ? HorizontalAlignmentValues.Center : HorizontalAlignmentValues.Left,
                Vertical = VerticalAlignmentValues.Center,
                WrapText = true
            }
        };
        return cf;
    }
}
