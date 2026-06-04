#!/usr/bin/env bash
# P10.6a end-to-end verify script. ONE COMMAND drives:
#   1. Kill anything on :5100
#   2. Build API on current branch + log commit
#   3. Run API in background
#   4. Curl each Settings route — anonymous + bearer-authenticated
#   5. Print PASS/FAIL per row + summary
#   6. Kill server on exit
#
# Pre-req: data/ccl_mes.db exists and has at least one admin user.
# Default expects admin/admin. Override via env: CCL_USER, CCL_PWD.
#
# Usage:
#   ./scripts/verify-p10.6a.sh           # human-readable, exits non-zero on FAIL
#   ./scripts/verify-p10.6a.sh --verbose # dump every curl body too

set -u
set +e   # we want to keep going on individual curl failures and report

VERBOSE=0
[[ "${1:-}" == "--verbose" ]] && VERBOSE=1

CCL_USER=${CCL_USER:-admin}
CCL_PWD=${CCL_PWD:-admin}
PORT=5100
API_URL="http://127.0.0.1:${PORT}"

# Walk up from script dir to repo root (look for the legacy .sln)
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HYBRID_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$HYBRID_ROOT/.." && pwd)"
API_PROJECT="$HYBRID_ROOT/src/CCL.MES.Api/CCL.MES.Api.csproj"
API_DLL_DIR="$HYBRID_ROOT/src/CCL.MES.Api/bin/Debug/net10.0"

PASS=0
FAIL=0
declare -a LINES=()

record() {
    local status="$1"; local label="$2"
    if [[ "$status" == "PASS" ]]; then
        PASS=$((PASS+1))
        LINES+=("PASS  $label")
    else
        FAIL=$((FAIL+1))
        LINES+=("FAIL  $label")
    fi
}

KEEP_ALIVE=0
[[ "${1:-}" == "--keep-alive" || "${2:-}" == "--keep-alive" ]] && KEEP_ALIVE=1

