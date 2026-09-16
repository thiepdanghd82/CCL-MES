# Troubleshooting

## `MissingDependencyException`

The format's optional extra is not installed. The message names the converter,
the extension and the feature.

```bash
pip install 'markitdown[pdf]'     # or docx, xlsx, xls, pptx, outlook, …
pip install 'markitdown[all]'     # or just take everything
```

Check you are installing into the same interpreter that runs `markitdown`:

```bash
which markitdown && python3 -c "import markitdown, sys; print(markitdown.__version__, sys.executable)"
```

## `UnsupportedFormatException`

No converter accepted the stream. Usual causes:

- The type genuinely is unsupported (`.doc`, `.ppt`, `.pages`, video without
  Azure CU). Convert to a modern format first.
- The content came from stdin or a stream with no filename, so detection had
  nothing to go on. Give a hint: `-x .pdf`, `-m application/pdf`, or
  `stream_info=StreamInfo(extension=".pdf")`.
- The extension lies about the content (a `.xlsx` that is really CSV). Pass the
  true `mimetype` hint.

## `FileConversionException`

Every converter that accepted the file then failed. Iterate `e.attempts` for
each `FailedConversionAttempt` — `attempt.converter` and `attempt.exc_info`
name the real error, which is usually a corrupt or password-protected file.

## PDF comes out empty or nearly empty

The PDF is scanned — an image per page, with no text layer. The built-in
converter has no OCR. Options, cheapest first:

1. `markitdown-ocr` plugin with a vision LLM (`references/plugins-ocr.md`).
2. Azure Document Intelligence (`-d -e <endpoint>`).
3. Azure Content Understanding (`--use-cu --cu-endpoint <endpoint>`).

## OCR plugin produces nothing

`llm_client` **and** `llm_model` must both be set; otherwise the plugin loads
and silently skips OCR. Confirm the plugin is visible with
`markitdown --list-plugins` (expect `ocr`) and that `-p` /
`enable_plugins=True` is on.

## Tables look wrong

- PDF tables are reconstructed from pdfplumber geometry — merged cells and
  nested tables degrade. Compare against the Azure backends before concluding
  the file is at fault.
- Excel formulas render as cached values; if the workbook was never recalculated
  the values may be stale. Open and save in Excel first.

## Garbled characters

Pass the charset: `-c UTF-8` on the CLI, or
`StreamInfo(charset="cp1258")` in Python. Legacy Vietnamese and Chinese files
often carry non-UTF-8 encodings that charset-normalizer guesses wrong.

## Image or audio metadata missing

`exiftool` is not on the path. Install it (`brew install exiftool`,
`apt install libimage-exiftool-perl`), or pass `exiftool_path=`, or set
`EXIFTOOL_PATH`. MarkItDown only auto-detects it in well-known directories.

## URL conversions fail or return a login page

The site requires auth or blocks the default client. Fetch it yourself with a
configured session and hand over the response:

```python
import requests
from markitdown import MarkItDown

s = requests.Session()
s.headers["User-Agent"] = "..."
r = s.get(url)
print(MarkItDown().convert_response(r).markdown)
```

Or construct `MarkItDown(requests_session=s)` to reuse it for every conversion.

## Base64 images bloat the output

They are truncated by default. If they came back in full, `--keep-data-uris`
was passed — drop it.

## Batch run is slow

Conversion is CPU-bound per file and independent across files: use
`scripts/md_batch.py --workers N`. Do **not** parallelise heavily when OCR or
an Azure backend is on — those are rate-limited and billed per call.

## `markitdown: command not found`

The venv is not active, or the install went to a different interpreter:

```bash
source ~/.venvs/markitdown/bin/activate
python3 -m markitdown file.pdf      # always works if the package is importable
```
