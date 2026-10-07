using APPQLXD.Core.Domain;
using APPQLXD.Core.Export;
using APPQLXD.Core.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using SpreadsheetDocument = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument;

namespace APPQLXD.Tests;

public class ExportTests
{
    [Fact]
    public void Placeholder_registry_has_slip_and_row_tokens()
    {
        var list = PlaceholderRegistry.FixedFor(ExportDocumentKind.PhieuNhap);
        Assert.Contains(list, x => x.Token == "$SoPhieu" && !x.IsRowField);
        Assert.Contains(list, x => x.Token == "$STT" && x.IsRowField);
        Assert.Contains(list, x => x.Token == "$Ten" && x.IsRowField);
        Assert.DoesNotContain(list, x => x.Token.StartsWith("$R_", StringComparison.Ordinal));
    }

    [Fact]
    public void Placeholder_slug_strips_vietnamese()
    {
        Assert.Equal("CoQuan", PlaceholderSlug.FromVietnamese("Cơ quan"));
        Assert.Equal("DonViNhan", PlaceholderSlug.FromVietnamese("Đơn vị nhận"));
    }

    [Fact]
    public void Field_token_stable_by_id()
    {
        var reg = new PlaceholderRegistry();
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var a = reg.TokenForField(id, "Ghi chú thêm");
        var b = reg.TokenForField(id, "Đổi tên trường");
        Assert.Equal(a, b);
        Assert.StartsWith("$F_", a);
    }

    [Fact]
    public void Word_engine_replaces_tokens_and_clones_rows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "appqlxd-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var template = Path.Combine(dir, "t.docx");
            SampleTemplateBuilder.WriteSimple(template, "TEST",
                ["$SoPhieu", "$Ngay"],
                ["$STT", "$Ten", "$SL"]);

            var engine = new WordTemplateEngine();
            var bytes = engine.Fill(template, new WordFillRequest
            {
                Scalars = new Dictionary<string, string>
                {
                    ["$SoPhieu"] = "PN-01",
                    ["$Ngay"] = "01/10/2026"
                },
                Rows =
                [
                    new Dictionary<string, string> { ["$STT"] = "1", ["$Ten"] = "Xăng", ["$SL"] = "10" },
                    new Dictionary<string, string> { ["$STT"] = "2", ["$Ten"] = "Dầu", ["$SL"] = "20" },
                    new Dictionary<string, string> { ["$STT"] = "3", ["$Ten"] = "NL", ["$SL"] = "30" }
                ]
            });

