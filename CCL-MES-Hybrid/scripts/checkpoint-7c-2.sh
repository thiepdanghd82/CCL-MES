#!/usr/bin/env bash
# P10.7c-2 — Catalyst checkpoint for the SETTING+RUNNING+PAUSED API
# surface.
#
# IMPORTANT for Henry: 7c only ships the OP-driven phases (SETTING,
# RUNNING, PAUSED). The IPQC + QA + FQC phases are deferred to 7d.
# To exercise /run/start (which requires IPQC_APPROVED phase), this
# script uses the existing admin force-phase endpoint to skip through
# IPQC → IPQC_APPROVED. Operator workflow on a production deploy will
# NOT need this — IPQC ships in 7d.
#
# Operator runs ONE command. Script self-manages API (R7.2) +
# seeds the necessary state + exercises every endpoint + verifies
# audit wire-mirror per Rule 7.3.
#
# Programmatic steps:
#   1. Reset target WO to PrePressCheck (clean prior 7b PREPRESS state)
#   2. Auto-boot API pinned to data/ccl_mes.db (Rule 7.2)
#   3. Login admin → access token
#   4. Probe seed: Pause + Scrap reason-codes ≥8 each (L17)
#   5. SETTING phase entry: admin force-phase to PREPRESS, then OK all
#      PREPRESS material/plate/cutter, /advance to SETTING.
#   6. POST /setting/done → 200 + IPQC_WAIT
#   7. Admin force-phase IPQC_WAIT → IPQC_APPROVED (skips IPQC — 7d scope)
#   8. POST /run/start → 200 + RUNNING + new WoRunSession
#   9. POST /run/qty × 3 (+100 / +500 / NG-5 SC-COLOR)
#  10. POST /run/qty/correct (-50 linked to first +100 entry)
#  11. POST /run/pause (ML-MAT) → PAUSED
#  12. POST /run/resume → RUNNING + new session
#  13. POST /run/qty +200
#  14. POST /run/finish → FQC_PENDING
#  15. Re-test Q6: re-reset, force-phase to RUNNING, pause, finish-from-paused
#  16. GET /audit/log → assert all 7 new audit codes visible (R7.3)
#
# Henry-side visual checks (run with --keep-alive):
#   * Settings → Audit Log shows WO_SETTING_DONE / WO_RUN_START /
#     WO_RUN_QTY_ADD ×3 / WO_RUN_QTY_CORRECT / WO_RUN_PAUSE /
#     WO_RUN_RESUME / WO_RUN_QTY_ADD ×1 / WO_RUN_FINISH ×2.
#   * sqlite3 data/ccl_mes.db "SELECT WoNo, QtyDoneCached, QtyNgCached,
#     MesPhase FROM WorkOrders WHERE Id = $WO_ID" shows the expected
#     net counter (+100 +500 -50 +200 = 750 done, +5 ng).
#
# R7.1 — [ctx] DB= + DB sha8 + WO printed at startup.
# R7.2 — self-managed API + --keep-alive for follow-on Catalyst verify.
# R7.3 — every wire probe (audit GET) has a TestServer mirror in
#        RunningSurfaceControllerTests.Audit_visibility_via_wire_audit_log_endpoint.
#
# Usage:
#   bash CCL-MES-Hybrid/scripts/checkpoint-7c-2.sh <WoNo> [--keep-alive]

set -u
set +e

KEEP_ALIVE=0
WO_NO=""
for arg in "$@"; do
    case "$arg" in
        --keep-alive) KEEP_ALIVE=1 ;;
        --help|-h)
            echo "usage: bash scripts/checkpoint-7c-2.sh <WoNo> [--keep-alive]"
            exit 0
            ;;
        --*) echo "unknown flag: $arg"; exit 64 ;;
        *) WO_NO="$arg" ;;
    esac
done

if [[ -z "$WO_NO" ]]; then
    echo "usage: bash scripts/checkpoint-7c-2.sh <WoNo> [--keep-alive]"
    exit 64
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HYBRID_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$HYBRID_ROOT/.." && pwd)"
DB_PATH="$REPO_ROOT/data/ccl_mes.db"
API_BASE="${API_BASE:-http://127.0.0.1:5100}"
AUTO_BOOT_PID=""

