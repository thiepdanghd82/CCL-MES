using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P11 per-leg IPQC (Q6) — <see cref="IpqcLegMaterializer"/> materialises one
/// WoIpqcCheck per leg scoped to the leg's SINGLE ProcessLine (the per-area
/// partition of the 1-leg multi-line bundle). Idempotent; WO-level (NULL-leg)
/// parity intact.
/// </summary>
public sealed class IpqcLegMaterializerTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public IpqcLegMaterializerTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    private async Task SeedLibraryAsync(string line, int count, string? productCode = null)
    {
        using var db = _fx.NewContext();
        for (var i = 1; i <= count; i++)
        {
            db.CheckItemLibraries.Add(new CheckItemLibrary
            {
                ItemId = $"{line}-{i}", ProcessLine = line, Ipqc = true,
                ProductCode = productCode, Active = true, Sort = i * 10,
                GroupLabel = line + " group", Code = $"{line}{i}",
                ItemVi = $"Item {line} {i}", ItemEn = $"Item {line} {i}",
                AcceptanceVi = "OK", AcceptanceEn = "OK",
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task<(long woId, long legId)> SeedWoLegAsync(string legKind, string processLine)
    {
        using var db = _fx.NewContext();
        var wo = new WorkOrder
        {
            WoNo = "WO-IPQC-LEG-" + Guid.NewGuid().ToString("N")[..6],
            CustomerId = _fx.SeedCustomerId, ProductId = _fx.SeedProductId,
            ProductName = "Test", ProductRevisionId = _fx.SeedRevisionId,
            MachineCode = "M-1", MachineName = "P1", TargetQty = 1000, Uom = "pcs",
            CurrentStep = ProcessStepCode.PrePressCheck, Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        var leg = new WoLeg
        {
            WorkOrderId = wo.Id, Sequence = 0, LegKind = legKind, Method = legKind + "-m",
            ProcessLine = processLine, SurfaceProfile = "FULL", InputSource = "IN_LINE",
            LegPhase = "PREPRESS", CreatedAt = DateTime.UtcNow,
        };
        db.WoLegs.Add(leg);
        await db.SaveChangesAsync();
        return (wo.Id, leg.Id);
    }

    private static Task<int> LegItemCount(MesDbContext db, long legId) =>
        db.WoIpqcCheckItems.CountAsync(i => EF.Property<long?>(i, "WoLegId") == legId);

    [Fact]
    public async Task Print_leg_materialises_only_its_process_line_items()
    {
        await SeedLibraryAsync("SILK", 25);
        await SeedLibraryAsync("PRESS_CNC", 27);
        var (_, legId) = await SeedWoLegAsync("PRINT", "SILK");

        using var db = _fx.NewContext();
        var outcome = await new IpqcLegMaterializer(db).MaterializeForLegAsync(legId);

        Assert.Equal(IpqcLegMaterializer.Materialized, outcome);
        var check = await db.WoIpqcChecks.SingleAsync(c => EF.Property<long?>(c, "WoLegId") == legId);
        Assert.Equal("SILK", check.ResolvedLines);
        Assert.Equal(25, await LegItemCount(db, legId));   // SILK only, NOT the 52 bundle
        Assert.All(await db.WoIpqcCheckItems.Where(i => EF.Property<long?>(i, "WoLegId") == legId).ToListAsync(),
            i => Assert.Equal("SILK", i.ProcessLine));
    }

    [Fact]
    public async Task Cut_leg_gets_press_cnc_partition()
    {
        await SeedLibraryAsync("SILK", 25);
        await SeedLibraryAsync("PRESS_CNC", 27);
        var (_, legId) = await SeedWoLegAsync("CUT", "PRESS_CNC");

        using var db = _fx.NewContext();
        await new IpqcLegMaterializer(db).MaterializeForLegAsync(legId);
        Assert.Equal(27, await LegItemCount(db, legId));
    }

    [Fact]
    public async Task Materialise_is_idempotent()
    {
        await SeedLibraryAsync("SILK", 5);
        var (_, legId) = await SeedWoLegAsync("PRINT", "SILK");

        string first, second;
        using (var d1 = _fx.NewContext()) first = await new IpqcLegMaterializer(d1).MaterializeForLegAsync(legId);
        using (var d2 = _fx.NewContext()) second = await new IpqcLegMaterializer(d2).MaterializeForLegAsync(legId);

        Assert.Equal(IpqcLegMaterializer.Materialized, first);
        Assert.Equal(IpqcLegMaterializer.AlreadyExists, second);
        using var db = _fx.NewContext();
        Assert.Equal(1, await db.WoIpqcChecks.CountAsync(c => EF.Property<long?>(c, "WoLegId") == legId));
        Assert.Equal(5, await LegItemCount(db, legId));
    }

    [Fact]
    public async Task Leg_without_library_line_returns_skipped_no_library()
    {
        // No library rows for FINISHING → check row created empty, outcome SkippedNoLibrary.
        var (_, legId) = await SeedWoLegAsync("ASSEMBLY", "FINISHING");

        using var db = _fx.NewContext();
        var outcome = await new IpqcLegMaterializer(db).MaterializeForLegAsync(legId);
        Assert.Equal(IpqcLegMaterializer.SkippedNoLibrary, outcome);
        Assert.Equal(0, await LegItemCount(db, legId));
    }

    [Fact]
    public async Task Per_leg_check_coexists_with_wo_level_null_leg_check_parity()
    {
        await SeedLibraryAsync("SILK", 3);
        var (woId, legId) = await SeedWoLegAsync("PRINT", "SILK");

        // WO-level (1-leg style) check with WoLegId NULL.
        using (var d0 = _fx.NewContext())
        {
            d0.WoIpqcChecks.Add(new WoIpqcCheck
            {
                WorkOrderId = woId, MaterialStatus = IpqcCheckStatus.Pending,
                PrintAStatus = IpqcCheckStatus.Pending, PrintBStatus = IpqcCheckStatus.Pending,
                PrintCStatus = IpqcCheckStatus.Pending, Judgment = IpqcJudgment.Pending,
                QaOutcome = QaOutcome.Pending,
            });
            await d0.SaveChangesAsync();
        }
        // Per-leg check coexists (partial unique index makes this legal).
        using (var d1 = _fx.NewContext()) await new IpqcLegMaterializer(d1).MaterializeForLegAsync(legId);

        using var db = _fx.NewContext();
        Assert.Equal(1, await db.WoIpqcChecks.CountAsync(c => c.WorkOrderId == woId && EF.Property<long?>(c, "WoLegId") == null));
        Assert.Equal(1, await db.WoIpqcChecks.CountAsync(c => EF.Property<long?>(c, "WoLegId") == legId));
    }
}
