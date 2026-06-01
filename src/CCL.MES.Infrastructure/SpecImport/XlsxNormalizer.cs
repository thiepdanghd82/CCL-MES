using System.IO.Compression;
using System.Text;
using System.Xml;

namespace CCL.MES.Infrastructure.SpecImport;

/// <summary>
/// Phase 8 PR #31a — Sửa case-mismatch trong xlsx OOXML zip trước khi giao
/// ClosedXML. SpecHub samples xuất từ SheetJS write có Content_Types Override
/// "/xl/sharedStrings.xml" (lowercase 's') nhưng file thật trong zip lưu
/// "xl/SharedStrings.xml" (uppercase 'S'). SheetJS đọc lenient; OpenXml SDK
/// strict — fail với `OpenXmlPackageException: invalid part with unexpected
/// content type` vì file fallback Default Extension="xml" → `application/xml`
/// thay vì sharedStrings ContentType.
///
/// Chiến lược: read zip in-memory, đọc Content_Types.xml; với mỗi Override
/// PartName, nếu file tương ứng KHÔNG tồn tại case-sensitive, tìm case-
/// insensitive và rewrite trong zip với case khớp Override (rebuild zip).
/// </summary>
public static class XlsxNormalizer
{
    /// <summary>
    /// Trả MemoryStream đã normalize. Nếu xlsx đã đúng case → return clone
    /// stream gốc nguyên (chi phí copy nhưng caller-friendly: 1 path).
    /// </summary>
    public static MemoryStream Normalize(Stream input)
    {
        // Copy to memory (xlsx luôn ≤ 5 MB per Q14 — chấp nhận overhead).
        var src = new MemoryStream();
        input.CopyTo(src);
        src.Position = 0;

        // First pass — chỉ cần probe để biết có cần fix không.
        var (needsFix, partRenames) = AnalyzeArchive(src);
        if (!needsFix)
        {
            src.Position = 0;
            return src;
        }

        // Second pass — build new zip với file đổi case.
        src.Position = 0;
        var dst = new MemoryStream();
        using (var srcZip = new ZipArchive(src, ZipArchiveMode.Read, leaveOpen: false))
        using (var dstZip = new ZipArchive(dst, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in srcZip.Entries)
            {
                var newName = partRenames.TryGetValue(entry.FullName, out var renamed) ? renamed : entry.FullName;
                var newEntry = dstZip.CreateEntry(newName);
                using var srcStream = entry.Open();
                using var dstStream = newEntry.Open();
                srcStream.CopyTo(dstStream);
            }
        }
        dst.Position = 0;
        return dst;
    }

    /// <summary>
    /// Đọc Content_Types.xml + check từng Override PartName. Build rename map
    /// (existing zip path → expected path khớp Override).
    /// </summary>
    private static (bool NeedsFix, Dictionary<string, string> Renames) AnalyzeArchive(Stream src)
    {
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        bool needsFix = false;

        using var zip = new ZipArchive(src, ZipArchiveMode.Read, leaveOpen: true);

        var ctEntry = zip.GetEntry("[Content_Types].xml");
        if (ctEntry is null) return (false, renames);  // not a valid xlsx — let ClosedXML báo lỗi đúng nghĩa.

        string ctXml;
        using (var reader = new StreamReader(ctEntry.Open(), Encoding.UTF8))
            ctXml = reader.ReadToEnd();

        // Parse [Content_Types].xml để lấy danh sách Override PartName.
        // OOXML standard prefix '/' — Strip cho match với zip path (zip path KHÔNG có leading '/').
        var doc = new XmlDocument();
        doc.LoadXml(ctXml);
        var nsMgr = new XmlNamespaceManager(doc.NameTable);
        nsMgr.AddNamespace("ct", "http://schemas.openxmlformats.org/package/2006/content-types");
        var overrides = doc.SelectNodes("//ct:Override", nsMgr);
        if (overrides is null) return (false, renames);

        // Build case-insensitive lookup của file paths trong zip
        var zipEntries = zip.Entries
            .ToDictionary(e => e.FullName, e => e.FullName, StringComparer.Ordinal);
        var zipEntriesCI = zip.Entries
            .GroupBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().FullName, StringComparer.OrdinalIgnoreCase);

        foreach (XmlNode ov in overrides)
        {
            var part = ov.Attributes?["PartName"]?.Value;
            if (string.IsNullOrEmpty(part)) continue;
            var expectedZipPath = part.TrimStart('/');
            if (zipEntries.ContainsKey(expectedZipPath)) continue;     // already matching case
            if (!zipEntriesCI.TryGetValue(expectedZipPath, out var actualZipPath)) continue;  // missing entirely (not our fix surface)
            if (string.Equals(actualZipPath, expectedZipPath, StringComparison.Ordinal)) continue;

            // Mismatched case — schedule rename.
            renames[actualZipPath] = expectedZipPath;
            needsFix = true;
        }

        return (needsFix, renames);
    }
}
