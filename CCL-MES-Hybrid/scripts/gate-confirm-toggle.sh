#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# L52 gate — OK/NG confirmation must go through Shared/ConfirmToggle.razor,
# NOT a hand-drawn `op-btn op-btn-success` + `op-btn op-btn-danger` pair.
#
# Symptom that was paid for: the same "xác nhận OK/NG" cluster was re-drawn on
# 6+ surfaces, each with its own label / spacing / touch-size. One shared
# segmented toggle fixes label, colour token, tap-target and a11y in ONE place.
#
# Detection (RATCHET, baseline 0): a `class="op-btn op-btn-success …"` button
# that has a `class="op-btn op-btn-danger …"` sibling within a short window and
# where NEITHER carries `ipqc-judgment-btn`. That is exactly the OK/NG toggle
# shape. Judgment rows (Go Run / Stop Line / Pass / Reject) tag their buttons
# `ipqc-judgment-btn` and are a DIFFERENT semantic (phase decision, not per-item
# OK/NG) — they are excluded on purpose. Lone success buttons (Save, Edit) and
# severity→class mappers (SpecConfirmActionModal) never form a pair, so they do
# not count either.
#
# See .claude/skills/cmes-confirm-toggle/SKILL.md + LESSONS-LEARNED.md L52.
#
# Usage: bash scripts/gate-confirm-toggle.sh            (0 = pass, 1 = fail)
#        bash scripts/gate-confirm-toggle.sh --self-test
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

# Baseline = number of bare OK/NG success+danger clusters left in Shared/*.razor.
# Target is 0: every OK/NG confirmation is a <ConfirmToggle>. Do NOT bump this
# without a STOP-gate reason in the PR body.
BASELINE=0
WINDOW=15   # lines an OK button may sit above its NG sibling in one action bar

here="$(cd "$(dirname "$0")" && pwd)"
RAZOR="$here/../src/CCL.MES.Hybrid.Razor/Shared"
[ -d "$RAZOR" ] || { echo "[gate:confirm-toggle] không thấy $RAZOR"; exit 2; }

count_clusters() {
  python3 - "$1" "$WINDOW" <<'PY'
import re,sys,glob,os
root,window = sys.argv[1],int(sys.argv[2])
SUCCESS = re.compile(r'class="op-btn op-btn-success')
DANGER  = re.compile(r'class="op-btn op-btn-danger')
JUDGE   = 'ipqc-judgment-btn'
hits=[]
for f in glob.glob(os.path.join(root,'*.razor')):
    lines=open(f,encoding='utf-8').read().splitlines()
    for i,ln in enumerate(lines):
        if SUCCESS.search(ln) and JUDGE not in ln:
            for j in range(i+1, min(i+1+window, len(lines))):
                if DANGER.search(lines[j]) and JUDGE not in lines[j]:
                    hits.append(f"{os.path.basename(f)}:{i+1}")
                    break
for h in hits: print(h)
print(f"__COUNT__ {len(hits)}")
PY
}

# ── self-test: prove the detector still catches an injected bare cluster ──────
if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  cat > "$tmp/GateSelftest.razor" <<'RZ'
<div class="ipqc-slot-actions">
    <button type="button" class="op-btn op-btn-success ipqc-slot-btn">OK</button>
    <button type="button" class="op-btn op-btn-danger ipqc-slot-btn">NG</button>
</div>
RZ
  n="$(count_clusters "$tmp" | sed -n 's/^__COUNT__ //p')"
  [ "$n" -ge 1 ] \
    && { echo "[gate:confirm-toggle] self-test OK (bare OK/NG cluster bị bắt: n=$n)"; exit 0; } \
    || { echo "[gate:confirm-toggle] self-test FAILED — detector không bắt được"; exit 1; }
fi

out="$(count_clusters "$RAZOR")"
count="$(echo "$out" | sed -n 's/^__COUNT__ //p')"
locs="$(echo "$out" | grep -v '^__COUNT__' || true)"

echo "[gate:confirm-toggle] cụm OK/NG trần (op-btn-success+danger) = $count (baseline $BASELINE)"
if [ "$count" -gt "$BASELINE" ]; then
  echo "[gate:confirm-toggle:FAIL] có cụm OK/NG vẽ tay mới:"
  echo "$locs" | sed 's/^/    /'
  echo "  Dùng <ConfirmToggle Status=… OnOk=… OnNg=… /> thay vì op-btn-success + op-btn-danger."
  echo "  Xem .claude/skills/cmes-confirm-toggle/SKILL.md."
  exit 1
fi
echo "[gate:confirm-toggle:OK] mọi xác nhận OK/NG đi qua ConfirmToggle."
exit 0
