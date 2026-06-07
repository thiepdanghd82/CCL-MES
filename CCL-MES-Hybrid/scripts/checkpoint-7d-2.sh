#!/usr/bin/env bash
# P10.7d-2 — Catalyst checkpoint for the IPQC + QA Approval API
# surface + Q3 dual-sig. Proves the wire underneath the 7d-3 UI
# before Henry hand-verifies + closes that PR.
#
# ROLE POLICY (per §5.5.0 contract amendment):
#   IpqcSubmit  : Admin | QC                — PUT slot + POST judgment
#   QaApprove   : Admin | QC | Supervisor   — POST qa/approve
#
# SELF-SEED DISCIPLINE (Henry 2026-06-07 STOP gate):
#   This script does NOT assume any test user exists. It seeds 2
#   DISTINCT users via the admin /admin/accounts endpoint:
#     - ipqc-test-checkpoint (role QC) — submits IPQC judgment
#     - qa-test-checkpoint   (role QC) — QA-approves (dual-sig OK
#                                        because usernames differ)
#   Idempotent: a second run that finds the users already present
#   (409 conflict on POST) skips the seed step + logs in directly.
#   Both users carry the username prefix that purge-test-audit.sh
#   recognises for cleanup.
#
# CRITICAL FOCUS — Q3 dual-sig (both paths PROVEN end-to-end):
#   step 10: SAME user (ipqc-test-checkpoint) tries to QA-approve
#            → 422 qa.same_user_as_ipqc_submitter + WO_QA_APPROVE_DENIED
#            audit (NOT WO_QA_APPROVE) + phase stays QA_PENDING.
#   step 11: DISTINCT user (qa-test-checkpoint) QA-approves
#            → 200 + IPQC_APPROVED + QaApprovedBy stamped.
#
# Operator runs ONE command + supplies ONLY the WO number:
#   bash CCL-MES-Hybrid/scripts/checkpoint-7d-2.sh <WoNo> [--keep-alive]
#
# After:
#   bash scripts/purge-test-audit.sh   # preview cleanup
#   bash scripts/purge-test-audit.sh --commit   # operator-driven --commit
#
# R7.1 — [ctx] DB= + DB sha8 + WO + 2 seeded users printed at startup.
# R7.2 — self-managed API + --keep-alive for follow-on Catalyst verify.
# R7.3 — every wire probe (audit GET) has a TestServer mirror in
#        IpqcReviewControllerTests.Audit_visibility_via_wire_audit_log_endpoint.
# S12  — per-step [N/total] labels + final_summary always prints in EXIT
#        trap regardless of early bail; non-zero exit on any FAIL.

set -u
set +e

KEEP_ALIVE=0
WO_NO=""
for arg in "$@"; do
    case "$arg" in
        --keep-alive) KEEP_ALIVE=1 ;;
        --help|-h)
            echo "usage: bash scripts/checkpoint-7d-2.sh <WoNo> [--keep-alive]"
            echo ""
            echo "  Self-seeds 2 test users (ipqc-test-checkpoint + qa-test-checkpoint)"
            echo "  via the admin /admin/accounts endpoint. Idempotent — re-running"
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
    echo "usage: bash scripts/checkpoint-7d-2.sh <WoNo> [--keep-alive]"
    exit 64
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HYBRID_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$HYBRID_ROOT/.." && pwd)"
DB_PATH="$REPO_ROOT/data/ccl_mes.db"
API_BASE="${API_BASE:-http://127.0.0.1:5100}"
AUTO_BOOT_PID=""

# Seeded test users (purge-test-audit.sh recognises this prefix).
IPQC_USER="ipqc-test-checkpoint"
QA_USER="qa-test-checkpoint"
TEST_PASSWORD="P@ss!Checkpoint1"

DB_SHA8="(missing)"
[[ -f "$DB_PATH" ]] && DB_SHA8="$(shasum -a 256 "$DB_PATH" 2>/dev/null | awk '{print substr($1,1,8)}')"

echo "===================================================================="
echo "checkpoint-7d-2 — IPQC + QA wire verify (Q3 dual-sig: 2 paths)"
echo "[ctx] DB         = $DB_PATH"
echo "[ctx] DB sha8    = $DB_SHA8"
echo "[ctx] API base   = $API_BASE"
echo "[ctx] HEAD       = $(cd "$REPO_ROOT" && git rev-parse --short HEAD 2>/dev/null || echo '?')"
echo "[ctx] WO         = $WO_NO"
echo "[ctx] IPQC_USER  = $IPQC_USER  (role QC; self-seeded)"
echo "[ctx] QA_USER    = $QA_USER  (role QC; self-seeded; MUST differ from IPQC_USER)"
echo "[ctx] role policy: IpqcSubmit=Admin|QC, QaApprove=Admin|QC|Supervisor"
echo "===================================================================="

