#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# L57 gate (B) — CỠ CHỮ và KHOẢNG CÁCH không được lái bằng đơn vị VIEWPORT.
#
# VÌ SAO GATE NÀY TỒN TẠI:
#   `vw`/`vh`/`vmin`/`vmax` co theo BỀ RỘNG CỬA SỔ. Chúng mù `data-density`
#   VÀ mù `--ui-scale`. Dùng chúng cho cỡ chữ / khoảng cách nghĩa là màn hình
#   đó IM LẶNG rút khỏi hệ thống density — bật shopfloor không đổi một pixel.
#
#   Hậu quả đo được trên cây trước ngày 2026-08-26:
#     · Khối `.qclib-*` (màn QC Library 86 mục) chứa 21 trong tổng số 48
#       `clamp()` của cả app.css. Ở `data-density="shopfloor"`, cỡ chữ bảng đo
#       được 13.76px — DƯỚI ngưỡng 16px mà chính hệ thiết kế bắt buộc — và ô
#       tick giữ nguyên 16px thay vì nở theo `--d-tap`. Đây là màn DUY NHẤT
#       trong app phớt lờ hoàn toàn công tắc density.
#     · Bốn ô SỐ VẬN HÀNH (`.rs-head-wo-no`, `.rs-timer-value`,
#       `.ipqc-counter-value`, `.shipped-stat-value`) dùng `clamp(...vw)` và
#       ĐẢO NGƯỢC ý đồ thiết kế: tablet xưởng hẹp kẹp về đáy 18px ⇒ người đứng
#       XA NHẤT nhận chữ NHỎ NHẤT; màn office rộng kẹp về đỉnh 24px. Ở
#       `--ui-scale` 1.5 + shopfloor, SỐ WO đứng yên 18px trong khi chữ thân
#       bài lên 24px — mã nhận dạng chính nhỏ hơn chữ thường.
#
#   Điểm đau: skill `cmes-design-tokens` lấy CHÍNH bảng QC này làm bài học mở
#   đầu ("6 commit liên tiếp chỉnh tay… clamp/vw…") và mục "Do NOT" cấm đúng
#   pattern này — nhưng bài học chỉ nằm trong văn bản, không có gate, nên code
#   sống tiếp nhiều tháng. Ghi chú baseline của gate-design-tokens còn xếp cả
#   24 `clamp()` vào diện "CÓ CHỦ ĐÍCH" trong khi 21 cái KHÔNG phải — baseline
#   có lý do sai còn tệ hơn không có baseline: nó rửa nợ thành quyết định.
#
# LUẬT: hard-fail ở 0. Cây hiện tại sạch và phải giữ nguyên.
#
# PHẠM VI — chỉ các thuộc tính LẼ RA phải lấy từ thang:
#   font-size · gap / row-gap / column-gap · padding* · letter-spacing
#
# ĐƯỢC PHÉP, KHÔNG tính là vi phạm:
#   · `cqi` / `cqw` / `cqh` — đơn vị CONTAINER QUERY. Co theo container cha,
#     không theo viewport ⇒ đây là fluid type ĐÚNG NGHĨA (KPI tile).
#   · `min-height: 100vh` / `min-width: min(560px, 84vw)` — viewport unit dùng
#     cho KHUNG BỐ CỤC hoặc CHẶN TRÊN, không lái cỡ chữ. Ngoài phạm vi gate.
#   · `.login-*` / `.lock-*` — hero marketing, fluid có chủ đích, và không phải
#     bề mặt shopfloor.
#   · `@media print` — L39 quản.
#
# Tested: PASS trên cây hiện tại; FAIL khi inject `font-size: clamp(...vw)`
# — chạy với --self-test.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
CSSDIR="$here/../src/CCL.MES.Hybrid.Razor/wwwroot/css"
CSS="$CSSDIR/app.css"
IX="$CSSDIR/ix.css"
[ -f "$CSS" ] || { echo "[gate:viewport] không thấy app.css tại $CSS"; exit 2; }
[ -f "$IX" ]  || { echo "[gate:viewport] không thấy ix.css tại $IX"; exit 2; }

