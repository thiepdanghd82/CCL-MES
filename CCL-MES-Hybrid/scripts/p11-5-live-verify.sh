#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────
# P11.5 LIVE E2E — Semi-Stock decoupling (keep-stock) qua API thật, tự
# tạo dữ liệu. Chạy trên COPY live DB (live + :5100/:5050 KHÔNG đụng).
# curl --max-time để không hang.
#
# Cases:
#   1 Auth login
#   2 Post 2 lô semi (hạn sớm + hạn muộn) → kho
#   3 GET kho: FEFO order (hạn sớm trước) + cờ Expiring
#   4 Seed WO có ASSEMBLY FROM_STOCK (IPQC_APPROVED)
#   5 Gate: advance ASSEMBLY→RUNNING TRƯỚC reserve → 422 (chặn)
#   6 Reserve FEFO đủ → 200, lô hạn sớm rút trước
#   7 Gate: advance ASSEMBLY→RUNNING SAU reserve → 200 (mở)
#   8 Consume → 200, lô rỗng → DEPLETED
#   9 Insufficient: reserve > tồn → 422 (không partial)
# ─────────────────────────────────────────────────────────────────────
set -uo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PORT=5126; API="http://127.0.0.1:$PORT"
SRC_DB="${MES_DB_PATH:-$REPO/data/ccl_mes.db}"; CK_DB="/tmp/p11-5-live-$$.db"; LOG="/tmp/p11-5-api-$$.log"
PASS=0; FAIL=0; API_PID=""; M="--max-time 25"
echo "[ctx] copy $SRC_DB → $CK_DB (source NEVER written); API=$API"
cleanup(){ [ -n "$API_PID" ] && kill "$API_PID" 2>/dev/null; echo ""; echo "═══ SUMMARY PASS=$PASS FAIL=$FAIL ═══"; [ "$FAIL" = 0 ] && echo "RESULT: ALL PASS" || echo "RESULT: FAIL"; rm -f "$CK_DB"; }
trap cleanup EXIT
ok(){ PASS=$((PASS+1)); echo "  ✓ $1"; }; no(){ FAIL=$((FAIL+1)); echo "  ✗ $1"; }
J(){ python3 -c "import sys,json;print(json.load(sys.stdin)$1)" 2>/dev/null; }
U(){ python3 -c "import uuid;print(uuid.uuid4())"; }

echo "[1] Copy live DB + apply pending migration (AddSemiStock) lên COPY"
cp "$SRC_DB" "$CK_DB"
# Live lags AddSemiStock (áp /tmp only tới giờ). API không auto-migrate →
# apply lên copy (isolated) để có bảng SemiLots. Live KHÔNG đụng.
MES_PROVIDER=Sqlite MES_CONNSTR="Data Source=$CK_DB" MES_DB_PATH="$CK_DB" \
  dotnet ef database update -p "$REPO/src/CCL.MES.Infrastructure" -s "$REPO/src/CCL.MES.Web" --no-build >/tmp/p11-5-mig-$$.log 2>&1 \
  && ok "copy migrated (SemiStock)" || { no "migrate copy fail"; tail -4 /tmp/p11-5-mig-$$.log; exit 1; }
echo "[1b] Boot API on :$PORT (copy DB)"
MES_DB_PATH="$CK_DB" nohup dotnet run --project "$REPO/CCL-MES-Hybrid/src/CCL.MES.Api" --no-launch-profile --urls "$API" >"$LOG" 2>&1 & API_PID=$!
UP=0; for _ in $(seq 1 120); do curl -s $M "$API/api/v2/health" >/dev/null 2>&1 && { UP=1; break; }; sleep 1; done
[ "$UP" = 1 ] && ok "API up" || { no "boot"; tail -6 "$LOG"; exit 1; }

