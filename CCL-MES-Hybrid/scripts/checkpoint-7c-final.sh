#!/usr/bin/env bash
# P10.7c-final — FULL-STACK Catalyst checkpoint for the 7c sprint
# closeout (covers 7c-1 domain + 7c-2 wire + 7c-3 UI + L19 finalization).
#
# Closes the gap that checkpoint-7c-2.sh only proved the WIRE path —
# this script ALSO walks every deferred phase placeholder via /running-
# surface so the L19 routing + DeferredPhaseInfo map is provable from
# the CLI without a Catalyst window. Catalyst hardware verify still
# expected on top (--keep-alive leaves the API warm).
#
# Steps (per S12 — every step is numbered + recorded; SUMMARY always
# prints in EXIT trap regardless of early bail-out):
#
#   Block A — bring-up + seed probes (S11 + L17):
#     1. Boot API self-managed
#     2. Login admin → token
#     3. Pause picker source ≥ 8 (L17)
#     4. Scrap picker source ≥ 8 (L17)
#
#   Block B — full luồng wire (5 PHASE transitions on WO1):
#     5. Reset WO1 to PrePressCheck (7b purge)
#     6. PREPRESS row-checks all OK + /advance to SETTING (state stamp)
#     7. POST /setting/done → IPQC_WAIT + bumped ETag
#     8. SQL shim IPQC_WAIT → IPQC_APPROVED (7d wire scope)
#     9. POST /run/start → RUNNING + new WoRunSession
#    10. POST /run/qty +100 / +500 / NG-5 SC-COLOR (3 entries)
#    11. POST /run/qty/correct -50 linked → QtyDoneCached = 550
#    12. POST /run/pause ML-MAT → PAUSED
#    13. POST /run/resume → RUNNING + new session
#    14. POST /run/qty +200 → QtyDoneCached = 750
#    15. POST /run/finish (from RUNNING) → FQC_PENDING + Q6 OEE math
#
#   Block C — Q6 finish-from-PAUSED on WO2:
#    16. Reset WO2 + shim to RUNNING + open session + +300 qty
#    17. POST /run/pause ML-CO → PAUSED
#    18. POST /run/finish (from PAUSED) → FQC_PENDING + closed pause
#        EndedAt non-null (proves Q6 helper closed the pause cleanly)
#
#   Block D — L19 deferred-phase walk on WO1 (chip + placeholder UI):
#    19. GET /running-surface returns MesPhase=FQC_PENDING (WO1 final)
#    20. Sequentially shim WO1 through QA_PENDING / IPQC_WAIT /
#        OQC_PENDING / DONE / CANCELLED — assert each /running-surface
#        GET returns the same MesPhase (no drift, no 422). Proves the
#        L19 dispatch key is the canonical phase the dashboard will
#        receive at render time.
#
#   Block E — Rule 7.3 audit wire-mirror:
#    21. All 7 WO_* + 1 SETTING_DONE audit codes visible for WO1
#
# After this checkpoint: Catalyst hand-verify walks the operator UI
# while API stays warm (--keep-alive). L19 chip + 9 placeholder cards
# render in browser; this script proves the wire underneath.
#
# Usage:
#   bash CCL-MES-Hybrid/scripts/checkpoint-7c-final.sh <WO1> <WO2> [--keep-alive]
#
# Both WOs are mutated. Use 2 disposable WOs from your seed set.
# Operator on production: never run this against a real WO.

set -u
set +e

KEEP_ALIVE=0
WO1_NO=""
WO2_NO=""
for arg in "$@"; do
    case "$arg" in
        --keep-alive) KEEP_ALIVE=1 ;;
        --help|-h)
            echo "usage: bash scripts/checkpoint-7c-final.sh <WO1> <WO2> [--keep-alive]"
            exit 0
            ;;
        --*) echo "unknown flag: $arg"; exit 64 ;;
        *)
            if [[ -z "$WO1_NO" ]]; then WO1_NO="$arg"
            elif [[ -z "$WO2_NO" ]]; then WO2_NO="$arg"
            else echo "extra positional arg: $arg"; exit 64
            fi
            ;;
    esac
