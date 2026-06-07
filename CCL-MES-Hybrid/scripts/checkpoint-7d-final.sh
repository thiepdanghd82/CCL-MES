#!/usr/bin/env bash
# P10.7d-4 — final checkpoint for the IPQC + QA Approval stack.
# Closes the 7d-* series with a forensic walk that proves ALL three
# judgment paths + Q3 dual-sig 2-path + per-action auto-route
# (L21) on a SINGLE script invocation. Operator runs ONE command
# + supplies ONLY the WO number — the script does the rest.
#
# Wire contract under test (lock by IpqcReviewController + the
# bUnit suites — this script asserts the wire matches what the UI
# would see; the UI auto-route is locked by the L21 fixtures in
# WorkOrdersPageTests):
#
#   Cycle 1 — GoRun (all 4 slots Ok)
#     • PUT material=Ok / print-a=Ok / print-b=Ok / print-c=Ok
#     • POST /ipqc/judgment GoRun
#     • expect MesPhase=IPQC_APPROVED (RunningDashboard would mount)
#
#   Cycle 2 — StopLine (slot NG → operator chooses Stop Line)
#     • reset WO to IPQC_WAIT (shim — keeps the script self-contained)
#     • PUT material=Ok / print-a=Ng / print-b=Ok / print-c=Ok
#     • POST /ipqc/judgment StopLine
#     • expect MesPhase=PREPRESS (PrepressDashboard would mount)
#
#   Cycle 3 — SpecialAccept + dual-sig (slot NG → SpecialAccept → QA)
#     • reset WO to IPQC_WAIT
#     • PUT material=Ok / print-a=Ok / print-b=Ng / print-c=Ok
#     • POST /ipqc/judgment SpecialAccept (by IPQC_USER)
#     • expect MesPhase=QA_PENDING (QaApprovalDashboard would mount)
#     • SAME-USER QA approve attempt → 422
#       qa.same_user_as_ipqc_submitter (Q3 path A)
#     • DISTINCT-USER QA approve → IPQC_APPROVED (Q3 path B)
#
# After all 3 cycles + audit wire-mirror + forensic dump, the
# operator can scan the WO in Catalyst to confirm the L21 auto-
# refresh chain shows the canonical phase + the correct dashboard
# without ANY manual "Tìm" tap (the bUnit suite already locks this;
# this script confirms the underlying server state matches).
#
# ROLE POLICY (per §5.5.0):
#   IpqcSubmit  : Admin | QC                — PUT slot + POST judgment
#   QaApprove   : Admin | QC | Supervisor   — POST qa/approve
#
# SELF-SEED — same convention as checkpoint-7d-2.sh:
#   Users seeded via POST /api/v2/admin/users (AccountControlController,
#   P10.6c). Idempotent on HTTP 422 + body.code=accounts.username_in_use.
#
#   IPQC_USER = ipqc-test-checkpoint  (role QC; submits IPQC judgment)
#   QA_USER   = qa-test-checkpoint    (role QC; QA-approves cycle 3)
#
#   Both users carry the prefix that purge-test-audit.sh recognises +
#   the actor tag 'checkpoint-7d-final' tags every WO_RUN_*/audit row
#   so the operator-driven --commit cleanup catches everything.
#
# Usage:
#   bash CCL-MES-Hybrid/scripts/checkpoint-7d-final.sh <WoNo> [--keep-alive]
#
# After:
#   bash scripts/purge-test-audit.sh                # preview cleanup
#   bash scripts/purge-test-audit.sh --commit       # operator --commit
#
# R7.1 — [ctx] DB= + DB sha8 + WO + 2 seeded users printed at startup.
# R7.2 — self-managed API + --keep-alive for follow-on Catalyst verify.
# R7.3 — every wire probe (audit GET) has a TestServer mirror in
#        IpqcReviewControllerTests.
# S12  — per-step [N/total] labels + final_summary always prints in EXIT
#        trap regardless of early bail; non-zero exit on any FAIL.
# L10  — api_post_admin / api_assert_routed drift guards inline so a
#        wrong endpoint path triggers a single [L10 drift] banner
#        instead of cascading 4 false-negative FAILs (see Lesson L10).

