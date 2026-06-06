using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P10.7c-1 — integration coverage for the 3 RUNNING-surface services
/// against a real SQLite DB via <see cref="IsolatedDbFixture"/>. Tests
/// assert end-to-end: service mutates entities + SaveChanges persists
/// + re-query confirms the wire shape matches the contract §5.4 entity
/// definitions.
/// </summary>
public sealed class RunningSurfaceServicesIntegrationTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public RunningSurfaceServicesIntegrationTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    private async Task<long> SeedWoAsync(string mesPhase = "IPQC_APPROVED")
    {
        var woId = await _fx.SeedWorkOrderAsync(
            _fx.SeedRevisionId,
            woNo: "WO-7C1-INT-" + Guid.NewGuid().ToString("N")[..6]);
        using var db = _fx.NewContext();
        var wo = await db.WorkOrders.FindAsync(woId);
        wo!.MesPhase = mesPhase;
        await db.SaveChangesAsync();
        return woId;
    }

    // ── WoRunSessionService ───────────────────────────────────────

    [Fact]
    public async Task Start_persists_session_row_and_phase()
    {
        var woId = await SeedWoAsync();
        using (var db = _fx.NewContext())
        {
            var wo = await db.WorkOrders.FindAsync(woId);
            var svc = new WoRunSessionService(db);
            svc.Start(wo!, "alice", DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        using var verify = _fx.NewContext();
        var sessions = await verify.WoRunSessions.Where(s => s.WoId == woId).ToListAsync();
        Assert.Single(sessions);
        Assert.Null(sessions[0].EndedAt);
        var woReload = await verify.WorkOrders.FindAsync(woId);
        Assert.Equal("RUNNING", woReload!.MesPhase);
    }

    [Fact]
    public async Task Finish_closes_session_and_transitions_to_FQC_PENDING()
    {
        var woId = await SeedWoAsync("RUNNING");

        long sessionId;
        using (var db = _fx.NewContext())
        {
            var session = new WoRunSession { WoId = woId, StartedAt = DateTime.UtcNow.AddMinutes(-30), StartedBy = "alice" };
            db.WoRunSessions.Add(session);
            await db.SaveChangesAsync();
            sessionId = session.Id;
        }

        using (var db = _fx.NewContext())
        {
            var wo = await db.WorkOrders.FindAsync(woId);
            var session = await db.WoRunSessions.FindAsync(sessionId);
            var svc = new WoRunSessionService(db);
            svc.Finish(wo!, session!, "alice", DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        using var verify = _fx.NewContext();
        var sessionAfter = await verify.WoRunSessions.FindAsync(sessionId);
        Assert.NotNull(sessionAfter!.EndedAt);
        Assert.Equal("alice", sessionAfter.EndedBy);
        var woReload = await verify.WorkOrders.FindAsync(woId);
        Assert.Equal("FQC_PENDING", woReload!.MesPhase);
    }

    // ── WoPauseService ────────────────────────────────────────────

    [Fact]
    public async Task Pause_closes_session_and_creates_pause_and_transitions_to_PAUSED()
    {
        var woId = await SeedWoAsync("RUNNING");
        long sessionId;
        using (var db = _fx.NewContext())
        {
            var session = new WoRunSession { WoId = woId, StartedAt = DateTime.UtcNow.AddMinutes(-5), StartedBy = "alice" };
            db.WoRunSessions.Add(session);
            await db.SaveChangesAsync();
            sessionId = session.Id;
        }

        using (var db = _fx.NewContext())
        {
            var wo = await db.WorkOrders.FindAsync(woId);
            var session = await db.WoRunSessions.FindAsync(sessionId);
            var svc = new WoPauseService(db);
            svc.Pause(wo!, session!, "ML-MAT", "nguyên liệu chậm", "alice", DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        using var verify = _fx.NewContext();
        var pauses = await verify.WoPauseEvents.Where(p => p.WoId == woId).ToListAsync();
        Assert.Single(pauses);
        Assert.Equal("ML-MAT", pauses[0].ReasonCode);
        Assert.Null(pauses[0].EndedAt);
        var session2 = await verify.WoRunSessions.FindAsync(sessionId);
        Assert.NotNull(session2!.EndedAt);
        var woReload = await verify.WorkOrders.FindAsync(woId);
        Assert.Equal("PAUSED", woReload!.MesPhase);
    }

    [Fact]
    public async Task Resume_closes_pause_and_opens_new_session_and_transitions_to_RUNNING()
    {
        var woId = await SeedWoAsync("PAUSED");
        long pauseId;
        long oldSessionId;
        using (var db = _fx.NewContext())
        {
            var oldSession = new WoRunSession
            {
                WoId = woId,
                StartedAt = DateTime.UtcNow.AddMinutes(-10),
                EndedAt = DateTime.UtcNow.AddMinutes(-5),
                StartedBy = "alice",
                EndedBy = "alice",
            };
            db.WoRunSessions.Add(oldSession);
            await db.SaveChangesAsync();
            oldSessionId = oldSession.Id;

            var pause = new WoPauseEvent
            {
                WoId = woId,
                RunSessionId = oldSessionId,
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                ReasonCode = "ML-MAT",
                StartedBy = "alice",
            };
            db.WoPauseEvents.Add(pause);
            await db.SaveChangesAsync();
            pauseId = pause.Id;
        }

        using (var db = _fx.NewContext())
        {
            var wo = await db.WorkOrders.FindAsync(woId);
            var pause = await db.WoPauseEvents.FindAsync(pauseId);
            var svc = new WoPauseService(db);
            svc.Resume(wo!, pause!, "alice", DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        using var verify = _fx.NewContext();
        var pauseAfter = await verify.WoPauseEvents.FindAsync(pauseId);
        Assert.NotNull(pauseAfter!.EndedAt);
        var sessions = await verify.WoRunSessions.Where(s => s.WoId == woId).OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.Null(sessions[1].EndedAt); // new session live
        var woReload = await verify.WorkOrders.FindAsync(woId);
        Assert.Equal("RUNNING", woReload!.MesPhase);
    }

    // ── WoQtyService ──────────────────────────────────────────────

    [Fact]
    public async Task Add_persists_entry_and_updates_cache()
    {
        var woId = await SeedWoAsync("RUNNING");
        long sessionId;
        using (var db = _fx.NewContext())
        {
            var session = new WoRunSession { WoId = woId, StartedAt = DateTime.UtcNow, StartedBy = "alice" };
            db.WoRunSessions.Add(session);
            await db.SaveChangesAsync();
            sessionId = session.Id;
        }

        using (var db = _fx.NewContext())
        {
            var wo = await db.WorkOrders.FindAsync(woId);
            var svc = new WoQtyService(db);
            svc.Add(wo!, sessionId, 100, 0, null, null, "alice", DateTime.UtcNow);
            svc.Add(wo!, sessionId, 500, 5, "SC-COLOR", "ΔE > 2", "alice", DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        using var verify = _fx.NewContext();
        var entries = await verify.WoQtyEntries.Where(e => e.WoId == woId).OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(100, entries[0].QtyDoneDelta);
        Assert.Equal(500, entries[1].QtyDoneDelta);
        Assert.Equal(5, entries[1].QtyNgDelta);
        Assert.Equal("SC-COLOR", entries[1].NgReasonCode);
        var woReload = await verify.WorkOrders.FindAsync(woId);
        Assert.Equal(600, woReload!.QtyDoneCached);
        Assert.Equal(5, woReload.QtyNgCached);
    }

    [Fact]
    public async Task Correct_appends_negative_delta_with_LinkedEntryId_Q5()
    {
        var woId = await SeedWoAsync("RUNNING");
        long sessionId, priorEntryId;
        using (var db = _fx.NewContext())
        {
            var session = new WoRunSession { WoId = woId, StartedAt = DateTime.UtcNow, StartedBy = "alice" };
            db.WoRunSessions.Add(session);
            await db.SaveChangesAsync();
            sessionId = session.Id;

            var wo = await db.WorkOrders.FindAsync(woId);
            var svc = new WoQtyService(db);
            var prior = svc.Add(wo!, sessionId, 500, 0, null, null, "alice", DateTime.UtcNow);
            await db.SaveChangesAsync();
            priorEntryId = prior.Id;
        }

        using (var db = _fx.NewContext())
        {
            var wo = await db.WorkOrders.FindAsync(woId);
            var prior = await db.WoQtyEntries.FindAsync(priorEntryId);
            var svc = new WoQtyService(db);
            svc.Correct(wo!, sessionId, prior!, -50, 0, "miscounted +500 → actual was 450", "alice", DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        using var verify = _fx.NewContext();
        var correction = await verify.WoQtyEntries.SingleAsync(e => e.LinkedEntryId == priorEntryId);
        Assert.Equal(-50, correction.QtyDoneDelta);
        Assert.Equal(priorEntryId, correction.LinkedEntryId);
        Assert.Contains("miscounted", correction.CorrectionReason);
        var woReload = await verify.WorkOrders.FindAsync(woId);
        Assert.Equal(450, woReload!.QtyDoneCached);
    }
}
