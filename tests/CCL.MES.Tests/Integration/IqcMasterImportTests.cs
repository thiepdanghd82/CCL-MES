using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P13 bước 3 — nạp tiêu chuẩn từ sheet <c>Raw</c> của file master IQC.
///
/// <para>Bộ này khoá HÀNH VI, không khoá con số của file master (file nằm ngoài
/// repo, ở máy Henry). Con số nghiệm thu thật đã đo bằng chính công cụ import
/// và ghi trong <c>p13-scope-proposal.md</c>: 672 spec mới · 348 làm giàu ·
/// 1231 hạng mục mới · chạy lần hai ra 0/0/0/0.</para>
/// </summary>
public sealed class IqcMasterImportTests : IClassFixture<IsolatedDbFixture>
{
    private readonly IsolatedDbFixture _fx;
    public IqcMasterImportTests(IsolatedDbFixture fx) => _fx = fx;

    private MesDbContext Db() => new(_fx.Options);

    private static IqcMasterRow Row(
        string mother, string? keo = null, string? day = null,
        string? rong = null, string? method = null) =>
        new(mother, null, mother + " name", "NCC X", method, keo, day, rong);

    private static async Task<IqcMasterImportResult> Run(
        MesDbContext db, params IqcMasterRow[] rows) =>
        await new IqcMasterImportService(db).ImportAsync(rows, "test", commit: true);

    // ── mã mới ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Ma_chua_co_spec_thi_TAO_MOI_va_vao_hang_CHO_QC_DUYET()
    {
        await using var db = Db();
        var r = await Run(db, Row("P13I-NEW-1", day: "0.16±0.016"));

        Assert.Equal(1, r.SpecsInserted);
        var spec = await db.IqcMaterialSpecs.AsNoTracking()
            .SingleAsync(x => x.MaterialCode == "P13I-NEW-1");
        // Henry chốt 2026-09-04: hàng nhập từ file ngoài phải chờ QC duyệt.
        Assert.Equal(IqcSpecApproval.PendingQc, spec.Approval);
        Assert.Equal(IqcMasterItemMap.Source, spec.ImportSource);
        Assert.StartsWith(IqcMasterItemMap.SpecNoPrefix, spec.SpecNo);
    }

    [Fact]
    public async Task Nguong_so_doc_duoc_thi_ghi_vao_hang_muc_DO_DAY()
    {
        await using var db = Db();
        await Run(db, Row("P13I-LIM-1", day: "Adhesive 0.16±0.016"));

        var spec = await db.IqcMaterialSpecs.AsNoTracking()
            .SingleAsync(x => x.MaterialCode == "P13I-LIM-1");
        var item = await db.IqcSpecItems.AsNoTracking()
            .SingleAsync(x => x.SpecNo == spec.SpecNo && x.ItemId == IqcMasterItemMap.Thickness);

        Assert.True(item.LimitParsed);
        Assert.Equal(0.144, item.LimitLow!.Value, 6);
        Assert.Equal(0.176, item.LimitUp!.Value, 6);
        Assert.Equal("Adhesive", item.LimitLabel);
        // Nguyên văn LUÔN giữ để người kiểm đối chiếu với giấy của NCC.
        Assert.Equal("Adhesive 0.16±0.016", item.AcceptanceVi);
    }

    [Fact]
    public async Task Do_rong_la_so_TRAN_thi_giu_TRI_DANH_NGHIA_nhung_KHONG_tu_cham()
    {
        await using var db = Db();
        await Run(db, Row("P13I-W-1", rong: "220"));

        var spec = await db.IqcMaterialSpecs.AsNoTracking()
            .SingleAsync(x => x.MaterialCode == "P13I-W-1");
        var item = await db.IqcSpecItems.AsNoTracking()
            .SingleAsync(x => x.SpecNo == spec.SpecNo && x.ItemId == IqcMasterItemMap.Width);

        Assert.Equal(220, item.LimitNominal!.Value, 6);   // đích cần đạt: hiện được
        Assert.Null(item.LimitLow);                       // nhưng KHÔNG có dung sai
        Assert.Null(item.LimitUp);
        Assert.False(item.LimitParsed);                   // ⇒ máy không chấm hộ
    }

    [Fact]
    public async Task O_ghi_N_A_thi_KHONG_de_ra_hang_muc_nao()
    {
        // "N/A" là lời khai vật liệu này KHÔNG CÓ tiêu chuẩn ấy (mực in không
        // có phép thử bóc keo), không phải "chưa khai". Dựng hạng mục cho nó là
        // bắt người kiểm bấm qua 128 dòng rỗng nghĩa mỗi lô.
        await using var db = Db();
        var r = await Run(db, Row("P13I-NA-1", keo: "N/A", day: "0.1±0.01"));

        Assert.Equal(1, r.ItemsInserted);
        var spec = await db.IqcMaterialSpecs.AsNoTracking()
            .SingleAsync(x => x.MaterialCode == "P13I-NA-1");
        var items = await db.IqcSpecItems.AsNoTracking()
            .Where(x => x.SpecNo == spec.SpecNo).ToListAsync();
        Assert.Equal(IqcMasterItemMap.Thickness, Assert.Single(items).ItemId);
    }

