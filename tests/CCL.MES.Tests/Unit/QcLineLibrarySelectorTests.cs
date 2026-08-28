using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Chọn hạng mục thư viện cho QC line đã resolve — khoá đường lùi theo CỜ
/// tick-box cho line không có dòng thư viện của riêng nó.
///
/// <para>Bệnh cũ: cả hai chỗ materialize lọc <c>lines.Contains(ProcessLine)</c>,
/// tức chỉ dùng nửa đầu của ma trận v5. Routing resolve ra <c>PRESS_CNC</c> (15
/// luật map: FBL · PPSC · RDC · ACNC · CNC · LASE · PUNC · MDRH · R2SC + keyword
/// SHEETCUT/POWER PRESS/LASER/PUNCH/DRILL) nhưng thư viện không có dòng nào
/// <c>ProcessLine='PRESS_CNC'</c> ⇒ 0 hạng mục ⇒ người đứng máy cắt mở IPQC ra
/// và không có gì để kiểm, trong khi thư viện CÓ SẴN 14 dòng bật cờ SheetCut.</para>
/// </summary>
public sealed class QcLineLibrarySelectorTests
{
    private static CheckItemLibrary Row(
        string id, string line, bool sheetCut = false, bool active = true,
        string group = "A·Ngoại quan") => new()
    {
        ItemId = id, ProcessLine = line, GroupLabel = group, Code = id,
        ItemVi = $"vi {id}", ItemEn = $"en {id}",
        AcceptanceVi = "vi spec", AcceptanceEn = "en spec",
        SheetCut = sheetCut, Ipqc = true, Active = active,
    };

    /// <summary>Thư viện thu nhỏ mô phỏng đúng hình dạng thật: LABEL có 2 dòng
    /// bật SheetCut, SILK không dòng nào, không có dòng PRESS_CNC nào.</summary>
    private static List<CheckItemLibrary> Lib() =>
    [
        Row("LBL-A1", "LABEL"),
        Row("LBL-A3", "LABEL", sheetCut: true),
        Row("LBL-B1", "LABEL", sheetCut: true, group: "B·Kích thước"),
        Row("SLK-A1", "SILK"),
    ];

    // ── đường lùi theo cờ ────────────────────────────────────────────────

    [Fact]
    public void PRESS_CNC_lay_hang_muc_qua_co_SheetCut()
    {
        var sel = QcLineLibrarySelector.Select(Lib(), new[] { "PRESS_CNC" });

        Assert.Equal(new[] { "LBL-A3", "LBL-B1" }, sel.Select(s => s.Row.ItemId).OrderBy(x => x));
    }

    [Fact]
    public void Hang_muc_qua_co_duoc_dong_dau_LINE_DA_RESOLVE_chu_khong_phai_ProcessLine()
    {
        // Đây là bẫy dễ bỏ sót nhất. UI chia chip TẦNG-1 theo trường này:
        // LABEL/DIGITAL/SILK → chip IN · PRESS_CNC/FINISHING → chip CẮT.
        // Giữ nguyên "LABEL" thì 14 hạng mục cắt nằm dưới chip IN — sai công đoạn.
        var sel = QcLineLibrarySelector.Select(Lib(), new[] { "PRESS_CNC" });

        Assert.All(sel, s => Assert.Equal("PRESS_CNC", s.Line));
        Assert.All(sel, s => Assert.Equal("LABEL", s.Row.ProcessLine)); // thư viện KHÔNG bị sửa
    }

    [Fact]
    public void Line_co_dong_thu_vien_rieng_thi_KHONG_dung_duong_lui()
    {
        var sel = QcLineLibrarySelector.Select(Lib(), new[] { "LABEL" });

        Assert.Equal(3, sel.Count);
        Assert.All(sel, s => Assert.Equal("LABEL", s.Line));
    }

    [Fact]
    public void Line_khong_co_thu_vien_va_khong_co_co_thi_tra_rong()
    {
        // FINISHING · DIGITAL · NONE: chưa có ánh xạ cờ. Trả rỗng để UI hiện
        // "chưa có thư viện" — thà vậy còn hơn dựng sai danh mục kiểm.
        Assert.Empty(QcLineLibrarySelector.Select(Lib(), new[] { "FINISHING" }));
        Assert.Empty(QcLineLibrarySelector.Select(Lib(), new[] { "DIGITAL" }));
        Assert.False(QcLineLibrarySelector.HasFlagFallback("FINISHING"));
        Assert.True(QcLineLibrarySelector.HasFlagFallback("PRESS_CNC"));
    }

