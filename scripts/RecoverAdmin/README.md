# RecoverAdmin

Console-only admin recovery tool. Phase 6 Bước 4.

## When to use

- Every Admin account has been disabled / deleted / lost its password.
- An admin demoted themselves by mistake (Account UI invariants should
  prevent this, but the script is the lifeline if a future code path
  drifts).
- You need to reset an existing user back to a known-good Admin state.

## Trust model

The trust boundary is the OS user with write access to the SQLite file.
There is no web surface and no remote authentication.

On production setups:
- `chmod 600 ccl_mes.db`
- Only the deploy account should be able to run this script.

Every run writes an audit row to `recover.audit.log` (sibling of
`Program.cs`) with timestamp + OS user + machine name + action. Review
that file as part of incident response.

## Usage

From `scripts/RecoverAdmin/`:

```bash
# Reset an existing user back to Admin + active + must-change-password
dotnet run -- --reset admin --new-password 'TempPwd_1234'

# Create a fresh recovery Admin (use when --reset says "not found")
dotnet run -- --create recovery-admin --password 'TempPwd_1234'
```

Both flows prompt for the literal string `CONFIRM-RECOVER` before
mutating the DB.

## Effect on the recovered user

- `Role = Admin`
- `IsActive = true`
- `MustChangePassword = true` — the next login forces the operator
  through the password-change flow.

## Override the DB path

By default the script reads `../../src/CCL.MES.Web/ccl_mes.db`. To point
at a different DB file (e.g. a snapshot or a non-default deploy path):

```bash
MES_DB_PATH=/abs/path/to/ccl_mes.db dotnet run -- --reset admin --new-password '…'
```

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 2 | Invalid arguments / usage |
| 3 | Aborted (`CONFIRM-RECOVER` not typed) |
| 4 | DB file not found |
| 5 | `--reset`: user not found |
| 6 | `--create`: user already exists |
