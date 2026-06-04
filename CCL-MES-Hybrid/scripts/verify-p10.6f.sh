#!/usr/bin/env bash
# P10.6f end-to-end verify script. Pattern locked in P10.6a.
#
# Usage (always from repo root):
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.6f.sh
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.6f.sh --keep-alive
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.6f.sh --verbose
#
# Scope:
#   P10.6f ships zero new server endpoints — Recent Scans is a
#   client-only, Preferences-backed widget. The verify script therefore
#   focuses on (1) the new client lib code paths via unit tests,
#   (2) the existing server still boots + serves the Settings + canary
#   surface clean (no regression), and (3) the InputText canary stays
#   green so the renderer-dead lesson can't recur.
#
# What it does:
#   1. Kill anything on :5100.
#   2. Build the API + Client lib + Razor lib on current branch.
#   3. Run client unit tests — includes the 15 new RecentScans tests.
#   4. Run API unit tests — full 164 regression sweep.
#   5. Boot API + probe canary endpoints stay 401 anon (route discovery
#      regression guard).
#   6. Print per-row PASS/FAIL + summary; exit non-zero on any FAIL.
#   7. On full PASS + --keep-alive, leave the server up + print its
#      PID so the Catalyst app boot + scan widget test hits the same
#      proven binary.

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
        echo "             Open Mac Catalyst app now and verify:"
        echo "             1. Log in → no sidebar widget yet (empty store)."
        echo "             2. Settings → Thiết bị / Mode → enable scanner (if not already)."
        echo "             3. Work Orders → Quét → scan a real WO code."
        echo "             4. Sidebar: 'QUÉT GẦN ĐÂY' section appears with 1 row."
        echo "             5. Scan another code → second row stacks at top."
        echo "             6. Tap a row → re-opens /workorders."
        echo "             7. Header 'Xoá' button → confirms then wipes."
        echo "             8. Quit + relaunch app → list survives (Preferences persisted)."
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
echo "P10.6f verify — $(date '+%Y-%m-%d %H:%M:%S')"
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
echo "[build] exit=$BUILD_RC"
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
DLL_STAT=$(stat -f "%Sm %z bytes" "$API_DLL_DIR/CCL.MES.Api.dll" 2>/dev/null \
    || stat -c "%y %s bytes" "$API_DLL_DIR/CCL.MES.Api.dll" 2>/dev/null)
echo "[build] $DLL_STAT"
record PASS "API build (commit $(cd "$REPO_ROOT" && git rev-parse --short HEAD))"

# ── 2. Run client unit tests (incl. new RecentScans coverage) ────────
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

# ── 2b. Confirm new RecentScans coverage exists + passes ─────────────
echo "[step] filter-run RecentScans tests to prove coverage"
RS_OUT=$(cd "$REPO_ROOT" && dotnet test "$CLIENT_TESTS" -c Debug --nologo --no-build \
    --filter "FullyQualifiedName~RecentScans" 2>&1)
RS_RC=$?
RS_PASSED=$(echo "$RS_OUT" | grep -oE 'Passed:[[:space:]]+[0-9]+' | head -1 | grep -oE '[0-9]+')
[[ -z "$RS_PASSED" ]] && RS_PASSED=0
[[ $VERBOSE -eq 1 ]] && echo "$RS_OUT" | tail -6
# 16 expected: 11 in InMemoryRecentScansServiceTests + 5 in RecentScansSerializerTests.
if [[ $RS_RC -eq 0 && $RS_PASSED -ge 16 ]]; then
    record PASS "RecentScans tests (passed=$RS_PASSED ≥ 16 expected)"
else
    record FAIL "RecentScans tests (rc=$RS_RC passed=$RS_PASSED expected ≥ 16)"
fi

# ── 3. Run API unit tests (full regression sweep) ─────────────────────
echo "[step] run API unit tests"
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

# ── 4. Boot API + smoke-probe route discovery canaries ────────────────
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

# ── 5. Anon route discovery — every Settings route still 401 not 404 ──
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

echo "[step] route discovery — anon (regression guard)"
probe_route "GET   /api/v2/settings/me anon"        GET   /api/v2/settings/me        401
probe_route "GET   /api/v2/settings/about anon"     GET   /api/v2/settings/about     401

# ── 6. Summary ────────────────────────────────────────────────────────
echo
echo "============================  SUMMARY  ============================"
printf '  %s\n' "${LINES[@]}"
echo
echo "  TOTAL: pass=$PASS fail=$FAIL"
[[ $FAIL -eq 0 ]] && exit 0 || exit 1
