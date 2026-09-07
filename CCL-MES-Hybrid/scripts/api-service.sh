#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Quản lý API CCL-MES như một DỊCH VỤ, thay cho `nohup … &` bằng tay.
#
# VÌ SAO SCRIPT NÀY TỒN TẠI (sự cố 2026-09-07):
#   API chết lúc boot vì cửa chặn khoá JWT, và KHÔNG AI BIẾT cho tới khi
#   người dùng mở app thấy "login fail". Hai nguyên nhân cộng lại:
#
#   1. Lệnh vận hành trong BAN-GIAO-2026-08-19.md KHÔNG đặt
#      ASPNETCORE_ENVIRONMENT. .NET mặc định "Production", nên nó tìm
#      appsettings.Production.local.json — trong khi khoá thật nằm ở
#      appsettings.Development.local.json. Chạy bằng `dotnet run` thì
#      launchSettings.json đặt Development nên lại chạy được. Cùng một
#      binary, hai kết quả, tuỳ ai gõ lệnh gì.
#   2. Không có gì trông chừng. Chết là nằm im.
#
#   Script này đóng cả hai: đặt môi trường TƯỜNG MINH, và cài launchd
#   KeepAlive để tiến trình tự dựng lại khi chết.
#
# Dùng:
#   bash api-service.sh install     # cài + chạy (tự khởi động khi đăng nhập)
#   bash api-service.sh status      # tiến trình · cổng · /health
#   bash api-service.sh restart
#   bash api-service.sh log         # theo dõi log
#   bash api-service.sh uninstall   # gỡ hẳn, trả về chạy tay
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

LABEL="com.ccl.mes.api"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
here="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$here/.." && pwd)"          # CCL-MES-Hybrid
REPO="$(cd "$ROOT/.." && pwd)"          # gốc repo
APIDIR="$ROOT/src/CCL.MES.Api"
DB="$REPO/data/ccl_mes.db"
URL="http://localhost:5100"
LOG="/tmp/ccl-api-5100.log"

# Release ưu tiên; chưa có thì dùng Debug và nói rõ.
BIN_REL="$APIDIR/bin/Release/net10.0/CCL.MES.Api"
BIN_DBG="$APIDIR/bin/Debug/net10.0/CCL.MES.Api"
if   [ -x "$BIN_REL" ]; then BIN="$BIN_REL"; FLAVOUR="Release"
elif [ -x "$BIN_DBG" ]; then BIN="$BIN_DBG"; FLAVOUR="Debug"
else BIN=""; FLAVOUR="(chưa build)"; fi

# Môi trường đặt TƯỜNG MINH — đây chính là thứ thiếu gây sự cố.
ENVNAME="${ASPNETCORE_ENVIRONMENT:-Production}"
KEYFILE="$APIDIR/appsettings.$ENVNAME.local.json"

say() { printf '%s\n' "$*"; }

preflight() {
  local bad=0
  say "[ctx] repo   = $REPO"
  say "[ctx] binary = ${BIN:-KHÔNG THẤY}  ($FLAVOUR)"
  say "[ctx] env    = $ENVNAME"
  say "[ctx] DB     = $DB"
  [ -n "$BIN" ] || { say "  ✗ chưa build API. Chạy: dotnet build $APIDIR"; bad=1; }
  [ -f "$DB" ]  || { say "  ✗ không thấy DB tại $DB"; bad=1; }

  # L22 — binary CŨ HƠN mã nguồn là bẫy im lặng: nó chạy, trả 200, và phục
  # vụ code của hôm qua. Sự cố 2026-09-07 suýt lặp lại đúng kiểu đó: bản
  # Release build lúc 08:10 không chứa cửa chặn khoá JWT vừa thêm lúc 21:57,
  # nhưng vẫn lên xanh nên không ai nghi ngờ.
  if [ -n "$BIN" ]; then
    local newest
    newest="$(find "$APIDIR" "$REPO/src/CCL.MES.Application" "$REPO/src/CCL.MES.Domain" \
                -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' \
                -newer "$BIN" -print -quit 2>/dev/null)"
    if [ -n "$newest" ]; then
      say "  ✗ binary $FLAVOUR CŨ HƠN mã nguồn."
      say "    mới hơn nó: ${newest#$REPO/}"
      say "    Build lại:  dotnet build $APIDIR -c $FLAVOUR"
      bad=1
    fi
  fi
  # Cửa chặn khoá JWT sẽ giết tiến trình lúc boot nếu thiếu file này.
  if [ ! -f "$KEYFILE" ] && [ -z "${Jwt__SigningKey:-}" ]; then
    say "  ✗ thiếu khoá ký cho môi trường '$ENVNAME'."
    say "    Tạo $KEYFILE với {\"Jwt\":{\"SigningKey\":\"<ngẫu nhiên ≥32 byte>\"}}"
    say "    hoặc đặt biến môi trường Jwt__SigningKey."
    say "    (file của môi trường KHÁC sẽ không được nạp — đúng lỗi 2026-09-07)"
    bad=1
  fi
  return $bad
}

