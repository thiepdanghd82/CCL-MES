#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# L57 gate (A) — VÙNG CHẠM của phần tử tương tác phải đi qua `--d-tap`,
#                không phải một số px cứng.
#
# VÌ SAO GATE NÀY TỒN TẠI:
#   `--d-tap` là ràng buộc VẬT LÝ, không phải sở thích. Chính app.css tuyên bố
#   trong khối density: "ngón tay đeo găng không bấm trúng 28px" ⇒ shopfloor
#   yêu cầu ≥44px. Vậy mà trước ngày 2026-08-26, trong 13 gate của repo KHÔNG
#   CÓ CÁI NÀO canh con số đó — chúng canh màu, cỡ chữ, i18n, audit trail, phân
#   tầng, enum, FK, nguồn OEE, print, row action, showcard, confirm toggle,
#   legacy đóng băng. Ràng buộc trung tâm nhất của sản phẩm thì không ai canh.
#
#   Hậu quả đo được trên cây trước khi sửa:
#     · `.setting-table .c-app input[type=checkbox]` = 20×20px cứng — LỐI DUY
#       NHẤT bật/tắt hạng mục check của khâu SETTING, người đeo găng bấm.
#       Thủng cả ngưỡng WCAG 2.2 (24px) lẫn ngưỡng repo tự khai (44px).
#     · `.fw-light` (nút ĐÓNG của mọi FloatingWindow) = 19.5px — thấp hơn CẢ
#       ngưỡng office 28px.
#     · `.row-kebab` = 28px cứng; `.md-drawer-close` không đặt kích thước nên
#       vùng chạm bằng đúng glyph ✕ (~11×21px).
#   Điểm đau: khuôn ĐÚNG đã tồn tại sẵn trong repo từ lâu
#   (`.ipqc-applicable-check` dùng `var(--d-tap)` kèm chú thích "glove-safe",
#   `.iqc-line-select` cũng vậy) — nhưng không có gate nên các surface khác cứ
#   lặng lẽ trượt ra ngoài khuôn.
#
# LUẬT (ratchet đi xuống):
#   Đếm số selector TƯƠNG TÁC đặt kích thước bằng px cứng < 44 mà KHÔNG có
#   `var(--d-tap)` / `var(--d-control-h)` / `var(--d-row-h)` trong cùng rule.
#   Số này chỉ được GIẢM. Muốn tăng ⇒ phải giải thích và bump BASELINE.
#
# KHUÔN HỢP LỆ — KHÔNG bị tính là vi phạm:
#   `width: var(--d-tap); min-width: 20px;`  ← sàn px đi KÈM token là ĐÚNG.
#   Sàn tồn tại để hộp không teo ở --ui-scale nhỏ; token vẫn là nguồn chính.
#
# NGOÀI PHẠM VI: `.login-*` (marketing), `@media print`, `.fw-handle` (tay nắm
# resize, không phải nút), và các thuộc tính không quyết định vùng chạm
# (`max-*`, `line-height`, `border-*`, `gap`, `flex-basis`).
#
# Tested: PASS trên cây hiện tại; FAIL khi inject một nút 24px — --self-test.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

# Đếm lại sau đợt sửa vùng chạm 2026-08-26: các surface operator đã về --d-tap.
# 2 chỗ còn lại là nợ CÓ CHỦ ĐÍCH, đều KHÔNG phải bề mặt người đeo găng chạm:
#   · .dwg-viewer-btn (32px) — thanh công cụ xem bản vẽ, bề mặt office/chuột.
#   · .rs-checklist-item input (22px) — CSS CHẾT: 0 lần dùng trong toàn bộ
#     .razor/.cs/.js. Nên xoá hẳn thay vì sửa; giữ trong baseline để lần dọn
#     CSS chết tới đây thì ratchet tự siết xuống 1 rồi 0.
BASELINE_TAP=2

here="$(cd "$(dirname "$0")" && pwd)"
CSSDIR="$here/../src/CCL.MES.Hybrid.Razor/wwwroot/css"
CSS="$CSSDIR/app.css"
IX="$CSSDIR/ix.css"
[ -f "$CSS" ] || { echo "[gate:tap] không thấy app.css tại $CSS"; exit 2; }
[ -f "$IX" ]  || { echo "[gate:tap] không thấy ix.css tại $IX"; exit 2; }

