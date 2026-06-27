#!/usr/bin/env bash
# P10.7e-4 — final checkpoint for the FQC + OQC + Reports stack.
# Closes the 7e-* series with a forensic walk that proves EVERY
# outgoing-quality transition on a SINGLE script invocation + a
# SINGLE WO. Operator runs ONE command + supplies ONLY the WO number;
# the script self-seeds the 3 QC users + shims the WO between cycles.
#
# Wire contract under test (locked by WoQcReviewController + the
# bUnit suites — this script asserts the running binary matches what
# the operator's FqcDashboard / OqcDashboard / ShippedSummaryDashboard
# would see; the L21 auto-route is locked by WorkOrdersPageTests):
#
#   Cycle 1 — FQC Reject → PREPRESS (rework loop)
#     • shim WO → FQC_PENDING (clears prior FQC check)
#     • GET /qc/fqc lazy-materialises ≥12 items from QcProfileSeed (L23)
#     • PUT every item Ok via the real wire (operator taps each row)
#     • POST /qc/fqc/judgment { Judgment: "Reject", JudgmentReason }
#     • expect 200 + MesPhase=PREPRESS + WO_FQC_REJECT_TO_PREPRESS audit
#
#   Cycle 2 — FQC Pass → OQC_PENDING (forward gate)
#     • shim WO → FQC_PENDING + re-materialise + PUT every item Ok
#     • POST /qc/fqc/judgment { Judgment: "Pass" }
#     • expect 200 + MesPhase=OQC_PENDING + WO_FQC_JUDGMENT audit
#
#   Cycle 3 — OQC 3-sig + Q5 4-path + Reject re-loop
#     • GET /qc/oqc materialises ≥28 items (L23) + PUT every item Ok
#     • POST /qc/oqc/inspect            sig 1 (Inspector)  → WO_OQC_INSPECT
#     • Q5 ❶ review by Inspector        → 422 oqc.same_user_as_inspector
#                                          + WO_OQC_REVIEW_DENIED
#     • POST /qc/oqc/review             sig 2 (Reviewer ≠ Inspector)
#                                          → WO_OQC_REVIEW
#     • Q5 ❷ approve by Reviewer        → 422 oqc.same_user_as_reviewer
#                                          + WO_OQC_APPROVE_DENIED
#     • Q5 ❸ approve by Inspector       → 422 oqc.same_user_as_inspector
#                                          + WO_OQC_APPROVE_DENIED
#     • POST /qc/oqc/approve { Outcome: "Reject", JudgmentReason }
#                                          → 200 + MesPhase=FQC_PENDING
#                                          + WO_OQC_REJECT_TO_FQC_PENDING
#
#   Cycle 4 — re-pass the loop all the way to SHIPPED
#     • FQC Pass → OQC_PENDING (fresh check) → 3 DISTINCT sigs
#     • POST /qc/oqc/approve { Outcome: "Approve" }
#                                          → 200 + MesPhase=SHIPPED
#                                          + WO_OQC_APPROVE + WO_SHIPPED
#                                            (both stamped same SaveChanges)
#
#   Cycle 5 — Q8 summary report (powers ShippedSummaryDashboard)
#     • GET /qc-summary report endpoint → 200 + woNo + MesPhase=SHIPPED
#       + totals + qc_summary legs present (live-recomputed)
#
#   Audit wire-mirror (R7.3): all 9 outgoing-quality AuditAction codes
#   surface against this WO. L21 wire: /by-no/<WoNo>/summary returns
#   SHIPPED so the auto-route would mount ShippedSummaryDashboard.
#
# ROLE POLICY (per Program.cs):
#   QcRead policy : Admin | Supervisor | QC  — GET view + summary
#   QcEdit policy : Admin | QC | Supervisor  — every mutation
#
# SELF-SEED — same convention as checkpoint-7e-2.sh + L10 drift guard:
#   Users seeded via POST /api/v2/admin/users (AccountControlController,
#   P10.6c). Idempotent on HTTP 422 + body.code=accounts.username_in_use.
#
#   INSPECTOR_USER = oqc-test-inspector  (role QC; sig 1 — Inspector)
#   REVIEWER_USER  = oqc-test-reviewer   (role QC; sig 2 — Reviewer)
#   APPROVER_USER  = oqc-test-approver   (role QC; sig 3 — Approver)
#
#   The 3 users + the 'checkpoint-7e-final' actor tag are recognised by
#   purge-test-audit.sh so the operator --commit cleanup catches every
#   WO_FQC_* / WO_OQC_* / WO_SHIPPED row + the WoQcChecks shim rows.
#
# Usage:
#   bash CCL-MES-Hybrid/scripts/checkpoint-7e-final.sh <WoNo> [--keep-alive]
#
# After:
#   bash scripts/purge-test-audit.sh                # preview cleanup
#   bash scripts/purge-test-audit.sh --commit       # operator --commit
#
# R7.1 — [ctx] DB= + DB sha8 + WO + 3 seeded users printed at startup.
# R7.2 — self-managed API + --keep-alive for follow-on Catalyst verify.
# R7.3 — every wire probe (audit GET) has a TestServer mirror in
#        WoQcReviewControllerTests.
# S12  — per-step [N/total] labels + final_summary always prints in EXIT
#        trap regardless of early bail; non-zero exit on any FAIL.
# L10  — api_post_admin / api_assert_routed drift guards inline so a
#        wrong endpoint path triggers a single [L10 drift] banner
#        instead of cascading false-negative FAILs.
# L22  — kill stale :5100 listeners + build-sanity probe BEFORE any
#        route exercise so a stale keep-alive binary (missing the 7e
#        controller) is caught at step 0 not mid-cycle.
# L23  — every cycle drives the REAL materialisation path (GET /qc +
#        PUT items) — never a shortcut INSERT — so a seed-trống /
#        profile-trống data-bed gap FAILs here the way it would on the
#        operator's dashboard.

