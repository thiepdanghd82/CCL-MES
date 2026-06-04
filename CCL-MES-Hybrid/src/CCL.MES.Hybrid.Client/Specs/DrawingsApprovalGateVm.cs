using CCL.MES.Shared.Drawings;

namespace CCL.MES.Hybrid.Client.Specs;

/// <summary>
/// P10.5e-2 — Pure helper that mirrors
/// <c>DrawingsService.CanActAs</c> for client-side chip UI gating
/// ONLY. The server is the source of truth — every operator-initiated
/// decide goes through <c>DrawingsApiController.Decide</c> which calls
/// the legacy <see cref="DrawingsService.CanActAs"/> again. Mirroring
/// it here is a UX optimisation so the chip is greyed-out before the
/// operator wastes a tap on a doomed request.
///
/// 4-case truth table (mirrors <c>DrawingsService.cs:351-369</c>):
///   - Admin: any chip → ✓
///   - Engineer + Department=npi: Npi chip only → ✓
///   - Engineer + Department=production: Production chip only → ✓
///   - Engineer + Department=qc: Qc chip only → ✓
///   - Supervisor (any dept): Production chip only → ✓
///   - Anyone else: ✗
///
/// Department comparison is case-insensitive; null/empty dept is
/// treated as no-match (consistent with the legacy guard).
/// </summary>
public static class DrawingsApprovalGateVm
{
    public enum ChipAvailability
    {
        /// <summary>Operator can click → opens decide modal.</summary>
        Actionable,

        /// <summary>Already decided (Approved / Rejected). The chip
        /// still renders + may be re-decided per D-5c semantics
        /// (legacy DecideAsync allows overwrite).</summary>
        AlreadyDecided,

        /// <summary>Operator's Role / Department doesn't match. Chip
        /// renders read-only with a tooltip explaining the gate.</summary>
        NotAuthorized,

        /// <summary>Version is in a terminal state (Superseded) so no
        /// decide is allowed regardless of role.</summary>
        VersionLocked,
    }

    /// <summary>
    /// Compute whether the supplied operator can click the chip for
    /// <paramref name="chip"/> on a version with the given
    /// <paramref name="versionStatus"/> + current approval
    /// <paramref name="approvalStatus"/>.
    ///
    /// <para>
    /// Returns <see cref="ChipAvailability.VersionLocked"/> first when
    /// the version is Superseded (server refuses regardless of role).
    /// Then checks <see cref="ChipAvailability.AlreadyDecided"/> —
    /// re-decide IS allowed per D-5c semantics but the UI flags the
    /// state distinctly so operators can choose to re-click vs leave
    /// alone. Finally falls through to the CanActAs truth table.
    /// </para>
    /// </summary>
    public static ChipAvailability ResolveAvailability(
        string actorRole,
        string? actorDepartment,
        DrawingApprovalRole chip,
        DrawingVersionStatus versionStatus,
        DrawingApprovalStatus approvalStatus)
    {
        if (versionStatus == DrawingVersionStatus.Superseded)
            return ChipAvailability.VersionLocked;

        if (!CanActAs(actorRole, actorDepartment, chip))
            return ChipAvailability.NotAuthorized;

        if (approvalStatus != DrawingApprovalStatus.Pending)
            return ChipAvailability.AlreadyDecided;

        return ChipAvailability.Actionable;
    }

    /// <summary>
    /// Server-side <c>CanActAs</c> truth table, ported verbatim.
    /// Lives here as a pure helper so xUnit can pin the 4 cases
    /// independently of the orchestrator (and we never re-implement
    /// the gate — same code shape on both sides).
    /// </summary>
    public static bool CanActAs(string actorRole, string? actorDepartment, DrawingApprovalRole chip)
    {
        if (string.Equals(actorRole, "Admin", StringComparison.OrdinalIgnoreCase))
            return true;

        var dept = (actorDepartment ?? "").Trim().ToLowerInvariant();
        return chip switch
        {
            DrawingApprovalRole.Npi =>
                string.Equals(actorRole, "Engineer", StringComparison.OrdinalIgnoreCase)
                && dept == "npi",
            DrawingApprovalRole.Production =>
                (string.Equals(actorRole, "Engineer", StringComparison.OrdinalIgnoreCase) && dept == "production")
                || string.Equals(actorRole, "Supervisor", StringComparison.OrdinalIgnoreCase),
            DrawingApprovalRole.Qc =>
                string.Equals(actorRole, "Engineer", StringComparison.OrdinalIgnoreCase)
                && dept == "qc",
            _ => false,
        };
    }

    /// <summary>VN tooltip for a NotAuthorized chip — explains WHY the
    /// chip is disabled so the operator can route to the right
    /// teammate without guessing.</summary>
    public static string TooltipForNotAuthorized(DrawingApprovalRole chip) => chip switch
    {
        DrawingApprovalRole.Npi        => "Chỉ Admin hoặc Engineer Phòng NPI mới duyệt được chip này.",
        DrawingApprovalRole.Production => "Chỉ Admin, Supervisor, hoặc Engineer Phòng Sản xuất mới duyệt được chip này.",
        DrawingApprovalRole.Qc         => "Chỉ Admin hoặc Engineer Phòng QC mới duyệt được chip này.",
        _                              => "Tài khoản không có quyền action chip này.",
    };
}