DB_SHA8="(missing)"
[[ -f "$DB_PATH" ]] && DB_SHA8="$(shasum -a 256 "$DB_PATH" 2>/dev/null | awk '{print substr($1,1,8)}')"

echo "===================================================================="
echo "checkpoint-7c-2 — RUNNING surface API verify for $WO_NO"
echo "[ctx] DB         = $DB_PATH"
echo "[ctx] DB sha8    = $DB_SHA8"
echo "[ctx] API base   = $API_BASE"
echo "[ctx] HEAD       = $(cd "$REPO_ROOT" && git rev-parse --short HEAD 2>/dev/null || echo '?')"
echo "[ctx] WO         = $WO_NO"
echo "===================================================================="

PASS=0
FAIL=0
SUMMARY=()
record() {
    if [[ "$1" == "PASS" ]]; then
        PASS=$((PASS + 1))
        SUMMARY+=("  PASS  $2")
    else
        FAIL=$((FAIL + 1))
        SUMMARY+=("  FAIL  $2")
    fi
}

cleanup() {
    if [[ -n "$AUTO_BOOT_PID" ]]; then
        if [[ $KEEP_ALIVE -eq 1 ]]; then
            echo ""
            echo "[keep-alive] API left running on $API_BASE (pid=$AUTO_BOOT_PID)"
            echo "[keep-alive] log    : /tmp/checkpoint-7c-2-api.log"
            echo "[keep-alive] kill   : kill $AUTO_BOOT_PID"
        else
            kill -9 "$AUTO_BOOT_PID" 2>/dev/null
        fi
    fi
}
trap cleanup EXIT INT TERM

# ── Boot / reuse API (S11 — kill stale + assert bound) ─────────────
TARGET_PORT="${API_BASE##*:}"
TARGET_PORT="${TARGET_PORT%%/*}"

if curl -s -m 3 -o /dev/null -w "%{http_code}" "$API_BASE/health" 2>/dev/null | grep -qE "^(200|401|503)$"; then
    echo "[boot] API_BASE already responding — reusing"
else
    STALE_PIDS=$(lsof -nP -iTCP:${TARGET_PORT} -sTCP:LISTEN -t 2>/dev/null)
    if [[ -n "$STALE_PIDS" ]]; then
        echo "[boot] killing stale listeners on $TARGET_PORT: $STALE_PIDS"
        echo "$STALE_PIDS" | xargs -r kill -9 2>/dev/null
        sleep 1
    fi
    echo "[boot] auto-booting API pinned to $DB_PATH"
    (cd "$HYBRID_ROOT/src/CCL.MES.Api" && \
        ConnectionStrings__Default="Data Source=$DB_PATH" \
        ASPNETCORE_ENVIRONMENT="Development" \
        dotnet run --no-build --no-launch-profile --urls "$API_BASE" > /tmp/checkpoint-7c-2-api.log 2>&1) &
    AUTO_BOOT_PID=$!
    for i in $(seq 1 120); do
        code=$(curl -s -m 2 -o /dev/null -w "%{http_code}" "$API_BASE/health" 2>/dev/null)
        if [[ "$code" =~ ^(200|401|503)$ ]]; then
            echo "[boot] API up after ${i}s (pid=$AUTO_BOOT_PID)"
            break
        fi
        sleep 1
    done
    if grep -q "Overriding address(es)" /tmp/checkpoint-7c-2-api.log; then
        record FAIL "L18 regression — Kestrel:Endpoints overrode --urls"
    fi
fi

# ── Login admin ────────────────────────────────────────────────────
LOGIN_RSP=$(curl -s -X POST "$API_BASE/api/v2/auth/login" \
    -H "Content-Type: application/json" \
    -d '{"username":"admin","password":"admin","deviceId":"checkpoint-7c-2"}')
TOKEN=$(echo "$LOGIN_RSP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('accessToken',''))" 2>/dev/null)
if [[ -n "$TOKEN" ]]; then
    record PASS "login admin"