PASS=0
FAIL=0
SUMMARY=()
TOTAL_STEPS=13
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

# S12 — ALWAYS print SUMMARY on EXIT.
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
        echo "  ✓ CHECKPOINT PASSED — IPQC + QA wire + Q3 dual-sig both paths"
        echo "    proven. Catalyst hand-verify next: scan WO, see IPQC_APPROVED"
        echo "    phase + audit log shows 4× WO_IPQC_CHECK + WO_IPQC_JUDGMENT"
        echo "    + WO_QA_APPROVE_DENIED + WO_QA_APPROVE."
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
            echo "[keep-alive] log    : /tmp/checkpoint-7d-2-api.log"
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
        -d "{\"username\":\"$user\",\"password\":\"$TEST_PASSWORD\",\"deviceId\":\"checkpoint-7d-2\"}")
    echo "$rsp" | json_field "accessToken"
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
        dotnet run --no-build --no-launch-profile --urls "$API_BASE" > /tmp/checkpoint-7d-2-api.log 2>&1) &
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

# Boot probe — dual-sig flag MUST be on for this checkpoint to be meaningful.
if grep -q "OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER=off" /tmp/checkpoint-7d-2-api.log 2>/dev/null; then
    echo "[warn] Dual-sig flag is OFF — same-user 422 path will SKIP (flag=off)."
    echo "       Q3 contract requires flag=on in prod. Stop + fix .env if this is prod."
fi

# ── 2. Login admin ───────────────────────────────────────────────
ADMIN_RSP=$(curl -s -X POST "$API_BASE/api/v2/auth/login" \
    -H "Content-Type: application/json" \
    -d '{"username":"admin","password":"admin","deviceId":"checkpoint-7d-2"}')
ADMIN_TOKEN=$(echo "$ADMIN_RSP" | json_field "accessToken")
if [[ -z "$ADMIN_TOKEN" ]]; then
    record FAIL "login admin failed: $ADMIN_RSP"
    exit 1
fi
record PASS "login admin"
ADMIN_AUTH="Authorization: Bearer $ADMIN_TOKEN"

# ── 3. Self-seed IPQC + QA test users (idempotent via /admin/accounts) ─
seed_user() {
    local user="$1"
    local role="$2"
    local rsp
    rsp=$(curl -s -w "\nHTTP:%{http_code}" -X POST "$API_BASE/api/v2/admin/accounts" \
        -H "$ADMIN_AUTH" -H "Content-Type: application/json" \
        -d "{\"username\":\"$user\",\"displayName\":\"P10.7d-2 checkpoint test user\",\"role\":\"$role\",\"department\":\"QC\",\"password\":\"$TEST_PASSWORD\"}")
    local http_code
    http_code=$(echo "$rsp" | grep -oE 'HTTP:[0-9]+$' | cut -d: -f2)
    case "$http_code" in
        201|200) return 0 ;;       # freshly seeded
        409)     return 0 ;;       # already present — idempotent OK
        *)
            echo "  [seed] user=$user role=$role http=$http_code rsp=$(echo "$rsp" | head -1)"
            return 1
            ;;
    esac
}

SEED_OK=1
seed_user "$IPQC_USER" "QC" || SEED_OK=0
seed_user "$QA_USER"   "QC" || SEED_OK=0
if [[ $SEED_OK -eq 1 ]]; then
    record PASS "self-seed 2 test users (IPQC + QA, role QC, idempotent)"
else
    record FAIL "self-seed users failed — see preview above"
fi

# ── 4. Login both test users ─────────────────────────────────────
IPQC_TOKEN=$(login_user "$IPQC_USER")
QA_TOKEN=$(login_user "$QA_USER")
if [[ -n "$IPQC_TOKEN" && -n "$QA_TOKEN" ]]; then
    record PASS "login $IPQC_USER + $QA_USER (test passwords)"
else
    record FAIL "login failed: IPQC_TOKEN='${IPQC_TOKEN:+(set)}' QA_TOKEN='${QA_TOKEN:+(set)}'"
    exit 1
fi
IPQC_AUTH="Authorization: Bearer $IPQC_TOKEN"
QA_AUTH="Authorization: Bearer $QA_TOKEN"

