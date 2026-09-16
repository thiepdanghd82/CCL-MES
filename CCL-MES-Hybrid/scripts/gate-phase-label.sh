#!/usr/bin/env bash
#
# CI gate — TOKEN PHASE KHÔNG ĐƯỢC IN THẲNG RA MÀN HÌNH.
#
# Người đứng máy phải đọc "Chờ IPQC", không phải "IPQC_WAIT". Từ ngữ 15 phase
# (14 MesPhase + LEG_DONE) đã chốt một lần trong TranslationCatalog.Legs và
# PhaseVisual.LabelKey biết đường tra; surface chỉ việc gọi PhaseText() ở
# LocalizedComponentBase, hoặc dựng <StatusPill>.
#
# VÌ SAO CẦN GATE RIÊNG, trong khi đã có gate-i18n-parity. Gate kia bắt CHUỖI
# TIẾNG VIỆT nằm trần trong .razor. Nhưng "IPQC_WAIT" không phải tiếng Việt, và
# "Ready to Run" khai cứng cũng không — nên suốt từ P10.7 tới 2026-09-16 có 8
# chỗ in token thô mà mọi gate đều xanh. Đây đúng là lớp lỗi gate kia mù.
#
# Usage: bash scripts/gate-phase-label.sh              (exit 0 = pass, 1 = fail)
#        bash scripts/gate-phase-label.sh --self-test  (chứng minh detector còn bắt được)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RAZOR="$ROOT/src/CCL.MES.Hybrid.Razor"

# Ratchet. Nợ hiện tại = 0: 8 chỗ cũ đã đi qua PhaseText() ngày 2026-09-16.
# Muốn nâng số này thì phải giải thích được trong PR body vì sao hợp lệ.
BASELINE=0

# MỘT biểu thức nhận diện, khai một lần để self-test chạy đúng cái detector
# thật (không để detector và bài test của nó trôi khỏi nhau — luật chung của
# bộ gate này).
#
# Bắt: một biểu thức Razor `@...MesPhase` / `@...LegPhase` đổ THẲNG vào markup.
# KHÔNG bắt `@if (x.MesPhase == …)` hay `@onclick="… x.MesPhase …"` vì sau `@`
# là `if`/`onclick` rồi tới ký tự ngoài lớp [A-Za-z0-9_.] nên chuỗi đứt.
RX='@\(?[A-Za-z_][A-Za-z0-9_.]*(MesPhase|LegPhase)\b'

# Dòng có một trong các dấu hiệu này là ĐÃ đi đúng đường — bỏ qua:
#   PhaseText( · <StatusPill · PhaseVisual.  → đã qua catalog
#   data-…phase="…"                          → thuộc tính máy đọc (test hook,
#                                              CSS selector), người KHÔNG đọc
#   MesPhase == / !=                         → so sánh điều kiện, thứ in ra là
#                                              nhánh T(...) chứ không phải token
#
# HẠN CHẾ ĐÃ BIẾT, nói thẳng: lọc theo DÒNG, không theo biểu thức. Một dòng vừa
# so sánh phase vừa in token thô sẽ lọt. Chấp nhận, vì phương án kia là parse
# Razor trong bash. Lưới thứ hai là PhaseLabelCoverageTests + review.
SAFE='PhaseText\(|<StatusPill|PhaseVisual\.|data-[a-z-]*phase=|(MesPhase|LegPhase)[[:space:]]*[=!]='

# ── self-test ─────────────────────────────────────────────────────────────────
if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  # (1) vi phạm: in thẳng token
  printf '<span class="x">@_view.MesPhase</span>\n' > "$tmp/GateSelftestPhase.razor"
  # (2) KHÔNG phải vi phạm: đã qua PhaseText, và một điều kiện @if
  printf '<span>@PhaseText(_view.MesPhase)</span>\n@if (_v.MesPhase == "RUNNING") { <b>x</b> }\n' \
    > "$tmp/GateSelftestOk.razor"

  hit="$(grep -rInE "$RX" --include='*.razor' "$tmp" 2>/dev/null | grep -vE "$SAFE" || true)"
  n="$(printf '%s' "$hit" | grep -c . || true)"

  if [ "$n" = "1" ] && printf '%s' "$hit" | grep -q 'GateSelftestPhase.razor'; then
    echo "[gate:phase-label] self-test OK — bắt đúng 1 token thô, và KHÔNG báo động giả"
    echo "                   trên dòng đã qua PhaseText() lẫn dòng @if điều kiện."
    exit 0
  fi
  echo "[gate:phase-label] self-test FAILED — detector bắt $n dòng, đáng ra đúng 1:"
  printf '%s\n' "$hit"
  exit 1
fi

# ── quét thật ─────────────────────────────────────────────────────────────────
hits="$(grep -rInE "$RX" --include='*.razor' "$RAZOR" 2>/dev/null | grep -vE "$SAFE" || true)"
count="$(printf '%s' "$hits" | grep -c . || true)"

if [ "$count" -gt "$BASELINE" ]; then
  echo "[gate:phase-label] token phase in THẲNG ra markup: $count (baseline $BASELINE)"
  printf '%s\n' "$hits" | sed 's|^|  |'
  echo
  echo "  → Dùng @PhaseText(<token>) — có sẵn ở LocalizedComponentBase — hoặc <StatusPill Phase=\"…\" />."
  echo "    Từ ngữ đã chốt trong TranslationCatalog.Legs, đừng tự bịa nhãn mới tại chỗ."
  exit 1
fi

echo "[gate:phase-label] token phase in thẳng ra markup: $count (baseline $BASELINE)"
echo "[gate:phase-label:OK] mọi surface đọc nhãn phase qua catalog — không ai phải đọc IPQC_WAIT."
exit 0
