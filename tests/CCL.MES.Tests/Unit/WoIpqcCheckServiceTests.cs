using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7d-1 — pure-helper coverage for slot mutation + judgment +
/// QA approval + dual-sig validation. Controllers (7d-2) call these
/// after their own body validation; the service never throws on
/// invariant violations — it relies on the controller to call them
/// in the right order.
/// </summary>
public sealed class WoIpqcCheckServiceTests
{
    private static WoIpqcCheck Empty() => new()
    {
        WorkOrderId = 42,
        MaterialStatus = IpqcCheckStatus.Pending,
        PrintAStatus = IpqcCheckStatus.Pending,
        PrintBStatus = IpqcCheckStatus.Pending,
        PrintCStatus = IpqcCheckStatus.Pending,
    };

    private static readonly DateTime NowFixed = new(2026, 6, 6, 14, 0, 0, DateTimeKind.Utc);

    // ── SetSlot — Material ─────────────────────────────────────────

    [Fact]
    public void SetSlot_Material_Ok_clears_NG_fields()
    {
        var check = Empty();
        check.MaterialNgReasonCode = "PRIOR";
        check.MaterialNgNote = "prior NG note";

        WoIpqcCheckService.SetSlot(check, WoIpqcCheckService.CheckSlot.Material,
            IpqcCheckStatus.Ok, null, null, "qc-alice", NowFixed);

        Assert.Equal(IpqcCheckStatus.Ok, check.MaterialStatus);
        Assert.Null(check.MaterialNgReasonCode);
        Assert.Null(check.MaterialNgNote);
        Assert.Equal(NowFixed, check.UpdatedAt);
        Assert.Equal("qc-alice", check.UpdatedBy);
    }

    [Fact]
    public void SetSlot_Material_Ng_persists_reason_and_note()
    {
        var check = Empty();
        WoIpqcCheckService.SetSlot(check, WoIpqcCheckService.CheckSlot.Material,
            IpqcCheckStatus.Ng, "SC-MAT-DAMAGE", "Bao bì rách", "qc-alice", NowFixed);

        Assert.Equal(IpqcCheckStatus.Ng, check.MaterialStatus);
        Assert.Equal("SC-MAT-DAMAGE", check.MaterialNgReasonCode);
        Assert.Equal("Bao bì rách", check.MaterialNgNote);
    }

    // ── SetSlot — Print A/B/C parity ──────────────────────────────

    [Theory]
    [InlineData(WoIpqcCheckService.CheckSlot.PrintA)]
    [InlineData(WoIpqcCheckService.CheckSlot.PrintB)]
    [InlineData(WoIpqcCheckService.CheckSlot.PrintC)]
    public void SetSlot_Print_slots_each_mutate_only_their_own_status(
        WoIpqcCheckService.CheckSlot slot)
    {
        var check = Empty();
        WoIpqcCheckService.SetSlot(check, slot, IpqcCheckStatus.Ng,
            "SC-COLOR", "ΔE quá 2", "qc-bob", NowFixed);

        // Only the targeted slot mutates; all others stay Pending.
        Assert.Equal(IpqcCheckStatus.Pending, check.MaterialStatus);
        var statuses = new[]
        {
            (slot == WoIpqcCheckService.CheckSlot.PrintA, check.PrintAStatus),
            (slot == WoIpqcCheckService.CheckSlot.PrintB, check.PrintBStatus),
            (slot == WoIpqcCheckService.CheckSlot.PrintC, check.PrintCStatus),
        };
        foreach (var (isTarget, status) in statuses)
            Assert.Equal(isTarget ? IpqcCheckStatus.Ng : IpqcCheckStatus.Pending, status);
    }

    [Fact]
    public void SetSlot_Ok_after_Ng_clears_NG_fields_on_each_slot()
    {
        var check = Empty();
        // Set NG first.
        WoIpqcCheckService.SetSlot(check, WoIpqcCheckService.CheckSlot.PrintB,
            IpqcCheckStatus.Ng, "SC-REG", "lệch màu", "qc", NowFixed);
        Assert.Equal("SC-REG", check.PrintBNgReasonCode);

        // Operator reconsiders + sets OK; NG fields cleared.
        WoIpqcCheckService.SetSlot(check, WoIpqcCheckService.CheckSlot.PrintB,
            IpqcCheckStatus.Ok, null, null, "qc", NowFixed.AddMinutes(1));
        Assert.Equal(IpqcCheckStatus.Ok, check.PrintBStatus);
        Assert.Null(check.PrintBNgReasonCode);
        Assert.Null(check.PrintBNgNote);
    }

    // ── SubmitJudgment ─────────────────────────────────────────────

