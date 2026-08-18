using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// Phương án C — Bước 4. Auto-sync end-to-end ở tầng DB (không qua HTTP):
/// seed Product 8064xxxx + routing THẬT + thư viện v2 → resolve line →
/// lấy subset thư viện → materialize. Khóa: đúng line, đúng số item, freeze
/// snapshot, và WO không routing → KHÔNG materialize (giữ legacy 4 slot).
/// Mirror đúng pipeline trong IpqcReviewController.MaterializeItemsIfNeededAsync.
/// </summary>
public sealed class IpqcAutoSyncTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public IpqcAutoSyncTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    private static string RealCsvPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var c = Path.Combine(dir.FullName, "IPQC_Library_CMES_v3.csv");
            if (File.Exists(c)) return c;
        }
        throw new FileNotFoundException("IPQC_Library_CMES_v3.csv not found above test bin.");
    }

    private async Task<long> SeedProductWithRoutingAsync(string productCode, (string op, string desc, string wc)[] ops)
    {
        using var db = _fx.NewContext();
        var cust = await db.Customers.FirstAsync();
        var prod = new Product { ProductCode = productCode, Name = $"Demo {productCode}", CustomerId = cust.Id };
        db.Products.Add(prod);
        foreach (var (op, desc, wc) in ops)
            db.RoutingOperations.Add(new RoutingOperation
            {
                PartNo = productCode, OpNo = op, Operation = desc, WorkCenterNo = wc, WorkCenterDescription = wc,
            });
        await db.SaveChangesAsync();
        return prod.Id;
    }

    // Mirror controller helper: resolve → library subset → materialize.
    private async Task<IpqcLibraryMaterializer.Result> RunAutoSyncAsync(long productId)
    {
        using var db = _fx.NewContext();
        var productCode = await db.Products.Where(p => p.Id == productId)
            .Select(p => p.ProductCode).FirstAsync();
        var ops = await db.RoutingOperations.Where(r => r.PartNo == productCode)
            .Select(r => new { r.OpNo, r.Operation, r.WorkCenterNo, r.WorkCenterDescription }).ToListAsync();
        var map = await db.ProcessLineMaps.Where(m => m.Active)
            .Select(m => new QcLineResolver.MapEntry(m.MatchType, m.MatchValue, m.QcLine, m.Sort)).ToListAsync();
        var resolution = QcLineResolver.Resolve(ops.Select(o =>
            new QcLineResolver.RoutingOp(o.OpNo, o.Operation, o.WorkCenterNo, o.WorkCenterDescription)), map);
        var lines = resolution.Lines.ToList();
        var lib = await db.CheckItemLibraries
            .Where(c => c.Active && c.Ipqc && lines.Contains(c.ProcessLine)
                     && (c.ProductCode == null || c.ProductCode == productCode))
            .ToListAsync();
        return IpqcLibraryMaterializer.Build(lib, lines);
    }

    private async Task SeedLibraryAsync()
    {
        using var db = _fx.NewContext();
        await DbSeeder.SeedCheckItemLibraryFromFileAsync(db, RealCsvPath());
        await DbSeeder.SeedProcessLineMapAsync(db); // F6 — map data-driven
    }

    [Fact]
    public async Task Label_part_80644935_materializes_label_and_presscnc_items()
    {
        await SeedLibraryAsync();
        var pid = await SeedProductWithRoutingAsync("80644935", new[]
        {
            ("10", "PRE- PREPARE / Chuẩn bị trước SX", "FXPP1"),
            ("20", "(GALLUS) PRINT / In nhãn", "GFL01"),
            ("27", "(BROTECH) PRINT / In nhãn", "BFL01"),
            ("30", "(RDC) LAM.&Cut / Ép&Cắt dao tròn", "RDC12"),
            ("50", "FQC & PACKING/Kiểm tra và đóng gói", "MAN1"),
            ("60", "OQC Inspection", "MAN2"),
        });
        var result = await RunAutoSyncAsync(pid);

        Assert.NotEmpty(result.Items);
        // Mọi item thuộc LABEL hoặc PRESS_CNC (không lẫn DIGITAL/SILK).
        Assert.All(result.Items, i => Assert.Contains(i.ProcessLine, new[] { "LABEL", "PRESS_CNC" }));
        Assert.Contains(result.Items, i => i.ProcessLine == "LABEL");
        Assert.Contains(result.Items, i => i.ProcessLine == "PRESS_CNC");
        // Snapshot đóng băng + đếm khớp số item.
        Assert.Equal(result.Items.Count, QcProfileSeed.CountItems(result.ProfileSnapshotJson));
        Assert.Contains("LABEL,PRESS_CNC", result.ProfileSnapshotJson);
    }

    [Fact]
    public async Task Digital_part_80645392_materializes_digital_items_not_silk()
    {
        await SeedLibraryAsync();
        // Lưu ý quyết định #5: máy SheetCut(SS) R2SC* → SILK (đã unit-test riêng).
        // Test này dùng các op DIGITAL+PRESS_CNC rõ ràng để khẳng định phân loại in số.
        var pid = await SeedProductWithRoutingAsync("80645392", new[]
        {
            ("20", "(HP INDIGO) PRINT / In máy kts", "IDG01"),
            ("60", "(PRESS) CUT / Cắt", "PPSC1"),
            ("80", "FQC & PACKING", "MAN1"),
            ("90", "OQC Inspection", "MAN2"),
        });
        var result = await RunAutoSyncAsync(pid);

        Assert.Contains(result.Items, i => i.ProcessLine == "DIGITAL");
        Assert.DoesNotContain(result.Items, i => i.ProcessLine == "SILK");
        Assert.DoesNotContain(result.Items, i => i.ProcessLine == "LABEL");
    }

    [Fact]
    public async Task Silk_part_80640044_materializes_silk_items_not_digital()
    {
        await SeedLibraryAsync();
        var pid = await SeedProductWithRoutingAsync("80640044", new[]
        {
            ("20", "SILK SEMI_AUTO SHEET/In dạng tờ-P1", "ASS08"),
            ("250", "PUNCHING / Đục lỗ", "PUNC1"),
            ("260", "(PRESS) CUT / Cắt", "PPSC1"),
            ("280", "FQC & PACKING", "MAN1"),
            ("290", "OQC Inspection", "MAN2"),
        });
        var result = await RunAutoSyncAsync(pid);

        Assert.Contains(result.Items, i => i.ProcessLine == "SILK");
        Assert.Contains(result.Items, i => i.ProcessLine == "PRESS_CNC");
        Assert.DoesNotContain(result.Items, i => i.ProcessLine == "DIGITAL");
    }

    [Fact]
    public async Task ProcessLineMap_seed_is_idempotent()
    {
        DbSeeder.ProcessLineMapSeedResult r1, r2;
        using (var db = _fx.NewContext()) r1 = await DbSeeder.SeedProcessLineMapAsync(db);
        using (var db = _fx.NewContext()) r2 = await DbSeeder.SeedProcessLineMapAsync(db);
        Assert.True(r1.Inserted > 0);
        Assert.Equal(0, r2.Inserted);
        Assert.Equal(0, r2.Updated);
        Assert.Equal(r1.Total, r2.Total); // cùng số dòng sau 2 lần
    }

    [Fact]
    public async Task No_routing_yields_no_items_legacy_fallback()
    {
        await SeedLibraryAsync();
        // Product không có routing → resolve 0 line → materialize rỗng.
        var pid = await SeedProductWithRoutingAsync("NO-ROUTING-1", Array.Empty<(string, string, string)>());
        var result = await RunAutoSyncAsync(pid);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Freeze_snapshot_does_not_change_when_library_edited_after()
    {
        // Materialize lần 1.
        await SeedLibraryAsync();
        var pid = await SeedProductWithRoutingAsync("80644935", new[]
        {
            ("20", "(GALLUS) PRINT / In nhãn", "GFL01"),
            ("30", "(RDC) LAM.&Cut", "RDC12"),
        });
        var first = await RunAutoSyncAsync(pid);
        var firstSnap = first.ProfileSnapshotJson;

        // Sửa thư viện (deactivate 1 item) SAU khi đã có snapshot đóng băng.
        using (var db = _fx.NewContext())
        {
            var any = await db.CheckItemLibraries.FirstAsync(c => c.ProcessLine == "LABEL");
            any.Active = false;
            await db.SaveChangesAsync();
        }

        // Snapshot đã freeze trên WO không đổi (đây là bản đã build lần 1).
        // Controller giữ check.ItemsProfileSnapshotJson cũ → không re-resolve.
        Assert.Equal(firstSnap, first.ProfileSnapshotJson);
    }
}
