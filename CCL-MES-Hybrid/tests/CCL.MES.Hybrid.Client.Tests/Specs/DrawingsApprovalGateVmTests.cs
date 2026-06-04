using CCL.MES.Hybrid.Client.Specs;
using CCL.MES.Shared.Drawings;

namespace CCL.MES.Hybrid.Client.Tests.Specs;

/// <summary>
/// P10.5e-2 — Pure-helper coverage for the client-side mirror of
/// <c>DrawingsService.CanActAs</c>. Pinned in xUnit so the Razor chip
/// UI can rely on the gate rules without re-deriving them — and so a
/// future PR-D-5c semantic change has ONE bell to ring.
/// </summary>
public sealed class DrawingsApprovalGateVmTests
{
    // ── CanActAs 4-case truth table ─────────────────────────────────

    [Theory]
    [InlineData("Admin", "npi", DrawingApprovalRole.Npi, true)]
    [InlineData("Admin", "production", DrawingApprovalRole.Production, true)]
    [InlineData("Admin", "qc", DrawingApprovalRole.Qc, true)]
    [InlineData("Admin", "", DrawingApprovalRole.Npi, true)]
    [InlineData("Admin", null, DrawingApprovalRole.Qc, true)]
    public void Admin_can_act_for_any_chip(string role, string? dept, DrawingApprovalRole chip, bool expected)
    {
        Assert.Equal(expected, DrawingsApprovalGateVm.CanActAs(role, dept, chip));
    }

    [Theory]
    [InlineData("Engineer", "npi", DrawingApprovalRole.Npi, true)]
    [InlineData("Engineer", "NPI", DrawingApprovalRole.Npi, true)]       // case insensitive
    [InlineData("Engineer", "  npi  ", DrawingApprovalRole.Npi, true)]    // trim
    [InlineData("Engineer", "production", DrawingApprovalRole.Npi, false)]
    [InlineData("Engineer", "qc", DrawingApprovalRole.Npi, false)]
    [InlineData("Engineer", "", DrawingApprovalRole.Npi, false)]
    [InlineData("Engineer", null, DrawingApprovalRole.Npi, false)]
    public void Engineer_npi_only_passes_Npi_chip(string role, string? dept, DrawingApprovalRole chip, bool expected)
    {
        Assert.Equal(expected, DrawingsApprovalGateVm.CanActAs(role, dept, chip));
    }

    [Theory]
    [InlineData("Engineer", "production", DrawingApprovalRole.Production, true)]
    [InlineData("Supervisor", "npi", DrawingApprovalRole.Production, true)]     // Supervisor any dept
    [InlineData("Supervisor", null, DrawingApprovalRole.Production, true)]
    [InlineData("Supervisor", "production", DrawingApprovalRole.Npi, false)]
    [InlineData("Supervisor", "qc", DrawingApprovalRole.Qc, false)]
    [InlineData("Engineer", "production", DrawingApprovalRole.Qc, false)]
    public void Production_chip_two_paths_Engineer_plus_dept_or_Supervisor(string role, string? dept, DrawingApprovalRole chip, bool expected)
    {
        Assert.Equal(expected, DrawingsApprovalGateVm.CanActAs(role, dept, chip));
    }

    [Theory]
    [InlineData("Engineer", "qc", DrawingApprovalRole.Qc, true)]
    [InlineData("Engineer", "QC", DrawingApprovalRole.Qc, true)]
    [InlineData("Engineer", "production", DrawingApprovalRole.Qc, false)]
    [InlineData("Engineer", "npi", DrawingApprovalRole.Qc, false)]
    public void Engineer_qc_only_passes_Qc_chip(string role, string? dept, DrawingApprovalRole chip, bool expected)
    {
        Assert.Equal(expected, DrawingsApprovalGateVm.CanActAs(role, dept, chip));
    }

