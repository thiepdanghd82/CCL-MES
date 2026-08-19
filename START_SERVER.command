#!/bin/bash
# CCL-MES — Standalone Server Launcher (macOS)  ***ĐÃ ĐÓNG BĂNG 2026-08-19***
#
# FROZEN. App Blazor Server legacy (:5050) không còn phục vụ nhà máy. Launcher
# này chỉ chạy khi có MES_LEGACY_WEB_FORCE=1 (khôi phục khẩn cấp), xem khối
# "0. ĐÓNG BĂNG / FROZEN" bên dưới và
# CCL-MES-Hybrid/docs/CUTOVER-LEGACY-WEB-FREEZE-2026-08-19.md
#
# Phase 6 Bước 6.5 — mirror Ops Control v1.2's server-launcher pattern.
# Double-click this file from Finder to start the SQLite-backed server
# on port 5050 (LAN-reachable). Cmd+C in Terminal to stop.
#
# Port note: user-chosen Q3 was 5000 (ASPNETCORE default), but macOS
# Monterey+ AirPlay Receiver squats port 5000 by default. Picked 5050
# instead — still .NET-family, free on macOS + Windows out of the box.
# Override via ASPNETCORE_URLS env if collision with another service.
#
# What this does:
#   - cd to repo root (so DATA_DIR resolves to <repo-root>/data/)
#   - check dotnet 10+
#   - free port 5050 from any stale instance
#   - print banner with localhost + LAN URL + data dir
#   - launch `dotnet run` with output tee'd to /tmp/ccl-mes-server.log
#
# Data folder:
#   Default          : <repo-root>/data/ccl_mes.db
#   Backup snapshots : <repo-root>/data/Backup/SQLite/
#   Override base    : MES_DATA_DIR=/absolute/path
#   Override file    : MES_DB_PATH=/absolute/file.db (rarely needed)

cd "$(dirname "${BASH_SOURCE[0]}")"

clear

# ── 0. ĐÓNG BĂNG / FROZEN ────────────────────────────────────
# GATE-ANCHOR: legacy-web-force-guard
# Canh bởi CCL-MES-Hybrid/scripts/gate-legacy-web-frozen.sh — khối này PHẢI
# nằm TRƯỚC lệnh `dotnet run`. Gỡ nó = gate đỏ, không phải "dọn dẹp".
if [ "${MES_LEGACY_WEB_FORCE:-}" != "1" ]; then
  echo ""
  echo "  ╔════════════════════════════════════════════════════════════════╗"
  echo "  ║   ⛔  ỨNG DỤNG NÀY ĐÃ NGỪNG PHỤC VỤ  —  2026-08-19             ║"
  echo "  ║       THIS APPLICATION IS RETIRED    —  2026-08-19             ║"
  echo "  ╚════════════════════════════════════════════════════════════════╝"
  echo ""
  echo "  VI  CCL-MES Blazor Server (:5050) đã đóng băng. Không còn ai ở nhà"
  echo "      máy dùng bản này; mọi công việc sản xuất đã chuyển sang app Hybrid."
  echo ""
  echo "      → Dùng thay thế :  CCL-MES Hybrid — API :5100 + app desktop MAUI"
  echo "      → Khởi động API :  cd CCL-MES-Hybrid/src/CCL.MES.Api && dotnet run"
  echo "      → Tài liệu      :  CCL-MES-Hybrid/docs/CUTOVER-LEGACY-WEB-FREEZE-2026-08-19.md"
  echo ""
  echo "  EN  CCL-MES Blazor Server (:5050) is frozen. Nobody on the shop floor"
  echo "      uses it any more; all production work moved to the Hybrid app."
  echo ""
  echo "      → Use instead   :  CCL-MES Hybrid — API :5100 + MAUI desktop app"
  echo "      → Start the API :  cd CCL-MES-Hybrid/src/CCL.MES.Api && dotnet run"
  echo "      → Cutover doc   :  CCL-MES-Hybrid/docs/CUTOVER-LEGACY-WEB-FREEZE-2026-08-19.md"
  echo ""
  echo "  ────────────────────────────────────────────────────────────────"
  echo "  VI  Chạy nhầm? Không hỏng gì cả — chưa khởi động server, chưa mở cổng 5050."
  echo "  EN  Ran this by mistake? Nothing broke — no server started, port 5050 untouched."
  echo ""
  echo "  VI  Thật sự cần chạy lại để khôi phục khẩn cấp? Phải nói rõ ý định:"
  echo "  EN  Genuinely need it back for an emergency rollback? Say so explicitly:"
  echo ""
  echo "        MES_LEGACY_WEB_FORCE=1 bash START_SERVER.command"
  echo ""
  [ -t 0 ] && read -r -p "  Nhấn Enter để thoát / Press Enter to exit..."
  exit 2
fi

echo ""
echo "  ⚠️   MES_LEGACY_WEB_FORCE=1 — chạy app ĐÃ ĐÓNG BĂNG theo yêu cầu tường minh."
echo "  ⚠️   MES_LEGACY_WEB_FORCE=1 — starting the FROZEN app on explicit request."
echo "      VI  Chỉ dùng để khôi phục khẩn cấp. Báo Henry sau khi xong."
echo "      EN  Emergency rollback only. Tell Henry once you are done."

echo ""
echo "  ╔══════════════════════════════════════════════════╗"
echo "  ║      CCL-MES — Standalone Server (.NET 10)       ║"
echo "  ║      SQLite mode (Ops Control v1.2 pattern)      ║"
echo "  ╚══════════════════════════════════════════════════╝"
echo ""

