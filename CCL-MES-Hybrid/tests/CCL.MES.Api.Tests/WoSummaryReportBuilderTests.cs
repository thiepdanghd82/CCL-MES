using CCL.MES.Api.Services;
using CCL.MES.Domain.Entities;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// A2 (thin-controller lát 2) — PURE unit tests for
/// <see cref="WoSummaryReportBuilder"/>. No WebApplicationFactory, no DB, no
/// HttpClient: the whole point of the extraction (L47 "buy verifiability") is
/// that the summary maths can be exercised as a plain function.
///
/// These LOCK the current behaviour byte-for-byte — including the two
/// deliberately UNCLAMPED formulas (quality can exceed 1.0 on a negative
/// qtyNg; oee is a bare product). The integration belt
/// <c>WoSummaryOeeTests</c> pins the same contract end-to-end; this file pins
/// the arithmetic without the web host.
/// </summary>
public sealed class WoSummaryReportBuilderTests
{
    private static readonly DateTime T0 = new(2026, 06, 05, 08, 00, 00, DateTimeKind.Utc);

    /// <summary>A minimal input with no sessions/pauses/QC and a resolved
    /// work center; individual tests override just what they exercise.</summary>
    private static WoSummaryReportInput Base(Action<WoSummaryReportInputBuilder>? tweak = null)
    {
        var b = new WoSummaryReportInputBuilder();
        tweak?.Invoke(b);
        return b.Build();
    }

    // ── availability ──────────────────────────────────────────────

    [Fact]
    public void Availability_null_when_no_run_and_no_pause()
    {
        var report = WoSummaryReportBuilder.Build(Base());
        Assert.Null(report.Oee.Availability);
        Assert.Equal(0, report.Runtime.RunSeconds);
        Assert.Equal(0, report.Runtime.PauseSeconds);
    }

    [Fact]
    public void Availability_is_run_over_run_plus_pause()
    {
        // 1h run + 1h pause → availability 0.5.
        var report = WoSummaryReportBuilder.Build(Base(b =>
        {
            b.Sessions.Add(new WoSummarySessionSpan(T0, T0.AddHours(1)));
            b.PauseEvents.Add(new WoSummaryPauseSpan("PA-JAM", T0, T0.AddHours(1)));
        }));

        Assert.Equal(3600, report.Runtime.RunSeconds);
        Assert.Equal(3600, report.Runtime.PauseSeconds);
        Assert.NotNull(report.Oee.Availability);
        Assert.Equal(0.5, report.Oee.Availability!.Value, precision: 6);
    }

    // ── runtime seconds via WoRuntimeMath (open span → now) ───────

    [Fact]
    public void Run_seconds_use_WoRuntimeMath_open_span_to_now()
    {
        // Open session (EndedAt null) runs to Now; 2h elapsed.
        var now = T0.AddHours(2);
        var report = WoSummaryReportBuilder.Build(Base(b =>
        {
            b.Now = now;
            b.Sessions.Add(new WoSummarySessionSpan(T0, null));
        }));

        Assert.Equal(7200, report.Runtime.RunSeconds);
        Assert.Equal(1, report.Runtime.SessionCount);
    }

    // ── quality (UNCLAMPED — Henry-decision debt) ─────────────────

    [Fact]
    public void Quality_null_when_qty_done_zero()
    {
        var report = WoSummaryReportBuilder.Build(Base(b => { b.QtyDone = 0; b.QtyNg = 0; }));
        Assert.Null(report.Oee.Quality);
    }

    [Fact]
    public void Quality_is_pass_fraction_with_positive_ng()
    {
        // 1000 done, 100 NG → 0.9.
        var report = WoSummaryReportBuilder.Build(Base(b => { b.QtyDone = 1000; b.QtyNg = 100; }));
        Assert.NotNull(report.Oee.Quality);
        Assert.Equal(0.9, report.Oee.Quality!.Value, precision: 6);
    }

    [Fact]
    public void Quality_is_NOT_clamped_when_ng_is_negative()
    {
        // Negative qtyNg (over-correction) pushes quality ABOVE 1.0. Locking
        // the current unclamped behaviour: (1000 − (−100)) / 1000 = 1.1.
        var report = WoSummaryReportBuilder.Build(Base(b => { b.QtyDone = 1000; b.QtyNg = -100; }));
        Assert.NotNull(report.Oee.Quality);
        Assert.Equal(1.1, report.Oee.Quality!.Value, precision: 6);
        Assert.True(report.Oee.Quality!.Value > 1.0);
    }

    // ── oee composition (null when any factor null) ───────────────

