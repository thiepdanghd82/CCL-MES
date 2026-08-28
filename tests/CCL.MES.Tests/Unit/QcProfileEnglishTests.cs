using System.Text.Json;
using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// i18n bảng FQC/OQC — bệnh ở đây KHÁC bệnh IPQC (L60).
///
/// <para>IPQC: nhãn CÓ được mang ra UI nhưng bị đóng băng ở một ngôn ngữ.
/// FQC/OQC: nhãn KHÔNG hề được mang ra — <c>WoQcCheckItems</c> chỉ có
/// <c>ItemKey</c>, DTO cũng chỉ có <c>ItemKey</c>, nên UI render thẳng
/// <c>color_match</c> · <c>no_tear</c> · <c>accept_reject</c> cho người vận
/// hành. Nhãn tiếng Việt CÓ trong <c>ProfileSnapshotJson</c> — snapshot còn
/// được gửi nguyên si xuống client — chỉ là không ai đọc nó.</para>
///
/// <para>Khoá: (a) bảng tra VI→EN đúng và không rỗng; (b) <c>Enrich</c> nhét
/// bản EN vào snapshot lúc đóng băng mà KHÔNG phá cấu trúc và KHÔNG đè bản
/// Ops đã nhập; (c) JSON hỏng không làm vỡ trang.</para>
/// </summary>
public sealed class QcProfileEnglishTests
{
    private const string Fqc = """
    {"name":"FQC","sections":[
      {"id":"s1","title":"Ngoại quan","items":[
        {"key":"no_tear","label":"Không rách / nếp / bẩn / mẻ","spec":"OK","method":"Visual"},
        {"key":"color_match","label":"Màu sắc đúng mẫu chuẩn","spec":"OK","method":"Visual + Spectro nếu nghi ngờ"},
        {"key":"dim_1","label":"Dim 1","spec":"± 0.5","method":"Visual"}
      ]}
    ]}
    """;

    private static JsonElement Item(string json, int idx)
        => JsonDocument.Parse(json).RootElement
            .GetProperty("sections")[0].GetProperty("items")[idx];

    private static string? Prop(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) ? v.GetString() : null;

    // ── (a) bảng tra ─────────────────────────────────────────────────────

    [Fact]
    public void Bang_tra_khong_co_khoa_rong_hay_gia_tri_rong()
    {
        Assert.NotEmpty(QcProfileEnglish.Vi2En);
        foreach (var (vi, en) in QcProfileEnglish.Vi2En)
        {
            Assert.False(string.IsNullOrWhiteSpace(vi));
            Assert.False(string.IsNullOrWhiteSpace(en));
            Assert.Equal(vi.Trim(), vi);
            Assert.Equal(en.Trim(), en);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Dim 1")]          // đã trung tính — KHÔNG dịch
    [InlineData("OK")]             // đã là tiếng Anh
    [InlineData("Pb (Lead)")]      // ký hiệu hoá học
    public void Chuoi_trung_tinh_hoac_khong_biet_thi_tra_null(string? vi)
        => Assert.Null(QcProfileEnglish.Translate(vi));

    [Fact]
    public void Dich_dung_chuoi_co_trong_bang()
    {
        Assert.Equal("Colour matches the master sample",
            QcProfileEnglish.Translate("Màu sắc đúng mẫu chuẩn"));
        Assert.Equal("Per drawing", QcProfileEnglish.Translate("Theo bản vẽ"));
        Assert.Equal("Final judgment", QcProfileEnglish.Translate("Phán định cuối"));
    }

    // ── (b) làm giàu snapshot ────────────────────────────────────────────

    [Fact]
    public void Enrich_them_ban_EN_va_GIU_NGUYEN_ban_VI()
    {
        var it = Item(QcProfileEnglish.Enrich(Fqc), 1);

        Assert.Equal("Màu sắc đúng mẫu chuẩn", Prop(it, "label"));
        Assert.Equal("Colour matches the master sample", Prop(it, "label_en"));
        Assert.Equal("Visual + spectro if in doubt", Prop(it, "method_en"));
    }

    [Fact]
    public void Chuoi_trung_tinh_KHONG_sinh_truong_en_thua()
    {
        // "Dim 1" · "± 0.5" · "Visual" không cần dịch ⇒ không thêm *_en.
        var it = Item(QcProfileEnglish.Enrich(Fqc), 2);

        Assert.Equal("Dim 1", Prop(it, "label"));
        Assert.Null(Prop(it, "label_en"));
        Assert.Null(Prop(it, "spec_en"));
        Assert.Null(Prop(it, "method_en"));
    }

    [Fact]
    public void Ban_EN_Ops_da_nhap_tay_KHONG_bi_de()
    {
        const string cust = """
        {"sections":[{"items":[
          {"key":"color_match","label":"Màu sắc đúng mẫu chuẩn","label_en":"Ops wording wins"}
        ]}]}
        """;
        Assert.Equal("Ops wording wins", Prop(Item(QcProfileEnglish.Enrich(cust), 0), "label_en"));
    }

    [Fact]
    public void Enrich_giu_nguyen_moi_truong_khac_va_cau_truc()
    {
        var root = JsonDocument.Parse(QcProfileEnglish.Enrich(Fqc)).RootElement;

        Assert.Equal("FQC", root.GetProperty("name").GetString());
        Assert.Equal("s1", root.GetProperty("sections")[0].GetProperty("id").GetString());
        Assert.Equal(3, root.GetProperty("sections")[0].GetProperty("items").GetArrayLength());
        // Đếm hạng mục phải KHÔNG đổi — nếu đổi là làm giàu đã phá snapshot.
        Assert.Equal(QcProfileSeed.CountItems(Fqc), QcProfileSeed.CountItems(QcProfileEnglish.Enrich(Fqc)));
    }

    // ── (c) đầu vào hỏng ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("không phải json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"sections":"không phải mảng"}""")]
    public void Dau_vao_hong_thi_tra_lai_nguyen_ban_chu_khong_no(string? json)
    {
        var ra = QcProfileEnglish.Enrich(json);
        Assert.Equal(json ?? "{}", ra);
    }

    [Fact]
    public void Seed_that_lam_giau_duoc_ca_FQC_lan_OQC()
    {
        foreach (var kind in new[] { "fqc", "oqc" })
        {
            var goc = QcProfileSeed.GetDefaultProfileJson(kind);
            Assert.False(string.IsNullOrWhiteSpace(goc));

            var giau = QcProfileEnglish.Enrich(goc);
            Assert.Equal(QcProfileSeed.CountItems(goc), QcProfileSeed.CountItems(giau));
            Assert.Contains("label_en", giau);
        }
    }
}