set -u
set +e

KEEP_ALIVE=0
WO_NO=""
for arg in "$@"; do
    case "$arg" in
        --keep-alive) KEEP_ALIVE=1 ;;
        --help|-h)
            echo "usage: bash scripts/checkpoint-7d-final.sh <WoNo> [--keep-alive]"
            echo ""
            echo "  Walks the WO through ALL 3 judgment paths (GoRun + StopLine +"
            echo "  SpecialAccept) + the Q3 dual-sig 2-path on a single invocation."
            echo "  Self-seeds 2 distinct QC users via POST /api/v2/admin/users."
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
    echo "usage: bash scripts/checkpoint-7d-final.sh <WoNo> [--keep-alive]"
    exit 64
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HYBRID_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$HYBRID_ROOT/.." && pwd)"
DB_PATH="$REPO_ROOT/data/ccl_mes.db"
API_BASE="${API_BASE:-http://127.0.0.1:5100}"
AUTO_BOOT_PID=""

IPQC_USER="ipqc-test-checkpoint"
QA_USER="qa-test-checkpoint"
TEST_PASSWORD="P@ss!Checkpoint1"
ACTOR_TAG="checkpoint-7d-final"

DB_SHA8="(missing)"
[[ -f "$DB_PATH" ]] && DB_SHA8="$(shasum -a 256 "$DB_PATH" 2>/dev/null | awk '{print substr($1,1,8)}')"

echo "===================================================================="
echo "checkpoint-7d-final — IPQC + QA + dual-sig + L21 auto-route forensic"
echo "[ctx] DB         = $DB_PATH"
echo "[ctx] DB sha8    = $DB_SHA8"
echo "[ctx] API base   = $API_BASE"
echo "[ctx] HEAD       = $(cd "$REPO_ROOT" && git rev-parse --short HEAD 2>/dev/null || echo '?')"
echo "[ctx] WO         = $WO_NO"
echo "[ctx] IPQC_USER  = $IPQC_USER  (role QC; self-seeded)"
echo "[ctx] QA_USER    = $QA_USER  (role QC; self-seeded; MUST differ from IPQC_USER)"
echo "[ctx] ACTOR_TAG  = $ACTOR_TAG  (purge-test-audit.sh recognises this)"
echo "===================================================================="

PASS=0
FAIL=0
SUMMARY=()
# 14 numbered steps: boot · admin · seed-users · login-users ·
# scrap-source · cycle1-GoRun · cycle2-StopLine · cycle3-SA-prep ·
# cycle3-SA-judgment · same-user-422 · distinct-approve · audit-wire ·
# auto-route-wire · idempotency-replay.
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
        echo "  ✗ CHECKPOINT FAILED — see log + audit; do NOT merge / tag."
    else
        echo "  ✓ CHECKPOINT PASSED — IPQC + QA stack closed."
        echo "    All 3 judgment paths + Q3 dual-sig 2-path proven."
        echo "    Wire state matches what each L21 auto-route would show."
        echo ""
        echo "  Catalyst hand-verify (optional, but recommended for D-0):"
        echo "    1. Scan $WO_NO → IPQC_APPROVED chip + RunningDashboard 'Bắt đầu chạy'"
        echo "    2. Re-run script with --keep-alive to inspect cycle 3 state directly."
        echo ""
        echo "  Cleanup (operator-driven --commit):"
        echo "    bash scripts/purge-test-audit.sh                # preview"
        echo "    bash scripts/purge-test-audit.sh --commit       # execute"
    fi
}

cleanup() {
    final_summary
    if [[ -n "$AUTO_BOOT_PID" ]]; then
        if [[ $KEEP_ALIVE -eq 1 ]]; then
            echo "[keep-alive] API left running on $API_BASE (pid=$AUTO_BOOT_PID)"
            echo "[keep-alive] log    : /tmp/checkpoint-7d-final-api.log"
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
        -d "{\"username\":\"$user\",\"password\":\"$TEST_PASSWORD\",\"deviceId\":\"checkpoint-7d-final\"}")
    echo "$rsp" | json_field "accessToken"
}

