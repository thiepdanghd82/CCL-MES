using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P13 bước 5 — luật quy đổi sheet NG. Mọi ca dưới đây lấy CHUỖI THẬT từ file
/// master 2026, không phải ví dụ bịa: 16 biến thể câu trả lời của NCC trên 152
/// dòng, và một ô đơn vị bị Excel ghi nhầm thành ngày tháng.
/// </summary>
public sealed class IqcNgImportTests
{
    private static IqcNgSheetRow Row(int n = 2) => new()
    {
        RowNumber = n,
        DetectedDate = new DateTime(2026, 3, 27),
        SupplierName = "CCL Design (Haian) Co., Ltd",
        PartNo = "3018",
        DefectName = "Nhăn",
    };

    // ── công đoạn phát hiện ─────────────────────────────────────────────

    [Theory]
    [InlineData("IQC", IqcNgStage.Iqc)]
    [InlineData("SX", IqcNgStage.Production)]
    [InlineData("sx", IqcNgStage.Production)]
    [InlineData("", IqcNgStage.Unknown)]
    [InlineData(null, IqcNgStage.Unknown)]
    // Ô lỗi công thức KHÔNG được đoán thành IQC: 38% vụ phát hiện ở sản xuất,
    // gán bừa là làm hỏng đúng con số dùng để quyết định.
    [InlineData("#REF!", IqcNgStage.Unknown)]
    public void Cong_doan_phat_hien(string? raw, IqcNgStage expected)
        => Assert.Equal(expected, IqcNgImport.MapStage(raw));

    // ── hình thức đền bù: chuỗi thật trên sheet ─────────────────────────

    [Theory]
    [InlineData("Đã bù hàng", IqcClaimSettlement.Replacement)]
    [InlineData("Trừ công nợ", IqcClaimSettlement.CreditNote)]
    [InlineData("Đã cấn trừ", IqcClaimSettlement.CreditNote)]
    [InlineData("Đã xuất hóa đơn giảm trừ", IqcClaimSettlement.CreditNote)]
    [InlineData("Đã xác nhận chờ bù", IqcClaimSettlement.None)]
    [InlineData("Close - k claim đc NCC", IqcClaimSettlement.None)]
    [InlineData(null, IqcClaimSettlement.None)]
    public void Hinh_thuc_den_bu(string? raw, IqcClaimSettlement expected)
        => Assert.Equal(expected, IqcNgImport.MapSettlement(raw));

    // ── vòng đời ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Đã bù hàng", true, IqcNgStatus.Settled)]
    [InlineData("Trừ công nợ", true, IqcNgStatus.Settled)]
    [InlineData("Đã xác nhận chờ bù", true, IqcNgStatus.SupplierConfirmed)]
    [InlineData("IQC đã TT (22/07/2026) -NCC chưa trả lời", true, IqcNgStatus.Claimed)]
    [InlineData("Close - k claim đc NCC", true, IqcNgStatus.ClosedNoClaim)]
    [InlineData(null, true, IqcNgStatus.Claimed)]
    [InlineData(null, false, IqcNgStatus.Open)]
    public void Vong_doi(string? answer, bool hasClaimDate, IqcNgStatus expected)
        => Assert.Equal(expected,
            IqcNgImport.MapStatus(answer, hasClaimDate ? new DateTime(2026, 4, 1) : null));

    [Fact]
    public void Close_thang_truoc_ca_ngay_claim()
    {
        // "Close - k claim đc NCC" có ngày claim, nhưng vẫn là KHÉP KHÔNG ĐÒI
        // ĐƯỢC. Nếu nhánh "có ngày claim" chạy trước thì 2 dòng này thành
        // Claimed và biến mất khỏi con số cần khi đàm phán lại hợp đồng.
        Assert.Equal(IqcNgStatus.ClosedNoClaim,
            IqcNgImport.MapStatus("Close - k claim đc NC", new DateTime(2026, 4, 1)));
    }

    // ── ô lỗi / ô bẩn ───────────────────────────────────────────────────

    [Theory]
    [InlineData("#REF!")]
    [InlineData("#N/A")]
    [InlineData("  ")]
    public void O_loi_cong_thuc_thanh_null(string raw) => Assert.Null(IqcNgImport.Clean(raw));

    [Fact]
    public void Don_vi_chi_nhan_CHU()
    {
        Assert.Equal("m", IqcNgImport.MapUom("m"));
        Assert.Equal("Sheet", IqcNgImport.MapUom("Sheet"));
        // Sheet có một ô cột QUY CÁCH bị Excel ghi thành ngày. Giữ nguyên thì
        // cột đơn vị trong DB có giá trị "2026-02-08 00:00:00".
        Assert.Null(IqcNgImport.MapUom("2026-02-08 00:00:00"));
        Assert.Null(IqcNgImport.MapUom("#REF!"));
    }

    // ── bỏ dòng ─────────────────────────────────────────────────────────

