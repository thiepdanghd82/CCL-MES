using CCL.MES.Domain.Entities;

namespace CCL.MES.Domain.StateMachine;

/// <summary>
/// IPQC first-article — pure helper reconciling a Prepress-scanned material
/// LOT against the IQC-released LOT. Inputs are primitives resolved by the
/// caller from the <c>WoMaterial → MaterialLot → IqcInspection</c> join so
/// this helper stays free of EF/DB concerns and is exhaustively unit-testable.
///
/// Divergence contract (Henry 2026-08-25):
///   ShadowFkNull   — the Prepress scan never resolved to a MaterialLot.
///   IqcNotPass     — the linked IqcInspection.Result ≠ "Pass".
///   PartNoMismatch — the WoMaterial.MaterialCode ≠ MaterialLot.PartNo
///                    (only meaningful when the FK resolved).
///   LotNotReleased — MaterialLot.Status ≠ "Released" (Q3: only Released valid).
///
/// When the shadow FK is null there is no lot to compare a PartNo against, so
/// <see cref="DivergenceFlags.PartNoMismatch"/> is not raised — but IqcResult
/// and LotStatus are null (≠ "Pass"/"Released") so the row is still divergent
/// via <see cref="DivergenceFlags.IqcNotPass"/> + <see cref="DivergenceFlags.LotNotReleased"/>.
/// </summary>
public static class MaterialSystemDivergence
{
    public readonly record struct Input(
        bool HasShadowFk,
        string? IqcResult,
        string? MaterialCode,
        string? LotPartNo,
        string? LotStatus);

    public readonly record struct Result(
        DivergenceFlags Flags,
        string Kind,
        bool IsDivergent);

    public static Result Compute(Input i)
    {
        var f = DivergenceFlags.None;

        if (!i.HasShadowFk)
            f |= DivergenceFlags.ShadowFkNull;

        if (!string.Equals(i.IqcResult, "Pass", StringComparison.OrdinalIgnoreCase))
            f |= DivergenceFlags.IqcNotPass;

        if (i.HasShadowFk &&
            !string.Equals(i.MaterialCode, i.LotPartNo, StringComparison.OrdinalIgnoreCase))
            f |= DivergenceFlags.PartNoMismatch;

        if (!string.Equals(i.LotStatus, "Released", StringComparison.OrdinalIgnoreCase))
            f |= DivergenceFlags.LotNotReleased;

        var kind = f == DivergenceFlags.None ? "None" : f.ToString().Replace(" ", "");
        return new Result(f, kind, f != DivergenceFlags.None);
    }
}
