# WO-Stuck Recovery Runbook

> **When this applies**: a Work Order is wedged at an intermediate
> phase (`SETTING`, `IPQC_WAIT`, `RUNNING`, etc.), no operator can
> advance it, and the standard UI offers no escape. The contract
> §8 admin endpoints (`/admin-force-phase`) are still pending —
> they land in P10.7a-2. Until then, the SQL fallback below is the
> only recovery path.
>
> **Authority**: contract §8.1 console-only recovery. Run as the
> deploy user (the file is mode 0600 — anyone with shell access
> who can read it is implicitly trusted to mutate it).

---

## 0. Before you touch anything

```bash
# 1. Snapshot the DB.
TS=$(date -u +%Y%m%dT%H%M%SZ)
cp data/ccl_mes.db data/ccl_mes.db.before-recovery-$TS
echo "Snapshot: data/ccl_mes.db.before-recovery-$TS"

# 2. Confirm the WO state.
sqlite3 data/ccl_mes.db \
  "SELECT Id, WoNo, CurrentStep, MesPhase, MaterialsReady, SetupConfirmed,
          RohsOk, ProducedQty, hex(RowVersion), UpdatedAt
   FROM WorkOrders WHERE WoNo = '<WO_NUMBER>';"
```

If the WO doesn't exist in the snapshot, **stop** — you're on the
wrong DB file. Confirm `data/ccl_mes.db` is the path the server
actually runs against (`grep ConnectionStrings__Default` in your
launch env or process env).

---

## 1. Test/dev environment — use the helper scripts

The 3 helpers ship with the P10.7a-1.3 PR + are idempotent:

### Reset a single WO to a specific phase

```bash
bash CCL-MES-Hybrid/scripts/reset-test-wo.sh <WO_NUMBER> [phase]
```

`phase` defaults to `PrePressCheck`. Valid values:
`PrePressCheck | OpSetting | IpqcApproval | ReadyToRun | Running |
Fqc | Oqc | Closed`.

What it does:
- `UPDATE WorkOrders SET CurrentStep = …, MesPhase = …,
   MaterialsReady = …, SetupConfirmed = …, RohsOk = …,
   ProducedQty = …` with a flag bundle appropriate for the target
   phase (e.g. `ReadyToRun` sets `SetupConfirmed = 1` so the
   PREPRESS → SETTING gate passes when the operator next taps Accept).
- The SQLite trigger bumps `RowVersion` → the operator's Catalyst
  cache becomes stale on next interaction, forcing a re-scan
  (mimics post-deploy semantics).
- Emits `WoStatusHistory[Action='TestReset']` + `AuditLogs[Action=
  'TEST_RESET']` with the from/to phases + reason so the forensic
  trail survives.

### Force an "another operator advanced this WO" race

```bash
bash CCL-MES-Hybrid/scripts/make-stale.sh <WO_NUMBER>
```

Logs in as admin/admin, fetches Summary, posts Advance with valid
headers. Confirms the new ETag differs from the old. Prints VN
instructions for the operator's next action.

### Seed extra test WOs

```bash
bash CCL-MES-Hybrid/scripts/seed-test-wos.sh [--template WO] [--count N]
```

Default: clones `WO-26-3683` × 4 → `WO-26-3684 .. WO-26-3687`.
Idempotent — re-running skips existing WoNo. Emits
`AuditLogs[Action='TEST_SEED']` per row.

---

## 2. Prod environment — manual SQL (`/admin-force-phase` lands P10.7a-2)

Until the admin endpoint ships, the manual path is:

