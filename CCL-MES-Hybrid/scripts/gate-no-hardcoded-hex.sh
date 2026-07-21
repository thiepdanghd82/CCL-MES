#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# L37 gate — new colours in app.css MUST go through a design token, not raw hex.
#
# Ratchet: count raw hex literals in RULE USAGES — i.e. every hex EXCEPT those on
# a custom-property DEFINITION line (`--x: #hex`). Token defs are the allowed home
# for hex, whether at :root OR in a scoped re-scope block (e.g. `.app-nav { --navy:
# #12203a }` — the dark chrome). If the usage count exceeds the baseline, a PR put
# a hardcoded colour in a normal rule instead of routing it to a --token → fail.
#
# Legit additions (a genuine one-off colour used directly in a rule) should bump
# BASELINE in the same PR with a one-line justification — a conscious, reviewed
# choice. Adding/altering a token DEFINITION never trips the gate.
#
# Tested: PASS on the current tree; FAIL when a raw hex is added to a normal rule
# (see the self-test, run with --self-test).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

BASELINE=35

here="$(cd "$(dirname "$0")" && pwd)"
CSS="$here/../src/CCL.MES.Hybrid.Razor/wwwroot/css/app.css"
[ -f "$CSS" ] || { echo "[gate:hex] app.css not found at $CSS"; exit 2; }

count_usage_hex() {
  python3 - "$1" <<'PY'
import re,sys
n=0
for ln in open(sys.argv[1],encoding='utf-8'):
    if re.match(r'\s*--[A-Za-z0-9-]+\s*:', ln):   # a custom-property DEFINITION line → allowed
        continue
    n += len(re.findall(r'#[0-9a-fA-F]{3,8}\b', ln))
print(n)
PY
}

if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp)"; trap 'rm -f "$tmp"' EXIT
  cp "$CSS" "$tmp"
  printf '\n.gate-selftest-xyz { color: #abcdef; }\n' >> "$tmp"
  before="$(count_usage_hex "$CSS")"; after="$(count_usage_hex "$tmp")"
  [ "$after" -gt "$before" ] && echo "[gate:hex] self-test OK (adding a raw hex is detected: $before -> $after)" \
    || { echo "[gate:hex] self-test FAILED — detector did not catch an added hex"; exit 1; }
  exit 0
fi

count="$(count_usage_hex "$CSS")"
echo "[gate:hex] raw hex in rule usages = $count (baseline $BASELINE)"
if [ "$count" -gt "$BASELINE" ]; then
  echo "[gate:hex:FAIL] new hardcoded hex in an app.css rule."
  echo "  Route the colour to a --token (define in :root; see L37), or bump BASELINE with a note."
  exit 1
fi
echo "[gate:hex:OK] no new hardcoded colours in rules — palette stays token-driven."
