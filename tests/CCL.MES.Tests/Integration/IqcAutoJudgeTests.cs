using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P13 bước 4 — MÁY chấm từng hạng mục, và người muốn nói khác thì phải ghi lý do
/// (Henry chốt 2026-09-04: máy chấm là RÀNG BUỘC).
///
/// <para>Ba thứ được khoá ở đây:</para>
/// <list type="number">
///   <item>máy chấm dựa trên ngưỡng ĐÃ ĐÓNG BĂNG trên dòng, không đọc lại spec;</item>
///   <item>thiếu dữ liệu ⇒ <c>Undecidable</c>, KHÔNG rơi về "đạt" (L67);</item>
///   <item>người nói khác máy mà không ghi lý do ⇒ 422, và có ghi thì server
///         đóng dấu ai đổi + lúc nào.</item>
/// </list>
/// </summary>
public sealed class IqcAutoJudgeTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public IqcAutoJudgeTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    private const string Actor = "qc-user";
    private const string Role = UserRole.Qc;

    private static IqcService Svc(MesDbContext db)
    {
        var audit = new InMemoryAuditWriter();
        var lots = new MaterialLotScanService(
            db, audit, Microsoft.Extensions.Options.Options.Create(new MaterialLotOptions()));
        return new IqcService(db, audit, lots);
    }

    /// <summary>Phiếu 4 hạng mục, mỗi hạng mục một KIỂU ghi nhận.</summary>
    private static async Task<long> SeedAsync(MesDbContext db)
    {
        var insp = new IqcInspection
        {
            PartNo = "30030146", BatchNumber = "LOT-J", LotNumber = "LOT-J",
            ReceivedDate = new DateTime(2026, 9, 5), Quantity = 10, UomQty = "rolls",
            MaterialCategory = IqcMaterialCategory.Roll, Result = QcResult.Pending,
        };
        insp.Details.Add(new IqcResultDetail
        {
            ItemKey = "RD-01", GroupCode = "NQ", ItemName = "Nhăn / Hằn", LabelVi = "Nhăn / Hằn",
            AcceptanceVi = "Không có", Kind = IqcCheckKind.DefectCount, Pass = null,
        });
        insp.Details.Add(new IqcResultDetail
        {
            ItemKey = "KT-03", GroupCode = "KT", ItemName = "Chiều rộng", LabelVi = "Chiều rộng",
            AcceptanceVi = "220 ± 2 mm", Kind = IqcCheckKind.Measure, MeasureCount = 5,
            LimitLow = 218, LimitUp = 222, LimitUnit = "mm", Pass = null,
        });
        insp.Details.Add(new IqcResultDetail
        {
            ItemKey = "BD-01", GroupCode = "BD", ItemName = "Độ bám dính", LabelVi = "Độ bám dính",
            AcceptanceVi = "≥ 10.0 N/25mm or tear", Kind = IqcCheckKind.Measure, MeasureCount = 1,
            LimitLow = 10.0, LimitUnit = "N/25mm", TearIsPass = true, Pass = null,
        });
        insp.Details.Add(new IqcResultDetail
        {
            ItemKey = "NQ-01", GroupCode = "NQ", ItemName = "Tem nhãn", LabelVi = "Tem nhãn",
            AcceptanceVi = "Đúng thông tin", Kind = IqcCheckKind.Verdict, Pass = null,
        });
        db.IqcInspections.Add(insp);
        await db.SaveChangesAsync();

        foreach (var d in insp.Details.Where(x => x.MeasureCount > 0))
            for (var seq = 1; seq <= d.MeasureCount; seq++)
                db.IqcResultMeasurements.Add(new IqcResultMeasurement
                { IqcResultDetailId = d.Id, Seq = seq, Value = null });
        await db.SaveChangesAsync();
        return insp.Id;
    }

    private static Task<long> ItemAsync(MesDbContext db, long id, string key) =>
        db.IqcResultDetails.Where(d => d.IqcInspectionId == id && d.ItemKey == key)
            .Select(d => d.Id).FirstAsync();

    private static Task<IqcResultDetail> RowAsync(MesDbContext db, long itemId) =>
        db.IqcResultDetails.AsNoTracking().SingleAsync(d => d.Id == itemId);

    // ── đếm lỗi: Ac = 0 ──────────────────────────────────────────────────

    [Fact]
    public async Task Dem_duoc_0_loi_thi_may_cham_DAT_va_nguoi_khong_phai_bam_them()
    {
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "RD-01");

        var r = await Svc(db).SetItemVerdictAsync(id, item, pass: null, null, null,
            Actor, Role, defectCount: 0);

        Assert.True(r.Ok);
        var row = await RowAsync(db, item);
        Assert.Equal("Pass", row.AutoVerdict);
        Assert.Equal("iqc.judge.zero_defect", row.AutoVerdictReason);
        Assert.True(row.Pass);          // nhận kết luận của máy
        Assert.Null(row.OverrideReason);
    }

    [Fact]
    public async Task Dem_duoc_1_loi_la_TRUOT_khong_co_so_chap_nhan()
    {
        // Ac = 0 đo được trên 3.715 lô: không một lô nào có lỗi mà vẫn đạt.
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "RD-01");

        await Svc(db).SetItemVerdictAsync(id, item, pass: null, null, null,
            Actor, Role, defectCount: 1);

        var row = await RowAsync(db, item);
        Assert.Equal("Fail", row.AutoVerdict);
        Assert.Equal("iqc.judge.defect_found", row.AutoVerdictReason);
        Assert.False(row.Pass);
    }

    [Fact]
    public async Task Chua_dem_thi_may_KHONG_QUYET_DUOC_chu_khong_phai_dat()
    {
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "RD-01");

        await Svc(db).SetItemVerdictAsync(id, item, pass: null, null, null, Actor, Role);

        var row = await RowAsync(db, item);
        Assert.Equal("Undecidable", row.AutoVerdict);
        Assert.Equal("iqc.judge.defect_incomplete", row.AutoVerdictReason);
        Assert.Null(row.Pass);          // vẫn CHƯA KIỂM
    }

    [Fact]
    public async Task So_loi_am_bi_tu_choi()
    {
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "RD-01");

        var r = await Svc(db).SetItemVerdictAsync(id, item, pass: null, null, null,
            Actor, Role, defectCount: -1);

        Assert.False(r.Ok);
        Assert.Equal(422, r.HttpStatus);
        Assert.Equal("iqc.invalid_defect_count", r.ErrorCode);
    }

    // ── ghi đè kèm lý do ─────────────────────────────────────────────────

    [Fact]
    public async Task Noi_khac_may_ma_khong_ghi_ly_do_thi_422()
    {
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "RD-01");

        var r = await Svc(db).SetItemVerdictAsync(id, item, pass: true, null, null,
            Actor, Role, defectCount: 3);

        Assert.False(r.Ok);
        Assert.Equal(422, r.HttpStatus);
        Assert.Equal("iqc.verdict_override_reason_required", r.ErrorCode);
    }

    [Fact]
    public async Task Ghi_de_KEM_ly_do_thi_server_dong_dau_ai_doi_va_luc_nao()
    {
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "RD-01");

        var before = DateTime.UtcNow.AddSeconds(-1);
        var r = await Svc(db).SetItemVerdictAsync(id, item, pass: true, null, null,
            Actor, Role, defectCount: 3,
            overrideReason: "3 vết hằn ngoài mép cắt bỏ, đã thống nhất với QA");

        Assert.True(r.Ok);
        var row = await RowAsync(db, item);
        Assert.True(row.Pass);
        Assert.Equal("Fail", row.AutoVerdict);      // kết luận MÁY vẫn đóng băng
        Assert.Equal("3 vết hằn ngoài mép cắt bỏ, đã thống nhất với QA", row.OverrideReason);
        Assert.Equal(Actor, row.OverriddenBy);      // server-stamp, client không khai
        Assert.NotNull(row.OverriddenAt);
        Assert.True(row.OverriddenAt >= before);
    }

    [Fact]
    public async Task Het_mau_thuan_thi_dau_ghi_de_duoc_XOA_chu_khong_de_lai()
    {
        // Dấu ghi đè cũ nằm lại trên một dòng không còn mâu thuẫn sẽ vu cho người
        // kiểm đã đè lên máy trong khi họ không hề làm thế.
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "RD-01");
        var svc = Svc(db);

        await svc.SetItemVerdictAsync(id, item, pass: true, null, null, Actor, Role,
            defectCount: 3, overrideReason: "lý do cũ");
        await svc.SetItemVerdictAsync(id, item, pass: null, null, null, Actor, Role,
            defectCount: 0);

        var row = await RowAsync(db, item);
        Assert.Equal("Pass", row.AutoVerdict);
        Assert.Null(row.OverrideReason);
        Assert.Null(row.OverriddenBy);
        Assert.Null(row.OverriddenAt);
    }

    // ── đo lường ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Nam_ca_5_phep_do_trong_nguong_thi_DAT()
    {
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "KT-03");

        await Svc(db).SetItemVerdictAsync(id, item, pass: null, null, null, Actor, Role,
            measurements: new double?[] { 220, 219.5, 221, 218.2, 220.4 });

        var row = await RowAsync(db, item);
        Assert.Equal("Pass", row.AutoVerdict);
        Assert.Equal("iqc.judge.all_in_range", row.AutoVerdictReason);
        Assert.True(row.Pass);
    }

    [Fact]
    public async Task Mot_phep_do_ra_ngoai_thi_TRUOT_va_chi_dung_o_thu_may()
    {
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "KT-03");

        await Svc(db).SetItemVerdictAsync(id, item, pass: null, null, null, Actor, Role,
            measurements: new double?[] { 220, 219.5, 217.0, 218.2, 220.4 });

        var row = await RowAsync(db, item);
        Assert.Equal("Fail", row.AutoVerdict);
        Assert.Equal("iqc.judge.below_low", row.AutoVerdictReason);
        // Không có con số này thì người kiểm phải tự dò lại 5 giá trị.
        Assert.Equal(3, row.AutoVerdictOffendingSeq);
    }

    [Fact]
    public async Task Moi_do_2_tren_5_thi_CHUA_QUYET_DUOC()
    {
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "KT-03");

        await Svc(db).SetItemVerdictAsync(id, item, pass: null, null, null, Actor, Role,
            measurements: new double?[] { 220, 219.5, null, null, null });

        var row = await RowAsync(db, item);
        Assert.Equal("Undecidable", row.AutoVerdict);
        Assert.Equal("iqc.judge.measurement_missing", row.AutoVerdictReason);
        Assert.Equal(3, row.AutoVerdictOffendingSeq);
        Assert.Null(row.Pass);
    }

    [Fact]
    public async Task Gui_sai_so_luong_phep_do_bi_tu_choi()
    {
        // Nhận 3 giá trị cho một hạng mục đo 5 lần rồi im lặng chấm là kết luận
        // trên dữ liệu không tồn tại.
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "KT-03");

        var r = await Svc(db).SetItemVerdictAsync(id, item, pass: null, null, null, Actor, Role,
            measurements: new double?[] { 220, 219.5, 221 });

        Assert.False(r.Ok);
        Assert.Equal(422, r.HttpStatus);
        Assert.Equal("iqc.measurement_count_mismatch", r.ErrorCode);
    }

    [Fact]
    public async Task Gia_tri_do_duoc_LUU_lai_chu_khong_chi_dung_de_cham()
    {
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "KT-03");

        await Svc(db).SetItemVerdictAsync(id, item, pass: null, null, null, Actor, Role,
            measurements: new double?[] { 220, 219.5, 221, 218.2, 220.4 });

        var vals = await db.IqcResultMeasurements.AsNoTracking()
            .Where(m => m.IqcResultDetailId == item).OrderBy(m => m.Seq)
            .Select(m => m.Value).ToListAsync();
        Assert.Equal(new double?[] { 220, 219.5, 221, 218.2, 220.4 }, vals);
    }

    // ── "or tear" ────────────────────────────────────────────────────────

    [Fact]
    public async Task Rach_vat_lieu_bien_tri_duoi_nguong_thanh_DAT()
    {
        // Vật liệu rách trước khi bong keo nghĩa là lực bám đã lớn hơn độ bền
        // của chính vật liệu — tiêu chuẩn ghi "or tear" đúng vì thế.
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "BD-01");

        await Svc(db).SetItemVerdictAsync(id, item, pass: null, null, null, Actor, Role,
            measurements: new double?[] { 6.4 }, tearObserved: true);

        var row = await RowAsync(db, item);
        Assert.Equal("Pass", row.AutoVerdict);
        Assert.Equal("iqc.judge.tear_accepted", row.AutoVerdictReason);
        Assert.True(row.TearObserved);
    }

    [Fact]
    public async Task KHONG_tick_rach_thi_duoi_nguong_van_TRUOT()
    {
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "BD-01");

        await Svc(db).SetItemVerdictAsync(id, item, pass: null, null, null, Actor, Role,
            measurements: new double?[] { 6.4 });

        var row = await RowAsync(db, item);
        Assert.Equal("Fail", row.AutoVerdict);
        Assert.Equal("iqc.judge.below_low", row.AutoVerdictReason);
    }

    // ── hạng mục người bấm ───────────────────────────────────────────────

    [Fact]
    public async Task Hang_muc_nguoi_bam_thi_may_IM_LANG_va_khong_doi_ly_do()
    {
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "NQ-01");

        var r = await Svc(db).SetItemVerdictAsync(id, item, pass: false, null, "NG-01", Actor, Role);

        Assert.True(r.Ok);
        var row = await RowAsync(db, item);
        Assert.Equal("Undecidable", row.AutoVerdict);
        Assert.Equal("iqc.judge.human_only", row.AutoVerdictReason);
        Assert.False(row.Pass);
        Assert.Null(row.OverrideReason);   // không có gì để mà trái
    }

    // ── phiếu mở TRƯỚC P13 ───────────────────────────────────────────────

    [Fact]
    public async Task Phieu_cu_chua_co_o_do_thi_dung_bu_tai_cho()
    {
        // 20 dòng kết quả có sẵn trên live mang Kind=Verdict; nhưng nếu ai đó
        // sửa một dòng cũ thành Measure thì nó phải chấm được, không được kẹt
        // vĩnh viễn vì thiếu bảng con.
        await using var db = _fx.NewContext();
        var id = await SeedAsync(db);
        var item = await ItemAsync(db, id, "NQ-01");
        var row = await db.IqcResultDetails.SingleAsync(d => d.Id == item);
        row.Kind = IqcCheckKind.Measure; row.MeasureCount = 2;
        row.LimitLow = 1; row.LimitUp = 9;
        await db.SaveChangesAsync();

        await Svc(db).SetItemVerdictAsync(id, item, pass: null, null, null, Actor, Role,
            measurements: new double?[] { 5, 6 });

        var after = await RowAsync(db, item);
        Assert.Equal("Pass", after.AutoVerdict);
        Assert.Equal(2, await db.IqcResultMeasurements.CountAsync(m => m.IqcResultDetailId == item));
    }
}
