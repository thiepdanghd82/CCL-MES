using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client.Qms;
using CCL.MES.Hybrid.Razor.Shared.Qms;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// Reusable QMS shell components — stepper, OK/NG toggle, verdict pill, check
/// table, decision panel, module top-bar. Each has focused render + interaction
/// coverage so the modules can compose them with confidence.
/// </summary>
public sealed class QmsShellComponentsTests : TestContext
{
    public QmsShellComponentsTests() => Services.AddI18n();

    [Fact]
    public void Stepper_marks_active_step_and_raises_click()
    {
        var clicked = -1;
        var cut = RenderComponent<QmsStepper>(p => p
            .Add(x => x.Steps, QmsMock.IqcSteps)
            .Add(x => x.ActiveIndex, 1)
            .Add(x => x.OnStepClick, EventCallback.Factory.Create<int>(this, i => clicked = i)));

        Assert.Equal(5, cut.FindAll(".qms-step").Count);
        Assert.Contains("qms-step-on", cut.Find("[data-testid='qms-step-2']").GetAttribute("class"));

        cut.Find("[data-testid='qms-step-4']").Click();
        Assert.Equal(3, clicked);   // zero-based index of step #4
    }

    [Fact]
    public void OkNgToggle_reflects_value_and_raises_change()
    {
        QmsVerdict? got = null;
        var cut = RenderComponent<QmsOkNgToggle>(p => p
            .Add(x => x.Value, QmsVerdict.Pass)
            .Add(x => x.OnChange, EventCallback.Factory.Create<QmsVerdict>(this, v => got = v)));

        Assert.Contains("qms-okng-ok-on", cut.Find(".qms-okng-ok").GetAttribute("class"));

        cut.Find(".qms-okng-ng").Click();
        Assert.Equal(QmsVerdict.Ng, got);
    }

    [Theory]
    [InlineData(QmsVerdict.Pass, "qms-pill-pass")]
    [InlineData(QmsVerdict.Ng, "qms-pill-ng")]
    public void VerdictPill_renders_pass_or_ng(QmsVerdict v, string cls)
    {
        var cut = RenderComponent<QmsVerdictPill>(p => p.Add(x => x.Verdict, v));
        Assert.Single(cut.FindAll($".{cls}"));
    }

    [Fact]
    public void VerdictPill_none_renders_nothing()
    {
        var cut = RenderComponent<QmsVerdictPill>(p => p.Add(x => x.Verdict, QmsVerdict.None));
        Assert.Empty(cut.FindAll(".qms-pill"));
    }

    [Fact]
    public void CheckTable_shows_process_column_only_when_enabled()
    {
        var withProc = RenderComponent<QmsCheckTable>(p => p
            .Add(x => x.Rows, QmsMock.IpqcChecks).Add(x => x.ShowProcess, true));
        var noProc = RenderComponent<QmsCheckTable>(p => p
            .Add(x => x.Rows, QmsMock.OqcChecks).Add(x => x.ShowProcess, false));

        // Header count: item/method/spec/result/verdict = 5; +process = 6.
        Assert.Equal(6, withProc.FindAll("thead th").Count);
        Assert.Equal(5, noProc.FindAll("thead th").Count);
        // NG row result renders red.
        Assert.Contains(withProc.FindAll(".qms-td-ng"), _ => true);
    }

    [Fact]
    public void DecisionPanel_renders_rollup_and_raises_decision()
    {
        QmsLotDecision got = QmsLotDecision.None;
        var cut = RenderComponent<QmsDecisionPanel>(p => p
            .Add(x => x.LotQty, 2400).Add(x => x.SampleQty, 32).Add(x => x.NgQty, 4)
            .Add(x => x.Decision, QmsLotDecision.None)
            .Add(x => x.OnDecide, EventCallback.Factory.Create<QmsLotDecision>(this, d => got = d)));

        Assert.Contains("pcs", cut.Markup);
        Assert.Single(cut.FindAll("[data-testid='qms-decision-panel']"));
        cut.Find("[data-testid='qms-decision-reject']").Click();
        Assert.Equal(QmsLotDecision.Reject, got);
    }

    // feat/qms-topbar-breadcrumb: the per-module QmsModuleTopBar (+ its
    // duplicate user badge) was removed; module identity now renders as a
    // breadcrumb in the main app top-bar. See TopBarModulePathTests.
}
