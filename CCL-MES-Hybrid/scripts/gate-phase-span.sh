#!/usr/bin/env bash
#
# CI gate — MỌI ĐIỂM ĐỔI MesPhase PHẢI NẰM TRONG DANH SÁCH ĐÃ BIẾT,
#           VÀ DANH SÁCH CÔNG ĐOẠN ĐƯỢC ĐO KHÔNG ĐƯỢC CHỒNG NGUỒN.
#
# Vì sao gate này tồn tại (đọc kèm P10.7-WO-STATE-CONTRACT.md §5.7):
#
#   Mốc thời gian công đoạn (WoPhaseSpan) được đóng dấu ở MỘT chỗ —
#   MesDbContext.SaveChanges đọc ChangeTracker. Thiết kế đó mua được tính
#   KHÔNG-QUÊN-ĐƯỢC (chỗ gán MesPhase thứ 15 thêm sau vẫn sinh mốc), nhưng
#   bán đi tính greppable: người đọc IpqcReviewController sẽ không thấy span
#   được ghi ở đâu. Gate này là phần đền bù: ai thêm một điểm đổi phase mới
#   sẽ bị chặn và buộc phải đọc §5.7 trước.
#
#   (A) `x.MesPhase = ...` ở file NGOÀI danh sách ⇒ đỏ.
#       Ratchet theo TỪNG FILE, không phải tổng — thêm một điểm trong file
#       đã biết cũng phải cố ý.
#
#   (B) WoPhaseSpanPolicy.TrackedPhases chứa phase ĐÃ có nguồn thời gian
#       khác (SETTING → 3 cột trên WorkOrder; RUNNING/PAUSED → WoRunSession
#       + WoPauseEvent) ⇒ đỏ. Đây là luật L63: một con số, một nguồn. Đo hai
#       nơi thì sớm muộn hai nơi lệch nhau và không ai biết tin bên nào.
#
# ĐỌC GÌ: mã nguồn runtime. Bỏ Migrations/ (ở đó `wo.MesPhase = 'PREPRESS'`
# là mệnh đề SQL WHERE, không phải phép gán C#), bỏ bin/obj, bỏ test, và
# STRIP comment trước khi so — chính file chỗ móc có một dòng chú thích
# `wo.MesPhase = ...` mà nếu không strip thì gate tự bắt chính nó.
#
# Tự kiểm: bash gate-phase-span.sh --self-test  → PASS → FAIL → PASS.

set -uo pipefail
cd "$(dirname "$0")/../.." || exit 2

GATE="phase-span"
CONTRACT="CCL-MES-Hybrid/docs/P10.7-WO-STATE-CONTRACT.md"
POLICY="src/CCL.MES.Domain/Entities/WoPhaseSpan.cs"

# ── Danh sách điểm đổi phase đã biết: "đường dẫn|số điểm" ───────────────
# Đo 2026-09-20. Đổi con số ở đây = tuyên bố có ý thức, phải giải thích
# trong PR body kèm lý do tại sao điểm mới không phá §5.7.
ALLOW=(
  "src/CCL.MES.Application/Services/RunningSurfaceServices.cs|4"
  "src/CCL.MES.Application/Services/WorkOrderService.cs|1"
  "CCL-MES-Hybrid/src/CCL.MES.Api/Controllers/RunningSurfaceController.cs|2"
  "CCL-MES-Hybrid/src/CCL.MES.Api/Controllers/IpqcReviewController.cs|2"
  "CCL-MES-Hybrid/src/CCL.MES.Api/Controllers/WoQcReviewController.cs|2"
  "CCL-MES-Hybrid/src/CCL.MES.Api/Controllers/RoutingController.cs|2"
  "CCL-MES-Hybrid/src/CCL.MES.Api/Controllers/AdminWorkOrdersController.cs|1"
)

# Công đoạn ĐÃ có nguồn thời gian khác — không được đo lần hai (L63).
ALREADY_TIMED=("SETTING" "RUNNING" "PAUSED")

ROOTS=(src/CCL.MES.Application src/CCL.MES.Domain src/CCL.MES.Infrastructure CCL-MES-Hybrid/src)

# Liệt kê điểm GÁN thật (đã strip comment, đã loại `==`).
scan_sites() {
  local extra_root="${1:-}"
  local roots=("${ROOTS[@]}")
  [ -n "$extra_root" ] && roots+=("$extra_root")
  grep -rn --include='*.cs' '\.MesPhase[[:space:]]*=' "${roots[@]}" 2>/dev/null \
    | grep -vE '/(bin|obj|Migrations)/' \
    | grep -vE '[Tt]ests?\.cs:' \
    | awk -F: '{
        body = $0; sub(/^[^:]*:[0-9]+:/, "", body);   # bỏ path:line
        sub(/\/\/.*/, "", body);                       # strip comment cuối dòng
        if (body ~ /\.MesPhase[ \t]*=[ \t]*[^=]/) print $1
      }'
}

