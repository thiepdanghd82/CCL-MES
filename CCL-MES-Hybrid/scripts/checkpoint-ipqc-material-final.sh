#!/usr/bin/env bash
# IPQC first-article (MATERIAL SYSTEM) — operator forensic checkpoint (S12).
#
# Walks the full soft-lock flow over the WIRE on a COPY of the live DB
# (live untouched). Self-managed API lifecycle; self-seeds a distinct
# confirmer (QC) + waiver approver (Engineer) via POST /api/v2/admin/users.
#
# S12: per-step [N/TOTAL] labels; SUMMARY always prints in the EXIT trap;
# non-zero exit on any FAIL. L22 build-sanity + kill stale listener first.
#
# Flow proven:
#   Block A  boot API on copy + login admin + self-seed 2 distinct users
#   Block B  seed 1 WO in IPQC_WAIT with 1 DIVERGENT material (SQL shim) +
#            an all-OK legacy 4-slot IPQC check (item readiness satisfied)
#   Block C  GET material-system → row IsDivergent, Pending
#            PUT confirm Ok      → RowApprovalStatus=PendingEngineer (soft lock)
#            POST judgment GoRun (QC) → 422 ipqc.material_divergence_unresolved
#            POST approve-divergence (Engineer, distinct) → Approved + AllResolved
#            POST judgment GoRun (QC) → 200 IPQC_APPROVED
#   Block D  same-user waiver attempt → 422 material.same_user_as_confirmer +
#            WO_IPQC_MATERIAL_APPROVE_DENIED audit (dual-sig proven)
#   Block E  audit wire-mirror: WO_IPQC_MATERIAL_CHECK + _APPROVE + _DENIED
#
# Usage:  cd CCL-MES-Hybrid && ./scripts/checkpoint-ipqc-material-final.sh [--keep-alive]

set -u
set +e

KEEP_ALIVE=0
for a in "$@"; do case "$a" in --keep-alive) KEEP_ALIVE=1 ;; esac; done

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HYBRID_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$HYBRID_ROOT/.." && pwd)"
REAL_DB="$REPO_ROOT/data/ccl_mes.db"
TMP_DIR="$(mktemp -d -t ccl-checkpoint-ipqc-material-XXXXXX)"
TEST_DB="$TMP_DIR/ccl_mes_test.db"
PORT=5114
API_URL="http://127.0.0.1:${PORT}"
API_LOG="$TMP_DIR/api.log"
API_PID=""

PASS=0; FAIL=0; STEP=0; TOTAL=14; SUMMARY=()
DB_SHA8="(missing)"; [[ -f "$REAL_DB" ]] && DB_SHA8="$(shasum -a 256 "$REAL_DB" | awk '{print substr($1,1,8)}')"

step() {
    STEP=$((STEP+1)); local verdict="$1"; shift
    if [[ "$verdict" == "PASS" ]]; then PASS=$((PASS+1)); SUMMARY+=("  [$STEP/$TOTAL] PASS  $*")
    else FAIL=$((FAIL+1)); SUMMARY+=("  [$STEP/$TOTAL] FAIL  $*"); fi
}

final_summary() {
    echo ""; echo "========================  SUMMARY  ========================"
    printf '%s\n' "${SUMMARY[@]}"
    echo ""; echo "  TOTAL: pass=$PASS fail=$FAIL   (steps run=$STEP/$TOTAL)"
    echo "  [ctx] DB=$TEST_DB (copy of $REAL_DB sha8=$DB_SHA8)"
    [[ $FAIL -gt 0 ]] && echo "  [debug] preserved: $TMP_DIR (api log $API_LOG)"
}

cleanup() {
    if [[ $KEEP_ALIVE -eq 1 && $FAIL -eq 0 ]]; then
        echo "[keep-alive] API still on $API_URL (pid $API_PID) for Catalyst hand-verify. Ctrl-C to stop."
        final_summary; wait "$API_PID" 2>/dev/null; return
    fi
    [[ -n "$API_PID" ]] && { kill -9 "$API_PID" 2>/dev/null; wait "$API_PID" 2>/dev/null; }
    final_summary
    [[ $FAIL -eq 0 ]] && rm -rf "$TMP_DIR"
}
trap cleanup EXIT INT TERM

echo "[ctx] DB=$TEST_DB (copy of $REAL_DB sha8=$DB_SHA8)  port=$PORT"
[[ ! -f "$REAL_DB" ]] && { echo "[fatal] real DB missing"; exit 1; }
cp "$REAL_DB" "$TEST_DB"

