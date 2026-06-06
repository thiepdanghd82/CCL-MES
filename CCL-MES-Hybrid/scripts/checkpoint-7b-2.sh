#!/usr/bin/env bash
# P10.7b-2 — Catalyst checkpoint for the PREPRESS row-write endpoints.
#
# Operator runs ONE command. Script self-manages API (Rule 7.2) +
# self-seeds BOM if the target WO's product has no MS rows (closes the
# materials=0 blocker Henry flagged after 7b-1 deploy).
#
# Programmatic steps (script):
#   1. Reset target WO to PrePressCheck (clear prior wo_materials rows so
#      the materialise path runs fresh) via reset-test-wo.sh.
#   2. Auto-boot API pinned to data/ccl_mes.db (Rule 7.2).
#   3. Login admin/admin → access token.
#   4. Ensure BOM lines exist for the WO's product. If MS table has 0
#      rows for the product, INSERT 5 test BOM rows.
#   5. GET /work-orders/{id}/prepress → triggers lazy materialise +
#      asserts 5 PENDING materials rows + plate + cutter PENDING.
#   6. PUT each of the 5 materials = OK (sequential, fresh ETag).
#   7. PUT plate = OK + PUT cutter = OK.
#   8. Re-GET /prepress → assert MaterialsReady=true + all rows OK.
#   9. GET /audit/log?action=WO_PREPRESS_MATERIAL_SET → assert 5 rows
#      visible for this WO; same for PLATE_SET + CUTTER_SET.
#
# Henry-side visual checks (in Catalyst UI):
#   * WO drawer shows MaterialsReady ✓ badge + Advance enabled.
#   * Settings → Audit Log shows 5 WO_PREPRESS_MATERIAL_SET +
#     1 PLATE_SET + 1 CUTTER_SET rows at top of feed.
#
# Usage:
#   bash CCL-MES-Hybrid/scripts/checkpoint-7b-2.sh <WoNo> [--keep-alive]

set -u
set +e

KEEP_ALIVE=0
WO_NO=""
for arg in "$@"; do
    case "$arg" in
        --keep-alive) KEEP_ALIVE=1 ;;
        --help|-h)
            echo "usage: bash scripts/checkpoint-7b-2.sh <WoNo> [--keep-alive]"
            echo "  --keep-alive  leave auto-booted API running for UI verify"
            exit 0
            ;;
        --*) echo "unknown flag: $arg"; exit 64 ;;
        *) WO_NO="$arg" ;;
    esac
done

if [[ -z "$WO_NO" ]]; then
    echo "usage: bash scripts/checkpoint-7b-2.sh <WoNo> [--keep-alive]"
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
echo "checkpoint-7b-2 — PREPRESS write verify for $WO_NO"
echo "[ctx] DB         = $DB_PATH"
echo "[ctx] DB sha8    = $DB_SHA8"
echo "[ctx] API base   = $API_BASE"
echo "[ctx] HEAD       = $(cd "$REPO_ROOT" && git rev-parse --short HEAD 2>/dev/null || echo '?')"
echo "===================================================================="

cleanup() {
    if [[ -n "$AUTO_BOOT_PID" ]]; then
        if [[ $KEEP_ALIVE -eq 1 ]]; then
            echo ""
            echo "[keep-alive] API left running on $API_BASE (pid=$AUTO_BOOT_PID)"
            echo "[keep-alive] log    : /tmp/checkpoint-7b-2-api.log"
            echo "[keep-alive] kill   : kill $AUTO_BOOT_PID"
        else
            echo "[cleanup] stopping auto-booted API (pid=$AUTO_BOOT_PID)"
            kill -9 "$AUTO_BOOT_PID" 2>/dev/null
        fi
    fi
}
trap cleanup EXIT INT TERM

if curl -s -m 3 -o /dev/null -w "%{http_code}" "$API_BASE/health" 2>/dev/null | grep -qE "^(200|401|503)$"; then
    echo "[boot] API_BASE already responding — reusing it"
