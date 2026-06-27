#!/usr/bin/env bash
# P10.7e-2 — Catalyst checkpoint for the FQC + OQC + Q5 3-sig wire
# surface. Self-seeds 3 DISTINCT QC users (Inspector / Reviewer /
# Approver) via POST /api/v2/admin/users and cycles every Q5
# violation path so each WO_OQC_*_DENIED audit code surfaces, then
# completes the happy 3-distinct path so the WO advances to SHIPPED.
#
# ROLE POLICY (per Program.cs):
#   QcRead policy: Admin | Supervisor | QC  — GET view
#   QcEdit policy: Admin | QC | Supervisor  — every mutation
#
# SELF-SEED (mirrors checkpoint-7d-2.sh pattern + L10 drift guard):
#   Users seeded via POST /api/v2/admin/users (AccountControlController,
#   P10.6c). Idempotent: HTTP 422 + body.code=accounts.username_in_use
#   treated as success.
#
#   IPQC_USER     = oqc-test-inspector  (role QC; sig 1 — Inspector)
#   QA_USER       = oqc-test-reviewer   (role QC; sig 2 — Reviewer)
#   APPROVER_USER = oqc-test-approver   (role QC; sig 3 — Approver)
#
#   All 3 users carry usernames the purge-test-audit.sh script
#   recognises for cleanup (extend IPQC_QA_TEST_USERS in 7e-2b
#   if not already present).
#
# Q5 CRITICAL focus — 4 paths PROVEN end-to-end:
#   step 8: Reviewer = Inspector → 422 oqc.same_user_as_inspector +
#           WO_OQC_REVIEW_DENIED audit (NOT WO_OQC_REVIEW)
#   step 10: Approver = Reviewer → 422 oqc.same_user_as_reviewer +
#           WO_OQC_APPROVE_DENIED audit
#   step 11: Approver = Inspector → 422 oqc.same_user_as_inspector +
#           WO_OQC_APPROVE_DENIED audit
#   step 13: Happy 3-distinct → 200 + SHIPPED + WO_OQC_APPROVE +
#           WO_SHIPPED audits both stamped same SaveChanges
#
# L21 wire assertion (step 14): /work-orders/by-no/<WoNo>/summary
# returns MesPhase=SHIPPED — confirms the canonical phase the L21
# auto-route would dispatch ShippedSummaryDashboard against (when
# that lands in 7e-3).
#
# Operator runs ONE command + supplies ONLY the WO number:
#   bash CCL-MES-Hybrid/scripts/checkpoint-7e-2.sh <WoNo> [--keep-alive]
#
# WO precondition — MesPhase=OQC_PENDING + WoQcChecks row(s) exist.
# The reset-* helpers from 7c/7d don't cover OQC yet; for now the
# script shims the WO + check rows directly. A future 7e-2b can ship
# a real reset-oqc-for-wo.sh helper.

set -u
set +e

KEEP_ALIVE=0
WO_NO=""
for arg in "$@"; do
    case "$arg" in
        --keep-alive) KEEP_ALIVE=1 ;;
        --help|-h)
            echo "usage: bash scripts/checkpoint-7e-2.sh <WoNo> [--keep-alive]"
            echo ""
            echo "  Self-seeds 3 distinct QC users (Inspector/Reviewer/Approver)"
            echo "  via POST /api/v2/admin/users. Idempotent — re-running"
            echo "  reuses the existing users."
            exit 0
            ;;
        --*) echo "unknown flag: $arg"; exit 64 ;;
        *)
            if [[ -z "$WO_NO" ]]; then WO_NO="$arg"
            else echo "extra positional arg: $arg"; exit 64
            fi
            ;;
    esac
done

if [[ -z "$WO_NO" ]]; then
    echo "usage: bash scripts/checkpoint-7e-2.sh <WoNo> [--keep-alive]"
    exit 64
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HYBRID_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$HYBRID_ROOT/.." && pwd)"
DB_PATH="$REPO_ROOT/data/ccl_mes.db"
API_BASE="${API_BASE:-http://127.0.0.1:5100}"
AUTO_BOOT_PID=""

INSPECTOR_USER="oqc-test-inspector"
REVIEWER_USER="oqc-test-reviewer"
APPROVER_USER="oqc-test-approver"
TEST_PASSWORD="P@ss!Checkpoint1"
ACTOR_TAG="checkpoint-7e-2"

