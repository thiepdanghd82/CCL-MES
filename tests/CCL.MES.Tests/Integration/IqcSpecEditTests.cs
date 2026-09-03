using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P12 bước 2b — soạn tiêu chuẩn kiểm theo MÃ nguyên liệu.
///
/// <para>Khoá bốn điều: (a) mã chưa có spec thì tạo spec CỤC BỘ, không đụng
/// không gian tên của file master; (b) xoá là xoá MỀM và <b>sống sót qua lần
/// seed kế tiếp</b>; (c) chỉ Engineer+ được ghi; (d) hạng mục phải thuộc thư
/// viện 21 mục chứ không cho gõ tự do.</para>
/// </summary>
public sealed class IqcSpecEditTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public IqcSpecEditTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    private const string Eng = "eng-user";
    private InMemoryAuditWriter _audit = new();

    private IqcSpecEditService Svc(MesDbContext db)
    {
        _audit = new InMemoryAuditWriter();
        return new IqcSpecEditService(db, _audit);
    }

    private static async Task SeedLibraryAsync(MesDbContext db)
    {
        db.IqcCheckItemLibraries.AddRange(
            new IqcCheckItemLibrary { ItemId = "NQ-01", GroupCode = "NQ", GroupLabelVi = "Ngoại quan",
                ItemVi = "Tem nhãn", ItemEn = "Labels", Sort = 20, Active = true,
                InDefaultMatrix = true, DefaultAcceptanceVi = "Đúng thông tin" },
            new IqcCheckItemLibrary { ItemId = "KT-02", GroupCode = "KT", GroupLabelVi = "Kích thước",
                ItemVi = "Chiều dài", Sort = 30, Active = true });
        await db.SaveChangesAsync();
    }

    /// <summary>Mã ĐÃ có spec từ file master.</summary>
    private static async Task SeedMasterSpecAsync(MesDbContext db)
    {
        db.IqcMaterialSpecs.Add(new IqcMaterialSpec
        { SpecNo = "CCL-SPEC-QC229", MaterialCode = "336-H1a", Active = true, CreatedBy = "seed" });
        db.IqcSpecItems.Add(new IqcSpecItem
        {
            SpecNo = "CCL-SPEC-QC229", ItemId = "NQ-01", Seq = 1,
            AcceptanceVi = "TIÊU CHUẨN RIÊNG", Active = true, CreatedBy = "seed",
        });
        await db.SaveChangesAsync();
    }

    // ── (a) mã chưa có spec ⇒ tạo spec CỤC BỘ ────────────────────────────

    [Fact]
    public async Task Ma_chua_co_spec_thi_TAO_spec_cuc_bo_va_them_hang_muc()
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);

        var r = await Svc(db).AddItemAsync(
            "TWP5050", "NQ-01", "Tem phải rõ chữ", null, "Soi mắt", null, "All lot",
            Eng, UserRole.Engineer);

        Assert.True(r.Ok, r.ErrorCode);
        Assert.True(r.SpecCreated);
        Assert.StartsWith(IqcSpecEditService.LocalSpecPrefix, r.SpecNo);

        var row = await db.IqcSpecItems.SingleAsync();
        Assert.Equal("NQ-01", row.ItemId);
        Assert.Equal(1, row.Seq);
        Assert.Equal("Tem phải rõ chữ", row.AcceptanceVi);
        Assert.Equal(Eng, row.CreatedBy);
    }

    [Fact]
    public async Task Spec_cuc_bo_KHONG_dung_khong_gian_ten_cua_file_master()
    {
        // File master đánh số CCL-SPEC-QC###. Đụng số là lần import sau ghi đè
        // mất công Engineer đã soạn — và không ai biết vì sao.
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMasterSpecAsync(db);
        var svc = Svc(db);

        await svc.AddItemAsync("MA-MOI-1", "NQ-01", "x", null, null, null, null, Eng, UserRole.Engineer);
        await svc.AddItemAsync("MA-MOI-2", "NQ-01", "y", null, null, null, null, Eng, UserRole.Engineer);

        var local = await db.IqcMaterialSpecs
            .Where(x => x.SpecNo.StartsWith(IqcSpecEditService.LocalSpecPrefix))
            .Select(x => x.SpecNo).OrderBy(x => x).ToListAsync();

        Assert.Equal(new[] { "MES-SPEC-0001", "MES-SPEC-0002" }, local);
        Assert.DoesNotContain(local, s => s.StartsWith("CCL-SPEC-"));
    }

    [Fact]
    public async Task Ma_DA_co_spec_thi_them_vao_spec_do_chu_khong_de_ra_spec_thu_hai()
    {
        // Hai spec cùng một mã ⇒ resolver bốc phải cái nào là chuyện hên xui.
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMasterSpecAsync(db);

        var r = await Svc(db).AddItemAsync(
            "336-H1a", "KT-02", "dài 500M ± 5", null, null, null, null, Eng, UserRole.Engineer);

        Assert.True(r.Ok, r.ErrorCode);
        Assert.False(r.SpecCreated);
        Assert.Equal("CCL-SPEC-QC229", r.SpecNo);
        Assert.Equal(1, await db.IqcMaterialSpecs.CountAsync(x => x.MaterialCode == "336-H1a"));
    }

    [Fact]
    public async Task Them_cung_ma_hang_muc_lan_hai_thi_Seq_tang_chu_khong_de_len_nhau()
    {
        // 12 cặp trong file master có nhiều tiêu chí cùng mã — khoá phải ba phần.
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        var svc = Svc(db);

        await svc.AddItemAsync("TWP5050", "NQ-01", "không rách", null, null, null, null, Eng, UserRole.Engineer);
        await svc.AddItemAsync("TWP5050", "NQ-01", "không ẩm", null, null, null, null, Eng, UserRole.Engineer);

        var seqs = await db.IqcSpecItems.Where(x => x.ItemId == "NQ-01")
            .OrderBy(x => x.Seq).Select(x => x.Seq).ToListAsync();
        Assert.Equal(new[] { 1, 2 }, seqs);
    }

    // ── (b) xoá MỀM và sống sót qua seed ─────────────────────────────────

    [Fact]
    public async Task Xoa_la_xoa_MEM_du_lieu_van_con_de_truy_vet()
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMasterSpecAsync(db);
        var id = await db.IqcSpecItems.Select(x => x.Id).FirstAsync();

        var r = await Svc(db).DeactivateItemAsync(id, Eng, UserRole.Engineer);

        Assert.True(r.Ok, r.ErrorCode);
        var row = await db.IqcSpecItems.FirstAsync(x => x.Id == id);
        Assert.False(row.Active);
        Assert.Equal(Eng, row.UpdatedBy);
        Assert.Equal(1, await db.IqcSpecItems.CountAsync());   // KHÔNG xoá cứng
        Assert.Contains(_audit.Rows, x => x.Action == AuditAction.IqcSpecItemDeactivated);
    }

    [Fact]
    public async Task Dong_da_XOA_MEM_khong_bi_lan_seed_ke_tiep_hoi_sinh()
    {
        // Seed chạy MỖI LẦN boot API. Trước bước 2b, seeder có
        // `if (!e.Active) e.Active = true` ⇒ Engineer xoá xong, khởi động lại
        // là dòng sống lại, không ai hiểu vì sao.
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMasterSpecAsync(db);
        var id = await db.IqcSpecItems.Select(x => x.Id).FirstAsync();
        await Svc(db).DeactivateItemAsync(id, Eng, UserRole.Engineer);

        // Chạy lại seeder với ĐÚNG dòng đó trong file master.
        await DbSeeder.SeedIqcLibraryAsync(db,
            "ItemId,GroupCode,GroupLabelVi,GroupLabelEn,ItemVi,ItemEn,InDefaultMatrix,DefaultAcceptanceVi,DefaultAcceptanceEn,DefaultMethodVi,DefaultMethodEn,Sort\n",
            "SpecNo,MaterialCode,MaterialCodeIfs,SupplierName,Revision\nCCL-SPEC-QC229,336-H1a,,,\n",
            "SpecNo,ItemId,Seq,AcceptanceVi,AcceptanceEn,MethodVi,MethodEn,SourceFrequency,Sort\nCCL-SPEC-QC229,NQ-01,1,TIÊU CHUẨN RIÊNG,,,,,0\n");

        Assert.False(await db.IqcSpecItems.Where(x => x.Id == id).Select(x => x.Active).FirstAsync(),
            "dòng đã xoá mềm bị lần seed kế tiếp bật lại — quyết định của Engineer bị âm thầm huỷ.");
    }

    [Fact]
    public async Task Bat_lai_duoc_khi_bam_nham()
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMasterSpecAsync(db);
        var id = await db.IqcSpecItems.Select(x => x.Id).FirstAsync();
        var svc = Svc(db);

        await svc.DeactivateItemAsync(id, Eng, UserRole.Engineer);
        var r = await svc.ReactivateItemAsync(id, Eng, UserRole.Engineer);

        Assert.True(r.Ok, r.ErrorCode);
        Assert.True(await db.IqcSpecItems.Where(x => x.Id == id).Select(x => x.Active).FirstAsync());
        Assert.Contains(_audit.Rows, x => x.Action == AuditAction.IqcSpecItemReactivated);
    }

    [Fact]
    public async Task Xoa_hai_lan_KHONG_bao_loi()
    {
        // Mạng chậm, chạm đúp — không phải sự cố.
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMasterSpecAsync(db);
        var id = await db.IqcSpecItems.Select(x => x.Id).FirstAsync();
        var svc = Svc(db);

        await svc.DeactivateItemAsync(id, Eng, UserRole.Engineer);
        Assert.True((await svc.DeactivateItemAsync(id, Eng, UserRole.Engineer)).Ok);
    }

    [Fact]
    public async Task Xoa_hang_muc_KHONG_dung_toi_phieu_da_mo()
    {
        // Phiếu giữ bản ĐÓNG BĂNG riêng — sửa thư viện không hồi tố hồ sơ đã ký.
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMasterSpecAsync(db);

        var insp = new IqcInspection
        {
            PartNo = "30030146", BatchNumber = "L1", ReceivedDate = DateTime.UtcNow,
            Quantity = 1, Result = QcResult.Pending,
        };
        insp.Details.Add(new IqcResultDetail
        {
            ItemKey = "NQ-01", SpecNo = "CCL-SPEC-QC229", ItemName = "Tem nhãn",
            LabelVi = "Tem nhãn", AcceptanceVi = "TIÊU CHUẨN RIÊNG", Pass = true,
        });
        db.IqcInspections.Add(insp);
        await db.SaveChangesAsync();

        var id = await db.IqcSpecItems.Select(x => x.Id).FirstAsync();
        await Svc(db).DeactivateItemAsync(id, Eng, UserRole.Engineer);

        var frozen = await db.IqcResultDetails.SingleAsync();
        Assert.Equal("TIÊU CHUẨN RIÊNG", frozen.AcceptanceVi);
        Assert.True(frozen.Pass);
    }

    // ── (c) chỉ Engineer+ được ghi ───────────────────────────────────────

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Supervisor)]
    [InlineData(UserRole.Engineer)]
    public async Task Engineer_tro_len_duoc_ghi(string role)
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);

        Assert.True((await Svc(db).AddItemAsync(
            "TWP5050", "NQ-01", "x", null, null, null, null, "u", role)).Ok);
    }

    [Theory]
    [InlineData(UserRole.Qc)]
    [InlineData(UserRole.Operator)]
    public async Task QC_va_Operator_KHONG_ghi_duoc_master(string role)
    {
        // QC kiểm được nhưng không soạn tiêu chuẩn — cùng luật SettingItemAdd.
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);

        var r = await Svc(db).AddItemAsync("TWP5050", "NQ-01", "x", null, null, null, null, "u", role);

        Assert.False(r.Ok);
        Assert.Equal(403, r.HttpStatus);
        Assert.Empty(await db.IqcSpecItems.ToListAsync());
    }

    [Fact]
    public async Task Vai_khong_du_quyen_cung_KHONG_xoa_duoc()
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMasterSpecAsync(db);
        var id = await db.IqcSpecItems.Select(x => x.Id).FirstAsync();

        var r = await Svc(db).DeactivateItemAsync(id, "u", UserRole.Qc);

        Assert.False(r.Ok);
        Assert.Equal(403, r.HttpStatus);
        Assert.True(await db.IqcSpecItems.Where(x => x.Id == id).Select(x => x.Active).FirstAsync());
    }

    // ── (d) hạng mục phải thuộc thư viện ─────────────────────────────────

    [Fact]
    public async Task Hang_muc_ngoai_thu_vien_bi_tu_choi()
    {
        // Cho gõ tự do thì sáu tháng sau có 40 biến thể của cùng một phép đo và
        // không ai tổng hợp được.
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);

        var r = await Svc(db).AddItemAsync(
            "TWP5050", "TU-BIA-01", "x", null, null, null, null, Eng, UserRole.Engineer);

        Assert.False(r.Ok);
        Assert.Equal(422, r.HttpStatus);
        Assert.Equal("iqc.item_not_in_library", r.ErrorCode);
        Assert.Empty(await db.IqcMaterialSpecs.ToListAsync());   // không tạo spec mồ côi
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Ma_nguyen_lieu_rong_bi_tu_choi(string? code)
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);

        var r = await Svc(db).AddItemAsync(code, "NQ-01", "x", null, null, null, null, Eng, UserRole.Engineer);

        Assert.False(r.Ok);
        Assert.Equal("iqc.invalid_material_code", r.ErrorCode);
    }

    // ── đọc ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Doc_mot_ma_CHUA_co_spec_thi_bao_ro_chu_khong_bao_loi()
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);

        var v = await Svc(db).GetByMaterialCodeAsync("TWP5050");

        Assert.Null(v.SpecNo);                 // 1 trong 590 mã
        Assert.Empty(v.Items);
        Assert.Equal(2, v.Library.Count);      // vẫn đủ hạng mục để chọn thêm
    }

    [Fact]
    public async Task Doc_kem_nhan_hai_ngon_ngu_va_co_danh_dau_nguon_file_master()
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMasterSpecAsync(db);

        var v = await Svc(db).GetByMaterialCodeAsync("336-H1a");

        var it = Assert.Single(v.Items);
        Assert.Equal("Tem nhãn", it.LabelVi);
        Assert.Equal("Labels", it.LabelEn);
        Assert.Equal("Ngoại quan", it.GroupLabelVi);
        Assert.True(it.FromMasterFile);        // sửa ở đây thì import sau ghi đè
        Assert.False(v.IsLocalSpec);
    }

    [Fact]
    public async Task Mac_dinh_KHONG_hien_dong_da_tat_tru_khi_hoi_ro()
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMasterSpecAsync(db);
        var id = await db.IqcSpecItems.Select(x => x.Id).FirstAsync();
        var svc = Svc(db);
        await svc.DeactivateItemAsync(id, Eng, UserRole.Engineer);

        Assert.Empty((await svc.GetByMaterialCodeAsync("336-H1a")).Items);
        Assert.Single((await svc.GetByMaterialCodeAsync("336-H1a", includeInactive: true)).Items);
    }

    [Theory]
    [InlineData("336-h1a")]
    [InlineData("  336-H1a  ")]
    public async Task Tra_ma_khong_phan_biet_hoa_thuong_va_da_trim(string code)
        => Assert.Equal("CCL-SPEC-QC229", await WithSpec(code));

    private async Task<string?> WithSpec(string code)
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMasterSpecAsync(db);
        return (await Svc(db).GetByMaterialCodeAsync(code)).SpecNo;
    }
}