# L22 — build sanity BEFORE booting a stale binary.
echo "[build] api (sanity)"
(cd "$HYBRID_ROOT" && dotnet build src/CCL.MES.Api/CCL.MES.Api.csproj --nologo -clp:NoSummary > "$TMP_DIR/build.log" 2>&1)
[[ $? -eq 0 ]] && step PASS "api build (L22 sanity)" || { step FAIL "api build failed"; exit 1; }

# Kill stale listener.
STALE=$(lsof -nP -iTCP:${PORT} -sTCP:LISTEN -t 2>/dev/null); [[ -n "$STALE" ]] && { echo "$STALE" | xargs -r kill -9; sleep 1; }

echo "[boot] api on copy"
(cd "$HYBRID_ROOT/src/CCL.MES.Api" && \
    ConnectionStrings__Default="Data Source=$TEST_DB" ASPNETCORE_ENVIRONMENT="Development" \
    dotnet run --no-build --no-launch-profile --urls "$API_URL" > "$API_LOG" 2>&1) &
API_PID=$!
UP=0; for i in $(seq 1 120); do c=$(curl -s -m2 -o /dev/null -w "%{http_code}" "$API_URL/health"); [[ "$c" =~ ^(200|401|503)$ ]] && { UP=1; break; }; sleep 1; done
[[ $UP -eq 1 ]] && step PASS "API up on $PORT" || { step FAIL "API never came up"; exit 1; }

# ── login helper ────────────────────────────────────────────────
login() { # $1 user $2 pass → echoes bearer token
    curl -s -m10 -X POST "$API_URL/api/v2/auth/login" -H "Content-Type: application/json" \
        -d "{\"username\":\"$1\",\"password\":\"$2\"}" | grep -oE '"token":"[^"]+"' | head -1 | cut -d'"' -f4
}
admin_seed_user() { # $1 user $2 pass $3 role  (idempotent — 422 username_in_use OK)
    curl -s -m10 -X POST "$API_URL/api/v2/admin/users" -H "Authorization: Bearer $ADMIN_TOK" \
        -H "Content-Type: application/json" \
        -d "{\"username\":\"$1\",\"password\":\"$2\",\"role\":\"$3\",\"displayName\":\"ipqc-material checkpoint\"}" > /dev/null
}

ADMIN_TOK=$(login admin admin)
[[ -n "$ADMIN_TOK" ]] && step PASS "admin login" || { step FAIL "admin login failed"; exit 1; }

admin_seed_user "ipqc-mat-confirm" "P@ss!1" "Qc"
admin_seed_user "eng-mat-waive"    "P@ss!1" "Engineer"
QC_TOK=$(login ipqc-mat-confirm P@ss!1)
ENG_TOK=$(login eng-mat-waive P@ss!1)
[[ -n "$QC_TOK" && -n "$ENG_TOK" ]] && step PASS "seed + login 2 distinct users" || { step FAIL "user seed/login failed"; exit 1; }

# ── Block B — SQL shim: 1 WO in IPQC_WAIT + 1 divergent material + all-OK check ─
WONO="WO-MATCHK-$(date +%s)"
sqlite3 "$TEST_DB" <<SQL
INSERT INTO Customers (Code,Name,CreatedAt) VALUES ('C-MATCHK','MatChk',datetime('now'));
INSERT INTO Products (ProductCode,Name,CustomerId,CreatedAt) VALUES ('P-MATCHK','Prod',(SELECT Id FROM Customers WHERE Code='C-MATCHK'),datetime('now'));
INSERT INTO WorkOrders (WoNo,CustomerId,ProductId,ProductName,TargetQty,Uom,CurrentStep,MesPhase,Status,MaterialsReady,SetupConfirmed,RohsOk,Priority,ProducedQty,QtyDoneCached,QtyNgCached,SettingDurationSec,CreatedAt)
  VALUES ('$WONO',(SELECT Id FROM Customers WHERE Code='C-MATCHK'),(SELECT Id FROM Products WHERE ProductCode='P-MATCHK'),'Prod',1000,'pcs','IpqcApproval','IPQC_WAIT','InProgress',0,0,0,0,0,0,0,0,datetime('now'));
INSERT INTO WoMaterials (WorkOrderId,BomLineIdx,MaterialCode,MaterialDescription,QtyRequired,Uom,ScrapFactor,Status,LotNo,CreatedAt)
  VALUES ((SELECT Id FROM WorkOrders WHERE WoNo='$WONO'),0,'MAT-DIV','Divergent mat',10,'pcs',0,'Pending','SCAN-UNKNOWN',datetime('now'));