    [Fact]
    public void SubmitJudgment_GoRun_stamps_submitter_and_clears_special_reason()
    {
        var check = Empty();
        WoIpqcCheckService.SubmitJudgment(check, IpqcJudgment.GoRun,
            specialAcceptReason: null, actor: "qc-alice", nowUtc: NowFixed);

        Assert.Equal(IpqcJudgment.GoRun, check.Judgment);
        Assert.Null(check.SpecialAcceptReason);
        Assert.Equal("qc-alice", check.IpqcSubmittedBy);
        Assert.Equal(NowFixed, check.IpqcSubmittedAt);
    }

    [Fact]
    public void SubmitJudgment_SpecialAccept_persists_reason()
    {
        var check = Empty();
        WoIpqcCheckService.SubmitJudgment(check, IpqcJudgment.SpecialAccept,
            specialAcceptReason: "ΔE = 2.3, lô gấp giao trong ngày",
            actor: "qc-alice", nowUtc: NowFixed);

        Assert.Equal(IpqcJudgment.SpecialAccept, check.Judgment);
        Assert.Equal("ΔE = 2.3, lô gấp giao trong ngày", check.SpecialAcceptReason);
    }

    [Fact]
    public void SubmitJudgment_StopLine_clears_special_reason_even_if_caller_supplies_one()
    {
        var check = Empty();
        WoIpqcCheckService.SubmitJudgment(check, IpqcJudgment.StopLine,
            specialAcceptReason: "ignored",
            actor: "qc-alice", nowUtc: NowFixed);

        Assert.Equal(IpqcJudgment.StopLine, check.Judgment);
        Assert.Null(check.SpecialAcceptReason);
    }

    // ── SubmitQaApproval ──────────────────────────────────────────

    [Fact]
    public void SubmitQaApproval_Approve_stamps_approver()
    {
        var check = Empty();
        check.IpqcSubmittedBy = "qc-alice";
        WoIpqcCheckService.SubmitQaApproval(check, QaOutcome.Approve,
            qaReason: null, actor: "qa-bob", nowUtc: NowFixed);

        Assert.Equal(QaOutcome.Approve, check.QaOutcome);
        Assert.Equal("qa-bob", check.QaApprovedBy);
        Assert.Equal(NowFixed, check.QaApprovedAt);
    }

    [Fact]
    public void SubmitQaApproval_Reject_persists_reason()
    {
        var check = Empty();
        WoIpqcCheckService.SubmitQaApproval(check, QaOutcome.Reject,
            qaReason: "Vi phạm spec màu — không cho phép special accept",
            actor: "qa-bob", nowUtc: NowFixed);

        Assert.Equal(QaOutcome.Reject, check.QaOutcome);
        Assert.Equal("Vi phạm spec màu — không cho phép special accept", check.QaReason);
    }

    // ── ValidateDualSig (Q3 CRITICAL — 4-eye principle) ────────────

    [Fact]
    public void ValidateDualSig_flag_off_always_passes()
    {
        // Same user + flag OFF → controller may proceed (dev/UAT mode).
        Assert.True(WoIpqcCheckService.ValidateDualSig(
            ipqcSubmittedBy: "alice", qaApproverUsername: "alice",
            requireDistinctApprover: false));
    }

    [Fact]
    public void ValidateDualSig_flag_on_distinct_usernames_passes()
    {
        Assert.True(WoIpqcCheckService.ValidateDualSig(
            "qc-alice", "qa-bob", requireDistinctApprover: true));
    }

    [Fact]
    public void ValidateDualSig_flag_on_same_username_fails()
    {
        Assert.False(WoIpqcCheckService.ValidateDualSig(
            "qc-alice", "qc-alice", requireDistinctApprover: true));
    }

    [Fact]
    public void ValidateDualSig_flag_on_case_insensitive_match_fails()
    {
        // L-style case-drift can't defeat the gate.
        Assert.False(WoIpqcCheckService.ValidateDualSig(
            "QC-Alice", "qc-alice", requireDistinctApprover: true));
        Assert.False(WoIpqcCheckService.ValidateDualSig(
            "alice", "ALICE", requireDistinctApprover: true));
    }

    [Fact]
    public void ValidateDualSig_empty_ipqc_submitter_passes_defensively()
    {
        // Shouldn't happen at the controller layer (judgment guard
        // enforced upstream) — but safer to pass than to block.
        Assert.True(WoIpqcCheckService.ValidateDualSig(
            "", "qa-bob", requireDistinctApprover: true));
        Assert.True(WoIpqcCheckService.ValidateDualSig(
            "   ", "qa-bob", requireDistinctApprover: true));
    }
}