run_check() {
  local extra_root="${1:-}"
  local violations=0

  # ── (A) điểm gán ngoài danh sách / lệch số ───────────────────────────
  local counts; counts=$(scan_sites "$extra_root" | sort | uniq -c | awk '{print $2"|"$1}')

  local seen_files=""
  while IFS='|' read -r file n; do
    [ -z "$file" ] && continue
    local expected=""
    for e in "${ALLOW[@]}"; do
      [ "${e%%|*}" = "$file" ] && expected="${e##*|}"
    done
    if [ -z "$expected" ]; then
      echo "[gate:$GATE] ✗ điểm đổi MesPhase ở file NGOÀI danh sách: $file ($n điểm)"
      echo "               đọc $CONTRACT §5.7 rồi thêm file vào ALLOW nếu thật sự cần."
      violations=$((violations+1))
    elif [ "$n" != "$expected" ]; then
      echo "[gate:$GATE] ✗ $file có $n điểm, danh sách ghi $expected"
      violations=$((violations+1))
    fi
    seen_files="$seen_files $file"
  done <<< "$counts"

  # file trong danh sách mà biến mất ⇒ danh sách trôi khỏi code
  for e in "${ALLOW[@]}"; do
    local f="${e%%|*}"
    case " $seen_files " in
      *" $f "*) ;;
      *) echo "[gate:$GATE] ✗ danh sách ghi $f nhưng không còn điểm gán nào ở đó"
         violations=$((violations+1)) ;;
    esac
  done

  echo "$violations"
}

check_tracked_phases() {
  local violations=0
  [ -f "$POLICY" ] || { echo "[gate:$GATE] ✗ không thấy $POLICY"; return 1; }
  local block; block=$(awk '/TrackedPhases/,/};/' "$POLICY")
  for ph in "${ALREADY_TIMED[@]}"; do
    if echo "$block" | grep -q "\"$ph\""; then
      echo "[gate:$GATE] ✗ TrackedPhases chứa $ph — công đoạn này ĐÃ có nguồn thời gian khác."
      echo "               Đo hai nơi = hai nguồn sự thật (L63). Xem §5.7 'Phạm vi theo dõi'."
      violations=$((violations+1))
    fi
  done
  return $violations
}

# ── self-test: PASS → FAIL (tiêm) → PASS ───────────────────────────────
if [ "${1:-}" = "--self-test" ]; then
  base=$(run_check | tail -1)
  [ "$base" = "0" ] || { echo "[gate:$GATE] self-test FAILED — bản nguyên vẹn đã đỏ ($base vi phạm)."; exit 1; }

  tmp=$(mktemp -d)
  mkdir -p "$tmp/Fake"
  cat > "$tmp/Fake/SneakyPhaseWriter.cs" <<'CS'
namespace Fake;
public class SneakyPhaseWriter
{
    public void Move(dynamic wo) { wo.MesPhase = "FQC_PENDING"; }
}
CS
  injected=$(run_check "$tmp" | tail -1)
  rm -rf "$tmp"

  if [ "$injected" != "0" ]; then
    echo "[gate:$GATE] self-test OK — nguyên vẹn XANH (0), tiêm 1 điểm gán ở file lạ ⇒ ĐỎ ($injected)."
    after=$(run_check | tail -1)
    [ "$after" = "0" ] && { echo "[gate:$GATE] self-test OK — gỡ tiêm ⇒ XANH lại."; exit 0; }
    echo "[gate:$GATE] self-test FAILED — không về xanh sau khi gỡ tiêm."; exit 1
  fi
  echo "[gate:$GATE] self-test FAILED — tiêm điểm gán lạ mà gate không bắt."
  exit 1
fi

# ── chạy thật ──────────────────────────────────────────────────────────
out=$(run_check); v=$(echo "$out" | tail -1)
echo "$out" | sed '$d' | grep -v '^$'

check_tracked_phases; tv=$?

total=$((v + tv))
echo "[gate:$GATE] điểm đổi MesPhase ngoài danh sách = $v (bắt buộc 0)"
echo "[gate:$GATE] công đoạn bị đo hai nơi           = $tv (bắt buộc 0)"

if [ "$total" -gt 0 ]; then
  echo "[gate:$GATE:FAIL] $total vi phạm — mốc thời gian công đoạn sẽ thiếu hoặc lệch nguồn."
  echo "  Hợp đồng: $CONTRACT §5.7"
  exit 1
fi
echo "[gate:$GATE:OK] mọi điểm đổi phase đều đã biết, và không công đoạn nào bị đo hai nơi."
exit 0
