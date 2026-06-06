#!/usr/bin/env bash
# P10.7b-3 — reset PREPRESS row checks for one WO so the operator can
# re-verify the NG-path picker flow without recreating fixtures.
#
# Scope (per WO):
#   * UPDATE WoMaterials   SET Status='Pending', NgReasonCode=NULL,
#                              NgNote=NULL, CheckedBy=NULL, CheckedAt=NULL,
#                              QtyLoaded=NULL, LotNo=NULL
#   * UPDATE WoPlateChecks SET Status='Pending', NgReasonCode=NULL,
#                              NgNote=NULL, CheckedBy=NULL, CheckedAt=NULL,
#                              PlateNo=NULL
#   * UPDATE WoCutterChecks SET Status='Pending', NgReasonCode=NULL,
#                              NgNote=NULL, CheckedBy=NULL, CheckedAt=NULL,
#                              CutterNo=NULL
#   * UPDATE WorkOrders SET MaterialsReady=0, MesPhase='PREPRESS',
#                           UpdatedAt=now(), UpdatedBy='reset-tool'
#     (resets the rollup + nudges RowVersion via the SQLite UPDATE trigger
#     so the dashboard fetches a fresh ETag on next GET /prepress.)
#
# Default: dry-run — preview affected rows + counts. NO writes.
# --commit: execute the UPDATE transaction.
#
# Operator workflow:
#   1. bash scripts/reset-prepress-for-wo.sh --wo WO-26-3684           # preview
#   2. bash scripts/reset-prepress-for-wo.sh --wo WO-26-3684 --commit  # execute
#
# Rules:
#   R7.1 — [ctx] DB= + DB sha8 printed at startup.
#   Agent MUST NOT run --commit on Henry's behalf — that decision is
#   the operator's after eyeballing the dry-run.
#
# Usage:
#   bash CCL-MES-Hybrid/scripts/reset-prepress-for-wo.sh \
#        --wo <wo-no> [--commit] [--db /abs/path]

set -u
set +e

DB_PATH=""
WO_NO=""
COMMIT=0
while [[ $# -gt 0 ]]; do
    case "$1" in
        --commit) COMMIT=1; shift ;;
        --wo)     WO_NO="$2"; shift 2 ;;
        --wo=*)   WO_NO="${1#--wo=}"; shift ;;
        --db)     DB_PATH="$2"; shift 2 ;;
        --db=*)   DB_PATH="${1#--db=}"; shift ;;
        --help|-h)
            cat <<EOF
usage: bash scripts/reset-prepress-for-wo.sh --wo <wo-no> [--commit] [--db /abs/path]

  --wo <wo-no>   Target WO number (required), e.g. WO-26-3684.
  --commit       Execute the UPDATE transaction. Without this flag the
                 script previews counts only — no writes.
  --db <path>    Target a non-default SQLite file (default = data/ccl_mes.db).

Preview mode is safe to run any number of times. --commit MUST be
operator-driven (Rule 7 — agent never runs it).
EOF
            exit 0
            ;;
        *) echo "unknown arg: $1"; exit 64 ;;
    esac
done

if [[ -z "$WO_NO" ]]; then
    echo "❌ --wo <wo-no> is required."
    echo "    usage: bash scripts/reset-prepress-for-wo.sh --wo WO-26-3684 [--commit]"
    exit 64
fi

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
echo "reset-prepress-for-wo — $(date '+%Y-%m-%d %H:%M:%S')"
echo "===================================================================="
echo "[ctx] DB         = $DB_ABS"
echo "[ctx] DB sha8    = $DB_SHA8"
echo "[ctx] WO         = $WO_NO"
echo "[ctx] mode       = $([ $COMMIT -eq 1 ] && echo 'COMMIT (will UPDATE)' || echo 'DRY-RUN (no writes)')"
echo "===================================================================="
echo ""

# Resolve WO id
WO_ID="$(sqlite3 "$DB_PATH" "SELECT Id FROM WorkOrders WHERE WoNo='$WO_NO' LIMIT 1;" 2>/dev/null)"
if [[ -z "$WO_ID" ]]; then
    echo "❌ No WO found with WoNo='$WO_NO'."
    exit 2
