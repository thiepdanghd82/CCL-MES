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

# Đo lại sau khi chuẩn hoá typography: 527 → 38.
# 38 còn lại là CÓ CHỦ ĐÍCH và không nên token-hoá:
#   24 × clamp()  fluid theo container (KPI card, login hero) — thang cố định
#                 không diễn đạt được ý này
#   11 × pt       print CSS, L39 quản (on-screen == bản in, đổi là phá hợp đồng)
#    2 × em       tương đối theo cha — đổi sang rem là đổi NGHĨA
#    1 × inherit
BASELINE_RAW_FS=38

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

# (C) L46 — MỌI phase khai báo trong Domain phải có mặt trong PhaseVisual.
# Thiếu một phase = nó rơi vào nhánh mặc định Neutral và hiện MÀU XÁM âm thầm ở
# mọi màn hình. Hard-fail: thêm state mới mà quên bảng màu là đúng thứ CI phải chặn.
PV="$here/../src/CCL.MES.Hybrid.Client/Status/PhaseVisual.cs"
DOMAIN="$here/../../src/CCL.MES.Domain/StateMachine"
if [ -f "$PV" ] && [ -d "$DOMAIN" ]; then
  missing="$(python3 - "$PV" "$DOMAIN" <<'PYP'
import re,sys,os
pv=open(sys.argv[1],encoding='utf-8').read()
miss=[]
for fn in ('MesPhase.cs','LegPhase.cs'):
    p=os.path.join(sys.argv[2],fn)
    if not os.path.exists(p): continue
    src=open(p,encoding='utf-8').read()
    body=src[src.index('enum'):] if 'enum' in src else src
    for m in re.finditer(r'^\s{4}([A-Z][A-Z0-9_]+)\s*=\s*\d+', body, re.M):
        tok=m.group(1)
        if '"'+tok+'"' not in pv: miss.append(fn.replace('.cs','')+'.'+tok)
print(','.join(sorted(set(miss))))
PYP
)"
  if [ -n "$missing" ]; then
    echo "[gate:tokens:FAIL] phase khai báo trong Domain nhưng thiếu trong PhaseVisual: $missing"
    echo "  Thiếu ⇒ rơi vào Neutral và hiện màu xám âm thầm ở mọi màn hình."
    rc=1
  else
    echo "[gate:tokens] phase phủ trong PhaseVisual        = đủ"
  fi
fi

# (D) D6.3 — trang lưới có ColClass() thì PHẢI áp cho CẢ <th> lẫn <td>.
# Bug đã xảy ra: class chỉ gắn ở <th> nên header căn phải còn số căn trái suốt
# 5 trang lưới. Kiểm tĩnh rẻ hơn nhiều so với phát hiện bằng mắt.
RZDIR="$here/../src/CCL.MES.Hybrid.Razor/Pages"
if [ -d "$RZDIR" ]; then
  badnum="$(python3 - "$RZDIR" <<'PYN'
import re,sys,glob,os
bad=[]
for f in glob.glob(os.path.join(sys.argv[1],'*.razor')):
    t=open(f,encoding='utf-8').read()
    if 'ColClass(' not in t: continue
    # <td> render ô theo cột mà không mang class nào
    for m in re.finditer(r'<td>\s*@Render[A-Za-z]*\(row', t):
        bad.append(os.path.basename(f))
        break
print(','.join(sorted(set(bad))))
PYN
)"
  if [ -n "$badnum" ]; then
    echo "[gate:tokens:FAIL] lưới áp ColClass cho <th> nhưng KHÔNG cho <td>: $badnum"
    echo "  Hệ quả: header căn phải, số trong ô vẫn căn trái ⇒ không so sánh được theo cột."
    echo "  Sửa: <td class=\"@ColClass(col.Id)\">…"
    rc=1
  else
    echo "[gate:tokens] cột số áp class cả th+td       = đủ"
  fi
fi

# (E) Họ chữ + độ đậm phải qua token — hard-fail 0.
# Trước chuẩn hoá: 6 stack monospace KHÁC NHAU cho cùng một việc (mã part / mã WO
# là thứ phải đọc chính xác từng ký tự, sáu stack = sáu kết quả render), và 8 độ
# đậm 300→900 trong khi font hệ thống không có đủ 8 nét thật ⇒ trình duyệt tổng
# hợp nét giả, 700 với 800 thường ra y hệt.
rawtypo="$(python3 - "$CSS" "$IX" <<'PYE'
import re,sys
fam=w=0
for p in sys.argv[1:]:
    s=open(p,encoding='utf-8').read()
    fam+=len([v for v in re.findall(r'font-family\s*:\s*([^;}\n]+)',s)
              if 'var(' not in v and v.strip()!='inherit'])
    w  +=len([v for v in re.findall(r'font-weight\s*:\s*([^;}\n]+)',s) if 'var(' not in v])
print(f'{fam} {w}')
PYE
)"
rawfam="${rawtypo% *}"; rawwt="${rawtypo#* }"
echo "[gate:tokens] font-family không qua token = $rawfam (bắt buộc 0)"
echo "[gate:tokens] font-weight không qua token = $rawwt (bắt buộc 0)"
if [ "$rawfam" -gt 0 ] || [ "$rawwt" -gt 0 ]; then
  echo "[gate:tokens:FAIL] họ chữ / độ đậm viết thẳng thay vì qua token."
  echo "  Dùng var(--font-sans) · var(--font-mono) · var(--fw-regular|medium|semibold|bold)."
  rc=1
fi

for need in '\-\-fs-2xs' '\-\-fs-3xl' '\-\-font-sans' '\-\-font-mono' '\-\-fw-semibold' '\-\-fs-md' '\-\-fs-base' '\-\-sp-4' '\-\-r-md' '\-\-mo-base' '\-\-focus-ring' '\-\-d-tap' '\-\-d-row-h' '\-\-d-font'; do
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