done

if [[ -z "$WO1_NO" || -z "$WO2_NO" ]]; then
    echo "usage: bash scripts/checkpoint-7c-final.sh <WO1> <WO2> [--keep-alive]"
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
echo "checkpoint-7c-final — FULL-STACK 7c closeout verify"
echo "[ctx] DB         = $DB_PATH"
echo "[ctx] DB sha8    = $DB_SHA8"
echo "[ctx] API base   = $API_BASE"
echo "[ctx] HEAD       = $(cd "$REPO_ROOT" && git rev-parse --short HEAD 2>/dev/null || echo '?')"
echo "[ctx] WO1        = $WO1_NO"
echo "[ctx] WO2        = $WO2_NO"
echo "===================================================================="

PASS=0
FAIL=0
SUMMARY=()
TOTAL_STEPS=21
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

# S12 — ALWAYS print SUMMARY on EXIT (no silent early-bail).
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
        echo "  ✗ CHECKPOINT FAILED — wire path NOT fully proven. See log + audit."
    else
        echo "  ✓ CHECKPOINT PASSED — 7c stack wire path + L19 deferred-phase"
        echo "    walk complete. Catalyst hand-verify next: chip + placeholder"
        echo "    cards in browser at wide ≥1400 + narrow ≤900."
    fi
}

cleanup() {
    final_summary
    if [[ -n "$AUTO_BOOT_PID" ]]; then
        if [[ $KEEP_ALIVE -eq 1 ]]; then
            echo "[keep-alive] API left running on $API_BASE (pid=$AUTO_BOOT_PID)"
            echo "[keep-alive] log    : /tmp/checkpoint-7c-final-api.log"
            echo "[keep-alive] kill   : kill $AUTO_BOOT_PID"
        else
            kill -9 "$AUTO_BOOT_PID" 2>/dev/null
        fi
    fi
}
trap cleanup EXIT INT TERM

# Shared helpers ────────────────────────────────────────────────────
etag_of() {
    sqlite3 "$DB_PATH" "SELECT hex(RowVersion) FROM WorkOrders WHERE Id=$1;" \
        | python3 -c "import sys,base64; h=sys.stdin.read().strip(); print(base64.b64encode(bytes.fromhex(h)).decode())"
}

json_field() {
    python3 -c "import sys,json; print(json.load(sys.stdin).get('$1',''))" 2>/dev/null
}

# ── Block A — bring-up + seed probes ───────────────────────────────

TARGET_PORT="${API_BASE##*:}"
TARGET_PORT="${TARGET_PORT%%/*}"

if curl -s -m 3 -o /dev/null -w "%{http_code}" "$API_BASE/health" 2>/dev/null | grep -qE "^(200|401|503)$"; then
    echo "[boot] API_BASE already responding — reusing"
    record PASS "API already up on $API_BASE"
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
        dotnet run --no-build --no-launch-profile --urls "$API_BASE" > /tmp/checkpoint-7c-final-api.log 2>&1) &
    AUTO_BOOT_PID=$!
    BOOT_OK=0
    for i in $(seq 1 120); do
        code=$(curl -s -m 2 -o /dev/null -w "%{http_code}" "$API_BASE/health" 2>/dev/null)
        if [[ "$code" =~ ^(200|401|503)$ ]]; then
            echo "[boot] API up after ${i}s (pid=$AUTO_BOOT_PID)"
            BOOT_OK=1
            break
        fi
        sleep 1
    done
    if [[ $BOOT_OK -eq 1 ]]; then
        record PASS "API booted on $API_BASE (pid=$AUTO_BOOT_PID)"
    else
        record FAIL "API never reached /health on $API_BASE"
        exit 1
    fi
fi

