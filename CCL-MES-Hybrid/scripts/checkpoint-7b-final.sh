#!/usr/bin/env bash
# P10.7b-final — Catalyst checkpoint for the COMPLETE PREPRESS surface
# across the entire 7b stack (7b-1 domain + 7b-2 API + 7b-3 picker UI).
#
# Exercises the full operator path end-to-end on the live DB:
#
#   OK-all path (rollup → ready → advance enabled)
#     1. Reset target WO to PREPRESS phase
#     2. Boot API + login admin
#     3. Probe Scrap picker source ≥8 codes
#     4. GET /prepress → enumerate material rows
#     5. PUT every material row = Ok (sequential, fresh ETag each)
#     6. PUT plate = Ok
#     7. PUT cutter = Ok
#     8. Re-GET → assert MaterialsReady=true
#     9. Wire audit: WO_PREPRESS_MATERIAL_SET / _PLATE_SET / _CUTTER_SET
#        rows visible at /api/v2/audit/log
#
#   NG-with-picker path (rollup → not-ready → advance disabled)
#     10. Reset target WO again
#     11. GET picker → confirm SC-MAT-DAMAGE present
#     12. PUT first material = Ng with SC-MAT-DAMAGE + note → 200
#     13. PUT remaining materials = Ok
#     14. PUT plate + cutter = Ok
#     15. Re-GET → assert MaterialsReady=false (NG row blocks rollup)
#     16. PUT Ng with unregistered code → 422 (catalog enforcement)
#     17. Wire audit: WO_PREPRESS_MATERIAL_SET row with NgReasonCode
#         = "SC-MAT-DAMAGE" visible
#
# Henry-side visual checks (in Catalyst UI) — script then halts with
# --keep-alive so the same binary the script proved is what Henry sees:
#
#   * Dashboard rollup pill: "Materials Ready ✓" on path 1; "Materials
#     chưa sẵn sàng" on path 2.
#   * Advance button enabled on path 1 / disabled on path 2.
#   * NG picker dropdown shows the 8 SC-* codes; "Lưu NG" stays
#     disabled until an option is chosen.
#   * Resize wide ≥1400px + narrow ≤900px — layout stays usable both
#     sizes per SKILLS.md S9.
#
# Rule 7.1 — [ctx] DB= + DB sha8 printed in first 10 lines.
# Rule 7.2 — self-managed API lifecycle (auto-boot + trap EXIT). With
# --keep-alive, the auto-booted API stays running so Henry can do the
# Catalyst visual checks against the exact binary the script just
# proved.
#
# Usage:
#   bash CCL-MES-Hybrid/scripts/checkpoint-7b-final.sh <WoNo> [--keep-alive]

set -u
set +e

KEEP_ALIVE=0
WO_NO=""
for arg in "$@"; do
    case "$arg" in
        --keep-alive) KEEP_ALIVE=1 ;;
        --help|-h)
            echo "usage: bash scripts/checkpoint-7b-final.sh <WoNo> [--keep-alive]"
            echo "  --keep-alive  leave auto-booted API running for UI verify"
            exit 0
            ;;
        --*) echo "unknown flag: $arg"; exit 64 ;;
        *) WO_NO="$arg" ;;
    esac
done

if [[ -z "$WO_NO" ]]; then
    echo "usage: bash scripts/checkpoint-7b-final.sh <WoNo> [--keep-alive]"
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
echo "checkpoint-7b-final — full PREPRESS path for $WO_NO"
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
            echo "[keep-alive] log    : /tmp/checkpoint-7b-final-api.log"
            echo "[keep-alive] kill   : kill $AUTO_BOOT_PID"
        else
            kill -9 "$AUTO_BOOT_PID" 2>/dev/null
        fi
    fi
}
trap cleanup EXIT INT TERM

# ── 0. Reset target WO ─────────────────────────────────────────────
echo "[step] reset WO $WO_NO to PREPRESS via reset-prepress-for-wo.sh --commit"
bash "$SCRIPT_DIR/reset-prepress-for-wo.sh" --wo "$WO_NO" --commit > /tmp/checkpoint-7b-final-reset.log 2>&1
RESET_EXIT=$?
if [[ $RESET_EXIT -eq 0 ]]; then
    record PASS "reset-prepress-for-wo completed"
else
    record FAIL "reset-prepress-for-wo failed exit=$RESET_EXIT (see /tmp/checkpoint-7b-final-reset.log)"
    tail -15 /tmp/checkpoint-7b-final-reset.log
fi

# ── 1. Boot / reuse API ────────────────────────────────────────────
# Resolve target port from API_BASE so the assert-bound logic below knows
# what to check.
TARGET_PORT="${API_BASE##*:}"
TARGET_PORT="${TARGET_PORT%%/*}"

