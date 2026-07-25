using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P11 per-leg check-flow — <see cref="PerLegCheckGate"/> (blocking data gates)
/// + <see cref="SettingLegService"/> (Setting session via WoRunSession, Q1).
/// Gates are vacuously TRUE with no per-leg surface (parity).
/// </summary>
public sealed class PerLegGateAndSettingTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public PerLegGateAndSettingTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    private async Task<long> SeedLegAsync(string kind = "PRINT", string line = "SILK")
    {
        using var db = _fx.NewContext();
        var wo = new WorkOrder
        {
            WoNo = "WO-GATE-" + Guid.NewGuid().ToString("N")[..6], CustomerId = _fx.SeedCustomerId,
            ProductId = _fx.SeedProductId, ProductName = "G", MachineCode = "M", MachineName = "P1",
            TargetQty = 100, Uom = "pcs", CurrentStep = ProcessStepCode.PrePressCheck, Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo); await db.SaveChangesAsync();
        var leg = new WoLeg { WorkOrderId = wo.Id, Sequence = 0, LegKind = kind, Method = "m", ProcessLine = line,
            SurfaceProfile = "FULL", InputSource = "IN_LINE", LegPhase = "PREPRESS", CreatedAt = DateTime.UtcNow };
        db.WoLegs.Add(leg); await db.SaveChangesAsync();
        return leg.Id;
    }

    private void AddPlate(MesDbContext db, long woId, long legId, PrepressCheckStatus st)
    {
        var p = new WoPlateCheck { WorkOrderId = woId, Status = st, CreatedAt = DateTime.UtcNow };
        db.WoPlateChecks.Add(p); db.Entry(p).Property("WoLegId").CurrentValue = legId;
    }

    private void AddIpqcItem(MesDbContext db, long legId, IpqcCheckStatus st)
    {
        var chk = db.WoIpqcChecks.Local.FirstOrDefault() ?? new WoIpqcCheck
        {
            WorkOrderId = db.WoLegs.Find(legId)!.WorkOrderId, MaterialStatus = IpqcCheckStatus.Pending,
            PrintAStatus = IpqcCheckStatus.Pending, PrintBStatus = IpqcCheckStatus.Pending,
            PrintCStatus = IpqcCheckStatus.Pending, Judgment = IpqcJudgment.Pending, QaOutcome = QaOutcome.Pending,
        };
        if (db.Entry(chk).State == EntityState.Detached) { db.WoIpqcChecks.Add(chk); db.Entry(chk).Property("WoLegId").CurrentValue = legId; }
        var it = new WoIpqcCheckItem { ItemKey = "K" + Guid.NewGuid().ToString("N")[..4], ProcessLine = "SILK",
            GroupLabel = "g", Label = "l", Status = st, Sort = 10 };
        chk.Items.Add(it); db.Entry(it).Property("WoLegId").CurrentValue = legId;
    }

    // ── Prepress gate ──
    [Fact]
    public async Task Prepress_gate_blocks_until_plate_ok()
    {
        var legId = await SeedLegAsync("PRINT");
        long woId;
        using (var db = _fx.NewContext()) { woId = db.WoLegs.Find(legId)!.WorkOrderId; AddPlate(db, woId, legId, PrepressCheckStatus.Pending); await db.SaveChangesAsync(); }
        using (var db = _fx.NewContext()) Assert.False(await new PerLegCheckGate(db).PrepressReadyAsync(legId));
        using (var db = _fx.NewContext()) { var p = await db.WoPlateChecks.FirstAsync(x => EF.Property<long?>(x, "WoLegId") == legId); p.Status = PrepressCheckStatus.Ok; await db.SaveChangesAsync(); }
        using (var db = _fx.NewContext()) Assert.True(await new PerLegCheckGate(db).PrepressReadyAsync(legId));
    }

    [Fact]
    public async Task Prepress_gate_vacuously_true_when_leg_has_no_surface()
    {
        var legId = await SeedLegAsync("ASSEMBLY", "FINISHING");   // no plate/cutter/materials
        using var db = _fx.NewContext();
        Assert.True(await new PerLegCheckGate(db).PrepressReadyAsync(legId));
    }

    // ── IPQC gate ──
    [Fact]
    public async Task Ipqc_gate_blocks_until_all_items_ok()
    {
        var legId = await SeedLegAsync("PRINT");
        using (var db = _fx.NewContext()) { AddIpqcItem(db, legId, IpqcCheckStatus.Ok); AddIpqcItem(db, legId, IpqcCheckStatus.Pending); await db.SaveChangesAsync(); }
        using (var db = _fx.NewContext()) Assert.False(await new PerLegCheckGate(db).IpqcAllOkAsync(legId));
        using (var db = _fx.NewContext()) { foreach (var i in await db.WoIpqcCheckItems.Where(x => EF.Property<long?>(x, "WoLegId") == legId).ToListAsync()) i.Status = IpqcCheckStatus.Ok; await db.SaveChangesAsync(); }
        using (var db = _fx.NewContext()) Assert.True(await new PerLegCheckGate(db).IpqcAllOkAsync(legId));
    }

    [Fact]
    public async Task Ipqc_gate_vacuously_true_when_no_items()
    {
        var legId = await SeedLegAsync();
        using var db = _fx.NewContext();
        Assert.True(await new PerLegCheckGate(db).IpqcAllOkAsync(legId));
    }

    // ── Setting session (WoRunSession reuse) ──
    [Fact]
    public async Task Setting_enter_then_done_gate_flips()
    {
        var legId = await SeedLegAsync();
        // No session yet → gate vacuously true (parity).
        using (var db = _fx.NewContext()) Assert.True(await new PerLegCheckGate(db).SettingDoneAsync(legId));
        // Enter → open session → gate false (must finish).
        using (var db = _fx.NewContext()) Assert.True(await new SettingLegService(db).EnterAsync(legId, "op"));
        using (var db = _fx.NewContext()) Assert.False(await new PerLegCheckGate(db).SettingDoneAsync(legId));
        // Enter again → idempotent (still 1 session).
        using (var db = _fx.NewContext()) await new SettingLegService(db).EnterAsync(legId, "op");
        using (var db = _fx.NewContext()) Assert.Equal(1, await db.WoRunSessions.CountAsync(s => EF.Property<long?>(s, "WoLegId") == legId));
        // Done → EndedAt stamped → gate true.
        using (var db = _fx.NewContext()) Assert.True(await new SettingLegService(db).DoneAsync(legId, "op"));
        using (var db = _fx.NewContext()) Assert.True(await new PerLegCheckGate(db).SettingDoneAsync(legId));
    }

    [Fact]
    public async Task Setting_done_with_no_open_session_returns_false()
    {
        var legId = await SeedLegAsync();
        using var db = _fx.NewContext();
        Assert.False(await new SettingLegService(db).DoneAsync(legId, "op"));
    }
}