DB_SHA8="(missing)"
[[ -f "$DB_PATH" ]] && DB_SHA8="$(shasum -a 256 "$DB_PATH" 2>/dev/null | awk '{print substr($1,1,8)}')"

echo "===================================================================="
echo "checkpoint-7e-2 — FQC/OQC wire + Q5 3-sig (4 paths)"
echo "[ctx] DB              = $DB_PATH"
echo "[ctx] DB sha8         = $DB_SHA8"
echo "[ctx] API base        = $API_BASE"
echo "[ctx] HEAD            = $(cd "$REPO_ROOT" && git rev-parse --short HEAD 2>/dev/null || echo '?')"
echo "[ctx] WO              = $WO_NO"
echo "[ctx] INSPECTOR_USER  = $INSPECTOR_USER  (role QC; self-seeded)"
echo "[ctx] REVIEWER_USER   = $REVIEWER_USER   (role QC; self-seeded; MUST differ from INSPECTOR)"
echo "[ctx] APPROVER_USER   = $APPROVER_USER   (role QC; self-seeded; MUST differ from INSPECTOR + REVIEWER)"
echo "[ctx] role policy: QcRead=Admin|Supervisor|QC; QcEdit=Admin|QC|Supervisor"
echo "===================================================================="

PASS=0
FAIL=0
SUMMARY=()
# 14 numbered steps: boot · admin · seed-3-users · login-3-users ·
# scrap-source · reset-WO · inspect (sig 1) · Q5 path A (R=I) ·
# review (sig 2) · Q5 path B (A=R) · Q5 path C (A=I) ·
# approve happy · audit-wire-mirror · L21 wire.
TOTAL_STEPS=14
CURRENT_STEP=0

record() {
    CURRENT_STEP=$((CURRENT_STEP + 1))
    if [[ "$1" == "PASS" ]]; then
        PASS=$((PASS + 1))
        echo "[$CURRENT_STEP/$TOTAL_STEPS] ✓ $2"
        SUMMARY+=("  PASS  $2")
    else
        FAIL=$((FAIL + 1))
        echo "[$CURRENT_STEP/$TOTAL_STEPS] ✗ $2"
        SUMMARY+=("  FAIL  $2")
    fi
}