            using var ms = new MemoryStream(bytes);
            using var doc = WordprocessingDocument.Open(ms, false);
            var text = string.Concat(doc.MainDocumentPart!.Document.Body!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text));
            Assert.Contains("PN-01", text);
            Assert.Contains("Xăng", text);
            Assert.Contains("Dầu", text);
            Assert.Contains("NL", text);
            Assert.DoesNotContain("$STT", text);
            Assert.DoesNotContain("$SoPhieu", text);

            var dataRows = doc.MainDocumentPart.Document.Body.Descendants<TableRow>()
                .Where(r => r.InnerText.Contains('1') || r.InnerText.Contains('2') || r.InnerText.Contains('3'))
                .ToList();
            Assert.True(dataRows.Count >= 3);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Word_engine_clones_rows_without_STT_and_accepts_ship_typo_aliases()
    {
        // Mẫu sổ tàu thực tế: hàng dữ liệu có $SoPhieu nhưng không có $STT; mã $DiGiai / $GNao.
        var dir = Path.Combine(Path.GetTempPath(), "appqlxd-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var template = Path.Combine(dir, "tau.docx");
            SampleTemplateBuilder.WriteSimple(template, "SỔ TÀU",
                ["$DT", "$LTau"],
                ["$SoPhieu", "$Ngay", "$DiGiai", "$MChinh", "$GNao", "$XXuat"]);

            var engine = new WordTemplateEngine();
            var bytes = engine.Fill(template, new WordFillRequest
            {
                RowExpandRule = WordRowExpandRule.BySttOrSoPhieu,
                Scalars = new Dictionary<string, string>
                {
                    ["$DT"] = "Tàu BP",
                    ["$LTau"] = "27-01"
                },
                Rows =
                [
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["$STT"] = "1",
                        ["$SoPhieu"] = "TT-01",
                        ["$Ngay"] = "05/07/2026",
                        ["$DGiai"] = "Tiêu thụ",
                        ["$MChinh"] = "2",
                        ["$GNeo"] = "3",
                        ["$XXuat"] = "100"
                    },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["$STT"] = "2",
                        ["$SoPhieu"] = "TT-02",
                        ["$Ngay"] = "06/07/2026",
                        ["$DGiai"] = "ĐC",
                        ["$MChinh"] = "1",
                        ["$GNeo"] = "1",
                        ["$XXuat"] = "50"
                    }
                ]
            });

            using var ms = new MemoryStream(bytes);
            using var doc = WordprocessingDocument.Open(ms, false);
            var text = string.Concat(doc.MainDocumentPart!.Document.Body!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text));
            Assert.Contains("Tàu BP", text);
            Assert.Contains("TT-01", text);
            Assert.Contains("TT-02", text);
            Assert.Contains("Tiêu thụ", text);
            Assert.DoesNotContain("$SoPhieu", text);
            Assert.DoesNotContain("$DiGiai", text);
            Assert.DoesNotContain("$GNao", text);
            Assert.DoesNotContain("$DGiai", text);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Nxt_excel_matches_ui_columns_n_xx_xd()
    {
        var sheet = new NxtSheet(
            [new NxtColumn("Xăng", false), new NxtColumn("Tổng", true)],
            [
                new NxtRow(NxtRowKind.Opening, null, null, "", null, "Mang sang", null, "",
                    [new NxtCell(null, null, 100), new NxtCell(null, null, 100)]),
                new NxtRow(NxtRowKind.Slip, Guid.NewGuid(), DocumentKind.Import, "N1",
                    new DateTime(2026, 7, 1), "Nhập", 12, "Nhiệm vụ",
                    [new NxtCell(50, null, 150), new NxtCell(50, null, 150)],
                    NxtVoucherColumn.N)
            ]);

        var bytes = NxtExcelWriter.WriteNxt(sheet, 2026, 3, "Kho A");
        using var ms = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(ms, false);
        var part = doc.WorkbookPart!.WorksheetParts.First();
        var sheetData = part.Worksheet.Elements<SheetData>().First();
        var rows = sheetData.Elements<Row>().ToList();
        Assert.True(rows.Count >= 4); // title + 2 header + data

        var merges = part.Worksheet.Elements<MergeCells>().FirstOrDefault();
        Assert.NotNull(merges);
        Assert.Contains(merges!.Elements<MergeCell>(), m => m.Reference?.Value == "A2:D2");

        // Sub-header row has N / XX / XD
        var subTexts = rows[2].Elements<Cell>().Select(c => c.InlineString?.Text?.Text ?? "").ToList();
        Assert.Contains("N", subTexts);
        Assert.Contains("XX", subTexts);
        Assert.Contains("XD", subTexts);
        Assert.Contains("Nhập", subTexts);

        // Row 3 = opening (Mang sang); row 4 = phiếu nhập → số ở cột N
        var opening = CellTexts(rows[3]);
        Assert.Equal("Mang sang", opening[4]);
        Assert.Equal("100", opening[9]); // tồn cột nhiên liệu đầu, N0

        var slip = CellTexts(rows[4]);
        Assert.Equal("N1", slip[0]);
        Assert.Equal("", slip[1]);
        Assert.Equal("01/07/2026", slip[3]);
    }

    private static List<string> CellTexts(Row row)
    {
        // Excel có thể bỏ ô trống — dựng lại theo CellReference
        var map = new Dictionary<int, string>();
        foreach (var cell in row.Elements<Cell>())
        {
            var refer = cell.CellReference?.Value ?? "";
            var col = 0;
            foreach (var ch in refer)
            {
                if (!char.IsLetter(ch)) break;
                col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            col -= 1;
            map[col] = cell.InlineString?.Text?.Text ?? "";
        }
        var max = map.Count == 0 ? 0 : map.Keys.Max();
        var list = new List<string>();
        for (var i = 0; i <= max; i++)
            list.Add(map.TryGetValue(i, out var t) ? t : "");
        return list;
    }

    [Fact]
    public void Nxt_total_excel_has_group_and_lot_headers()
    {
        var sheet = new NxtTotalSheet(
        [
            new NxtTotalRow("Xăng", "Xăng A92", 20000, Guid.NewGuid(), "TX",
                10, 2, 3, 5, 20,
                5, 0, 0, 0, 5,
                2, 0, 1, 0, 3,
                13, 2, 2, 5, 22),
            new NxtTotalRow("Xăng", "Xăng A95", 22000, Guid.NewGuid(), "SSCĐ", 1, 0, 0, 1)
        ]);
        var bytes = NxtExcelWriter.WriteNxtTotal(sheet, 2026, 3, "Kho lớn");
        using var ms = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(ms, false);
        var part = doc.WorkbookPart!.WorksheetParts.First();
        var texts = part.Worksheet.Elements<SheetData>().First()
            .Descendants<DocumentFormat.OpenXml.Spreadsheet.Text>()
            .Select(t => t.Text)
            .ToList();
        Assert.Contains("Lô", texts);
        Assert.Contains("Tồn đầu", texts);
        Assert.Contains("Nhập", texts);
        Assert.Contains("Xuất", texts);
        Assert.Contains("Tồn sau", texts);
        Assert.Contains("Kho HĐ", texts);
        Assert.Contains("Máy", texts);
        Assert.Contains("PT", texts);
        Assert.Contains("Tàu", texts);
        Assert.Contains("Tổng", texts);
        Assert.Contains("Xăng", texts);
        Assert.Contains("  TX", texts);
        Assert.Contains("Xăng A92", texts);

        // Kiểm tra mapper Word
        var request = NxtExportMapper.MapNxtTotal(sheet, 2026, 3, "Kho lớn");
        var map = request.Rows[0];
        Assert.Equal("20", map["$TonDau"]);
        Assert.Equal("10", map["$TonDau_Kho"]);
        Assert.Equal("2", map["$TonDau_May"]);
        Assert.Equal("3", map["$TonDau_Xe"]);
        Assert.Equal("5", map["$TonDau_Tau"]);
        Assert.Equal("5", map["$Nhap"]);
        Assert.Equal("3", map["$Xuat"]);
        Assert.Equal("22", map["$TonCuoi"]);
        Assert.Equal("13", map["$TonCuoi_Kho"]);
    }

    [Fact]
    public void Sample_templates_created_on_ensure()
    {
        var dir = Path.Combine(Path.GetTempPath(), "appqlxd-tpl-" + Guid.NewGuid().ToString("N"));
        try
        {
            SampleTemplateBuilder.EnsureDefaults(dir);
            Assert.True(File.Exists(TemplateNames.ResolvePath(TemplateNames.PhieuNhap, dir, WarehouseScope.Xd)));
            Assert.True(File.Exists(TemplateNames.ResolvePath(TemplateNames.SoTtTau, dir, WarehouseScope.Xd)));
            Assert.True(File.Exists(TemplateNames.ResolvePath(TemplateNames.SoNxtTong, dir, WarehouseScope.Ptkt)));
            Assert.True(Directory.Exists(TemplateNames.ResolveFolder(dir, WarehouseScope.Xd)));
            Assert.True(Directory.Exists(TemplateNames.ResolveFolder(dir, WarehouseScope.Ptkt)));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Export_numbers_use_vietnam_format_like_ui()
    {
        Assert.Equal("1.234", ExportNumberFormat.Qty(1234m));
        Assert.Equal("15,5", ExportNumberFormat.Decimal(15.5m));
        Assert.Equal("0,987654", ExportNumberFormat.Factor(0.987654m));
        Assert.Equal("20.000", ExportNumberFormat.Money(20000m));
    }

    [Fact]
    public void Consumer_book_export_includes_cong_row_and_scalars()
    {
        var book = new ConsumerTransferBook(
            "Máy A", "M1", "Xăng", "3-04.1", "ĐVT",
            100, 0,
            [
                new ConsumerTransferBookRow(true, null, "", null, "Tồn đầu", "", "", null, null, null, null, null, null,
                    null, null, 50, null, null, null),
                new ConsumerTransferBookRow(false, Guid.NewGuid(), "ĐC1", new DateTime(2026, 7, 1), "ĐC", "Kho", "Máy",
                    1200, null, 10, 10, null, null, 100, 90, 60, null, null, null)
            ]);

        var fill = BookExportMapper.MapConsumer(book, 2026, 3);
        Assert.Equal("Cộng", fill.Scalars["$C_DGiai"]);
        Assert.Equal("1.200", fill.Scalars["$C_SoKm"]);
        Assert.Equal("100", fill.Scalars["$C_XNhap"]);
        Assert.Equal("90", fill.Scalars["$C_XXuat"]);
        Assert.Equal("Tồn đầu", fill.Scalars["$O_DGiai"]);
        Assert.Equal("50", fill.Scalars["$O_XTon"]);
        Assert.Equal("Tồn mang sang quý sau", fill.Scalars["$T_DGiai"]);
        Assert.Equal("60", fill.Scalars["$T_XTon"]); // tồn cuối dòng ĐC
        Assert.Equal("60", fill.Scalars["$C_XTon"]); // alias cũ
        // Alias mã cũ vẫn có sau Expand
        Assert.Equal("Cộng", fill.Scalars["$C_DienGiai"]);
        Assert.Equal("Cộng", fill.Rows[^1]["$DGiai"]);
        Assert.Equal("1.200", fill.Rows[^1]["$SoKm"]);
        Assert.Equal("90", fill.Rows[0]["$XXuat"]); // dòng dữ liệu (bỏ tồn đầu), N0 vi-VN
        Assert.Contains(PlaceholderRegistry.FixedFor(ExportDocumentKind.SoTtMayXe), p => p.Token == "$C_SoKm");
        Assert.Contains(PlaceholderRegistry.FixedFor(ExportDocumentKind.SoTtMayXe), p => p.Token == "$C_XNhap");
        Assert.Contains(PlaceholderRegistry.FixedFor(ExportDocumentKind.SoTtMayXe), p => p.Token == "$O_XTon");
        Assert.Contains(PlaceholderRegistry.FixedFor(ExportDocumentKind.SoTtMayXe), p => p.Token == "$T_XTon");
        Assert.Contains(PlaceholderRegistry.FixedFor(ExportDocumentKind.SoTtTau), p => p.Token == "$O_DTon");
    }

    [Fact]
    public void Consumer_book_export_idle_quarter_closing_equals_opening()
    {
        var book = new ConsumerTransferBook(
            "Máy A", "M1", "Xăng", "3-04.1", "ĐVT",
            0, 0,
            [
                new ConsumerTransferBookRow(true, null, "", null, "Tồn quý trước chuyển sang", "", "",
                    null, null, null, null, null, null, null, null, 40000, null, null, 15800)
            ]);

        var fill = BookExportMapper.MapConsumer(book, 2026, 3);
        Assert.Equal("40.000", fill.Scalars["$O_XTon"]);
        Assert.Equal("15.800", fill.Scalars["$O_DTon"]);
        Assert.Equal("40.000", fill.Scalars["$T_XTon"]);
        Assert.Equal("15.800", fill.Scalars["$T_DTon"]);
        // Chỉ còn dòng Cộng trong Rows — không có dòng phát sinh.
        Assert.Single(fill.Rows);
        Assert.Equal("Cộng", fill.Rows[0]["$DGiai"]);
    }

    [Fact]
    public void Word_engine_book_removes_empty_template_row_when_idle()
    {
        var dir = Path.Combine(Path.GetTempPath(), "appqlxd-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var template = Path.Combine(dir, "idle.docx");
            using (var doc = WordprocessingDocument.Create(template, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                var body = main.Document.Body!;
                var table = new DocumentFormat.OpenXml.Wordprocessing.Table();
                table.AppendChild(MakeRow("Tồn quý trước chuyển sang", "", "$O_XTon", "$O_DTon"));
                table.AppendChild(MakeRow("$SoPhieu", "$Ngay", "$DiGiai", "$XXuat"));
                table.AppendChild(MakeRow("Tồn mang sang quý sau", "", "$T_XTon", "$T_DTon"));
                body.AppendChild(table);
                main.Document.Save();
            }

            var engine = new WordTemplateEngine();
            var bytes = engine.Fill(template, new WordFillRequest
            {
                RowExpandRule = WordRowExpandRule.BySttOrSoPhieu,
                Scalars = new Dictionary<string, string>
                {
                    ["$O_XTon"] = "40.000",
                    ["$O_DTon"] = "15.800",
                    ["$T_XTon"] = "40.000",
                    ["$T_DTon"] = "15.800",
                    ["$C_DGiai"] = "Cộng",
                    ["$C_XXuat"] = "0"
                },
                Rows = []
            });

            using var ms = new MemoryStream(bytes);
            using var outDoc = WordprocessingDocument.Open(ms, false);
            var rows = outDoc.MainDocumentPart!.Document.Body!.Descendants<TableRow>().ToList();
            var texts = rows.Select(r => string.Concat(r.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text))).ToList();
            Assert.Equal(2, texts.Count);
            Assert.Contains("Tồn quý trước chuyển sang", texts[0], StringComparison.Ordinal);
            Assert.Contains("40.000", texts[0], StringComparison.Ordinal);
            Assert.Contains("Tồn mang sang quý sau", texts[1], StringComparison.Ordinal);
            Assert.Contains("40.000", texts[1], StringComparison.Ordinal);
            Assert.Contains("15.800", texts[1], StringComparison.Ordinal);
            Assert.DoesNotContain(texts, t => t.Contains("$SoPhieu", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(texts, t => t.Contains("Cộng", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Word_engine_slip_does_not_clone_header_row_with_SoPhieu()
    {
        // Phiếu NX: bảng header có $DVNhan + $SoPhieu; bảng dòng hàng có $STT.
        // Không được nhân dòng header theo số dòng hàng.
        var dir = Path.Combine(Path.GetTempPath(), "appqlxd-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var template = Path.Combine(dir, "phieu.docx");
            using (var doc = WordprocessingDocument.Create(template, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                var body = main.Document.Body!;
                var header = new DocumentFormat.OpenXml.Wordprocessing.Table();
                header.AppendChild(MakeRow("Đơn vị nhận hàng :", "$DVNhan", "Số", "$SoPhieu"));
                header.AppendChild(MakeRow("Có giá đến ngày:", "$GiaDen", "", ""));
                body.AppendChild(header);
                var lines = new DocumentFormat.OpenXml.Wordprocessing.Table();
                lines.AppendChild(MakeRow("$STT", "$Ten", "$SL"));
                body.AppendChild(lines);
                main.Document.Save();
            }

            var engine = new WordTemplateEngine();
            var bytes = engine.Fill(template, new WordFillRequest
            {
                RowExpandRule = WordRowExpandRule.BySttOnly,
                Scalars = new Dictionary<string, string>
                {
                    ["$SoPhieu"] = "DEMO-PN",
                    ["$DVNhan"] = "Hải đoàn BP 18",
                    ["$GiaDen"] = "31/12/2026"
                },
                Rows =
                [
                    new Dictionary<string, string> { ["$STT"] = "1", ["$Ten"] = "Xăng", ["$SL"] = "10" },
                    new Dictionary<string, string> { ["$STT"] = "2", ["$Ten"] = "Dầu", ["$SL"] = "20" },
                    new Dictionary<string, string> { ["$STT"] = "3", ["$Ten"] = "NL", ["$SL"] = "30" }
                ]
            });

            using var ms = new MemoryStream(bytes);
            using var outDoc = WordprocessingDocument.Open(ms, false);
            var text = string.Concat(outDoc.MainDocumentPart!.Document.Body!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text));
            Assert.Equal(1, CountOccurrences(text, "Đơn vị nhận hàng"));
            Assert.Equal(1, CountOccurrences(text, "DEMO-PN"));
            Assert.Equal(1, CountOccurrences(text, "Hải đoàn BP 18"));
            Assert.Equal(1, CountOccurrences(text, "Có giá đến ngày"));
            Assert.Contains("Xăng", text);
            Assert.Contains("Dầu", text);
            Assert.Contains("NL", text);
            Assert.DoesNotContain("$SoPhieu", text);
            Assert.DoesNotContain("$STT", text);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Word_engine_inserts_cong_row_before_static_closing_row()
    {
        var dir = Path.Combine(Path.GetTempPath(), "appqlxd-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var template = Path.Combine(dir, "tau_cong.docx");
            // Giống mẫu thực tế: hàng $SoPhieu + hàng cố định "Tồn mang sang quý sau"
            using (var doc = WordprocessingDocument.Create(template, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                var body = main.Document.Body!;
                var table = new DocumentFormat.OpenXml.Wordprocessing.Table();
                table.AppendChild(MakeRow("Tồn quý trước chuyển sang", "", "", ""));
                table.AppendChild(MakeRow("$SoPhieu", "$Ngay", "$DiGiai", "$XXuat"));
                table.AppendChild(MakeRow("Tồn mang sang quý sau", "", "", "$T_XTon"));
                body.AppendChild(table);
                main.Document.Save();
            }

            var engine = new WordTemplateEngine();
            var bytes = engine.Fill(template, new WordFillRequest
            {
                RowExpandRule = WordRowExpandRule.BySttOrSoPhieu,
                Scalars = new Dictionary<string, string>
                {
                    ["$C_DGiai"] = "Cộng",
                    ["$C_XXuat"] = "1.526",
                    ["$T_XTon"] = "654"
                },
                Rows =
                [
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["$SoPhieu"] = "TT-01",
                        ["$Ngay"] = "01/07/2026",
                        ["$DGiai"] = "Tiêu thụ",
                        ["$XXuat"] = "100"
                    }
                ]
            });

            using var ms = new MemoryStream(bytes);
            using var outDoc = WordprocessingDocument.Open(ms, false);
            var rows = outDoc.MainDocumentPart!.Document.Body!.Descendants<TableRow>().ToList();
            var texts = rows.Select(r => string.Concat(r.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text))).ToList();
            Assert.Contains(texts, t => t.Contains("TT-01", StringComparison.Ordinal));
            Assert.Contains(texts, t => t.Contains("Cộng", StringComparison.Ordinal));
            Assert.Contains(texts, t => t.Contains("1.526", StringComparison.Ordinal));
            Assert.Contains(texts, t => t.Contains("Tồn mang sang quý sau", StringComparison.Ordinal));
            Assert.Contains(texts, t => t.Contains("654", StringComparison.Ordinal));
            var congIdx = texts.FindIndex(t => t.Contains("Cộng", StringComparison.Ordinal));
            var closeIdx = texts.FindIndex(t => t.Contains("Tồn mang sang", StringComparison.Ordinal));
            Assert.True(congIdx >= 0 && closeIdx > congIdx);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var idx = text.IndexOf(needle, start, StringComparison.Ordinal);
            if (idx < 0) return count;
            count++;
            start = idx + needle.Length;
        }
    }

    private static TableRow MakeRow(params string[] cells)
    {
        var row = new TableRow();
        foreach (var c in cells)
        {
            row.AppendChild(new TableCell(new Paragraph(
                new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.Text(c)))));
        }

        return row;
    }
}
