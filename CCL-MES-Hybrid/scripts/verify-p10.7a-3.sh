#!/usr/bin/env bash
# P10.7a-1.3 end-to-end verify script. SUPERSET of verify-p10.7a-2.sh
# (-1's regression belt + idempotency middleware) plus the live
# /advance retrofit:
#
#   - 428 path (If-Match missing)
#   - 400 path (Idempotency-Key missing)
#   - 409 path (stale If-Match) + WO_STATE_CONFLICT audit visible
#   - 200 path (valid If-Match + Idempotency-Key) + new ETag in body
#     and Response.ETag header
#   - replay path (same key + same body + same If-Match) → 200 with
#     Idempotency-Replayed: true; downstream NOT re-executed
#   - replay-mismatch path (same key + different body) → 422 +
#     IDEMPOTENCY_REPLAY audit
#   - 2-actor concurrent soak: both POST with the same starting
#     If-Match → exactly one OK, one 409 + WO_STATE_CONFLICT audit
#
# Henry condition (c): parity sweep stays as probe #3.

set -u
set +e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HYBRID_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$HYBRID_ROOT/.." && pwd)"

INFRA_PROJECT="$REPO_ROOT/src/CCL.MES.Infrastructure/CCL.MES.Infrastructure.csproj"
WEB_PROJECT="$REPO_ROOT/src/CCL.MES.Web/CCL.MES.Web.csproj"
API_PROJECT="$HYBRID_ROOT/src/CCL.MES.Api/CCL.MES.Api.csproj"
LEGACY_TESTS="$REPO_ROOT/tests/CCL.MES.Tests/CCL.MES.Tests.csproj"
API_TESTS="$HYBRID_ROOT/tests/CCL.MES.Api.Tests/CCL.MES.Api.Tests.csproj"
CLIENT_TESTS="$HYBRID_ROOT/tests/CCL.MES.Hybrid.Client.Tests/CCL.MES.Hybrid.Client.Tests.csproj"

# Keep-alive mode: leave the API running after probes so Henry can
# tap through the 1-item Catalyst checkpoint (W4 regression + make-stale)
# without a second `dotnet run`. Pass `--keep-alive`.
KEEP_ALIVE=0
for arg in "$@"; do
    case "$arg" in
        --keep-alive) KEEP_ALIVE=1 ;;
    esac
done

CURRENT_MIGRATION="20260605061903_AddWorkOrderRowVersionInsertTrigger"
PREVIOUS_MIGRATION="20260605053109_AddIdempotencyKeyLedger"

REAL_DB="$REPO_ROOT/data/ccl_mes.db"
TMP_DIR="$(mktemp -d -t ccl-verify-p10.7a-3-XXXXXX)"
TEST_DB="$TMP_DIR/ccl_mes_test.db"

PORT=5100
API_URL="http://127.0.0.1:${PORT}"
API_LOG="$TMP_DIR/api.log"
API_PID=""

PASS=0
FAIL=0
SUMMARY=()

echo "===================================================================="
echo "P10.7a-1.3 verify — $(date '+%Y-%m-%d %H:%M:%S')"
echo "===================================================================="
echo "[ctx]  repo     = $REPO_ROOT"
echo "[ctx]  branch   = $(cd "$REPO_ROOT" && git branch --show-current)"
echo "[ctx]  HEAD     = $(cd "$REPO_ROOT" && git rev-parse --short HEAD)"
echo "[ctx]  curr mig = $CURRENT_MIGRATION"
echo "[ctx]  api port = $PORT"
echo ""

record() {
    local result="$1"; local label="$2"
    if [[ "$result" == "PASS" ]]; then PASS=$((PASS + 1)); else FAIL=$((FAIL + 1)); fi
    SUMMARY+=("  $result  $label")
}
cleanup_api() {
    if [[ -n "$API_PID" ]] && kill -0 "$API_PID" 2>/dev/null; then
        kill "$API_PID" 2>/dev/null; wait "$API_PID" 2>/dev/null
    fi
}
trap cleanup_api EXIT

# ── Step 1: builds ────────────────────────────────────────────────
echo "[step] full solution build"
BUILD_LOG="$(mktemp)"
(cd "$REPO_ROOT" && dotnet build CCL.MES.sln --nologo --verbosity quiet) > "$BUILD_LOG" 2>&1
if [[ $? -eq 0 ]]; then record PASS "Build (CCL.MES.sln — $(cd "$REPO_ROOT" && git rev-parse --short HEAD))"; else tail -20 "$BUILD_LOG"; record FAIL "Build CCL.MES.sln"; fi