    // ── khử trùng ────────────────────────────────────────────────────────

    [Fact]
    public void LABEL_kem_PRESS_CNC_khong_sinh_hang_muc_trung()
    {
        // WoIpqcCheckItems có unique index (WoIpqcCheckId, ItemKey) — trùng là
        // vỡ ghi. LBL-A3 vừa thuộc LABEL vừa bật SheetCut.
        var sel = QcLineLibrarySelector.Select(Lib(), new[] { "LABEL", "PRESS_CNC" });

        Assert.Equal(3, sel.Count);
        Assert.Equal(sel.Select(s => s.Row.ItemId).Distinct().Count(), sel.Count);
    }

    [Fact]
    public void Khu_trung_theo_LINE_DAU_TIEN_THANG()
    {
        // Hệ quả cần biết: WO có cả LABEL lẫn PRESS_CNC thì hạng mục cắt đã nằm
        // sẵn trong LABEL ⇒ ở lại chip IN, chip CẮT không mọc thêm.
        var sel = QcLineLibrarySelector.Select(Lib(), new[] { "LABEL", "PRESS_CNC" });
        Assert.All(sel, s => Assert.Equal("LABEL", s.Line));

        // Đảo thứ tự thì hạng mục cắt về PRESS_CNC, phần còn lại vẫn LABEL.
        var dao = QcLineLibrarySelector.Select(Lib(), new[] { "PRESS_CNC", "LABEL" });
        Assert.Equal("PRESS_CNC", dao.Single(s => s.Row.ItemId == "LBL-A3").Line);
        Assert.Equal("LABEL", dao.Single(s => s.Row.ItemId == "LBL-A1").Line);
    }

    [Fact]
    public void SILK_kem_PRESS_CNC_thi_hang_muc_cat_ve_chip_CAT()
    {
        // Đây là trường hợp fix thực sự thay đổi kết quả: WO chạy lụa + cắt.
        var sel = QcLineLibrarySelector.Select(Lib(), new[] { "SILK", "PRESS_CNC" });

        Assert.Equal("SILK", sel.Single(s => s.Row.ItemId == "SLK-A1").Line);
        Assert.Equal(2, sel.Count(s => s.Line == "PRESS_CNC"));
    }

    // ── biên ─────────────────────────────────────────────────────────────

    [Fact]
    public void Dong_Inactive_bi_loai_ke_ca_qua_duong_co()
    {
        var lib = new List<CheckItemLibrary> { Row("LBL-A3", "LABEL", sheetCut: true, active: false) };
        var sel = QcLineLibrarySelector.Select(lib, new[] { "PRESS_CNC" });
        Assert.Empty(IpqcLibraryMaterializer.Build(sel, new[] { "PRESS_CNC" }).Items);
    }

    [Theory]
    [InlineData("press_cnc")]
    [InlineData("  PRESS_CNC  ")]
    public void Ten_line_khong_phan_biet_hoa_thuong_va_da_trim(string line)
    {
        Assert.Equal(2, QcLineLibrarySelector.Select(Lib(), new[] { line }).Count);
    }

    [Fact]
    public void Dau_vao_rong_hoac_null_khong_no()
    {
        Assert.Empty(QcLineLibrarySelector.Select(null, new[] { "LABEL" }));
        Assert.Empty(QcLineLibrarySelector.Select(Lib(), null));
        Assert.Empty(QcLineLibrarySelector.Select(Lib(), new[] { "", "   " }));
    }

    // ── nối vào materializer ─────────────────────────────────────────────

    [Fact]
    public void Materializer_dong_bang_line_da_resolve_len_WoIpqcCheckItem()
    {
        var built = IpqcLibraryMaterializer.Build(Lib(), new[] { "PRESS_CNC" });

        Assert.Equal(2, built.Items.Count);
        Assert.All(built.Items, i => Assert.Equal("PRESS_CNC", i.ProcessLine));
        Assert.Contains("PRESS_CNC", built.ProfileSnapshotJson);
    }
}