scan_vp() {
  python3 - "$@" <<'PY'
import re, sys

PROPS = (r'(font-size|gap|row-gap|column-gap|padding|padding-block|'
         r'padding-inline|padding-top|padding-bottom|padding-left|padding-right|'
         r'letter-spacing)')
VIEWPORT = re.compile(r'\d(?:\.\d+)?v(?:w|h|min|max)\b')
SKIP_SEL = re.compile(r'\.login-|\.lock-', re.I)

src = ""
for p in sys.argv[1:]:
    src += open(p, encoding='utf-8').read() + "\n"
src = re.sub(r'/\*.*?\*/', '', src, flags=re.S)
src = re.sub(r'@media\s+print\s*\{.*?\n\}', '', src, flags=re.S)

for m in re.finditer(r'([^{}]+)\{([^{}]*)\}', src):
    sel, body = m.group(1).strip(), m.group(2)
    if not sel or sel.startswith('@') or SKIP_SEL.search(sel):
        continue
    for pm in re.finditer(PROPS + r'\s*:\s*([^;}\n]+)', body):
        val = pm.group(2).strip()
        if VIEWPORT.search(val):
            print(f"{sel.split(',')[0].strip()[:52]}\t{pm.group(1)}\t{val[:46]}")
PY
}

if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp)"; tmpix="$(mktemp)"
  trap 'rm -f "$tmp" "$tmpix"' EXIT
  cp "$CSS" "$tmp"; cp "$IX" "$tmpix"
  before="$(scan_vp "$CSS" "$IX" | grep -c . || true)"

  printf '\n.gate-selftest-vp { font-size: clamp(14px, 2vw, 20px); }\n' >> "$tmp"
  after="$(scan_vp "$tmp" "$tmpix" | grep -c . || true)"
  if [ "$after" -gt "$before" ]; then
    echo "[gate:viewport] self-test OK (clamp(...vw) trên font-size bị bắt: $before -> $after)"
  else
    echo "[gate:viewport:FAIL] self-test HỎNG — không bắt được clamp(...vw)"
    exit 1
  fi

  # Container query là HỢP LỆ, không được báo nhầm
  cp "$CSS" "$tmp"
  printf '\n.gate-selftest-cq { font-size: clamp(20px, 3cqi, 28px); }\n' >> "$tmp"
  cq="$(scan_vp "$tmp" "$tmpix" | grep -c . || true)"
  if [ "$cq" -eq "$before" ]; then
    echo "[gate:viewport] self-test OK (đơn vị container-query cqi KHÔNG bị báo nhầm)"
  else
    echo "[gate:viewport:FAIL] self-test HỎNG — cqi bị báo nhầm là vi phạm"
    exit 1
  fi

  # min-height: 100vh (khung bố cục) HỢP LỆ, ngoài phạm vi
  cp "$CSS" "$tmp"
  printf '\n.gate-selftest-shell { min-height: 100vh; }\n' >> "$tmp"
  sh="$(scan_vp "$tmp" "$tmpix" | grep -c . || true)"
  if [ "$sh" -eq "$before" ]; then
    echo "[gate:viewport] self-test OK (min-height:100vh khung bố cục KHÔNG bị báo nhầm)"
  else
    echo "[gate:viewport:FAIL] self-test HỎNG — khung bố cục bị báo nhầm"
    exit 1
  fi
  exit 0
fi

hits="$(scan_vp "$CSS" "$IX")"
count="$(printf '%s' "$hits" | grep -c . || true)"

echo "[gate:viewport] cỡ chữ/khoảng cách lái bằng đơn vị viewport = $count (bắt buộc 0)"

if [ "$count" -gt 0 ]; then
  echo "[gate:viewport:FAIL] có thuộc tính lấy cỡ từ VIEWPORT thay vì từ thang:"
  printf '%s\n' "$hits" | while IFS=$'\t' read -r sel prop val; do
    [ -z "$sel" ] && continue
    echo "    $sel  →  $prop: $val"
  done
  echo ""
  echo "  vw/vh co theo BỀ RỘNG CỬA SỔ ⇒ mù data-density VÀ mù --ui-scale."
  echo "  Màn hình dùng nó tự rút khỏi hệ density: bật shopfloor không đổi gì."
  echo "  Đã từng làm màn QC Library giữ chữ 13.76px cho người đeo găng."
  echo ""
  echo "  Sửa: chọn ĐÚNG BẬC của thang, và đổi theo density ở một chỗ:"
  echo "      .khối { font-size: var(--x-fs); }"
  echo "      .khối { --x-fs: var(--fs-md); }"
  echo "      :root[data-density=\"shopfloor\"] .khối { --x-fs: var(--fs-base); }"
  echo "  Xem khuôn có sẵn: .qclib-* (app.css) hoặc --op-fs-id/--op-fs-num."
  echo "  Cần fluid THẬT? Dùng đơn vị container-query (cqi), không dùng vw."
  exit 1
fi

echo "[gate:viewport:OK] cỡ chữ + khoảng cách đi qua thang, không lái bằng viewport."
