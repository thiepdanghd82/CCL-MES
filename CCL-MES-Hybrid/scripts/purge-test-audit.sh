#!/usr/bin/env bash
# P10.7b-1 — housekeeping: purge test-only audit rows from the live DB.
#
# Scope:
#   * Action='TEST_RESET' AND ActorUsername='test-tool' — every row
#     emitted by scripts/reset-test-wo.sh during dev/CI runs.
#   * Action='SYS_RECOVERY' AND Detail LIKE one of the test-only patterns
#     ('%reproduce%' / '%checkpoint%' / '%manual checkpoint%' /
#      '%operator A left mid-shift%' / '%wire-visibility%' / '%soak%').
#     These match the 7a-2 hotfix repro + the bUnit / checkpoint test
#     vocabulary; real ops SYS_RECOVERY rows use operator-typed Vietnamese
#     reasons (REC-OP-WEDGE / REC-HW-FAULT / etc.) and pass through.
#
# Default: dry-run — SELECT + print counts + list candidate rows. NO DELETE.
# --commit: BEGIN; DELETE both patterns; COMMIT; + print post-count.
#
# Operator workflow (Henry-confirmed):
#   1. bash scripts/purge-test-audit.sh                  # review preview
#   2. bash scripts/purge-test-audit.sh --commit          # execute purge
#
# Agent MUST NOT run --commit; that decision is the operator's after
# eyeballing the dry-run output.
#
# Rule 7.1 — [ctx] DB= + DB sha8 printed at startup.
#
# Usage:
#   bash CCL-MES-Hybrid/scripts/purge-test-audit.sh [--commit] [--db /abs/path]

set -u
set +e

DB_PATH=""
COMMIT=0
for arg in "$@"; do
    case "$arg" in
        --commit) COMMIT=1 ;;
        --db=*)   DB_PATH="${arg#--db=}" ;;
        --db)     shift ;;
        --help|-h)
            echo "usage: bash scripts/purge-test-audit.sh [--commit] [--db /abs/path]"
            echo "  Default (no flag): dry-run — preview rows + counts, NO DELETE."
            echo "  --commit          : execute the purge transaction."
            echo "  --db <path>       : target a non-default SQLite file (default = data/ccl_mes.db)."
            exit 0
            ;;
        --*) echo "unknown flag: $arg"; exit 64 ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HYBRID_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$HYBRID_ROOT/.." && pwd)"

if [[ -z "$DB_PATH" ]]; then
    DB_PATH="$REPO_ROOT/data/ccl_mes.db"
fi

if [[ ! -f "$DB_PATH" ]]; then
    echo "❌ DB not found: $DB_PATH"
    exit 1
fi

DB_ABS="$(cd "$(dirname "$DB_PATH")" && pwd)/$(basename "$DB_PATH")"
DB_SHA8="$(shasum -a 256 "$DB_PATH" 2>/dev/null | awk '{print substr($1,1,8)}')"

echo "===================================================================="
echo "purge-test-audit — $(date '+%Y-%m-%d %H:%M:%S')"
echo "===================================================================="
echo "[ctx] DB         = $DB_ABS"
echo "[ctx] DB sha8    = $DB_SHA8"
echo "[ctx] mode       = $([ $COMMIT -eq 1 ] && echo 'COMMIT (will DELETE)' || echo 'DRY-RUN (no writes)')"
echo "===================================================================="
echo ""

PATTERN_NOISE_LIKE="(\"Detail\" LIKE '%reproduce%' \
  OR \"Detail\" LIKE '%checkpoint%' \
  OR \"Detail\" LIKE '%manual checkpoint%' \
  OR \"Detail\" LIKE '%operator A left mid-shift%' \
  OR \"Detail\" LIKE '%wire-visibility%' \
  OR \"Detail\" LIKE '%soak%')"

TOTAL_BEFORE=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM AuditLogs;")
TESTRESET_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM AuditLogs WHERE Action='TEST_RESET' AND ActorUsername='test-tool';")
NOISE_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM AuditLogs WHERE Action='SYS_RECOVERY' AND $PATTERN_NOISE_LIKE;")
PURGE_TOTAL=$((TESTRESET_COUNT + NOISE_COUNT))

echo "── Pre-purge audit counts ──"
echo "  TOTAL AuditLogs                              : $TOTAL_BEFORE"
echo "  TEST_RESET (actor=test-tool) candidates      : $TESTRESET_COUNT"
echo "  SYS_RECOVERY noise (Detail LIKE patterns)    : $NOISE_COUNT"
echo "  TOTAL TO PURGE                               : $PURGE_TOTAL"
echo ""

if [[ "$PURGE_TOTAL" == "0" ]]; then
    echo "✓ Nothing to purge — audit log is clean."
    exit 0
fi

echo "── Candidate rows ──"
echo ""
echo "TEST_RESET rows:"
sqlite3 -column -header "$DB_PATH" \
  "SELECT Id, datetime(Timestamp) AS T, Action, TargetId, substr(Detail,1,60) AS Detail60
   FROM AuditLogs WHERE Action='TEST_RESET' AND ActorUsername='test-tool'
   ORDER BY Id;"
echo ""
echo "SYS_RECOVERY noise rows:"
sqlite3 -column -header "$DB_PATH" \
  "SELECT Id, datetime(Timestamp) AS T, Action, TargetId, substr(Detail,1,80) AS Detail80
   FROM AuditLogs WHERE Action='SYS_RECOVERY' AND $PATTERN_NOISE_LIKE
   ORDER BY Id;"
echo ""

if [[ $COMMIT -eq 0 ]]; then
    echo "── DRY-RUN — no rows written. Re-run with --commit to execute. ──"
    exit 0
fi

# COMMIT path
echo "── Executing purge transaction ──"
sqlite3 "$DB_PATH" <<SQL
BEGIN;
DELETE FROM AuditLogs WHERE Action='TEST_RESET' AND ActorUsername='test-tool';
DELETE FROM AuditLogs WHERE Action='SYS_RECOVERY' AND $PATTERN_NOISE_LIKE;
COMMIT;
SQL
PURGE_EXIT=$?

if [[ $PURGE_EXIT -ne 0 ]]; then
    echo "❌ Purge transaction FAILED (sqlite3 exit=$PURGE_EXIT)."
    exit 1
fi

TOTAL_AFTER=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM AuditLogs;")
DELETED=$((TOTAL_BEFORE - TOTAL_AFTER))
echo ""
echo "── Post-purge audit counts ──"
echo "  TOTAL AuditLogs (before)  : $TOTAL_BEFORE"
echo "  TOTAL AuditLogs (after)   : $TOTAL_AFTER"
echo "  Rows deleted              : $DELETED"
echo ""
if [[ "$DELETED" == "$PURGE_TOTAL" ]]; then
    echo "✓ Purge complete — deleted count matches preview."
else
    echo "⚠ Deleted count ($DELETED) differs from preview ($PURGE_TOTAL) — investigate."
    exit 1
fi
