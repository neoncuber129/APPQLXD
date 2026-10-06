using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Wordprocessing;

namespace APPQLXD.Core.Export;

/// <summary>
/// Quy tắc chọn hàng mẫu để nhân dòng trong bảng Word.
/// Phiếu NX: chỉ $STT (vì $SoPhieu nằm ở header). Sổ TT: $STT hoặc $SoPhieu.
/// </summary>
public enum WordRowExpandRule
{
    /// <summary>Phiếu nhập/xuất, NXT: chỉ nhân hàng có $STT.</summary>
    BySttOnly = 0,
    /// <summary>Sổ tiêu thụ: ưu tiên $STT; không có thì hàng có $SoPhieu.</summary>
    BySttOrSoPhieu = 1
}

public sealed class WordFillRequest
{
    public required IReadOnlyDictionary<string, string> Scalars { get; init; }
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; init; } = [];
    public WordRowExpandRule RowExpandRule { get; init; } = WordRowExpandRule.BySttOnly;
}

/// <summary>
/// Điền template .docx: thay $Token; nhân bản hàng bảng theo <see cref="WordRowExpandRule"/>.
/// </summary>
public sealed class WordTemplateEngine
{
    private static readonly Regex TokenRegex = new(@"\$[A-Za-z][A-Za-z0-9_]*", RegexOptions.Compiled);

    public byte[] Fill(string templatePath, WordFillRequest request)
    {
        if (!File.Exists(templatePath))
            throw new FileNotFoundException($"Không tìm thấy template: {templatePath}", templatePath);

        // Mẫu cũ (mã dài) và mẫu mới (mã ngắn) đều điền được.
        request = new WordFillRequest
        {
            Scalars = PlaceholderAliases.Expand(request.Scalars),
            Rows = PlaceholderAliases.ExpandRows(request.Rows),
            RowExpandRule = request.RowExpandRule
        };

        using var ms = new MemoryStream();
        using (var fs = File.OpenRead(templatePath))
            fs.CopyTo(ms);
        ms.Position = 0;

        using (var doc = WordprocessingDocument.Open(ms, true))
        {
            var body = doc.MainDocumentPart?.Document?.Body
                       ?? throw new InvalidOperationException("Template Word không hợp lệ (thiếu Body).");
            ExpandTableRows(body, request.Rows, request.Scalars, request.RowExpandRule);
            ReplaceInElement(body, request.Scalars);
            doc.MainDocumentPart!.Document.Save();
        }

        return ms.ToArray();
    }

    public byte[] FillMany(string templatePath, IReadOnlyList<WordFillRequest> requests)
    {
        if (requests.Count == 0)
            throw new ArgumentException("Không có bản ghi để xuất.", nameof(requests));
        if (requests.Count == 1)
            return Fill(templatePath, requests[0]);

        // Fill first into working doc, then append clones of subsequent fills with page breaks.
        var firstBytes = Fill(templatePath, requests[0]);
        using var destMs = new MemoryStream();
        destMs.Write(firstBytes);
        destMs.Position = 0;

        using (var dest = WordprocessingDocument.Open(destMs, true))
        {
            var destBody = dest.MainDocumentPart!.Document.Body!;
            for (var i = 1; i < requests.Count; i++)
            {
                destBody.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                var partBytes = Fill(templatePath, requests[i]);
                using var partMs = new MemoryStream(partBytes);
                using var partDoc = WordprocessingDocument.Open(partMs, false);
                var srcBody = partDoc.MainDocumentPart!.Document.Body!;
                foreach (var child in srcBody.Elements().Select(e => e.CloneNode(true)))
                    destBody.AppendChild(child);
            }

            dest.MainDocumentPart.Document.Save();
        }

        return destMs.ToArray();
    }

    /// <summary>Gộp nhiều file đã fill (template có thể khác nhau) thành một docx + page break.</summary>
    public byte[] MergeFilledDocuments(IReadOnlyList<byte[]> documents)
    {
        if (documents.Count == 0)
            throw new ArgumentException("Không có tài liệu.", nameof(documents));
        if (documents.Count == 1)
            return documents[0];

        using var destMs = new MemoryStream();
        destMs.Write(documents[0]);
        destMs.Position = 0;

        using (var dest = WordprocessingDocument.Open(destMs, true))
        {
            var destBody = dest.MainDocumentPart!.Document.Body!;
            for (var i = 1; i < documents.Count; i++)
            {
                destBody.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                using var partMs = new MemoryStream(documents[i]);
                using var partDoc = WordprocessingDocument.Open(partMs, false);
                var srcBody = partDoc.MainDocumentPart!.Document.Body!;
                foreach (var child in srcBody.Elements().Select(e => e.CloneNode(true)))
                    destBody.AppendChild(child);
            }

            dest.MainDocumentPart.Document.Save();
        }

        return destMs.ToArray();
    }