echo "[2] Login operator"
TOK=$(curl -s $M -X POST "$API/api/v2/auth/login" -H "Content-Type: application/json" -d '{"username":"operator","password":"operator","deviceId":"p11-5"}' | J "['accessToken']")
[ -n "$TOK" ] && ok "login" || { no "login"; exit 1; }
AUTH="Authorization: Bearer $TOK"; SPEC=4321

postlot(){ curl -s $M -X POST "$API/api/v2/semi-lots" -H "$AUTH" -H "Idempotency-Key: $(U)" -H "Content-Type: application/json" -d "$1"; }
echo "[3] Post 2 lô semi (spec=$SPEC): hạn muộn 400 + hạn sớm 400"
L_LATE=$(postlot "{\"lotNo\":\"SEMI-LATE-$$\",\"semiKind\":\"PRINTED_SEMI\",\"qty\":400,\"specRevisionId\":$SPEC,\"sourceWorkOrderId\":1,\"expiryAt\":\"2026-12-01\"}" | J "['ok']")
L_EARLY=$(postlot "{\"lotNo\":\"SEMI-EARLY-$$\",\"semiKind\":\"PRINTED_SEMI\",\"qty\":400,\"specRevisionId\":$SPEC,\"sourceWorkOrderId\":1,\"expiryAt\":\"2026-07-28\"}" | J "['ok']")
[ "$L_LATE" = "True" ] && [ "$L_EARLY" = "True" ] && ok "post 2 lô OK" || no "post lô fail"

echo "[4] GET kho FEFO + cờ Expiring"
V=$(curl -s $M -H "$AUTH" "$API/api/v2/semi-lots?spec=$SPEC")
FIRST=$(echo "$V" | python3 -c "import sys,json;print(json.load(sys.stdin)['lots'][0]['lotNo'])" 2>/dev/null)
echo "$FIRST" | grep -q "EARLY" && ok "FEFO: lô hạn sớm đứng đầu ($FIRST)" || no "FEFO order sai: $FIRST"
EXPIRING=$(echo "$V" | python3 -c "import sys,json;print(sum(1 for l in json.load(sys.stdin)['lots'] if l['expiring']))" 2>/dev/null)
[ "$EXPIRING" -ge 1 ] && ok "cờ Expiring bật ($EXPIRING lô ≤7 ngày)" || no "expiring không bật"
AVAIL=$(echo "$V" | J "['totalAvailable']"); [ "$AVAIL" = "800" ] && ok "tồn khả dụng = 800" || no "tồn = $AVAIL"

echo "[5] Seed WO có ASSEMBLY FROM_STOCK (spec=$SPEC, target 500)"
sqlite3 "$CK_DB" "INSERT INTO Customers (Code,Name,CreatedAt) VALUES ('C-p115','c','2026-07-23');
 INSERT INTO Products (ProductCode,Name,CustomerId,CreatedAt) VALUES ('P-p115','p',(SELECT max(Id) FROM Customers),'2026-07-23');
 INSERT INTO WorkOrders (WoNo,CustomerId,ProductId,ProductName,TargetQty,Uom,ProducedQty,CurrentStep,Status,Priority,MaterialsReady,SetupConfirmed,RohsOk,MesPhase,QtyDoneCached,QtyNgCached,CreatedAt) VALUES ('WO-P115','1',(SELECT max(Id) FROM Products),'p',500,'pcs',0,'PrePressCheck','InProgress',0,0,0,0,'SPLIT',0,0,'2026-07-23');
 INSERT INTO WoLegs (WorkOrderId,Sequence,LegKind,Method,ProcessLine,SurfaceProfile,InputSource,LegPhase,SpecRevisionId,RowVersion,QtyDoneCached,QtyNgCached,CreatedAt) VALUES ((SELECT Id FROM WorkOrders WHERE WoNo='WO-P115'),0,'ASSEMBLY','Assembly','FINISHING','LITE','FROM_STOCK','IPQC_APPROVED',$SPEC,x'',0,0,'2026-07-23');"
