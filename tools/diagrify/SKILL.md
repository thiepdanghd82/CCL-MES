---
name: diagrify
description: Create validated architecture, workflow (swim-lane process), sequence and lifecycle/state diagrams as a single self-contained interactive HTML file — inline SVG, dark/light theme, search, focus, upstream/downstream tracing, guided views, optional trace animation, PNG/SVG export. Accepts a plain-language description, a pasted Mermaid flowchart / sequenceDiagram / stateDiagram, or an SOP/process document. Use when the user asks to draw or visualize a system architecture, plant/IT systems map, manufacturing or NPI process flow, approval workflow, API call sequence, order/sample/ticket lifecycle, state machine, or to convert Mermaid into a polished shareable diagram.
license: MIT
metadata:
  version: "0.1"
  author: Henry Dang
  inspired_by: tt-a1i/archify (MIT)
---

# Diagrify

Turn a short typed JSON specification into one polished, interactive HTML diagram. The agent authors JSON; Diagrify validates it, renders SVG deterministically, checks the geometry, and only then writes the file. No install, no runtime dependencies — Node.js ≥ 18 is enough.

## Fast path (do this for every request)

1. **Pick the type** from the question. When unsure run `node bin/diagrify.mjs guide "<scenario>" --json`.

   | Type | Use for |
   |---|---|
   | `architecture` | Components, systems, machines, stores and the boundaries around them (IT map, plant systems, cloud topology) |
   | `workflow` | Ordered steps across departments / lanes with gates, branches and rework loops (NPI, approvals, SOP, CI/CD, runbooks) |
   | `sequence` | A handful of participants exchanging messages over time (API calls, FAI sign-off, order confirmation) |
   | `lifecycle` | One thing moving through states, waits, retries and terminal outcomes (sample request, ticket, deployment) |

2. **Read exactly two files**: `schemas/<type>.schema.json` and one matching example in `examples/`. Use the example for field *shape*, never for facts. Author fresh ids, wording and layout for the user's domain.

3. **Write the candidate JSON first** (the next tool action after reading). Keep it small: one obvious main path, short side branches, ≤ 12 primary nodes, short labels (node label ≤ 18 characters, sublabel ≤ 18, edge label ≤ 3 words). Put detail into `cards`, not into more nodes. Do not add `via`, `labelAt`, `labelDx`, `labelDy`, `fromSide`, `toSide` or `route` until a diagnostic asks for one.

4. **Validate after every edit**:

   ```bash
   node bin/diagrify.mjs validate <type> <candidate.json> --json
   ```

   A pass reports `"ok": true` with `schema`, `render`, `geometry` and `viewBox` all `true`. On failure, change only the diagnosed `subject`, using one of its `supportedFixes`, then rerun. Stop after two rounds that do not reduce the error count and report the remaining diagnostics honestly. Warnings (`LABEL_OVER_EDGE`, `ISOLATED_NODE`, `LARGE_CANVAS`) do not block delivery but are worth one fix attempt.

5. **Deliver** once, as the final acceptance step:

   ```bash
   node bin/diagrify.mjs deliver <type> <candidate.json> <output.html> --json [--open]
   ```

   Delivery re-runs every check, writes the HTML atomically and returns SHA-256 + byte counts for both the specification and the artifact. A non-zero exit is never success; a failed delivery leaves any previous output untouched.

6. **Optional browser evidence** (needs `npm i -D playwright` once):

   ```bash
   node bin/diagrify.mjs visual-check <output.html> --json
   ```

   It opens the delivered file at 1440×900 and 1920×1080 in both themes, checks for horizontal overflow and saves screenshots next to the file. Screenshots are evidence for a human or image-capable reviewer; they do not replace looking at them.

## Diagnostic codes

| Code | Meaning | Repair |
|---|---|---|
| `SCHEMA` | Unknown field, wrong type, bad enum, length limit | Fix the named field |
| `UNKNOWN_REF` / `DUPLICATE_ID` / `SELF_LOOP` | Ids do not line up | Fix the id |
| `SLOT_COLLISION` | Two workflow nodes share a lane + column, or two states share a row + column | Move one node |
| `NODE_OVERLAP` | Architecture components closer than 24 px | Move one component |
| `MAINPATH_GAP` | `mainPath` names two nodes with no edge between them | Add the edge or fix the order |
| `NO_START` / `NO_TERMINAL` | Lifecycle without start or terminal state | Add the state |
| `TEXT_OVERFLOW` | Label or sublabel wider than the node | Shorten wording; architecture may widen `size` |
| `EDGE_CROSSES_NODE` | A route passes through an unrelated node | Move the node; set `route: "above"/"below"` (workflow, lifecycle) or `fromSide`/`toSide`/`via` (architecture) |
| `LABEL_OVER_NODE` / `LABEL_COLLISION` | An edge label overlaps a node or another label | Set `labelAt` (0.05–0.95 along the route) or `labelDx` / `labelDy` |

## Authoring rules

- **One language** for authored content, chosen from the user's request. `meta.locale` (`en`, `vi`, `zh-CN`) only translates the fixed viewer chrome (buttons, legend, guide); it never translates your content. Omit it for English.
- **Node types** (`type`): `frontend`, `backend`, `database`, `cloud`, `security`, `messagebus`, `external`, `process`, `machine`, `quality`, `storage`, `people`, `document`. Lifecycle states use `start`, `active`, `waiting`, `success`, `failure`, `neutral`.
- **Edge variants** (`variant`): `default`, `emphasis` (main path), `security` (control / exception), `dashed` (optional / async). Workflow edges on `mainPath` become `emphasis` automatically; lifecycle transitions into a `failure` state become `security` automatically.
- **Workflow layout** is automatic from `lane` + `col`. Columns read left to right in time order; every lane/column cell holds at most one node. Forward edges route through column gaps; backward edges use the free strip at the top or bottom of the source lane. `phases` label column ranges above the lanes, `groups` draw a dashed box inside one lane.
- **Lifecycle layout**: `row: 0` is the main rail (left to right), rows 1–4 hold branches and terminal states beneath the state they leave.
- **Architecture layout** is explicit: `pos: [x, y]` (top-left) and optional `size: [w, h]` (default 140×60). Keep ≥ 24 px clear gap and leave ≥ 90 px between nodes that share a labelled edge. Boundaries wrap listed component ids automatically.
- **Sequence**: participants left to right in the order they first act; messages top to bottom. `kind: "return"` draws a dashed reply, `kind: "async"` a dashed one-way message. `segments` draw alt / loop / opt frames around a message range.
- `meta.views` (≤ 5) are curated chapters: each names existing node ids and a one-line note. Add them when the diagram has more than one story to tell.
- `meta.animation: "trace"` is opt-in; only enable it for a demo or presentation. `meta.theme` sets the initial theme (default dark); the reader can switch at any time.
- Never invent facts. If the user did not state a relationship, ask or leave it out; mention gaps in the reply.

## Mermaid input

Read Mermaid for topology and meaning, then author fresh Diagrify JSON: `flowchart`/`graph` → `workflow` (or `architecture` for a component map), `sequenceDiagram` → `sequence`, `stateDiagram` → `lifecycle`. Do not copy Mermaid styling.

## Output

Report: the delivered HTML path, diagram type, the validation summary (counts + any remaining warnings), the specification/artifact SHA-256 from the receipt, whether `visual-check` ran, and what you did or did not visually review. Mention every fact you had to assume.

See `references/authoring-guide.md` only when you need field-by-field detail or the viewer's keyboard / deep-link contract.