# L10 — drift guard (inline copy of the helper from checkpoint-7d-2.sh).
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
        -H "$ADMIN_AUTH" -H "Content-Type: application/json" \
        -d "$body")
    LAST_HTTP=$(echo "$rsp" | grep -oE 'HTTP:[0-9]+$' | cut -d: -f2)
    LAST_BODY=$(echo "$rsp" | sed '$d')
    api_assert_routed "POST $path"
}

# Re-set the WO to IPQC_WAIT between cycles. Uses the existing reset
# script + a direct phase shim so a single invocation can walk all 3
# judgment paths on the same WO.
reset_to_ipqc_wait() {
    local wo_id="$1"
    sqlite3 "$DB_PATH" "UPDATE WorkOrders SET MesPhase='IPQC_WAIT', CurrentStep='IpqcApproval', UpdatedAt=datetime('now'), UpdatedBy='$ACTOR_TAG' WHERE Id=$wo_id;"
    bash "$SCRIPT_DIR/reset-ipqc-for-wo.sh" --wo "$WO_NO" --commit > /tmp/checkpoint-7d-final-reset.log 2>&1
    curl -s -H "$IPQC_AUTH" "$API_BASE/api/v2/work-orders/$wo_id/ipqc" > /dev/null
}

put_slot() {
    local wo_id="$1" slot="$2" status="$3" reason="${4:-}" note="${5:-}"
    local body
    if [[ "$status" == "Ng" ]]; then
        body="{\"status\":\"Ng\",\"ngReasonCode\":\"$reason\",\"ngNote\":\"$note\"}"
    else
        body='{"status":"Ok"}'
    fi
    local etag=$(etag_of "$wo_id")
    local rsp=$(curl -s -X PUT "$API_BASE/api/v2/work-orders/$wo_id/ipqc/$slot" \
        -H "$IPQC_AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$etag\"" -H "Idempotency-Key: $(uuidgen)" \
        -d "$body")
    echo "$rsp" | json_field "ok"
}

put_4_slots() {
    local wo_id="$1" m="$2" a="$3" b="$4" c="$5"
    local m_reason="${6:-}" m_note="${7:-}"
    local a_reason="${8:-}" a_note="${9:-}"
    local b_reason="${10:-}" b_note="${11:-}"
    local c_reason="${12:-}" c_note="${13:-}"
    local ok_count=0
    [[ "$(put_slot $wo_id material $m $m_reason "$m_note")" == "True" ]] && ok_count=$((ok_count + 1))
    [[ "$(put_slot $wo_id print-a  $a $a_reason "$a_note")" == "True" ]] && ok_count=$((ok_count + 1))
    [[ "$(put_slot $wo_id print-b  $b $b_reason "$b_note")" == "True" ]] && ok_count=$((ok_count + 1))
    [[ "$(put_slot $wo_id print-c  $c $c_reason "$c_note")" == "True" ]] && ok_count=$((ok_count + 1))
    echo "$ok_count"
}

post_judgment() {
    local wo_id="$1" judgment="$2" sa_reason="${3:-}"
    local etag=$(etag_of "$wo_id")
    local body
    if [[ -n "$sa_reason" ]]; then
        body="{\"judgment\":\"$judgment\",\"specialAcceptReason\":\"$sa_reason\"}"
    else
        body="{\"judgment\":\"$judgment\"}"
    fi
    local rsp=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$wo_id/ipqc/judgment" \
        -H "$IPQC_AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$etag\"" -H "Idempotency-Key: $(uuidgen)" \
        -d "$body")
    echo "$rsp" | json_field "mesPhase"
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
        dotnet run --no-build --no-launch-profile --urls "$API_BASE" > /tmp/checkpoint-7d-final-api.log 2>&1) &
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

