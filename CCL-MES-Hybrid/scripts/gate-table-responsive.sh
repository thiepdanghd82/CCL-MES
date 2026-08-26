#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# L58 gate (B) — bảng rộng hơn tablet ngang PHẢI có luật responsive cho chính nó.
#
# VÌ SAO GATE NÀY TỒN TẠI:
#   App sẽ chạy trên tablet dưới xưởng. Audit 2026-08-26 đếm 42 bảng nhưng chỉ
#   40 luật responsive cho toàn app — và phần lớn 40 luật đó nhắm vào FORM và
#   LƯỚI THẺ, không phải bảng. Kết quả đo được:
#
#     .prepress-table  min-width 1400px  → 0 luật cho bảng   ⚠ BỀ MẶT XƯỞNG
#     .accounts-table  min-width 1200px  → 0 luật
#     .qclib-grid      min-width 1180px  → rule 900px chỉ đổi .qclib-form-ticks
#     .audit-table     min-width 1100px  → 0 luật
#
#   `.prepress-table` là nguy hiểm nhất: trên tablet dọc ~800pt, người đứng máy
#   phải CUỘN NGANG qua một bảng rộng 1400px để xác nhận công đoạn chế bản.
#   Cuộn ngang khi đeo găng, cầm tablet một tay, là cách chắc chắn để bấm nhầm
#   dòng — và ở đây bấm nhầm dòng nghĩa là ký xác nhận sai công đoạn.
#
#   Quy luật quan sát được: màn làm SAU (có container query) thì sập card đúng;
#   màn làm TRƯỚC thì không. Đây không phải quyết định thiết kế — đó là nợ tích
#   luỹ, và trước gate này không có gì chặn nó lớn thêm.
#
# LUẬT: ratchet đi xuống. Bảng khai `min-width` ≥ 1024px (--bp-tablet-l) phải
# có ÍT NHẤT MỘT luật @media/@container nhắm vào cùng họ class VÀ đổi bố cục
# (display · grid-template · flex-direction · white-space · overflow).
#
# GHI CHÚ: "có luật" chỉ là điều kiện CẦN, không phải ĐỦ. Gate tĩnh không biết
# bảng đó có DÙNG ĐƯỢC trên tablet hay không — chỉ ảnh chụp ở --bp-tablet-p mới
# trả lời được. Xem yêu cầu chụp màn trong skill cmes-design-tokens.
#
# Tested: PASS trên cây hiện tại; FAIL khi thêm bảng rộng không có luật —
# chạy với --self-test.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

# Đếm bằng chính gate này (luật L57 — đếm lại, không chép tay). SÁU bảng:
#   .prepress-table 1400 · .accounts-table 1200 · .qclib-grid 1180
#   .audit-table 1100 · .trace-grid 1100 · .trace-prod 1100
# Lưu ý .trace-*: một bộ dò thô ban đầu báo "đã có luật" vì khớp lỏng theo tiền
# tố họ. Kiểm lại từng rule cho thấy @container(max-width:700px) của trace CHỈ
# đổi `.trace-kv-grid` (lưới key-value), KHÔNG đụng hai bảng rộng. Gate này khớp
# chặt hơn nên đếm đúng 6.
# Kế hoạch xử lý: docs/AUDIT-RESPONSIVE-TABLET.md §6.2
# 6 → 5 (.prepress-table sập card) → 4 (.qclib-grid cột dính).
# Còn lại đều là bề mặt VĂN PHÒNG, ưu tiên thấp:
#   .accounts-table 1200 · .audit-table 1100 · .trace-grid 1100 · .trace-prod 1100
BASELINE_TBL=4

here="$(cd "$(dirname "$0")" && pwd)"
CSSDIR="$here/../src/CCL.MES.Hybrid.Razor/wwwroot/css"
CSS="$CSSDIR/app.css"
IX="$CSSDIR/ix.css"
[ -f "$CSS" ] || { echo "[gate:tbl] không thấy app.css tại $CSS"; exit 2; }

