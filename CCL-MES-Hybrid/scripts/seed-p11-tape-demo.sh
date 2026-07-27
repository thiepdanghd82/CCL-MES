#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────
# seed-p11-tape-demo.sh — bộ WO MẪU phủ MỌI trường hợp routing DAG có
# nhánh SEMI  TAPE → ASSEMBLY (topology T3 silkscreen) + biến thể
# keep-stock P11.5, để Henry MỞ APP verify trực quan (LegsDashboard +
# Kho bán thành phẩm). KHÔNG đổi code sản phẩm — chỉ seed + drive qua API
# thật, tự-quản lý vòng đời API.
#
# CASE MATRIX (mỗi WoNo = WO-DEMO-T3-<case>):
#   A  T3 in-line, gate HARD ĐANG CHẶN  — PRINT∥TAPE→ASSEMBLY→CUT(MagicLine);
#      ASSEMBLY @IPQC_APPROVED, PRINT/TAPE còn PREPRESS → RUNNING = 422.
#   B  T3 in-line, gate MỞ             — CUT(CNC); PRINT+TAPE→LEG_DONE(+qty)
#      → ASSEMBLY vào RUNNING (200). ASSEMBLY dừng ở RUNNING.
#   C  T3 in-line, JOIN xong           — CUT(RDC); TẤT CẢ leg→LEG_DONE →
#      JOIN → WO MesPhase=FQC_PENDING (mở FqcDashboard).
#   D  keep-stock, CHƯA reserve        — ASSEMBLY(FROM_STOCK)→CUT(PowerPunch)
#      @IPQC_APPROVED + kho 2×PRINTED_SEMI + 2×TAPE_SEMI → RUNNING = 422.
#   E  keep-stock, ĐÃ reserve (FEFO)   — như D + reserve PRINTED đủ target →
#      lô hạn sớm rút TRƯỚC (depleted) → RUNNING (200).
#   F  MIXED                           — TAPE in-line xong 60 ∥ kho 40 →
#      ASSEMBLY(MIXED)→CUT(MagicLine); reserve 40 → in-line60+kho40=100 → RUNNING.
#   G  (control) T2 label KHÔNG tape   — HP print → RDC cut (2 leg, không
#      nhánh tape/assembly) để đối chiếu.
#
# ⚠ Ops-confirm (RoutingLegMapSeed): keyword TAPE/ASSEMBLY chỉ thắng khi op
# KHÔNG mang WorkCenter khớp prefix PRINT/CUT (WC-prefix ưu tiên 2 > OpKeyword
# ưu tiên 3). Sau materialize MỖI case A/B/C/G, script ĐỌC /legs và ASSERT
# legKind + edge; drift → in '[⚠ leg-map drift]' + KHÔNG drive tiếp (không
# seed sai topology im lặng). D/E/F là keep-stock SQL-authored (materialize
# ép IN_LINE + validator §2 buộc IN_LINE-assembly có PRINT+TAPE, nên
# FROM_STOCK/MIXED KHÔNG materialize được) → topology assert từ /legs.
#
# SAFETY (playbook §4 + STACKED-PR R7):
#   • Mặc định target = COPY BỀN của live: data/demo/p11-tape-demo.db
#     (cp từ data/ccl_mes.db). Live data/ccl_mes.db KHÔNG BAO GIỜ bị ghi.
#   • --commit  : ghi thẳng LIVE (CONFIRM gate + sha8). WoNo/Product/lot có
#     prefix để purge (companion: purge-test-audit.sh).
#   • --keep-alive : sau seed KHÔNG kill API — bind :5100 (cổng desktop app
#     hardwire) để Henry mở app xem NGAY. Nếu không: cổng isolated :5127,
#     kill cuối, copy DB để lại trên đĩa.
#   • curl --max-time; login operator/operator.
#
# Usage:
#   bash CCL-MES-Hybrid/scripts/seed-p11-tape-demo.sh                 # copy, verify, kill
#   bash CCL-MES-Hybrid/scripts/seed-p11-tape-demo.sh --keep-alive    # copy → serve :5100 cho app
#   bash CCL-MES-Hybrid/scripts/seed-p11-tape-demo.sh --commit        # ghi LIVE (CONFIRM)
# ─────────────────────────────────────────────────────────────────────
set -uo pipefail