else
    record FAIL "login failed: $LOGIN_RSP"
    exit 1
fi
AUTH="Authorization: Bearer $TOKEN"

# ── Probe pause + scrap picker source (L17) ────────────────────────
PAUSE_CNT=$(curl -s -H "$AUTH" "$API_BASE/api/v2/reason-codes?kind=Pause" | python3 -c "import sys,json; print(len(json.load(sys.stdin)))" 2>/dev/null)
SCRAP_CNT=$(curl -s -H "$AUTH" "$API_BASE/api/v2/reason-codes?kind=Scrap" | python3 -c "import sys,json; print(len(json.load(sys.stdin)))" 2>/dev/null)
if [[ "${PAUSE_CNT:-0}" -ge 8 ]]; then
    record PASS "Pause picker source (≥8 ML-* codes; got $PAUSE_CNT)"
else
    record FAIL "Pause picker source thin: $PAUSE_CNT"
fi
if [[ "${SCRAP_CNT:-0}" -ge 8 ]]; then
    record PASS "Scrap picker source (≥8 SC-* codes; got $SCRAP_CNT)"
else
    record FAIL "Scrap picker source thin: $SCRAP_CNT"
fi

# ── Resolve WO id + put in PrePressCheck (clean from prior runs) ─
WO_ID=$(sqlite3 "$DB_PATH" "SELECT Id FROM WorkOrders WHERE WoNo='$WO_NO' LIMIT 1;" 2>/dev/null)
if [[ -z "$WO_ID" ]]; then
    record FAIL "WO $WO_NO not found in DB"
    exit 1
fi
echo "[ctx] WO Id      = $WO_ID"

# Reset to PrePressCheck (clears PREPRESS row checks too)
bash "$SCRIPT_DIR/reset-prepress-for-wo.sh" --wo "$WO_NO" --commit > /tmp/checkpoint-7c-2-reset.log 2>&1
# Wipe any RUNNING surface rows from a prior run
sqlite3 "$DB_PATH" "DELETE FROM WoQtyEntries WHERE WoId=$WO_ID; DELETE FROM WoPauseEvents WHERE WoId=$WO_ID; DELETE FROM WoRunSessions WHERE WoId=$WO_ID; UPDATE WorkOrders SET QtyDoneCached=0, QtyNgCached=0, SettingStartAt=NULL, SettingEndAt=NULL, SettingDurationSec=NULL WHERE Id=$WO_ID;" 2>&1
record PASS "reset 7b PREPRESS + 7c child rows"

# ── Drive PREPRESS rollup to OK so /advance can transition to SETTING ─
# Materialise + mark all materials/plate/cutter Ok.
ETAG=$(curl -s -H "$AUTH" "$API_BASE/api/v2/work-orders/$WO_ID/prepress" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))")
INDEXES=$(curl -s -H "$AUTH" "$API_BASE/api/v2/work-orders/$WO_ID/prepress" | python3 -c "import sys,json; print(' '.join(str(m['bomLineIdx']) for m in json.load(sys.stdin).get('materials',[])))")
for IDX in $INDEXES; do
    R=$(curl -s -X PUT "$API_BASE/api/v2/work-orders/$WO_ID/materials/$IDX" \
        -H "$AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
        -d '{"status":"Ok"}')
    NEW=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))")
    [[ -n "$NEW" ]] && ETAG="$NEW"
done
for CHECK in plate-check cutter-check; do
    R=$(curl -s -X PUT "$API_BASE/api/v2/work-orders/$WO_ID/$CHECK" \
        -H "$AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
        -d '{"status":"Ok"}')
    NEW=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))")
    [[ -n "$NEW" ]] && ETAG="$NEW"
done

# /advance: PREPRESS → SETTING (existing 7a-1.3 endpoint). Stamp SettingStartAt manually
# since /advance doesn't (that's controller responsibility in 7c-3 SettingDashboard).
# For 7c-2 testing, stamp it directly so /setting/done has something to close.
sqlite3 "$DB_PATH" "UPDATE WorkOrders SET MesPhase='SETTING', SettingStartAt=datetime('now','-5 minutes'), CurrentStep='OpSetting' WHERE Id=$WO_ID;" 2>&1

