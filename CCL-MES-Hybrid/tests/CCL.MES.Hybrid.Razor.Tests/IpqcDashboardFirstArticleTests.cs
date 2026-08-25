using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.IpqcReview;
using CCL.MES.Shared.ReasonCodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// h-3 — IPQC first-article UI. Covers the MATERIAL (SYSTEM) reconciliation
/// panel (matched vs divergent → banner), the Engineer waiver RBAC-by-omission
/// (hidden for QC/Operator, shown for Engineer/Supervisor/Admin), the 3-tab
/// stepper grouped by CheckType (+ "Other" fallback), the MeasuredValue input,
/// and the 409 → refetch + 422 collapse paths on the material endpoints.
///
/// Rule 7.3 wire-mirror: each write probe pairs with an integration test in
/// WoIpqcMaterialControllerTests hitting the SAME URL via TestServer.
/// </summary>
public sealed class IpqcDashboardFirstArticleTests : TestContext
{
    private readonly RecordingApi _api = new();
    private readonly StubAuthSession _session = new();

    public IpqcDashboardFirstArticleTests()
    {
        Services.AddSingleton<ICclApiClient>(_api);
        _session.SetUser("qc-user", "QC");
        Services.AddSingleton<IAuthSession>(_session);
        Services.AddI18n();
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("qc-user");
    }

    // ── View builders ───────────────────────────────────────────────

    private static IpqcView ViewWithFaTabs() => new()
    {
        WoId = 77, WoNo = "WO-FA-1", MesPhase = "IPQC_WAIT", ETag = "v1",
        ResolvedLines = "LABEL",
        Items = new[]
        {
            new IpqcViewItem { ItemKey = "VIS-1", ProcessLine = "LABEL", GroupLabel = "A", Label = "Ngoại quan in",
                CheckType = "Visual", Status = "Pending" },
            new IpqcViewItem { ItemKey = "DIM-1", ProcessLine = "LABEL", GroupLabel = "B", Label = "Kích thước nhãn",
                CheckType = "Measure", Status = "Pending", MeasuredValue = "83.5" },
            new IpqcViewItem { ItemKey = "FUN-1", ProcessLine = "LABEL", GroupLabel = "C", Label = "Bám dính",
                CheckType = "Functional", Status = "Pending" },
            new IpqcViewItem { ItemKey = "OTH-1", ProcessLine = "LABEL", GroupLabel = "D", Label = "Chưa phân loại",
                CheckType = null, Status = "Pending" },
        },
    };

    private static IpqcMaterialSystemView MatchedMaterial() => new()
    {
        WoId = 77, WoNo = "WO-FA-1", MesPhase = "IPQC_WAIT", ETag = "v1",
        AllResolved = true, AnyPendingWaiver = false, AnyRejected = false,
        Rows = new[]
        {
            new IpqcMaterialRow { BomLineIdx = 0, MaterialCode = "MAT-A", MaterialDescription = "Giấy couche",
                SourceIqcReceiptNo = "IQC-100", ActualAtMachine = "IQC-100",
                IsDivergent = false, DivergenceKind = "None", Status = "Pending",
                DivergenceApprovalStatus = "NotRequired" },
        },
    };

    private static IpqcMaterialSystemView DivergentMaterial() => new()
    {
        WoId = 77, WoNo = "WO-FA-1", MesPhase = "IPQC_WAIT", ETag = "v1",
        AllResolved = false, AnyPendingWaiver = true, AnyRejected = false,
        Rows = new[]
        {
            new IpqcMaterialRow { BomLineIdx = 0, MaterialCode = "MAT-A", MaterialDescription = "Giấy couche",
                SourceIqcReceiptNo = "IQC-100", ActualAtMachine = "IQC-999",
                IsDivergent = true, DivergenceKind = "LotMismatch", DivergenceFlags = 1,
                Status = "Ok", DivergenceApprovalStatus = "PendingEngineer" },
        },
    };

    private static List<ReasonCodeOption> Scraps() => new()
    {
        new() { Code = "SC-MAT-LOT", LabelEn = "Wrong lot", LabelVi = "Sai lô", Kind = "Scrap", Sort = 1 },
    };

    // ── MATERIAL (SYSTEM) panel ─────────────────────────────────────