    // ── mã đã có ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Ma_DA_co_spec_thi_LAM_GIAU_chu_khong_dung_them_spec_thu_hai()
    {
        await using var db = Db();
        db.IqcMaterialSpecs.Add(new IqcMaterialSpec
        {
            SpecNo = "CCL-SPEC-QC900", MaterialCode = "P13I-EXIST-1",
            Approval = IqcSpecApproval.Approved,
        });
        await db.SaveChangesAsync();

        var r = await Run(db, Row("P13I-EXIST-1", day: "0.2±0.02", method: "FTM1"));

        Assert.Equal(0, r.SpecsInserted);
        Assert.Equal(1, r.SpecsEnriched);
        var specs = await db.IqcMaterialSpecs.AsNoTracking()
            .Where(x => x.MaterialCode == "P13I-EXIST-1").ToListAsync();
        Assert.Single(specs);
        Assert.Equal("FTM1", specs[0].TestMethod);
    }

    [Fact]
    public async Task Lam_giau_KHONG_duoc_day_mot_spec_DA_DUYET_ve_cho_duyet()
    {
        // File ngoài nhắc tới một mã không phải là lý do xoá chữ ký của QC.
        await using var db = Db();
        db.IqcMaterialSpecs.Add(new IqcMaterialSpec
        {
            SpecNo = "CCL-SPEC-QC901", MaterialCode = "P13I-APPR-1",
            Approval = IqcSpecApproval.Approved, ApprovedBy = "qc-lead",
        });
        await db.SaveChangesAsync();

        await Run(db, Row("P13I-APPR-1", day: "0.3±0.03", method: "FTM2"));

        var spec = await db.IqcMaterialSpecs.AsNoTracking()
            .SingleAsync(x => x.MaterialCode == "P13I-APPR-1");
        Assert.Equal(IqcSpecApproval.Approved, spec.Approval);
        Assert.Equal("qc-lead", spec.ApprovedBy);
        Assert.Null(spec.ImportSource);       // vẫn là spec của app, không phải hàng nhập
    }

    [Fact]
    public async Task Mot_ma_co_NHIEU_spec_thi_ghi_vao_SpecNo_nho_nhat_khong_no()
    {
        // Live có 7 mã như vậy (SFG-APB2M000102 có SÁU spec). ToDictionary
        // thẳng sẽ ném ArgumentException và giết cả lần import.
        await using var db = Db();
        db.IqcMaterialSpecs.AddRange(
            new IqcMaterialSpec { SpecNo = "CCL-SPEC-QC903", MaterialCode = "P13I-DUP-1" },
            new IqcMaterialSpec { SpecNo = "CCL-SPEC-QC902", MaterialCode = "P13I-DUP-1" });
        await db.SaveChangesAsync();

        var r = await Run(db, Row("P13I-DUP-1", day: "0.4±0.04"));

        Assert.Equal(1, r.CodesWithDuplicateSpecs);
        var items = await db.IqcSpecItems.AsNoTracking()
            .Where(x => x.SpecNo == "CCL-SPEC-QC902" || x.SpecNo == "CCL-SPEC-QC903")
            .ToListAsync();
        // Ghi vào ĐÚNG MỘT spec — resolver gộp hạng mục từ mọi spec của một mã,
        // rải ra cả hai chỉ tạo bản sao cho nó phải hợp nhất lại.
        Assert.Equal("CCL-SPEC-QC902", Assert.Single(items).SpecNo);
    }

    // ── idempotent ───────────────────────────────────────────────────────

    [Fact]
    public async Task Chay_lai_lan_hai_ra_0_o_MOI_cot()
    {
        await using var db = Db();
        var rows = new[]
        {
            Row("P13I-IDEM-1", keo: "270 N/m", day: "0.16±0.016", rong: "220", method: "FTM1"),
            Row("P13I-IDEM-2", day: "0.08±0.008"),
        };
        var first = await new IqcMasterImportService(db).ImportAsync(rows, "test", commit: true);
        Assert.True(first.SpecsInserted > 0 && first.ItemsInserted > 0);

        var second = await new IqcMasterImportService(db).ImportAsync(rows, "test", commit: true);
        Assert.Equal(0, second.SpecsInserted);
        Assert.Equal(0, second.SpecsEnriched);
        Assert.Equal(0, second.ItemsInserted);
        Assert.Equal(0, second.ItemsUpdated);
    }

    [Fact]
    public async Task Chay_kho_KHONG_ghi_gi_ca()
    {
        await using var db = Db();
        var before = await db.IqcMaterialSpecs.CountAsync();

        var r = await new IqcMasterImportService(db)
            .ImportAsync([Row("P13I-DRY-1", day: "0.5±0.05")], "test", commit: false);

        // Vẫn ĐẾM đầy đủ để người dùng xem trước…
        Assert.Equal(1, r.SpecsInserted);
        // …nhưng KHÔNG lưu.
        await using var fresh = Db();
        Assert.Equal(before, await fresh.IqcMaterialSpecs.CountAsync());
    }

