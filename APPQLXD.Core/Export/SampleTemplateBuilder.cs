using APPQLXD.Core.Domain;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace APPQLXD.Core.Export;

/// <summary>Tạo file .docx mẫu tối thiểu (header + 1 dòng bảng $STT) nếu chưa có — riêng cho Kho XD và Kho VT.</summary>
public static class SampleTemplateBuilder
{
    public static void EnsureDefaults(string? baseDirectory = null)
    {
        var root = TemplateNames.ResolveRoot(baseDirectory);
        Directory.CreateDirectory(root);
        MigrateLegacyFlatTemplates(root);

        EnsureScope(baseDirectory, WarehouseScope.Xd);
        EnsureScope(baseDirectory, WarehouseScope.Ptkt);
    }

    private static void EnsureScope(string? baseDirectory, WarehouseScope scope)
    {
        var dir = TemplateNames.ResolveFolder(baseDirectory, scope);
        Directory.CreateDirectory(dir);

        Ensure(Path.Combine(dir, TemplateNames.PhieuNhap), "PHIẾU NHẬP XĂNG DẦU",
            ["$CQ", "$DV", "$SoPhieu", "$Ngay", "$DVGiao", "$DVNhan", "$TC", "$BC", "$TongTien"],
            ["$STT", "$Ten", "$MaSo", "$CL", "$SL", "$Nhiet", "$TD", "$VCF", "$Thuc", "$Gia", "$TTien", "$LLo"]);

        Ensure(Path.Combine(dir, TemplateNames.PhieuXuat), "PHIẾU XUẤT XĂNG DẦU",
            ["$CQ", "$DV", "$SoPhieu", "$Ngay", "$DVGiao", "$DVNhan", "$TC", "$NNhan", "$BC", "$TongTien"],
            ["$STT", "$Ten", "$MaSo", "$CL", "$SL", "$Nhiet", "$TD", "$VCF", "$Thuc", "$Gia", "$TTien", "$LLo"]);

        Ensure(Path.Combine(dir, TemplateNames.SoTtMayXe), "SỔ TIÊU THỤ MÁY / PHƯƠNG TIỆN",
            ["$Nam", "$Quy", "$DT", "$BienSo", "$NL", "$MS", "$DV",
                "$O_DGiai", "$O_XTon", "$O_DTon",
                "$C_SoKm", "$C_GMay", "$C_DM", "$C_TT", "$C_XNhap", "$C_XXuat", "$C_DNhap", "$C_DXuat",
                "$T_DGiai", "$T_XTon", "$T_DTon"],
            ["$STT", "$SoPhieu", "$Ngay", "$DGiai", "$NoiDi", "$NoiDen", "$SoKm", "$GMay", "$DM", "$TT",
                "$XNhap", "$XXuat", "$XTon", "$DNhap", "$DXuat", "$DTon", "$LLo"]);

        Ensure(Path.Combine(dir, TemplateNames.SoTtTau), "SỔ TIÊU THỤ TÀU",
            ["$Nam", "$Quy", "$DT", "$LTau", "$NL", "$MS", "$DV",
                "$O_DGiai", "$O_XTon", "$O_DTon",
                "$C_MChinh", "$C_TongGio", "$C_XDung", "$C_DDung",
                "$C_XNhap", "$C_XXuat", "$C_DNhap", "$C_DXuat",
                "$T_DGiai", "$T_XTon", "$T_DTon"],
            ["$STT", "$SoPhieu", "$Ngay", "$DGiai", "$MChinh", "$GNeo", "$Cx25", "$Cx50", "$Cx75", "$Cx100",
                "$TongGio", "$MPhu", "$GPhu", "$XDung", "$DDung",
                "$XNhap", "$XXuat", "$XTon", "$DNhap", "$DXuat", "$DTon", "$TDong"]);

        Ensure(Path.Combine(dir, TemplateNames.SoNxt), "SỔ NHẬP – XUẤT – TỒN",
            ["$Nam", "$Quy", "$Kho"],
            ["$STT", "$SoPhieu", "$Ngay", "$DGiai", "$SoKm", "$NV", "$Nhap", "$Xuat", "$Ton"]);

        Ensure(Path.Combine(dir, TemplateNames.SoNxtTong), "NXT TỔNG",
            ["$Nam", "$Quy", "$Kho"],
            ["$STT", "$Nhom", "$Ten", "$Gia", "$LLo", "$TonDau", "$Nhap", "$Xuat", "$TCuoi", "$GC"]);
    }

    /// <summary>Chuyển file .docx nằm trực tiếp trong templateword/ sang kho_xd (lần đầu nâng cấp).</summary>
    private static void MigrateLegacyFlatTemplates(string root)
    {
        var xd = Path.Combine(root, TemplateNames.FolderXd);
        Directory.CreateDirectory(xd);
        foreach (var file in Directory.EnumerateFiles(root, "*.docx"))
        {
            var name = Path.GetFileName(file);
            var dest = Path.Combine(xd, name);
            if (File.Exists(dest))
            {
                try { File.Delete(file); } catch { /* giữ bản cũ nếu không xóa được */ }
                continue;
            }

            try { File.Move(file, dest); }
            catch
            {
                try { File.Copy(file, dest, overwrite: false); } catch { /* ignore */ }
            }
        }
    }

    private static void Ensure(string path, string title, string[] headers, string[] rowTokens)
    {
        if (File.Exists(path)) return;
        WriteSimple(path, title, headers, rowTokens);
    }

    public static void WriteSimple(string path, string title, string[] headers, string[] rowTokens)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;

        body.AppendChild(new Paragraph(
            new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
            new Run(new RunProperties(new Bold(), new FontSize { Val = "28" }), new Text(title))));

        foreach (var h in headers)
        {
            body.AppendChild(new Paragraph(new Run(new Text($"{h.TrimStart('$')}: {h}"))));
        }

        body.AppendChild(new Paragraph(new Run(new Text(" "))));

        var table = new Table(
            new TableProperties(
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4 },
                    new BottomBorder { Val = BorderValues.Single, Size = 4 },
                    new LeftBorder { Val = BorderValues.Single, Size = 4 },
                    new RightBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

        var headerRow = new TableRow();
        foreach (var t in rowTokens)
            headerRow.AppendChild(Cell(t.TrimStart('$')));
        table.AppendChild(headerRow);

        var dataRow = new TableRow();
        foreach (var t in rowTokens)
            dataRow.AppendChild(Cell(t));
        table.AppendChild(dataRow);

        body.AppendChild(table);
        main.Document.Save();
    }

    private static TableCell Cell(string text) =>
        new(
            new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Auto }),
            new Paragraph(new Run(new Text(text))));
}
