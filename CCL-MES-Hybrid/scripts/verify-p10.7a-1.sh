#!/usr/bin/env bash
# P10.7a-1 end-to-end verify script. Pattern matches verify-p10.6c.sh
# (P10.6 stack). Mandatory probes Henry-required at PR-stack open:
#
#   - Build clean (Domain + Infrastructure + Web + Hybrid Api + Tests).
#   - Legacy CCL.MES.Tests sweep green (regression belt for project
#     reference into Domain — adding MesPhase/RowVersion must not break
#     any of the 336 legacy tests).
#   - Hybrid CCL.MES.Api.Tests sweep green (now ≥230 after +13
#     AuditEmitHelper tests).
#   - LEGACY PARITY FILTER — 8/8 [Category=LegacyParity] tests MUST
#     stay green for the entire 7a-1 stack (Henry condition (c)).
#   - Migration apply / Down / re-apply round-trip on a COPY of real
#     data/ccl_mes.db. Row counts before/after. SQLite trigger fires.
#     Backfill SQL correctness verified per row.
#
# Usage (always from repo root parent of CCL-MES-Hybrid):
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.7a-1.sh
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.7a-1.sh --verbose
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

# Project paths
INFRA_PROJECT="$REPO_ROOT/src/CCL.MES.Infrastructure/CCL.MES.Infrastructure.csproj"
WEB_PROJECT="$REPO_ROOT/src/CCL.MES.Web/CCL.MES.Web.csproj"
LEGACY_TESTS="$REPO_ROOT/tests/CCL.MES.Tests/CCL.MES.Tests.csproj"
API_TESTS="$HYBRID_ROOT/tests/CCL.MES.Api.Tests/CCL.MES.Api.Tests.csproj"

# Migration tracking
CURRENT_MIGRATION="20260605045839_AddWorkOrderRowVersionAndMesPhase"
PREVIOUS_MIGRATION="20260601143151_AddSpecQcCaptureAndReasonCode"

# Test DB copy paths
REAL_DB="$REPO_ROOT/data/ccl_mes.db"
TMP_DIR="$(mktemp -d -t ccl-verify-p10.7a-1-XXXXXX)"
TEST_DB="$TMP_DIR/ccl_mes_test.db"

# Tally
PASS=0
FAIL=0
SUMMARY=()

# ── Banner ────────────────────────────────────────────────────────
echo "===================================================================="
echo "P10.7a-1 verify — $(date '+%Y-%m-%d %H:%M:%S')"
echo "===================================================================="
echo "[ctx]  repo     = $REPO_ROOT"
echo "[ctx]  branch   = $(cd "$REPO_ROOT" && git branch --show-current)"
echo "[ctx]  HEAD     = $(cd "$REPO_ROOT" && git rev-parse --short HEAD)"
echo "[ctx]  test DB  = $TEST_DB (copy of $REAL_DB)"
echo "[ctx]  curr mig = $CURRENT_MIGRATION"
echo "[ctx]  prev mig = $PREVIOUS_MIGRATION"
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

# ── Step 1: full build clean ──────────────────────────────────────
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

# Hybrid solution
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

# ── Step 2: legacy parity filter (Henry condition (c)) ────────────
echo "[step] legacy parity sweep (Henry condition (c) — every PR of stack)"
PARITY_LOG="$(mktemp)"
dotnet test "$LEGACY_TESTS" \
    --filter "Category=LegacyParity" \
    --nologo --verbosity quiet > "$PARITY_LOG" 2>&1