# ── 5. Scrap picker probe (L17) ──────────────────────────────────
SCRAP_CNT=$(curl -s -H "$IPQC_AUTH" "$API_BASE/api/v2/reason-codes?kind=Scrap" \
    | python3 -c "import sys,json; print(len(json.load(sys.stdin)))" 2>/dev/null)
if [[ "${SCRAP_CNT:-0}" -ge 8 ]]; then
    record PASS "Scrap picker source (≥8 codes; got $SCRAP_CNT)"
else
    record FAIL "Scrap picker thin: $SCRAP_CNT"
fi

# ── Resolve WO + reset to IPQC_WAIT ──────────────────────────────
WO_ID=$(sqlite3 "$DB_PATH" "SELECT Id FROM WorkOrders WHERE WoNo='$WO_NO' LIMIT 1;" 2>/dev/null)
if [[ -z "$WO_ID" ]]; then
    record FAIL "WO $WO_NO not found"
    exit 1
fi
echo "[ctx] WO Id      = $WO_ID"

# Shim to IPQC_WAIT (the script handles a WO in any prior phase).
sqlite3 "$DB_PATH" "UPDATE WorkOrders SET MesPhase='IPQC_WAIT', CurrentStep='IpqcApproval', UpdatedAt=datetime('now'), UpdatedBy='checkpoint-7d-2' WHERE Id=$WO_ID;"
bash "$SCRIPT_DIR/reset-ipqc-for-wo.sh" --wo "$WO_NO" --commit > /tmp/checkpoint-7d-2-reset.log 2>&1
# Ensure check row exists (lazy materialise on first GET).
curl -s -H "$IPQC_AUTH" "$API_BASE/api/v2/work-orders/$WO_ID/ipqc" > /dev/null
NEW_PHASE=$(sqlite3 "$DB_PATH" "SELECT MesPhase FROM WorkOrders WHERE Id=$WO_ID;")
if [[ "$NEW_PHASE" == "IPQC_WAIT" ]]; then
    record PASS "reset WO to IPQC_WAIT + lazy-materialise check row"
else
    record FAIL "reset failed — phase is $NEW_PHASE"
fi
ETAG=$(etag_of "$WO_ID")

# ── 7. PUT all 4 slots ──────────────────────────────────────────
SLOT_OK=0
for ENTRY in 'material:Ok::' 'print-a:Ok::' 'print-b:Ng:SC-COLOR:lệch màu nhẹ' 'print-c:Ok::'; do
    SLOT="${ENTRY%%:*}"
    REST="${ENTRY#*:}"
    STATUS="${REST%%:*}"
    REST="${REST#*:}"
    NG_REASON="${REST%%:*}"
    NG_NOTE="${REST#*:}"
    if [[ "$STATUS" == "Ng" ]]; then
        BODY="{\"status\":\"Ng\",\"ngReasonCode\":\"$NG_REASON\",\"ngNote\":\"$NG_NOTE\"}"
    else
        BODY='{"status":"Ok"}'
    fi
    R=$(curl -s -X PUT "$API_BASE/api/v2/work-orders/$WO_ID/ipqc/$SLOT" \
        -H "$IPQC_AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
        -d "$BODY")
    OK=$(echo "$R" | json_field "ok")
    NEW=$(echo "$R" | json_field "eTag")
    if [[ "$OK" == "True" ]]; then
        ETAG="$NEW"
        SLOT_OK=$((SLOT_OK + 1))
    else
        echo "  [slot $SLOT] response: $R"
    fi
done
if [[ $SLOT_OK -eq 4 ]]; then
    record PASS "PUT all 4 slots OK/NG (Material Ok + PrintA Ok + PrintB Ng SC-COLOR + PrintC Ok)"
else
    record FAIL "PUT slots: only $SLOT_OK/4 accepted"
fi

# ── 8. POST /ipqc/judgment SpecialAccept (PrintB is NG) ──────────
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO_ID/ipqc/judgment" \
    -H "$IPQC_AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{"judgment":"SpecialAccept","specialAcceptReason":"Lô gấp giao trong ngày, ΔE 2.3 chấp nhận được"}')
PHASE=$(echo "$R" | json_field "mesPhase")
NEW=$(echo "$R" | json_field "eTag")
SUBMITTER=$(sqlite3 "$DB_PATH" "SELECT IpqcSubmittedBy FROM WoIpqcChecks WHERE WorkOrderId=$WO_ID;")
if [[ "$PHASE" == "QA_PENDING" && "$SUBMITTER" == "$IPQC_USER" ]]; then
    record PASS "POST /ipqc/judgment SpecialAccept by $IPQC_USER → QA_PENDING"
    ETAG="$NEW"