    [Fact]
    public void Matched_material_renders_panel_grid_without_divergent_banner()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithFaTabs());
        _api.IpqcMaterialSystemImpl = (_, _) => Task.FromResult(MatchedMaterial());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-material-panel']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-material-row-0']"));
            // No divergent banner + no divergence tag on the matched row.
            Assert.Empty(cut.FindAll("[data-testid='ipqc-material-divergent-banner']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-material-row-0-divtag']"));
        });
    }

    [Fact]
    public void Divergent_material_shows_red_banner_and_divergence_tag()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithFaTabs());
        _api.IpqcMaterialSystemImpl = (_, _) => Task.FromResult(DivergentMaterial());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-material-divergent-banner']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-material-row-0-divtag']"));
        });
    }

    [Fact]
    public void Material_ok_calls_put_material_system_endpoint()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithFaTabs());
        _api.IpqcMaterialSystemImpl = (_, _) => Task.FromResult(MatchedMaterial());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-material-row-0-ok']")));
        cut.Find("[data-testid='ipqc-material-row-0-ok']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(_api.PutIpqcMaterialSystemCalls);
            Assert.Equal(77L, call.Id);
            Assert.Equal(0, call.BomLineIdx);
            Assert.Equal("Ok", call.Req.Status);
        });
    }

    // ── Engineer waiver — RBAC-by-omission ──────────────────────────

    [Fact]
    public void Waiver_buttons_hidden_for_qc_role_but_pending_state_visible()
    {
        _session.SetUser("qc-user", "QC");
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithFaTabs());
        _api.IpqcMaterialSystemImpl = (_, _) => Task.FromResult(DivergentMaterial());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            // QC sees the pending-state note, NOT the approve/reject buttons.
            Assert.NotNull(cut.Find("[data-testid='ipqc-material-row-0-waiver-norole']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-material-row-0-waiver-approve']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-material-row-0-waiver-reject']"));
        });
    }

    [Fact]
    public void Waiver_buttons_shown_for_engineer_and_approve_calls_endpoint()
    {
        _session.SetUser("eng-user", "Engineer");
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithFaTabs());
        _api.IpqcMaterialSystemImpl = (_, _) => Task.FromResult(DivergentMaterial());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-material-row-0-waiver-approve']")));

        // Reason required — type it, then approve.
        cut.Find("[data-testid='ipqc-material-row-0-waiver-reason']").Input("Đã đối chiếu chứng từ, chấp nhận lô thay thế");
        cut.Find("[data-testid='ipqc-material-row-0-waiver-approve']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(_api.PostIpqcMaterialApproveDivergenceCalls);
            Assert.Equal(0, call.BomLineIdx);
            Assert.Equal("Approve", call.Req.Outcome);
            Assert.False(string.IsNullOrWhiteSpace(call.Req.Reason));
        });
    }

    // ── Stepper tabs ────────────────────────────────────────────────

    [Fact]
    public void Tabs_group_items_by_checktype_and_switching_filters_the_list()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithFaTabs());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            // 4 tabs incl. the "Other" fallback for the null CheckType item.
            Assert.NotNull(cut.Find("[data-testid='ipqc-tab-visual']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-tab-dimension']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-tab-function']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-tab-other']"));
            // Default active tab = Visual → only VIS-1 rendered.
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-VIS-1']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-item-DIM-1']"));
        });

        // Switch to Dimension → DIM-1 appears, VIS-1 gone.
        cut.Find("[data-testid='ipqc-tab-dimension']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-DIM-1']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-item-VIS-1']"));
        });
    }

    [Fact]
    public void Other_tab_absent_when_no_fallback_items()
    {
        var view = ViewWithFaTabs() with
        {
            Items = new[]
            {
                new IpqcViewItem { ItemKey = "VIS-1", ProcessLine = "LABEL", GroupLabel = "A", Label = "V",
                    CheckType = "Visual", Status = "Pending" },
            },
        };
        _api.IpqcViewImpl = (_, _) => Task.FromResult(view);

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-tab-visual']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-tab-other']"));
        });
    }

    // ── Measured value ──────────────────────────────────────────────

    [Fact]
    public void Measured_value_input_sent_with_item_put()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithFaTabs());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        // Go to the Dimension tab where DIM-1 lives.
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-tab-dimension']")));
        cut.Find("[data-testid='ipqc-tab-dimension']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-item-DIM-1-measured']")));
        cut.Find("[data-testid='ipqc-item-DIM-1-measured']").Input("0.94");
        cut.Find("[data-testid='ipqc-item-DIM-1-measured-save']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(_api.PutIpqcItemCalls);
            Assert.Equal("DIM-1", call.ItemKey);
            Assert.Equal("0.94", call.Req.MeasuredValue);
        });
    }

    // ── Error paths ─────────────────────────────────────────────────

    [Fact]
    public void Material_409_state_conflict_shows_banner_and_refetches()
    {
        var loads = 0;
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithFaTabs());
        _api.IpqcMaterialSystemImpl = (_, _) =>
        {
            loads++;
            return Task.FromResult(MatchedMaterial());
        };
        _api.PutIpqcMaterialSystemImpl = (_, _, _, _, _) =>
            Task.FromResult(new IpqcMaterialSetResponse
            {
                Ok = false, ErrorCode = "wo.state_conflict", ETag = "v2", MesPhase = "IPQC_WAIT",
            });

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-material-row-0-ok']")));
        var loadsBefore = loads;
        cut.Find("[data-testid='ipqc-material-row-0-ok']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-set-error']"));
            // The non-ok response triggers a material refetch (loads increased).
            Assert.True(loads > loadsBefore);
        });
    }

    [Fact]
    public void Material_422_not_divergent_collapses_to_set_error_banner()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithFaTabs());
        _api.IpqcMaterialSystemImpl = (_, _) => Task.FromResult(DivergentMaterial());
        _api.PostIpqcMaterialApproveDivergenceImpl = (_, _, _, _, _) =>
            Task.FromResult(new IpqcMaterialSetResponse
            {
                Ok = false, ErrorCode = "material.not_divergent", ETag = "v1", MesPhase = "IPQC_WAIT",
            });

        _session.SetUser("eng-user", "Engineer");

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-material-row-0-waiver-approve']")));
        cut.Find("[data-testid='ipqc-material-row-0-waiver-reason']").Input("lý do hợp lệ");
        cut.Find("[data-testid='ipqc-material-row-0-waiver-approve']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-set-error']")));
    }
}
