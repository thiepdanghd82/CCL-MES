#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# L58 gate (A) — BREAKPOINT phải đi qua thang, không tự chế.
#
# VÌ SAO GATE NÀY TỒN TẠI:
#   Audit tablet 2026-08-26 đếm được **12 ngưỡng khác nhau** trong app.css:
#       480 · 520 · 560 · 600 · 640 · 700 · 720 · 900 · 1000 · 1080 · 1081 · 1400
#   Bốn cái sát nhau ở vùng nhỏ (600·640·700·720) và **1080 vs 1081 cách nhau
#   1px**. Mỗi màn tự chọn số của mình, không ai dùng chung.
#
#   Đây là câu chuyện đã xảy ra BA LẦN trong repo này:
#     · MÀU trước L37       — hex rải rác khắp nơi
#     · KÍCH THƯỚC trước L41 — 6 commit chỉnh tay một bảng
#     · CỠ CHỮ trước L49     — "103 cỡ chữ khác nhau cho 527 khai báo"
#   Lần thứ tư là BREAKPOINT. Hệ quả cụ thể: không ai trả lời được "app hỗ trợ
#   tablet nào", vì trong code không có định nghĩa nào về "tablet".
#
# THANG (định nghĩa ở :root trong app.css, đặt tên theo THIẾT BỊ):
#   --bp-phone 480 · --bp-tablet-p 768 · --bp-tablet-l 1024 · --bp-desk 1280
#   --bp-wide 1600 · --bp-ultra 2560
#
#   --bp-ultra thêm 2026-09-09. Thang cũ gộp máy chiếu 1920 với desktop 4K
#   3840 vào cùng một nấc --bp-wide, nhưng đó là hai lớp thiết bị ngược nhau:
#   máy chiếu nhìn TỪ XA (cần khối to, ít cột), 4K ngồi GẦN (thừa bề ngang).
#   Vì không có nấc nào tả được 4K nên Dashboard IQC bỏ trống 1360px = 38%.
#   Ngưỡng này ĐO BẰNG @container trên .qms-page (đã trừ rail): 1920→1632,
#   2560→2272, 3840→3552 — nên 2560 tách đúng 4K, không đụng 32" và máy chiếu.
#
# Detector quy rem/em về px @16 — cùng một ngưỡng viết bằng đơn vị khác vẫn là
# ngưỡng đó, không phải ngoại lệ.
#
# LƯU Ý KỸ THUẬT: CSS chưa cho dùng var() trong điều kiện @media/@container,
# nên thang này là HỢP ĐỒNG + gate chứ không phải cơ chế runtime. Vẫn viết số
# thật trong @media, nhưng chỉ được viết 6 số trên. Cách này y hệt cách L41
# diệt cỡ chữ tự chế: thang không tự ép được, gate mới ép được.
#
# LUẬT: ratchet đi xuống. Đếm số ngưỡng KHÁC BIỆT nằm ngoài thang.
#
# NGOÀI PHẠM VI: @media print · prefers-reduced-motion · max-height (trục dọc,
# thang này chỉ nói về bề rộng).
#
# Tested: PASS trên cây hiện tại; FAIL khi thêm một ngưỡng thứ 13 — --self-test.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

# Ngưỡng ngoài thang còn lại, đo 2026-08-26. Mỗi cái là một màn hình chưa được
# gộp về thang. Ratchet chỉ được giảm — xem docs/AUDIT-RESPONSIVE-TABLET.md §6.
# Đếm bằng chính gate này, không chép tay (luật L57):
#   520(2) 560(1) 600(2) 640(6) 700(1) 720(1) 900(11) 1000(2) 1080(1) 1081(1)
#   1100(1) 1400(3)  = 12 ngưỡng khác biệt / 32 lần dùng
# (480 · 768 · 1024 · 1280 · 1600 · 2560 nằm TRONG thang nên không tính)
BASELINE_BP=12

here="$(cd "$(dirname "$0")" && pwd)"
CSSDIR="$here/../src/CCL.MES.Hybrid.Razor/wwwroot/css"
CSS="$CSSDIR/app.css"
IX="$CSSDIR/ix.css"
[ -f "$CSS" ] || { echo "[gate:bp] không thấy app.css tại $CSS"; exit 2; }

scan_bp() {
  python3 - "$@" <<'PY'
import re, sys
SCALE = {480, 768, 1024, 1280, 1600, 2560}
src = ""
for p in sys.argv[1:]:
    src += open(p, encoding='utf-8').read() + "\n"
src = re.sub(r'/\*.*?\*/', '', src, flags=re.S)

found = {}
for m in re.finditer(r'@(media|container)([^{]*)\{', src):
    cond = m.group(2)
    if 'print' in cond or 'prefers-reduced-motion' in cond:
        continue
    # rem/em quy về px @16: viết `60rem` thay vì `960px` KHÔNG phải là một
    # ngoại lệ, chỉ là cùng ngưỡng đó viết bằng đơn vị khác. Bản đầu của gate
    # chỉ quét `px` nên hai ngưỡng 60rem/48rem lọt hoàn toàn — đúng kiểu lách
    # mà gate này sinh ra để chặn.
    for w in re.finditer(r'(?:min|max)-width\s*:\s*([\d.]+)(px|rem|em)', cond):
        v, unit = float(w.group(1)), w.group(2)
        px = int(round(v if unit == 'px' else v * 16))
        if px not in SCALE:
            found[px] = found.get(px, 0) + 1

for px in sorted(found):
    print(f"{px}\t{found[px]}")
PY
}