else
    record FAIL "/ipqc/judgment: phase=$PHASE submitter=$SUBMITTER"
fi

# ── 9. POST /qa/approve SAME user (Q3 dual-sig violation) ───────
R=$(curl -s -w "\nHTTP:%{http_code}" -X POST "$API_BASE/api/v2/work-orders/$WO_ID/qa/approve" \
    -H "$IPQC_AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{"outcome":"Approve"}')
HTTP_CODE=$(echo "$R" | grep -oE 'HTTP:[0-9]+$' | cut -d: -f2)
ERR_CODE=$(echo "$R" | head -1 | python3 -c "import sys,json; print(json.load(sys.stdin).get('code',''))" 2>/dev/null)
PHASE_AFTER=$(sqlite3 "$DB_PATH" "SELECT MesPhase FROM WorkOrders WHERE Id=$WO_ID;")
if [[ "$HTTP_CODE" == "422" && "$ERR_CODE" == "qa.same_user_as_ipqc_submitter" && "$PHASE_AFTER" == "QA_PENDING" ]]; then
    record PASS "POST /qa/approve SAME user ($IPQC_USER) → 422 qa.same_user_as_ipqc_submitter (phase unchanged)"
else
    record FAIL "Same-user path: http=$HTTP_CODE err=$ERR_CODE phase=$PHASE_AFTER (expected 422 + same_user code + QA_PENDING)"
fi

# ── 10. POST /qa/approve DISTINCT user (Q3 happy path) ──────────
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO_ID/qa/approve" \
    -H "$QA_AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{"outcome":"Approve","qaReason":"Lô đặc biệt — chấp nhận sản xuất"}')
PHASE=$(echo "$R" | json_field "mesPhase")
QA_APPROVED=$(sqlite3 "$DB_PATH" "SELECT QaApprovedBy FROM WoIpqcChecks WHERE WorkOrderId=$WO_ID;")
if [[ "$PHASE" == "IPQC_APPROVED" && "$QA_APPROVED" == "$QA_USER" ]]; then
    record PASS "POST /qa/approve DISTINCT user ($QA_USER) → IPQC_APPROVED + QaApprovedBy stamped"
else
    record FAIL "Distinct-user happy path: phase=$PHASE qa_approver=$QA_APPROVED"
fi

# ── 11. Audit wire-mirror (R7.3) ─────────────────────────────────
AUDIT_MISS=()
for ACTION in WO_IPQC_CHECK WO_IPQC_JUDGMENT WO_QA_APPROVE_DENIED WO_QA_APPROVE; do
    AUDIT=$(curl -s -H "$ADMIN_AUTH" "$API_BASE/api/v2/audit/log?action=$ACTION&page=1&pageSize=50")
    if ! echo "$AUDIT" | grep -q "\"targetId\":\"$WO_ID\""; then
        AUDIT_MISS+=("$ACTION")
    fi
done
if [[ ${#AUDIT_MISS[@]} -eq 0 ]]; then
    record PASS "audit wire-mirror (4/4): WO_IPQC_CHECK + WO_IPQC_JUDGMENT + WO_QA_APPROVE_DENIED + WO_QA_APPROVE visible"
else
    record FAIL "audit wire missing: ${AUDIT_MISS[*]}"
fi

# ── KEEP-ALIVE forensic dump ────────────────────────────────────
if [[ $KEEP_ALIVE -eq 1 ]]; then
    echo ""
    echo "── KEEP-ALIVE: forensic state for Catalyst hand-verify ─────────"
    sqlite3 "$DB_PATH" -header -column "
        SELECT WoNo, MesPhase, CurrentStep FROM WorkOrders WHERE Id=$WO_ID;
    "
    echo ""
    sqlite3 "$DB_PATH" -header -column "
        SELECT MaterialStatus, PrintAStatus, PrintBStatus, PrintCStatus,
               Judgment, IpqcSubmittedBy, QaOutcome, QaApprovedBy
        FROM WoIpqcChecks WHERE WorkOrderId=$WO_ID;
    "
    echo ""
    sqlite3 "$DB_PATH" -header -column "
        SELECT Username, Role FROM Users WHERE Username IN ('$IPQC_USER', '$QA_USER');
    "
    echo ""
    echo "  Henry next: scan WO $WO_NO in Catalyst → expect MesPhase = IPQC_APPROVED."
    echo "  IPQC dashboard ships in 7d-3 — until then the 7c L19 deferred"
    echo "  placeholder will render on this phase + the chip shows IPQC_APPROVED."
fi

if [[ $FAIL -gt 0 ]]; then
    exit 1
fi
exit 0
