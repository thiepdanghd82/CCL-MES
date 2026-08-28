using System.Text;
using System.Text.Json;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.Services;

/// <summary>
/// Phương án C — Bước 4 (lõi thuần). Dựng bộ hạng mục IPQC data-driven từ
/// subset <see cref="CheckItemLibrary"/> (đã lọc theo process line resolve từ
/// routing + QcStage=IPQC + Active) → trả về:
///   1. <c>ProfileSnapshotJson</c> (shape giống <see cref="QcProfileSeed"/>:
///      {name, sections:[{id,title,items:[{key,label,spec,method,severity,defect,line}]}]})
///      để FREEZE vào <see cref="WoIpqcCheck.ItemsProfileSnapshotJson"/>.
///   2. Danh sách <see cref="WoIpqcCheckItem"/> (Status=Pending) materialize vào check.
///
/// Thuần + tĩnh — controller (Bước 4) lọc thư viện + gọi đây + 1 SaveChanges
/// atomic. Sửa thư viện sau đó KHÔNG hồi tố (snapshot đã đóng băng).
/// </summary>
public static class IpqcLibraryMaterializer
{
    // Thứ tự process line ổn định (khớp QcLineResolver).
    private static readonly string[] LineOrder =
        { QcLineResolver.Label, QcLineResolver.Digital, QcLineResolver.Silk, QcLineResolver.PressCnc };

    public sealed record Result(string ProfileSnapshotJson, IReadOnlyList<WoIpqcCheckItem> Items);

    /// <summary>
    /// Dựng snapshot + items từ thư viện đã lọc. <paramref name="resolvedLines"/>
    /// quyết định thứ tự nhóm. Item rỗng → snapshot tối thiểu (sections rỗng) +
    /// items rỗng (caller tự quyết fallback về 4 slot legacy).
    /// </summary>
    public static Result Build(
        IReadOnlyList<CheckItemLibrary> libraryRows,
        IReadOnlyList<string> resolvedLines)
        => Build(
            QcLineLibrarySelector.Select(libraryRows, resolvedLines),
            resolvedLines);

    /// <summary>
    /// Dạng đầy đủ: mỗi hạng mục đi kèm LINE ĐÃ RESOLVE để đóng dấu lên nó.
    ///
    /// <para>Cần bản này vì hạng mục nạp qua đường CỜ tick-box (vd
    /// <c>PRESS_CNC → SheetCut</c>) thuộc về một <c>ProcessLine</c> KHÁC trong
    /// thư viện (LABEL), nhưng phải được đóng băng với line đã resolve —
    /// UI chia chip công đoạn theo đúng trường đó. Xem
    /// <see cref="QcLineLibrarySelector"/>.</para>
    /// </summary>
    public static Result Build(
        IReadOnlyList<QcLineLibrarySelector.Selection> selections,
        IReadOnlyList<string> resolvedLines)
    {
        var rows = (selections ?? Array.Empty<QcLineLibrarySelector.Selection>())
            .Where(s => s.Row.Active)
            .OrderBy(s => LineIndex(s.Line))
            .ThenBy(s => s.Row.Sort)
            .ThenBy(s => s.Row.ItemId, StringComparer.Ordinal)
            .ToList();

        var items = new List<WoIpqcCheckItem>(rows.Count);
        var sort = 0;
        foreach (var sel in rows)
        {
            var r = sel.Row;
            items.Add(new WoIpqcCheckItem
            {
                ItemKey = r.ItemId,
                ProcessLine = sel.Line,
                GroupLabel = r.GroupLabel,
                Label = string.IsNullOrWhiteSpace(r.ItemVi) ? r.ItemEn : r.ItemVi,
                AcceptanceCriteria = string.IsNullOrWhiteSpace(r.AcceptanceVi) ? r.AcceptanceEn : r.AcceptanceVi,
                Method = r.Method,

                // Bản EN đóng băng CÙNG LÚC với bản VI ngay trên. Không tra cứu
                // lúc hiển thị: hồ sơ đã ký đọc lại sau này phải ra đúng chữ
                // người vận hành đã thấy, ở cả hai ngôn ngữ. Thiếu bản EN thì
                // để null — UI rơi về bản VI chứ không bao giờ để ô trống.
                GroupLabelEn = Blank(r.GroupLabelEn) ? CheckItemVocabularyEn.Group(r.GroupLabel) : r.GroupLabelEn,
                LabelEn = Blank(r.ItemEn) ? null : r.ItemEn,
                AcceptanceCriteriaEn = Blank(r.AcceptanceEn) ? null : r.AcceptanceEn,
                MethodEn = Blank(r.MethodEn) ? CheckItemVocabularyEn.Method(r.Method) : r.MethodEn,
                Severity = r.Severity,
                DefectCode = r.DefectCode,
                // IPQC first-article (Q2) — freeze the library CheckType so the
                // 3-tab stepper (Visual / Dimension / Function) stays stable.
                CheckType = r.CheckType,
                Status = IpqcCheckStatus.Pending,
                Sort = (sort += 10),
            });
        }

        return new Result(BuildSnapshotJson(rows, resolvedLines), items);
    }

