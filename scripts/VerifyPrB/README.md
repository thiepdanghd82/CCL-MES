# PR-B verifier — SpecPdfDocumentBuilder dispatch harness

One-shot console runner that proves the unified `<SpecShowcard />` PR-B
template dispatch is wired correctly in the PDF builder as well.

## What it does

Walks 5 synthetic `SpecDetailDto` inputs through `PdfSpecSheetExporter`:

| # | Case | What it exercises |
|---|------|-------------------|
| 1 | SILK / silkscreen | Existing PR-A silk template (Print Params + 10-color table) |
| 2 | FLEXO / flexo | Existing PR-A flexo template (3 sub-tables) |
| 3 | GENERIC / indigo empty | New generic fallback path + warning chip + no-data paragraph |
| 4 | GENERIC / letter + silk rows | Generic fallback rendering silk-shape data via reused helper |
| 5 | GENERIC / diecut + flexo cut | Generic fallback rendering flexo-cut data via reused helper |

Pass = PDF byte array non-empty + no exception. Outputs written to
`/tmp/pr-b-verify/*.pdf` for visual sanity (`open <file>.pdf` on macOS).

## Run

```bash
dotnet run --project scripts/VerifyPrB
```

Exits non-zero if any case fails.

## Scope

- NOT a CI test — manual engineer tool only.
- NOT registered in `CCL.MES.sln` so `dotnet build` at repo root ignores it.
- Synthetic DTOs only; does NOT touch the SQLite database, auth, or the
  Razor circuit. For the Razor showcard render verify you still need to
  log in, import the 6 silk + flexo samples, and visit
  `/npi/engineer-spec/{id}` per the user's hardware-test recipe.
