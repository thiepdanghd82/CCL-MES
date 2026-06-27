using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// Phương án C — Bước 1. Coverage cho parser CSV thư viện + seeder idempotent
/// (CheckItemLibrary + mở rộng ReasonCode). Dùng file v2 thật để khóa số lượng
/// (106 item / 5 line incl FINISHING) và chứng minh chạy 2 lần ra cùng kết quả.
/// </summary>
public sealed class QcCheckLibrarySeederTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public QcCheckLibrarySeederTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    // ── Parser (unit) ───────────────────────────────────────────────

    [Fact]
    public void Parser_handles_quoted_multiline_and_maps_columns()
    {
        // 19 cột; dòng dữ liệu có ô đa dòng trong ngoặc kép + dấu phẩy.
        var csv =
            "ItemID,Line,Group,Code,ItemVI,ItemEN,AccVI,AccEN,Method,Sev,AQL,Sampling,Type,Defect,Pareto,Short,ISO,When,Note\n" +
            "LBL-A1,LABEL,\"A·Ngoại\nquan\",A1,\"Nội dung, đúng\",Content,tc-vi,tc-en,Soi,Critical,0.65,FAI,Visual,CONTENT,,Y,,,ghi chú\n" +
            ",,,,,,,,,,,,,,,,,,\n";   // dòng rỗng → bỏ
        var rows = QcCheckLibraryCsv.Parse(csv);
        var r = Assert.Single(rows);
        Assert.Equal("LBL-A1", r.ItemId);
        Assert.Equal("LABEL", r.ProcessLine);
        Assert.Equal("A·Ngoại\nquan", r.GroupLabel);    // newline trong ô giữ nguyên
        Assert.Equal("Nội dung, đúng", r.ItemVi);        // dấu phẩy trong ô giữ nguyên
        Assert.Equal("CONTENT", r.DefectCode);
        Assert.Equal("0.65", r.Aql);
        Assert.Null(r.ParetoPct);                        // ô rỗng → null
    }

    // ── F4 (finding #8): CSV strict — bỏ hàng lỗi, không seed im lặng ─

    [Fact]
    public void ParseDetailed_skips_truncated_and_missing_required_rows()
    {
        var header = "ItemID,Line,Group,Code,ItemVI,ItemEN,AccVI,AccEN,Method,Sev,AQL,Sampling,Type,Defect,Pareto,Short,ISO,When,Note\n";
        var good = "LBL-A1,LABEL,A,A1,Nội dung,Content,tc,tc,Soi,Crit,0.65,FAI,Visual,CONTENT,,Y,,,note\n";
        var truncated = "LBL-A2,LABEL,A,A2\n";                       // chỉ 4 cột (<19)
        var missingReq = "LBL-A3,,A,A3,x,x,y,y,Soi,Crit,,,Visual,D,,,,,n\n"; // ProcessLine rỗng
        var result = QcCheckLibraryCsv.ParseDetailed(header + good + truncated + missingReq);

        Assert.Single(result.Rows);                       // chỉ hàng tốt
        Assert.Equal("LBL-A1", result.Rows[0].ItemId);
        Assert.Equal(2, result.Skipped.Count);            // truncated + missing-required
        // KHÔNG có item ProcessLine='' lọt vào.
        Assert.DoesNotContain(result.Rows, r => r.ProcessLine.Length == 0);
    }

    [Fact]
    public async Task Seed_does_not_create_empty_processline_item_from_bad_row()
    {
        var header = "ItemID,Line,Group,Code,ItemVI,ItemEN,AccVI,AccEN,Method,Sev,AQL,Sampling,Type,Defect,Pareto,Short,ISO,When,Note\n";
        var truncated = "BAD-1,LABEL,A,A1\n";  // thiếu cột
        var parsed = QcCheckLibraryCsv.ParseDetailed(header + truncated);
        using var db = _fx.NewContext();
        await DbSeeder.SeedCheckItemLibraryAsync(db, parsed.Rows);
        Assert.Equal(0, await db.CheckItemLibraries.CountAsync());
        Assert.Single(parsed.Skipped);
    }

    // ── Seeder (integration) — file v2 thật ─────────────────────────

    private static string RealCsvPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var c = Path.Combine(dir.FullName, "IPQC_Library_CMES_v3.csv");
            if (File.Exists(c)) return c;
        }
        throw new FileNotFoundException("IPQC_Library_CMES_v3.csv not found above test bin.");
    }

    [Fact]
    public void Real_library_parses_to_106_items_across_5_lines()
    {
        var rows = QcCheckLibraryCsv.Parse(File.ReadAllText(RealCsvPath()));
        Assert.Equal(106, rows.Count);   // v3: +5 FINISHING (Q2)
        var byLine = rows.GroupBy(r => r.ProcessLine).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(34, byLine["LABEL"]);
        Assert.Equal(15, byLine["DIGITAL"]);
        Assert.Equal(25, byLine["SILK"]);
        Assert.Equal(27, byLine["PRESS_CNC"]);
        Assert.Equal(5, byLine["FINISHING"]);
    }

    [Fact]
    public async Task Seed_is_idempotent_and_extends_reason_codes()
    {
        var rows = QcCheckLibraryCsv.Parse(File.ReadAllText(RealCsvPath()));

        // Lần 1 — insert toàn bộ + thêm reason codes mới.
        DbSeeder.CheckLibrarySeedResult r1;
        using (var db = _fx.NewContext())
            r1 = await DbSeeder.SeedCheckItemLibraryAsync(db, rows);
        Assert.Equal(106, r1.LibInserted);
        Assert.Equal(0, r1.LibUpdated);
        Assert.True(r1.ReasonAdded > 0, "phải thêm ít nhất 1 defect code vào ReasonCode");

        using (var db = _fx.NewContext())
        {
            Assert.Equal(106, await db.CheckItemLibraries.CountAsync());
            // Mọi defect code thư viện đều có trong ReasonCode(Scrap).
            var defects = rows.Where(x => !string.IsNullOrEmpty(x.DefectCode))
                              .Select(x => x.DefectCode!).Distinct().ToList();
            foreach (var d in defects)
                Assert.True(await db.ReasonCodes.AnyAsync(c => c.Code == d && c.Kind == ReasonCodeKind.Scrap),
                    $"thiếu ReasonCode cho defect '{d}'");
        }

        // Lần 2 — cùng input → NOOP (idempotent).
        DbSeeder.CheckLibrarySeedResult r2;
        using (var db = _fx.NewContext())
            r2 = await DbSeeder.SeedCheckItemLibraryAsync(db, rows);
        Assert.Equal(0, r2.LibInserted);
        Assert.Equal(0, r2.LibUpdated);
        Assert.Equal(0, r2.ReasonAdded);

        using (var db = _fx.NewContext())
            Assert.Equal(106, await db.CheckItemLibraries.CountAsync());   // không nhân đôi
    }

    [Fact]
    public async Task Reseed_updates_changed_field_only()
    {
        var rows = QcCheckLibraryCsv.Parse(File.ReadAllText(RealCsvPath()));
        using (var db = _fx.NewContext())
            await DbSeeder.SeedCheckItemLibraryAsync(db, rows);

        // Sửa Note của item đầu → upsert phải update đúng 1 dòng.
        var edited = rows.Select((r, i) => i == 0
            ? new QcCheckLibraryRow
            {
                ItemId = r.ItemId, ProcessLine = r.ProcessLine, GroupLabel = r.GroupLabel, Code = r.Code,
                ItemVi = r.ItemVi, ItemEn = r.ItemEn, AcceptanceVi = r.AcceptanceVi, AcceptanceEn = r.AcceptanceEn,
                Method = r.Method, Severity = r.Severity, Aql = r.Aql, Sampling = r.Sampling, CheckType = r.CheckType,
                DefectCode = r.DefectCode, ParetoPct = r.ParetoPct, ShortForm = r.ShortForm, IsoRef = r.IsoRef,
                AppliesWhen = r.AppliesWhen, Note = "EDITED-NOTE",
            }
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
