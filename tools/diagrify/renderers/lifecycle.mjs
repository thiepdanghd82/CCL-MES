import { esc, renderNode, renderEdge, wrapSvg, textWidth } from '../lib/svg.mjs';
import { routeGridEdge } from './workflow.mjs';

const PAD = 32;
const COL_W = 250;
const ROW_H = 132;
const NODE_W = 144;
const NODE_H = 58;

const SHAPE = { start: 'pill', success: 'pill', failure: 'pill', waiting: 'rect', active: 'rect', neutral: 'rect' };

export function renderLifecycle(doc) {
  const maxCol = Math.max(...doc.states.map((s) => s.col));
  const maxRow = Math.max(...doc.states.map((s) => s.row ?? 0));
  const top = PAD + 8;
  const laneY = (row) => top + row * ROW_H;
  const colX = (col) => PAD + col * COL_W + (COL_W - NODE_W) / 2;

  const boxes = new Map();
  for (const s of doc.states) {
    const row = s.row ?? 0;
    boxes.set(s.id, { id: s.id, x: colX(s.col), y: laneY(row) + (ROW_H - NODE_H) / 2, w: NODE_W, h: NODE_H, col: s.col, lane: row });
  }

  const body = [];
  const layout = { nodes: [], edges: [], labels: [] };

  // Main rail highlight for row 0.
  body.push(`<rect class="rail" x="${PAD}" y="${laneY(0) + 8}" width="${(maxCol + 1) * COL_W}" height="${ROW_H - 16}" rx="12"/>`);
  body.push(`<text class="rail-label" x="${PAD + 14}" y="${laneY(0) + 20}" dominant-baseline="middle">${esc('MAIN RAIL')}</text>`);

  const obstacles = [...boxes.values()];
  const pairs = new Set(doc.transitions.map((t) => `${t.from}>${t.to}`));
  const stack = new Map();
  for (const t of doc.transitions) {
    const a = boxes.get(t.from); const b = boxes.get(t.to);
    const { points, labelSide, labelSegment, labelDx = 0, labelDy = 0 } = routeGridEdge(a, b, t.route || 'auto', obstacles, { laneY, colW: COL_W, nodeW: NODE_W, laneH: ROW_H, hasReverse: pairs.has(`${t.to}>${t.from}`), labelWidth: t.label ? textWidth(t.label, 11) + 10 : 0, stack });
    const targetType = doc.states.find((s) => s.id === t.to)?.type;
    const variant = t.variant || (targetType === 'failure' ? 'security' : a.lane === 0 && b.lane === 0 && b.col > a.col ? 'emphasis' : 'default');
    const edge = renderEdge({ id: t.id, from: t.from, to: t.to, points, label: t.label, variant, labelSide, labelSegment, labelAt: t.labelAt, labelDx: (t.labelDx ?? 0) + labelDx, labelDy: (t.labelDy ?? 0) + labelDy });
    body.push(edge.svg);
    layout.edges.push({ id: t.id, from: t.from, to: t.to, points });
    if (edge.label) layout.labels.push(edge.label);
  }

  for (const s of doc.states) {
    const box = boxes.get(s.id);
    body.push(renderNode({ ...s, ...box, shape: SHAPE[s.type] || 'rect' }));
    layout.nodes.push({ id: s.id, x: box.x, y: box.y, w: box.w, h: box.h, type: s.type, label: s.label, sublabel: s.sublabel });
  }

  const viewBox = { x: 0, y: 0, w: PAD * 2 + (maxCol + 1) * COL_W, h: top + (maxRow + 1) * ROW_H + PAD };
  return { svg: wrapSvg({ viewBox, body: body.join('\n'), title: doc.meta.title }), layout, viewBox };
}
