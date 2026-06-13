using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Shared.Backup;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P-Backup — automated backup scheduler endpoints
/// (GET/PUT /backup/schedule + POST /backup/run-now). Exercises the full
/// wire path through MesApiFactory (real Program.cs DI → real
/// BackupSchedulerService singleton + store + verifier) against a fresh
/// per-test SQLite DB.
///
/// Guards:
///   1. Engineer auth → 403 on all 3 routes (AdminOnly enforcement).
///   2. Admin GET schedule → 200 with effective defaults.
///   3. Admin PUT schedule → persists + status reflects it.
///   4. Admin PUT invalid hour → 422 backup.invalid_schedule.
///   5. Admin POST run-now → 200 + a verified snapshot, list shows it.
/// </summary>
public sealed class BackupScheduleControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public BackupScheduleControllerTests(MesApiFactory fx) => _fx = fx;

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

    [Theory]
    [InlineData("GET", "/api/v2/backup/schedule")]
    [InlineData("PUT", "/api/v2/backup/schedule")]
    [InlineData("POST", "/api/v2/backup/run-now")]
    public async Task Engineer_auth_gets_403_on_schedule_routes(string verb, string url)
    {
        var client = await EngineerClientAsync($"eng-sch-{verb}-{url.GetHashCode():x}");
        using var req = new HttpRequestMessage(new HttpMethod(verb), url);
        if (verb == "PUT") req.Content = JsonContent.Create(new BackupScheduleUpdateRequest { Hour = 3 });
        using var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_gets_schedule_status()
    {
        var client = await AdminClientAsync("admin-sch-get");
        var resp = await client.GetAsync("/api/v2/backup/schedule");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var status = await resp.Content.ReadFromJsonAsync<BackupScheduleStatusDto>();
        Assert.NotNull(status);
        Assert.InRange(status!.Hour, 0, 23);
        Assert.True(status.RetentionDays >= 1);
        Assert.False(string.IsNullOrEmpty(status.TimeZone));
    }

    [Fact]
    public async Task Admin_sets_schedule_and_status_reflects_it()
    {
        var client = await AdminClientAsync("admin-sch-set");
        var put = await client.PutAsJsonAsync("/api/v2/backup/schedule",
            new BackupScheduleUpdateRequest { Enabled = true, Hour = 4, RetentionDays = 14, MinKeep = 7 });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var status = (await put.Content.ReadFromJsonAsync<BackupScheduleStatusDto>())!;
        Assert.True(status.Enabled);
        Assert.Equal(4, status.Hour);
        Assert.Equal(14, status.RetentionDays);
        Assert.Equal(7, status.MinKeep);
        Assert.NotNull(status.NextRunAtUtc);

        // Re-fetch — the persisted edit survives a fresh request.
        var again = (await (await client.GetAsync("/api/v2/backup/schedule"))
            .Content.ReadFromJsonAsync<BackupScheduleStatusDto>())!;
        Assert.True(again.Enabled);
        Assert.Equal(4, again.Hour);
    }

    [Fact]
    public async Task Admin_set_invalid_hour_returns_422()
    {
        var client = await AdminClientAsync("admin-sch-bad");
        var put = await client.PutAsJsonAsync("/api/v2/backup/schedule",
            new BackupScheduleUpdateRequest { Hour = 99 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, put.StatusCode);
    }

    [Fact]
    public async Task Admin_run_now_creates_verified_snapshot()
    {
        var client = await AdminClientAsync("admin-sch-run");
        var run = await client.PostAsync("/api/v2/backup/run-now", content: null);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var result = (await run.Content.ReadFromJsonAsync<BackupRunResultDto>())!;
        Assert.True(result.Ok, $"run not ok: {result.Error} / integrity={result.Integrity}");
        Assert.True(result.VerifyOk);
        Assert.Equal("ok", result.Integrity, ignoreCase: true);
        Assert.False(string.IsNullOrEmpty(result.SqliteFile));

        // The new snapshot shows up in the list.
        var list = await (await client.GetAsync("/api/v2/backup"))
            .Content.ReadFromJsonAsync<List<BackupSnapshotDto>>();
        Assert.Contains(list!, r => r.FileName == result.SqliteFile);
    }
}
