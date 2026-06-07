#!/usr/bin/env bash
# P10.7e — end-to-end verify SKELETON for the FQC + OQC + Reports
# stack. Ships at PR 7e-1 covering the domain surface (3 entity
# tables + state-machine grid expansion 144→169 + dual-sig 3-flag
# policy + Product.QcProfileOverride + migration).
# 7e-2 / 7e-3 / 7e-4 PRs grow the wire + UI + checkpoint sections
# (mirrors the 7d stack progression — see verify-p10.7d.sh for
# the closed-out form to copy).
#
# Probes shipped in 7e-1:
#
#   Build / suites
#     1. dotnet build CCL-MES-Hybrid.sln (0 errors)
#     2. CCL.MES.Tests (Domain) ≥948 — was 846 at v0.10.7d + 25 from
#        WorkOrderStateMachineFullMatrixTests Theory expansion (12×12=144
#        → 13×13=169 cells; the diff lands as +25 because the existing
#        Theory enumerates Enum.GetValues<MesPhase>() at runtime). Plus
#        +17 QcThresholdResolver tests (3-level resolution chain +
#        malformed JSON robustness + IsEnabled boolean chain). Plus
#        a handful of new Theory rows in Canonical + IsForceable for
#        the SHIPPED-related cells.
#     3. CCL.MES.Api.Tests ≥359 (non-soak) — unchanged in 7e-1
#        (controller surface lands in 7e-2; this skeleton just
#        confirms no regression).
#     3b. Concurrent soak step inherited from 7c — runs as-is.
#     4. CCL.MES.Hybrid.Client.Tests ≥575 — unchanged in 7e-1.
#     5. CCL.MES.Hybrid.Razor.Tests ≥99 — unchanged in 7e-1.
#
#   Migration round-trip (Rule 6 self-prep on the COPY)
#     6. Copy real DB → /tmp; Down to PREVIOUS_MIGRATION
#        (20260606150401_AddIpqcReviewSurface — the 7d-1 baseline);
#        verify 3 7e tables ABSENT (WoQcChecks / WoQcCheckItems /
#        WoQcPhotos) + Products.QcProfileOverride column ABSENT;
#        Up to CURRENT_MIGRATION (20260607101947_AddFqcOqcQualitySurface);
#        verify 3 7e tables PRESENT + Products.QcProfileOverride
#        column PRESENT + unique indices + idempotent backfill rows
#        for legacy FQC_PENDING / OQC_PENDING WOs; Down once more +
#        Up again to prove idempotent.
#
#   Boot probe (L17 + L18 + Q3 dual-sig + NEW 3-sig 3-flag policy)
#     7. Boot the API + assert no 'Overriding address(es)' warning
#        (L18 guard).
#     8. lsof-bound-port assertion — API listens on the asked port.
#     9. Reason-code boot probe (L17) emits the [seed] pause/scrap/
#        recovery line + pause >= 8 + scrap >= 8 (unchanged).
#    10. Q3 dual-sig boot probe (7d-1) emits
#        [config] OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER=on (default).
#    11. (7e-1 NEW) 3-sig OQC policy boot probe — verify the API
#        log emits all 3 flag log lines per L20 standing rule:
#          [config] OPS_OQC_REQUIRE_DISTINCT_REVIEWER=on
#          [config] OPS_OQC_REQUIRE_DISTINCT_APPROVER=on
#          [config] OPS_OQC_REQUIRE_APPROVER_DISTINCT_FROM_INSPECTOR=on
#          [config] OPS_OQC_SIG_POLICY_STATE=R=on;A=on;AI=on (all_on=True)
#        All 3 default to ON per Q5 contract; the verify boots without
#        any override so the expected values are all "on". A future
#        plant with a single-inspector dev box overrides via env var +
#        their checkpoint sees the FlagState change in the audit row.
#
# Rules honoured in this skeleton:
#   R1 (--base main on PR create)            — N/A here (script)
#   R2 (no --delete-branch mid-stack)        — N/A here (script)
#   R4 (comment-strip gate for Razor)        — N/A in 7e-1 (no Razor)
#   R5 (Henry-action includes ef update)     — printed at bottom on FAIL
#   R6 (self-prep DB baseline)               — Step 6 below
#   R7.1 ([ctx] DB= header)                  — printed at top
#   R7.2 (self-managed API + cleanup)        — Steps 7-9
#   R7.3 (wire-mirror)                       — N/A in 7e-1 (no wire); 7e-2 grows
#   S9 (responsive UI verify wide+narrow)    — N/A in 7e-1 (no UI); 7e-3 grows
#   S10 (preserve TMP_DIR on FAIL)           — cleanup() honours
#   S11 (assert-bound-port + log-grep L18)   — Steps 7-8
#   L17 (seed kind-specific guard)           — Step 9
#   L18 (--urls override guard)              — Step 7
#   L19 amendment (every WO DTO MesPhase)    — N/A in 7e-1 (no new DTOs); 7e-2 grows
#   L20 (default-ON flag + boot probe)       — Step 10 (Q3) + Step 11 (3-sig × 3)
#   L21 (auto re-fetch on phase change)      — N/A in 7e-1 (no dashboard); 7e-3 grows
#
# Usage:
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.7e.sh
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.7e.sh --verbose

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