    [Fact]
    public void Oee_null_when_performance_factor_null()
    {
        // Full run + pause + qty → availability & quality non-null, but no
        // work center speed → performance null → oee null (unavailable reason
        // travels on performance).
        var report = WoSummaryReportBuilder.Build(Base(b =>
        {
            b.QtyDone = 300;
            b.Sessions.Add(new WoSummarySessionSpan(T0, T0.AddHours(1)));
            b.PauseEvents.Add(new WoSummaryPauseSpan("PA-JAM", T0, T0.AddMinutes(30)));
            b.WorkCenterResolved = true;
            b.IdealSpeedPcsH = null; // speed missing
        }));

        Assert.NotNull(report.Oee.Availability);
        Assert.NotNull(report.Oee.Quality);
        Assert.Null(report.Oee.Performance);
        Assert.Equal("workcenter_speed_missing", report.Oee.PerformanceUnavailableReason);
        Assert.Null(report.Oee.Oee);
    }

    [Fact]
    public void Oee_is_product_of_three_factors_when_all_present()
    {
        // 1h run + 1h pause → availability 0.5.
        // 600 pcs/h, 3600s run → planned 600; qtyDone 300 → performance 0.5.
        // 300 done, 0 NG → quality 1.0.  oee = 0.5 × 0.5 × 1.0 = 0.25.
        var report = WoSummaryReportBuilder.Build(Base(b =>
        {
            b.QtyDone = 300;
            b.Sessions.Add(new WoSummarySessionSpan(T0, T0.AddHours(1)));
            b.PauseEvents.Add(new WoSummaryPauseSpan("PA-JAM", T0, T0.AddHours(1)));
            b.WorkCenterResolved = true;
            b.IdealSpeedPcsH = 600;
        }));

        Assert.Equal(0.5, report.Oee.Availability!.Value, precision: 6);
        Assert.Equal(0.5, report.Oee.Performance!.Value, precision: 6);
        Assert.Equal(1.0, report.Oee.Quality!.Value, precision: 6);
        Assert.NotNull(report.Oee.Oee);
        Assert.Equal(0.25, report.Oee.Oee!.Value, precision: 6);
    }

    // ── pareto ordering + "(unknown)" bucket ──────────────────────

    [Fact]
    public void Pareto_orders_by_seconds_desc_then_code_and_buckets_unknown()
    {
        var report = WoSummaryReportBuilder.Build(Base(b =>
        {
            // BB: 30 min ; AA: 60 min ; empty→"(unknown)": 60 min (two events).
            b.PauseEvents.Add(new WoSummaryPauseSpan("BB", T0, T0.AddMinutes(30)));
            b.PauseEvents.Add(new WoSummaryPauseSpan("AA", T0, T0.AddMinutes(60)));
            b.PauseEvents.Add(new WoSummaryPauseSpan("", T0, T0.AddMinutes(45)));
            b.PauseEvents.Add(new WoSummaryPauseSpan(null, T0, T0.AddMinutes(15)));
        }));

        var rows = report.PausePareto;
        Assert.Equal(3, rows.Count);
        // AA (3600s) and "(unknown)" (2700+900=3600s) tie at 3600 → code asc:
        // "(" < "A" in Ordinal, so "(unknown)" precedes "AA".
        Assert.Equal("(unknown)", rows[0].ReasonCode);
        Assert.Equal(2, rows[0].Count);
        Assert.Equal(3600, rows[0].TotalSeconds);
        Assert.Equal("AA", rows[1].ReasonCode);
        Assert.Equal(3600, rows[1].TotalSeconds);
        Assert.Equal("BB", rows[2].ReasonCode);
        Assert.Equal(1800, rows[2].TotalSeconds);
    }

    // ── qc legs ───────────────────────────────────────────────────

    [Fact]
    public void Qc_legs_absent_render_pending()
    {
        var report = WoSummaryReportBuilder.Build(Base()); // no ipqc/fqc/oqc
        Assert.Equal("Pending", report.QcSummary.Ipqc.Judgment);
        Assert.Equal("Pending", report.QcSummary.Fqc.Judgment);
        Assert.Equal("Pending", report.QcSummary.Oqc.Judgment);
        Assert.Null(report.QcSummary.Fqc.Reviewer);
    }

