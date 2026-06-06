#!/usr/bin/env bash
# P10.7c — end-to-end verify SKELETON for the SETTING+RUNNING+PAUSED
# stack. Ships at PR 7c-1 covering only the domain surface (entities +
# migration + state-machine + services). 7c-2 / 7c-3 / 7c-4 PRs grow
# the wire + UI + checkpoint probe sections.
#
# Probes shipped in 7c-1:
#
#   Build / suites
#     1. dotnet build CCL-MES-Hybrid.sln (0 errors)
#     2. CCL.MES.Tests (Domain) ≥747 (was 733 pre-7c + 8 unit + 6
#        integration + 4 LegacyParity = +18 new fixtures; matrix
#        +1 cell change covered by existing 144-cell theory).
#     3. CCL.MES.Api.Tests ≥304 (unchanged from 7b — no controllers yet).
#     4. CCL.MES.Hybrid.Client.Tests ≥549 (unchanged — no client wrapper yet).
#     5. CCL.MES.Hybrid.Razor.Tests ≥24 (unchanged — no UI yet).
#
#   Migration round-trip (Rule 6 self-prep on the COPY)
#     6. Copy real DB → /tmp; Down to PREVIOUS_MIGRATION
#        (20260606023809_AddPrepressRowChecks); verify 3 7c tables
#        absent (WoRunSessions / WoPauseEvents / WoQtyEntries);
#        Up to CURRENT_MIGRATION (20260606093621_AddRunningSurfaceDomain);
#        verify 3 7c tables present + 5 WO new columns; Down once more
#        + Up again to prove idempotent.
#
#   Boot probe (L17 + L18 + the new L17-extension reason_codes pause)
#     7. Boot the API + assert no 'Overriding address(es)' warning
#        (L18 guard).
#     8. lsof-bound-port assertion — API listens on the asked port.
#     9. Reason-code boot probe still emits the [seed] pause/scrap/recovery
#        line + pause >= 8 (Q4 Pause picker source).
#
# 7c-2 will add the wire probes (auth + GET /setting + POST /run/start,
# /run/qty, /run/pause, /run/resume, /run/finish + 422/428/409
# negative paths + audit wire-mirror for the 7 new audit codes).
# 7c-3 will add the bUnit + Catalyst checkpoint slots.
# 7c-4 will add the soak (Concurrent_run_qty_add_N_equals_10) +
# extend purge-test-audit.sh for WO_RUN_* test rows.
#
# Rules honoured in this skeleton:
#   R1 (--base main on PR create)            — N/A here (script)
#   R2 (no --delete-branch mid-stack)        — N/A here (script)
#   R4 (comment-strip gate for Razor)        — checked by Rule 4 gate
#                                              outside this script
#   R5 (Henry-action includes ef update)     — printed at bottom on FAIL
#   R6 (self-prep DB baseline)               — Step 6 below
#   R7.1 ([ctx] DB= header)                  — printed at top
#   R7.2 (self-managed API + cleanup)        — Steps 7-9
#   R7.3 (wire-mirror)                       — N/A in 7c-1 (no wire); 7c-2 grows
#   S9 (responsive UI verify wide+narrow)    — N/A in 7c-1 (no UI); 7c-3 grows
#   S10 (preserve TMP_DIR on FAIL)           — cleanup() honours
#   S11 (assert-bound-port + log-grep L18)   — Step 7-8
#   L17 (seed kind-specific guard)           — Step 9
#   L18 (--urls override guard)              — Step 7
#
# Usage:
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.7c.sh
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.7c.sh --verbose

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

CURRENT_MIGRATION="20260606093621_AddRunningSurfaceDomain"
PREVIOUS_MIGRATION="20260606023809_AddPrepressRowChecks"

REAL_DB="$REPO_ROOT/data/ccl_mes.db"
TMP_DIR="$(mktemp -d -t ccl-verify-p10.7c-XXXXXX)"
TEST_DB="$TMP_DIR/ccl_mes_test.db"

PORT=5102
API_URL="http://127.0.0.1:${PORT}"
API_LOG="$TMP_DIR/api.log"
API_PID=""

PASS=0
FAIL=0
SUMMARY=()

DB_SHA8="(missing)"
[[ -f "$REAL_DB" ]] && DB_SHA8="$(shasum -a 256 "$REAL_DB" 2>/dev/null | awk '{print substr($1,1,8)}')"

echo "===================================================================="
echo "P10.7c verify (SKELETON — 7c-1 scope) — $(date '+%Y-%m-%d %H:%M:%S')"
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
        # Pre-migration probe: 3 7c tables MUST be absent.
        TABLES_PRE=$(sqlite3 "$TEST_DB" "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('WoRunSessions','WoPauseEvents','WoQtyEntries');" 2>/dev/null | wc -l | tr -d ' ')
        if [[ "$TABLES_PRE" == "0" ]]; then
            record PASS "pre-migration baseline (3 7c tables absent)"
        else
            record FAIL "pre-migration: expected 0 7c tables, got $TABLES_PRE"
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
            TABLES_POST=$(sqlite3 "$TEST_DB" "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('WoRunSessions','WoPauseEvents','WoQtyEntries');" 2>/dev/null | wc -l | tr -d ' ')
            if [[ "$TABLES_POST" == "3" ]]; then
                record PASS "post-migration (3 7c tables present)"
            else
                record FAIL "post-migration: expected 3 7c tables, got $TABLES_POST"
            fi

            # WO new columns probe.
            WO_COLS=$(sqlite3 "$TEST_DB" "PRAGMA table_info(WorkOrders);" 2>/dev/null | grep -E "SettingStartAt|SettingEndAt|SettingDurationSec|QtyDoneCached|QtyNgCached" | wc -l | tr -d ' ')
            if [[ "$WO_COLS" == "5" ]]; then
                record PASS "post-migration WO row gains 5 7c columns"
            else
                record FAIL "post-migration: expected 5 WO 7c columns, got $WO_COLS"
            fi

            # Idempotent re-up (NOOP).
            dotnet ef database update "$CURRENT_MIGRATION" \
                --connection "Data Source=$TEST_DB" \
                --project "$INFRA_PROJECT" \
                --startup-project "$API_PROJECT" \
                --no-build > "$TMP_DIR/migration-noop.log" 2>&1
            if [[ $? -eq 0 ]]; then
                record PASS "migration re-apply (idempotent NOOP)"
            else
                record FAIL "migration re-apply failed"
            fi
        fi
    fi
fi

# ── Step 4 — boot probe (L17 + L18 guards from 7b-4) ─────────────
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
echo "  Note: 7c-1 ships DOMAIN only — wire probes (PUT /run/*) land in"
echo "        7c-2; bUnit + Catalyst checkpoint in 7c-3; soak + purge"
echo "        extension + L17/L18 wire-mirror tests in 7c-4."
echo ""

if [[ $FAIL -gt 0 ]]; then
    exit 1
fi
exit 0