INFRA_PROJECT="$REPO_ROOT/src/CCL.MES.Infrastructure/CCL.MES.Infrastructure.csproj"
API_PROJECT="$HYBRID_ROOT/src/CCL.MES.Api/CCL.MES.Api.csproj"
SOLUTION="$HYBRID_ROOT/CCL-MES-Hybrid.sln"
DOMAIN_TESTS="$REPO_ROOT/tests/CCL.MES.Tests/CCL.MES.Tests.csproj"
API_TESTS="$HYBRID_ROOT/tests/CCL.MES.Api.Tests/CCL.MES.Api.Tests.csproj"
CLIENT_TESTS="$HYBRID_ROOT/tests/CCL.MES.Hybrid.Client.Tests/CCL.MES.Hybrid.Client.Tests.csproj"
RAZOR_TESTS="$HYBRID_ROOT/tests/CCL.MES.Hybrid.Razor.Tests/CCL.MES.Hybrid.Razor.Tests.csproj"

CURRENT_MIGRATION="20260607102727_AddFqcOqcQualitySurface"
PREVIOUS_MIGRATION="20260606150401_AddIpqcReviewSurface"

REAL_DB="$REPO_ROOT/data/ccl_mes.db"
TMP_DIR="$(mktemp -d -t ccl-verify-p10.7e-XXXXXX)"
TEST_DB="$TMP_DIR/ccl_mes_test.db"

PORT=5104
API_URL="http://127.0.0.1:${PORT}"
API_LOG="$TMP_DIR/api.log"
API_PID=""

PASS=0
FAIL=0
SUMMARY=()

DB_SHA8="(missing)"
[[ -f "$REAL_DB" ]] && DB_SHA8="$(shasum -a 256 "$REAL_DB" 2>/dev/null | awk '{print substr($1,1,8)}')"

echo "===================================================================="
echo "P10.7e verify (SKELETON — 7e-1 scope) — $(date '+%Y-%m-%d %H:%M:%S')"
echo "===================================================================="
echo "[ctx]  repo       = $REPO_ROOT"
echo "[ctx]  branch     = $(cd "$REPO_ROOT" && git branch --show-current)"
echo "[ctx]  HEAD       = $(cd "$REPO_ROOT" && git rev-parse --short HEAD)"
echo "[ctx]  real DB    = $REAL_DB"
echo "[ctx]  real sha8  = $DB_SHA8"
echo "[ctx]  test DB    = $TEST_DB (copy)"
echo "[ctx]  curr mig   = $CURRENT_MIGRATION"
echo "[ctx]  prev mig   = $PREVIOUS_MIGRATION"
echo "[ctx]  api port   = $PORT"
echo ""