    [Fact]
    public void Thieu_ngay_phat_hien_thi_BO_va_noi_ly_do()
    {
        var r = Row(); r.DetectedDate = null;
        var m = IqcNgImport.Map(r);
        Assert.Null(m.Record);
        Assert.Contains("NGÀY PHÁT HIỆN", m.SkipReason);
    }

    [Fact]
    public void Dong_mau_trong_thi_BO()
    {
        // r141-146 của file thật: chỉ còn số công thức, không NCC/mã/lỗi.
        var r = new IqcNgSheetRow { RowNumber = 141, DetectedDate = new DateTime(2026, 1, 1) };
        var m = IqcNgImport.Map(r);
        Assert.Null(m.Record);
        Assert.Contains("trống", m.SkipReason);
    }

    // ── khoá idempotent ─────────────────────────────────────────────────

    // ── khoá idempotent theo NỘI DUNG ───────────────────────────────────

    [Fact]
    public void Chen_dong_o_giua_KHONG_lam_lech_khoa()
    {
        // Đây là lý do đổi khoá. Với khoá theo vị trí dòng, chèn một dòng ở
        // giữa làm mọi dòng dưới tụt số ⇒ lần nạp sau coi chúng là vụ MỚI và
        // nhân đôi cả sổ.
        var before = IqcNgImport.MapAll(new[] { R(2, "A"), R(3, "B"), R(4, "C") })
            .Where(x => x.Mapped.Record is not null)
            .ToDictionary(x => x.Row.DefectName!, x => x.Mapped.Record!.ImportSource!);

        // chèn một dòng mới vào giữa: B và C tụt xuống dòng 4 và 5
        var after = IqcNgImport.MapAll(new[] { R(2, "A"), R(3, "MỚI"), R(4, "B"), R(5, "C") })
            .Where(x => x.Mapped.Record is not null)
            .ToDictionary(x => x.Row.DefectName!, x => x.Mapped.Record!.ImportSource!);

        Assert.Equal(before["A"], after["A"]);
        Assert.Equal(before["B"], after["B"]);
        Assert.Equal(before["C"], after["C"]);
    }

    [Fact]
    public void Hai_dong_TRUNG_HET_van_ra_hai_ban_ghi()
    {
        // Cùng NCC + cùng mã + cùng ngày + cùng lỗi là chuyện CÓ THẬT trên
        // sheet (một lô nhiều loại lỗi, hoặc nhiều lô cùng ngày). Gộp là mất
        // một vụ — nên khoá nội dung phải kèm số thứ tự.
        var m = IqcNgImport.MapAll(new[] { Row(9), Row(10) });
        var keys = m.Select(x => x.Mapped.Record!.ImportSource!).ToList();
        Assert.Equal(2, keys.Distinct().Count());
        Assert.All(keys, k => Assert.StartsWith("xlsx:NG Material:h", k));
        Assert.EndsWith("-0", keys[0]);
        Assert.EndsWith("-1", keys[1]);
    }

    [Fact]
    public void Doi_MOT_truong_khoa_thi_doi_khoa()
    {
        var a = IqcNgImport.MapAll(new[] { Row(2) })[0].Mapped.Record!.ImportSource;
        var r = Row(2); r.PartNo = "9999";
        var b = IqcNgImport.MapAll(new[] { r })[0].Mapped.Record!.ImportSource;
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void So_thu_tu_ON_DINH_theo_so_dong_tang_dan()
    {
        // Người gọi đưa vào theo thứ tự nào cũng phải ra cùng kết quả, nếu
        // không thì hai lần nạp cùng một file lại ra khoá khác nhau.
        var asc = IqcNgImport.MapAll(new[] { Row(5), Row(9) })
            .Select(x => (x.Row.RowNumber, x.Mapped.Record!.ImportSource)).ToList();
        var desc = IqcNgImport.MapAll(new[] { Row(9), Row(5) })
            .Select(x => (x.Row.RowNumber, x.Mapped.Record!.ImportSource)).ToList();
        Assert.Equal(asc, desc);
    }

    private static IqcNgSheetRow R(int n, string defect)
    {
        var r = Row(n); r.DefectName = defect; return r;
    }

    [Fact]
    public void Khong_tu_noi_lo_noi_bo()
    {
        // Số lô trên sheet là số lô của NHÀ CUNG CẤP, khớp MaterialLots 0/140.
        var r = Row(); r.SupplierLotNo = "QT2502006";
        var rec = IqcNgImport.Map(r).Record!;
        Assert.Equal("QT2502006", rec.SupplierLotNo);
        Assert.Null(rec.MaterialLotId);
        Assert.Null(rec.IqcInspectionId);
    }

    [Fact]
    public void Cat_dung_do_dai_cot()
    {
        var r = Row();
        r.DefectName = new string('x', 400);      // cột 256
        r.SupplierAnswer = new string('y', 900); // cột 512
        var rec = IqcNgImport.Map(r).Record!;
        Assert.Equal(256, rec.DefectName!.Length);
        Assert.Equal(512, rec.SupplierNote!.Length);
    }
}
