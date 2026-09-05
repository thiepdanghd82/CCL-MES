using CCL.MES.Application.Services;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P13 — gộp nhiều bộ tiêu chuẩn của một mã về MỘT bộ.
///
/// <para>Luật cốt lõi bộ này canh: <b>chép hạng mục còn thiếu TRƯỚC, tắt bộ cũ
/// SAU</b>. Đo trên live: bộ có SpecNo lớn hơn thường ÍT hạng mục hơn —
/// <c>HS-SW200</c> bộ mới thiếu 6 hạng mục so với bộ cũ, gồm cả RoHS và nhận
/// dạng vật liệu. Tắt thẳng là âm thầm bỏ 6 phép kiểm khỏi mọi lô sau này.</para>
/// </summary>
public sealed class IqcSpecConsolidateTests : IClassFixture<IsolatedDbFixture>
{
    private readonly IsolatedDbFixture _fx;
    public IqcSpecConsolidateTests(IsolatedDbFixture fx) => _fx = fx;

    private MesDbContext Db() => _fx.NewContext();

    private static IqcSpecEditService Svc(MesDbContext db) =>
        new(db, new InMemoryAuditWriter());

    private static async Task SeedAsync(
        MesDbContext db, string code, params (string SpecNo, bool Active, string[] Items)[] sets)
    {
        foreach (var (no, active, items) in sets)
        {
            db.IqcMaterialSpecs.Add(new IqcMaterialSpec
            { SpecNo = no, MaterialCode = code, Active = active });
            foreach (var it in items)
                db.IqcSpecItems.Add(new IqcSpecItem
                {
                    SpecNo = no, ItemId = it, Seq = 1, Active = true,
                    AcceptanceVi = $"{no}/{it}",
                });
        }
        await db.SaveChangesAsync();
    }

    // ── luật cốt lõi ─────────────────────────────────────────────────────

    [Fact]
    public async Task Chep_hang_muc_con_THIEU_sang_bo_giu_lai_TRUOC_khi_tat_bo_cu()
    {
        // Đúng hình dạng của HS-SW200 trên live: bộ mới (SpecNo lớn hơn) thiếu
        // hạng mục so với bộ cũ.
        await using var db = Db();
        await SeedAsync(db, "CONS-1",
            ("CCL-SPEC-Q1146", true, ["NL-01", "NQ-01", "KT-01", "MT-01"]),
            ("CCL-SPEC-Q1304", true, ["NQ-01"]));

        var r = await Svc(db).ConsolidateAsync("CONS-1", "eng", UserRole.Engineer);

        Assert.True(r.Ok);
        Assert.Equal("CCL-SPEC-Q1304", r.KeptSpecNo);   // SpecNo lớn nhất
        Assert.Equal(3, r.ItemsMerged);                  // NL-01, KT-01, MT-01
        Assert.Equal(1, r.SpecsDeactivated);

        var kept = await db.IqcSpecItems.AsNoTracking()
            .Where(x => x.SpecNo == "CCL-SPEC-Q1304" && x.Active)
            .Select(x => x.ItemId).ToListAsync();
        // KHÔNG mất phép kiểm nào.
        Assert.Equal(4, kept.Count);
        Assert.Contains("MT-01", kept);   // RoHS — thứ dễ mất nhất nếu tắt thẳng
        Assert.Contains("NL-01", kept);
    }

    [Fact]
    public async Task Bo_cu_bi_TAT_chu_khong_bi_xoa_cung()
    {
        // Xoá mềm: hồ sơ chất lượng thì không xoá vật lý. Phiếu cũ đã đóng băng
        // bằng chứng, nhưng bộ tiêu chuẩn vẫn phải tra ngược được.
        await using var db = Db();
        await SeedAsync(db, "CONS-2",
            ("CCL-SPEC-Q2100", true, ["NQ-01"]),
            ("CCL-SPEC-Q2200", true, ["NQ-01"]));

        await Svc(db).ConsolidateAsync("CONS-2", "eng", UserRole.Engineer);

        var old = await db.IqcMaterialSpecs.AsNoTracking()
            .SingleAsync(x => x.SpecNo == "CCL-SPEC-Q2100");
        Assert.False(old.Active);
        // Hạng mục của bộ cũ VẪN CÒN — tra ngược được.
        Assert.NotEmpty(await db.IqcSpecItems.AsNoTracking()
            .Where(x => x.SpecNo == "CCL-SPEC-Q2100").ToListAsync());
    }

