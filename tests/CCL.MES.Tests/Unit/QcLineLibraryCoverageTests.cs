using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// CƠ CHẾ CHẶN cho lớp lỗi "routing resolve ra một QC line mà không có gì để kiểm".
///
/// <para>Ma trận thư viện v5 có HAI nửa: <c>ProcessLine</c> (hạng mục thuộc dòng
/// sản phẩm nào) và 16 cờ tick-box (áp dụng cho phương pháp/công đoạn nào). Cả
/// hai chỗ materialize từng chỉ dùng nửa đầu, nên mọi QC line không có dòng thư
/// viện riêng đều rơi thẳng vào <c>SkippedNoLibrary</c> — người đứng máy mở IPQC
/// ra và không có gì để kiểm, KHÔNG có lỗi nào được ghi.</para>
///
/// <para>Test này khoá: mỗi QC line mà <see cref="ProcessLineMapSeed"/> có thể
/// phát ra PHẢI được xử lý có chủ đích — hoặc có dòng thư viện riêng, hoặc có
/// đường lùi theo cờ, hoặc nằm trong danh sách "biết là chưa có" dưới đây.
/// Thêm một QC line mới mà quên cả ba ⇒ test ĐỎ ngay, thay vì im lặng ra màn
/// hình trống dưới xưởng.</para>
/// </summary>
public sealed class QcLineLibraryCoverageTests
{
    /// <summary>QC line CÓ dòng thư viện của riêng nó (ProcessLine khớp trực tiếp).
    /// Đo trên dữ liệu thật 2026-08-28: LABEL 34 hạng mục · SILK 25.</summary>
    private static readonly string[] CoDongThuVien = ["LABEL", "SILK"];

    /// <summary>QC line CHƯA có gì — đã biết, đang chờ Ops chốt. Danh sách này chỉ
    /// được PHÉP NGẮN ĐI (ratchet, đúng Nguyên tắc III của hiến pháp). Muốn thêm
    /// một dòng vào đây thì phải giải thích được vì sao người đứng máy ở công
    /// đoạn đó không cần danh mục kiểm nào.
    ///
    /// <para><c>NONE</c> là ngoại lệ hợp lệ vĩnh viễn: nó nghĩa là "op này không
    /// thuộc công đoạn QC nào", không phải "thiếu thư viện".</para></summary>
    private static readonly string[] ChuaCoThuVien = ["DIGITAL", "FINISHING", "NONE"];

    [Fact]
    public void Moi_QC_line_trong_seed_deu_duoc_xu_ly_co_chu_dich()
    {
        var lines = ProcessLineMapSeed.DefaultEntries()
            .Select(e => e.QcLine)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var boSot = lines
            .Where(l => !CoDongThuVien.Contains(l, StringComparer.OrdinalIgnoreCase)
                     && !QcLineLibrarySelector.HasFlagFallback(l)
                     && !ChuaCoThuVien.Contains(l, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.True(boSot.Count == 0,
            "QC line có thể resolve ra từ routing nhưng KHÔNG có dòng thư viện, " +
            "KHÔNG có đường lùi theo cờ, và KHÔNG nằm trong danh sách 'biết là chưa có'. " +
            "Người đứng máy ở công đoạn này sẽ mở IPQC ra và thấy trống: " +
            string.Join(" · ", boSot));
    }

    [Fact]
    public void PRESS_CNC_van_giu_duong_lui_theo_co()
    {
        // Khoá tường minh: gỡ ánh xạ PRESS_CNC → SheetCut là tái phát nguyên bug.
        Assert.True(QcLineLibrarySelector.HasFlagFallback("PRESS_CNC"));
    }

    [Fact]
    public void Danh_sach_chua_co_thu_vien_khong_duoc_dai_ra()
    {
        // Ratchet đi xuống. Con số này CHỈ được giảm; tăng là STOP-gate.
        Assert.True(ChuaCoThuVien.Length <= 3,
            $"Danh sách 'chưa có thư viện' đã dài ra thành {ChuaCoThuVien.Length}. " +
            "Ratchet chỉ đi xuống — xem Nguyên tắc III của hiến pháp.");
    }
}
