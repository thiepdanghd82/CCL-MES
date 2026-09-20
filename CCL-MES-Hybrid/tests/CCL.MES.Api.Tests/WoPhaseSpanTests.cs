using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Mốc thời gian công đoạn (<see cref="WoPhaseSpan"/>) — nguồn số cho hiệu
/// suất + OEE của PREPRESS · IPQC · FQC · OQC.
///
/// <para><b>Vì sao đóng dấu ở SaveChanges chứ không ở controller.</b>
/// <c>wo.MesPhase = ...</c> nằm ở 14 chỗ rải 6 file. Đóng dấu tay thì chỗ thứ
/// 15 thêm sau sẽ lặng lẽ không sinh dữ liệu, và cái sai chỉ lộ ra lúc ai đó
/// hỏi vì sao OEE một công đoạn trống rỗng. Test dưới đây đổi phase bằng
/// DbContext TRẦN — không qua controller nào — nên nó chứng minh đúng điều
/// cần chứng minh: bất kể ai đổi phase ở đâu, mốc vẫn được ghi.</para>
///
/// <para><b>Bug class được chặn.</b> Tiền lệ SETTING
/// (<c>WorkOrder.SettingStartAt</c>) ghi rõ "set MỘT LẦN, re-entry không
/// reset" — nó BỎ QUA lần vào thứ hai. Hệ này có rework thật (IPQC StopLine →
/// PREPRESS, FQC Reject → PREPRESS…), nên copy cách ấy là báo sai giờ đúng ở
/// những WO đáng đo nhất. <see cref="Rework_loop_keeps_every_visit_separately"/>
/// khoá chuyện đó.</para>
/// </summary>
public sealed class WoPhaseSpanTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public WoPhaseSpanTests(MesApiFactory fx) => _fx = fx;

    private async Task<long> SeedWoAsync(string phase, string woNo)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var customer = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "Cust" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var product = new Product
        {
            ProductCode = "P-" + Guid.NewGuid().ToString("N")[..6],
            Name = "Prod",
            CustomerId = customer.Id,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var wo = new WorkOrder
        {
            WoNo = woNo,
            CustomerId = customer.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            TargetQty = 100,
            Uom = "pcs",
            MesPhase = phase,
            CurrentStep = ProcessStepCode.PrePressCheck,
            Status = WoStatus.InProgress,
            CreatedBy = "seed",
            UpdatedBy = "seed",
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        return wo.Id;
    }

    /// <summary>Đổi phase bằng DbContext trần — cố ý KHÔNG qua controller.</summary>
    private async Task MovePhaseAsync(long woId, string toPhase, string actor)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var wo = await db.WorkOrders.FirstAsync(w => w.Id == woId);
        wo.MesPhase = toPhase;
        wo.UpdatedBy = actor;
        wo.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task<List<WoPhaseSpan>> SpansAsync(long woId)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        return await db.WoPhaseSpans.AsNoTracking()
            .Where(s => s.WoId == woId).OrderBy(s => s.Id).ToListAsync();
    }

    // ── Đóng dấu cơ bản ─────────────────────────────────────────────

    [Fact]
    public async Task Entering_a_tracked_phase_opens_a_span()
    {
        var wo = await SeedWoAsync("NEW", $"WO-SPAN-A-{Guid.NewGuid():N}"[..18]);
        await MovePhaseAsync(wo, "PREPRESS", "alice");

        var spans = await SpansAsync(wo);
        var open = Assert.Single(spans);
        Assert.Equal("PREPRESS", open.Phase);
        Assert.Null(open.EndedAt);
        Assert.Equal("alice", open.StartedBy);
    }

    [Fact]
    public async Task Leaving_a_phase_closes_its_span_and_opens_the_next()
    {
        var wo = await SeedWoAsync("NEW", $"WO-SPAN-B-{Guid.NewGuid():N}"[..18]);
        await MovePhaseAsync(wo, "PREPRESS", "alice");
        await MovePhaseAsync(wo, "IPQC_WAIT", "bob");

        var spans = await SpansAsync(wo);
        Assert.Equal(2, spans.Count);

        Assert.Equal("PREPRESS", spans[0].Phase);
        Assert.NotNull(spans[0].EndedAt);       // đã đóng
        Assert.Equal("bob", spans[0].EndedBy);  // ai đẩy WO đi thì đóng sổ

        Assert.Equal("IPQC_WAIT", spans[1].Phase);
        Assert.Null(spans[1].EndedAt);          // đang mở
    }

    // ── THE regression — vòng rework ────────────────────────────────

    /// <summary>
    /// PREPRESS → IPQC_WAIT → PREPRESS (StopLine) → IPQC_WAIT. Phải còn ĐỦ 4
    /// dòng: hai lần vào PREPRESS và hai lần vào IPQC, mỗi lần một dòng. Nếu
    /// ai đó đổi sang kiểu "một cột Start cho mỗi công đoạn" như SETTING, lần
    /// vào thứ hai biến mất và test này đỏ.
    /// </summary>
    [Fact]
    public async Task Rework_loop_keeps_every_visit_separately()
    {
        var wo = await SeedWoAsync("NEW", $"WO-SPAN-C-{Guid.NewGuid():N}"[..18]);
        await MovePhaseAsync(wo, "PREPRESS", "alice");
        await MovePhaseAsync(wo, "IPQC_WAIT", "bob");
        await MovePhaseAsync(wo, "PREPRESS", "qc");      // StopLine → quay lại
        await MovePhaseAsync(wo, "IPQC_WAIT", "alice");

        var spans = await SpansAsync(wo);
        Assert.Equal(4, spans.Count);
        Assert.Equal(new[] { "PREPRESS", "IPQC_WAIT", "PREPRESS", "IPQC_WAIT" },
                     spans.Select(s => s.Phase).ToArray());

        // Ba dòng đầu đã đóng, dòng cuối còn mở.
        Assert.All(spans.Take(3), s => Assert.NotNull(s.EndedAt));
        Assert.Null(spans[3].EndedAt);

        // Hai LẦN vào PREPRESS vẫn đếm được riêng — đó là cả lý do dùng bảng.
        Assert.Equal(2, spans.Count(s => s.Phase == "PREPRESS"));

        // VisitNo đánh số đúng thứ tự — "WO này quay lại IPQC mấy lần" đọc
        // được bằng một câu SQL, không cần window function.
        Assert.Equal(new[] { 1, 1, 2, 2 }, spans.Select(s => s.VisitNo).ToArray());
    }

    /// <summary>
    /// Bất biến thật của hệ: <b>một WO ở đúng MỘT công đoạn tại một thời
    /// điểm</b>. Ép ở tầng DB (<c>UX_WoPhaseSpans_OpenPerWo</c>) chứ không chỉ
    /// trông chờ code nhớ đóng sổ — bản index yếu hơn
    /// (<c>UNIQUE(WoId, Phase)</c>) vẫn cho tồn tại khoảng PREPRESS mở song
    /// song với khoảng IPQC mở, tức đúng cái lỗi quên-đóng-sổ mà bảng này
    /// sinh ra để chặn.
    /// </summary>
    [Fact]
    public async Task A_wo_can_never_have_two_open_spans_at_once()
    {
        var wo = await SeedWoAsync("NEW", $"WO-SPAN-H-{Guid.NewGuid():N}"[..18]);
        await MovePhaseAsync(wo, "PREPRESS", "alice");
        await MovePhaseAsync(wo, "IPQC_WAIT", "bob");
        await MovePhaseAsync(wo, "PREPRESS", "qc");
        await MovePhaseAsync(wo, "FQC_PENDING", "alice");

        var spans = await SpansAsync(wo);
        Assert.Equal(1, spans.Count(s => s.EndedAt is null));

        // Và thử ép tay hai khoảng mở → DB phải từ chối.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        db.WoPhaseSpans.Add(new WoPhaseSpan
        {
            WoId = wo, Phase = "OQC_PENDING", VisitNo = 1,
            StartedAt = DateTime.UtcNow, StartedBy = "x", CreatedAt = DateTime.UtcNow,
        });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ── Không dựng nguồn sự thật thứ hai ────────────────────────────

    /// <summary>
    /// SETTING đã có 3 cột trên WorkOrder; RUNNING/PAUSED đã có WoRunSession +
    /// WoPauseEvent. Ghi thêm ở đây nữa là hai nguồn cho cùng một con số —
    /// đúng bệnh L63. Danh sách theo dõi phải giữ đúng phạm vi.
    /// </summary>
    [Theory]
    [InlineData("SETTING")]
    [InlineData("RUNNING")]
    [InlineData("PAUSED")]
    public async Task Phases_that_already_have_a_time_source_are_not_duplicated(string phase)
    {
        var wo = await SeedWoAsync("NEW", $"WO-SPAN-D-{Guid.NewGuid():N}"[..18]);
        await MovePhaseAsync(wo, phase, "alice");

        Assert.Empty(await SpansAsync(wo));
    }

    /// <summary>Rời một công đoạn ĐƯỢC theo dõi sang một công đoạn KHÔNG theo
    /// dõi vẫn phải đóng sổ — nếu không, khoảng PREPRESS treo mở mãi và cộng
    /// tới "bây giờ" suốt đời WO.</summary>
    [Fact]
    public async Task Leaving_a_tracked_phase_for_an_untracked_one_still_closes_the_span()
    {
        var wo = await SeedWoAsync("NEW", $"WO-SPAN-E-{Guid.NewGuid():N}"[..18]);
        await MovePhaseAsync(wo, "PREPRESS", "alice");
        await MovePhaseAsync(wo, "SETTING", "bob");

        var span = Assert.Single(await SpansAsync(wo));
        Assert.Equal("PREPRESS", span.Phase);
        Assert.NotNull(span.EndedAt);
    }

    // ── Bốn công đoạn đề bài yêu cầu ────────────────────────────────

    [Theory]
    [InlineData("PREPRESS")]
    [InlineData("IPQC_WAIT")]
    [InlineData("QA_PENDING")]
    [InlineData("FQC_PENDING")]
    [InlineData("OQC_PENDING")]
    public async Task All_four_requested_stages_are_tracked(string phase)
    {
        var wo = await SeedWoAsync("NEW", $"WO-SPAN-F-{Guid.NewGuid():N}"[..18]);
        await MovePhaseAsync(wo, phase, "alice");

        var span = Assert.Single(await SpansAsync(wo));
        Assert.Equal(phase, span.Phase);
    }

    /// <summary>
    /// Đường ghi KHÔNG đóng dấu audit (không chạm UpdatedAt/UpdatedBy) phải ra
    /// "system", KHÔNG được lấy actor còn sót của lần sửa trước — gán nhầm
    /// việc cho người khác tệ hơn là để trống.
    /// </summary>
    [Fact]
    public async Task Actor_is_system_when_the_write_path_stamped_nothing()
    {
        var wo = await SeedWoAsync("NEW", $"WO-SPAN-I-{Guid.NewGuid():N}"[..18]);
        await MovePhaseAsync(wo, "PREPRESS", "alice");   // có đóng dấu

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var row = await db.WorkOrders.FirstAsync(w => w.Id == wo);
            row.MesPhase = "IPQC_WAIT";                  // KHÔNG chạm UpdatedAt/By
            await db.SaveChangesAsync();
        }

        var ipqc = (await SpansAsync(wo)).Single(s => s.Phase == "IPQC_WAIT");
        Assert.Equal("system", ipqc.StartedBy);
        Assert.NotEqual("alice", ipqc.StartedBy);        // ← actor còn sót
    }

    /// <summary>
    /// CÙNG một người thao tác hai lần liên tiếp: <c>UpdatedBy</c> không đổi
    /// giá trị nên EF báo "không sửa". Nếu chỉ soi <c>UpdatedBy.IsModified</c>
    /// thì lần thứ hai rơi nhầm về "system" — đã đo được đúng lỗi đó trên live
    /// (admin bấm force-phase, span ghi "system"). <c>UpdatedAt</c> là mốc thời
    /// gian nên luôn đổi, và đó là thứ phân biệt được "có đóng dấu" với "còn sót".
    /// </summary>
    [Fact]
    public async Task Same_actor_twice_in_a_row_is_still_attributed_not_downgraded_to_system()
    {
        var wo = await SeedWoAsync("NEW", $"WO-SPAN-J-{Guid.NewGuid():N}"[..18]);
        await MovePhaseAsync(wo, "PREPRESS", "admin");
        await MovePhaseAsync(wo, "IPQC_WAIT", "admin");   // cùng người, y hệt giá trị

        var spans = await SpansAsync(wo);
        Assert.All(spans, sp => Assert.Equal("admin", sp.StartedBy));
        Assert.DoesNotContain(spans, sp => sp.StartedBy == "system");
        // Người đóng sổ khoảng PREPRESS cũng là admin, không phải "system".
        Assert.Equal("admin", spans.Single(sp => sp.Phase == "PREPRESS").EndedBy);
    }

    /// <summary>Phase lưu tên MesPhase CHÍNH TẮC, không phải tên bước legacy
    /// (PrePressCheck…). Đây đúng chỗ audit WO_ADVANCE làm sai và vì thế
    /// không dùng được để suy ra thời gian công đoạn (L19).</summary>
    [Fact]
    public async Task Span_stores_canonical_mes_phase_not_the_legacy_step_name()
    {
        var wo = await SeedWoAsync("NEW", $"WO-SPAN-G-{Guid.NewGuid():N}"[..18]);
        await MovePhaseAsync(wo, "PREPRESS", "alice");

        var span = Assert.Single(await SpansAsync(wo));
        Assert.Equal("PREPRESS", span.Phase);
        Assert.NotEqual("PrePressCheck", span.Phase);
    }
}
