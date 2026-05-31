using System.Diagnostics;
using System.Text;

namespace CCL.MES.Application.Services.NpiImport;

/// <summary>
/// Phase 7 hạng mục 1 — pure CSV parser cho preview phase (KHÔNG ghi DB).
/// Tách khỏi Web-layer engine để Application stay host-agnostic.
///
/// RFC-4180 tolerant: UTF-8 BOM, quoted fields, embedded "", \r\n,
/// trailing partial row, empty lines. Mirror CMES service.ts parseCsv.
/// </summary>
public static class NpiCsvParser
{
    public static CsvParseResult<TEntity> Parse<TEntity>(Stream csvStream, ICsvImportTarget<TEntity> target)
        where TEntity : class
    {
        var sw = Stopwatch.StartNew();
        var (header, dataRows) = ParseCsv(csvStream);
        var lowered = header.Select(h => h.Trim().ToLowerInvariant()).ToArray();

        var indexMap = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (field, aliases) in target.HeaderAliases)
        {
            foreach (var alias in aliases)
            {
                var idx = Array.IndexOf(lowered, alias.ToLowerInvariant());
                if (idx >= 0)
                {
                    indexMap[field] = idx;
                    break;
                }
            }
        }

        var missingRequired = target.RequiredFields.Where(f => !indexMap.ContainsKey(f)).ToList();

        var rows = new List<TEntity>(dataRows.Count);
        var skipReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        int skipped = 0;
        int parsed = dataRows.Count;

        if (missingRequired.Count == 0)
        {
            foreach (var rawRow in dataRows)
            {
                if (rawRow.Length < target.MinColumnCount)
                {
                    skipped++;
                    skipReasons["short_row"] = skipReasons.GetValueOrDefault("short_row", 0) + 1;
                    continue;
                }
                var entity = target.MapRow(rawRow, indexMap);
                if (entity is null)
                {
                    skipped++;
                    skipReasons["missing_required"] = skipReasons.GetValueOrDefault("missing_required", 0) + 1;
                    continue;
                }
                rows.Add(entity);
            }
        }

        sw.Stop();
        return new CsvParseResult<TEntity>
        {
            HeaderRaw = header,
            MappedFields = indexMap.Keys.ToArray(),
            Rows = rows,
            Parsed = parsed,
            Skipped = skipped,
            SkipReasons = skipReasons,
            MissingRequired = missingRequired,
            ElapsedMs = (int)sw.ElapsedMilliseconds,
        };
    }

    private static (List<string> Header, List<string[]> Rows) ParseCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();

        var lines = new List<string[]>();
        var field = new StringBuilder();
        var row = new List<string>();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }
            if (c == '"') { inQuotes = true; continue; }
            if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
                continue;
            }
            if (c == '\r') continue;
            if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                if (row.Count > 1 || (row.Count == 1 && row[0].Length > 0))
                    lines.Add(row.ToArray());
                row.Clear();
                continue;
            }
            field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Count > 1 || (row.Count == 1 && row[0].Length > 0))
                lines.Add(row.ToArray());
        }

        if (lines.Count == 0)
            return (new List<string>(), new List<string[]>());

        var header = lines[0].Select(h => h.Trim()).ToList();
        var data = lines.Skip(1).ToList();
        return (header, data);
    }
}
