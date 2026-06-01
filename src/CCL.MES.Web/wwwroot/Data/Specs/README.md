# Spec sample bundle — Phase 8 PR #31a + #31b

4 silkscreen + 2 flexo xlsx files used by the "Refresh samples" Admin button
on the **NPI · Engineer Spec** page. They are also the test fixtures for the
xlsx parser regression tests.

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
| `DEMO_FLEXO_1.xlsx` | `DEMO_CUSTOMER_4` | 2 print / 3 cut / 5 ink | derived from `G-EHB-HC-DISNEY.xlsx` (CCL VINA seal, DISNEY brand reference scrubbed everywhere) |
| `DEMO_FLEXO_2.xlsx` | `DEMO_CUSTOMER_5` | 1 print / 1 cut / 3 ink | derived from `080-0005-1618-ZE-NP.xlsx` (FIT label) |

## Flexo-specific notes (PR #31b)

Flexo template packs THREE distinct data tables in one worksheet (printing +
cutting + ink), each with its own row count. The "Print rows / Cut rows / Ink
rows" column shows all three. Sanitization scanned EVERY cell against original
substrings (incl. `DISNEY` brand reference in `DEMO_FLEXO_1`) — verified zero
leak via parser round-trip with `Contains` assertion.

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
