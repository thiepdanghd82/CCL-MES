#!/usr/bin/env bash
# IPQC first-article (MATERIAL SYSTEM) — end-to-end verify for the h-1..h-4 stack.
#
# 4-PR stack:
#   h-1  Domain   WoIpqcMaterialCheck + enums + MaterialSystemDivergence +
#                 IpqcMaterialRollup + MeasuredValue/CheckType cols + migration
#                 20260825053900_AddIpqcMaterialCheck (freeze-at-confirm, Q4)
#   h-2  Wire     WoIpqcMaterialController (GET/PUT/POST) + EngineerWaive policy
#                 + OPS_IPQC_REQUIRE_DISTINCT_MATERIAL_WAIVER flag + GoRun gate
#   h-3  UI       IpqcDashboard MATERIAL panel + 3-tab CheckType stepper +
#                 MeasuredValue input + Engineer waiver (RBAC-by-omission)
#   h-4  THIS     closeout — verify + checkpoint + purge extension + LESSONS
#
# Probes:
#   Build / suites
#     1. dotnet build CCL-MES-Hybrid.sln (0 errors)
#     2. Domain tests (incl. MaterialSystemDivergence 4-case + IpqcMaterialRollup
#        + LegacyParity)
#     3. Api tests (incl. WoIpqcMaterialController 428/400/409/422 + matched/
#        divergent confirm + engineer approve/denied dual-sig + not_divergent +
#        GoRun block→waive→allow + audit wire-mirror)
#     4. Hybrid Client tests (i18n parity + IpqcReviewErrorLocaliser material codes)
#     5. Hybrid Razor tests (IpqcDashboard first-article fixtures)
#
#   Migration round-trip (Rule 6 self-prep on the COPY)
#     6. Copy real DB → /tmp; Down to PREVIOUS_MIGRATION (AddSettingCheckPersist);
#        assert WoIpqcMaterialChecks ABSENT; Up to CURRENT (AddIpqcMaterialCheck);
#        assert table PRESENT + unique index (WorkOrderId,BomLineIdx) +
#        MeasuredValue/CheckType cols on WoIpqcCheckItems + idempotent backfill
#        for IPQC_WAIT WOs; re-Up NOOP.
#
#   Boot probe (L18 guard + waiver flag default-ON boot probe NEW)
#     7. Boot API on the COPY; assert bound port + no 'Overriding address(es)'.
#     8. Assert [config] OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER=on (inherited).
#     9. Assert [config] OPS_IPQC_REQUIRE_DISTINCT_MATERIAL_WAIVER=on (NEW,
#        default-ON per Henry Q1 — an op-engineer can confirm the deploy loaded
#        the expected 4-eye enforcement for the divergence waiver).
#
# Companion (operator-driven on hardware):
#   checkpoint-ipqc-material-final.sh <WoNo> [--keep-alive]
#   purge-test-audit.sh [--commit]
#
# Usage:  cd CCL-MES-Hybrid && ./scripts/verify-ipqc-material.sh [--verbose]

set -u
set +e

VERBOSE=0
for arg in "$@"; do case "$arg" in --verbose) VERBOSE=1 ;; esac; done

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

CURRENT_MIGRATION="20260825053900_AddIpqcMaterialCheck"
PREVIOUS_MIGRATION="20260824102916_AddSettingCheckPersist"

REAL_DB="$REPO_ROOT/data/ccl_mes.db"
TMP_DIR="$(mktemp -d -t ccl-verify-ipqc-material-XXXXXX)"
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
echo "IPQC first-article (MATERIAL SYSTEM) verify — $(date '+%Y-%m-%d %H:%M:%S')"
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
    if [[ "$1" == "PASS" ]]; then PASS=$((PASS + 1)); SUMMARY+=("  PASS  $2")
    else FAIL=$((FAIL + 1)); SUMMARY+=("  FAIL  $2"); fi
}