# ── 1. Tool check: dotnet ────────────────────────────────────
if ! command -v dotnet >/dev/null 2>&1; then
  echo "  ❌  .NET 10 chưa được cài đặt."
  echo "      brew install --cask dotnet-sdk"
  echo "      hoặc https://dotnet.microsoft.com/download"
  read -p "  Nhấn Enter để thoát..."
  exit 1
fi

DOTNET_VERSION=$(dotnet --version 2>&1 | head -1)
DOTNET_MAJOR=$(echo "$DOTNET_VERSION" | cut -d. -f1)
echo "  ✓  dotnet: $DOTNET_VERSION"
if [ "$DOTNET_MAJOR" -lt 10 ]; then
  echo "  ⚠️   .NET $DOTNET_VERSION < v10 — khuyến nghị nâng SDK."
fi
echo ""

# ── 2. Data folder preflight ────────────────────────────────
DATA_DIR="${MES_DATA_DIR:-$PWD/data}"
mkdir -p "$DATA_DIR/Backup/SQLite"
if [ -f "$DATA_DIR/ccl_mes.db" ]; then
  DB_SIZE=$(du -h "$DATA_DIR/ccl_mes.db" | cut -f1)
  echo "  ✓  DB:       $DATA_DIR/ccl_mes.db ($DB_SIZE)"
else
  echo "  ✓  DB sẽ tạo mới tại: $DATA_DIR/ccl_mes.db (first boot)"
fi
echo "  ✓  Backup:   $DATA_DIR/Backup/SQLite/"
echo ""

# ── 3. Tìm IP LAN ───────────────────────────────────────────
LOCAL_IP=""
for iface in en0 en1 en2; do
  LOCAL_IP=$(ipconfig getifaddr "$iface" 2>/dev/null)
  [ -n "$LOCAL_IP" ] && break
done
[ -z "$LOCAL_IP" ] && LOCAL_IP="<không phát hiện — kiểm tra Wi-Fi/Ethernet>"

# ── 4. Free port 5050 ───────────────────────────────────────
OLD_PIDS=$(lsof -ti:5050 2>/dev/null)
if [ -n "$OLD_PIDS" ]; then
  PID_LINE=$(echo "$OLD_PIDS" | tr '\n' ' ')
  echo "  ⚠️   Port 5050 đang được giữ bởi PID $PID_LINE — đang TERM..."
  kill $OLD_PIDS 2>/dev/null
  for i in 1 2 3 4 5 6 7 8 9 10; do
    sleep 0.3
    STILL=$(lsof -ti:5050 2>/dev/null)
    [ -z "$STILL" ] && break
    if [ "$i" = "5" ]; then
      echo "  ⚠️   Tiến trình cũ không tự dừng — dùng SIGKILL..."
      kill -9 $STILL 2>/dev/null
    fi
  done
  if [ -n "$(lsof -ti:5050 2>/dev/null)" ]; then
    echo "  ❌  Không thể giải phóng port 5050."
    read -p "  Nhấn Enter để thoát..."
    exit 1
  fi
fi

# ── 5. Banner ────────────────────────────────────────────────
echo ""
echo "  ┌──────────────────────────────────────────────────────┐"
echo "  │                                                      │"
echo "  │          🚀  CCL-MES SERVER starting...              │"
echo "  │                                                      │"
echo "  │   📍  Máy này (Mac):                                 │"
echo "  │       http://localhost:5050                          │"
echo "  │                                                      │"
echo "  │   🌐  Máy khác trên LAN:                             │"
if [[ "$LOCAL_IP" != *"không phát hiện"* ]]; then
  printf "  │       http://%-37s│\n" "$LOCAL_IP:5050"
else
  echo "  │       (không phát hiện được IP LAN)                  │"
fi
echo "  │                                                      │"
printf "  │   📁  Data:   %-39s│\n" "$DATA_DIR"
echo "  │   📝  Log:    /tmp/ccl-mes-server.log                │"
echo "  │                                                      │"
echo "  │   ⏹   Để TẮT: Cmd+C trong Terminal này               │"
echo "  │                                                      │"
echo "  └──────────────────────────────────────────────────────┘"
echo ""
echo "  ▶  Server output (Ctrl+C để tắt):"
echo ""

# ── 6. Launch ────────────────────────────────────────────────
# Bind 0.0.0.0:5050 cho LAN. Program.cs Bước 6.5 sẽ tự resolve
# DATA_DIR từ $MES_DATA_DIR > $PWD/data theo provider Sqlite.
#
# MES_LEGACY_WEB_DRYRUN=1 — kiểm chứng cổng force mà KHÔNG bind 0.0.0.0:5050
# lên LAN nhà máy và KHÔNG chạm data/ccl_mes.db. Dùng bởi gate + người verify.
if [ "${MES_LEGACY_WEB_DRYRUN:-}" = "1" ]; then
  echo "  [DRYRUN] cổng force đã mở — sẽ chạy lệnh sau (nhưng không chạy):"
  echo "  [DRYRUN] force gate opened — would run (but does not):"
  echo "           ASPNETCORE_URLS=\"http://0.0.0.0:5050\" dotnet run --project src/CCL.MES.Web --no-launch-profile"
  exit 0
fi

ASPNETCORE_URLS="http://0.0.0.0:5050" \
  dotnet run --project src/CCL.MES.Web --no-launch-profile 2>&1 \
  | tee /tmp/ccl-mes-server.log

# ── 7. Cleanup ───────────────────────────────────────────────
echo ""
echo "  ✓  Server đã dừng."
echo "  📝  Log đầy đủ tại: /tmp/ccl-mes-server.log"
read -p "  Nhấn Enter để đóng cửa sổ này..."