PARITY_EXIT=$?
PARITY_PASSED=$(grep -oE "Passed:\s*[0-9]+" "$PARITY_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
PARITY_FAILED=$(grep -oE "Failed:\s*[0-9]+" "$PARITY_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
if [[ $PARITY_EXIT -eq 0 && "$PARITY_PASSED" == "8" && "$PARITY_FAILED" == "0" ]]; then
    record PASS "Legacy parity sweep (8/8 PASS — CanAdvance(wo) behavior unchanged)"
else
    tail -10 "$PARITY_LOG"
    record FAIL "Legacy parity sweep (passed=$PARITY_PASSED failed=$PARITY_FAILED exit=$PARITY_EXIT)"
fi

# ── Step 3: full legacy test sweep ────────────────────────────────
echo "[step] full legacy CCL.MES.Tests"
LEGACY_LOG="$(mktemp)"
dotnet test "$LEGACY_TESTS" --nologo --verbosity quiet > "$LEGACY_LOG" 2>&1
LEGACY_EXIT=$?
LEGACY_PASSED=$(grep -oE "Passed:\s*[0-9]+" "$LEGACY_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
LEGACY_FAILED=$(grep -oE "Failed:\s*[0-9]+" "$LEGACY_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
if [[ $LEGACY_EXIT -eq 0 && "$LEGACY_FAILED" == "0" ]]; then
    record PASS "Legacy tests ($LEGACY_PASSED PASS / $LEGACY_FAILED FAIL)"
else
    tail -10 "$LEGACY_LOG"
    record FAIL "Legacy tests (passed=$LEGACY_PASSED failed=$LEGACY_FAILED)"
fi

# ── Step 4: full Hybrid Api tests ─────────────────────────────────
echo "[step] full CCL.MES.Api.Tests"
API_LOG="$(mktemp)"
dotnet test "$API_TESTS" --nologo --verbosity quiet > "$API_LOG" 2>&1
API_EXIT=$?
API_PASSED=$(grep -oE "Passed:\s*[0-9]+" "$API_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
API_FAILED=$(grep -oE "Failed:\s*[0-9]+" "$API_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
if [[ $API_EXIT -eq 0 && "$API_FAILED" == "0" ]]; then
    record PASS "Hybrid Api.Tests ($API_PASSED PASS / $API_FAILED FAIL)"
else
    tail -10 "$API_LOG"
    record FAIL "Hybrid Api.Tests (passed=$API_PASSED failed=$API_FAILED)"
fi

# ── Step 5: filter-run canonical + projection + helper ────────────
echo "[step] filter-run new canonical state machine + projection + helper tests"
for filter in \
    "WorkOrderStateMachineCanonical" \
    "WorkOrderStateMachineProjection" \
    "AuditEmitHelperTests"; do
    F_LOG="$(mktemp)"
    if [[ "$filter" == "AuditEmitHelperTests" ]]; then
        dotnet test "$API_TESTS" --filter "FullyQualifiedName~$filter" \
            --nologo --verbosity quiet > "$F_LOG" 2>&1
    else
        dotnet test "$LEGACY_TESTS" --filter "FullyQualifiedName~$filter" \
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

# ── Step 6-A: copy real data file for migration round-trip ────────
echo "[step] migration round-trip on copy of real data/ccl_mes.db"
if [[ ! -f "$REAL_DB" ]]; then
    record FAIL "Real DB not found at $REAL_DB"
else
    cp "$REAL_DB" "$TEST_DB"
    if [[ -f "$TEST_DB" ]]; then
        BEFORE_BYTES=$(stat -f%z "$TEST_DB" 2>/dev/null || stat -c%s "$TEST_DB" 2>/dev/null)
        BEFORE_WO_COUNT=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WorkOrders;" 2>/dev/null)
        record PASS "Test DB copy ($BEFORE_BYTES bytes, $BEFORE_WO_COUNT WO rows)"

        # Self-prep (STACKED-PR-CHECKLIST Rule 6): Down test DB copy to
        # PREVIOUS_MIGRATION baseline so probes below run on known state
        # regardless of dev DB advance level. NOOP if already at baseline.
        SELF_PREP_LOG="$(mktemp)"
        dotnet ef database update "$PREVIOUS_MIGRATION" \
            --connection "Data Source=$TEST_DB" \
            --project "$INFRA_PROJECT" \
            --startup-project "$WEB_PROJECT" \
            --no-build > "$SELF_PREP_LOG" 2>&1
        if [[ $? -ne 0 ]]; then
            echo "[self-prep] FAILED to Down test DB to $PREVIOUS_MIGRATION"
            tail -15 "$SELF_PREP_LOG"
            echo "[abort] verify needs prep baseline; ensure current branch has all migration sources."
            rm -rf "$TMP_DIR"
            exit 2
        fi
        [[ $VERBOSE -eq 1 ]] && echo "[self-prep] test DB at $PREVIOUS_MIGRATION baseline"

        # Confirm columns NOT present pre-migration (we're starting from
        # a copy of the OLD schema baseline).
        COL_BEFORE=$(sqlite3 "$TEST_DB" "PRAGMA table_info(WorkOrders);" 2>/dev/null | grep -E '\|MesPhase\||\|RowVersion\|' | wc -l | tr -d ' ')
        if [[ "$COL_BEFORE" == "0" ]]; then
            record PASS "Pre-migration: MesPhase + RowVersion ABSENT (clean baseline)"
        else
            record FAIL "Pre-migration: MesPhase or RowVersion already present (col matches=$COL_BEFORE)"
        fi

        # Step 6-B: apply migration
        echo "[step] migration Up — apply via dotnet ef database update"
        UP_LOG="$(mktemp)"
        dotnet ef database update "$CURRENT_MIGRATION" \
            --connection "Data Source=$TEST_DB" \
            --project "$INFRA_PROJECT" \
            --startup-project "$WEB_PROJECT" \
            --no-build > "$UP_LOG" 2>&1
        UP_EXIT=$?
        [[ $VERBOSE -eq 1 ]] && tail -10 "$UP_LOG"
        if [[ $UP_EXIT -eq 0 ]]; then
            record PASS "Migration Up applied (exit=$UP_EXIT)"
        else
            tail -15 "$UP_LOG"
            record FAIL "Migration Up — exit=$UP_EXIT"
        fi

        # Step 6-C: verify columns + backfill + trigger
        AFTER_WO_COUNT=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WorkOrders;" 2>/dev/null)
        if [[ "$AFTER_WO_COUNT" == "$BEFORE_WO_COUNT" ]]; then
            record PASS "Row count preserved post-Up ($BEFORE_WO_COUNT == $AFTER_WO_COUNT)"
        else
            record FAIL "Row count drifted ($BEFORE_WO_COUNT → $AFTER_WO_COUNT)"
        fi

        COL_AFTER=$(sqlite3 "$TEST_DB" "PRAGMA table_info(WorkOrders);" 2>/dev/null | grep -E '\|MesPhase\||\|RowVersion\|' | wc -l | tr -d ' ')
        if [[ "$COL_AFTER" == "2" ]]; then
            record PASS "Post-Up: MesPhase + RowVersion columns PRESENT"
        else
            record FAIL "Post-Up: column count != 2 (got $COL_AFTER)"
        fi

        # Backfill: no row should have MesPhase = ''
        EMPTY_PHASE=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WorkOrders WHERE MesPhase IS NULL OR MesPhase = '';" 2>/dev/null)
        if [[ "$EMPTY_PHASE" == "0" ]]; then
            record PASS "Backfill: 0 rows with empty/null MesPhase"
        else
            record FAIL "Backfill: $EMPTY_PHASE row(s) with empty/null MesPhase"
        fi

        # Backfill distribution
        echo ""
        echo "[backfill distribution — MesPhase by count]"
        sqlite3 -column -header "$TEST_DB" "SELECT MesPhase, COUNT(*) AS n FROM WorkOrders GROUP BY MesPhase ORDER BY n DESC;" 2>/dev/null
        echo ""

        # RowVersion non-empty for all rows
        EMPTY_RV=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WorkOrders WHERE length(RowVersion) = 0;" 2>/dev/null)
        if [[ "$EMPTY_RV" == "0" ]]; then
            record PASS "Backfill: RowVersion non-empty for all $AFTER_WO_COUNT rows"
        else
            record FAIL "Backfill: $EMPTY_RV row(s) with empty RowVersion"
        fi

        # SQLite trigger exists
        TRIGGER_EXISTS=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name='WorkOrders_RowVersion_OnUpdate';" 2>/dev/null)
        if [[ "$TRIGGER_EXISTS" == "1" ]]; then
            record PASS "SQLite trigger WorkOrders_RowVersion_OnUpdate created"
        else
            record FAIL "SQLite trigger missing post-Up"
        fi

        # Trigger fires test: pick first row, capture RowVersion, UPDATE without setting RowVersion, expect new RV.
        if [[ "$AFTER_WO_COUNT" -gt 0 ]]; then
            FIRST_ID=$(sqlite3 "$TEST_DB" "SELECT Id FROM WorkOrders ORDER BY Id LIMIT 1;" 2>/dev/null)
            RV_OLD=$(sqlite3 "$TEST_DB" "SELECT hex(RowVersion) FROM WorkOrders WHERE Id = $FIRST_ID;" 2>/dev/null)
            sqlite3 "$TEST_DB" "UPDATE WorkOrders SET WoNo = WoNo WHERE Id = $FIRST_ID;" 2>/dev/null
            RV_NEW=$(sqlite3 "$TEST_DB" "SELECT hex(RowVersion) FROM WorkOrders WHERE Id = $FIRST_ID;" 2>/dev/null)
            if [[ -n "$RV_OLD" && -n "$RV_NEW" && "$RV_OLD" != "$RV_NEW" ]]; then
                record PASS "Trigger fires on UPDATE (RowVersion bumped: $RV_OLD → $RV_NEW)"
            else
                record FAIL "Trigger did NOT bump RowVersion (old=$RV_OLD new=$RV_NEW)"
            fi
        else
            echo "[step] trigger test SKIPPED (no rows in test DB)"
        fi

        # Step 6-D: Down() apply
        echo "[step] migration Down — revert to $PREVIOUS_MIGRATION"
        DOWN_LOG="$(mktemp)"
        dotnet ef database update "$PREVIOUS_MIGRATION" \
            --connection "Data Source=$TEST_DB" \
            --project "$INFRA_PROJECT" \
            --startup-project "$WEB_PROJECT" \
            --no-build > "$DOWN_LOG" 2>&1
        DOWN_EXIT=$?
        [[ $VERBOSE -eq 1 ]] && tail -10 "$DOWN_LOG"
        if [[ $DOWN_EXIT -eq 0 ]]; then
            record PASS "Migration Down applied (revert to $PREVIOUS_MIGRATION)"
        else
            tail -15 "$DOWN_LOG"
            record FAIL "Migration Down — exit=$DOWN_EXIT"
        fi

        # Down: columns + trigger gone, row count preserved
        DOWN_WO_COUNT=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WorkOrders;" 2>/dev/null)
        if [[ "$DOWN_WO_COUNT" == "$BEFORE_WO_COUNT" ]]; then
            record PASS "Post-Down: row count preserved ($DOWN_WO_COUNT)"
        else
            record FAIL "Post-Down: row count $DOWN_WO_COUNT != $BEFORE_WO_COUNT"
        fi

        COL_DOWN=$(sqlite3 "$TEST_DB" "PRAGMA table_info(WorkOrders);" 2>/dev/null | grep -E '\|MesPhase\||\|RowVersion\|' | wc -l | tr -d ' ')
        if [[ "$COL_DOWN" == "0" ]]; then
            record PASS "Post-Down: MesPhase + RowVersion ABSENT (Down dropped both)"
        else
            record FAIL "Post-Down: column lingered (count=$COL_DOWN)"
        fi

        TRIGGER_DOWN=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name='WorkOrders_RowVersion_OnUpdate';" 2>/dev/null)
        if [[ "$TRIGGER_DOWN" == "0" ]]; then
            record PASS "Post-Down: trigger removed"
        else
            record FAIL "Post-Down: trigger lingered"
        fi

        # Step 6-E: re-apply Up()
        echo "[step] migration re-apply Up — idempotency check"
        REAPPLY_LOG="$(mktemp)"
        dotnet ef database update "$CURRENT_MIGRATION" \
            --connection "Data Source=$TEST_DB" \
            --project "$INFRA_PROJECT" \
            --startup-project "$WEB_PROJECT" \
            --no-build > "$REAPPLY_LOG" 2>&1
        REAPPLY_EXIT=$?
        [[ $VERBOSE -eq 1 ]] && tail -10 "$REAPPLY_LOG"
        if [[ $REAPPLY_EXIT -eq 0 ]]; then
            record PASS "Migration re-Up succeeded after Down (apply/down/re-apply round-trip clean)"
        else
            tail -15 "$REAPPLY_LOG"
            record FAIL "Migration re-Up — exit=$REAPPLY_EXIT"
        fi

        REAPPLY_PHASE=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WorkOrders WHERE MesPhase IS NULL OR MesPhase = '';" 2>/dev/null)
        if [[ "$REAPPLY_PHASE" == "0" ]]; then
            record PASS "Re-Up: backfill re-applied cleanly (0 empty MesPhase)"
        else
            record FAIL "Re-Up: $REAPPLY_PHASE rows with empty MesPhase"
        fi
    fi
fi

# ── Step 7: temp dir cleanup ──────────────────────────────────────
echo ""
echo "[cleanup] removing $TMP_DIR"
rm -rf "$TMP_DIR"

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
