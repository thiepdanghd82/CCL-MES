#!/usr/bin/env bash
# P10.7b-1 — housekeeping: purge test-only audit rows from the live DB.
# P10.7c-4 — extended to cover WO_RUN_* test rows + WoRunSessions /
#             WoPauseEvents / WoQtyEntries tagged with checkpoint-7c* +
#             manual-l19-test + checkpoint-l19-walk vocabulary.
# P10.7e-4 — extended to cover WO_FQC_* / WO_OQC_* / WO_SHIPPED audit
#             rows emitted by checkpoint-7e-2 / checkpoint-7e-final (actor
#             ∈ the 3 self-seeded OQC test users) + the WoQcChecks /
#             WoQcCheckItems / WoQcPhotos rows those checkpoints stamp
#             (InspectedBy/ReviewedBy/ApprovedBy ∈ the test users) +
#             the 3 seeded OQC test users themselves.
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
#   * WO_PREPRESS_* with checkpoint-7b / verify-p10.7b vocab (existing).
#   * P10.7c-4 NEW — WO_SETTING_* + WO_RUN_* audit rows where Detail
#     contains 'checkpoint-7c%' / 'verify-p10.7c%' / 'manual-l19-test' /
#     'manual-test' / 'checkpoint-l19-walk'. These are all test-script
#     vocabularies — real operator-driven RUNNING audit rows use
#     application-supplied notes (Vietnamese strings from the
#     dashboard's Pause modal / NG modal) and pass through.
#   * P10.7c-4 NEW — WoRunSessions / WoPauseEvents / WoQtyEntries rows
#     whose StartedBy / EnteredBy / UpdatedBy match the checkpoint
#     vocabulary. Domain rows from real operator runs use the operator's
#     username + are preserved.
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

# P10.7b-4 — also purge BOM seed rows + WO_PREPRESS_* audit noise from
# 7b-2 and 7b-final checkpoint scripts. BOM rows are tagged with
# CreatedBy='checkpoint-7b-2' / 'verify-p10.7b' / 'checkpoint-7b-final'
# so they can be removed without touching real BOMs. WO_PREPRESS_*
# audit rows from the scripts share Detail substrings (LOT-VERIFY-* /
# LOT-FINAL-* / PLATE-VERIFY-* / CUT-VERIFY-* / CUT-FINAL-* / the
# explicit verify-script NG note) so the noise filter catches them.
PATTERN_PREPRESS_NOISE_LIKE="(\"Detail\" LIKE '%checkpoint-7b%' \
  OR \"Detail\" LIKE '%verify-p10.7b%' \
  OR \"Detail\" LIKE '%LOT-VERIFY%' \
  OR \"Detail\" LIKE '%LOT-FINAL%' \
  OR \"Detail\" LIKE '%PLATE-VERIFY%' \
  OR \"Detail\" LIKE '%PLATE-FINAL%' \
  OR \"Detail\" LIKE '%CUT-VERIFY%' \
  OR \"Detail\" LIKE '%CUT-FINAL%' \
  OR \"Detail\" LIKE '%verify-script NG path%')"

# P10.7c-4 — RUNNING surface audit noise: every detail JSON emitted by
# checkpoint-7c*.sh / verify-p10.7c.sh / Henry's manual L19 shim flows.
# The patterns target the test-script vocabularies; real ops audits
# carry the operator's Vietnamese pause/NG notes (which never include
# these tokens) so they pass through unchanged.
PATTERN_RUNNING_NOISE_LIKE="(\"Detail\" LIKE '%checkpoint-7c%' \
  OR \"Detail\" LIKE '%verify-p10.7c%' \
  OR \"Detail\" LIKE '%checkpoint-l19-walk%' \
  OR \"Detail\" LIKE '%manual-l19-test%' \
  OR \"Detail\" LIKE '%manual-test%' \
  OR \"Detail\" LIKE '%checkpoint final NG sample%' \
  OR \"Detail\" LIKE '%checkpoint-7c-2 NG sample%' \
  OR \"Detail\" LIKE '%checkpoint pause%' \
  OR \"Detail\" LIKE '%checkpoint miscount fix%')"

