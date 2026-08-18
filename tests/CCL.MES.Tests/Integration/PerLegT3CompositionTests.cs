using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P11 per-leg END-TO-END composition — a T3 WO (PRINT SILK ∥ TAPE FINISHING →
/// ASSEMBLY FINISHING → CUT PRESS_CNC) forked into 4 legs. Runs exactly what
/// RoutingController.Materialize does after the fork (Prepress + IPQC materialiser
/// per leg) and asserts each leg owns its OWN check set scoped by process line:
///   • Pre-press: full BOM per leg + plate/cutter by LegKind (Q-B).
///   • IPQC: only the leg's ProcessLine items (per-area partition, Q6).
/// </summary>
public sealed class PerLegT3CompositionTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public PerLegT3CompositionTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    private async Task SeedBomAsync(int lines)
    {
        using var db = _fx.NewContext();
        for (var i = 0; i < lines; i++)
            db.ManufacturingStructures.Add(new ManufacturingStructure
            {
                ParentPart = _fx.SeedProductCode, ComponentPart = $"COMP-{i}",
                ComponentDescription = $"Comp {i}", QtyAssembly = 1.0 + i, Uom = "pcs", ScrapFactor = 0,
            });
        await db.SaveChangesAsync();
    }

    private async Task SeedLibraryAsync(string line, int count)
    {
        using var db = _fx.NewContext();
        for (var i = 1; i <= count; i++)
            db.CheckItemLibraries.Add(new CheckItemLibrary
            {
                ItemId = $"{line}-{i}", ProcessLine = line, Ipqc = true, Active = true, Sort = i * 10,
                GroupLabel = line, Code = $"{line}{i}", ItemVi = $"{line} {i}", ItemEn = $"{line} {i}",
                AcceptanceVi = "OK", AcceptanceEn = "OK",
            });
        await db.SaveChangesAsync();
    }

    private async Task<long> SeedT3WoAsync()
    {
        using var db = _fx.NewContext();
        var wo = new WorkOrder
        {
            WoNo = "WO-T3-" + Guid.NewGuid().ToString("N")[..6], CustomerId = _fx.SeedCustomerId,
            ProductId = _fx.SeedProductId, ProductName = "T3", ProductRevisionId = _fx.SeedRevisionId,
            MachineCode = "M-1", MachineName = "P1", TargetQty = 100, Uom = "pcs",
            CurrentStep = ProcessStepCode.PrePressCheck, Status = WoStatus.InProgress, MesPhase = "SPLIT",
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        void Leg(int seq, string kind, string line) => db.WoLegs.Add(new WoLeg
        {
            WorkOrderId = wo.Id, Sequence = seq, LegKind = kind, Method = kind + "-m", ProcessLine = line,
            SurfaceProfile = "FULL", InputSource = "IN_LINE", LegPhase = "PREPRESS", CreatedAt = DateTime.UtcNow,
        });
        Leg(0, "PRINT", "SILK");
        Leg(1, "TAPE", "FINISHING");
        Leg(2, "ASSEMBLY", "FINISHING");
        Leg(3, "CUT", "PRESS_CNC");
        await db.SaveChangesAsync();
        return wo.Id;
    }

    [Fact]
    public async Task T3_fork_gives_each_leg_its_own_prepress_and_ipqc_by_process_line()
    {
        await SeedBomAsync(3);
        await SeedLibraryAsync("SILK", 4);
        await SeedLibraryAsync("PRESS_CNC", 3);
        await SeedLibraryAsync("FINISHING", 2);
        var woId = await SeedT3WoAsync();

        // Run the exact per-leg materialisation loop the controller runs post-fork.
        using (var db = _fx.NewContext())
        {
            var legIds = await db.WoLegs.Where(l => l.WorkOrderId == woId).OrderBy(l => l.Sequence)
                .Select(l => l.Id).ToListAsync();
            var prepress = new PrepressBomSnapshotService(db);
            var ipqc = new IpqcLegMaterializer(db);
            foreach (var legId in legIds)
            {
                await prepress.MaterializeForLegAsync(legId);
                await ipqc.MaterializeForLegAsync(legId);
            }
        }

        using var check = _fx.NewContext();
        var legs = await check.WoLegs.Where(l => l.WorkOrderId == woId).OrderBy(l => l.Sequence).ToListAsync();
        long id0 = legs.First(l => l.Sequence == 0).Id;   // PRINT / SILK
        long id1 = legs.First(l => l.Sequence == 1).Id;   // TAPE / FINISHING
        long id2 = legs.First(l => l.Sequence == 2).Id;   // ASSEMBLY / FINISHING
        long id3 = legs.First(l => l.Sequence == 3).Id;   // CUT / PRESS_CNC

        // Pre-press: every leg has the full BOM (3) scoped to itself.
        foreach (var legId in new[] { id0, id1, id2, id3 })
            Assert.Equal(3, await check.WoMaterials.CountAsync(m => EF.Property<long?>(m, "WoLegId") == legId));

        // Plate only on PRINT(0); cutter on CUT(3) + TAPE(1); ASSEMBLY(2) neither.
        Assert.Equal(1, await check.WoPlateChecks.CountAsync(p => EF.Property<long?>(p, "WoLegId") == id0));
        Assert.Equal(0, await check.WoPlateChecks.CountAsync(p => EF.Property<long?>(p, "WoLegId") == id3));
        Assert.Equal(1, await check.WoCutterChecks.CountAsync(c => EF.Property<long?>(c, "WoLegId") == id3));
        Assert.Equal(1, await check.WoCutterChecks.CountAsync(c => EF.Property<long?>(c, "WoLegId") == id1));
        Assert.Equal(0, await check.WoCutterChecks.CountAsync(c => EF.Property<long?>(c, "WoLegId") == id2));
        Assert.Equal(0, await check.WoPlateChecks.CountAsync(p => EF.Property<long?>(p, "WoLegId") == id2));

        // IPQC: each leg's items = ONLY its process line's library count.
        Assert.Equal(4, await check.WoIpqcCheckItems.CountAsync(i => EF.Property<long?>(i, "WoLegId") == id0));   // PRINT → SILK
        Assert.Equal(2, await check.WoIpqcCheckItems.CountAsync(i => EF.Property<long?>(i, "WoLegId") == id1));   // TAPE → FINISHING
        Assert.Equal(2, await check.WoIpqcCheckItems.CountAsync(i => EF.Property<long?>(i, "WoLegId") == id2));   // ASSEMBLY → FINISHING
        Assert.Equal(3, await check.WoIpqcCheckItems.CountAsync(i => EF.Property<long?>(i, "WoLegId") == id3));   // CUT → PRESS_CNC
        // 4 checks total, one per leg — none WO-level (NULL leg).
        Assert.Equal(4, await check.WoIpqcChecks.CountAsync(c => c.WorkOrderId == woId));
        Assert.Equal(0, await check.WoIpqcChecks.CountAsync(c => c.WorkOrderId == woId && EF.Property<long?>(c, "WoLegId") == null));
    }
}
