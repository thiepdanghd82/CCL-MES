using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared.WoQcReview;

namespace CCL.MES.Api.Services;

/// <summary>
/// A2 (thin-controller lát 2) — the PURE, unit-testable computation half of
/// <c>GET /api/v2/work-orders/{id}/summary-report</c>
/// (<c>WoQcReviewController.GetSummaryReport</c>). Extracted per L47
/// (<c>OqcSignaturePolicy</c> precedent): "buy verifiability". The controller
/// keeps every EF query (thin orchestration), packs the loaded data into
/// <see cref="WoSummaryReportInput"/>, and calls <see cref="Build"/>.
///
/// This is a byte-for-byte behaviour-preserving refactor of the inline logic;
/// no formula changed. In particular:
///   • quality = (qtyDone − qtyNg) / qtyDone is NOT clamped to ≤ 1.0 — a
///     negative qtyNg can push it above 1. That clamp-parity-with-ShopOrders
///     debt is unresolved (Henry-decision); see the TODO at the quality site.
///   • oee = availability × performance × quality is NOT clamped.
///   • The speed lookup (<c>WorkCenterSpeedLookup.ResolveAsync</c>) stays in
///     the controller because it is async EF; its already-computed result is
///     passed in via <see cref="WoSummaryReportInput.WorkCenterResolved"/> +
///     <see cref="WoSummaryReportInput.IdealSpeedPcsH"/>.
///   • <c>now</c> is passed in (not read from <c>DateTime.UtcNow</c>) so the
///     runtime/pause seconds are deterministic under test.
/// </summary>
public static class WoSummaryReportBuilder
{
    private const string ShippedPhase = "SHIPPED";

    public static WoSummaryReport Build(WoSummaryReportInput input)
    {
        var now = input.Now;
        var qtyDone = input.QtyDone;
        var qtyNg = input.QtyNg;

        // Run seconds — shared formula (WoRuntimeMath), same seconds the
        // Traceability list uses.
        long runSeconds = WoRuntimeMath.ElapsedSeconds(
            input.Sessions.Select(s => new WoRuntimeMath.Span(s.StartedAt, s.EndedAt)), now);

        // Pause seconds + Pareto buckets. Bucket key "(unknown)" when the
        // reason code is empty; order by seconds desc then code asc (Ordinal).
        long pauseSeconds = 0;
        var paretoBuckets = new Dictionary<string, (int Count, long Seconds)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in input.PauseEvents)
        {
            var secs = WoRuntimeMath.ElapsedSeconds(new WoRuntimeMath.Span(p.StartedAt, p.EndedAt), now);
            pauseSeconds += secs;
            var code = string.IsNullOrEmpty(p.ReasonCode) ? "(unknown)" : p.ReasonCode;
            if (!paretoBuckets.TryGetValue(code, out var b)) b = (0, 0);
            paretoBuckets[code] = (b.Count + 1, b.Seconds + secs);
        }
        var pareto = paretoBuckets
            .OrderByDescending(kv => kv.Value.Seconds)
            .ThenBy(kv => kv.Key)
            .Select(kv => new WoSummaryParetoRow
            {
                ReasonCode = kv.Key,
                Count = kv.Value.Count,
                TotalSeconds = kv.Value.Seconds,
            }).ToList();

        // OEE = Availability × Performance × Quality (Nakajima).
        double? availability = null;
        double? performance;
        double? quality = null;
        double? oee = null;

        if (runSeconds + pauseSeconds > 0)
            availability = (double)runSeconds / (runSeconds + pauseSeconds);

        // Đợt 1 C3 — speed already resolved by the controller (async EF) from
        // WorkCenter.IdealSpeedPcsH, the single canonical source.
        var perf = OeePerformance.Compute(
            input.WorkCenterResolved, input.IdealSpeedPcsH, runSeconds, qtyDone);
        performance = perf.Performance;
        var performanceUnavailableReason = perf.UnavailableReason;

        if (qtyDone > 0)
            // TODO(Henry-decision): clamp parity với ShopOrders — chưa chốt
            quality = (double)(qtyDone - qtyNg) / qtyDone;

        if (availability is not null && performance is not null && quality is not null)
            oee = availability.Value * performance.Value * quality.Value;

        // QC summary — IPQC leg (special-accept reason precedence over QA reason)
        // + FQC + OQC legs (absent → Pending).
        var ipqc = input.Ipqc;
        var qcSummary = new WoSummaryQc
        {
            Ipqc = new WoSummaryQcLeg
            {
                Judgment = ipqc is null ? "Pending" : ipqc.Judgment,
                SubmittedBy = ipqc?.IpqcSubmittedBy,
                Approver = ipqc?.QaApprovedBy,
                Reason = ipqc?.SpecialAcceptReason ?? ipqc?.QaReason,
            },
            Fqc = MapQcLeg(input.Fqc),
            Oqc = MapQcLeg(input.Oqc),
        };