if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp)"; tmpix="$(mktemp)"; trap 'rm -f "$tmp" "$tmpix"' EXIT
  cp "$CSS" "$tmp"; cp "$IX" "$tmpix"
  before="$(scan_bp "$CSS" "$IX" | grep -c . || true)"

  printf '\n@media (max-width: 933px) { .gate-selftest-bp { display: none; } }\n' >> "$tmp"
  after="$(scan_bp "$tmp" "$tmpix" | grep -c . || true)"
  [ "$after" -gt "$before" ] \
    && echo "[gate:bp] self-test OK (ngưỡng tự chế 933px bị bắt: $before -> $after)" \
    || { echo "[gate:bp:FAIL] self-test HỎNG — không bắt được ngưỡng tự chế"; exit 1; }

  cp "$CSS" "$tmp"
  printf '\n@media (max-width: 768px) { .gate-selftest-ok { display: none; } }\n' >> "$tmp"
  ok="$(scan_bp "$tmp" "$tmpix" | grep -c . || true)"
  [ "$ok" -eq "$before" ] \
    && echo "[gate:bp] self-test OK (ngưỡng TRONG thang 768px KHÔNG bị báo nhầm)" \
    || { echo "[gate:bp:FAIL] self-test HỎNG — ngưỡng hợp lệ bị báo nhầm"; exit 1; }

  # rem phải bị bắt y như px — 60rem = 960px, ngoài thang.
  cp "$CSS" "$tmp"
  printf '\n@media (max-width: 60rem) { .gate-selftest-rem { display: none; } }\n' >> "$tmp"
  rem="$(scan_bp "$tmp" "$tmpix" | grep -c . || true)"
  [ "$rem" -gt "$before" ] \
    && echo "[gate:bp] self-test OK (ngưỡng viết bằng rem cũng bị bắt: 60rem -> 960px)" \
    || { echo "[gate:bp:FAIL] self-test HỎNG — rem lách qua detector"; exit 1; }

  # 48rem = 768px nằm TRONG thang ⇒ không được báo nhầm.
  cp "$CSS" "$tmp"
  printf '\n@media (max-width: 48rem) { .gate-selftest-remok { display: none; } }\n' >> "$tmp"
  remok="$(scan_bp "$tmp" "$tmpix" | grep -c . || true)"
  [ "$remok" -eq "$before" ] \
    && echo "[gate:bp] self-test OK (48rem = 768px trong thang, không báo nhầm)" \
    || { echo "[gate:bp:FAIL] self-test HỎNG — rem hợp lệ bị báo nhầm"; exit 1; }

  # Nấc 6 (--bp-ultra 2560) vừa thêm: phải được công nhận…
  cp "$CSS" "$tmp"
  printf '\n@container (min-width: 2560px) { .gate-selftest-ultra { display: none; } }\n' >> "$tmp"
  ultra="$(scan_bp "$tmp" "$tmpix" | grep -c . || true)"
  [ "$ultra" -eq "$before" ] \
    && echo "[gate:bp] self-test OK (2560px trong thang, không báo nhầm)" \
    || { echo "[gate:bp:FAIL] self-test HỎNG — nấc 2560 hợp lệ bị báo nhầm"; exit 1; }

  # …nhưng KHÔNG được nới thành "cứ số to là qua": 2561 vẫn phải bị bắt.
  cp "$CSS" "$tmp"
  printf '\n@container (min-width: 2561px) { .gate-selftest-near { display: none; } }\n' >> "$tmp"
  near="$(scan_bp "$tmp" "$tmpix" | grep -c . || true)"
  [ "$near" -gt "$before" ] \
    && echo "[gate:bp] self-test OK (2561px sát nấc vẫn bị bắt: $before -> $near)" \
    || { echo "[gate:bp:FAIL] self-test HỎNG — số sát nấc lách qua"; exit 1; }
  exit 0
fi

hits="$(scan_bp "$CSS" "$IX")"
count="$(printf '%s' "$hits" | grep -c . || true)"

echo "[gate:bp] ngưỡng breakpoint ngoài thang = $count (baseline $BASELINE_BP)"

if [ "$count" -gt "$BASELINE_BP" ]; then
  echo "[gate:bp:FAIL] có ngưỡng breakpoint không nằm trong thang:"
  printf '%s\n' "$hits" | while IFS=$'\t' read -r px n; do
    [ -z "$px" ] && continue
    echo "    ${px}px — dùng $n lần"
  done
  echo ""
  echo "  Thang (đặt tên theo THIẾT BỊ, xem :root trong app.css):"
  echo "     480  --bp-phone     điện thoại / máy quét cầm tay"
  echo "     768  --bp-tablet-p  tablet DỌC — bề mặt xưởng chính"
  echo "    1024  --bp-tablet-l  tablet NGANG"
  echo "    1280  --bp-desk      màn bàn làm việc"
  echo "    1600  --bp-wide      màn rộng / màn treo tường"
  echo "    2560  --bp-ultra     desktop 4K / siêu rộng — ĐO BẰNG @container"
  echo ""
  echo "  Chọn bậc GẦN NHẤT, đừng thêm bậc mới. Cần một bậc thật sự mới thì thêm"
  echo "  vào :root, đặt tên theo thiết bị, dùng ở ≥2 nơi, và ghi lý do."
  echo "  Mẹo: nếu bố cục chỉ vỡ ở đúng một con số lạ, thường thứ cần sửa là"
  echo "  cho phần tử CO LIÊN TỤC (min-width:0 + ellipsis), không phải thêm ngưỡng."
  exit 1
fi

echo "[gate:bp:OK] breakpoint đi qua thang, không có ngưỡng tự chế mới."
