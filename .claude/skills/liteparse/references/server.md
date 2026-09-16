# REST and gRPC server

Lives in a separate repo: **run-llama/liteparse-server**, published as
`@llamaindex/liteparse-rest` (Express) and `@llamaindex/liteparse-grpc`.

```bash
npm install -g @llamaindex/liteparse-rest
liteparse-rest-server                 # listens on port 5707
# or, without installing:
npx @llamaindex/liteparse-rest
```

The published binary is the **slim** build: no Redis caching, no rate limiting,
no OpenTelemetry — and therefore no external dependencies. For observability,
caching and rate limiting, follow the repo's `examples/docker-compose` guide
(Redis, OpenTelemetry → Jaeger, Prometheus → Grafana).

## Docker

```bash
docker build -f slim.Dockerfile -t liteparse-rest .
docker run -p 5707:5707 liteparse-rest
```

API then at `http://localhost:5707`.

Not to be confused with the **core repo's** own Dockerfiles, which build the
`lit` CLI rather than a server — see `install.md`.

## Endpoints

Base URL `http://localhost:5707`.

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/parse` | Parse a file into JSON pages, text, or markdown |
| `POST` | `/screenshots` | Render pages to PNG, streamed as NDJSON |
| `POST` | `/is-complex` | Per-page complexity / OCR need |

Every endpoint takes multipart form fields `file` (required) and `config`
(optional, a JSON-serialized `LiteParseConfig`).

### POST /parse

Query parameters:

| Param | Type | Default | Effect |
|---|---|---|---|
| `text` | boolean | `false` | `true` → `text/plain` |
| `markdown` | boolean | `false` | `true` → `text/plain`, markdown-formatted |

Responses: `200 text/plain` (with `text=true` or `markdown=true`),
`200 application/json` with `{ "pages": [...] }` otherwise, `400` for a missing
`file`, `429` when rate-limited.

```bash
curl -F file=@document.pdf "http://localhost:5707/parse?markdown=true"
curl -F file=@document.pdf \
     -F 'config={"ocrEnabled":false,"maxPages":10}' \
     http://localhost:5707/parse
```

### POST /screenshots

Query parameter `pages` — comma-separated 1-based page numbers (default: all).

Response `200 application/x-ndjson`, one JSON object per line:

```json
{ "index": 0, "mimetype": "image/png", "data": "<base64>",
  "page_number": 1, "height": 1056, "width": 816 }
```

Streaming matters here: a 200-page render arrives incrementally instead of as
one enormous body.

```bash
curl -F file=@document.pdf "http://localhost:5707/screenshots?pages=1,2,3"
```

### POST /is-complex

Response `200 application/json`:

```json
{ "pages": [ { "pageNumber": 1, "textLength": 918, "textCoverage": 0.096,
               "hasSubstantialImages": false, "imageBlockCount": 0,
               "imageCoverage": 0, "largestImageCoverage": 0,
               "fullPageImage": false, "isGarbled": false,
               "pageArea": 484704, "needsOcr": true,
               "reasons": ["sparse-text"] } ] }
```

Note the server speaks **camelCase** here, unlike the `lit` CLI's snake_case
JSON.

## gRPC

`@llamaindex/liteparse-grpc` in the same repo exposes the same capabilities
over gRPC, with a matching client. See its own README for the proto and client
usage.

## Test script

`scripts/server-test.py` in the server repo is self-contained (uses `uv` to
manage its own `httpx` dependency):

```bash
./scripts/server-test.py file path/to/document.pdf            # JSON pages
./scripts/server-test.py file path/to/document.pdf text       # plain text
./scripts/server-test.py file path/to/document.pdf markdown   # markdown
```

## When a server is the right shape

- A desktop app (Electron/Tauri) that cannot ship the native module — run the
  server as a sidecar instead of bundling `.node` binaries per platform
- Several services sharing one parsing host, so LibreOffice and tessdata are
  installed once
- Language boundaries — anything that can POST a file can parse

For a single script or a batch job, skip it: the CLI or the in-process bindings
are faster and simpler.
