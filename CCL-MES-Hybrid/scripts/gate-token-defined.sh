#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# L56 gate — mọi `var(--x)` phải trỏ tới một token CÓ THẬT.
#
# VÌ SAO GATE NÀY TỒN TẠI (sự cố có thật, đã ra tới production):
#   `var(--chưa-định-nghĩa)` KHÔNG im lặng bỏ qua và KHÔNG dùng giá trị mặc
#   định. Nó hỏng ở computed-value time: thuộc tính trở thành `unset` — màu chữ
#   tụt về `inherit`, nền/viền tụt về `initial` (trong suốt / không viền).
#
#   Đo được ngày 2026-08-26 trên cây trước khi sửa:
#     · `.qc-result-toggle.is-active.qc-result-na` dùng `background: var(--c-ink-4)`
#       với `color: white` ⇒ nền rgba(0,0,0,0) + chữ trắng + viền trắng
#       ⇒ nút "N/A" khi được chọn là VÔ HÌNH trên nền sáng.
#     · 69 nhãn phụ dùng `--c-ink-4` hiện màu ĐEN (inherit) thay vì mờ
#       ⇒ phân cấp bằng màu (iX #4) hỏng âm thầm trên khắp app.
#     · Cả module IQC (`.iqc-*`) viết theo `--c-border` / `--c-surface-2` /
#       `--c-ink-1` ⇒ render KHÔNG CÓ VIỀN NÀO.
#
#   Điểm đau nhất: repo ĐÃ trả giá cho đúng lỗi này một lần rồi. Chú thích tại
#   `app.css` §`.grid-btn.grid-btn-secondary` ghi nguyên văn rằng
#   `.grid-btn-secondary` từng tham chiếu "the UNDEFINED token --c-ink-4" làm nút
#   trắng-trên-trắng, và nó được vá RIÊNG LẺ. Không ai quét 95 chỗ còn lại, và
#   không gate nào được thêm — nên lỗi sống tiếp thêm nhiều tháng.
#   Gate này chính là "cơ chế chặn tái phát" mà lần vá đó còn thiếu.
#
# LUẬT: hard-fail ở 0. KHÔNG ratchet — không tồn tại "nợ hợp lệ" ở đây. Một
# token hoặc được định nghĩa, hoặc không; không có vùng xám, không dương tính
# giả. Cây hiện tại sạch và phải giữ nguyên như vậy.
#
# NGOẠI LỆ HỢP LỆ DUY NHẤT: `var(--x, fallback)` — có giá trị dự phòng thì
# thiếu định nghĩa không gây hỏng. Ví dụ đang dùng: `var(--text-muted, #666)`.
#
# Comment bị loại bỏ trước khi so khớp — một chú thích NHẮC TỚI tên token là
# tài liệu, không phải cách dùng.
#
# Tested: PASS trên cây hiện tại; FAIL khi inject `var(--gate-selftest-ghost)`
# — chạy với --self-test.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
CSSDIR="$here/../src/CCL.MES.Hybrid.Razor/wwwroot/css"
CSS="$CSSDIR/app.css"
IX="$CSSDIR/ix.css"
[ -f "$CSS" ] || { echo "[gate:token-defined] không thấy app.css tại $CSS"; exit 2; }
[ -f "$IX" ]  || { echo "[gate:token-defined] không thấy ix.css tại $IX"; exit 2; }

# In ra mỗi dòng một token ma. Rỗng = sạch.
scan_ghosts() {
  python3 - "$@" <<'PY'
import re, sys

src = ""
for path in sys.argv[1:]:
    src += open(path, encoding='utf-8').read() + "\n"

# Bỏ comment: token nhắc trong chú thích là tài liệu, không phải cách dùng.
body = re.sub(r'/\*.*?\*/', '', src, flags=re.S)

# Định nghĩa: `--x:` đứng đầu dòng, hoặc ngay sau `{` / `;`
# (nhiều token khai chung một dòng là hợp lệ: `--r-sm: 4px;  --r-md: 8px;`)
defined = set(re.findall(r'(?:^|[;{])\s*(--[A-Za-z0-9_-]+)\s*:', body, re.M))

# Cách dùng KHÔNG có fallback: `var(--x)` hoặc `var( --x )`, theo sau là `)`
# — dạng `var(--x, y)` có dự phòng nên không tính là hỏng.
used_bare = set(re.findall(r'var\(\s*(--[A-Za-z0-9_-]+)\s*\)', body))

ghosts = sorted(used_bare - defined)
for g in ghosts:
    n = len(re.findall(r'var\(\s*' + re.escape(g) + r'\s*\)', body))
    print(f"{g}\t{n}")
PY
}

if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp)"; tmpix="$(mktemp)"
  trap 'rm -f "$tmp" "$tmpix"' EXIT
  cp "$CSS" "$tmp"; cp "$IX" "$tmpix"
  before="$(scan_ghosts "$CSS" "$IX" | wc -l | tr -d ' ')"
  printf '\n.gate-selftest-xyz { color: var(--gate-selftest-ghost); }\n' >> "$tmp"
  after="$(scan_ghosts "$tmp" "$tmpix" | wc -l | tr -d ' ')"
  if [ "$after" -gt "$before" ]; then
    echo "[gate:token-defined] self-test OK (token ma được phát hiện: $before -> $after)"
  else
    echo "[gate:token-defined:FAIL] self-test HỎNG — bộ dò không bắt được token ma vừa chèn"
    exit 1
  fi
  # Kiểm chứng ngoại lệ fallback KHÔNG bị báo nhầm
  cp "$CSS" "$tmp"
  printf '\n.gate-selftest-fb { color: var(--gate-selftest-ghost2, #666); }\n' >> "$tmp"
  fb="$(scan_ghosts "$tmp" "$tmpix" | wc -l | tr -d ' ')"
  if [ "$fb" -eq "$before" ]; then
    echo "[gate:token-defined] self-test OK (var(--x, fallback) KHÔNG bị báo nhầm)"
  else
    echo "[gate:token-defined:FAIL] self-test HỎNG — dạng có fallback bị báo nhầm là ma"
    exit 1
  fi
  exit 0
fi

ghosts="$(scan_ghosts "$CSS" "$IX")"
count="$(printf '%s' "$ghosts" | grep -c . || true)"

echo "[gate:token-defined] var() trỏ token không tồn tại = $count (bắt buộc 0)"

if [ "$count" -gt 0 ]; then
  echo "[gate:token-defined:FAIL] có var() trỏ tới token CHƯA ĐƯỢC ĐỊNH NGHĨA:"
  printf '%s\n' "$ghosts" | while IFS=$'\t' read -r tok n; do
    [ -z "$tok" ] && continue
    echo "    $tok — dùng $n lần"
  done
  echo ""
  echo "  Đây KHÔNG phải chuyện thẩm mỹ: thuộc tính hỏng ở computed-value time."
  echo "  Màu chữ tụt về inherit; nền/viền tụt về trong suốt/không viền."
  echo "  Đã từng làm một nút trắng-trên-trắng (xem chú thích .grid-btn-secondary)."
  echo ""
  echo "  Sửa: (a) định nghĩa token ở :root trong app.css — nếu nó là một bậc"
  echo "       THẬT của thang; hoặc (b) thêm bí danh trỏ token đã có, ví dụ"
  echo "       --c-ink-4: var(--c-muted);  hoặc (c) sửa chỗ dùng cho đúng tên."
  echo "  KHÔNG bump baseline — gate này không có baseline."
  exit 1
fi

echo "[gate:token-defined:OK] mọi var() đều trỏ token có thật."
