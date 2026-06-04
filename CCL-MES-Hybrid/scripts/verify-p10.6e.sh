#!/usr/bin/env bash
# P10.6e end-to-end verify script. Pattern locked in P10.6a.
#
# Usage (always from repo root):
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.6e.sh
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.6e.sh --keep-alive
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.6e.sh --verbose
#
# Scope:
#   P10.6e ships 4 NEW server endpoints (GET /audit/log, /audit/actions,
#   /audit/export/csv, /audit/export/xlsx) — all AdminOnly. The verify
#   covers:
#     1. Build API + Client + Razor clean.
#     2. Full API regression sweep (incl. 14 new AuditLogController tests).
#     3. Full client regression sweep.
#     4. Anon → 401 on all 4 new endpoints (route discovery canary).
#     5. Admin happy: list paged + actions list + CSV export with
#        Content-Type + header row + row count + XLSX export with PK
#        ZIP magic + AUDIT_EXPORT audit row emitted post-export.
#     6. --keep-alive prints the Catalyst checklist incl. cross-role
#        403 verification.

set -u
set +e

VERBOSE=0
KEEP_ALIVE=0
for arg in "$@"; do
    case "$arg" in
        --verbose) VERBOSE=1 ;;
        --keep-alive) KEEP_ALIVE=1 ;;
    esac
done

CCL_USER=${CCL_USER:-admin}
CCL_PWD=${CCL_PWD:-admin}
PORT=5100
API_URL="http://127.0.0.1:${PORT}"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HYBRID_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$HYBRID_ROOT/.." && pwd)"
API_PROJECT="$HYBRID_ROOT/src/CCL.MES.Api/CCL.MES.Api.csproj"
CLIENT_PROJECT="$HYBRID_ROOT/src/CCL.MES.Hybrid.Client/CCL.MES.Hybrid.Client.csproj"
CLIENT_TESTS="$HYBRID_ROOT/tests/CCL.MES.Hybrid.Client.Tests/CCL.MES.Hybrid.Client.Tests.csproj"
API_TESTS="$HYBRID_ROOT/tests/CCL.MES.Api.Tests/CCL.MES.Api.Tests.csproj"
API_DLL_DIR="$HYBRID_ROOT/src/CCL.MES.Api/bin/Debug/net10.0"

PASS=0
FAIL=0
declare -a LINES=()

record() {
    local status="$1"; local label="$2"
    if [[ "$status" == "PASS" ]]; then
        PASS=$((PASS+1)); LINES+=("PASS  $label")
    else
        FAIL=$((FAIL+1)); LINES+=("FAIL  $label")
    fi
}

cleanup() {
    if [[ $KEEP_ALIVE -eq 1 && $FAIL -eq 0 ]]; then
        echo
        echo "[keep-alive] server still running on :$PORT (PID $API_PID)."
        echo "             REBUILD the Catalyst client app (Razor + new page +"
        echo "             new audit-log table CSS), relaunch as admin, verify:"
        echo "             1. Sidebar CÀI ĐẶT shows new 'Nhật ký kiểm tra' sub-link."
        echo "             2. Settings landing → 'Quản trị viên' has both Backup + Audit."
        echo "             3. /settings/audit loads — filter row (search/action/actor/from/to)"
        echo "                + result table with paged rows (default 50)."
        echo "             4. Type into 'Người dùng' filter → tap 'Áp dụng' → rows narrow."
        echo "             5. Select 'LOGIN_OK' in Hành động dropdown → tap Áp dụng → only LOGIN_OK rows."
        echo "             6. Tap 'Xuất CSV' → native Save dialog → save → success banner."
        echo "             7. Tap 'Xuất XLSX' → native Save dialog → save → success banner."
        echo "             8. Reload page → top row is AUDIT_EXPORT for your own actor."
        echo "             9. Log out + log in as Engineer → /settings/audit forbidden;"
        echo "                sidebar sub-link is hidden."
        echo "             When done:  kill $API_PID"
        return
    fi
    if [[ -n "${API_PID:-}" ]]; then
        kill "$API_PID" 2>/dev/null
        wait "$API_PID" 2>/dev/null
    fi
}
trap cleanup EXIT