BOM_SEED_TAGS="'checkpoint-7b-2','verify-p10.7b','checkpoint-7b-final'"

# P10.7c-4 — actor tags written by the test scripts into domain tables
# (WoRunSessions.StartedBy / WoPauseEvents.StartedBy /
# WoQtyEntries.EnteredBy / WoQtyEntries.CreatedBy).
RUNNING_ACTOR_TAGS="'checkpoint-7c-2','checkpoint-7c-final','verify-p10.7c','checkpoint-l19-walk','manual-l19-test','manual-test','checkpoint-7d-2','checkpoint-7d-final'"

# P10.7d-2 — usernames the 7d-2 checkpoint self-seeds via POST
# /api/v2/admin/users (AccountControlController, P10.6c). Both have
# role QC + DisplayName "P10.7d-* checkpoint test user" so they're
# easy to distinguish from real operator accounts. Re-used by
# checkpoint-7d-final.sh (cycles 1+2+3 + Q3 dual-sig).
IPQC_QA_TEST_USERS="'ipqc-test-checkpoint','qa-test-checkpoint'"

# P10.7e-4 — usernames self-seeded by checkpoint-7e-2.sh + checkpoint-
# 7e-final.sh via POST /api/v2/admin/users. All 3 carry role QC +
# DisplayName "P10.7e-* checkpoint test user". Inspector/Reviewer/
# Approver drive the FQC single-sig + OQC 3-sig chains, so every 7e
# audit row + WoQcChecks signature column references one of them. Real
# QC operators use real usernames + pass through untouched.
OQC_TEST_USERS="'oqc-test-inspector','oqc-test-reviewer','oqc-test-approver'"

# P10.7d-4 — IPQC + QA audit row noise: every WO_IPQC_CHECK /
# WO_IPQC_JUDGMENT / WO_QA_APPROVE / WO_QA_APPROVE_DENIED row
# emitted by checkpoint-7d-* scripts has one of the 2 seeded users
# as ActorUsername (script ran as that user via their JWT). Real
# operator audits use real usernames so this pattern is safe.

TOTAL_BEFORE=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM AuditLogs;")
TESTRESET_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM AuditLogs WHERE Action='TEST_RESET' AND ActorUsername='test-tool';")
NOISE_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM AuditLogs WHERE Action='SYS_RECOVERY' AND $PATTERN_NOISE_LIKE;")
PREPRESS_AUDIT_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM AuditLogs WHERE Action IN ('WO_PREPRESS_MATERIAL_SET','WO_PREPRESS_PLATE_SET','WO_PREPRESS_CUTTER_SET') AND $PATTERN_PREPRESS_NOISE_LIKE;")

# P10.7c-4 — count RUNNING surface noise rows
RUNNING_AUDIT_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM AuditLogs WHERE Action IN ('WO_SETTING_START','WO_SETTING_DONE','WO_RUN_START','WO_RUN_QTY_ADD','WO_RUN_QTY_CORRECT','WO_RUN_PAUSE','WO_RUN_RESUME','WO_RUN_FINISH','WO_STATE_CONFLICT') AND $PATTERN_RUNNING_NOISE_LIKE;")

# Domain test rows tied to checkpoint actor tags (only safe to delete
# the children — qty entries + pauses + sessions in dep-order).
QTY_ENTRY_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoQtyEntries WHERE EnteredBy IN ($RUNNING_ACTOR_TAGS);" 2>/dev/null)
QTY_ENTRY_COUNT="${QTY_ENTRY_COUNT:-0}"
PAUSE_EVENT_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoPauseEvents WHERE StartedBy IN ($RUNNING_ACTOR_TAGS);" 2>/dev/null)
PAUSE_EVENT_COUNT="${PAUSE_EVENT_COUNT:-0}"
RUN_SESSION_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoRunSessions WHERE StartedBy IN ($RUNNING_ACTOR_TAGS);" 2>/dev/null)
RUN_SESSION_COUNT="${RUN_SESSION_COUNT:-0}"

BOM_SEED_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM ManufacturingStructures WHERE CreatedBy IN ($BOM_SEED_TAGS);" 2>/dev/null)
BOM_SEED_COUNT="${BOM_SEED_COUNT:-0}"