    [Theory]
    [InlineData("Viewer", "production")]
    [InlineData("Operator", "qc")]
    [InlineData("", "npi")]
    [InlineData(null, "production")]
    public void Unknown_role_fails_all_chips(string? role, string dept)
    {
        Assert.False(DrawingsApprovalGateVm.CanActAs(role ?? "", dept, DrawingApprovalRole.Npi));
        Assert.False(DrawingsApprovalGateVm.CanActAs(role ?? "", dept, DrawingApprovalRole.Production));
        Assert.False(DrawingsApprovalGateVm.CanActAs(role ?? "", dept, DrawingApprovalRole.Qc));
    }

    // ── ResolveAvailability state machine ───────────────────────────

    [Fact]
    public void Superseded_version_locks_chip_regardless_of_role()
    {
        var result = DrawingsApprovalGateVm.ResolveAvailability(
            "Admin", "npi", DrawingApprovalRole.Npi,
            DrawingVersionStatus.Superseded, DrawingApprovalStatus.Pending);
        Assert.Equal(DrawingsApprovalGateVm.ChipAvailability.VersionLocked, result);
    }

    [Fact]
    public void Pending_actionable_when_operator_authorized()
    {
        var result = DrawingsApprovalGateVm.ResolveAvailability(
            "Engineer", "npi", DrawingApprovalRole.Npi,
            DrawingVersionStatus.PendingApproval, DrawingApprovalStatus.Pending);
        Assert.Equal(DrawingsApprovalGateVm.ChipAvailability.Actionable, result);
    }

    [Fact]
    public void Pending_not_authorized_when_department_mismatch()
    {
        var result = DrawingsApprovalGateVm.ResolveAvailability(
            "Engineer", "production", DrawingApprovalRole.Npi,
            DrawingVersionStatus.PendingApproval, DrawingApprovalStatus.Pending);
        Assert.Equal(DrawingsApprovalGateVm.ChipAvailability.NotAuthorized, result);
    }

    [Theory]
    [InlineData(DrawingApprovalStatus.Approved)]
    [InlineData(DrawingApprovalStatus.Rejected)]
    public void Already_decided_chip_returns_AlreadyDecided_for_authorized_operator(
        DrawingApprovalStatus approvalStatus)
    {
        var result = DrawingsApprovalGateVm.ResolveAvailability(
            "Admin", null, DrawingApprovalRole.Npi,
            DrawingVersionStatus.PendingApproval, approvalStatus);
        Assert.Equal(DrawingsApprovalGateVm.ChipAvailability.AlreadyDecided, result);
    }

    [Fact]
    public void NotAuthorized_takes_precedence_over_AlreadyDecided()
    {
        var result = DrawingsApprovalGateVm.ResolveAvailability(
            "Engineer", "qc", DrawingApprovalRole.Production,
            DrawingVersionStatus.PendingApproval, DrawingApprovalStatus.Approved);
        Assert.Equal(DrawingsApprovalGateVm.ChipAvailability.NotAuthorized, result);
    }

    [Fact]
    public void VersionLocked_takes_precedence_over_everything()
    {
        var result = DrawingsApprovalGateVm.ResolveAvailability(
            "Admin", null, DrawingApprovalRole.Npi,
            DrawingVersionStatus.Superseded, DrawingApprovalStatus.Approved);
        Assert.Equal(DrawingsApprovalGateVm.ChipAvailability.VersionLocked, result);
    }

    // ── Tooltip VN strings ──────────────────────────────────────────

    [Theory]
    [InlineData(DrawingApprovalRole.Npi,        "NPI")]
    [InlineData(DrawingApprovalRole.Production, "Sản xuất")]
    [InlineData(DrawingApprovalRole.Qc,         "QC")]
    public void TooltipForNotAuthorized_carries_role_name(DrawingApprovalRole chip, string expectedDept)
    {
        var tip = DrawingsApprovalGateVm.TooltipForNotAuthorized(chip);
        Assert.Contains(expectedDept, tip);
    }
}
