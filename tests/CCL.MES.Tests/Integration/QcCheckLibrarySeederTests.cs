using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Infrastructure;
using CCL.MES.Infrastructure.QcLibrary;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// Re-model v5 — coverage cho parser xlsx thư viện (ma trận tick-box) + seeder
/// idempotent (CheckItemLibrary + mở rộng ReasonCode). Dùng file v5 THẬT
/// (sheet IPQC_FQC_OQC_MAP, 59 item / 2 line LABEL·SILK) để khóa số lượng +
/// chứng minh chạy 2 lần ra cùng kết quả. CSV parser cũ (v1..v3) vẫn có unit
/// test riêng bên dưới (giữ tương thích ngược).
/// </summary>
public sealed class QcCheckLibrarySeederTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public QcCheckLibrarySeederTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    // ── CSV parser (legacy v1..v3) — unit ───────────────────────────────

    [Fact]
    public void Parser_handles_quoted_multiline_and_maps_columns()
    {
        var csv =
            "ItemID,Line,Group,Code,ItemVI,ItemEN,AccVI,AccEN,Method,Sev,AQL,Sampling,Type,Defect,Pareto,Short,ISO,When,Note\n" +
            "LBL-A1,LABEL,\"A·Ngoại\nquan\",A1,\"Nội dung, đúng\",Content,tc-vi,tc-en,Soi,Critical,0.65,FAI,Visual,CONTENT,,Y,,,ghi chú\n" +
            ",,,,,,,,,,,,,,,,,,\n";
        var rows = QcCheckLibraryCsv.Parse(csv);
        var r = Assert.Single(rows);
        Assert.Equal("LBL-A1", r.ItemId);
        Assert.Equal("LABEL", r.ProcessLine);
        Assert.Equal("A·Ngoại\nquan", r.GroupLabel);
        Assert.Equal("Nội dung, đúng", r.ItemVi);
        Assert.Equal("CONTENT", r.DefectCode);
        Assert.Equal("0.65", r.Aql);
        Assert.Null(r.ParetoPct);
    }

    [Fact]
    public void ParseDetailed_skips_truncated_and_missing_required_rows()
    {
        var header = "ItemID,Line,Group,Code,ItemVI,ItemEN,AccVI,AccEN,Method,Sev,AQL,Sampling,Type,Defect,Pareto,Short,ISO,When,Note\n";
        var good = "LBL-A1,LABEL,A,A1,Nội dung,Content,tc,tc,Soi,Crit,0.65,FAI,Visual,CONTENT,,Y,,,note\n";
        var truncated = "LBL-A2,LABEL,A,A2\n";
        var missingReq = "LBL-A3,,A,A3,x,x,y,y,Soi,Crit,,,Visual,D,,,,,n\n";
        var result = QcCheckLibraryCsv.ParseDetailed(header + good + truncated + missingReq);

        Assert.Single(result.Rows);
        Assert.Equal("LBL-A1", result.Rows[0].ItemId);
        Assert.Equal(2, result.Skipped.Count);
        Assert.DoesNotContain(result.Rows, r => r.ProcessLine.Length == 0);
    }

    [Fact]
    public async Task Seed_does_not_create_empty_processline_item_from_bad_row()
    {
        var header = "ItemID,Line,Group,Code,ItemVI,ItemEN,AccVI,AccEN,Method,Sev,AQL,Sampling,Type,Defect,Pareto,Short,ISO,When,Note\n";
        var truncated = "BAD-1,LABEL,A,A1\n";
        var parsed = QcCheckLibraryCsv.ParseDetailed(header + truncated);
        using var db = _fx.NewContext();
        await DbSeeder.SeedCheckItemLibraryAsync(db, parsed.Rows);
        Assert.Equal(0, await db.CheckItemLibraries.CountAsync());
        Assert.Single(parsed.Skipped);
    }

    // ── v5 parser (xlsx, ma trận tick-box) — file thật ──────────────────

    private static string RealV5Path()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var c = Path.Combine(dir.FullName, "IPQC_Library_CMES_v5.xlsx");
            if (File.Exists(c)) return c;
        }
        throw new FileNotFoundException("IPQC_Library_CMES_v5.xlsx not found above test bin.");
    }

    private static IReadOnlyList<QcCheckLibraryRow> ParseV5()
    {
        using var fs = File.OpenRead(RealV5Path());
        return QcLibraryV5Parser.Parse(fs);
    }

    [Fact]
    public void Real_v5_library_parses_to_59_items_across_label_and_silk()
    {
        var rows = ParseV5();
        Assert.Equal(59, rows.Count);
        var byLine = rows.GroupBy(r => r.ProcessLine).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(34, byLine["LABEL"]);
        Assert.Equal(25, byLine["SILK"]);
        Assert.Equal(2, byLine.Count);   // v5 chỉ 2 line
    }

    [Fact]
    public void V5_tickbox_maps_dot_to_true_and_blank_to_false()
    {
        var rows = ParseV5();
        // LBL-A1: ● Flexo/LetterPress/HpIndigo + Zebra + IPQC/FQC/OQC; KHÔNG SilkScreen.
        var a1 = Assert.Single(rows, r => r.ItemId == "LBL-A1");
        Assert.True(a1.Flexo);
        Assert.True(a1.LetterPress);
        Assert.True(a1.HpIndigo);
        Assert.True(a1.Ipqc);
        Assert.True(a1.Fqc);
        Assert.True(a1.Oqc);
        Assert.False(a1.SilkScreen);
        Assert.False(a1.BlankLabel);
        // Ít nhất 1 item stage IPQC=true (đủ để materialize).
        Assert.Contains(rows, r => r.Ipqc);
    }

    [Fact]
    public async Task V5_seed_is_idempotent_and_extends_reason_codes()
    {
        var rows = ParseV5();

        DbSeeder.CheckLibrarySeedResult r1;
        using (var db = _fx.NewContext())
            r1 = await DbSeeder.SeedCheckItemLibraryAsync(db, rows);
        Assert.Equal(59, r1.LibInserted);
        Assert.Equal(0, r1.LibUpdated);
        Assert.True(r1.ReasonAdded > 0, "phải thêm ít nhất 1 defect code vào ReasonCode");

        using (var db = _fx.NewContext())
        {
            Assert.Equal(59, await db.CheckItemLibraries.CountAsync());
            var defects = rows.Where(x => !string.IsNullOrEmpty(x.DefectCode))
                              .Select(x => x.DefectCode!).Distinct().ToList();
            foreach (var d in defects)
                Assert.True(await db.ReasonCodes.AnyAsync(c => c.Code == d && c.Kind == ReasonCodeKind.Scrap),
                    $"thiếu ReasonCode cho defect '{d}'");
        }

        DbSeeder.CheckLibrarySeedResult r2;
        using (var db = _fx.NewContext())
            r2 = await DbSeeder.SeedCheckItemLibraryAsync(db, rows);
        Assert.Equal(0, r2.LibInserted);
        Assert.Equal(0, r2.LibUpdated);
        Assert.Equal(0, r2.ReasonAdded);

        using (var db = _fx.NewContext())
            Assert.Equal(59, await db.CheckItemLibraries.CountAsync());
    }

    [Fact]
    public async Task V5_reseed_updates_changed_field_only()
    {
        var rows = ParseV5();
        using (var db = _fx.NewContext())
            await DbSeeder.SeedCheckItemLibraryAsync(db, rows);

        var edited = rows.Select((r, i) => i == 0
            ? r with { Note = "EDITED-NOTE" }
            : r).ToList();

        DbSeeder.CheckLibrarySeedResult r2;
        using (var db = _fx.NewContext())
            r2 = await DbSeeder.SeedCheckItemLibraryAsync(db, edited);
        Assert.Equal(0, r2.LibInserted);
        Assert.Equal(1, r2.LibUpdated);

        using (var db = _fx.NewContext())
        {
            var first = await db.CheckItemLibraries.FirstAsync(x => x.ItemId == rows[0].ItemId);
            Assert.Equal("EDITED-NOTE", first.Note);
        }
    }
}
