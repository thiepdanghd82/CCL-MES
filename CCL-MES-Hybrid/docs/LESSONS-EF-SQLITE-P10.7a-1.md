# Lessons learned — EF Core + SQLite optimistic concurrency (P10.7a-1)

Captured at the close of the P10.7a-1 4-PR stack (2026-06-05).
Each lesson is paired with the symptom that surfaced it + the
durable fix already shipped in the stack.

---

## 1. SQLite trigger fires AFTER `UPDATE` — EF reads the pre-trigger value

### Symptom
PR 7a-1.3 wire probes returned a `200` with an `ETag` HTTP header
**identical** to the request's `If-Match`. The xUnit `Advance_with
_valid_IfMatch_returns_200_with_new_ETag_in_body_and_header`
failed `Assert.NotEqual(oldEtag, body.ETag)` with
`Actual: "ctIqhIj/ME4="` on both sides.

### Cause
EF Core 10 with the SQLite provider emits
`UPDATE … WHERE Id = @id AND RowVersion = @old RETURNING RowVersion`.
The `RETURNING` clause reads the column AFTER the UPDATE
statement executes but BEFORE row triggers fire (SQLite's
per-row trigger semantics). So EF gets back the value the
application set (which equals `@old` because nothing else
changed), NOT the trigger-bumped value.

### Fix
Re-read via `AsNoTracking + Select(w => w.RowVersion)` AFTER
`SaveChangesAsync`:

```csharp
await _svc.AdvanceAsync(id, actor);
var freshRowVersion = await _db.WorkOrders
    .Where(w => w.Id == id)
    .AsNoTracking()
    .Select(w => w.RowVersion)
    .SingleOrDefaultAsync();
var newEtagRaw = Convert.ToBase64String(freshRowVersion);
```

The `AsNoTracking` is what makes this work — without it, EF's
change tracker returns the same tracked instance from cache and
the stale value sticks around. The `Select` projection avoids
materialising a full WorkOrder entity for one byte[] field.

### Apply to
Any future endpoint that mutates `WorkOrder` (or any other
trigger-versioned entity) AND surfaces the new RowVersion to the
client. Same pattern for `WoStatusHistory`, `RunSession`, etc.,
once 7a-2+ ship them.

---

## 2. `DbUpdateConcurrencyException` poisons the change tracker for downstream middleware

### Symptom
The N=50 soak test threw the EF concurrency exception PAST the
controller's `try/catch` — the `IdempotencyMiddleware`'s downstream
`SaveChangesAsync` (writing the response envelope row) re-tried
the failed UPDATE with the SAME tracked entity + the SAME stale
RowVersion, throwing again. TestServer surfaces the second throw
as an unhandled exception through `client.SendAsync`.

### Cause
After `SaveChanges` throws `DbUpdateConcurrencyException`, the EF
DbContext STILL holds the failed WorkOrder (and any queued
`WoStatusHistory` row) in `EntityState.Modified` / `Added`. The
change tracker doesn't auto-detach on exception — the assumption
is that the calling code will call `Reload()` + retry. Per-request
scoped DbContext shared between the controller + the middleware
means the middleware's subsequent `SaveChanges` re-attempts the
failed entry.

### Fix
In the controller's `catch (DbUpdateConcurrencyException)`:

```csharp
catch (DbUpdateConcurrencyException)
{
    if (_db is Microsoft.EntityFrameworkCore.DbContext dbCtx)
        dbCtx.ChangeTracker.Clear();

    // … emit WO_STATE_CONFLICT audit + return 409.
}
```

`ChangeTracker.Clear()` detaches every tracked entity in one call.
Downstream `SaveChanges` (the middleware writing the response
envelope) starts from an empty change set — no re-attempt of the
failed WorkOrder update.

### Apply to
Any controller that calls `SaveChanges` AND is wrapped by middleware
that ALSO calls `SaveChanges` (idempotency middleware, audit
middleware, etc.). Anywhere two SaveChanges sites share a DbContext
scope across a try/catch boundary.

---

## 3. EF `[Timestamp]` doesn't auto-populate `byte[] RowVersion` on INSERT under SQLite

### Symptom
PR 7a-1.3 bUnit tests + first wire probes for `Summary` returned
**empty** `eTag` strings for freshly-seeded WOs. The wire log
showed `eTag: ""`; `If-Match: ""` on the next advance → 428.

### Cause
The PR 7a-1.1 migration added the SQLite trigger
`WorkOrders_RowVersion_OnUpdate` that bumps `RowVersion` to a
fresh `randomblob(8)` AFTER every UPDATE. It did NOT add an
INSERT trigger. EF Core's `[Timestamp]` semantics rely on SQL
Server auto-populating the column at INSERT time; SQLite has no
equivalent, so newly-inserted rows kept the EF default of an
empty `byte[]`.

### Fix
PR 7a-1.3 added migration `AddWorkOrderRowVersionInsertTrigger`
with both a backfill SQL + an `INSERT` trigger:

```sql
CREATE TRIGGER IF NOT EXISTS WorkOrders_RowVersion_OnInsert
AFTER INSERT ON WorkOrders
FOR EACH ROW
WHEN length(NEW.RowVersion) = 0
BEGIN
    UPDATE WorkOrders
    SET RowVersion = randomblob(8)
    WHERE rowid = NEW.rowid;