KEEP_ALIVE=0; COMMIT=0
for a in "$@"; do case "$a" in
  --keep-alive) KEEP_ALIVE=1 ;;
  --commit)     COMMIT=1 ;;
  --help|-h) sed -n '2,60p' "$0"; exit 0 ;;
  *) echo "unknown flag: $a"; exit 64 ;;
esac; done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HYBRID_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO="$(cd "$HYBRID_ROOT/.." && pwd)"
LIVE_DB="${MES_DB_PATH:-$REPO/data/ccl_mes.db}"
DEMO_DIR="$REPO/data/demo"; DEMO_DB="$DEMO_DIR/p11-tape-demo.db"
LOG="/tmp/seed-p11-tape-demo-api-$$.log"
M="--max-time 30"
PASS=0; FAIL=0; API_PID=""; DRIFT=0

# Port: keep-alive → :5100 (desktop app hardwire); else isolated :5127.
PORT=$([ "$KEEP_ALIVE" = 1 ] && echo 5100 || echo 5127)
API="http://127.0.0.1:$PORT"

ok(){ PASS=$((PASS+1)); echo "  ✓ $1"; }
no(){ FAIL=$((FAIL+1)); echo "  ✗ $1"; }
J(){ python3 -c "import sys,json;print(json.load(sys.stdin)$1)" 2>/dev/null; }
U(){ python3 -c "import uuid;print(uuid.uuid4())"; }

# ── target DB selection ────────────────────────────────────────────
if [ "$COMMIT" = 1 ]; then
  TARGET_DB="$LIVE_DB"
  SHA8="$(shasum -a 256 "$LIVE_DB" 2>/dev/null | awk '{print substr($1,1,8)}')"
  echo "═══════════════════════════════════════════════════════════════"
  echo "[ctx] MODE = --commit  → GHI THẲNG LIVE"
  echo "[ctx] DB   = $TARGET_DB (LIVE)"
  echo "[ctx] sha8 = $SHA8"
  echo "[ctx] WoNo prefix = WO-DEMO-T3-*  · Product PDEMO-T3-*  · lot SEMI-DEMO-*  (purge được)"
  echo "═══════════════════════════════════════════════════════════════"
  printf "Gõ CONFIRM-COMMIT để seed demo vào LIVE DB: "
  read -r ans; [ "$ans" = "CONFIRM-COMMIT" ] || { echo "Huỷ."; exit 1; }
else
  [ -f "$LIVE_DB" ] || { echo "❌ live DB not found: $LIVE_DB"; exit 1; }
  mkdir -p "$DEMO_DIR"
  cp "$LIVE_DB" "$DEMO_DB"
  TARGET_DB="$DEMO_DB"
  SHA8="$(shasum -a 256 "$DEMO_DB" 2>/dev/null | awk '{print substr($1,1,8)}')"
  echo "[ctx] MODE = copy (live NEVER written)"
  echo "[ctx] live = $LIVE_DB"
  echo "[ctx] DB   = $TARGET_DB (copy, sha8 $SHA8)"
fi
echo "[ctx] API  = $API"

DB(){ sqlite3 "$TARGET_DB" "$1"; }

cleanup(){
  if [ "$KEEP_ALIVE" = 1 ] && [ "$FAIL" = 0 ]; then
    echo ""; echo "─── --keep-alive: API còn chạy trên $API (PID $API_PID) → mở DESKTOP APP để xem ───"
  else
    [ -n "$API_PID" ] && kill "$API_PID" 2>/dev/null
  fi
  echo ""; echo "═══ SUMMARY: PASS=$PASS FAIL=$FAIL  DRIFT=$DRIFT ═══"
  [ "$FAIL" = 0 ] && echo "RESULT: ALL PASS" || echo "RESULT: FAIL"
}
trap cleanup EXIT

