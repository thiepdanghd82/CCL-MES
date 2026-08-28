using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P12 — parser ba file CSV master của thư viện IQC.
///
/// <para>Khoá <b>VỊ TRÍ CỘT</b>. Skill <c>cmes-defect-library-import</c> nêu
/// đúng lỗi thường gặp: header CSV nhiều dòng làm map theo tên lệch âm thầm.
/// Đổi thứ tự cột trong file mà quên sửa parser ⇒ tiêu chuẩn của hạng mục này
/// nhảy sang hạng mục khác, và không có gì báo lỗi — người kiểm ký lên nó.</para>
/// </summary>
public sealed class IqcLibraryCsvTests
{
    // ── hạng mục ─────────────────────────────────────────────────────────

    private const string ItemsCsv = """
    ItemId,GroupCode,GroupLabelVi,GroupLabelEn,ItemVi,ItemEn,Sort
    NL-01,NL,Nguyên liệu,Ingredient,Kiểm tra vật liệu / nhận dạng,Material identification,10
    NQ-01,NQ,Ngoại quan,External inspection,Tem nhãn,Labels / marking,20
    """;

    [Fact]
    public void Doc_dung_vi_tri_cot_hang_muc()
    {
        var r = Assert.IsAssignableFrom<IReadOnlyList<IqcLibraryCsv.ItemRow>>(
            IqcLibraryCsv.ParseItems(ItemsCsv))[0];

        Assert.Equal("NL-01", r.ItemId);
        Assert.Equal("NL", r.GroupCode);
        Assert.Equal("Nguyên liệu", r.GroupLabelVi);
        Assert.Equal("Ingredient", r.GroupLabelEn);
        Assert.Equal("Kiểm tra vật liệu / nhận dạng", r.ItemVi);
        Assert.Equal("Material identification", r.ItemEn);
        Assert.Equal(10, r.Sort);
    }

    // ── spec ↔ nguyên liệu ───────────────────────────────────────────────

    [Fact]
    public void Doc_dung_vi_tri_cot_spec_va_giu_ma_IFS()
    {
        const string csv = """
        SpecNo,MaterialCode,MaterialCodeIfs,SupplierName,Revision
        CCL-SPEC-QC060,TESA 4982,70000076,TESA,R03
        CCL-SPEC-QC001,SW-7325F,,AVERY DENNISON,R03
        """;
        var rows = IqcLibraryCsv.ParseSpecs(csv);

        Assert.Equal("70000076", rows[0].MaterialCodeIfs);
        Assert.Equal("TESA 4982", rows[0].MaterialCode);
        // Không có mã IFS ⇒ null, KHÔNG phải chuỗi rỗng: null là "chưa biết",
        // chuỗi rỗng lọt mọi phép kiểm null rồi khớp nhầm khi resolve.
        Assert.Null(rows[1].MaterialCodeIfs);
    }

    // ── tiêu chuẩn theo nguyên liệu ──────────────────────────────────────

    [Fact]
    public void Doc_dung_vi_tri_cot_tieu_chuan_va_giu_tan_suat_goc()
    {
        const string csv = """
        SpecNo,ItemId,Seq,AcceptanceVi,AcceptanceEn,MethodVi,MethodEn,SourceFrequency,Sort
        CCL-SPEC-QC001,NL-01,1,Theo mẫu chuẩn được lưu,According to the standard form,Kiểm mác SP,Check label,All lot,1
        """;
        var r = IqcLibraryCsv.ParseSpecItems(csv)[0];

        Assert.Equal("CCL-SPEC-QC001", r.SpecNo);
        Assert.Equal("NL-01", r.ItemId);
        Assert.Equal("Theo mẫu chuẩn được lưu", r.AcceptanceVi);
        Assert.Equal("According to the standard form", r.AcceptanceEn);
        Assert.Equal("Kiểm mác SP", r.MethodVi);
        Assert.Equal("All lot", r.SourceFrequency);
    }