if curl -s -m 3 -o /dev/null -w "%{http_code}" "$API_BASE/health" 2>/dev/null | grep -qE "^(200|401|503)$"; then
    echo "[boot] API_BASE already responding — reusing"
else
    # P10.7b-4 hotfix — Lesson L18: kill stale listeners on target port
    # BEFORE booting so the dotnet run doesn't collide with a leftover.
    STALE_PIDS=$(lsof -nP -iTCP:${TARGET_PORT} -sTCP:LISTEN -t 2>/dev/null)
    if [[ -n "$STALE_PIDS" ]]; then
        echo "[boot] killing stale listeners on $TARGET_PORT: $STALE_PIDS"
        echo "$STALE_PIDS" | xargs -r kill -9 2>/dev/null
        sleep 1
    fi

    echo "[boot] auto-booting API pinned to $DB_PATH (urls=$API_BASE)"
    (cd "$HYBRID_ROOT/src/CCL.MES.Api" && \
        ConnectionStrings__Default="Data Source=$DB_PATH" \
        ASPNETCORE_ENVIRONMENT="Development" \
        dotnet run --no-build --no-launch-profile --urls "$API_BASE" > /tmp/checkpoint-7b-final-api.log 2>&1) &
    AUTO_BOOT_PID=$!
    for i in $(seq 1 60); do
        code=$(curl -s -m 2 -o /dev/null -w "%{http_code}" "$API_BASE/health" 2>/dev/null)
        if [[ "$code" =~ ^(200|401|503)$ ]]; then
            echo "[boot] API up after ${i}s (pid=$AUTO_BOOT_PID)"
            break
        fi
        sleep 1
    done

    # L18 assertion: API actually bound on the target port (not 5100).
    BOUND_PID=$(lsof -nP -iTCP:${TARGET_PORT} -sTCP:LISTEN -t 2>/dev/null | head -1)
    if [[ -z "$BOUND_PID" ]]; then
        record FAIL "API never bound on port $TARGET_PORT (see /tmp/checkpoint-7b-final-api.log)"
        tail -20 /tmp/checkpoint-7b-final-api.log
        exit 1
    fi
    if grep -q "Overriding address(es)" /tmp/checkpoint-7b-final-api.log; then
        record FAIL "L18 regression — Kestrel:Endpoints overrode --urls (Overriding address warning in log)"
    fi
fi

# ── 2. Login ───────────────────────────────────────────────────────
LOGIN_RSP=$(curl -s -X POST "$API_BASE/api/v2/auth/login" \
    -H "Content-Type: application/json" \
    -d "{\"username\":\"admin\",\"password\":\"admin\",\"deviceId\":\"checkpoint-7b-final\"}")
TOKEN=$(echo "$LOGIN_RSP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('accessToken',''))" 2>/dev/null)
if [[ -n "$TOKEN" ]]; then
    record PASS "login admin (token_len=${#TOKEN})"
else
    record FAIL "login failed: $LOGIN_RSP"
    exit 1
fi
AUTH="Authorization: Bearer $TOKEN"

# ── 3. Probe picker source ─────────────────────────────────────────
RC_BODY=$(curl -s -H "$AUTH" "$API_BASE/api/v2/reason-codes?kind=Scrap")
RC_COUNT=$(echo "$RC_BODY" | python3 -c "import sys,json; print(len(json.load(sys.stdin)))" 2>/dev/null)
HAS_DAMAGE=$(echo "$RC_BODY" | python3 -c "
import sys, json
print(any(r['code'] == 'SC-MAT-DAMAGE' for r in json.load(sys.stdin)))
" 2>/dev/null)
if [[ "${RC_COUNT:-0}" -ge 8 && "$HAS_DAMAGE" == "True" ]]; then
    record PASS "Scrap picker source ≥8 codes incl. SC-MAT-DAMAGE (count=$RC_COUNT)"
else
    record FAIL "Scrap picker source count=$RC_COUNT damage=$HAS_DAMAGE"
fi

# Resolve WO id from WoNo
WO_ID=$(sqlite3 "$DB_PATH" "SELECT Id FROM WorkOrders WHERE WoNo='$WO_NO' LIMIT 1;" 2>/dev/null)
if [[ -z "$WO_ID" ]]; then
    record FAIL "WO $WO_NO not found in DB"
    exit 1
fi

# ── 4. OK-ALL PATH ────────────────────────────────────────────────
echo ""
echo "── PATH 1: OK-all (rollup → ready → advance enabled) ──────────"

PRE=$(curl -s -H "$AUTH" "$API_BASE/api/v2/work-orders/$WO_ID/prepress")
ETAG=$(echo "$PRE" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))" 2>/dev/null)
INDEXES=$(echo "$PRE" | python3 -c "import sys,json; print(' '.join(str(m['bomLineIdx']) for m in json.load(sys.stdin).get('materials',[])))" 2>/dev/null)
INDEX_COUNT=$(echo "$INDEXES" | wc -w | tr -d ' ')
if [[ "$INDEX_COUNT" -gt 0 ]]; then
    record PASS "GET /prepress → $INDEX_COUNT material rows"
