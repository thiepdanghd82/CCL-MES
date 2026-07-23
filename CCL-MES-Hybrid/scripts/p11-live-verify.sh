#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────
# P11 LIVE E2E VERIFY — rebuild-boot-login-drive mọi trường hợp fork-join
# qua API thật, tự tạo dữ liệu. Chạy trên COPY của live DB (live +
# :5100/:5050 + data/ccl_mes.db KHÔNG bị đụng).
#
# Cases (mỗi case tự seed WO riêng):
#   1  Auth: login operator OK + 401 khi thiếu token
#   2  Header contract: 428 (no If-Match) · 400 (no Idem) · 409 (stale)
#   3  T1 combined-inline (in+bế 1 op) → materialize forked=FALSE
#   4  T2 print→cut (HP → RDC) → 2 leg, advance cả 2 → JOIN FQC_PENDING
#   5  T3 silkscreen tape+assembly → 4 leg + 3 edge; ASSEMBLY HARD-blocked
#      tới khi PRINT+TAPE done; rồi JOIN
#   6  Unmapped op → materialize 422 routing.unmapped
#   7  Rework: advance leg rồi rework → về PREPRESS
#   8  Concurrency per-leg: 10 advance song song → 1 win 9 conflict
# ─────────────────────────────────────────────────────────────────────
set -uo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PORT=5125; API="http://127.0.0.1:$PORT"
SRC_DB="${MES_DB_PATH:-$REPO/data/ccl_mes.db}"; CK_DB="/tmp/p11-live-$$.db"; LOG="/tmp/p11-live-api-$$.log"
PASS=0; FAIL=0; API_PID=""

echo "[ctx] source DB=$SRC_DB → copy $CK_DB (source NEVER written)"; echo "[ctx] API=$API (isolated)"
cleanup(){ [ -n "$API_PID" ] && kill "$API_PID" 2>/dev/null; echo ""; echo "═══ SUMMARY: PASS=$PASS FAIL=$FAIL ═══"; [ "$FAIL" = 0 ] && echo "RESULT: ALL PASS" || echo "RESULT: FAIL"; rm -f "$CK_DB"; }
trap cleanup EXIT
ok(){ PASS=$((PASS+1)); echo "  ✓ $1"; }
no(){ FAIL=$((FAIL+1)); echo "  ✗ $1"; }
J(){ python3 -c "import sys,json;print(json.load(sys.stdin)$1)" 2>/dev/null; }
UUID(){ python3 -c "import uuid;print(uuid.uuid4())"; }

# seed 1 WO PREPRESS + routing ops. $1=tag  $2..=  "op|wc" tuples
seed_wo(){ local tag="$1"; shift; local part="P11L-$tag-$(python3 -c 'import random;print(random.randint(1000,9999))')"
  sqlite3 "$CK_DB" "INSERT INTO Customers (Code,Name,CreatedAt) VALUES ('C-$part','c','2026-07-23');
    INSERT INTO Products (ProductCode,Name,CustomerId,CreatedAt) VALUES ('$part','p',(SELECT max(Id) FROM Customers),'2026-07-23');"
  local seq=10; for t in "$@"; do local op="${t%%|}"; op="${t%|*}"; local wc="${t#*|}"
    sqlite3 "$CK_DB" "INSERT INTO RoutingOperations (PartNo,OpNo,Operation,WorkCenterNo,CreatedAt) VALUES ('$part','$seq','$op',$([ -n "$wc" ] && echo "'$wc'" || echo NULL),'2026-07-23');"; seq=$((seq+10)); done
  sqlite3 "$CK_DB" "INSERT INTO WorkOrders (WoNo,CustomerId,ProductId,ProductName,TargetQty,Uom,ProducedQty,CurrentStep,Status,Priority,MaterialsReady,SetupConfirmed,RohsOk,MesPhase,QtyDoneCached,QtyNgCached,CreatedAt) VALUES ('WO-$part',1,(SELECT max(Id) FROM Products),'p',100,'pcs',0,'PrePressCheck','InProgress',0,0,0,0,'PREPRESS',0,0,'2026-07-23');"
  sqlite3 "$CK_DB" "SELECT Id FROM WorkOrders WHERE WoNo='WO-$part';"; }