    [Fact]
    public void Tan_suat_goc_duoc_GIU_NGUYEN_ke_ca_khi_la_theo_thang()
    {
        // Quyết định D1: kiểm mọi lô. Nhưng ghi đè chính sách KHÔNG được xoá
        // dấu vết spec gốc nói gì — khi audit hỏi phải trả lời được.
        const string csv = """
        SpecNo,ItemId,Seq,AcceptanceVi,AcceptanceEn,MethodVi,MethodEn,SourceFrequency,Sort
        CCL-SPEC-QC001,MT-01,1,"Cd,Cl,Hg, Pb",,XRF,,Kiểm tra mỗi tháng một lần.,1
        """;
        var r = IqcLibraryCsv.ParseSpecItems(csv)[0];

        Assert.Equal("Kiểm tra mỗi tháng một lần.", r.SourceFrequency);
        // Ô có dấu phẩy bên trong nháy kép KHÔNG được tách thành nhiều cột.
        Assert.Equal("Cd,Cl,Hg, Pb", r.AcceptanceVi);
    }

    [Fact]
    public void Nhieu_tieu_chi_cung_ma_hang_muc_trong_mot_spec_deu_duoc_giu()
    {
        // Chuẩn hoá 63→21 mã làm một spec có thể mang nhiều tiêu chí cùng mã.
        // Ví dụ thật: CCL-SPEC-QC264 / NQ-06 (đóng gói mực can) có 3 tiêu chí.
        // Bỏ Seq ⇒ khoá (spec,item) nuốt mất 13 tiêu chí trên toàn bộ dữ liệu.
        const string csv = """
        SpecNo,ItemId,Seq,AcceptanceVi,AcceptanceEn,MethodVi,MethodEn,SourceFrequency,Sort
        S1,NQ-06,1,Không rách,,,,All lot,1
        S1,NQ-06,2,Không ẩm ướt,,,,All lot,2
        S1,NQ-06,3,Nắp không rò rỉ,,,,All lot,3
        """;
        var rows = IqcLibraryCsv.ParseSpecItems(csv);

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.Seq));
        Assert.Equal(3, rows.Select(r => r.AcceptanceVi).Distinct().Count());
    }

    // ── RFC-4180 ─────────────────────────────────────────────────────────

    [Fact]
    public void O_co_xuong_dong_ben_trong_nhay_kep_khong_ket_thuc_ban_ghi()
    {
        var csv = "SpecNo,ItemId,Seq,AcceptanceVi,AcceptanceEn,MethodVi,MethodEn,SourceFrequency,Sort\n"
                + "S1,I1,1,\"dòng một\ndòng hai\",,,,All lot,1\n";
        var rows = IqcLibraryCsv.ParseSpecItems(csv);

        Assert.Single(rows);
        Assert.Contains("dòng hai", rows[0].AcceptanceVi);
    }

    [Fact]
    public void Nhay_kep_doi_la_mot_dau_nhay_literal()
    {
        var csv = "ItemId,GroupCode,GroupLabelVi,GroupLabelEn,ItemVi,ItemEn,Sort\n"
                + "X-1,X,G,G,\"ống 3\"\" phi lớn\",EN,10\n";
        Assert.Equal("ống 3\" phi lớn", IqcLibraryCsv.ParseItems(csv)[0].ItemVi);
    }

    [Fact]
    public void Bo_BOM_header_va_dong_khoa_rong()
    {
        var csv = "﻿ItemId,GroupCode,GroupLabelVi,GroupLabelEn,ItemVi,ItemEn,Sort\n"
                + "NL-01,NL,G,G,Item,Item,10\n"
                + ",,,,,,\n";
        var rows = IqcLibraryCsv.ParseItems(csv);

        Assert.Single(rows);
        Assert.Equal("NL-01", rows[0].ItemId);
    }

    [Fact]
    public void Dong_thieu_cot_duoc_dem_chu_khong_nem_exception()
    {
        // File do người sửa tay rất hay thiếu cột cuối. Ném exception ở đây
        // làm chết cả lần seed vì một dòng lẻ.
        var csv = "SpecNo,MaterialCode,MaterialCodeIfs,SupplierName,Revision\n"
                + "S1,MAT-1\n";
        var rows = IqcLibraryCsv.ParseSpecs(csv);

        Assert.Single(rows);
        Assert.Equal("MAT-1", rows[0].MaterialCode);
        Assert.Null(rows[0].Revision);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ItemId,GroupCode,GroupLabelVi,GroupLabelEn,ItemVi,ItemEn,Sort")]
    public void Rong_hoac_chi_co_header_thi_tra_rong(string csv)
        => Assert.Empty(IqcLibraryCsv.ParseItems(csv));
}