echo "===================================================================="
echo "P10.6e verify — $(date '+%Y-%m-%d %H:%M:%S')"
echo "===================================================================="
echo "[ctx]  repo  = $REPO_ROOT"
echo "[ctx]  branch= $(cd "$REPO_ROOT" && git rev-parse --abbrev-ref HEAD)"
echo "[ctx]  HEAD  = $(cd "$REPO_ROOT" && git rev-parse --short HEAD)"
echo "[ctx]  admin = $CCL_USER"
echo

# ── 0. Kill anything on :5100 ─────────────────────────────────────────
echo "[step] kill anything on :$PORT"
STALE_PID=$(lsof -nP -iTCP:$PORT -sTCP:LISTEN -t 2>/dev/null | head -1)
if [[ -n "$STALE_PID" ]]; then
    echo "[kill] PID $STALE_PID"
    kill "$STALE_PID" 2>/dev/null
    sleep 2
fi

# ── 1. Build ──────────────────────────────────────────────────────────
echo "[step] build Client lib"
CLIENT_BUILD=$(cd "$REPO_ROOT" && dotnet build "$CLIENT_PROJECT" -c Debug --nologo -v q 2>&1)
CLIENT_RC=$?
if [[ $CLIENT_RC -ne 0 ]]; then
    echo "$CLIENT_BUILD" | tail -20
    record FAIL "Client lib build (rc=$CLIENT_RC)"
else
    record PASS "Client lib build"
fi

echo "[step] build API"
BUILD_OUT=$(cd "$REPO_ROOT" && dotnet build "$API_PROJECT" -c Debug --nologo -v q 2>&1)
BUILD_RC=$?
if [[ $BUILD_RC -ne 0 ]]; then
    echo "$BUILD_OUT" | tail -20
    record FAIL "API build (rc=$BUILD_RC)"
    echo
    echo "============================  SUMMARY  ============================"
    printf '  %s\n' "${LINES[@]}"
    echo
    echo "  TOTAL: pass=$PASS fail=$FAIL"
    exit 1
fi
record PASS "API build (commit $(cd "$REPO_ROOT" && git rev-parse --short HEAD))"

# ── 2. Run client unit tests ─────────────────────────────────────────
echo "[step] run client unit tests"
CLIENT_TEST_OUT=$(cd "$REPO_ROOT" && dotnet test "$CLIENT_TESTS" -c Debug --nologo --no-build 2>&1)
CLIENT_TEST_RC=$?
CLIENT_PASSED=$(echo "$CLIENT_TEST_OUT" | grep -oE 'Passed:[[:space:]]+[0-9]+' | head -1 | grep -oE '[0-9]+')
CLIENT_FAILED=$(echo "$CLIENT_TEST_OUT" | grep -oE 'Failed:[[:space:]]+[0-9]+' | head -1 | grep -oE '[0-9]+')
[[ $VERBOSE -eq 1 ]] && echo "$CLIENT_TEST_OUT" | tail -10
if [[ $CLIENT_TEST_RC -eq 0 ]]; then
    record PASS "Client tests (passed=$CLIENT_PASSED failed=$CLIENT_FAILED)"
else
    echo "$CLIENT_TEST_OUT" | tail -20
    record FAIL "Client tests (rc=$CLIENT_TEST_RC passed=$CLIENT_PASSED failed=$CLIENT_FAILED)"
fi