else
    record FAIL "GET /prepress → 0 materials (BOM snapshot empty?)"
    exit 1
fi

# PUT each material = Ok
ALL_OK=1
for IDX in $INDEXES; do
    R=$(curl -s -X PUT "$API_BASE/api/v2/work-orders/$WO_ID/materials/$IDX" \
        -H "$AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$ETAG\"" \
        -H "Idempotency-Key: $(uuidgen)" \
        -d "{\"status\":\"Ok\",\"qtyLoaded\":10,\"lotNo\":\"LOT-FINAL-$IDX\"}")
    OK=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('ok',False))" 2>/dev/null)
    NEW=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))" 2>/dev/null)
    if [[ "$OK" == "True" && -n "$NEW" ]]; then
        ETAG="$NEW"
    else
        ALL_OK=0
        echo "[debug] PUT /materials/$IDX failed: $R"
        break
    fi
done
if [[ $ALL_OK -eq 1 ]]; then
    record PASS "PUT all $INDEX_COUNT materials = Ok"
else
    record FAIL "PUT materials Ok failed mid-sequence"
fi

# PUT plate = Ok
R=$(curl -s -X PUT "$API_BASE/api/v2/work-orders/$WO_ID/plate-check" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" \
    -H "Idempotency-Key: $(uuidgen)" \
    -d '{"status":"Ok","plateNo":"PLATE-FINAL"}')
OK=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('ok',False))" 2>/dev/null)
if [[ "$OK" == "True" ]]; then
    record PASS "PUT plate = Ok"
    ETAG=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))" 2>/dev/null)
else
    record FAIL "PUT plate failed: $R"
fi

# PUT cutter = Ok
R=$(curl -s -X PUT "$API_BASE/api/v2/work-orders/$WO_ID/cutter-check" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" \
    -H "Idempotency-Key: $(uuidgen)" \
    -d '{"status":"Ok","cutterNo":"CUT-FINAL"}')
OK=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('ok',False))" 2>/dev/null)
READY=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('materialsReady',False))" 2>/dev/null)
if [[ "$OK" == "True" && "$READY" == "True" ]]; then
    record PASS "PUT cutter = Ok + rollup flipped to Ready"
else
    record FAIL "PUT cutter / rollup: ok=$OK ready=$READY body=$R"
fi

# Audit wire mirror
for ACTION in WO_PREPRESS_MATERIAL_SET WO_PREPRESS_PLATE_SET WO_PREPRESS_CUTTER_SET; do
    AUDIT=$(curl -s -H "$AUTH" "$API_BASE/api/v2/audit/log?action=$ACTION&page=1&pageSize=50")
    if echo "$AUDIT" | grep -q "\"targetId\":\"$WO_ID\""; then
        record PASS "audit wire $ACTION visible for WO $WO_ID"
    else
        record FAIL "audit wire $ACTION missing"
    fi
done

# ── 5. NG-WITH-PICKER PATH ────────────────────────────────────────
echo ""
echo "── PATH 2: NG-with-picker (rollup → not-ready → advance disabled) ──"

# Re-reset
bash "$SCRIPT_DIR/reset-prepress-for-wo.sh" --wo "$WO_NO" --commit > /tmp/checkpoint-7b-final-reset2.log 2>&1
PRE=$(curl -s -H "$AUTH" "$API_BASE/api/v2/work-orders/$WO_ID/prepress")
ETAG=$(echo "$PRE" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))" 2>/dev/null)
INDEXES=$(echo "$PRE" | python3 -c "import sys,json; print(' '.join(str(m['bomLineIdx']) for m in json.load(sys.stdin).get('materials',[])))" 2>/dev/null)
FIRST=$(echo "$INDEXES" | awk '{print $1}')
REST=$(echo "$INDEXES" | cut -d' ' -f2-)

# PUT first material = Ng with SC-MAT-DAMAGE
NG=$(curl -s -X PUT "$API_BASE/api/v2/work-orders/$WO_ID/materials/$FIRST" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -H "If-Match: \"$ETAG\"" \
    -H "Idempotency-Key: $(uuidgen)" \
    -d '{"status":"Ng","ngReasonCode":"SC-MAT-DAMAGE","ngNote":"checkpoint-7b-final NG path"}')
