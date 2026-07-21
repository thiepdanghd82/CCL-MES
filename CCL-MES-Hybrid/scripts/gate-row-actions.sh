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
# Match a table-header cell whose text is Action / Actions / Hành động.
matches="$(grep -rInE '<th[^>]*>[[:space:]]*(Actions?|Hành động)[[:space:]]*</th>' \
             --include='*.razor' "$RAZOR" 2>/dev/null || true)"

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