    private static void ExpandTableRows(
        A.Body body,
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyDictionary<string, string> scalars,
        WordRowExpandRule rule)
    {
        foreach (var table in body.Descendants<Table>().ToList())
        {
            var templateRow = FindTemplateRow(table, rule);
            if (templateRow is null)
                continue;

            // Giữ bản mẫu để chèn dòng Cộng sau các dòng dữ liệu (trước dòng cố định như "Tồn mang sang").
            var templateSnapshot = (TableRow)templateRow.CloneNode(true);
            var dataRows = rows.Where(r => !IsCongRow(r)).ToList();
            TableRow? lastDataRow;

            if (dataRows.Count == 0)
            {
                if (rule == WordRowExpandRule.BySttOrSoPhieu)
                {
                    // Sổ TT không phát sinh: bỏ hàng mẫu trống giữa tồn đầu và tồn mang sang.
                    var before = templateRow.PreviousSibling() as TableRow;
                    templateRow.Remove();
                    lastDataRow = before;
                }
                else
                {
                    ReplaceInElement(templateRow, EmptyRowMap(templateRow));
                    lastDataRow = templateRow;
                }
            }
            else
            {
                TableRow insertAfter = templateRow;
                foreach (var row in dataRows)
                {
                    var clone = (TableRow)templateRow.CloneNode(true);
                    ReplaceInElement(clone, row);
                    table.InsertAfter(clone, insertAfter);
                    insertAfter = clone;
                }

                templateRow.Remove();
                lastDataRow = insertAfter;
            }

            // Mẫu đã có hàng $C_… cố định → chỉ cần scalar; không chèn thêm.
            if (TableHasCongPlaceholders(table))
                continue;
            if (!HasCongScalars(scalars))
                continue;
            // Sổ TT không phát sinh: không chèn dòng Cộng 0 giữa tồn đầu và tồn mang sang.
            if (dataRows.Count == 0 && rule == WordRowExpandRule.BySttOrSoPhieu)
                continue;
            if (lastDataRow is null)
                continue;

            var cong = (TableRow)templateSnapshot.CloneNode(true);
            ReplaceInElement(cong, BuildCongRowMap(GetRowText(templateSnapshot), scalars));
            table.InsertAfter(cong, lastDataRow);
        }
    }

    /// <summary>
    /// Hàng mẫu theo nhóm:
    /// — Phiếu NX / NXT: chỉ $STT ($SoPhieu là header, không nhân).
    /// — Sổ TT: ưu tiên $STT; không có thì $SoPhieu (mẫu sổ không cột STT).
    /// Bỏ qua hàng chỉ có $C_… (dòng Cộng cố định).
    /// </summary>
    private static TableRow? FindTemplateRow(Table table, WordRowExpandRule rule)
    {
        TableRow? soPhieuRow = null;
        foreach (var row in table.Elements<TableRow>())
        {
            var text = GetRowText(row);
            if (IsCongPlaceholderRow(text))
                continue;
            if (text.Contains("$STT", StringComparison.OrdinalIgnoreCase))
                return row;
            if (rule == WordRowExpandRule.BySttOrSoPhieu
                && soPhieuRow is null
                && text.Contains("$SoPhieu", StringComparison.OrdinalIgnoreCase))
                soPhieuRow = row;
        }

        return soPhieuRow;
    }