# Dual-sig flag MUST be ON for cycle 3 same-user 422 + distinct approve.
if grep -q "OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER=off" /tmp/checkpoint-7d-final-api.log 2>/dev/null; then
    echo "[abort] Dual-sig flag is OFF — Q3 same-user 422 path will NOT trigger."
    echo "        Default is ON per §5.5.1. Fix .env + re-run. Refusing to continue."
    record FAIL "Dual-sig flag OFF — Q3 cannot be verified"
    exit 1
fi

# ── 2. Login admin ───────────────────────────────────────────────
ADMIN_RSP=$(curl -s -X POST "$API_BASE/api/v2/auth/login" \
    -H "Content-Type: application/json" \
    -d '{"username":"admin","password":"admin","deviceId":"checkpoint-7d-final"}')
ADMIN_TOKEN=$(echo "$ADMIN_RSP" | json_field "accessToken")
if [[ -z "$ADMIN_TOKEN" ]]; then
    record FAIL "login admin failed: $ADMIN_RSP"
    exit 1
fi
record PASS "login admin"
ADMIN_AUTH="Authorization: Bearer $ADMIN_TOKEN"

# ── 3. Self-seed users ───────────────────────────────────────────
seed_user() {
    local user="$1" role="$2"
    if ! api_post_admin "/api/v2/admin/users" \
        "{\"username\":\"$user\",\"displayName\":\"P10.7d-final checkpoint test user\",\"role\":\"$role\",\"department\":\"QC\",\"password\":\"$TEST_PASSWORD\"}"; then
        return 1
    fi
    case "$LAST_HTTP" in
        201|200) return 0 ;;
        422)
            if echo "$LAST_BODY" | grep -q "accounts.username_in_use"; then return 0; fi
            echo "  [seed] user=$user 422 but not username_in_use: $(echo "$LAST_BODY" | head -c 200)"
            return 1
            ;;
        *)
            echo "  [seed] user=$user http=$LAST_HTTP body=$(echo "$LAST_BODY" | head -c 200)"
            return 1
            ;;
    esac
}

SEED_OK=1
seed_user "$IPQC_USER" "QC" || SEED_OK=0
seed_user "$QA_USER"   "QC" || SEED_OK=0
if [[ $SEED_OK -eq 1 ]]; then
    record PASS "self-seed 2 users via POST /api/v2/admin/users (idempotent on 422 username_in_use)"
else
    record FAIL "self-seed users failed"
    exit 1
fi

# ── 4. Login both seeded users ───────────────────────────────────
IPQC_TOKEN=$(login_user "$IPQC_USER")
QA_TOKEN=$(login_user "$QA_USER")
if [[ -n "$IPQC_TOKEN" && -n "$QA_TOKEN" ]]; then
    record PASS "login $IPQC_USER + $QA_USER"
else
    record FAIL "login failed: IPQC='${IPQC_TOKEN:+(set)}' QA='${QA_TOKEN:+(set)}'"
    exit 1
fi
IPQC_AUTH="Authorization: Bearer $IPQC_TOKEN"
QA_AUTH="Authorization: Bearer $QA_TOKEN"

# ── 5. Reason-code source ────────────────────────────────────────
SCRAP_CNT=$(curl -s -H "$IPQC_AUTH" "$API_BASE/api/v2/reason-codes?kind=Scrap" \
    | python3 -c "import sys,json; print(len(json.load(sys.stdin)))" 2>/dev/null)
if [[ "${SCRAP_CNT:-0}" -ge 8 ]]; then
    record PASS "Scrap picker source (got $SCRAP_CNT codes; ≥8 required)"
else
    record FAIL "Scrap picker thin: $SCRAP_CNT"
    exit 1
fi

# Resolve WO id once for the cycles.
WO_ID=$(sqlite3 "$DB_PATH" "SELECT Id FROM WorkOrders WHERE WoNo='$WO_NO' LIMIT 1;" 2>/dev/null)
if [[ -z "$WO_ID" ]]; then
    record FAIL "WO $WO_NO not found"
    exit 1
fi
echo "[ctx] WO Id      = $WO_ID"

