namespace CCL.MES.Api.Observability;

/// <summary>
/// Đợt 1 C1 — per-request bag of the two business identifiers a log line
/// needs to be useful on the shop floor: which WO, which work center.
///
/// Scoped. Seeded empty by the DI container, filled in by whichever layer
/// first learns the value — the audit writer for every mutation, the OEE
/// read path for the summary report. Controllers never touch it: that
/// would be business logic in the HTTP layer, which
/// <c>gate-thin-controller.sh</c> exists to prevent.
///
/// Deliberately NOT carrying a shift code. Đợt 1 log shape is
/// TraceId · WoNo · Actor · WorkCenter. Shift lands in Đợt 3 on a real
/// <c>ShiftCalendar</c> — data-driven, time-effective, per site — not on a
/// hardcoded UTC+7 06/14/22 split.
/// </summary>
public sealed class MesRequestContext
{
    /// <summary>Human-readable WO number, e.g. "WO-26-2852". Null until a
    /// layer that knows it reports it.</summary>
    public string? WoNo { get; private set; }

    /// <summary>Work-center / machine code, e.g. "SL-01".</summary>
    public string? WorkCenter { get; private set; }

    /// <summary>First non-empty value wins. A request touching several WOs
    /// (batch endpoints) keeps the first so the log line stays stable
    /// instead of flickering to whichever row happened to be last.</summary>
    public void NoteWorkOrder(string? woNo)
    {
        if (WoNo is null && !string.IsNullOrWhiteSpace(woNo)) WoNo = woNo;
    }

    /// <inheritdoc cref="NoteWorkOrder"/>
    public void NoteWorkCenter(string? code)
    {
        if (WorkCenter is null && !string.IsNullOrWhiteSpace(code)) WorkCenter = code;
    }
}
