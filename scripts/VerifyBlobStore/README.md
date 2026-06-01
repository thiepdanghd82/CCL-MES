# VerifyBlobStore

Phase 8 PR-D-5a verification harness — exercises `FilesystemBlobStore`
against a temp blob root. No DB, no auth, no Razor circuit, no production
data touched.

## Run

```bash
dotnet run --project scripts/VerifyBlobStore
```

Exit code = number of failed checks. `0` on full pass.

## What it tests (8 cases)

1. **Round-trip**: put → get → SHA matches; key shape correct.
2. **Idempotency**: same content twice = same key; dedup convergence.
3. **Path traversal A**: `drawings/../../etc/passwd` rejected via regex.
4. **Path traversal B**: `drawings/1/../2/v1.pdf` rejected (regex tokens).
5. **Oversize**: stream > `MaxBytes` rejected during write (early-abort).
6. **Extension allowlist**: `.exe` rejected (not in CMES drawing kinds).
7. **Probe-resistance**: `ExistsAsync` + `GetAsync` reject bad-format keys.
8. **Delete safety**: `DeleteAsync` rejects traversal keys.

Plus a **containment audit**: scans the temp dir after the run and fails
if any file lives outside `<DataDir>/blobs/`.

## Excluded from `.sln`

Same convention as `scripts/VerifyPrB`: engineer-use tool, not a CI
runner. Building the solution root will NOT include this project; run it
explicitly with `--project` to hit it.