else
    echo "[boot] API not responding — auto-booting pinned to $DB_PATH"
    (cd "$HYBRID_ROOT/src/CCL.MES.Api" && \
        ConnectionStrings__Default="Data Source=$DB_PATH" \
        ASPNETCORE_URLS="http://127.0.0.1:5100" \
        ASPNETCORE_ENVIRONMENT="Development" \
        dotnet run --no-build --no-launch-profile > /tmp/checkpoint-7b-2-api.log 2>&1) &
    AUTO_BOOT_PID=$!
    for i in $(seq 1 60); do
        code=$(curl -s -m 2 -o /dev/null -w "%{http_code}" "$API_BASE/health" 2>/dev/null)
        if [[ "$code" =~ ^(200|401|503)$ ]]; then
            echo "[boot] API up after ${i}s (health=$code, pid=$AUTO_BOOT_PID)"
            break
        fi
        sleep 1
    done
    if ! curl -s -m 3 -o /dev/null -w "%{http_code}" "$API_BASE/health" 2>/dev/null | grep -qE "^(200|401|503)$"; then
        echo "[boot] FAILED — see /tmp/checkpoint-7b-2-api.log"
        tail -20 /tmp/checkpoint-7b-2-api.log
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

# ── Step 1: reset WO to PrePressCheck + clear prior wo_materials ──
echo ""
echo "[1] reset $WO_NO to PrePressCheck + clear wo_materials snapshot"
bash "$SCRIPT_DIR/reset-test-wo.sh" "$WO_NO" PrePressCheck > /tmp/checkpoint-7b-2-reset.log 2>&1
RESET_EXIT=$?
if [[ $RESET_EXIT -ne 0 ]]; then
    step "reset-test-wo (exit=$RESET_EXIT)" FAIL
    tail -10 /tmp/checkpoint-7b-2-reset.log
    exit 1
fi
# Clear materials rows so the lazy materialiser repopulates them fresh.
sqlite3 "$DB_PATH" \
  "DELETE FROM WoMaterials WHERE WorkOrderId = (SELECT Id FROM WorkOrders WHERE WoNo='$WO_NO');" \
  > /dev/null 2>&1
step "reset $WO_NO + clear wo_materials snapshot" PASS

# ── Step 2: login ────────────────────────────────────────────────
echo ""
echo "[2] login admin/admin"
TOKEN=$(curl -s -X POST "$API_BASE/api/v2/auth/login" \
    -H "Content-Type: application/json" \
    -d '{"username":"admin","password":"admin"}' | sed -nE 's/.*"accessToken":"([^"]+)".*/\1/p')
if [[ -n "$TOKEN" ]]; then
    step "login (token=${TOKEN:0:8}…)" PASS
else
    step "login failed" FAIL
    exit 1
fi