cleanup() {
    if [[ $KEEP_ALIVE -eq 1 && $FAIL -eq 0 ]]; then
        echo
        echo "[keep-alive] server still running on :$PORT (PID $API_PID)."
        echo "             Open Mac Catalyst app now and test /settings/*."
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
echo "P10.6a verify — $(date '+%Y-%m-%d %H:%M:%S')"
echo "===================================================================="
echo "[ctx]  repo  = $REPO_ROOT"
echo "[ctx]  branch= $(cd "$REPO_ROOT" && git rev-parse --abbrev-ref HEAD)"
echo "[ctx]  HEAD  = $(cd "$REPO_ROOT" && git rev-parse --short HEAD)"
echo "[ctx]  user  = $CCL_USER"
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
echo "[step] build API"
BUILD_OUT=$(cd "$REPO_ROOT" && dotnet build "$API_PROJECT" -c Debug --nologo -v q 2>&1)
BUILD_RC=$?
echo "[build] exit=$BUILD_RC"
if [[ $BUILD_RC -ne 0 ]]; then
    echo "$BUILD_OUT" | tail -20
    record FAIL "Build (rc=$BUILD_RC)"
    echo
    echo "============================  SUMMARY  ============================"
    printf '  %s\n' "${LINES[@]}"
    echo
    echo "  TOTAL: pass=$PASS fail=$FAIL"
    exit 1
fi
DLL_STAT=$(stat -f "%Sm %z bytes" "$API_DLL_DIR/CCL.MES.Api.dll" 2>/dev/null \
    || stat -c "%y %s bytes" "$API_DLL_DIR/CCL.MES.Api.dll" 2>/dev/null)
echo "[build] $DLL_STAT"
record PASS "Build (commit $(cd "$REPO_ROOT" && git rev-parse --short HEAD))"

# ── 2. Run API in background ──────────────────────────────────────────
echo "[step] start API on :$PORT"
LOG_FILE=$(mktemp -t ccl-api-verify-XXXXXX)
cd "$REPO_ROOT"
ASPNETCORE_ENVIRONMENT=Development \
    dotnet "$API_DLL_DIR/CCL.MES.Api.dll" --urls "$API_URL" > "$LOG_FILE" 2>&1 &
API_PID=$!
echo "[run]  PID=$API_PID  log=$LOG_FILE"

# Wait for /health to respond — bounded
for i in 1 2 3 4 5 6 7 8 9 10; do
    sleep 1
    CODE=$(curl -s -o /dev/null --max-time 1 -w "%{http_code}" "$API_URL/api/v2/health" 2>/dev/null || echo 000)
    [[ "$CODE" == "200" ]] && break
done
if [[ "$CODE" != "200" ]]; then
    echo "[boot] API did not come up. tail of log:"
    tail -30 "$LOG_FILE"
    record FAIL "API boot (final code=$CODE)"
else
    record PASS "API boot (200 /health)"
fi

# ── 3. Route discovery — anonymous 401, NOT 404 ───────────────────────
probe_route() {
    local label="$1" method="$2" path="$3" expect="$4"
    local code
    if [[ "$method" == "GET" ]]; then
        code=$(curl -s -o /tmp/_v_body --max-time 5 -w "%{http_code}" -X GET "$API_URL$path")
    else
        code=$(curl -s -o /tmp/_v_body --max-time 5 -w "%{http_code}" -X "$method" \
            -H 'Content-Type: application/json' -d '{}' "$API_URL$path")
    fi
    [[ $VERBOSE -eq 1 ]] && { echo "[curl] $method $path → $code"; head -c 200 /tmp/_v_body 2>/dev/null; echo; }
    if [[ "$code" == "$expect" ]]; then
        record PASS "$label (got $code expected $expect)"
    else
        record FAIL "$label (got $code expected $expect — body: $(head -c 120 /tmp/_v_body 2>/dev/null))"
    fi
}

echo "[step] route discovery — anon"
probe_route "GET   /api/v2/settings/me anon"        GET   /api/v2/settings/me        401
probe_route "PATCH /api/v2/settings/me anon"        PATCH /api/v2/settings/me        401
probe_route "POST  /api/v2/settings/password anon"  POST  /api/v2/settings/password  401

# ── 4. Login + happy paths ────────────────────────────────────────────
echo "[step] login as $CCL_USER"
LOGIN=$(curl -s --max-time 5 -X POST "$API_URL/api/v2/auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"username\":\"$CCL_USER\",\"password\":\"$CCL_PWD\"}")
TOKEN=$(echo "$LOGIN" | python3 -c 'import json,sys
try: print(json.load(sys.stdin).get("accessToken",""))
except: print("")' 2>/dev/null)
if [[ -z "$TOKEN" ]]; then
    echo "[login] FAIL — response: $(echo "$LOGIN" | head -c 240)"
    record FAIL "Login $CCL_USER (no token)"
else
    record PASS "Login $CCL_USER (token_len=${#TOKEN})"
fi

if [[ -n "$TOKEN" ]]; then
    # 4a — GET /me → 200 + profile JSON
    GET_CODE=$(curl -s -o /tmp/_v_body --max-time 5 -w "%{http_code}" \
        -H "Authorization: Bearer $TOKEN" "$API_URL/api/v2/settings/me")
    GET_BODY=$(cat /tmp/_v_body)
    [[ $VERBOSE -eq 1 ]] && echo "[curl] GET /me auth → $GET_CODE  $GET_BODY"
    if [[ "$GET_CODE" == "200" ]] && echo "$GET_BODY" | python3 -c 'import json,sys; d=json.load(sys.stdin); assert "username" in d and "role" in d' 2>/dev/null; then
        UN=$(echo "$GET_BODY" | python3 -c 'import json,sys;print(json.load(sys.stdin)["username"])')
        ROLE=$(echo "$GET_BODY" | python3 -c 'import json,sys;print(json.load(sys.stdin)["role"])')
        record PASS "GET    /api/v2/settings/me auth (200, username=$UN, role=$ROLE)"
    else
        record FAIL "GET    /api/v2/settings/me auth (code=$GET_CODE, body=$(head -c 200 /tmp/_v_body))"
    fi

    # 4b — PATCH /me set DisplayName
    NEW_NAME="Verify-$(date +%H%M%S)"
    PATCH_CODE=$(curl -s -o /tmp/_v_body --max-time 5 -w "%{http_code}" -X PATCH \
        -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
        -d "{\"displayName\":\"$NEW_NAME\"}" "$API_URL/api/v2/settings/me")
    [[ $VERBOSE -eq 1 ]] && { echo "[curl] PATCH /me → $PATCH_CODE"; head -c 200 /tmp/_v_body; echo; }
    if [[ "$PATCH_CODE" == "200" ]] && grep -q "$NEW_NAME" /tmp/_v_body; then
        record PASS "PATCH  /api/v2/settings/me auth (DisplayName=$NEW_NAME)"
    else
        record FAIL "PATCH  /api/v2/settings/me auth (code=$PATCH_CODE, body=$(head -c 200 /tmp/_v_body))"
    fi

    # 4c — PATCH /me 101 chars → 422 profile.display_name_too_long
    LONG=$(python3 -c 'print("x"*101)')
    LONG_CODE=$(curl -s -o /tmp/_v_body --max-time 5 -w "%{http_code}" -X PATCH \
        -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
        -d "{\"displayName\":\"$LONG\"}" "$API_URL/api/v2/settings/me")
    [[ $VERBOSE -eq 1 ]] && { echo "[curl] PATCH /me 101ch → $LONG_CODE"; head -c 200 /tmp/_v_body; echo; }
    if [[ "$LONG_CODE" == "422" ]] && grep -q "profile.display_name_too_long" /tmp/_v_body; then
        record PASS "PATCH  /api/v2/settings/me long (422 profile.display_name_too_long)"
    else
        record FAIL "PATCH  /api/v2/settings/me long (code=$LONG_CODE, body=$(head -c 200 /tmp/_v_body))"
    fi

    # 4d — POST /password wrong_current → 422
    WRONG_CODE=$(curl -s -o /tmp/_v_body --max-time 5 -w "%{http_code}" -X POST \
        -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
        -d '{"currentPassword":"DEFINITELY_WRONG","newPassword":"DOES-NOT-MATTER"}' \
        "$API_URL/api/v2/settings/password")
    [[ $VERBOSE -eq 1 ]] && { echo "[curl] POST /password wrong → $WRONG_CODE"; head -c 200 /tmp/_v_body; echo; }
    if [[ "$WRONG_CODE" == "422" ]] && grep -q "auth.wrong_current" /tmp/_v_body; then
        record PASS "POST   /api/v2/settings/password wrong (422 auth.wrong_current)"
    else
        record FAIL "POST   /api/v2/settings/password wrong (code=$WRONG_CODE, body=$(head -c 200 /tmp/_v_body))"
    fi

    # 4e — POST /password short → 422
    SHORT_CODE=$(curl -s -o /tmp/_v_body --max-time 5 -w "%{http_code}" -X POST \
        -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
        -d "{\"currentPassword\":\"$CCL_PWD\",\"newPassword\":\"abc\"}" \
        "$API_URL/api/v2/settings/password")
    [[ $VERBOSE -eq 1 ]] && { echo "[curl] POST /password short → $SHORT_CODE"; head -c 200 /tmp/_v_body; echo; }
    if [[ "$SHORT_CODE" == "422" ]] && grep -q "auth.new_too_short" /tmp/_v_body; then
        record PASS "POST   /api/v2/settings/password short (422 auth.new_too_short)"
    else
        record FAIL "POST   /api/v2/settings/password short (code=$SHORT_CODE, body=$(head -c 200 /tmp/_v_body))"
    fi
fi

# ── 5. Summary ────────────────────────────────────────────────────────
echo
echo "============================  SUMMARY  ============================"
printf '  %s\n' "${LINES[@]}"
echo
echo "  TOTAL: pass=$PASS fail=$FAIL"
[[ $FAIL -eq 0 ]] && exit 0 || exit 1