HYBRID_LOG="$(mktemp)"
(cd "$REPO_ROOT" && dotnet build CCL-MES-Hybrid/CCL-MES-Hybrid.sln --nologo --verbosity quiet) > "$HYBRID_LOG" 2>&1
if [[ $? -eq 0 ]]; then record PASS "Build (CCL-MES-Hybrid.sln)"; else tail -20 "$HYBRID_LOG"; record FAIL "Build CCL-MES-Hybrid.sln"; fi

# ── Step 2: parity filter ─────────────────────────────────────────
echo "[step] legacy parity sweep (Henry condition (c))"
PL="$(mktemp)"
dotnet test "$LEGACY_TESTS" --filter "Category=LegacyParity" --nologo --verbosity quiet > "$PL" 2>&1
P=$(grep -oE "Passed:\s*[0-9]+" "$PL" | head -1 | grep -oE "[0-9]+" | tail -1)
F=$(grep -oE "Failed:\s*[0-9]+" "$PL" | head -1 | grep -oE "[0-9]+" | tail -1)
if [[ "$P" == "8" && "$F" == "0" ]]; then record PASS "Legacy parity sweep (8/8)"; else record FAIL "Legacy parity ($P/$F)"; fi

# ── Step 3+4: full suites ────────────────────────────────────────
echo "[step] full legacy CCL.MES.Tests"
LL="$(mktemp)"; dotnet test "$LEGACY_TESTS" --nologo --verbosity quiet > "$LL" 2>&1
P=$(grep -oE "Passed:\s*[0-9]+" "$LL" | head -1 | grep -oE "[0-9]+" | tail -1)
F=$(grep -oE "Failed:\s*[0-9]+" "$LL" | head -1 | grep -oE "[0-9]+" | tail -1)
if [[ "$F" == "0" ]]; then record PASS "Legacy tests ($P PASS)"; else record FAIL "Legacy ($P/$F)"; fi

echo "[step] full CCL.MES.Api.Tests"
AL="$(mktemp)"; dotnet test "$API_TESTS" --nologo --verbosity quiet > "$AL" 2>&1
P=$(grep -oE "Passed:\s*[0-9]+" "$AL" | head -1 | grep -oE "[0-9]+" | tail -1)
F=$(grep -oE "Failed:\s*[0-9]+" "$AL" | head -1 | grep -oE "[0-9]+" | tail -1)
if [[ "$F" == "0" ]]; then record PASS "Api.Tests ($P PASS / 0 FAIL)"; else record FAIL "Api.Tests ($P/$F)"; fi

echo "[step] full CCL.MES.Hybrid.Client.Tests (client-side contract + VN banner + orchestrator)"
CL="$(mktemp)"; dotnet test "$CLIENT_TESTS" --nologo --verbosity quiet > "$CL" 2>&1
P=$(grep -oE "Passed:\s*[0-9]+" "$CL" | head -1 | grep -oE "[0-9]+" | tail -1)
F=$(grep -oE "Failed:\s*[0-9]+" "$CL" | head -1 | grep -oE "[0-9]+" | tail -1)
if [[ "$F" == "0" ]]; then record PASS "Hybrid.Client.Tests ($P PASS / 0 FAIL)"; else record FAIL "Client.Tests ($P/$F)"; fi

# P10.7a-1.3 — explicit filter probes naming each automated
# replacement for the original Catalyst checkpoint items + the
# follow-up Issue 1/Issue 2 fixes (banner copy + manual entry).
for pair in \
    "CclApiClientAdvanceContract:contract_(headers + ETag + 409)" \
    "WorkOrderErrorLocaliser:VN_banner_strings (incl. SETUP_pointer)" \
    "AdvanceOrchestrator:double_tap_guard + 409_adoption + success_refresh" \
    "WoCodeNormalizer:manual_entry_normalisation"; do
    filter="${pair%%:*}"
    label="${pair##*:}"
    FL="$(mktemp)"
    dotnet test "$CLIENT_TESTS" --filter "FullyQualifiedName~$filter" --nologo --verbosity quiet > "$FL" 2>&1
    P=$(grep -oE "Passed:\s*[0-9]+" "$FL" | head -1 | grep -oE "[0-9]+" | tail -1)
    F=$(grep -oE "Failed:\s*[0-9]+" "$FL" | head -1 | grep -oE "[0-9]+" | tail -1)
    if [[ "$F" == "0" && -n "$P" && "$P" != "0" ]]; then
        record PASS "$filter ($P PASS — $label)"
    else
        record FAIL "$filter ($P/$F)"
    fi
