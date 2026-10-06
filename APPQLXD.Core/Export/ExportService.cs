using System.IO.Compression;
using APPQLXD.Core.Domain;
using APPQLXD.Core.Models;
using APPQLXD.Core.Services;

namespace APPQLXD.Core.Export;

public enum MultiExportMode
{
    SingleFileWithPageBreaks,
    Zip,
    IndividualFiles
}

public sealed record ExportArtifact(string FileName, byte[] Bytes);

public sealed class ExportService
{
    private readonly WordTemplateEngine _word = new();
    private readonly PlaceholderRegistry _registry = new();
    private readonly string _baseDirectory;
    private readonly WarehouseScope _scope;

    public ExportService(string? baseDirectory = null, WarehouseScope scope = WarehouseScope.Xd)
    {
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        _scope = scope;
        SampleTemplateBuilder.EnsureDefaults(_baseDirectory);
    }

    public PlaceholderRegistry Registry => _registry;

    public WarehouseScope Scope => _scope;

    /// <summary>Thư mục gốc templateword (chứa kho_xd và kho_vt).</summary>
    public string TemplateRoot => TemplateNames.ResolveRoot(_baseDirectory);

    /// <summary>Thư mục mẫu theo kho hiện tại (kho_xd hoặc kho_vt).</summary>
    public string TemplateFolder => TemplateNames.ResolveFolder(_baseDirectory, _scope);

    public string TemplatePath(string fileName) => TemplateNames.ResolvePath(fileName, _baseDirectory, _scope);

    public byte[] ExportSlipWord(DocumentDetail doc)
    {
        var path = RequireTemplate(SlipExportMapper.TemplateFileName(doc));
        return _word.Fill(path, SlipExportMapper.Map(doc, _registry));
    }

    public IReadOnlyList<ExportArtifact> ExportSlips(
        IReadOnlyList<DocumentDetail> docs,
        MultiExportMode mode,
        IProgress<DemoProgress>? progress = null)
    {
        if (docs.Count == 0)
            throw new ArgumentException("Chưa chọn phiếu.", nameof(docs));

        var total = docs.Count;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filled = new List<ExportArtifact>(total);
        for (var i = 0; i < docs.Count; i++)
        {
            var d = docs[i];
            Report(progress, $"Đang xuất phiếu {i + 1}/{total}", i, total);
            filled.Add(new ExportArtifact(UniqueSlipFileName(d, usedNames), ExportSlipWord(d)));
        }

        Report(progress, "Đang đóng gói file...", total, total + 1);
        IReadOnlyList<ExportArtifact> result;
        if (docs.Count == 1 || mode == MultiExportMode.IndividualFiles)
            result = filled;
        else if (mode == MultiExportMode.Zip)
            result = [new ExportArtifact(SlipBundleName(docs, "zip"), ZipArtifacts(filled))];
        else
            result = [new ExportArtifact(SlipBundleName(docs, "docx"), _word.MergeFilledDocuments(filled.Select(x => x.Bytes).ToList()))];

        Report(progress, "Đã xuất xong", total + 1, total + 1);
        return result;
    }

    public byte[] ExportConsumerBookWord(ConsumerTransferBook book, int year, int quarter)
    {
        var path = RequireTemplate(TemplateNames.SoTtMayXe);
        return _word.Fill(path, BookExportMapper.MapConsumer(book, year, quarter));
    }

    public byte[] ExportShipBookWord(ShipQuarterBookDto book)
    {
        var path = RequireTemplate(TemplateNames.SoTtTau);
        return _word.Fill(path, BookExportMapper.MapShip(book));
    }

    public sealed record BookExportItem(
        string DisplayName,
        bool IsShip,
        ConsumerTransferBook? ConsumerBook,
        ShipQuarterBookDto? ShipBook,
        int Year,
        int Quarter);

    public IReadOnlyList<ExportArtifact> ExportBooks(
        IReadOnlyList<BookExportItem> books,
        MultiExportMode mode,
        IProgress<DemoProgress>? progress = null)
    {
        if (books.Count == 0)
            throw new ArgumentException("Chưa chọn sổ.", nameof(books));

        byte[] FillOne(BookExportItem b) =>
            b.IsShip
                ? ExportShipBookWord(b.ShipBook!)
                : ExportConsumerBookWord(b.ConsumerBook!, b.Year, b.Quarter);

        string NameOf(BookExportItem b) =>
            SafeName($"so_tt_{(b.IsShip ? "tau" : "may_xe")}_{b.DisplayName}_Q{b.Quarter}_{b.Year}.docx");

        var total = books.Count;
        var filled = new List<ExportArtifact>(total);
        for (var i = 0; i < books.Count; i++)
        {
            var b = books[i];
            Report(progress, $"Đang xuất sổ {i + 1}/{total}: {b.DisplayName}", i, total);
            filled.Add(new ExportArtifact(NameOf(b), FillOne(b)));
        }

        Report(progress, "Đang đóng gói file...", total, total + 1);
        IReadOnlyList<ExportArtifact> result;
        if (books.Count == 1 || mode == MultiExportMode.IndividualFiles)
            result = filled;
        else if (mode == MultiExportMode.Zip)
            result = [new ExportArtifact("so_tieu_thu.zip", ZipArtifacts(filled))];
        else
            result = [new ExportArtifact("so_tieu_thu_nhieu.docx", _word.MergeFilledDocuments(filled.Select(x => x.Bytes).ToList()))];

        Report(progress, "Đã xuất xong", total + 1, total + 1);
        return result;
    }

