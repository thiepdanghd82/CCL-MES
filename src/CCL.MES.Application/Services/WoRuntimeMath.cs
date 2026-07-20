namespace CCL.MES.Application.Services;

/// <summary>
/// Pure runtime/quality math shared by the WO summary report
/// (<c>WoQcReviewController.GetSummaryReport</c>) and the Quality
/// Traceability list (<c>TraceabilityQuery</c>). Extracted so the two
/// surfaces can NEVER drift on how run/pause seconds and first-pass
/// yield are computed (Lesson: "one upsert / one formula" — see the
/// Plan-C library importer precedent).
/// </summary>
public static class WoRuntimeMath
{
    /// <summary>A time span that may still be open (EndedAt == null → "now").
    /// Models both a WoRunSession and a WoPauseEvent uniformly.</summary>
    public readonly record struct Span(DateTime StartedAt, DateTime? EndedAt);

    /// <summary>Elapsed whole seconds of a single span; an open span runs to
    /// <paramref name="now"/>. Never negative (clock skew / EndedAt before
    /// StartedAt → 0).</summary>
    public static long ElapsedSeconds(Span s, DateTime now)
    {
        var ended = s.EndedAt ?? now;
        return ended > s.StartedAt ? (long)(ended - s.StartedAt).TotalSeconds : 0;
    }

    /// <summary>Total elapsed seconds across many spans (run sessions or
    /// pause events). Sum of the single-span formula so both callers agree
    /// to the second.</summary>
    public static long ElapsedSeconds(IEnumerable<Span> spans, DateTime now)
    {
        long total = 0;
        foreach (var s in spans) total += ElapsedSeconds(s, now);
        return total;
    }

    // ── First-pass yield ────────────────────────────────────────────
    //
    // Business definition (chốt với nghiệp vụ — sửa 1 chỗ này nếu MES đổi ý):
    //   FQC input  = số thành phẩm tốt đưa vào FQC = qtyDone (ProducedQty).
    //   FQC output = số PASS = qtyDone − qtyNg   (qtyNg = Σ WoQtyEntry.QtyNgDelta,
    //                ledger authoritative — không dùng cache).
    //   First-pass yield = FQC output / FQC input, = 0 khi input = 0.
    // Không có field đếm FQC-output riêng nên suy ra từ ledger NG; đây cũng
    // trùng định nghĩa "không có NG trong toàn tuyến" mà đề bài nêu.
    /// <summary>Human-readable formula string, surfaced in code + tests so a
    /// change here is intentional and greppable.</summary>
    public const string FirstPassYieldFormula = "(qtyDone - qtyNg) / qtyDone";

    /// <summary>FQC output (pass) count = qtyDone − qtyNg, floored at 0.</summary>
    public static int FqcOutput(int qtyDone, int qtyNg) => Math.Max(0, qtyDone - qtyNg);

    /// <summary>First-pass yield as a 0..1 fraction; 0 when input (qtyDone) is 0.</summary>
    public static double FirstPassYield(int qtyDone, int qtyNg)
        => qtyDone > 0 ? (double)FqcOutput(qtyDone, qtyNg) / qtyDone : 0d;
}
