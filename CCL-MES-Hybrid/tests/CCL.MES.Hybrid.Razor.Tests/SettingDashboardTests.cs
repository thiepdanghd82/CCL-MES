using System.Net;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.RunningSurface;
using CCL.MES.Shared.SettingChecks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P10.7g-3 — bUnit render tests for the API-driven <see cref="SettingDashboard"/>.
///
/// The dashboard now reads two server views: RunningSurfaceView (timer / phase /
/// HasPrint/HasCut) + SettingChecksView (per-item persisted checklist). Every
/// OK/NG/applicable/defect toggle PUTs to /setting-checks/{itemKey}; two add-new
/// affordances (F3 defect dropdown "＋ Thêm mới…" + F4 "＋ Thêm hạng mục") POST
/// to /setting-checks/defect and /setting-checks/item.
///
/// Rule 4: every &lt;input&gt;/&lt;button&gt;/&lt;select&gt; here is plain HTML.
/// Rule 7.3 wire-mirror: server integration probes live in
/// SettingChecksControllerTests.
/// </summary>
public sealed class SettingDashboardTests : TestContext
{
    private readonly StubAuthSession _session = new();

    public SettingDashboardTests()
    {
        var api = new RecordingApi();
        Services.AddSingleton<ICclApiClient>(api);
        _session.SetUser("eng-1", "Engineer");
        Services.AddSingleton<IAuthSession>(_session);
        Services.AddI18n();
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    // ── View builders ──────────────────────────────────────────────

    private static RunningSurfaceView RsView(
        string etag = "rs==",
        DateTime? startAt = null,
        string phase = "SETTING",
        bool hasPrint = true,
        bool hasCut = true) => new()
    {
        WoId = 42,
        WoNo = "WO-26-3701",
        MesPhase = phase,
        ETag = etag,
        TargetQty = 1000,
        SettingStartAt = startAt,
        HasPrintProcess = hasPrint,
        HasCutProcess = hasCut,
    };

    private static SettingCheckItemView Item(
        string key, string kind, string label,
        string status = "Pending", bool applicable = true,
        string? defect = null, int sort = 0,
        params (string code, string vi, string en)[] defects) => new()
    {
        ItemKey = key,
        ProcessKind = kind,
        Label = label,
        Standard = label + " standard",
        Status = status,
        Applicable = applicable,
        DefectCode = defect,
        Sort = sort,
        DefectOptions = defects
            .Select((d, i) => new SettingDefectOptionView { DefectCode = d.code, LabelVi = d.vi, LabelEn = d.en, Sort = i })
            .ToList(),
    };

    private static SettingChecksView ChecksView(
        string etag = "chk==",
        bool ready = false,
        bool hasPrint = true,
        bool hasCut = true,
        IEnumerable<SettingCheckItemView>? items = null)
    {
        var list = items?.ToList() ?? DefaultItems(hasPrint, hasCut);
        return new SettingChecksView
        {
            WoId = 42,
            WoNo = "WO-26-3701",
            MesPhase = "SETTING",
            ETag = etag,
            HasPrint = hasPrint,
            HasCut = hasCut,
            Ready = ready,
            Items = list,
        };
    }

    // A compact but realistic materialised set: 2 print + 2 cut items, one with
    // a defect drop-list so the F3 dropdown has content.
    private static List<SettingCheckItemView> DefaultItems(bool hasPrint, bool hasCut)
    {
        var list = new List<SettingCheckItemView>();
        if (hasPrint)
        {
            list.Add(Item("print-0", "Print", "Bản in / khuôn", sort: 0,
                defects: new[] { ("PL-VER", "Sai phiên bản bản in", "Wrong plate revision"),
                                 ("PL-WEAR", "Mòn / xước bản", "Worn plate") }));
            list.Add(Item("print-1", "Print", "Vật tư in", sort: 10));
        }
        if (hasCut)
        {
            list.Add(Item("cut-0", "Cut", "Khuôn cắt / dao", sort: 0,
                defects: new[] { ("DI-VER", "Sai mã khuôn", "Wrong die code") }));
            list.Add(Item("cut-1", "Cut", "Lắp dao đúng khổ", sort: 10));
        }
        return list;
    }

    private static void SetViews(RecordingApi api, RunningSurfaceView? rs = null, SettingChecksView? chk = null)
    {
        var rsv = rs ?? RsView(startAt: DateTime.UtcNow.AddMinutes(-2));
        var chkv = chk ?? ChecksView();
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(rsv);
        api.SettingChecksViewImpl = (_, _) => Task.FromResult(chkv);
    }

    // ── Initial render ─────────────────────────────────────────────

    [Fact]
    public void Initial_render_shows_view_and_fetches_both_surfaces()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api);

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='setting-timer']"));
            Assert.NotNull(cut.Find("[data-testid='setting-checklist']"));
        });
        Assert.Single(api.RunningSurfaceViewCalls);
        Assert.Single(api.SettingChecksViewCalls);
        Assert.Equal(42L, api.SettingChecksViewCalls[0]);
    }

    [Fact]
    public void Initial_load_failure_shows_localised_error_banner()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.RunningSurfaceViewImpl = (_, _) => Task.FromException<RunningSurfaceView>(
            new ApiException((int)HttpStatusCode.NotFound,
                new ApiError { Code = "wo.not_found", MessageEn = "no wo" }));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='setting-initial-error']");
            Assert.Contains("WO not found on the server.", banner.TextContent);
        });
    }

    [Fact]
    public void Invalid_phase_banner_renders_when_phase_is_not_SETTING()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(RsView(phase: "RUNNING"));
        api.SettingChecksViewImpl = (_, _) => Task.FromResult(ChecksView());

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='setting-invalid-phase']");
            Assert.Contains("Lệnh SX không ở bước SETTING", banner.TextContent);
        });
        // Checks view must NOT be fetched for a non-SETTING WO.
        Assert.Empty(api.SettingChecksViewCalls);
    }

    // ── Entry stamp (closes 7c-2 gap) ──────────────────────────────

    [Fact]
    public void SettingStartAt_null_fires_setting_enter_with_If_Match()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var callIdx = 0;
        api.RunningSurfaceViewImpl = (_, _) =>
        {
            callIdx++;
            return Task.FromResult(callIdx == 1
                ? RsView(etag: "v1", startAt: null)
                : RsView(etag: "v2", startAt: DateTime.UtcNow));
        };
        api.SettingChecksViewImpl = (_, _) => Task.FromResult(ChecksView());
        api.SettingEnterImpl = (_, _, _) => Task.FromResult(new RunningSurfaceSetResponse
        {
            Ok = true, ETag = "v2", MesPhase = "SETTING",
        });

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.SettingEnterCalls);
            Assert.Equal("v1", api.SettingEnterCalls[0].ETag);
        });
    }

    [Fact]
    public void SettingStartAt_already_stamped_skips_setting_enter_call()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api, RsView(startAt: DateTime.UtcNow.AddMinutes(-3)));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-timer']")));
        Assert.Empty(api.SettingEnterCalls);
    }

    // ── Renders from the server view (not RAM) ─────────────────────

    [Fact]
    public void Rows_render_from_the_server_checks_view()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api);

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-checklist']")));
        // 2 print rows from the view.
        Assert.Equal(2, cut.FindAll("[data-testid^='setting-row-print-']").Count);
        Assert.Contains("Bản in", cut.Find("[data-testid='setting-row-print-0']").TextContent);

        // Switch to Cut → the view's cut rows.
        cut.Find("[data-testid='setting-tab-cut']").Click();
        Assert.Equal(2, cut.FindAll("[data-testid^='setting-row-cut-']").Count);
        Assert.Contains("Khuôn cắt", cut.Find("[data-testid='setting-row-cut-0']").TextContent);
    }

    // ── PUT set-item on OK ─────────────────────────────────────────
    // Wire-mirror: SettingChecksControllerTests PUT {itemKey} status=Ok.

    [Fact]
    public void Tapping_OK_PUTs_set_item_with_If_Match_from_checks_etag()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api, chk: ChecksView(etag: "chk-v1"));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-item-print-0-ok']")));

        cut.Find("[data-testid='setting-item-print-0-ok']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PutSettingItemCalls);
            Assert.Equal("chk-v1", api.PutSettingItemCalls[0].ETag);
            Assert.Equal("print-0", api.PutSettingItemCalls[0].ItemKey);
            Assert.Equal("Ok", api.PutSettingItemCalls[0].Req.Status);
        });
    }

    // ── L54 regression — NG là 2 bước: tap NG "arm" (KHÔNG PUT vì server bắt
    //    buộc defect khi NG) → chọn defect mới PUT. Tránh 422 "no error code". ─

    [Fact]
    public void Tapping_NG_arms_locally_without_PUT_and_reveals_defect_dropdown()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api, chk: ChecksView(etag: "chk-v1"));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-item-print-0-ng']")));

        // No defect dropdown until NG is armed.
        Assert.Empty(cut.FindAll("[data-testid='setting-defect-print-0']"));

        cut.Find("[data-testid='setting-item-print-0-ng']").Click();

        cut.WaitForAssertion(() =>
        {
            // Dropdown revealed…
            Assert.NotNull(cut.Find("[data-testid='setting-defect-print-0']"));
            // …but NOTHING persisted yet (server would 422 on NG-without-defect).
            Assert.Empty(api.PutSettingItemCalls);
        });
    }

    [Fact]
    public void Picking_defect_after_NG_PUTs_status_Ng_with_defect()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api, chk: ChecksView(etag: "chk-v1"));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-item-print-0-ng']")));

        cut.Find("[data-testid='setting-item-print-0-ng']").Click();
        cut.Find("[data-testid='setting-defect-print-0']").Change("PL-VER");

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PutSettingItemCalls);
            Assert.Equal("Ng", api.PutSettingItemCalls[0].Req.Status);
            Assert.Equal("PL-VER", api.PutSettingItemCalls[0].Req.DefectCode);
        });
    }

    [Fact]
    public void Set_item_409_conflict_renders_banner_and_reloads()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api);
        api.PutSettingItemImpl = (_, _, _, _, _) => Task.FromResult(new SettingChecksSetResponse
        {
            Ok = false, ErrorCode = "wo.state_conflict", ETag = "chk-v2", MesPhase = "SETTING",
        });

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-item-print-0-ok']")));

        cut.Find("[data-testid='setting-item-print-0-ok']").Click();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='setting-set-error']");
            Assert.Contains("Another operation has already updated this WO", banner.TextContent);
        });
        // Optimistic-revert: reloaded (initial 1 + reload after 409 = 2).
        Assert.Equal(2, api.SettingChecksViewCalls.Count);
    }

    // ── F1 — applicability excluded from gate ──────────────────────

    [Fact]
    public void Unchecking_applicability_PUTs_applicable_false()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api);

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-apply-print-0']")));

        cut.Find("[data-testid='setting-apply-print-0']").Change(false);

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PutSettingItemCalls);
            Assert.False(api.PutSettingItemCalls[0].Req.Applicable);
        });
    }

    [Fact]
    public void NA_item_reads_result_na_from_the_view()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var items = new[]
        {
            Item("print-0", "Print", "Bản in / khuôn", applicable: false, sort: 0),
            Item("print-1", "Print", "Vật tư in", status: "Ok", sort: 10),
        };
        SetViews(api, chk: ChecksView(hasCut: false, items: items));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() =>
            Assert.Contains("Không áp dụng",
                cut.Find("[data-testid='setting-result-print-0']").TextContent));
    }

    // ── F2 — Result column from server status ──────────────────────

    [Fact]
    public void Result_column_reflects_server_ok_and_ng()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var items = new[]
        {
            Item("print-0", "Print", "Bản in / khuôn", status: "Ok", sort: 0),
            Item("print-1", "Print", "Vật tư in", status: "Ng", sort: 10),
        };
        SetViews(api, chk: ChecksView(hasCut: false, items: items));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Đạt", cut.Find("[data-testid='setting-result-print-0']").TextContent);
            Assert.Contains("NG", cut.Find("[data-testid='setting-result-print-1']").TextContent);
        });
    }

    // ── F3 — per-item defect dropdown from item.DefectOptions ──────

    [Fact]
    public void NG_row_shows_defect_dropdown_from_that_items_options()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var items = new[]
        {
            Item("print-0", "Print", "Bản in / khuôn", status: "Ng", sort: 0,
                defects: new[] { ("PL-VER", "Sai phiên bản bản in", "Wrong plate revision"),
                                 ("PL-WEAR", "Mòn / xước bản", "Worn plate") }),
        };
        SetViews(api, chk: ChecksView(hasCut: false, items: items));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-defect-print-0']")));

        var sel = cut.Find("[data-testid='setting-defect-print-0']");
        // placeholder + 2 defects + "＋ Thêm mới…" (Engineer role in ctor).
        Assert.Equal(4, sel.QuerySelectorAll("option").Length);
        Assert.Contains("Sai phiên bản bản in", sel.TextContent);
    }

    [Fact]
    public void Choosing_a_defect_PUTs_set_item_ng_with_that_code()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var items = new[]
        {
            Item("print-0", "Print", "Bản in / khuôn", status: "Ng", sort: 0,
                defects: new[] { ("PL-VER", "Sai phiên bản bản in", "Wrong plate revision") }),
        };
        SetViews(api, chk: ChecksView(etag: "chk-v1", hasCut: false, items: items));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-defect-print-0']")));

        cut.Find("[data-testid='setting-defect-print-0']").Change("PL-VER");

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PutSettingItemCalls);
            Assert.Equal("PL-VER", api.PutSettingItemCalls[0].Req.DefectCode);
            Assert.Equal("Ng", api.PutSettingItemCalls[0].Req.Status);
        });
    }

    // ── F3 add-new — "＋ Thêm mới…" opens inline form + POSTs defect ─
    // Wire-mirror: SettingChecksControllerTests POST /setting-checks/defect.

    [Fact]
    public void AddNew_defect_opens_inline_form_and_posts()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var items = new[]
        {
            Item("print-0", "Print", "Bản in / khuôn", status: "Ng", sort: 0,
                defects: new[] { ("PL-VER", "Sai phiên bản bản in", "Wrong plate revision") }),
        };
        SetViews(api, chk: ChecksView(etag: "chk-v1", hasCut: false, items: items));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-defect-print-0-addnew']")));

        // Selecting the sentinel option opens the inline form.
        cut.Find("[data-testid='setting-defect-print-0']").Change("__add_new__");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-defect-print-0-addnew-form']")));
        // No PUT fired for the sentinel.
        Assert.Empty(api.PutSettingItemCalls);

        cut.Find("[data-testid='setting-defect-print-0-addnew-code']").Input("PL-CUSTOM");
        cut.Find("[data-testid='setting-defect-print-0-addnew-vi']").Input("Lỗi tuỳ chỉnh");
        cut.Find("[data-testid='setting-defect-print-0-addnew-en']").Input("Custom defect");
        cut.Find("[data-testid='setting-defect-print-0-addnew-save']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PostSettingDefectCalls);
            var req = api.PostSettingDefectCalls[0].Req;
            Assert.Equal("print-0", req.ItemId);
            Assert.Equal("PL-CUSTOM", req.DefectCode);
            Assert.Equal("Lỗi tuỳ chỉnh", req.LabelVi);
            Assert.Equal("Custom defect", req.LabelEn);
        });
    }

    [Fact]
    public void AddNew_defect_option_hidden_for_operator_role()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        _session.SetUser("op-1", "Operator");
        var items = new[]
        {
            Item("print-0", "Print", "Bản in / khuôn", status: "Ng", sort: 0,
                defects: new[] { ("PL-VER", "Sai phiên bản bản in", "Wrong plate revision") }),
        };
        SetViews(api, chk: ChecksView(hasCut: false, items: items));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-defect-print-0']")));

        // RBAC-by-omission: no "＋ Thêm mới…" option for an Operator.
        Assert.Empty(cut.FindAll("[data-testid='setting-defect-print-0-addnew']"));
    }

    // ── F4 add-row — "＋ Thêm hạng mục" opens form + POSTs item ─────
    // Wire-mirror: SettingChecksControllerTests POST /setting-checks/item.

    [Fact]
    public void AddRow_opens_inline_form_and_posts_item_for_active_tab()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api, chk: ChecksView(etag: "chk-v1"));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-print-addrow']")));

        cut.Find("[data-testid='setting-print-addrow']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-print-addrow-form']")));

        cut.Find("[data-testid='setting-print-addrow-name']").Input("Hạng mục mới");
        cut.Find("[data-testid='setting-print-addrow-standard']").Input("Tiêu chuẩn mới");
        cut.Find("[data-testid='setting-print-addrow-save']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PostSettingItemCalls);
            var req = api.PostSettingItemCalls[0].Req;
            Assert.Equal("Print", req.ProcessKind);
            Assert.Equal("Hạng mục mới", req.Label);
            Assert.Equal("Tiêu chuẩn mới", req.Standard);
            Assert.Equal("chk-v1", api.PostSettingItemCalls[0].ETag);
        });
    }

    [Fact]
    public void AddRow_on_cut_tab_posts_cut_process_kind()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api);

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-tab-cut']")));
        cut.Find("[data-testid='setting-tab-cut']").Click();

        cut.Find("[data-testid='setting-cut-addrow']").Click();
        cut.Find("[data-testid='setting-cut-addrow-name']").Input("Dao mới");
        cut.Find("[data-testid='setting-cut-addrow-save']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PostSettingItemCalls);
            Assert.Equal("Cut", api.PostSettingItemCalls[0].Req.ProcessKind);
        });
    }

    // ── Advance guard driven by server rollup Ready ────────────────

    [Fact]
    public void Done_button_disabled_when_server_rollup_not_ready()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api, chk: ChecksView(ready: false));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("[data-testid='setting-done-btn']").HasAttribute("disabled"));
            Assert.NotNull(cut.Find("[data-testid='setting-action-hint']"));
        });
    }

    [Fact]
    public void Done_button_enabled_when_server_rollup_ready()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api, chk: ChecksView(ready: true));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='setting-done-btn']").HasAttribute("disabled")));
    }

    [Fact]
    public void Setting_done_click_posts_with_running_surface_etag()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api, RsView(etag: "rs-v1", startAt: DateTime.UtcNow.AddMinutes(-5)),
                 ChecksView(ready: true));
        api.SettingDoneImpl = (_, _, _) => Task.FromResult(new RunningSurfaceSetResponse
        {
            Ok = true, ETag = "rs-v2", MesPhase = "IPQC_WAIT",
        });

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='setting-done-btn']").HasAttribute("disabled")));
        cut.Find("[data-testid='setting-done-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.SettingDoneCalls);
            Assert.Equal("rs-v1", api.SettingDoneCalls[0].ETag);
        });
    }

    [Fact]
    public void Setting_done_incomplete_422_renders_localised_banner()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api, chk: ChecksView(ready: true));
        api.SettingDoneImpl = (_, _, _) => Task.FromException<RunningSurfaceSetResponse>(
            new ApiException((int)HttpStatusCode.UnprocessableEntity,
                new ApiError { Code = "setting.incomplete", MessageEn = "not ok" }));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='setting-done-btn']").HasAttribute("disabled")));
        cut.Find("[data-testid='setting-done-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='setting-set-error']");
            Assert.Contains("Chưa đủ điều kiện hoàn tất", banner.TextContent);
        });
    }

    // ── L21 auto-refresh ───────────────────────────────────────────

    [Fact]
    public void Setting_done_success_invokes_OnPhaseChanged()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api, chk: ChecksView(ready: true));
        api.SettingDoneImpl = (_, _, _) => Task.FromResult(new RunningSurfaceSetResponse
        {
            Ok = true, ETag = "rs-v2", MesPhase = "IPQC_WAIT",
        });

        var phaseChangedCount = 0;
        var cut = RenderComponent<SettingDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.OnPhaseChanged, EventCallback.Factory.Create(this, () => phaseChangedCount++)));

        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='setting-done-btn']").HasAttribute("disabled")));
        cut.Find("[data-testid='setting-done-btn']").Click();

        cut.WaitForAssertion(() => Assert.Equal(1, phaseChangedCount));
    }

    [Fact]
    public void Setting_done_409_does_NOT_invoke_OnPhaseChanged()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api, chk: ChecksView(ready: true));
        api.SettingDoneImpl = (_, _, _) => Task.FromResult(new RunningSurfaceSetResponse
        {
            Ok = false, ErrorCode = "wo.state_conflict", ETag = "rs-v2", MesPhase = "SETTING",
        });

        var phaseChangedCount = 0;
        var cut = RenderComponent<SettingDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.OnPhaseChanged, EventCallback.Factory.Create(this, () => phaseChangedCount++)));

        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='setting-done-btn']").HasAttribute("disabled")));
        cut.Find("[data-testid='setting-done-btn']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-set-error']")));
        Assert.Equal(0, phaseChangedCount);
    }

    // ── Routing-driven tab visibility (kept from 7f) ───────────────

    [Fact]
    public void Print_only_WO_hides_Cut_tab()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api, RsView(startAt: DateTime.UtcNow.AddMinutes(-1), hasCut: false),
                 ChecksView(hasCut: false));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-checklist']")));
        Assert.Empty(cut.FindAll("[data-testid='setting-tab-cut']"));
        Assert.NotNull(cut.Find("[data-testid='setting-single-process']"));
        Assert.Empty(cut.FindAll("[data-testid^='setting-row-cut-']"));
    }

    [Fact]
    public void Cut_only_WO_snaps_active_tab_to_cut()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api, RsView(startAt: DateTime.UtcNow.AddMinutes(-1), hasPrint: false),
                 ChecksView(hasPrint: false));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-checklist']")));
        Assert.Empty(cut.FindAll("[data-testid='setting-tab-print']"));
        Assert.Equal(2, cut.FindAll("[data-testid^='setting-row-cut-']").Count);
        Assert.Empty(cut.FindAll("[data-testid^='setting-row-print-']"));
    }

    [Fact]
    public void Both_processes_render_the_switcher()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api);

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-subtabs']")));
        Assert.NotNull(cut.Find("[data-testid='setting-tab-print']"));
        Assert.NotNull(cut.Find("[data-testid='setting-tab-cut']"));
    }

    [Fact]
    public void Setting_rows_use_shared_ConfirmToggle()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        SetViews(api);

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-item-print-0-confirm']")));
        var toggle = cut.Find("[data-testid='setting-item-print-0-confirm']");
        Assert.Contains("confirm-toggle", toggle.GetAttribute("class"));
    }
}
