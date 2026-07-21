using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using CCL.MES.Api.Tests._Support;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Wire tests for the WorkCenter WRITE surface (Admin only) + export (any
/// NpiRead viewer): create (unique/409), update, delete, CSV upsert import
/// (idempotent + per-row report), CSV export (BOM), and the RBAC 403 matrix.
/// </summary>
public sealed class NpiWorkCenterControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public NpiWorkCenterControllerTests(MesApiFactory fx) => _fx = fx;

    private sealed record ImportReport(int Inserted, int Updated, int Skipped, List<ErrRow> Errors);
    private sealed record ErrRow(int Row, string Reason);
    private sealed record WcRow(long Id, string Code, string? Description, bool? Active);

    private async Task<HttpClient> ClientAsync(string user, string role)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", role);
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, "P@ss!1");
        return c;
    }

    private static object Body(string code, string desc = "d", double? speed = 100, string? shift = "A", bool active = true)
        => new { Code = code, Description = desc, Area = "AreaX", IdealSpeedPcsH = speed, ShiftPattern = shift, Active = active };

    private static MultipartFormDataContent Csv(string content, bool bom = false)
    {
        var form = new MultipartFormDataContent();
        var bytes = bom ? Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray()
                        : Encoding.UTF8.GetBytes(content);
        var fc = new ByteArrayContent(bytes);
        fc.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fc, "file", "wc.csv");
        return form;
    }

    private static string Code(string tag) => ("WC" + tag).ToUpperInvariant()[..Math.Min(12, ("WC" + tag).Length)];

    // ── RBAC: create/update/delete/import are Admin only ──
    [Theory]
    [InlineData("Supervisor")]
    [InlineData("Engineer")]
    [InlineData("QC")]
    public async Task Mutations_are_forbidden_for_non_admin(string role)
    {
        var c = await ClientAsync($"wc-403-{role}", role);
        var create = await c.PostAsJsonAsync("/api/v2/npi/workcenters", Body(Code("403")));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var update = await c.PutAsJsonAsync("/api/v2/npi/workcenters/1", Body(Code("403")));
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        var del = await c.DeleteAsync("/api/v2/npi/workcenters/1");
        Assert.Equal(HttpStatusCode.Forbidden, del.StatusCode);

        var imp = await c.PostAsync("/api/v2/npi/workcenters/import", Csv("Code\nWC-X01\n"));
        Assert.Equal(HttpStatusCode.Forbidden, imp.StatusCode);
    }

    [Fact]
    public async Task Create_requires_auth()
    {
        var c = _fx.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/v2/npi/workcenters", Body("WC-NOAUTH"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Create: happy + unique 409 + invalid 422 ──
    [Fact]
    public async Task Admin_creates_then_duplicate_code_is_409()
    {
        var c = await ClientAsync("wc-create", "Admin");
        var code = Code(Guid.NewGuid().ToString("N")[..5]);

        var ok = await c.PostAsJsonAsync("/api/v2/npi/workcenters", Body(code));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var row = await ok.Content.ReadFromJsonAsync<WcRow>();
        Assert.Equal(code, row!.Code);

        var dup = await c.PostAsJsonAsync("/api/v2/npi/workcenters", Body(code));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        var bad = await c.PostAsJsonAsync("/api/v2/npi/workcenters", Body("x")); // fails regex
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
    }

    [Fact]
    public async Task Admin_updates_and_deletes()
    {
        var c = await ClientAsync("wc-upd-del", "Admin");
        var code = Code(Guid.NewGuid().ToString("N")[..5]);
        var created = await (await c.PostAsJsonAsync("/api/v2/npi/workcenters", Body(code))).Content.ReadFromJsonAsync<WcRow>();

        var upd = await c.PutAsJsonAsync($"/api/v2/npi/workcenters/{created!.Id}", Body(code, desc: "updated"));
        Assert.Equal(HttpStatusCode.OK, upd.StatusCode);
        Assert.Equal("updated", (await upd.Content.ReadFromJsonAsync<WcRow>())!.Description);

        var del = await c.DeleteAsync($"/api/v2/npi/workcenters/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        var del2 = await c.DeleteAsync($"/api/v2/npi/workcenters/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, del2.StatusCode);
    }

    // ── Import: upsert idempotent + per-row report ──
    [Fact]
    public async Task Import_upsert_is_idempotent_and_reports_rows()
    {
        var c = await ClientAsync("wc-import", "Admin");
        var tag = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var a = $"WC-{tag}A"; var b = $"WC-{tag}B";
        var csv = $"Code,Description,Area,Ideal Speed,Shift,Active\n{a},First,Print,120,A,1\n{b},Second,Cut,,B+bad,yes\nxx,badcode,,,,\n";

        var r1 = await (await c.PostAsync("/api/v2/npi/workcenters/import", Csv(csv, bom: true))).Content.ReadFromJsonAsync<ImportReport>();
        Assert.Equal(2, r1!.Inserted);         // a + b inserted; "xx" invalid code skipped
        Assert.Equal(0, r1.Updated);
        Assert.True(r1.Skipped >= 1);
        Assert.NotEmpty(r1.Errors);

        // Same file again → both UPDATED, nothing inserted (idempotent, non-deleting).
        var r2 = await (await c.PostAsync("/api/v2/npi/workcenters/import", Csv(csv))).Content.ReadFromJsonAsync<ImportReport>();
        Assert.Equal(0, r2!.Inserted);
        Assert.Equal(2, r2.Updated);
    }

    // ── Export: CSV with BOM, viewer-allowed, respects search ──
    [Fact]
    public async Task Export_returns_csv_with_bom_for_any_viewer()
    {
        var admin = await ClientAsync("wc-exp-admin", "Admin");
        var tag = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        await admin.PostAsJsonAsync("/api/v2/npi/workcenters", Body($"WC-{tag}", desc: "ExportMe"));

        var viewer = await ClientAsync("wc-exp-qc", "QC");   // NpiRead can export
        var resp = await viewer.GetAsync($"/api/v2/npi/workcenters/export?search=WC-{tag}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/csv", resp.Content.Headers.ContentType!.MediaType);

        var text = await resp.Content.ReadAsStringAsync();
        Assert.StartsWith("﻿", text);                          // UTF-8 BOM
        Assert.Contains("ID,Code,Description", text);               // header
        Assert.Contains($"WC-{tag}", text);
        Assert.Contains("ExportMe", text);
    }

    // ── Round-trip: export → import same data → no duplication ──
    [Fact]
    public async Task Export_then_import_roundtrip_does_not_duplicate()
    {
        var c = await ClientAsync("wc-round", "Admin");
        var tag = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        await c.PostAsJsonAsync("/api/v2/npi/workcenters", Body($"WC-{tag}", desc: "Round"));

        var csv = await (await c.GetAsync($"/api/v2/npi/workcenters/export?search=WC-{tag}")).Content.ReadAsStringAsync();
        var report = await (await c.PostAsync("/api/v2/npi/workcenters/import", Csv(csv))).Content.ReadFromJsonAsync<ImportReport>();
        // The exported row already exists → update, never a second insert.
        Assert.Equal(0, report!.Inserted);
        Assert.True(report.Updated >= 1);
    }
}