# P10.7d-2 — count seeded test users
IPQC_QA_USER_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM Users WHERE Username IN ($IPQC_QA_TEST_USERS);" 2>/dev/null)
IPQC_QA_USER_COUNT="${IPQC_QA_USER_COUNT:-0}"

# P10.7d-4 — count IPQC + QA audit rows emitted by the 7d checkpoints.
IPQC_QA_AUDIT_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM AuditLogs WHERE Action IN ('WO_IPQC_CHECK','WO_IPQC_JUDGMENT','WO_QA_APPROVE','WO_QA_APPROVE_DENIED') AND ActorUsername IN ($IPQC_QA_TEST_USERS);" 2>/dev/null)
IPQC_QA_AUDIT_COUNT="${IPQC_QA_AUDIT_COUNT:-0}"

# P10.7e-4 — FQC/OQC outgoing-quality audit noise: every WO_FQC_* /
# WO_OQC_* / WO_SHIPPED row emitted by the 7e checkpoints has one of
# the 3 seeded OQC test users as ActorUsername (the script ran as that
# user via their JWT). Real operator audits use real usernames so this
# pattern is safe.
FQC_OQC_AUDIT_ACTIONS="'WO_FQC_JUDGMENT','WO_FQC_REJECT_TO_PREPRESS','WO_OQC_INSPECT','WO_OQC_REVIEW','WO_OQC_REVIEW_DENIED','WO_OQC_APPROVE','WO_OQC_APPROVE_DENIED','WO_OQC_REJECT_TO_FQC_PENDING','WO_SHIPPED'"
FQC_OQC_AUDIT_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM AuditLogs WHERE Action IN ($FQC_OQC_AUDIT_ACTIONS) AND ActorUsername IN ($OQC_TEST_USERS);" 2>/dev/null)
FQC_OQC_AUDIT_COUNT="${FQC_OQC_AUDIT_COUNT:-0}"

# P10.7e-4 — count the seeded OQC test users.
OQC_USER_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM Users WHERE Username IN ($OQC_TEST_USERS);" 2>/dev/null)
OQC_USER_COUNT="${OQC_USER_COUNT:-0}"

# P10.7e-4 — count the WoQcChecks (+ child items + photos) whose
# signature columns reference a seeded OQC test user. Deleted in
# dependency order: WoQcPhotos → WoQcCheckItems → WoQcChecks.
QC_CHECK_PRED="(InspectedBy IN ($OQC_TEST_USERS) OR ReviewedBy IN ($OQC_TEST_USERS) OR ApprovedBy IN ($OQC_TEST_USERS) OR UpdatedBy='checkpoint-7e-final')"
QC_CHECK_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoQcChecks WHERE $QC_CHECK_PRED;" 2>/dev/null)
QC_CHECK_COUNT="${QC_CHECK_COUNT:-0}"
QC_ITEM_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoQcCheckItems WHERE WoQcCheckId IN (SELECT Id FROM WoQcChecks WHERE $QC_CHECK_PRED);" 2>/dev/null)
QC_ITEM_COUNT="${QC_ITEM_COUNT:-0}"
QC_PHOTO_COUNT=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoQcPhotos WHERE WoQcCheckItemId IN (SELECT Id FROM WoQcCheckItems WHERE WoQcCheckId IN (SELECT Id FROM WoQcChecks WHERE $QC_CHECK_PRED));" 2>/dev/null)
QC_PHOTO_COUNT="${QC_PHOTO_COUNT:-0}"

AUDIT_PURGE_TOTAL=$((TESTRESET_COUNT + NOISE_COUNT + PREPRESS_AUDIT_COUNT + RUNNING_AUDIT_COUNT + IPQC_QA_AUDIT_COUNT + FQC_OQC_AUDIT_COUNT))
DOMAIN_PURGE_TOTAL=$((QTY_ENTRY_COUNT + PAUSE_EVENT_COUNT + RUN_SESSION_COUNT + QC_PHOTO_COUNT + QC_ITEM_COUNT + QC_CHECK_COUNT))
GRAND_TOTAL=$((AUDIT_PURGE_TOTAL + BOM_SEED_COUNT + DOMAIN_PURGE_TOTAL + IPQC_QA_USER_COUNT + OQC_USER_COUNT))

