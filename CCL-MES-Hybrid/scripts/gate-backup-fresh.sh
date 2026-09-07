#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Gate — lịch backup đang bật thì bản snapshot mới nhất không được cũ hơn 48h.
#
# VÌ SAO: kiểm định 2026-09-07 — OPS_BACKUP_SCHEDULE / UI đã có nhưng scheduler
# từng im lặng tắt; mọi bản sao thủ công nằm cùng đĩa với live. Một sự cố đĩa
# xoá sạch DB + bản sao. Gate này không thay off-site (cron backup-offsite.sh),
# chỉ bắt "lịch bật mà không ai chạy / không có file mới".
#
# Nguồn lịch: <DATA_DIR>/Library/SystemConfig/backup-schedule.json (UI thắng
# env). Snapshot: <DATA_DIR>/Backup/SQLite/* (bỏ -wal/-shm).
#
# Usage:
#   bash scripts/gate-backup-fresh.sh
#   MES_DATA_DIR=/abs/data bash scripts/gate-backup-fresh.sh
#   bash scripts/gate-backup-fresh.sh --self-test
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
MAX_AGE_HOURS="${MES_BACKUP_MAX_AGE_HOURS:-48}"

resolve_data_dir() {
  if [ -n "${MES_DATA_DIR:-}" ]; then
    echo "$MES_DATA_DIR"
    return
  fi
  if [ -n "${MES_DB_PATH:-}" ]; then
    dirname "$MES_DB_PATH"
    return
  fi
  echo "$root/data"
}

schedule_enabled() {
  local f="$1"
  [ -f "$f" ] || return 1
  python3 - "$f" <<'PY'
import json, sys
try:
    cfg = json.load(open(sys.argv[1]))
except Exception:
    sys.exit(1)
sys.exit(0 if cfg.get("Enabled") is True else 1)
PY
}

newest_snapshot_epoch() {
  local dir="$1"
  python3 - "$dir" <<'PY'
import os, sys, glob
d = sys.argv[1]
cands = []
for p in glob.glob(os.path.join(d, "*")):
    base = os.path.basename(p)
    if base.endswith("-wal") or base.endswith("-shm"):
        continue
    if not os.path.isfile(p):
        continue
    # Chỉ nhận file snapshot/backup thật — bỏ .gitkeep / readme.
    if base.startswith(".") or base.endswith(".md") or base == ".gitkeep":
        continue
    cands.append(os.path.getmtime(p))
print(int(max(cands)) if cands else 0)
PY
}

run_gate() {
  local data_dir schedule_file backup_dir now age_h newest
  data_dir="$(resolve_data_dir)"
  schedule_file="$data_dir/Library/SystemConfig/backup-schedule.json"
  backup_dir="$data_dir/Backup/SQLite"

  echo "[gate:backup-fresh] DATA_DIR=$data_dir  max_age=${MAX_AGE_HOURS}h"

  if ! schedule_enabled "$schedule_file"; then
    echo "[gate:backup-fresh] PASS — lịch tắt (hoặc chưa có backup-schedule.json)."
    echo "  Bật: Settings → Backup, hoặc OPS_BACKUP_SCHEDULE=1."
    echo "  Off-site vẫn cần cron scripts/backup-offsite.sh (xem docs/BACKUP.md)."
    return 0
  fi

  echo "[gate:backup-fresh] lịch BẬT — kiểm tuổi snapshot…"
  if [ ! -d "$backup_dir" ]; then
    echo "[gate:backup-fresh:FAIL] không thấy thư mục $backup_dir"
    return 1
  fi

  newest="$(newest_snapshot_epoch "$backup_dir")"
  if [ "$newest" -eq 0 ]; then
    echo "[gate:backup-fresh:FAIL] lịch bật nhưng không có file snapshot trong Backup/SQLite/."
    echo "  Chạy Settings → Backup → Run backup now, hoặc POST /api/v2/backup/run-now."
    return 1
  fi

  now="$(date +%s)"
  age_h=$(( (now - newest) / 3600 ))
  echo "[gate:backup-fresh] snapshot mới nhất tuổi ≈ ${age_h}h (mtime $(date -r "$newest" '+%Y-%m-%d %H:%M:%S' 2>/dev/null || date -d "@$newest" '+%Y-%m-%d %H:%M:%S' 2>/dev/null || echo "$newest"))"

  if [ "$age_h" -gt "$MAX_AGE_HOURS" ]; then
    echo "[gate:backup-fresh:FAIL] snapshot cũ hơn ${MAX_AGE_HOURS}h (thực tế ~${age_h}h)."
    echo "  Chạy backup ngay + kiểm cron off-site."
    return 1
  fi

  echo "[gate:backup-fresh] PASS — snapshot trong hạn ${MAX_AGE_HOURS}h."
  return 0
}

self_test() {
  local tmp
  tmp="$(mktemp -d)"
  mkdir -p "$tmp/Library/SystemConfig" "$tmp/Backup/SQLite"
  echo '{"Enabled":false}' >"$tmp/Library/SystemConfig/backup-schedule.json"
  if ! MES_DATA_DIR="$tmp" bash "$0" >/dev/null; then
    echo "[gate:backup-fresh] self-test FAIL — lịch tắt phải PASS"; rm -rf "$tmp"; return 1
  fi
  echo '{"Enabled":true}' >"$tmp/Library/SystemConfig/backup-schedule.json"
  if MES_DATA_DIR="$tmp" bash "$0" >/dev/null 2>&1; then
    echo "[gate:backup-fresh] self-test FAIL — lịch bật + 0 file phải FAIL"; rm -rf "$tmp"; return 1
  fi
  touch "$tmp/Backup/SQLite/ccl_mes.db.bak.snapshot-selftest"
  if ! MES_DATA_DIR="$tmp" bash "$0" >/dev/null; then
    echo "[gate:backup-fresh] self-test FAIL — file mới phải PASS"; rm -rf "$tmp"; return 1
  fi
  # Giả lập cũ (~100h): dùng epoch nếu touch -t hỗ trợ.
  if touch -t "$(date -v-100H '+%Y%m%d%H%M.%S' 2>/dev/null || true)" \
      "$tmp/Backup/SQLite/ccl_mes.db.bak.snapshot-selftest" 2>/dev/null; then
    if MES_DATA_DIR="$tmp" bash "$0" >/dev/null 2>&1; then
      echo "[gate:backup-fresh] self-test FAIL — file cũ phải FAIL"; rm -rf "$tmp"; return 1
    fi
  fi
  rm -rf "$tmp"
  echo "[gate:backup-fresh] self-test PASS"
  return 0
}

case "${1:-}" in
  --self-test) self_test ;;
  *) run_gate ;;
esac