final_summary() {
    echo ""
    echo "============================  SUMMARY  ============================"
    if [[ ${#SUMMARY[@]} -eq 0 ]]; then
        echo "  (no steps recorded — early abort before first probe)"
    else
        printf '%s\n' "${SUMMARY[@]}"
    fi
    echo ""
    echo "  TOTAL: pass=$PASS fail=$FAIL"
    echo ""
    if [[ $FAIL -gt 0 ]]; then
        echo "  ✗ CHECKPOINT FAILED — wire path NOT proven. See log + audit."
    else
        echo "  ✓ CHECKPOINT PASSED — FQC/OQC wire + Q5 3-sig 4 paths proven."
        echo "    WO $WO_NO advanced to SHIPPED via Q5 happy 3-distinct."
        echo "    Audit log carries WO_OQC_REVIEW_DENIED + WO_OQC_APPROVE_DENIED ×2"
        echo "    + WO_OQC_INSPECT + WO_OQC_REVIEW + WO_OQC_APPROVE + WO_SHIPPED."
        echo ""
        echo "  Catalyst hand-verify (after 7e-3 ships dashboards):"
        echo "    bash scripts/checkpoint-7e-2.sh <WoNo> --keep-alive"
    fi
}

cleanup() {
    final_summary
    if [[ -n "$AUTO_BOOT_PID" ]]; then
        if [[ $KEEP_ALIVE -eq 1 ]]; then
            echo "[keep-alive] API left running on $API_BASE (pid=$AUTO_BOOT_PID)"
            echo "[keep-alive] log    : /tmp/checkpoint-7e-2-api.log"
            echo "[keep-alive] kill   : kill $AUTO_BOOT_PID"
        else
            kill -9 "$AUTO_BOOT_PID" 2>/dev/null
        fi
    fi
}
trap cleanup EXIT INT TERM

# Helpers ───────────────────────────────────────────────────────────
etag_of() {
    sqlite3 "$DB_PATH" "SELECT hex(RowVersion) FROM WorkOrders WHERE Id=$1;" \
        | python3 -c "import sys,base64; h=sys.stdin.read().strip(); print(base64.b64encode(bytes.fromhex(h)).decode())"
}
json_field() {
    python3 -c "import sys,json; print(json.load(sys.stdin).get('$1',''))" 2>/dev/null
}
login_user() {
    local user="$1"
    local rsp
    rsp=$(curl -s -X POST "$API_BASE/api/v2/auth/login" \
        -H "Content-Type: application/json" \
        -d "{\"username\":\"$user\",\"password\":\"$TEST_PASSWORD\",\"deviceId\":\"checkpoint-7e-2\"}")
    echo "$rsp" | json_field "accessToken"
}

# L10 — drift guard.
LAST_HTTP=""
LAST_BODY=""
api_assert_routed() {
    case "$LAST_HTTP" in
        404|405)
            echo ""
            echo "  [L10 drift] $1 → HTTP $LAST_HTTP — wrong path. Verify"
            echo "  controller route + method, then re-run."
            echo "  body: $(echo "$LAST_BODY" | head -c 200)"
            return 1
            ;;
    esac
    return 0
}
api_post_admin() {
    local path="$1" body="$2" rsp
    rsp=$(curl -s -w "\nHTTP:%{http_code}" -X POST "$API_BASE$path" \
        -H "$ADMIN_AUTH" -H "Content-Type: application/json" -d "$body")
    LAST_HTTP=$(echo "$rsp" | grep -oE 'HTTP:[0-9]+$' | cut -d: -f2)
    LAST_BODY=$(echo "$rsp" | sed '$d')
    api_assert_routed "POST $path"
}

# Henry RCA on PR #122 step 7 (2026-06-07): the earlier qc_post inline-
# curl pattern swallowed HTTP status on empty-body responses, so a
# server-side 404 (route missing on a stale build) reported as just
# "POST /qc/oqc/inspect failed: " with no diagnostic. This wrapper
# ALWAYS captures + exposes status + body via the LAST_HTTP / LAST_BODY
# globals + routes through api_assert_routed for the L10 drift guard
# class. Caller checks LAST_HTTP for fine-grained branching (200 vs 422
# vs 404 etc.) and on FAIL the script PRINTS status+body so RCA is
# 1-look.
qc_post() {
    # qc_post <auth_header_var_name> <path> <body_json>
    local auth_var="$1" path="$2" body="$3"
    local etag idem rsp
    etag=$(etag_of "$WO_ID")
    idem=$(uuidgen)
    rsp=$(curl -s -w "\nHTTP:%{http_code}" -X POST "$API_BASE$path" \
        -H "${!auth_var}" -H "Content-Type: application/json" \
        -H "If-Match: \"$etag\"" -H "Idempotency-Key: $idem" \
        -d "$body")
    LAST_HTTP=$(echo "$rsp" | grep -oE 'HTTP:[0-9]+$' | cut -d: -f2)
    LAST_BODY=$(echo "$rsp" | sed '$d')
    # L10 drift guard fires for 404/405; other status codes flow to the
    # caller's branching logic.
    api_assert_routed "POST $path"
}

# Sanity-fail helper — when LAST_HTTP is unexpected, print the FULL
# status + body so Henry's RCA is one paste away. Caller passes the
# expected status code(s) to narrow the "what should have happened"
# part of the diagnostic.
qc_diag() {
    # qc_diag <label> <expected_http>
    echo "  [diag] $1 — HTTP $LAST_HTTP (expected $2)"
    echo "  [diag] body: $(echo "$LAST_BODY" | head -c 400)"
}

# ── 1. Boot API ───────────────────────────────────────────────────
TARGET_PORT="${API_BASE##*:}"
TARGET_PORT="${TARGET_PORT%%/*}"

if curl -s -m 3 -o /dev/null -w "%{http_code}" "$API_BASE/health" 2>/dev/null | grep -qE "^(200|401|503)$"; then
    record PASS "API already up on $API_BASE"
else
    STALE_PIDS=$(lsof -nP -iTCP:${TARGET_PORT} -sTCP:LISTEN -t 2>/dev/null)
    if [[ -n "$STALE_PIDS" ]]; then
        echo "[boot] killing stale listeners on $TARGET_PORT: $STALE_PIDS"
        echo "$STALE_PIDS" | xargs -r kill -9 2>/dev/null
        sleep 1
    fi
    (cd "$HYBRID_ROOT/src/CCL.MES.Api" && \
        ConnectionStrings__Default="Data Source=$DB_PATH" \
        ASPNETCORE_ENVIRONMENT="Development" \
        dotnet run --no-build --no-launch-profile --urls "$API_BASE" > /tmp/checkpoint-7e-2-api.log 2>&1) &
    AUTO_BOOT_PID=$!
    BOOT_OK=0
    for i in $(seq 1 120); do
        code=$(curl -s -m 2 -o /dev/null -w "%{http_code}" "$API_BASE/health" 2>/dev/null)
        if [[ "$code" =~ ^(200|401|503)$ ]]; then
            BOOT_OK=1
            break
        fi
        sleep 1
    done
    if [[ $BOOT_OK -eq 1 ]]; then
        record PASS "API booted on $API_BASE (pid=$AUTO_BOOT_PID)"
    else
        record FAIL "API never reached /health"
        exit 1
    fi
fi

# All 3 OQC flags must default ON.
if grep -q "OPS_OQC_REQUIRE_DISTINCT_REVIEWER=off\|OPS_OQC_REQUIRE_DISTINCT_APPROVER=off\|OPS_OQC_REQUIRE_APPROVER_DISTINCT_FROM_INSPECTOR=off" /tmp/checkpoint-7e-2-api.log 2>/dev/null; then
    echo "[abort] One or more 3-sig flags OFF — Q5 violations cannot be proven."
    echo "        All 3 default ON per §3.4 + Lesson L20. Fix .env + re-run."
    record FAIL "3-sig flags partially OFF"
    exit 1
fi

# ── 2. Login admin ───────────────────────────────────────────────
ADMIN_RSP=$(curl -s -X POST "$API_BASE/api/v2/auth/login" \
    -H "Content-Type: application/json" \
    -d '{"username":"admin","password":"admin","deviceId":"checkpoint-7e-2"}')
ADMIN_TOKEN=$(echo "$ADMIN_RSP" | json_field "accessToken")
if [[ -z "$ADMIN_TOKEN" ]]; then
    record FAIL "login admin failed: $ADMIN_RSP"
    exit 1
fi
record PASS "login admin"
ADMIN_AUTH="Authorization: Bearer $ADMIN_TOKEN"

# Build-sanity probe (Henry RCA on PR #122 step 7, 2026-06-07):
# the previous run's API was a keep-alive from verify-p10.7e.sh booted
# on commit f08b79d (7e-1, BEFORE WoQcReviewController landed in 7e-2).
# Step 7's POST /qc/oqc/inspect 404'd because the route didn't exist
# on that stale binary. Prevention: GET the new route shape against
# any WO id (returns 200 + body, 401 if auth bad, or 404 if WO missing
# but the route IS registered). A genuine route-missing returns 404
# WITH the kestrel "Endpoint not found" pattern in the body — distinct
# from the route-handler 404 which carries an ApiError JSON body.
echo "[build-sanity] probing GET /api/v2/work-orders/0/qc/fqc — confirms 7e-2 controller is on the running binary"
SANITY_RSP=$(curl -s -w "\nHTTP:%{http_code}" -X GET "$API_BASE/api/v2/work-orders/0/qc/fqc" \
    -H "$ADMIN_AUTH")
SANITY_HTTP=$(echo "$SANITY_RSP" | grep -oE 'HTTP:[0-9]+$' | cut -d: -f2)
SANITY_BODY=$(echo "$SANITY_RSP" | sed '$d')
# 401 means route IS registered + auth challenge (we sent a token, but
# anything quirky in token cache => still proves the route exists).
# 404 with ApiError JSON body { code: "wo.not_found" } proves route is
# wired (it handled the request, looked up WO id 0, found nothing).
# A genuine missing route returns 404 with EMPTY body — that's the
# stale-build case Henry hit.
if [[ "$SANITY_HTTP" == "404" && -z "$SANITY_BODY" ]]; then
    echo "[build-sanity] ✗ stale binary detected — running API does NOT have WoQcReviewController."
    echo "[build-sanity] body: '<empty>' (real 7e-2 route would return ApiError JSON or 401)"
    echo "[build-sanity] Kill the keep-alive API + re-run this script. Refusing to continue."
    echo "[build-sanity]   STALE_PID=\$(lsof -nP -iTCP:5100 -sTCP:LISTEN -t)"
    echo "[build-sanity]   [[ -n \"\$STALE_PID\" ]] && kill -9 \$STALE_PID"
    record FAIL "build-sanity: API on $API_BASE lacks WoQcReviewController (stale binary)"
    exit 1
fi
echo "[build-sanity] ✓ HTTP=$SANITY_HTTP (route wired)"

# ── 3. Self-seed 3 users ─────────────────────────────────────────
seed_user() {
    local user="$1" role="$2"
    if ! api_post_admin "/api/v2/admin/users" \
        "{\"username\":\"$user\",\"displayName\":\"P10.7e-2 checkpoint test user\",\"role\":\"$role\",\"department\":\"QC\",\"password\":\"$TEST_PASSWORD\"}"; then
        return 1
    fi
    case "$LAST_HTTP" in
        201|200) return 0 ;;
        422)
            if echo "$LAST_BODY" | grep -q "accounts.username_in_use"; then return 0; fi
            echo "  [seed] user=$user 422 not username_in_use: $(echo "$LAST_BODY" | head -c 200)"
            return 1
            ;;
        *)
            echo "  [seed] user=$user http=$LAST_HTTP"
            return 1
            ;;
    esac
}

