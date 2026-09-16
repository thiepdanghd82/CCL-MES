# Python API

```bash
pip install liteparse      # also installs the `lit` CLI
```

```python
from liteparse import LiteParse

parser = LiteParse()
result = parser.parse("document.pdf")
print(result.text)
print(f"pages: {result.total_pages}")

for page in result.pages:
    print(f"page {page.page_num}: {len(page.text_items)} items")
```

`parse()` accepts a path **or** raw `bytes` — handy for uploads:

```python
with open("document.pdf", "rb") as f:
    result = parser.parse(f.read())
```

## Configuration

Everything is set on the constructor:

```python
parser = LiteParse(
    ocr_enabled=True,               # default True
    ocr_language="eng",             # Tesseract code (vie, chi_sim, jpn…)
    ocr_server_url=None,            # HTTP OCR server instead of Tesseract
    tessdata_path=None,
    max_pages=1000,
    target_pages="1-5,10",
    extract_screenshots=False,      # pages as PNG bytes on result.screenshots
    continue_on_page_error=False,   # skip broken pages, fill result.page_errors
    dpi=150,
    output_format="json",           # "json" | "text" | "markdown"
    image_mode="placeholder",       # markdown only: placeholder | off | embed
    extract_images=False,           # bytes + metadata on result.images
    image_output_dir=None,          # write image files; needs extract_images
    extract_links=True,             # [text](url) in markdown
    keep_headers_footers=False,
    extract_vector_graphics=False,
    extract_annotations=False,
    extract_form_fields=False,
    extract_structure_tree=False,
    extract_blocks=False,           # classified layout blocks with bboxes
    extract_content_bounds=False,
    extract_document_metadata=False,
    extract_xfa_packets=False,
    preserve_very_small_text=False,
    extract_text_metadata=False,    # MCID, font metrics, colors, char codes
    password=None,
    quiet=False,                    # progress goes to stderr unless True
    num_workers=4,                  # concurrent OCR workers
)
```

`parser.get_config()` returns the resolved `LiteParseConfig`.

**In a pipeline, set `quiet=True`.** The default prints per-stage timings
(`[liteparse] extract: 67.9ms (2 pages)` …) to stderr.

## Markdown output

```python
parser = LiteParse(output_format="markdown", image_mode="placeholder",
                   extract_links=True, quiet=True)
print(parser.parse("document.pdf").text)     # rendered Markdown on .text
```

Reconstruction is heuristic — headings, tables and lists are inferred from
spatial layout. Text laid out by absolute positioning (common in
engineering-drawing exports and hand-built quote sheets) can come out as loose
lines rather than a table. Validate on a sample before batching.

## Methods

| Method | Returns |
|---|---|
| `parse(source, ...)` | `ParseResult` — path or `bytes` |
| `parse_batches(source, batch_size=...)` | iterator of `ParseBatch` for bounded memory |
| `is_complex(source)` | `list[PageComplexityStats]` |
| `screenshot(source, page_numbers=[1,2,3])` | `list[ScreenshotResult]` |
| `warm_up()` | pre-initialize pool workers (~60 ms) |
| `close()` | free workers; or use `with LiteParse(...) as parser:` |
| `get_config()` | `LiteParseConfig` |

Module-level: `search_items(items, phrase, *, case_sensitive=False)` — finds a
phrase that may span several adjacent `TextItem`s and returns synthetic merged
items with combined bounding boxes. This is the primitive behind
"search a keyword, highlight the region on the page image".

## Complexity routing

```python
parser = LiteParse(quiet=True)
pages = parser.is_complex("document.pdf")

if any(p.needs_ocr for p in pages):
    result = parser.parse("document.pdf")                      # OCR path
else:
    result = LiteParse(ocr_enabled=False).parse("document.pdf")  # cheap path

for p in pages:
    if p.needs_ocr:
        print(f"page {p.page_number}: {', '.join(p.reasons)}")
```