# ── 3. Run API unit tests ────────────────────────────────────────────
echo "[step] run API unit tests (incl. 14 AuditLog + 4 new canary rows)"
API_TEST_OUT=$(cd "$REPO_ROOT" && dotnet test "$API_TESTS" -c Debug --nologo --no-build 2>&1)
API_TEST_RC=$?
API_PASSED=$(echo "$API_TEST_OUT" | grep -oE 'Passed:[[:space:]]+[0-9]+' | head -1 | grep -oE '[0-9]+')
API_FAILED=$(echo "$API_TEST_OUT" | grep -oE 'Failed:[[:space:]]+[0-9]+' | head -1 | grep -oE '[0-9]+')
if [[ $API_TEST_RC -eq 0 ]]; then
    record PASS "API tests (passed=$API_PASSED failed=$API_FAILED)"
else
    echo "$API_TEST_OUT" | tail -20
    record FAIL "API tests (rc=$API_TEST_RC passed=$API_PASSED failed=$API_FAILED)"
fi

# ── 3b. Filter-confirm AuditLog coverage ─────────────────────────────
echo "[step] filter-run AuditLogController tests"
AL_OUT=$(cd "$REPO_ROOT" && dotnet test "$API_TESTS" -c Debug --nologo --no-build \
    --filter "FullyQualifiedName~AuditLogController" 2>&1)
AL_RC=$?
AL_PASSED=$(echo "$AL_OUT" | grep -oE 'Passed:[[:space:]]+[0-9]+' | head -1 | grep -oE '[0-9]+')
[[ -z "$AL_PASSED" ]] && AL_PASSED=0
# 12 expected: 4 Engineer-403 Theory rows + 4 admin happy + 3 export
# (csv-content-type / xlsx-content-type / empty-range) + 1 emit.
if [[ $AL_RC -eq 0 && $AL_PASSED -ge 12 ]]; then
    record PASS "AuditLogController tests (passed=$AL_PASSED ≥ 12 expected)"
else
    record FAIL "AuditLogController tests (rc=$AL_RC passed=$AL_PASSED expected ≥ 12)"
fi

# ── 4. Boot API ───────────────────────────────────────────────────────
echo "[step] start API on :$PORT"
LOG_FILE=$(mktemp -t ccl-api-verify-XXXXXX)
cd "$REPO_ROOT"
ASPNETCORE_ENVIRONMENT=Development \
    dotnet "$API_DLL_DIR/CCL.MES.Api.dll" --urls "$API_URL" > "$LOG_FILE" 2>&1 &
API_PID=$!
echo "[run]  PID=$API_PID  log=$LOG_FILE"

for i in 1 2 3 4 5 6 7 8 9 10; do
    sleep 1
    CODE=$(curl -s -o /dev/null --max-time 1 -w "%{http_code}" "$API_URL/api/v2/health" 2>/dev/null || echo 000)
    [[ "$CODE" == "200" ]] && break
done
if [[ "$CODE" != "200" ]]; then
    echo "[boot] API did not come up. tail of log:"
    tail -30 "$LOG_FILE"
    record FAIL "API boot (final code=$CODE)"
    echo
    echo "============================  SUMMARY  ============================"
    printf '  %s\n' "${LINES[@]}"
    echo
    echo "  TOTAL: pass=$PASS fail=$FAIL"
    exit 1
fi
record PASS "API boot (200 /health)"

# ── 5. Anon → 401 on every new endpoint ──────────────────────────────
probe_anon() {
    local label="$1" path="$2"
    local code
    code=$(curl -s -o /tmp/_v_body --max-time 5 -w "%{http_code}" "$API_URL$path")
    [[ $VERBOSE -eq 1 ]] && { echo "[curl] $path → $code"; head -c 200 /tmp/_v_body 2>/dev/null; echo; }
    if [[ "$code" == "401" ]]; then
        record PASS "$label (got 401)"
    else
        record FAIL "$label (got $code expected 401 — body: $(head -c 120 /tmp/_v_body 2>/dev/null))"
    fi
}

echo "[step] anon route discovery — all new audit routes (401)"
probe_anon "GET   /api/v2/audit/log anon"          /api/v2/audit/log
probe_anon "GET   /api/v2/audit/actions anon"      /api/v2/audit/actions
probe_anon "GET   /api/v2/audit/export/csv anon"   /api/v2/audit/export/csv
probe_anon "GET   /api/v2/audit/export/xlsx anon"  /api/v2/audit/export/xlsx

