using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Tests.Integration._Support;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// Test đợt 1 (2026-06-23) — OeeService.ComputeAsync coverage. Audit
/// 2026-06-22 flagged OEE as having NO integration test + risk of
/// divide-by-zero when a machine has no usable production logs.
///
/// Surface contract (src/CCL.MES.Application/Services/OeeService.cs):
///   * unknown machineId → null
///   * Availability = Run / (Run + Stop + Setup); 0 when planned time = 0
///   * Performance  = min(1.0, idealMin / Run); 0 when Run = 0; capped at 1
///   * Quality      = Good / (Good + Reject); 0 when total output = 0
///   * OEE          = Availability × Performance × Quality
///   * only logs with StartAt within [from, to] are aggregated
///
/// Durations are driven by explicit StartAt/EndAt (DurationMinutes is a
/// computed property = EndAt − StartAt) so every figure is deterministic.
/// </summary>
public sealed class OeeServiceComputeTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public OeeServiceComputeTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    // A fixed UTC base well clear of "now" so the default window never
    // surprises us; tests always pass an explicit [from, to] anyway.
    private static readonly DateTime Base =
        new(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);

    private async Task<long> SeedMachineAsync(string code, double idealCycleSec)
    {
        using var db = _fx.NewContext();
        var m = new Machine { Code = code, Name = "Machine " + code, IdealCycleTimeSec = idealCycleSec };
        db.Machines.Add(m);
        await db.SaveChangesAsync();
        return m.Id;
    }

    private async Task AddLogAsync(
        long woId, long machineId, ProductionEventType type,
        DateTime startAt, double durationMin, int good = 0, int reject = 0)
    {
        using var db = _fx.NewContext();
        db.ProductionLogs.Add(new ProductionLog
        {
            WorkOrderId = woId,
            MachineId = machineId,
            EventType = type,
            StartAt = startAt,
            EndAt = startAt.AddMinutes(durationMin),
            GoodQty = good,
            RejectQty = reject,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Unknown_machine_returns_null()
    {
        using var db = _fx.NewContext();
        var result = await new OeeService(db).ComputeAsync(999_999);
        Assert.Null(result);
    }

    [Fact]
    public async Task No_logs_yields_all_zero_no_divide_by_zero()
    {
        var machineId = await SeedMachineAsync("OEE-EMPTY", 10.0);

        using var db = _fx.NewContext();
        var result = await new OeeService(db).ComputeAsync(
            machineId, Base.AddHours(-1), Base.AddDays(1));

        Assert.NotNull(result);
        Assert.Equal(0, result!.PlannedMinutes);
        Assert.Equal(0, result.Availability, 3);
        Assert.Equal(0, result.Performance, 3);
        Assert.Equal(0, result.Quality, 3);
        Assert.Equal(0, result.Oee, 3);
        Assert.Equal(0, result.GoodQty);
        Assert.Equal(0, result.RejectQty);
    }

    [Fact]
    public async Task Known_numbers_compute_expected_oee()
    {
        var woId = await _fx.SeedWorkOrderAsync(_fx.SeedRevisionId);
        // IdealCycleTimeSec = 54s → idealMin = 54 * 400 / 60 = 360 min.
        var machineId = await SeedMachineAsync("OEE-KNOWN", 54.0);

        // Availability 400/(400+60+40) = 0.8
        await AddLogAsync(woId, machineId, ProductionEventType.Run,   Base,                   400, good: 380, reject: 20);
        await AddLogAsync(woId, machineId, ProductionEventType.Stop,  Base.AddMinutes(401),    60);
        await AddLogAsync(woId, machineId, ProductionEventType.Setup, Base.AddMinutes(462),    40);

        using var db = _fx.NewContext();
        var result = await new OeeService(db).ComputeAsync(
            machineId, Base.AddHours(-1), Base.AddDays(1));

        Assert.NotNull(result);
        Assert.Equal(500.0, result!.PlannedMinutes, 1);
        Assert.Equal(400.0, result.RunMinutes, 1);
        Assert.Equal(60.0, result.DownMinutes, 1);
        Assert.Equal(380, result.GoodQty);
        Assert.Equal(20, result.RejectQty);
        Assert.Equal(0.800, result.Availability, 3);    // 400/500
        Assert.Equal(0.900, result.Performance, 3);     // 360/400
        Assert.Equal(0.950, result.Quality, 3);         // 380/400
        Assert.Equal(0.684, result.Oee, 3);             // 0.8*0.9*0.95
    }

    [Fact]
    public async Task Performance_is_capped_at_one()
    {
        var woId = await _fx.SeedWorkOrderAsync(_fx.SeedRevisionId);
        // idealMin = 60 * 200 / 60 = 200 min vs Run 100 → raw 2.0, must cap.
        var machineId = await SeedMachineAsync("OEE-FAST", 60.0);
        await AddLogAsync(woId, machineId, ProductionEventType.Run, Base, 100, good: 200, reject: 0);

        using var db = _fx.NewContext();
        var result = await new OeeService(db).ComputeAsync(
            machineId, Base.AddHours(-1), Base.AddDays(1));

        Assert.NotNull(result);
        Assert.Equal(1.000, result!.Performance, 3);
    }

    [Fact]
    public async Task Quality_is_zero_when_no_output()
    {
        var woId = await _fx.SeedWorkOrderAsync(_fx.SeedRevisionId);
        var machineId = await SeedMachineAsync("OEE-NOOUT", 10.0);
        // Run time but zero good/reject → total 0 → quality + performance 0,
        // but availability stays computable (no NaN / no throw).
        await AddLogAsync(woId, machineId, ProductionEventType.Run, Base, 120, good: 0, reject: 0);

        using var db = _fx.NewContext();
        var result = await new OeeService(db).ComputeAsync(
            machineId, Base.AddHours(-1), Base.AddDays(1));

        Assert.NotNull(result);
        Assert.Equal(1.000, result!.Availability, 3);   // only run time
        Assert.Equal(0.000, result.Quality, 3);
        Assert.Equal(0.000, result.Performance, 3);
        Assert.Equal(0.000, result.Oee, 3);
    }

    [Fact]
    public async Task Logs_outside_window_are_excluded()
    {
        var woId = await _fx.SeedWorkOrderAsync(_fx.SeedRevisionId);
        var machineId = await SeedMachineAsync("OEE-WINDOW", 10.0);

        // In-window run (counts) + an older run 5 days before `from` (must not).
        await AddLogAsync(woId, machineId, ProductionEventType.Run, Base,                100, good: 100);
        await AddLogAsync(woId, machineId, ProductionEventType.Run, Base.AddDays(-5),    100, good: 999);

        using var db = _fx.NewContext();
        var result = await new OeeService(db).ComputeAsync(
            machineId, Base.AddHours(-1), Base.AddDays(1));

        Assert.NotNull(result);
        Assert.Equal(100, result!.GoodQty);     // not 100 + 999
        Assert.Equal(100.0, result.RunMinutes, 1);
    }
}