NG_OK=$(echo "$NG" | python3 -c "import sys,json; print(json.load(sys.stdin).get('ok',False))" 2>/dev/null)
if [[ "$NG_OK" == "True" ]]; then
    record PASS "PUT first material = Ng + SC-MAT-DAMAGE → 200"
    ETAG=$(echo "$NG" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))" 2>/dev/null)
else
    record FAIL "PUT Ng SC-MAT-DAMAGE failed: $NG"
fi

# PUT unregistered code → 422 (catalog enforcement)
SECOND=$(echo "$REST" | awk '{print $1}')
if [[ -n "$SECOND" ]]; then
    STATUS=$(curl -s -o /dev/null -w '%{http_code}' \
        -X PUT "$API_BASE/api/v2/work-orders/$WO_ID/materials/$SECOND" \
        -H "$AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$ETAG\"" \
        -H "Idempotency-Key: $(uuidgen)" \
        -d '{"status":"Ng","ngReasonCode":"NOT-A-CODE","ngNote":"bogus"}')
    if [[ "$STATUS" == "422" ]]; then
        record PASS "PUT Ng unregistered code → 422 (catalog enforced)"
    else
        record FAIL "PUT Ng unregistered code expected 422 got $STATUS"
    fi
fi

# PUT remaining = Ok
ALL_OK=1
for IDX in $REST; do
    R=$(curl -s -X PUT "$API_BASE/api/v2/work-orders/$WO_ID/materials/$IDX" \
        -H "$AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$ETAG\"" \
        -H "Idempotency-Key: $(uuidgen)" \
        -d "{\"status\":\"Ok\",\"qtyLoaded\":10}")
    OK=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('ok',False))" 2>/dev/null)
    NEW=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))" 2>/dev/null)
    if [[ "$OK" == "True" && -n "$NEW" ]]; then
        ETAG="$NEW"
    else
        ALL_OK=0
        break
    fi
done

# PUT plate + cutter = Ok
for CHECK in plate-check cutter-check; do
    R=$(curl -s -X PUT "$API_BASE/api/v2/work-orders/$WO_ID/$CHECK" \
        -H "$AUTH" -H "Content-Type: application/json" \
        -H "If-Match: \"$ETAG\"" \
        -H "Idempotency-Key: $(uuidgen)" \
        -d '{"status":"Ok"}')
    NEW=$(echo "$R" | python3 -c "import sys,json; print(json.load(sys.stdin).get('eTag',''))" 2>/dev/null)
    [[ -n "$NEW" ]] && ETAG="$NEW"
done

# Re-GET → MaterialsReady ought to be false (NG row blocks rollup)
FINAL=$(curl -s -H "$AUTH" "$API_BASE/api/v2/work-orders/$WO_ID/prepress")
READY=$(echo "$FINAL" | python3 -c "import sys,json; print(json.load(sys.stdin).get('materialsReady',None))" 2>/dev/null)
if [[ "$READY" == "False" ]]; then
    record PASS "rollup MaterialsReady=false on NG path (advance disabled)"
else
    record FAIL "rollup MaterialsReady=$READY on NG path (expected False)"
fi

# Audit wire mirror — NG row with reason code visible
AUDIT=$(curl -s -H "$AUTH" "$API_BASE/api/v2/audit/log?action=WO_PREPRESS_MATERIAL_SET&page=1&pageSize=50")
if echo "$AUDIT" | grep -q "SC-MAT-DAMAGE"; then
    record PASS "audit wire WO_PREPRESS_MATERIAL_SET row carries NgReasonCode=SC-MAT-DAMAGE"
else
    record FAIL "audit wire NG row missing SC-MAT-DAMAGE in detail JSON"
fi

# ── Summary ───────────────────────────────────────────────────────
echo ""
echo "============================  SUMMARY  ============================"
printf '%s\n' "${SUMMARY[@]}"
echo ""
echo "  TOTAL: pass=$PASS fail=$FAIL"
echo ""

if [[ $KEEP_ALIVE -eq 1 ]]; then
    echo "── Next: Henry Catalyst visual checks ──────────────────────────"
    echo "  1. Open WO $WO_NO on Catalyst (auto-reset is at NG-path state)."
    echo "  2. Dashboard rollup pill = \"Materials chưa sẵn sàng\"; Advance disabled."
    echo "  3. Tap \"Đánh NG\" on any pending row → picker shows the 8 SC-* codes."
    echo "  4. \"Lưu NG\" disabled until a code is chosen."
    echo "  5. Resize window: wide ≥1400px + narrow ≤900px — layout intact."
fi

if [[ $FAIL -gt 0 ]]; then
    exit 1
fi
exit 0
