# CLI reference (`lit`)

Identical across the pip, npm and cargo installs. `liteparse` is a symlink to
`lit` in the Docker images.

## parse

```
lit parse [OPTIONS] <file>

  -o, --output <file>           Output file path
      --format <format>         json | text | markdown          [default: text]
      --no-ocr                  Disable OCR
      --ocr-language <lang>     Tesseract language code          [default: eng]
      --ocr-server-url <url>    HTTP OCR server (else Tesseract)
      --tessdata-path <path>    Path to tessdata directory
      --max-pages <n>           Max pages to parse               [default: 1000]
      --target-pages <pages>    e.g. "1-5,10,15-20"
      --dpi <dpi>               Rendering DPI                    [default: 150]
      --image-mode <mode>       off | placeholder | embed        [default: placeholder]
      --extract-images          Extract embedded image bytes + metadata
      --image-output-dir <dir>  Write extracted images; requires --extract-images
      --extract-text-metadata   Rich PDF text metadata on text items
      --extract-vector-graphics Page vector shapes + merged H/V lines
      --extract-annotations     PDF annotations in page output
      --extract-form-fields     AcroForm widget fields and values
      --extract-structure-tree  Tagged-PDF logical structure
      --extract-blocks          Classified layout blocks with bboxes
      --extract-content-bounds  Per-page union bbox of content objects
      --extract-xfa-packets     Raw XFA packets (index, name, XML)
      --no-links                Emit link text plain, no [text](url)
      --keep-headers-footers    Keep running headers/footers in markdown
      --preserve-small-text     Keep very small text
      --password <password>     Password for encrypted documents
      --num-workers <n>         Concurrent OCR workers  [default: CPU cores - 1]
  -q, --quiet                   Suppress progress output
```

```bash
lit parse document.pdf                                   # text to stdout
lit parse document.pdf --format markdown -o output.md
lit parse document.pdf --format json -o output.json
lit parse document.pdf --target-pages "1-5,10,15-20"
lit parse document.pdf --no-ocr
curl -sL https://example.com/report.pdf | lit parse -    # stdin
```

Markdown-specific:

```bash
lit parse doc.pdf --format markdown --image-mode off     # strip images
lit parse doc.pdf --format markdown --image-mode embed \
    --extract-images --image-output-dir ./images         # images to disk
lit parse doc.pdf --format markdown --no-links
```

`--image-mode` controls **presentation only**: `placeholder` (default) emits
`![](img_pN_K.png)` refs in reading order, `off` strips images, `embed` emits
the same refs as `placeholder`. Only `--extract-images` actually pulls bytes;
`--image-output-dir` requires it. JSON carries each image's `name`, `path`,
page bbox, pixel dimensions, rotation, format and duplicate relationship —
never the pixel bytes. Identical image resources share one output file.

## is-complex

```
lit is-complex [OPTIONS] <file>
      --compact           Dense JSON instead of pretty-printed
      --max-pages <n>     [default: 1000]
      --target-pages <p>
      --password <pw>
  -q, --quiet             Suppress the stderr verdict
```

Per-page JSON to **stdout**, a `COMPLEX`/`SIMPLE` verdict to **stderr**, and a
**non-zero exit when any page needs OCR** — so it works as a shell predicate.

```bash
lit is-complex doc.pdf                                  # verdict + JSON
lit is-complex doc.pdf --quiet && lit parse doc.pdf --no-ocr
lit is-complex doc.pdf --compact | jq '[.[] | select(.needs_ocr) | .page_number]'
```

Each page carries `needs_ocr` and `reasons`, drawn from `scanned`, `no-text`,
`sparse-text`, `embedded-images`, `garbled`, `vector-text`, `annotation-text`.

## batch-parse

```
lit batch-parse [OPTIONS] <input-dir> <output-dir>
      --format <format>       json | text | markdown     [default: text]
      --no-ocr
      --ocr-language <lang>   [default: eng]
      --ocr-server-url <url>
      --tessdata-path <path>
      --max-pages <n>         Per file                   [default: 1000]
      --dpi <dpi>             [default: 150]
      --recursive             Recurse into subdirectories
      --extension <ext>       Only this extension, e.g. ".pdf"
      --password <password>
      --num-workers <n>
  -q, --quiet
```

```bash
lit batch-parse ./input ./output --format markdown --recursive --extension .pdf
```

## screenshot

```
lit screenshot [OPTIONS] <file>
  -o, --output-dir <dir>     [default: ./screenshots]
      --target-pages <pages> e.g. "1,3,5" or "1-5"
      --dpi <dpi>            [default: 150]
      --password <password>
  -q, --quiet
```

```bash
lit screenshot doc.pdf -o ./shots
lit screenshot doc.pdf --target-pages "1,3,5" --dpi 300 -o ./shots
```

AcroForm field appearances (filled values, checkbox states) are drawn into the
raster, so form data is visible to a vision model and to OCR.

## Piping notes

- Progress lines go to stderr; `-q` silences them. Redirecting stdout alone is
  not enough to get clean output in a pipeline.
- `lit parse -` reads from stdin.
- CLI JSON uses **snake_case** keys in every binding, including Node. camelCase
  belongs to the programmatic Node/WASM APIs only.
