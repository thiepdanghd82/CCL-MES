using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7b-1 — legacy parity guard (locked, Trait=LegacyParity).
///
/// Henry condition (c) — every PR of the 7b stack runs this filter:
/// the new row-level PREPRESS surfaces MUST NOT alter the legacy
/// WorkOrderStateMachine.CanAdvance(PrePressCheck → OpSetting)
/// behavior. The 7a-1 + 7a-2 stacks left CanAdvance() reading
/// (wo.ProductRevisionId is not null && wo.MaterialsReady) for the
/// legacy gate. 7b-1 is ADDITIVE: PrepressBomSnapshotService
/// materialises rows; the rollup helper computes a new bool; but the
/// underlying WorkOrder.MaterialsReady column + CanAdvance predicate
/// stay byte-identical.
///
/// These fixtures are the alarm bell: if a future PR refactors the
/// legacy bool out of CanAdvance or removes WorkOrder.MaterialsReady,
/// these fail loudly + the regression is caught before merge.
/// </summary>
public sealed class PrepressLegacyParityTests
{
    [Fact]
    [Trait("Category", "LegacyParity")]
    public void Legacy_CanAdvance_PrePressCheck_to_OpSetting_still_reads_MaterialsReady_bool()
    {
        // Mirrors the 7a-1 parity test pattern — no new helper between
        // legacy bool and the gate. If a future PR inserts a row-level
        // check here, this fails and the contract has to be re-opened.
        var wo = new WorkOrder
        {
            CurrentStep = ProcessStepCode.PrePressCheck,
            ProductRevisionId = 1L,
            MaterialsReady = true,
        };
        var result = WorkOrderStateMachine.CanAdvance(wo);
        Assert.True(result.Allowed);
    }

    [Fact]
    [Trait("Category", "LegacyParity")]
    public void Legacy_CanAdvance_fails_when_MaterialsReady_false_regardless_of_row_state()
    {
        // The row-level helper is intentionally NOT consulted by the
        // legacy gate. Even if we imagine all row checks would pass,
        // the gate still reads the cached bool. Caller is responsible
        // for keeping bool ↔ rows consistent via the rollup helper.
        var wo = new WorkOrder
        {
            CurrentStep = ProcessStepCode.PrePressCheck,
            ProductRevisionId = 1L,
            MaterialsReady = false,
        };
        var result = WorkOrderStateMachine.CanAdvance(wo);
        Assert.False(result.Allowed);
        Assert.Equal(WoErrorCode.RequiresSpecAndMaterials, result.Error);
    }

    [Fact]
    [Trait("Category", "LegacyParity")]
    public void Legacy_CanAdvance_fails_when_ProductRevisionId_null_regardless_of_bool()
    {
        // The other half of the legacy gate — ProductRevisionId must be
        // set. 7b does NOT change this; new row tables don't override
        // the spec-presence guard.
        var wo = new WorkOrder
        {
            CurrentStep = ProcessStepCode.PrePressCheck,
            ProductRevisionId = null,
            MaterialsReady = true,
        };
        var result = WorkOrderStateMachine.CanAdvance(wo);
        Assert.False(result.Allowed);
    }

    [Fact]
    [Trait("Category", "LegacyParity")]
    public void MaterialsReady_bool_column_default_stays_false()
    {
        // EF default. Existing WOs (pre-7b) had this default. New WOs
        // post-7b should still default false until the rollup flips it.
        var wo = new WorkOrder();
        Assert.False(wo.MaterialsReady);
    }

    [Fact]
    [Trait("Category", "LegacyParity")]
    public void Rollup_NoSnapshot_means_caller_leaves_legacy_bool_alone()
    {
        // The HasSnapshot=false path is the parity safety net: legacy
        // WOs with no child rows MUST NOT have their bool clobbered
        // to false by the rollup helper.
        var (hasSnap, _) = MaterialsReadinessRollup.Compute(null, null, null);
        Assert.False(hasSnap);
    }
}