`reasons` values: `scanned`, `no-text`, `sparse-text`, `embedded-images`,
`garbled`, `vector-text`, `annotation-text`. Raw bytes work here too.

## Screenshots

```python
shots = parser.screenshot("document.pdf", page_numbers=[1, 2, 3])
for s in shots:
    print(s.page_num, s.width, s.height, s.is_solid_fill)
    open(f"page_{s.page_num}.png", "wb").write(s.image_bytes)
```

`is_solid_fill` flags a page that rendered blank — a quick way to skip empties
before sending images to a vision model.

## Worker pool and hard timeouts

PDFium is not thread-safe: in-process parses serialize on a process-global
lock, so threads buy nothing. For throughput, or to enforce a deadline, use
persistent worker processes:

```python
from liteparse import LiteParse, ParseTimeoutError

with LiteParse(pool_size=4, parse_timeout=15, quiet=True) as parser:
    parser.warm_up()
    try:
        result = parser.parse("document.pdf")
    except ParseTimeoutError as e:
        print(f"killed rogue document: {e.source} (deadline {e.timeout}s)")
```

The pool is also the **only** way to enforce a timeout — a rogue document is
killed, named, and never stalls the pipeline.

## Exceptions

```python
from liteparse import ParseError, ParseTimeoutError   # ParseTimeoutError(ParseError)
```

## Exported names

`LiteParse`, `search_items`, `LiteParseConfig`, `ParseResult`, `ParseBatch`,
`ParsedPage`, `TextItem`, `WordBox`, `LayoutBlock`, `LayoutCell`,
`DocumentAnnotation`, `AnnotationRect`, `FormField`, `StructureTree`,
`StructureTreeElement`, `ExtractedImage`, `ImageRect`, `VectorGraphics`,
`VectorShape`, `VectorLine`, `ScreenshotResult`, `ScreenshotRect`,
`PageComplexityStats`, `LayoutComplexityStats`, `DocumentMetadata`,
`XfaPacket`, `PageError`, `ParseError`, `ParseTimeoutError`.

Field-by-field breakdown: `output-model.md`.

## Options the README does not mention

These exist on `LiteParse.__init__` (v2.14.2) and are worth knowing — several
solve problems that otherwise look unsolvable:

| Option | What it does |
|---|---|
| `ocr_server_headers={"Authorization": "Bearer …"}` | Extra HTTP headers on every request to `ocr_server_url` — the way to reach an authenticated OCR service |
| `ocr_failure_fatal=False` | Default is `True`: a systemic OCR failure (every OCR task failed, at least one on a text-sparse page) aborts the parse. Set `False` to keep already-recovered native text and return partial results instead of raising |
| `ocr_hedge_delays_ms=[0, 5000, 10000]` | Request hedging for HTTP OCR — each attempt fires a duplicate request on this schedule. Empty or single-element means no hedging (the default) |
| `emit_word_boxes=True` | Per-word sub-boxes on `TextItem.words`. Roughly doubles the text-item payload — enable only for word-level bbox attribution |
| `crop_box=(top, right, bottom, left)` | Restrict output to a page sub-region. Each value is the **fraction** cropped from that side, top-left origin, in `[0, 1]`; `(0, 0, 0, 0.5)` keeps the right half |
| `skip_diagonal_text=True` | Drop items rotated more than 2° off the nearest right angle — excludes rotated watermarks and stamps |
| `include_complexity=True` | Compute complexity signals during `parse()` and attach them as `ParsedPage.complexity`, the same data `is_complex()` returns. Costs an extra vector-text detection pass, so it is off by default |
| `render_form_fields` | Controls whether AcroForm field appearances are drawn into rendered pages |

`crop_box` plus `skip_diagonal_text` is the practical recipe for pulling a
title block or a spec panel out of a drawing export while ignoring the
diagonal "CONTROLLED COPY" stamp over it.