SEED_OK=1
seed_user "$INSPECTOR_USER" "QC" || SEED_OK=0
seed_user "$REVIEWER_USER"  "QC" || SEED_OK=0
seed_user "$APPROVER_USER"  "QC" || SEED_OK=0
if [[ $SEED_OK -eq 1 ]]; then
    record PASS "self-seed 3 users (Inspector + Reviewer + Approver, idempotent)"
else
    record FAIL "self-seed users failed"
    exit 1
fi

# ── 4. Login all 3 users ─────────────────────────────────────────
INSPECTOR_TOKEN=$(login_user "$INSPECTOR_USER")
REVIEWER_TOKEN=$(login_user "$REVIEWER_USER")
APPROVER_TOKEN=$(login_user "$APPROVER_USER")
if [[ -n "$INSPECTOR_TOKEN" && -n "$REVIEWER_TOKEN" && -n "$APPROVER_TOKEN" ]]; then
    record PASS "login Inspector + Reviewer + Approver"
else
    record FAIL "login failed: I='${INSPECTOR_TOKEN:+(set)}' R='${REVIEWER_TOKEN:+(set)}' A='${APPROVER_TOKEN:+(set)}'"
    exit 1
fi
INSPECTOR_AUTH="Authorization: Bearer $INSPECTOR_TOKEN"
REVIEWER_AUTH="Authorization: Bearer $REVIEWER_TOKEN"
APPROVER_AUTH="Authorization: Bearer $APPROVER_TOKEN"