# ── boot API on TARGET_DB (pre-kill :5100 if we need it) ───────────
if [ "$PORT" = 5100 ]; then
  echo "[boot] pre-kill tiến trình đang giữ :5100 (nếu có) để bind demo DB…"
  EX=$(lsof -nP -iTCP:5100 -sTCP:LISTEN 2>/dev/null | awk 'NR>1{print $2}' | sort -u)
  [ -n "$EX" ] && kill $EX 2>/dev/null && sleep 2
fi
echo "[boot] dotnet run CCL.MES.Api --urls $API  (DB=$TARGET_DB)"
MES_DB_PATH="$TARGET_DB" nohup dotnet run --project "$REPO/CCL-MES-Hybrid/src/CCL.MES.Api" \
  --no-launch-profile --urls "$API" >"$LOG" 2>&1 & API_PID=$!
UP=0; for _ in $(seq 1 120); do curl -s $M "$API/api/v2/health" >/dev/null 2>&1 && { UP=1; break; }; sleep 1; done
[ "$UP" = 1 ] && ok "API booted on $API" || { no "API boot"; tail -8 "$LOG"; exit 1; }

TOK=$(curl -s $M -X POST "$API/api/v2/auth/login" -H "Content-Type: application/json" \
  -d '{"username":"operator","password":"operator","deviceId":"seed-p11-tape-demo"}' | J "['accessToken']")
[ -n "$TOK" ] && ok "login operator" || { no "login failed"; exit 1; }
AUTH="Authorization: Bearer $TOK"

# ── helpers ─────────────────────────────────────────────────────────
NOW="2026-07-24 00:00:00"
declare -a ROWS   # summary: WoNo|topology|phase|gate|view

