#!/usr/bin/env bash
#
# backup-offsite.sh — IBM 3-2-1 rule "1 off-site" copy for CCL-MES.
#
# Companion to the in-process BackupSchedulerService (which handles the
# local 3-copies portion). This script picks the LATEST local backup
# artifacts and rsyncs them to a remote destination — meant to run from
# cron/launchd AFTER the in-process scheduler finishes (~02:30 daily).
#
# Ported from Ops Control v1.3 scripts/backup-offsite.sh, adapted to the
# CCL-MES backup layout:
#   <DATA_DIR>/Backup/SQLite/ccl_mes.db.bak.snapshot-*   (SqliteConnection.BackupDatabase)
#   <DATA_DIR>/Backup/Blobs/blobs_YYYYMMDD.tar.gz        (drawings/CAD tarball)
#
# Configuration — set these env vars in the cron line, a profile, or an
# env file. All paths support shell expansion (~).
#
#   MES_DATA_DIR         /opt/ccl-mes/data                  (required)
#   MES_OFFSITE_TARGET   user@nas:/volume1/ccl-mes-backup   (required)
#                        # rsync target — ssh form (user@host:/path) OR
#                        #   /Volumes/usb/ccl-mes-backup     (local mount)
#   MES_OFFSITE_SSH_KEY  ~/.ssh/ccl_backup_id_ed25519       (optional)
#   MES_OFFSITE_RETAIN   14                                 (optional, days)
#   MES_BACKUP_WEBHOOK   https://hooks.slack.com/...        (optional)
#   MES_OFFSITE_DRY_RUN  1                                  (optional, test only)
#
# Behaviour:
#   1. Pick the newest snapshot (ccl_mes.db.bak.*, excl. -shm/-wal) +
#      newest *.tar.gz under $MES_DATA_DIR/Backup/.
#   2. rsync them to $MES_OFFSITE_TARGET preserving timestamps.
#   3. Verify the off-site snapshot by sha256 checksum match.
#   4. Optionally prune off-site files older than $MES_OFFSITE_RETAIN days
#      (LOCAL-mount targets only — remote pruning over ssh is too risky).
#   5. Webhook alert on failure (best-effort, Slack incoming-webhook JSON).
#
# Why a SEPARATE script vs in-process: a network failure (NAS down, VPN
# dropped, NFS hung) must never block the .NET server event loop, and
# rsync-over-ssh wants a real shell + key agent.
#
# Cron example (nightly at 02:30, after the 02:00 in-process backup):
#   30 2 * * * /opt/ccl-mes/scripts/backup-offsite.sh >> /var/log/ccl-offsite.log 2>&1

set -euo pipefail

# ── Config + defaults ───────────────────────────────────────────────
DATA_DIR="${MES_DATA_DIR:-}"
TARGET="${MES_OFFSITE_TARGET:-}"
SSH_KEY="${MES_OFFSITE_SSH_KEY:-}"
RETAIN_DAYS="${MES_OFFSITE_RETAIN:-14}"
WEBHOOK="${MES_BACKUP_WEBHOOK:-}"
DRY_RUN="${MES_OFFSITE_DRY_RUN:-0}"

if [[ -z "$DATA_DIR" || -z "$TARGET" ]]; then
  echo "ERROR: MES_DATA_DIR + MES_OFFSITE_TARGET must be set" >&2
  echo "  e.g. MES_DATA_DIR=/opt/ccl-mes/data \\" >&2
  echo "       MES_OFFSITE_TARGET=backup@nas.local:/volume1/ccl-mes-backup \\" >&2
  echo "       $0" >&2
  exit 2
fi

BACKUP_DIR="$DATA_DIR/Backup"
if [[ ! -d "$BACKUP_DIR" ]]; then
  echo "ERROR: backup dir not found: $BACKUP_DIR" >&2
  echo "Enable the in-process scheduler first (OPS_BACKUP_SCHEDULE=1 or Settings → Backup)." >&2
  exit 2
fi

START_TS=$(date -u +%Y-%m-%dT%H:%M:%SZ)
HOSTNAME_SHORT=$(hostname -s 2>/dev/null || hostname)
LOG_PREFIX="[offsite ${HOSTNAME_SHORT} ${START_TS}]"

log() { echo "${LOG_PREFIX} $*"; }

# ── Webhook alert helper ────────────────────────────────────────────
notify_webhook() {
  local status="$1"; shift
  local msg="$*"
  if [[ -z "$WEBHOOK" ]]; then return 0; fi
  local payload
  payload=$(printf '{"text":"%s — CCL-MES off-site backup %s\\n%s"}' \
    "$HOSTNAME_SHORT" "$status" "${msg//\"/\\\"}")
  curl -sS -m 5 -X POST -H 'Content-Type: application/json' \
    -d "$payload" "$WEBHOOK" >/dev/null 2>&1 || true
}

trap 'notify_webhook "FAILED" "exit at line $LINENO (last command: $BASH_COMMAND)"' ERR

# ── Pick newest local artifacts ─────────────────────────────────────
SQLITE_DIR="$BACKUP_DIR/SQLite"
BLOBS_DIR="$BACKUP_DIR/Blobs"

LATEST_SQLITE=""
LATEST_BLOBS=""

# newest snapshot, excluding SQLite WAL sidecars (-shm / -wal) which are
# NOT standalone DBs and must never be shipped on their own.
if [[ -d "$SQLITE_DIR" ]]; then
  LATEST_SQLITE=$(find "$SQLITE_DIR" -type f -name "ccl_mes.db.bak.*" \
    ! -name "*-shm" ! -name "*-wal" \
    -exec stat -f "%m %N" {} \; 2>/dev/null \
    | sort -n | tail -n 1 | cut -d' ' -f2- || true)