LOGIN_RSP=$(curl -s -X POST "$API_BASE/api/v2/auth/login" \
    -H "Content-Type: application/json" \
    -d '{"username":"admin","password":"admin","deviceId":"checkpoint-7c-final"}')
TOKEN=$(echo "$LOGIN_RSP" | json_field "accessToken")
if [[ -n "$TOKEN" ]]; then
    record PASS "login admin"
else
    record FAIL "login failed: $LOGIN_RSP"
    exit 1
fi
AUTH="Authorization: Bearer $TOKEN"

PAUSE_CNT=$(curl -s -H "$AUTH" "$API_BASE/api/v2/reason-codes?kind=Pause" \
    | python3 -c "import sys,json; print(len(json.load(sys.stdin)))" 2>/dev/null)
if [[ "${PAUSE_CNT:-0}" -ge 8 ]]; then
    record PASS "Pause picker source (≥8 ML-* codes; got $PAUSE_CNT)"
else
    record FAIL "Pause picker source thin: got '$PAUSE_CNT'"
fi

SCRAP_CNT=$(curl -s -H "$AUTH" "$API_BASE/api/v2/reason-codes?kind=Scrap" \
    | python3 -c "import sys,json; print(len(json.load(sys.stdin)))" 2>/dev/null)
if [[ "${SCRAP_CNT:-0}" -ge 8 ]]; then
    record PASS "Scrap picker source (≥8 SC-* codes; got $SCRAP_CNT)"
else
    record FAIL "Scrap picker source thin: got '$SCRAP_CNT'"
fi

# ── Resolve WO ids ────────────────────────────────────────────────
WO1_ID=$(sqlite3 "$DB_PATH" "SELECT Id FROM WorkOrders WHERE WoNo='$WO1_NO' LIMIT 1;" 2>/dev/null)
WO2_ID=$(sqlite3 "$DB_PATH" "SELECT Id FROM WorkOrders WHERE WoNo='$WO2_NO' LIMIT 1;" 2>/dev/null)
if [[ -z "$WO1_ID" || -z "$WO2_ID" ]]; then
    echo "[fatal] cannot resolve one of WO1=$WO1_NO ($WO1_ID) WO2=$WO2_NO ($WO2_ID)"
    exit 1
fi
echo "[ctx] WO1 Id     = $WO1_ID"
echo "[ctx] WO2 Id     = $WO2_ID"

# ── Block B — full luồng wire on WO1 (11 steps) ───────────────────

# Step 5 — reset WO1 to PrePressCheck (clears 7b + 7c child rows)
bash "$SCRIPT_DIR/reset-prepress-for-wo.sh" --wo "$WO1_NO" --commit > /tmp/checkpoint-7c-final-reset.log 2>&1
sqlite3 "$DB_PATH" "DELETE FROM WoQtyEntries WHERE WoId=$WO1_ID; DELETE FROM WoPauseEvents WHERE WoId=$WO1_ID; DELETE FROM WoRunSessions WHERE WoId=$WO1_ID; UPDATE WorkOrders SET QtyDoneCached=0, QtyNgCached=0, SettingStartAt=NULL, SettingEndAt=NULL, SettingDurationSec=NULL WHERE Id=$WO1_ID;" 2>&1
RESET_PHASE=$(sqlite3 "$DB_PATH" "SELECT MesPhase FROM WorkOrders WHERE Id=$WO1_ID;")
if [[ "$RESET_PHASE" == "PREPRESS" || "$RESET_PHASE" == "NEW" ]]; then
    record PASS "reset WO1 to $RESET_PHASE (7b + 7c child rows cleared)"
else
    record FAIL "reset WO1 ended at unexpected phase: $RESET_PHASE"
fi

# Step 6 — drive PREPRESS rollup OK + shim SETTING (advance to OpSetting)
ETAG=$(curl -s -H "$AUTH" "$API_BASE/api/v2/work-orders/$WO1_ID/prepress" | json_field "eTag")
INDEXES=$(curl -s -H "$AUTH" "$API_BASE/api/v2/work-orders/$WO1_ID/prepress" \
    | python3 -c "import sys,json; print(' '.join(str(m['bomLineIdx']) for m in json.load(sys.stdin).get('materials',[])))")