echo "── Pre-purge counts ──"
echo "  TOTAL AuditLogs                                    : $TOTAL_BEFORE"
echo "  TEST_RESET (actor=test-tool) candidates            : $TESTRESET_COUNT"
echo "  SYS_RECOVERY noise (Detail LIKE patterns)          : $NOISE_COUNT"
echo "  WO_PREPRESS_* test rows (7b-* / verify-p10.7b)     : $PREPRESS_AUDIT_COUNT"
echo "  WO_SETTING/RUN_* test rows (7c-* / l19-walk)       : $RUNNING_AUDIT_COUNT"
echo "  WO_IPQC_*/WO_QA_* test rows (7d-* checkpoints)     : $IPQC_QA_AUDIT_COUNT"
echo "  WO_FQC_*/WO_OQC_*/WO_SHIPPED test rows (7e-*)      : $FQC_OQC_AUDIT_COUNT"
echo "  WoQcChecks / Items / Photos (7e signatures)        : $QC_CHECK_COUNT / $QC_ITEM_COUNT / $QC_PHOTO_COUNT"
echo "  Seeded OQC checkpoint test users                   : $OQC_USER_COUNT"
echo "  WoQtyEntries (RUNNING actor tags)                  : $QTY_ENTRY_COUNT"
echo "  WoPauseEvents (RUNNING actor tags)                 : $PAUSE_EVENT_COUNT"
echo "  WoRunSessions (RUNNING actor tags)                 : $RUN_SESSION_COUNT"
echo "  ManufacturingStructures BOM seed rows              : $BOM_SEED_COUNT"
echo "  Seeded IPQC/QA checkpoint test users               : $IPQC_QA_USER_COUNT"
echo "  TOTAL AUDIT TO PURGE                               : $AUDIT_PURGE_TOTAL"
echo "  TOTAL DOMAIN TO PURGE                              : $DOMAIN_PURGE_TOTAL"
echo "  TOTAL ALL (audit + domain + BOM seed)              : $GRAND_TOTAL"
echo ""

if [[ "$GRAND_TOTAL" == "0" ]]; then
    echo "✓ Nothing to purge — audit log + BOM seed are clean."
    exit 0
fi

echo "── Candidate audit rows ──"
echo ""
if [[ "$TESTRESET_COUNT" != "0" ]]; then
    echo "TEST_RESET rows:"
    sqlite3 -column -header "$DB_PATH" \
      "SELECT Id, datetime(Timestamp) AS T, Action, TargetId, substr(Detail,1,60) AS Detail60
       FROM AuditLogs WHERE Action='TEST_RESET' AND ActorUsername='test-tool'
       ORDER BY Id;"
    echo ""
fi
if [[ "$NOISE_COUNT" != "0" ]]; then
    echo "SYS_RECOVERY noise rows:"
    sqlite3 -column -header "$DB_PATH" \
      "SELECT Id, datetime(Timestamp) AS T, Action, TargetId, substr(Detail,1,80) AS Detail80
       FROM AuditLogs WHERE Action='SYS_RECOVERY' AND $PATTERN_NOISE_LIKE
       ORDER BY Id;"
    echo ""
fi
if [[ "$PREPRESS_AUDIT_COUNT" != "0" ]]; then
    echo "WO_PREPRESS_* test rows:"
    sqlite3 -column -header "$DB_PATH" \
      "SELECT Id, datetime(Timestamp) AS T, Action, TargetId, substr(Detail,1,80) AS Detail80
       FROM AuditLogs WHERE Action IN ('WO_PREPRESS_MATERIAL_SET','WO_PREPRESS_PLATE_SET','WO_PREPRESS_CUTTER_SET') AND $PATTERN_PREPRESS_NOISE_LIKE
       ORDER BY Id;"
    echo ""