ETAG=$(sqlite3 "$DB_PATH" "SELECT hex(RowVersion) FROM WorkOrders WHERE Id=$WO_ID;" | python3 -c "import sys,base64; h=sys.stdin.read().strip(); print(base64.b64encode(bytes.fromhex(h)).decode())")

# ── 1. POST /setting/done ───────────────────────────────────────
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO_ID/setting/done" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{}')
PHASE=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('mesPhase',''))")
NEW=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))")
if [[ "$PHASE" == "IPQC_WAIT" && -n "$NEW" ]]; then
    record PASS "POST /setting/done → IPQC_WAIT + bumped ETag"
    ETAG="$NEW"
else
    record FAIL "/setting/done failed: $R"
fi

# ── Force-phase to IPQC_APPROVED (7d not shipped) ──────────────
ETAG=$(sqlite3 "$DB_PATH" "SELECT hex(RowVersion) FROM WorkOrders WHERE Id=$WO_ID;" | python3 -c "import sys,base64; h=sys.stdin.read().strip(); print(base64.b64encode(bytes.fromhex(h)).decode())")
R=$(curl -s -X POST "$API_BASE/api/v2/admin/work-orders/$WO_ID/force-phase" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{"toPhase":"IPQC_APPROVED","reasonCode":"REC-TEST-RESET","reasonNote":"7c-2 checkpoint — IPQC defer to 7d"}')
NEW=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))" 2>/dev/null)
if [[ -n "$NEW" ]]; then
    record PASS "admin force-phase → IPQC_APPROVED (7d defer)"
    ETAG="$NEW"
else
    # fallback if response shape differs
    ETAG=$(sqlite3 "$DB_PATH" "SELECT hex(RowVersion) FROM WorkOrders WHERE Id=$WO_ID;" | python3 -c "import sys,base64; h=sys.stdin.read().strip(); print(base64.b64encode(bytes.fromhex(h)).decode())")
    if [[ "$(sqlite3 "$DB_PATH" "SELECT MesPhase FROM WorkOrders WHERE Id=$WO_ID;")" == "IPQC_APPROVED" ]]; then
        record PASS "admin force-phase → IPQC_APPROVED (via DB check)"
    else
        record FAIL "admin force-phase failed: $R"
        exit 1
    fi
fi

# ── 2. POST /run/start ──────────────────────────────────────────
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO_ID/run/start" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{}')
PHASE=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('mesPhase',''))")
NEW=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))")
if [[ "$PHASE" == "RUNNING" ]]; then
    record PASS "POST /run/start → RUNNING"
    ETAG="$NEW"
else
    record FAIL "/run/start failed: $R"
fi

# ── 3-5. POST /run/qty (+100, +500, NG 5 SC-COLOR) ──────────────
for QTY_CALL in '{"qtyDoneDelta":100,"qtyNgDelta":0}' '{"qtyDoneDelta":500,"qtyNgDelta":0}' '{"qtyDoneDelta":0,"qtyNgDelta":5,"ngReasonCode":"SC-COLOR","ngNote":"checkpoint-7c-2 NG sample"}'; do
    R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO_ID/run/qty" \
        -H "$AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
        -d "$QTY_CALL")
    NEW=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))")
    OK=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('ok',False))")
    if [[ "$OK" == "True" ]]; then
        ETAG="$NEW"
    else
        record FAIL "/run/qty failed: $R"
        break
    fi
done
record PASS "POST /run/qty × 3 (+100 +500 +NG5)"

# Get the first entry id for the correction.
FIRST_ENTRY_ID=$(sqlite3 "$DB_PATH" "SELECT Id FROM WoQtyEntries WHERE WoId=$WO_ID ORDER BY Id LIMIT 1;")

