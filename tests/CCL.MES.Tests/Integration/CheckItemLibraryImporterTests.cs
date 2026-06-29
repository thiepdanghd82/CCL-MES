using CCL.MES.Application.Services;
using CCL.MES.Infrastructure.SpecExport;
using CCL.MES.Tests.Integration._Support;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// QC Library admin (Bước 1) — shared importer + xlsx parser.
/// Blocking tests: valid import counts, idempotent re-import, strict
/// ProcessLine rejection, xlsx missing-required-column skip.
/// </summary>
public sealed class CheckItemLibraryImporterTests : IClassFixture<IsolatedDbFixture>
{
    private readonly IsolatedDbFixture _fx;
    public CheckItemLibraryImporterTests(IsolatedDbFixture fx) => _fx = fx;

    private static string Csv(params string[] dataRows)
    {
        // 19-col header + rows.
        var header = "ItemId,ProcessLine,GroupLabel,Code,ItemVi,ItemEn,AcceptanceVi,AcceptanceEn," +
                     "Method,Severity,Aql,Sampling,CheckType,DefectCode,ParetoPct,ShortForm,IsoRef,AppliesWhen,Note";
        return header + "\n" + string.Join("\n", dataRows);
    }

    private static string Row(string itemId, string line, string vi = "noi dung", string defect = "")
        => $"{itemId},{line},A·Ngoại quan,A1,{vi},content,khop,matches,Visual,◆ Critical,,,Visual,{defect},,,,,";

    [Fact]
    public async Task Import_inserts_then_reimport_is_idempotent()
    {
        var parsed = QcCheckLibraryCsv.ParseDetailed(Csv(
            Row("IMP1-A1", "LABEL"), Row("IMP1-A2", "LABEL", defect: "IMP1_CONTENT")));

        using (var db = _fx.NewContext())
        {
            var r1 = await CheckItemLibraryImporter.ImportAsync(db, parsed, "tester");
            Assert.Equal(2, r1.Parsed);
            Assert.Equal(2, r1.Inserted);
            Assert.Equal(0, r1.Updated);
            Assert.Equal(0, r1.Skipped);
            Assert.Empty(r1.Errors);
        }
        // Re-import the SAME rows → 0 net change (idempotent upsert by ItemId).
        using (var db = _fx.NewContext())
        {
            var r2 = await CheckItemLibraryImporter.ImportAsync(db, parsed, "tester");
            Assert.Equal(0, r2.Inserted);
            Assert.Equal(0, r2.Updated);
        }
        using (var db = _fx.NewContext())
            Assert.Equal(2, await db.CheckItemLibraries.CountAsync(c => c.ItemId.StartsWith("IMP1-")));
    }

    [Fact]
    public async Task Import_rejects_invalid_processline_without_silent_seed()
    {
        var parsed = QcCheckLibraryCsv.ParseDetailed(Csv(
            Row("IMP2-OK", "PRESS_CNC"), Row("IMP2-BAD", "BOGUS_LINE")));

        using var db = _fx.NewContext();
        var r = await CheckItemLibraryImporter.ImportAsync(db, parsed, "tester");

        Assert.Equal(1, r.Inserted);          // only the valid one
        Assert.Equal(1, r.Skipped);           // BOGUS_LINE dropped
        Assert.Contains(r.Errors, e => e.Contains("IMP2-BAD") && e.Contains("ProcessLine"));
        Assert.False(await db.CheckItemLibraries.AnyAsync(c => c.ItemId == "IMP2-BAD"));
        Assert.True(await db.CheckItemLibraries.AnyAsync(c => c.ItemId == "IMP2-OK"));
    }

    [Fact]
    public void Xlsx_parse_skips_row_missing_required_field()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("lib");
        for (int c = 0; c < QcCheckLibraryXlsx.TemplateHeaders.Length; c++)
            ws.Cell(1, c + 1).Value = QcCheckLibraryXlsx.TemplateHeaders[c];
        // Good row.
        var good = new[] { "XL-1", "LABEL", "A·NQ", "A1", "vi", "en", "acc", "acc", "Visual", "", "", "", "", "", "", "", "", "", "" };
        for (int c = 0; c < good.Length; c++) ws.Cell(2, c + 1).Value = good[c];
        // Bad row — empty ItemVi (required col 4).
        var bad = new[] { "XL-2", "LABEL", "A·NQ", "A1", "", "en", "acc", "acc" };
        for (int c = 0; c < bad.Length; c++) ws.Cell(3, c + 1).Value = bad[c];

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var parsed = QcCheckLibraryXlsx.ParseDetailed(ms);
        Assert.Single(parsed.Rows);
        Assert.Equal("XL-1", parsed.Rows[0].ItemId);
        Assert.Contains(parsed.Skipped, s => s.Contains("XL-2") && s.Contains("ItemVi"));
    }

    [Fact]
    public async Task Import_does_not_retro_change_a_materialized_WO_snapshot()
    {
        // A WO already materialized its IPQC items + froze the snapshot.
        const string frozenLabel = "Đúng nội dung in (FROZEN)";
        const string frozenSnapshot = "{\"frozen\":true}";
        using (var db = _fx.NewContext())
        {
            var check = new CCL.MES.Domain.Entities.WoIpqcCheck
            {
                WorkOrderId = 987654, // standalone (no FK to WorkOrder)
                ItemsProfileSnapshotJson = frozenSnapshot,
            };
            check.Items.Add(new CCL.MES.Domain.Entities.WoIpqcCheckItem
            {
                ItemKey = "FRZ-A1", ProcessLine = "LABEL", GroupLabel = "A", Label = frozenLabel,
            });
            db.WoIpqcChecks.Add(check);
            await db.SaveChangesAsync();
        }

        // Now EDIT the library item with the same ItemId (different content).
        var parsed = QcCheckLibraryCsv.ParseDetailed(Csv(Row("FRZ-A1", "LABEL", vi: "Nội dung MỚI sau khi sửa")));
        using (var db = _fx.NewContext())
            await CheckItemLibraryImporter.ImportAsync(db, parsed, "tester");

        // The materialized WO item + snapshot are UNCHANGED (freeze invariant).
        using (var db = _fx.NewContext())
        {
            var item = await db.WoIpqcCheckItems.FirstAsync(i => i.ItemKey == "FRZ-A1");
            Assert.Equal(frozenLabel, item.Label);
            var chk = await db.WoIpqcChecks.FirstAsync(c => c.WorkOrderId == 987654);
            Assert.Equal(frozenSnapshot, chk.ItemsProfileSnapshotJson);
        }
    }
}
