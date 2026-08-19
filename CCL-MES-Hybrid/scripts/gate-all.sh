#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Pha 4 AUDIT của vòng lặp (docs/AGENT-LOOP.md) — chạy TOÀN BỘ gate tĩnh.
#
# Theo S12: mỗi bước in nhãn [N/total], và LUÔN in SUMMARY ở cuối kể cả khi
# fail — im lặng nghĩa là không ai biết nó có chạy hay không.
#
# Gate xanh KHÔNG có nghĩa là tính năng chạy. Đó là pha 5 VERIFY, việc khác.
#
# Usage:
#   bash scripts/gate-all.sh              # chạy tất cả
#   bash scripts/gate-all.sh --self-test  # chạy self-test của mọi gate có hỗ trợ
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail   # KHÔNG -e: một gate fail vẫn phải chạy tiếp để báo cáo đủ

here="$(cd "$(dirname "$0")" && pwd)"
MODE="${1:-}"

GATES=(
  "hex:gate-no-hardcoded-hex.sh:L37 màu qua token"
  "row-actions:gate-row-actions.sh:L35 không cột Actions"
  "showcard:gate-floating-showcard.sh:L34 showcard bọc FloatingWindow"
  "spec-print:gate-spec-print.sh:L39 native print + bảng auto/nowrap"
  "thin:gate-thin-controller.sh:L40 logic không ở controller"
  "tokens:gate-design-tokens.sh:L41 kích thước qua thang + density"
  "i18n:gate-i18n-parity.sh:L42 catalog lành, không chuỗi cứng"
  "audit:gate-audit-emit.sh:L43 mutation có vết, detail sạch"
  "oee-source:gate-oee-single-source.sh:C3 một nguồn tốc độ OEE, null luôn kèm lý do"
)

total="${#GATES[@]}"
pass=0; fail=0; skip=0
declare -a RESULTS

echo "═══ gate-all — $total gate ═══"
[ -n "$MODE" ] && echo "mode: $MODE"
echo

i=0
for g in "${GATES[@]}"; do
  i=$((i+1))
  name="${g%%:*}"; rest="${g#*:}"; script="${rest%%:*}"; desc="${rest#*:}"
  printf '[%d/%d] %-12s %s\n' "$i" "$total" "$name" "$desc"
  if [ ! -f "$here/$script" ]; then
    echo "        ⊘ SKIP — không thấy $script"
    RESULTS+=("SKIP  $name  (thiếu $script)"); skip=$((skip+1)); echo; continue
  fi
  if [ "$MODE" = "--self-test" ]; then
    out="$(bash "$here/$script" --self-test 2>&1)"; rc=$?
    if echo "$out" | grep -qi "self-test"; then :; else
      echo "        ⊘ SKIP — gate chưa hỗ trợ --self-test"
      RESULTS+=("SKIP  $name  (không có self-test)"); skip=$((skip+1)); echo; continue
    fi
  else
    out="$(bash "$here/$script" 2>&1)"; rc=$?
  fi
  echo "$out" | sed 's/^/        /'
  if [ $rc -eq 0 ]; then RESULTS+=("PASS  $name"); pass=$((pass+1));
  else RESULTS+=("FAIL  $name"); fail=$((fail+1)); fi
  echo
done

echo "═══════════════════ SUMMARY ═══════════════════"
for r in "${RESULTS[@]}"; do echo "  $r"; done
echo "───────────────────────────────────────────────"
echo "  PASS=$pass  FAIL=$fail  SKIP=$skip  TOTAL=$total"
if [ "$fail" -gt 0 ]; then
  echo "  VERDICT: FAIL — sửa vi phạm, hoặc bump BASELINE kèm lý do trong PR body."
  echo "           Bump BASELINE mà không giải thích được = STOP-gate, hỏi Henry."
  exit 1
fi
echo "  VERDICT: PASS — không vi phạm luật đã biết."
echo "           Nhắc: đây mới là pha 4 (tĩnh). Pha 5 VERIFY vẫn phải chạy thật."
exit 0
