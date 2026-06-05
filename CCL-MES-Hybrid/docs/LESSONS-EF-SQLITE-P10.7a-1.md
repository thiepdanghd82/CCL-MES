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
