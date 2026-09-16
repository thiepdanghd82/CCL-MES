---
name: liteparse
description: Parse PDF, Office and image documents locally with LiteParse (run-llama) — spatial text extraction with bounding boxes, heuristic Markdown reconstruction, page screenshots, and a cheap complexity/OCR check. Use for PDF to Markdown or JSON, extracting text with coordinates for visual citations, deciding whether a document needs OCR, batch-parsing a folder, rendering pages to PNG for a vision model, or running the lit CLI, Python/Node bindings, browser WASM build, or the REST/gRPC server. Triggers - liteparse, lit parse, pdf to markdown, pdf bounding box, text with coordinates, is-complex, needs OCR, page screenshot for LLM, local document parser, parse PDF offline, doc PDF khong can cloud, trich xuat toa do chu trong PDF.
license: Skill text Apache-2.0 (derived from run-llama/liteparse, Apache-2.0).
---

# LiteParse

Fast, fully local document parsing. Rust core over PDFium, with bindings for
Python, Node/TypeScript, Rust and the browser (WASM), plus a `lit` CLI.
No cloud calls, no LLM in the loop.

Written against **run-llama/liteparse v2.14.2** (27 Aug 2026).

## What makes it different

Most converters give you text. LiteParse gives you text **plus where it is on
the page** — every item carries a bounding box in top-left 72-DPI viewport
coordinates. That is what makes visual citations, layout-aware chunking and
"highlight the source" features possible.

Three things only this tool does well:

- **`is-complex`** — a cheap text-layer-only pass that tells you, per page,
  whether OCR is needed and why (`scanned`, `no-text`, `sparse-text`,
  `embedded-images`, `garbled`, `vector-text`, `annotation-text`). Route,
  reject or price a document before paying for a full parse.
- **Screenshots** — render pages to PNG at any DPI, with AcroForm field values
  drawn in, for feeding a vision model.
- **Layout blocks** — the classifier's own decomposition (headings, paragraphs,
  list items, tables, code, rules, figures) as data, each with a bbox, and
  table cells that carry their own bbox.

## Choosing between LiteParse and MarkItDown

| Need | Use |
|---|---|
| Bounding boxes, coordinates, visual citations | **LiteParse** |
| Decide up front whether a PDF needs OCR | **LiteParse** (`is-complex`) |
| Page images for a vision model | **LiteParse** (`screenshot`) |
| Speed on large PDF batches | **LiteParse** (Rust + worker pool) |
| Faithful Excel tables, PPTX notes, .msg, EPub, YouTube | **MarkItDown** |
| Best-effort Markdown from a messy PDF | try both — LiteParse is heuristic |
| Truly hard documents (dense tables, multi-column scans) | neither — a cloud parser like LlamaParse |

## Setup

```bash
pip install liteparse        # Python + the lit CLI
npm i -g @llamaindex/liteparse   # Node/TS + the same lit CLI
```

Optional but usually wanted:

- **LibreOffice** — required to read `.docx/.xlsx/.pptx/.odt/.rtf/.pages/.key`
  (converted to PDF first). `brew install --cask libreoffice`
- **Tesseract** — bundled; nothing to install. For air-gapped use set
  `TESSDATA_PREFIX` to a folder of `.traineddata` files.

Verify with `python3 scripts/lp_check.py`. Details in `references/install.md`.

## Fastest useful commands

```bash
lit parse document.pdf --format markdown -o out.md   # PDF → Markdown
lit is-complex document.pdf --quiet && lit parse document.pdf --no-ocr
lit parse doc.pdf --format json --extract-blocks -o out.json
lit screenshot doc.pdf --dpi 300 -o ./shots
lit batch-parse ./in ./out --format markdown --recursive
```

`is-complex` exits non-zero when any page needs OCR, so it composes as a shell
predicate. Full flag list: `references/cli.md`.

## Python in 20 seconds

