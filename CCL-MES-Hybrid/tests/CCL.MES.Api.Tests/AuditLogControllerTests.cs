using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Shared.Audit;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.6e — admin Audit Log viewer + CSV/XLSX export.
///
/// Coverage:
///   1. Anon → 401 (covered separately by RouteDiscoveryCanaryTests for
///      all 4 endpoints).
///   2. Engineer auth → 403 on all 4 endpoints — POLICY enforcement
///      proven via real HTTP, not just route discovery.
///   3. Admin can list paged JSON.
///   4. Admin can filter by action + by actor.
///   5. Admin can fetch distinct actions list.
///   6. CSV export → 200 + content-type "text/csv" + header row even
///      when result set is empty (zero-row range MUST still produce a
///      valid file).
///   7. XLSX export → 200 + content-type matches ClosedXML's MIME +
///      non-empty bytes even when result set is empty.
///   8. Successful export EMITS an AUDIT_EXPORT row visible via the
///      list endpoint on the next call.
///   9. Empty-range CSV is still valid (header row only).
/// </summary>
public sealed class AuditLogControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public AuditLogControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> AdminClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Admin);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private async Task<HttpClient> EngineerClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    // ── Role-gating ─────────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "/api/v2/audit/log")]
    [InlineData("GET", "/api/v2/audit/actions")]
    [InlineData("GET", "/api/v2/audit/export/csv")]
    [InlineData("GET", "/api/v2/audit/export/xlsx")]
    public async Task Engineer_auth_gets_403_on_every_audit_route(string verb, string url)
    {
        var client = await EngineerClientAsync($"eng-aud-{verb}-{url.GetHashCode():x}");
        using var req = new HttpRequestMessage(new HttpMethod(verb), url);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Admin happy ─────────────────────────────────────────────────

    [Fact]
    public async Task Admin_can_list_paged_audit_log()
    {
        var client = await AdminClientAsync("admin-aud-list");
        // Login above already emitted at least one LOGIN_OK row.
        var resp = await client.GetAsync("/api/v2/audit/log?page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AuditLogPagedResult>();
        Assert.NotNull(body);
        Assert.True(body!.Total > 0, "login should have emitted at least one audit row");
        Assert.True(body.Items.Count > 0);
        Assert.Equal(1, body.Page);
        Assert.Equal(50, body.PageSize);
        Assert.All(body.Items, e => Assert.False(string.IsNullOrEmpty(e.Action)));
    }

    [Fact]
    public async Task Admin_can_filter_by_action_LOGIN_OK()
    {
        var client = await AdminClientAsync("admin-aud-filter-action");
        var resp = await client.GetAsync("/api/v2/audit/log?action=LOGIN_OK&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<AuditLogPagedResult>())!;
        Assert.True(body.Items.Count > 0);
        Assert.All(body.Items, e => Assert.Equal("LOGIN_OK", e.Action));
    }

    [Fact]
    public async Task Admin_can_filter_by_actor_username()
    {
        var actor = "admin-aud-filter-actor";
        var client = await AdminClientAsync(actor);
        var resp = await client.GetAsync($"/api/v2/audit/log?actor={actor}&page=1&pageSize=10");
        var body = (await resp.Content.ReadFromJsonAsync<AuditLogPagedResult>())!;
        Assert.True(body.Items.Count > 0);
        Assert.All(body.Items, e => Assert.Contains(actor, e.ActorUsername));
    }

    [Fact]
    public async Task Admin_can_list_distinct_actions()
    {
        var client = await AdminClientAsync("admin-aud-actions");
        var resp = await client.GetAsync("/api/v2/audit/actions");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var actions = await resp.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(actions);
        Assert.Contains("LOGIN_OK", actions!);
    }

    // ── Exports ─────────────────────────────────────────────────────

    [Fact]
    public async Task Csv_export_returns_csv_content_type_and_header_row()
    {
        var client = await AdminClientAsync("admin-aud-csv");
        var resp = await client.GetAsync("/api/v2/audit/export/csv");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.NotNull(resp.Content.Headers.ContentType);
        Assert.Contains("text/csv", resp.Content.Headers.ContentType!.ToString());
        var fileName = resp.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        Assert.NotNull(fileName);
        Assert.StartsWith("AuditLog_", fileName!);
        Assert.EndsWith(".csv", fileName!);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 3, "UTF-8 BOM + header row should be present");
        // Skip the 3-byte UTF-8 BOM before checking for the column header.
        var text = System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.StartsWith("Timestamp_UTC,Actor,Role,Action,Target_Type,Target_Id,Detail,IP,Source",
            text);
    }

    [Fact]
    public async Task Xlsx_export_returns_xlsx_content_type_and_non_empty_body()
    {
        var client = await AdminClientAsync("admin-aud-xlsx");
        var resp = await client.GetAsync("/api/v2/audit/export/xlsx");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.NotNull(resp.Content.Headers.ContentType);
        Assert.Contains("spreadsheetml.sheet", resp.Content.Headers.ContentType!.ToString());
        var fileName = resp.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        Assert.NotNull(fileName);
        Assert.EndsWith(".xlsx", fileName!);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 100, "ClosedXML workbook should have at least header row content");
        // Quick sanity: xlsx files are ZIP archives — first 2 bytes are "PK".
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }

    [Fact]
    public async Task Csv_export_with_empty_range_still_returns_valid_file_with_header()
    {
        var client = await AdminClientAsync("admin-aud-csv-empty");
        // Pick a year-2000 narrow range that no fixture audit row falls into.
        var resp = await client.GetAsync(
            "/api/v2/audit/export/csv?from=2000-01-01T00:00:00Z&to=2000-01-02T00:00:00Z");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 3);
        var text = System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        // ONLY the header line should be present (+ trailing CRLF).
        Assert.Equal(
            "Timestamp_UTC,Actor,Role,Action,Target_Type,Target_Id,Detail,IP,Source\r\n",
            text);
    }

    [Fact]
    public async Task Successful_export_emits_AUDIT_EXPORT_audit_row()
    {
        var client = await AdminClientAsync("admin-aud-emit");
        // Snapshot the AUDIT_EXPORT count pre-export.
        var pre = await client.GetAsync("/api/v2/audit/log?action=AUDIT_EXPORT&page=1&pageSize=1");
        var preBody = (await pre.Content.ReadFromJsonAsync<AuditLogPagedResult>())!;
        var preCount = preBody.Total;

        // Do an export.
        var exp = await client.GetAsync("/api/v2/audit/export/csv");
        Assert.Equal(HttpStatusCode.OK, exp.StatusCode);

        // Re-list — count must have ticked up.
        var post = await client.GetAsync("/api/v2/audit/log?action=AUDIT_EXPORT&page=1&pageSize=1");
        var postBody = (await post.Content.ReadFromJsonAsync<AuditLogPagedResult>())!;
        Assert.True(postBody.Total > preCount,
            $"AUDIT_EXPORT count expected to grow from {preCount}; got {postBody.Total}");
        // Top row carries our actor.
        Assert.Equal("admin-aud-emit", postBody.Items[0].ActorUsername);
        Assert.Equal("AUDIT_EXPORT", postBody.Items[0].Action);
    }
}
