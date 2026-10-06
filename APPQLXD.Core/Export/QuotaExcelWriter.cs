using System.Globalization;
using System.Text;
using APPQLXD.Core.Calculations;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace APPQLXD.Core.Export;

/// <summary>Xuất Excel sổ hạn mức — khớp bố cục 3 tầng header trên UI.</summary>
public static class QuotaExcelWriter
{
    private const int ColCount = 19;
    private const uint StyleTitle = 1;
    private const uint StyleHead = 2;
    private const uint StyleBody = 3;
    private const uint StyleHeaderRow = 4; // đỏ đậm — dòng nhóm / cộng

    public static byte[] Write(QuotaSheet sheet)
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook, true))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();
            var styles = wbPart.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = BuildStyles();
            styles.Stylesheet.Save();

            var wsPart = wbPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var merges = new MergeCells();

            var viewLabel = sheet.LotView == NxtLotViewMode.Iuu ? "IUU" : "TX + SSCĐ";
            var title =
                $"Hạn mức năm {sheet.Year} — Quý {sheet.Quarter} — {viewLabel} ({sheet.FromDate:dd/MM/yyyy} – {sheet.ToDate:dd/MM/yyyy})";
            sheetData.AppendChild(StyledRow(1, StyleTitle, Abs(title)));
            merges.AppendChild(new MergeCell { Reference = $"A1:{Col(ColCount - 1)}1" });

            // Hàng 2 (header tầng 1)
            sheetData.AppendChild(StyledRow(2, StyleHead, FillSparse(
            [
                (0, "Số TT"), (1, "Nhiệm vụ"),
                (2, "Hạn mức được phép sử dụng"),
                (5, "Hoạt động của xe, máy, tàu"),
                (9, "Nhiên liệu tiêu thụ"),
                (15, "Cộng NLTT"), (16, "Lũy tích sử dụng"),
                (17, "So sánh giữa hạn mức và sử dụng")
            ])));
            // Hàng 3 (tầng 2)
            sheetData.AppendChild(StyledRow(3, StyleHead, FillSparse(
            [
                (2, "Xăng"), (3, "Điêzel"), (4, "Cộng"),
                (5, "Sử dụng xăng"), (7, "Sử dụng điêzel"),
                (9, "Xăng"), (12, "Điêzel"),
                (17, "Còn"), (18, "Quá")
            ])));
            // Hàng 4 (tầng 3)
            sheetData.AppendChild(StyledRow(4, StyleHead, FillSparse(
            [
                (5, "Km"), (6, "Giờ"), (7, "Km"), (8, "Giờ"),
                (9, "Xe"), (10, "Máy"), (11, "Cộng"),
                (12, "Xe"), (13, "Máy"), (14, "Cộng")
            ])));

            // Merges tầng 1 (row 2–4)
            Merge(merges, 0, 2, 0, 4); // Số TT
            Merge(merges, 1, 2, 1, 4); // Nhiệm vụ
            Merge(merges, 2, 2, 4, 2); // Hạn mức
            Merge(merges, 5, 2, 8, 2); // Hoạt động
            Merge(merges, 9, 2, 14, 2); // NL tiêu thụ
            Merge(merges, 15, 2, 15, 4); // Cộng NLTT
            Merge(merges, 16, 2, 16, 4); // Lũy tích
            Merge(merges, 17, 2, 18, 2); // So sánh

            // Merges tầng 2 (row 3–4)
            Merge(merges, 2, 3, 2, 4); // Xăng HM
            Merge(merges, 3, 3, 3, 4); // Điêzel HM
            Merge(merges, 4, 3, 4, 4); // Cộng HM
            Merge(merges, 5, 3, 6, 3); // Sử dụng xăng
            Merge(merges, 7, 3, 8, 3); // Sử dụng điêzel
            Merge(merges, 9, 3, 11, 3); // Xăng NL
            Merge(merges, 12, 3, 14, 3); // Điêzel NL
            Merge(merges, 17, 3, 17, 4); // Còn
            Merge(merges, 18, 3, 18, 4); // Quá

            uint rowIndex = 5;
            foreach (var r in sheet.Rows)
            {
                var style = r.IsHeader ? StyleHeaderRow : StyleBody;
                var values = new[]
                {
                    r.Stt,
                    r.Name,
                    Qty(r.GasolineLimit),
                    Qty(r.DieselLimit),
                    Qty(r.LimitTotal),
                    Qty(r.GasolineKm),
                    Qty(r.GasolineHours),
                    Qty(r.DieselKm),
                    Qty(r.DieselHours),
                    Qty(r.GasolineVehicle),
                    Qty(r.GasolineMachine),
                    Qty(r.GasolineFuelTotal),
                    Qty(r.DieselVehicle),
                    Qty(r.DieselMachine),
                    Qty(r.DieselFuelTotal),
                    Qty(r.FuelTotal),
                    Qty(r.Cumulative),
                    Qty(r.Remaining),
                    Qty(r.Excess)
                };
                sheetData.AppendChild(StyledRow(rowIndex, style, values.Select(t => (t, style)).ToArray()));
                rowIndex++;
            }

            var worksheet = new Worksheet();
            worksheet.AppendChild(BuildColumns());
            worksheet.AppendChild(sheetData);
            merges.Count = (uint)merges.ChildElements.Count;
            worksheet.AppendChild(merges);
            wsPart.Worksheet = worksheet;

            var sheetName = sheet.LotView == NxtLotViewMode.Iuu ? "Hạn mức IUU" : "Hạn mức TX-SSCĐ";
            wbPart.Workbook.AppendChild(new Sheets(
                new Sheet { Id = wbPart.GetIdOfPart(wsPart), SheetId = 1, Name = sheetName }));
            wbPart.Workbook.Save();
        }

        return ms.ToArray();
    }

    private static void Merge(MergeCells merges, int c1, uint r1, int c2, uint r2) =>
        merges.AppendChild(new MergeCell { Reference = $"{Col(c1)}{r1}:{Col(c2)}{r2}" });

    private static Columns BuildColumns()
    {
        double[] widths =
        [
            8, 22, 10, 10, 10,
            8, 8, 8, 8,
            8, 8, 9, 8, 8, 9,
            10, 8, 9, 9
        ];
        var cols = new Columns();
        for (uint i = 0; i < widths.Length; i++)
            cols.AppendChild(new Column { Min = i + 1, Max = i + 1, Width = widths[i], CustomWidth = true });
        return cols;
    }

    private static (string Text, uint Style)[] Abs(string text)
    {
        var arr = new (string, uint)[ColCount];
        arr[0] = (text, StyleTitle);
        for (var i = 1; i < ColCount; i++)
            arr[i] = ("", StyleTitle);
        return arr;
    }

    private static (string Text, uint Style)[] FillSparse(IEnumerable<(int Col, string Text)> cells)
    {
        var arr = new (string, uint)[ColCount];
        for (var i = 0; i < ColCount; i++)
            arr[i] = ("", StyleHead);
        foreach (var (col, text) in cells)
            if (col >= 0 && col < ColCount)
                arr[col] = (text, StyleHead);
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

    private static string Qty(decimal? value)
    {
        if (value is null or 0)
            return "";
        var val = QuantityMath.RoundQty(value.Value);
        return val == QuantityMath.Whole(val) ? ExportNumberFormat.Qty(val) : ExportNumberFormat.Decimal(val);
    }

    private static Stylesheet BuildStyles()
    {
        var fonts = new Fonts(
            new Font(),
            new Font(new Bold(), new FontSize { Val = 13 }),
            new Font(new Bold(), new FontSize { Val = 10 }),
            new Font(new FontSize { Val = 10 }),
            new Font(new Bold(), new Color { Rgb = "FFB91C1C" }, new FontSize { Val = 10 }));
        fonts.Count = (uint)fonts.ChildElements.Count;

        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            Solid("FFE8EEF4"),
            Solid("FFFFFFFF"),
            Solid("FFFFF8F8"));
        fills.Count = (uint)fills.ChildElements.Count;

        var borders = new Borders(new Border(), ThinBorder());
        borders.Count = (uint)borders.ChildElements.Count;

        var cellFormats = new CellFormats(
            new CellFormat(),
            Cf(1, 0, 0, true),  // title
            Cf(2, 2, 1, true),  // head
            Cf(3, 3, 1, true),  // body
            Cf(4, 4, 1, true)); // header row (red)
        cellFormats.Count = (uint)cellFormats.ChildElements.Count;

        return new Stylesheet(fonts, fills, borders, cellFormats);
    }

    private static Fill Solid(string rgb) =>
        new(new PatternFill
        {
            PatternType = PatternValues.Solid,
            ForegroundColor = new ForegroundColor { Rgb = rgb },
            BackgroundColor = new BackgroundColor { Indexed = 64 }
        });

    private static Border ThinBorder() =>
        new(
            new LeftBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FF1B2836" } },
            new RightBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FF1B2836" } },
            new TopBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FF1B2836" } },
            new BottomBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FF1B2836" } },
            new DiagonalBorder());

    private static CellFormat Cf(uint font, uint fill, uint border, bool center) =>
        new()
        {
            FontId = font,
            FillId = fill,
            BorderId = border,
            ApplyFont = true,
            ApplyFill = fill > 0,
            ApplyBorder = border > 0,
            Alignment = new Alignment
            {
                Horizontal = center ? HorizontalAlignmentValues.Center : HorizontalAlignmentValues.Left,
                Vertical = VerticalAlignmentValues.Center,
                WrapText = true
            },
            ApplyAlignment = true
        };
}
