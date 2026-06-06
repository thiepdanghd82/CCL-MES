using CCL.MES.Domain;
using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.Services;

/// <summary>
/// P10.7d-1 — IPQC check + judgment + QA approval domain helpers.
/// Mirrors the 7c <c>WoSettingService</c> / <c>WoQtyService</c>
/// pattern: pure helpers that mutate the entities in-place, controllers
/// (7d-2) wrap the whole operation in one atomic SaveChanges per
/// §6 concurrency rules + L11 (post-write ETag re-read) + L12
/// (ChangeTracker.Clear on concurrency exception).
///
/// Services never call SaveChanges — the controller layer owns the
/// commit + audit emit + ETag re-read pattern that 7b/7c established.
/// </summary>
public static class WoIpqcCheckService
{
    /// <summary>
    /// Slot discriminator for per-slot writes. Maps 1:1 to the entity
    /// status field tuples (Material / PrintA / PrintB / PrintC) so the
    /// audit row's <c>slot</c> field matches the storage field name.
    /// </summary>
    public enum CheckSlot { Material, PrintA, PrintB, PrintC }

    /// <summary>
    /// Apply a single slot mutation in-place. Caller validates the
    /// NG-reason-code-required + note-required invariants BEFORE
    /// calling (controller emits the 422 response).
    /// </summary>
    public static void SetSlot(
        WoIpqcCheck check,
        CheckSlot slot,
        IpqcCheckStatus status,
        string? ngReasonCode,
        string? ngNote,
        string actor,
        DateTime nowUtc)
    {
        switch (slot)
        {
            case CheckSlot.Material:
                check.MaterialStatus = status;
                check.MaterialNgReasonCode = status == IpqcCheckStatus.Ng ? ngReasonCode : null;
                check.MaterialNgNote = status == IpqcCheckStatus.Ng ? ngNote : null;
                break;
            case CheckSlot.PrintA:
                check.PrintAStatus = status;
                check.PrintANgReasonCode = status == IpqcCheckStatus.Ng ? ngReasonCode : null;
                check.PrintANgNote = status == IpqcCheckStatus.Ng ? ngNote : null;
                break;
            case CheckSlot.PrintB:
                check.PrintBStatus = status;
                check.PrintBNgReasonCode = status == IpqcCheckStatus.Ng ? ngReasonCode : null;
                check.PrintBNgNote = status == IpqcCheckStatus.Ng ? ngNote : null;
                break;
            case CheckSlot.PrintC:
                check.PrintCStatus = status;
                check.PrintCNgReasonCode = status == IpqcCheckStatus.Ng ? ngReasonCode : null;
                check.PrintCNgNote = status == IpqcCheckStatus.Ng ? ngNote : null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot));
        }
        check.UpdatedAt = nowUtc;
        check.UpdatedBy = actor;
    }

    /// <summary>
    /// Apply a judgment write. Caller checks
    /// <see cref="CCL.MES.Domain.StateMachine.IpqcReadinessRollup.IsJudgmentConsistent"/>
    /// BEFORE calling so an inconsistent judgment never reaches this
    /// helper. <c>specialAcceptReason</c> must be non-null + 1-500
    /// chars when judgment is SpecialAccept.
    /// </summary>
    public static void SubmitJudgment(
        WoIpqcCheck check,
        IpqcJudgment judgment,
        string? specialAcceptReason,
        string actor,
        DateTime nowUtc)
    {
        check.Judgment = judgment;
        check.SpecialAcceptReason = judgment == IpqcJudgment.SpecialAccept ? specialAcceptReason : null;
        check.IpqcSubmittedBy = actor;
        check.IpqcSubmittedAt = nowUtc;
        check.UpdatedAt = nowUtc;
        check.UpdatedBy = actor;
    }

    /// <summary>
    /// Apply a QA approval write (Approve or Reject). Caller MUST
    /// invoke <see cref="ValidateDualSig"/> BEFORE this so the
    /// same-user violation never reaches the storage layer.
    /// </summary>
    public static void SubmitQaApproval(
        WoIpqcCheck check,
        QaOutcome outcome,
        string? qaReason,
        string actor,
        DateTime nowUtc)
    {
        check.QaOutcome = outcome;
        check.QaReason = outcome == QaOutcome.Reject ? qaReason : qaReason; // optional on Approve, required on Reject (controller enforces)
        check.QaApprovedBy = actor;
        check.QaApprovedAt = nowUtc;
        check.UpdatedAt = nowUtc;
        check.UpdatedBy = actor;
    }

    /// <summary>
    /// Validate the dual-sig invariant per §5.5.1. Returns true when
    /// the QA approver is allowed to proceed; false when the controller
    /// must emit 422 + <c>WO_QA_APPROVE_DENIED</c> audit. When the
    /// feature flag is OFF (<paramref name="requireDistinctApprover"/>
    /// = false), always returns true.
    /// </summary>
    public static bool ValidateDualSig(
        string ipqcSubmittedBy,
        string qaApproverUsername,
        bool requireDistinctApprover)
    {
        if (!requireDistinctApprover) return true;
        // Empty/whitespace IPQC submitter shouldn't happen at this code
        // path (judgment guard enforced upstream) but be safe: treat as
        // "no submitter on record → no dual-sig to enforce → allow".
        if (string.IsNullOrWhiteSpace(ipqcSubmittedBy)) return true;
        return !string.Equals(ipqcSubmittedBy, qaApproverUsername,
            StringComparison.OrdinalIgnoreCase);
    }
}