# seed Customer+Product+WO (PREPRESS). $1=case $2=targetQty → echo WO id
seed_wo(){ local c="$1"; local tgt="$2"; local part="PDEMO-T3-$c"; local wono="WO-DEMO-T3-$c"
  DB "DELETE FROM WorkOrders WHERE WoNo='$wono';
      DELETE FROM Products WHERE ProductCode='$part';
      DELETE FROM Customers WHERE Code='CDEMO-$c';"
  DB "INSERT INTO Customers (Code,Name,CreatedAt) VALUES ('CDEMO-$c','Demo KH $c','$NOW');
      INSERT INTO Products (ProductCode,Name,CustomerId,CreatedAt) VALUES ('$part','Demo T3 $c',(SELECT max(Id) FROM Customers),'$NOW');
      INSERT INTO WorkOrders (WoNo,CustomerId,ProductId,ProductName,TargetQty,Uom,ProducedQty,CurrentStep,Status,Priority,MaterialsReady,SetupConfirmed,RohsOk,MesPhase,QtyDoneCached,QtyNgCached,CreatedAt)
        VALUES ('$wono',(SELECT max(Id) FROM Customers),(SELECT max(Id) FROM Products),'Demo T3 $c',$tgt,'pcs',0,'PrePressCheck','InProgress',0,0,0,0,'PREPRESS',0,0,'$NOW');"
  DB "SELECT Id FROM WorkOrders WHERE WoNo='$wono';"
}
# add routing op: $1=partCase $2=seq $3=op $4=wc(empty→NULL)
add_op(){ local part="PDEMO-T3-$1"; local wc="$4"
  DB "INSERT INTO RoutingOperations (PartNo,OpNo,Operation,WorkCenterNo,CreatedAt)
      VALUES ('$part','$2','$3',$([ -n "$wc" ] && echo "'$wc'" || echo NULL),'$NOW');"; }

GET(){ curl -s $M -H "$AUTH" "$API/api/v2/work-orders/$1/legs"; }
woetag(){ GET "$1" | J "['woETag']"; }
mesphase(){ GET "$1" | J "['mesPhase']"; }
legid(){ GET "$1" | python3 -c "import sys,json;print([l['legId'] for l in json.load(sys.stdin)['legs'] if l['sequence']==$2][0])" 2>/dev/null; }
legetag(){ GET "$1" | python3 -c "import sys,json;print([l['legETag'] for l in json.load(sys.stdin)['legs'] if l['sequence']==$2][0])" 2>/dev/null; }
mat_body(){ curl -s $M -X POST "$API/api/v2/work-orders/$1/legs/materialize" -H "$AUTH" -H "If-Match: \"$(woetag $1)\"" -H "Idempotency-Key: $(U)" -H "Content-Type: application/json" -d '{}'; }
adv(){ curl -s $M -X POST "$API/api/v2/work-orders/$1/legs/$2/advance" -H "$AUTH" -H "If-Match: \"$3\"" -H "Idempotency-Key: $(U)" -H "Content-Type: application/json" -d "{\"toPhase\":\"$4\"}"; }
adv_code(){ curl -s $M -o /dev/null -w "%{http_code}" -X POST "$API/api/v2/work-orders/$1/legs/$2/advance" -H "$AUTH" -H "If-Match: \"$3\"" -H "Idempotency-Key: $(U)" -H "Content-Type: application/json" -d "{\"toPhase\":\"$4\"}"; }
run_to_done(){ for P in SETTING IPQC_WAIT IPQC_APPROVED RUNNING LEG_DONE; do adv "$1" "$(legid $1 $2)" "$(legetag $1 $2)" "$P" >/dev/null; done; }
to_ipqc_appr(){ for P in SETTING IPQC_WAIT IPQC_APPROVED; do adv "$1" "$(legid $1 $2)" "$(legetag $1 $2)" "$P" >/dev/null; done; }

# ASSERT topology: $1=wo $2=case $3="K1,K2,.." $4=edgeCount → increments PASS/FAIL, sets DRIFT
assert_topo(){ local wo="$1" c="$2" kinds="$3" ec="$4"
  local out; out=$(GET "$wo" | python3 -c "
import sys,json
v=json.load(sys.stdin)
legs=sorted(v.get('legs',[]),key=lambda l:l['sequence'])
got=[l['legKind'] for l in legs]; exp='$kinds'.split(',')
edges=v.get('edges',[]); okk=(got==exp); oke=(len(edges)==$ec)
if not okk: print('  [⚠ leg-map drift] case $c: legKind=%s expected=%s' % (got,exp)); print('     → op có thể khớp WC-prefix (ưu tiên 2) trước OpKeyword (ưu tiên 3) — kiểm ProcessLegMap.')
if not oke: print('  [⚠ edge drift] case $c: edges=%d expected=$ec  %s' % (len(edges),[(e['dependsOnLegId'],e['legId'],e['dependencyGate']) for e in edges]))
print('OK' if (okk and oke) else 'BAD')")
  if echo "$out" | grep -q '^OK$'; then ok "case $c topology = [$kinds] +$ec edge"; return 0
  else echo "$out" | grep -v '^\(OK\|BAD\)$'; no "case $c topology drift"; DRIFT=$((DRIFT+1)); return 1; fi
}

# SQL-seed một leg (keep-stock). $1=wo $2=seq $3=kind $4=method $5=line $6=inputSrc $7=phase $8=spec(NULL|n) $9=qtyDone
seed_leg(){ DB "INSERT INTO WoLegs (WorkOrderId,Sequence,LegKind,Method,ProcessLine,SurfaceProfile,InputSource,LegPhase,SpecRevisionId,RowVersion,QtyDoneCached,QtyNgCached,CreatedAt,LegDoneAt)
  VALUES ($1,$2,'$3','$4','$5','FULL','$6','$7',$8,x'',$9,0,'$NOW',$([ "$7" = LEG_DONE ] && echo "'$NOW'" || echo NULL));"; }
legdb(){ DB "SELECT Id FROM WoLegs WHERE WorkOrderId=$1 AND Sequence=$2;"; }
seed_edge(){ DB "INSERT INTO WoLegDependencies (WorkOrderId,LegId,DependsOnLegId,DependencyGate,RequiredQty,CreatedAt) VALUES ($1,$2,$3,'$4',$5,'$NOW');"; }

postlot(){ curl -s $M -X POST "$API/api/v2/semi-lots" -H "$AUTH" -H "Idempotency-Key: $(U)" -H "Content-Type: application/json" -d "$1" | J "['ok']"; }
reserve(){ curl -s $M -X POST "$API/api/v2/work-orders/$1/legs/$2/semi/reserve" -H "$AUTH" -H "Idempotency-Key: $(U)" -H "Content-Type: application/json" -d "{\"qty\":$3,\"semiKind\":\"$4\"}"; }

TGT=100
echo ""; echo "══════════ SEED CASES (target qty=$TGT) ══════════"

# ── A: T3 in-line, HARD gate ĐANG CHẶN ─────────────────────────────
echo "[A] T3 in-line — ASSEMBLY chờ PRINT+TAPE (HARD chặn RUNNING)"
WA=$(seed_wo A $TGT)
add_op A 10 "Silkscreen print" "MSS01"; add_op A 20 "CẮT TAPE" ""; add_op A 30 "DÁN TAPE với semi-in" ""; add_op A 40 "Cắt Magic line" ""
BA=$(mat_body "$WA"); [ "$(echo "$BA" | J "['legCount']")" = "4" ] && ok "A materialize 4 leg → SPLIT" || no "A materialize sai: $BA"
if assert_topo "$WA" A "PRINT,TAPE,ASSEMBLY,CUT" 3; then
  to_ipqc_appr "$WA" 2
  CA=$(adv_code "$WA" "$(legid $WA 2)" "$(legetag $WA 2)" "RUNNING")
  [ "$CA" = "422" ] && ok "A ASSEMBLY→RUNNING bị HARD chặn (422, PRINT/TAPE còn PREPRESS)" || no "A gate không chặn: http=$CA"
fi
ROWS+=("WO-DEMO-T3-A|T3 PRINT∥TAPE→ASSEMBLY→CUT(MagicLine)|$(mesphase $WA)|HARD chặn (đỏ) — ASSEMBLY @IPQC_APPROVED|LegsDashboard: banner gate ĐỎ trên leg DÁN, PRINT/TAPE chưa xong")

# ── B: T3 in-line, HARD gate MỞ ────────────────────────────────────
echo "[B] T3 in-line — PRINT+TAPE done → ASSEMBLY vào RUNNING"
WB=$(seed_wo B $TGT)
add_op B 10 "Silkscreen print" "MSS02"; add_op B 20 "CẮT TAPE" ""; add_op B 30 "DÁN TAPE với semi-in" ""; add_op B 40 "Cắt CNC" "CNC02"
mat_body "$WB" >/dev/null
if assert_topo "$WB" B "PRINT,TAPE,ASSEMBLY,CUT" 3; then
  run_to_done "$WB" 0; DB "UPDATE WoLegs SET QtyDoneCached=$TGT WHERE WorkOrderId=$WB AND Sequence=0;"
  run_to_done "$WB" 1; DB "UPDATE WoLegs SET QtyDoneCached=$TGT WHERE WorkOrderId=$WB AND Sequence=1;"
  to_ipqc_appr "$WB" 2
  CB=$(adv_code "$WB" "$(legid $WB 2)" "$(legetag $WB 2)" "RUNNING")
  [ "$CB" = "200" ] && ok "B PRINT+TAPE LEG_DONE(+qty) → ASSEMBLY RUNNING (200)" || no "B gate không mở: http=$CB"
fi
ROWS+=("WO-DEMO-T3-B|T3 PRINT∥TAPE→ASSEMBLY→CUT(CNC)|$(mesphase $WB)|HARD mở — ASSEMBLY @RUNNING|LegsDashboard: gate XANH, nút Advance ASSEMBLY hoạt động")

# ── C: T3 in-line, JOIN xong → FQC_PENDING ─────────────────────────
echo "[C] T3 in-line — tất cả leg LEG_DONE → JOIN → FQC_PENDING"
WC=$(seed_wo C $TGT)
add_op C 10 "Silkscreen print" "MSS03"; add_op C 20 "CẮT TAPE" ""; add_op C 30 "DÁN TAPE với semi-in" ""; add_op C 40 "Die cut RDC" "RDC03"
mat_body "$WC" >/dev/null
if assert_topo "$WC" C "PRINT,TAPE,ASSEMBLY,CUT" 3; then
  run_to_done "$WC" 0; DB "UPDATE WoLegs SET QtyDoneCached=$TGT WHERE WorkOrderId=$WC AND Sequence=0;"
  run_to_done "$WC" 1; DB "UPDATE WoLegs SET QtyDoneCached=$TGT WHERE WorkOrderId=$WC AND Sequence=1;"
  run_to_done "$WC" 2   # ASSEMBLY
  run_to_done "$WC" 3   # CUT terminal → JOIN
  MP=$(mesphase "$WC")
  [ "$MP" = "FQC_PENDING" ] && ok "C JOIN → WO MesPhase=FQC_PENDING" || no "C join sai: mesPhase=$MP"
fi
ROWS+=("WO-DEMO-T3-C|T3 PRINT∥TAPE→ASSEMBLY→CUT(RDC)|$(mesphase $WC)|JOIN xong (4/4 LEG_DONE)|Tìm WO → tự route vào FqcDashboard (FQC_PENDING)")

# ── D: keep-stock CHƯA reserve → RUNNING 422 ───────────────────────
echo "[D] keep-stock — ASSEMBLY(FROM_STOCK) chưa reserve → RUNNING 422"
SPEC_D=91001
WD=$(seed_wo D $TGT)
seed_leg "$WD" 0 ASSEMBLY Assembly FINISHING FROM_STOCK IPQC_APPROVED $SPEC_D 0
seed_leg "$WD" 1 CUT PowerPunch PRESS_CNC IN_LINE PREPRESS NULL 0
seed_edge "$WD" "$(legdb $WD 1)" "$(legdb $WD 0)" SOFT 0
DB "UPDATE WorkOrders SET MesPhase='SPLIT' WHERE Id=$WD;"
assert_topo "$WD" D "ASSEMBLY,CUT" 1
for kd in "PRINTED_SEMI PR EARLY 400 2026-07-28" "PRINTED_SEMI PR LATE 400 2026-12-01" "TAPE_SEMI TP EARLY 300 2026-07-28" "TAPE_SEMI TP LATE 300 2026-12-01"; do
  set -- $kd; postlot "{\"lotNo\":\"SEMI-DEMO-D-$2-$3\",\"semiKind\":\"$1\",\"qty\":$4,\"specRevisionId\":$SPEC_D,\"sourceWorkOrderId\":1,\"expiryAt\":\"$5\"}" >/dev/null; done
DC=$(adv_code "$WD" "$(legdb $WD 0)" "$(legetag $WD 0)" "RUNNING")
[ "$DC" = "422" ] && ok "D chưa reserve → keep-stock gate chặn (422)" || no "D gate không chặn: http=$DC"
ROWS+=("WO-DEMO-T3-D|keep-stock ASSEMBLY⟵kho → CUT(PowerPunch)|$(mesphase $WD)|keep-stock chặn — chưa reserve (422)|Kho spec $SPEC_D: 2 PRINTED+2 TAPE chưa giữ; ASSEMBLY kẹt @IPQC_APPROVED")

# ── E: keep-stock ĐÃ reserve FEFO → RUNNING 200 ────────────────────
echo "[E] keep-stock — reserve FEFO đủ target → RUNNING 200 (lô hạn sớm rút trước)"
SPEC_E=91002
WE=$(seed_wo E $TGT)
seed_leg "$WE" 0 ASSEMBLY Assembly FINISHING FROM_STOCK IPQC_APPROVED $SPEC_E 0
seed_leg "$WE" 1 CUT CNC PRESS_CNC IN_LINE PREPRESS NULL 0
seed_edge "$WE" "$(legdb $WE 1)" "$(legdb $WE 0)" SOFT 0
DB "UPDATE WorkOrders SET MesPhase='SPLIT' WHERE Id=$WE;"
assert_topo "$WE" E "ASSEMBLY,CUT" 1
# hạn sớm 60 (≤7 ngày → Expiring) + hạn muộn 100 → reserve 100 rút 60(early)+40(late)
postlot "{\"lotNo\":\"SEMI-DEMO-E-PR-EARLY\",\"semiKind\":\"PRINTED_SEMI\",\"qty\":60,\"specRevisionId\":$SPEC_E,\"sourceWorkOrderId\":1,\"expiryAt\":\"2026-07-28\"}" >/dev/null
postlot "{\"lotNo\":\"SEMI-DEMO-E-PR-LATE\",\"semiKind\":\"PRINTED_SEMI\",\"qty\":100,\"specRevisionId\":$SPEC_E,\"sourceWorkOrderId\":1,\"expiryAt\":\"2026-12-01\"}" >/dev/null
postlot "{\"lotNo\":\"SEMI-DEMO-E-TP-EARLY\",\"semiKind\":\"TAPE_SEMI\",\"qty\":60,\"specRevisionId\":$SPEC_E,\"sourceWorkOrderId\":1,\"expiryAt\":\"2026-07-28\"}" >/dev/null
postlot "{\"lotNo\":\"SEMI-DEMO-E-TP-LATE\",\"semiKind\":\"TAPE_SEMI\",\"qty\":100,\"specRevisionId\":$SPEC_E,\"sourceWorkOrderId\":1,\"expiryAt\":\"2026-12-01\"}" >/dev/null
RB=$(reserve "$WE" "$(legdb $WE 0)" $TGT PRINTED_SEMI)
[ "$(echo "$RB" | J "['allocated']")" = "$TGT" ] && ok "E reserve PRINTED $TGT OK" || no "E reserve sai: $RB"
EARLY_AV=$(DB "SELECT QtyAvailable FROM SemiLots WHERE LotNo='SEMI-DEMO-E-PR-EARLY';")
[ "$EARLY_AV" = "0" ] && ok "E FEFO: lô hạn sớm (SEMI-DEMO-E-PR-EARLY) rút hết TRƯỚC" || no "E FEFO sai: early avail=$EARLY_AV"
EC=$(adv_code "$WE" "$(legdb $WE 0)" "$(legetag $WE 0)" "RUNNING")
[ "$EC" = "200" ] && ok "E reserve đủ → keep-stock gate mở → RUNNING (200)" || no "E gate không mở: http=$EC"
ROWS+=("WO-DEMO-T3-E|keep-stock ASSEMBLY⟵kho → CUT(CNC)|$(mesphase $WE)|keep-stock MỞ — reserved FEFO 100|Kho spec $SPEC_E: PR-EARLY depleted, PR-LATE reserved 40; ASSEMBLY @RUNNING")

# ── F: MIXED — in-line TAPE 60 + kho 40 ────────────────────────────
echo "[F] MIXED — TAPE in-line xong 60 + reserve kho 40 → RUNNING 200"
SPEC_F=91003; INLINE=60; STOCK=$((TGT-INLINE))
WF=$(seed_wo F $TGT)
seed_leg "$WF" 0 TAPE PowerPunch FINISHING IN_LINE LEG_DONE NULL $INLINE
seed_leg "$WF" 1 ASSEMBLY Assembly FINISHING MIXED IPQC_APPROVED $SPEC_F 0
seed_leg "$WF" 2 CUT MagicLine PRESS_CNC IN_LINE PREPRESS NULL 0
seed_edge "$WF" "$(legdb $WF 1)" "$(legdb $WF 0)" HARD $INLINE   # in-line phần TAPE (60)
seed_edge "$WF" "$(legdb $WF 2)" "$(legdb $WF 1)" SOFT 0
DB "UPDATE WorkOrders SET MesPhase='SPLIT' WHERE Id=$WF;"
assert_topo "$WF" F "TAPE,ASSEMBLY,CUT" 2
postlot "{\"lotNo\":\"SEMI-DEMO-F-PR-EARLY\",\"semiKind\":\"PRINTED_SEMI\",\"qty\":40,\"specRevisionId\":$SPEC_F,\"sourceWorkOrderId\":1,\"expiryAt\":\"2026-07-28\"}" >/dev/null
postlot "{\"lotNo\":\"SEMI-DEMO-F-PR-LATE\",\"semiKind\":\"PRINTED_SEMI\",\"qty\":100,\"specRevisionId\":$SPEC_F,\"sourceWorkOrderId\":1,\"expiryAt\":\"2026-12-01\"}" >/dev/null
FC1=$(adv_code "$WF" "$(legdb $WF 1)" "$(legetag $WF 1)" "RUNNING")
[ "$FC1" = "422" ] && ok "F trước reserve: in-line 60 < target 100 → chặn (422)" || no "F chưa chặn: http=$FC1"
RF=$(reserve "$WF" "$(legdb $WF 1)" $STOCK PRINTED_SEMI)
[ "$(echo "$RF" | J "['allocated']")" = "$STOCK" ] && ok "F reserve kho $STOCK OK" || no "F reserve sai: $RF"
FC2=$(adv_code "$WF" "$(legdb $WF 1)" "$(legetag $WF 1)" "RUNNING")
[ "$FC2" = "200" ] && ok "F in-line 60 + kho 40 = 100 ≥ target → RUNNING (200)" || no "F gate không mở: http=$FC2"
ROWS+=("WO-DEMO-T3-F|MIXED TAPE(in-line60)∥kho40 → ASSEMBLY → CUT(MagicLine)|$(mesphase $WF)|MIXED mở — 60 in-line + 40 kho|Kho spec $SPEC_F: F-PR-EARLY reserved 40; TAPE leg LEG_DONE; ASSEMBLY @RUNNING")

# ── G: control T2 label, KHÔNG tape ────────────────────────────────
echo "[G] control T2 — HP print → RDC cut (không nhánh tape/assembly)"
WG=$(seed_wo G $TGT)
add_op G 10 "HP Indigo print" "IDG07"; add_op G 20 "Die cut RDC" "RDC07"
mat_body "$WG" >/dev/null
assert_topo "$WG" G "PRINT,CUT" 1
ROWS+=("WO-DEMO-T3-G|T2 PRINT(HP)→CUT(RDC) — control|$(mesphase $WG)|— (không có gate assembly)|Đối chiếu: LegsDashboard 2 leg, KHÔNG có leg TAPE/ASSEMBLY")

# ── summary table ───────────────────────────────────────────────────
echo ""
echo "════════════════════════════ BẢNG DEMO ════════════════════════════"
printf '%s\n' "${ROWS[@]}" | awk -F'|' '{printf "\n● %s\n    topology : %s\n    mesPhase : %s\n    gate     : %s\n    XEM GÌ   : %s\n", $1,$2,$3,$4,$5}'
echo ""
echo "════════════════════════════ MỞ APP XEM ════════════════════════════"
if [ "$KEEP_ALIVE" = 1 ]; then
  echo "API demo ĐANG CHẠY trên $API (DB=$TARGET_DB)."
  echo "→ MỞ DESKTOP APP (CCL MES.app) — nó tự trỏ :5100 → thấy WO-DEMO-T3-A..G."
  echo "→ Quét/mở lần lượt: WO-DEMO-T3-A · -B · -C · -D · -E · -F · -G"
  echo ""
  echo "KHÔI PHỤC live sau khi xem xong (Ctrl+C script rồi):"
  echo "  MES_DB_PATH=\"$LIVE_DB\" dotnet run --project CCL-MES-Hybrid/src/CCL.MES.Api --no-launch-profile --urls http://127.0.0.1:5100"
else
  echo "Demo DB đã seed: $TARGET_DB"
  echo "→ Để xem trong DESKTOP APP, chạy API :5100 trỏ vào demo DB rồi mở app:"
  echo "  MES_DB_PATH=\"$TARGET_DB\" dotnet run --project CCL-MES-Hybrid/src/CCL.MES.Api --no-launch-profile --urls http://127.0.0.1:5100"
  echo "  (hoặc chạy lại script này với --keep-alive để tự bind :5100)"
fi
echo "WoNo: WO-DEMO-T3-A · -B · -C · -D · -E · -F · -G"
echo "Purge (khi chạy --commit lên live): bash CCL-MES-Hybrid/scripts/purge-test-audit.sh [--commit]"
echo "═══════════════════════════════════════════════════════════════════"

[ "$FAIL" = 0 ]
