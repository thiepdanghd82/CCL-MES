#!/usr/bin/env bash
# P10.7a-1.3 — reset a test WO back to a given phase so Henry can
# re-run the Catalyst checkpoint without restoring the DB.
#
# Direct SQL is the right tool here because:
#   - The legacy WorkOrderStateMachine is forward-only (no rewind path);
#     adding one would be a real product change, not a test affordance.
#   - The reset MUST bump RowVersion via the SQLite trigger so the
#     client's cached ETag becomes stale + the operator must re-scan,
#     which mimics post-deploy semantics.
#   - An audit row with action TEST_RESET keeps the forensic trail
#     intact — a future "why did this WO go backwards?" question has
#     a one-row answer.
#
# Usage:
#   bash scripts/reset-test-wo.sh <WO_NUMBER> [phase]
#
# Default phase: PrePressCheck (PREPRESS in MesPhase).
# Other values: OpSetting | IpqcApproval | ReadyToRun | Running |
#               Fqc | Oqc | Closed
#
# Examples:
#   bash scripts/reset-test-wo.sh WO-26-3683                  # to PrePressCheck
#   bash scripts/reset-test-wo.sh WO-26-3683 ReadyToRun       # to ReadyToRun
#   bash scripts/reset-test-wo.sh WO-26-3683 --db /tmp/db     # alt DB

set -u
set +e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HYBRID_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$HYBRID_ROOT/.." && pwd)"

DEFAULT_DB="$REPO_ROOT/data/ccl_mes.db"
DB_PATH="$DEFAULT_DB"

WO_NO="${1:-}"
shift 2>/dev/null || true

TARGET_STEP="${1:-PrePressCheck}"
case "${TARGET_STEP:-PrePressCheck}" in
    --db|--db=*) TARGET_STEP="PrePressCheck" ;;
    *)
        # First positional after WO is the phase.
        if [[ "${1:-}" != "" && "${1:0:2}" != "--" ]]; then
            shift
        fi
        ;;
esac

for arg in "$@"; do
    case "$arg" in
        --db=*) DB_PATH="${arg#--db=}" ;;
        --db) shift; DB_PATH="$1" ;;
    esac
done

if [[ -z "$WO_NO" ]]; then
    cat <<USAGE
Usage: $0 <WO_NUMBER> [phase]

Resets a test WO to the named phase via direct SQL.
The trigger bumps RowVersion; the audit log gets a TEST_RESET row.

Default phase: PrePressCheck (operator can re-scan + advance).

Examples:
  bash scripts/reset-test-wo.sh WO-26-3683
  bash scripts/reset-test-wo.sh WO-26-3683 ReadyToRun
  bash scripts/reset-test-wo.sh WO-26-3683 --db /tmp/db
USAGE
    exit 2
fi

if [[ ! -f "$DB_PATH" ]]; then
    echo "❌ DB not found: $DB_PATH"
    exit 1
fi

# Map legacy ProcessStepCode → canonical MesPhase (mirrors the
# migration backfill SQL; this is the authoritative table). NB: PAUSED
# / NEW are not test-targetable since they don't appear in legacy enum
# alone — only via the canonical path.
case "$TARGET_STEP" in
    PrePressCheck)  MES_PHASE="PREPRESS";       MATS=1; SETUP=0; ROHS=0; QTY=0 ;;
    OpSetting)      MES_PHASE="SETTING";        MATS=1; SETUP=0; ROHS=0; QTY=0 ;;
    IpqcApproval)   MES_PHASE="IPQC_WAIT";      MATS=1; SETUP=1; ROHS=0; QTY=0 ;;
    ReadyToRun)     MES_PHASE="IPQC_APPROVED";  MATS=1; SETUP=1; ROHS=0; QTY=0 ;;
    Running)        MES_PHASE="RUNNING";        MATS=1; SETUP=1; ROHS=0; QTY=0 ;;
    Fqc)            MES_PHASE="FQC_PENDING";    MATS=1; SETUP=1; ROHS=0; QTY=100 ;;
    Oqc)            MES_PHASE="OQC_PENDING";    MATS=1; SETUP=1; ROHS=0; QTY=100 ;;
    Closed)         MES_PHASE="DONE";           MATS=1; SETUP=1; ROHS=1; QTY=100 ;;
    *)
        echo "❌ Unknown phase: $TARGET_STEP"
        echo "   Valid: PrePressCheck | OpSetting | IpqcApproval | ReadyToRun |"
        echo "          Running | Fqc | Oqc | Closed"
        exit 2
        ;;