END;
```

The `WHEN length(NEW.RowVersion) = 0` guard skips inserts that
already populated the column (test fixtures, replication targets).

### Apply to
Every new entity in this codebase that uses EF Core `[Timestamp]`
on a `byte[]` RowVersion. Ship the matching UPDATE + INSERT
triggers together in the same migration — don't repeat the
"2 PRs to make it work" cycle we shipped here.

---

---

## 4. Wire-path drift (audit endpoint URL + filter param mismatch)

### Symptom
P10.7a-2.2 Catalyst checkpoint on Henry's hardware (2026-06-06):
force-phase returned 200 + bumped ETag; WO state transitioned
correctly per the GET /summary follow-up. BUT step 6 reported
`audit row missing (SYS_RECOVERY=0, REC-OP-WEDGE=0); response empty`.
15 xUnit fixtures had passed. Apparent paradox: "state mutation
commits OK but audit absent."

### Cause
Reproducing on the canonical `data/ccl_mes.db` proved the
audit row WAS persisted (row 147, SYS_RECOVERY, full detail JSON
with from/to phase + reason code + sys_user_id). The bug was in
the checkpoint script's READ side of the audit log:

- **Wrong path**: script queried `/api/v2/admin/audit/log` →
  HTTP 404 (route doesn't exist). Real route is
  `/api/v2/audit/log` (AuditLogController is at `[Route(ApiVersion.Prefix + "/audit")]`
  — no `/admin` segment despite `[Authorize(Policy = "AdminOnly")]`).
- **Wrong filter params**: script sent `?targetType=WorkOrder&targetId=N`.
  Endpoint accepts `?search / action / actor / from / to / page / pageSize`
  — `targetType` + `targetId` are silently ignored.

Both layers of bug were INVISIBLE to the existing 15 xUnit fixtures
because every assertion read `_db.AuditLogs.Where(...)` directly via
the test DbContext. The wire READ path was never exercised.

### Fix
Three parts in the same PR:

1. Script URL + params corrected to
   `GET /api/v2/audit/log?action=SYS_RECOVERY&page=1&pageSize=50`,
   then grep response body for `"targetId":"<wo_id>"` +
   `REC-OP-WEDGE`.
2. New xUnit fixture
   `AdminWorkOrdersForcePhaseTests.Sys_recovery_audit_row_visible_via_wire_audit_log_endpoint`
   calls the SAME URL the checkpoint uses via TestServer, asserts
   the same substring shape (incl. the escaped `\"from_phase\":\"SETTING\"`
   form because detail is JSON-encoded inside another JSON string —
   easy to miss, the literal-quote assertion failed first time).
3. STACKED-PR-CHECKLIST Rule 7.3 mandates that every wire probe in
   an operator script has a matching integration test hitting the
   same endpoint.

### Apply to
Every future endpoint that ships with an operator-facing wire
probe (script, runbook curl, monitor URL). DbContext-only tests
are necessary but never sufficient — they prove the WRITE side
works while the READ side may have drifted (URL rename, param
rename, response-shape change, AdminOnly→Authenticate role
change, route prefix change). The wire mirror is the regression
guard.

---

## 5. Operator script vs server keep-alive DB drift

### Symptom
Same Henry checkpoint session (2026-06-06): the verify keep-alive
server (started in one terminal) and `checkpoint-7a-2.sh` (run
from another) targeted DIFFERENT SQLite files. Symptoms varied
between SQLite Error 14 ("unable to open database file") + invisible
state drift (the script reset a WO that the live server never saw).

### Cause
Operator had to coordinate `ConnectionStrings__Default` /
`ASPNETCORE_URLS` env across two terminals manually. The
checkpoint script printed no `[ctx] DB=` line at startup so the
mismatch wasn't visible until the audit grep returned empty AND
the WO read returned a different MesPhase than the script just
wrote.

### Fix
Three sub-rules added to STACKED-PR-CHECKLIST Rule 7:

- **7.1**: every script that touches a DB MUST print
  `[ctx] DB=<abs-path>` + `DB sha8=...` in its first 10 lines.
  Operator can eyeball two scripts' DB sha8 to see if they're
  pointed at the same file.
- **7.2**: every `checkpoint-*` script MUST self-manage its API
  lifecycle. Probe `$API_BASE/health`; reuse if up; else auto-boot
  the API pinned to the SAME DB the script is mutating; trap EXIT
  to kill the auto-booted process on exit. New `--keep-alive` flag
  leaves the process running for UI-verify use.
- **7.3**: every wire probe has a TestServer mirror (see lesson 4).

### Apply to
Every operator-facing script in `CCL-MES-Hybrid/scripts/` from
P10.7a-2.3 onward. Existing scripts retrofit at next touch. The
self-managed lifecycle is mandatory for `checkpoint-*` scripts;
the `[ctx] DB` print is mandatory for ANY script that opens
a DB file.

---

## Backlog item — client-side intent-key sharing

(Not a lesson; logged here so the next sprint picks it up.)

The current Razor `OnAdvance` generates a fresh `Idempotency-Key`
UUID per invocation. A fast operator double-tap = two keys =
two requests both reach the server, only one wins via the
`RowVersion` check (the loser gets 409 + the WO_STATE_CONFLICT
audit row).

The orchestrator's `Interlocked.CompareExchange` guard fixes the
"second tap reached the wire" half of the problem, BUT if a
future surface (e.g. a hardware footswitch fires `OnAdvance` twice
in 50 ms, faster than the UI re-render), the guard can race.

**Better**: cache the Idempotency-Key for `_summary.Id` on the
client and reuse it across taps until the operator either (a)
sees the success banner OR (b) explicitly resets state. Then a
genuine double-tap hits the server's idempotency replay path
instead of the RowVersion conflict path; both end up returning
the stored response with `Idempotency-Replayed: true`.

Estimated effort: 0.5 day in `AdvanceOrchestrator`. Suggested PR
slot: P10.7a-2 alongside the `/admin-force-phase` endpoint, OR
the operator-feedback amendment PR if the rest of 7a-2 is full.