set -u
set +e

KEEP_ALIVE=0
WO_NO=""
for arg in "$@"; do
    case "$arg" in
        --keep-alive) KEEP_ALIVE=1 ;;
        --help|-h)
            echo "usage: bash scripts/checkpoint-7e-final.sh <WoNo> [--keep-alive]"
            echo ""
            echo "  Walks ONE WO through ALL 7e transitions on a single"
            echo "  invocation: FQC Reject→PREPRESS, FQC Pass→OQC, OQC 3-sig"
            echo "  + Q5 4-path + OQC Reject→FQC re-loop, then re-passes the"
            echo "  loop all the way to SHIPPED + reads the Q8 summary report."
            echo "  Self-seeds 3 distinct QC users via POST /api/v2/admin/users."
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
    echo "usage: bash scripts/checkpoint-7e-final.sh <WoNo> [--keep-alive]"
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
ACTOR_TAG="checkpoint-7e-final"

DB_SHA8="(missing)"
[[ -f "$DB_PATH" ]] && DB_SHA8="$(shasum -a 256 "$DB_PATH" 2>/dev/null | awk '{print substr($1,1,8)}')"

echo "===================================================================="
echo "checkpoint-7e-final — full FQC/OQC/Reports walk on ONE WO"
echo "[ctx] DB              = $DB_PATH"
echo "[ctx] DB sha8         = $DB_SHA8"
echo "[ctx] API base        = $API_BASE"
echo "[ctx] HEAD            = $(cd "$REPO_ROOT" && git rev-parse --short HEAD 2>/dev/null || echo '?')"
echo "[ctx] WO              = $WO_NO"
echo "[ctx] INSPECTOR_USER  = $INSPECTOR_USER  (role QC; self-seeded; sig 1)"
echo "[ctx] REVIEWER_USER   = $REVIEWER_USER   (role QC; self-seeded; sig 2; ≠ INSPECTOR)"
echo "[ctx] APPROVER_USER   = $APPROVER_USER   (role QC; self-seeded; sig 3; ≠ INSPECTOR + REVIEWER)"
echo "[ctx] role policy: QcRead=Admin|Supervisor|QC; QcEdit=Admin|QC|Supervisor"
echo "===================================================================="

PASS=0
FAIL=0
SUMMARY=()
TOTAL_STEPS=25
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
        echo "  ✗ CHECKPOINT FAILED — 7e walk NOT proven end-to-end. See log + audit."
    else
        echo "  ✓ CHECKPOINT PASSED — every 7e transition proven on WO $WO_NO:"
        echo "    FQC Reject→PREPRESS · FQC Pass→OQC · OQC 3-sig + Q5 4-path"
        echo "    · OQC Reject→FQC re-loop · re-pass → SHIPPED · Q8 summary."
        echo "    Audit log carries all 9 outgoing-quality action codes."
        echo ""
        echo "  Catalyst hand-verify (dashboards live):"
        echo "    bash scripts/checkpoint-7e-final.sh $WO_NO --keep-alive"
        echo "  Then cleanup the test audit + shim rows:"
        echo "    bash scripts/purge-test-audit.sh --commit"
    fi
}

