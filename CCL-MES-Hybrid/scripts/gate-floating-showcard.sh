#!/usr/bin/env bash
#
# CI gate — every SHOWCARD / detail-dialog component in the Hybrid Razor UI must
# reuse the shared <FloatingWindow> chrome (drag / 8-way resize / traffic-lights
# / rect persistence) instead of hand-rolling its own window. See
# .claude/skills/cmes-floating-showcard/SKILL.md + LESSONS-LEARNED.md L34.
#
# A NEW component named *DetailDialog.razor or *Showcard*.razor that does NOT
# reference <FloatingWindow> fails the gate. Pre-existing inline "showcards"
# (spec-sheet card layouts — NOT draggable windows) are allow-listed below with
# a reason; add to ALLOW only for genuinely non-window surfaces.
#
# Usage: bash scripts/gate-floating-showcard.sh   (exit 0 = pass, 1 = fail)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RAZOR="$ROOT/src/CCL.MES.Hybrid.Razor"

# Allow-listed non-window surfaces (basename → reason):
#   FloatingWindow.razor      — IS the chrome primitive.
#   SpecShowcard{Compact,Full,Edit}.razor — inline spec-sheet CARD layouts
#     embedded in a page (not overlays / not draggable windows).
#   SpecDrawingShowcards.razor — inline collapsible drawing cards + a bespoke
#     zoom/rotate image-viewer overlay (its own controls; not a data window).
ALLOW=(
  "FloatingWindow.razor"
  "SpecShowcardCompact.razor"
  "SpecShowcardFull.razor"
  "SpecShowcardEdit.razor"
  "SpecDrawingShowcards.razor"
)

is_allowed() {
  local base="$1"
  for a in "${ALLOW[@]}"; do [ "$base" = "$a" ] && return 0; done
  return 1
}

fail=0
checked=0
while IFS= read -r f; do
  base="$(basename "$f")"
  is_allowed "$base" && continue
  checked=$((checked + 1))
  if ! grep -q "<FloatingWindow" "$f"; then
    echo "[gate:FAIL] $base is a showcard/detail-dialog but does NOT wrap <FloatingWindow>."
    echo "            → Wrap its body in <FloatingWindow> (see cmes-floating-showcard skill),"
    echo "              or, if it is genuinely NOT a draggable window, add it to ALLOW with a reason."
    fail=1
  fi
done < <(find "$RAZOR" \( -type d \( -name bin -o -name obj \) -prune \) -o \
              -type f \( -iname "*Showcard*.razor" -o -iname "*DetailDialog*.razor" \) -print)

# ── INLINE showcard scan (P11 — L34 gap fix) ──────────────────────────────
# A showcard can be hand-rolled INLINE in ANY .razor (not just *Showcard*/
# *DetailDialog* files) — e.g. the per-leg IPQC inspector that lived inside
# LegsDashboard.razor. Signature of hand-rolled window chrome = a literal
# role="dialog" (FloatingWindow renders that for you). So: any .razor that
# writes role="dialog" ITSELF but does NOT wrap <FloatingWindow> is a showcard
# that dodged the filename check → FAIL. The two dialog PRIMITIVES are allowed:
#   FloatingWindow.razor — the showcard chrome itself.
#   Modal.razor          — the centred transactional-modal primitive (forms/confirm).
# A transactional <Modal> USED in a page carries no literal role="dialog" (the
# Modal component supplies it), so pages using <Modal> are never flagged.
DIALOG_ALLOW=(
  "FloatingWindow.razor"
  "Modal.razor"
)
is_dialog_allowed() {
  local base="$1"
  for a in "${DIALOG_ALLOW[@]}"; do [ "$base" = "$a" ] && return 0; done
  return 1
}

while IFS= read -r f; do
  base="$(basename "$f")"
  is_dialog_allowed "$base" && continue
  # literal role="dialog" in this file?
  if grep -nq 'role="dialog"' "$f"; then
    checked=$((checked + 1))
    if ! grep -q "<FloatingWindow" "$f"; then
      line="$(grep -n 'role="dialog"' "$f" | head -1 | cut -d: -f1)"
      echo "[gate:FAIL] $base:$line hand-rolls a dialog (role=\"dialog\") WITHOUT <FloatingWindow>."
      echo "            → A keep-open detail/inspector window is a SHOWCARD: extract its body"
      echo "              into a component that wraps <FloatingWindow> + let the parent own the"
      echo "              multi-window state (IFloatingWindowStore) — see cmes-floating-showcard."
      echo "              A transactional form/confirm must use the <Modal> component instead."
      fail=1
    fi
  fi
done < <(find "$RAZOR" \( -type d \( -name bin -o -name obj \) -prune \) -o \
              -type f -iname "*.razor" -print)

if [ "$fail" = 0 ]; then
  echo "[gate:OK] $checked showcard/detail-dialog surface(s) wrap <FloatingWindow> (filename + inline scan)."
fi
exit $fail
