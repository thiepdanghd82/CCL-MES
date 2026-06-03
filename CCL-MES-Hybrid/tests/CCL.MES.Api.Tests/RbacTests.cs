using System.Net;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;

namespace CCL.MES.Api.Tests;

/// <summary>
/// RBAC end-to-end: a real JWT issued to a real role hits a real endpoint,
/// and the policy port enforces the same role membership the legacy Web
/// app does. If any policy here breaks, P10.1's defence-in-depth promise
/// is broken.
/// </summary>
public sealed class RbacTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public RbacTests(MesApiFactory fx) => _fx = fx;

    [Fact]
    public async Task AdminOnly_admits_admin()
    {
        await _fx.SeedUserAsync("sysadm", "Pa55w.rd!", UserRole.Admin);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "sysadm", "Pa55w.rd!");

        var resp = await client.GetAsync("/api/v2/system-log?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task AdminOnly_denies_engineer()
    {
        await _fx.SeedUserAsync("nonadmin", "Pa55w.rd!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "nonadmin", "Pa55w.rd!");

        var resp = await client.GetAsync("/api/v2/system-log?pageSize=1");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task NpiRead_admits_qc_role()
    {
        // Legacy Program.cs:193-194 — NpiRead includes Admin/Supervisor/Engineer/QC.
        await _fx.SeedUserAsync("qcuser", "Pa55w.rd!", UserRole.Qc);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "qcuser", "Pa55w.rd!");

        var resp = await client.GetAsync("/api/v2/npi/workcenters?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task NpiRead_denies_operator()
    {
        await _fx.SeedUserAsync("opuser", "Pa55w.rd!", UserRole.Operator);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "opuser", "Pa55w.rd!");

        var resp = await client.GetAsync("/api/v2/npi/workcenters?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task NpiSpecRead_denies_qc()
    {
        // Legacy Program.cs:195-196 — NpiSpecRead is Admin/Supervisor/Engineer (no QC).
        await _fx.SeedUserAsync("qcnotspec", "Pa55w.rd!", UserRole.Qc);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "qcnotspec", "Pa55w.rd!");

        var resp = await client.GetAsync("/api/v2/specs?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task QcRead_admits_admin_and_supervisor_and_qc()
    {
        foreach (var role in new[] { UserRole.Admin, UserRole.Supervisor, UserRole.Qc })
        {
            var username = $"qcread-{role.ToLowerInvariant()}";
            await _fx.SeedUserAsync(username, "Pa55w.rd!", role);
            var client = _fx.CreateClient();
            await _fx.LoginAndAuthenticateAsync(client, username, "Pa55w.rd!");

            var resp = await client.GetAsync("/api/v2/iqc?page=1&pageSize=1");
            Assert.True(resp.StatusCode == HttpStatusCode.OK,
                $"role {role} must be admitted by QcRead but got {resp.StatusCode}");
        }
    }

    [Fact]
    public async Task QcRead_denies_engineer()
    {
        // Legacy Program.cs:197-198 — QcRead is Admin/Supervisor/QC (no Engineer).
        await _fx.SeedUserAsync("enguser", "Pa55w.rd!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "enguser", "Pa55w.rd!");

        var resp = await client.GetAsync("/api/v2/iqc?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Fallback_policy_admits_authenticated_user_on_unpolicied_endpoint()
    {
        // /work-orders only has [Authorize] (no policy) — any authenticated user passes.
        await _fx.SeedUserAsync("anyone", "Pa55w.rd!", UserRole.Operator);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "anyone", "Pa55w.rd!");

        var resp = await client.GetAsync("/api/v2/work-orders");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