    // ── một mã xuất hiện nhiều dòng trong file ───────────────────────────

    [Fact]
    public async Task Ma_lap_lai_thi_lay_dong_KHAI_DUOC_NHIEU_NHAT_khong_lay_dong_cuoi()
    {
        // File master có 2319 dòng cho 1028 mã ⇒ mã lặp là chuyện thường, và
        // dòng lặp hay bỏ trống ô. Lấy dòng cuối là mất tiêu chuẩn, và kết quả
        // phụ thuộc thứ tự đọc file.
        await using var db = Db();
        await Run(db,
            Row("P13I-REP-1", keo: "270 N/m", day: "0.16±0.016", rong: "220"),
            Row("P13I-REP-1"));   // dòng sau rỗng hơn

        var spec = await db.IqcMaterialSpecs.AsNoTracking()
            .SingleAsync(x => x.MaterialCode == "P13I-REP-1");
        var items = await db.IqcSpecItems.AsNoTracking()
            .Where(x => x.SpecNo == spec.SpecNo).ToListAsync();
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task Ma_spec_sinh_ra_ON_DINH_giua_cac_lan_chay()
    {
        // string.GetHashCode randomize theo tiến trình ⇒ chạy lại ra mã khác và
        // import lần hai sẽ đẻ spec trùng. Băm FNV-1a thì không.
        var a = IqcMasterItemMap.SpecNoFor("336T-AT1");
        var b = IqcMasterItemMap.SpecNoFor("  336t-at1  ");
        Assert.Equal(a, b);
        Assert.StartsWith("IQC26-", a);
        await Task.CompletedTask;
    }

    // ── HAI NGUỒN cùng ghi một dòng: bẫy đắt nhất của bước này ───────────

    [Fact]
    public async Task Hang_muc_DA_CO_thi_import_KHONG_duoc_ghi_de_chuoi_tieu_chuan()
    {
        // Đã xảy ra thật trên live: import ghi "10.0 N/25mm" lên
        // CCL-SPEC-QC001/BD-01, rồi lần boot kế tiếp seeder CSV lấy lại chuỗi
        // cũ "FTM 2" — nhưng seeder KHÔNG đụng cột ngưỡng số. Kết quả: màn hình
        // hiện "FTM 2" (một PHƯƠNG PHÁP, không phải chỉ tiêu) trong khi máy
        // chấm theo ≥10.0. Người kiểm đọc một đằng, máy chấm một nẻo.
        await using var db = Db();
        db.IqcMaterialSpecs.Add(new IqcMaterialSpec
        { SpecNo = "CCL-SPEC-QC910", MaterialCode = "P13I-OWN-1" });
        db.IqcSpecItems.Add(new IqcSpecItem
        {
            SpecNo = "CCL-SPEC-QC910", ItemId = IqcMasterItemMap.Adhesion, Seq = 1,
            AcceptanceVi = "FTM 2", CreatedBy = "seed",
        });
        await db.SaveChangesAsync();

        var r = await Run(db, Row("P13I-OWN-1", keo: "10.0 N/25mm"));

        var item = await db.IqcSpecItems.AsNoTracking()
            .SingleAsync(x => x.SpecNo == "CCL-SPEC-QC910");
        // Chuỗi GIỮ NGUYÊN của app…
        Assert.Equal("FTM 2", item.AcceptanceVi);
        // …và KHÔNG mang ngưỡng của Excel. Chuỗi với ngưỡng phải cùng một nguồn.
        Assert.Null(item.LimitLow);
        Assert.False(item.LimitParsed);
        // Xung đột được ĐẾM để QC đối chiếu, không tự chọn bên nào thắng.
        Assert.Equal(1, r.TextConflicts);
    }

    [Fact]
    public async Task Hang_muc_da_co_van_duoc_doc_nguong_tu_chinh_chuoi_CUA_NO()
    {
        // Không đụng chuỗi KHÔNG có nghĩa là bỏ mặc: nếu chuỗi của app tự nó
        // đọc được thành ngưỡng thì vẫn điền, và chắc chắn khớp vì cùng nguồn.
        await using var db = Db();
        db.IqcMaterialSpecs.Add(new IqcMaterialSpec
        { SpecNo = "CCL-SPEC-QC911", MaterialCode = "P13I-OWN-2" });
        db.IqcSpecItems.Add(new IqcSpecItem
        {
            SpecNo = "CCL-SPEC-QC911", ItemId = IqcMasterItemMap.Thickness, Seq = 1,
            AcceptanceVi = "0.16±0.016", CreatedBy = "seed",
        });
        await db.SaveChangesAsync();

        await Run(db, Row("P13I-OWN-2", day: "0.20±0.02"));   // Excel khai KHÁC

        var item = await db.IqcSpecItems.AsNoTracking()
            .SingleAsync(x => x.SpecNo == "CCL-SPEC-QC911");
        Assert.Equal("0.16±0.016", item.AcceptanceVi);        // chuỗi của app
        Assert.Equal(0.144, item.LimitLow!.Value, 6);         // ngưỡng CỦA CHÍNH nó
        Assert.True(item.LimitParsed);
    }
}