    private static void Report(IProgress<DemoProgress>? progress, string phase, int done, int total) =>
        progress?.Report(new DemoProgress(phase, done, total, done, total));

    public byte[] ExportNxtWord(NxtSheet sheet, int year, int quarter, string warehouseLabel)
    {
        var path = RequireTemplate(TemplateNames.SoNxt);
        return _word.Fill(path, NxtExportMapper.MapNxt(sheet, year, quarter, warehouseLabel));
    }

    public byte[] ExportNxtExcel(NxtSheet sheet, int year, int quarter, string warehouseLabel) =>
        NxtExcelWriter.WriteNxt(sheet, year, quarter, warehouseLabel);

    public byte[] ExportNxtTotalWord(NxtTotalSheet sheet, int year, int quarter, string warehouseLabel)
    {
        var path = RequireTemplate(TemplateNames.SoNxtTong);
        return _word.Fill(path, NxtExportMapper.MapNxtTotal(sheet, year, quarter, warehouseLabel));
    }

    public byte[] ExportNxtTotalExcel(NxtTotalSheet sheet, int year, int quarter, string warehouseLabel) =>
        NxtExcelWriter.WriteNxtTotal(sheet, year, quarter, warehouseLabel);

    public byte[] ExportQuotaExcel(QuotaSheet sheet) =>
        QuotaExcelWriter.Write(sheet);

    public void WriteAll(IReadOnlyList<ExportArtifact> artifacts, string destinationPathOrFolder)
    {
        if (artifacts.Count == 1 && !Directory.Exists(destinationPathOrFolder))
        {
            File.WriteAllBytes(destinationPathOrFolder, artifacts[0].Bytes);
            return;
        }

        Directory.CreateDirectory(destinationPathOrFolder);
        foreach (var a in artifacts)
            File.WriteAllBytes(Path.Combine(destinationPathOrFolder, a.FileName), a.Bytes);
    }

    private string RequireTemplate(string fileName)
    {
        SampleTemplateBuilder.EnsureDefaults(_baseDirectory);
        var path = TemplatePath(fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Thiếu template Word. Đặt file tại: {path}", path);
        return path;
    }

    private static byte[] ZipArtifacts(IReadOnlyList<ExportArtifact> artifacts)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var a in artifacts)
            {
                var entry = zip.CreateEntry(a.FileName, CompressionLevel.Optimal);
                using var s = entry.Open();
                s.Write(a.Bytes);
            }
        }
        return ms.ToArray();
    }

    private static string SafeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }

    private static string UniqueSlipFileName(DocumentDetail doc, HashSet<string> used)
    {
        var baseName = SlipFileName(doc);
        if (used.Add(baseName))
            return baseName;

        var stem = Path.GetFileNameWithoutExtension(baseName);
        var ext = Path.GetExtension(baseName);
        var alt = SafeName($"{stem}_{doc.Number}{ext}");
        if (used.Add(alt))
            return alt;

        alt = SafeName($"{stem}_{doc.Id.ToString("N")[..8]}{ext}");
        used.Add(alt);
        return alt;
    }

    /// <summary>Tên từng phiếu: loại_Q{quý}_{năm}_{số phiếu}.docx</summary>
    private static string SlipFileName(DocumentDetail doc)
    {
        var date = doc.DocumentDate.Date;
        var year = date.Year;
        var quarter = (date.Month - 1) / 3 + 1;
        var form = string.IsNullOrWhiteSpace(doc.Slip.FormNumber) ? doc.Number : doc.Slip.FormNumber.Trim();
        return SafeName($"{KindSlug(doc.Kind)}_Q{quarter}_{year}_{form}.docx");
    }

    /// <summary>Tên gói zip/docx gộp: loại_Q{quý}_{năm}.ext (hoặc rút gọn khi lẫn loại/kỳ).</summary>
    private static string SlipBundleName(IReadOnlyList<DocumentDetail> docs, string ext)
    {
        var years = docs.Select(d => d.DocumentDate.Year).Distinct().OrderBy(x => x).ToList();
        var quarters = docs.Select(d => (d.DocumentDate.Month - 1) / 3 + 1).Distinct().OrderBy(x => x).ToList();
        var kinds = docs.Select(d => d.Kind).Distinct().ToList();

        var period = years.Count == 1 && quarters.Count == 1
            ? $"Q{quarters[0]}_{years[0]}"
            : years.Count == 1
                ? years[0].ToString()
                : $"{years[0]}-{years[^1]}";

        var kindPart = kinds.Count == 1
            ? KindSlug(kinds[0])
            : kinds.All(k => k == DocumentKind.Import)
                ? "phieu_nhap"
                : kinds.All(k => k is DocumentKind.Issue or DocumentKind.Transfer or DocumentKind.LotConvert
                    or DocumentKind.Consumption)
                    ? "phieu_xuat"
                    : "phieu";

        return SafeName($"{kindPart}_{period}.{ext.TrimStart('.')}");
    }

    private static string KindSlug(DocumentKind kind) => kind switch
    {
        DocumentKind.Import => "phieu_nhap",
        DocumentKind.Issue => "phieu_xuat",
        DocumentKind.Transfer => "phieu_dieu_chuyen",
        DocumentKind.LotConvert => "phieu_doi_lo",
        DocumentKind.Consumption => "phieu_xuat_tieu_thu",
        DocumentKind.Auxiliary => "phieu_tieu_thu_kho_phu",
        DocumentKind.Opening => "ton_dau_ky",
        _ => "phieu"
    };
}
