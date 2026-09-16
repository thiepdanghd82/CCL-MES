# Format support matrix

Built-in converters, in the order MarkItDown considers them (specific formats
first, generic fallbacks last).

| Format | Extensions | Detected MIME types | Needs extra | Output / caveats |
|---|---|---|---|---|
| PDF | `.pdf` | `application/pdf`, `application/x-pdf` | `[pdf]` | Text + tables rendered as aligned Markdown tables. **No OCR** — a scanned PDF yields nothing without the OCR plugin or Azure. |
| Word | `.docx` | `…wordprocessingml.document` | `[docx]` | DOCX → HTML (mammoth) → Markdown. Headings, lists, tables, links preserved. `.doc` (legacy) is **not** supported — convert to `.docx` first. |
| Excel | `.xlsx` | `…spreadsheetml.sheet` | `[xlsx]` | `## <sheet name>` heading + one Markdown table per sheet (pandas → HTML → Markdown). Formulas appear as computed values; charts and images are dropped. |
| Excel legacy | `.xls` | `application/vnd.ms-excel`, `application/excel` | `[xls]` | Same output, via xlrd. |
| PowerPoint | `.pptx` | `…presentationml` | `[pptx]` | Slide-by-slide with `<!-- Slide number: N -->` markers; text frames, tables, and speaker notes under `### Notes:`. Pictures become LLM descriptions when `llm_client` is set, otherwise alt-text placeholders. `.ppt` not supported. |
| Images | `.jpg`, `.jpeg`, `.png` | `image/jpeg`, `image/png` | — (exiftool optional) | EXIF metadata (ImageSize, Title, Caption, Description, Keywords…) plus an LLM description when `llm_client`/`llm_model` are given. No built-in OCR. |
| Audio | `.wav`, `.mp3`, `.m4a`, `.mp4` | `audio/x-wav`, `audio/mpeg`, `video/mp4` | `[audio-transcription]` | ID3/EXIF metadata (Title, Artist, Album, Genre…) plus speech transcription for wav/mp3 via SpeechRecognition. |
| HTML | `.html`, `.htm` | `text/html`, `application/xhtml` | — | markdownify-based. Generic priority (10) — runs after specific converters. |
| CSV | `.csv` | `text/csv`, `application/csv` | — | Rendered as a Markdown table. |
| JSON / XML / plain text | `.json`, `.xml`, `.txt`, `.md`, source code… | `text/*`, `application/json` | — | PlainTextConverter, generic priority — the final fallback. |
| EPub | `.epub` | `application/epub`, `application/epub+zip`, `application/x-epub+zip` | — | Metadata (title, authors) + each chapter's XHTML converted in spine order. |
| Outlook message | `.msg` | `application/vnd.ms-outlook` | `[outlook]` | From/To/Subject/Date headers plus body. |
| Jupyter notebook | `.ipynb` | — | — | Markdown cells verbatim, code cells fenced, outputs included. |
| ZIP archive | `.zip` | `application/zip` | — | Iterates the archive and converts every member, concatenating the results with per-file headings. Generic priority. |
| YouTube URL | — | url match | `[youtube-transcription]` | Video metadata + transcript. Requires a URL, not a file. |
| Wikipedia URL | — | url match | — | Article body cleaned of navigation chrome. |
| Bing SERP | — | url match | — | Search-result page cleaned into a list. |
| RSS / Atom | `.rss`, `.atom`, `.xml` feeds | feed MIME types | — | Feed title plus item titles, links and summaries. |
| Azure Doc Intelligence | any supported doc | — | `[az-doc-intel]` | See `azure.md`. |
| Azure Content Understanding | documents, images, audio, **video** | — | `[az-content-understanding]` | See `azure.md`. |

## Priority model

- `PRIORITY_SPECIFIC_FILE_FORMAT = 0.0` — nearly all converters.
- `PRIORITY_GENERIC_FILE_FORMAT = 10.0` — PlainText, HTML, ZIP.
- Lower value is tried first; ties break toward the most recently registered.
- Plugins may register at any value, e.g. `-1.0` to pre-empt a built-in.

## Type detection

When no hint is supplied, MarkItDown builds candidate `StreamInfo` guesses from
the filename/extension **and** from content sniffing with
[magika](https://github.com/google/magika), then tries every converter that
`accepts()` a guess, in priority order. The first successful conversion wins;
if all fail, a `FileConversionException` lists each attempt.

## Known gaps

- No `.doc`, `.ppt`, or `.pages` — convert to the modern format first.
- No OCR in the box: use `markitdown-ocr` or Azure for scanned material.
- No video in the box: only Azure Content Understanding handles video.
- Excel charts, embedded drawings and shapes are not extracted by the built-in
  XLSX converter (the OCR plugin lists worksheet images separately).
- Password-protected files are not supported.