# ── 5. Scrap picker source probe (L17) ───────────────────────────
SCRAP_CNT=$(curl -s -H "$INSPECTOR_AUTH" "$API_BASE/api/v2/reason-codes?kind=Scrap" \
    | python3 -c "import sys,json; print(len(json.load(sys.stdin)))" 2>/dev/null)
if [[ "${SCRAP_CNT:-0}" -ge 8 ]]; then
    record PASS "Scrap picker source (≥8 codes; got $SCRAP_CNT)"
else
    record FAIL "Scrap picker thin: $SCRAP_CNT"
fi

# ── Resolve WO + shim to OQC_PENDING ──────────────────────────────
WO_ID=$(sqlite3 "$DB_PATH" "SELECT Id FROM WorkOrders WHERE WoNo='$WO_NO' LIMIT 1;" 2>/dev/null)
if [[ -z "$WO_ID" ]]; then
    record FAIL "WO $WO_NO not found"
    exit 1
fi
echo "[ctx] WO Id      = $WO_ID"

# P10.7e-3 FIX (Henry RCA on PR #123) — L23:
# Phase shim stays SQL (no API for backward phase transitions yet) but
# the QC check materialisation NOW goes through the REAL API path
# operators use. Pre-fix: direct INSERT of a stub check with empty
# profile + single "appearance" item masked the operator-visible 0/0
# gap. The real-path probe below will FAIL if QcProfileSeed regresses
# (silently shrinks to 0 items, etc.) the way the production UI would.
sqlite3 "$DB_PATH" "UPDATE WorkOrders SET MesPhase='OQC_PENDING', CurrentStep='Oqc', UpdatedAt=datetime('now'), UpdatedBy='$ACTOR_TAG' WHERE Id=$WO_ID;"
# Wipe prior check + items so the GET below truly lazy-materialises
# from QcProfileSeed via the controller path (not from a stale stub).
sqlite3 "$DB_PATH" "DELETE FROM WoQcCheckItems WHERE WoQcCheckId IN (SELECT Id FROM WoQcChecks WHERE WorkOrderId=$WO_ID AND QcKind='OQC');"
sqlite3 "$DB_PATH" "DELETE FROM WoQcChecks WHERE WorkOrderId=$WO_ID AND QcKind='OQC';"
NEW_PHASE=$(sqlite3 "$DB_PATH" "SELECT MesPhase FROM WorkOrders WHERE Id=$WO_ID;")
if [[ "$NEW_PHASE" == "OQC_PENDING" ]]; then
    record PASS "reset WO to OQC_PENDING (cleared stub check; profile will materialise via API)"
