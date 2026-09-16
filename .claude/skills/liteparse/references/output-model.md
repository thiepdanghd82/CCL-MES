# Output model

Python names are snake_case; the Node/WASM programmatic API uses the camelCase
equivalent (`page_num` → `pageNum`). CLI JSON is snake_case everywhere.

## Coordinate space

All boxes are **top-left origin, 72-DPI viewport coordinates** — the same space
as `text_items`, regardless of the `dpi` used for rendering or OCR. So a bbox
maps onto a screenshot by scaling with `dpi / 72`.

## ParseResult

| Field | Type | Notes |
|---|---|---|
| `pages` | `list[ParsedPage]` | |
| `text` | `str` | whole document; rendered Markdown when `output_format="markdown"` |
| `total_pages` | `int` | pages in the source document |
| `images` | `list[ExtractedImage]` | empty unless `extract_images=True` |
| `screenshots` | `list[ScreenshotResult]` | populated by `extract_screenshots=True` |
| `image_error_count` | `int` | |
| `page_errors` | `list[PageError]` | filled when `continue_on_page_error=True` |
| `form_type` | `int \| None` | |
| `creator`, `producer` | `str \| None` | from PDF `/Info`; API-only, never in CLI JSON |
| `doc_meta` | `DocumentMetadata \| None` | needs `extract_document_metadata` |
| `xfa_packets` | `list[XfaPacket] \| None` | needs `extract_xfa_packets` |

## ParsedPage

`page_num`, `width`, `height`, `text`, `markdown`, `text_items`,
`complexity` (with `include_complexity`), `vector_graphics`, `annotations`,
`form_fields`, `structure_tree`, `blocks`, `content_bounds`.

Optional fields are `None` when their extraction flag is off.

## TextItem

Always present: `text`, `x`, `y`, `width`, `height`, `rotation`,
`words` (`list[WordBox]`, only with `emit_word_boxes`), `confidence` (OCR).

With `extract_text_metadata=True`: `font_name`, `font_size`, `font_height`,
`font_ascent`, `font_descent`, `font_weight`, `text_width`, `font_is_buggy`,
`mcid`, `fill_color`, `stroke_color`, `char_codes`,
`trailing_space_generated`.

`WordBox` is `text` + `x/y/width/height`.

## LayoutBlock (`extract_blocks=True`)

The Markdown renderer classifies each page into blocks and renders them; this
flag exposes that decomposition as data, in reading order — the same blocks the
Markdown was built from.

- `kind`: `heading` | `paragraph` | `list_item` | `code` | `table` |
  `grid_fallback` | `rule` | `figure`
- `bbox`: union of every source line feeding the block, so a wrapped heading
  reports its whole band
- kind-specific, omitted when not applicable: `text`, `level`, `bold`, `italic`
  (headings); `ordered`, `marker` (list items); `lines`, `lang` (code);
  `header`, `rows` (tables); `id`, `format` (figures)

Table cells are `LayoutCell` objects, not bare strings — each has `text` and its
own `bbox`, so a cell maps back to the region it was read from. For ruled tables
that box is the drawn grid cell; for borderless tables it is the extent of the
spans the cell was built from. Padding cells that only square off a ragged grid
carry no `bbox`, since there is no ink behind them.

```json
{
  "kind": "table",
  "bbox": { "x": 72.0, "y": 310.5, "width": 468.0, "height": 96.0 },
  "header": [
    { "text": "Territory Code", "bbox": { "x": 72.0, "y": 310.5, "width": 120.0, "height": 24.0 } },
    { "text": "Factor", "bbox": { "x": 192.0, "y": 310.5, "width": 96.0, "height": 24.0 } }
  ],
  "rows": [
    [
      { "text": "001", "bbox": { "x": 72.0, "y": 334.5, "width": 120.0, "height": 24.0 } },
      { "text": "1.25", "bbox": { "x": 192.0, "y": 334.5, "width": 96.0, "height": 24.0 } }
    ]
  ]
}
```

## PageComplexityStats

`page_number`, `text_length`, `text_coverage`, `has_substantial_images`,
`image_block_count`, `image_coverage`, `largest_image_coverage`,
`full_page_image`, `uncovered_vector_area`, `is_garbled`, `page_area`,
`needs_ocr`, `reasons`, `layout`.

`reasons` values: `scanned`, `no-text`, `sparse-text`, `embedded-images`,
`garbled`, `vector-text`, `annotation-text`.

## ScreenshotResult

`page_num`, `width`, `height`, `image_bytes` (PNG), `is_solid_fill`
(page rendered blank), `rects` (needs `detect_screenshot_rects`) — solid
same-color rectangles and lines detected in the raster, in viewport coords.
That last one is how you find table rules and boxes on a scanned or flattened
page that carries no vector paths.

## ExtractedImage (`extract_images=True`)

`id`, `name`, `path` (set when `image_output_dir` is given), `page`, `bbox`,
`width`, `height` (intrinsic pixels), `rotation`, `format`, `bytes`,
`duplicate_of`. Valid source JPEGs are preserved as-is and exact duplicates
reuse one file. **CLI JSON carries metadata only** — never base64 pixel data.

## VectorGraphics (`extract_vector_graphics=True`)

Off by default because path-heavy PDFs get large.

- `shapes`: path bbox, stroke/fill paint state and ARGB colors, and whether the
  path contains a Bezier curve
- `lines`: compatible horizontal/vertical segments merged using stroke width and
  paint color, in top-left 72-DPI viewport coords

Follows LlamaParse's PDFium path extraction, except the shape rectangle is
`bbox` (not PDFium's `coords`) and uses `width`/`height` (not `w`/`h`).
Diagonal and curved segments belong to their parent shape but are not emitted
as lines.

## StructureTree (`extract_structure_tree=True`)

Every root plus recursive elements: element type, ID, actual/alternate text,
title, typed scalar attributes, marked-content IDs, children, and referenced
link annotations. Untagged pages yield `roots: []`; the field is `None`/absent
when disabled.

## DocumentMetadata (`extract_document_metadata=True`)

`/Info` creation and modification dates, PDF version, encryption permissions,
signature state, incremental-save markers, trailer ID comparison, the catalog
XMP packet (capped at 64 KiB, with `xmp_truncated` when cut) and source file
size.

Off by default because it streams the whole source file once. It is `None` for
inputs converted from a non-PDF format — the facts would describe the
intermediate PDF, not your file. `xmp` needs a structural parse, so it is
skipped for sources over 16 MiB and in WASM builds; the other fields still
populate.

## Annotations and form fields

`extract_annotations` gives each page an `annotations` list: subtype, contents,
author/title, PDF date strings, viewport-space rectangle, quadpoint rectangles,
and URI for external links. It is independent of `extract_links`, which only
controls Markdown link rendering.

`extract_form_fields` adds AcroForm widget fields and values, repairing
orphaned widgets in memory.
