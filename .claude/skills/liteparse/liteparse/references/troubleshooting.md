# Troubleshooting

## `ParseError: OCR failed ... failed to download tessdata ... HTTP 403`

The Tesseract engine is compiled in but the **language data is downloaded on
first use** from `github.com/tesseract-ocr/tessdata_best`. On an offline or
egress-filtered machine there is nothing to fall back to and the whole parse
fails — even on a PDF whose text layer was extracted fine.

Three ways out:

```bash
# 1. pre-stage the language files, then point at them
export TESSDATA_PREFIX=/opt/tessdata      # holding eng.traineddata, vie.traineddata …
# 2. skip OCR entirely
lit parse doc.pdf --no-ocr
# 3. keep the natively extracted text instead of aborting (Python)
LiteParse(ocr_failure_fatal=False)
```

Route first (`is-complex`) and you will know whether OCR was even needed.

## The document title vanished from the Markdown

Header/footer stripping is on by default and will drop a title line at the top
of the page — on a short document there are not enough pages for the
repeated-line heuristic to tell a title from a running header. The heading is
missing from `blocks` too, not just the rendered text.

```python
LiteParse(output_format="markdown")                            # title dropped
LiteParse(output_format="markdown", keep_headers_footers=True) # "# Quotation Q-2026-081"
```

CLI: `--keep-headers-footers`. Worth setting by default for one- and two-page
documents such as quotes, certificates and spec sheets.

## Office files fail, PDFs work

LibreOffice is missing or not on PATH. `.docx/.xlsx/.pptx/.odt/.rtf/.pages/.key`
are converted to PDF by LibreOffice before parsing.

```bash
brew install --cask libreoffice        # macOS
sudo apt-get install libreoffice       # Debian/Ubuntu
```

Windows: add `C:\Program Files\LibreOffice\program` to PATH. In Docker, use
`full.Dockerfile` — the slim image has no LibreOffice.

## Timing lines polluting my output

Progress goes to **stderr** by default (`[liteparse] extract: 67.9ms (2 pages)`).
Pass `quiet=True` / `quiet: true` / `-q`. Redirecting stdout alone does not
silence it.

## `Promise.all` / threads give no speedup

Expected. PDFium is not thread-safe, so all in-process parses serialize on a
process-global lock. Real parallelism needs the worker pool:
`LiteParse(pool_size=4)` in Python, `new LiteParse({ poolSize: 4 })` in Node.

## A document hangs forever

Only the worker pool can enforce a deadline. `parse_timeout=15` (Python) /
`parseTimeoutMs: 15_000` (Node) kills the worker and raises
`ParseTimeoutError`, which names the offending source. Without the pool there
is no timeout at all.

## Markdown output looks nothing like the document

Reconstruction is heuristic — headings, tables and lists are inferred from
spatial layout, with no semantic model. Things to try, in order:

1. `--extract-blocks` (`extract_blocks=True`) to see what the classifier
   actually found — if a table came back as `paragraph` blocks, the Markdown
   was never going to be right.
2. `--keep-headers-footers` if useful content is being stripped as a repeated
   running header.
3. `--preserve-small-text` if footnotes or dimension callouts vanish.
4. Work from `--format json` and the `blocks` array instead of the rendered
   Markdown — for anything downstream-programmatic that is the better input.
5. For genuinely hard documents (dense tables, multi-column scans, handwriting)
   a heuristic parser is the wrong tool; a cloud parser such as LlamaParse is
   the honest answer.

## Empty or garbled text from a PDF

Run `lit is-complex doc.pdf` first. `scanned` or `no-text` means there is no
text layer and you need OCR. `garbled` means a broken encoding — OCR usually
beats the text layer there, so force it rather than trusting extraction.

## OCR produces nothing / wrong language

- Language code must be Tesseract's three-letter form (`vie`, not `vi`) for the
  built-in engine; two-letter ISO codes are for the HTTP OCR API.
- Air-gapped machine: set `TESSDATA_PREFIX` or `--tessdata-path`.
- Confirm OCR is not disabled (`--no-ocr`, `ocr_enabled=False`).

## Parse aborts on a mostly-fine document

A systemic OCR failure aborts by default. Set `ocr_failure_fatal=False` to keep
the natively recovered text and return partial results. For per-page failures,
`continue_on_page_error=True` skips the bad pages and fills
`result.page_errors`.

## `result.images` is empty despite images in the PDF

`extract_images` defaults to false. Markdown still emits placeholder refs, but
no bytes are pulled. Set `extract_images=True`, and add `image_output_dir` to
write files.

## Node: keys are snake_case where I expected camelCase

The programmatic API is camelCase; **CLI JSON is snake_case in every binding**,
matching the Rust CLI schema. Code that shells out to `lit` and code that calls
the library see different keys.

## Bounding boxes don't line up with my screenshot

Boxes are in top-left 72-DPI viewport coordinates regardless of render DPI.
Scale by `dpi / 72` to map onto a screenshot rendered at that DPI.

## Memory blows up on a huge PDF

Node: `parseBatches(source, { batchSize: 20 })`. Note that cross-page passes
(header/footer stripping, image dedup) become batch-local, so output can differ
from `parse()`. Otherwise cap with `max_pages` or slice with `target_pages`.

## `lit: command not found`

The pip/npm install put it somewhere off PATH. `npm i -g @llamaindex/liteparse`
for a global CLI, or use the library API. In the Docker images `liteparse` is a
symlink to `lit`.

## Native module won't load in Electron

The npm package ships a per-platform native binary. Unpack it (`asarUnpack`)
and ship the right build per target, or sidestep it with the WASM package or
the REST server as a sidecar.
