#!/usr/bin/env bash
# P10.7a-1.3 — seed 4 test WOs (WO-26-3684 .. WO-26-3687) cloned from
# WO-26-3683 so Henry's Catalyst checkpoint has enough scan targets
# without restoring the DB or hand-creating records.
#
# Each clone:
#   - Same Customer / Product / ProductRevision / Machine as the
#     template WO (FKs valid).
#   - CurrentStep = PrePressCheck, MesPhase = PREPRESS.
#   - MaterialsReady = 1 (PREPRESS → SETTING gate passes when operator
#     clicks Accept — the legacy state machine guard is satisfied).
#   - Fresh RowVersion via the INSERT trigger.
#   - TEST_SEED audit row for forensic trail.
#
# Idempotent: re-running on an already-seeded DB is a no-op for any
# WO_NO that already exists (we check first + skip).
#
# Usage:
#   bash scripts/seed-test-wos.sh                            # default template + count
#   bash scripts/seed-test-wos.sh --template WO-26-3683 --count 4
#   bash scripts/seed-test-wos.sh --db /tmp/db

set -u
set +e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
HYBRID_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$HYBRID_ROOT/.." && pwd)"

DB_PATH="$REPO_ROOT/data/ccl_mes.db"
TEMPLATE_WO="WO-26-3683"
COUNT=4

while [[ $# -gt 0 ]]; do
    case "$1" in
        --template) shift; TEMPLATE_WO="$1" ;;
        --template=*) TEMPLATE_WO="${1#--template=}" ;;
        --count) shift; COUNT="$1" ;;
        --count=*) COUNT="${1#--count=}" ;;
        --db) shift; DB_PATH="$1" ;;
        --db=*) DB_PATH="${1#--db=}" ;;
        *)
            echo "Unknown arg: $1"
            exit 2
            ;;
    esac
    shift
done

if [[ ! -f "$DB_PATH" ]]; then
    echo "❌ DB not found: $DB_PATH"
    exit 1
fi

