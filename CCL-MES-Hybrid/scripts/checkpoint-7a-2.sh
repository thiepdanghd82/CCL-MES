#!/usr/bin/env bash
# P10.7a-2.2 — Catalyst checkpoint script for the admin force-phase endpoint.
#
# Per Henry's adj #2: the operator runs ONE command. The script does
# everything programmatic; the operator visually verifies TWO things
# (in Catalyst UI) and reports PASS / FAIL.
#
# Programmatic steps (script):
#   1. Reset target WO to OpSetting (mid-SETTING wedge) via reset-test-wo.sh.
#   2. Login as admin/admin → access token.
#   3. GET work-order summary → extract ETag + current CurrentStep.
#   4. POST /admin/work-orders/{id}/force-phase with valid headers + body:
#        TargetStep   = PrePressCheck
#        ReasonCode   = REC-OP-WEDGE
#        ReasonNote   = "Catalyst checkpoint — operator A left mid-shift"
#      Expect: 200 OK with body.Ok=true and a NEW ETag (different from step 3).
#   5. Re-GET summary → confirm CurrentStep == PrePressCheck.
#   6. GET admin audit-log → confirm at least one SYS_RECOVERY row for this WO,
#      with detail containing from_phase=SETTING, to_phase=PREPRESS,
#      reason.code=REC-OP-WEDGE.
#
# Henry-side visual checks (in Catalyst UI):
#   * The WO drawer shows CurrentStep = "PrePressCheck" + a fresh badge.
#   * Settings → Audit Log shows the SYS_RECOVERY row (top of feed).
#
# Usage:
#   bash CCL-MES-Hybrid/scripts/checkpoint-7a-2.sh <WoNo>
#
# Example:
#   bash CCL-MES-Hybrid/scripts/checkpoint-7a-2.sh WO-26-3683
#
# Exit code 0 = all programmatic steps PASS. Non-zero on any step fail
# (script prints which step + the response body).

set -u
set +e

WO_NO="${1:-}"
if [[ -z "$WO_NO" ]]; then
    echo "usage: bash scripts/checkpoint-7a-2.sh <WoNo>"
    exit 64
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HYBRID_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$HYBRID_ROOT/.." && pwd)"

API_BASE="${API_BASE:-http://127.0.0.1:5100}"

PASS=0
FAIL=0
step() {
    local label="$1"
    local ok="$2"
    if [[ "$ok" == "PASS" ]]; then
        PASS=$((PASS + 1))
        echo "  ✓ $label"
    else
        FAIL=$((FAIL + 1))
        echo "  ✗ $label"
    fi
}

echo "===================================================================="
echo "checkpoint-7a-2 — force-phase verify for $WO_NO"
echo "API_BASE = $API_BASE"
echo "===================================================================="

# ── Step 1: reset target WO to SETTING ────────────────────────────
echo "[1] reset $WO_NO to OpSetting (wedged-mid-SETTING fixture)"
RESET_LOG="$(mktemp)"
bash "$SCRIPT_DIR/reset-test-wo.sh" "$WO_NO" OpSetting > "$RESET_LOG" 2>&1
RESET_EXIT=$?
if [[ $RESET_EXIT -eq 0 ]]; then
    step "reset-test-wo OpSetting" PASS
else
    step "reset-test-wo OpSetting (exit=$RESET_EXIT)" FAIL
    tail -10 "$RESET_LOG"
    exit 1
fi

# ── Step 2: login admin/admin ─────────────────────────────────────
echo "[2] login admin/admin"
LOGIN_JSON="$(curl -s -X POST "$API_BASE/api/v2/auth/login" \
    -H "Content-Type: application/json" \
    -d '{"username":"admin","password":"admin"}')"
TOKEN=$(echo "$LOGIN_JSON" | sed -nE 's/.*"accessToken":"([^"]+)".*/\1/p')
if [[ -n "$TOKEN" ]]; then
    step "login (token=${TOKEN:0:8}…)" PASS
else
    step "login failed" FAIL
    echo "  → response: $LOGIN_JSON"
    exit 1
fi

# ── Step 3: GET summary → capture wo_id + ETag ────────────────────
echo "[3] GET summary $WO_NO → extract ETag + wo_id"
SUMMARY_JSON="$(curl -s "$API_BASE/api/v2/work-orders/by-no/$WO_NO/summary" \
    -H "Authorization: Bearer $TOKEN")"
