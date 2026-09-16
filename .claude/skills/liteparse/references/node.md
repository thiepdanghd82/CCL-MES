# Node / TypeScript API

```bash
npm i @llamaindex/liteparse       # add -g to get the `lit` CLI on PATH
```

```typescript
import { LiteParse } from '@llamaindex/liteparse';

const parser = new LiteParse();
const result = await parser.parse('document.pdf');
console.log(result.text);
console.log(`pages: ${result.totalPages}`);

for (const page of result.pages) {
  console.log(`page ${page.pageNum}: ${page.textItems.length} items`);
}
```

CommonJS works too:

```javascript
const { LiteParse } = require('@llamaindex/liteparse');
(async () => {
  const result = await new LiteParse().parse('document.pdf');
  console.log(result.text);
})();
```

Buffers and `Uint8Array` are accepted directly:

```typescript
import { readFile } from 'fs/promises';
const result = await parser.parse(await readFile('document.pdf'));
```

## camelCase vs snake_case — the trap

The **programmatic API is camelCase** (`totalPages`, `textItems`,
`extractImages`). The **CLI JSON output is snake_case in every binding**,
including the Node CLI, so it matches the Rust CLI schema. Code that shells out
to `lit` and code that calls the library see different key styles.

## Configuration

```typescript
const parser = new LiteParse({
  ocrEnabled: true,
  ocrLanguage: 'eng',
  ocrServerUrl: undefined,
  tessdataPath: undefined,
  maxPages: 1000,
  targetPages: '1-5,10',
  extractScreenshots: false,
  continueOnPageError: false,
  dpi: 150,
  outputFormat: 'json',          // 'json' | 'text' | 'markdown'
  imageMode: 'placeholder',      // 'placeholder' | 'off' | 'embed'
  extractImages: false,
  imageOutputDir: './images',
  extractLinks: true,
  keepHeadersFooters: false,
  extractVectorGraphics: false,
  extractAnnotations: false,
  extractFormFields: false,
  extractStructureTree: false,
  extractXfaPackets: false,
  extractContentBounds: false,
  extractDocumentMetadata: false,
  detectScreenshotRects: false,
  preserveVerySmallText: false,
  extractTextMetadata: false,
  password: undefined,
  quiet: false,
  numWorkers: 4,
});
```

## Markdown

```typescript
const parser = new LiteParse({
  outputFormat: 'markdown',
  imageMode: 'placeholder',
  extractLinks: true,
});
console.log((await parser.parse('document.pdf')).text);
```

## Bounded-memory parsing — `parseBatches`

For documents with very many text items, consume page batches without holding
the whole result:

```typescript
for await (const batch of parser.parseBatches('large.pdf', { batchSize: 20 })) {
  await processPages(batch.result.pages);   // batch.startPage … batch.endPage
}
```

Each batch is an ordinary parse result for its page range and becomes
collectible as soon as you advance the iterator. A non-PDF source is converted
once, not per batch.

**Caveat:** cross-page passes only see their own batch, so repeated
header/footer removal and image deduplication become batch-local and the output
can differ from `parse()`. Prefer `parse()` unless the materialized result size
is the actual problem.

## Complexity routing

```typescript
const pages = await parser.isComplex('document.pdf');
if (pages.some((p) => p.needsOcr)) {
  await parser.parse('document.pdf');
} else {
  await new LiteParse({ ocrEnabled: false }).parse('document.pdf');
}
for (const p of pages.filter((p) => p.needsOcr)) {
  console.log(`page ${p.pageNumber}: ${p.reasons.join(', ')}`);
}
```

## Screenshots

```typescript
const shots = parser.screenshot('document.pdf', [1, 2, 3]);
for (const s of shots) {
  console.log(s.pageNum, s.width, s.height, s.isSolidFill);  // s.imageBuffer = PNG
}
```

With `detectScreenshotRects: true` each screenshot also reports `rects` — solid
same-color rectangles and lines found in the raster, in viewport coords. That
covers scanned or flattened pages that carry no vector paths.

## Worker pool and hard timeouts

PDFium serializes all in-process parses on a global lock — even `Promise.all`
over several `parse()` calls runs them one at a time. Use a process pool:

```typescript
import { LiteParse, ParseTimeoutError } from '@llamaindex/liteparse';

const parser = new LiteParse({ poolSize: 4, parseTimeoutMs: 15_000 });
await parser.warmUp();                       // ~45 ms

try {
  const result = await parser.parse('document.pdf');
} catch (e) {
  if (e instanceof ParseTimeoutError) {
    console.warn(`killed rogue document: ${e.source} (deadline ${e.timeoutMs}ms)`);
  }
}

parser.close();     // frees workers; an idle pool never blocks process exit
```

## Electron / Tauri note

The npm package ships a **native** binary per platform, so an Electron app must
unpack it (`asarUnpack`) and ship the right build per target. If bundling the
native module is not workable, the alternatives are the WASM package
(`wasm.md`) or running the REST server as a sidecar (`server.md`).
