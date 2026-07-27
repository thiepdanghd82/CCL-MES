#!/usr/bin/env bash
#
# CI gate — the Spec-sheet print / PDF path must stay WYSIWYG on maccatalyst.
# Guards the invariants documented in .claude/skills/cmes-spec-print/SKILL.md
# + LESSONS-LEARNED.md L39. Catches the regressions the project has paid for:
#   1. printing via window.print() instead of native UIPrintInteractionController
#   2. wide print tables reverting to table-layout:fixed + wrap (multi-line rows)
#   3. per-column font sizes instead of ONE uniform token
#   4. MigraDoc detail sheet reverting to Portrait (cuts columns / 3 pages)
#
# Usage: bash scripts/gate-spec-print.sh   (exit 0 = pass, 1 = fail)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REPO="$(cd "$ROOT/.." && pwd)"
RAZOR="$ROOT/src/CCL.MES.Hybrid.Razor"
CSS="$RAZOR/wwwroot/css/app.css"
PRINTSVC="$ROOT/src/CCL.MES.Hybrid/Platforms/MacCatalyst/CatalystPrintService.cs"
IPRINT="$ROOT/src/CCL.MES.Hybrid.Client/Printing/IPrintService.cs"
PDFBUILD="$REPO/src/CCL.MES.Infrastructure/SpecExport/SpecPdfDocumentBuilder.cs"

fail=0
note() { echo "[gate:spec-print:FAIL] $1"; fail=1; }

# 1. Native print path present (window.print() is dead in WKWebView).
[ -f "$IPRINT" ] || note "IPrintService abstraction missing ($IPRINT)."
if [ -f "$PRINTSVC" ]; then
  grep -q "UIPrintInteractionController" "$PRINTSVC" \
    || note "CatalystPrintService no longer drives UIPrintInteractionController."
  grep -q "ViewPrintFormatter" "$PRINTSVC" \
    || note "CatalystPrintService no longer prints the WKWebView ViewPrintFormatter (WYSIWYG)."
else
  note "CatalystPrintService.cs missing ($PRINTSVC)."
fi

# 2. GLOBAL @media print block exists (scoped .razor.css is dead on maccatalyst).
grep -q "@media print" "$CSS" || note "app.css has no @media print block."

# 3. Wide process table stays one-line: auto layout + nowrap + ONE font token.
# Block-aware: any CSS rule whose selector list names .spec-print-table-full
# must NOT set table-layout:fixed (that splits width evenly → cells wrap).
awk '
  {
    line = $0
    if (line ~ /\{/) {
      split(line, a, "{")            # selector text may share the { line
      insel = ((sel " " a[1]) ~ /spec-print-table-full/)
      sel = ""
    } else if (line ~ /\}/) {
      insel = 0; sel = ""
    } else {
      sel = sel " " line             # accumulate multi-line selector list
    }
    if (insel && line ~ /table-layout:[[:space:]]*fixed/) found = 1
  }
  END { exit(found ? 1 : 0) }
' "$CSS" || note ".spec-print-table-full uses table-layout:fixed (→ wraps; use auto + nowrap)."
grep -q "white-space: nowrap" "$CSS" \
  || note "no white-space:nowrap for the process table (rows would wrap)."
grep -q -- "--spec-print-table-fs" "$CSS" \
  || note "the uniform table font token --spec-print-table-fs is gone (per-column sizes drift)."

# 4. MigraDoc detail sheet default orientation is Landscape (portrait cut data).
if [ -f "$PDFBUILD" ]; then
  # The detail sheet's orientation parameter must default to Landscape (the
  # multi-line signature keeps it as `MigraDocOrientation orientation =
  # MigraDocOrientation.Landscape`). Portrait cut 13 of 21 columns.
  grep -q "MigraDocOrientation orientation = MigraDocOrientation.Landscape" "$PDFBUILD" \
    || note "SpecPdfDocumentBuilder.BuildDetailSheet default orientation is not Landscape."
fi

if [ "$fail" = 0 ]; then
  echo "[gate:spec-print:OK] native print + global @media print + one-line table (auto/nowrap/token) + landscape MigraDoc."
fi
exit $fail