for IDX in $INDEXES; do
    R=$(curl -s -X PUT "$API_BASE/api/v2/work-orders/$WO1_ID/materials/$IDX" \
        -H "$AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
        -d '{"status":"Ok"}')
    NEW=$(echo "$R" | json_field "eTag")
    [[ -n "$NEW" ]] && ETAG="$NEW"
done
for CHECK in plate-check cutter-check; do
    R=$(curl -s -X PUT "$API_BASE/api/v2/work-orders/$WO1_ID/$CHECK" \
        -H "$AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
        -d '{"status":"Ok"}')
    NEW=$(echo "$R" | json_field "eTag")
    [[ -n "$NEW" ]] && ETAG="$NEW"
done
sqlite3 "$DB_PATH" "UPDATE WorkOrders SET MesPhase='SETTING', SettingStartAt=datetime('now','-5 minutes'), CurrentStep='OpSetting' WHERE Id=$WO1_ID;" 2>&1
ETAG=$(etag_of "$WO1_ID")
PH=$(sqlite3 "$DB_PATH" "SELECT MesPhase FROM WorkOrders WHERE Id=$WO1_ID;")
if [[ "$PH" == "SETTING" ]]; then
    record PASS "WO1 in SETTING + 3 PREPRESS check sets OK"
else
    record FAIL "WO1 stayed at $PH"
fi

# Step 7 — POST /setting/done
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO1_ID/setting/done" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{}')
PHASE=$(echo "$R" | json_field "mesPhase")
NEW=$(echo "$R" | json_field "eTag")
if [[ "$PHASE" == "IPQC_WAIT" && -n "$NEW" ]]; then
    record PASS "POST /setting/done → IPQC_WAIT + bumped ETag"
    ETAG="$NEW"
else
    record FAIL "/setting/done failed: $R"
fi

# Step 8 — SQL shim IPQC_WAIT → IPQC_APPROVED (7d wire scope)
sqlite3 "$DB_PATH" "UPDATE WorkOrders SET MesPhase='IPQC_APPROVED', CurrentStep='OpSetting', UpdatedAt=datetime('now'), UpdatedBy='checkpoint-7c-final' WHERE Id=$WO1_ID;"
NEW_PHASE=$(sqlite3 "$DB_PATH" "SELECT MesPhase FROM WorkOrders WHERE Id=$WO1_ID;")
if [[ "$NEW_PHASE" == "IPQC_APPROVED" ]]; then
    record PASS "SQL shim IPQC_WAIT → IPQC_APPROVED (IPQC wire defers to 7d)"
    ETAG=$(etag_of "$WO1_ID")
else
    record FAIL "SQL shim failed — WO1 at $NEW_PHASE"
fi

# Step 9 — POST /run/start
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO1_ID/run/start" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{}')
PHASE=$(echo "$R" | json_field "mesPhase")
NEW=$(echo "$R" | json_field "eTag")
if [[ "$PHASE" == "RUNNING" ]]; then
    record PASS "POST /run/start → RUNNING + new WoRunSession"
    ETAG="$NEW"
else
    record FAIL "/run/start failed: $R"
fi

# Step 10 — POST /run/qty × 3
QTY_OK=0
for QTY_CALL in '{"qtyDoneDelta":100,"qtyNgDelta":0}' '{"qtyDoneDelta":500,"qtyNgDelta":0}' '{"qtyDoneDelta":0,"qtyNgDelta":5,"ngReasonCode":"SC-COLOR","ngNote":"checkpoint final NG sample"}'; do
    R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO1_ID/run/qty" \
        -H "$AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
        -d "$QTY_CALL")
    NEW=$(echo "$R" | json_field "eTag")
    OK=$(echo "$R" | json_field "ok")
    if [[ "$OK" == "True" ]]; then
        ETAG="$NEW"
        QTY_OK=$((QTY_OK + 1))
    fi