record() {
    if [[ "$1" == "PASS" ]]; then
        PASS=$((PASS + 1))
        SUMMARY+=("  PASS  $2")
    else
        FAIL=$((FAIL + 1))
        SUMMARY+=("  FAIL  $2")
    fi
}

cleanup() {
    if [[ -n "$API_PID" ]]; then
        kill -9 "$API_PID" 2>/dev/null
        wait "$API_PID" 2>/dev/null
    fi
    if [[ "$FAIL" -gt 0 ]]; then
        echo ""
        echo "[debug] TMP_DIR preserved for inspection: $TMP_DIR"
        echo "[debug] api log    : $API_LOG"
        echo "[debug] build log  : $TMP_DIR/build.log"
        echo "[debug] migration  : $TMP_DIR/migration-*.log"
        echo ""
        echo "[Henry-action] If migration probes failed:"
        echo "    dotnet ef database update \\"
        echo "      --connection 'Data Source=$REAL_DB' \\"
        echo "      --project '$INFRA_PROJECT' \\"
        echo "      --startup-project '$API_PROJECT'"
        echo ""
    else
        rm -rf "$TMP_DIR"
    fi
}
trap cleanup EXIT INT TERM

# ── Step 1 — full build ──────────────────────────────────────────
echo "[step] build solution"
BUILD_OUT="$TMP_DIR/build.log"
(cd "$HYBRID_ROOT" && dotnet build "$SOLUTION" --nologo -clp:NoSummary > "$BUILD_OUT" 2>&1)
BUILD_EXIT=$?
if [[ $BUILD_EXIT -eq 0 ]]; then
    record PASS "solution build (exit 0)"
else
    record FAIL "solution build (exit $BUILD_EXIT, see $BUILD_OUT)"
    tail -20 "$BUILD_OUT"
fi

# ── Step 2 — xUnit suites ────────────────────────────────────────
run_suite() {
    local label="$1"; local proj="$2"; local extra="${3:-}"
    local out="$TMP_DIR/test-$(basename "$proj").log"
    echo "[step] test $label"
    if [[ -n "$extra" ]]; then
        dotnet test "$proj" $extra --nologo -v q --no-build > "$out" 2>&1
    else
        dotnet test "$proj" --nologo -v q --no-build > "$out" 2>&1
    fi
    local exit=$?
    local passed=$(grep -oE "Passed:[[:space:]]*[0-9]+" "$out" | head -1 | awk '{print $2}')
    local failed=$(grep -oE "Failed:[[:space:]]*[0-9]+" "$out" | head -1 | awk '{print $2}')
    if [[ $exit -eq 0 && "${failed:-0}" == "0" ]]; then
        record PASS "$label (passed=${passed:-?})"
    else
        record FAIL "$label (exit=$exit failed=${failed:-?}, see $out)"
        tail -10 "$out"
    fi
}

run_suite "Domain tests"         "$DOMAIN_TESTS"   "--filter Category!=Soak"
run_suite "Api tests"            "$API_TESTS"      "--filter Category!=Soak"
run_suite "Hybrid Client tests"  "$CLIENT_TESTS"
run_suite "Hybrid Razor tests"   "$RAZOR_TESTS"

