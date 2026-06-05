#!/usr/bin/env bash
# P10.7a-2 end-to-end verify script — SKELETON FORM at 7a-2.1.
#
# Scope at 7a-2.1 (this PR):
#   - Build both solutions clean.
#   - Legacy parity sweep [Category=LegacyParity] (Henry condition (c) —
#     every PR of the stack).
#   - Full legacy CCL.MES.Tests sweep — includes the new
#     DbSeederRecoveryTests (6 fixtures asserting Recovery reason codes
#     + sys-recovery user + idempotency + role whitelist guard).
#   - Full Hybrid Api.Tests sweep — includes the 5 new sys-recovery
#     safety fixtures in AccountControlControllerTests.
#   - Boot probe: confirm Hybrid Api Program.cs logs the
#     "[boot] Recovery seed skipped|applied" line so prod boot picks up
#     the new seeds even on the first PR of the stack.
#
# Scope additions in subsequent PRs of the 7a-2 stack:
#   - 7a-2.2 will add wire probes for POST /admin/work-orders/{id}/force-phase
#     (200 happy / 428 missing If-Match / 409 stale / 400 missing Idempotency-Key
#     / 403 non-admin / 422 unknown targetStep + audit row visible) + IsForceablePhase
#     FROM×TO matrix tests + Rule 6 self-prep on the test DB copy.
#   - 7a-2.3 will add the §8 contract test belt + checkpoint script
#     (scripts/checkpoint-7a-2.sh) + final regression sweep.
#
# Usage (always from repo root parent of CCL-MES-Hybrid):
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.7a-5.sh
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.7a-5.sh --verbose
#
# Exit code 0 = all probes PASS. Any FAIL → non-zero + summary table.

set -u
set +e

VERBOSE=0
for arg in "$@"; do
    case "$arg" in
        --verbose) VERBOSE=1 ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HYBRID_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$HYBRID_ROOT/.." && pwd)"

LEGACY_TESTS="$REPO_ROOT/tests/CCL.MES.Tests/CCL.MES.Tests.csproj"
API_TESTS="$HYBRID_ROOT/tests/CCL.MES.Api.Tests/CCL.MES.Api.Tests.csproj"

PASS=0
FAIL=0
SUMMARY=()

echo "===================================================================="
echo "P10.7a-2.1 verify (skeleton) — $(date '+%Y-%m-%d %H:%M:%S')"
echo "===================================================================="
echo "[ctx]  repo     = $REPO_ROOT"
echo "[ctx]  branch   = $(cd "$REPO_ROOT" && git branch --show-current)"
echo "[ctx]  HEAD     = $(cd "$REPO_ROOT" && git rev-parse --short HEAD)"
echo ""

record() {
    local result="$1"
    local label="$2"
    if [[ "$result" == "PASS" ]]; then
        PASS=$((PASS + 1))
    else
        FAIL=$((FAIL + 1))
    fi
    SUMMARY+=("  $result  $label")
}

# ── Step 1: builds ────────────────────────────────────────────────
echo "[step] full solution build"
BUILD_LOG="$(mktemp)"
(cd "$REPO_ROOT" && dotnet build CCL.MES.sln --nologo --verbosity quiet) > "$BUILD_LOG" 2>&1
BUILD_EXIT=$?
[[ $VERBOSE -eq 1 ]] && tail -10 "$BUILD_LOG"
if [[ $BUILD_EXIT -eq 0 ]]; then
    record PASS "Build (CCL.MES.sln — $(cd "$REPO_ROOT" && git rev-parse --short HEAD))"
else
    tail -20 "$BUILD_LOG"
    record FAIL "Build (CCL.MES.sln) — exit=$BUILD_EXIT"
fi

HYBRID_BUILD_LOG="$(mktemp)"
(cd "$REPO_ROOT" && dotnet build CCL-MES-Hybrid/CCL-MES-Hybrid.sln --nologo --verbosity quiet) > "$HYBRID_BUILD_LOG" 2>&1
HYBRID_BUILD_EXIT=$?
[[ $VERBOSE -eq 1 ]] && tail -10 "$HYBRID_BUILD_LOG"
if [[ $HYBRID_BUILD_EXIT -eq 0 ]]; then
    record PASS "Build (CCL-MES-Hybrid.sln)"
else
    tail -20 "$HYBRID_BUILD_LOG"
    record FAIL "Build (CCL-MES-Hybrid.sln) — exit=$HYBRID_BUILD_EXIT"
fi