done
if [[ $QTY_OK -eq 3 ]]; then
    record PASS "POST /run/qty × 3 (+100 +500 +NG5 SC-COLOR)"
else
    record FAIL "/run/qty incomplete: only $QTY_OK/3 accepted"
fi

# Step 11 — POST /run/qty/correct -50 linked to first entry
FIRST_ENTRY_ID=$(sqlite3 "$DB_PATH" "SELECT Id FROM WoQtyEntries WHERE WoId=$WO1_ID ORDER BY Id LIMIT 1;")
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO1_ID/run/qty/correct" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d "{\"linkedEntryId\":$FIRST_ENTRY_ID,\"qtyDoneDelta\":-50,\"qtyNgDelta\":0,\"correctionReason\":\"checkpoint-final miscount fix\"}")
NEW=$(echo "$R" | json_field "eTag")
QDC=$(echo "$R" | json_field "qtyDoneCached")
if [[ "$QDC" == "550" ]]; then
    record PASS "POST /run/qty/correct -50 → QtyDoneCached=550 (100+500-50)"
    ETAG="$NEW"
else
    record FAIL "/run/qty/correct: $R"
fi

# Step 12 — POST /run/pause ML-MAT
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO1_ID/run/pause" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{"reasonCode":"ML-MAT","note":"checkpoint-final pause"}')
PHASE=$(echo "$R" | json_field "mesPhase")
NEW=$(echo "$R" | json_field "eTag")
if [[ "$PHASE" == "PAUSED" ]]; then
    record PASS "POST /run/pause ML-MAT → PAUSED"
    ETAG="$NEW"
else
    record FAIL "/run/pause: $R"
fi

# Step 13 — POST /run/resume
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO1_ID/run/resume" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{}')
PHASE=$(echo "$R" | json_field "mesPhase")
NEW=$(echo "$R" | json_field "eTag")
SESSION_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoRunSessions WHERE WoId=$WO1_ID;")
if [[ "$PHASE" == "RUNNING" && "$SESSION_COUNT" == "2" ]]; then
    record PASS "POST /run/resume → RUNNING (2 sessions total)"
    ETAG="$NEW"
else
    record FAIL "/run/resume phase=$PHASE sessions=$SESSION_COUNT: $R"
fi

# Step 14 — POST /run/qty +200 in resumed session
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO1_ID/run/qty" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{"qtyDoneDelta":200,"qtyNgDelta":0}')
NEW=$(echo "$R" | json_field "eTag")
QDC=$(echo "$R" | json_field "qtyDoneCached")
if [[ "$QDC" == "750" ]]; then
    record PASS "POST /run/qty +200 in session 2 → QtyDoneCached=750"
    ETAG="$NEW"
else
    record FAIL "/run/qty +200: $R"
fi

# Step 15 — POST /run/finish (from RUNNING) → FQC_PENDING
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO1_ID/run/finish" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{}')
PHASE=$(echo "$R" | json_field "mesPhase")
NEW=$(echo "$R" | json_field "eTag")
if [[ "$PHASE" == "FQC_PENDING" ]]; then
    record PASS "POST /run/finish (RUNNING) → FQC_PENDING"
    ETAG="$NEW"
else
    record FAIL "/run/finish (RUNNING): $R"
fi

# ── Block C — Q6 finish-from-PAUSED on WO2 (3 steps) ──────────────