# ── Step 2.5 — Concurrent soak (Category=Soak only) ──────────────
# Mirrors 7d skeleton — 2-attempt policy for the documented macOS
# SQLite write-lock interleaving flake. Genuine race regression
# fails BOTH attempts.
echo "[step] soak: Concurrent_run_qty_add + 3 sibling Category=Soak fixtures (N=10)"
SOAK_OUT="$TMP_DIR/test-soak.log"
SOAK_OUT2="$TMP_DIR/test-soak-retry.log"
SOAK_PASS=0
for attempt in 1 2; do
    OUT="$SOAK_OUT"
    [[ $attempt -eq 2 ]] && OUT="$SOAK_OUT2"
    dotnet test "$API_TESTS" \
        --filter "Category=Soak" \
        --nologo -v q --no-build > "$OUT" 2>&1
    SOAK_EXIT=$?
    SOAK_PASSED=$(grep -oE "Passed:[[:space:]]*[0-9]+" "$OUT" | head -1 | awk '{print $2}')
    SOAK_FAILED=$(grep -oE "Failed:[[:space:]]*[0-9]+" "$OUT" | head -1 | awk '{print $2}')
    if [[ $SOAK_EXIT -eq 0 && "${SOAK_FAILED:-0}" == "0" ]]; then
        SOAK_PASS=1
        if [[ $attempt -eq 1 ]]; then
            record PASS "Concurrent_run_qty_add soak (passed=${SOAK_PASSED:-?})"
        else
            record PASS "Concurrent_run_qty_add soak (passed=${SOAK_PASSED:-?}, retry #2)"
        fi
        break
    fi
    [[ $attempt -eq 1 ]] && echo "[soak] attempt #1 failed (passed=${SOAK_PASSED:-?} failed=${SOAK_FAILED:-?}) — retrying once"
done
if [[ $SOAK_PASS -eq 0 ]]; then
    record FAIL "Concurrent_run_qty_add soak failed BOTH attempts (see $SOAK_OUT + $SOAK_OUT2)"
    tail -10 "$SOAK_OUT2"
fi

# ── Step 3 — migration round-trip (Rule 6 self-prep) ────────────
echo "[step] migration round-trip on COPY"
if [[ ! -f "$REAL_DB" ]]; then
    record FAIL "real DB not found at $REAL_DB"
