#!/usr/bin/env bash
# P10.7a-1.4 — contract §10.6.2 CI grep audit. Coarse-grain check
# that every WO-mutating source file references BOTH:
#   - an audit-emit call (`_audit.EmitAsync` or
#     `AuditEmitHelper.EmitMesAsync`), AND
#   - an If-Match / RowVersion concurrency gate (`IfMatch`,
#     `RowVersion`, `state_conflict`, or `WoStateConflict`).
#
# Coarse-grain is intentional: a regression where someone adds a new
# admin endpoint that mutates a WO without either of these calls
# fails the gate. False positives (e.g. a file that mutates a WO via
# a helper that itself emits + checks) get an inline
# `// P10.7-audit-exempt: <reason>` comment to acknowledge.
#
# Exit code:
#   0 — every WO-mutating file has both pairs (or is exempted).
#   1 — at least one finding.
#
# Usage:
#   bash CCL-MES-Hybrid/scripts/audit-state-machine-emits.sh
#   bash CCL-MES-Hybrid/scripts/audit-state-machine-emits.sh --verbose

set -u
set +e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HYBRID_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$HYBRID_ROOT/.." && pwd)"

VERBOSE=0
for arg in "$@"; do
    [[ "$arg" == "--verbose" ]] && VERBOSE=1
done

# Scope: every .cs file that touches WO state mutation. Today the
# writers are: legacy WorkOrderService + the Hybrid Api
# WorkOrdersController (advance) + AdminWorkOrdersController (P10.7a-2.2
# force-phase). Every new mutation entry-point lands here.
SCAN_PATHS=(
    "$REPO_ROOT/src/CCL.MES.Application/Services/WorkOrderService.cs"
    "$HYBRID_ROOT/src/CCL.MES.Api/Controllers/WorkOrdersController.cs"
    "$HYBRID_ROOT/src/CCL.MES.Api/Controllers/AdminWorkOrdersController.cs"
)

EMIT_RE='_audit\.EmitAsync|AuditEmitHelper\.EmitMesAsync|audit\.EmitAsync'
RV_RE='IfMatch|RowVersion|state_conflict|WoStateConflict'
MUTATION_RE='SaveChangesAsync'

FINDINGS=()
TOTAL=0
OK=0

for src in "${SCAN_PATHS[@]}"; do
    rel="${src#$REPO_ROOT/}"
    if [[ ! -f "$src" ]]; then
        FINDINGS+=("MISSING_FILE     $rel")
        continue
    fi

    has_mutation=0
    has_emit=0
    has_rv=0
    has_exempt=0

    grep -qE "$MUTATION_RE" "$src" && has_mutation=1
    grep -qE "$EMIT_RE" "$src" && has_emit=1
    grep -qE "$RV_RE" "$src" && has_rv=1
    grep -q "P10.7-audit-exempt" "$src" && has_exempt=1

    if [[ $has_mutation -eq 0 ]]; then
        [[ $VERBOSE -eq 1 ]] && echo "  · $rel — no SaveChangesAsync; skipped"
        continue
    fi

    TOTAL=$((TOTAL + 1))
    if [[ $has_emit -eq 1 && $has_rv -eq 1 ]]; then
        OK=$((OK + 1))
        [[ $VERBOSE -eq 1 ]] && echo "  ✓ $rel"
        continue
    fi
    if [[ $has_exempt -eq 1 ]]; then
        OK=$((OK + 1))
        [[ $VERBOSE -eq 1 ]] && echo "  ✓ $rel (exempt)"
        continue
    fi
    if [[ $has_emit -eq 0 ]]; then
        FINDINGS+=("MISSING_EMIT     $rel")
    fi
    if [[ $has_rv -eq 0 ]]; then
        FINDINGS+=("MISSING_RV_GATE  $rel")
    fi
done

echo ""
echo "═════════════════════════════════════════════════════════════"
echo "  audit-state-machine-emits — P10.7a-1.4 CI grep audit"
echo "═════════════════════════════════════════════════════════════"
echo "  Mutating files scanned : $TOTAL"
echo "  Both emit + RV check   : $OK"
echo "  Findings               : ${#FINDINGS[@]}"
echo ""
if [[ ${#FINDINGS[@]} -gt 0 ]]; then
    for f in "${FINDINGS[@]}"; do
        echo "    - $f"
    done
    echo ""
    echo "  If intentional (e.g. mutation goes through a helper that"
    echo "  itself emits + checks), add a // P10.7-audit-exempt: <reason>"
    echo "  comment inside the file and re-run."
    exit 1
fi
echo "  ✓ Clean."
exit 0
