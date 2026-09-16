import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { validateDiagram } from '../lib/validate.mjs';
import { renderDocument } from '../lib/render.mjs';
import { checkLayout } from '../lib/layout-check.mjs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.join(__dirname, '..');
const EXAMPLES = path.join(ROOT, 'examples');
const CLI = path.join(ROOT, 'bin', 'diagrify.mjs');
const examples = fs.readdirSync(EXAMPLES).filter((f) => f.endsWith('.json'));

const load = (f) => JSON.parse(fs.readFileSync(path.join(EXAMPLES, f), 'utf8'));

test('every example passes schema, semantic and geometry checks', () => {
  for (const f of examples) {
    const doc = load(f);
    const v = validateDiagram(doc.diagram_type, doc);
    assert.deepEqual(v.diagnostics, [], `${f}: ${JSON.stringify(v.diagnostics)}`);
    const rendered = renderDocument(doc);
    const layout = checkLayout(rendered.layout);
    assert.deepEqual(layout.diagnostics, [], `${f}: ${JSON.stringify(layout.diagnostics)}`);
  }
});

test('rendered HTML is self-contained and carries the viewer contract', () => {
  const doc = load('npi-process.workflow.json');
  const { html } = renderDocument(doc);
  assert.ok(html.includes('<svg'), 'inline SVG');
  assert.ok(!/<(script|link)[^>]+(src|href)="https?:/.test(html), 'no external scripts or stylesheets');
  assert.ok(html.includes('id="diagram-data"'), 'embedded data');
  for (const id of ['theme', 'export', 'search', 'legend', 'cards', 'views']) assert.ok(html.includes(`id="${id}"`), id);
  assert.ok(html.includes(`data-id="${doc.nodes[0].id}"`), 'node ids are addressable');
  assert.ok(html.includes('data-anim="trace"'), 'animation flag');
});

test('rendering is deterministic', () => {
  const doc = load('web-app.architecture.json');
  assert.equal(renderDocument(doc).html, renderDocument(doc).html);
});

test('authored text is escaped', () => {
  const doc = load('web-app.architecture.json');
  doc.meta.title = '<script>alert(1)</script>';
  doc.components[0].label = 'A & B <x>';
  const { html } = renderDocument(doc);
  assert.ok(!html.includes('<script>alert(1)</script>'));
  assert.ok(html.includes('A &amp; B &lt;x&gt;'));
});

test('schema rejects unknown fields and bad references with diagnostics', () => {
  const doc = load('web-app.architecture.json');
  doc.components[0].colour = 'red';
  let v = validateDiagram('architecture', doc);
  assert.equal(v.ok, false);
  assert.ok(v.diagnostics.some((d) => d.code === 'SCHEMA' && d.subject.includes('colour')));

  const doc2 = load('web-app.architecture.json');
  doc2.connections[0].to = 'nowhere';
  v = validateDiagram('architecture', doc2);
  assert.ok(v.diagnostics.some((d) => d.code === 'UNKNOWN_REF'));
});

test('workflow slot collisions and lifecycle terminal rules are caught', () => {
  const wf = load('npi-process.workflow.json');
  wf.nodes[1].lane = wf.nodes[0].lane; wf.nodes[1].col = wf.nodes[0].col;
  assert.ok(validateDiagram('workflow', wf).diagnostics.some((d) => d.code === 'SLOT_COLLISION'));

  const lc = load('sample-request.lifecycle.json');
  lc.states = lc.states.filter((s) => s.type !== 'success' && s.type !== 'failure');
  lc.transitions = lc.transitions.filter((t) => lc.states.some((s) => s.id === t.from) && lc.states.some((s) => s.id === t.to));
  assert.ok(validateDiagram('lifecycle', lc).diagnostics.some((d) => d.code === 'NO_TERMINAL'));
});

test('geometry check flags an edge crossing an unrelated node', () => {
  const doc = load('web-app.architecture.json');
  // Drop a component right in the middle of the users -> cdn connection.
  doc.components.push({ id: 'blocker', type: 'external', label: 'Blocker', pos: [190, 244], size: [60, 52] });
  doc.components.find((c) => c.id === 'cdn').pos = [300, 240];
  const v = validateDiagram('architecture', doc);
  if (v.ok) {
    const layout = checkLayout(renderDocument(doc).layout);
    assert.ok(layout.diagnostics.some((d) => d.code === 'EDGE_CROSSES_NODE'));
  } else {
    assert.ok(v.diagnostics.some((d) => d.code === 'NODE_OVERLAP'));
  }
});

test('CLI deliver writes the artifact atomically and reports a receipt', () => {
  const out = path.join(ROOT, 'test', '.tmp', 'deliver.html');
  fs.rmSync(path.dirname(out), { recursive: true, force: true });
  const json = JSON.parse(execFileSync('node', [CLI, 'deliver', 'workflow', path.join(EXAMPLES, 'npi-process.workflow.json'), out, '--json'], { encoding: 'utf8' }));
  assert.equal(json.ok, true);
  assert.match(json.receipt.artifact.sha256, /^[a-f0-9]{64}$/);
  assert.equal(fs.statSync(out).size, json.receipt.artifact.bytes);
  fs.rmSync(path.dirname(out), { recursive: true, force: true });
});

test('CLI deliver fails closed and keeps the previous artifact', () => {
  const dir = path.join(ROOT, 'test', '.tmp2');
  fs.rmSync(dir, { recursive: true, force: true });
  fs.mkdirSync(dir, { recursive: true });
  const out = path.join(dir, 'x.html');
  fs.writeFileSync(out, 'previous');
  const bad = load('web-app.architecture.json');
  bad.connections[0].to = 'nowhere';
  const src = path.join(dir, 'bad.json');
  fs.writeFileSync(src, JSON.stringify(bad));
  const r = spawnSync('node', [CLI, 'deliver', 'architecture', src, out, '--json'], { encoding: 'utf8' });
  assert.equal(r.status, 1);
  assert.equal(JSON.parse(r.stdout).ok, false);
  assert.equal(fs.readFileSync(out, 'utf8'), 'previous');
  fs.rmSync(dir, { recursive: true, force: true });
});

test('CLI guide recommends a type', () => {
  const g = JSON.parse(execFileSync('node', [CLI, 'guide', 'Show an API request with cache miss and retry', '--json'], { encoding: 'utf8' }));
  assert.equal(g.recommendation, 'sequence');
});
