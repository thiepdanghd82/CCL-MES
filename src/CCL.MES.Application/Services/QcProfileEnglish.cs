using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace CCL.MES.Application.Services;

/// <summary>
/// Bản EN cho profile FQC/OQC, và bộ làm giàu nhét nó vào snapshot NGAY TRƯỚC
/// KHI đóng băng.
///
/// <para>VÌ SAO: <see cref="QcProfileSeed"/> là JSON nhúng trong C# do Ops đọc
/// và sửa bằng tiếng Việt, chỉ có <c>label</c> · <c>spec</c> · <c>method</c>.
/// Bắt Ops nhân đôi mọi dòng thành <c>label_en</c>/<c>spec_en</c>/<c>method_en</c>
/// là mời bản dịch lệch nhau giữa các dòng cùng nghĩa — 31 dòng item nhưng chỉ
/// 25 nhãn, 14 spec và 4 method KHÁC NHAU. Để bản dịch ở đây thì mỗi thuật ngữ
/// có ĐÚNG MỘT bản EN và đi qua code review.</para>
///
/// <para>ĐÓNG BĂNG, KHÔNG TRA LÚC HIỂN THỊ: <see cref="Enrich"/> chạy trong
/// <c>QcProfileResolver.ResolveSnapshot</c> — điểm thắt duy nhất mà mọi đường
/// freeze đi qua. Từ đó bản EN nằm yên trong <c>WoQcChecks.ProfileSnapshotJson</c>
/// cùng bản VI, bất biến y như nhau. Sửa bảng tra ở đây KHÔNG hồi tố hồ sơ đã ký
/// — đúng Nguyên tắc IV của hiến pháp, và cùng khuôn với
/// <see cref="CheckItemVocabularyEn"/> bên IPQC.</para>
///
/// <para>Chuỗi đã trung tính ngôn ngữ thì KHÔNG có mặt ở đây: ký hiệu hoá học
/// (Pb · Cd · Hg · Cr · Cl · S · Sb · Sn), <c>Dim 1..4</c>, ngưỡng số
/// (<c>0</c> · <c>&lt; 100</c> · <c>± 0.5</c>), và các từ vốn đã là tiếng Anh
/// (<c>OK</c> · <c>Visual</c> · <c>Signature</c> · <c>QA Manager</c>). Dịch
/// chúng chỉ thêm nhiễu.</para>
/// </summary>
public static class QcProfileEnglish
{
    /// <summary>VI → EN cho cả ba trường. Một bảng chung vì cùng một chuỗi có
    /// thể xuất hiện ở nhãn lẫn spec (vd "Theo spec").</summary>
    public static readonly IReadOnlyDictionary<string, string> Vi2En =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── nhãn hạng mục ──
            ["Chấp nhận / Loại bỏ"] = "Accept / Reject",
            ["Không rách / nếp / bẩn / mẻ"] = "No tear / crease / dirt / chip",
            ["Logo & vùng in"] = "Logo & print area",
            ["Lỗi khác"] = "Other defects",
            ["Lỗi khác (báo IPQC)"] = "Other defects (report to IPQC)",
            ["Màu sắc"] = "Colour",
            ["Màu sắc đúng mẫu chuẩn"] = "Colour matches the master sample",
            ["Người kiểm (Prepared)"] = "Inspector (Prepared)",
            ["Phê duyệt (Approved)"] = "Approved by",
            ["Vị trí mối nối"] = "Joint position",
            ["Xác nhận (Confirm)"] = "Confirmed by",

            // ── tiêu chí chấp nhận ──
            ["Theo bản vẽ"] = "Per drawing",
            ["Theo spec"] = "Per spec",