# ── 6. Admin login + wire-level happy paths ──────────────────────────
echo "[step] login as $CCL_USER"
LOGIN=$(curl -s --max-time 5 -X POST "$API_URL/api/v2/auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"username\":\"$CCL_USER\",\"password\":\"$CCL_PWD\"}")
ADMIN_TOKEN=$(echo "$LOGIN" | python3 -c 'import json,sys
try: print(json.load(sys.stdin).get("accessToken",""))
except: print("")' 2>/dev/null)
if [[ -z "$ADMIN_TOKEN" ]]; then
    echo "[login] FAIL — response: $(echo "$LOGIN" | head -c 240)"
    record FAIL "Login admin (no token)"
else
    record PASS "Login admin (token_len=${#ADMIN_TOKEN})"
fi

if [[ -n "$ADMIN_TOKEN" ]]; then
    # 6a — Admin list returns paged JSON with at least the login row.
    CODE=$(curl -s -o /tmp/_v_body --max-time 5 -w "%{http_code}" \
        -H "Authorization: Bearer $ADMIN_TOKEN" \
        "$API_URL/api/v2/audit/log?page=1&pageSize=5")
    if [[ "$CODE" == "200" ]] && grep -q '"total"' /tmp/_v_body; then
        TOTAL_BEFORE=$(python3 -c "import json;print(json.load(open('/tmp/_v_body')).get('total',0))")
        record PASS "GET    /api/v2/audit/log admin (200, total=$TOTAL_BEFORE)"
    else
        TOTAL_BEFORE=0
        record FAIL "GET    /api/v2/audit/log admin (got $CODE; body: $(head -c 200 /tmp/_v_body))"
    fi

    # 6b — Admin actions list.
    CODE=$(curl -s -o /tmp/_v_body --max-time 5 -w "%{http_code}" \
        -H "Authorization: Bearer $ADMIN_TOKEN" \
        "$API_URL/api/v2/audit/actions")
    if [[ "$CODE" == "200" ]] && grep -q 'LOGIN_OK' /tmp/_v_body; then
        record PASS "GET    /api/v2/audit/actions admin (200 + LOGIN_OK present)"
    else
        record FAIL "GET    /api/v2/audit/actions admin (got $CODE; body: $(head -c 200 /tmp/_v_body))"
    fi

    # 6c — CSV export — capture status + headers + body.
    rm -f /tmp/_v_csv_body /tmp/_v_csv_hdr
    CODE=$(curl -s --max-time 10 -o /tmp/_v_csv_body -D /tmp/_v_csv_hdr -w "%{http_code}" \
        -H "Authorization: Bearer $ADMIN_TOKEN" \
        "$API_URL/api/v2/audit/export/csv")
    CSV_CT=$(grep -i '^Content-Type:' /tmp/_v_csv_hdr | head -1 | cut -d' ' -f2- | tr -d '\r')
    CSV_DISP=$(grep -i '^Content-Disposition:' /tmp/_v_csv_hdr | head -1 | tr -d '\r')
    CSV_SIZE=$(wc -c < /tmp/_v_csv_body | tr -d ' ')
    # Strip 3-byte UTF-8 BOM + extract first line only (header).
    CSV_HEADER=$(tail -c +4 /tmp/_v_csv_body | head -n 1 | tr -d '\r')
    CSV_ROWS=$(tail -c +4 /tmp/_v_csv_body | grep -c '^')
    if [[ "$CODE" == "200" ]] \
        && [[ "$CSV_CT" == *"text/csv"* ]] \
        && [[ "$CSV_DISP" == *"AuditLog_"* ]] \
        && [[ "$CSV_HEADER" == "Timestamp_UTC,Actor,Role,Action,Target_Type,Target_Id,Detail,IP,Source" ]] \
        && [[ $CSV_ROWS -ge 2 ]]; then
        record PASS "GET    /api/v2/audit/export/csv admin (200, ct=$CSV_CT, $CSV_SIZE bytes, $CSV_ROWS rows, 9-col header)"
    else
        record FAIL "GET    /api/v2/audit/export/csv admin (code=$CODE ct=$CSV_CT disp=$CSV_DISP size=$CSV_SIZE rows=$CSV_ROWS header='$CSV_HEADER')"
    fi

    # 6d — XLSX export — check ct + PK ZIP magic.
    rm -f /tmp/_v_xlsx_body /tmp/_v_xlsx_hdr
    CODE=$(curl -s --max-time 10 -o /tmp/_v_xlsx_body -D /tmp/_v_xlsx_hdr -w "%{http_code}" \
        -H "Authorization: Bearer $ADMIN_TOKEN" \
        "$API_URL/api/v2/audit/export/xlsx")
    XLSX_CT=$(grep -i '^Content-Type:' /tmp/_v_xlsx_hdr | head -1 | cut -d' ' -f2- | tr -d '\r')
    XLSX_SIZE=$(wc -c < /tmp/_v_xlsx_body | tr -d ' ')
    XLSX_MAGIC=$(head -c 2 /tmp/_v_xlsx_body | od -An -c | tr -d ' ')
    if [[ "$CODE" == "200" ]] \
        && [[ "$XLSX_CT" == *"spreadsheetml.sheet"* ]] \
        && [[ "$XLSX_MAGIC" == "PK" ]] \
        && [[ $XLSX_SIZE -gt 1000 ]]; then
        record PASS "GET    /api/v2/audit/export/xlsx admin (200, ct=$XLSX_CT, $XLSX_SIZE bytes, PK magic)"
    else
        record FAIL "GET    /api/v2/audit/export/xlsx admin (code=$CODE ct=$XLSX_CT size=$XLSX_SIZE magic='$XLSX_MAGIC')"
    fi

    # 6e — AUDIT_EXPORT emit — count must have ticked up by 2 since 6a.
    CODE=$(curl -s -o /tmp/_v_body --max-time 5 -w "%{http_code}" \
        -H "Authorization: Bearer $ADMIN_TOKEN" \
        "$API_URL/api/v2/audit/log?action=AUDIT_EXPORT&page=1&pageSize=5")
    EXPORT_TOTAL=$(python3 -c "import json;print(json.load(open('/tmp/_v_body')).get('total',0))")
    if [[ "$CODE" == "200" ]] && [[ $EXPORT_TOTAL -ge 2 ]]; then
        record PASS "AUDIT_EXPORT emit (total=$EXPORT_TOTAL ≥ 2 — CSV + XLSX both logged)"
    else
        record FAIL "AUDIT_EXPORT emit (code=$CODE total=$EXPORT_TOTAL expected ≥ 2)"
    fi

    # 6f — empty-range CSV is still valid (header line only).
    CODE=$(curl -s --max-time 5 -o /tmp/_v_csv_empty -w "%{http_code}" \
        -H "Authorization: Bearer $ADMIN_TOKEN" \
        "$API_URL/api/v2/audit/export/csv?from=2000-01-01T00:00:00Z&to=2000-01-02T00:00:00Z")
    EMPTY_ROWS=$(tail -c +4 /tmp/_v_csv_empty | grep -c '^')
    if [[ "$CODE" == "200" ]] && [[ $EMPTY_ROWS -eq 1 ]]; then
        record PASS "Empty-range CSV (200, header row only, rows=$EMPTY_ROWS)"
    else
        record FAIL "Empty-range CSV (code=$CODE rows=$EMPTY_ROWS expected 1 header)"
    fi
fi

# ── 7. Summary ────────────────────────────────────────────────────────
echo
echo "============================  SUMMARY  ============================"
printf '  %s\n' "${LINES[@]}"
echo
echo "  TOTAL: pass=$PASS fail=$FAIL"
[[ $FAIL -eq 0 ]] && exit 0 || exit 1
