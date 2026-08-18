#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# L41 gate — KÍCH THƯỚC trong app.css phải qua thang token, y như MÀU đã qua
# token từ L37. Và hai density (office / shopfloor) phải còn nguyên.
#
# Kiểm 2 phần:
#   (A) PATTERN — :root phải còn thang chữ (--fs-*), thang khoảng cách (--sp-*),
#       token density (--d-tap/--d-row-h/--d-font) và khối [data-density="shopfloor"].
#       Mất bất kỳ cái nào = hệ thiết kế bị tháo → FAIL cứng.
#   (B) RATCHET — số khai báo `font-size:` KHÔNG dùng var() không được tăng.
#
# Triệu chứng đã trả giá: 6 commit liên tiếp chỉnh tay một bảng QC
# (0.9rem→1.08rem, nới cột 3.4%, clamp/vw…) vì không có thang để chọn.
#
# Tested: PASS trên cây hiện tại; FAIL khi thêm `font-size: 13px` (--self-test).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

BASELINE_RAW_FS=527   # đo 2026-08-18, trước khi bắt đầu chuyển dần sang var(--fs-*)

here="$(cd "$(dirname "$0")" && pwd)"
CSSDIR="$here/../src/CCL.MES.Hybrid.Razor/wwwroot/css"
CSS="$CSSDIR/app.css"
IX="$CSSDIR/ix.css"      # CCL iX foundation — cùng thang, quét chung
[ -f "$CSS" ] || { echo "[gate:tokens] không thấy app.css tại $CSS"; exit 2; }
[ -f "$IX" ]  || { echo "[gate:tokens] không thấy ix.css tại $IX"; exit 2; }

count_raw_fs() {
  python3 - "$@" <<'PY2'
import re,sys
n=0
for path in sys.argv[1:]:
    css=open(path,encoding='utf-8').read()
    n+=sum(1 for v in re.findall(r'font-size\s*:\s*([^;}\n]+)',css) if 'var(' not in v)
print(n)
PY2
}

if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp)"; trap 'rm -f "$tmp"' EXIT
  cp "$CSS" "$tmp"
  before="$(count_raw_fs "$CSS" "$IX")"
  printf '\n.gate-selftest-fs { font-size: 13px; }\n' >> "$tmp"
  after="$(count_raw_fs "$tmp" "$IX")"
  [ "$after" -gt "$before" ] \
    && { echo "[gate:tokens] self-test OK (font-size literal bị bắt: $before -> $after)"; exit 0; } \
    || { echo "[gate:tokens] self-test FAILED — detector không bắt được"; exit 1; }
fi

rc=0
for need in '\-\-fs-md' '\-\-fs-base' '\-\-sp-4' '\-\-r-md' '\-\-mo-base' '\-\-focus-ring' '\-\-d-tap' '\-\-d-row-h' '\-\-d-font'; do
  if ! grep -qE "$need\s*:" "$CSS" "$IX"; then
    echo "[gate:tokens:FAIL] thiếu token bắt buộc trong :root → $(echo "$need" | tr -d '\\')"
    rc=1
  fi
done
if ! grep -q 'data-density="shopfloor"' "$CSS" "$IX"; then
  echo "[gate:tokens:FAIL] mất khối [data-density=\"shopfloor\"] — density xưởng bị tháo."
  rc=1
fi

raw="$(count_raw_fs "$CSS" "$IX")"
echo "[gate:tokens] font-size không dùng var() = $raw (baseline $BASELINE_RAW_FS)"
if [ "$raw" -gt "$BASELINE_RAW_FS" ]; then
  echo "[gate:tokens:FAIL] có cỡ chữ mới đặt bằng số thay vì bậc thang."
  echo "  Dùng var(--fs-*) hoặc var(--d-font). Không có bậc vừa ⇒ bố cục sai, không phải lý do viết 1.08rem."
  rc=1
fi
[ $rc -eq 0 ] && echo "[gate:tokens:OK] thang + hai density còn nguyên, không có cỡ chữ tự chế mới."
exit $rc
