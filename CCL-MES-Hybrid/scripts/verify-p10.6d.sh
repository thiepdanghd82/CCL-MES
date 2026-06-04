#!/usr/bin/env bash
# P10.6d end-to-end verify script. Pattern locked in P10.6a.
#
# Usage (always from repo root):
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.6d.sh
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.6d.sh --keep-alive
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.6d.sh --verbose
#
# What it does:
#   1. Kill anything on :5100.
#   2. Build the API on current branch + log the commit SHA.
#   3. Run API on the freshly-loaded .dll (NOT the stale mmap'd
#      process).
#   4. Probe each Settings route — anonymous + bearer-authenticated —
#      and assert the expected HTTP status / body fragment.
#   5. Print per-row PASS/FAIL + summary; exit non-zero on any FAIL.
#   6. On full PASS + --keep-alive, leave the server up + print its
#      PID so the Catalyst test hits the same proven binary.

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
        echo "             Open Mac Catalyst app now and test /settings/about + /settings/connection."
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
echo "P10.6d verify — $(date '+%Y-%m-%d %H:%M:%S')"
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
probe_route "GET   /api/v2/settings/about anon"     GET   /api/v2/settings/about     401

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
    # 4-pre — bearer-auth'd 404 sanity: confirm routing actually
    # discriminates real-vs-fake paths once auth lets the request
    # through. Anon path returns 401 first via the fallback policy so
    # we can't tell route-exists from auth-blocks at that layer.
    FAKE_CODE=$(curl -s -o /tmp/_v_body --max-time 5 -w "%{http_code}" \
        -H "Authorization: Bearer $TOKEN" "$API_URL/api/v2/settings/this-route-does-not-exist")
    [[ $VERBOSE -eq 1 ]] && { echo "[curl] GET fake auth → $FAKE_CODE"; head -c 200 /tmp/_v_body; echo; }
    if [[ "$FAKE_CODE" == "404" ]]; then
        record PASS "GET    fake settings route auth (404 — routing discriminates real vs fake)"
    else
        record FAIL "GET    fake settings route auth (got $FAKE_CODE expected 404)"
    fi

    # 4a — GET /me sanity (carry-over from 6a)
    GET_CODE=$(curl -s -o /tmp/_v_body --max-time 5 -w "%{http_code}" \
        -H "Authorization: Bearer $TOKEN" "$API_URL/api/v2/settings/me")
    [[ $VERBOSE -eq 1 ]] && { echo "[curl] GET /me auth → $GET_CODE"; head -c 200 /tmp/_v_body; echo; }
    if [[ "$GET_CODE" == "200" ]] && grep -q '"username"' /tmp/_v_body; then
        record PASS "GET    /api/v2/settings/me auth (200 + username)"
    else
        record FAIL "GET    /api/v2/settings/me auth (code=$GET_CODE, body=$(head -c 200 /tmp/_v_body))"
    fi

    # 4b — GET /about returns 200 + AboutDto shape
    ABOUT_CODE=$(curl -s -o /tmp/_v_body --max-time 5 -w "%{http_code}" \
        -H "Authorization: Bearer $TOKEN" "$API_URL/api/v2/settings/about")
    [[ $VERBOSE -eq 1 ]] && { echo "[curl] GET /about auth → $ABOUT_CODE"; head -c 500 /tmp/_v_body; echo; }
    if [[ "$ABOUT_CODE" == "200" ]] \
       && grep -q '"serverVersion"' /tmp/_v_body \
       && grep -q '"environmentName"' /tmp/_v_body \
       && grep -q '"userCount"' /tmp/_v_body; then
        SVER=$(python3 -c "import json;print(json.load(open('/tmp/_v_body')).get('serverVersion',''))")
        ENV=$(python3 -c  "import json;print(json.load(open('/tmp/_v_body')).get('environmentName',''))")
        UC=$(python3 -c   "import json;print(json.load(open('/tmp/_v_body')).get('userCount',-1))")
        record PASS "GET    /api/v2/settings/about auth (200, serverVersion=$SVER env=$ENV users=$UC)"
    else
        record FAIL "GET    /api/v2/settings/about auth (code=$ABOUT_CODE, body=$(head -c 240 /tmp/_v_body))"
    fi

    # 4c — About body machineName + serverTimeUtc both set
    if [[ "$ABOUT_CODE" == "200" ]]; then
        MN=$(python3 -c "import json;d=json.load(open('/tmp/_v_body'));print(d.get('machineName',''))")
        TM=$(python3 -c "import json;d=json.load(open('/tmp/_v_body'));print(d.get('serverTimeUtc',''))")
        if [[ -n "$MN" && -n "$TM" ]]; then
            record PASS "       /about body (machineName=$MN, serverTimeUtc=$TM)"
        else
            record FAIL "       /about body (machineName=$MN, serverTimeUtc=$TM — missing field)"
        fi
    fi
fi

# ── 5. Summary ────────────────────────────────────────────────────────
echo
echo "============================  SUMMARY  ============================"
printf '  %s\n' "${LINES[@]}"
echo
echo "  TOTAL: pass=$PASS fail=$FAIL"
[[ $FAIL -eq 0 ]] && exit 0 || exit 1