scan_tbl() {
  python3 - "$@" <<'PY'
import re, sys

THRESHOLD = 1024          # --bp-tablet-l
# `position` PHẢI có mặt: khuôn CỘT DÍNH (một trong hai khuôn mà chính phần help
# của gate này khuyến nghị) dùng `position: sticky`, không dùng display/grid.
# Thiếu nó thì gate tự mâu thuẫn — bảo người ta dùng cột dính rồi không công
# nhận cột dính. Phát hiện khi áp khuôn cho .qclib-grid, 2026-08-26.
LAYOUT = re.compile(r'(display|grid-template|flex-direction|white-space|overflow|position)\s*:')

src = ""
for p in sys.argv[1:]:
    src += open(p, encoding='utf-8').read() + "\n"
src = re.sub(r'/\*.*?\*/', '', src, flags=re.S)

# 1. Thu các khối @media/@container và nội dung
resp_blocks = []
for m in re.finditer(r'@(?:media|container)[^{]*\{', src):
    st = m.end(); d = 1; i = st
    while i < len(src) and d > 0:
        if src[i] == '{': d += 1
        elif src[i] == '}': d -= 1
        i += 1
    resp_blocks.append(src[st:i])

# 2. Bảng rộng: selector khai min-width >= THRESHOLD
wide = {}
for m in re.finditer(r'([^{}]+)\{([^{}]*)\}', src):
    sel, body = m.group(1).strip(), m.group(2)
    if sel.startswith('@') or not sel.startswith('.'):
        continue
    w = re.search(r'min-width\s*:\s*(\d+)px', body)
    if not w or int(w.group(1)) < THRESHOLD:
        continue
    root = sel.split(',')[0].strip().split()[0].lstrip('.').split(':')[0]
    wide[root] = max(wide.get(root, 0), int(w.group(1)))

# 3. Có luật nhắm vào CÙNG HỌ và đổi bố cục không?
for root, px in sorted(wide.items(), key=lambda x: -x[1]):
    fam = root.rsplit('-', 1)[0] if '-' in root else root
    covered = False
    for blk in resp_blocks:
        for sm in re.finditer(r'\.([a-z][a-z0-9-]*)', blk):
            if not sm.group(1).startswith(fam):
                continue
            # luật đó có thật sự đổi bố cục không?
            seg = blk[max(0, sm.start() - 200): sm.start() + 300]
            if LAYOUT.search(seg):
                covered = True
                break
        if covered:
            break
    if not covered:
        print(f".{root}\t{px}")
PY
}

if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp)"; tmpix="$(mktemp)"; trap 'rm -f "$tmp" "$tmpix"' EXIT
  cp "$CSS" "$tmp"; cp "$IX" "$tmpix"
  before="$(scan_tbl "$CSS" "$IX" | grep -c . || true)"

  printf '\n.gate-selftest-tbl { min-width: 1300px; width: 100%%; }\n' >> "$tmp"
  after="$(scan_tbl "$tmp" "$tmpix" | grep -c . || true)"
  [ "$after" -gt "$before" ] \
    && echo "[gate:tbl] self-test OK (bảng 1300px không luật bị bắt: $before -> $after)" \
    || { echo "[gate:tbl:FAIL] self-test HỎNG — không bắt được bảng rộng trần"; exit 1; }

  # Bảng rộng CÓ luật đổi bố cục ⇒ không được báo nhầm
  cp "$CSS" "$tmp"
  printf '\n.gate-selftest-ok-table { min-width: 1300px; }\n' >> "$tmp"
  printf '@media (max-width: 768px) { .gate-selftest-ok-row { display: block; } }\n' >> "$tmp"
  ok="$(scan_tbl "$tmp" "$tmpix" | grep -c . || true)"
  [ "$ok" -eq "$before" ] \
    && echo "[gate:tbl] self-test OK (bảng rộng CÓ luật sập card KHÔNG bị báo nhầm)" \
    || { echo "[gate:tbl:FAIL] self-test HỎNG — bảng đã có luật vẫn bị báo"; exit 1; }
  exit 0
fi

hits="$(scan_tbl "$CSS" "$IX")"
count="$(printf '%s' "$hits" | grep -c . || true)"

echo "[gate:tbl] bảng ≥1024px không có luật responsive = $count (baseline $BASELINE_TBL)"

if [ "$count" -gt "$BASELINE_TBL" ]; then
  echo "[gate:tbl:FAIL] bảng rộng hơn tablet ngang mà không có luật cho chính nó:"
  printf '%s\n' "$hits" | while IFS=$'\t' read -r sel px; do
    [ -z "$sel" ] && continue
    echo "    $sel  —  min-width: ${px}px"
  done
  echo ""
  echo "  Trên tablet dọc (~768px) bảng này bắt người dùng CUỘN NGANG."
  echo "  Với người đeo găng cầm tablet một tay, cuộn ngang = bấm nhầm dòng."
  echo ""
  echo "  Hai khuôn đã có sẵn trong repo, chọn theo BẢN CHẤT dữ liệu:"
  echo "    · SẬP CARD  — bảng ít cột, mỗi dòng là một thực thể."
  echo "      Khuôn đang chạy: .ipqc-mat-row + [data-label]::before"
  echo "    · CỘT DÍNH  — bảng ma trận nhiều cột (vd lưới tick QC Library)."
  echo "      Giữ 1-2 cột định danh dính trái, phần còn lại cuộn trong vùng riêng."
  echo ""
  echo "  Đừng phát minh khuôn thứ ba. Và nhớ: gate xanh KHÔNG có nghĩa dùng được"
  echo "  — phải chụp màn ở 768px mới biết."
  exit 1
fi

echo "[gate:tbl:OK] bảng rộng đều có luật responsive, không có nợ mới."