            // ── phương pháp ──
            ["Phán định cuối"] = "Final judgment",
            ["Visual + Spectro nếu nghi ngờ"] = "Visual + spectro if in doubt",
        };

    /// <summary>Trả bản EN, hoặc <c>null</c> nếu chuỗi đã trung tính / chưa có
    /// bản dịch — phía gọi tự rơi về bản VI, không bao giờ để ô trống.</summary>
    public static string? Translate(string? vi)
    {
        if (string.IsNullOrWhiteSpace(vi)) return null;
        return Vi2En.TryGetValue(vi.Trim(), out var en) ? en : null;
    }

    /// <summary>
    /// Chép <paramref name="profileJson"/> và thêm <c>label_en</c> ·
    /// <c>spec_en</c> · <c>method_en</c> vào mỗi item có bản dịch.
    ///
    /// <para>Giữ NGUYÊN mọi trường sẵn có (kể cả trường lạ mà bản này chưa biết)
    /// — snapshot là bằng chứng, không phải cấu trúc do lớp này sở hữu. Nếu item
    /// ĐÃ có <c>*_en</c> (Ops nhập tay, hoặc override theo mã hàng) thì giữ
    /// nguyên, KHÔNG đè.</para>
    ///
    /// <para>JSON hỏng hoặc không đúng shape ⇒ trả lại nguyên bản. Làm giàu là
    /// việc phụ; làm hỏng snapshot vì nó thì mất nhiều hơn được.</para>
    /// </summary>
    public static string Enrich(string? profileJson)
    {
        if (string.IsNullOrWhiteSpace(profileJson) || profileJson == "{}") return profileJson ?? "{}";

        try
        {
            using var doc = JsonDocument.Parse(profileJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return profileJson;

            using var ms = new MemoryStream();
            // Encoder PHẢI cho tiếng Việt đi thẳng. Mặc định Utf8JsonWriter
            // escape mọi ký tự ngoài ASCII: "không" → "kh\u00F4ng". Snapshot là
            // BẰNG CHỨNG — người kiểm toán mở DB ra phải đọc được chữ.
            //
            // ĐO ĐƯỢC, không phải phỏng đoán: emoji ngoài BMP (🔍 trong trường
            // "icon" trang trí của section) VẪN bị escape thành cặp surrogate
            // "\uD83D\uDD0D" — cả UnicodeRanges.All LẪN UnsafeRelaxedJsonEscaping
            // đều vậy. Chấp nhận: JSON parse lại vẫn ra đúng 🔍, và thứ cần đọc
            // bằng mắt là nhãn/tiêu chí tiếng Việt thì đã đọc được.
            //
            // Chọn Create(UnicodeRanges.All) chứ không UnsafeRelaxed vì cùng kết
            // quả cho nội dung này nhưng giữ escape cho < > & — không có lý do
            // đánh đổi phòng thủ đó lấy con số không.
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions
            {
                Indented = false,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            }))
                WriteRoot(doc.RootElement, w);
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (JsonException)
        {
            return profileJson;
        }
    }

    private static void WriteRoot(JsonElement root, Utf8JsonWriter w)
    {
        w.WriteStartObject();
        foreach (var p in root.EnumerateObject())
        {
            if (p.NameEquals("sections") && p.Value.ValueKind == JsonValueKind.Array)
            {
                w.WriteStartArray("sections");
                foreach (var section in p.Value.EnumerateArray()) WriteSection(section, w);
                w.WriteEndArray();
            }
            else p.WriteTo(w);
        }
        w.WriteEndObject();
    }

    private static void WriteSection(JsonElement section, Utf8JsonWriter w)
    {
        if (section.ValueKind != JsonValueKind.Object) { section.WriteTo(w); return; }

        w.WriteStartObject();
        foreach (var p in section.EnumerateObject())
        {
            if (p.NameEquals("items") && p.Value.ValueKind == JsonValueKind.Array)
            {
                w.WriteStartArray("items");
                foreach (var item in p.Value.EnumerateArray()) WriteItem(item, w);
                w.WriteEndArray();
            }
            else p.WriteTo(w);
        }
        w.WriteEndObject();
    }

    private static void WriteItem(JsonElement item, Utf8JsonWriter w)
    {
        if (item.ValueKind != JsonValueKind.Object) { item.WriteTo(w); return; }

        w.WriteStartObject();
        var da = false; // đã có label_en?
        var db = false; // spec_en?
        var dc = false; // method_en?
        foreach (var p in item.EnumerateObject())
        {
            p.WriteTo(w);
            if (p.NameEquals("label_en")) da = true;
            if (p.NameEquals("spec_en")) db = true;
            if (p.NameEquals("method_en")) dc = true;
        }

        Add(item, w, "label", "label_en", da);
        Add(item, w, "spec", "spec_en", db);
        Add(item, w, "method", "method_en", dc);
        w.WriteEndObject();
    }

    private static void Add(JsonElement item, Utf8JsonWriter w, string src, string dst, bool daCo)
    {
        if (daCo) return;
        if (!item.TryGetProperty(src, out var v) || v.ValueKind != JsonValueKind.String) return;
        var en = Translate(v.GetString());
        if (en is not null) w.WriteString(dst, en);
    }
}