cleanup() {
    final_summary
    if [[ -n "$AUTO_BOOT_PID" ]]; then
        if [[ $KEEP_ALIVE -eq 1 ]]; then
            echo "[keep-alive] API left running on $API_BASE (pid=$AUTO_BOOT_PID)"
            echo "[keep-alive] log    : /tmp/checkpoint-7e-final-api.log"
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
    local user="$1" rsp
    rsp=$(curl -s -X POST "$API_BASE/api/v2/auth/login" \
        -H "Content-Type: application/json" \
        -d "{\"username\":\"$user\",\"password\":\"$TEST_PASSWORD\",\"deviceId\":\"checkpoint-7e-final\"}")
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

# POST a QC mutation. Always captures status + body via LAST_HTTP /
# LAST_BODY (Henry RCA on PR #122 — empty-body 404 swallowed status).
# ETag is read fresh from the DB RowVersion each call so the helper is
# self-correcting after every prior mutation.
qc_post() {
    # qc_post <auth_header_var_name> <path> <body_json>
    local auth_var="$1" path="$2" body="$3" etag idem rsp
    etag=$(etag_of "$WO_ID")
    idem=$(uuidgen)
    rsp=$(curl -s -w "\nHTTP:%{http_code}" -X POST "$API_BASE$path" \
        -H "${!auth_var}" -H "Content-Type: application/json" \
        -H "If-Match: \"$etag\"" -H "Idempotency-Key: $idem" \
        -d "$body")
    LAST_HTTP=$(echo "$rsp" | grep -oE 'HTTP:[0-9]+$' | cut -d: -f2)
    LAST_BODY=$(echo "$rsp" | sed '$d')
    api_assert_routed "POST $path"
}

qc_diag() {
    # qc_diag <label> <expected_http>
    echo "  [diag] $1 — HTTP $LAST_HTTP (expected $2)"
    echo "  [diag] body: $(echo "$LAST_BODY" | head -c 400)"
}

# Shim the WO to a phase via SQL (no API exists for backward phase
# moves) + wipe the named kind's check rows so the next GET truly
# lazy-materialises from QcProfileSeed (mirrors checkpoint-7e-2.sh).
# Direct SQL is ONLY the phase + check-clear plumbing; every state the
# operator actually drives goes through the real wire below (L23).
shim_phase() {
    # shim_phase <MesPhase> <CurrentStep> <QcKind-to-clear>
    local phase="$1" step="$2" kind="$3"
    sqlite3 "$DB_PATH" "UPDATE WorkOrders SET MesPhase='$phase', CurrentStep='$step', UpdatedAt=datetime('now'), UpdatedBy='$ACTOR_TAG' WHERE Id=$WO_ID;"
    sqlite3 "$DB_PATH" "DELETE FROM WoQcCheckItems WHERE WoQcCheckId IN (SELECT Id FROM WoQcChecks WHERE WorkOrderId=$WO_ID AND QcKind='$kind');"
    sqlite3 "$DB_PATH" "DELETE FROM WoQcChecks WHERE WorkOrderId=$WO_ID AND QcKind='$kind';"
}

# GET /qc/<kind> → assert ≥<min> items materialise → PUT every item Ok
# via the real per-row wire. Sets MAT_COUNT. Returns 0 on full success.
materialise_ok() {
    # materialise_ok <kind> <min_items> <kind-label-UPPER>
    local kind="$1" min="$2" label="$3" rsp keys etag put_rsp put_http put_body put_count put_failed
    rsp=$(curl -s -w "\nHTTP:%{http_code}" -X GET "$API_BASE/api/v2/work-orders/$WO_ID/qc/$kind" \
        -H "$INSPECTOR_AUTH")
    LAST_HTTP=$(echo "$rsp" | grep -oE 'HTTP:[0-9]+$' | cut -d: -f2)
    LAST_BODY=$(echo "$rsp" | sed '$d')
    api_assert_routed "GET /qc/$kind"
    if [[ "$LAST_HTTP" != "200" ]]; then
        qc_diag "GET /qc/$kind materialisation failed" "200"
        MAT_COUNT=0
        return 1
    fi
    MAT_COUNT=$(echo "$LAST_BODY" | python3 -c "import sys,json; print(len(json.load(sys.stdin).get('items',[])))" 2>/dev/null)
    if [[ "${MAT_COUNT:-0}" -lt "$min" ]]; then
        qc_diag "$label profile materialisation thin" "≥$min items"
        echo "  [L23] GET /qc/$kind returned only ${MAT_COUNT:-0} items — QcProfileSeed broken or shrunk"
        return 1
    fi
    keys=$(echo "$LAST_BODY" | python3 -c "import sys,json; print(' '.join(i['itemKey'] for i in json.load(sys.stdin).get('items',[])))" 2>/dev/null)
    etag=$(etag_of "$WO_ID")
    put_failed=0
    put_count=0
    for KEY in $keys; do
        put_rsp=$(curl -s -w "\nHTTP:%{http_code}" -X PUT \
            "$API_BASE/api/v2/work-orders/$WO_ID/qc/$kind/items/$KEY" \
            -H "$INSPECTOR_AUTH" -H "Content-Type: application/json" \
            -H "If-Match: \"$etag\"" -H "Idempotency-Key: $(uuidgen)" \
            -d '{"status":"Ok"}')
        put_http=$(echo "$put_rsp" | grep -oE 'HTTP:[0-9]+$' | cut -d: -f2)
        put_body=$(echo "$put_rsp" | sed '$d')
        if [[ "$put_http" != "200" ]]; then
            echo "  [diag] PUT $kind items/$KEY → HTTP $put_http"
            echo "  [diag] body: $(echo "$put_body" | head -c 200)"
            put_failed=$((put_failed + 1))
            break
        fi
        etag=$(echo "$put_body" | json_field "eTag")
        put_count=$((put_count + 1))
    done
    if [[ "$put_failed" -eq 0 && "$put_count" -eq "$MAT_COUNT" ]]; then
        return 0
    fi
    echo "  [diag] PUT /qc/$kind/items — $put_count/$MAT_COUNT succeeded ($put_failed failed)"
    return 1
}

# ── 1. Boot API (L22 — kill stale listeners first) ────────────────
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
        dotnet run --no-build --no-launch-profile --urls "$API_BASE" > /tmp/checkpoint-7e-final-api.log 2>&1) &
    AUTO_BOOT_PID=$!
    BOOT_OK=0
    for i in $(seq 1 120); do
        code=$(curl -s -m 2 -o /dev/null -w "%{http_code}" "$API_BASE/health" 2>/dev/null)
        if [[ "$code" =~ ^(200|401|503)$ ]]; then BOOT_OK=1; break; fi
        sleep 1
    done
    if [[ $BOOT_OK -eq 1 ]]; then
        record PASS "API booted on $API_BASE (pid=$AUTO_BOOT_PID)"
    else
        record FAIL "API never reached /health"
        exit 1
    fi