esac

# Confirm the WO exists + capture its ID for the audit emit.
EXIST_ROW=$(sqlite3 -separator '|' "$DB_PATH" \
    "SELECT Id, CurrentStep, MesPhase, hex(RowVersion) FROM WorkOrders WHERE WoNo = '$WO_NO';" 2>/dev/null)
if [[ -z "$EXIST_ROW" ]]; then
    echo "❌ WO '$WO_NO' not found in $DB_PATH"
    exit 1
fi
WO_ID=$(echo "$EXIST_ROW" | cut -d'|' -f1)
FROM_STEP=$(echo "$EXIST_ROW" | cut -d'|' -f2)
FROM_PHASE=$(echo "$EXIST_ROW" | cut -d'|' -f3)
OLD_RV=$(echo "$EXIST_ROW" | cut -d'|' -f4)

echo "════════════════════════════════════════════════════════════════════"
echo "  reset-test-wo — $WO_NO ($FROM_STEP / $FROM_PHASE) → $TARGET_STEP / $MES_PHASE"
echo "════════════════════════════════════════════════════════════════════"

# Single transaction:
#   1. UPDATE the WO. The trigger fires (NEW.RowVersion = OLD.RowVersion
#      because EF wasn't involved; the trigger guard catches it) and
#      writes a fresh randomblob(8).
#   2. INSERT the WoStatusHistory + AuditLogs rows so this reset has
#      forensic trail.
NOW_UTC=$(date -u +'%Y-%m-%dT%H:%M:%SZ')
sqlite3 "$DB_PATH" <<SQL
BEGIN;
UPDATE WorkOrders
SET CurrentStep    = '$TARGET_STEP',
    MesPhase       = '$MES_PHASE',
    MaterialsReady = $MATS,
    SetupConfirmed = $SETUP,
    RohsOk         = $ROHS,
    ProducedQty    = $QTY,
    UpdatedAt      = '$NOW_UTC'
WHERE WoNo = '$WO_NO';

INSERT INTO WoStatusHistories
    (CreatedAt, CreatedBy, WorkOrderId, FromStep, ToStep, Action, ByUser, Reason, UpdatedAt, UpdatedBy)
VALUES
    ('$NOW_UTC', 'test-tool', $WO_ID, '$FROM_STEP', '$TARGET_STEP', 'TestReset', 'test-tool',
     'reset-test-wo.sh: forced reset from $FROM_STEP to $TARGET_STEP for Henry checkpoint',
     '$NOW_UTC', 'test-tool');

INSERT INTO AuditLogs
    (Timestamp, ActorUsername, ActorRole, Action, TargetType, TargetId, Detail, Source, CreatedAt, CreatedBy)
VALUES
    ('$NOW_UTC', 'test-tool', 'sys', 'TEST_RESET', 'WorkOrder', $WO_ID,
     json_object(
        'wo_id', $WO_ID,
        'wo_no', '$WO_NO',
        'from_step', '$FROM_STEP',
        'from_phase', '$FROM_PHASE',
        'to_step', '$TARGET_STEP',
        'to_phase', '$MES_PHASE',
        'origin', 'reset-test-wo.sh'
     ),
     'TestTool', '$NOW_UTC', 'test-tool');
COMMIT;
SQL

RC=$?
if [[ $RC -ne 0 ]]; then
    echo "❌ SQL failed with rc=$RC"
    exit 1
fi

NEW_RV=$(sqlite3 "$DB_PATH" "SELECT hex(RowVersion) FROM WorkOrders WHERE WoNo = '$WO_NO';" 2>/dev/null)

echo ""
echo "  ✓ Reset complete"
echo "    Phase   : $FROM_STEP / $FROM_PHASE → $TARGET_STEP / $MES_PHASE"
echo "    Flags   : MaterialsReady=$MATS, SetupConfirmed=$SETUP, RohsOk=$ROHS, ProducedQty=$QTY"
echo "    RowVer  : ${OLD_RV:0:12}... → ${NEW_RV:0:12}..."
echo "    Audit   : WoStatusHistories[Action='TestReset'] + AuditLogs[Action='TEST_RESET']"
echo ""
echo "  Next step:"
echo "    Quay lại app Catalyst, quét lại $WO_NO (ETag cũ đã bị bump nên client cache bị mất),"
echo "    rồi tap 'Nhận / Bắt đầu' để test lại flow."
echo "════════════════════════════════════════════════════════════════════"
