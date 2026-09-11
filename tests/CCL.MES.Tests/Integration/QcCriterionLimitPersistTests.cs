using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.Auth;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P4 — cặn số rút từ cặp Target + Tolerance phải XUỐNG TỚI DB, không chỉ đúng
/// trong hàm thuần.
///
/// <para>Trước 2026-09-11, <c>SpecQcWindowService.ApplyRow</c> cố ý để trống ba
/// cột số (comment "untouched"). Hệ quả: kỹ sư gõ "20 ± 0,5 mm" vào tab QC
/// Plans mà không gì so được với con số người kiểm đo ra — hạng mục đo lường
/// vẫn phải chấm bằng mắt, trong khi cột chứa đã nằm sẵn trong bảng.</para>
/// </summary>
public sealed class QcCriterionLimitPersistTests : IClassFixture<IsolatedDbFixture>
{
    private readonly IsolatedDbFixture _fx;
    private readonly InMemoryAuditWriter _audit = new();
    public QcCriterionLimitPersistTests(IsolatedDbFixture fx) => _fx = fx;

    private SpecQcWindowService Svc() => new(_fx.NewContext(), _audit);

    private async Task<QcCriterion> SaveOneAsync(
        string name, string? target, string? tolerance, QcStage stage)
    {
        await Svc().UpsertStageAsync(
            revisionId: _fx.SeedRevisionId, stage: stage,
            rows: new List<QcCriterionRow> { new(null, name, target, tolerance, null, null) },
            actor: "p4.test", actorRole: UserRole.Engineer);

        await using var db = _fx.NewContext();
        return await db.QcCriteria.AsNoTracking()
            .Where(c => c.Name == name)
            .OrderByDescending(c => c.Id).FirstAsync();
    }

    [Fact]
    public async Task Go_dung_sai_day_du_thi_can_so_xuong_DB()
    {
        var c = await SaveOneAsync("P4 đầy đủ", "", "20 ± 0,5 mm", QcStage.IpqcPrint);

        Assert.Equal(19.5, c.ToleranceMin!.Value, 3);            // ← đỏ nếu bỏ nhánh ghi
        Assert.Equal(20.5, c.ToleranceMax!.Value, 3);
        Assert.Equal(20, c.TargetValue!.Value, 3);
        Assert.Equal("mm", c.Unit);
    }

    [Fact]
    public async Task Target_va_Tolerance_o_HAI_O_van_ghep_duoc()
    {
        // Cách gõ tự nhiên nhất với bảng 2 cột: gốc một ô, dung sai một ô.
        var c = await SaveOneAsync("P4 hai ô", "20 mm", "±0,5", QcStage.IpqcCut);

        Assert.Equal(19.5, c.ToleranceMin!.Value, 3);
        Assert.Equal(20.5, c.ToleranceMax!.Value, 3);
    }

    [Fact]
    public async Task CHU_giu_nguyen_nhu_ky_su_go_chu_khong_bi_thay_bang_so()
    {
        // Số là thứ RÚT RA; chữ mới là thứ kỹ sư viết và là thứ in ra hồ sơ.
        var c = await SaveOneAsync("P4 giữ chữ", "20 mm", "±0,5", QcStage.Fqc);

        Assert.Equal("20 mm", c.PassCriteria);
        Assert.Equal("±0,5", c.MeasureMethod);
    }

    [Fact]
    public async Task Cau_CHU_khong_doc_duoc_thi_de_NULL_chu_khong_bia()
    {
        // 11/11 hạng mục Measure của IPQC hiện ghi kiểu này.
        var c = await SaveOneAsync("P4 chữ", "", "Theo bản vẽ", QcStage.Oqc);

        Assert.Null(c.ToleranceMin);
        Assert.Null(c.ToleranceMax);
        Assert.Null(c.TargetValue);
        Assert.Equal("Theo bản vẽ", c.MeasureMethod);            // chữ vẫn còn
    }

    [Fact]
    public async Task Sua_tu_CO_SO_sang_KHONG_doc_duoc_thi_XOA_can_cu()
    {
        // Ca dễ sai nhất: lần đầu gõ "20 ± 0,5" ra cặn số, lần sau sửa thành
        // "Theo bản vẽ". Giữ lại cặn cũ là để hồ sơ nói một đằng, máy chấm một
        // nẻo — và không ai nhìn thấy vì cột số không hiện trên bảng.
        var first = await SaveOneAsync("P4 đổi ý", "", "20 ± 0,5 mm", QcStage.IpqcPrint);
        Assert.NotNull(first.ToleranceMin);

        await Svc().UpsertStageAsync(
            revisionId: _fx.SeedRevisionId, stage: QcStage.IpqcPrint,
            rows: new List<QcCriterionRow> { new(first.Id, "P4 đổi ý", "", "Theo bản vẽ", null, null) },
            actor: "p4.test", actorRole: UserRole.Engineer);

        await using var db = _fx.NewContext();
        var after = await db.QcCriteria.AsNoTracking().FirstAsync(c => c.Id == first.Id);
        Assert.Null(after.ToleranceMin);                         // ← đỏ nếu chỉ ghi khi đọc được
        Assert.Null(after.ToleranceMax);
        Assert.Null(after.Unit);
    }
}