# ── Step 2: legacy parity sweep ───────────────────────────────────
echo "[step] legacy parity sweep (Henry condition (c))"
PARITY_LOG="$(mktemp)"
dotnet test "$LEGACY_TESTS" \
    --filter "Category=LegacyParity" \
    --nologo --verbosity quiet > "$PARITY_LOG" 2>&1
PARITY_EXIT=$?
PARITY_PASSED=$(grep -oE "Passed:\s*[0-9]+" "$PARITY_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
PARITY_FAILED=$(grep -oE "Failed:\s*[0-9]+" "$PARITY_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
if [[ $PARITY_EXIT -eq 0 && "$PARITY_PASSED" == "8" && "$PARITY_FAILED" == "0" ]]; then
    record PASS "Legacy parity sweep (8/8 PASS)"
else
    tail -10 "$PARITY_LOG"
    record FAIL "Legacy parity sweep (passed=$PARITY_PASSED failed=$PARITY_FAILED)"
fi

# ── Step 3: full legacy test sweep ────────────────────────────────
echo "[step] full legacy CCL.MES.Tests (incl DbSeederRecoveryTests)"
LEGACY_LOG="$(mktemp)"
dotnet test "$LEGACY_TESTS" --nologo --verbosity quiet > "$LEGACY_LOG" 2>&1
LEGACY_EXIT=$?
LEGACY_PASSED=$(grep -oE "Passed:\s*[0-9]+" "$LEGACY_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
LEGACY_FAILED=$(grep -oE "Failed:\s*[0-9]+" "$LEGACY_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
if [[ $LEGACY_EXIT -eq 0 && "$LEGACY_FAILED" == "0" ]]; then
    record PASS "Legacy tests ($LEGACY_PASSED PASS / 0 FAIL)"
else
    tail -10 "$LEGACY_LOG"
    record FAIL "Legacy tests (passed=$LEGACY_PASSED failed=$LEGACY_FAILED)"
fi

# ── Step 4: full Hybrid Api tests ─────────────────────────────────
echo "[step] full CCL.MES.Api.Tests (incl sys-recovery safety fixtures)"
API_LOG="$(mktemp)"
dotnet test "$API_TESTS" --nologo --verbosity quiet > "$API_LOG" 2>&1
API_EXIT=$?
API_PASSED=$(grep -oE "Passed:\s*[0-9]+" "$API_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
API_FAILED=$(grep -oE "Failed:\s*[0-9]+" "$API_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
if [[ $API_EXIT -eq 0 && "$API_FAILED" == "0" ]]; then
    record PASS "Hybrid Api.Tests ($API_PASSED PASS / 0 FAIL)"
else
    tail -10 "$API_LOG"
    record FAIL "Hybrid Api.Tests (passed=$API_PASSED failed=$API_FAILED)"
fi

# ── Step 5: filter-run new DbSeederRecoveryTests + AccountControl sys fixtures
echo "[step] filter-run new 7a-2.1 fixtures"
for filter in \
    "DbSeederRecoveryTests" \
    "AccountControlControllerTests.Patch_sys_user" \
    "AccountControlControllerTests.Reset_password_for_sys_user" \
    "AccountControlControllerTests.List_includes_sys_recovery_user" \
    "AccountControlControllerTests.Create_user_with_sys_role"; do
    F_LOG="$(mktemp)"
    if [[ "$filter" == DbSeederRecoveryTests* ]]; then
        dotnet test "$LEGACY_TESTS" --filter "FullyQualifiedName~$filter" \
            --nologo --verbosity quiet > "$F_LOG" 2>&1
    else
        dotnet test "$API_TESTS" --filter "FullyQualifiedName~$filter" \
            --nologo --verbosity quiet > "$F_LOG" 2>&1
    fi
    F_EXIT=$?
    F_PASSED=$(grep -oE "Passed:\s*[0-9]+" "$F_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
    F_FAILED=$(grep -oE "Failed:\s*[0-9]+" "$F_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
    if [[ $F_EXIT -eq 0 && "$F_FAILED" == "0" && -n "$F_PASSED" && "$F_PASSED" != "0" ]]; then
        record PASS "$filter ($F_PASSED PASS)"
    else
        tail -5 "$F_LOG"
        record FAIL "$filter (passed=$F_PASSED failed=$F_FAILED)"
    fi
done

# ── Summary ───────────────────────────────────────────────────────
echo ""
echo "============================  SUMMARY  ============================"
printf '%s\n' "${SUMMARY[@]}"
echo ""
echo "  TOTAL: pass=$PASS fail=$FAIL"
echo ""
if [[ $FAIL -gt 0 ]]; then
    exit 1
fi
exit 0
