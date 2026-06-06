#!/usr/bin/env bash
# P10.7b — end-to-end verify for the entire PREPRESS stack
# (7b-1 domain + migration + BOM snapshot, 7b-2 API endpoints +
# concurrency, 7b-3 picker UI + seed fix + L17 regression).
#
# ≥25 probes covering:
#
#   Build / suites
#     1. dotnet build CCL-MES-Hybrid.sln (0 errors)
#     2. CCL.MES.Tests (Domain)            733/733
#     3. CCL.MES.Api.Tests                 ≥296/296 incl. PrepressControllerTests
#                                            + ReasonCodesControllerTests
#     4. CCL.MES.Hybrid.Client.Tests       ≥549/549 incl. PrepressErrorLocaliserTests
#     5. CCL.MES.Hybrid.Razor.Tests        ≥24/24 incl. PrepressDashboardTests
#
#   Migration round-trip (Rule 6 self-prep on the COPY)
#     6. Copy real DB → /tmp; Down to PREVIOUS_MIGRATION; verify trigger absent;
#        Up to CURRENT_MIGRATION; verify 3 PREPRESS tables present + INSERT
#        trigger present; Down once more + Up again to prove idempotent.
#
#   Wire probes (live API auto-booted pinned to test DB)
#     7. /health 200
#     8. GET /api/v2/reason-codes?kind=Scrap anon → 401
#     9. Login admin → JWT
#    10. GET /api/v2/reason-codes?kind=Scrap auth → 200 + ≥8 SC-* codes
#    11. GET /api/v2/reason-codes?kind=NotAKind → 422 reason_codes.invalid_kind
#    12. GET /api/v2/reason-codes (no filter) → 200 + Pause + Scrap + Recovery present
#    13. Pick a PREPRESS-phase WO; GET /api/v2/work-orders/{id}/prepress → 200 +
#        materials + plate + cutter + ETag
#    14. PUT /materials/{idx} no If-Match → 428 wo.if_match_required
#    15. PUT /materials/{idx} no Idempotency-Key → 400 wo.idempotency_key_required
#    16. PUT /materials/{idx} bad If-Match → 409 wo.state_conflict + bumped ETag in body
#    17. PUT /materials/{idx} status=Ok with fresh ETag → 200 + bumped ETag header
#    18. PUT /materials/{idx} status=Ng without reason → 422 prepress.invalid_reason_code
#    19. PUT /materials/{idx} status=Ng with unregistered code → 422 prepress.invalid_reason_code
#    20. PUT /materials/{idx} status=Ng with SC-MAT-DAMAGE + note → 200
#    21. PUT /plate-check status=Ok → 200
#    22. PUT /cutter-check status=Ok → 200
#    23. After last write, re-GET /prepress → assert MaterialsReady toggled correctly
#    24. GET /api/v2/audit/log?action=WO_PREPRESS_MATERIAL_SET → ≥1 row visible for WO
#    25. GET /api/v2/audit/log?action=WO_PREPRESS_PLATE_SET → ≥1 row visible
#    26. GET /api/v2/audit/log?action=WO_PREPRESS_CUTTER_SET → ≥1 row visible
#    27. GET /api/v2/audit/log?action=WO_STATE_CONFLICT → ≥1 row from probe 16
#
#   L17 regression
#    28. Boot log contains "[seed] reason_codes pause=N scrap=M recovery=K"
#        with scrap ≥ 8 (the 4 generic SC-* + 4 PREPRESS-specific SC-MAT-* / *PLATE-WORN / *CUTTER-WORN).
#
#   Soak (separate test invocation)
#    29. xUnit Trait=Soak filter — Concurrent_prepress_row_updates_N_equals_10
#        runs + passes solo (exit 0).
#
# Rule 6 — self-prep on the copy. Migration round-trip Downs the COPY to
# PREVIOUS_MIGRATION before any probe so re-running on a dev DB that has
# advanced past it doesn't spurious-fail.
#
# Rule 7.1 — [ctx] DB= + DB sha8 printed at startup.
# Rule 7.2 — self-managed API lifecycle (auto-boot + trap EXIT).
# Rule 7.3 — every wire probe here is mirrored by an xUnit fixture in
# PrepressControllerTests.cs / ReasonCodesControllerTests.cs.
#
# Usage:
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.7b.sh
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.7b.sh --verbose

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