woetag(){ curl -s -H "$AUTH" "$API/api/v2/work-orders/$1/legs" | J "['woETag']"; }
legid(){ curl -s -H "$AUTH" "$API/api/v2/work-orders/$1/legs" | python3 -c "import sys,json;print([l['legId'] for l in json.load(sys.stdin)['legs'] if l['sequence']==$2][0])" 2>/dev/null; }
legetag(){ curl -s -H "$AUTH" "$API/api/v2/work-orders/$1/legs" | python3 -c "import sys,json;print([l['legETag'] for l in json.load(sys.stdin)['legs'] if l['sequence']==$2][0])" 2>/dev/null; }
mat(){ curl -s -o /dev/null -w "%{http_code}" -X POST "$API/api/v2/work-orders/$1/legs/materialize" -H "$AUTH" -H "If-Match: \"$2\"" -H "Idempotency-Key: $(UUID)" -H "Content-Type: application/json" -d '{}'; }
mat_body(){ curl -s -X POST "$API/api/v2/work-orders/$1/legs/materialize" -H "$AUTH" -H "If-Match: \"$2\"" -H "Idempotency-Key: $(UUID)" -H "Content-Type: application/json" -d '{}'; }
adv(){ curl -s -X POST "$API/api/v2/work-orders/$1/legs/$2/advance" -H "$AUTH" -H "If-Match: \"$3\"" -H "Idempotency-Key: $(UUID)" -H "Content-Type: application/json" -d "{\"toPhase\":\"$4\"}"; }
run_to_done(){ for P in SETTING IPQC_WAIT IPQC_APPROVED RUNNING LEG_DONE; do adv "$1" "$(legid $1 $2)" "$(legetag $1 $2)" "$P" >/dev/null; done; }

# ── boot ───────────────────────────────────────────────────────────
echo "[1] Rebuild Api + boot on :$PORT (copy DB)"; cp "$SRC_DB" "$CK_DB"
MES_DB_PATH="$CK_DB" nohup dotnet run --project "$REPO/CCL-MES-Hybrid/src/CCL.MES.Api" --no-launch-profile --urls "$API" >"$LOG" 2>&1 & API_PID=$!
UP=0; for _ in $(seq 1 120); do curl -s "$API/api/v2/health" >/dev/null 2>&1 && { UP=1; break; }; sleep 1; done
[ "$UP" = 1 ] && ok "API booted" || { no "API boot"; tail -6 "$LOG"; exit 1; }

# ── 1. Auth ────────────────────────────────────────────────────────
echo "[2] Auth"
TOK=$(curl -s -X POST "$API/api/v2/auth/login" -H "Content-Type: application/json" -d '{"username":"operator","password":"operator","deviceId":"p11-live"}' | J "['accessToken']")
[ -n "$TOK" ] && ok "login operator (Bearer)" || no "login failed"
AUTH="Authorization: Bearer $TOK"
UNAUTH=$(curl -s -o /dev/null -w "%{http_code}" "$API/api/v2/work-orders/1/legs")
[ "$UNAUTH" = "401" ] && ok "GET legs không token → 401" || no "unauth = $UNAUTH (kỳ vọng 401)"