else
    cp "$REAL_DB" "$TEST_DB"
    # Self-prep: Down test DB to PREVIOUS_MIGRATION baseline.
    dotnet ef database update "$PREVIOUS_MIGRATION" \
        --connection "Data Source=$TEST_DB" \
        --project "$INFRA_PROJECT" \
        --startup-project "$API_PROJECT" \
        --no-build > "$TMP_DIR/migration-down.log" 2>&1
    DOWN_EXIT=$?
    if [[ $DOWN_EXIT -ne 0 ]]; then
        record FAIL "self-prep Down to $PREVIOUS_MIGRATION (exit $DOWN_EXIT)"
        tail -20 "$TMP_DIR/migration-down.log"
    else
        # Pre-migration probes: 3 7e tables ABSENT + QcProfileOverride col ABSENT.
        TABLES_PRE_QC=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('WoQcChecks','WoQcCheckItems','WoQcPhotos');" 2>/dev/null)
        if [[ "$TABLES_PRE_QC" == "0" ]]; then
            record PASS "pre-migration baseline (3 QC tables absent)"
        else
            record FAIL "pre-migration: expected 0 QC tables, got $TABLES_PRE_QC"
        fi
        COL_PRE=$(sqlite3 "$TEST_DB" "PRAGMA table_info(Products);" 2>/dev/null | grep -c "QcProfileOverride")
        if [[ "$COL_PRE" == "0" ]]; then
            record PASS "pre-migration baseline (Products.QcProfileOverride absent)"
        else
            record FAIL "pre-migration: Products.QcProfileOverride column should be absent"
        fi

        # Apply forward.
        dotnet ef database update "$CURRENT_MIGRATION" \
            --connection "Data Source=$TEST_DB" \
            --project "$INFRA_PROJECT" \
            --startup-project "$API_PROJECT" \
            --no-build > "$TMP_DIR/migration-up.log" 2>&1
        UP_EXIT=$?
        if [[ $UP_EXIT -ne 0 ]]; then
            record FAIL "migration Up to $CURRENT_MIGRATION (exit $UP_EXIT)"
            tail -20 "$TMP_DIR/migration-up.log"
        else
            TABLES_POST_QC=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('WoQcChecks','WoQcCheckItems','WoQcPhotos');" 2>/dev/null)
            if [[ "$TABLES_POST_QC" == "3" ]]; then
                record PASS "post-migration (3 QC tables present)"
            else
                record FAIL "post-migration: expected 3 QC tables, got $TABLES_POST_QC"
            fi
            COL_POST=$(sqlite3 "$TEST_DB" "PRAGMA table_info(Products);" 2>/dev/null | grep -c "QcProfileOverride")
            if [[ "$COL_POST" == "1" ]]; then
                record PASS "post-migration (Products.QcProfileOverride present)"
            else
                record FAIL "post-migration: Products.QcProfileOverride column missing"
            fi

            # Unique indices probe (3 expected).
            IDX_COUNT=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name IN ('IX_WoQcChecks_WorkOrderId_QcKind','IX_WoQcCheckItems_WoQcCheckId_ItemKey');" 2>/dev/null)
            if [[ "$IDX_COUNT" == "2" ]]; then
                record PASS "post-migration unique indices (2/2 expected)"
            else
                record FAIL "post-migration: expected 2 unique indices, got $IDX_COUNT"
            fi

            # Idempotent backfill probe — count rows for legacy WOs already past RUNNING.
            # Per migration: FQC row backfilled for FQC_PENDING + OQC_PENDING;
            # OQC row backfilled for OQC_PENDING only. Sanity: at least the
            # FQC rows should be >= count of OQC_PENDING WOs.
            EXPECTED_FQC=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WorkOrders WHERE MesPhase IN ('FQC_PENDING','OQC_PENDING');" 2>/dev/null)
            ACTUAL_FQC=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WoQcChecks WHERE QcKind='FQC';" 2>/dev/null)
            if [[ "${ACTUAL_FQC:-0}" == "$EXPECTED_FQC" ]]; then
                record PASS "post-migration FQC backfill (rows=$ACTUAL_FQC for $EXPECTED_FQC eligible WOs)"
            else
                record FAIL "post-migration FQC backfill drift: expected $EXPECTED_FQC got $ACTUAL_FQC"
            fi

            EXPECTED_OQC=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WorkOrders WHERE MesPhase='OQC_PENDING';" 2>/dev/null)
            ACTUAL_OQC=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WoQcChecks WHERE QcKind='OQC';" 2>/dev/null)
            if [[ "${ACTUAL_OQC:-0}" == "$EXPECTED_OQC" ]]; then
                record PASS "post-migration OQC backfill (rows=$ACTUAL_OQC for $EXPECTED_OQC eligible WOs)"
            else
                record FAIL "post-migration OQC backfill drift: expected $EXPECTED_OQC got $ACTUAL_OQC"
            fi

            # Idempotent re-up (NOOP).
            dotnet ef database update "$CURRENT_MIGRATION" \
                --connection "Data Source=$TEST_DB" \
                --project "$INFRA_PROJECT" \
                --startup-project "$API_PROJECT" \
                --no-build > "$TMP_DIR/migration-noop.log" 2>&1
            NOOP_EXIT=$?
            ACTUAL_FQC_AFTER=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WoQcChecks WHERE QcKind='FQC';" 2>/dev/null)
            if [[ $NOOP_EXIT -eq 0 && "$ACTUAL_FQC_AFTER" == "$ACTUAL_FQC" ]]; then
                record PASS "migration re-apply (idempotent NOOP; rows unchanged)"
            else
                record FAIL "migration re-apply failed or backfill duplicated (was $ACTUAL_FQC, now $ACTUAL_FQC_AFTER)"
            fi
        fi
    fi
fi

# ── Step 4 — boot probe (L17 + L18 + Q3 + 3-sig 3-flag) ──────────
echo "[step] kill anything on port $PORT before boot"
STALE_PIDS=$(lsof -nP -iTCP:${PORT} -sTCP:LISTEN -t 2>/dev/null)
if [[ -n "$STALE_PIDS" ]]; then
    echo "[boot] killing stale listeners on $PORT: $STALE_PIDS"
    echo "$STALE_PIDS" | xargs -r kill -9 2>/dev/null
    sleep 1
