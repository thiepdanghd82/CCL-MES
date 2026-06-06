namespace CCL.MES.Shared.ReasonCodes;

/// <summary>
/// P10.7b-3 — wire shape for the reason-code picker on the PREPRESS
/// dashboard (Material / Plate / Cutter NG). The client uses
/// <see cref="Code"/> as the option value (sent back on PUT as
/// <c>NgReasonCode</c>) and <see cref="LabelVi"/> as the visible
/// option text. Stays a flat record — no Domain dependency so the
/// MAUI shell never pulls CCL.MES.Domain in.
/// </summary>
public sealed record ReasonCodeOption
{
    public string Code { get; init; } = "";
    public string LabelEn { get; init; } = "";
    public string LabelVi { get; init; } = "";
    public string Kind { get; init; } = "";
    public int Sort { get; init; }
}
