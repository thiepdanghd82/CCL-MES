import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateAgainstSchema } from './schema.mjs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const SCHEMA_DIR = path.join(__dirname, '..', 'schemas');

export const DIAGRAM_TYPES = ['architecture', 'workflow', 'sequence', 'lifecycle'];

export function loadSchema(type) {
  if (!DIAGRAM_TYPES.includes(type)) throw new Error(`Unknown diagram type "${type}". Expected one of ${DIAGRAM_TYPES.join(', ')}.`);
  const common = JSON.parse(fs.readFileSync(path.join(SCHEMA_DIR, 'common.schema.json'), 'utf8'));
  const schema = JSON.parse(fs.readFileSync(path.join(SCHEMA_DIR, `${type}.schema.json`), 'utf8'));
  return { ...schema, $defs: { ...common.$defs, ...(schema.$defs || {}) } };
}

function diag(code, subject, message, fixes = []) {
  return { code, subject, message, supportedFixes: fixes };
}

// Schema + semantic validation. Returns { ok, diagnostics }.
export function validateDiagram(type, data) {
  const diagnostics = [];
  const schema = loadSchema(type);
  for (const err of validateAgainstSchema(schema, data)) {
    diagnostics.push(diag('SCHEMA', err.path, err.message, ['Edit the named field so it matches the schema.']));
  }
  if (diagnostics.length) return { ok: false, diagnostics };
  if (data.diagram_type !== type) {
    diagnostics.push(diag('TYPE_MISMATCH', '$.diagram_type', `diagram_type is "${data.diagram_type}" but the command asked for "${type}".`, ['Run the command with the matching type.']));
    return { ok: false, diagnostics };
  }

  const checkers = { architecture: checkArchitecture, workflow: checkWorkflow, sequence: checkSequence, lifecycle: checkLifecycle };
  checkers[type](data, diagnostics);
  checkViews(data, diagnostics, nodeIdsOf(type, data));
  return { ok: diagnostics.length === 0, diagnostics };
}

export function nodeIdsOf(type, data) {
  const list = { architecture: data.components, workflow: data.nodes, sequence: data.participants, lifecycle: data.states }[type] || [];
  return new Set(list.map((n) => n.id));
}

function uniqueIds(items, subject, diagnostics) {
  const seen = new Set();
  for (const [i, item] of items.entries()) {
    if (seen.has(item.id)) diagnostics.push(diag('DUPLICATE_ID', `${subject}[${i}].id`, `id "${item.id}" is used more than once.`, ['Give each item a unique id.']));
    seen.add(item.id);
  }
}

function refsExist(edges, ids, subject, diagnostics) {
  for (const [i, e] of edges.entries()) {
    for (const key of ['from', 'to']) {
      if (!ids.has(e[key])) diagnostics.push(diag('UNKNOWN_REF', `${subject}[${i}].${key}`, `"${e[key]}" does not name an existing node.`, ['Point the edge at an existing node id, or add that node.']));
    }
    if (e.from === e.to) diagnostics.push(diag('SELF_LOOP', `${subject}[${i}]`, `"${e.id}" connects "${e.from}" to itself, which no renderer draws.`, ['Remove the self-loop or express it as a label on the node.']));
  }
}

function checkViews(data, diagnostics, ids) {
  for (const [i, view] of (data.meta.views || []).entries()) {
    for (const [j, id] of view.focus.entries()) {
      if (!ids.has(id)) diagnostics.push(diag('UNKNOWN_REF', `$.meta.views[${i}].focus[${j}]`, `"${id}" does not name an existing node.`, ['Use only ids that exist in the diagram.']));
    }
  }
}

function checkArchitecture(data, diagnostics) {
  uniqueIds(data.components, '$.components', diagnostics);
  uniqueIds(data.connections, '$.connections', diagnostics);
  const ids = nodeIdsOf('architecture', data);
  refsExist(data.connections, ids, '$.connections', diagnostics);
  const boxes = data.components.map((c) => ({ id: c.id, x: c.pos[0], y: c.pos[1], w: c.size?.[0] ?? 140, h: c.size?.[1] ?? 60 }));
  for (let i = 0; i < boxes.length; i += 1) {
    for (let j = i + 1; j < boxes.length; j += 1) {
      const a = boxes[i]; const b = boxes[j];
      const gap = 24;
      const overlap = a.x < b.x + b.w + gap && a.x + a.w + gap > b.x && a.y < b.y + b.h + gap && a.y + a.h + gap > b.y;
      if (overlap) diagnostics.push(diag('NODE_OVERLAP', `$.components[${j}].pos`, `"${b.id}" is closer than ${gap}px to "${a.id}".`, ['Move one component so the clear gap is at least 24px.']));
    }
  }
  for (const [i, b] of (data.boundaries || []).entries()) {
    for (const [j, id] of b.wraps.entries()) {
      if (!ids.has(id)) diagnostics.push(diag('UNKNOWN_REF', `$.boundaries[${i}].wraps[${j}]`, `"${id}" does not name an existing component.`, ['Use only component ids.']));
    }
  }
}

