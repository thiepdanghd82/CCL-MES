#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# gate-qc-process-kind — phép phân loại công đoạn IN / CẮT tồn tại ở HAI nơi và
# hai nơi phải liệt kê ĐÚNG CÙNG một tập line.
#
#   server  src/CCL.MES.Application/Services/QcProcessKind.cs
#           → PrintLines / CutLines, dùng để chọn nguồn CAVITY (cỡ mẫu FAI)
#   UI      CCL-MES-Hybrid/src/.../Shared/IpqcDashboard.razor
#           → ProcessForItem(), dùng để chia chip công đoạn
#
# VÌ SAO PHẢI CANH: hai nơi không thể gộp — Hybrid.Razor chỉ tham chiếu
# CCL.MES.Shared + Hybrid.Client, KHÔNG tham chiếu CCL.MES.Application. Nên bản
# sao là bắt buộc, và bản sao không ai canh thì sẽ trôi.
#
# Hậu quả nếu trôi: thêm một line mới vào UI mà quên bên server ⇒ hạng mục hiện
# dưới chip CẮT nhưng cavity giải theo bảng IN. Cả hai đầu đều RA SỐ, màn hình
# trông bình thường, và cỡ mẫu sai không ai phát hiện. Đúng hạng bug mà L85 nói:
# một giá trị nằm đúng ô, không ai kiểm, không gì báo.
#
# exit 0 khớp · 1 lệch · 2 không kết luận được (thiếu file / đổi cấu trúc)
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
HYBRID="$(cd "$here/.." && pwd)"
ROOT="$(cd "$HYBRID/.." && pwd)"

SRV="$ROOT/src/CCL.MES.Application/Services/QcProcessKind.cs"
UI="$HYBRID/src/CCL.MES.Hybrid.Razor/Shared/IpqcDashboard.razor"

# Rút tập line từ phía SERVER: nội dung hai mảng PrintLines / CutLines.
srv_lines() {  # $1 = PrintLines | CutLines
  grep -A 1 "IReadOnlyList<string> $1" "$SRV" 2>/dev/null \
    | grep -oE '"[A-Z_]+"' | tr -d '"' | sort -u
}

# Rút tập line từ phía UI: các nhánh của switch trong ProcessForItem.
ui_lines() {   # $1 = ProcPrint | ProcCut
  awk '/ProcessForItem/,/^    }/' "$UI" 2>/dev/null \
    | grep "=> $1" | grep -oE '"[A-Z_]+"' | tr -d '"' | sort -u
}

fail=0
for pair in "PrintLines:ProcPrint:IN" "CutLines:ProcCut:CẮT"; do
  srv_key="${pair%%:*}"; rest="${pair#*:}"; ui_key="${rest%%:*}"; label="${rest#*:}"

  a="$(srv_lines "$srv_key")"; b="$(ui_lines "$ui_key")"

  if [ -z "$a" ] || [ -z "$b" ]; then
    echo "[gate:qc-process-kind:FAIL] không rút được danh sách $label."
    echo "  server($srv_key)=[$(echo $a)]  ui($ui_key)=[$(echo $b)]"
    echo "  Cấu trúc file đã đổi — sửa bộ rút, ĐỪNG gỡ gate."
    exit 2
  fi

  if [ "$a" != "$b" ]; then
    echo "[gate:qc-process-kind:FAIL] tập line $label LỆCH giữa server và UI:"
    echo "  server: $(echo $a)"
    echo "  UI    : $(echo $b)"
    echo "  Chỉ có ở server: $(comm -23 <(echo "$a") <(echo "$b") | tr '\n' ' ')"
    echo "  Chỉ có ở UI    : $(comm -13 <(echo "$a") <(echo "$b") | tr '\n' ' ')"
    fail=1
  else
    echo "[gate:qc-process-kind] $label khớp: $(echo $a)"
  fi
done

if [ "$fail" -ne 0 ]; then
  echo "  Sửa cho hai nơi cùng một tập. Cavity giải sai công đoạn thì cỡ mẫu sai"
  echo "  mà màn hình vẫn ra số — không ai nhìn thấy."
  exit 1
fi

echo "[gate:qc-process-kind:OK] server và UI cùng một tập line IN/CẮT."
exit 0