    private static bool IsCongRow(IReadOnlyDictionary<string, string> row)
    {
        foreach (var key in new[] { "$DGiai", "$DiGiai", "$DienGiai", "$C_DGiai" })
        {
            if (row.TryGetValue(key, out var v)
                && string.Equals(v?.Trim(), "Cộng", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool HasCongScalars(IReadOnlyDictionary<string, string> scalars) =>
        scalars.Keys.Any(k => k.StartsWith("$C_", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Mẫu đã có hàng Cộng cố định ($C_DGiai hoặc ≥3 mã $C_…).
    /// Một mình $T_XTon / $C_XTon trên dòng "Tồn mang sang" không tính là hàng Cộng.
    /// </summary>
    private static bool TableHasCongPlaceholders(Table table) =>
        table.Elements<TableRow>().Any(r => IsCongPlaceholderRow(GetRowText(r)));

    private static bool IsCongPlaceholderRow(string text)
    {
        if (text.Contains("$C_DGiai", StringComparison.OrdinalIgnoreCase)
            || text.Contains("$C_DiGiai", StringComparison.OrdinalIgnoreCase)
            || text.Contains("$C_DienGiai", StringComparison.OrdinalIgnoreCase))
            return true;
        var congTokens = 0;
        foreach (Match m in TokenRegex.Matches(text))
        {
            var t = m.Value;
            // Bỏ mã tồn cuối (đôi khi viết nhầm $C_XTon) và mã tồn đầu $O_…
            if (t.Equals("$C_XTon", StringComparison.OrdinalIgnoreCase)
                || t.Equals("$C_DTon", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("$T_", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("$O_", StringComparison.OrdinalIgnoreCase))
                continue;
            if (t.StartsWith("$C_", StringComparison.OrdinalIgnoreCase))
                congTokens++;
        }

        return congTokens >= 3;
    }

    /// <summary>
    /// Map token trên hàng mẫu → giá trị Cộng ($MChinh←$C_MChinh, $DiGiai←"Cộng", …).
    /// </summary>
    private static Dictionary<string, string> BuildCongRowMap(
        string templateText,
        IReadOnlyDictionary<string, string> scalars)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in TokenRegex.Matches(templateText))
        {
            var token = m.Value;
            if (token.Equals("$STT", StringComparison.OrdinalIgnoreCase)
                || token.Equals("$SoPhieu", StringComparison.OrdinalIgnoreCase)
                || token.Equals("$Ngay", StringComparison.OrdinalIgnoreCase)
                || token.Equals("$NoiDi", StringComparison.OrdinalIgnoreCase)
                || token.Equals("$NoiDen", StringComparison.OrdinalIgnoreCase)
                || token.Equals("$LLo", StringComparison.OrdinalIgnoreCase)
                || token.Equals("$XTon", StringComparison.OrdinalIgnoreCase)
                || token.Equals("$DTon", StringComparison.OrdinalIgnoreCase)
                || token.Equals("$TDong", StringComparison.OrdinalIgnoreCase))
            {
                map[token] = "";
                continue;
            }

            if (token.Equals("$DGiai", StringComparison.OrdinalIgnoreCase)
                || token.Equals("$DiGiai", StringComparison.OrdinalIgnoreCase)
                || token.Equals("$DienGiai", StringComparison.OrdinalIgnoreCase))
            {
                map[token] = scalars.TryGetValue("$C_DGiai", out var label) ? label : "Cộng";
                continue;
            }

            if (scalars.TryGetValue(token, out var asIs))
            {
                map[token] = asIs;
                continue;
            }

            var congKey = token.StartsWith("$C_", StringComparison.OrdinalIgnoreCase)
                ? token
                : "$C_" + token.TrimStart('$');
            map[token] = scalars.TryGetValue(congKey, out var cv) ? cv : "";
        }

        return PlaceholderAliases.Expand(map);
    }

    private static Dictionary<string, string> EmptyRowMap(TableRow row)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in TokenRegex.Matches(GetRowText(row)))
            map[m.Value] = "";
        return map;
    }

    private static string GetRowText(TableRow row)
    {
        var sb = new StringBuilder();
        foreach (var t in row.Descendants<Text>())
            sb.Append(t.Text);
        return sb.ToString();
    }

    private static void ReplaceInElement(OpenXmlElement element, IReadOnlyDictionary<string, string> values)
    {
        foreach (var paragraph in element.Descendants<Paragraph>().ToList())
            ReplaceInParagraph(paragraph, values);
    }

    private static void ReplaceInParagraph(Paragraph paragraph, IReadOnlyDictionary<string, string> values)
    {
        var texts = paragraph.Descendants<Text>().ToList();
        if (texts.Count == 0) return;

        var full = string.Concat(texts.Select(t => t.Text ?? ""));
        if (!full.Contains('$')) return;

        var replaced = TokenRegex.Replace(full, m =>
        {
            if (values.TryGetValue(m.Value, out var v))
                return v ?? "";
            // also try without case sensitivity
            var hit = values.FirstOrDefault(kv => string.Equals(kv.Key, m.Value, StringComparison.OrdinalIgnoreCase));
            return hit.Key is null ? m.Value : hit.Value ?? "";
        });

        if (replaced == full) return;

        // Put all text in first run; clear the rest.
        texts[0].Text = replaced;
        texts[0].Space = SpaceProcessingModeValues.Preserve;
        for (var i = 1; i < texts.Count; i++)
            texts[i].Text = "";
    }
}
