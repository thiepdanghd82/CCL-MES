using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// i18n bảng IPQC — khoá ba mắt xích của chuỗi "đổi được ngôn ngữ":
///
///   1. TỪ VỰNG   — <see cref="CheckItemVocabularyEn"/> phủ đúng tập chuỗi VI
///                  mà seed thật dùng. Khoá sai một dấu = im lặng rơi về VI.
///   2. ĐÓNG BĂNG — <see cref="IpqcLibraryMaterializer"/> ghi bản EN CÙNG LÚC
///                  với bản VI, không tra cứu lúc hiển thị.
///   3. CHỌN      — <c>IpqcViewItem.*For(bool)</c> rơi về VI khi thiếu EN.
///                  Mắt xích 3 nằm ở project Hybrid (IpqcViewItemLanguageTests)
///                  vì CCL.MES.Tests không tham chiếu CCL.MES.Shared.
///
/// Vì sao đáng khoá: bệnh cũ là <c>Label = ItemVi ?? ItemEn</c> — luôn chọn
/// tiếng Việt rồi ĐÓNG BĂNG lựa chọn đó, nên đổi cờ EN trên topbar không đổi
/// được một chữ nào trong bảng. Không test nào bắt được vì bảng vẫn có chữ.
/// </summary>
public sealed class IpqcItemEnglishTests
{
    // ── 1. TỪ VỰNG ───────────────────────────────────────────────────────

    [Fact]
    public void Moi_GroupLabel_trong_seed_SETTING_deu_co_ban_EN()
    {
        var thieu = SettingLibrarySeed.Items()
            .Select(i => i.GroupLabel)
            .Distinct(StringComparer.Ordinal)
            .Where(g => CheckItemVocabularyEn.Group(g) is null)
            .ToList();

        Assert.True(thieu.Count == 0,
            "GroupLabel dùng trong SettingLibrarySeed nhưng thiếu bản EN trong " +
            "CheckItemVocabularyEn (UI sẽ im lặng hiện tiếng Việt khi chọn EN): " +
            string.Join(" · ", thieu));
    }

    [Fact]
    public void Khoa_va_gia_tri_tu_vung_khong_rong_va_da_trim()
    {
        foreach (var (map, ten) in new[]
                 {
                     (CheckItemVocabularyEn.GroupEn, nameof(CheckItemVocabularyEn.GroupEn)),
                     (CheckItemVocabularyEn.MethodEn, nameof(CheckItemVocabularyEn.MethodEn)),
                 })
        {
            foreach (var (vi, en) in map)
            {
                Assert.False(string.IsNullOrWhiteSpace(vi), $"{ten}: có khoá rỗng");
                Assert.False(string.IsNullOrWhiteSpace(en), $"{ten}: '{vi}' ánh xạ sang chuỗi rỗng");
                Assert.Equal(vi.Trim(), vi);
                Assert.Equal(en.Trim(), en);
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("chuỗi không có trong từ điển")]
    public void Tra_khong_thay_thi_tra_null_chu_khong_tra_rong(string? vi)
    {
        Assert.Null(CheckItemVocabularyEn.Group(vi));
        Assert.Null(CheckItemVocabularyEn.Method(vi));
    }

    // ── 2. ĐÓNG BĂNG ─────────────────────────────────────────────────────

    private static CheckItemLibrary LibRow() => new()
    {
        ItemId = "LBL-A1",
        ProcessLine = "LABEL",
        GroupLabel = "A·Ngoại quan",
        Code = "A1",
        ItemVi = "Đúng nội dung in (text/logo/mã/seri)",
        ItemEn = "Print content correct (text/logo/code/serial)",
        AcceptanceVi = "Nội dung, chữ, logo, mã, seri ĐÚNG so spec/mẫu chuẩn",
        AcceptanceEn = "Content, text, logo, code and serial match the spec/master sample",
        Method = "Soi mắt",
        Ipqc = true,
        Active = true,
    };

    [Fact]
    public void Materializer_dong_bang_CA_HAI_ngon_ngu()
    {
        var r = IpqcLibraryMaterializer.Build(new[] { LibRow() }, new[] { "LABEL" });
        var it = Assert.Single(r.Items);

        // Bản VI giữ nguyên hành vi cũ — không được đổi.
        Assert.Equal("Đúng nội dung in (text/logo/mã/seri)", it.Label);
        Assert.Equal("A·Ngoại quan", it.GroupLabel);
        Assert.Equal("Soi mắt", it.Method);

        // Bản EN đóng băng cùng lúc, KHÔNG null.
        Assert.Equal("Print content correct (text/logo/code/serial)", it.LabelEn);
        Assert.Equal("Content, text, logo, code and serial match the spec/master sample",
            it.AcceptanceCriteriaEn);
        Assert.Equal("A·Appearance", it.GroupLabelEn);
        Assert.Equal("Visual inspection", it.MethodEn);
    }

    [Fact]
    public void Cot_EN_cua_thu_vien_thang_bang_tu_dien_khi_ca_hai_cung_co()
    {
        // Ops nhập tay bản EN vào thư viện ⇒ ưu tiên bản đó, từ điển chỉ là
        // đường lùi. Nếu đảo thứ tự này thì bản Ops nhập bị nuốt im lặng.
        var row = LibRow();
        row.MethodEn = "Visual check (Ops wording)";
        row.GroupLabelEn = "A·Look and feel";

        var it = Assert.Single(IpqcLibraryMaterializer.Build(new[] { row }, new[] { "LABEL" }).Items);
        Assert.Equal("Visual check (Ops wording)", it.MethodEn);
        Assert.Equal("A·Look and feel", it.GroupLabelEn);
    }

    [Fact]
    public void Thu_vien_thieu_ban_EN_thi_de_null_chu_khong_de_chuoi_rong()
    {
        // Chuỗi rỗng nguy hiểm hơn null: nó lọt mọi phép kiểm null và làm UI
        // hiện ô trắng thay vì rơi về bản VI.
        var row = LibRow();
        row.ItemEn = "";
        row.AcceptanceEn = "   ";
        row.Method = "phương pháp lạ không có trong từ điển";

        var it = Assert.Single(IpqcLibraryMaterializer.Build(new[] { row }, new[] { "LABEL" }).Items);
        Assert.Null(it.LabelEn);
        Assert.Null(it.AcceptanceCriteriaEn);
        Assert.Null(it.MethodEn);
    }
}
