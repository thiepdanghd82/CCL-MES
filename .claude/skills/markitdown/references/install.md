# Installation

## Requirements

- Python **3.10 or higher** (3.12 recommended)
- A virtual environment is strongly recommended — MarkItDown's `[all]` extra
  pulls in pandas, lxml, pdfplumber, python-pptx and the Azure SDKs.

## Virtual environment

```bash
# stdlib venv
python3 -m venv .venv && source .venv/bin/activate

# uv (use `uv pip install`, not plain pip, inside a uv venv)
uv venv --python=3.12 .venv && source .venv/bin/activate

# conda
conda create -n markitdown python=3.12 && conda activate markitdown
```

## PyPI vs source

PyPI and the GitHub `main` branch are both at 0.1.7, so the PyPI install is the
normal choice. If `markitdown --version` reports something older, you are almost
certainly behind a corporate PyPI mirror — check with
`pip index versions markitdown`, and install from source if the mirror is stuck.

## From PyPI

```bash
pip install 'markitdown[all]'          # everything
pip install 'markitdown[pdf,docx,pptx]' # only what you need
```

## From source

```bash
git clone https://github.com/microsoft/markitdown.git
cd markitdown
pip install -e 'packages/markitdown[all]'
```

## Optional dependency extras

| Extra | Enables | Pulls in |
|---|---|---|
| `all` | everything below | all of the below |
| `pptx` | PowerPoint `.pptx` | python-pptx |
| `docx` | Word `.docx` | mammoth ~1.11, lxml |
| `xlsx` | Excel `.xlsx` | pandas, openpyxl |
| `xls` | legacy Excel `.xls` | pandas, xlrd |
| `pdf` | PDF | pdfminer.six, pdfplumber |
| `outlook` | Outlook `.msg` | olefile |
| `audio-transcription` | speech-to-text for `.wav` / `.mp3` | pydub, SpeechRecognition |
| `youtube-transcription` | YouTube transcripts | youtube-transcript-api |
| `az-doc-intel` | Azure Document Intelligence | azure-ai-documentintelligence, azure-identity |
| `az-content-understanding` | Azure Content Understanding | azure-ai-contentunderstanding>=1.2.0b1, azure-identity |

Core dependencies always installed: beautifulsoup4, requests, markdownify,
magika (content-type detection), charset-normalizer, defusedxml.

## Optional external binary — exiftool

Image and audio converters read EXIF/ID3 metadata through **exiftool** if it is
present. Without it, metadata is simply omitted; nothing errors.

```bash
brew install exiftool          # macOS
sudo apt install libimage-exiftool-perl   # Debian/Ubuntu
```

MarkItDown finds it via the `EXIFTOOL_PATH` env var, then `shutil.which()`
restricted to well-known directories (`/usr/bin`, `/usr/local/bin`, `/opt`,
`/opt/homebrew/bin`, `C:\Windows\System32`, `C:\Program Files`…). You can also
pass `exiftool_path=` to `MarkItDown()`.

## Docker

```bash
docker build -t markitdown:latest .
docker run --rm -i markitdown:latest < ~/your-file.pdf > output.md
```

The image reads from stdin and writes to stdout, so no volume mount is needed
for one-off conversions.

## Companion packages

```bash
pip install markitdown-mcp     # MCP server exposing convert_to_markdown(uri)
pip install markitdown-ocr     # LLM-vision OCR plugin
pip install openai             # or any OpenAI-compatible client, for OCR / captions
```