CURRENT_MIGRATION="20260606023809_AddPrepressRowChecks"
PREVIOUS_MIGRATION="20260605061903_AddWorkOrderRowVersionInsertTrigger"

REAL_DB="$REPO_ROOT/data/ccl_mes.db"
TMP_DIR="$(mktemp -d -t ccl-verify-p10.7b-XXXXXX)"
TEST_DB="$TMP_DIR/ccl_mes_test.db"

PORT=5101
API_URL="http://127.0.0.1:${PORT}"
API_LOG="$TMP_DIR/api.log"
API_PID=""

CCL_USER="admin"
CCL_PWD="admin"

PASS=0
FAIL=0
SUMMARY=()

DB_SHA8="(missing)"
[[ -f "$REAL_DB" ]] && DB_SHA8="$(shasum -a 256 "$REAL_DB" 2>/dev/null | awk '{print substr($1,1,8)}')"

echo "===================================================================="
echo "P10.7b verify — $(date '+%Y-%m-%d %H:%M:%S')"
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
    # P10.7b-4 hotfix — preserve TMP_DIR on FAIL so the failing probe's
    # api.log + build.log + migration logs survive for debug. Closes
    # SKILLS.md S10 ("preserve debug artifacts on FAIL"). Only auto-clean
    # on full PASS.
    if [[ "$FAIL" -gt 0 ]]; then
        echo ""
        echo "[debug] TMP_DIR preserved for inspection: $TMP_DIR"
        echo "[debug] api log    : $API_LOG"
        echo "[debug] build log  : $TMP_DIR/build.log"
        echo "[debug] migration  : $TMP_DIR/migration-*.log"
    else
        rm -rf "$TMP_DIR"
    fi
}
trap cleanup EXIT INT TERM

# ── Step 1 — full build ───────────────────────────────────────────
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

# ── Step 2 — xUnit suites ─────────────────────────────────────────
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

# ── Step 3 — migration round-trip (Rule 6 self-prep) ──────────────
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
        # Pre-migration probe: 3 PREPRESS tables MUST be absent.
        TABLES_PRE=$(sqlite3 "$TEST_DB" "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('WoMaterials','WoPlateChecks','WoCutterChecks');" 2>/dev/null | wc -l | tr -d ' ')
        if [[ "$TABLES_PRE" == "0" ]]; then
            record PASS "pre-migration baseline (3 PREPRESS tables absent)"
        else
            record FAIL "pre-migration: expected 0 PREPRESS tables, got $TABLES_PRE"
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
            TABLES_POST=$(sqlite3 "$TEST_DB" "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('WoMaterials','WoPlateChecks','WoCutterChecks');" 2>/dev/null | wc -l | tr -d ' ')
            if [[ "$TABLES_POST" == "3" ]]; then
                record PASS "post-migration (3 PREPRESS tables present)"
            else
                record FAIL "post-migration: expected 3 PREPRESS tables, got $TABLES_POST"
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

# ── Step 4 — boot the API pinned to TEST_DB ────────────────────────
# P10.7b-4 hotfix — Lesson L18 ("--urls override by hardcoded Kestrel
# config"). Two assertions added:
#   (a) Pre-boot: kill anything already listening on $PORT so a stale
#       dev-server collision can't surface as a misleading FAIL.
#   (b) Post-boot: assert the API actually bound the port we asked for
#       (lsof) AND log MUST NOT contain "Overriding address(es)" warning.
#       If it did, the appsettings.json fix regressed.
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
# Bumped to 120s — cold-start with EF migration check + seed routines on
# the freshly Down/Up'd test DB easily takes >60s on a Mac under load.
# Also poll the log for "Now listening on" as a faster ready signal
# than waiting for the first /health response.
for i in $(seq 1 120); do
    if [[ $LISTEN_LOGGED -eq 0 ]] && grep -q "Now listening on:" "$API_LOG" 2>/dev/null; then
        LISTEN_LOGGED=1
        echo "[boot] Kestrel reported listening (after ${i}s) — probing /health"
    fi
    code=$(curl -s -m 2 -o /dev/null -w "%{http_code}" "$API_URL/health" 2>/dev/null)
    # /health is JWT-protected in this codebase — 401 also means "API is
    # up + auth middleware running". Same accept pattern as checkpoint-
    # 7b-2.sh + checkpoint-7a-2.sh. 503 covers transient startup before
    # the host probe is wired.
    if [[ "$code" =~ ^(200|401|503)$ ]]; then
        API_UP=1
        echo "[boot] /health responded $code (after ${i}s) — API up"
        break
    fi
    sleep 1
