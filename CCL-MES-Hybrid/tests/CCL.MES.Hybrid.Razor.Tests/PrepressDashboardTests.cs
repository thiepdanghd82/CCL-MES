using System.Net;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Hybrid.Client.RecentScans;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Prepress;
using CCL.MES.Shared.ReasonCodes;
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
    // Scan dep (USB wedge "Scan materials" feature). Mutate _hwOptions
    // inside a test BEFORE rendering to enable the scan input; the default
    // (ScanEnabled=false) keeps every pre-existing fixture rendering exactly
    // as before.
    private readonly HardwareOptions _hwOptions = new();
    private readonly StubAuthSession _session = new();

    public PrepressDashboardTests()
    {
        var api = new RecordingApi();
        // Default Scrap reason list — 3 codes — so every fixture starts
        // with a non-empty picker. Empty-list scenario overrides below.
        api.ReasonCodesImpl = (_, _) => Task.FromResult<IReadOnlyList<ReasonCodeOption>>(SampleScrapReasons());
        _session.SetUser("op.demo", "Operator");   // default: cannot special-accept
        Services.AddSingleton<ICclApiClient>(api);
        Services.AddSingleton<IRecentScansService>(new InMemoryRecentScansService());
        Services.AddSingleton<IAuthSession>(_session);
        Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(_hwOptions));
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    // Simulate a Zebra DS2208 wedge scan: type the payload into the box, then
    // the scanner's Enter suffix submits.
    private static void WedgeScan(IRenderedComponent<PrepressDashboard> cut, string payload)
    {
        var input = cut.Find("[data-testid='prepress-scan-input']");
        input.Input(payload);
        input.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
    }

    private static IReadOnlyList<ReasonCodeOption> SampleScrapReasons() => new List<ReasonCodeOption>
    {
        new() { Code = "SC-COLOR",      LabelVi = "Lệch màu",                Kind = "Scrap", Sort = 10 },
        new() { Code = "SC-MAT-DAMAGE", LabelVi = "Vật tư hỏng / nhiễm bẩn", Kind = "Scrap", Sort = 50 },
        new() { Code = "SC-PLATE-WORN", LabelVi = "Bản mòn / không dùng được", Kind = "Scrap", Sort = 70 },
    };

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
            Assert.Contains("WO not found on the server.", banner.TextContent);
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
    public void Material_Ng_arm_then_pick_then_confirm_sends_chosen_code()
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

        // Picker renders + has the 3 seeded options + the placeholder.
        cut.WaitForAssertion(() =>
        {
            var picker = cut.Find("[data-testid='material-ng-reason-picker']");
            Assert.Equal(4, picker.QuerySelectorAll("option").Length);
        });

        // Submit-disabled gate: until a valid code is chosen, Lưu NG stays off.
        firstRow = cut.FindAll("[data-testid='material-row']")[0];
        var confirmBefore = firstRow.QuerySelector("[data-testid='btn-ng-confirm']")!;
        Assert.True(confirmBefore.HasAttribute("disabled"),
            "No code chosen → Lưu NG MUST be disabled (L17 chặn tái phát — submit-422 loop).");

        // Choose a valid Scrap code via the <select> + type a note.
        cut.Find("[data-testid='material-ng-reason-picker']").Change("SC-MAT-DAMAGE");
        cut.FindAll("input[aria-label='NG note']")[0].Input("biên cuộn rách");

        firstRow = cut.FindAll("[data-testid='material-row']")[0];
        var confirmAfter = firstRow.QuerySelector("[data-testid='btn-ng-confirm']")!;
        Assert.False(confirmAfter.HasAttribute("disabled"),
            "Valid code chosen → Lưu NG MUST be enabled.");

        confirmAfter.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PutPrepressMaterialCalls);
            var (_, bomIdx, _, req) = api.PutPrepressMaterialCalls[0];
            Assert.Equal(1, bomIdx);
            Assert.Equal("Ng", req.Status);
            Assert.Equal("SC-MAT-DAMAGE", req.NgReasonCode);
            Assert.Equal("biên cuộn rách", req.NgNote);
        });
    }

    // ── L17 regression — picker empty list ──────────────────────────
    // Wire-mirror: ReasonCodesControllerTests.L17_regression_Recovery_present_does_not_block_Scrap_listing

    [Fact]
    public void Empty_scrap_reasons_disables_NG_arm_and_shows_banner()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.ReasonCodesImpl = (_, _) => Task.FromResult<IReadOnlyList<ReasonCodeOption>>(Array.Empty<ReasonCodeOption>());
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView());

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='prepress-reasons-empty']"));
        });

        // Every NG arm button (material rows + plate + cutter) MUST be disabled
        // when the picker source is empty — operator can't submit a 422 loop.
        var armButtons = cut.FindAll("[data-testid='btn-ng-arm']");
        Assert.NotEmpty(armButtons);
        Assert.All(armButtons, b => Assert.True(b.HasAttribute("disabled"),
            "Empty scrap reasons → material NG-arm MUST be disabled."));

        Assert.True(cut.Find("[data-testid='plate-btn-ng-arm']").HasAttribute("disabled"),
            "Empty scrap reasons → plate NG-arm MUST be disabled.");
        Assert.True(cut.Find("[data-testid='cutter-btn-ng-arm']").HasAttribute("disabled"),
            "Empty scrap reasons → cutter NG-arm MUST be disabled.");
    }

    [Fact]
    public void Dashboard_fetches_Scrap_kind_from_reason_codes_endpoint_on_init()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView());

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='material-row']")));

        Assert.Single(api.ReasonCodesCalls);
        Assert.Equal("Scrap", api.ReasonCodesCalls[0]);
    }

    [Fact]
    public void Picker_options_render_Code_and_LabelVi_text()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView());

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='material-row']").Count));

        cut.FindAll("[data-testid='material-row']")[0]
           .QuerySelector("[data-testid='btn-ng-arm']")!.Click();

        cut.WaitForAssertion(() =>
        {
            var picker = cut.Find("[data-testid='material-ng-reason-picker']");
            var options = picker.QuerySelectorAll("option").Select(o => o.TextContent).ToList();
            Assert.Contains(options, t => t.Contains("SC-COLOR") && t.Contains("Lệch màu"));
            Assert.Contains(options, t => t.Contains("SC-MAT-DAMAGE") && t.Contains("Vật tư hỏng"));
        });
    }

    [Fact]
    public void Plate_NG_arm_disabled_when_reasons_empty_even_with_view_loaded()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.ReasonCodesImpl = (_, _) => Task.FromResult<IReadOnlyList<ReasonCodeOption>>(Array.Empty<ReasonCodeOption>());
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView());

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='plate-btn-ng-arm']")));

        var plateArm = cut.Find("[data-testid='plate-btn-ng-arm']");
        Assert.True(plateArm.HasAttribute("disabled"));
        Assert.Contains("NG code catalog is empty",
            plateArm.GetAttribute("title") ?? "");
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
            Assert.Contains("Another operation", err.TextContent);
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
            Assert.Contains("not in the PREPRESS phase", banner.TextContent);
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

    // ── Scan materials (USB wedge) ──────────────────────────────────

    [Fact]
    public void Scan_input_hidden_when_scan_disabled()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView());

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='prepress-dashboard']")));
        Assert.Empty(cut.FindAll("[data-testid='prepress-scan-input']"));
    }

    [Fact]
    public void Scan_input_visible_when_scan_enabled()
    {
        _hwOptions.ScanEnabled = true;
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView());

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='prepress-scan-input']")));
    }

    [Fact]
    public void Wedge_scan_of_known_material_records_it_ok()
    {
        _hwOptions.ScanEnabled = true;
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();

        var pending = new List<PrepressMaterialRow>
        {
            new() { Id = 1, BomLineIdx = 1, MaterialCode = "30031145", Status = "Pending" },
            new() { Id = 2, BomLineIdx = 2, MaterialCode = "30030532", Status = "Pending" },
        };
        var afterOk = new List<PrepressMaterialRow>
        {
            new() { Id = 1, BomLineIdx = 1, MaterialCode = "30031145", Status = "Ok" },
            new() { Id = 2, BomLineIdx = 2, MaterialCode = "30030532", Status = "Pending" },
        };
        var call = 0;
        api.PrepressViewImpl = (_, _) => Task.FromResult(
            call++ == 0 ? SampleView(materials: pending) : SampleView(etag: "new==", materials: afterOk));
        api.PutPrepressMaterialImpl = (_, _, _, _, _) =>
            Task.FromResult(new PrepressSetResponse { Ok = true, ETag = "new==" });

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='prepress-scan-input']")));

        // Real DS2208 label for part 30031145 (live scan).
        WedgeScan(cut, "30031145/80/(BU'488) / BU'-0112N (215mm x 1000M)");

        cut.WaitForAssertion(() =>
        {
            var put = Assert.Single(api.PutPrepressMaterialCalls);
            Assert.Equal(1, put.BomLineIdx);                 // the 30031145 line
            Assert.Equal("Ok", put.Req.Status);
            var status = cut.Find("[data-testid='prepress-scan-status']");
            Assert.Contains("recorded OK", status.TextContent);
        });
    }

    [Fact]
    public void Wedge_scan_of_unknown_material_shows_not_in_bom_and_does_not_put()
    {
        _hwOptions.ScanEnabled = true;
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        // SampleView default BOM = M-001 / M-002, so 99999999 is unknown.
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView());

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='prepress-scan-input']")));

        WedgeScan(cut, "99999999/1/(z) some desc");

        cut.WaitForAssertion(() =>
        {
            var status = cut.Find("[data-testid='prepress-scan-status']");
            Assert.Contains("not in this WO's BOM", status.TextContent);
        });
        Assert.Empty(api.PutPrepressMaterialCalls);
    }

    [Fact]
    public void Manual_part_scan_renders_dark_purple()
    {
        _hwOptions.ScanEnabled = true;
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView());

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='part-scan']")));

        var input = cut.FindAll("[data-testid='part-scan']")[0];
        input.Input("M-001");   // manual typing into the Part Scan cell

        cut.WaitForAssertion(() =>
        {
            var cell = cut.FindAll("[data-testid='part-scan']")[0];
            Assert.Contains("scan-manual", cell.GetAttribute("class"));
        });
    }

    [Fact]
    public void Manual_part_scan_matching_line_code_auto_records_ok()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var call = 0;
        api.PrepressViewImpl = (_, _) => Task.FromResult(
            call++ == 0 ? SampleView() : SampleView(etag: "new=="));
        api.PutPrepressMaterialImpl = (_, _, _, _, _) =>
            Task.FromResult(new PrepressSetResponse { Ok = true, ETag = "new==" });

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='part-scan']")));

        // Row 0 is M-001 (BomLineIdx 1). Typing its own code + commit auto-OKs.
        cut.FindAll("[data-testid='part-scan']")[0].Change("M-001");

        cut.WaitForAssertion(() =>
        {
            var put = Assert.Single(api.PutPrepressMaterialCalls);
            Assert.Equal(1, put.BomLineIdx);
            Assert.Equal("Ok", put.Req.Status);
        });
    }

    [Fact]
    public void Manual_part_scan_mismatch_does_not_ok_and_asks_for_special_accept()
    {
        _hwOptions.ScanEnabled = true;   // so the status line is rendered
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView());

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='part-scan']")));

        // Wrong code on row 0 (M-001) → must NOT auto-OK.
        cut.FindAll("[data-testid='part-scan']")[0].Change("WRONG-CODE");

        cut.WaitForAssertion(() =>
        {
            var status = cut.Find("[data-testid='prepress-scan-status']");
            Assert.Contains("Special Accept", status.TextContent);
        });
        Assert.Empty(api.PutPrepressMaterialCalls);
    }

    // ── Special accept (role-gated) ─────────────────────────────────

    private static void ArmSpecialAcceptWithReason(IRenderedComponent<PrepressDashboard> cut)
    {
        cut.FindAll("[data-testid='btn-special-accept-arm']")[0].Click();
        cut.Find("[data-testid='material-ng-reason-picker']").Change("SC-COLOR");
    }

    [Fact]
    public void Special_accept_button_hidden_for_operator()
    {
        _session.SetUser("op.demo", "Operator");
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView());

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='btn-ng-arm']")));

        // Visible in the default action row (no arming needed) — but not for Operator.
        Assert.Empty(cut.FindAll("[data-testid='btn-special-accept-arm']"));
    }

    [Fact]
    public void Special_accept_button_visible_for_engineer_in_default_row()
    {
        _session.SetUser("eng.demo", "Engineer");
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView());

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        // Shown right next to Mark NG, before any NG arming.
        cut.WaitForAssertion(() =>
            Assert.NotEmpty(cut.FindAll("[data-testid='btn-special-accept-arm']")));
    }

    [Fact]
    public void Special_accepted_row_shows_distinct_OK_special_accept_status()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var materials = new List<PrepressMaterialRow>
        {
            // Reloaded server state after a special-accept: OK + deviation kept.
            new() { Id = 1, BomLineIdx = 1, MaterialCode = "M-001", Status = "Ok",
                    NgReasonCode = "SC-COLOR", NgNote = "concession" },
            // A clean OK for contrast.
            new() { Id = 2, BomLineIdx = 2, MaterialCode = "M-002", Status = "Ok" },
        };
        api.PrepressViewImpl = (_, _) => Task.FromResult(SampleView(materials: materials));

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            var pills = cut.FindAll("[data-testid='status-pill']");
            Assert.Contains("Special Accept", pills[0].TextContent);          // deviation row
            Assert.DoesNotContain("Special Accept", pills[1].TextContent);    // clean OK
        });
    }

    [Fact]
    public void Engineer_special_accept_calls_api_with_reason()
    {
        _session.SetUser("eng.demo", "Engineer");
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var call = 0;
        api.PrepressViewImpl = (_, _) => Task.FromResult(
            call++ == 0 ? SampleView() : SampleView(etag: "new=="));
        api.SpecialAcceptMaterialImpl = (_, _, _, _, _) =>
            Task.FromResult(new PrepressSetResponse { Ok = true, ETag = "new==" });

        var cut = RenderComponent<PrepressDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='btn-special-accept-arm']")));
        ArmSpecialAcceptWithReason(cut);
        cut.Find("[data-testid='btn-special-accept']").Click();

        cut.WaitForAssertion(() =>
        {
            var sa = Assert.Single(api.SpecialAcceptMaterialCalls);
            Assert.Equal(1, sa.BomLineIdx);              // first row (M-001, BomLineIdx 1)
            Assert.Equal("SC-COLOR", sa.Req.NgReasonCode);
        });
    }
}