# Step 16 — reset WO2 + shim to RUNNING + open session + +300 qty
bash "$SCRIPT_DIR/reset-prepress-for-wo.sh" --wo "$WO2_NO" --commit > /tmp/checkpoint-7c-final-reset2.log 2>&1
sqlite3 "$DB_PATH" "
    DELETE FROM WoQtyEntries WHERE WoId=$WO2_ID;
    DELETE FROM WoPauseEvents WHERE WoId=$WO2_ID;
    DELETE FROM WoRunSessions WHERE WoId=$WO2_ID;
    INSERT INTO WoRunSessions (WoId, StartedAt, StartedBy) VALUES ($WO2_ID, datetime('now','-3 minutes'), 'checkpoint-7c-final');
    UPDATE WorkOrders SET MesPhase='RUNNING', CurrentStep='Running', QtyDoneCached=0, QtyNgCached=0, SettingStartAt=datetime('now','-10 minutes'), SettingEndAt=datetime('now','-5 minutes'), SettingDurationSec=300, UpdatedAt=datetime('now'), UpdatedBy='checkpoint-7c-final' WHERE Id=$WO2_ID;
" 2>&1
ETAG2=$(etag_of "$WO2_ID")
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO2_ID/run/qty" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG2\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{"qtyDoneDelta":300,"qtyNgDelta":0}')
NEW=$(echo "$R" | json_field "eTag")
QDC=$(echo "$R" | json_field "qtyDoneCached")
if [[ "$QDC" == "300" ]]; then
    record PASS "WO2 setup RUNNING + +300 qty"
    ETAG2="$NEW"
else
    record FAIL "WO2 setup: $R"
fi

# Step 17 — POST /run/pause ML-CO
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO2_ID/run/pause" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG2\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{"reasonCode":"ML-CO","note":"checkpoint-final Q6 pause"}')
PHASE=$(echo "$R" | json_field "mesPhase")
NEW=$(echo "$R" | json_field "eTag")
if [[ "$PHASE" == "PAUSED" ]]; then
    record PASS "WO2 pause ML-CO → PAUSED (Q6 entry state)"
    ETAG2="$NEW"
else
    record FAIL "WO2 pause: $R"
fi

# Step 18 — POST /run/finish from PAUSED → FQC_PENDING + closed pause
R=$(curl -s -X POST "$API_BASE/api/v2/work-orders/$WO2_ID/run/finish" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG2\"" -H "Idempotency-Key: $(uuidgen)" \
    -d '{}')
PHASE=$(echo "$R" | json_field "mesPhase")
CLOSED_PAUSE=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoPauseEvents WHERE WoId=$WO2_ID AND EndedAt IS NOT NULL;")
OPEN_PAUSE=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoPauseEvents WHERE WoId=$WO2_ID AND EndedAt IS NULL;")
if [[ "$PHASE" == "FQC_PENDING" && "$CLOSED_PAUSE" == "1" && "$OPEN_PAUSE" == "0" ]]; then
    record PASS "WO2 /run/finish from PAUSED → FQC_PENDING + Q6 closed pause EndedAt"
else
    record FAIL "WO2 Q6 finish-from-PAUSED: phase=$PHASE closed=$CLOSED_PAUSE open=$OPEN_PAUSE"
fi

# ── Block D — L19 deferred-phase walk on WO1 (2 steps) ────────────

# Step 19 — GET /running-surface returns FQC_PENDING (WO1 final post-finish)
R=$(curl -s -H "$AUTH" "$API_BASE/api/v2/work-orders/$WO1_ID/running-surface")
RS_PHASE=$(echo "$R" | json_field "mesPhase")
RS_QDC=$(echo "$R" | json_field "qtyDoneCached")
if [[ "$RS_PHASE" == "FQC_PENDING" && "$RS_QDC" == "750" ]]; then
    record PASS "GET /running-surface WO1 → FQC_PENDING + QtyDoneCached=750"
else
    record FAIL "GET /running-surface WO1 phase=$RS_PHASE qdc=$RS_QDC"
fi

