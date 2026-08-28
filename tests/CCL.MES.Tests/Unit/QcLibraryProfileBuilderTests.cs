using System.Text.Json;
using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Nguyên tắc Henry 2026-08-28: <b>IPQC · FQC · OQC dùng CÙNG bộ hạng mục và
/// CÙNG cấu trúc tab</b>; khác biệt duy nhất là FQC/OQC không kiểm lại vật tư.
///
/// <para>Trước đó có HAI bộ máy QC song song: IPQC lấy hạng mục từ
/// <c>CheckItemLibrary</c> (data-driven), còn FQC/OQC lấy từ
/// <c>QcProfileSeed</c> — một bản chép tay tờ giấy CCL-10-F6 trộn bốn loại thứ
/// khác nhau vào cùng một lưới OK/NG: metadata đầu form (khách hàng, mã SP, lô),
/// hạng mục kiểm thật, chỉ tiêu RoHS, và ô chữ ký. Người vận hành phải bấm OK/NG
/// cho dòng "Khách hàng".</para>
///
/// <para>Thư viện đã sẵn sàng cho việc hợp nhất từ đầu — mọi dòng đều mang cờ
/// <c>Ipqc</c>/<c>Fqc</c>/<c>Oqc</c>, chỉ là đường FQC/OQC chưa bao giờ đọc.</para>
/// </summary>
public sealed class QcLibraryProfileBuilderTests
{
    private static CheckItemLibrary Row(
        string id, string line, string group, string vi, string en,
        bool ipqc = true, bool fqc = true, bool oqc = true, int sort = 0) => new()
    {
        ItemId = id, ProcessLine = line, GroupLabel = group, GroupLabelEn = group + " (EN)",
        Code = id, ItemVi = vi, ItemEn = en,
        AcceptanceVi = "tiêu chí " + id, AcceptanceEn = "spec " + id,
        Method = "Soi mắt", MethodEn = "Visual inspection",
        Ipqc = ipqc, Fqc = fqc, Oqc = oqc, Active = true, Sort = sort,
    };

    /// <summary>Thư viện thu nhỏ đúng hình dạng thật: LABEL có 4 nhóm; nhóm
    /// E·RoHS là <c>ProcessLine="ALL"</c> và CHỈ bật cờ OQC.</summary>
    private static List<CheckItemLibrary> Lib() =>
    [
        Row("LBL-A1", "LABEL", "A·Ngoại quan", "Ngoại quan", "Appearance", sort: 10),
        Row("LBL-A2", "LABEL", "A·Ngoại quan", "Xước", "Scratch", sort: 20),
        Row("LBL-B1", "LABEL", "B·Kích thước", "Kích thước", "Dimension", sort: 30),
        Row("SLK-A1", "SILK", "A·Ngoại quan", "Lụa", "Silk", sort: 10),
        Row("pb_ppm", RohsLibrarySeed.AllLines, RohsLibrarySeed.Group, "Pb (Lead)", "Pb (Lead)",
            ipqc: false, fqc: false, oqc: true, sort: 60),
    ];

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;

    private static List<string> SectionTitles(string json) =>
        Root(json).GetProperty("sections").EnumerateArray()
            .Select(s => s.GetProperty("title").GetString()!).ToList();

    private static List<string> Keys(string json) =>
        Root(json).GetProperty("sections").EnumerateArray()
            .SelectMany(s => s.GetProperty("items").EnumerateArray())
            .Select(i => i.GetProperty("key").GetString()!).ToList();

    // ── nguyên tắc: cùng hạng mục ────────────────────────────────────────

    [Fact]
    public void FQC_lay_dung_bo_hang_muc_nhu_IPQC()
    {
        var lib = Lib();
        var fqc = QcLibraryProfileBuilder.Build(lib.Where(r => r.Fqc).ToList(), new[] { "LABEL" }, "FQC");
        var ipqcItems = QcLineLibrarySelector
            .Select(lib.Where(r => r.Ipqc).ToList(), new[] { "LABEL" })
            .Select(s => s.Row.ItemId).OrderBy(x => x);

        Assert.Equal(ipqcItems, Keys(fqc).OrderBy(x => x));
    }

    [Fact]
    public void OQC_bang_bo_cua_FQC_CONG_nhom_E_RoHS()
    {
        var lib = Lib();
        var fqc = Keys(QcLibraryProfileBuilder.Build(lib.Where(r => r.Fqc).ToList(), new[] { "LABEL" }, "FQC"));
        var oqc = Keys(QcLibraryProfileBuilder.Build(lib.Where(r => r.Oqc).ToList(), new[] { "LABEL" }, "OQC"));

        Assert.Equal(fqc.Concat(new[] { "pb_ppm" }).OrderBy(x => x), oqc.OrderBy(x => x));
        Assert.Contains(RohsLibrarySeed.Group, SectionTitles(
            QcLibraryProfileBuilder.Build(lib.Where(r => r.Oqc).ToList(), new[] { "LABEL" }, "OQC")));
    }

    [Fact]
    public void Nhom_E_RoHS_KHONG_lot_vao_IPQC_hay_FQC()
    {
        var lib = Lib();
        foreach (var rows in new[] { lib.Where(r => r.Ipqc).ToList(), lib.Where(r => r.Fqc).ToList() })
        {
            var json = QcLibraryProfileBuilder.Build(rows, new[] { "LABEL" }, "FQC");
            Assert.DoesNotContain("pb_ppm", Keys(json));
            Assert.DoesNotContain(RohsLibrarySeed.Group, SectionTitles(json));
        }
    }