else
    record FAIL "reset failed — phase is $NEW_PHASE"
fi

# ── 6.1 Real materialise: GET /qc/oqc → profile seeded from
#       QcProfileSeed (28-item canonical) + items synthesised as Pending.
#       This is the exact path the operator's OqcDashboard hits.
GET_RSP=$(curl -s -w "\nHTTP:%{http_code}" -X GET "$API_BASE/api/v2/work-orders/$WO_ID/qc/oqc" \
    -H "$INSPECTOR_AUTH")
LAST_HTTP=$(echo "$GET_RSP" | grep -oE 'HTTP:[0-9]+$' | cut -d: -f2)
LAST_BODY=$(echo "$GET_RSP" | sed '$d')
api_assert_routed "GET /qc/oqc"
if [[ "$LAST_HTTP" != "200" ]]; then
    qc_diag "GET /qc/oqc materialisation failed" "200"
    record FAIL "GET /qc/oqc returned $LAST_HTTP — profile materialisation broken"
    exit 1
fi
ITEM_COUNT=$(echo "$LAST_BODY" | python3 -c "import sys,json; v=json.load(sys.stdin); print(len(v.get('items',[])))")
if [[ "$ITEM_COUNT" -ge 28 ]]; then
    record PASS "GET /qc/oqc materialised $ITEM_COUNT items via QcProfileSeed (≥28 required)"
else
    qc_diag "Profile materialisation thin" "≥28 items (canonical OQC profile)"
    record FAIL "L23 — GET /qc/oqc returned only $ITEM_COUNT items; QcProfileSeed broken or shrunk"
    exit 1
fi

# Extract item keys from the response so we can PUT each via the real
# wire path — this is what the OqcDashboard would do as the operator
# taps OK on every row.
ITEM_KEYS=$(echo "$LAST_BODY" | python3 -c "import sys,json; v=json.load(sys.stdin); print(' '.join(i['itemKey'] for i in v.get('items',[])))")

# ── 6.2 Mark every item Ok via PUT — REAL endpoint per operator flow.
#       Each PUT bumps RowVersion via the trigger; capture the latest
#       ETag from the in-band response for the next PUT.
ETAG=$(etag_of "$WO_ID")
PUT_FAILED=0
PUT_COUNT=0
for KEY in $ITEM_KEYS; do
    IDEM=$(uuidgen)
    PUT_RSP=$(curl -s -w "\nHTTP:%{http_code}" -X PUT \
        "$API_BASE/api/v2/work-orders/$WO_ID/qc/oqc/items/$KEY" \
        -H "$INSPECTOR_AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $IDEM" \
        -d '{"status":"Ok"}')
    PUT_HTTP=$(echo "$PUT_RSP" | grep -oE 'HTTP:[0-9]+$' | cut -d: -f2)
    PUT_BODY=$(echo "$PUT_RSP" | sed '$d')
    if [[ "$PUT_HTTP" != "200" ]]; then
        echo "  [diag] PUT items/$KEY → HTTP $PUT_HTTP"
        echo "  [diag] body: $(echo "$PUT_BODY" | head -c 200)"
        PUT_FAILED=$((PUT_FAILED + 1))
        break
    fi
    ETAG=$(echo "$PUT_BODY" | json_field "eTag")
    PUT_COUNT=$((PUT_COUNT + 1))
