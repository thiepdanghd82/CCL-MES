#!/usr/bin/env bash
# P10.7d-2 — reset IPQC check row + WO phase for one WO so the operator
# can re-verify the dual-sig flow without recreating fixtures. Mirrors
# reset-prepress-for-wo.sh shape so checkpoint scripts can chain both.
#
# Scope (per WO):
#   * UPDATE WoIpqcChecks SET MaterialStatus='Pending',
#                            MaterialNgReasonCode=NULL, MaterialNgNote=NULL,
#                            PrintAStatus='Pending', PrintANgReasonCode=NULL, PrintANgNote=NULL,
#                            PrintBStatus='Pending', PrintBNgReasonCode=NULL, PrintBNgNote=NULL,
#                            PrintCStatus='Pending', PrintCNgReasonCode=NULL, PrintCNgNote=NULL,
#                            Judgment='Pending', SpecialAcceptReason=NULL,
#                            IpqcSubmittedBy=NULL, IpqcSubmittedAt=NULL,
#                            QaOutcome='Pending', QaReason=NULL,
#                            QaApprovedBy=NULL, QaApprovedAt=NULL,
#                            UpdatedAt=now(), UpdatedBy='reset-ipqc-tool'
#   * UPDATE WorkOrders SET MesPhase='IPQC_WAIT', CurrentStep='IpqcApproval',
#                          UpdatedAt=now(), UpdatedBy='reset-ipqc-tool'
#     (nudges RowVersion via the SQLite UPDATE trigger so the next GET
#     /ipqc fetches a fresh ETag.)
#
# Default: dry-run — preview affected rows + counts. NO writes.
# --commit: execute the UPDATE transaction.
#
# Operator workflow:
#   1. bash scripts/reset-ipqc-for-wo.sh --wo WO-26-3684             # preview
#   2. bash scripts/reset-ipqc-for-wo.sh --wo WO-26-3684 --commit    # execute
#
# Rule 7.1 — [ctx] DB= + DB sha8 printed at startup.
# Agent MUST NOT run --commit on Henry's behalf.
#
# Usage:
#   bash CCL-MES-Hybrid/scripts/reset-ipqc-for-wo.sh \
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
usage: bash scripts/reset-ipqc-for-wo.sh --wo <wo-no> [--commit] [--db /abs/path]

  --wo <wo-no>   Target WO number (required), e.g. WO-26-3684.
  --commit       Execute the UPDATE transaction.
  --db <path>    Target a non-default SQLite file (default = data/ccl_mes.db).
EOF
            exit 0
            ;;
        *) echo "unknown arg: $1"; exit 64 ;;
    esac
done

if [[ -z "$WO_NO" ]]; then
    echo "❌ --wo <wo-no> is required."
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

WO_ID=$(sqlite3 "$DB_PATH" "SELECT Id FROM WorkOrders WHERE WoNo='$WO_NO' LIMIT 1;" 2>/dev/null)
if [[ -z "$WO_ID" ]]; then
    echo "❌ WO $WO_NO not found in $DB_ABS"
    exit 1
fi

echo "===================================================================="
echo "reset-ipqc-for-wo — $(date '+%Y-%m-%d %H:%M:%S')"
echo "===================================================================="
echo "[ctx] DB      = $DB_ABS"
echo "[ctx] DB sha8 = $DB_SHA8"
echo "[ctx] WO      = $WO_NO (Id=$WO_ID)"
echo "[ctx] mode    = $([ $COMMIT -eq 1 ] && echo 'COMMIT (will UPDATE)' || echo 'DRY-RUN (no writes)')"
echo "===================================================================="
echo ""

echo "── Pre-reset state ──"
sqlite3 "$DB_PATH" -header -column "
  SELECT WoNo, MesPhase, CurrentStep FROM WorkOrders WHERE Id=$WO_ID;
"
echo ""
sqlite3 "$DB_PATH" -header -column "
  SELECT MaterialStatus, PrintAStatus, PrintBStatus, PrintCStatus,
         Judgment, IpqcSubmittedBy, QaOutcome, QaApprovedBy
  FROM WoIpqcChecks WHERE WorkOrderId=$WO_ID;
"
echo ""

if [[ $COMMIT -eq 0 ]]; then
    echo "── DRY-RUN — no rows written. Re-run with --commit to execute. ──"
    exit 0
fi

# COMMIT path
echo "── Executing reset transaction ──"
sqlite3 "$DB_PATH" <<SQL
BEGIN;
UPDATE WoIpqcChecks SET
    MaterialStatus='Pending', MaterialNgReasonCode=NULL, MaterialNgNote=NULL,
    PrintAStatus='Pending', PrintANgReasonCode=NULL, PrintANgNote=NULL,
    PrintBStatus='Pending', PrintBNgReasonCode=NULL, PrintBNgNote=NULL,
    PrintCStatus='Pending', PrintCNgReasonCode=NULL, PrintCNgNote=NULL,
    Judgment='Pending', SpecialAcceptReason=NULL,
    IpqcSubmittedBy=NULL, IpqcSubmittedAt=NULL,
    QaOutcome='Pending', QaReason=NULL,
    QaApprovedBy=NULL, QaApprovedAt=NULL,
    UpdatedAt=datetime('now'), UpdatedBy='reset-ipqc-tool'
WHERE WorkOrderId=$WO_ID;

UPDATE WorkOrders SET
    MesPhase='IPQC_WAIT', CurrentStep='IpqcApproval',
    UpdatedAt=datetime('now'), UpdatedBy='reset-ipqc-tool'
WHERE Id=$WO_ID;
COMMIT;
SQL
RC=$?

if [[ $RC -ne 0 ]]; then
    echo "❌ Reset transaction FAILED (sqlite3 exit=$RC)."
    exit 1
fi

echo ""
echo "── Post-reset state ──"
sqlite3 "$DB_PATH" -header -column "
  SELECT WoNo, MesPhase, CurrentStep FROM WorkOrders WHERE Id=$WO_ID;
"
echo ""
sqlite3 "$DB_PATH" -header -column "
  SELECT MaterialStatus, PrintAStatus, PrintBStatus, PrintCStatus,
         Judgment, IpqcSubmittedBy, QaOutcome
  FROM WoIpqcChecks WHERE WorkOrderId=$WO_ID;
"
echo ""
echo "✓ Reset complete. WO $WO_NO is back in IPQC_WAIT with all 4 slots Pending."