fi
echo "[lookup] WoId    = $WO_ID"
echo ""

# Pre-reset snapshot
echo "── current state ─────────────────────────────────────────────"
echo "WorkOrder:"
sqlite3 "$DB_PATH" -header -column "
    SELECT Id, WoNo, MesPhase, MaterialsReady, CurrentStep
      FROM WorkOrders
     WHERE Id=$WO_ID;
"
echo ""
echo "WoMaterials (count by status):"
sqlite3 "$DB_PATH" -header -column "
    SELECT Status, COUNT(*) AS n
      FROM WoMaterials
     WHERE WorkOrderId=$WO_ID
  GROUP BY Status;
"
echo ""
echo "WoPlateCheck:"
sqlite3 "$DB_PATH" -header -column "
    SELECT Id, Status, PlateNo, NgReasonCode, CheckedBy
      FROM WoPlateChecks
     WHERE WorkOrderId=$WO_ID;
"
echo ""
echo "WoCutterCheck:"
sqlite3 "$DB_PATH" -header -column "
    SELECT Id, Status, CutterNo, NgReasonCode, CheckedBy
      FROM WoCutterChecks
     WHERE WorkOrderId=$WO_ID;
"
echo ""

if [[ $COMMIT -eq 0 ]]; then
    echo "── dry-run summary ───────────────────────────────────────────"
    echo "Would reset:"
    echo "  * $(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoMaterials WHERE WorkOrderId=$WO_ID;") material rows"
    echo "  * $(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoPlateChecks WHERE WorkOrderId=$WO_ID;") plate row(s)"
    echo "  * $(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoCutterChecks WHERE WorkOrderId=$WO_ID;") cutter row(s)"
    echo "  * Set MesPhase='PREPRESS', MaterialsReady=0 on the WO."
    echo ""
    echo "To execute: re-run with --commit:"
    echo "  bash scripts/reset-prepress-for-wo.sh --wo $WO_NO --commit"
    exit 0
fi

echo "── COMMIT ─────────────────────────────────────────────────────"
sqlite3 "$DB_PATH" <<SQL
BEGIN;

UPDATE WoMaterials
   SET Status='Pending',
       NgReasonCode=NULL,
       NgNote=NULL,
       CheckedBy=NULL,
       CheckedAt=NULL,
       QtyLoaded=NULL,
       LotNo=NULL,
       UpdatedAt=datetime('now'),
       UpdatedBy='reset-tool'
 WHERE WorkOrderId=$WO_ID;

UPDATE WoPlateChecks
   SET Status='Pending',
       NgReasonCode=NULL,
       NgNote=NULL,
       CheckedBy=NULL,
       CheckedAt=NULL,
       PlateNo=NULL,
       UpdatedAt=datetime('now'),
       UpdatedBy='reset-tool'
 WHERE WorkOrderId=$WO_ID;

UPDATE WoCutterChecks
   SET Status='Pending',
       NgReasonCode=NULL,
       NgNote=NULL,
       CheckedBy=NULL,
       CheckedAt=NULL,
       CutterNo=NULL,
       UpdatedAt=datetime('now'),
       UpdatedBy='reset-tool'
 WHERE WorkOrderId=$WO_ID;

UPDATE WorkOrders
   SET MaterialsReady=0,
       MesPhase='PREPRESS',
       UpdatedAt=datetime('now'),
       UpdatedBy='reset-tool'
 WHERE Id=$WO_ID;

COMMIT;
SQL

echo ""
echo "── post-reset state ──────────────────────────────────────────"
sqlite3 "$DB_PATH" -header -column "
    SELECT WoNo, MesPhase, MaterialsReady FROM WorkOrders WHERE Id=$WO_ID;
"
echo ""
echo "WoMaterials (count by status):"
sqlite3 "$DB_PATH" -header -column "
    SELECT Status, COUNT(*) AS n
      FROM WoMaterials
     WHERE WorkOrderId=$WO_ID
  GROUP BY Status;
"

echo ""
echo "✓ Reset complete. Reload the PREPRESS dashboard on Catalyst to verify."
