using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.Services;

/// <summary>
/// Dựng profile FQC/OQC <b>từ thư viện hạng mục</b>, đúng hình dạng JSON mà
/// <c>QcProfileSeed</c> vẫn trả — nên mọi thứ phía sau (<c>ExtractProfileItemKeys</c>,
/// <c>ProfileKeyCount</c>, <c>ExtractItemText</c>, materialize item, UI) không
/// phải đổi một dòng nào.
///
/// <para><b>NGUYÊN TẮC (Henry, 2026-08-28):</b> IPQC · FQC · OQC dùng CÙNG bộ
/// hạng mục và CÙNG cấu trúc tab. Khác biệt duy nhất: FQC/OQC không kiểm lại
/// bảng vật tư. Thư viện đã sẵn sàng cho điều đó từ đầu — mọi dòng đều mang cờ
/// <c>Ipqc</c>/<c>Fqc</c>/<c>Oqc</c>, chỉ là đường FQC/OQC chưa bao giờ đọc
/// chúng: nó lấy hạng mục từ <c>QcProfileSeed</c>, một bản chép tay của tờ giấy
/// CCL-10-F6.</para>
///
/// <para><b>MỘT TẦNG, KHÔNG HAI.</b> IPQC chia hai tầng (chip công đoạn → tab
/// nhóm) vì nó kiểm TẠI MÁY, theo khu vực. FQC/OQC kiểm lô thành phẩm đã rời
/// chuyền — một lần, không đứng ở máy nào — nên section ở đây gom thẳng theo
/// <c>GroupLabel</c> (A·B·C·D·E), không chẻ theo process line. Bắt người kiểm
/// cuối bấm qua lại PRINT/CẮT cho cùng một lô trước mặt là bắt họ mô phỏng một
/// sự phân chia không còn tồn tại ở công đoạn của họ.</para>
///
/// <para><b>Bản EN đóng băng luôn</b> (<c>label_en</c>/<c>spec_en</c>/<c>method_en</c>)
/// lấy thẳng từ cột <c>*En</c> của thư viện — không phải tra lúc hiển thị.
/// Xem <see cref="QcProfileEnglish"/> và Nguyên tắc IV của hiến pháp.</para>
/// </summary>
public static class QcLibraryProfileBuilder
{
    /// <summary>Nguồn hạng mục, ghi vào trường <c>source</c> của snapshot để
    /// đọc hồ sơ cũ là biết nó dựng bằng đường nào.</summary>
    public const string Source = "CheckItemLibrary";

    /// <summary>
    /// Trả profile JSON, hoặc <c>"{}"</c> nếu không chọn được hạng mục nào —
    /// phía gọi lùi về <see cref="QcProfileSeed"/> chứ không dựng màn hình trống.
    /// </summary>
    /// <param name="rows">Thư viện đã lọc theo cờ stage (<c>Fqc</c> hoặc <c>Oqc</c>).</param>
    /// <param name="resolvedLines">QC line resolve từ routing.</param>
    /// <param name="kind">"FQC" hoặc "OQC" — chỉ dùng cho nhãn hiển thị.</param>
    public static string Build(
        IReadOnlyList<CheckItemLibrary>? rows,
        IReadOnlyList<string>? resolvedLines,
        string kind)
    {
        var sel = QcLineLibrarySelector.Select(rows, resolvedLines);
        if (sel.Count == 0) return "{}";

        // Gom theo GroupLabel, thứ tự A·B·C·D·E rồi Sort trong nhóm. Nhóm rỗng
        // nhãn xuống cuối để không đẩy các nhóm có tên lên sau nó.
        var groups = sel
            .GroupBy(s => (s.Row.GroupLabel ?? "").Trim())
            .OrderBy(g => string.IsNullOrEmpty(g.Key) ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var lines = resolvedLines is { Count: > 0 } ? string.Join(",", resolvedLines) : "";

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions
        {
            Indented = false,
            // Cùng lý do với QcProfileEnglish: snapshot là bằng chứng, tiếng Việt
            // phải đọc được bằng mắt khi mở DB.
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        }))
        {
            w.WriteStartObject();
            w.WriteString("name", $"{kind.ToUpperInvariant()} — theo thư viện hạng mục");
            w.WriteString("lines", lines);
            w.WriteString("source", Source);
            w.WriteStartArray("sections");

            foreach (var g in groups)
            {
                w.WriteStartObject();
                w.WriteString("id", string.IsNullOrEmpty(g.Key) ? "other" : g.Key);
                w.WriteString("title", string.IsNullOrEmpty(g.Key) ? "Khác" : g.Key);
                var titleEn = g.Select(x => x.Row.GroupLabelEn).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                if (!string.IsNullOrWhiteSpace(titleEn)) w.WriteString("title_en", titleEn);
                w.WriteStartArray("items");

                foreach (var s in g.OrderBy(x => x.Row.Sort).ThenBy(x => x.Row.ItemId, StringComparer.Ordinal))
                {
                    var r = s.Row;
                    w.WriteStartObject();
                    w.WriteString("key", r.ItemId);
                    Put(w, "label", Vi(r.ItemVi, r.ItemEn));
                    Put(w, "label_en", Blank(r.ItemEn) ? null : r.ItemEn);
                    Put(w, "spec", Vi(r.AcceptanceVi, r.AcceptanceEn));
                    Put(w, "spec_en", Blank(r.AcceptanceEn) ? null : r.AcceptanceEn);
                    Put(w, "method", r.Method);
                    Put(w, "method_en", Blank(r.MethodEn) ? CheckItemVocabularyEn.Method(r.Method) : r.MethodEn);
                    Put(w, "severity", r.Severity);
                    Put(w, "defect", r.DefectCode);
                    // Line đã resolve, KHÔNG phải ProcessLine của thư viện —
                    // cùng lý do đóng dấu ở IpqcLibraryMaterializer.
                    w.WriteString("line", s.Line);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());

        static bool Blank(string? x) => string.IsNullOrWhiteSpace(x);
        static string? Vi(string? vi, string? en) => Blank(vi) ? (Blank(en) ? null : en) : vi;
        static void Put(Utf8JsonWriter w, string name, string? v)
        {
            if (!string.IsNullOrWhiteSpace(v)) w.WriteString(name, v);
        }
    }
}
