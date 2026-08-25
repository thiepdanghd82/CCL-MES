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

    // 2026-08-25: TẦNG-2 now groups by GroupLabel ("4 tab con giống QC Library"),
    // NOT CheckType. GroupLabels below use the label line "A·Ngoại quan" … "D·Chức
    // năng"; tab key = the GroupLabel string. One item carries a null GroupLabel so
    // the "Khác" fallback tab is exercised.
    private static IpqcView ViewWithFaTabs() => new()
    {
        WoId = 77, WoNo = "WO-FA-1", MesPhase = "IPQC_WAIT", ETag = "v1",
        ResolvedLines = "LABEL",
        Items = new[]
        {
            new IpqcViewItem { ItemKey = "VIS-1", ProcessLine = "LABEL", GroupLabel = "A·Ngoại quan", Label = "Ngoại quan in",
                CheckType = "Visual", Status = "Pending" },
            new IpqcViewItem { ItemKey = "DIM-1", ProcessLine = "LABEL", GroupLabel = "B·Kích thước", Label = "Kích thước nhãn",
                CheckType = "Measure", Status = "Pending", MeasuredValue = "83.5" },
            new IpqcViewItem { ItemKey = "COL-1", ProcessLine = "LABEL", GroupLabel = "C·Màu sắc", Label = "ΔE màu",
                CheckType = "Measure", Status = "Pending" },
            new IpqcViewItem { ItemKey = "FUN-1", ProcessLine = "LABEL", GroupLabel = "D·Chức năng", Label = "Bám dính",
                CheckType = "Functional", Status = "Pending" },
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

    // ── TẦNG-2 GroupLabel tabs (2026-08-25) ─────────────────────────

    // Tab key = the GroupLabel string, so testids embed the label verbatim.
    private const string TabA = "ipqc-tab-A·Ngoại quan";
    private const string TabB = "ipqc-tab-B·Kích thước";
    private const string TabC = "ipqc-tab-C·Màu sắc";
    private const string TabD = "ipqc-tab-D·Chức năng";

    [Fact]
    public void Tabs_group_items_by_grouplabel_and_switching_filters_the_list()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithFaTabs());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            // 4 GroupLabel tabs (A/B/C/D) — one per distinct GroupLabel.
            Assert.NotNull(cut.Find($"[data-testid='{TabA}']"));
            Assert.NotNull(cut.Find($"[data-testid='{TabB}']"));
            Assert.NotNull(cut.Find($"[data-testid='{TabC}']"));
            Assert.NotNull(cut.Find($"[data-testid='{TabD}']"));
            // Tab label = the raw GroupLabel value (data, no i18n).
            Assert.Contains("A·Ngoại quan", cut.Find($"[data-testid='{TabA}'] .ipqc-tab-label").TextContent);
            // Default active tab = first (A) → only VIS-1 rendered.
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-VIS-1']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-item-DIM-1']"));
        });

        // Switch to B → DIM-1 appears, VIS-1 gone.
        cut.Find($"[data-testid='{TabB}']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-DIM-1']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-item-VIS-1']"));
        });
    }

    [Fact]
    public void Tab_count_is_dynamic_not_hardcoded_four()
    {
        // Two distinct GroupLabels + one null → 2 real tabs + "Khác" = 3 tabs.
        var view = ViewWithFaTabs() with
        {
            Items = new[]
            {
                new IpqcViewItem { ItemKey = "A1", ProcessLine = "LABEL", GroupLabel = "A·Ngoại quan", Label = "a1", Status = "Pending" },
                new IpqcViewItem { ItemKey = "B1", ProcessLine = "LABEL", GroupLabel = "B·Kích thước", Label = "b1", Status = "Pending" },
                new IpqcViewItem { ItemKey = "N1", ProcessLine = "LABEL", GroupLabel = null, Label = "n1", Status = "Pending" },
            },
        };
        _api.IpqcViewImpl = (_, _) => Task.FromResult(view);

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            var tabs = cut.FindAll("[data-testid='ipqc-tabs'] .ipqc-tab-chip");
            Assert.Equal(3, tabs.Count);
        });
    }

    [Fact]
    public void Null_grouplabel_item_lands_in_khac_tab()
    {
        var view = ViewWithFaTabs() with
        {
            Items = new[]
            {
                new IpqcViewItem { ItemKey = "A1", ProcessLine = "LABEL", GroupLabel = "A·Ngoại quan", Label = "a1", Status = "Pending" },
                new IpqcViewItem { ItemKey = "N1", ProcessLine = "LABEL", GroupLabel = "  ", Label = "unclassified", Status = "Pending" },
            },
        };
        _api.IpqcViewImpl = (_, _) => Task.FromResult(view);

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        // "Khác" tab present + labelled from i18n; N1 lives there, A1 does not.
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-tab-__khac__']")));
        Assert.Contains("Khác", cut.Find("[data-testid='ipqc-tab-__khac__'] .ipqc-tab-label").TextContent);
        cut.Find("[data-testid='ipqc-tab-__khac__']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-N1']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-item-A1']"));
        });
    }

    [Fact]
    public void Khac_tab_absent_when_all_items_have_grouplabel()
    {
        var view = ViewWithFaTabs() with
        {
            Items = new[]
            {
                new IpqcViewItem { ItemKey = "A1", ProcessLine = "LABEL", GroupLabel = "A·Ngoại quan", Label = "a1", Status = "Pending" },
            },
        };
        _api.IpqcViewImpl = (_, _) => Task.FromResult(view);

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find($"[data-testid='{TabA}']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-tab-__khac__']"));
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

        // Go to the B tab where DIM-1 lives.
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"[data-testid='{TabB}']")));
        cut.Find($"[data-testid='{TabB}']").Click();

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

    // ── # column + "Áp dụng" (applicable) checkbox (2026-08-25) ──────

    [Fact]
    public void Row_number_column_is_one_based_in_active_tab()
    {
        // Tab A has one item (VIS-1) → row number "1".
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithFaTabs());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
            Assert.Equal("1", cut.Find("[data-testid='ipqc-item-VIS-1-num']").TextContent.Trim()));
    }

    [Fact]
    public void Row_numbers_are_sequential_within_tab()
    {
        // Two items in the SAME GroupLabel → numbers 1, 2.
        var view = ViewWithFaTabs() with
        {
            Items = new[]
            {
                new IpqcViewItem { ItemKey = "A1", ProcessLine = "LABEL", GroupLabel = "A·Ngoại quan", Label = "a1", Status = "Pending" },
                new IpqcViewItem { ItemKey = "A2", ProcessLine = "LABEL", GroupLabel = "A·Ngoại quan", Label = "a2", Status = "Pending" },
            },
        };
        _api.IpqcViewImpl = (_, _) => Task.FromResult(view);

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("1", cut.Find("[data-testid='ipqc-item-A1-num']").TextContent.Trim());
            Assert.Equal("2", cut.Find("[data-testid='ipqc-item-A2-num']").TextContent.Trim());
        });
    }

    [Fact]
    public void Applicable_checkbox_defaults_checked()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithFaTabs());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            var box = (AngleSharp.Html.Dom.IHtmlInputElement)cut.Find("[data-testid='ipqc-item-VIS-1-applicable']");
            Assert.True(box.IsChecked);
        });
    }

    [Fact]
    public void Unchecking_applicable_calls_endpoint_and_greys_row_and_drops_total()
    {
        // Tab A holds 2 items → applicable-total starts at 2. On uncheck of A2
        // the server returns the item with Applicable=false so total drops to 1.
        var applicableA2 = true;
        IpqcView Build() => ViewWithFaTabs() with
        {
            Items = new[]
            {
                new IpqcViewItem { ItemKey = "A1", ProcessLine = "LABEL", GroupLabel = "A·Ngoại quan", Label = "a1", Status = "Pending" },
                new IpqcViewItem { ItemKey = "A2", ProcessLine = "LABEL", GroupLabel = "A·Ngoại quan", Label = "a2", Status = "Pending", Applicable = applicableA2 },
            },
        };
        _api.IpqcViewImpl = (_, _) => Task.FromResult(Build());
        _api.PutIpqcItemApplicableImpl = (_, etag, _, req, _) =>
        {
            applicableA2 = req.Applicable;   // server persists → next reload reflects it
            return Task.FromResult(new IpqcSetResponse { Ok = true, ETag = etag, MesPhase = "IPQC_WAIT" });
        };

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));

        // Tab A badge = confirmed/applicable-total = "0/2" at start.
        cut.WaitForAssertion(() =>
            Assert.Equal("0/2", cut.Find($"[data-testid='{TabA}-count']").TextContent.Trim()));

        cut.Find("[data-testid='ipqc-item-A2-applicable']").Change(false);

        cut.WaitForAssertion(() =>
        {
            // Endpoint called with Applicable=false for A2.
            var call = Assert.Single(_api.PutIpqcItemApplicableCalls);
            Assert.Equal("A2", call.ItemKey);
            Assert.False(call.Req.Applicable);
            // Row greyed (ipqc-item-row-na) after reload.
            Assert.Contains("ipqc-item-row-na", cut.Find("[data-testid='ipqc-item-A2']").ClassName);
            // applicable-total dropped to 1 → badge "0/1".
            Assert.Equal("0/1", cut.Find($"[data-testid='{TabA}-count']").TextContent.Trim());
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

    // ── TẦNG 1 — Process axis (Print / Cut) × TẦNG 2 (V/D/F) ────────

    // Tab testids embed the GroupLabel string verbatim.
    private const string PrintTabNgoaiQuan = "ipqc-tab-A·Ngoại quan";
    private const string PrintTabMauSac = "ipqc-tab-C·Màu sắc";
    private const string CutTabNgoaiQuan = "ipqc-tab-A·Ngoại quan";
    private const string CutTabChucNang = "ipqc-tab-D·Chức năng";

    /// <summary>Two-axis view: PRINT process holds LABEL/DIGITAL/SILK items,
    /// CUT holds PRESS_CNC/FINISHING, one UNKNOWN ProcessLine falls to Other.
    /// GroupLabels within each process are DISTINCT so the TẦNG-2 GroupLabel
    /// tabs are exercised per process.</summary>
    private static IpqcView ViewWithProcessAxes() => new()
    {
        WoId = 88, WoNo = "WO-PROC-1", MesPhase = "IPQC_WAIT", ETag = "v1",
        ResolvedLines = "LABEL+PRESS_CNC",
        Items = new[]
        {
            // PRINT (LABEL) — 2 GroupLabels: A·Ngoại quan + C·Màu sắc.
            new IpqcViewItem { ItemKey = "P-VIS", ProcessLine = "LABEL", GroupLabel = "A·Ngoại quan", Label = "Ngoại quan in",
                CheckType = "Visual", Method = "Mắt thường", AcceptanceCriteria = "Không loang", Status = "Ok" },
            new IpqcViewItem { ItemKey = "P-DIM", ProcessLine = "DIGITAL", GroupLabel = "C·Màu sắc", Label = "ΔE màu",
                CheckType = "Measure", Method = "Spectro", AcceptanceCriteria = "ΔE ≤ 2", Status = "Pending", MeasuredValue = "1.2" },
            // CUT (PRESS_CNC) — 2 GroupLabels: A·Ngoại quan + D·Chức năng.
            new IpqcViewItem { ItemKey = "C-VIS", ProcessLine = "PRESS_CNC", GroupLabel = "A·Ngoại quan", Label = "Ba via",
                CheckType = "Visual", Method = "Mắt thường", AcceptanceCriteria = "Không ba via", Status = "Pending" },
            new IpqcViewItem { ItemKey = "C-FUN", ProcessLine = "FINISHING", GroupLabel = "D·Chức năng", Label = "Lực bóc",
                CheckType = "Functional", Method = "Máy kéo", AcceptanceCriteria = "≥ 5N", Status = "Pending" },
        },
    };

    [Fact]
    public void Process_axis_renders_print_and_cut_chips_by_processline()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithProcessAxes());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 88L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-process-print']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-process-cut']"));
            // No Other chip — every item maps to Print or Cut.
            Assert.Empty(cut.FindAll("[data-testid='ipqc-process-other']"));
        });
    }

    [Fact]
    public void Process_badge_shows_confirmed_over_total_scoped_to_process()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithProcessAxes());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 88L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            // PRINT = 2 items, 1 confirmed (P-VIS Ok) → "1/2".
            Assert.Equal("1/2", cut.Find("[data-testid='ipqc-process-print-badge']").TextContent.Trim());
            // CUT = 2 items, 0 confirmed → "0/2".
            Assert.Equal("0/2", cut.Find("[data-testid='ipqc-process-cut-badge']").TextContent.Trim());
        });
    }

    [Fact]
    public void Selecting_process_filters_items_to_that_subset()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithProcessAxes());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 88L)
            .Add(d => d.ScrapReasons, Scraps()));

        // Default process = PRINT, default tab = Visual → only P-VIS visible.
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-P-VIS']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-item-C-VIS']"));
        });

        // Switch to CUT → CUT items only; PRINT items gone.
        cut.Find("[data-testid='ipqc-process-cut']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-C-VIS']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-item-P-VIS']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-item-P-DIM']"));
        });
    }

    [Fact]
    public void Grouplabel_tabs_are_scoped_to_active_process()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithProcessAxes());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 88L)
            .Add(d => d.ScrapReasons, Scraps()));

        // PRINT active → GroupLabels A·Ngoại quan + C·Màu sắc; D·Chức năng absent.
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"[data-testid='{PrintTabNgoaiQuan}']")));
        Assert.NotNull(cut.Find($"[data-testid='{PrintTabMauSac}']"));
        Assert.Empty(cut.FindAll($"[data-testid='{CutTabChucNang}']"));

        // Switch to CUT → GroupLabels A·Ngoại quan + D·Chức năng; C·Màu sắc absent.
        cut.Find("[data-testid='ipqc-process-cut']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find($"[data-testid='{CutTabChucNang}']"));
            Assert.Empty(cut.FindAll($"[data-testid='{PrintTabMauSac}']"));
        });
    }

    [Fact]
    public void Changing_process_resets_tab_to_first_grouplabel()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithProcessAxes());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 88L)
            .Add(d => d.ScrapReasons, Scraps()));

        // On PRINT, go to the C·Màu sắc tab (P-DIM).
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"[data-testid='{PrintTabMauSac}']")));
        cut.Find($"[data-testid='{PrintTabMauSac}']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-item-P-DIM']")));

        // Switch to CUT: tab resets to the first GroupLabel (A·Ngoại quan → C-VIS).
        cut.Find("[data-testid='ipqc-process-cut']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-C-VIS']"));
            Assert.Equal("true", cut.Find($"[data-testid='{CutTabNgoaiQuan}']").GetAttribute("aria-selected"));
        });
    }

    [Fact]
    public void Item_table_renders_process_method_and_spec_columns()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithProcessAxes());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 88L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-table']"));
            var row = cut.Find("[data-testid='ipqc-item-P-VIS']");
            // PROCESS column removed (2026-08-25) — the Print/Cut tabs already
            // convey process; METHOD + SPEC still render from item fields.
            Assert.Null(row.QuerySelector(".ipqc-col-process"));
            Assert.Contains("Mắt thường", row.QuerySelector(".ipqc-col-method")!.TextContent);
            Assert.Contains("Không loang", row.QuerySelector(".ipqc-col-spec")!.TextContent);
            // Appearance tab (A·Ngoại quan) hides the RESULT input (2026-08-25).
            Assert.Empty(cut.FindAll("[data-testid='ipqc-item-P-VIS-measured']"));
        });
    }

    [Fact]
    public void Appearance_tab_hides_result_input_but_other_tabs_show_it()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithProcessAxes());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 88L)
            .Add(d => d.ScrapReasons, Scraps()));

        // Default tab = A·Ngoại quan → RESULT input hidden.
        cut.WaitForAssertion(() =>
            Assert.Empty(cut.FindAll("[data-testid='ipqc-item-P-VIS-measured']")));

        // Switch to the C·Màu sắc tab (P-DIM) → RESULT input present.
        cut.Find("[data-testid='ipqc-tab-C·Màu sắc']").Click();
        cut.WaitForAssertion(() =>
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-P-DIM-measured']")));
    }

    [Fact]
    public void Single_process_wo_shows_only_that_chip_without_breaking()
    {
        var view = ViewWithProcessAxes() with
        {
            Items = new[]
            {
                new IpqcViewItem { ItemKey = "P-ONLY", ProcessLine = "LABEL", GroupLabel = "In", Label = "Ngoại quan",
                    CheckType = "Visual", Status = "Pending" },
            },
        };
        _api.IpqcViewImpl = (_, _) => Task.FromResult(view);

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 88L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-process-print']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-process-cut']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-process-other']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-P-ONLY']"));
        });
    }

    [Fact]
    public void Unknown_processline_item_lands_in_other_process()
    {
        var view = ViewWithProcessAxes() with
        {
            Items = new[]
            {
                new IpqcViewItem { ItemKey = "P-VIS", ProcessLine = "LABEL", GroupLabel = "In", Label = "In",
                    CheckType = "Visual", Status = "Pending" },
                new IpqcViewItem { ItemKey = "X-ONE", ProcessLine = "MYSTERY", GroupLabel = "?", Label = "Chưa map",
                    CheckType = "Visual", Status = "Pending" },
            },
        };
        _api.IpqcViewImpl = (_, _) => Task.FromResult(view);

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 88L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            // Other chip present (unknown ProcessLine) + Print chip present.
            Assert.NotNull(cut.Find("[data-testid='ipqc-process-print']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-process-other']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-process-cut']"));
        });

        // Other process contains the unmapped item, not the LABEL one.
        cut.Find("[data-testid='ipqc-process-other']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-item-X-ONE']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-item-P-VIS']"));
        });
    }

    [Fact]
    public void Item_ok_in_process_subset_calls_put_item_endpoint()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(ViewWithProcessAxes());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 88L)
            .Add(d => d.ScrapReasons, Scraps()));

        // Switch to CUT, confirm the Visual item C-VIS.
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-process-cut']")));
        cut.Find("[data-testid='ipqc-process-cut']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-item-C-VIS-ok']")));
        cut.Find("[data-testid='ipqc-item-C-VIS-ok']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(_api.PutIpqcItemCalls);
            Assert.Equal("C-VIS", call.ItemKey);
            Assert.Equal("Ok", call.Req.Status);
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