# Read template WO's FK values so the clones reference valid rows.
TEMPLATE_ROW=$(sqlite3 -separator '|' "$DB_PATH" \
    "SELECT Id, CustomerId, ProductId, ProductRevisionId, ProductName, MachineCode, MachineName, TargetQty, Uom
     FROM WorkOrders WHERE WoNo = '$TEMPLATE_WO';" 2>/dev/null)

if [[ -z "$TEMPLATE_ROW" ]]; then
    echo "❌ Template WO '$TEMPLATE_WO' not found in $DB_PATH"
    echo "   Available WOs (first 5):"
    sqlite3 "$DB_PATH" "SELECT WoNo FROM WorkOrders ORDER BY Id LIMIT 5;"
    exit 1
fi

T_ID=$(echo "$TEMPLATE_ROW" | cut -d'|' -f1)
T_CUSTOMER=$(echo "$TEMPLATE_ROW" | cut -d'|' -f2)
T_PRODUCT=$(echo "$TEMPLATE_ROW" | cut -d'|' -f3)
T_PRODREV=$(echo "$TEMPLATE_ROW" | cut -d'|' -f4)
T_PRODNAME=$(echo "$TEMPLATE_ROW" | cut -d'|' -f5)
T_MACHCODE=$(echo "$TEMPLATE_ROW" | cut -d'|' -f6)
T_MACHNAME=$(echo "$TEMPLATE_ROW" | cut -d'|' -f7)
T_TARGET=$(echo "$TEMPLATE_ROW" | cut -d'|' -f8)
T_UOM=$(echo "$TEMPLATE_ROW" | cut -d'|' -f9)

# ProductRevisionId may be NULL on the template; the clones inherit
# whatever the template had. The PREPRESS → SETTING guard wants it
# non-null, so if the template has it null we don't fail seeding —
# we leave the clones at the same state. Operator can run
# reset-test-wo.sh to force a fresh state.
[[ "$T_PRODREV" == "" ]] && PRODREV_SQL="NULL" || PRODREV_SQL="$T_PRODREV"

echo "════════════════════════════════════════════════════════════════════"
echo "  seed-test-wos — cloning $TEMPLATE_WO × $COUNT"
echo "════════════════════════════════════════════════════════════════════"
echo "  Template id=$T_ID, customer=$T_CUSTOMER, product=$T_PRODUCT, prodRev=$PRODREV_SQL"
echo "  Output WOs:"

# Number the clones — WO-26-3684, 3685, ... based on the template's
# numeric suffix.
TEMPLATE_PREFIX=$(echo "$TEMPLATE_WO" | sed -E 's/[0-9]+$//')
TEMPLATE_SUFFIX=$(echo "$TEMPLATE_WO" | sed -E 's/^.*[^0-9]([0-9]+)$/\1/')
[[ -z "$TEMPLATE_PREFIX" ]] && TEMPLATE_PREFIX="WO-TEST-"
[[ -z "$TEMPLATE_SUFFIX" ]] && TEMPLATE_SUFFIX="0"

CREATED=0
SKIPPED=0
NOW_UTC=$(date -u +'%Y-%m-%dT%H:%M:%SZ')

for ((i = 1; i <= COUNT; i++)); do
    NEW_SUFFIX=$((TEMPLATE_SUFFIX + i))
    NEW_WO="${TEMPLATE_PREFIX}${NEW_SUFFIX}"

    # Skip if already exists.
    EXISTS=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WorkOrders WHERE WoNo = '$NEW_WO';" 2>/dev/null)
    if [[ "$EXISTS" -gt 0 ]]; then
        echo "    - $NEW_WO  [exists, skipped]"
        SKIPPED=$((SKIPPED + 1))
        continue
    fi

    sqlite3 "$DB_PATH" <<SQL 2>/dev/null
BEGIN;
INSERT INTO WorkOrders
    (CreatedAt, CreatedBy, WoNo, CustomerId, ProductId, ProductRevisionId, ProductName,
     MachineCode, MachineName, TargetQty, Uom, ProducedQty, CurrentStep, Status,
     Priority, MaterialsReady, SetupConfirmed, RohsOk, PlannedStart, PlannedEnd,
     UpdatedAt, UpdatedBy, MesPhase, RowVersion)
VALUES
    ('$NOW_UTC', 'test-tool', '$NEW_WO', $T_CUSTOMER, $T_PRODUCT, $PRODREV_SQL, '$T_PRODNAME',
     '$T_MACHCODE', '$T_MACHNAME', $T_TARGET, '$T_UOM', 0, 'PrePressCheck', 'Draft',
     0, 1, 0, 0, NULL, NULL,
     '$NOW_UTC', 'test-tool', 'PREPRESS', X'');

INSERT INTO AuditLogs
    (Timestamp, ActorUsername, ActorRole, Action, TargetType, TargetId, Detail, Source, CreatedAt, CreatedBy)
VALUES
    ('$NOW_UTC', 'test-tool', 'sys', 'TEST_SEED', 'WorkOrder',
     (SELECT Id FROM WorkOrders WHERE WoNo = '$NEW_WO'),
     json_object(
        'wo_id', (SELECT Id FROM WorkOrders WHERE WoNo = '$NEW_WO'),
        'wo_no', '$NEW_WO',
        'template_wo', '$TEMPLATE_WO',
        'origin', 'seed-test-wos.sh'
     ),
     'TestTool', '$NOW_UTC', 'test-tool');
COMMIT;
SQL

    RC=$?
    if [[ $RC -ne 0 ]]; then
        echo "    - $NEW_WO  [SQL FAILED rc=$RC]"
        continue
    fi

    NEW_ID=$(sqlite3 "$DB_PATH" "SELECT Id FROM WorkOrders WHERE WoNo = '$NEW_WO';" 2>/dev/null)
    NEW_RV=$(sqlite3 "$DB_PATH" "SELECT hex(RowVersion) FROM WorkOrders WHERE WoNo = '$NEW_WO';" 2>/dev/null)
    echo "    + $NEW_WO  id=$NEW_ID  RowVersion=${NEW_RV:0:12}..."
    CREATED=$((CREATED + 1))
done

echo ""
echo "  Created: $CREATED  ·  Skipped (already existed): $SKIPPED"
echo ""
echo "  Next step:"
echo "    Quay lại Catalyst app, quét bất kỳ trong $CREATED WO mới + WO gốc"
echo "    để test scan/Accept/conflict flow."
echo "════════════════════════════════════════════════════════════════════"