fi

# All 3 OQC 3-sig flags must default ON — Q5 violations can't be proven otherwise.
if grep -q "OPS_OQC_REQUIRE_DISTINCT_REVIEWER=off\|OPS_OQC_REQUIRE_DISTINCT_APPROVER=off\|OPS_OQC_REQUIRE_APPROVER_DISTINCT_FROM_INSPECTOR=off" /tmp/checkpoint-7e-final-api.log 2>/dev/null; then
    echo "[abort] One or more 3-sig flags OFF — Q5 violations cannot be proven."
    echo "        All 3 default ON per §3.4 + Lesson L20. Fix .env + re-run."
    record FAIL "3-sig flags partially OFF"
    exit 1
fi

# ── 2. Login admin ───────────────────────────────────────────────
ADMIN_RSP=$(curl -s -X POST "$API_BASE/api/v2/auth/login" \
    -H "Content-Type: application/json" \
    -d '{"username":"admin","password":"admin","deviceId":"checkpoint-7e-final"}')
ADMIN_TOKEN=$(echo "$ADMIN_RSP" | json_field "accessToken")
if [[ -z "$ADMIN_TOKEN" ]]; then
    record FAIL "login admin failed: $ADMIN_RSP"
    exit 1
fi
record PASS "login admin"
ADMIN_AUTH="Authorization: Bearer $ADMIN_TOKEN"

# Build-sanity probe (L22) — confirm the running binary carries the 7e
# WoQcReviewController. A genuine missing route returns 404 with EMPTY
# body; the wired route returns ApiError JSON (wo.not_found on id 0) or 401.
echo "[build-sanity] probing GET /api/v2/work-orders/0/qc/fqc — confirms 7e controller is on the running binary"
SANITY_RSP=$(curl -s -w "\nHTTP:%{http_code}" -X GET "$API_BASE/api/v2/work-orders/0/qc/fqc" -H "$ADMIN_AUTH")
SANITY_HTTP=$(echo "$SANITY_RSP" | grep -oE 'HTTP:[0-9]+$' | cut -d: -f2)
SANITY_BODY=$(echo "$SANITY_RSP" | sed '$d')
if [[ "$SANITY_HTTP" == "404" && -z "$SANITY_BODY" ]]; then
    echo "[build-sanity] ✗ stale binary — running API lacks WoQcReviewController."
    echo "[build-sanity]   STALE_PID=\$(lsof -nP -iTCP:5100 -sTCP:LISTEN -t); [[ -n \"\$STALE_PID\" ]] && kill -9 \$STALE_PID"
    record FAIL "build-sanity: API on $API_BASE lacks WoQcReviewController (stale binary)"
    exit 1
fi
echo "[build-sanity] ✓ HTTP=$SANITY_HTTP (route wired)"

