#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# L37 gate — new colours in app.css MUST go through a design token, not raw hex.
#
# Ratchet: count raw hex literals OUTSIDE the first :root{...} block (token
# DEFINITIONS live in :root and are allowed). If the count exceeds the baseline,
# a PR added a hardcoded colour instead of routing it to a --token → fail.
#
# Legit additions (a genuinely new one-off colour, or a new scoped token def)
# should bump BASELINE in the same PR with a one-line justification. That makes
# every new raw hex a conscious, reviewed choice — which is the whole point.
#
# Tested: PASS on the current tree; FAIL when a raw hex is added to a normal
# rule outside :root (see the self-test at the bottom, run with --self-test).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

BASELINE=49

here="$(cd "$(dirname "$0")" && pwd)"
CSS="$here/../src/CCL.MES.Hybrid.Razor/wwwroot/css/app.css"
[ -f "$CSS" ] || { echo "[gate:hex] app.css not found at $CSS"; exit 2; }

count_outside_root() {
  python3 - "$1" <<'PY'
import re,sys
css=open(sys.argv[1],encoding='utf-8').read()
m=re.search(r':root\s*\{',css)
if not m:
    print(len(re.findall(r'#[0-9a-fA-F]{3,8}\b',css))); raise SystemExit
i=m.end()-1; d=0; s=i
while i<len(css):
    if css[i]=='{': d+=1
    elif css[i]=='}':
        d-=1
        if d==0: break
    i+=1
rest=css[:s]+css[i+1:]                       # everything except the :root block
print(len(re.findall(r'#[0-9a-fA-F]{3,8}\b',rest)))
PY
}

if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp)"; trap 'rm -f "$tmp"' EXIT
  cp "$CSS" "$tmp"
  printf '\n.gate-selftest-xyz { color: #abcdef; }\n' >> "$tmp"
  before="$(count_outside_root "$CSS")"; after="$(count_outside_root "$tmp")"
  [ "$after" -gt "$before" ] && echo "[gate:hex] self-test OK (adding a raw hex is detected: $before -> $after)" \
    || { echo "[gate:hex] self-test FAILED — detector did not catch an added hex"; exit 1; }
  exit 0
fi

count="$(count_outside_root "$CSS")"
echo "[gate:hex] raw hex outside :root = $count (baseline $BASELINE)"
if [ "$count" -gt "$BASELINE" ]; then
  echo "[gate:hex:FAIL] new hardcoded hex in app.css outside :root."
  echo "  Route the colour to a --token in :root (see L37), or bump BASELINE with a note."
  exit 1
fi
echo "[gate:hex:OK] no new hardcoded colours — palette stays token-driven."