```bash
# Replace WO_NO, USER_ID, TARGET_PHASE.
WO_NO="WO-26-1234"
USER_ID="0"   # 0 = system; replace with the sys-user PK if known
TARGET_PHASE="PrePressCheck"

NOW_UTC=$(date -u +'%Y-%m-%dT%H:%M:%SZ')

sqlite3 data/ccl_mes.db <<SQL
BEGIN;

UPDATE WorkOrders
SET CurrentStep = '$TARGET_PHASE',
    -- Apply the canonical phase via the legacy → canonical map
    -- (see migration 20260605045839 backfill SQL for the table).
    MesPhase = CASE '$TARGET_PHASE'
        WHEN 'PrePressCheck' THEN 'PREPRESS'
        WHEN 'OpSetting'     THEN 'SETTING'
        WHEN 'IpqcApproval'  THEN 'IPQC_WAIT'
        WHEN 'ReadyToRun'    THEN 'IPQC_APPROVED'
        WHEN 'Running'       THEN 'RUNNING'
        WHEN 'Fqc'           THEN 'FQC_PENDING'
        WHEN 'Oqc'           THEN 'OQC_PENDING'
        WHEN 'Closed'        THEN 'DONE'
        ELSE                      'PREPRESS'
    END,
    UpdatedAt = '$NOW_UTC'
WHERE WoNo = '$WO_NO';

-- Status history row so the forensic trail names the human reason.
INSERT INTO WoStatusHistories
    (CreatedAt, CreatedBy, WorkOrderId, FromStep, ToStep, Action, ByUser, Reason)
SELECT
    '$NOW_UTC', 'sys-recovery', Id,
    (SELECT CurrentStep FROM WorkOrders WHERE WoNo = '$WO_NO'),
    '$TARGET_PHASE',
    'SysRecovery',
    'sys-recovery',
    'Manual SQL recovery — operator-reported wedge at <previous step>; ' ||
    'P10.7a-2 /admin-force-phase endpoint not yet shipped.'
FROM WorkOrders WHERE WoNo = '$WO_NO';

-- Audit log row with the canonical SYS_RECOVERY action so the
-- AuditLog viewer (P10.6e) surfaces it.
INSERT INTO AuditLogs
    (Timestamp, ActorUsername, ActorRole, Action, TargetType, TargetId,
     Detail, Source)
SELECT
    '$NOW_UTC', 'sys-recovery', 'sys', 'SYS_RECOVERY', 'WorkOrder', Id,
    json_object(
        'wo_id', Id,
        'wo_no', '$WO_NO',
        'to_phase', '$TARGET_PHASE',
        'origin', 'docs/runbooks/wo-stuck-recovery.md',
        'method', 'manual-sql'
    ),
    'Console'
FROM WorkOrders WHERE WoNo = '$WO_NO';

COMMIT;
SQL
```

The `UPDATE` triggers the SQLite `WorkOrders_RowVersion_OnUpdate`
trigger, bumping `RowVersion`. The operator's Catalyst cache becomes
stale → forced re-scan → fresh ETag picked up on the next tap.

---

## 3. Verification (mandatory)

```bash
# Confirm the WO landed where you expected.
sqlite3 data/ccl_mes.db \
  "SELECT WoNo, CurrentStep, MesPhase, hex(RowVersion) FROM WorkOrders
   WHERE WoNo = '$WO_NO';"

# Confirm the audit row was written.
sqlite3 data/ccl_mes.db \
  "SELECT Timestamp, Action, Detail FROM AuditLogs
   WHERE TargetType = 'WorkOrder'
     AND TargetId = (SELECT Id FROM WorkOrders WHERE WoNo = '$WO_NO')
   ORDER BY Id DESC LIMIT 3;"

# Tell the operator to re-scan the WO. The cached ETag is stale; the
# next tap returns 409 → adopts the new ETag → next tap goes through.
echo "Operator: please re-scan $WO_NO and try Accept again."
```

If the audit row is missing, the transaction rolled back silently —
look at the sqlite3 stderr for the failure (commonly a trigger
firing on a stale tracked row from a different connection).

---

## 4. When the situation is worse than "wedged WO"

| Symptom | Likely cause | Where to look |
|---|---|---|
| `HTTP 500 · http.non_success` on login | DB lags pending migrations | `[boot]` line in server log — P10.7a-1.4 added the warning probe |
| Migration `Down()` left orphan trigger | SQLite trigger rule (drop trigger BEFORE drop column) | Migration file's `Down()` body must drop triggers first; see `AddWorkOrderRowVersionAndMesPhase` for the template |
| All operators get 409 forever | A previous SQL recovery skipped the trigger fire (e.g. `INSERT OR REPLACE` instead of `UPDATE`) | Confirm `hex(RowVersion)` differs row-by-row before + after your SQL; if not, run `UPDATE WorkOrders SET RowVersion = randomblob(8) WHERE WoNo = …` explicitly |
| `IdempotencyKeys` table missing | DB at 7a-1.1 baseline (before 7a-1.2 migration) | `dotnet ef database update --connection …` per `STACKED-PR-CHECKLIST.md` Rule 5 |

---

## 5. After recovery — file the incident

Drop a 5-line note in the team channel with: WO number, original
wedge phase, target phase, the `SYS_RECOVERY` audit `Id`, and what
made you choose that target. Future "why did this WO go backwards?"
questions have a one-row answer.

If the wedge was caused by a software bug (the operator did
nothing weird, the WO just locked itself), file an issue with a
link to the SYS_RECOVERY audit row + the server log slice around
the wedge moment. That's how P10.7a-2 (the proper recovery
endpoint) gets prioritised correctly.
