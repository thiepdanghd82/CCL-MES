#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# L59 gate — `width: 100%` + padding/border PHẢI đi kèm `box-sizing: border-box`.
#
# VÌ SAO GATE NÀY TỒN TẠI:
#   Dưới `content-box` (mặc định của CSS), `width: 100%` KHÔNG tính padding và
#   border — phần tử rộng hơn khung chứa đúng bằng padding+border. Không có
#   cảnh báo, không có lỗi; chỉ có một ô tràn ra ngoài mép.
#
#   Henry bắt được ngày 2026-08-26 ở ô "Approval / rejection reason" của IPQC:
#     .ipqc-input { width: 100%; padding: 8px 10px; border: 1px solid …; }
#   ⇒ tràn 22px, trong khi hai nút ngay dưới dừng đúng lề. Nhìn là thấy sai,
#   nhưng đọc CSS thì từng dòng đều hợp lệ.
#
#   ĐIỂM ĐAU: app.css đã có **13 chỗ khai `box-sizing` LẺ** (`.login-input`,
#   `.lock-input`, …). Tức lỗi này đã được phát hiện ít nhất 13 lần và vá lẻ
#   13 lần — chưa lần nào thành luật. Quét lại thấy còn **22 selector** cùng
#   bệnh đang chờ tràn. Đúng dạng thất bại L4/L56/L58 đã nêu: bài học được vá
#   một chỗ, không ai quét hết, không gate nào được thêm.
#
# ĐÃ SỬA: reset có phạm vi `input, select, textarea, button` ở đầu app.css.
# KHÔNG lật toàn cục `*` — 7.3k dòng CSS này đã tinh chỉnh bằng mắt dưới
# content-box; lật hết mà không xem được 105 màn là đánh cược, không phải sửa.
#
# LUẬT: ratchet đi xuống. Đếm selector có `width: 100%` VÀ (padding có số HOẶC
# border có số) mà KHÔNG khai `box-sizing` — trừ các thẻ đã được reset ở trên.
#
# Tested: PASS trên cây hiện tại; FAIL khi thêm một selector mới cùng bệnh —
# chạy với --self-test.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

# Đếm bằng chính gate này (luật L57 — đếm lại, không chép tay): 22 trước khi
# reset, còn **17** sau khi reset form-control phủ 5 cái. Phần còn lại là
# CONTAINER (.login-card · .grid · .spec-dwg-card-head · .drawing-preview-frame
# · …) — ngoài phạm vi reset form-control có chủ đích. Kế hoạch: xử lý từng
# cụm kèm ảnh chụp, xem docs/AUDIT-RESPONSIVE-TABLET.md.
BASELINE_BOX=17

here="$(cd "$(dirname "$0")" && pwd)"
CSSDIR="$here/../src/CCL.MES.Hybrid.Razor/wwwroot/css"
CSS="$CSSDIR/app.css"
IX="$CSSDIR/ix.css"
[ -f "$CSS" ] || { echo "[gate:box] không thấy app.css tại $CSS"; exit 2; }

scan_box() {
  python3 - "$@" <<'PY'
import re, sys

# Thẻ đã được reset border-box ở đầu app.css ⇒ selector nhắm vào chúng thì an toàn.
RESET_TAGS = re.compile(r'\b(input|select|textarea|button)\b')

src = ""
for p in sys.argv[1:]:
    src += open(p, encoding='utf-8').read() + "\n"
src = re.sub(r'/\*.*?\*/', '', src, flags=re.S)

for m in re.finditer(r'([^{}]+)\{([^{}]*)\}', src):
    sel, body = m.group(1).strip(), m.group(2)
    if not sel or sel.startswith('@'):
        continue
    if 'box-sizing' in body:
        continue
    if not re.search(r'(?:^|;)\s*width\s*:\s*100%', body):
        continue
    has_pad = re.search(r'(?:^|;)\s*padding[a-z-]*\s*:\s*[^;}]*\d', body)
    has_bor = re.search(r'(?:^|;)\s*border\s*:\s*\d', body)
    if not (has_pad or has_bor):
        continue
    first = sel.split(',')[0].strip()
    if RESET_TAGS.search(first):        # đã được reset form-control phủ
        continue
    print(first[:56])
PY
}

if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp)"; tmpix="$(mktemp)"; trap 'rm -f "$tmp" "$tmpix"' EXIT
  cp "$CSS" "$tmp"; cp "$IX" "$tmpix"
  before="$(scan_box "$CSS" "$IX" | grep -c . || true)"

  printf '\n.gate-selftest-box { width: 100%%; padding: 8px 10px; border: 1px solid red; }\n' >> "$tmp"
  after="$(scan_box "$tmp" "$tmpix" | grep -c . || true)"
  [ "$after" -gt "$before" ] \
    && echo "[gate:box] self-test OK (width:100%+padding không box-sizing bị bắt: $before -> $after)" \
    || { echo "[gate:box:FAIL] self-test HỎNG — không bắt được"; exit 1; }

  cp "$CSS" "$tmp"
  printf '\n.gate-selftest-ok { width: 100%%; padding: 8px; box-sizing: border-box; }\n' >> "$tmp"
  ok="$(scan_box "$tmp" "$tmpix" | grep -c . || true)"
  [ "$ok" -eq "$before" ] \
    && echo "[gate:box] self-test OK (đã khai box-sizing KHÔNG bị báo nhầm)" \
    || { echo "[gate:box:FAIL] self-test HỎNG — khai đúng vẫn bị báo"; exit 1; }
  exit 0
fi

hits="$(scan_box "$CSS" "$IX")"
count="$(printf '%s' "$hits" | grep -c . || true)"

echo "[gate:box] width:100% + padding/border thiếu box-sizing = $count (baseline $BASELINE_BOX)"

if [ "$count" -gt "$BASELINE_BOX" ]; then
  echo "[gate:box:FAIL] có phần tử sẽ TRÀN khỏi khung chứa:"
  printf '%s\n' "$hits" | head -20 | sed 's/^/    /'
  echo ""
  echo "  Dưới content-box, width:100% KHÔNG tính padding+border ⇒ phần tử rộng"
  echo "  hơn khung chứa đúng bằng padding+border. Không có cảnh báo nào."
  echo ""
  echo "  Sửa: thêm 'box-sizing: border-box' vào rule đó."
  echo "  Form control (input/select/textarea/button) đã được reset ở đầu"
  echo "  app.css — nếu selector của bạn nhắm vào chúng thì không cần khai lại."
  exit 1
fi

echo "[gate:box:OK] không có phần tử tràn khung mới."