fi
if [[ "$RUNNING_AUDIT_COUNT" != "0" ]]; then
    echo "WO_SETTING/RUN_* test rows:"
    sqlite3 -column -header "$DB_PATH" \
      "SELECT Id, datetime(Timestamp) AS T, Action, TargetId, substr(Detail,1,80) AS Detail80
       FROM AuditLogs WHERE Action IN ('WO_SETTING_START','WO_SETTING_DONE','WO_RUN_START','WO_RUN_QTY_ADD','WO_RUN_QTY_CORRECT','WO_RUN_PAUSE','WO_RUN_RESUME','WO_RUN_FINISH','WO_STATE_CONFLICT') AND $PATTERN_RUNNING_NOISE_LIKE
       ORDER BY Id;"
    echo ""
fi
if [[ "$QTY_ENTRY_COUNT" != "0" ]]; then
    echo "WoQtyEntries (RUNNING actor tags):"
    sqlite3 -column -header "$DB_PATH" \
      "SELECT Id, WoId, RunSessionId, QtyDoneDelta, QtyNgDelta, EnteredBy
       FROM WoQtyEntries WHERE EnteredBy IN ($RUNNING_ACTOR_TAGS)
       ORDER BY Id;"
    echo ""
fi
if [[ "$PAUSE_EVENT_COUNT" != "0" ]]; then
    echo "WoPauseEvents (RUNNING actor tags):"
    sqlite3 -column -header "$DB_PATH" \
      "SELECT Id, WoId, ReasonCode, datetime(StartedAt) AS Start, datetime(EndedAt) AS Ended, StartedBy
       FROM WoPauseEvents WHERE StartedBy IN ($RUNNING_ACTOR_TAGS)
       ORDER BY Id;"
    echo ""
fi
if [[ "$RUN_SESSION_COUNT" != "0" ]]; then
    echo "WoRunSessions (RUNNING actor tags):"
    sqlite3 -column -header "$DB_PATH" \
      "SELECT Id, WoId, datetime(StartedAt) AS Start, datetime(EndedAt) AS Ended, StartedBy
       FROM WoRunSessions WHERE StartedBy IN ($RUNNING_ACTOR_TAGS)
       ORDER BY Id;"
    echo ""
fi
if [[ "$BOM_SEED_COUNT" != "0" ]]; then
    echo "ManufacturingStructures BOM seed rows:"
    sqlite3 -column -header "$DB_PATH" \
      "SELECT Id, ParentPart, ComponentPart, QtyAssembly, Uom, CreatedBy
       FROM ManufacturingStructures WHERE CreatedBy IN ($BOM_SEED_TAGS)
       ORDER BY Id;"
    echo ""
fi
if [[ "$IPQC_QA_USER_COUNT" != "0" ]]; then
    echo "Seeded IPQC/QA checkpoint test users (Username, Role, DisplayName):"
    sqlite3 -column -header "$DB_PATH" \
      "SELECT Id, Username, Role, DisplayName
       FROM Users WHERE Username IN ($IPQC_QA_TEST_USERS)
       ORDER BY Id;"
    echo ""
fi
if [[ "$IPQC_QA_AUDIT_COUNT" != "0" ]]; then
    echo "WO_IPQC_*/WO_QA_* test rows (most recent 8):"
    sqlite3 -column -header "$DB_PATH" \
      "SELECT Id, datetime(Timestamp) AS T, Action, ActorUsername AS Actor,
              substr(Detail, 1, 60) AS Detail60
       FROM AuditLogs
       WHERE Action IN ('WO_IPQC_CHECK','WO_IPQC_JUDGMENT','WO_QA_APPROVE','WO_QA_APPROVE_DENIED')
         AND ActorUsername IN ($IPQC_QA_TEST_USERS)
       ORDER BY Id DESC LIMIT 8;"
    echo ""
fi

if [[ "$FQC_OQC_AUDIT_COUNT" != "0" ]]; then
    echo "WO_FQC_*/WO_OQC_*/WO_SHIPPED test rows (most recent 10):"
    sqlite3 -column -header "$DB_PATH" \
      "SELECT Id, datetime(Timestamp) AS T, Action, ActorUsername AS Actor,
              substr(Detail, 1, 50) AS Detail50
       FROM AuditLogs
       WHERE Action IN ($FQC_OQC_AUDIT_ACTIONS) AND ActorUsername IN ($OQC_TEST_USERS)
       ORDER BY Id DESC LIMIT 10;"
    echo ""