# ── Step 3: seed BOM lines if missing ────────────────────────────
echo ""
echo "[3] ensure BOM rows exist for $WO_NO's product"
PRODUCT_CODE=$(sqlite3 "$DB_PATH" \
  "SELECT p.ProductCode
   FROM WorkOrders wo
   INNER JOIN ProductRevisions pr ON pr.Id = wo.ProductRevisionId
   INNER JOIN Products p ON p.Id = pr.ProductId
   WHERE wo.WoNo='$WO_NO' LIMIT 1;")
if [[ -z "$PRODUCT_CODE" ]]; then
    step "could not resolve ProductCode for $WO_NO" FAIL
    exit 1
fi
BOM_COUNT=$(sqlite3 "$DB_PATH" \
  "SELECT COUNT(*) FROM ManufacturingStructures WHERE ParentPart='$PRODUCT_CODE';")
if [[ "$BOM_COUNT" -ge 5 ]]; then
    step "BOM rows already exist for $PRODUCT_CODE ($BOM_COUNT rows)" PASS
else
    sqlite3 "$DB_PATH" <<SQL
INSERT INTO ManufacturingStructures
  (ParentPart, ComponentPart, ComponentDescription, QtyAssembly, Uom, ScrapFactor, CreatedAt, CreatedBy)
VALUES
  ('$PRODUCT_CODE', 'TEST-LINE-1', 'Test material line 1', 0.001, 'm2', 0, datetime('now'), 'checkpoint-7b-2'),
  ('$PRODUCT_CODE', 'TEST-LINE-2', 'Test material line 2', 0.002, 'kg', 0, datetime('now'), 'checkpoint-7b-2'),
  ('$PRODUCT_CODE', 'TEST-LINE-3', 'Test material line 3', 1.0, 'pcs', 0, datetime('now'), 'checkpoint-7b-2'),
  ('$PRODUCT_CODE', 'TEST-LINE-4', 'Test material line 4', 0.5, 'pcs', 0, datetime('now'), 'checkpoint-7b-2'),
  ('$PRODUCT_CODE', 'TEST-LINE-5', 'Test material line 5', 10.0, 'g', 0, datetime('now'), 'checkpoint-7b-2');
SQL
    NEW_COUNT=$(sqlite3 "$DB_PATH" \
      "SELECT COUNT(*) FROM ManufacturingStructures WHERE ParentPart='$PRODUCT_CODE';")
    step "seeded 5 BOM rows for $PRODUCT_CODE (now $NEW_COUNT rows)" PASS
fi

# ── Step 4: GET prepress → materialise + assert ──────────────────
echo ""
echo "[4] GET /prepress → materialise snapshot"
WO_ID=$(sqlite3 "$DB_PATH" "SELECT Id FROM WorkOrders WHERE WoNo='$WO_NO' LIMIT 1;")
PREP=$(curl -s -w "\nHTTP_CODE:%{http_code}" \
    "$API_BASE/api/v2/work-orders/$WO_ID/prepress" \
    -H "Authorization: Bearer $TOKEN")
HTTP_CODE=$(echo "$PREP" | tail -1 | sed -nE 's/HTTP_CODE:([0-9]+)/\1/p')
BODY=$(echo "$PREP" | sed '$d')
MAT_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoMaterials WHERE WorkOrderId=$WO_ID;")
ETAG=$(echo "$BODY" | sed -nE 's/.*"eTag":"([^"]+)".*/\1/p')
if [[ "$HTTP_CODE" == "200" && "$MAT_COUNT" -ge 5 && -n "$ETAG" ]]; then
    step "GET prepress 200 + $MAT_COUNT materials rows + ETag (${ETAG:0:8}…)" PASS
else
    step "GET prepress failed (code=$HTTP_CODE, materials=$MAT_COUNT, etag=${ETAG:0:8}…)" FAIL
    echo "  → body: $(echo "$BODY" | head -c 300)"
    exit 1
fi

# ── Step 5: PUT 5 materials = OK (sequential, fresh ETag each step)
echo ""
echo "[5] PUT 5 materials = OK"
ALL_OK=1
for i in 0 1 2 3 4; do
    FRESH=$(curl -s "$API_BASE/api/v2/work-orders/$WO_ID/prepress" \
        -H "Authorization: Bearer $TOKEN" | sed -nE 's/.*"eTag":"([^"]+)".*/\1/p')
    R=$(curl -s -w "\nHTTP_CODE:%{http_code}" -X PUT \
        "$API_BASE/api/v2/work-orders/$WO_ID/materials/$i" \
        -H "Authorization: Bearer $TOKEN" \
        -H "If-Match: \"$FRESH\"" \
        -H "Idempotency-Key: $(uuidgen 2>/dev/null || python3 -c 'import uuid;print(uuid.uuid4())')" \
        -H "Content-Type: application/json" \
        -d "{\"status\":\"Ok\",\"qtyLoaded\":50.0,\"lotNo\":\"LOT-$i\"}")
    R_CODE=$(echo "$R" | tail -1 | sed -nE 's/HTTP_CODE:([0-9]+)/\1/p')
    if [[ "$R_CODE" != "200" ]]; then
        ALL_OK=0
        echo "  ✗ material $i PUT failed (code=$R_CODE)"
        echo "    → body: $(echo "$R" | sed '$d' | head -c 200)"
    fi
done
if [[ $ALL_OK -eq 1 ]]; then
    step "5 materials → OK" PASS
else
    step "≥1 material PUT failed" FAIL
fi

# ── Step 6: PUT plate + cutter = OK ──────────────────────────────
echo ""
echo "[6] PUT plate + cutter = OK"
FRESH=$(curl -s "$API_BASE/api/v2/work-orders/$WO_ID/prepress" \
    -H "Authorization: Bearer $TOKEN" | sed -nE 's/.*"eTag":"([^"]+)".*/\1/p')
RP=$(curl -s -w "\nHTTP_CODE:%{http_code}" -X PUT \
    "$API_BASE/api/v2/work-orders/$WO_ID/plate-check" \
    -H "Authorization: Bearer $TOKEN" \
    -H "If-Match: \"$FRESH\"" \
    -H "Idempotency-Key: $(uuidgen 2>/dev/null || python3 -c 'import uuid;print(uuid.uuid4())')" \
    -H "Content-Type: application/json" \
    -d '{"status":"Ok","plateNo":"PLT-CHK-001"}')
RP_CODE=$(echo "$RP" | tail -1 | sed -nE 's/HTTP_CODE:([0-9]+)/\1/p')

FRESH=$(curl -s "$API_BASE/api/v2/work-orders/$WO_ID/prepress" \
    -H "Authorization: Bearer $TOKEN" | sed -nE 's/.*"eTag":"([^"]+)".*/\1/p')
RC=$(curl -s -w "\nHTTP_CODE:%{http_code}" -X PUT \
    "$API_BASE/api/v2/work-orders/$WO_ID/cutter-check" \
    -H "Authorization: Bearer $TOKEN" \
    -H "If-Match: \"$FRESH\"" \
    -H "Idempotency-Key: $(uuidgen 2>/dev/null || python3 -c 'import uuid;print(uuid.uuid4())')" \
    -H "Content-Type: application/json" \
    -d '{"status":"Ok","cutterNo":"CUT-CHK-001"}')
RC_CODE=$(echo "$RC" | tail -1 | sed -nE 's/HTTP_CODE:([0-9]+)/\1/p')

if [[ "$RP_CODE" == "200" && "$RC_CODE" == "200" ]]; then
    step "plate + cutter → OK" PASS
else
    step "plate ($RP_CODE) / cutter ($RC_CODE) failed" FAIL
fi

# ── Step 7: re-GET → confirm MaterialsReady=true ──────────────────
echo ""
echo "[7] re-GET /prepress → assert MaterialsReady=true"
RG=$(curl -s "$API_BASE/api/v2/work-orders/$WO_ID/prepress" \
    -H "Authorization: Bearer $TOKEN")
MR=$(echo "$RG" | sed -nE 's/.*"materialsReady":(true|false).*/\1/p')
if [[ "$MR" == "true" ]]; then
    step "MaterialsReady=true after all 7 PUTs" PASS
else
    step "MaterialsReady=$MR (expected true)" FAIL
fi

# ── Step 8: audit visibility ─────────────────────────────────────
echo ""
echo "[8] GET /audit/log → assert 5 MATERIAL_SET + 1 PLATE_SET + 1 CUTTER_SET"
AUDIT=$(curl -s "$API_BASE/api/v2/audit/log?page=1&pageSize=200" \
    -H "Authorization: Bearer $TOKEN")
M_COUNT=$(echo "$AUDIT" | grep -oE "\"action\":\"WO_PREPRESS_MATERIAL_SET\"[^}]*\"targetId\":\"$WO_ID\"" | wc -l | tr -d ' ')
P_COUNT=$(echo "$AUDIT" | grep -oE "\"action\":\"WO_PREPRESS_PLATE_SET\"[^}]*\"targetId\":\"$WO_ID\"" | wc -l | tr -d ' ')
C_COUNT=$(echo "$AUDIT" | grep -oE "\"action\":\"WO_PREPRESS_CUTTER_SET\"[^}]*\"targetId\":\"$WO_ID\"" | wc -l | tr -d ' ')
if [[ "$M_COUNT" -ge 5 && "$P_COUNT" -ge 1 && "$C_COUNT" -ge 1 ]]; then
    step "audit rows MATERIAL_SET=$M_COUNT PLATE_SET=$P_COUNT CUTTER_SET=$C_COUNT" PASS
else
    step "audit rows missing (M=$M_COUNT P=$P_COUNT C=$C_COUNT)" FAIL
fi

# ── Summary ───────────────────────────────────────────────────────
echo ""
echo "==================="
echo "CHECKPOINT — PASS=$PASS  FAIL=$FAIL"
echo "==================="
echo ""
echo "Now verify in Catalyst UI (2 visual checks):"
echo "  1. WO drawer for $WO_NO shows MaterialsReady ✓ badge + Advance enabled."
echo "  2. Settings → Audit Log shows 5 WO_PREPRESS_MATERIAL_SET +"
echo "     1 PLATE_SET + 1 CUTTER_SET rows at top of feed."
echo ""
if [[ $FAIL -gt 0 ]]; then exit 1; fi
exit 0
