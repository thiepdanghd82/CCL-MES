#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Backup hằng ngày qua launchd StartCalendarInterval.
#
# VÌ SAO CẦN CÁI NÀY khi đã có scheduler trong API (sự cố 2026-09-08):
#   Scheduler của API dùng `Task.Delay` trong tiến trình. Máy chạy hệ này là
#   MacBook và nó NGỦ qua đêm. Đo được sáng 08/09:
#     snapshot 07/09 : 6 file      snapshot 08/09 : 0 file
#     pmset          : máy ngủ xuyên 02:00, chạy pin
#     DelayUntilNextRun: mốc đã qua thì AddDays(1) — KHÔNG có logic chạy bù
#   Cửa sổ 02:00 trôi mất im lặng. `gate-backup-fresh` vẫn PASS vì nó chỉ đo
#   tuổi < 48h, nên máy ngủ mỗi đêm thì phải hai đêm gate mới kêu.
#
#   launchd `StartCalendarInterval` là cơ chế ĐÚNG của macOS: hệ điều hành TỰ
#   chạy job bị lỡ ngay khi máy thức dậy. Timer trong tiến trình không làm được.
#
# Vì sao KHÔNG gọi API mà tự chụp:
#   Backup phải chạy được cả khi API đang chết — đó chính là lúc cần nó nhất.
#   `sqlite3 .backup` dùng online-backup API, an toàn khi có người đang ghi WAL.
#
# Không trùng với scheduler của API: script BỎ QUA nếu hôm nay đã có snapshot.
#
# Dùng:
#   bash backup-daily.sh            # chạy (bỏ qua nếu hôm nay đã có)
#   bash backup-daily.sh --force    # chụp bằng mọi giá
#   bash backup-daily.sh install    # cài launchd 02:00 hằng ngày
#   bash backup-daily.sh uninstall
#   bash backup-daily.sh status
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

LABEL="com.ccl.mes.backup"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
here="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$here/../.." && pwd)"
DATA="${MES_DATA_DIR:-$REPO/data}"
DB="${MES_DB_PATH:-$DATA/ccl_mes.db}"
DEST="$DATA/Backup/SQLite"
LOG="${MES_BACKUP_LOG:-$DATA/Backup/backup-daily.log}"
CFG="$DATA/Library/SystemConfig/backup-schedule.json"

