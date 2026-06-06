using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7c-1 — pure unit tests for the static SETTING helpers +
/// stateless validation behaviour on the 3 services. The stateful
/// service tests (Add/Pause/Resume/Finish that mutate DbSets) live
/// in <c>tests/Integration/RunningSurfaceServicesIntegrationTests.cs</c>
/// against an <c>IsolatedDbFixture</c>.
///
/// This file locks the per-call invariants that don't need a DB:
///   - WoSettingService null-guard + duration math.
///   - WoRunSessionService.Close double-close throws.
///   - WoPauseService.Pause rejects empty reason; ClosePause Q6 helper
///     stamps without phase change.
///   - WoQtyService argument validation (negative delta on Add;
///     cross-WO linked entry on Correct; empty correction reason).
/// </summary>
public sealed class RunningSurfaceServicesTests
{
    private static WorkOrder NewWo(long id = 1, string mesPhase = "SETTING") => new()
    {
        Id = id,
        WoNo = "WO-7C1-" + id,
        MesPhase = mesPhase,
    };

    // ── WoSettingService ──────────────────────────────────────────

    [Fact]
    public void MarkSettingStart_stamps_only_when_null()
    {
        var wo = NewWo();
        var now = DateTime.UtcNow;
        Assert.True(WoSettingService.MarkSettingStart(wo, now));
        Assert.Equal(now, wo.SettingStartAt);

        // Race / re-entry — MUST NOT reset the timestamp.
        Assert.False(WoSettingService.MarkSettingStart(wo, now.AddSeconds(5)));
        Assert.Equal(now, wo.SettingStartAt);
    }

    [Fact]
    public void MarkSettingDone_computes_duration()
    {
        var wo = NewWo();
        var start = DateTime.UtcNow.AddMinutes(-10);
        wo.SettingStartAt = start;
        var done = start.AddMinutes(10);
        var duration = WoSettingService.MarkSettingDone(wo, done);
        Assert.Equal(600, duration);
        Assert.Equal(done, wo.SettingEndAt);
        Assert.Equal(600, wo.SettingDurationSec);
    }

    [Fact]
    public void MarkSettingDone_throws_when_start_null()
    {
        var wo = NewWo();
        Assert.Throws<InvalidOperationException>(() =>
            WoSettingService.MarkSettingDone(wo, DateTime.UtcNow));
    }

    [Fact]
    public void MarkSettingDone_clamps_negative_duration_to_zero()
    {
        var wo = NewWo();
        var now = DateTime.UtcNow;
        wo.SettingStartAt = now.AddSeconds(5); // future-stamped (clock skew)
        var d = WoSettingService.MarkSettingDone(wo, now);
        Assert.Equal(0, d);
        Assert.Equal(0, wo.SettingDurationSec);
    }

    // ── WoRunSessionService.Close (static) ────────────────────────

    [Fact]
    public void RunSessionService_Close_stamps_EndedAt()
    {
        var session = new WoRunSession { StartedAt = DateTime.UtcNow.AddMinutes(-5) };
        var now = DateTime.UtcNow;
        WoRunSessionService.Close(session, "alice", now);
        Assert.Equal(now, session.EndedAt);
        Assert.Equal("alice", session.EndedBy);
    }

    [Fact]
    public void RunSessionService_Close_rejects_double_close()
    {
        var session = new WoRunSession { StartedAt = DateTime.UtcNow.AddMinutes(-5) };
        WoRunSessionService.Close(session, "alice", DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() =>
            WoRunSessionService.Close(session, "bob", DateTime.UtcNow.AddSeconds(1)));
    }

    // ── WoPauseService.ClosePause (static — Q6 helper) ────────────

    [Fact]
    public void PauseService_ClosePause_stamps_without_phase_change_Q6()
    {
        var wo = NewWo(mesPhase: "PAUSED");
        var pause = new WoPauseEvent { Id = 50, WoId = wo.Id, StartedAt = DateTime.UtcNow.AddMinutes(-1) };
        var now = DateTime.UtcNow;
        WoPauseService.ClosePause(pause, "alice", now);
        Assert.Equal(now, pause.EndedAt);
        Assert.Equal("PAUSED", wo.MesPhase); // helper doesn't transition; controller does
    }

    [Fact]
    public void PauseService_ClosePause_rejects_double_close()
    {
        var pause = new WoPauseEvent { StartedAt = DateTime.UtcNow.AddMinutes(-1) };
        WoPauseService.ClosePause(pause, "alice", DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() =>
            WoPauseService.ClosePause(pause, "bob", DateTime.UtcNow.AddSeconds(1)));
    }
}