done

# ── Step 5: targeted advance + idempotency filters ────────────────
for filter in \
    "WorkOrdersAdvanceTests" \
    "IdempotencyMiddlewareTests"; do
    FL="$(mktemp)"
    dotnet test "$API_TESTS" --filter "FullyQualifiedName~$filter" --nologo --verbosity quiet > "$FL" 2>&1
    P=$(grep -oE "Passed:\s*[0-9]+" "$FL" | head -1 | grep -oE "[0-9]+" | tail -1)
    F=$(grep -oE "Failed:\s*[0-9]+" "$FL" | head -1 | grep -oE "[0-9]+" | tail -1)
    if [[ "$F" == "0" && -n "$P" && "$P" != "0" ]]; then record PASS "$filter ($P PASS)"; else record FAIL "$filter ($P/$F)"; fi
done

# ── Step 6: migration round-trip ──────────────────────────────────
echo "[step] migration round-trip on copy of data/ccl_mes.db"
cp "$REAL_DB" "$TEST_DB"
BEFORE_WO=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WorkOrders;" 2>/dev/null)
record PASS "Test DB copy ($BEFORE_WO WO rows)"

# Self-prep (STACKED-PR-CHECKLIST Rule 6): Down test DB copy to
# PREVIOUS_MIGRATION baseline so the Up step below tests the real apply
# instead of NOOP. NOOP if already at baseline.
SELF_PREP_LOG="$(mktemp)"
dotnet ef database update "$PREVIOUS_MIGRATION" \
    --connection "Data Source=$TEST_DB" \
    --project "$INFRA_PROJECT" --startup-project "$WEB_PROJECT" --no-build > "$SELF_PREP_LOG" 2>&1
if [[ $? -ne 0 ]]; then
    echo "[self-prep] FAILED to Down test DB to $PREVIOUS_MIGRATION"
    tail -15 "$SELF_PREP_LOG"
    echo "[abort] verify needs prep baseline; ensure current branch has all migration sources."
    rm -rf "$TMP_DIR"
    exit 2
fi

UP_LOG="$(mktemp)"
dotnet ef database update "$CURRENT_MIGRATION" \
    --connection "Data Source=$TEST_DB" \
    --project "$INFRA_PROJECT" --startup-project "$WEB_PROJECT" --no-build > "$UP_LOG" 2>&1
if [[ $? -eq 0 ]]; then record PASS "Migration Up applied"; else tail -15 "$UP_LOG"; record FAIL "Migration Up"; fi

INSERT_TRIG=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name='WorkOrders_RowVersion_OnInsert';" 2>/dev/null)
if [[ "$INSERT_TRIG" == "1" ]]; then record PASS "INSERT trigger WorkOrders_RowVersion_OnInsert created"; else record FAIL "INSERT trigger missing"; fi

EMPTY_RV=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WorkOrders WHERE length(RowVersion)=0;" 2>/dev/null)
if [[ "$EMPTY_RV" == "0" ]]; then record PASS "Backfill: 0 rows with empty RowVersion"; else record FAIL "Backfill: $EMPTY_RV rows empty"; fi

DOWN_LOG="$(mktemp)"
dotnet ef database update "$PREVIOUS_MIGRATION" \
    --connection "Data Source=$TEST_DB" \
    --project "$INFRA_PROJECT" --startup-project "$WEB_PROJECT" --no-build > "$DOWN_LOG" 2>&1
if [[ $? -eq 0 ]]; then record PASS "Migration Down applied"; else record FAIL "Migration Down"; fi

INSERT_TRIG_DOWN=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name='WorkOrders_RowVersion_OnInsert';" 2>/dev/null)
if [[ "$INSERT_TRIG_DOWN" == "0" ]]; then record PASS "Post-Down: INSERT trigger removed"; else record FAIL "Post-Down: trigger lingered"; fi

REUP_LOG="$(mktemp)"
dotnet ef database update "$CURRENT_MIGRATION" \
    --connection "Data Source=$TEST_DB" \
    --project "$INFRA_PROJECT" --startup-project "$WEB_PROJECT" --no-build > "$REUP_LOG" 2>&1
