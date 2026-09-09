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

    [Fact]
    public void Khoa_idempotent_la_VI_TRI_DONG()
    {
        // Hai dòng GIỐNG HỆT nhau về nội dung vẫn phải ra hai bản ghi khác
        // nhau: cùng NCC + cùng mã + cùng ngày là chuyện có thật trên sheet
        // (nhiều lô, hoặc một lô nhiều loại lỗi). Gộp là mất một vụ.
        var a = IqcNgImport.Map(Row(7)).Record!;
        var b = IqcNgImport.Map(Row(8)).Record!;
        Assert.Equal("xlsx:NG Material:r7", a.ImportSource);
        Assert.Equal("xlsx:NG Material:r8", b.ImportSource);
        Assert.NotEqual(a.ImportSource, b.ImportSource);
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
