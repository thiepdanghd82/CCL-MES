#!/usr/bin/env bash
# P10.7a-1.2 end-to-end verify script. SUPERSET of verify-p10.7a-1.sh:
# every probe from -1 (build + parity + suites + migration round-trip
# for the MesPhase+RowVersion migration) plus the new idempotency
# scope:
#
#   - IdempotencyMiddlewareTests filter (12/12 PASS)
#   - Migration AddIdempotencyKeyLedger applied / reverted / re-applied
#     on a copy of the real data/ccl_mes.db
#   - Wire probes against a live API:
#       same key + same body twice → 1 execute, 1 replay
#       (Idempotency-Replayed: true header on the second response)
#       same key + DIFFERENT body → 422 + IDEMPOTENCY_REPLAY audit row
#       key > 64 chars → 400
#
# Henry condition (c): parity sweep [Category=LegacyParity] STILL
# runs as probe #3. Any drift = stack-wide fail.
#
# Usage (always from repo root parent of CCL-MES-Hybrid):
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.7a-2.sh
#   cd CCL-MES-Hybrid && ./scripts/verify-p10.7a-2.sh --verbose

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
WEB_PROJECT="$REPO_ROOT/src/CCL.MES.Web/CCL.MES.Web.csproj"
API_PROJECT="$HYBRID_ROOT/src/CCL.MES.Api/CCL.MES.Api.csproj"
LEGACY_TESTS="$REPO_ROOT/tests/CCL.MES.Tests/CCL.MES.Tests.csproj"
API_TESTS="$HYBRID_ROOT/tests/CCL.MES.Api.Tests/CCL.MES.Api.Tests.csproj"

# Migration tracking
CURRENT_MIGRATION="20260605053109_AddIdempotencyKeyLedger"
PREVIOUS_MIGRATION="20260605045839_AddWorkOrderRowVersionAndMesPhase"

REAL_DB="$REPO_ROOT/data/ccl_mes.db"
TMP_DIR="$(mktemp -d -t ccl-verify-p10.7a-2-XXXXXX)"
TEST_DB="$TMP_DIR/ccl_mes_test.db"

# Live API config
PORT=5100  # appsettings.json hardcodes this; kill stale process first
API_URL="http://127.0.0.1:${PORT}"
API_LOG="$TMP_DIR/api.log"
API_PID=""

CCL_USER="verify-7a2-admin"
CCL_PWD="verifypass1234"

PASS=0
FAIL=0
SUMMARY=()

echo "===================================================================="
echo "P10.7a-1.2 verify — $(date '+%Y-%m-%d %H:%M:%S')"
echo "===================================================================="
echo "[ctx]  repo     = $REPO_ROOT"
echo "[ctx]  branch   = $(cd "$REPO_ROOT" && git branch --show-current)"
echo "[ctx]  HEAD     = $(cd "$REPO_ROOT" && git rev-parse --short HEAD)"
echo "[ctx]  test DB  = $TEST_DB (copy of $REAL_DB)"
echo "[ctx]  curr mig = $CURRENT_MIGRATION"
echo "[ctx]  prev mig = $PREVIOUS_MIGRATION"
echo "[ctx]  api port = $PORT"
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

cleanup_api() {
    if [[ -n "$API_PID" ]] && kill -0 "$API_PID" 2>/dev/null; then
        kill "$API_PID" 2>/dev/null
        wait "$API_PID" 2>/dev/null
    fi
}
trap cleanup_api EXIT

# ── Step 1: full solution build ───────────────────────────────────
echo "[step] full solution build"
BUILD_LOG="$(mktemp)"
(cd "$REPO_ROOT" && dotnet build CCL.MES.sln --nologo --verbosity quiet) > "$BUILD_LOG" 2>&1
BUILD_EXIT=$?
if [[ $BUILD_EXIT -eq 0 ]]; then
    record PASS "Build (CCL.MES.sln — $(cd "$REPO_ROOT" && git rev-parse --short HEAD))"
