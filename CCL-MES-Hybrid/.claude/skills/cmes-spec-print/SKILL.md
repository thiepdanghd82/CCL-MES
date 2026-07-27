---
name: cmes-spec-print
description: >
  How to print / export the Spec sheet (and any WYSIWYG document) in CCL-MES
  Hybrid on maccatalyst. window.print() is DEAD in WKWebView — drive native
  UIPrintInteractionController; put print CSS in GLOBAL app.css; keep the
  printed PDF pixel-faithful to the on-screen view. Use when adding/编辑 a
  print button, @media print CSS, or the server MigraDoc export.
---

# CMES spec print / PDF (WYSIWYG)

**Golden rule:** the printed page / saved PDF must look like the on-screen
view. On Mac Catalyst that means printing the LIVE DOM natively, and styling
it with a GLOBAL `@media print` block — never a second, drifting renderer.

CI gate: `scripts/gate-spec-print.sh`. Lesson: `docs/LESSONS-LEARNED.md` **L39**.

## 1. window.print() is a no-op in WKWebView → native print

The MAUI BlazorWebView on maccatalyst SWALLOWS `window.print()` (returns
without a panel). So printing goes through a .NET abstraction:

- `CCL.MES.Hybrid.Client/Printing/IPrintService` — `IsNativePrintSupported`
  + `PrintCurrentViewAsync(jobName)`. Stub returns false (tests / Windows).
- `Platforms/MacCatalyst/CatalystPrintService` — drives
  `UIPrintInteractionController.SharedPrintController` with
  `wkWebView.ViewPrintFormatter` (the OS rasterises the live DOM = WYSIWYG,
  with `@media print` applied). Default `UIPrintInfo.Orientation = Landscape`
  for wide sheets; the panel lets the operator pick A4/A3 + orientation +
  scale + Save-as-PDF.
- The `WKWebView` ref is captured in `MainPage.OnBlazorWebViewInitialized`
  (`CatalystWebViewHolder`); a `cclMesPrint` `WKScriptMessageHandler` +
  `wwwroot/js/print.js` bridges `window.cclMesPrint.print()` / Cmd+P to the
  same native panel.
- The Razor print button calls `IPrintService.PrintCurrentViewAsync`; when
  `IsNativePrintSupported` is false it falls back to the server MigraDoc PDF.

⚠ macOS panel owns the final orientation toggle — `UIPrintInfo.Orientation`
is a default, not a lock. Design the print CSS to look right in landscape.

## 2. Print CSS lives in GLOBAL app.css (scoped .razor.css is DEAD)

maccatalyst loads only `wwwroot/css/app.css`. Put the whole `@media print`
block there. It must:
- `@page { size: A4 landscape }` for wide sheets.
- Reveal ONLY the document (`.spec-showcard-full`), hide all app chrome
  (nav / topbar / tabs / toolbar / buttons) — visibility technique + absolute
  lift so no reserved whitespace.
- Compact vertically (landscape trades height for width — the sheet MUST be
  short) via smaller fonts + tight padding.
- `print-color-adjust: exact` so colour bands print.
- Colours via `:root` tokens / CSS keywords only (L37 hex gate).

## 3. Wide tables: ONE line per row — auto columns + nowrap + ONE font token

The killer bug (L39): a wide multi-column table (e.g. the 21-col Print
Process table) rendered with `table-layout: fixed` splits width EVENLY, so
long cells wrap 2–3 lines and the font looks uneven. Fix — make print match
on-screen:

- `table-layout: auto` (columns size to content), **not** `fixed`.
- Cells `white-space: nowrap` + `word-break: normal` → one line per row.
- ONE shared font-size **token** (`--spec-print-table-fs`) on header + body
  → uniform size. If a row is still too wide for the page, drop the token
  value in ONE place — never re-introduce wrap.
- On-screen: the same `nowrap` + a `.spec-table-scroll { overflow-x:auto }`
  wrapper so a narrow viewport scrolls horizontally instead of wrapping.
  On-screen Full view and the printed PDF then stay WYSIWYG.

Product-Info / Revision tables also use `auto` (long cells like Material may
wrap; short columns stay one line).

## 4. Server MigraDoc export = the FALLBACK (Windows / native-fail)

`src/CCL.MES.Infrastructure/SpecExport/SpecPdfDocumentBuilder.BuildDetailSheet`
+ `PdfSpecSheetExporter`:
- A4 **Landscape** default (portrait was cutting 13 of 21 columns — data loss).
- Show ALL columns (never cut data to fit).
- Auto-fit ≤ 2 pages: `RenderFitted` renders, reads `PdfDocument.PageCount`,
  and if > 2 rebuilds one `DetailLayout` step tighter (smaller uniform body
  font + denser padding) — MigraDoc can't measure pages pre-render.
- Hairline borders `StyleConstants.DetailBorderWidthPt` (0.25pt); section
  titles `KeepWithNext` so nothing orphans onto a stray page.

## Do NOT

- Rely on `window.print()` as the print path on maccatalyst.
- Put print styling in a scoped `.razor.css` (dead on maccatalyst).
- Use `table-layout: fixed` + wrap on a wide data table (→ the L39 bug).
- Sprinkle per-column font sizes — one `--spec-print-table-fs` token.
- Cut columns/data to make the sheet fit — shrink the uniform font instead.