# ── 3. Self-seed 3 users ─────────────────────────────────────────
seed_user() {
    local user="$1" role="$2"
    if ! api_post_admin "/api/v2/admin/users" \
        "{\"username\":\"$user\",\"displayName\":\"P10.7e-final checkpoint test user\",\"role\":\"$role\",\"department\":\"QC\",\"password\":\"$TEST_PASSWORD\"}"; then
        return 1
    fi
    case "$LAST_HTTP" in
        201|200) return 0 ;;
        422)
            if echo "$LAST_BODY" | grep -q "accounts.username_in_use"; then return 0; fi
            echo "  [seed] user=$user 422 not username_in_use: $(echo "$LAST_BODY" | head -c 200)"; return 1 ;;
        *) echo "  [seed] user=$user http=$LAST_HTTP"; return 1 ;;
    esac
}
SEED_OK=1
seed_user "$INSPECTOR_USER" "QC" || SEED_OK=0
seed_user "$REVIEWER_USER"  "QC" || SEED_OK=0
seed_user "$APPROVER_USER"  "QC" || SEED_OK=0
if [[ $SEED_OK -eq 1 ]]; then
    record PASS "self-seed 3 users (Inspector + Reviewer + Approver, idempotent)"
else
    record FAIL "self-seed users failed"; exit 1
fi

# ── 4. Login all 3 users ─────────────────────────────────────────
INSPECTOR_TOKEN=$(login_user "$INSPECTOR_USER")
REVIEWER_TOKEN=$(login_user "$REVIEWER_USER")
APPROVER_TOKEN=$(login_user "$APPROVER_USER")
if [[ -n "$INSPECTOR_TOKEN" && -n "$REVIEWER_TOKEN" && -n "$APPROVER_TOKEN" ]]; then
    record PASS "login Inspector + Reviewer + Approver"
else
    record FAIL "login failed: I='${INSPECTOR_TOKEN:+(set)}' R='${REVIEWER_TOKEN:+(set)}' A='${APPROVER_TOKEN:+(set)}'"; exit 1
fi
INSPECTOR_AUTH="Authorization: Bearer $INSPECTOR_TOKEN"
REVIEWER_AUTH="Authorization: Bearer $REVIEWER_TOKEN"
APPROVER_AUTH="Authorization: Bearer $APPROVER_TOKEN"

# ── 5. Scrap picker source probe (L17) — feeds FQC/OQC reject reasons ─
SCRAP_CNT=$(curl -s -H "$INSPECTOR_AUTH" "$API_BASE/api/v2/reason-codes?kind=Scrap" \
    | python3 -c "import sys,json; print(len(json.load(sys.stdin)))" 2>/dev/null)
if [[ "${SCRAP_CNT:-0}" -ge 8 ]]; then
    record PASS "Scrap picker source (≥8 codes; got $SCRAP_CNT)"
else
    record FAIL "Scrap picker thin: $SCRAP_CNT"
fi

# ── Resolve WO ────────────────────────────────────────────────────
WO_ID=$(sqlite3 "$DB_PATH" "SELECT Id FROM WorkOrders WHERE WoNo='$WO_NO' LIMIT 1;" 2>/dev/null)
if [[ -z "$WO_ID" ]]; then
    record FAIL "WO $WO_NO not found"; exit 1
fi
echo "[ctx] WO Id      = $WO_ID"

# ═══════════════════════════════════════════════════════════════════
# CYCLE 1 — FQC Reject → PREPRESS (rework)
# ═══════════════════════════════════════════════════════════════════
shim_phase "FQC_PENDING" "Fqc" "FQC"
NEW_PHASE=$(sqlite3 "$DB_PATH" "SELECT MesPhase FROM WorkOrders WHERE Id=$WO_ID;")
if [[ "$NEW_PHASE" == "FQC_PENDING" ]]; then
    record PASS "C1 reset WO → FQC_PENDING (cleared FQC check; profile materialises via API)"
else
    record FAIL "C1 reset failed — phase is $NEW_PHASE"; exit 1
fi

if materialise_ok "fqc" 12 "FQC"; then
    record PASS "C1 GET /qc/fqc materialised $MAT_COUNT items + PUT every item Ok via real wire (≥12)"
else
    record FAIL "C1 FQC materialise/PUT failed"; exit 1
fi

qc_post INSPECTOR_AUTH "/api/v2/work-orders/$WO_ID/qc/fqc/judgment" \
    '{"judgment":"Reject","judgmentReason":"checkpoint-7e-final FQC reject — rework to prepress"}'
PHASE=$(echo "$LAST_BODY" | json_field "mesPhase")
if [[ "$LAST_HTTP" == "200" && "$PHASE" == "PREPRESS" ]]; then
    record PASS "C1 FQC Reject → MesPhase=PREPRESS (rework loop)"
