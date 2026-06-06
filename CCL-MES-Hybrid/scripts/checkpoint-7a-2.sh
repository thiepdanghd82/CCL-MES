#!/usr/bin/env bash
# P10.7a-2.2 — Catalyst checkpoint script for the admin force-phase endpoint.
#
# Operator runs ONE command. Script does everything programmatic + manages
# its OWN API lifecycle so the operator never has to dual-track keep-alive
# servers across terminals. Henry visually verifies TWO things in Catalyst
# UI at the end.
#
# Programmatic steps (script):
#   1. Reset target WO to OpSetting (mid-SETTING wedge) via reset-test-wo.sh.
#   2. Login as admin/admin → access token.
#   3. GET work-order summary → extract ETag + current CurrentStep.
#   4. POST /admin/work-orders/{id}/force-phase with valid headers + body.
#   5. Re-GET summary → confirm CurrentStep == PrePressCheck.
#   6. GET /api/v2/audit/log?action=SYS_RECOVERY → confirm row for THIS WO
#      with detail containing REC-OP-WEDGE.
#
# Henry-side visual checks (in Catalyst UI):
#   * WO drawer for the WO shows CurrentStep = "PrePressCheck" + fresh badge.
#   * Settings → Audit Log shows the SYS_RECOVERY row (top of feed).
#
# Self-managed server lifecycle (Rule 7 from P10.7a-2.2 incident):
#   * If $API_BASE already responds, reuse it.
#   * Otherwise auto-boot CCL.MES.Api on http://127.0.0.1:5100 pinned to
#     the same data/ccl_mes.db this script targets. Trap EXIT to kill it.
#   * Every step prints [ctx] DB=<abs-path> at startup so operator never
#     wonders which DB is in play.
#
# Usage:
#   bash CCL-MES-Hybrid/scripts/checkpoint-7a-2.sh <WoNo>
#
# Example:
#   bash CCL-MES-Hybrid/scripts/checkpoint-7a-2.sh WO-26-3683
#
# Exit code 0 = all 6 programmatic steps PASS. Non-zero on any step fail.

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
DB_PATH="$REPO_ROOT/data/ccl_mes.db"
API_BASE="${API_BASE:-http://127.0.0.1:5100}"
AUTO_BOOT_PID=""

# ── [ctx] header (Rule 7) ─────────────────────────────────────────
DB_SHA_PREFIX="(missing)"
if [[ -f "$DB_PATH" ]]; then
    DB_SHA_PREFIX="$(shasum -a 256 "$DB_PATH" 2>/dev/null | awk '{print substr($1,1,8)}')"
fi
echo "===================================================================="
echo "checkpoint-7a-2 — force-phase verify for $WO_NO"
echo "[ctx] DB         = $DB_PATH"
echo "[ctx] DB sha8    = $DB_SHA_PREFIX"
echo "[ctx] API base   = $API_BASE"
echo "[ctx] HEAD       = $(cd "$REPO_ROOT" && git rev-parse --short HEAD 2>/dev/null || echo '?')"
echo "===================================================================="

# ── Self-managed server lifecycle (Rule 7) ────────────────────────
cleanup() {
    if [[ -n "$AUTO_BOOT_PID" ]]; then
        echo "[cleanup] stopping auto-booted API (pid=$AUTO_BOOT_PID)"
        kill -9 "$AUTO_BOOT_PID" 2>/dev/null
    fi
}
trap cleanup EXIT INT TERM

if curl -s -m 3 -o /dev/null -w "%{http_code}" "$API_BASE/health" 2>/dev/null | grep -qE "^(200|401|503)$"; then
    echo "[boot] API_BASE already responding — reusing it"
else
    echo "[boot] API not responding on $API_BASE — auto-booting pinned to DB_PATH"
    (cd "$HYBRID_ROOT/src/CCL.MES.Api" && \
        ConnectionStrings__Default="Data Source=$DB_PATH" \
        ASPNETCORE_URLS="http://127.0.0.1:5100" \
        ASPNETCORE_ENVIRONMENT="Development" \
        dotnet run --no-build --no-launch-profile > /tmp/checkpoint-7a-2-api.log 2>&1) &
    AUTO_BOOT_PID=$!
    # Wait up to 60s for /health to respond (any code — 401 means up + auth wall)
    for i in $(seq 1 60); do
        code=$(curl -s -m 2 -o /dev/null -w "%{http_code}" "$API_BASE/health" 2>/dev/null)
        if [[ "$code" =~ ^(200|401|503)$ ]]; then
            echo "[boot] API up after ${i}s (health=$code, pid=$AUTO_BOOT_PID)"
            break
        fi
        sleep 1
    done
    if ! curl -s -m 3 -o /dev/null -w "%{http_code}" "$API_BASE/health" 2>/dev/null | grep -qE "^(200|401|503)$"; then
        echo "[boot] FAILED — API did not start in 60s; see /tmp/checkpoint-7a-2-api.log"
        tail -20 /tmp/checkpoint-7a-2-api.log
        exit 1
    fi
fi

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

# ── Step 1: reset target WO to SETTING ────────────────────────────
echo ""
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
echo ""
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
echo ""
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
echo ""
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
echo ""
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

# ── Step 6: GET /api/v2/audit/log?action=SYS_RECOVERY ─────────────
# Endpoint route per AuditLogController: ApiVersion.Prefix + "/audit" = /api/v2/audit
# Filter params: search, action, actor, from, to, page, pageSize. NO targetType/targetId.
# We filter on action=SYS_RECOVERY + grep the response for this WO's targetId.
echo ""
echo "[6] GET /api/v2/audit/log?action=SYS_RECOVERY → confirm row for wo_id=$WO_ID"
AUDIT_RESP="$(curl -s "$API_BASE/api/v2/audit/log?action=SYS_RECOVERY&page=1&pageSize=50" \
    -H "Authorization: Bearer $TOKEN")"
HAS_TARGET=$(echo "$AUDIT_RESP" | grep -c "\"targetId\":\"$WO_ID\"")
HAS_REC_OP_WEDGE=$(echo "$AUDIT_RESP" | grep -c "REC-OP-WEDGE")
if [[ "$HAS_TARGET" -gt 0 && "$HAS_REC_OP_WEDGE" -gt 0 ]]; then
    step "audit row SYS_RECOVERY for targetId=$WO_ID + REC-OP-WEDGE present" PASS
else
    step "audit row missing (targetId=$WO_ID hits=$HAS_TARGET, REC-OP-WEDGE hits=$HAS_REC_OP_WEDGE)" FAIL
    echo "  → first 500 chars of audit response: $(echo "$AUDIT_RESP" | head -c 500)"
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