    [Fact]
    public void Fqc_oqc_legs_map_fields_explicitly()
    {
        var report = WoSummaryReportBuilder.Build(Base(b =>
        {
            b.Fqc = new WoSummaryQcLegInput
            {
                Judgment = WoQcJudgment.Pass,
                InspectedBy = "insp",
                JudgmentReason = "clean",
            };
            b.Oqc = new WoSummaryQcLegInput
            {
                Judgment = WoQcJudgment.Reject,
                InspectedBy = "i2",
                ReviewedBy = "r2",
                ApprovedBy = "a2",
                JudgmentReason = "scuffed",
            };
        }));

        Assert.Equal("Pass", report.QcSummary.Fqc.Judgment);
        Assert.Equal("insp", report.QcSummary.Fqc.SubmittedBy);
        Assert.Equal("clean", report.QcSummary.Fqc.Reason);

        Assert.Equal("Reject", report.QcSummary.Oqc.Judgment);
        Assert.Equal("i2", report.QcSummary.Oqc.SubmittedBy);
        Assert.Equal("r2", report.QcSummary.Oqc.Reviewer);
        Assert.Equal("a2", report.QcSummary.Oqc.Approver);
        Assert.Equal("scuffed", report.QcSummary.Oqc.Reason);
    }

    // ── ipqc reason precedence (SpecialAccept ?? Qa) ──────────────

    [Fact]
    public void Ipqc_reason_prefers_special_accept_over_qa()
    {
        var report = WoSummaryReportBuilder.Build(Base(b =>
        {
            b.Ipqc = new WoSummaryIpqcInput
            {
                Judgment = "SpecialAccept",
                IpqcSubmittedBy = "sub",
                QaApprovedBy = "qa",
                SpecialAcceptReason = "special",
                QaReason = "qa-reason",
            };
        }));

        Assert.Equal("SpecialAccept", report.QcSummary.Ipqc.Judgment);
        Assert.Equal("sub", report.QcSummary.Ipqc.SubmittedBy);
        Assert.Equal("qa", report.QcSummary.Ipqc.Approver);
        Assert.Equal("special", report.QcSummary.Ipqc.Reason);
    }

    [Fact]
    public void Ipqc_reason_falls_back_to_qa_when_no_special_accept()
    {
        var report = WoSummaryReportBuilder.Build(Base(b =>
        {
            b.Ipqc = new WoSummaryIpqcInput
            {
                Judgment = "GoRun",
                SpecialAcceptReason = null,
                QaReason = "qa-only",
            };
        }));

        Assert.Equal("qa-only", report.QcSummary.Ipqc.Reason);
    }

    // ── shippedAt only when MesPhase == SHIPPED ───────────────────

    [Fact]
    public void ShippedAt_set_only_when_phase_is_shipped()
    {
        var updated = T0.AddDays(1);

        var shipped = WoSummaryReportBuilder.Build(Base(b =>
        {
            b.MesPhase = "SHIPPED";
            b.UpdatedAt = updated;
        }));
        Assert.Equal(updated, shipped.ShippedAt);

        var running = WoSummaryReportBuilder.Build(Base(b =>
        {
            b.MesPhase = "RUNNING";
            b.UpdatedAt = updated;
        }));
        Assert.Null(running.ShippedAt);
    }

    [Fact]
    public void MesPhase_null_maps_to_empty_string()
    {
        var report = WoSummaryReportBuilder.Build(Base(b => b.MesPhase = null));
        Assert.Equal("", report.MesPhase);
        Assert.Null(report.ShippedAt);
    }

    /// <summary>Mutable convenience builder for the immutable input record so
    /// each test overrides only the fields it exercises.</summary>
    private sealed class WoSummaryReportInputBuilder
    {
        public long WoId { get; set; } = 42;
        public string WoNo { get; set; } = "WO-A2-TEST";
        public string? MesPhase { get; set; } = "RUNNING";
        public int TargetQty { get; set; } = 1000;
        public int QtyDone { get; set; }
        public int QtyNg { get; set; }
        public DateTime? UpdatedAt { get; set; } = T0;
        public DateTime Now { get; set; } = T0.AddHours(4);
        public List<WoSummarySessionSpan> Sessions { get; } = new();
        public List<WoSummaryPauseSpan> PauseEvents { get; } = new();
        public bool WorkCenterResolved { get; set; }
        public double? IdealSpeedPcsH { get; set; }
        public WoSummaryIpqcInput? Ipqc { get; set; }
        public WoSummaryQcLegInput? Fqc { get; set; }
        public WoSummaryQcLegInput? Oqc { get; set; }

        public WoSummaryReportInput Build() => new()
        {
            WoId = WoId,
            WoNo = WoNo,
            MesPhase = MesPhase,
            TargetQty = TargetQty,
            QtyDone = QtyDone,
            QtyNg = QtyNg,
            UpdatedAt = UpdatedAt,
            Now = Now,
            Sessions = Sessions,
            PauseEvents = PauseEvents,
            WorkCenterResolved = WorkCenterResolved,
            IdealSpeedPcsH = IdealSpeedPcsH,
            Ipqc = Ipqc,
            Fqc = Fqc,
            Oqc = Oqc,
        };
    }
}