fi
if [[ "$QC_CHECK_COUNT" != "0" ]]; then
    echo "WoQcChecks (7e signature columns reference a test user):"
    sqlite3 -column -header "$DB_PATH" \
      "SELECT Id, WorkOrderId, QcKind, Judgment, InspectedBy, ReviewedBy, ApprovedBy
       FROM WoQcChecks WHERE $QC_CHECK_PRED
       ORDER BY Id;"
    echo "  (+ $QC_ITEM_COUNT child WoQcCheckItems + $QC_PHOTO_COUNT WoQcPhotos cascade)"
    echo ""
fi
if [[ "$OQC_USER_COUNT" != "0" ]]; then
    echo "Seeded OQC checkpoint test users (Username, Role, DisplayName):"
    sqlite3 -column -header "$DB_PATH" \
      "SELECT Id, Username, Role, DisplayName
       FROM Users WHERE Username IN ($OQC_TEST_USERS)
       ORDER BY Id;"
    echo ""
fi

if [[ $COMMIT -eq 0 ]]; then
    echo "── DRY-RUN — no rows written. Re-run with --commit to execute. ──"
    exit 0
fi

# COMMIT path
# P10.7c-4 — domain rows deleted in dependency order (children first):
# WoQtyEntries (FK → WoRunSessions) → WoPauseEvents (FK → WoRunSessions)
# → WoRunSessions. Audit rows are independent and can purge in any order.
echo "── Executing purge transaction ──"
sqlite3 "$DB_PATH" <<SQL
BEGIN;
DELETE FROM AuditLogs WHERE Action='TEST_RESET' AND ActorUsername='test-tool';
DELETE FROM AuditLogs WHERE Action='SYS_RECOVERY' AND $PATTERN_NOISE_LIKE;
DELETE FROM AuditLogs WHERE Action IN ('WO_PREPRESS_MATERIAL_SET','WO_PREPRESS_PLATE_SET','WO_PREPRESS_CUTTER_SET') AND $PATTERN_PREPRESS_NOISE_LIKE;
DELETE FROM AuditLogs WHERE Action IN ('WO_SETTING_START','WO_SETTING_DONE','WO_RUN_START','WO_RUN_QTY_ADD','WO_RUN_QTY_CORRECT','WO_RUN_PAUSE','WO_RUN_RESUME','WO_RUN_FINISH','WO_STATE_CONFLICT') AND $PATTERN_RUNNING_NOISE_LIKE;
DELETE FROM AuditLogs WHERE Action IN ('WO_IPQC_CHECK','WO_IPQC_JUDGMENT','WO_QA_APPROVE','WO_QA_APPROVE_DENIED') AND ActorUsername IN ($IPQC_QA_TEST_USERS);
DELETE FROM AuditLogs WHERE Action IN ($FQC_OQC_AUDIT_ACTIONS) AND ActorUsername IN ($OQC_TEST_USERS);
DELETE FROM WoQtyEntries WHERE EnteredBy IN ($RUNNING_ACTOR_TAGS);
DELETE FROM WoPauseEvents WHERE StartedBy IN ($RUNNING_ACTOR_TAGS);
DELETE FROM WoRunSessions WHERE StartedBy IN ($RUNNING_ACTOR_TAGS);
DELETE FROM WoQcPhotos WHERE WoQcCheckItemId IN (SELECT Id FROM WoQcCheckItems WHERE WoQcCheckId IN (SELECT Id FROM WoQcChecks WHERE $QC_CHECK_PRED));
DELETE FROM WoQcCheckItems WHERE WoQcCheckId IN (SELECT Id FROM WoQcChecks WHERE $QC_CHECK_PRED);
DELETE FROM WoQcChecks WHERE $QC_CHECK_PRED;
DELETE FROM ManufacturingStructures WHERE CreatedBy IN ($BOM_SEED_TAGS);
DELETE FROM Users WHERE Username IN ($IPQC_QA_TEST_USERS);
DELETE FROM Users WHERE Username IN ($OQC_TEST_USERS);
COMMIT;
SQL
PURGE_EXIT=$?

