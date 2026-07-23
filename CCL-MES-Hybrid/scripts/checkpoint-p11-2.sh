#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────
# P11-2 — RoutingController operator checkpoint (S12 discipline).
# Self-managed API lifecycle; boots on an ISOLATED port + a COPY of the
# live DB (live :5100/:5050 + data/ccl_mes.db NEVER touched). Walks the
# full T3 fork-join luồng qua wire (curl):
#   materialize (fork PREPRESS→SPLIT) → advance 4 leg → join → FQC_PENDING.
#
# Per-step [N/TOTAL] labels + SUMMARY always printed in EXIT trap +
# non-zero exit on any FAIL (S12).
# ─────────────────────────────────────────────────────────────────────
set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PORT=5123
API="http://127.0.0.1:$PORT"
SRC_DB="${MES_DB_PATH:-$REPO/data/ccl_mes.db}"
CK_DB="/tmp/p11-2-checkpoint-$$.db"
LOG="/tmp/p11-2-checkpoint-api-$$.log"
TOTAL=11; STEP=0; FAILS=0
API_PID=""

echo "[ctx] REPO=$REPO"
echo "[ctx] source DB=$SRC_DB  (copied → $CK_DB; source NEVER written)"
echo "[ctx] API=$API (isolated)"

cleanup() {
  [ -n "$API_PID" ] && kill "$API_PID" 2>/dev/null
  final_summary
  rm -f "$CK_DB"
}
final_summary() {
  echo ""
  echo "═══ P11-2 CHECKPOINT SUMMARY ═══"
  echo "steps run: $STEP/$TOTAL · FAILS: $FAILS"
  [ "$FAILS" = "0" ] && echo "RESULT: PASS" || echo "RESULT: FAIL"
}
trap cleanup EXIT
step(){ STEP=$((STEP+1)); echo "[$STEP/$TOTAL] $1"; }
ok(){ echo "  ✓ $1"; }
bad(){ echo "  ✗ $1"; FAILS=$((FAILS+1)); }
json(){ python3 -c "import sys,json;d=json.load(sys.stdin);print(d$1)" 2>/dev/null; }

# ── prep isolated DB copy ───────────────────────────────────────────
step "Copy live DB → isolated checkpoint DB"
cp "$SRC_DB" "$CK_DB" && ok "copied" || { bad "copy failed"; exit 1; }

step "Seed T3 WO (silkscreen: in ∥ cắt tape → assembly → cắt) on copy"
PART="P11CK-$(date +%s)"
sqlite3 "$CK_DB" <<SQL
INSERT INTO Customers (Code,Name,CreatedAt) VALUES ('CK-$PART','ck','2026-07-23');
INSERT INTO Products (ProductCode,Name,CustomerId,CreatedAt) VALUES ('$PART','ck',(SELECT max(Id) FROM Customers),'2026-07-23');
INSERT INTO RoutingOperations (PartNo,OpNo,Operation,WorkCenterNo,CreatedAt) VALUES
 ('$PART','10','Silkscreen print','MSS01','2026-07-23'),
 ('$PART','20','CẮT TAPE',NULL,'2026-07-23'),
 ('$PART','30','DÁN TAPE với semi-in',NULL,'2026-07-23'),
 ('$PART','40','CẮT OUTLINE',NULL,'2026-07-23');
INSERT INTO WorkOrders (WoNo,CustomerId,ProductId,ProductName,TargetQty,Uom,ProducedQty,CurrentStep,Status,Priority,MaterialsReady,SetupConfirmed,RohsOk,MesPhase,QtyDoneCached,QtyNgCached,CreatedAt)
 VALUES ('WO-P11CK','1',(SELECT max(Id) FROM Products),'ck',100,'pcs',0,'PrePressCheck','InProgress',0,0,0,0,'PREPRESS',0,0,'2026-07-23');
SQL
WOID=$(sqlite3 "$CK_DB" "SELECT Id FROM WorkOrders WHERE WoNo='WO-P11CK';")
[ -n "$WOID" ] && ok "WO id=$WOID seeded (PREPRESS)" || { bad "seed failed"; exit 1; }

# ── boot API ────────────────────────────────────────────────────────
step "Boot API on :$PORT against copy DB"
# appsettings.json pins Urls=:5100 (thắng ASPNETCORE_URLS) → override qua
# command-line arg (--urls, precedence cao nhất) để không đụng live :5100.
MES_DB_PATH="$CK_DB" \
  nohup dotnet run --project "$REPO/CCL-MES-Hybrid/src/CCL.MES.Api" --no-launch-profile --urls "$API" >"$LOG" 2>&1 &
API_PID=$!
UP=0
for _ in $(seq 1 120); do
  if curl -s "$API/api/v2/health" >/dev/null 2>&1; then UP=1; break; fi
  sleep 1
done
if [ "$UP" = "1" ]; then ok "API up (pid=$API_PID)"; else bad "API did not boot"; tail -8 "$LOG"; exit 1; fi

step "Login operator (Bearer) — RoutingController là [Authorize] mọi role"
# Demo account pwd=username (admin live pwd có thể đã đổi → dùng operator).
TOK=$(curl -s -X POST "$API/api/v2/auth/login" -H "Content-Type: application/json" \
  -d '{"username":"operator","password":"operator","deviceId":"checkpoint-p11-2"}' | json "['accessToken']")
[ -n "$TOK" ] && ok "token acquired" || { bad "login failed"; exit 1; }
AUTH="Authorization: Bearer $TOK"