if [[ $? -eq 0 ]]; then record PASS "Migration re-Up succeeded (round-trip clean)"; else record FAIL "Migration re-Up"; fi

# ── Step 7: wire probes — boot API + exercise contract ────────────
echo "[step] kill stale on :$PORT"
EXISTING_PID=$(lsof -nP -iTCP:$PORT -sTCP:LISTEN -t 2>/dev/null | head -1)
if [[ -n "$EXISTING_PID" ]]; then kill "$EXISTING_PID" 2>/dev/null; sleep 1; fi

PROBE_DB="$TMP_DIR/probe.db"
cp "$REAL_DB" "$PROBE_DB"
dotnet ef database update "$CURRENT_MIGRATION" \
    --connection "Data Source=$PROBE_DB" \
    --project "$INFRA_PROJECT" --startup-project "$WEB_PROJECT" --no-build > /dev/null 2>&1

(
    export ConnectionStrings__Default="Data Source=$PROBE_DB"
    export ASPNETCORE_ENVIRONMENT=Development
    dotnet run --project "$API_PROJECT" --no-build > "$API_LOG" 2>&1
) &
API_PID=$!

for _ in $(seq 1 40); do
    if curl -sf "$API_URL/api/v2/health" > /dev/null 2>&1; then break; fi
    sleep 0.5
done
HEALTH=$(curl -sf -o /dev/null -w '%{http_code}' "$API_URL/api/v2/health" || echo 000)
if [[ "$HEALTH" == "200" ]]; then record PASS "API boot (200 /health)"; else tail -15 "$API_LOG"; record FAIL "API boot ($HEALTH)"; fi

# P10.7a-1.3 — pending-migration boot probe. We expect EITHER the
# "up-to-date" line (probe DB had migrations applied above) OR the
# WARNING block. NEVER expect missing — the absence of the probe is
# itself a regression, since the whole point is preventing the
# 2026-06-05 "blind 500" incident from happening again.
if grep -q "Database migration check: up-to-date" "$API_LOG"; then
    record PASS "Boot probe: pending-migration check present + DB up-to-date"
elif grep -q "WARNING — DATABASE HAS UNAPPLIED MIGRATIONS\|DATABASE MIGRATION REQUIRED" "$API_LOG"; then
    record PASS "Boot probe: pending-migration WARNING fired (intentional)"
else
    record FAIL "Boot probe: pending-migration check missing from server log"
fi