else
    tail -20 "$BUILD_LOG"; record FAIL "Build CCL.MES.sln"
fi

HYBRID_BUILD_LOG="$(mktemp)"
(cd "$REPO_ROOT" && dotnet build CCL-MES-Hybrid/CCL-MES-Hybrid.sln --nologo --verbosity quiet) > "$HYBRID_BUILD_LOG" 2>&1
if [[ $? -eq 0 ]]; then
    record PASS "Build (CCL-MES-Hybrid.sln)"
else
    tail -20 "$HYBRID_BUILD_LOG"; record FAIL "Build CCL-MES-Hybrid.sln"
fi

# ── Step 2: legacy parity filter (Henry condition (c)) ────────────
echo "[step] legacy parity sweep (Henry condition (c) — every PR of stack)"
PARITY_LOG="$(mktemp)"
dotnet test "$LEGACY_TESTS" --filter "Category=LegacyParity" --nologo --verbosity quiet > "$PARITY_LOG" 2>&1
PARITY_PASSED=$(grep -oE "Passed:\s*[0-9]+" "$PARITY_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
PARITY_FAILED=$(grep -oE "Failed:\s*[0-9]+" "$PARITY_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
if [[ "$PARITY_PASSED" == "8" && "$PARITY_FAILED" == "0" ]]; then
    record PASS "Legacy parity sweep (8/8 PASS — CanAdvance(wo) behavior unchanged)"
else
    tail -10 "$PARITY_LOG"; record FAIL "Legacy parity (passed=$PARITY_PASSED failed=$PARITY_FAILED)"
fi

# ── Step 3: full legacy test sweep ────────────────────────────────
echo "[step] full legacy CCL.MES.Tests"
LEGACY_LOG="$(mktemp)"
dotnet test "$LEGACY_TESTS" --nologo --verbosity quiet > "$LEGACY_LOG" 2>&1
LEGACY_PASSED=$(grep -oE "Passed:\s*[0-9]+" "$LEGACY_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
LEGACY_FAILED=$(grep -oE "Failed:\s*[0-9]+" "$LEGACY_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
if [[ "$LEGACY_FAILED" == "0" ]]; then
    record PASS "Legacy tests ($LEGACY_PASSED PASS / 0 FAIL)"
else
    tail -10 "$LEGACY_LOG"; record FAIL "Legacy tests (passed=$LEGACY_PASSED failed=$LEGACY_FAILED)"
fi

# ── Step 4: full Hybrid Api tests ─────────────────────────────────
echo "[step] full CCL.MES.Api.Tests"
APIT_LOG="$(mktemp)"
dotnet test "$API_TESTS" --nologo --verbosity quiet > "$APIT_LOG" 2>&1
APIT_PASSED=$(grep -oE "Passed:\s*[0-9]+" "$APIT_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
APIT_FAILED=$(grep -oE "Failed:\s*[0-9]+" "$APIT_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
if [[ "$APIT_FAILED" == "0" ]]; then
    record PASS "Hybrid Api.Tests ($APIT_PASSED PASS / 0 FAIL)"
else
    tail -10 "$APIT_LOG"; record FAIL "Hybrid Api.Tests (passed=$APIT_PASSED failed=$APIT_FAILED)"
fi

# ── Step 5: filter-run idempotency middleware tests ───────────────
echo "[step] filter-run IdempotencyMiddlewareTests"
IDEM_LOG="$(mktemp)"
dotnet test "$API_TESTS" --filter "FullyQualifiedName~IdempotencyMiddleware" --nologo --verbosity quiet > "$IDEM_LOG" 2>&1
IDEM_PASSED=$(grep -oE "Passed:\s*[0-9]+" "$IDEM_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
IDEM_FAILED=$(grep -oE "Failed:\s*[0-9]+" "$IDEM_LOG" | head -1 | grep -oE "[0-9]+" | tail -1)
if [[ "$IDEM_PASSED" == "12" && "$IDEM_FAILED" == "0" ]]; then
    record PASS "IdempotencyMiddlewareTests (12/12 PASS)"
else
    tail -10 "$IDEM_LOG"; record FAIL "IdempotencyMiddlewareTests (passed=$IDEM_PASSED failed=$IDEM_FAILED)"
fi

# ── Step 6: migration round-trip on copy of real data ─────────────
echo "[step] migration round-trip on copy of real data/ccl_mes.db"
if [[ ! -f "$REAL_DB" ]]; then
    record FAIL "Real DB not found at $REAL_DB"
else
    cp "$REAL_DB" "$TEST_DB"
    BEFORE_BYTES=$(stat -f%z "$TEST_DB" 2>/dev/null || stat -c%s "$TEST_DB" 2>/dev/null)
    BEFORE_WO_COUNT=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WorkOrders;" 2>/dev/null)
    record PASS "Test DB copy ($BEFORE_BYTES bytes, $BEFORE_WO_COUNT WO rows)"

    # NB: the test DB does NOT have ANY migrations applied yet (it's a
    # copy of data/ccl_mes.db which is behind on EF history). The
    # dotnet ef database update will apply ALL pending migrations,
    # including the prior 7a-1.1 one + this new one.

    # Confirm IdempotencyKeys table NOT present pre-migration.
    TABLE_BEFORE=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='IdempotencyKeys';" 2>/dev/null)
    if [[ "$TABLE_BEFORE" == "0" ]]; then
        record PASS "Pre-migration: IdempotencyKeys table ABSENT"
    else
        record FAIL "Pre-migration: IdempotencyKeys already present"
    fi

    # Up to current
    echo "[step] migration Up — apply via dotnet ef database update"
    UP_LOG="$(mktemp)"
    dotnet ef database update "$CURRENT_MIGRATION" \
        --connection "Data Source=$TEST_DB" \
        --project "$INFRA_PROJECT" --startup-project "$WEB_PROJECT" \
        --no-build > "$UP_LOG" 2>&1
    if [[ $? -eq 0 ]]; then
        record PASS "Migration Up applied (current = $CURRENT_MIGRATION)"
    else
        tail -15 "$UP_LOG"; record FAIL "Migration Up"
    fi

    AFTER_WO_COUNT=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM WorkOrders;" 2>/dev/null)
    if [[ "$AFTER_WO_COUNT" == "$BEFORE_WO_COUNT" ]]; then
        record PASS "Row count preserved post-Up ($BEFORE_WO_COUNT == $AFTER_WO_COUNT)"
    else
        record FAIL "Row count drifted ($BEFORE_WO_COUNT → $AFTER_WO_COUNT)"
    fi

    TABLE_AFTER=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='IdempotencyKeys';" 2>/dev/null)
    if [[ "$TABLE_AFTER" == "1" ]]; then
        record PASS "Post-Up: IdempotencyKeys table CREATED"
    else
        record FAIL "Post-Up: IdempotencyKeys missing"
    fi

    UNIQUE_IDX=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_IdempotencyKeys_KeyValue_ActorId';" 2>/dev/null)
    if [[ "$UNIQUE_IDX" == "1" ]]; then
        record PASS "Post-Up: unique index (KeyValue, ActorId) CREATED"
    else
        record FAIL "Post-Up: unique index missing"
    fi

    EXPIRES_IDX=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_IdempotencyKeys_ExpiresAtUtc';" 2>/dev/null)
    if [[ "$EXPIRES_IDX" == "1" ]]; then
        record PASS "Post-Up: ExpiresAtUtc index CREATED (TTL sweep ready)"
    else
        record FAIL "Post-Up: ExpiresAtUtc index missing"
    fi

    # Down
    echo "[step] migration Down — revert to $PREVIOUS_MIGRATION"
    DOWN_LOG="$(mktemp)"
    dotnet ef database update "$PREVIOUS_MIGRATION" \
        --connection "Data Source=$TEST_DB" \
        --project "$INFRA_PROJECT" --startup-project "$WEB_PROJECT" \
        --no-build > "$DOWN_LOG" 2>&1
    if [[ $? -eq 0 ]]; then
        record PASS "Migration Down applied (revert to $PREVIOUS_MIGRATION)"
    else
        tail -15 "$DOWN_LOG"; record FAIL "Migration Down"
    fi

    TABLE_DOWN=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='IdempotencyKeys';" 2>/dev/null)
    if [[ "$TABLE_DOWN" == "0" ]]; then
        record PASS "Post-Down: IdempotencyKeys table DROPPED"
    else
        record FAIL "Post-Down: IdempotencyKeys lingered"
    fi

    # Re-apply
    echo "[step] migration re-apply Up — idempotency check"
    REAPPLY_LOG="$(mktemp)"
    dotnet ef database update "$CURRENT_MIGRATION" \
        --connection "Data Source=$TEST_DB" \
        --project "$INFRA_PROJECT" --startup-project "$WEB_PROJECT" \
        --no-build > "$REAPPLY_LOG" 2>&1
    if [[ $? -eq 0 ]]; then
        record PASS "Migration re-Up succeeded (apply/down/re-apply round-trip clean)"
    else
        tail -15 "$REAPPLY_LOG"; record FAIL "Migration re-Up"
    fi
fi

# ── Step 7: wire probes — boot API + exercise idempotency ─────────
echo "[step] kill anything on :$PORT before boot"
EXISTING_PID=$(lsof -nP -iTCP:$PORT -sTCP:LISTEN -t 2>/dev/null | head -1)
if [[ -n "$EXISTING_PID" ]]; then
    echo "[step] killing stale PID $EXISTING_PID"
    kill "$EXISTING_PID" 2>/dev/null
    sleep 1
fi

echo "[step] boot API for wire probes (port $PORT, isolated test DB)"
PROBE_DB="$TMP_DIR/probe.db"
cp "$REAL_DB" "$PROBE_DB"
dotnet ef database update "$CURRENT_MIGRATION" \
    --connection "Data Source=$PROBE_DB" \
    --project "$INFRA_PROJECT" --startup-project "$WEB_PROJECT" \
    --no-build > /dev/null 2>&1

(
    export ConnectionStrings__Default="Data Source=$PROBE_DB"
    export ASPNETCORE_ENVIRONMENT=Development
    dotnet run --project "$API_PROJECT" --no-build > "$API_LOG" 2>&1
) &
API_PID=$!

# Wait for /health
for _ in $(seq 1 40); do
    if curl -sf "$API_URL/api/v2/health" > /dev/null 2>&1 || curl -sf "$API_URL/health" > /dev/null 2>&1; then
        break
    fi
    sleep 0.5
done

HEALTH=$(curl -sf -o /dev/null -w '%{http_code}' "$API_URL/api/v2/health" || echo 000)
if [[ "$HEALTH" == "200" ]]; then
    record PASS "API boot ($API_URL/api/v2/health = 200)"
else
    tail -20 "$API_LOG"; record FAIL "API boot (health=$HEALTH)"
    # Still try to write the summary
fi

if [[ "$HEALTH" == "200" ]]; then
    # Seed user via direct DB insert is awkward — use the public POST
    # /auth/login endpoint with a pre-seeded user. Spin up via the
    # admin-bootstrap script if available; for now, seed a user
    # directly through SQL.
    # Hash a known password via bcrypt is non-trivial in bash; just
    # use the existing "admin" / "admin" fixture that Program.cs
    # seeds on first boot (if applicable). If no auto-seed, this
    # probe section degrades but the xUnit suite still covers
    # behaviour exhaustively.
    LOGIN_RSP=$(curl -s -X POST "$API_URL/api/v2/auth/login" \
        -H "Content-Type: application/json" \
        -d '{"username":"admin","password":"admin"}' 2>&1)
    TOKEN=$(echo "$LOGIN_RSP" | python3 -c "import sys,json;
try:
    d=json.load(sys.stdin); print(d.get('accessToken',''))
except Exception:
    pass" 2>/dev/null)

    if [[ -n "$TOKEN" && "$TOKEN" != "null" ]]; then
        record PASS "Wire login as admin (token length=${#TOKEN})"

        # We'll use a benign mutating endpoint that doesn't change state
        # at all: POST /api/v2/settings/me with profile patch. Or
        # actually we need a guaranteed-existing endpoint. Use
        # /api/v2/audit/export/csv (POST? No, it's GET). Settle for
        # POST /api/v2/auth/refresh which is mutating + always present.
        # Refresh is a good idempotency target: same key = same response.
        REFRESH_TOKEN=$(echo "$LOGIN_RSP" | python3 -c "import sys,json;
try:
    d=json.load(sys.stdin); print(d.get('refreshToken',''))
except: pass" 2>/dev/null)

        KEY=$(uuidgen)
        BODY="{\"refreshToken\":\"$REFRESH_TOKEN\"}"

        # First call
        H1=$(mktemp)
        RSP1=$(curl -s -o "$H1" -w '%{http_code}' \
            -X POST "$API_URL/api/v2/auth/refresh" \
            -H "Content-Type: application/json" \
            -H "Idempotency-Key: $KEY" \
            -d "$BODY")

        if [[ "$RSP1" == "200" ]]; then
            record PASS "Wire first POST with key (200, key=${KEY:0:8})"
        else
            record FAIL "Wire first POST status=$RSP1"
        fi

        # Second call — same key, same body
        H2=$(mktemp)
        REPLAY_HEADER=$(curl -s -o "$H2" -D - \
            -X POST "$API_URL/api/v2/auth/refresh" \
            -H "Content-Type: application/json" \
            -H "Idempotency-Key: $KEY" \
            -d "$BODY" 2>&1 | grep -i "Idempotency-Replayed" || true)

        if [[ -n "$REPLAY_HEADER" ]]; then
            record PASS "Wire replay: Idempotency-Replayed header present"
        else
            record FAIL "Wire replay: header missing"
        fi

        # Compare bodies
        if cmp -s "$H1" "$H2"; then
            record PASS "Wire replay: response body byte-equal"
        else
            record FAIL "Wire replay: body diverged"
        fi

        # Third call — same key, DIFFERENT body
        RSP3=$(curl -s -o /dev/null -w '%{http_code}' \
            -X POST "$API_URL/api/v2/auth/refresh" \
            -H "Content-Type: application/json" \
            -H "Idempotency-Key: $KEY" \
            -d "{\"refreshToken\":\"different\"}")
        if [[ "$RSP3" == "422" ]]; then
            record PASS "Wire replay-mismatch returns 422"
        else
            record FAIL "Wire replay-mismatch status=$RSP3 (expected 422)"
        fi

        # IDEMPOTENCY_REPLAY audit row visible
        AUDIT_RSP=$(curl -s -X GET "$API_URL/api/v2/audit/log?action=IDEMPOTENCY_REPLAY&take=5" \
            -H "Authorization: Bearer $TOKEN")
        if echo "$AUDIT_RSP" | grep -q "IDEMPOTENCY_REPLAY"; then
            record PASS "Wire audit: IDEMPOTENCY_REPLAY row visible via /audit/log"
        else
            record FAIL "Wire audit: row not visible"
        fi

        # Too-long key
        LONG_KEY=$(printf '%.0sx' {1..65})
        RSP_LONG=$(curl -s -o /dev/null -w '%{http_code}' \
            -X POST "$API_URL/api/v2/auth/refresh" \
            -H "Content-Type: application/json" \
            -H "Idempotency-Key: $LONG_KEY" \
            -d "$BODY")
        if [[ "$RSP_LONG" == "400" ]]; then
            record PASS "Wire too-long key rejected with 400"
        else
            record FAIL "Wire too-long key status=$RSP_LONG (expected 400)"
        fi
    else
        record FAIL "Wire login failed (no token from /auth/login admin/admin)"
        echo "[debug] login response: $LOGIN_RSP" | head -3
    fi
fi

# Stop API
cleanup_api
API_PID=""

# ── Step 8: cleanup ───────────────────────────────────────────────
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
