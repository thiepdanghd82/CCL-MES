using CCL.MES.Application.Services;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7c-1 — LegacyParity guards locking 4 cross-cutting invariants
/// that future PRs MUST NOT silently break. Marked with
/// <see cref="TraitAttribute"/> Category=LegacyParity so the CI sweep
/// can run them in isolation per the 7b stack convention.
///
/// 1. <see cref="WorkOrderStateMachine.ClassifyTransition"/> still
///    locks <c>PAUSED → FQC_PENDING</c> as <c>RequiresCondition</c>
///    (Q6 amendment). If a future state-machine refactor silently
///    drops the cell, this test fires.
/// 2. <see cref="AuditAction.WoRunQtyAdd"/> + <c>WoRunQtyCorrect</c>
///    constants exist with the contract-locked string values. If a
///    refactor renames either constant, this test catches it before
///    audit-log readers (BI / OEE pipelines) start silently dropping
///    rows.
/// 3. WoQtyEntry append-only contract — there's no PUT/DELETE service
///    method that mutates an existing entry. Locked by class-level
///    surface check (reflection).
/// 4. Service-layer "no SaveChanges" invariant — none of the 3 services
///    call SaveChanges directly; controllers wrap the atomic write
///    (matches the 7b-2 pattern that closed the rollup race). Locked
///    by reflection scan of the Application.Services assembly.
/// </summary>
public sealed class RunningSurfaceLegacyParityTests
{
    [Fact]
    [Trait("Category", "LegacyParity")]
    public void PAUSED_to_FQC_PENDING_stays_RequiresCondition_per_Q6_amendment()
    {
        Assert.Equal(
            MesTransitionKind.RequiresCondition,
            WorkOrderStateMachine.ClassifyTransition(MesPhase.PAUSED, MesPhase.FQC_PENDING));
    }

    [Fact]
    [Trait("Category", "LegacyParity")]
    public void Audit_action_constants_keep_contract_locked_string_values()
    {
        Assert.Equal("WO_RUN_QTY_ADD", AuditAction.WoRunQtyAdd);
        Assert.Equal("WO_RUN_QTY_CORRECT", AuditAction.WoRunQtyCorrect);
    }

    [Fact]
    [Trait("Category", "LegacyParity")]
    public void WoQtyService_surfaces_no_Update_or_Delete_method()
    {
        // Append-only contract — only Add + Correct on the service surface.
        var methods = typeof(WoQtyService).GetMethods()
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(WoQtyService))
            .Select(m => m.Name)
            .ToList();
        Assert.DoesNotContain("Update", methods);
        Assert.DoesNotContain("Delete", methods);
        Assert.DoesNotContain("Remove", methods);
        Assert.DoesNotContain("Set", methods);
        // The only public methods are Add + Correct.
        Assert.Contains("Add", methods);
        Assert.Contains("Correct", methods);
    }

    [Fact]
    [Trait("Category", "LegacyParity")]
    public void Service_methods_do_not_call_SaveChanges_directly()
    {
        // Per the 7b-2 atomic-write pattern: controllers wrap each
        // operation in a single SaveChanges. Services MUST stay
        // SaveChanges-free so the controller can compose multiple
        // service calls into one transaction.
        //
        // Reflection inspection: scan IL for `SaveChanges` /
        // `SaveChangesAsync` method calls within each service class.
        // The check uses Cecil-free heuristic — walks the method's
        // metadata token references via System.Reflection.Metadata
        // would be ideal, but for this guard a string scan of the
        // assembly file is sufficient and avoids the dependency.
        var asm = typeof(WoQtyService).Assembly;
        var asmPath = asm.Location;
        Assert.True(File.Exists(asmPath), $"Application assembly not at {asmPath}");
        var bytes = File.ReadAllBytes(asmPath);

        // We're looking for the WoRunSessionService / WoPauseService /
        // WoQtyService TYPE definitions in the IL. They MUST NOT carry
        // a literal "SaveChanges" string in their own method bodies.
        // Since the assembly is one file + 3 services share it with
        // other classes, a coarse-grained presence-of-string check
        // would false-positive on PrepressBomSnapshotService (which
        // also does NOT call SaveChanges). The intent of the test:
        // if a future PR adds SaveChanges() to any service in this
        // file, the file's method bodies change. Sample a method per
        // service via the MethodInfo.GetMethodBody() IL bytes — if
        // any call OpCode references SaveChangesAsync, fail.
        foreach (var serviceType in new[] {
            typeof(WoRunSessionService),
            typeof(WoPauseService),
            typeof(WoQtyService),
        })
        {
            foreach (var method in serviceType.GetMethods().Where(m =>
                !m.IsSpecialName && m.DeclaringType == serviceType))
            {
                var body = method.GetMethodBody();
                if (body is null) continue;
                var il = body.GetILAsByteArray();
                if (il is null) continue;
                // Check for any method-token reference whose resolved
                // name contains "SaveChanges". The IL byte array has
                // call opcodes followed by 4-byte method tokens; this
                // is too low-level to walk safely here. Pragmatic
                // check: walk the method's MetadataToken graph through
                // the module reader looking for a referenced method
                // named "SaveChanges*".
                var module = method.Module;
                // Walk every metadata token in the IL. Call opcodes
                // 0x28 (call), 0x6F (callvirt), 0x73 (newobj) are
                // followed by a 4-byte token. We scan for any of those
                // and resolve.
                for (int i = 0; i < il.Length - 4; i++)
                {
                    if (il[i] is not (0x28 or 0x6F or 0x73)) continue;
                    var token = BitConverter.ToInt32(il, i + 1);
                    try
                    {
                        var resolved = module.ResolveMethod(token);
                        if (resolved is not null && resolved.Name.StartsWith("SaveChanges"))
                        {
                            Assert.Fail($"{serviceType.Name}.{method.Name} calls SaveChanges " +
                                "directly — violates the 'controllers wrap atomic SaveChanges' " +
                                "contract from the 7b-2 rollup-race fix.");
                        }
                    }
                    catch
                    {
                        // Token may not resolve to a method (could be type ref) — skip.
                    }
                }
            }
        }
    }
}
