using System.Net;
using System.Net.Http.Json;
using System.Text;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Quality;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P13 bước 6 — HTTP mỏng cho khối NG/claim. Domain đã khoá ở IqcNgServiceTests;
/// đây chỉ wire: 201, list, 403 Operator, thiếu Idempotency-Key, không nhảy cóc.
/// </summary>
public sealed class IqcNgControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public IqcNgControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> ClientAsync(string user, string role)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", role);
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, "P@ss!1");
        return c;
    }

    private static HttpRequestMessage Post(string path, object body, bool idem = true, string? key = null)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(body);
        var r = new HttpRequestMessage(HttpMethod.Post, path)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (idem) r.Headers.TryAddWithoutValidation("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return r;
    }

    private static object Body() => new
    {
        detectedAt = new DateTime(2026, 3, 1),
        detectedStage = "Production",
        partNo = "30030146",
        supplierLotNo = "QT2502006",
        defectName = "Xước",
        ngAreaM2 = 12.5,
        ngRolls = 2,
    };

    [Fact]
    public async Task Create_happy_returns_201_and_lists_without_iqc_ticket()
    {
        var c = await ClientAsync("qc-ng-happy", UserRole.Qc);

        var resp = await c.SendAsync(Post("/api/v2/iqc/ng", Body()));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var created = (await resp.Content.ReadFromJsonAsync<IqcNgMutationResponse>())!;
        Assert.True(created.Id > 0);
        Assert.Equal("Open", created.Status);

        var list = await c.GetFromJsonAsync<IqcNgListResponse>("/api/v2/iqc/ng?partNo=30030146");
        Assert.Contains(list!.Items, x => x.Id == created.Id && x.IqcInspectionId is null && x.DetectedStage == "Production");
    }

    [Fact]
    public async Task Create_without_idempotency_key_returns_400()
    {
        var c = await ClientAsync("qc-ng-idem", UserRole.Qc);
        var resp = await c.SendAsync(Post("/api/v2/iqc/ng", Body(), idem: false));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("wo.idempotency_key_required", err!.Code);
    }

    [Fact]
    public async Task Operator_cannot_write_returns_403()
    {
        var c = await ClientAsync("op-ng-write", UserRole.Operator);
        var resp = await c.SendAsync(Post("/api/v2/iqc/ng", Body()));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Settle_from_open_returns_422()
    {
        var c = await ClientAsync("qc-ng-jump", UserRole.Qc);
        var created = (await (await c.SendAsync(Post("/api/v2/iqc/ng", Body())))
            .Content.ReadFromJsonAsync<IqcNgMutationResponse>())!;

        var resp = await c.SendAsync(Post($"/api/v2/iqc/ng/{created.Id}/settle",
            new { settlement = "Replacement" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("iqc.ng.invalid_transition", err!.Code);
    }

    [Fact]
    public async Task Close_without_reason_returns_422()
    {
        var c = await ClientAsync("qc-ng-close", UserRole.Qc);
        var created = (await (await c.SendAsync(Post("/api/v2/iqc/ng", Body())))
            .Content.ReadFromJsonAsync<IqcNgMutationResponse>())!;

        var resp = await c.SendAsync(Post($"/api/v2/iqc/ng/{created.Id}/close-no-claim",
            new { reason = "" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("iqc.ng.close_reason_required", err!.Code);
    }
}
