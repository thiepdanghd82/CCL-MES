# Diagrify JSON schemas

| Schema | `diagram_type` | Structural arrays |
|---|---|---|
| `architecture.schema.json` | `architecture` | `components`, `boundaries?`, `connections` |
| `workflow.schema.json` | `workflow` | `lanes`, `phases?`, `groups?`, `mainPath?`, `nodes`, `edges` |
| `sequence.schema.json` | `sequence` | `participants`, `segments?`, `messages`, `activations?` |
| `lifecycle.schema.json` | `lifecycle` | `states`, `transitions` |
| `common.schema.json` | shared `$defs` only | `meta`, `view`, `card`, `id`, node/edge enums |

Every document requires `schema_version: 1`, `diagram_type`, `meta.title` and its structural arrays. `additionalProperties: false` applies everywhere, so a misspelled field is a `SCHEMA` diagnostic rather than a silent no-op.

The validator in `lib/schema.mjs` supports the subset used here: `type`, `required`, `properties`, `additionalProperties`, `enum`, `const`, `items`, `min/maxItems`, `min/maxLength`, `minimum/maximum`, `pattern`, local `$ref`, `oneOf`/`anyOf`. Schemas are loaded with `common.$defs` merged in, so they also work with any standard Draft 2020-12 validator after the same merge.

See `../references/authoring-guide.md` for field semantics.
