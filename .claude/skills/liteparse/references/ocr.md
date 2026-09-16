# OCR

OCR is **on by default** and runs only on pages the parser judges text-sparse.
`is-complex` tells you in advance which pages those are.

## Default: built-in Tesseract

The engine is compiled in, but **language data is fetched on first use** from
`github.com/tesseract-ocr/tessdata_best`. No network, no OCR — the parse raises
`ParseError: OCR failed ... HTTP 403 Forbidden` rather than degrading. Pre-stage
`.traineddata` files and set `TESSDATA_PREFIX` on any machine that cannot reach
GitHub, or parse with `--no-ocr`.

```bash
lit parse document.pdf                     # OCR enabled
lit parse document.pdf --ocr-language vie  # Vietnamese
lit parse document.pdf --no-ocr            # off
```

Language codes are Tesseract's three-letter form — `eng`, `vie`, `chi_sim`,
`chi_tra`, `jpn`, `kor`, `fra`, `deu`. (The **HTTP OCR API** uses two-letter
ISO codes instead: `en`, `vi`, `zh`. Do not mix them up.)

Offline or air-gapped:

```bash
export TESSDATA_PREFIX=/path/to/tessdata      # folder of .traineddata files
lit parse document.pdf --ocr-language eng
lit parse document.pdf --tessdata-path /path/to/tessdata   # or per-call
```

## HTTP OCR servers

For better accuracy — especially CJK and Vietnamese diacritics — point
LiteParse at an HTTP OCR server:

```bash
lit parse document.pdf --ocr-server-url http://localhost:8828/ocr
```

```python
LiteParse(
    ocr_server_url="http://localhost:8828/ocr",
    ocr_server_headers={"Authorization": "Bearer <token>"},   # optional
)
```

Reference wrappers ship in the repo's `ocr/` directory, each a small Flask
server run with `uv run server.py`:

| Wrapper | Default port | Notes |
|---|---|---|
| `ocr/easyocr` | 8828 | EasyOCR, 80+ languages |
| `ocr/paddleocr` | 8829 | PaddleOCR, strong CJK |
| `ocr/suryaocr` | 8830 | Surya OCR 2, multilingual; `TORCH_DEVICE=cuda\|cpu`; Docker image with `--gpus all` |

## Writing your own OCR server

Implement one endpoint and any engine works. Full spec in the repo's
`OCR_API_SPEC.md`.

**`POST /ocr`**, `multipart/form-data`:

| Field | Required | Description |
|---|---|---|
| `file` | yes | Image file (PNG, JPG, …) |
| `language` | no | Two-letter ISO code, default `en` |

Response, `application/json`:

```json
{
  "results": [
    { "text": "recognized text",
      "bbox": [x1, y1, x2, y2],
      "confidence": 0.95,
      "polygon": [[x1,y1],[x2,y2],[x3,y3],[x4,y4]] }
  ]
}
```

Rules that matter:

- **Coordinates** are pixels, origin top-left, x right, y down.
- **`bbox`** must be axis-aligned `[x1, y1, x2, y2]` with `x2 > x1`, `y2 > y1`.
  If your engine returns rotated boxes, take the min/max.
- **`polygon`** is optional but worth sending: 4 points, TL → TR → BR → BL **in
  the glyphs' upright reading frame**. LiteParse uses it to detect vertical or
  sideways text (legal sidebars, rotated stamps) and route it through the
  rotation reading-order handler instead of flattening it into body lines.
- **`confidence`** normalized to 0.0–1.0; send `1.0` if your engine has none.
- **Order results in reading order** (top-to-bottom, left-to-right).
- Status codes `200` / `400` / `500`; errors as `{"error": "..."}`.

## OCR reliability knobs (Python)

| Option | Use |
|---|---|
| `ocr_failure_fatal=False` | Default `True` aborts the parse when OCR fails systemically. `False` keeps recovered native text and returns partial results |
| `ocr_hedge_delays_ms=[0, 5000, 10000]` | Hedge HTTP OCR requests — fire a duplicate on this schedule to cut tail latency |
| `num_workers=N` | Concurrent OCR workers; CLI default is CPU cores − 1 |

## Browser

WASM has neither Tesseract nor the HTTP backend. Supply a JS `ocrEngine` with a
`recognize()` method returning the same `{ text, bbox, confidence }` shape —
see `wasm.md`.

## Cost discipline

OCR is the expensive stage. The cheap pattern:

```bash
lit is-complex doc.pdf --quiet && lit parse doc.pdf --no-ocr || lit parse doc.pdf
```

Parse without OCR when no page needs it; fall back to the OCR path only when
`is-complex` says so.
