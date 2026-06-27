using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P10.7b-1 — PrepressBomSnapshotService coverage.
///
/// Surface contract:
///   * MaterializeAsync is idempotent (re-running on a snapshot-OK WO
///     returns 0 inserted + does not duplicate).
///   * Plate + Cutter rows always created if missing (1:1, PENDING).
///   * Materials rows materialised from ManufacturingStructures where
///     ParentPart = Product.ProductCode (BOM snapshot per §5.3).
///   * WO with null ProductRevisionId still gets plate + cutter
///     (operator-recoverable PREPRESS state).
///   * WO whose ProductCode has no MS rows still gets plate + cutter,
///     materials list stays empty.
/// </summary>
public sealed class PrepressBomSnapshotServiceTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public PrepressBomSnapshotServiceTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    private async Task<long> SeedBomAsync(string productCode, params (string Comp, double Qty, string Uom)[] lines)
    {
        using var db = _fx.NewContext();
        foreach (var (Comp, Qty, Uom) in lines)
        {
            db.ManufacturingStructures.Add(new ManufacturingStructure
            {
                ParentPart = productCode,
                ComponentPart = Comp,
                ComponentDescription = "Desc " + Comp,
                QtyAssembly = Qty,
                Uom = Uom,
                ScrapFactor = 0,
            });
        }
        await db.SaveChangesAsync();
        return 0;
    }

    private async Task<long> SeedWoAsync(string woNo = "WO-7B-1", long? productId = null,
        long? revisionId = null, int targetQty = 1000)
    {
        using var db = _fx.NewContext();
        var wo = new WorkOrder
        {
            WoNo = woNo,
            CustomerId = _fx.SeedCustomerId,
            ProductId = productId ?? _fx.SeedProductId,
            ProductName = "Test Product",
            ProductRevisionId = revisionId ?? _fx.SeedRevisionId,
            MachineCode = "M-1",
            MachineName = "Press 1",
            TargetQty = targetQty,
            Uom = "pcs",
            CurrentStep = ProcessStepCode.PrePressCheck,
            Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        return wo.Id;
    }

    [Fact]
    public async Task First_materialise_inserts_plate_and_cutter_pending_rows()
    {
        var woId = await SeedWoAsync();
        using var db = _fx.NewContext();
        var svc = new PrepressBomSnapshotService(db);

        await svc.MaterializeAsync(woId);

        var plate = await db.WoPlateChecks.SingleOrDefaultAsync(p => p.WorkOrderId == woId);
        var cutter = await db.WoCutterChecks.SingleOrDefaultAsync(c => c.WorkOrderId == woId);
        Assert.NotNull(plate);
        Assert.NotNull(cutter);
        Assert.Equal(PrepressCheckStatus.Pending, plate!.Status);
        Assert.Equal(PrepressCheckStatus.Pending, cutter!.Status);
    }

    [Fact]
    public async Task Re_materialise_is_noop_for_plate_and_cutter()
    {
        var woId = await SeedWoAsync("WO-7B-2");
        using (var d1 = _fx.NewContext())
            await new PrepressBomSnapshotService(d1).MaterializeAsync(woId);

        using var db = _fx.NewContext();
        var svc = new PrepressBomSnapshotService(db);
        await svc.MaterializeAsync(woId);
        await svc.MaterializeAsync(woId);

        var plateCount = await db.WoPlateChecks.CountAsync(p => p.WorkOrderId == woId);
        var cutterCount = await db.WoCutterChecks.CountAsync(c => c.WorkOrderId == woId);
        Assert.Equal(1, plateCount);
        Assert.Equal(1, cutterCount);
    }

    [Fact]
    public async Task Materials_snapshot_pulls_from_ManufacturingStructure_by_ProductCode()
    {
        // Seed the BOM for the fixture product.
        using (var d = _fx.NewContext())
        {
            var product = await d.Products.SingleAsync(p => p.Id == _fx.SeedProductId);
            await SeedBomAsync(product.ProductCode,
                ("COMP-A", 0.001, "m2"),
                ("COMP-B", 2.5, "kg"),
                ("COMP-C", 1.0e-6, "pcs"));
        }

        var woId = await SeedWoAsync("WO-7B-3", targetQty: 1000);
        using var db = _fx.NewContext();
        var svc = new PrepressBomSnapshotService(db);

        var inserted = await svc.MaterializeAsync(woId);

        Assert.Equal(3, inserted);
        var mats = await db.WoMaterials
            .Where(m => m.WorkOrderId == woId)
            .OrderBy(m => m.BomLineIdx)
            .ToListAsync();
        Assert.Equal(3, mats.Count);
        Assert.Equal(0, mats[0].BomLineIdx);
        Assert.Equal(1, mats[1].BomLineIdx);
        Assert.Equal(2, mats[2].BomLineIdx);
        Assert.Equal("COMP-A", mats[0].MaterialCode);
        Assert.Equal("m2", mats[0].Uom);
        // QtyRequired = QtyAssembly × TargetQty = 0.001 × 1000 = 1.0
        Assert.Equal(1.0, mats[0].QtyRequired, precision: 6);
        // QtyRequired = 2.5 × 1000 = 2500
        Assert.Equal(2500.0, mats[1].QtyRequired, precision: 3);
        Assert.All(mats, m => Assert.Equal(PrepressCheckStatus.Pending, m.Status));
    }

    [Fact]
    public async Task Re_materialise_does_not_duplicate_material_rows()
    {
        using (var d = _fx.NewContext())
        {
            var product = await d.Products.SingleAsync(p => p.Id == _fx.SeedProductId);
            await SeedBomAsync(product.ProductCode, ("ONLY-COMP", 1.0, "pcs"));
        }

        var woId = await SeedWoAsync("WO-7B-4");
        using (var d1 = _fx.NewContext())
            await new PrepressBomSnapshotService(d1).MaterializeAsync(woId);
        using (var d2 = _fx.NewContext())
            await new PrepressBomSnapshotService(d2).MaterializeAsync(woId);
        using (var d3 = _fx.NewContext())
            await new PrepressBomSnapshotService(d3).MaterializeAsync(woId);

        using var db = _fx.NewContext();
        var matCount = await db.WoMaterials.CountAsync(m => m.WorkOrderId == woId);
        Assert.Equal(1, matCount);
    }

    [Fact]
    public async Task WO_with_null_ProductRevisionId_still_gets_plate_and_cutter_but_no_materials()
    {
        var woId = await SeedWoAsync("WO-7B-5", revisionId: null);
        // Note: SeedWoAsync above coerces revisionId to fixture default; force null explicitly.
        using (var d = _fx.NewContext())
        {
            var wo = await d.WorkOrders.SingleAsync(w => w.Id == woId);
            wo.ProductRevisionId = null;
            await d.SaveChangesAsync();
        }

        using var db = _fx.NewContext();
        var svc = new PrepressBomSnapshotService(db);
        var inserted = await svc.MaterializeAsync(woId);

        Assert.Equal(0, inserted);
        Assert.True(await db.WoPlateChecks.AnyAsync(p => p.WorkOrderId == woId));
        Assert.True(await db.WoCutterChecks.AnyAsync(c => c.WorkOrderId == woId));
        Assert.False(await db.WoMaterials.AnyAsync(m => m.WorkOrderId == woId));
    }

    [Fact]
    public async Task Product_with_no_MS_rows_still_gets_plate_and_cutter_but_no_materials()
    {
        // SeedBomAsync NOT called → ManufacturingStructures is empty for
        // this product code.
        var woId = await SeedWoAsync("WO-7B-6");
        using var db = _fx.NewContext();
        var svc = new PrepressBomSnapshotService(db);
        var inserted = await svc.MaterializeAsync(woId);

        Assert.Equal(0, inserted);
        Assert.True(await db.WoPlateChecks.AnyAsync(p => p.WorkOrderId == woId));
        Assert.True(await db.WoCutterChecks.AnyAsync(c => c.WorkOrderId == woId));
        Assert.False(await db.WoMaterials.AnyAsync(m => m.WorkOrderId == woId));
    }

    [Fact]
    public async Task Missing_WO_returns_zero_and_no_writes()
    {
        using var db = _fx.NewContext();
        var svc = new PrepressBomSnapshotService(db);
        var inserted = await svc.MaterializeAsync(9_999_999);
        Assert.Equal(0, inserted);
        Assert.False(await db.WoPlateChecks.AnyAsync());
        Assert.False(await db.WoCutterChecks.AnyAsync());
        Assert.False(await db.WoMaterials.AnyAsync());
    }

    // ── P10.10 — Scrap Factor / Scrap % snapshot + re-sync ──────────

    private async Task SeedBomScrapAsync(string productCode,
        params (string Comp, double Qty, string Uom, double ScrapFactor, double? ScrapPct)[] lines)
    {
        using var db = _fx.NewContext();
        foreach (var (Comp, Qty, Uom, ScrapFactor, ScrapPct) in lines)
        {
            db.ManufacturingStructures.Add(new ManufacturingStructure
            {
                ParentPart = productCode,
                ComponentPart = Comp,
                ComponentDescription = "Desc " + Comp,
                QtyAssembly = Qty,
                Uom = Uom,
                ScrapFactor = ScrapFactor,
                ScrapPct = ScrapPct,
            });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Materials_snapshot_carries_scrap_factor_and_percent_from_BOM()
    {
        using (var d = _fx.NewContext())
        {
            var product = await d.Products.SingleAsync(p => p.Id == _fx.SeedProductId);
            await SeedBomScrapAsync(product.ProductCode,
                ("COMP-A", 0.01105, "m2", 3.825, 3),
                ("COMP-B", 0.000051, "kg", 0.1, null));
        }

        var woId = await SeedWoAsync("WO-7B-SCRAP-1");
        using var db = _fx.NewContext();
        await new PrepressBomSnapshotService(db).MaterializeAsync(woId);

        var mats = await db.WoMaterials
            .Where(m => m.WorkOrderId == woId)
            .OrderBy(m => m.BomLineIdx)
            .ToListAsync();
        Assert.Equal(2, mats.Count);
        Assert.Equal(3.825, mats[0].ScrapFactor, precision: 4);
        Assert.Equal(3, mats[0].ScrapPercent);
        Assert.Equal(0.1, mats[1].ScrapFactor, precision: 4);
        Assert.Null(mats[1].ScrapPercent);
    }

    [Fact]
    public async Task Re_sync_refreshes_BOM_columns_but_preserves_operator_entries()
    {
        using (var d = _fx.NewContext())
        {
            var product = await d.Products.SingleAsync(p => p.Id == _fx.SeedProductId);
            await SeedBomScrapAsync(product.ProductCode,
                ("COMP-A", 1.0, "kg", 2.0, 5));
        }

        var woId = await SeedWoAsync("WO-7B-SCRAP-2", targetQty: 100);
        using (var d1 = _fx.NewContext())
            await new PrepressBomSnapshotService(d1).MaterializeAsync(woId);

        // Operator records a loaded qty + lot + marks OK.
        using (var d2 = _fx.NewContext())
        {
            var row = await d2.WoMaterials.SingleAsync(m => m.WorkOrderId == woId);
            row.QtyLoaded = 99.5;
            row.LotNo = "LOT-26-9999";
            row.Status = PrepressCheckStatus.Ok;
            row.CheckedBy = "operator1";
            await d2.SaveChangesAsync();
        }

        // BOM (Structure) changes upstream: scrap + qty + description edited.
        using (var d3 = _fx.NewContext())
        {
            var product = await d3.Products.SingleAsync(p => p.Id == _fx.SeedProductId);
            var ms = await d3.ManufacturingStructures.SingleAsync(m => m.ParentPart == product.ProductCode);
            ms.ScrapFactor = 7.7;
            ms.ScrapPct = 9;
            ms.QtyAssembly = 2.0;          // → QtyRequired = 2.0 × 100 = 200
            ms.ComponentDescription = "Updated desc";
            await d3.SaveChangesAsync();
        }

        // Re-scan: re-sync should refresh BOM columns, keep operator columns.
        using var db = _fx.NewContext();
        await new PrepressBomSnapshotService(db).MaterializeAsync(woId);

        var after = await db.WoMaterials.SingleAsync(m => m.WorkOrderId == woId);
        // BOM-sourced refreshed:
        Assert.Equal(7.7, after.ScrapFactor, precision: 4);
        Assert.Equal(9, after.ScrapPercent);
        Assert.Equal(200.0, after.QtyRequired, precision: 3);
        Assert.Equal("Updated desc", after.MaterialDescription);
        // Operator entries preserved:
        Assert.Equal(99.5, after.QtyLoaded);
        Assert.Equal("LOT-26-9999", after.LotNo);
        Assert.Equal(PrepressCheckStatus.Ok, after.Status);
        Assert.Equal("operator1", after.CheckedBy);
    }
}