else
    qc_diag "C1 FQC Reject unexpected" "200 + phase=PREPRESS"
    record FAIL "C1 FQC Reject failed: phase=$PHASE"
fi

# ═══════════════════════════════════════════════════════════════════
# CYCLE 2 — FQC Pass → OQC_PENDING (forward gate)
# ═══════════════════════════════════════════════════════════════════
shim_phase "FQC_PENDING" "Fqc" "FQC"
if materialise_ok "fqc" 12 "FQC"; then
    record PASS "C2 re-materialise FQC $MAT_COUNT items + PUT every item Ok"
else
    record FAIL "C2 FQC materialise/PUT failed"; exit 1
fi

qc_post INSPECTOR_AUTH "/api/v2/work-orders/$WO_ID/qc/fqc/judgment" '{"judgment":"Pass"}'
PHASE=$(echo "$LAST_BODY" | json_field "mesPhase")
if [[ "$LAST_HTTP" == "200" && "$PHASE" == "OQC_PENDING" ]]; then
    record PASS "C2 FQC Pass → MesPhase=OQC_PENDING"
else
    qc_diag "C2 FQC Pass unexpected" "200 + phase=OQC_PENDING"
    record FAIL "C2 FQC Pass failed: phase=$PHASE"; exit 1
fi

# ═══════════════════════════════════════════════════════════════════
# CYCLE 3 — OQC 3-sig + Q5 4-path + Reject re-loop
# ═══════════════════════════════════════════════════════════════════
# Fresh OQC check materialises lazily on first GET (none exists yet).
if materialise_ok "oqc" 28 "OQC"; then
    record PASS "C3 GET /qc/oqc materialised $MAT_COUNT items + PUT every item Ok via real wire (≥28)"
else
    record FAIL "C3 OQC materialise/PUT failed"; exit 1
fi

qc_post INSPECTOR_AUTH "/api/v2/work-orders/$WO_ID/qc/oqc/inspect" '{"note":"Inspector signed"}'
if [[ "$LAST_HTTP" == "200" && "$(echo "$LAST_BODY" | json_field "ok")" == "True" ]]; then
    record PASS "C3 POST /qc/oqc/inspect by $INSPECTOR_USER (sig 1)"
else
    qc_diag "C3 inspect sig 1 unexpected" "200 + ok=True"
    record FAIL "C3 OQC inspect failed"; exit 1
fi

qc_post INSPECTOR_AUTH "/api/v2/work-orders/$WO_ID/qc/oqc/review" '{}'
ERR_CODE=$(echo "$LAST_BODY" | json_field "errorCode")
if [[ "$LAST_HTTP" == "422" && "$ERR_CODE" == "oqc.same_user_as_inspector" ]]; then
    record PASS "C3 Q5 ❶: Reviewer = Inspector → 422 oqc.same_user_as_inspector (DENIED)"
else
    qc_diag "C3 Q5 ❶ unexpected" "422 + oqc.same_user_as_inspector"
    record FAIL "C3 Q5 ❶ failed"
fi

qc_post REVIEWER_AUTH "/api/v2/work-orders/$WO_ID/qc/oqc/review" '{"note":"Reviewer signed"}'
if [[ "$LAST_HTTP" == "200" && "$(echo "$LAST_BODY" | json_field "ok")" == "True" ]]; then
    record PASS "C3 POST /qc/oqc/review by $REVIEWER_USER (sig 2)"
else
    qc_diag "C3 review sig 2 unexpected" "200 + ok=True"
    record FAIL "C3 OQC review failed"; exit 1
fi

qc_post REVIEWER_AUTH "/api/v2/work-orders/$WO_ID/qc/oqc/approve" '{"outcome":"Approve"}'
ERR_CODE=$(echo "$LAST_BODY" | json_field "errorCode")
if [[ "$LAST_HTTP" == "422" && "$ERR_CODE" == "oqc.same_user_as_reviewer" ]]; then
    record PASS "C3 Q5 ❷: Approver = Reviewer → 422 oqc.same_user_as_reviewer (DENIED)"
else
    qc_diag "C3 Q5 ❷ unexpected" "422 + oqc.same_user_as_reviewer"
    record FAIL "C3 Q5 ❷ failed"
fi

qc_post INSPECTOR_AUTH "/api/v2/work-orders/$WO_ID/qc/oqc/approve" '{"outcome":"Approve"}'
ERR_CODE=$(echo "$LAST_BODY" | json_field "errorCode")
if [[ "$LAST_HTTP" == "422" && "$ERR_CODE" == "oqc.same_user_as_inspector" ]]; then
    record PASS "C3 Q5 ❸: Approver = Inspector → 422 oqc.same_user_as_inspector (DENIED)"