say() { printf '%s\n' "$*"; }
logline() { printf '%s  %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*" >> "$LOG"; }

# Giữ/hạn theo đúng cấu hình người dùng đã đặt trong UI, không tự chế số khác.
read_cfg() { # $1=khoá $2=mặc định
  python3 - "$CFG" "$1" "$2" <<'PY' 2>/dev/null || echo "$3"
import json,sys
try: print(json.load(open(sys.argv[1])).get(sys.argv[2], sys.argv[3]))
except Exception: print(sys.argv[3])
PY
}

cmd_run() {
  local force="${1:-}"
  mkdir -p "$DEST" "$(dirname "$LOG")"
  [ -f "$DB" ] || { logline "LỖI: không thấy DB $DB"; say "✗ không thấy DB $DB"; return 1; }

  local today; today="$(date '+%Y%m%d')"
  if [ "$force" != "--force" ] && ls "$DEST" 2>/dev/null | grep -q "bak\.snapshot-$today"; then
    logline "bỏ qua — hôm nay ($today) đã có snapshot"
    say "[i] hôm nay đã có snapshot, bỏ qua (dùng --force để ép)"
    return 0
  fi

  local ts out
  ts="$(date '+%Y%m%d-%H%M%S')"
  out="$DEST/$(basename "$DB").bak.snapshot-$ts"

  # .backup = online backup API: an toàn khi API đang ghi (WAL).
  if ! sqlite3 "$DB" ".backup '$out'" 2>>"$LOG"; then
    logline "LỖI: sqlite3 .backup thất bại"
    say "✗ chụp thất bại — xem $LOG"; return 1
  fi

  # Backup CHƯA kiểm chứng thì chưa phải backup.
  local chk; chk="$(sqlite3 "$out" 'PRAGMA integrity_check;' 2>>"$LOG" | head -1)"
  if [ "$chk" != "ok" ]; then
    logline "LỖI: integrity_check='$chk' — xoá bản hỏng $out"
    rm -f "$out"; say "✗ bản chụp HỎNG (integrity_check=$chk), đã xoá"; return 1
  fi
  local rows; rows="$(sqlite3 "$out" 'SELECT COUNT(*) FROM WorkOrders;' 2>/dev/null)"
  local size; size="$(du -h "$out" | cut -f1)"
  logline "OK $(basename "$out")  $size  WorkOrders=$rows  integrity=ok"
  say "[✓] $(basename "$out")  ($size, WorkOrders=$rows, integrity ok)"

  cmd_prune
}

cmd_prune() {
  local days keep
  days="$(read_cfg RetentionDays 30)"; keep="$(read_cfg MinKeep 10)"
  python3 - "$DEST" "$days" "$keep" "$LOG" <<'PY'
import os,sys,time
dest,days,keep,log=sys.argv[1],int(sys.argv[2]),int(sys.argv[3]),sys.argv[4]
# Chỉ đụng snapshot tự sinh. Bản chụp TAY trước migration (before-*) là bằng
# chứng của quy trình Phase A→C — không bao giờ xoá tự động.
c=[(os.path.getmtime(os.path.join(dest,f)), f) for f in os.listdir(dest)
   if ".bak.snapshot-" in f and not f.endswith(("-wal","-shm"))]
c.sort(reverse=True)
cut=time.time()-days*86400
gone=[f for m,f in c[keep:] if m<cut]
for f in gone:
    for suf in ("","-wal","-shm"):
        p=os.path.join(dest,f+suf)
        if os.path.exists(p): os.remove(p)
if gone:
    with open(log,"a") as fh:
        fh.write(f"{time.strftime('%Y-%m-%d %H:%M:%S')}  dọn {len(gone)} snapshot quá {days} ngày (giữ tối thiểu {keep})\n")
    print(f"[i] đã dọn {len(gone)} snapshot cũ")
PY
}

write_plist() {
  mkdir -p "$(dirname "$PLIST")"
  local hour; hour="$(read_cfg Hour 2)"
  cat > "$PLIST" <<PLIST_EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>$LABEL</string>
  <key>ProgramArguments</key>
  <array>
    <string>/bin/bash</string>
    <string>$here/backup-daily.sh</string>
  </array>
  <key>WorkingDirectory</key><string>$REPO</string>
  <!-- launchd TỰ chạy job bị lỡ khi máy thức dậy — đây là điểm khác then chốt
       so với Task.Delay trong tiến trình, vốn im lặng bỏ qua cửa sổ đã trôi. -->
  <key>StartCalendarInterval</key>
  <dict><key>Hour</key><integer>$hour</integer><key>Minute</key><integer>0</integer></dict>
  <key>StandardOutPath</key><string>$LOG</string>
  <key>StandardErrorPath</key><string>$LOG</string>
</dict>
</plist>
PLIST_EOF
  say "[i] lịch: $(printf '%02d' "$hour"):00 hằng ngày (đọc từ backup-schedule.json)"
}

cmd_install() {
  # ĐÃ THỬ VÀ KHÔNG DÙNG ĐƯỢC — đừng đi lại đường này (đo 2026-09-08).
  # macOS TCC chặn job launchd đọc ~/Documents, mà cả repo nằm trong đó:
  #   $ launchctl kickstart gui/$UID/com.ccl.mes.backup
  #   → last exit code = 126
  #   → /bin/bash: …/backup-daily.sh: Operation not permitted
  #   → getcwd: cannot access parent directories: Operation not permitted
  # Job API chạy được vì nó là BINARY biên dịch (định danh TCC riêng); còn
  # script chạy qua /bin/bash thì /bin/bash không có quyền đó.
  # Nên lịch backup nằm trong tiến trình API (đã có logic chạy bù khi lỡ cửa
  # sổ do máy ngủ). Script này giữ lại cho việc chụp TAY — nó chạy được cả khi
  # API đã chết, tức đúng lúc cần nhất.
  say "✗ KHÔNG cài được job launchd cho script này."
  say "  macOS TCC chặn launchd đọc ~/Documents (đã thử: exit 126,"
  say "  'Operation not permitted'). Lịch backup nằm trong tiến trình API."
  say ""
  say "  Chụp tay:            bash $(basename "$0")"
  say "  Xem lịch trong API:  bash api-service.sh status"
  say "  Muốn dùng launchd:   phải chuyển repo ra khỏi ~/Documents,"
  say "                       hoặc cấp Full Disk Access cho /bin/bash (KHÔNG nên)."
  return 1
  # shellcheck disable=SC2317
  write_plist
  launchctl bootout  "gui/$UID/$LABEL" 2>/dev/null
  launchctl bootstrap "gui/$UID" "$PLIST" 2>/dev/null || launchctl load "$PLIST" 2>/dev/null
  say "[✓] đã cài $PLIST"
  cmd_status
}
cmd_uninstall() {
  launchctl bootout "gui/$UID/$LABEL" 2>/dev/null || launchctl unload "$PLIST" 2>/dev/null
  rm -f "$PLIST"; say "[✓] đã gỡ job backup hằng ngày."
}
cmd_status() {
  say "job launchd : $([ -f "$PLIST" ] && echo 'đã cài' || echo 'chưa cài')"
  say "nạp vào hệ  : $(launchctl print "gui/$UID/$LABEL" >/dev/null 2>&1 && echo 'có' || echo 'không')"
  say "snapshot mới: $(ls -t "$DEST" 2>/dev/null | grep 'bak\.snapshot-' | grep -v -- '-wal\|-shm' | head -1 || echo '(chưa có)')"
  say "log         : $LOG"
}

case "${1:-run}" in
  run|"")     cmd_run "${2:-}" ;;
  --force)    cmd_run --force  ;;
  install)    cmd_install      ;;
  uninstall)  cmd_uninstall    ;;
  status)     cmd_status       ;;
  prune)      cmd_prune        ;;
  *) say "dùng: $(basename "$0") {run|--force|install|uninstall|status|prune}"; exit 2 ;;
esac