done
if [[ "$PUT_FAILED" -eq 0 && "$PUT_COUNT" -eq "$ITEM_COUNT" ]]; then
    record PASS "PUT /qc/oqc/items × $PUT_COUNT — every profile item marked Ok via real wire"
else
    record FAIL "PUT /qc/oqc/items — $PUT_COUNT/$ITEM_COUNT succeeded ($PUT_FAILED failed)"
    exit 1
fi

# ── 7. Inspector signs (sig 1) ───────────────────────────────────
qc_post INSPECTOR_AUTH "/api/v2/work-orders/$WO_ID/qc/oqc/inspect" '{"note":"Inspector signed"}'
OK=$(echo "$LAST_BODY" | json_field "ok")
NEW=$(echo "$LAST_BODY" | json_field "eTag")
if [[ "$LAST_HTTP" == "200" && "$OK" == "True" ]]; then
    record PASS "POST /qc/oqc/inspect by $INSPECTOR_USER (sig 1) HTTP=$LAST_HTTP"
    # Refresh ETag from the just-bumped server value via SQL — the
    # in-band body ETag is also fine but SQL bypasses any client
    # parse gotcha.
    SLEEP_HACK=  # placeholder — qc_post does NOT auto-update ETag
else
    qc_diag "Inspector sig 1 unexpected" "200 with ok=True"
    record FAIL "POST /qc/oqc/inspect failed"
    exit 1
fi

# ── 8. Q5 path ❶ — Reviewer = Inspector → 422 ─────────────────────
qc_post INSPECTOR_AUTH "/api/v2/work-orders/$WO_ID/qc/oqc/review" '{}'
ERR_CODE=$(echo "$LAST_BODY" | json_field "errorCode")
if [[ "$LAST_HTTP" == "422" && "$ERR_CODE" == "oqc.same_user_as_inspector" ]]; then
    record PASS "Q5 ❶: Reviewer = Inspector → 422 oqc.same_user_as_inspector (DENIED)"
else
    qc_diag "Q5 ❶ unexpected" "422 + oqc.same_user_as_inspector"
    record FAIL "Q5 ❶ failed"
fi

# ── 9. Reviewer (distinct) signs (sig 2) ─────────────────────────
qc_post REVIEWER_AUTH "/api/v2/work-orders/$WO_ID/qc/oqc/review" '{"note":"Reviewer signed"}'
OK=$(echo "$LAST_BODY" | json_field "ok")
if [[ "$LAST_HTTP" == "200" && "$OK" == "True" ]]; then
    record PASS "POST /qc/oqc/review by $REVIEWER_USER (sig 2) HTTP=$LAST_HTTP"
else
    qc_diag "Reviewer sig 2 unexpected" "200 with ok=True"
    record FAIL "POST /qc/oqc/review failed"
    exit 1
fi

# ── 10. Q5 path ❷ — Approver = Reviewer → 422 ────────────────────
qc_post REVIEWER_AUTH "/api/v2/work-orders/$WO_ID/qc/oqc/approve" '{"outcome":"Approve"}'
ERR_CODE=$(echo "$LAST_BODY" | json_field "errorCode")
if [[ "$LAST_HTTP" == "422" && "$ERR_CODE" == "oqc.same_user_as_reviewer" ]]; then
    record PASS "Q5 ❷: Approver = Reviewer → 422 oqc.same_user_as_reviewer (DENIED)"
else
    qc_diag "Q5 ❷ unexpected" "422 + oqc.same_user_as_reviewer"
    record FAIL "Q5 ❷ failed"
fi