    [Fact]
    public void RoHS_ap_cho_MOI_dong_san_pham_du_thu_vien_khong_co_dong_SILK()
    {
        var lib = Lib();
        var oqc = QcLibraryProfileBuilder.Build(lib.Where(r => r.Oqc).ToList(), new[] { "SILK" }, "OQC");

        Assert.Contains("pb_ppm", Keys(oqc));   // ProcessLine = "ALL"
        Assert.Contains("SLK-A1", Keys(oqc));
        Assert.DoesNotContain("LBL-A1", Keys(oqc));
    }

    // ── nguyên tắc: cùng cấu trúc, MỘT tầng ──────────────────────────────

    [Fact]
    public void Section_gom_theo_NHOM_chu_khong_che_theo_cong_doan()
    {
        // Cách 2 (Henry chốt): FQC/OQC kiểm lô đã rời chuyền nên không có tầng
        // chip công đoạn. Section = GroupLabel, xếp A → B → … → E.
        var lib = Lib();
        var oqc = QcLibraryProfileBuilder.Build(lib.Where(r => r.Oqc).ToList(),
            new[] { "LABEL", "PRESS_CNC" }, "OQC");

        var titles = SectionTitles(oqc);
        Assert.Equal(titles.OrderBy(x => x, StringComparer.Ordinal), titles);
        Assert.All(titles, t => Assert.DoesNotContain("LABEL", t));
        Assert.All(titles, t => Assert.DoesNotContain("PRESS_CNC", t));
    }

    [Fact]
    public void Moi_hang_muc_mang_du_nhan_tieu_chi_phuong_phap_ca_hai_ngon_ngu()
    {
        var lib = Lib();
        var it = Root(QcLibraryProfileBuilder.Build(lib.Where(r => r.Fqc).ToList(), new[] { "LABEL" }, "FQC"))
            .GetProperty("sections")[0].GetProperty("items")[0];

        Assert.Equal("Ngoại quan", it.GetProperty("label").GetString());
        Assert.Equal("Appearance", it.GetProperty("label_en").GetString());
        Assert.Equal("tiêu chí LBL-A1", it.GetProperty("spec").GetString());
        Assert.Equal("Soi mắt", it.GetProperty("method").GetString());
        Assert.Equal("Visual inspection", it.GetProperty("method_en").GetString());
        // Line ĐÃ RESOLVE được đóng dấu, không phải ProcessLine của thư viện.
        Assert.Equal("LABEL", it.GetProperty("line").GetString());
    }

    [Fact]
    public void Tieng_Viet_khong_bi_escape_trong_snapshot()
    {
        var json = QcLibraryProfileBuilder.Build(Lib().Where(r => r.Fqc).ToList(), new[] { "LABEL" }, "FQC");
        Assert.Contains("Ngoại quan", json);
        Assert.DoesNotContain("Ngo\\u1EA1i", json);
    }

    // ── biên: phải lùi về đường cũ chứ không dựng màn hình trống ──────────

    [Theory]
    [InlineData("FINISHING")]   // line chưa có thư viện
    [InlineData("DIGITAL")]
    public void Line_khong_chon_duoc_hang_muc_nao_thi_tra_rong_de_caller_lui(string line)
        => Assert.Equal("{}", QcLibraryProfileBuilder.Build(Lib(), new[] { line }, "FQC"));

    [Fact]
    public void Thu_vien_rong_hoac_khong_co_line_thi_tra_rong()
    {
        Assert.Equal("{}", QcLibraryProfileBuilder.Build(new List<CheckItemLibrary>(), new[] { "LABEL" }, "FQC"));
        Assert.Equal("{}", QcLibraryProfileBuilder.Build(Lib(), Array.Empty<string>(), "FQC"));
        Assert.Equal("{}", QcLibraryProfileBuilder.Build(null, null, "FQC"));
    }

    [Fact]
    public void Snapshot_dung_hinh_dang_ma_phia_sau_dang_doc()
    {
        // ExtractProfileItemKeys / ProfileKeyCount / ExtractItemText đều đọc
        // shape này. Đổi shape = vỡ toàn bộ đường FQC/OQC mà không có lỗi build.
        var json = QcLibraryProfileBuilder.Build(Lib().Where(r => r.Oqc).ToList(), new[] { "LABEL" }, "OQC");
        var root = Root(json);

        Assert.Equal(QcLibraryProfileBuilder.Source, root.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("sections").ValueKind);
        Assert.Equal(Keys(json).Count, QcProfileSeed.CountItems(json));
    }

    // ── ngưỡng compliance không được trôi ────────────────────────────────

    [Fact]
    public void Nguong_ppm_cua_RoHS_dung_nhu_profile_production()
    {
        var mong_doi = new Dictionary<string, string>
        {
            ["cr_ppm"] = "< 100", ["cl_ppm"] = "< 800", ["s_ppm"] = "< 10000", ["cd_ppm"] = "< 20",
            ["hg_ppm"] = "< 100", ["pb_ppm"] = "< 100", ["sn_ppm"] = "< 800", ["sb_ppm"] = "< 700",
        };

        Assert.Equal(8, RohsLibrarySeed.Items().Count);
        foreach (var r in RohsLibrarySeed.Items())
            Assert.Equal(mong_doi[r.ItemId], r.Spec);
    }
}
