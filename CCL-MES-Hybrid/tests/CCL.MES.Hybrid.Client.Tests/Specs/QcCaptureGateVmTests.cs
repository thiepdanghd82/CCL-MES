using CCL.MES.Hybrid.Client.Specs;
using CCL.MES.Shared.QcSpecs;

namespace CCL.MES.Hybrid.Client.Tests.Specs;

/// <summary>
/// P10.5f — Pure-helper coverage for the QC capture gate. Pinned so
/// the Razor capture modal can rely on the FAIL → reason guard +
/// editor-role check + Scrap-kind filter + latest-per-criterion
/// projection without re-deriving them.
/// </summary>
public sealed class QcCaptureGateVmTests
{
    // ── IsEditor — mirrors _editorRoles in legacy QC services ───────

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("admin", true)]
    [InlineData("Engineer", true)]
    [InlineData("ENGINEER", true)]
    [InlineData("Supervisor", false)]
    [InlineData("Qc", false)]
    [InlineData("Viewer", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEditor_only_passes_Admin_or_Engineer(string? role, bool expected)
    {
        Assert.Equal(expected, QcCaptureGateVm.IsEditor(role));
    }

    // ── IsCaptureValid — FAIL → reason required ─────────────────────

    [Theory]
    [InlineData(QcCaptureResult.Pass, null, true)]
    [InlineData(QcCaptureResult.Pass, "", true)]
    [InlineData(QcCaptureResult.Pass, "SC-XYZ", true)]
    [InlineData(QcCaptureResult.Na,   null, true)]
    [InlineData(QcCaptureResult.Na,   "  ", true)]
    [InlineData(QcCaptureResult.Fail, null, false)]
    [InlineData(QcCaptureResult.Fail, "", false)]
    [InlineData(QcCaptureResult.Fail, "  ", false)]
    [InlineData(QcCaptureResult.Fail, "SC-COLOR", true)]
    public void IsCaptureValid_FAIL_requires_reason(QcCaptureResult result, string? reason, bool expected)
    {
        Assert.Equal(expected, QcCaptureGateVm.IsCaptureValid(result, reason));
    }

    [Fact]
    public void ExplainInvalid_returns_null_when_valid()
    {
        Assert.Null(QcCaptureGateVm.ExplainInvalid(QcCaptureResult.Pass, null));
        Assert.Null(QcCaptureGateVm.ExplainInvalid(QcCaptureResult.Fail, "SC-MAT"));
    }

    [Fact]
    public void ExplainInvalid_FAIL_without_reason_mentions_reason()
    {
        var msg = QcCaptureGateVm.ExplainInvalid(QcCaptureResult.Fail, null);
        Assert.NotNull(msg);
        Assert.Contains("reason code", msg, StringComparison.OrdinalIgnoreCase);
    }

    // ── ScrapKindOnly — only Scrap-kind survives + sort order ───────

    [Fact]
    public void ScrapKindOnly_filters_to_scrap_kind()
    {
        var all = new[]
        {
            new QcReasonCode { Id = 1, Code = "PA-WAIT", Kind = "Pause", Sort = 1 },
            new QcReasonCode { Id = 2, Code = "SC-COLOR", Kind = "Scrap", Sort = 2 },
            new QcReasonCode { Id = 3, Code = "SC-MAT", Kind = "Scrap", Sort = 1 },
            new QcReasonCode { Id = 4, Code = "PA-OTHER", Kind = "Pause", Sort = 3 },
        };

        var scrapOnly = QcCaptureGateVm.ScrapKindOnly(all);

        Assert.Equal(2, scrapOnly.Count);
        Assert.Equal("SC-MAT", scrapOnly[0].Code);    // Sort=1 first
        Assert.Equal("SC-COLOR", scrapOnly[1].Code);
        Assert.DoesNotContain(scrapOnly, r => r.Kind == "Pause");
    }

    [Fact]
    public void ScrapKindOnly_handles_case_insensitive_kind()
    {
        var all = new[]
        {
            new QcReasonCode { Id = 1, Code = "SC-1", Kind = "scrap" },
            new QcReasonCode { Id = 2, Code = "SC-2", Kind = "SCRAP" },
        };
        var scrapOnly = QcCaptureGateVm.ScrapKindOnly(all);
        Assert.Equal(2, scrapOnly.Count);
    }

    [Fact]
    public void ScrapKindOnly_empty_input_returns_empty()
    {
        var scrapOnly = QcCaptureGateVm.ScrapKindOnly(Array.Empty<QcReasonCode>());
        Assert.Empty(scrapOnly);
    }

    // ── LatestPerCriterion — server returns ASC, last wins ──────────

    [Fact]
    public void LatestPerCriterion_keeps_last_seen_per_criterion()
    {
        // Server returns CapturedAt ASC; latest survives per criterion.
        var captures = new[]
        {
            new QcCaptureItem { Id = 1, QcCriterionId = 100, CapturedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Result = QcCaptureResult.Pass },
            new QcCaptureItem { Id = 2, QcCriterionId = 100, CapturedAt = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), Result = QcCaptureResult.Fail },
            new QcCaptureItem { Id = 3, QcCriterionId = 200, CapturedAt = new(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc), Result = QcCaptureResult.Na },
        };

        var latest = QcCaptureGateVm.LatestPerCriterion(captures);

        Assert.Equal(2, latest.Count);
        Assert.Equal(QcCaptureResult.Fail, latest[100].Result);  // newer wins
        Assert.Equal(QcCaptureResult.Na, latest[200].Result);
    }

    [Fact]
    public void LatestPerCriterion_empty_input_returns_empty()
    {
        var result = QcCaptureGateVm.LatestPerCriterion(Array.Empty<QcCaptureItem>());
        Assert.Empty(result);
    }
}