cleanup() {
    if [[ -n "$API_PID" ]]; then kill -9 "$API_PID" 2>/dev/null; wait "$API_PID" 2>/dev/null; fi
    if [[ "$FAIL" -gt 0 ]]; then
        echo ""
        echo "[debug] TMP_DIR preserved: $TMP_DIR"
        echo "[debug] api log   : $API_LOG"
        echo "[debug] build log : $TMP_DIR/build.log"
        echo ""
        echo "[Henry-action] apply the migration to the live DB (Phase C) only when ready:"
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

# ── Step 1 — build ───────────────────────────────────────────────
echo "[step] build solution"
BUILD_OUT="$TMP_DIR/build.log"
(cd "$HYBRID_ROOT" && dotnet build "$SOLUTION" --nologo -clp:NoSummary > "$BUILD_OUT" 2>&1)
BUILD_EXIT=$?
if [[ $BUILD_EXIT -eq 0 ]]; then record PASS "solution build (exit 0)"
else record FAIL "solution build (exit $BUILD_EXIT, see $BUILD_OUT)"; tail -20 "$BUILD_OUT"; fi

# ── Step 2 — xUnit suites ────────────────────────────────────────
run_suite() {
    local label="$1"; local proj="$2"; local extra="${3:-}"
    local out="$TMP_DIR/test-$(basename "$proj").log"
    echo "[step] test $label"
    if [[ -n "$extra" ]]; then dotnet test "$proj" $extra --nologo -v q --no-build > "$out" 2>&1
    else dotnet test "$proj" --nologo -v q --no-build > "$out" 2>&1; fi
    local exit=$?
    local passed=$(grep -oE "Passed:[[:space:]]*[0-9]+" "$out" | head -1 | awk '{print $2}')
    local failed=$(grep -oE "Failed:[[:space:]]*[0-9]+" "$out" | head -1 | awk '{print $2}')
    if [[ $exit -eq 0 && "${failed:-0}" == "0" ]]; then record PASS "$label (passed=${passed:-?})"
    else record FAIL "$label (exit=$exit failed=${failed:-?}, see $out)"; tail -12 "$out"; fi
}

run_suite "Domain tests"        "$DOMAIN_TESTS"  "--filter Category!=Soak"
run_suite "Api tests"           "$API_TESTS"     "--filter Category!=Soak"
run_suite "Hybrid Client tests" "$CLIENT_TESTS"
run_suite "Hybrid Razor tests"  "$RAZOR_TESTS"

# ── Step 3 — migration round-trip (Rule 6 self-prep on COPY) ─────
echo "[step] migration round-trip on COPY"
if [[ ! -f "$REAL_DB" ]]; then
    record FAIL "real DB not found at $REAL_DB"
else
    cp "$REAL_DB" "$TEST_DB"
    dotnet ef database update "$PREVIOUS_MIGRATION" \
        --connection "Data Source=$TEST_DB" --project "$INFRA_PROJECT" \
        --startup-project "$API_PROJECT" --no-build > "$TMP_DIR/migration-down.log" 2>&1
    if [[ $? -ne 0 ]]; then
        record FAIL "self-prep Down to $PREVIOUS_MIGRATION"; tail -20 "$TMP_DIR/migration-down.log"
    else
        PRE=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='WoIpqcMaterialChecks';" 2>/dev/null)
        [[ "$PRE" == "0" ]] && record PASS "pre-migration baseline (WoIpqcMaterialChecks absent)" \
                            || record FAIL "pre-migration: expected table absent, got $PRE"

        dotnet ef database update "$CURRENT_MIGRATION" \
            --connection "Data Source=$TEST_DB" --project "$INFRA_PROJECT" \
            --startup-project "$API_PROJECT" --no-build > "$TMP_DIR/migration-up.log" 2>&1
        if [[ $? -ne 0 ]]; then
            record FAIL "migration Up to $CURRENT_MIGRATION"; tail -20 "$TMP_DIR/migration-up.log"
        else
            POST=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='WoIpqcMaterialChecks';" 2>/dev/null)
            [[ "$POST" == "1" ]] && record PASS "post-migration (WoIpqcMaterialChecks present)" \
                                 || record FAIL "post-migration: expected table present, got $POST"

            IDX=$(sqlite3 "$TEST_DB" "SELECT name FROM sqlite_master WHERE type='index' AND name='IX_WoIpqcMaterialChecks_WorkOrderId_BomLineIdx';" 2>/dev/null)
            [[ "$IDX" == "IX_WoIpqcMaterialChecks_WorkOrderId_BomLineIdx" ]] \
                && record PASS "post-migration unique index (WorkOrderId,BomLineIdx)" \
                || record FAIL "post-migration: unique index missing, got '$IDX'"

            COLS=$(sqlite3 "$TEST_DB" "PRAGMA table_info(WoIpqcCheckItems);" 2>/dev/null | grep -icE "MeasuredValue|CheckType")
            [[ "$COLS" == "2" ]] && record PASS "post-migration WoIpqcCheckItems += MeasuredValue + CheckType" \
                                 || record FAIL "post-migration: expected 2 new item cols, got $COLS"

            EXP=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WoMaterials m JOIN WorkOrders w ON w.Id=m.WorkOrderId WHERE w.MesPhase='IPQC_WAIT' AND m.WoLegId IS NULL;" 2>/dev/null)
            ACT=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WoIpqcMaterialChecks;" 2>/dev/null)
            [[ "${ACT:-0}" == "$EXP" ]] && record PASS "post-migration backfill (rows=$ACT for $EXP eligible materials)" \
                                        || record FAIL "post-migration backfill drift: expected $EXP got $ACT"

            dotnet ef database update "$CURRENT_MIGRATION" \
                --connection "Data Source=$TEST_DB" --project "$INFRA_PROJECT" \
                --startup-project "$API_PROJECT" --no-build > "$TMP_DIR/migration-noop.log" 2>&1
            [[ $? -eq 0 ]] && record PASS "migration re-apply (idempotent NOOP)" \
                           || record FAIL "migration re-apply failed"
        fi
    fi
fi

# ── Step 4 — boot probe (L18 guard + waiver flag default-ON) ─────
echo "[step] kill stale listeners on $PORT"
STALE=$(lsof -nP -iTCP:${PORT} -sTCP:LISTEN -t 2>/dev/null)
[[ -n "$STALE" ]] && { echo "$STALE" | xargs -r kill -9 2>/dev/null; sleep 1; }

echo "[step] boot API on $TEST_DB"
(cd "$HYBRID_ROOT/src/CCL.MES.Api" && \
    ConnectionStrings__Default="Data Source=$TEST_DB" \
    ASPNETCORE_ENVIRONMENT="Development" \
    dotnet run --no-build --no-launch-profile --urls "http://127.0.0.1:${PORT}" > "$API_LOG" 2>&1) &
API_PID=$!

API_UP=0
for i in $(seq 1 120); do
    code=$(curl -s -m 2 -o /dev/null -w "%{http_code}" "$API_URL/health" 2>/dev/null)
    if [[ "$code" =~ ^(200|401|503)$ ]]; then API_UP=1; echo "[boot] /health $code after ${i}s"; break; fi
    sleep 1
done

if [[ $API_UP -eq 1 ]]; then
    BOUND=$(lsof -nP -iTCP:${PORT} -sTCP:LISTEN -t 2>/dev/null | head -1)
    [[ -n "$BOUND" ]] && record PASS "API bound on $PORT (pid=$BOUND)" \
                      || record FAIL "API /health up but nothing listening on $PORT"
    grep -q "Overriding address(es)" "$API_LOG" \
        && record FAIL "L18 regression — 'Overriding address(es)' in log" \
        || record PASS "L18 guard — no address override warning"

    QA_STATE=$(grep -oE '\[config\] OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER=(on|off)' "$API_LOG" | tail -1 | cut -d= -f2)
    [[ "$QA_STATE" == "on" ]] && record PASS "QA dual-sig boot probe = on (inherited)" \
                              || record FAIL "QA dual-sig boot probe not 'on' (got '$QA_STATE')"

    W_STATE=$(grep -oE '\[config\] OPS_IPQC_REQUIRE_DISTINCT_MATERIAL_WAIVER=(on|off)' "$API_LOG" | tail -1 | cut -d= -f2)
    if [[ "$W_STATE" == "on" ]]; then
        record PASS "Material-waiver boot probe = on (default-ON per Q1)"
    elif [[ "$W_STATE" == "off" ]]; then
        record FAIL "Material-waiver flag is OFF — check env override (Q1 default is ON)"
    else
        record FAIL "Material-waiver boot probe missing — Program.cs probe not emitted"
    fi
else
    record FAIL "API never reached /health on $PORT (see $API_LOG)"; tail -30 "$API_LOG"
fi

# ── Summary ──────────────────────────────────────────────────────
echo ""
echo "============================  SUMMARY  ============================"
printf '%s\n' "${SUMMARY[@]}"
echo ""
echo "  TOTAL: pass=$PASS fail=$FAIL"
echo ""
echo "  Companion (operator-driven on hardware):"
echo "    bash scripts/checkpoint-ipqc-material-final.sh <WoNo> [--keep-alive]"
echo "    bash scripts/purge-test-audit.sh            # preview"
echo "    bash scripts/purge-test-audit.sh --commit   # cleanup"
echo ""

[[ $FAIL -gt 0 ]] && exit 1
exit 0