else
    qc_diag "C3 Q5 ❸ unexpected" "422 + oqc.same_user_as_inspector"
    record FAIL "C3 Q5 ❸ failed"
fi

qc_post APPROVER_AUTH "/api/v2/work-orders/$WO_ID/qc/oqc/approve" \
    '{"outcome":"Reject","judgmentReason":"checkpoint-7e-final OQC reject — re-loop to FQC"}'
PHASE=$(echo "$LAST_BODY" | json_field "mesPhase")
if [[ "$LAST_HTTP" == "200" && "$PHASE" == "FQC_PENDING" ]]; then
    record PASS "C3 OQC Reject (by Approver) → MesPhase=FQC_PENDING (re-loop)"
else
    qc_diag "C3 OQC Reject unexpected" "200 + phase=FQC_PENDING"
    record FAIL "C3 OQC Reject failed: phase=$PHASE"
fi

# ═══════════════════════════════════════════════════════════════════
# CYCLE 4 — re-pass the loop to SHIPPED
# ═══════════════════════════════════════════════════════════════════
shim_phase "FQC_PENDING" "Fqc" "FQC"
if materialise_ok "fqc" 12 "FQC"; then :; else record FAIL "C4 FQC re-materialise failed"; exit 1; fi
qc_post INSPECTOR_AUTH "/api/v2/work-orders/$WO_ID/qc/fqc/judgment" '{"judgment":"Pass"}'
PHASE=$(echo "$LAST_BODY" | json_field "mesPhase")
if [[ "$LAST_HTTP" == "200" && "$PHASE" == "OQC_PENDING" ]]; then
    record PASS "C4 FQC Pass → OQC_PENDING ($MAT_COUNT FQC items)"
else
    qc_diag "C4 FQC Pass unexpected" "200 + phase=OQC_PENDING"
    record FAIL "C4 FQC Pass failed: phase=$PHASE"; exit 1
fi

# Clear the prior (Reject-stamped) OQC check so a fresh 3-sig chain runs.
sqlite3 "$DB_PATH" "DELETE FROM WoQcCheckItems WHERE WoQcCheckId IN (SELECT Id FROM WoQcChecks WHERE WorkOrderId=$WO_ID AND QcKind='OQC');"
sqlite3 "$DB_PATH" "DELETE FROM WoQcChecks WHERE WorkOrderId=$WO_ID AND QcKind='OQC';"
if materialise_ok "oqc" 28 "OQC"; then
    record PASS "C4 re-materialise OQC $MAT_COUNT items + PUT every item Ok"
else
    record FAIL "C4 OQC re-materialise failed"; exit 1
fi

qc_post INSPECTOR_AUTH "/api/v2/work-orders/$WO_ID/qc/oqc/inspect" '{"note":"Inspector re-signed"}'
if [[ "$LAST_HTTP" == "200" && "$(echo "$LAST_BODY" | json_field "ok")" == "True" ]]; then
    record PASS "C4 OQC inspect (sig 1)"
else
    qc_diag "C4 inspect unexpected" "200 + ok=True"; record FAIL "C4 OQC inspect failed"; exit 1
fi

qc_post REVIEWER_AUTH "/api/v2/work-orders/$WO_ID/qc/oqc/review" '{"note":"Reviewer re-signed"}'
if [[ "$LAST_HTTP" == "200" && "$(echo "$LAST_BODY" | json_field "ok")" == "True" ]]; then
    record PASS "C4 OQC review (sig 2)"
else
    qc_diag "C4 review unexpected" "200 + ok=True"; record FAIL "C4 OQC review failed"; exit 1
fi

qc_post APPROVER_AUTH "/api/v2/work-orders/$WO_ID/qc/oqc/approve" '{"outcome":"Approve"}'
PHASE=$(echo "$LAST_BODY" | json_field "mesPhase")
if [[ "$LAST_HTTP" == "200" && "$(echo "$LAST_BODY" | json_field "ok")" == "True" && "$PHASE" == "SHIPPED" ]]; then
    record PASS "C4 OQC Approve (sig 3, distinct) → MesPhase=SHIPPED"
else
    qc_diag "C4 OQC Approve unexpected" "200 + ok=True + phase=SHIPPED"
    record FAIL "C4 OQC Approve failed: phase=$PHASE"
fi

# ═══════════════════════════════════════════════════════════════════
# CYCLE 5 — Q8 summary report (ShippedSummaryDashboard source)
# ═══════════════════════════════════════════════════════════════════
SUM_RSP=$(curl -s -w "\nHTTP:%{http_code}" -X GET "$API_BASE/api/v2/work-orders/$WO_ID/summary-report" \
    -H "$APPROVER_AUTH")
