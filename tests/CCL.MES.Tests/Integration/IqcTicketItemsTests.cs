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
/// P12 bước 3 — đọc bộ hạng mục của phiếu và ghi phán định từng hạng mục.
///
/// <para>Khoá ba thứ: (a) số MỤC do server tính chứ không để UI suy;
/// (b) tiêu chuẩn còn placeholder <c>XXX</c> KHÔNG được chấm ĐẠT;
/// (c) mọi lần ghi đều để lại vết audit.</para>
/// </summary>
public sealed class IqcTicketItemsTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public IqcTicketItemsTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    private const string Actor = "qc-user";
    private const string Role = UserRole.Qc;

    private InMemoryAuditWriter _audit = new();

    private IqcService Svc(MesDbContext db)
    {
        _audit = new InMemoryAuditWriter();
        var lots = new MaterialLotScanService(
            db, _audit, Microsoft.Extensions.Options.Options.Create(new MaterialLotOptions()));
        return new IqcService(db, _audit, lots);
    }

    /// <summary>Phiếu có 3 hạng mục trải đủ ba mục: MT-02 (hồ sơ) · NQ-01
    /// (ngoại quan) · CU-01 (chức năng, tiêu chuẩn còn XXX).</summary>
    private static async Task<long> SeedTicketAsync(MesDbContext db)
    {
        var insp = new IqcInspection
        {
            PartNo = "30030146", BatchNumber = "LOT-1", LotNumber = "LOT-1",
            ReceivedDate = new DateTime(2026, 8, 28), Quantity = 100,
            Result = QcResult.Pending,
        };
        insp.Details.Add(new IqcResultDetail
        {
            ItemKey = "MT-02", GroupCode = "MT", GroupLabelVi = "Vật liệu",
            ItemName = "Hồ sơ HSF", LabelVi = "Hồ sơ HSF", SpecNo = "CCL-SPEC-QC229",
            AcceptanceVi = "Kiểm tra HSF", Pass = null,
        });
        insp.Details.Add(new IqcResultDetail
        {
            ItemKey = "NQ-01", GroupCode = "NQ", GroupLabelVi = "Ngoại quan",
            ItemName = "Tem nhãn", LabelVi = "Tem nhãn", SpecNo = "CCL-SPEC-QC229",
            AcceptanceVi = "Đúng thông tin", Pass = null,
        });
        insp.Details.Add(new IqcResultDetail
        {
            ItemKey = "CU-01", GroupCode = "CU", GroupLabelVi = "Độ cứng",
            ItemName = "Độ cứng bút chì", LabelVi = "Độ cứng bút chì",
            SpecNo = "CCL-SPEC-QC229", AcceptanceVi = "Loại Bút, Qủa nặng:  XXX",
            AcceptanceUnspecified = true, Pass = null,
        });
        db.IqcInspections.Add(insp);
        await db.SaveChangesAsync();
        return insp.Id;
    }

    private static async Task<long> ItemIdAsync(MesDbContext db, long ticketId, string key) =>
        await db.IqcResultDetails
            .Where(d => d.IqcInspectionId == ticketId && d.ItemKey == key)
            .Select(d => d.Id).FirstAsync();

    // ── (a) server tính số MỤC ───────────────────────────────────────────

    [Fact]
    public async Task Doc_hang_muc_thi_moi_dong_mang_san_so_MUC()
    {
        await using var db = _fx.NewContext();
        var id = await SeedTicketAsync(db);

        var r = await Svc(db).GetTicketItemsAsync(id);

        Assert.NotNull(r);
        Assert.Equal(3, r!.Items.Count);
        Assert.Equal(IqcTicketSection.Documents,  r.Items.Single(i => i.ItemKey == "MT-02").Section);
        Assert.Equal(IqcTicketSection.Visual,     r.Items.Single(i => i.ItemKey == "NQ-01").Section);
        Assert.Equal(IqcTicketSection.Functional, r.Items.Single(i => i.ItemKey == "CU-01").Section);
        Assert.Equal("CCL-SPEC-QC229", r.SpecNo);
        Assert.False(r.FromDefaultMatrix);
    }

    [Fact]
    public async Task Phieu_khong_ton_tai_tra_null_chu_khong_tra_bo_rong()
    {
        // Bộ rỗng và "không có phiếu" là hai chuyện khác nhau; trộn chúng lại thì
        // controller không phân biệt được 404 với 200-rỗng.
        await using var db = _fx.NewContext();
        Assert.Null(await Svc(db).GetTicketItemsAsync(999_999));
    }

    // ── (b) tiêu chuẩn XXX ───────────────────────────────────────────────

    [Fact]
    public async Task KHONG_cho_cham_DAT_khi_tieu_chuan_con_placeholder_XXX()
    {
        // Hỏi người kiểm "đạt hay không so với XXX?" rồi lưu phán định của họ là
        // ghi một chữ ký lên tiêu chí trống.
        await using var db = _fx.NewContext();
        var id = await SeedTicketAsync(db);
        var item = await ItemIdAsync(db, id, "CU-01");

        var r = await Svc(db).SetItemVerdictAsync(id, item, pass: true, null, null, Actor, Role);

        Assert.False(r.Ok);
        Assert.Equal(422, r.HttpStatus);
        Assert.Equal("iqc.acceptance_unspecified", r.ErrorCode);
        Assert.Null(await db.IqcResultDetails.Where(d => d.Id == item).Select(d => d.Pass).FirstAsync());
    }

    [Fact]
    public async Task VAN_cho_cham_KHONG_DAT_khi_tieu_chuan_con_placeholder()
    {
        // Thấy hỏng thật thì phải ghi được, bất kể tiêu chí đã điền hay chưa.
        await using var db = _fx.NewContext();
        var id = await SeedTicketAsync(db);
        var item = await ItemIdAsync(db, id, "CU-01");

        var r = await Svc(db).SetItemVerdictAsync(id, item, pass: false, null, "NG-01", Actor, Role);

        Assert.True(r.Ok);
        Assert.False(await db.IqcResultDetails.Where(d => d.Id == item).Select(d => d.Pass).FirstAsync());
    }

    // ── ghi phán định ────────────────────────────────────────────────────

    [Fact]
    public async Task Ghi_DAT_luu_dung_va_de_lai_vet_audit()
    {
        await using var db = _fx.NewContext();
        var id = await SeedTicketAsync(db);
        var item = await ItemIdAsync(db, id, "NQ-01");
        var svc = Svc(db);

        var r = await svc.SetItemVerdictAsync(id, item, pass: true, "OK bằng mắt", null, Actor, Role);

        Assert.True(r.Ok);
        var row = await db.IqcResultDetails.FirstAsync(d => d.Id == item);
        Assert.True(row.Pass);
        Assert.Equal("OK bằng mắt", row.MeasuredValue);

        var a = Assert.Single(_audit.Rows, x => x.Action == AuditAction.IqcItemSet);
        Assert.Equal(item.ToString(), a.TargetId);
        Assert.Contains("\"verdict\":\"pass\"", a.Detail);
        Assert.Contains("NQ-01", a.Detail);
    }

    [Fact]
    public async Task Go_ve_CHUA_KIEM_duoc_khi_bam_nham()
    {
        // Người kiểm bấm nhầm phải gỡ được, nếu không họ để nguyên một phán định
        // sai còn hơn đi xin admin sửa DB.
        await using var db = _fx.NewContext();
        var id = await SeedTicketAsync(db);
        var item = await ItemIdAsync(db, id, "NQ-01");
        var svc = Svc(db);

        await svc.SetItemVerdictAsync(id, item, pass: true, null, null, Actor, Role);
        var r = await svc.SetItemVerdictAsync(id, item, pass: null, null, null, Actor, Role);

        Assert.True(r.Ok);
        Assert.Null(await db.IqcResultDetails.Where(d => d.Id == item).Select(d => d.Pass).FirstAsync());
        Assert.Contains(_audit.Rows, x => x.Detail!.Contains("\"verdict\":\"unchecked\""));
    }

    // ── CHỐT phiếu: đánh giá hết mới cho chốt (Henry 2026-08-28) ─────────

    [Fact]
    public async Task Con_hang_muc_CHUA_KIEM_thi_KHONG_chot_duoc()
    {
        // Chốt phiếu khi còn hạng mục trống là ký vào một hồ sơ chưa kiểm xong.
        await using var db = _fx.NewContext();
        var id = await SeedTicketAsync(db);
        var svc = Svc(db);
        await svc.SetItemVerdictAsync(id, await ItemIdAsync(db, id, "NQ-01"),
            pass: true, null, null, Actor, Role);

        var r = await svc.CompleteTicketAsync(id, Actor, Role);

        Assert.False(r.Ok);
        Assert.Equal(422, r.HttpStatus);
        Assert.Equal("iqc.items_incomplete", r.ErrorCode);
        Assert.Equal(2, r.Pending);          // MT-02 và CU-01 còn trống
        Assert.Equal(3, r.Total);
        Assert.Equal(QcResult.Pending,
            await db.IqcInspections.Where(x => x.Id == id).Select(x => x.Result).FirstAsync());
    }

    [Fact]
    public async Task Cham_HET_thi_chot_duoc_va_ket_luan_suy_ra_tu_hang_muc()
    {
        await using var db = _fx.NewContext();
        var id = await SeedTicketAsync(db);
        var svc = Svc(db);
        foreach (var key in new[] { "MT-02", "NQ-01" })
            await svc.SetItemVerdictAsync(id, await ItemIdAsync(db, id, key), true, null, null, Actor, Role);
        // CU-01 có tiêu chuẩn XXX nên chỉ chấm KHÔNG ĐẠT được.
        await svc.SetItemVerdictAsync(id, await ItemIdAsync(db, id, "CU-01"), false, null, "NG-01", Actor, Role);

        var r = await svc.CompleteTicketAsync(id, Actor, Role);

        Assert.True(r.Ok, r.ErrorCode);
        // Còn MỘT hạng mục không đạt ⇒ cả lô KHÔNG ĐẠT. Không có ô cho người
        // kiểm gõ kết luận trái với dữ liệu họ vừa chấm.
        Assert.Equal("Fail", r.Result);
        Assert.Equal(1, r.Failed);
        Assert.Equal(QcResult.Fail,
            await db.IqcInspections.Where(x => x.Id == id).Select(x => x.Result).FirstAsync());
        Assert.Contains(_audit.Rows, x => x.Action == AuditAction.IqcComplete);
    }

    [Fact]
    public async Task Tat_ca_DAT_thi_lo_DAT()
    {
        await using var db = _fx.NewContext();
        var id = await SeedTicketAsync(db);
        var svc = Svc(db);
        foreach (var key in new[] { "MT-02", "NQ-01" })
            await svc.SetItemVerdictAsync(id, await ItemIdAsync(db, id, key), true, null, null, Actor, Role);
        // Gỡ cờ XXX của CU-01 để chấm ĐẠT được.
        var cu = await db.IqcResultDetails.FirstAsync(d => d.IqcInspectionId == id && d.ItemKey == "CU-01");
        cu.AcceptanceUnspecified = false;
        await db.SaveChangesAsync();
        await svc.SetItemVerdictAsync(id, cu.Id, true, null, null, Actor, Role);

        var r = await svc.CompleteTicketAsync(id, Actor, Role);

        Assert.True(r.Ok, r.ErrorCode);
        Assert.Equal("Pass", r.Result);
        Assert.Equal(0, r.Failed);
    }

    [Fact]
    public async Task Phieu_CU_khong_co_hang_muc_nao_van_chot_duoc()
    {
        // 25 phiếu trước P12 không có gì để kiểm; chặn thì chúng mắc kẹt vĩnh viễn.
        await using var db = _fx.NewContext();
        var insp = new IqcInspection
        {
            PartNo = "X", BatchNumber = "L", ReceivedDate = DateTime.UtcNow,
            Quantity = 1, Result = QcResult.Pending,
        };
        db.IqcInspections.Add(insp);
        await db.SaveChangesAsync();

        var r = await Svc(db).CompleteTicketAsync(insp.Id, Actor, Role);

        Assert.True(r.Ok, r.ErrorCode);
        Assert.Equal("Pass", r.Result);
    }

    [Fact]
    public async Task Vai_khong_du_quyen_KHONG_chot_duoc()
    {
        await using var db = _fx.NewContext();
        var id = await SeedTicketAsync(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Svc(db).CompleteTicketAsync(id, "op", UserRole.Operator));
    }

    [Fact]
    public async Task Hang_muc_cua_phieu_KHAC_thi_404_chu_khong_ghi_nham()
    {
        await using var db = _fx.NewContext();
        var a = await SeedTicketAsync(db);
        var b = await SeedTicketAsync(db);
        var itemOfB = await ItemIdAsync(db, b, "NQ-01");

        var r = await Svc(db).SetItemVerdictAsync(a, itemOfB, pass: true, null, null, Actor, Role);

        Assert.False(r.Ok);
        Assert.Equal(404, r.HttpStatus);
        Assert.Null(await db.IqcResultDetails.Where(d => d.Id == itemOfB).Select(d => d.Pass).FirstAsync());
    }

    [Fact]
    public async Task Vai_KHONG_du_quyen_thi_bi_chan_truoc_khi_cham_DB()
    {
        await using var db = _fx.NewContext();
        var id = await SeedTicketAsync(db);
        var item = await ItemIdAsync(db, id, "NQ-01");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Svc(db).SetItemVerdictAsync(id, item, pass: true, null, null, "op", UserRole.Operator));

        Assert.Null(await db.IqcResultDetails.Where(d => d.Id == item).Select(d => d.Pass).FirstAsync());
    }
}
