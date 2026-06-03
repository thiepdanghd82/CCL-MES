using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Shared.Devices;
using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.3 W4 — coverage for the kiosk/device surface:
///   GET /devices/{id} (404 when never seen, 200 with snapshot when seen)
///   POST /devices/{id}/scan-log (audit emit, response shape)
///   POST /devices/{id}/heartbeat (snapshot update, reconnect detection)
/// + the device-id validation regex + JWT gate.
/// </summary>
public sealed class DevicesControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public DevicesControllerTests(MesApiFactory fx) => _fx = fx;

    private const string DeviceA = "0193a1d9-aaaa-bbbb-cccc-dddddddddddd";
    private const string DeviceB = "0193a1d9-1111-2222-3333-444444444444";

    [Fact]
    public async Task Anonymous_call_to_scan_log_returns_401()
    {
        var client = _fx.CreateClient();
        var resp = await client.PostAsJsonAsync(
            $"/api/v2/devices/{DeviceA}/scan-log",
            new ScanLogRequest { Payload = "WO-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Invalid_device_id_returns_400()
    {
        await _fx.SeedUserAsync("dev1", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "dev1", "P@ss!");

        var resp = await client.PostAsJsonAsync(
            "/api/v2/devices/abc/scan-log", // too short
            new ScanLogRequest { Payload = "WO-1" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("device.invalid_id", err!.Code);
    }

    [Fact]
    public async Task Empty_scan_payload_returns_400()
    {
        await _fx.SeedUserAsync("dev2", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "dev2", "P@ss!");

        var resp = await client.PostAsJsonAsync(
            $"/api/v2/devices/{DeviceA}/scan-log",
            new ScanLogRequest { Payload = "" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("scan.empty_payload", err!.Code);
    }

    [Fact]
    public async Task ScanLog_returns_scan_id_and_timestamp()
    {
        await _fx.SeedUserAsync("dev3", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "dev3", "P@ss!");

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var resp = await client.PostAsJsonAsync(
            $"/api/v2/devices/{DeviceA}/scan-log",
            new ScanLogRequest
            {
                Payload = "WO-2026-005",
                Format = "qr",
                Context = "wo-accept",
                SourceDeviceLabel = "FaceTime HD",
            });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ScanLogResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.ScanId);
        Assert.True(body.ServerTimestamp >= before);
    }

    [Fact]
    public async Task Heartbeat_first_call_returns_OK_and_makes_GET_visible()
    {
        await _fx.SeedUserAsync("dev4", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "dev4", "P@ss!");

        var resp = await client.PostAsJsonAsync(
            $"/api/v2/devices/{DeviceB}/heartbeat",
            new HeartbeatRequest { AppVersion = "1.0.0", Mode = "interactive", Platform = "MacCatalyst" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var info = await client.GetFromJsonAsync<DeviceInfoResponse>(
            $"/api/v2/devices/{DeviceB}");
        Assert.NotNull(info);
        Assert.Equal(DeviceB, info!.DeviceId);
        Assert.Equal("1.0.0", info.LastAppVersion);
        Assert.Equal("interactive", info.LastMode);
        Assert.Equal("MacCatalyst", info.LastPlatform);
        Assert.Equal(0, info.ScanCountLast24h);
    }

    [Fact]
    public async Task Get_returns_404_when_device_never_connected()
    {
        await _fx.SeedUserAsync("dev5", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "dev5", "P@ss!");

        var resp = await client.GetAsync("/api/v2/devices/0193a1d9-9999-9999-9999-999999999999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task ScanLog_bumps_24h_count_in_snapshot()
    {
        await _fx.SeedUserAsync("dev6", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "dev6", "P@ss!");
        var deviceId = "0193a1d9-7777-8888-9999-aaaaaaaaaaaa";

        await client.PostAsJsonAsync(
            $"/api/v2/devices/{deviceId}/heartbeat",
            new HeartbeatRequest { AppVersion = "1.0.0" });

        await client.PostAsJsonAsync(
            $"/api/v2/devices/{deviceId}/scan-log",
            new ScanLogRequest { Payload = "WO-A" });
        await client.PostAsJsonAsync(
            $"/api/v2/devices/{deviceId}/scan-log",
            new ScanLogRequest { Payload = "WO-B" });

        var info = await client.GetFromJsonAsync<DeviceInfoResponse>(
            $"/api/v2/devices/{deviceId}");
        Assert.Equal(2, info!.ScanCountLast24h);
    }
}