    [Fact]
    public async Task Hai_bo_khai_KHAC_NHAU_cho_cung_hang_muc_thi_ban_cua_bo_GIU_LAI_thang()
    {
        // Không hợp nhất, không chọn hộ. Bộ giữ lại đã có chỉ tiêu của nó.
        await using var db = Db();
        await SeedAsync(db, "CONS-3",
            ("CCL-SPEC-Q3100", true, ["KT-04"]),
            ("CCL-SPEC-Q3200", true, ["KT-04"]));

        var r = await Svc(db).ConsolidateAsync("CONS-3", "eng", UserRole.Engineer);

        Assert.Equal(0, r.ItemsMerged);   // không thiếu gì ⇒ không chép
        var items = await db.IqcSpecItems.AsNoTracking()
            .Where(x => x.SpecNo == "CCL-SPEC-Q3200" && x.Active).ToListAsync();
        Assert.Equal("CCL-SPEC-Q3200/KT-04", Assert.Single(items).AcceptanceVi);
    }

    [Fact]
    public async Task Bo_da_TAT_san_thi_khong_duoc_keo_hang_muc_cua_no_sang()
    {
        // SFG-APB2M000102 trên live: 5/6 bộ đã tắt từ trước. Kéo hạng mục của
        // bộ ai đó CỐ Ý tắt sang bộ đang dùng là hồi sinh một quyết định đã bị
        // huỷ — cùng tinh thần với luật "seeder không hồi sinh dòng đã tắt".
        await using var db = Db();
        await SeedAsync(db, "CONS-4",
            ("CCL-SPEC-Q4552", true, ["NQ-01"]),
            ("CCL-SPEC-Q4557", false, ["MT-03"]));

        var r = await Svc(db).ConsolidateAsync("CONS-4", "eng", UserRole.Engineer);

        // Chỉ còn MỘT bộ đang bật ⇒ không có gì để gộp.
        Assert.Equal("CCL-SPEC-Q4552", r.KeptSpecNo);
        Assert.Equal(0, r.ItemsMerged);
        Assert.Equal(0, r.SpecsDeactivated);
        Assert.DoesNotContain("MT-03", await db.IqcSpecItems.AsNoTracking()
            .Where(x => x.SpecNo == "CCL-SPEC-Q4552").Select(x => x.ItemId).ToListAsync());
    }

    // ── biên ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ma_chi_co_MOT_bo_thi_khong_lam_gi_ca()
    {
        await using var db = Db();
        await SeedAsync(db, "CONS-5", ("CCL-SPEC-Q5100", true, ["NQ-01"]));

        var r = await Svc(db).ConsolidateAsync("CONS-5", "eng", UserRole.Engineer);

        Assert.True(r.Ok);
        Assert.Equal("CCL-SPEC-Q5100", r.KeptSpecNo);
        Assert.Equal(0, r.ItemsMerged);
        Assert.Equal(0, r.SpecsDeactivated);
    }

    [Fact]
    public async Task Chay_lai_lan_hai_khong_doi_gi_them()
    {
        await using var db = Db();
        await SeedAsync(db, "CONS-6",
            ("CCL-SPEC-Q6100", true, ["NL-01", "NQ-01"]),
            ("CCL-SPEC-Q6200", true, ["NQ-01"]));

        await Svc(db).ConsolidateAsync("CONS-6", "eng", UserRole.Engineer);
        var second = await Svc(db).ConsolidateAsync("CONS-6", "eng", UserRole.Engineer);

        Assert.Equal(0, second.ItemsMerged);
        Assert.Equal(0, second.SpecsDeactivated);
    }

    [Fact]
    public async Task QC_KHONG_duoc_gop_day_la_viec_cua_Engineer_tro_len()
    {
        await using var db = Db();
        await SeedAsync(db, "CONS-7",
            ("CCL-SPEC-Q7100", true, ["NQ-01"]),
            ("CCL-SPEC-Q7200", true, ["NQ-01"]));

        var r = await Svc(db).ConsolidateAsync("CONS-7", "qc", UserRole.Qc);

        Assert.False(r.Ok);
        Assert.Equal(403, r.HttpStatus);
        // Và KHÔNG được đụng gì vào dữ liệu.
        Assert.Equal(2, await db.IqcMaterialSpecs.CountAsync(x => x.MaterialCode == "CONS-7" && x.Active));
    }

    [Fact]
    public async Task Chay_kho_dem_dung_nhung_KHONG_ghi()
    {
        await using var db = Db();
        await SeedAsync(db, "CONS-8",
            ("CCL-SPEC-Q8100", true, ["NL-01", "NQ-01"]),
            ("CCL-SPEC-Q8200", true, ["NQ-01"]));

        var svc = Svc(db);
        var r = await svc.ConsolidateAsync("CONS-8", "eng", UserRole.Engineer, commit: false);

        Assert.Equal(1, r.ItemsMerged);

        // Và KHÔNG được để entity bẩn trong tracker: một SaveChanges vì lý do
        // khác sau đó sẽ ghi thẳng xuống DB. "Chạy khô" mà vẫn ghi được là thứ
        // tệ hơn không có chạy khô.
        Assert.False(db.ChangeTracker.HasChanges());
        await db.SaveChangesAsync();

        await using var fresh = Db();
        Assert.Equal(2, await fresh.IqcMaterialSpecs
            .CountAsync(x => x.MaterialCode == "CONS-8" && x.Active));
    }
}
