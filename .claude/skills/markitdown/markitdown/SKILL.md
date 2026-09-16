---
name: markitdown
description: Convert PDF, Word, Excel, PowerPoint, images, audio, HTML, CSV/JSON/XML, EPub, Outlook .msg, Jupyter notebooks, ZIP archives and YouTube/Wikipedia URLs into clean Markdown using Microsoft MarkItDown. Use when extracting text from documents, batch-converting a folder into Markdown, OCR-ing scanned PDFs, preparing documents for an LLM/RAG pipeline, or setting up the markitdown CLI, Python API, plugins, Azure backends or MCP server. Triggers - markitdown, convert to markdown, extract text from PDF, pdf to markdown, docx to markdown, xlsx to markdown, batch convert documents, OCR scanned PDF, chuyen PDF sang markdown, trich xuat noi dung tai lieu, doc file cho AI.
license: Skill text MIT (derived from microsoft/markitdown, MIT). MarkItDown © Microsoft.
---

# MarkItDown

Turn almost any document into Markdown that an LLM can read. Written against
[microsoft/markitdown](https://github.com/microsoft/markitdown) v0.1.7, which is
also the current PyPI release — `pip install 'markitdown[all]'` gives you
everything documented here.

MarkItDown preserves **structure** (headings, lists, tables, links) rather than
pixel fidelity. It is built for text-analysis and LLM pipelines, not for
human-facing high-fidelity conversion.

## Decision: which path to take

| Situation | Do this |
|---|---|
| One file, quick look | `markitdown file.pdf -o file.md` |
| A whole folder | `scripts/md_batch.py` (see below) |
| Inside a Python script / pipeline | Python API — `references/python-api.md` |
| Scanned PDF, text in images, drawings | OCR plugin — `references/plugins-ocr.md` |
| Need highest-quality cloud extraction, or audio/video, or field extraction | Azure — `references/azure.md` |
| Expose conversion to an AI agent / Claude Desktop | MCP server — `references/mcp-server.md` |
| "Does it support X?" | Format matrix — `references/formats.md` |
| Something failed | `references/troubleshooting.md` |

## Setup (do this once)

```bash
python3 -m venv ~/.venvs/markitdown
source ~/.venvs/markitdown/bin/activate
pip install 'markitdown[all]'
markitdown --version          # confirms the install
```

Requires **Python 3.10+**. Install narrower extras when full install is
unwanted: `pip install 'markitdown[pdf,docx,xlsx,pptx]'`. Full extras table in
`references/install.md`.

Verify the environment at any time:

```bash
python3 scripts/md_check.py
```

## Single file

```bash
markitdown report.pdf -o report.md      # to a file
markitdown report.pdf > report.md       # same, via shell redirect
cat report.pdf | markitdown -x .pdf     # from stdin; -x gives the format hint
markitdown https://en.wikipedia.org/wiki/Printing   # URLs work too
```

Flags worth knowing: `-x/--extension`, `-m/--mime-type`, `-c/--charset` (hints
when the source has no filename), `--keep-data-uris` (keeps base64 images
instead of truncating them), `-p/--use-plugins`, `--list-plugins`.
Full CLI reference: `references/cli.md`.

## Whole folder (the common case)

```bash
python3 scripts/md_batch.py ~/Documents/Specs -o ~/Documents/Specs_md
```

Walks the tree, converts every supported file, mirrors the folder structure,
skips files already converted, and writes `_manifest.csv` (source, output,
status, bytes, error) plus `_errors.log`. Useful options:

- `--ext .pdf .docx` — restrict to certain extensions
- `--flat` — dump all output into one folder instead of mirroring the tree
- `--overwrite` — redo files that already have a `.md`
- `--single out.md` — concatenate everything into one Markdown file with
  `# ---- <relative path> ----` separators (good for feeding one big context)
- `--workers 4` — parallel conversion
- `--plugins` — enable installed plugins (e.g. OCR)
- `--dry-run` — list what would be converted and stop

## Python

```python
from markitdown import MarkItDown

md = MarkItDown(enable_plugins=False)
result = md.convert("quote.xlsx")
print(result.markdown)        # .text_content is a soft-deprecated alias
print(result.title)
```

`convert()` dispatches on the source type: path/`Path` → `convert_local`,
`http:`/`https:`/`file:`/`data:` URI → `convert_uri`, `requests.Response` →
`convert_response`, binary stream → `convert_stream`. In untrusted contexts
call the **narrowest** method directly rather than `convert()`.
Full API, `StreamInfo` hints, custom converters and priorities:
`references/python-api.md`.

## Image descriptions and OCR

Built-in image handling extracts EXIF only, unless an LLM client is supplied:

```python
from markitdown import MarkItDown
from openai import OpenAI

md = MarkItDown(llm_client=OpenAI(), llm_model="gpt-4o")
print(md.convert("label_artwork.jpg").markdown)
```

For text *inside* images embedded in PDF/DOCX/PPTX/XLSX — including fully
scanned PDFs — install the `markitdown-ocr` plugin and pass the same
`llm_client`/`llm_model` with `enable_plugins=True`. See
`references/plugins-ocr.md`.

## Security — read before pointing this at anything untrusted

MarkItDown does I/O with the privileges of the calling process, exactly like
`open()` or `requests.get()`. It will read any file and fetch any URL the
process can reach. Sanitize inputs, and prefer `convert_stream()` /
`convert_local()` over the generic `convert()` when the source is not fully
trusted. The MCP server has no authentication — keep it bound to `localhost`.

## Files in this skill

```
references/install.md        Python versions, extras table, source/Docker install
references/cli.md            Every CLI flag, stdin/stdout, exit behaviour
references/python-api.md     MarkItDown class, convert_* methods, StreamInfo,
                             custom converters, writing a plugin
references/formats.md        Format → converter → required extra → caveats
references/plugins-ocr.md    Plugin system + markitdown-ocr in depth
references/azure.md          Document Intelligence and Content Understanding
references/mcp-server.md     markitdown-mcp: STDIO/HTTP/SSE, Docker, Claude Desktop
references/troubleshooting.md Common failures and fixes
scripts/md_batch.py          Batch/recursive folder converter with manifest
scripts/md_check.py          Environment and dependency checker
```