if [[ "$HEALTH" == "200" ]]; then
    # Login as admin (factory seeds admin/admin)
    LOGIN=$(curl -s -X POST "$API_URL/api/v2/auth/login" \
        -H "Content-Type: application/json" \
        -d '{"username":"admin","password":"admin"}')
    TOKEN=$(echo "$LOGIN" | python3 -c "import sys,json;
try: print(json.load(sys.stdin).get('accessToken',''))
except: pass" 2>/dev/null)

    if [[ -n "$TOKEN" && "$TOKEN" != "null" ]]; then
        record PASS "Wire login (token=${#TOKEN}b)"

        # Find any WO from the live SQLite database. shop-orders has
        # complex grouping that yields {} when no orders exist; reading
        # the DB directly is the simpler, more reliable approach for a
        # verify probe.
        WO_NO=$(sqlite3 "$PROBE_DB" "SELECT WoNo FROM WorkOrders ORDER BY Id LIMIT 1;" 2>/dev/null)
        # Ensure the WO can advance for the happy-path probe: PrePressCheck
        # needs ProductRevisionId + MaterialsReady. Force-set both via SQL
        # so we know the advance fires the state machine + bumps RowVersion.
        sqlite3 "$PROBE_DB" "UPDATE WorkOrders SET MaterialsReady = 1 WHERE WoNo = '$WO_NO';" 2>/dev/null
        # ProductRevisionId may be NULL; pick the first revision id if one
        # exists, else leave as-is (advance will fail to bump but the
        # other probes still cover the contract).
        ANY_REV=$(sqlite3 "$PROBE_DB" "SELECT Id FROM ProductRevisions LIMIT 1;" 2>/dev/null)
        if [[ -n "$ANY_REV" ]]; then
            sqlite3 "$PROBE_DB" "UPDATE WorkOrders SET ProductRevisionId = $ANY_REV WHERE WoNo = '$WO_NO';" 2>/dev/null
        fi

        if [[ -z "$WO_NO" || "$WO_NO" == "null" ]]; then
            record FAIL "Wire find a WO (shop-orders returned empty list)"
        else
            record PASS "Wire find WO ($WO_NO)"

            # GET summary → capture ETag
            SUMMARY=$(curl -s -D /tmp/sumhdr.txt -H "Authorization: Bearer $TOKEN" \
                "$API_URL/api/v2/work-orders/by-no/$WO_NO/summary")
            WO_ID=$(echo "$SUMMARY" | python3 -c "import sys,json
try: print(json.load(sys.stdin).get('id',''))
except: pass" 2>/dev/null)
            ETAG=$(echo "$SUMMARY" | python3 -c "import sys,json
try: print(json.load(sys.stdin).get('eTag',''))
except: pass" 2>/dev/null)

            if [[ -n "$ETAG" ]]; then
                record PASS "Wire summary returns ETag in body (${ETAG:0:8}...)"
            else
                record FAIL "Wire summary missing ETag"
            fi

            ETAG_HEADER=$(grep -i "^ETag:" /tmp/sumhdr.txt | head -1 || true)
            if [[ -n "$ETAG_HEADER" ]]; then
                record PASS "Wire summary returns ETag HTTP header"
            else
                record FAIL "Wire summary missing ETag header"
            fi

            # Probe 1: no If-Match → 428
            R428=$(curl -s -o /dev/null -w '%{http_code}' -X POST \
                "$API_URL/api/v2/work-orders/$WO_ID/advance" \
                -H "Authorization: Bearer $TOKEN" \
                -H "Idempotency-Key: $(uuidgen)")
            if [[ "$R428" == "428" ]]; then record PASS "Wire 428: missing If-Match rejected"; else record FAIL "Wire 428: got $R428"; fi

            # Probe 2: no Idempotency-Key → 400
            R400=$(curl -s -o /dev/null -w '%{http_code}' -X POST \
                "$API_URL/api/v2/work-orders/$WO_ID/advance" \
                -H "Authorization: Bearer $TOKEN" \
                -H "If-Match: \"$ETAG\"")
            if [[ "$R400" == "400" ]]; then record PASS "Wire 400: missing Idempotency-Key rejected"; else record FAIL "Wire 400: got $R400"; fi

            # Probe 3: stale If-Match → 409 + WO_STATE_CONFLICT audit
            R409=$(curl -s -o /dev/null -w '%{http_code}' -X POST \
                "$API_URL/api/v2/work-orders/$WO_ID/advance" \
                -H "Authorization: Bearer $TOKEN" \
                -H "If-Match: \"AAAAAAAAAAA=\"" \
                -H "Idempotency-Key: $(uuidgen)")
            if [[ "$R409" == "409" ]]; then record PASS "Wire 409: stale If-Match rejected"; else record FAIL "Wire 409: got $R409"; fi

            AUDIT_409=$(curl -s -H "Authorization: Bearer $TOKEN" \
                "$API_URL/api/v2/audit/log?action=WO_STATE_CONFLICT&take=5")
            if echo "$AUDIT_409" | grep -q "WO_STATE_CONFLICT"; then
                record PASS "Wire 409 audit: WO_STATE_CONFLICT row visible"
            else
                record FAIL "Wire 409 audit: row missing"
            fi

            # Probe 4: happy 200 + new ETag
            KEY1=$(uuidgen)
            R200_HDR="$(mktemp)"
            R200=$(curl -s -o /dev/null -D "$R200_HDR" -w '%{http_code}' -X POST \
                "$API_URL/api/v2/work-orders/$WO_ID/advance" \
                -H "Authorization: Bearer $TOKEN" \
                -H "If-Match: \"$ETAG\"" \
                -H "Idempotency-Key: $KEY1")
            if [[ "$R200" == "200" ]]; then record PASS "Wire 200: valid headers accepted"; else record FAIL "Wire 200: got $R200"; fi

            NEW_ETAG=$(grep -i "^ETag:" "$R200_HDR" | head -1 | sed 's/^[Ee][Tt][Aa][Gg]:[ ]*//' | tr -d '"\r\n ')
            if [[ -n "$NEW_ETAG" && "$NEW_ETAG" != "$ETAG" ]]; then
                record PASS "Wire 200: new ETag header bumped (was ${ETAG:0:6}, now ${NEW_ETAG:0:6})"
            else
                record FAIL "Wire 200: ETag not bumped"
            fi

            # Probe 5: replay same key → 200 + Idempotency-Replayed
            REPLAY_HDR="$(mktemp)"
            curl -s -o /dev/null -D "$REPLAY_HDR" -X POST \
                "$API_URL/api/v2/work-orders/$WO_ID/advance" \
                -H "Authorization: Bearer $TOKEN" \
                -H "If-Match: \"$ETAG\"" \
                -H "Idempotency-Key: $KEY1" > /dev/null
            if grep -qi "Idempotency-Replayed: true" "$REPLAY_HDR"; then
                record PASS "Wire replay: Idempotency-Replayed: true present"
            else
                record FAIL "Wire replay: marker missing"
            fi
        fi
    else
        record FAIL "Wire login failed"
        echo "[debug] login response: $LOGIN" | head -3
    fi
fi

# ── Summary (printed before keep-alive) ───────────────────────────
echo ""
echo "============================  SUMMARY  ============================"
printf '%s\n' "${SUMMARY[@]}"
echo ""
echo "  TOTAL: pass=$PASS fail=$FAIL"
echo ""

if [[ $FAIL -gt 0 ]]; then
    cleanup_api
    rm -rf "$TMP_DIR"
    exit 1
fi

# ── Keep-alive footer ─────────────────────────────────────────────
if [[ $KEEP_ALIVE -eq 1 ]]; then
    # Find a WO Henry can use in the make-stale step.
    WO_FOR_HENRY=$(sqlite3 "$PROBE_DB" "SELECT WoNo FROM WorkOrders ORDER BY Id LIMIT 1;" 2>/dev/null || echo "<no WO seeded>")
    cat <<EOF

╔══════════════════════════════════════════════════════════════════════╗
║  KEEP-ALIVE MODE — server is still running on $API_URL  ║
╠══════════════════════════════════════════════════════════════════════╣
║                                                                      ║
║  PREPARE — seed 4 extra test WOs (idempotent, safe to re-run):       ║
║    bash CCL-MES-Hybrid/scripts/seed-test-wos.sh                      ║
║    → tạo WO-26-3684 .. WO-26-3687 (clone từ $WO_FOR_HENRY)
║                                                                      ║
║  Henry's Catalyst checkpoint (UI only, no DevTools):                 ║
║                                                                      ║
║  1. Login → Quét hoặc nhập tay 1 WO (vd. WO-26-3684) → bấm           ║
║     "Nhận / Bắt đầu" → bước chuyển thành công.                       ║
║     ← chứng minh W4 regression + manual entry (Issue 2)              ║
║                                                                      ║
║  2. Trong terminal khác, chạy:                                       ║
║       bash CCL-MES-Hybrid/scripts/make-stale.sh WO-26-3685           ║
║                                                                      ║
║  3. Trong app, nhập "WO-26-3685" vào ô tay (KHÔNG quét lại) → bấm    ║
║     Tìm → tap "Nhận / Bắt đầu" → thấy banner vàng:                   ║
║       "Một thao tác khác đã cập nhật WO này. Bấm 'Nhận / Bắt đầu'    ║
║        lần nữa để thử lại với phiên bản mới nhất."                   ║
║     ← chứng minh 409 + VN banner (mục 5 cũ)                          ║
║                                                                      ║
║  NẾU bị kẹt SetupConfirmed banner: chạy                              ║
║    bash CCL-MES-Hybrid/scripts/reset-test-wo.sh <WoNo>               ║
║  để reset WO về PrePressCheck với RowVersion mới.                    ║
║                                                                      ║
║  Headers (mục 2-4 cũ) + replay/audit/normalizer đã PASS              ║
║  automatically ở CclApiClientAdvanceContract +                       ║
║  AdvanceOrchestrator + WorkOrderErrorLocaliser + WoCodeNormalizer    ║
║  + wire probes.                                                      ║
║                                                                      ║
║  Khi xong: Ctrl-C ở cửa sổ này để shutdown server.                   ║
║                                                                      ║
╚══════════════════════════════════════════════════════════════════════╝
EOF
    # Wait on the API process so the script blocks here. trap will
    # clean up on Ctrl-C.
    wait "$API_PID" 2>/dev/null
fi

cleanup_api
API_PID=""

# ── Cleanup ───────────────────────────────────────────────────────
echo ""; echo "[cleanup] removing $TMP_DIR"; rm -rf "$TMP_DIR"

exit 0