# ── 2. Header contract ─────────────────────────────────────────────
echo "[3] Header contract (428/400/409)"
HWO=$(seed_wo hdr "HP Indigo print|IDG01" "Die cut RDC|RDC01"); HE=$(woetag $HWO)
C428=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API/api/v2/work-orders/$HWO/legs/materialize" -H "$AUTH" -H "Idempotency-Key: $(UUID)" -d '{}')
[ "$C428" = "428" ] && ok "thiếu If-Match → 428" || no "no-ifmatch = $C428"
C400=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API/api/v2/work-orders/$HWO/legs/materialize" -H "$AUTH" -H "If-Match: \"$HE\"" -d '{}')
[ "$C400" = "400" ] && ok "thiếu Idempotency-Key → 400" || no "no-idem = $C400"
CSTALE=$(mat $HWO "AAAAstale")
[ "$CSTALE" = "409" ] && ok "If-Match cũ → 409 state_conflict" || no "stale = $CSTALE"

# ── 3. T1 combined-inline ──────────────────────────────────────────
echo "[4] T1 combined-inline (in+bế 1 op) → không fork"
T1=$(seed_wo t1 "IN BẾ inline 1 lượt|"); B1=$(mat_body $T1 "$(woetag $T1)")
[ "$(echo "$B1" | J "['forked']")" = "False" ] && [ "$(echo "$B1" | J "['legCount']")" = "1" ] && ok "T1 forked=false legCount=1 (WO đơn công đoạn)" || no "T1 sai: $B1"

# ── 4. T2 print→cut → join ─────────────────────────────────────────
echo "[5] T2 HP print → RDC cut → 2 leg → JOIN"
T2=$(seed_wo t2 "HP Indigo print|IDG01" "Die cut RDC|RDC01"); B2=$(mat_body $T2 "$(woetag $T2)")
[ "$(echo "$B2" | J "['legCount']")" = "2" ] && [ "$(echo "$B2" | J "['mesPhase']")" = "SPLIT" ] && ok "T2 fork 2 leg → SPLIT" || no "T2 fork sai: $B2"
run_to_done $T2 0   # PRINT (seq0)
for P in SETTING IPQC_WAIT IPQC_APPROVED RUNNING; do adv $T2 "$(legid $T2 1)" "$(legetag $T2 1)" "$P" >/dev/null; done
JB=$(adv $T2 "$(legid $T2 1)" "$(legetag $T2 1)" "LEG_DONE")   # CUT (terminal)
[ "$(echo "$JB" | J "['joined']")" = "True" ] && [ "$(echo "$JB" | J "['woMesPhase']")" = "FQC_PENDING" ] && ok "T2 JOIN → FQC_PENDING" || no "T2 join sai: $JB"

# ── 5. T3 silkscreen tape+assembly + HARD gate + join ──────────────
echo "[6] T3 silkscreen ∥ tape → assembly → cut (HARD gate)"
T3=$(seed_wo t3 "Silkscreen print|MSS01" "CẮT TAPE|" "DÁN TAPE với semi-in|" "CẮT OUTLINE|")
B3=$(mat_body $T3 "$(woetag $T3)")
[ "$(echo "$B3" | J "['legCount']")" = "4" ] && ok "T3 fork 4 leg → SPLIT" || no "T3 fork sai: $B3"
# ASSEMBLY (seq2) tới IPQC_APPROVED; PRINT/TAPE còn PREPRESS → HARD chặn RUNNING
for P in SETTING IPQC_WAIT IPQC_APPROVED; do adv $T3 "$(legid $T3 2)" "$(legetag $T3 2)" "$P" >/dev/null; done
GB=$(adv $T3 "$(legid $T3 2)" "$(legetag $T3 2)" "RUNNING")
[ "$(echo "$GB" | J "['errorCode']")" != "None" ] || echo "$GB" | grep -qi "inputs_not_ready" && ok "ASSEMBLY→RUNNING bị HARD chặn (semi chưa xong)" || no "HARD gate không chặn: $GB"
# hoàn tất PRINT + TAPE → HARD mở
run_to_done $T3 0; sqlite3 "$CK_DB" "UPDATE WoLegs SET QtyDoneCached=100 WHERE WorkOrderId=$T3 AND Sequence=0;"
run_to_done $T3 1; sqlite3 "$CK_DB" "UPDATE WoLegs SET QtyDoneCached=100 WHERE WorkOrderId=$T3 AND Sequence=1;"
GO=$(adv $T3 "$(legid $T3 2)" "$(legetag $T3 2)" "RUNNING")
[ "$(echo "$GO" | J "['ok']")" = "True" ] && ok "PRINT+TAPE done → ASSEMBLY vào RUNNING được" || no "gate không mở: $GO"
adv $T3 "$(legid $T3 2)" "$(legetag $T3 2)" "LEG_DONE" >/dev/null
run_to_done $T3 3   # CUT terminal
WP=$(curl -s -H "$AUTH" "$API/api/v2/work-orders/$T3/legs" | J "['mesPhase']")
[ "$WP" = "FQC_PENDING" ] && ok "T3 JOIN → FQC_PENDING" || no "T3 join sai: mesPhase=$WP"