fi
if [[ -d "$BLOBS_DIR" ]]; then
  LATEST_BLOBS=$(find "$BLOBS_DIR" -type f -name "*.tar.gz" \
    -exec stat -f "%m %N" {} \; 2>/dev/null \
    | sort -n | tail -n 1 | cut -d' ' -f2- || true)
fi

if [[ -z "$LATEST_SQLITE" && -z "$LATEST_BLOBS" ]]; then
  log "ERROR: no local backup artifacts found in $BACKUP_DIR"
  notify_webhook "FAILED" "no local backups under $BACKUP_DIR — scheduler may not be running"
  exit 1
fi

log "latest sqlite: ${LATEST_SQLITE:-<none>}"
log "latest blobs:  ${LATEST_BLOBS:-<none>}"
log "destination:   $TARGET"

# ── rsync transfer ──────────────────────────────────────────────────
RSYNC_OPTS=(-avz --partial --stats)
if [[ "$DRY_RUN" == "1" ]]; then
  RSYNC_OPTS+=(--dry-run)
  log "DRY-RUN mode (set MES_OFFSITE_DRY_RUN=0 to actually transfer)"
fi

# Targets without ":" are local paths (USB drive, NFS mount).
if [[ "$TARGET" == *":"* ]]; then
  if [[ -n "$SSH_KEY" ]]; then
    RSYNC_OPTS+=(-e "ssh -i $SSH_KEY -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10")
  else
    RSYNC_OPTS+=(-e "ssh -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10")
  fi
else
  if [[ ! -d "$TARGET" ]]; then
    log "ERROR: local target dir does not exist: $TARGET"
    log "  Mount the drive first, or use ssh form (user@host:/path)."
    notify_webhook "FAILED" "local target dir missing: $TARGET (drive unmounted?)"
    exit 1
  fi
fi

TRANSFERRED=()
if [[ -n "$LATEST_SQLITE" ]]; then
  log "transferring sqlite snapshot…"
  rsync "${RSYNC_OPTS[@]}" "$LATEST_SQLITE" "$TARGET/"
  TRANSFERRED+=("$(basename "$LATEST_SQLITE")")
fi
if [[ -n "$LATEST_BLOBS" ]]; then
  log "transferring blob tarball…"
  rsync "${RSYNC_OPTS[@]}" "$LATEST_BLOBS" "$TARGET/"
  TRANSFERRED+=("$(basename "$LATEST_BLOBS")")
fi

# ── Verify checksum on destination (sqlite only) ────────────────────
if [[ -n "$LATEST_SQLITE" && "$DRY_RUN" != "1" ]]; then
  LOCAL_SUM=$(shasum -a 256 "$LATEST_SQLITE" | cut -d' ' -f1)
  REMOTE_NAME=$(basename "$LATEST_SQLITE")
  if [[ "$TARGET" == *":"* ]]; then
    REMOTE_HOST="${TARGET%%:*}"
    REMOTE_PATH="${TARGET#*:}"
    SSH_OPTS=()
    if [[ -n "$SSH_KEY" ]]; then SSH_OPTS=(-i "$SSH_KEY"); fi
    REMOTE_SUM=$(ssh "${SSH_OPTS[@]}" -o ConnectTimeout=10 "$REMOTE_HOST" \
      "shasum -a 256 \"$REMOTE_PATH/$REMOTE_NAME\" 2>/dev/null \
        || sha256sum \"$REMOTE_PATH/$REMOTE_NAME\" 2>/dev/null" \
      | cut -d' ' -f1 || true)
  else
    REMOTE_SUM=$(shasum -a 256 "$TARGET/$REMOTE_NAME" 2>/dev/null | cut -d' ' -f1 || true)
  fi
  if [[ "$LOCAL_SUM" == "$REMOTE_SUM" && -n "$REMOTE_SUM" ]]; then
    log "checksum verified: $LOCAL_SUM"
  else
    log "WARN: checksum mismatch or remote unreadable. local=$LOCAL_SUM remote=${REMOTE_SUM:-<unreadable>}"
    notify_webhook "WARN" "off-site sqlite checksum mismatch ($REMOTE_NAME)"
  fi
fi

# ── Optional: prune off-site files older than RETAIN_DAYS ───────────
# LOCAL-mount targets only — remote pruning over ssh is risky (one bad
# path = wipe wrong dir). For remote targets configure NAS-side retention.
if [[ "$TARGET" != *":"* && "$RETAIN_DAYS" -gt 0 && "$DRY_RUN" != "1" ]]; then
  PRUNED=$(find "$TARGET" \( -name "ccl_mes.db.bak.*" -o -name "*.tar.gz" \) \
    -type f -mtime "+$RETAIN_DAYS" -print -delete 2>/dev/null | wc -l | tr -d ' ')
  if [[ "$PRUNED" -gt 0 ]]; then
    log "pruned $PRUNED local-target file(s) older than ${RETAIN_DAYS}d"
  fi
fi

DURATION=$(($(date +%s) - $(date -ju -f "%Y-%m-%dT%H:%M:%SZ" "$START_TS" +%s 2>/dev/null || date -d "$START_TS" +%s)))
log "OK — transferred ${#TRANSFERRED[@]} file(s) in ${DURATION}s: ${TRANSFERRED[*]:-<none>}"
notify_webhook "OK" "$(printf 'transferred %d file(s) in %ss: %s' "${#TRANSFERRED[@]}" "$DURATION" "${TRANSFERRED[*]:-none}")"
exit 0
