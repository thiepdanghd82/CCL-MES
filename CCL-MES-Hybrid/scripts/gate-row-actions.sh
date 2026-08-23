#!/usr/bin/env bash
#
# CI gate — grid ROW ACTIONS (Copy/Edit/Delete/…) belong in a right-click /
# long-press / kebab menu via Shared/RowContextMenu.razor, NOT an "Actions"
# column of inline buttons. A NEW component that adds a `<th …>Action(s)</th>`
# / `<th …>Hành động</th>` header fails the gate unless it is on the allow-list
# below (with a reason). See .claude/skills/cmes-row-context-menu/SKILL.md +
# LESSONS-LEARNED.md L35.
#
# Usage: bash scripts/gate-row-actions.sh   (exit 0 = pass, 1 = fail)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RAZOR="$ROOT/src/CCL.MES.Hybrid.Razor"

# The one detection regex — a table-header cell whose text is Action / Actions /
# Hành động. Defined ONCE so the self-test exercises the exact same matcher the
# real scan uses (no drift between detector and its test).
RX='<th[^>]*>[[:space:]]*(Actions?|Hành động)[[:space:]]*</th>'

# ── self-test: prove the detector still catches an injected violation ──────────
if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  printf '<table><thead><tr><th class="x">Actions</th></tr></thead></table>\n' \
    > "$tmp/GateSelftestActions.razor"
  if grep -rIqE "$RX" --include='*.razor' "$tmp" 2>/dev/null; then
    echo "[gate:row-actions] self-test OK (an injected \"Actions\" column header is detected)"
    exit 0
  fi
  echo "[gate:row-actions] self-test FAILED — detector missed an injected Actions column"
  exit 1
fi

# Pre-existing surfaces grandfathered in, or where "Action" is a DATA column:
#   SettingsAuditLog.razor  — "Action" = the audit action TYPE (data, not row-actions).
#   WoMaterialsList.razor / SpecShowcardFull.razor / SettingsAccounts.razor
#       — pre-RowContextMenu inline action columns (migrate opportunistically).
ALLOW=(
  "SettingsAuditLog.razor"
  "WoMaterialsList.razor"
  "SpecShowcardFull.razor"
  "SettingsAccounts.razor"
)

is_allowed() {
  for a in "${ALLOW[@]}"; do [ "$1" = "$a" ] && return 0; done
  return 1
}

fail=0
# Match a table-header cell whose text is Action / Actions / Hành động (via $RX).
matches="$(grep -rInE "$RX" --include='*.razor' "$RAZOR" 2>/dev/null || true)"

while IFS= read -r line; do
  [ -z "$line" ] && continue
  file="${line%%:*}"
  base="$(basename "$file")"
  is_allowed "$base" && continue
  echo "[gate:FAIL] $base has an \"Actions\" column for row actions."
  echo "            → Use Shared/RowContextMenu.razor (right-click / long-press / ⋯ kebab)"
  echo "              instead of an inline Action column, or allow-list it with a reason."
  fail=1
done <<< "$matches"

if [ "$fail" = 0 ]; then
  echo "[gate:OK] no un-allowed \"Actions\" columns — row actions use RowContextMenu."
fi
exit $fail
