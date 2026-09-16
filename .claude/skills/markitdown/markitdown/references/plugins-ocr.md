# Plugins and the OCR plugin

## The plugin system

Plugins are **disabled by default**. They are discovered through the
`markitdown.plugin` entry-point group.

```bash
markitdown --list-plugins            # what is installed
markitdown --use-plugins file.pdf    # run with plugins enabled
```

```python
md = MarkItDown(enable_plugins=True, llm_client=..., llm_model=...)
```

Everything passed to `MarkItDown()` is forwarded to each plugin's
`register_converters(markitdown, **kwargs)`, which is how a plugin receives
`llm_client` / `llm_model`. Plugins register converters at any priority; a
negative priority pre-empts the built-ins.

Find plugins by searching GitHub for the hashtag `#markitdown-plugin`. To write
one, see the "Writing a plugin" section of `python-api.md` and the upstream
`packages/markitdown-sample-plugin` template.

## markitdown-ocr — text inside images

Adds LLM-vision OCR to the **PDF, DOCX, PPTX and XLSX** converters, so text
that lives inside embedded images (screenshots, scanned pages, drawings,
photographed labels) makes it into the Markdown. No new ML libraries or binary
dependencies — it reuses the `llm_client` / `llm_model` pattern.

```bash
pip install markitdown-ocr
pip install openai        # or any OpenAI-compatible client
```

### Usage

```bash
markitdown document.pdf --use-plugins --llm-client openai --llm-model gpt-4o
```

```python
from markitdown import MarkItDown
from openai import OpenAI

md = MarkItDown(
    enable_plugins=True,
    llm_client=OpenAI(),
    llm_model="gpt-4o",
)
print(md.convert("document_with_images.pdf").markdown)
```

Any OpenAI-compatible client works, including Azure OpenAI:

```python
from openai import AzureOpenAI
md = MarkItDown(
    enable_plugins=True,
    llm_client=AzureOpenAI(
        api_key="...",
        azure_endpoint="https://your-resource.openai.azure.com/",
        api_version="2024-02-01",
    ),
    llm_model="gpt-4o",
)
```

Custom extraction prompt (useful for tables, dimensioned drawings, labels):

```python
md = MarkItDown(
    enable_plugins=True, llm_client=OpenAI(), llm_model="gpt-4o",
    llm_prompt="Extract all text from this image, preserving table structure.",
)
```

**If no `llm_client` is given, the plugin still loads but OCR is silently
skipped** and the standard built-in converter is used. Silent no-op is the most
common "why is there no OCR text" cause.

### How it works

1. MarkItDown discovers the plugin via the `markitdown.plugin` entry point.
2. It calls `register_converters()`, forwarding `llm_client` / `llm_model`.
3. The plugin builds an `LLMVisionOCRService` from those kwargs.
4. Four OCR-enhanced converters register at **priority -1.0** — ahead of the
   built-ins at 0.0.
5. Per file: extract embedded images → send each to the LLM with an extraction
   prompt → insert the returned text inline, preserving structure. A failed LLM
   call becomes a warning; conversion continues without that image's text.

### Per-format behaviour

**PDF** — embedded images are pulled by position (`page.images` / page
XObjects) and interleaved with surrounding text in vertical reading order.
Pages with no extractable text are treated as **scanned**: the whole page is
rendered at 300 DPI and sent as one image. Malformed PDFs that
pdfplumber/pdfminer cannot open (truncated EOF and similar) are retried with
PyMuPDF page rendering, so content is still recovered.

**DOCX** — images come from document part relationships (`doc.part.rels`). OCR
runs *before* the DOCX→HTML→Markdown pipeline: placeholder tokens go into the
HTML so the Markdown converter does not escape them, then the placeholders are
replaced with the formatted OCR blocks. Headings, paragraphs and tables around
the images stay intact.

**PPTX** — picture shapes, placeholder shapes holding images, and images inside
groups are all handled, processed in top-to-left reading order per slide. If an
`llm_client` is configured, a description is requested first and OCR is the
fallback when no description comes back.

**XLSX** — worksheet images (`sheet._images`) are extracted per sheet, their
cell position derived from anchor coordinates (column/row → Excel letter
notation), and listed under `### Images in this sheet:` **after** the sheet's
data table — they are not interleaved into table rows.

### Output marker

Every extracted block is wrapped as:

```text
*[Image OCR]
<extracted text>
[End OCR]*
```

Handy for post-processing: `grep -n '\[Image OCR\]' out.md` shows where OCR
contributed, and the markers are easy to strip if the downstream consumer does
not want them.

### Troubleshooting

| Symptom | Check |
|---|---|
| No OCR text at all | `llm_client` **and** `llm_model` both set? Without them OCR silently no-ops. |
| Plugin not used | `markitdown --list-plugins` should list `ocr`; and `-p` / `enable_plugins=True` must be on. |
| API errors as warnings | API key, quota, and that the model supports vision input. |
| Garbled tables | Give a task-specific `llm_prompt` asking to preserve table structure. |

Cost note: every embedded image is one vision call. On a large scanned PDF that
is one call per page — batch deliberately.