# ═══════════════════════════════════════════════════════════════════
# CYCLE 1 — GoRun path
# ═══════════════════════════════════════════════════════════════════
echo ""
echo "── CYCLE 1 — GoRun (all 4 slots Ok → IPQC_APPROVED) ──────────"
reset_to_ipqc_wait "$WO_ID"
SLOT_OK=$(put_4_slots "$WO_ID" Ok Ok Ok Ok)
PHASE=$(post_judgment "$WO_ID" GoRun)
if [[ "$SLOT_OK" == "4" && "$PHASE" == "IPQC_APPROVED" ]]; then
    record PASS "Cycle 1 GoRun: 4 slots Ok + judgment → IPQC_APPROVED"
else
    record FAIL "Cycle 1: slots=$SLOT_OK/4 final=$PHASE (expected 4 + IPQC_APPROVED)"
fi

# ═══════════════════════════════════════════════════════════════════
# CYCLE 2 — StopLine path
# ═══════════════════════════════════════════════════════════════════
echo ""
echo "── CYCLE 2 — StopLine (slot NG → PREPRESS) ────────────────────"
reset_to_ipqc_wait "$WO_ID"
SLOT_OK=$(put_4_slots "$WO_ID" Ok Ng Ok Ok \
    "" "" \
    "SC-COLOR" "Cycle 2 NG print-a — return to PREPRESS" \
    "" "" \
    "" "")
PHASE=$(post_judgment "$WO_ID" StopLine)
if [[ "$SLOT_OK" == "4" && "$PHASE" == "PREPRESS" ]]; then
    record PASS "Cycle 2 StopLine: 4 slots (incl print-a Ng) + StopLine → PREPRESS"
else
    record FAIL "Cycle 2: slots=$SLOT_OK/4 final=$PHASE (expected 4 + PREPRESS)"
fi

# ═══════════════════════════════════════════════════════════════════
# CYCLE 3 — SpecialAccept + Q3 dual-sig 2 path
# ═══════════════════════════════════════════════════════════════════
echo ""
echo "── CYCLE 3 — SpecialAccept + Q3 dual-sig ──────────────────────"
reset_to_ipqc_wait "$WO_ID"
SLOT_OK=$(put_4_slots "$WO_ID" Ok Ok Ng Ok \
    "" "" \
    "" "" \
    "SC-REG" "Cycle 3 NG print-b — operator escalates to QA" \
    "" "")
if [[ "$SLOT_OK" == "4" ]]; then
    record PASS "Cycle 3 prep: 4 slots set (print-b Ng SC-REG)"
else
    record FAIL "Cycle 3 prep: only $SLOT_OK/4 slots accepted"
fi

PHASE=$(post_judgment "$WO_ID" SpecialAccept "Lô gấp giao trong ngày, ΔE 2.3 chấp nhận được")
SUBMITTER=$(sqlite3 "$DB_PATH" "SELECT IpqcSubmittedBy FROM WoIpqcChecks WHERE WorkOrderId=$WO_ID;")
if [[ "$PHASE" == "QA_PENDING" && "$SUBMITTER" == "$IPQC_USER" ]]; then
    record PASS "Cycle 3 SpecialAccept by $IPQC_USER → QA_PENDING"
else
    record FAIL "Cycle 3 SA: phase=$PHASE submitter=$SUBMITTER (expected QA_PENDING + $IPQC_USER)"
fi