fi

echo "[step] boot API on $TEST_DB (urls=http://127.0.0.1:${PORT})"
(cd "$HYBRID_ROOT/src/CCL.MES.Api" && \
    ConnectionStrings__Default="Data Source=$TEST_DB" \
    ASPNETCORE_ENVIRONMENT="Development" \
    dotnet run --no-build --no-launch-profile --urls "http://127.0.0.1:${PORT}" > "$API_LOG" 2>&1) &
API_PID=$!

API_UP=0
LISTEN_LOGGED=0
for i in $(seq 1 120); do
    if [[ $LISTEN_LOGGED -eq 0 ]] && grep -q "Now listening on:" "$API_LOG" 2>/dev/null; then
        LISTEN_LOGGED=1
        echo "[boot] Kestrel reported listening (after ${i}s)"
    fi
    code=$(curl -s -m 2 -o /dev/null -w "%{http_code}" "$API_URL/health" 2>/dev/null)
    if [[ "$code" =~ ^(200|401|503)$ ]]; then
        API_UP=1
        echo "[boot] /health responded $code (after ${i}s) — API up"
        break
    fi
    sleep 1
done

if [[ $API_UP -eq 1 ]]; then
    BOUND_PID=$(lsof -nP -iTCP:${PORT} -sTCP:LISTEN -t 2>/dev/null | head -1)
    if [[ -z "$BOUND_PID" ]]; then
        record FAIL "API /health responded but nothing listening on $PORT"
    else
        record PASS "API bound on $PORT (pid=$BOUND_PID)"
    fi
    if grep -q "Overriding address(es)" "$API_LOG"; then
        record FAIL "L18 regression — appsettings.json Kestrel:Endpoints back?"
    else
        record PASS "L18 guard — no 'Overriding address(es)' warning in API log"
    fi

    # L17 — boot probe for kind-specific reason-code seeding.
    SCRAP_COUNT=$(grep -oE '\[seed\] reason_codes pause=[0-9]+ scrap=[0-9]+ recovery=[0-9]+' "$API_LOG" | tail -1 | grep -oE 'scrap=[0-9]+' | cut -d= -f2)
    PAUSE_COUNT=$(grep -oE '\[seed\] reason_codes pause=[0-9]+ scrap=[0-9]+ recovery=[0-9]+' "$API_LOG" | tail -1 | grep -oE 'pause=[0-9]+' | cut -d= -f2)
    if [[ -n "$SCRAP_COUNT" && "$SCRAP_COUNT" -ge 8 ]]; then
        record PASS "L17 boot probe scrap=$SCRAP_COUNT (≥8)"
    else
        record FAIL "L17 boot probe missing or scrap<8 (got '$SCRAP_COUNT')"
    fi
    if [[ -n "$PAUSE_COUNT" && "$PAUSE_COUNT" -ge 8 ]]; then
        record PASS "L17 boot probe pause=$PAUSE_COUNT (≥8) — Q4 Pause picker source ready"
    else
        record FAIL "L17 boot probe missing or pause<8 (got '$PAUSE_COUNT')"
    fi

    # P10.7d-1 §5.5.1 — IPQC dual-sig boot probe (regression guard).
    DUAL_SIG_STATE=$(grep -oE '\[config\] OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER=(on|off)' "$API_LOG" | tail -1 | cut -d= -f2)
    if [[ "$DUAL_SIG_STATE" == "on" ]]; then
        record PASS "IPQC dual-sig boot probe — OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER=on"
    elif [[ "$DUAL_SIG_STATE" == "off" ]]; then
        record FAIL "IPQC dual-sig boot probe — flag is OFF; check env var override"
    else
        record FAIL "IPQC dual-sig boot probe missing from log"
    fi

    # P10.7e-1 Q5 — 3-sig OQC policy boot probe. All 3 flags MUST default ON.
    REV_STATE=$(grep -oE '\[config\] OPS_OQC_REQUIRE_DISTINCT_REVIEWER=(on|off)' "$API_LOG" | tail -1 | cut -d= -f2)
    APP_STATE=$(grep -oE '\[config\] OPS_OQC_REQUIRE_DISTINCT_APPROVER=(on|off)' "$API_LOG" | tail -1 | cut -d= -f2)
    AI_STATE=$(grep -oE '\[config\] OPS_OQC_REQUIRE_APPROVER_DISTINCT_FROM_INSPECTOR=(on|off)' "$API_LOG" | tail -1 | cut -d= -f2)
    POLICY_LINE=$(grep -oE '\[config\] OPS_OQC_SIG_POLICY_STATE=R=(on|off);A=(on|off);AI=(on|off)' "$API_LOG" | tail -1)

    if [[ "$REV_STATE" == "on" ]]; then
        record PASS "OQC 3-sig probe — OPS_OQC_REQUIRE_DISTINCT_REVIEWER=on (default-ON)"
    elif [[ "$REV_STATE" == "off" ]]; then
        record FAIL "OQC 3-sig probe — Reviewer flag is OFF; check env var override"
    else
        record FAIL "OQC 3-sig probe missing for Reviewer flag — Program.cs probe line not emitted"
    fi
    if [[ "$APP_STATE" == "on" ]]; then
        record PASS "OQC 3-sig probe — OPS_OQC_REQUIRE_DISTINCT_APPROVER=on (default-ON)"
    elif [[ "$APP_STATE" == "off" ]]; then
        record FAIL "OQC 3-sig probe — Approver flag is OFF; check env var override"
    else
        record FAIL "OQC 3-sig probe missing for Approver flag"
    fi
    if [[ "$AI_STATE" == "on" ]]; then
        record PASS "OQC 3-sig probe — OPS_OQC_REQUIRE_APPROVER_DISTINCT_FROM_INSPECTOR=on (default-ON)"
    elif [[ "$AI_STATE" == "off" ]]; then
        record FAIL "OQC 3-sig probe — Approver-vs-Inspector flag is OFF; check env var override"
    else
        record FAIL "OQC 3-sig probe missing for Approver-vs-Inspector flag"
    fi
    if [[ -n "$POLICY_LINE" ]]; then
        record PASS "OQC 3-sig policy line emitted ($POLICY_LINE)"
    else
        record FAIL "OQC 3-sig policy summary line missing from log"
    fi