LAST_HTTP=$(echo "$SUM_RSP" | grep -oE 'HTTP:[0-9]+$' | cut -d: -f2)
LAST_BODY=$(echo "$SUM_RSP" | sed '$d')
api_assert_routed "GET /summary-report"
SUM_OK=$(echo "$LAST_BODY" | python3 -c "
import sys,json
try:
    v=json.load(sys.stdin)
except Exception:
    print('0'); sys.exit()
ok = v.get('mesPhase')=='SHIPPED' and v.get('woNo') and 'totals' in v and 'qcSummary' in v
print('1' if ok else '0')
" 2>/dev/null)
if [[ "$LAST_HTTP" == "200" && "$SUM_OK" == "1" ]]; then
    record PASS "C5 GET /summary-report → 200 + woNo + MesPhase=SHIPPED + totals + qcSummary (live-recomputed)"
else
    qc_diag "C5 summary-report unexpected" "200 + shipped + totals + qcSummary"
    record FAIL "C5 summary-report failed (ok=$SUM_OK)"
fi

# ═══════════════════════════════════════════════════════════════════
# Audit wire-mirror (R7.3) — all 9 outgoing-quality action codes
# ═══════════════════════════════════════════════════════════════════
AUDIT_MISS=()
for ACTION in \
    WO_FQC_REJECT_TO_PREPRESS \
    WO_FQC_JUDGMENT \
    WO_OQC_INSPECT \
    WO_OQC_REVIEW \
    WO_OQC_REVIEW_DENIED \
    WO_OQC_APPROVE_DENIED \
    WO_OQC_REJECT_TO_FQC_PENDING \
    WO_OQC_APPROVE \
    WO_SHIPPED; do
    AUDIT=$(curl -s -H "$ADMIN_AUTH" "$API_BASE/api/v2/audit/log?action=$ACTION&page=1&pageSize=50")
    if ! echo "$AUDIT" | grep -q "\"targetId\":\"$WO_ID\""; then
        AUDIT_MISS+=("$ACTION")
    fi
done
if [[ ${#AUDIT_MISS[@]} -eq 0 ]]; then
    record PASS "audit wire-mirror (9/9): FQC_REJECT_TO_PREPRESS + FQC_JUDGMENT + OQC_INSPECT + OQC_REVIEW + OQC_REVIEW_DENIED + OQC_APPROVE_DENIED + OQC_REJECT_TO_FQC_PENDING + OQC_APPROVE + WO_SHIPPED"
else
    record FAIL "audit wire missing: ${AUDIT_MISS[*]}"
fi

# ── L21 wire assertion (every WO DTO projects MesPhase per L19) ───
FINAL_PHASE=$(curl -s -H "$APPROVER_AUTH" "$API_BASE/api/v2/work-orders/by-no/$WO_NO/summary" | json_field "mesPhase")
if [[ "$FINAL_PHASE" == "SHIPPED" ]]; then
    record PASS "L21 wire: /by-no/$WO_NO/summary returns SHIPPED — auto-route mounts ShippedSummaryDashboard"
else
    record FAIL "L21 wire: by-no/summary returned MesPhase='$FINAL_PHASE' (expected SHIPPED)"
fi

# ── Keep-alive forensic dump ─────────────────────────────────────
if [[ $KEEP_ALIVE -eq 1 ]]; then
    echo ""
    echo "── KEEP-ALIVE: forensic state ──────────────────────────────────"
    sqlite3 "$DB_PATH" -header -column "
        SELECT WoNo, MesPhase, CurrentStep, UpdatedBy FROM WorkOrders WHERE Id=$WO_ID;"
    echo ""
    sqlite3 "$DB_PATH" -header -column "
        SELECT QcKind, Judgment, InspectedBy, ReviewedBy, ApprovedBy
        FROM WoQcChecks WHERE WorkOrderId=$WO_ID ORDER BY QcKind;"
    echo ""
    sqlite3 "$DB_PATH" -header -column "
        SELECT Action, COUNT(*) AS rows
        FROM AuditLogs
        WHERE TargetId='$WO_ID' AND TargetType='WorkOrder'
          AND Action IN ('WO_FQC_REJECT_TO_PREPRESS','WO_FQC_JUDGMENT',
                         'WO_OQC_INSPECT','WO_OQC_REVIEW','WO_OQC_REVIEW_DENIED',
                         'WO_OQC_APPROVE_DENIED','WO_OQC_REJECT_TO_FQC_PENDING',
                         'WO_OQC_APPROVE','WO_SHIPPED')
        GROUP BY Action ORDER BY Action;"
fi

if [[ $FAIL -gt 0 ]]; then exit 1; fi
exit 0
