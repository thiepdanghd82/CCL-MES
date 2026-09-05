using CCL.MES.Application.Services;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P13 bước 5 — khối NG / claim NCC ở tầng ghi.
///
/// <para>Khoá bốn điều: (a) trạng thái khởi tạo do SERVER đặt; (b) không nhảy
/// cóc được trong vòng đời; (c) mọi bước đều để lại vết audit kèm CẢ HAI đầu
/// của bước chuyển; (d) vụ phát hiện ở SẢN XUẤT — 38% số vụ thật — ghi được mà
/// không cần phiếu IQC nào.</para>
/// </summary>
public sealed class IqcNgServiceTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public IqcNgServiceTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    private const string Actor = "qc-user";
    private const string Role = UserRole.Qc;
    private InMemoryAuditWriter _audit = new();

    private IqcNgService Svc(MesDbContext db)
    {
        _audit = new InMemoryAuditWriter();
        return new IqcNgService(db, _audit);
    }

    private static IqcNgRecord Row(IqcNgStage stage = IqcNgStage.Iqc) => new()
    {
        DetectedAt = new DateTime(2026, 3, 1),
        DetectedStage = stage,
        DefectName = "Xước",
        NgAreaM2 = 12.5,
        NgRolls = 2,
        PartNo = "30030146",
        SupplierLotNo = "QT2502006",
        SupplierName = "NCC A",
    };

    private static Task<IqcNgRecord> GetAsync(MesDbContext db, long id) =>
        db.IqcNgRecords.AsNoTracking().SingleAsync(x => x.Id == id);

    // ── (a) trạng thái khởi tạo do server ────────────────────────────────

    [Fact]
    public async Task Ban_ghi_moi_luon_bat_dau_o_Open_du_client_khai_gi()
    {
        // Một bản ghi mới sinh ra ở trạng thái "đã xử lý xong" là hồ sơ bịa.
        await using var db = _fx.NewContext();
        var row = Row();
        row.Status = IqcNgStatus.Settled;
        row.Settlement = IqcClaimSettlement.Replacement;
        row.ClaimedAt = new DateTime(2026, 1, 1);

        var r = await Svc(db).CreateAsync(row, Actor, Role);

        Assert.True(r.Ok);
        var saved = await GetAsync(db, r.Id);
        Assert.Equal(IqcNgStatus.Open, saved.Status);
        Assert.Equal(IqcClaimSettlement.None, saved.Settlement);
        Assert.Null(saved.ClaimedAt);
        Assert.Equal(Actor, saved.CreatedBy);
    }

    [Fact]
    public async Task Vu_phat_hien_o_SAN_XUAT_ghi_duoc_ma_KHONG_can_phieu_IQC()
    {
        // 64/169 = 38% số vụ. Treo khối NG vào phiếu IQC thì ngần ấy vụ tiếp
        // tục sống ngoài app, đúng như một năm vừa rồi.
        await using var db = _fx.NewContext();
        var row = Row(IqcNgStage.Production);
        row.IqcInspectionId = null;
        row.MaterialLotId = null;

        var r = await Svc(db).CreateAsync(row, Actor, Role);

        Assert.True(r.Ok);
        var saved = await GetAsync(db, r.Id);
        Assert.Equal(IqcNgStage.Production, saved.DetectedStage);
        Assert.Null(saved.IqcInspectionId);
    }

    [Fact]
    public async Task Ban_ghi_thieu_thong_tin_bi_tu_choi_422()
    {
        await using var db = _fx.NewContext();
        var row = Row(); row.NgAreaM2 = null; row.NgRolls = null; row.NgQty = null;

        var r = await Svc(db).CreateAsync(row, Actor, Role);

        Assert.False(r.Ok);
        Assert.Equal(422, r.HttpStatus);
        Assert.Equal("iqc.ng.quantity_required", r.ErrorCode);
        Assert.Empty(await db.IqcNgRecords.ToListAsync());   // không ghi nửa vời
    }

    [Fact]
    public async Task Operator_KHONG_ghi_duoc_khoi_NG()
    {
        await using var db = _fx.NewContext();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Svc(db).CreateAsync(Row(), "op", UserRole.Operator));
    }

    // ── (b) vòng đời ─────────────────────────────────────────────────────

    [Fact]
    public async Task Duong_day_du_Open_Claim_XacNhan_XuLy()
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.CreateAsync(Row(), Actor, Role)).Id;

        Assert.True((await svc.ClaimAsync(id, "CCL COMPLAINT 20260304", null, Actor, Role)).Ok);
        Assert.True((await svc.SupplierConfirmAsync(id, "NCC đồng ý bù", Actor, Role)).Ok);
        var done = await svc.SettleAsync(id, IqcClaimSettlement.Replacement, null, null, Actor, Role);

        Assert.True(done.Ok);
        var saved = await GetAsync(db, id);
        Assert.Equal(IqcNgStatus.Settled, saved.Status);
        Assert.Equal(IqcClaimSettlement.Replacement, saved.Settlement);
        Assert.Equal("CCL COMPLAINT 20260304", saved.ClaimRef);
        Assert.NotNull(saved.ClaimedAt);
        Assert.NotNull(saved.SettledAt);
    }

    [Fact]
    public async Task NCC_bu_thang_bo_qua_buoc_xac_nhan()
    {
        // 84/169 vụ đi thẳng đường này.
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.CreateAsync(Row(), Actor, Role)).Id;
        await svc.ClaimAsync(id, "CCL#260203 8D", null, Actor, Role);

        var r = await svc.SettleAsync(id, IqcClaimSettlement.CreditNote, null, "trừ công nợ", Actor, Role);

        Assert.True(r.Ok);
        Assert.Equal(IqcClaimSettlement.CreditNote, (await GetAsync(db, id)).Settlement);
    }

    [Fact]
    public async Task KHONG_khep_duoc_vu_chua_tung_gui_claim()
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.CreateAsync(Row(), Actor, Role)).Id;

        var r = await svc.SettleAsync(id, IqcClaimSettlement.Replacement, null, null, Actor, Role);

        Assert.False(r.Ok);
        // Nhảy cóc bị chặn ở luật vòng đời trước cả luật "phải có ngày claim".
        Assert.Equal("iqc.ng.invalid_transition", r.ErrorCode);
        Assert.Equal(IqcNgStatus.Open, (await GetAsync(db, id)).Status);
    }

    [Fact]
    public async Task Khep_ma_khong_noi_hinh_thuc_den_bu_thi_422()
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.CreateAsync(Row(), Actor, Role)).Id;
        await svc.ClaimAsync(id, null, null, Actor, Role);

        var r = await svc.SettleAsync(id, IqcClaimSettlement.None, null, null, Actor, Role);

        Assert.False(r.Ok);
        Assert.Equal("iqc.ng.settlement_required", r.ErrorCode);
        // Bị từ chối thì KHÔNG được đổi trạng thái nửa vời.
        Assert.Equal(IqcNgStatus.Claimed, (await GetAsync(db, id)).Status);
    }

    [Fact]
    public async Task Vu_da_khep_thi_khong_mo_lai_lang_le_duoc()
    {
        // Con số "đòi được bao nhiêu" là thứ đem đi đàm phán hợp đồng.
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.CreateAsync(Row(), Actor, Role)).Id;
        await svc.ClaimAsync(id, null, null, Actor, Role);
        await svc.SettleAsync(id, IqcClaimSettlement.Replacement, null, null, Actor, Role);

        var again = await svc.ClaimAsync(id, "lần hai", null, Actor, Role);

        Assert.False(again.Ok);
        Assert.Equal("iqc.ng.invalid_transition", again.ErrorCode);
    }

    [Fact]
    public async Task Khep_khong_doi_duoc_PHAI_ghi_ly_do()
    {
        // Sáu tháng sau không ai biết là NCC từ chối, hay là mình quên đòi.
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.CreateAsync(Row(), Actor, Role)).Id;

        var thieu = await svc.CloseNoClaimAsync(id, "  ", Actor, Role);
        Assert.False(thieu.Ok);
        Assert.Equal("iqc.ng.close_reason_required", thieu.ErrorCode);

        var co = await svc.CloseNoClaimAsync(id, "NCC đã ngừng hợp tác từ 02/2026", Actor, Role);
        Assert.True(co.Ok);
        var saved = await GetAsync(db, id);
        Assert.Equal(IqcNgStatus.ClosedNoClaim, saved.Status);
        Assert.Equal("NCC đã ngừng hợp tác từ 02/2026", saved.Remark);
    }

    [Fact]
    public async Task Vu_khong_ton_tai_tra_404()
    {
        await using var db = _fx.NewContext();
        var r = await Svc(db).ClaimAsync(999_999, null, null, Actor, Role);
        Assert.False(r.Ok);
        Assert.Equal(404, r.HttpStatus);
    }

    // ── (c) audit ────────────────────────────────────────────────────────

    [Fact]
    public async Task Audit_ghi_CA_HAI_dau_cua_buoc_chuyen()
    {
        // "Đổi sang Settled" một mình không cho biết vụ đó có đi qua bước NCC
        // xác nhận hay không.
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.CreateAsync(Row(), Actor, Role)).Id;
        await svc.ClaimAsync(id, "CCL COMPLAINT 20260304", null, Actor, Role);

        var claim = Assert.Single(_audit.Rows, a => a.Action == "IQC_NG_CLAIM");
        Assert.Contains("\"from\":\"Open\"", claim.Detail);
        Assert.Contains("\"to\":\"Claimed\"", claim.Detail);
        Assert.Contains("CCL COMPLAINT 20260304", claim.Detail);
        Assert.Equal(id.ToString(), claim.TargetId);
    }

    [Fact]
    public async Task Tao_moi_de_lai_vet_kem_so_luong_va_cong_doan()
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        await svc.CreateAsync(Row(IqcNgStage.Production), Actor, Role);

        var a = Assert.Single(_audit.Rows, x => x.Action == "IQC_NG_CREATE");
        Assert.Contains("Production", a.Detail);
        Assert.Contains("30030146", a.Detail);
        Assert.Contains("12.5", a.Detail);
    }

    // ── (d) đọc ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Loc_duoc_theo_trang_thai_va_theo_ma_vat_lieu()
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var a = (await svc.CreateAsync(Row(), Actor, Role)).Id;
        var b = Row(); b.PartNo = "30030287"; b.DetectedAt = new DateTime(2026, 4, 1);
        var bId = (await svc.CreateAsync(b, Actor, Role)).Id;
        await svc.ClaimAsync(bId, null, null, Actor, Role);

        var open = await svc.ListAsync(status: IqcNgStatus.Open);
        Assert.Equal(a, Assert.Single(open).Id);

        var byPart = await svc.ListAsync(partNo: "30030287");
        Assert.Equal(bId, Assert.Single(byPart).Id);

        // Mới nhất trước — QC mở màn hình là thấy việc của tuần này.
        var all = await svc.ListAsync();
        Assert.Equal(bId, all[0].Id);
    }
}
