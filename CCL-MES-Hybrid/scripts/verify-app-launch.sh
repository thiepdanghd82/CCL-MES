#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# verify-app-launch — chứng minh bản .app vừa dựng THẬT SỰ CHẠY được.
# Đây là pha 5 VERIFY cho app Catalyst; gate tĩnh không thay được nó.
#
# SỰ CỐ 2026-09-14:
#   Publish xanh, gate 26/26 xanh, `open` xong `pgrep` thấy tiến trình ⇒ tôi
#   báo "app đã mở lại". Nhưng app **abort sau 0,13 giây**: mono
#   `load_aot_module` → `monoeg_g_log` → `abort()`. Ảnh AOT trong bundle không
#   khớp DLL vừa biên dịch lại — publish tăng tiến dùng lại ảnh AOT cũ.
#
#   Phép kiểm cũ ("pgrep thấy tiến trình") vô giá trị: tiến trình có mọc, rồi
#   chết ngay. Người dùng là người phát hiện, không phải tôi.
#
# CÁCH ĐO:
#   Đếm số báo cáo sự cố TRƯỚC khi mở, mở app, giữ N giây, rồi đếm LẠI. Sinh
#   thêm báo cáo = sập, kể cả khi tiến trình vẫn còn (app có thể tự mở lại).
#   Không dựa vào `pgrep` một mình.
#
# Dùng:
#   bash scripts/verify-app-launch.sh [đường-dẫn-.app] [số-giây]
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APP="${1:-$ROOT/CCL-MES-Hybrid/src/CCL.MES.Hybrid/bin/Release/net10.0-maccatalyst/CCL MES.app}"
HOLD="${2:-20}"
REPORTS="$HOME/Library/Logs/DiagnosticReports"

[ -d "$APP" ] || { echo "[verify-app-launch:FAIL] không thấy bundle: $APP"; exit 2; }

count_reports() { ls -1 "$REPORTS"/CCL.MES.Hybrid*.ips 2>/dev/null | wc -l | tr -d ' '; }

before="$(count_reports)"
echo "[verify-app-launch] bundle : ${APP#$ROOT/}"
echo "[verify-app-launch] báo cáo sự cố trước khi mở: $before"

pkill -f "CCL MES.app/Contents/MacOS" 2>/dev/null
sleep 2

open "$APP" || { echo "[verify-app-launch:FAIL] không mở được bundle."; exit 1; }

for _ in $(seq 1 "$HOLD"); do sleep 1; done

after="$(count_reports)"
pid="$(pgrep -f 'CCL MES.app/Contents/MacOS' | head -1)"

if [ "$after" -gt "$before" ]; then
  newest="$(ls -t "$REPORTS"/CCL.MES.Hybrid*.ips 2>/dev/null | head -1)"
  echo "[verify-app-launch:FAIL] app SẬP — báo cáo sự cố mới: $(basename "$newest")"
  echo "  Khung gây sập (10 dòng đầu của luồng triggered):"
  python3 - "$newest" <<'PY' 2>/dev/null || true
import json,sys
b=json.loads(open(sys.argv[1]).read().split("\n",1)[1])
imgs=b.get("usedImages",[])
for t in b.get("threads",[]):
    if t.get("triggered"):
        for fr in t.get("frames",[])[:10]:
            im=imgs[fr["imageIndex"]] if fr.get("imageIndex",-1)<len(imgs) else {}
            print(f"    {im.get('name','?'):<24} {fr.get('symbol','')}")
        break
PY
  echo "  Nếu thấy load_aot_module / mono_assembly_* : ảnh AOT lệch DLL."
  echo "  Chữa: xoá bin+obj maccatalyst rồi publish lại SẠCH (đừng publish tăng tiến)."
  exit 1
fi

if [ -z "$pid" ]; then
  echo "[verify-app-launch:FAIL] tiến trình không còn sống sau ${HOLD}s (không sinh báo cáo — có thể bị thoát êm)."
  exit 1
fi

echo "[verify-app-launch:OK] app sống liên tục ${HOLD}s (PID $pid), không sinh báo cáo sự cố mới."
exit 0