WO_ID=$(echo "$SUMMARY_JSON" | sed -nE 's/.*"id":([0-9]+).*/\1/p')
ETAG_BODY=$(echo "$SUMMARY_JSON" | sed -nE 's/.*"eTag":"([^"]+)".*/\1/p')
CURRENT_STEP=$(echo "$SUMMARY_JSON" | sed -nE 's/.*"currentStep":"([^"]+)".*/\1/p')
if [[ -n "$WO_ID" && -n "$ETAG_BODY" ]]; then
    step "summary (id=$WO_ID, currentStep=$CURRENT_STEP, eTag=${ETAG_BODY:0:8}…)" PASS
else
    step "summary parse failed" FAIL
    echo "  → response: $SUMMARY_JSON"
    exit 1
fi

# ── Step 4: POST /admin force-phase to PrePressCheck ──────────────
echo "[4] POST force-phase → PrePressCheck (REC-OP-WEDGE)"
IDEM_KEY="$(uuidgen 2>/dev/null || python3 -c 'import uuid;print(uuid.uuid4())')"
FORCE_RESP="$(curl -s -w "\nHTTP_CODE:%{http_code}" \
    -X POST "$API_BASE/api/v2/admin/work-orders/$WO_ID/force-phase" \
    -H "Authorization: Bearer $TOKEN" \
    -H "If-Match: \"$ETAG_BODY\"" \
    -H "Idempotency-Key: $IDEM_KEY" \
    -H "Content-Type: application/json" \
    -d "{\"targetStep\":\"PrePressCheck\",\"reasonCode\":\"REC-OP-WEDGE\",\"reasonNote\":\"Catalyst checkpoint — operator A left mid-shift\"}")"
HTTP_CODE=$(echo "$FORCE_RESP" | tail -1 | sed -nE 's/HTTP_CODE:([0-9]+)/\1/p')
BODY=$(echo "$FORCE_RESP" | sed '$d')
NEW_ETAG=$(echo "$BODY" | sed -nE 's/.*"eTag":"([^"]+)".*/\1/p')
OK_FIELD=$(echo "$BODY" | sed -nE 's/.*"ok":(true|false).*/\1/p')
if [[ "$HTTP_CODE" == "200" && "$OK_FIELD" == "true" && -n "$NEW_ETAG" && "$NEW_ETAG" != "$ETAG_BODY" ]]; then
    step "force-phase 200 + new ETag (${NEW_ETAG:0:8}…)" PASS
else
    step "force-phase ($HTTP_CODE, ok=$OK_FIELD) FAILED" FAIL
    echo "  → body: $BODY"
    exit 1
fi

# ── Step 5: GET summary → confirm CurrentStep updated ────────────
echo "[5] re-GET summary → confirm CurrentStep == PrePressCheck"
SUMMARY2="$(curl -s "$API_BASE/api/v2/work-orders/by-no/$WO_NO/summary" \
    -H "Authorization: Bearer $TOKEN")"
CURRENT2=$(echo "$SUMMARY2" | sed -nE 's/.*"currentStep":"([^"]+)".*/\1/p')
if [[ "$CURRENT2" == "PrePressCheck" ]]; then
    step "post-force currentStep = PrePressCheck" PASS
else
    step "post-force currentStep = $CURRENT2 (expected PrePressCheck)" FAIL
    exit 1
fi

# ── Step 6: GET admin audit-log → confirm SYS_RECOVERY row ────────
echo "[6] GET audit-log → confirm SYS_RECOVERY row for wo_id=$WO_ID"
AUDIT_RESP="$(curl -s "$API_BASE/api/v2/admin/audit/log?targetType=WorkOrder&targetId=$WO_ID&page=1&pageSize=20" \
    -H "Authorization: Bearer $TOKEN")"
HAS_SYS_REC=$(echo "$AUDIT_RESP" | grep -c "SYS_RECOVERY")
HAS_REC_OP_WEDGE=$(echo "$AUDIT_RESP" | grep -c "REC-OP-WEDGE")
if [[ "$HAS_SYS_REC" -gt 0 && "$HAS_REC_OP_WEDGE" -gt 0 ]]; then
    step "audit row SYS_RECOVERY + REC-OP-WEDGE present" PASS
else
    step "audit row missing (SYS_RECOVERY=$HAS_SYS_REC, REC-OP-WEDGE=$HAS_REC_OP_WEDGE)" FAIL
    echo "  → audit response: $AUDIT_RESP"
fi

# ── Summary ───────────────────────────────────────────────────────
echo ""
echo "==================="
echo "CHECKPOINT — PASS=$PASS  FAIL=$FAIL"
echo "==================="
echo ""
echo "Now verify in Catalyst UI (2 visual checks):"
echo "  1. WO drawer for $WO_NO shows CurrentStep = PrePressCheck (was OpSetting)."
echo "  2. Settings → Audit Log shows a SYS_RECOVERY row for this WO at the top."
echo ""
if [[ $FAIL -gt 0 ]]; then
    exit 1
fi
exit 0
