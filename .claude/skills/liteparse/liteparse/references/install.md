# Installation

LiteParse is a Rust core shipped as prebuilt native binaries. All installs
except WASM include the same `lit` CLI.

| Target | Install | Notes |
|---|---|---|
| Python | `pip install liteparse` | PyO3 bindings + `lit` |
| Node / TypeScript | `npm i @llamaindex/liteparse` (`-g` for the CLI on PATH) | napi-rs bindings + `lit` |
| Rust library | `cargo add liteparse` | |
| Rust CLI | `cargo install liteparse` | builds from source |
| Browser | `npm i @llamaindex/liteparse-wasm` | no CLI, no native OCR |
| REST/gRPC server | `npm i -g @llamaindex/liteparse-rest` | separate repo, see `server.md` |

Platforms: Linux, macOS (Intel and Apple Silicon), Windows.

## LibreOffice — needed for Office formats

Office and OpenDocument files are converted to PDF first, by LibreOffice. With
LibreOffice absent, those formats fail; PDFs and images are unaffected.

```bash
brew install --cask libreoffice          # macOS
sudo apt-get install libreoffice         # Ubuntu/Debian
choco install libreoffice-fresh          # Windows
```

On Windows add `C:\Program Files\LibreOffice\program` to PATH.

Covered by this path: `.doc .docx .docm .odt .rtf .pages`,
`.ppt .pptx .pptm .odp .key`, `.xls .xlsx .xlsm .ods .csv .tsv .numbers`.

Images (`.jpg .jpeg .png .gif .bmp .tiff .webp .svg`) are converted natively in
Rust — since v2.8.0 ImageMagick is no longer required.

## Tesseract — read this before trusting "bundled"

The Tesseract *engine* is compiled in, but the **language data is not**. On the
first OCR run for a language, LiteParse downloads
`<lang>.traineddata` from `github.com/tesseract-ocr/tessdata_best`. On a machine
without access to GitHub the parse fails outright:

```
ParseError: OCR failed: OCR failed for all 1 page(s): failed to download
tessdata for language "eng" from https://github.com/tesseract-ocr/tessdata_best/
raw/main/eng.traineddata: HTTP 403 Forbidden
```

So on an offline, air-gapped or egress-filtered machine you must pre-stage the
language data — or run with `--no-ocr`:

```bash
export TESSDATA_PREFIX=/path/to/tessdata
lit parse document.pdf --ocr-language eng
# or per-invocation
lit parse document.pdf --tessdata-path /path/to/tessdata
```

Language codes are Tesseract's three-letter form (`eng`, `vie`, `chi_sim`,
`jpn`, `fra`), **not** the two-letter ISO codes used by the HTTP OCR API.

## Environment variables

| Variable | Effect |
|---|---|
| `TESSDATA_PREFIX` | Directory of Tesseract `.traineddata` files, for offline use |

## Docker

The repo ships two Dockerfiles:

```bash
docker build -t liteparse .              # slim: lit + pdfium + tesseract-eng
docker build -f full.Dockerfile -t liteparse-full .   # adds LibreOffice
```

Both are multi-stage: a `rust:1-bookworm` builder (needs `libclang-dev`,
`libtesseract-dev`, `libleptonica-dev`, `cmake`, `g++`) producing
`/usr/local/bin/lit` on `debian:bookworm-slim`, with the PDFium shared library
at `/usr/local/lib/pdfium-rs` and `LD_LIBRARY_PATH` set accordingly.
`liteparse` is symlinked to `lit`. Use the **full** image whenever Office
formats are in scope.

## Building from source

```bash
cargo build --release -p liteparse       # CLI
cd packages/node   && npm run build      # Node bindings
cd packages/python && maturin develop --release   # Python bindings
cd packages/wasm   && npm run build      # WASM
```

Workspace layout: `crates/liteparse` (core + CLI), `crates/liteparse-napi`,
`crates/liteparse-python`, `crates/liteparse-wasm`, `crates/pdfium`,
`crates/pdfium-sys`; `packages/{node,python,wasm}` are the publishable wrappers.

## Version pinning

Tags are per-artifact: `python-vX.Y.Z`, `node-vX.Y.Z`, `crates-vX.Y.Z`,
`wasm-vX.Y.Z`, `docker-vX.Y.Z`. They advance together, so `python-v2.14.2` and
`node-v2.14.2` are the same core. License: Apache-2.0.
