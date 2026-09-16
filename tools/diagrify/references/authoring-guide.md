# Diagrify authoring guide

Field-by-field reference for the four JSON document types, plus the viewer contract. Read `SKILL.md` first; this file is for detail.

## Common `meta`

| Field | Type | Notes |
|---|---|---|
| `title` | string ≤ 80 | Required. Page title and `<h1>`. |
| `subtitle` | string ≤ 140 | Optional supporting line. Never restate the title. |
| `animation` | `none` \| `trace` | `trace` adds a Play button and `?play=1` support. Static is the default. |
| `theme` | `dark` \| `light` | Initial theme. Readers can toggle; `?theme=light` overrides. |
| `locale` | `en` \| `vi` \| `zh-CN` | Viewer chrome language only. |
| `legend` | `auto` \| `hidden` | `auto` lists only kinds present in the diagram. |
| `quality_profile` | `standard` \| `showcase` | Informational for now; showcase is the expectation for delivered artifacts. |
| `viewBox` | `[w, h]` | Architecture only: force a canvas size instead of the automatic bounding box. |
| `views` | ≤ 5 × `{ id, label, focus[], note? }` | Guided chapters; `focus` lists node ids. |
| `cards` | ≤ 4 × `{ dot?, title, items[] }` | Conclusion cards under the diagram; `dot` ∈ cyan, emerald, amber, rose, violet, slate. |

Ids match `^[a-z][a-z0-9_-]{0,47}$` and must be unique inside their array.

## Architecture

```json
{ "schema_version": 1, "diagram_type": "architecture", "meta": { "title": "…" },
  "components": [ { "id": "api", "type": "backend", "label": "API", "sublabel": "FastAPI", "tag": "v2", "pos": [400, 200], "size": [140, 60], "variant": "emphasis" } ],
  "boundaries": [ { "kind": "region", "label": "Plant network", "wraps": ["api"], "padding": 28 } ],
  "connections": [ { "id": "c1", "from": "users", "to": "api", "label": "HTTPS", "variant": "emphasis", "fromSide": "right", "toSide": "left", "via": [[300, 230]], "labelAt": 0.5, "labelDx": 0, "labelDy": 0 } ] }
```

- Ports are chosen automatically from the relative position of the two boxes; several edges on one side are spread so they never share a port.
- `via` waypoints are hints: Diagrify inserts the missing corner whenever two consecutive points are not axis-aligned.
- Boundary `kind` ∈ `region`, `zone`, `security-group`, `site`, `department` (colour only).
- Canvas = bounding box of components and boundaries + 40 px; keep it under 2400×1600.

## Workflow

```json
{ "schema_version": 1, "diagram_type": "workflow", "meta": { "title": "…" },
  "lanes": [ { "id": "sales", "label": "Sales", "variant": "emphasis" } ],
  "phases": [ { "id": "p1", "label": "Inquiry", "fromCol": 0, "toCol": 1 } ],
  "groups": [ { "id": "g1", "label": "Cost model", "lane": "npi", "fromCol": 2, "toCol": 3, "variant": "emphasis" } ],
  "mainPath": ["inquiry", "quote"],
  "nodes": [ { "id": "inquiry", "lane": "sales", "col": 0, "type": "people", "label": "RFQ", "sublabel": "spec · qty", "tag": "gate", "shape": "rect" } ],
  "edges": [ { "id": "e1", "from": "inquiry", "to": "quote", "label": "review", "variant": "default", "role": "branch", "route": "auto" } ] }
```

- Grid: 200 px per column, 120 px per lane, nodes 136×56. Lane header is 150 px wide.
- `route`: `auto` (default), `above`, `below`. Corridor routes leave the node vertically, travel in the lane's free strip and enter the target through a column gap.
- Lane `variant` ∈ `default`, `emphasis`, `exception` (tints the lane header).
- `role` ∈ `main`, `branch`, `return`, `error` is semantic metadata exposed as a CSS class (`r-error`).

## Sequence

```json
{ "schema_version": 1, "diagram_type": "sequence", "meta": { "title": "…" },
  "participants": [ { "id": "qa", "type": "quality", "label": "QA", "sublabel": "FAI" } ],
  "segments": [ { "id": "s1", "kind": "alt", "label": "customer decision", "fromMessage": "m6", "toMessage": "m9" } ],
  "messages": [ { "id": "m1", "from": "qa", "to": "npi", "label": "FAI report", "kind": "sync", "variant": "default", "note": "PDF + samples" } ],
  "activations": [ { "participant": "qa", "fromMessage": "m1", "toMessage": "m4" } ] }
```

- Messages are drawn in array order, 48 px apart; a self-message (`from == to`) draws a small loop.
- `kind` ∈ `sync`, `async` (dashed), `return` (dashed). `variant` overrides the colour.
- Segment `kind` ∈ `alt`, `loop`, `opt`, `par`, `note`.

## Lifecycle

```json
{ "schema_version": 1, "diagram_type": "lifecycle", "meta": { "title": "…" },
  "states": [ { "id": "new", "type": "start", "label": "New", "sublabel": "RFQ received", "col": 0, "row": 0, "tag": "customer" } ],
  "transitions": [ { "id": "t1", "from": "new", "to": "review", "label": "assign", "variant": "default", "route": "auto" } ] }
```

- Grid: 250 px per column, 132 px per row, nodes 144×58. `row` defaults to 0 (main rail).
- Start / success / failure states are pills; others are rectangles.
- Transitions along the main rail become `emphasis`; transitions into a `failure` state become `security`.

## Viewer contract

| Action | Control |
|---|---|
| Focus a node and its direct relationships | click, or Enter on a focused node |
| Trace authored reach | focus → **Upstream** / **Downstream** |
| Guided view | `Guided views…` select |
| Search | `/` then type |
| Theme | `T` or the Theme button; remembered per browser |
| Export | `E` → PNG (2×), copy PNG, SVG (theme CSS embedded), JSON source |
| Play trace motion | `P` (only when `meta.animation` is `trace`) |
| Zoom / pan / reset | wheel · drag · `+` `-` `0` · double-click |
| Diagram guide | `?` |
| Clear | `Esc` |

Deep links: `#focus=<id>`, `#focus=<id>&reach=upstream|downstream`, `#view=<view-id>`; query `?theme=dark|light`, `?play=1`.

Every highlight reuses authored nodes and relationships; the viewer never infers topology, impact or runtime behaviour.