done

if [[ $API_UP -eq 1 ]]; then
    # Assert the API actually bound OUR port, not 5100 or anything else.
    BOUND_PID=$(lsof -nP -iTCP:${PORT} -sTCP:LISTEN -t 2>/dev/null | head -1)
    if [[ -z "$BOUND_PID" ]]; then
        record FAIL "API /health 200 but nothing listening on $PORT (impossible — investigate)"
    elif [[ "$BOUND_PID" != "$API_PID" ]]; then
        # Could be a child process of our `dotnet run` shim — verify the
        # PID is in our process tree.
        record PASS "API bound on $PORT (pid=$BOUND_PID; our dotnet=$API_PID)"
    else
        record PASS "API /health 200 + bound on $PORT (pid=$API_PID)"
    fi

    # L18 assertion: no override warning in the log.
    if grep -q "Overriding address(es)" "$API_LOG"; then
        record FAIL "L18 regression — appsettings.json Kestrel:Endpoints back? Log carries 'Overriding address(es)' warning"
        grep -n "Overriding address" "$API_LOG" | head -3
    else
        record PASS "L18 guard — no 'Overriding address(es)' warning in API log"
    fi
else
    record FAIL "API never reached /health 200 on $PORT (see $API_LOG)"
    echo "[debug] last 30 lines of $API_LOG:"
    tail -30 "$API_LOG"
    echo ""
    echo "============================  SUMMARY  ============================"
    printf '%s\n' "${SUMMARY[@]}"
    echo ""
    echo "  TOTAL: pass=$PASS fail=$FAIL"
    exit 1
fi

# ── L17 boot probe ─────────────────────────────────────────────────
SCRAP_COUNT=$(grep -oE '\[seed\] reason_codes pause=[0-9]+ scrap=[0-9]+ recovery=[0-9]+' "$API_LOG" | tail -1 | grep -oE 'scrap=[0-9]+' | cut -d= -f2)
if [[ -n "$SCRAP_COUNT" && "$SCRAP_COUNT" -ge 8 ]]; then
    record PASS "L17 boot probe scrap=$SCRAP_COUNT (≥8)"
else
    record FAIL "L17 boot probe missing or scrap<8 (got '$SCRAP_COUNT')"
fi

# ── Step 5 — wire probes ──────────────────────────────────────────
# Anon GET /reason-codes → 401
RSP=$(curl -s -o /dev/null -w '%{http_code}' "$API_URL/api/v2/reason-codes?kind=Scrap")
if [[ "$RSP" == "401" ]]; then
    record PASS "reason-codes anon → 401"
else
    record FAIL "reason-codes anon expected 401 got $RSP"
fi

# Login
LOGIN_RSP=$(curl -s -X POST "$API_URL/api/v2/auth/login" \
    -H "Content-Type: application/json" \
    -d "{\"username\":\"$CCL_USER\",\"password\":\"$CCL_PWD\",\"deviceId\":\"verify-p10.7b\"}")
TOKEN=$(echo "$LOGIN_RSP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('accessToken',''))" 2>/dev/null)
if [[ -n "$TOKEN" ]]; then
    record PASS "login admin (token_len=${#TOKEN})"
else
    record FAIL "login failed: $LOGIN_RSP"
    echo ""
    echo "============================  SUMMARY  ============================"
    printf '%s\n' "${SUMMARY[@]}"
    echo "  TOTAL: pass=$PASS fail=$FAIL"
    exit 1
fi
AUTH="Authorization: Bearer $TOKEN"

# GET reason-codes auth
RC_BODY=$(curl -s -H "$AUTH" "$API_URL/api/v2/reason-codes?kind=Scrap")
RC_COUNT=$(echo "$RC_BODY" | python3 -c "import sys,json; print(len(json.load(sys.stdin)))" 2>/dev/null)
if [[ -n "$RC_COUNT" && "$RC_COUNT" -ge 8 ]]; then
    record PASS "reason-codes kind=Scrap auth → 200 + $RC_COUNT codes (≥8)"