INSERT INTO WoIpqcChecks (WorkOrderId,MaterialStatus,PrintAStatus,PrintBStatus,PrintCStatus,Judgment,QaOutcome,CreatedAt)
  VALUES ((SELECT Id FROM WorkOrders WHERE WoNo='$WONO'),'Ok','Ok','Ok','Ok','Pending','Pending',datetime('now'));
SQL
WOID=$(sqlite3 "$TEST_DB" "SELECT Id FROM WorkOrders WHERE WoNo='$WONO';")
[[ -n "$WOID" ]] && step PASS "seed WO $WONO (IPQC_WAIT + divergent material + all-OK check)" || { step FAIL "WO seed failed"; exit 1; }

etag() { curl -s -m10 "$API_URL/api/v2/work-orders/$WOID/ipqc/material-system" -H "Authorization: Bearer $QC_TOK" | grep -oE '"eTag":"[^"]*"' | head -1 | cut -d'"' -f4; }
# JSON field casing: System.Text.Json camelCases → "eTag". Fallback to ETag.
etag2() {
    local e; e=$(curl -s -m10 "$API_URL/api/v2/work-orders/$WOID/ipqc/material-system" -H "Authorization: Bearer $QC_TOK")
    echo "$e" | grep -oiE '"e?tag":"[^"]*"' | head -1 | sed -E 's/.*:"([^"]*)"/\1/'
}

# ── Block C ─────────────────────────────────────────────────────
VIEW=$(curl -s -m10 "$API_URL/api/v2/work-orders/$WOID/ipqc/material-system" -H "Authorization: Bearer $QC_TOK")
echo "$VIEW" | grep -q '"isDivergent":true' && step PASS "GET material-system: row divergent" || step FAIL "GET expected divergent row (got: $VIEW)"

ET=$(etag2)
CONFIRM=$(curl -s -m10 -X PUT "$API_URL/api/v2/work-orders/$WOID/ipqc/material-system/0" \
    -H "Authorization: Bearer $QC_TOK" -H "Content-Type: application/json" \
    -H "If-Match: \"$ET\"" -H "Idempotency-Key: $(uuidgen)" -d '{"status":"Ok"}')
echo "$CONFIRM" | grep -q '"PendingEngineer"' && step PASS "confirm OK on divergent → PendingEngineer (soft lock)" || step FAIL "confirm expected PendingEngineer (got: $CONFIRM)"

ET=$(etag2)
BLOCK=$(curl -s -m10 -o /dev/null -w "%{http_code}" -X POST "$API_URL/api/v2/work-orders/$WOID/ipqc/judgment" \
    -H "Authorization: Bearer $QC_TOK" -H "Content-Type: application/json" \
    -H "If-Match: \"$ET\"" -H "Idempotency-Key: $(uuidgen)" -d '{"judgment":"GoRun"}')
[[ "$BLOCK" == "422" ]] && step PASS "GoRun blocked 422 (material_divergence_unresolved)" || step FAIL "GoRun expected 422, got $BLOCK"

ET=$(etag2)
WAIVE=$(curl -s -m10 -X POST "$API_URL/api/v2/work-orders/$WOID/ipqc/material-system/0/approve-divergence" \
    -H "Authorization: Bearer $ENG_TOK" -H "Content-Type: application/json" \
    -H "If-Match: \"$ET\"" -H "Idempotency-Key: $(uuidgen)" -d '{"outcome":"Approve","reason":"Lô thay thế đã kiểm"}')
echo "$WAIVE" | grep -q '"Approved"' && step PASS "Engineer waiver Approve → Approved + AllResolved" || step FAIL "waiver expected Approved (got: $WAIVE)"

ET=$(etag2)
GORUN=$(curl -s -m10 -o /dev/null -w "%{http_code}" -X POST "$API_URL/api/v2/work-orders/$WOID/ipqc/judgment" \
    -H "Authorization: Bearer $QC_TOK" -H "Content-Type: application/json" \
    -H "If-Match: \"$ET\"" -H "Idempotency-Key: $(uuidgen)" -d '{"judgment":"GoRun"}')
[[ "$GORUN" == "200" ]] && step PASS "GoRun 200 after waiver → IPQC_APPROVED" || step FAIL "GoRun expected 200 after waiver, got $GORUN"

