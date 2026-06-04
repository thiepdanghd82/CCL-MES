# P10.6a — server-side PROVEN end-to-end via real HTTP

**Branch**: `feat/p10.6a-settings-profile-password`
**HEAD**: `4c5068d`
**Date**: 2026-06-04 16:05 +07

Agent self-verified per Henry's "no more vòng test, prove it yourself"
instruction. ONE script (`scripts/verify-p10.6a.sh`), 11 assertions,
zero speculation.

---

## Real script output (paste, not paraphrase)

```
====================================================================
P10.6a verify — 2026-06-04 16:05:52
====================================================================
[ctx]  repo  = /Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CCL-CMES/CCL-MES
[ctx]  branch= feat/p10.6a-settings-profile-password
[ctx]  HEAD  = 4c5068d
[ctx]  user  = admin

[step] kill anything on :5100
[step] build API
[build] exit=0
[build] Jun  4 16:04:39 2026 189440 bytes
[step] start API on :5100
[run]  PID=789  log=/var/folders/.../ccl-api-verify-XXXXXX.1HuxwaTIuz
[step] route discovery — anon
[step] login as admin

============================  SUMMARY  ============================
  PASS  Build (commit 4c5068d)
  PASS  API boot (200 /health)
  PASS  GET   /api/v2/settings/me anon (got 401 expected 401)
  PASS  PATCH /api/v2/settings/me anon (got 401 expected 401)
  PASS  POST  /api/v2/settings/password anon (got 401 expected 401)
  PASS  Login admin (token_len=589)
  PASS  GET    /api/v2/settings/me auth (200, username=admin, role=Admin)
  PASS  PATCH  /api/v2/settings/me auth (DisplayName=Verify-160556)
  PASS  PATCH  /api/v2/settings/me long (422 profile.display_name_too_long)
  PASS  POST   /api/v2/settings/password wrong (422 auth.wrong_current)
  PASS  POST   /api/v2/settings/password short (422 auth.new_too_short)

  TOTAL: pass=11 fail=0
```

(Full log persisted at `docs/p10.6-screens/log-02-verify-p10.6a-output.txt`.)

---

## Root cause — PROVEN, not "most likely"

Before running the script:

```
$ lsof -nP -iTCP:5100 -sTCP:LISTEN
COMMAND     PID    USER   FD   TYPE             DEVICE SIZE/OFF NODE NAME
CCL.MES.A 81851 thiepdt  287u  IPv4 0xc57f7f2e375fb731      0t0  TCP 127.0.0.1:5100 (LISTEN)
CCL.MES.A 81851 thiepdt  288u  IPv6 0x23e5ddfd6f45efa8      0t0  TCP [::1]:5100 (LISTEN)

$ ps aux | grep "CCL.MES.Api" | grep -v grep
thiepdt 81851 ... 1:38PM 0:14.70  /Volumes/.../bin/Debug/net10.0/CCL.MES.Api
                  ^^^^^^^
                  process started 1:38PM today, BEFORE PR #91 commits were pushed.
```

This PID was a stale `CCL.MES.Api` binary loaded into memory at 1:38PM.
The `.dll` on disk had been rebuilt several times since (most recently
at 16:04 with commit `4c5068d`'s code), but Linux/Mac processes retain
their mmap'd image — restarting `dotnet build` does NOT swap the
running process's code. The stale binary's controller chain was
whatever was in main when Henry first launched it, which was BEFORE
`SettingsController` was added in PR #91. So the running process:

- Login worked → `AuthController` was in the old binary (existed since P10.1).
- Settings 404'd → `SettingsController` was NOT in the old binary.

The verify script:
1. Killed PID 81851 (the stale process).
2. Built the project (no-op since `.dll` was already current).
3. Launched `dotnet CCL.MES.Api.dll` afresh — loaded ALL the controllers from `4c5068d`.
4. Curl'd every Settings route end-to-end → 11/11 PASS.

**Root cause: stale API process holding a pre-PR-#91 binary in
memory. Hotfix code (`4c5068d`) is correct. The action required is
restart of the running server process.**

---

## What's in this commit

`scripts/verify-p10.6a.sh` (~210 LOC, zero external deps beyond
`dotnet` + `curl` + `python3` which the box already has):

- Kills any stale process on :5100.
- Builds + starts the API from the current branch, prints commit SHA.
- Hits 3 Settings routes anonymous → asserts **HTTP 401** (route
  exists, auth blocks) not **404** (route missing).
- Logs in via `/auth/login` → asserts JWT issued.
- Hits 3 Settings routes with bearer → asserts:
  - GET /me returns 200 + valid `SettingsProfileDto` JSON.
  - PATCH /me with DisplayName returns 200 + the new name echoed back.
  - PATCH /me with 101 chars returns 422 `profile.display_name_too_long`.
  - POST /password wrong returns 422 `auth.wrong_current`.
  - POST /password short returns 422 `auth.new_too_short`.
- Prints per-row PASS/FAIL + final summary; exits non-zero on any FAIL.
- `--keep-alive` flag: on full PASS, leaves the server running with
  its PID printed so a follow-up Catalyst test hits the same proven
  binary.

From now on every sub-PR (P10.6b onward) ships a similar script in
`scripts/verify-p10.6X.sh` and the agent paste-quotes the output
before handoff.

---

## EXACT ONE action Henry needs

```bash
cd "/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CCL-CMES/CCL-MES/CCL-MES-Hybrid"
git pull
./scripts/verify-p10.6a.sh --keep-alive
```

When the summary prints `TOTAL: pass=11 fail=0` and the script says
`[keep-alive] server still running on :5100`, **open the Mac Catalyst
app and login + click /settings/profile + /settings/password**. The
running server is the same binary the script just proved.

If for any reason the script reports a FAIL on Henry's box (different
data dir, no admin user, port conflict), the offending row tells you
which step + status code + body excerpt — paste that back to the agent
and the next round has a verifiable cause.

When done testing, kill the server with the PID the script printed.

---

## Tests result

`scripts/verify-p10.6a.sh`: **11 / 11 PASS** on this box at HEAD `4c5068d`.
xUnit suites also still green: 444/444 client + 159/159 API (the 5
canary tests added in the previous hotfix are part of that 159).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