function checkWorkflow(data, diagnostics) {
  uniqueIds(data.lanes, '$.lanes', diagnostics);
  uniqueIds(data.nodes, '$.nodes', diagnostics);
  uniqueIds(data.edges, '$.edges', diagnostics);
  const laneIds = new Set(data.lanes.map((l) => l.id));
  const ids = nodeIdsOf('workflow', data);
  const slots = new Map();
  for (const [i, n] of data.nodes.entries()) {
    if (!laneIds.has(n.lane)) diagnostics.push(diag('UNKNOWN_REF', `$.nodes[${i}].lane`, `lane "${n.lane}" is not declared in lanes[].`, ['Add the lane or change the node lane.']));
    const key = `${n.lane}:${n.col}`;
    if (slots.has(key)) diagnostics.push(diag('SLOT_COLLISION', `$.nodes[${i}]`, `"${n.id}" and "${slots.get(key)}" share lane "${n.lane}" column ${n.col}.`, ['Move one node to a free column.']));
    slots.set(key, n.id);
  }
  refsExist(data.edges, ids, '$.edges', diagnostics);
  for (const [i, g] of (data.groups || []).entries()) {
    if (!laneIds.has(g.lane)) diagnostics.push(diag('UNKNOWN_REF', `$.groups[${i}].lane`, `lane "${g.lane}" is not declared.`, ['Use a declared lane id.']));
    if (g.toCol < g.fromCol) diagnostics.push(diag('RANGE', `$.groups[${i}]`, 'toCol must be >= fromCol.', ['Swap the values.']));
  }
  for (const [i, p] of (data.phases || []).entries()) {
    if (p.toCol < p.fromCol) diagnostics.push(diag('RANGE', `$.phases[${i}]`, 'toCol must be >= fromCol.', ['Swap the values.']));
  }
  for (const [i, id] of (data.mainPath || []).entries()) {
    if (!ids.has(id)) diagnostics.push(diag('UNKNOWN_REF', `$.mainPath[${i}]`, `"${id}" is not a node.`, ['Use node ids only.']));
  }
  const edgeKeys = new Set(data.edges.map((e) => `${e.from}>${e.to}`));
  for (let i = 1; i < (data.mainPath || []).length; i += 1) {
    const key = `${data.mainPath[i - 1]}>${data.mainPath[i]}`;
    if (!edgeKeys.has(key)) diagnostics.push(diag('MAINPATH_GAP', `$.mainPath[${i}]`, `No edge connects "${data.mainPath[i - 1]}" to "${data.mainPath[i]}".`, ['Add the edge or fix the mainPath order.']));
  }
}

function checkSequence(data, diagnostics) {
  uniqueIds(data.participants, '$.participants', diagnostics);
  uniqueIds(data.messages, '$.messages', diagnostics);
  const ids = nodeIdsOf('sequence', data);
  const msgIds = new Set(data.messages.map((m) => m.id));
  for (const [i, m] of data.messages.entries()) {
    for (const key of ['from', 'to']) {
      if (!ids.has(m[key])) diagnostics.push(diag('UNKNOWN_REF', `$.messages[${i}].${key}`, `"${m[key]}" is not a participant.`, ['Use participant ids only.']));
    }
  }
  const order = new Map(data.messages.map((m, i) => [m.id, i]));
  for (const [i, s] of [...(data.segments || []).entries(), ...(data.activations || []).entries()]) {
    const subject = s.label !== undefined ? `$.segments[${i}]` : `$.activations[${i}]`;
    for (const key of ['fromMessage', 'toMessage']) {
      if (!msgIds.has(s[key])) diagnostics.push(diag('UNKNOWN_REF', `${subject}.${key}`, `"${s[key]}" is not a message id.`, ['Reference an existing message.']));
    }
    if (msgIds.has(s.fromMessage) && msgIds.has(s.toMessage) && order.get(s.toMessage) < order.get(s.fromMessage)) {
      diagnostics.push(diag('RANGE', subject, 'toMessage must come after fromMessage.', ['Swap the values.']));
    }
    if (s.participant && !ids.has(s.participant)) diagnostics.push(diag('UNKNOWN_REF', `${subject}.participant`, `"${s.participant}" is not a participant.`, ['Use participant ids only.']));
  }
}

function checkLifecycle(data, diagnostics) {
  uniqueIds(data.states, '$.states', diagnostics);
  uniqueIds(data.transitions, '$.transitions', diagnostics);
  const ids = nodeIdsOf('lifecycle', data);
  refsExist(data.transitions, ids, '$.transitions', diagnostics);
  const slots = new Map();
  for (const [i, s] of data.states.entries()) {
    const key = `${s.row ?? 0}:${s.col}`;
    if (slots.has(key)) diagnostics.push(diag('SLOT_COLLISION', `$.states[${i}]`, `"${s.id}" and "${slots.get(key)}" share row ${s.row ?? 0} column ${s.col}.`, ['Move one state to a free cell.']));
    slots.set(key, s.id);
  }
  if (!data.states.some((s) => s.type === 'start')) diagnostics.push(diag('NO_START', '$.states', 'A lifecycle needs one state of type "start".', ['Mark the entry state as start.']));
  if (!data.states.some((s) => s.type === 'success' || s.type === 'failure')) diagnostics.push(diag('NO_TERMINAL', '$.states', 'A lifecycle needs at least one terminal state (success or failure).', ['Add a terminal state.']));
}
