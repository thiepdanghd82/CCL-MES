import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { esc } from './svg.mjs';
import { LOCALES, resolveLocale } from './i18n.mjs';
import { renderArchitecture } from '../renderers/architecture.mjs';
import { renderWorkflow } from '../renderers/workflow.mjs';
import { renderSequence } from '../renderers/sequence.mjs';
import { renderLifecycle } from '../renderers/lifecycle.mjs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const TEMPLATE = path.join(__dirname, '..', 'assets', 'template.html');
export const VERSION = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'package.json'), 'utf8')).version;

const RENDERERS = { architecture: renderArchitecture, workflow: renderWorkflow, sequence: renderSequence, lifecycle: renderLifecycle };

// Render a validated document into { html, svg, layout, viewBox }.
export function renderDocument(doc) {
  const render = RENDERERS[doc.diagram_type];
  if (!render) throw new Error(`No renderer for "${doc.diagram_type}".`);
  const { svg, layout, viewBox } = render(doc);
  const locale = resolveLocale(doc.meta.locale);
  const ui = LOCALES[locale];
  const theme = doc.meta.theme || 'dark';
  const animation = doc.meta.animation || 'none';

  const edgeList = layout.edges.map((e) => ({ id: e.id, from: e.from, to: e.to }));
  const data = {
    version: VERSION,
    type: doc.diagram_type,
    slug: slugify(doc.meta.title),
    theme,
    animation,
    views: doc.meta.views || [],
    edges: edgeList,
    ui: { ...ui, guide: undefined },
    guide: ui.guide,
    source: doc,
  };

  const template = fs.readFileSync(TEMPLATE, 'utf8');
  const html = template
    .replace(/\{\{UI\.(\w+)\}\}/g, (_, key) => esc(ui[key] ?? key))
    .replace(/\{\{TITLE\}\}/g, esc(doc.meta.title))
    .replace(/\{\{SUBTITLE\}\}/g, esc(doc.meta.subtitle || ''))
    .replace(/\{\{TYPE\}\}/g, esc(doc.diagram_type))
    .replace(/\{\{LANG\}\}/g, esc(locale))
    .replace(/\{\{THEME\}\}/g, esc(theme))
    .replace(/\{\{ANIMATION\}\}/g, esc(animation))
    .replace(/\{\{VERSION\}\}/g, esc(VERSION))
    .replace(/\{\{LEGEND_HIDDEN\}\}/g, doc.meta.legend === 'hidden' ? 'hidden' : '')
    .replace('{{LEGEND}}', renderLegend(doc, layout, ui))
    .replace('{{CARDS}}', renderCards(doc.cards || []))
    .replace('{{SVG}}', svg)
    .replace('{{DATA}}', JSON.stringify(data).replace(/</g, '\\u003c'));
  return { html, svg, layout, viewBox };
}

function renderLegend(doc, layout, ui) {
  const nodeKinds = [...new Set(layout.nodes.map((n) => n.type))];
  const edgeVariants = new Set();
  const list = doc.connections || doc.edges || doc.messages || doc.transitions || [];
  for (const e of list) edgeVariants.add(e.variant || 'default');
  if (doc.diagram_type === 'workflow' && doc.mainPath?.length) edgeVariants.add('emphasis');
  if (doc.diagram_type === 'lifecycle') { edgeVariants.add('emphasis'); if (doc.states.some((s) => s.type === 'failure')) edgeVariants.add('security'); }
  const rows = [];
  for (const k of nodeKinds) rows.push(`<div class="lg"><span class="sw" style="--c:var(--${esc(k)})"></span>${esc(ui.types[k] || k)}</div>`);
  for (const v of ['default', 'emphasis', 'security', 'dashed']) {
    if (!edgeVariants.has(v)) continue;
    const cssVar = v === 'default' ? '--edge' : `--edge-${v}`;
    const dash = v === 'security' || v === 'dashed' ? ' dash' : '';
    rows.push(`<div class="lg"><span class="sw line${dash}" style="--c:var(${cssVar})"></span>${esc(ui.edges[v])}</div>`);
  }
  return rows.join('\n');
}

function renderCards(cards) {
  return cards.map((c) => `<section class="card dot-${esc(c.dot || 'slate')}"><h3><i></i>${esc(c.title)}</h3><ul>${c.items.map((i) => `<li>${esc(i)}</li>`).join('')}</ul></section>`).join('\n');
}

export function slugify(text) {
  return String(text).normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/gi, 'd').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'diagram';
}
