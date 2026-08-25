using CCL.MES.Domain;
using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.Services;

/// <summary>
/// IPQC first-article — pure mutation helpers for MATERIAL (SYSTEM) rows,
/// kept out of the controller (thin-controller rule). No I/O: the caller
/// resolves the divergence snapshot (via the materializer join) and hands it
/// in; these methods only mutate the tracked entity. The executor owns
/// SaveChanges.
/// </summary>
public static class WoIpqcMaterialCheckService
{
    /// <summary>Frozen divergence evidence computed from the
    /// WoMaterial → MaterialLot → IqcInspection join at confirm time.</summary>
    public readonly record struct MaterialDivergenceSnapshot(
        string? SourceIqcReceiptNo,
        string? ExpectedPartNo,
        string? ActualLotNo,
        string? MaterialLotStatus,
        string? IqcResult,
        bool HasShadowFk,
        int DivergenceFlags,
        string DivergenceKind,
        bool IsDivergent);

    /// <summary>Confirm OK/NG on a material row and FREEZE the divergence
    /// snapshot (Q4: freeze-at-confirm). A divergent row moves to
    /// PendingEngineer (soft lock); a matched row is NotRequired.</summary>
    public static void Confirm(
        WoIpqcMaterialCheck row, IpqcCheckStatus status,
        MaterialDivergenceSnapshot snap,
        string? ngReasonCode, string? ngNote,
        string actor, DateTime now)
    {
        row.Status = status;
        row.NgReasonCode = status == IpqcCheckStatus.Ng ? ngReasonCode : null;
        row.NgNote = status == IpqcCheckStatus.Ng ? ngNote : null;
        row.ConfirmedBy = actor;
        row.ConfirmedAt = now;

        // Freeze evidence (Q4). Once frozen, a later MaterialLot.Status change
        // does not rewrite what the operator/engineer signed against.
        row.SourceIqcReceiptNo = snap.SourceIqcReceiptNo;
        row.ExpectedPartNo = snap.ExpectedPartNo;
        row.ActualLotNo = snap.ActualLotNo;
        row.MaterialLotStatusSnapshot = snap.MaterialLotStatus;
        row.IqcResultSnapshot = snap.IqcResult;
        row.HasShadowFk = snap.HasShadowFk;
        row.DivergenceFlags = snap.DivergenceFlags;
        row.DivergenceKind = snap.DivergenceKind;

        // Soft lock (Q1): a divergent row needs an Engineer waiver regardless of
        // the operator OK/NG. A re-confirm on an already-waived row keeps the
        // waiver (only re-arm if it slid back to matched).
        if (snap.IsDivergent)
        {
            if (row.DivergenceApprovalStatus == DivergenceApprovalStatus.NotRequired)
                row.DivergenceApprovalStatus = DivergenceApprovalStatus.PendingEngineer;
        }
        else
        {
            row.DivergenceApprovalStatus = DivergenceApprovalStatus.NotRequired;
        }
    }

    /// <summary>Engineer waiver decision on a divergent row. Approve → resolved;
    /// Reject → stays blocked (operator must fix the lot / StopLine).</summary>
    public static void ApproveDivergence(
        WoIpqcMaterialCheck row, bool approve, string reason, string actor, DateTime now)
    {
        row.DivergenceApprovalStatus = approve
            ? DivergenceApprovalStatus.Approved
            : DivergenceApprovalStatus.Rejected;
        row.ApprovedBy = actor;
        row.ApprovedAt = now;
        row.ApprovalReason = reason;
    }

    /// <summary>Dual-sig guard (Q1). Returns false (violation) when the flag is
    /// ON and the approver equals the row's confirmer (case-insensitive).</summary>
    public static bool ValidateDistinctWaiver(string? confirmedBy, string approver, bool required)
    {
        if (!required) return true;
        if (string.IsNullOrEmpty(confirmedBy)) return true; // no confirmer pinned yet
        return !string.Equals(confirmedBy, approver, StringComparison.OrdinalIgnoreCase);
    }
}
