using System.Net;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Prepress;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P10.7b-3 — bUnit render tests for <see cref="PrepressDashboard"/>.
///
/// Rule 7.3: every wire-path probe asserted here is mirrored by an
/// integration test that hits the SAME endpoint via TestServer in
/// <c>PrepressControllerTests</c>. The wire-mirror trail is called
/// out per fixture so a future refactor can't break the contract
/// silently.
/// </summary>
public sealed class PrepressDashboardTests : TestContext
{
    public PrepressDashboardTests()
    {
        var api = new RecordingApi();
        Services.AddSingleton<ICclApiClient>(api);
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    private static PrepressView SampleView(string etag = "abc==", bool ready = false,
        string mesPhase = "PREPRESS",
        IReadOnlyList<PrepressMaterialRow>? materials = null,
        PrepressPlateRow? plate = null,
        PrepressCutterRow? cutter = null) => new()
    {
        WoId = 42,
        WoNo = "WO-26-3683",
        MesPhase = mesPhase,
        MaterialsReady = ready,
        ETag = etag,
        Materials = materials ?? new List<PrepressMaterialRow>
        {
            new() { Id = 1, BomLineIdx = 1, MaterialCode = "M-001", QtyRequired = 100, Uom = "kg", Status = "Pending" },
            new() { Id = 2, BomLineIdx = 2, MaterialCode = "M-002", QtyRequired = 50, Uom = "kg", Status = "Pending" },
        },
        PlateCheck = plate ?? new PrepressPlateRow { Id = 10, Status = "Pending" },
        CutterCheck = cutter ?? new PrepressCutterRow { Id = 20, Status = "Pending" },
    };

    // ── Render gate ─────────────────────────────────────────────────

    [Fact]
    public void Initial_render_shows_loading_then_view_when_api_resolves()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView());

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='prepress-dashboard']"));
            Assert.NotNull(cut.Find("[data-testid='prepress-rollup-pill']"));
        });
        Assert.Single(api.PrepressViewCalls);
        Assert.Equal(42L, api.PrepressViewCalls[0]);
    }

    [Fact]
    public void Initial_load_failure_shows_localised_error_banner()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromException<PrepressView>(
            new ApiException((int)HttpStatusCode.NotFound,
                new ApiError { Code = "wo.not_found", MessageEn = "no wo" }));

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='prepress-initial-error']");
            Assert.Contains("Không tìm thấy WO trên máy chủ.", banner.TextContent);
        });
    }

    // ── Rollup gating ───────────────────────────────────────────────

    [Fact]
    public void Advance_button_disabled_while_materials_not_ready()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView(ready: false));

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            var btn = cut.Find("[data-testid='prepress-advance-btn']");
            Assert.True(btn.HasAttribute("disabled"),
                "Materials not ready → Advance button must be disabled.");
            Assert.NotNull(cut.Find("[data-testid='prepress-advance-hint']"));
        });
    }

    [Fact]
    public void Advance_button_enabled_when_materials_ready()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView(ready: true));

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            var btn = cut.Find("[data-testid='prepress-advance-btn']");
            Assert.False(btn.HasAttribute("disabled"),
                "Materials ready → Advance button must be enabled.");
        });
    }

    [Fact]
    public void Advance_click_invokes_OnAdvanceRequested()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView(ready: true));

        var bubbled = 0;
        var cut = RenderComponent<PrepressDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.OnAdvanceRequested, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => { bubbled++; })));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='prepress-advance-btn']")));
        cut.Find("[data-testid='prepress-advance-btn']").Click();
        Assert.Equal(1, bubbled);
    }

    // ── Material set OK happy path ──────────────────────────────────
    // Wire-mirror: PrepressControllerTests.Put_material_happy_path_returns_200_with_bumped_etag_and_audit

    [Fact]
    public void Material_Ok_click_sends_If_Match_carrying_view_ETag_and_reloads_view()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var first = SampleView(etag: "v1");
        var second = SampleView(etag: "v2", materials: new List<PrepressMaterialRow>
        {
            new() { Id = 1, BomLineIdx = 1, MaterialCode = "M-001", QtyRequired = 100, Uom = "kg", Status = "Ok", CheckedBy = "alice" },
            new() { Id = 2, BomLineIdx = 2, MaterialCode = "M-002", QtyRequired = 50, Uom = "kg", Status = "Pending" },
        });
        var callIdx = 0;
        api.PrepressViewImpl = (_, _) =>
        {
            callIdx++;
            return Task.FromResult(callIdx == 1 ? first : second);
        };
        api.PutPrepressMaterialImpl = (_, _, _, _, _) => Task.FromResult(new PrepressSetResponse
        {
            Ok = true,
            MaterialsReady = false,
            ETag = "v2",
        });

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='material-row']").Count));

        var firstRow = cut.FindAll("[data-testid='material-row']")[0];
        firstRow.QuerySelector("[data-testid='btn-ok']")!.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PutPrepressMaterialCalls);
            Assert.Equal("v1", api.PutPrepressMaterialCalls[0].ETag);
            Assert.Equal("Ok", api.PutPrepressMaterialCalls[0].Req.Status);
            Assert.Equal(2, api.PrepressViewCalls.Count);
        });
    }

    // ── Material set Ng arming ──────────────────────────────────────

    [Fact]
    public void Material_Ng_arm_then_confirm_sends_reason_and_note()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView());
        api.PutPrepressMaterialImpl = (_, _, _, _, _) => Task.FromResult(new PrepressSetResponse
        {
            Ok = true,
            MaterialsReady = false,
            ETag = "v2",
        });

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='material-row']").Count));

        var firstRow = cut.FindAll("[data-testid='material-row']")[0];
        firstRow.QuerySelector("[data-testid='btn-ng-arm']")!.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("input[aria-label='Mã lý do NG']"));
            Assert.NotNull(cut.Find("input[aria-label='Ghi chú NG']"));
        });
        cut.FindAll("input[aria-label='Mã lý do NG']")[0].Input("SCRAP-FOIL-TEAR");
        cut.FindAll("input[aria-label='Ghi chú NG']")[0].Input("biên cuộn rách");

        firstRow = cut.FindAll("[data-testid='material-row']")[0];
        firstRow.QuerySelector("[data-testid='btn-ng-confirm']")!.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PutPrepressMaterialCalls);
            var (_, bomIdx, _, req) = api.PutPrepressMaterialCalls[0];
            Assert.Equal(1, bomIdx);
            Assert.Equal("Ng", req.Status);
            Assert.Equal("SCRAP-FOIL-TEAR", req.NgReasonCode);
            Assert.Equal("biên cuộn rách", req.NgNote);
        });
    }

    // ── 409 wo.state_conflict reload ────────────────────────────────
    // Wire-mirror: PrepressControllerTests.Concurrent_prepress_row_updates_N_equals_10_yield_consistent_rollup

    [Fact]
    public void State_conflict_response_shows_VN_banner_and_reloads_view()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var callIdx = 0;
        api.PrepressViewImpl = (_, _) =>
        {
            callIdx++;
            return Task.FromResult(SampleView(etag: callIdx == 1 ? "v1" : "v_fresh"));
        };
        api.PutPrepressMaterialImpl = (_, _, _, _, _) => Task.FromResult(new PrepressSetResponse
        {
            Ok = false,
            ErrorCode = "wo.state_conflict",
            ETag = "v_fresh",
            MaterialsReady = false,
        });

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='material-row']").Count));

        var firstRow = cut.FindAll("[data-testid='material-row']")[0];
        firstRow.QuerySelector("[data-testid='btn-ok']")!.Click();

        cut.WaitForAssertion(() =>
        {
            var err = cut.Find("[data-testid='prepress-set-error']");
            Assert.Contains("Một thao tác khác", err.TextContent);
            Assert.Equal(2, api.PrepressViewCalls.Count);
        });
    }

    // ── 422 invalid_phase collapses dashboard ───────────────────────
    // Wire-mirror: PrepressControllerTests.Put_material_outside_PREPRESS_phase_returns_422_invalid_phase

    [Fact]
    public void Invalid_phase_422_collapses_dashboard_to_VN_banner()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView(mesPhase: "SETTING"));

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='prepress-invalid-phase']");
            Assert.Contains("không ở giai đoạn PREPRESS", banner.TextContent);
            Assert.Empty(cut.FindAll("[data-testid='material-row']"));
            Assert.Empty(cut.FindAll("[data-testid='prepress-advance-btn']"));
        });
    }

    // ── Plate / Cutter wiring ───────────────────────────────────────
    // Wire-mirror: PrepressControllerTests.Put_plate_happy_path + Put_cutter_happy_path

    [Fact]
    public void Plate_Ok_click_routes_through_PlateCheck_component_to_API()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView());
        api.PutPrepressPlateImpl = (_, _, _, _) => Task.FromResult(new PrepressSetResponse
        {
            Ok = true, MaterialsReady = false, ETag = "v2",
        });

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='plate-btn-ok']")));

        cut.Find("[data-testid='plate-btn-ok']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PutPrepressPlateCalls);
            Assert.Equal("Ok", api.PutPrepressPlateCalls[0].Req.Status);
        });
    }

    [Fact]
    public void Cutter_Ok_click_routes_through_CutterCheck_component_to_API()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView());
        api.PutPrepressCutterImpl = (_, _, _, _) => Task.FromResult(new PrepressSetResponse
        {
            Ok = true, MaterialsReady = false, ETag = "v2",
        });

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='cutter-btn-ok']")));

        cut.Find("[data-testid='cutter-btn-ok']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PutPrepressCutterCalls);
            Assert.Equal("Ok", api.PutPrepressCutterCalls[0].Req.Status);
        });
    }

    // ── Progress pill ───────────────────────────────────────────────

    [Fact]
    public void Materials_progress_pill_counts_OK_rows()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView(materials: new List<PrepressMaterialRow>
        {
            new() { Id = 1, BomLineIdx = 1, MaterialCode = "M-001", QtyRequired = 100, Status = "Ok" },
            new() { Id = 2, BomLineIdx = 2, MaterialCode = "M-002", QtyRequired = 50, Status = "Pending" },
            new() { Id = 3, BomLineIdx = 3, MaterialCode = "M-003", QtyRequired = 25, Status = "Ng" },
        }));

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            var pill = cut.Find("[data-testid='materials-progress']");
            Assert.Contains("1 / 3 OK", pill.TextContent);
        });
    }

    // ── Empty BOM snapshot ──────────────────────────────────────────

    [Fact]
    public void Empty_materials_list_shows_BOM_empty_banner()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView(
            materials: new List<PrepressMaterialRow>()));

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='materials-empty']"));
        });
    }
}
