using CCL.MES.Application.Services;
using CCL.MES.Domain;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Phase 9 T1 — RBAC matrix for the 3-chip drawing approval gate.
/// Locks in the rule from PR-D-5c (CLAUDE.md / docs/PERMISSION_MATRIX
/// §4.2):
///   - Admin     → can act as any chip, no department needed.
///   - Engineer  → can act ONLY on the chip that matches their dept.
///   - Supervisor→ can act as Production chip ONLY (the legacy alias).
///   - Operator / Viewer / blank → never.
/// Department string is case-insensitive; whitespace trimmed.
///
/// Pure static helper — zero IO, zero DB, zero allocation. Each test is
/// a single Theory expansion to keep the matrix scannable.
/// </summary>
public class DrawingsService_CanActAsTests
{
    // ── Admin override (cross-chip, no dept needed) ────────────────────

    [Theory]
    [InlineData(DrawingApprovalRole.Npi)]
    [InlineData(DrawingApprovalRole.Production)]
    [InlineData(DrawingApprovalRole.Qc)]
    public void Admin_can_act_on_every_chip_without_department(DrawingApprovalRole chip)
    {
        Assert.True(DrawingsService.CanActAs("Admin", actorDepartment: null, chip));
        Assert.True(DrawingsService.CanActAs("admin", actorDepartment: "",   chip));   // case-insensitive
    }

    // ── Engineer + matching department ─────────────────────────────────

    [Theory]
    [InlineData("npi",        DrawingApprovalRole.Npi,        true)]
    [InlineData("NPI",        DrawingApprovalRole.Npi,        true)]   // case-insensitive dept
    [InlineData("  npi  ",    DrawingApprovalRole.Npi,        true)]   // whitespace trimmed
    [InlineData("production", DrawingApprovalRole.Production, true)]
    [InlineData("qc",         DrawingApprovalRole.Qc,         true)]
    public void Engineer_with_matching_department_passes(
        string dept, DrawingApprovalRole chip, bool expected)
    {
        Assert.Equal(expected, DrawingsService.CanActAs("Engineer", dept, chip));
    }

    // ── Engineer + WRONG department → reject (privilege escalation guard) ─

    [Theory]
    [InlineData("production", DrawingApprovalRole.Npi)]
    [InlineData("qc",         DrawingApprovalRole.Npi)]
    [InlineData("npi",        DrawingApprovalRole.Qc)]
    [InlineData("production", DrawingApprovalRole.Qc)]
    [InlineData("npi",        DrawingApprovalRole.Production)]
    [InlineData("qc",         DrawingApprovalRole.Production)]
    public void Engineer_with_wrong_department_rejected(
        string dept, DrawingApprovalRole chip)
    {
        Assert.False(DrawingsService.CanActAs("Engineer", dept, chip));
    }

    [Fact]
    public void Engineer_with_no_department_rejected_on_npi_chip()
    {
        Assert.False(DrawingsService.CanActAs("Engineer", null, DrawingApprovalRole.Npi));
        Assert.False(DrawingsService.CanActAs("Engineer", "",   DrawingApprovalRole.Npi));
    }

    // ── Supervisor — Production chip ONLY (legacy alias) ───────────────

    [Fact]
    public void Supervisor_can_act_on_Production_chip_without_department()
    {
        Assert.True(DrawingsService.CanActAs("Supervisor", actorDepartment: null, DrawingApprovalRole.Production));
        Assert.True(DrawingsService.CanActAs("supervisor", actorDepartment: "",   DrawingApprovalRole.Production));
    }

    [Theory]
    [InlineData(DrawingApprovalRole.Npi)]
    [InlineData(DrawingApprovalRole.Qc)]
    public void Supervisor_cannot_act_on_Npi_or_Qc_chip(DrawingApprovalRole chip)
    {
        Assert.False(DrawingsService.CanActAs("Supervisor", actorDepartment: null, chip));
        Assert.False(DrawingsService.CanActAs("Supervisor", actorDepartment: "npi", chip));
    }

    // ── Other roles — always reject ────────────────────────────────────

    [Theory]
    [InlineData("Operator",  "npi")]
    [InlineData("Operator",  "production")]
    [InlineData("Operator",  "qc")]
    [InlineData("QC",        "qc")]      // role 'QC' is for QC inspection authority, NOT drawing chips
    [InlineData("Viewer",    "npi")]
    [InlineData("",          "npi")]
    [InlineData("UNKNOWN",   "production")]
    public void Other_roles_rejected_on_every_chip(string role, string dept)
    {
        Assert.False(DrawingsService.CanActAs(role, dept, DrawingApprovalRole.Npi));
        Assert.False(DrawingsService.CanActAs(role, dept, DrawingApprovalRole.Production));
        Assert.False(DrawingsService.CanActAs(role, dept, DrawingApprovalRole.Qc));
    }
}