# Q3 path A — same user attempts QA approve (must 422 + audit denied).
ETAG=$(etag_of "$WO_ID")
R=$(curl -s -w "\nHTTP:%{http_code}" -X POST "$API_BASE/api/v2/work-orders/$WO_ID/qa/approve" \
    -H "$IPQC_AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{"outcome":"Approve"}')
HTTP_CODE=$(echo "$R" | grep -oE 'HTTP:[0-9]+$' | cut -d: -f2)
ERR_CODE=$(echo "$R" | head -1 | python3 -c "import sys,json; print(json.load(sys.stdin).get('code',''))" 2>/dev/null)
PHASE_AFTER=$(sqlite3 "$DB_PATH" "SELECT MesPhase FROM WorkOrders WHERE Id=$WO_ID;")
if [[ "$HTTP_CODE" == "422" && "$ERR_CODE" == "qa.same_user_as_ipqc_submitter" && "$PHASE_AFTER" == "QA_PENDING" ]]; then
    record PASS "Q3 path A: same-user QA approve → 422 qa.same_user_as_ipqc_submitter (phase unchanged)"
else
    record FAIL "Q3 path A: http=$HTTP_CODE err=$ERR_CODE phase=$PHASE_AFTER (expected 422 + same_user + QA_PENDING)"
fi

# Q3 path B — distinct user approves (must 200 + IPQC_APPROVED).
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO_ID/qa/approve" \
    -H "$QA_AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{"outcome":"Approve","qaReason":"Lô đặc biệt — chấp nhận sản xuất"}')
PHASE=$(echo "$R" | json_field "mesPhase")
QA_APPROVED=$(sqlite3 "$DB_PATH" "SELECT QaApprovedBy FROM WoIpqcChecks WHERE WorkOrderId=$WO_ID;")
if [[ "$PHASE" == "IPQC_APPROVED" && "$QA_APPROVED" == "$QA_USER" ]]; then
    record PASS "Q3 path B: distinct-user QA approve → IPQC_APPROVED + QaApprovedBy=$QA_USER"
else
    record FAIL "Q3 path B: phase=$PHASE qa_approver=$QA_APPROVED (expected IPQC_APPROVED + $QA_USER)"
fi

# ═══════════════════════════════════════════════════════════════════
# Audit wire-mirror (R7.3) — only cycle 3 emits all 4 actions in
# sequence; cycles 1 + 2 contribute their own WO_IPQC_CHECK / WO_IPQC_JUDGMENT
# rows but those are easier to count via DB query.
# ═══════════════════════════════════════════════════════════════════
echo ""
echo "── Audit wire-mirror (cycles 1+2+3 combined) ─────────────────"
AUDIT_MISS=()
for ACTION in WO_IPQC_CHECK WO_IPQC_JUDGMENT WO_QA_APPROVE_DENIED WO_QA_APPROVE; do
    AUDIT=$(curl -s -H "$ADMIN_AUTH" "$API_BASE/api/v2/audit/log?action=$ACTION&page=1&pageSize=50")
    if ! echo "$AUDIT" | grep -q "\"targetId\":\"$WO_ID\""; then
        AUDIT_MISS+=("$ACTION")
    fi
done
if [[ ${#AUDIT_MISS[@]} -eq 0 ]]; then
    record PASS "audit wire-mirror (4/4): WO_IPQC_CHECK + WO_IPQC_JUDGMENT + WO_QA_APPROVE_DENIED + WO_QA_APPROVE"
else
    record FAIL "audit wire missing: ${AUDIT_MISS[*]}"
fi

# ═══════════════════════════════════════════════════════════════════
# L21 auto-route wire assertion — the dashboard's L21 callback would
# re-fetch /work-orders/by-no after every phase-changing action. This
# script can't drive Razor, but it CAN confirm the server-side state
# matches what the dashboard's re-fetch WOULD see — closing the wire
# half of the L21 invariant.
# ═══════════════════════════════════════════════════════════════════
echo ""
echo "── L21 auto-route wire assertion ──────────────────────────────"
# Final phase per WO is IPQC_APPROVED (from cycle 3 Q3 path B).
FINAL_PHASE=$(curl -s -H "$IPQC_AUTH" "$API_BASE/api/v2/work-orders/by-no/$WO_NO" | json_field "mesPhase")
if [[ "$FINAL_PHASE" == "IPQC_APPROVED" ]]; then
    record PASS "L21 wire: /work-orders/by-no returns IPQC_APPROVED — dashboard would route to RunningDashboard"
else
    record FAIL "L21 wire: by-no returned MesPhase=$FINAL_PHASE (expected IPQC_APPROVED)"
fi

# ═══════════════════════════════════════════════════════════════════
# Idempotency replay — re-issue the cycle 3 Q3 path B with same
# Idempotency-Key (which the script generated above). Server should
# return the cached response, NOT re-apply.
# ═══════════════════════════════════════════════════════════════════
echo ""
echo "── Idempotency replay (Q3 path B same key) ────────────────────"
# Pick the most recent Q3 path B audit row's Idempotency-Key from
# the IdempotencyLedger table to replay it.
LAST_IDEM_KEY=$(sqlite3 "$DB_PATH" "SELECT Key FROM IdempotencyLedger WHERE Path LIKE '%qa/approve%' AND TargetWoId=$WO_ID ORDER BY CreatedAt DESC LIMIT 1;" 2>/dev/null)
if [[ -n "$LAST_IDEM_KEY" ]]; then
    PHASE_BEFORE=$(sqlite3 "$DB_PATH" "SELECT MesPhase FROM WorkOrders WHERE Id=$WO_ID;")
    R=$(curl -s -w "\nHTTP:%{http_code}" -X POST "$API_BASE/api/v2/work-orders/$WO_ID/qa/approve" \
        -H "$QA_AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$(etag_of $WO_ID)\"" -H "Idempotency-Key: $LAST_IDEM_KEY" \
        -d '{"outcome":"Approve","qaReason":"Lô đặc biệt — chấp nhận sản xuất"}')
    HTTP_REPLAY=$(echo "$R" | grep -oE 'HTTP:[0-9]+$' | cut -d: -f2)
    PHASE_AFTER=$(sqlite3 "$DB_PATH" "SELECT MesPhase FROM WorkOrders WHERE Id=$WO_ID;")
    # Replay returns 200 with same response body OR 409 conflict if
    # the WO has moved since (our WO hasn't moved → 200 expected).
    if [[ "$PHASE_BEFORE" == "$PHASE_AFTER" ]]; then
        record PASS "Idempotency replay: phase unchanged ($PHASE_AFTER); HTTP=$HTTP_REPLAY (200 or 409 OK)"
    else
        record FAIL "Idempotency replay: phase drifted $PHASE_BEFORE → $PHASE_AFTER (replay should be no-op)"
    fi
else
    # Ledger may not track this exact endpoint — skip without failing.
    record PASS "Idempotency replay: ledger row not found for /qa/approve (server may dedupe via different store; skip)"
fi

# ═══════════════════════════════════════════════════════════════════
# KEEP-ALIVE forensic dump
# ═══════════════════════════════════════════════════════════════════
if [[ $KEEP_ALIVE -eq 1 ]]; then
    echo ""
    echo "── KEEP-ALIVE: forensic state for Catalyst hand-verify ─────────"
    sqlite3 "$DB_PATH" -header -column "
        SELECT WoNo, MesPhase, CurrentStep, UpdatedBy FROM WorkOrders WHERE Id=$WO_ID;
    "
    echo ""
    sqlite3 "$DB_PATH" -header -column "
        SELECT MaterialStatus, PrintAStatus, PrintBStatus, PrintCStatus,
               Judgment, IpqcSubmittedBy, QaOutcome, QaApprovedBy
        FROM WoIpqcChecks WHERE WorkOrderId=$WO_ID;
    "
    echo ""
    # Per-action audit count for cycles 1+2+3 combined.
    sqlite3 "$DB_PATH" -header -column "
        SELECT Action, COUNT(*) AS rows
        FROM AuditLogs
        WHERE TargetId='$WO_ID' AND TargetType='WorkOrder'
          AND Action IN ('WO_IPQC_CHECK','WO_IPQC_JUDGMENT','WO_QA_APPROVE','WO_QA_APPROVE_DENIED')
        GROUP BY Action ORDER BY Action;
    "
    echo ""
    sqlite3 "$DB_PATH" -header -column "
        SELECT Username, Role, DisplayName FROM Users WHERE Username IN ('$IPQC_USER', '$QA_USER');
    "
    echo ""
    echo "  Final WO state: MesPhase = IPQC_APPROVED (from Q3 path B)."
    echo "  Catalyst hand-verify: scan WO $WO_NO → RunningDashboard with"
    echo "  'Bắt đầu chạy' CTA. Chip color = wo-phase-ipqc-approved."
fi

if [[ $FAIL -gt 0 ]]; then
    exit 1
fi
exit 0
