using System.Globalization;
using System.Text;
using CCL.MES.Application.AuditLogExport;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure.AuditLogExport;
using CCL.MES.Tests.Integration._Support;
using CCL.MES.Web.Services;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// Phase 9 audit-export — integration coverage for the
/// <see cref="CsvAuditLogExporter"/> + <see cref="XlsxAuditLogExporter"/>
/// pipeline through real <see cref="AuditLogService.ListForExportAsync"/>
/// on isolated /tmp SQLite. Hard cap, filter shape, RFC 4180 escape on
/// embedded JSON commas/quotes, UTF-8 BOM, and XLSX round-trip via
/// ClosedXML reload.
/// </summary>
public sealed class AuditLogExportTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    private readonly AuditLogService _service;
    private readonly CsvAuditLogExporter _csv;
    private readonly XlsxAuditLogExporter _xlsx;
    private readonly AuditLogExportContext _ctx;

    public AuditLogExportTests()
    {
        _fx = new IsolatedDbFixture();
        _service = new AuditLogService(_fx.NewContext());
        _csv = new CsvAuditLogExporter();
        _xlsx = new XlsxAuditLogExporter();
        _ctx = new AuditLogExportContext(
            Title:             "Audit Log",
            FilterDescription: null,
            GeneratedAt:       new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc),
            GeneratedBy:       "tester",
            Culture:           CultureInfo.InvariantCulture);
    }

    public void Dispose() => _fx.Dispose();

    // ── CSV exporter — pure unit-shape ──────────────────────────────────

    [Fact]
    public void Csv_export_starts_with_UTF8_BOM_and_header_row()
    {
        var rows = new List<AuditLog>
        {
            new()
            {
                Timestamp     = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
                ActorUsername = "alice",
                ActorRole     = "Admin",
                Action        = "LOGIN_OK",
                Source        = "Web",
            },
        };

        var bytes = _csv.Export(rows, _ctx);

        Assert.True(bytes.Length > 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split("\r\n", StringSplitOptions.None);
        Assert.StartsWith("Timestamp_UTC,Actor,Role,Action,Target_Type,Target_Id,Detail,IP,Source", lines[0]);
    }

    [Fact]
    public void Csv_escapes_embedded_quotes_and_commas_in_detail_JSON()
    {
        var rows = new List<AuditLog>
        {
            new()
            {
                Timestamp     = DateTime.UtcNow,
                ActorUsername = "bob",
                ActorRole     = "Engineer",
                Action        = "SPEC_COPY",
                Detail        = "{\"reason\":\"Customer asked, urgent\",\"flags\":\"a,b,c\"}",
                Source        = "Web",
            },
        };

        var bytes = _csv.Export(rows, _ctx);
        var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');

        // Embedded `"` doubled per RFC 4180; the whole cell wrapped in quotes.
        Assert.Contains("\"{\"\"reason\"\":\"\"Customer asked, urgent\"\",\"\"flags\"\":\"\"a,b,c\"\"}\"", text);
    }

    [Fact]
    public void Csv_empty_set_returns_header_only_with_BOM()
    {
        var bytes = _csv.Export(Array.Empty<AuditLog>(), _ctx);
        var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);                              // header only
        Assert.StartsWith("Timestamp_UTC", lines[0]);
    }

    // ── XLSX exporter — load back via ClosedXML ────────────────────────

    [Fact]
    public void Xlsx_export_produces_loadable_workbook_with_expected_rows()
    {
        var rows = Enumerable.Range(1, 5).Select(i => new AuditLog
        {
            Timestamp     = new DateTime(2026, 6, 1, 10, i, 0, DateTimeKind.Utc),
            ActorUsername = $"user{i}",
            ActorRole     = i % 2 == 0 ? "Admin" : "Engineer",
            Action        = "LOGIN_OK",
            Source        = "Web",
        }).ToList();

        var bytes = _xlsx.Export(rows, _ctx);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();

        Assert.Equal("Timestamp (UTC)", ws.Cell(1, 1).GetString());
        Assert.Equal("Source",           ws.Cell(1, 9).GetString());
        Assert.Equal("user1",            ws.Cell(2, 2).GetString());
        Assert.Equal("user5",            ws.Cell(6, 2).GetString());

        // Header style — bold + colored fill.
        var headerCell = ws.Cell(1, 1);
        Assert.True(headerCell.Style.Font.Bold);
    }

    [Fact]
    public void Xlsx_empty_set_produces_valid_workbook_with_header_only()
    {
        var bytes = _xlsx.Export(Array.Empty<AuditLog>(), _ctx);
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();
        // Header row present, no data rows.
        Assert.Equal("Timestamp (UTC)", ws.Cell(1, 1).GetString());
        Assert.True(ws.Cell(2, 1).IsEmpty());
    }

    // ── AuditLogService.ListForExportAsync — real EF query ────────────

    [Fact]
    public async Task ListForExport_returns_all_rows_without_paging()
    {
        await SeedAuditAsync(150);

        var result = await _service.ListForExportAsync(
            search: null, action: null, actor: null,
            from: null, to: null, hardCap: 10_000);

        Assert.False(result.Exceeded);
        Assert.Equal(150, result.MatchCount);
        Assert.Equal(150, result.Items.Count);
    }

    [Fact]
    public async Task ListForExport_filters_by_action()
    {
        await SeedAuditAsync(50, action: "LOGIN_OK");
        await SeedAuditAsync(20, action: "SPEC_COPY");

        var result = await _service.ListForExportAsync(
            search: null, action: "SPEC_COPY", actor: null,
            from: null, to: null, hardCap: 10_000);

        Assert.Equal(20, result.Items.Count);
        Assert.All(result.Items, r => Assert.Equal("SPEC_COPY", r.Action));
    }

    [Fact]
    public async Task ListForExport_filters_by_date_range()
    {
        await SeedAuditAsync(10, baseUtc: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedAuditAsync(10, baseUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await _service.ListForExportAsync(
            search: null, action: null, actor: null,
            from: new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc),
            to:   new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            hardCap: 10_000);

        // Only the June batch falls in the window (May seeds are 10..19s
        // after 2026-05-01 — all before 2026-05-25).
        Assert.Equal(10, result.Items.Count);
    }

    [Fact]
    public async Task ListForExport_filters_by_actor_LIKE_match()
    {
        await SeedAuditAsync(5, actor: "alice");
        await SeedAuditAsync(7, actor: "bob");
        await SeedAuditAsync(3, actor: "bob.qc");

        var result = await _service.ListForExportAsync(
            search: null, action: null, actor: "bob",
            from: null, to: null, hardCap: 10_000);

        // LIKE %bob% matches "bob" + "bob.qc".
        Assert.Equal(10, result.Items.Count);
    }

    [Fact]
    public async Task ListForExport_refuses_when_match_count_exceeds_hardCap()
    {
        await SeedAuditAsync(50);

        var result = await _service.ListForExportAsync(
            search: null, action: null, actor: null,
            from: null, to: null, hardCap: 10);

        Assert.True(result.Exceeded);
        Assert.Equal(50, result.MatchCount);
        Assert.Empty(result.Items);                        // never materialised
    }

    // ── End-to-end pipeline: service → exporter → file ───────────────

    [Fact]
    public async Task End_to_end_filter_then_export_produces_csv_with_matching_row_count()
    {
        await SeedAuditAsync(7, action: "LOGIN_OK");
        await SeedAuditAsync(3, action: "SPEC_COPY");

        var result = await _service.ListForExportAsync(
            search: null, action: "LOGIN_OK", actor: null,
            from: null, to: null, hardCap: 10_000);
        var bytes = _csv.Export(result.Items, _ctx);
        var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(7 + 1, lines.Length);                 // 7 rows + header
        Assert.All(lines.Skip(1), line => Assert.Contains("LOGIN_OK", line));
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private async Task SeedAuditAsync(
        int count,
        string action = "LOGIN_OK",
        string actor = "tester",
        DateTime? baseUtc = null)
    {
        using var db = _fx.NewContext();
        var t0 = baseUtc ?? new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < count; i++)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Timestamp     = t0.AddSeconds(i),
                ActorUsername = actor,
                ActorRole     = "Admin",
                Action        = action,
                Source        = "Web",
            });
        }
        await db.SaveChangesAsync();
    }
}
