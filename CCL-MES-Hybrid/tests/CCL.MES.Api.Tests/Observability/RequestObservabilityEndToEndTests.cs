using System.Diagnostics.Metrics;
using System.Net;
using CCL.MES.Api.Observability;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using Xunit;

namespace CCL.MES.Api.Tests.Observability;

/// <summary>
/// Đợt 1 C1 — proves the middleware is actually wired into the real
/// pipeline, not merely unit-testable. A 403 produced by the genuine
/// authorization middleware (which short-circuits before any controller
/// runs) must still be counted, which is the reason
/// RequestObservabilityMiddleware sits first in Program.cs.
/// </summary>
public sealed class RequestObservabilityEndToEndTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public RequestObservabilityEndToEndTests(MesApiFactory fx) => _fx = fx;

    private static (MeterListener Listener, List<long> Values) ListenTo(string instrumentName)
    {
        var values = new List<long>();
        var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == MesTelemetry.SourceName && inst.Name == instrumentName)
                    l.EnableMeasurementEvents(inst);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, m, _, _) =>
        {
            lock (values) values.Add(m);
        });
        listener.Start();
        return (listener, values);
    }

    [Fact]
    public async Task Real_403_from_the_authorization_middleware_is_counted()
    {
        var (listener, values) = ListenTo("mes.rbac_denials");
        using var _ = listener;

        // AdminOnly policy; Operator is refused. The request never reaches
        // a controller — only a middleware sitting ahead of UseAuthorization
        // can observe it.
        var name = "obs-op-" + Guid.NewGuid().ToString("N")[..6];
        await _fx.SeedUserAsync(name, "Pa55w.rd!", UserRole.Operator);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, name, "Pa55w.rd!");

        var before = values.Count;
        var resp = await client.GetAsync("/api/v2/system-log?pageSize=1");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.True(values.Count > before,
            "HTTP 403 did not increment mes.rbac_denials — middleware is not wired into the pipeline.");
    }
}