WO=$(sqlite3 "$CK_DB" "SELECT Id FROM WorkOrders WHERE WoNo='WO-P115';")
LEG=$(sqlite3 "$CK_DB" "SELECT Id FROM WoLegs WHERE WorkOrderId=$WO AND Sequence=0;")
[ -n "$LEG" ] && ok "WO $WO / ASSEMBLY leg $LEG (FROM_STOCK, IPQC_APPROVED)" || { no "seed WO"; exit 1; }
legetag(){ curl -s $M -H "$AUTH" "$API/api/v2/work-orders/$WO/legs" | python3 -c "import sys,json;print([l['legETag'] for l in json.load(sys.stdin)['legs'] if l['sequence']==0][0])" 2>/dev/null; }
advrun(){ curl -s $M -o /dev/null -w "%{http_code}" -X POST "$API/api/v2/work-orders/$WO/legs/$LEG/advance" -H "$AUTH" -H "If-Match: \"$(legetag)\"" -H "Idempotency-Key: $(U)" -H "Content-Type: application/json" -d '{"toPhase":"RUNNING"}'; }

echo "[6] Gate: advance ASSEMBLY→RUNNING TRƯỚC reserve → chặn"
G1=$(advrun); [ "$G1" = "422" ] && ok "chưa reserve → RUNNING bị chặn (422)" || no "gate không chặn: $G1"

echo "[7] Reserve FEFO 500 → lô hạn sớm rút trước"
RB=$(curl -s $M -X POST "$API/api/v2/work-orders/$WO/legs/$LEG/semi/reserve" -H "$AUTH" -H "Idempotency-Key: $(U)" -H "Content-Type: application/json" -d '{"qty":500,"semiKind":"PRINTED_SEMI"}')
[ "$(echo "$RB" | J "['allocated']")" = "500" ] && ok "reserve 500 OK" || no "reserve sai: $RB"
V2=$(curl -s $M -H "$AUTH" "$API/api/v2/semi-lots?spec=$SPEC")
EARLY_AVAIL=$(echo "$V2" | python3 -c "import sys,json;print([l['qtyAvailable'] for l in json.load(sys.stdin)['lots'] if 'EARLY' in l['lotNo']][0])" 2>/dev/null)
[ "$EARLY_AVAIL" = "0" ] && ok "lô hạn sớm rút hết trước (FEFO)" || no "FEFO reserve sai: early avail=$EARLY_AVAIL"

echo "[8] Gate: advance ASSEMBLY→RUNNING SAU reserve → mở"
G2=$(advrun); [ "$G2" = "200" ] && ok "đã reserve đủ → RUNNING được (200)" || no "gate không mở: $G2"

echo "[9] Consume → lô DEPLETED"
CB=$(curl -s $M -o /dev/null -w "%{http_code}" -X POST "$API/api/v2/work-orders/$WO/legs/$LEG/semi/consume" -H "$AUTH" -H "Idempotency-Key: $(U)" -H "Content-Type: application/json" -d '{}')
DEP=$(sqlite3 "$CK_DB" "SELECT COUNT(*) FROM SemiLots WHERE Status='DEPLETED' AND LotNo LIKE 'SEMI-EARLY%';")
[ "$CB" = "200" ] && [ "$DEP" = "1" ] && ok "consume OK, lô hạn sớm DEPLETED" || no "consume sai: http=$CB depleted=$DEP"

echo "[10] Insufficient: reserve 999 (tồn còn 300) → 422 no-partial"
IB=$(curl -s $M -o /dev/null -w "%{http_code}" -X POST "$API/api/v2/work-orders/$WO/legs/$LEG/semi/reserve" -H "$AUTH" -H "Idempotency-Key: $(U)" -H "Content-Type: application/json" -d '{"qty":999,"semiKind":"PRINTED_SEMI"}')
[ "$IB" = "422" ] && ok "reserve vượt tồn → 422 (không partial)" || no "insufficient sai: $IB"

[ "$FAIL" = 0 ]