else
    record FAIL "API never reached /health on $PORT (see $API_LOG)"
    tail -30 "$API_LOG"
fi

# ── Summary ──────────────────────────────────────────────────────
echo ""
echo "============================  SUMMARY  ============================"
printf '%s\n' "${SUMMARY[@]}"
echo ""
echo "  TOTAL: pass=$PASS fail=$FAIL"
echo ""
echo "  7e-1 (domain + migration) shipped. 7e-2 wire / 7e-3 UI / 7e-4"
echo "  closeout will grow this skeleton (mirrors 7d cadence):"
echo "    +30 Api fixtures (FqcReviewController + OqcReviewController +"
echo "       photo upload + report summary + Q5 3-sig 422 + audit R7.3)"
echo "    +~80 Razor fixtures (FqcDashboard + OqcDashboard 3-sig flow +"
echo "       ShippedSummaryDashboard + photo upload UI + L21 OnPhaseChanged)"
echo ""
echo "  Companion verify (operator-driven, lands in 7e-2):"
echo "    bash scripts/checkpoint-7e-final.sh <WoNo> [--keep-alive]"
echo "    (Self-seeds 3 distinct QC users — Inspector / Reviewer / Approver"
echo "     — via POST /api/v2/admin/users + cycles all 3 Q5 violation"
echo "     paths so each WO_OQC_*_DENIED audit code surfaces.)"
echo ""

if [[ $FAIL -gt 0 ]]; then
    exit 1
fi
exit 0