woetag(){ curl -s -H "$AUTH" "$API/api/v2/work-orders/$WOID/legs" | json "['woETag']"; }
legid(){ curl -s -H "$AUTH" "$API/api/v2/work-orders/$WOID/legs" | python3 -c "import sys,json;print([l['legId'] for l in json.load(sys.stdin)['legs'] if l['sequence']==$1][0])"; }
legetag(){ curl -s -H "$AUTH" "$API/api/v2/work-orders/$WOID/legs" | python3 -c "import sys,json;print([l['legETag'] for l in json.load(sys.stdin)['legs'] if l['sequence']==$1][0])"; }

# ── fork ────────────────────────────────────────────────────────────
step "Materialize (fork PREPRESS → SPLIT)"
ETAG=$(woetag)
MRSP=$(curl -s -X POST "$API/api/v2/work-orders/$WOID/legs/materialize" -H "$AUTH" \
  -H "If-Match: \"$ETAG\"" -H "Idempotency-Key: $(uuidgen)" -H "Content-Type: application/json" -d '{}')
[ "$(echo "$MRSP" | json "['forked']")" = "True" ] && [ "$(echo "$MRSP" | json "['legCount']")" = "4" ] \
  && ok "forked into 4 legs, MesPhase=$(echo "$MRSP" | json "['mesPhase']")" || bad "materialize wrong: $MRSP"

step "GET legs — 4 leg + 3 cạnh + CUT terminal"
V=$(curl -s -H "$AUTH" "$API/api/v2/work-orders/$WOID/legs")
LN=$(echo "$V" | json "['legs'].__len__()"); EN=$(echo "$V" | json "['edges'].__len__()")
[ "$LN" = "4" ] && [ "$EN" = "3" ] && ok "legs=$LN edges=$EN" || bad "view wrong legs=$LN edges=$EN"

# ── advance PRINT + TAPE fully to LEG_DONE ─────────────────────────
advance_leg() { # $1=seq  $2=target
  local lid et rsp; lid=$(legid "$1"); et=$(legetag "$1")
  rsp=$(curl -s -X POST "$API/api/v2/work-orders/$WOID/legs/$lid/advance" -H "$AUTH" \
    -H "If-Match: \"$et\"" -H "Idempotency-Key: $(uuidgen)" -H "Content-Type: application/json" -d "{\"toPhase\":\"$2\"}")
  echo "$rsp" | json "['legPhase']"
}
run_to_done() { # $1=seq
  for P in SETTING IPQC_WAIT IPQC_APPROVED RUNNING LEG_DONE; do advance_leg "$1" "$P" >/dev/null; done
}

step "Advance PRINT (seq0) → LEG_DONE"
run_to_done 0; sqlite3 "$CK_DB" "UPDATE WoLegs SET QtyDoneCached=100 WHERE WorkOrderId=$WOID AND Sequence=0;"
[ "$(sqlite3 "$CK_DB" "SELECT LegPhase FROM WoLegs WHERE WorkOrderId=$WOID AND Sequence=0;")" = "LEG_DONE" ] && ok "PRINT done" || bad "PRINT not done"

step "Advance TAPE (seq1) → LEG_DONE"
run_to_done 1; sqlite3 "$CK_DB" "UPDATE WoLegs SET QtyDoneCached=100 WHERE WorkOrderId=$WOID AND Sequence=1;"
[ "$(sqlite3 "$CK_DB" "SELECT LegPhase FROM WoLegs WHERE WorkOrderId=$WOID AND Sequence=1;")" = "LEG_DONE" ] && ok "TAPE done" || bad "TAPE not done"

step "Advance ASSEMBLY (seq2) → LEG_DONE (HARD gate mở vì PRINT+TAPE done)"
run_to_done 2
[ "$(sqlite3 "$CK_DB" "SELECT LegPhase FROM WoLegs WHERE WorkOrderId=$WOID AND Sequence=2;")" = "LEG_DONE" ] && ok "ASSEMBLY done" || bad "ASSEMBLY not done (gate?)"

step "Advance CUT (seq3, terminal) → LEG_DONE ⇒ JOIN SPLIT→FQC_PENDING"
for P in SETTING IPQC_WAIT IPQC_APPROVED RUNNING; do advance_leg 3 "$P" >/dev/null; done
LID=$(legid 3); ET=$(legetag 3)
JRSP=$(curl -s -X POST "$API/api/v2/work-orders/$WOID/legs/$LID/advance" -H "$AUTH" \
  -H "If-Match: \"$ET\"" -H "Idempotency-Key: $(uuidgen)" -H "Content-Type: application/json" -d '{"toPhase":"LEG_DONE"}')
[ "$(echo "$JRSP" | json "['joined']")" = "True" ] && [ "$(echo "$JRSP" | json "['woMesPhase']")" = "FQC_PENDING" ] \
  && ok "JOINED → WO MesPhase=FQC_PENDING" || bad "join failed: $JRSP"

step "Audit emitted: WO_SPLIT_FORKED + WO_SPLIT_JOINED (SQL — /audit/log là AdminOnly)"
FCNT=$(sqlite3 "$CK_DB" "SELECT COUNT(*) FROM AuditLogs WHERE Action='WO_SPLIT_FORKED' AND TargetId='$WOID';")
JCNT=$(sqlite3 "$CK_DB" "SELECT COUNT(*) FROM AuditLogs WHERE Action='WO_SPLIT_JOINED' AND TargetId='$WOID';")
[ "$FCNT" -ge 1 ] && [ "$JCNT" -ge 1 ] && ok "WO_SPLIT_FORKED=$FCNT WO_SPLIT_JOINED=$JCNT" || bad "audit missing (forked=$FCNT joined=$JCNT)"

# EXIT trap prints SUMMARY + cleans up.
[ "$FAILS" = "0" ]