# ── 11. Q5 path ❸ — Approver = Inspector → 422 ───────────────────
qc_post INSPECTOR_AUTH "/api/v2/work-orders/$WO_ID/qc/oqc/approve" '{"outcome":"Approve"}'
ERR_CODE=$(echo "$LAST_BODY" | json_field "errorCode")
if [[ "$LAST_HTTP" == "422" && "$ERR_CODE" == "oqc.same_user_as_inspector" ]]; then
    record PASS "Q5 ❸: Approver = Inspector → 422 oqc.same_user_as_inspector (DENIED)"
else
    qc_diag "Q5 ❸ unexpected" "422 + oqc.same_user_as_inspector"
    record FAIL "Q5 ❸ failed"
fi

# ── 12. Approver (distinct) signs (sig 3) — happy → SHIPPED ──────
qc_post APPROVER_AUTH "/api/v2/work-orders/$WO_ID/qc/oqc/approve" '{"outcome":"Approve"}'
OK=$(echo "$LAST_BODY" | json_field "ok")
PHASE=$(echo "$LAST_BODY" | json_field "mesPhase")
if [[ "$LAST_HTTP" == "200" && "$OK" == "True" && "$PHASE" == "SHIPPED" ]]; then
    record PASS "Q5 ❹: Approver (distinct) → SHIPPED"
else
    qc_diag "Q5 ❹ happy unexpected" "200 + ok=True + phase=SHIPPED"
    record FAIL "Q5 ❹ happy failed: ok=$OK phase=$PHASE"
fi

# ── 13. Audit wire-mirror (R7.3) ─────────────────────────────────
AUDIT_MISS=()
for ACTION in WO_OQC_INSPECT WO_OQC_REVIEW WO_OQC_REVIEW_DENIED WO_OQC_APPROVE WO_OQC_APPROVE_DENIED WO_SHIPPED; do
    AUDIT=$(curl -s -H "$ADMIN_AUTH" "$API_BASE/api/v2/audit/log?action=$ACTION&page=1&pageSize=50")
    if ! echo "$AUDIT" | grep -q "\"targetId\":\"$WO_ID\""; then
        AUDIT_MISS+=("$ACTION")
    fi
done
if [[ ${#AUDIT_MISS[@]} -eq 0 ]]; then
    record PASS "audit wire-mirror (6/6): INSPECT + REVIEW + REVIEW_DENIED + APPROVE + APPROVE_DENIED + SHIPPED"
else
    record FAIL "audit wire missing: ${AUDIT_MISS[*]}"
fi

# ── 14. L21 wire assertion (every WO DTO MesPhase per L19) ───────
FINAL_PHASE=$(curl -s -H "$APPROVER_AUTH" "$API_BASE/api/v2/work-orders/by-no/$WO_NO/summary" | json_field "mesPhase")
if [[ "$FINAL_PHASE" == "SHIPPED" ]]; then
    record PASS "L21 wire: /by-no/$WO_NO/summary returns SHIPPED — UI auto-route would mount ShippedSummaryDashboard (in 7e-3)"
else
    record FAIL "L21 wire: by-no/summary returned MesPhase='$FINAL_PHASE' (expected SHIPPED)"
fi

# ── Keep-alive forensic dump ─────────────────────────────────────
if [[ $KEEP_ALIVE -eq 1 ]]; then
    echo ""
    echo "── KEEP-ALIVE: forensic state ──────────────────────────────────"
    sqlite3 "$DB_PATH" -header -column "
        SELECT WoNo, MesPhase, CurrentStep, UpdatedBy FROM WorkOrders WHERE Id=$WO_ID;
    "
    echo ""
    sqlite3 "$DB_PATH" -header -column "
        SELECT QcKind, Judgment, InspectedBy, ReviewedBy, ApprovedBy
        FROM WoQcChecks WHERE WorkOrderId=$WO_ID;
    "
    echo ""
    sqlite3 "$DB_PATH" -header -column "
        SELECT Action, COUNT(*) AS rows
        FROM AuditLogs
        WHERE TargetId='$WO_ID' AND TargetType='WorkOrder'
          AND Action IN ('WO_OQC_INSPECT','WO_OQC_REVIEW','WO_OQC_REVIEW_DENIED',
                         'WO_OQC_APPROVE','WO_OQC_APPROVE_DENIED','WO_SHIPPED')
        GROUP BY Action ORDER BY Action;
    "
fi

if [[ $FAIL -gt 0 ]]; then exit 1; fi
exit 0