if [[ $PURGE_EXIT -ne 0 ]]; then
    echo "❌ Purge transaction FAILED (sqlite3 exit=$PURGE_EXIT)."
    exit 1
fi

TOTAL_AFTER=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM AuditLogs;")
BOM_SEED_AFTER=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM ManufacturingStructures WHERE CreatedBy IN ($BOM_SEED_TAGS);")
QTY_ENTRY_AFTER=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoQtyEntries WHERE EnteredBy IN ($RUNNING_ACTOR_TAGS);")
PAUSE_EVENT_AFTER=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoPauseEvents WHERE StartedBy IN ($RUNNING_ACTOR_TAGS);")
RUN_SESSION_AFTER=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoRunSessions WHERE StartedBy IN ($RUNNING_ACTOR_TAGS);")
IPQC_QA_USER_AFTER=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM Users WHERE Username IN ($IPQC_QA_TEST_USERS);")
OQC_USER_AFTER=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM Users WHERE Username IN ($OQC_TEST_USERS);")
QC_CHECK_AFTER=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoQcChecks WHERE $QC_CHECK_PRED;")
QC_ITEM_AFTER=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoQcCheckItems WHERE WoQcCheckId IN (SELECT Id FROM WoQcChecks WHERE $QC_CHECK_PRED);")
QC_PHOTO_AFTER=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM WoQcPhotos WHERE WoQcCheckItemId IN (SELECT Id FROM WoQcCheckItems WHERE WoQcCheckId IN (SELECT Id FROM WoQcChecks WHERE $QC_CHECK_PRED));")
AUDIT_DELETED=$((TOTAL_BEFORE - TOTAL_AFTER))
BOM_DELETED=$((BOM_SEED_COUNT - BOM_SEED_AFTER))
USER_DELETED=$(( (IPQC_QA_USER_COUNT - IPQC_QA_USER_AFTER) + (OQC_USER_COUNT - OQC_USER_AFTER) ))
DOMAIN_DELETED=$(( (QTY_ENTRY_COUNT - QTY_ENTRY_AFTER) + (PAUSE_EVENT_COUNT - PAUSE_EVENT_AFTER) + (RUN_SESSION_COUNT - RUN_SESSION_AFTER) + (QC_PHOTO_COUNT - QC_PHOTO_AFTER) + (QC_ITEM_COUNT - QC_ITEM_AFTER) + (QC_CHECK_COUNT - QC_CHECK_AFTER) ))
USER_PURGE_TOTAL=$((IPQC_QA_USER_COUNT + OQC_USER_COUNT))

echo ""
echo "── Post-purge counts ──"
echo "  TOTAL AuditLogs (before)        : $TOTAL_BEFORE"
echo "  TOTAL AuditLogs (after)         : $TOTAL_AFTER"
echo "  Audit rows deleted              : $AUDIT_DELETED"
echo "  BOM seed rows (before)          : $BOM_SEED_COUNT"
echo "  BOM seed rows (after)           : $BOM_SEED_AFTER"
echo "  BOM seed rows deleted           : $BOM_DELETED"
echo "  Domain test rows deleted        : $DOMAIN_DELETED (qty/pause/session + qc check/item/photo)"
echo "  Test users deleted (IPQC/QA+OQC): $USER_DELETED"
echo ""
if [[ "$AUDIT_DELETED" == "$AUDIT_PURGE_TOTAL" && "$BOM_DELETED" == "$BOM_SEED_COUNT" && "$DOMAIN_DELETED" == "$DOMAIN_PURGE_TOTAL" && "$USER_DELETED" == "$USER_PURGE_TOTAL" ]]; then
    echo "✓ Purge complete — deleted counts match preview."
else
    echo "⚠ Deleted counts diverged from preview — investigate."
    echo "    audit  preview=$AUDIT_PURGE_TOTAL  deleted=$AUDIT_DELETED"
    echo "    bom    preview=$BOM_SEED_COUNT    deleted=$BOM_DELETED"
    echo "    domain preview=$DOMAIN_PURGE_TOTAL deleted=$DOMAIN_DELETED"
    echo "    users  preview=$USER_PURGE_TOTAL deleted=$USER_DELETED"
    exit 1
fi
