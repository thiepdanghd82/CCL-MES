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

# Phép so — tách thành hàm để `--self-test` chạy ĐÚNG bộ rút và ĐÚNG phép so của
# lượt thật (chỉ đổi $SRV/$UI sang bản sao). Nếu self-test tự viết lại logic so
# thì nó chỉ chứng minh chính nó, và sẽ trôi khỏi detector lúc nào không biết —
# cùng một luật với các gate khác trong bộ này.
compare_all() {
  local fail=0
  for pair in "PrintLines:ProcPrint:IN" "CutLines:ProcCut:CẮT"; do
    local srv_key="${pair%%:*}"; local rest="${pair#*:}"
    local ui_key="${rest%%:*}"; local label="${rest#*:}"

    local a b
    a="$(srv_lines "$srv_key")"; b="$(ui_lines "$ui_key")"

    if [ -z "$a" ] || [ -z "$b" ]; then
      echo "[gate:qc-process-kind:FAIL] không rút được danh sách $label."
      echo "  server($srv_key)=[$(echo $a)]  ui($ui_key)=[$(echo $b)]"
      echo "  Cấu trúc file đã đổi — sửa bộ rút, ĐỪNG gỡ gate."
      return 2
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
  return $fail
}

# ── self-test ─────────────────────────────────────────────────────────────────
# Chứng minh detector CÒN BẮT ĐƯỢC, theo cả hai chiều: bản sao nguyên vẹn phải
# XANH (không báo động giả), bản sao đã tiêm một line chỉ-có-ở-UI phải ĐỎ.
# Kiểu trôi thật ngoài đời đúng là kiểu này: thêm line vào UI, quên bên server.
if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  cp "$SRV" "$tmp/srv.cs" 2>/dev/null || { echo "[gate:qc-process-kind] self-test FAILED — không đọc được $SRV"; exit 1; }
  cp "$UI"  "$tmp/ui.razor" 2>/dev/null || { echo "[gate:qc-process-kind] self-test FAILED — không đọc được $UI"; exit 1; }

  SRV="$tmp/srv.cs"; UI="$tmp/ui.razor"
  if ! compare_all >/dev/null 2>&1; then
    echo "[gate:qc-process-kind] self-test FAILED — bản sao NGUYÊN VẸN đã bị báo lệch (báo động giả)."
    exit 1
  fi

  # Tiêm: UI nhận thêm FOIL vào nhánh CẮT, server không có.
  python3 - "$tmp/ui.razor" <<'PY'
import io,sys
p=sys.argv[1]; s=io.open(p,encoding='utf-8').read()
old='"PRESS_CNC" or "FINISHING" => ProcCut,'
assert s.count(old)==1, f'self-test không tìm thấy mỏ neo để tiêm ({s.count(old)} lần)'
io.open(p,'w',encoding='utf-8').write(s.replace(old,'"PRESS_CNC" or "FINISHING" or "FOIL" => ProcCut,'))
PY
  [ $? -eq 0 ] || { echo "[gate:qc-process-kind] self-test FAILED — không tiêm được (cấu trúc UI đã đổi)."; exit 1; }

  out="$(compare_all 2>&1)"; rc=$?
  if [ "$rc" -eq 1 ] && echo "$out" | grep -q "FOIL"; then
    echo "[gate:qc-process-kind] self-test OK — bản nguyên vẹn XANH, và một line"
    echo "                       chỉ-có-ở-UI (FOIL) bị bắt đúng: $(echo "$out" | grep 'Chỉ có ở UI')"
    exit 0
  fi
  echo "[gate:qc-process-kind] self-test FAILED — tiêm FOIL vào UI mà detector không bắt (rc=$rc):"
  printf '%s\n' "$out"
  exit 1
fi

# ── quét thật ─────────────────────────────────────────────────────────────────
compare_all; rc=$?
if [ "$rc" -eq 2 ]; then exit 2; fi
if [ "$rc" -ne 0 ]; then
  echo "  Sửa cho hai nơi cùng một tập. Cavity giải sai công đoạn thì cỡ mẫu sai"
  echo "  mà màn hình vẫn ra số — không ai nhìn thấy."
  exit 1
fi

echo "[gate:qc-process-kind:OK] server và UI cùng một tập line IN/CẮT."
exit 0