        var shippedAt = input.MesPhase == ShippedPhase ? input.UpdatedAt : null;

        return new WoSummaryReport
        {
            WoId = input.WoId,
            WoNo = input.WoNo,
            MesPhase = input.MesPhase ?? "",
            ShippedAt = shippedAt,
            Totals = new WoSummaryTotals
            {
                QtyTarget = input.TargetQty,
                QtyDone = qtyDone,
                QtyNg = qtyNg,
            },
            Runtime = new WoSummaryRuntime
            {
                RunSeconds = runSeconds,
                PauseSeconds = pauseSeconds,
                SessionCount = input.Sessions.Count,
            },
            Oee = new WoSummaryOee
            {
                Availability = availability,
                Performance = performance,
                PerformanceUnavailableReason = performanceUnavailableReason,
                Quality = quality,
                Oee = oee,
            },
            PausePareto = pareto,
            QcSummary = qcSummary,
        };
    }

    private static WoSummaryQcLeg MapQcLeg(WoSummaryQcLegInput? leg)
    {
        if (leg is null) return new WoSummaryQcLeg();
        return new WoSummaryQcLeg
        {
            Judgment = leg.Judgment.ToString(),
            SubmittedBy = leg.InspectedBy,
            Reviewer = leg.ReviewedBy,
            Approver = leg.ApprovedBy,
            Reason = leg.JudgmentReason,
        };
    }
}

/// <summary>Strongly-typed, already-loaded input for
/// <see cref="WoSummaryReportBuilder.Build"/>. Every field is a plain scalar
/// or an in-memory list — no EF, no HttpContext, no DbContext — so the builder
/// stays pure and unit-testable.</summary>
public sealed record WoSummaryReportInput
{
    // ── WO core scalars ────────────────────────────────────────────
    public long WoId { get; init; }
    public string WoNo { get; init; } = "";
    public string? MesPhase { get; init; }
    public int TargetQty { get; init; }
    public int QtyDone { get; init; }
    public int QtyNg { get; init; }
    public DateTime? UpdatedAt { get; init; }

    /// <summary>Deterministic "now" — controller passes <c>DateTime.UtcNow</c>;
    /// tests pass a fixed instant.</summary>
    public DateTime Now { get; init; }

    // ── runtime spans ──────────────────────────────────────────────
    public IReadOnlyList<WoSummarySessionSpan> Sessions { get; init; } = Array.Empty<WoSummarySessionSpan>();
    public IReadOnlyList<WoSummaryPauseSpan> PauseEvents { get; init; } = Array.Empty<WoSummaryPauseSpan>();

    // ── speed (already resolved async by the controller) ───────────
    public bool WorkCenterResolved { get; init; }
    public double? IdealSpeedPcsH { get; init; }

    // ── QC legs ────────────────────────────────────────────────────
    public WoSummaryIpqcInput? Ipqc { get; init; }
    public WoSummaryQcLegInput? Fqc { get; init; }
    public WoSummaryQcLegInput? Oqc { get; init; }
}

/// <summary>One run session span (open when EndedAt is null).</summary>
public readonly record struct WoSummarySessionSpan(DateTime StartedAt, DateTime? EndedAt);

/// <summary>One pause event span + its reason code (empty → "(unknown)").</summary>
public readonly record struct WoSummaryPauseSpan(string? ReasonCode, DateTime StartedAt, DateTime? EndedAt);

/// <summary>IPQC leg projection (judgment already stringified by the caller so
/// the builder stays free of the IpqcJudgment enum type).</summary>
public sealed record WoSummaryIpqcInput
{
    public string Judgment { get; init; } = "Pending";
    public string? IpqcSubmittedBy { get; init; }
    public string? QaApprovedBy { get; init; }
    public string? QaReason { get; init; }
    public string? SpecialAcceptReason { get; init; }
}

/// <summary>FQC/OQC leg projection — the strongly-typed replacement for the
/// old reflection-based <c>MapQcLeg&lt;TRow&gt;</c> dynamic hack.</summary>
public sealed record WoSummaryQcLegInput
{
    public WoQcJudgment Judgment { get; init; } = WoQcJudgment.Pending;
    public string? InspectedBy { get; init; }
    public string? ReviewedBy { get; init; }
    public string? ApprovedBy { get; init; }
    public string? JudgmentReason { get; init; }
}