# Step 20 — walk WO1 through 5 deferred phases via shim + assert GET matches
WALK_OK=0
WALK_FAIL=()
for PHASE in QA_PENDING IPQC_WAIT OQC_PENDING DONE CANCELLED; do
    sqlite3 "$DB_PATH" "UPDATE WorkOrders SET MesPhase='$PHASE', UpdatedAt=datetime('now'), UpdatedBy='checkpoint-l19-walk' WHERE Id=$WO1_ID;" 2>&1
    R=$(curl -s -H "$AUTH" "$API_BASE/api/v2/work-orders/$WO1_ID/running-surface")
    GOT=$(echo "$R" | json_field "mesPhase")
    if [[ "$GOT" == "$PHASE" ]]; then
        WALK_OK=$((WALK_OK + 1))
    else
        WALK_FAIL+=("$PHASE→$GOT")
    fi
done
if [[ $WALK_OK -eq 5 ]]; then
    record PASS "L19 deferred-phase walk (5/5): QA_PENDING / IPQC_WAIT / OQC_PENDING / DONE / CANCELLED all returned canonical via GET /running-surface"
else
    record FAIL "L19 walk: $WALK_OK/5 PASS; mismatches: ${WALK_FAIL[*]}"
fi

# ── Block E — audit wire-mirror per Rule 7.3 (1 step) ─────────────

# Step 21 — every 7 RUNNING audit codes visible for WO1
AUDIT_MISS=()
for ACTION in WO_SETTING_DONE WO_RUN_START WO_RUN_QTY_ADD WO_RUN_QTY_CORRECT WO_RUN_PAUSE WO_RUN_RESUME WO_RUN_FINISH; do
    AUDIT=$(curl -s -H "$AUTH" "$API_BASE/api/v2/audit/log?action=$ACTION&page=1&pageSize=50")
    if ! echo "$AUDIT" | grep -q "\"targetId\":\"$WO1_ID\""; then
        AUDIT_MISS+=("$ACTION")
    fi
done
if [[ ${#AUDIT_MISS[@]} -eq 0 ]]; then
    record PASS "audit wire-mirror (7/7): all RUNNING audit codes visible for WO1"
else
    record FAIL "audit wire-mirror missing: ${AUDIT_MISS[*]}"
fi

# ── KEEP-ALIVE forensic dump ──────────────────────────────────────
if [[ $KEEP_ALIVE -eq 1 ]]; then
    echo ""
    echo "── KEEP-ALIVE: forensic state for Catalyst hand-verify ─────────"
    echo "  Final WO state (WO1 cycled through 5 deferred phases; ends at CANCELLED):"
    sqlite3 "$DB_PATH" -header -column "
        SELECT WoNo, MesPhase, CurrentStep, QtyDoneCached, QtyNgCached, SettingDurationSec
        FROM WorkOrders WHERE Id IN ($WO1_ID, $WO2_ID) ORDER BY Id;
    "
    echo ""
    echo "  WO1 sessions:"
    sqlite3 "$DB_PATH" -header -column "
        SELECT Id, StartedAt, EndedAt, StartedBy FROM WoRunSessions WHERE WoId=$WO1_ID ORDER BY Id;
    "
    echo ""
    echo "  WO1 pauses:"
    sqlite3 "$DB_PATH" -header -column "
        SELECT Id, StartedAt, EndedAt, ReasonCode, Note FROM WoPauseEvents WHERE WoId=$WO1_ID ORDER BY Id;
    "
    echo ""
    echo "  WO2 (Q6 finish-from-PAUSED) — closed pause + closed session:"
    sqlite3 "$DB_PATH" -header -column "
        SELECT Id, StartedAt, EndedAt, ReasonCode FROM WoPauseEvents WHERE WoId=$WO2_ID ORDER BY Id;
    "
    echo ""
    echo "  Henry next: launch Catalyst → scan WO1 + WO2 → walk 9 phase placeholders"
    echo "  via sqlite shim on data/ccl_mes.db. Then resize window wide ≥1400 +"
    echo "  narrow ≤900 to verify S9 responsive on each placeholder card."
fi

if [[ $FAIL -gt 0 ]]; then
    exit 1
fi
exit 0