scan_tap() {
  python3 - "$@" <<'PY'
import re, sys

# Selector nào là "tương tác": đủ hẹp để không kéo theo nhãn/ô tĩnh.
INTERACTIVE = re.compile(
    r'(?:^|[\s.#>+~])(?:'
    r'button|a\[href\]|'
    r'[a-z-]*btn[a-z-]*|[a-z-]*chip[a-z-]*|[a-z-]*kebab[a-z-]*|'
    r'[a-z-]*toggle[a-z-]*|[a-z-]*tab\b|[a-z-]*close\b|[a-z-]*check\b'
    r')|input\[type="?(?:checkbox|radio)"?\]',
    re.I)

# Thuộc tính quyết định vùng chạm. `max-*` không tính — nó giới hạn trên.
SIZE = re.compile(r'(?:^|;)\s*(min-height|height|min-width|width)\s*:\s*([^;}\n]+)')

# Token density: có mặt bất kỳ cái nào ⇒ rule đã đi qua thang.
DENSITY = re.compile(r'var\(\s*--d-(?:tap|control-h|row-h)\s*[,)]')

SKIP_SEL = re.compile(r'\.login-|\.fw-handle|\.fw-[nsew]{1,2}\b|::-webkit|@keyframes', re.I)

src = ""
for p in sys.argv[1:]:
    src += open(p, encoding='utf-8').read() + "\n"
src = re.sub(r'/\*.*?\*/', '', src, flags=re.S)          # bỏ comment
src = re.sub(r'@media\s+print\s*\{.*?\n\}', '', src, flags=re.S)  # bỏ print-CSS

hits = []
for m in re.finditer(r'([^{}]+)\{([^{}]*)\}', src):
    sel, body = m.group(1).strip(), m.group(2)
    if not sel or sel.startswith('@'):
        continue
    if SKIP_SEL.search(sel) or not INTERACTIVE.search(sel):
        continue
    if DENSITY.search(body):
        continue                                          # đã qua thang → hợp lệ
    for pm in SIZE.finditer(body):
        val = pm.group(2).strip()
        vm = re.fullmatch(r'(\d+(?:\.\d+)?)px', val)
        if not vm:
            continue
        px = float(vm.group(1))
        if 12 <= px < 44:                                 # <12px là icon/hairline, không phải vùng chạm
            hits.append((sel.split(',')[0].strip()[:56], pm.group(1), px))
            break

seen, out = set(), []
for h in hits:
    if h[0] in seen:
        continue
    seen.add(h[0]); out.append(h)

for sel, prop, px in out:
    print(f"{sel}\t{prop}\t{px:g}")
PY
}

if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp)"; tmpix="$(mktemp)"
  trap 'rm -f "$tmp" "$tmpix"' EXIT
  cp "$CSS" "$tmp"; cp "$IX" "$tmpix"
  before="$(scan_tap "$CSS" "$IX" | grep -c . || true)"

  printf '\n.gate-selftest-btn { min-height: 24px; padding: 0; }\n' >> "$tmp"
  after="$(scan_tap "$tmp" "$tmpix" | grep -c . || true)"
  if [ "$after" -gt "$before" ]; then
    echo "[gate:tap] self-test OK (nút 24px bị bắt: $before -> $after)"
  else
    echo "[gate:tap:FAIL] self-test HỎNG — không bắt được nút 24px"
    exit 1
  fi

  # Khuôn ĐÚNG (token + sàn px) không được báo nhầm
  cp "$CSS" "$tmp"
  printf '\n.gate-selftest-btn2 { width: var(--d-tap); min-width: 20px; }\n' >> "$tmp"
  ok="$(scan_tap "$tmp" "$tmpix" | grep -c . || true)"
  if [ "$ok" -eq "$before" ]; then
    echo "[gate:tap] self-test OK (khuôn 'var(--d-tap) + sàn px' KHÔNG bị báo nhầm)"
  else
    echo "[gate:tap:FAIL] self-test HỎNG — khuôn hợp lệ bị báo nhầm là vi phạm"
    exit 1
  fi
  exit 0
fi

hits="$(scan_tap "$CSS" "$IX")"
count="$(printf '%s' "$hits" | grep -c . || true)"

echo "[gate:tap] vùng chạm px cứng < 44 không qua --d-* = $count (baseline $BASELINE_TAP)"

if [ "$count" -gt "$BASELINE_TAP" ]; then
  echo "[gate:tap:FAIL] có phần tử tương tác đặt vùng chạm bằng số px cứng:"
  printf '%s\n' "$hits" | while IFS=$'\t' read -r sel prop px; do
    [ -z "$sel" ] && continue
    echo "    $sel  →  $prop: ${px}px"
  done
  echo ""
  echo "  --d-tap là ràng buộc VẬT LÝ: 28px office / 44px shopfloor."
  echo "  Số px cứng mù CẢ density LẪN --ui-scale ⇒ người đeo găng không bấm trúng."
  echo ""
  echo "  Khuôn đúng (đã dùng ở .ipqc-applicable-check, .iqc-line-select):"
  echo "      width: var(--d-tap); height: var(--d-tap);"
  echo "      min-width: 20px; min-height: 20px;   /* sàn, không phải nguồn */"
  echo "  Nếu hộp NHÌN phải nhỏ, tách vùng chạm khỏi phần nhìn — xem .fw-light"
  echo "  (nút trong suốt --d-tap, chấm màu ở ::before) hoặc ix.css .app-nav-pin."
  echo "  Nới luôn cột/ô chứa nó, nếu không 44px sẽ tràn cột px cứng."
  exit 1
fi

echo "[gate:tap:OK] vùng chạm đi qua thang density, không có số cứng mới."