```python
from liteparse import LiteParse

parser = LiteParse(output_format="markdown", ocr_enabled=False, quiet=True)
result = parser.parse("document.pdf")
print(result.text)                       # rendered Markdown
print(result.total_pages, result.creator, result.producer)

for item in result.pages[0].text_items:  # every item has x/y/width/height
    print(item.text, item.x, item.y)
```

`parse()` also takes raw `bytes`. Full config, the result model and the worker
pool: `references/python.md`, `references/output-model.md`.

## Node/TypeScript in 20 seconds

```typescript
import { LiteParse } from '@llamaindex/liteparse';

const parser = new LiteParse({ outputFormat: 'markdown', ocrEnabled: false });
const result = await parser.parse('document.pdf');
console.log(result.text);
```

Options are camelCase in the programmatic API but snake_case in CLI JSON
output — a real trip-up. See `references/node.md`.

## Batch a folder

```bash
python3 scripts/lp_batch.py ~/Documents/Specs -o ~/Documents/Specs_md
```

Mirrors the tree, writes Markdown (or JSON), and writes `_manifest.csv` with
per-file page count, timing and the `is-complex` verdict. Useful options:

- `--route` — run `is-complex` first and only enable OCR on pages that need it
- `--format json --extract-blocks` — structured output with layout blocks
- `--screenshots ./shots --dpi 200` — also render page PNGs
- `--pool 4 --timeout 30` — worker pool with a hard per-document deadline
- `--keep-headers-footers` — stop short documents losing their title
- `--ocr-nonfatal` — keep native text when OCR fails instead of aborting
- `--ext .pdf .docx`, `--overwrite`, `--dry-run`

`lit batch-parse` covers the simple case; this script adds routing, the
manifest and timeouts.

## Things that will bite you

- **Markdown is heuristic**, not semantic. Headings and tables are inferred
  from spatial layout, so a hand-positioned table can come out as loose text.
  Check the output on a sample before trusting a batch.
- **PDFium is not thread-safe.** Every in-process parse serializes on a global
  lock — `Promise.all` and Python threads buy you nothing. Use the worker pool
  (`pool_size` / `poolSize`) for real parallelism, and it is also the only way
  to enforce a hard timeout on a rogue document.
- **Progress output goes to stderr by default.** Pass `quiet=True` / `-q` in
  pipelines.
- **Tesseract language data is downloaded on first use**, not bundled. Offline
  or egress-filtered machines get `ParseError: OCR failed ... HTTP 403` and the
  whole parse aborts. Pre-stage `TESSDATA_PREFIX`, use `--no-ocr`, or set
  `ocr_failure_fatal=False`.
- **Header/footer stripping eats the title** of a short document — a one-page
  quote loses its `# Quotation Q-2026-081` heading, in `blocks` as well as in
  the rendered text. Set `keep_headers_footers=True` for short documents.
- **Office formats need LibreOffice on PATH**, otherwise they simply fail.
- **`extract_images` defaults to false** — Markdown still emits placeholder
  refs, but `result.images` stays empty until you opt in.
- An **official agent skill** also exists upstream:
  `npx skills add run-llama/llamaparse-agent-skills --skill liteparse`.
  This skill is a fuller reference built from the v2.14.2 source.

## Files in this skill

```
references/install.md        Packages, LibreOffice, Tesseract, env vars, Docker
references/cli.md            lit parse / batch-parse / screenshot / is-complex
references/python.md         LiteParse config, methods, worker pool, search_items
references/node.md           TS API, parseBatches, pool, camelCase vs snake_case
references/wasm.md           Browser build, JS-side OCR engine, config table
references/server.md         liteparse-rest REST + gRPC, Docker, endpoints
references/output-model.md   ParseResult / ParsedPage / TextItem / blocks / bboxes
references/ocr.md            Tesseract, HTTP OCR servers, the OCR API spec
references/troubleshooting.md
scripts/lp_batch.py          Batch parser with complexity routing + manifest
scripts/lp_check.py          Environment checker
```
