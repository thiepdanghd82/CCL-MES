# Browser / WASM

```bash
npm install @llamaindex/liteparse-wasm
```

Runs entirely in the browser. No server, no cloud calls, no native binary.

```ts
import init, { LiteParse } from "@llamaindex/liteparse-wasm";

await init();                       // load the wasm module shipped with the package

const parser = new LiteParse({
  ocrEnabled: false,                // native OCR is unavailable here — see below
  outputFormat: "json",
});

const bytes = new Uint8Array(await file.arrayBuffer());   // fetch / File / drag-drop
const result = await parser.parse(bytes);

console.log(result.text);
console.log(result.pages[0]);       // per-page items with bboxes
```

Input is always a `Uint8Array` — there is no filesystem.

## Config options

All optional, camelCase:

| Option | Type | Default | Description |
|---|---|---|---|
| `ocrLanguage` | `string` | `"eng"` | Passed to the OCR engine |
| `ocrEnabled` | `boolean` | `true` | Run OCR on text-sparse pages |
| `maxPages` | `number` | `1000` | Stop after this many pages |
| `targetPages` | `string` | — | e.g. `"1-5,10,15-20"` |
| `extractScreenshots` | `boolean` | `false` | PNG bytes on `result.screenshots` |
| `dpi` | `number` | `150` | Render DPI for OCR / screenshots |
| `outputFormat` | `"json" \| "text" \| "markdown"` | `"json"` | `"markdown"` returns rendered Markdown on `result.text` |
| `imageMode` | `"off" \| "placeholder" \| "embed"` | `"placeholder"` | Raster image handling in markdown |
| `extractLinks` | `boolean` | `true` | `[text](url)` in markdown |
| `extractVectorGraphics` | `boolean` | `false` | Shapes + merged H/V lines |
| `extractAnnotations` | `boolean` | `false` | Annotations with geometry |
| `extractStructureTree` | `boolean` | `false` | Tagged-PDF structure |
| `preserveVerySmallText` | `boolean` | `false` | Keep tiny text |
| `password` | `string` | — | For protected PDFs |
| `quiet` | `boolean` | `false` | Suppress progress logging |
| `ocrEngine` | `object` | — | JS-side OCR engine, see below |

## OCR in the browser

The native Tesseract and HTTP-OCR backends do not exist in WASM. To get OCR,
supply an object with a `recognize` method:

```ts
const parser = new LiteParse({
  ocrEnabled: true,
  ocrLanguage: "eng",
  ocrEngine: {
    /**
     * @param imageData PNG-encoded image bytes
     * @param width     rendered page width in pixels
     * @param height    rendered page height in pixels
     * @param language  e.g. "eng"
     * @returns array of { text, bbox: [x1, y1, x2, y2], confidence }
     */
    async recognize(imageData, width, height, language) {
      return [{ text: "Hello", bbox: [10, 20, 80, 40], confidence: 0.98 }];
    },
  },
});
```

Typical implementation: a Web Worker wrapping tesseract.js, or a fetch to a
remote OCR service. The return shape matches the HTTP OCR API spec (`ocr.md`),
so one adapter can serve both.

## Complexity check

```ts
const parser = new LiteParse({ ocrEnabled: false });
const pages = await parser.isComplex(new Uint8Array(await file.arrayBuffer()));

for (const page of pages.filter((p) => p.needsOcr)) {
  console.log(`Page ${page.pageNumber}: ${page.reasons.join(", ")}`);
}
```

Cheap way to decide whether wiring up the JS OCR engine is worth it for a given
upload.

## WASM limitations vs the native builds

- No Tesseract, no HTTP OCR backend — only a JS `ocrEngine`
- No LibreOffice, so **PDF and images only**; no `.docx`/`.xlsx`/`.pptx`
- No worker pool, no `parseTimeoutMs`
- No `lit` CLI
- `docMeta.xmp` is skipped in WASM builds (other metadata fields still populate)

## Building from source

Requires Rust and [`wasm-pack`](https://rustwasm.github.io/wasm-pack/):

```sh
cd packages/wasm
npm run build            # web target (default)
npm run build:bundler    # webpack / rollup / vite
npm run build:nodejs     # node.js
```

Output lands in `pkg/`.