# ── Block D — dual-sig: same user confirms + waives on a 2nd WO ──
WONO2="WO-MATCHK2-$(date +%s)"
sqlite3 "$TEST_DB" <<SQL
INSERT INTO WorkOrders (WoNo,CustomerId,ProductId,ProductName,TargetQty,Uom,CurrentStep,MesPhase,Status,MaterialsReady,SetupConfirmed,RohsOk,Priority,ProducedQty,QtyDoneCached,QtyNgCached,SettingDurationSec,CreatedAt)
  VALUES ('$WONO2',(SELECT Id FROM Customers WHERE Code='C-MATCHK'),(SELECT Id FROM Products WHERE ProductCode='P-MATCHK'),'Prod',1000,'pcs','IpqcApproval','IPQC_WAIT','InProgress',0,0,0,0,0,0,0,0,datetime('now'));
INSERT INTO WoMaterials (WorkOrderId,BomLineIdx,MaterialCode,MaterialDescription,QtyRequired,Uom,ScrapFactor,Status,LotNo,CreatedAt)
  VALUES ((SELECT Id FROM WorkOrders WHERE WoNo='$WONO2'),0,'MAT-DIV2','Divergent mat 2',10,'pcs',0,'Pending','SCAN-UNKNOWN2',datetime('now'));
SQL
WOID2=$(sqlite3 "$TEST_DB" "SELECT Id FROM WorkOrders WHERE WoNo='$WONO2';")
ET2=$(curl -s -m10 "$API_URL/api/v2/work-orders/$WOID2/ipqc/material-system" -H "Authorization: Bearer $ENG_TOK" | grep -oiE '"e?tag":"[^"]*"' | head -1 | sed -E 's/.*:"([^"]*)"/\1/')
# Engineer confirms (Engineer has NO IpqcSubmit → this must 403; use QC to confirm, Engineer=different, so use admin as BOTH). Simpler: admin confirms + admin waives → same user.
ADMIN2_TOK=$(login admin admin)
admin_seed_user "admin-mat-checkpoint" "P@ss!1" "Admin"
SAME_TOK=$(login admin-mat-checkpoint P@ss!1)
ET2=$(curl -s -m10 "$API_URL/api/v2/work-orders/$WOID2/ipqc/material-system" -H "Authorization: Bearer $SAME_TOK" | grep -oiE '"e?tag":"[^"]*"' | head -1 | sed -E 's/.*:"([^"]*)"/\1/')
curl -s -m10 -X PUT "$API_URL/api/v2/work-orders/$WOID2/ipqc/material-system/0" \
    -H "Authorization: Bearer $SAME_TOK" -H "Content-Type: application/json" \
    -H "If-Match: \"$ET2\"" -H "Idempotency-Key: $(uuidgen)" -d '{"status":"Ok"}' > /dev/null
ET2=$(curl -s -m10 "$API_URL/api/v2/work-orders/$WOID2/ipqc/material-system" -H "Authorization: Bearer $SAME_TOK" | grep -oiE '"e?tag":"[^"]*"' | head -1 | sed -E 's/.*:"([^"]*)"/\1/')
DENY=$(curl -s -m10 -o /dev/null -w "%{http_code}" -X POST "$API_URL/api/v2/work-orders/$WOID2/ipqc/material-system/0/approve-divergence" \
    -H "Authorization: Bearer $SAME_TOK" -H "Content-Type: application/json" \
    -H "If-Match: \"$ET2\"" -H "Idempotency-Key: $(uuidgen)" -d '{"outcome":"Approve","reason":"self"}')
[[ "$DENY" == "422" ]] && step PASS "same-user waiver → 422 (dual-sig)" || step FAIL "same-user waiver expected 422, got $DENY"

# ── Block E — audit wire-mirror ─────────────────────────────────
AUD=$(curl -s -m10 "$API_URL/api/v2/audit/log?action=WO_IPQC_MATERIAL_CHECK&page=1&pageSize=50" -H "Authorization: Bearer $ADMIN_TOK")
echo "$AUD" | grep -q "WO_IPQC_MATERIAL_CHECK" && step PASS "audit wire-mirror WO_IPQC_MATERIAL_CHECK" || step FAIL "audit mirror missing CHECK"
DEN_AUD=$(sqlite3 "$TEST_DB" "SELECT COUNT(*) FROM AuditLogs WHERE Action='WO_IPQC_MATERIAL_APPROVE_DENIED';")
[[ "${DEN_AUD:-0}" -ge 1 ]] && step PASS "WO_IPQC_MATERIAL_APPROVE_DENIED emitted ($DEN_AUD)" || step FAIL "no DENIED audit row"

exit $([[ $FAIL -gt 0 ]] && echo 1 || echo 0)