    /// <summary>Rỗng/whitespace ⇒ coi như KHÔNG có bản dịch. Chuỗi rỗng nguy
    /// hiểm hơn null: nó lọt qua mọi phép kiểm null và làm UI hiển thị ô trắng
    /// thay vì rơi về bản VI.</summary>
    private static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);

    /// <summary>Snapshot JSON theo nhóm GroupLabel (giữ shape QcProfileSeed để
    /// rollup/UI tái dùng). Đóng băng đúng-thời-điểm.</summary>
    public static string BuildSnapshotJson(
        IReadOnlyList<CheckItemLibrary> rows,
        IReadOnlyList<string> resolvedLines)
        => BuildSnapshotJson(
            QcLineLibrarySelector.Select(rows, resolvedLines),
            resolvedLines);

    /// <inheritdoc cref="BuildSnapshotJson(IReadOnlyList{CheckItemLibrary}, IReadOnlyList{string})"/>
    public static string BuildSnapshotJson(
        IReadOnlyList<QcLineLibrarySelector.Selection> rows,
        IReadOnlyList<string> resolvedLines)
    {
        var lines = resolvedLines is { Count: > 0 }
            ? string.Join(",", resolvedLines)
            : string.Join(",", rows.Select(s => s.Line).Distinct());

        var opts = new JsonWriterOptions { Indented = false };
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, opts))
        {
            w.WriteStartObject();
            w.WriteString("name", "IPQC — auto-sync theo routing");
            w.WriteString("lines", lines);
            w.WriteString("source", "CheckItemLibrary");
            w.WriteStartArray("sections");

            // Nhóm theo LINE ĐÃ RESOLVE (không phải ProcessLine của thư viện) —
            // cùng lý do với việc đóng dấu ở Build: hạng mục nạp qua cờ tick-box
            // phải nằm dưới công đoạn mà routing thật sự đi qua.
            foreach (var grp in rows
                .GroupBy(s => (Line: s.Line, Group: s.Row.GroupLabel)))
            {
                w.WriteStartObject();
                w.WriteString("id", $"{grp.Key.Line}:{grp.Key.Group}");
                w.WriteString("line", grp.Key.Line);
                w.WriteString("title", grp.Key.Group);
                w.WriteStartArray("items");
                foreach (var sel in grp)
                {
                    var r = sel.Row;
                    w.WriteStartObject();
                    w.WriteString("key", r.ItemId);
                    w.WriteString("label", string.IsNullOrWhiteSpace(r.ItemVi) ? r.ItemEn : r.ItemVi);
                    w.WriteString("spec", string.IsNullOrWhiteSpace(r.AcceptanceVi) ? r.AcceptanceEn : r.AcceptanceVi);
                    if (!string.IsNullOrWhiteSpace(r.Method)) w.WriteString("method", r.Method);
                    if (!string.IsNullOrWhiteSpace(r.Severity)) w.WriteString("severity", r.Severity);
                    if (!string.IsNullOrWhiteSpace(r.DefectCode)) w.WriteString("defect", r.DefectCode);
                    w.WriteString("line", sel.Line);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static int LineIndex(string? line)
    {
        for (var i = 0; i < LineOrder.Length; i++)
            if (string.Equals(LineOrder[i], line, StringComparison.Ordinal)) return i;
        return LineOrder.Length; // unknown line cuối
    }
}