write_plist() {
  mkdir -p "$(dirname "$PLIST")"
  cat > "$PLIST" <<PLIST_EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>$LABEL</string>
  <key>ProgramArguments</key>
  <array><string>$BIN</string></array>
  <key>WorkingDirectory</key><string>$APIDIR</string>
  <key>EnvironmentVariables</key>
  <dict>
    <key>ASPNETCORE_ENVIRONMENT</key><string>$ENVNAME</string>
    <key>ASPNETCORE_URLS</key><string>$URL</string>
    <key>MES_DB_PATH</key><string>$DB</string>
  </dict>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
  <!-- 30s giữa hai lần thử: nếu boot hỏng thật thì log không bị ngập,
       nhưng vẫn đủ nhanh để một lần crash lẻ tự lành trước khi ai kịp thấy. -->
  <key>ThrottleInterval</key><integer>30</integer>
  <key>StandardOutPath</key><string>$LOG</string>
  <key>StandardErrorPath</key><string>$LOG</string>
</dict>
</plist>
PLIST_EOF
}

cmd_install() {
  preflight || { say ""; say "DỪNG — sửa các dòng ✗ ở trên rồi chạy lại."; return 1; }
  # Hạ tiến trình chạy tay để không tranh cổng 5100.
  pkill -f "net10.0/CCL.MES.Api" 2>/dev/null && say "[i] đã dừng tiến trình chạy tay"
  sleep 1
  write_plist
  launchctl bootout  "gui/$UID/$LABEL" 2>/dev/null
  launchctl bootstrap "gui/$UID" "$PLIST" 2>/dev/null || launchctl load "$PLIST" 2>/dev/null
  say "[✓] đã cài $PLIST"
  sleep 6; cmd_status
}

cmd_uninstall() {
  launchctl bootout "gui/$UID/$LABEL" 2>/dev/null || launchctl unload "$PLIST" 2>/dev/null
  rm -f "$PLIST"
  say "[✓] đã gỡ dịch vụ. API giờ chỉ chạy khi khởi động bằng tay."
}

cmd_restart() { launchctl kickstart -k "gui/$UID/$LABEL" 2>/dev/null && say "[✓] đã khởi động lại"; sleep 5; cmd_status; }
cmd_stop()    { launchctl bootout "gui/$UID/$LABEL" 2>/dev/null; say "[i] đã dừng (cài lại bằng: install)"; }
cmd_log()     { tail -f "$LOG"; }

cmd_status() {
  local pid port code
  pid="$(pgrep -f 'net10.0/CCL.MES.Api' | head -1)"
  port="$(lsof -nP -iTCP:5100 -sTCP:LISTEN 2>/dev/null | tail -1)"
  code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$URL/api/v2/health" 2>/dev/null)"
  say "tiến trình : ${pid:-KHÔNG CHẠY}"
  say "cổng 5100  : ${port:+đang lắng nghe}${port:-trống}"
  say "/health    : HTTP ${code:-000}"
  say "dịch vụ    : $([ -f "$PLIST" ] && echo 'đã cài (tự dựng lại khi chết)' || echo 'chưa cài — chạy tay')"
  if [ "${code:-000}" != "200" ]; then
    say ""
    say "─── 15 dòng log cuối (bỏ EF JSON) ───"
    grep -v '^{"EventId"' "$LOG" 2>/dev/null | tail -15
    return 1
  fi
}

case "${1:-status}" in
  install)   cmd_install   ;;
  uninstall) cmd_uninstall ;;
  restart)   cmd_restart   ;;
  stop)      cmd_stop      ;;
  status)    cmd_status    ;;
  log)       cmd_log       ;;
  *) say "dùng: $(basename "$0") {install|uninstall|restart|stop|status|log}"; exit 2 ;;
esac
