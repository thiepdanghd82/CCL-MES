# Spec sample bundle — Phase 8 PR #31a

4 silkscreen xlsx files used by the "Refresh samples" Admin button on the
**NPI · Engineer Spec** page. They are also the test fixtures for the
silkscreen xlsx parser regression tests.

## Sanitization manifest

All four files are SANITIZED derivatives of real CCL Vietnam spec sheets
held in the SpecHub design prototype at
`/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/SpecHub/Data/Specs/`
(READ-ONLY reference; never modified by this repository).

The following fields were REPLACED with fully-synthetic demo values before
commit, so no real customer-identifying information ships in the repo:

| Field | Replaced with |
|---|---|
| Customer name | `DEMO_CUSTOMER_1`, `_2`, `_3` |
| Part No (customer SKU / SAP code) | `DEMO-PN-001`, `DEMO-PN-002`, `DEMO-DT-001`, `DEMO-JN-001` |
| Part Name (product descriptor) | `Demo Panel B`, `Demo Decal A`, `Demo Console A` |
| Filename | `DEMO_SILK_<n>.xlsx` |

The following fields are PRESERVED because they are CCL-internal (not
customer-identifying) and need to stay realistic for parser regression
testing:

- `RefNo` (CCL spec catalog serial e.g. `CCL-Silk-19235`)
- Plate codes (`SP1620-1`, `SP2387-5`, …) — internal CCL plate inventory
- Ink codes (`HI1160`, `VI20`, `MI3`, …) — internal CCL chemistry catalog
- Maker / Brand names (`CCL MIX`, `SEIKO`, `TEIKOKU`, `3M`) — industry vendors
- Inspection level, mesh count, angle, viscosity, dry temp — pure engineering data

## Files

| File | Customer | Print rows | Notes |
|---|---|---|---|
| `DEMO_SILK_1.xlsx` | `DEMO_CUSTOMER_1` | 9 colors | derived from `AWW0146C98C0-WC0.xlsx` (Panasonic Panel Face B) |
| `DEMO_SILK_2.xlsx` | `DEMO_CUSTOMER_1` | 10 colors | derived from `AWW0146C6FC0-0C5.xlsx` (Panasonic Panel Face B variant) |
| `DEMO_SILK_3.xlsx` | `DEMO_CUSTOMER_2` | 7 colors | derived from `3205884802.xlsx` (DELTA decal) |
| `DEMO_SILK_4.xlsx` | `DEMO_CUSTOMER_3` | 6 colors | derived from `Silk_1000527330.xlsx` (Johnson console window) |

## Refresh-samples behavior

- Default (idempotent): skip files whose RefNo already exists; do not
  duplicate.
- `?force=1`: soft-trash existing rev with matching RefNo + create new rev
  from the bundled file. Used by Admin during dev to re-test the parser
  after spec model updates.

## Re-sanitizing

If a new sample is added to `SpecHub/Data/Specs/` and needs to be bundled
here, follow the procedure in `docs/PHASE8-PR31-PLAN.md` §7 (sanitize via
ClosedXML edit script + verify no PII leak via parser round-trip check).