# ── 6. Unmapped ────────────────────────────────────────────────────
echo "[7] Unmapped op → 422 routing.unmapped (không đoán)"
UM=$(seed_wo um "Công đoạn lạ ZZZ|ZZZ99" "In lụa|MSS01"); UB=$(mat_body $UM "$(woetag $UM)")
echo "$UB" | grep -qi "unmapped" && ok "op không map → 422 routing.unmapped" || no "unmapped không báo: $UB"

# ── 7. Rework ──────────────────────────────────────────────────────
echo "[8] Rework leg → PREPRESS"
RW=$(seed_wo rw "HP Indigo print|IDG01" "Die cut RDC|RDC01"); mat $RW "$(woetag $RW)" >/dev/null
adv $RW "$(legid $RW 0)" "$(legetag $RW 0)" "SETTING" >/dev/null
RB=$(curl -s -X POST "$API/api/v2/work-orders/$RW/legs/$(legid $RW 0)/rework" -H "$AUTH" -H "If-Match: \"$(legetag $RW 0)\"" -H "Idempotency-Key: $(UUID)" -H "Content-Type: application/json" -d '{"reason":"NG lệch màu"}')
[ "$(echo "$RB" | J "['legPhase']")" = "PREPRESS" ] && [ "$(echo "$RB" | J "['woMesPhase']")" = "SPLIT" ] && ok "rework → leg PREPRESS, WO giữ SPLIT (Q10)" || no "rework sai: $RB"

# ── 8. Concurrency per-leg ─────────────────────────────────────────
echo "[9] Concurrency: 10 advance song song cùng leg → 1 win 9 conflict"
CW=$(seed_wo cc "HP Indigo print|IDG01" "Die cut RDC|RDC01"); mat $CW "$(woetag $CW)" >/dev/null
LID=$(legid $CW 0); LE=$(legetag $CW 0); rm -f /tmp/p11cc.$$;
# --max-time chặn hang khi SQLite write-lock serialise 10 request song song
# (L25 macOS interleaving). Semantics per-leg 1-winner cũng được soak
# in-process RoutingControllerTests.Concurrent_advance chứng minh.
for i in $(seq 1 10); do (curl -s --max-time 20 -o /dev/null -w "%{http_code}\n" -X POST "$API/api/v2/work-orders/$CW/legs/$LID/advance" -H "$AUTH" -H "If-Match: \"$LE\"" -H "Idempotency-Key: $(UUID)" -H "Content-Type: application/json" -d '{"toPhase":"SETTING"}' >> /tmp/p11cc.$$) & done; wait
OKN=$(grep -c "^200" /tmp/p11cc.$$ 2>/dev/null || echo 0); CFN=$(grep -c "^409" /tmp/p11cc.$$ 2>/dev/null || echo 0); rm -f /tmp/p11cc.$$
[ "$OKN" = "1" ] && [ "$CFN" = "9" ] && ok "concurrency 1 win / 9 conflict (per-leg RowVersion)" || no "concurrency: 200=$OKN 409=$CFN"

[ "$FAIL" = 0 ]