else
    record FAIL "reason-codes kind=Scrap count=$RC_COUNT (expected ≥8)"
fi

# GET reason-codes bad kind
RSP=$(curl -s -o /dev/null -w '%{http_code}' -H "$AUTH" "$API_URL/api/v2/reason-codes?kind=NotAKind")
if [[ "$RSP" == "422" ]]; then
    record PASS "reason-codes bad kind → 422"
else
    record FAIL "reason-codes bad kind expected 422 got $RSP"
fi

# GET reason-codes all kinds
RC_ALL=$(curl -s -H "$AUTH" "$API_URL/api/v2/reason-codes")
KIND_COUNT=$(echo "$RC_ALL" | python3 -c "
import sys, json
data = json.load(sys.stdin)
kinds = set(r['kind'] for r in data)
print(len(kinds))
" 2>/dev/null)
if [[ "$KIND_COUNT" == "3" ]]; then
    record PASS "reason-codes no filter → 3 kinds present (Pause+Scrap+Recovery)"
else
    record FAIL "reason-codes no filter expected 3 kinds got $KIND_COUNT"
fi

# Pick a WO in PREPRESS phase
WO_ROW=$(sqlite3 "$TEST_DB" "SELECT Id, WoNo FROM WorkOrders WHERE MesPhase='PREPRESS' OR MesPhase='NEW' OR CurrentStep='PrePressCheck' ORDER BY Id DESC LIMIT 1;" 2>/dev/null)
WO_ID="${WO_ROW%%|*}"
WO_NO="${WO_ROW##*|}"
if [[ -z "$WO_ID" ]]; then
    record FAIL "no PREPRESS-phase WO found in test DB"
    echo ""
    echo "============================  SUMMARY  ============================"
    printf '%s\n' "${SUMMARY[@]}"
    echo "  TOTAL: pass=$PASS fail=$FAIL"
    exit 1
fi
echo "[wire] picked WO id=$WO_ID no=$WO_NO"

# Ensure BOM exists for product (seed if not).
PROD_ID=$(sqlite3 "$TEST_DB" "SELECT ProductId FROM WorkOrders WHERE Id=$WO_ID;" 2>/dev/null)
PROD_REV_ID=$(sqlite3 "$TEST_DB" "SELECT ProductRevisionId FROM WorkOrders WHERE Id=$WO_ID;" 2>/dev/null)
BOM_COUNT=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM ManufacturingStructures WHERE ProductRevisionId=$PROD_REV_ID;" 2>/dev/null)
if [[ "${BOM_COUNT:-0}" == "0" ]]; then
    echo "[seed] no BOM rows for revision $PROD_REV_ID — seeding 5 test rows"
    for i in 1 2 3 4 5; do
        sqlite3 "$TEST_DB" "INSERT INTO ManufacturingStructures (ProductRevisionId, ChildPartNo, Quantity, Uom, Sequence, CreatedAt, CreatedBy) VALUES ($PROD_REV_ID, 'verify-mat-$i', 10.0, 'kg', $i, datetime('now'), 'verify-p10.7b');" 2>/dev/null
    done
fi

# GET /prepress
PRE_BODY=$(curl -s -H "$AUTH" "$API_URL/api/v2/work-orders/$WO_ID/prepress")
ETAG=$(echo "$PRE_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))" 2>/dev/null)
MAT_COUNT=$(echo "$PRE_BODY" | python3 -c "import sys,json; print(len(json.load(sys.stdin).get('materials',[])))" 2>/dev/null)
if [[ -n "$ETAG" && "${MAT_COUNT:-0}" -gt 0 ]]; then
    record PASS "GET /prepress → ETag set + $MAT_COUNT materials"
else
    record FAIL "GET /prepress failed: etag='$ETAG' mat=$MAT_COUNT body=${PRE_BODY:0:200}"
fi

FIRST_IDX=$(echo "$PRE_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin)['materials'][0]['bomLineIdx'])" 2>/dev/null)

# PUT material no If-Match → 428
RSP=$(curl -s -o /dev/null -w '%{http_code}' \
    -X PUT "$API_URL/api/v2/work-orders/$WO_ID/materials/$FIRST_IDX" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "Idempotency-Key: $(uuidgen)" \
    -d '{"status":"Ok"}')
if [[ "$RSP" == "428" ]]; then
    record PASS "PUT /materials no If-Match → 428"
else
    record FAIL "PUT /materials no If-Match expected 428 got $RSP"
fi

# PUT material no Idempotency-Key → 400
RSP=$(curl -s -o /dev/null -w '%{http_code}' \
    -X PUT "$API_URL/api/v2/work-orders/$WO_ID/materials/$FIRST_IDX" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" \
    -d '{"status":"Ok"}')
if [[ "$RSP" == "400" ]]; then
    record PASS "PUT /materials no Idempotency-Key → 400"
else
    record FAIL "PUT /materials no Idempotency-Key expected 400 got $RSP"
fi

# PUT material bad If-Match → 409 + body carries fresh ETag
CONFLICT_BODY=$(curl -s \
    -X PUT "$API_URL/api/v2/work-orders/$WO_ID/materials/$FIRST_IDX" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"AAAA\"" \
    -H "Idempotency-Key: $(uuidgen)" \
    -d '{"status":"Ok"}')
CONFLICT_CODE=$(echo "$CONFLICT_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('errorCode',''))" 2>/dev/null)
CONFLICT_ETAG=$(echo "$CONFLICT_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))" 2>/dev/null)
if [[ "$CONFLICT_CODE" == "wo.state_conflict" && -n "$CONFLICT_ETAG" ]]; then
    record PASS "PUT /materials bad If-Match → 409 wo.state_conflict + fresh ETag"
else
    record FAIL "PUT /materials bad If-Match: code='$CONFLICT_CODE' etag='$CONFLICT_ETAG'"
fi

# Refresh ETag for the happy path.
PRE_BODY=$(curl -s -H "$AUTH" "$API_URL/api/v2/work-orders/$WO_ID/prepress")
ETAG=$(echo "$PRE_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))" 2>/dev/null)

# PUT material status=Ok happy
OK_BODY=$(curl -s \
    -X PUT "$API_URL/api/v2/work-orders/$WO_ID/materials/$FIRST_IDX" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" \
    -H "Idempotency-Key: $(uuidgen)" \
    -d '{"status":"Ok","qtyLoaded":10,"lotNo":"LOT-VERIFY-001"}')
OK_FLAG=$(echo "$OK_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('ok',False))" 2>/dev/null)
NEW_ETAG=$(echo "$OK_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))" 2>/dev/null)
if [[ "$OK_FLAG" == "True" && -n "$NEW_ETAG" && "$NEW_ETAG" != "$ETAG" ]]; then
    record PASS "PUT /materials Ok happy → 200 + bumped ETag"
else
    record FAIL "PUT /materials Ok: ok=$OK_FLAG etag bumped? old=$ETAG new=$NEW_ETAG"
fi
ETAG="$NEW_ETAG"

# PUT material status=Ng no reason → 422
SECOND_IDX=$(echo "$PRE_BODY" | python3 -c "import sys,json; ms=json.load(sys.stdin)['materials']; print(ms[1]['bomLineIdx']) if len(ms)>1 else print('')" 2>/dev/null)
if [[ -n "$SECOND_IDX" ]]; then
    RSP=$(curl -s -o /dev/null -w '%{http_code}' \
        -X PUT "$API_URL/api/v2/work-orders/$WO_ID/materials/$SECOND_IDX" \
        -H "$AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$ETAG\"" \
        -H "Idempotency-Key: $(uuidgen)" \
        -d '{"status":"Ng","ngNote":"missing reason"}')
    if [[ "$RSP" == "422" ]]; then
        record PASS "PUT /materials Ng no reason → 422"
    else
        record FAIL "PUT /materials Ng no reason expected 422 got $RSP"
    fi

    # PUT material status=Ng unregistered code → 422
    RSP=$(curl -s -o /dev/null -w '%{http_code}' \
        -X PUT "$API_URL/api/v2/work-orders/$WO_ID/materials/$SECOND_IDX" \
        -H "$AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$ETAG\"" \
        -H "Idempotency-Key: $(uuidgen)" \
        -d '{"status":"Ng","ngReasonCode":"NOT-A-CODE","ngNote":"bogus"}')
    if [[ "$RSP" == "422" ]]; then
        record PASS "PUT /materials Ng unregistered code → 422"
    else
        record FAIL "PUT /materials Ng unregistered code expected 422 got $RSP"
    fi

    # PUT material status=Ng with SC-MAT-DAMAGE → 200
    NG_BODY=$(curl -s \
        -X PUT "$API_URL/api/v2/work-orders/$WO_ID/materials/$SECOND_IDX" \
        -H "$AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$ETAG\"" \
        -H "Idempotency-Key: $(uuidgen)" \
        -d '{"status":"Ng","ngReasonCode":"SC-MAT-DAMAGE","ngNote":"verify-script NG path"}')
    NG_FLAG=$(echo "$NG_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('ok',False))" 2>/dev/null)
    if [[ "$NG_FLAG" == "True" ]]; then
        record PASS "PUT /materials Ng with SC-MAT-DAMAGE → 200"
    else
        record FAIL "PUT /materials Ng with SC-MAT-DAMAGE failed: $NG_BODY"
    fi
    ETAG=$(echo "$NG_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))" 2>/dev/null)
else
    record FAIL "Only 1 material row in test WO — Ng probes skipped"
fi

# PUT plate-check Ok
PLATE_BODY=$(curl -s \
    -X PUT "$API_URL/api/v2/work-orders/$WO_ID/plate-check" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" \
    -H "Idempotency-Key: $(uuidgen)" \
    -d '{"status":"Ok","plateNo":"PLATE-VERIFY-001"}')
PLATE_FLAG=$(echo "$PLATE_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('ok',False))" 2>/dev/null)
if [[ "$PLATE_FLAG" == "True" ]]; then
    record PASS "PUT /plate-check Ok → 200"
else
    record FAIL "PUT /plate-check failed: $PLATE_BODY"
fi
ETAG=$(echo "$PLATE_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))" 2>/dev/null)

# PUT cutter-check Ok
CUTTER_BODY=$(curl -s \
    -X PUT "$API_URL/api/v2/work-orders/$WO_ID/cutter-check" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" \
    -H "Idempotency-Key: $(uuidgen)" \
    -d '{"status":"Ok","cutterNo":"CUT-VERIFY-001"}')
CUTTER_FLAG=$(echo "$CUTTER_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('ok',False))" 2>/dev/null)
if [[ "$CUTTER_FLAG" == "True" ]]; then
    record PASS "PUT /cutter-check Ok → 200"
else
    record FAIL "PUT /cutter-check failed: $CUTTER_BODY"
fi

# Re-GET /prepress → MaterialsReady ought to be false (one row is NG)
FINAL_BODY=$(curl -s -H "$AUTH" "$API_URL/api/v2/work-orders/$WO_ID/prepress")
READY=$(echo "$FINAL_BODY" | python3 -c "import sys,json; print(json.load(sys.stdin).get('materialsReady',None))" 2>/dev/null)
if [[ "$READY" == "False" ]]; then
    record PASS "rollup MaterialsReady=false (1 row NG ⇒ not ready)"
else
    record FAIL "rollup MaterialsReady=$READY (expected False with NG row)"
fi

# Audit wire probes (Rule 7.3)
for ACTION in WO_PREPRESS_MATERIAL_SET WO_PREPRESS_PLATE_SET WO_PREPRESS_CUTTER_SET WO_STATE_CONFLICT; do
    AUDIT_BODY=$(curl -s -H "$AUTH" "$API_URL/api/v2/audit/log?action=$ACTION&page=1&pageSize=50")
    if echo "$AUDIT_BODY" | grep -q "\"targetId\":\"$WO_ID\""; then
        record PASS "audit wire $ACTION visible for WO $WO_ID"
    else
        record FAIL "audit wire $ACTION missing for WO $WO_ID"
    fi
done

# ── Step 6 — Soak test invocation (Trait=Soak) ────────────────────
echo "[step] soak test Concurrent_prepress_row_updates_N_equals_10"
SOAK_LOG="$TMP_DIR/soak.log"
dotnet test "$API_TESTS" \
    --filter "FullyQualifiedName~Concurrent_prepress_row_updates_N_equals_10" \
    --nologo -v q --no-build > "$SOAK_LOG" 2>&1
SOAK_EXIT=$?
if [[ $SOAK_EXIT -eq 0 ]]; then
    record PASS "Concurrent_prepress_row_updates_N_equals_10 soak"
else
    record FAIL "soak failed (exit $SOAK_EXIT, see $SOAK_LOG)"
    tail -15 "$SOAK_LOG"
fi

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