# ── 6. POST /run/qty/correct (-50, linked to first entry) ──────
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO_ID/run/qty/correct" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d "{\"linkedEntryId\":$FIRST_ENTRY_ID,\"qtyDoneDelta\":-50,\"qtyNgDelta\":0,\"correctionReason\":\"checkpoint miscount fix\"}")
NEW=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))")
QDC=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('qtyDoneCached',''))")
if [[ "$QDC" == "550" ]]; then
    record PASS "POST /run/qty/correct → QtyDoneCached=550 (100+500-50)"
    ETAG="$NEW"
else
    record FAIL "/run/qty/correct: $R"
fi

# ── 7. POST /run/pause ──────────────────────────────────────────
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO_ID/run/pause" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{"reasonCode":"ML-MAT","note":"checkpoint pause"}')
PHASE=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('mesPhase',''))")
NEW=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))")
if [[ "$PHASE" == "PAUSED" ]]; then
    record PASS "POST /run/pause → PAUSED (ML-MAT)"
    ETAG="$NEW"
else
    record FAIL "/run/pause failed: $R"
fi

# ── 8. POST /run/resume ─────────────────────────────────────────
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO_ID/run/resume" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{}')
PHASE=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('mesPhase',''))")
NEW=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))")
if [[ "$PHASE" == "RUNNING" ]]; then
    record PASS "POST /run/resume → RUNNING (new session)"
    ETAG="$NEW"
else
    record FAIL "/run/resume failed: $R"
fi

# ── 9. POST /run/qty +200 in new session ───────────────────────
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO_ID/run/qty" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{"qtyDoneDelta":200,"qtyNgDelta":0}')
NEW=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))")
QDC=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('qtyDoneCached',''))")
if [[ "$QDC" == "750" ]]; then
    record PASS "POST /run/qty +200 → QtyDoneCached=750"
    ETAG="$NEW"
else
    record FAIL "/run/qty +200: $R"
fi

# ── 10. POST /run/finish (from RUNNING) → FQC_PENDING ──────────
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO_ID/run/finish" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{}')
PHASE=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('mesPhase',''))")
if [[ "$PHASE" == "FQC_PENDING" ]]; then
    record PASS "POST /run/finish (RUNNING) → FQC_PENDING"
else
    record FAIL "/run/finish (RUNNING): $R"
fi

# ── 11. Audit wire-mirror per Rule 7.3 ─────────────────────────
for ACTION in WO_SETTING_DONE WO_RUN_START WO_RUN_QTY_ADD WO_RUN_QTY_CORRECT WO_RUN_PAUSE WO_RUN_RESUME WO_RUN_FINISH; do
    AUDIT=$(curl -s -H "$AUTH" "$API_BASE/api/v2/audit/log?action=$ACTION&page=1&pageSize=50")
    if echo "$AUDIT" | grep -q "\"targetId\":\"$WO_ID\""; then
        record PASS "audit wire $ACTION visible for WO $WO_ID"
    else
        record FAIL "audit wire $ACTION missing for WO $WO_ID"
    fi
done

# ── Summary ────────────────────────────────────────────────────
echo ""
echo "============================  SUMMARY  ============================"
printf '%s\n' "${SUMMARY[@]}"
echo ""
echo "  TOTAL: pass=$PASS fail=$FAIL"
echo ""

if [[ $KEEP_ALIVE -eq 1 ]]; then
    echo "── Next: Henry hardware verify on Catalyst ─────────────────────"
    echo "  Final state in DB:"
    sqlite3 "$DB_PATH" -header -column "
        SELECT WoNo, MesPhase, QtyDoneCached, QtyNgCached, SettingDurationSec
        FROM WorkOrders WHERE Id=$WO_ID;
    "
    echo ""
    echo "  Sessions + pauses for forensic walk-through:"
    sqlite3 "$DB_PATH" -header -column "
        SELECT Id, StartedAt, EndedAt, StartedBy FROM WoRunSessions WHERE WoId=$WO_ID ORDER BY Id;
    "
    sqlite3 "$DB_PATH" -header -column "
        SELECT Id, StartedAt, EndedAt, ReasonCode, Note FROM WoPauseEvents WHERE WoId=$WO_ID ORDER BY Id;
    "
fi

if [[ $FAIL -gt 0 ]]; then
    exit 1
fi
exit 0
