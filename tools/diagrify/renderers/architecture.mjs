import { renderNode, renderEdge, renderBoundary, orthogonalRoute, portOf, autoSides, boundsOf, wrapSvg, dedupe } from '../lib/svg.mjs';

const DEFAULT_W = 140;
const DEFAULT_H = 60;

export function renderArchitecture(doc) {
  const boxes = new Map();
  for (const c of doc.components) {
    boxes.set(c.id, { id: c.id, x: c.pos[0], y: c.pos[1], w: c.size?.[0] ?? DEFAULT_W, h: c.size?.[1] ?? DEFAULT_H });
  }

  const layout = { nodes: [], edges: [], labels: [] };
  const body = [];

  // Boundaries first so they sit behind nodes.
  const boundaryBoxes = [];
  for (const b of doc.boundaries || []) {
    const pad = b.padding ?? 28;
    const inner = boundsOf(b.wraps.map((id) => boxes.get(id)), pad);
    inner.y -= 14; inner.h += 14; // room for the label
    boundaryBoxes.push(inner);
    body.push(renderBoundary({ label: b.label, kind: b.kind || 'region', ...inner }));
  }

  // Count how many edges share each node side so ports can be spread.
  const sideUsage = new Map();
  const resolved = doc.connections.map((c) => {
    const a = boxes.get(c.from); const b = boxes.get(c.to);
    const [autoFrom, autoTo] = autoSides(a, b);
    const fromSide = c.fromSide || autoFrom;
    const toSide = c.toSide || autoTo;
    bump(sideUsage, `${c.from}:${fromSide}`);
    bump(sideUsage, `${c.to}:${toSide}`);
    return { c, a, b, fromSide, toSide };
  });

  // Order edges on a shared side by the position of the far endpoint so the
  // spread ports never cross each other.
  const sideOrder = new Map();
  for (const r of resolved) {
    push(sideOrder, `${r.c.from}:${r.fromSide}`, { edge: r.c.id, far: center(r.b), side: r.fromSide });
    push(sideOrder, `${r.c.to}:${r.toSide}`, { edge: r.c.id, far: center(r.a), side: r.toSide });
  }
  const ratioFor = new Map();
  for (const [key, list] of sideOrder) {
    const horizontalSide = list[0].side === 'top' || list[0].side === 'bottom';
    list.sort((p, q) => (horizontalSide ? p.far[0] - q.far[0] : p.far[1] - q.far[1]));
    list.forEach((item, i) => ratioFor.set(`${key}:${item.edge}`, spreadRatio(list.length, i)));
  }

  for (const r of resolved) {
    const pa = portOf(r.a, r.fromSide, ratioFor.get(`${r.c.from}:${r.fromSide}:${r.c.id}`));
    const pb = portOf(r.b, r.toSide, ratioFor.get(`${r.c.to}:${r.toSide}:${r.c.id}`));
    const points = r.c.via?.length
      ? orthogonalizeVia(pa, r.c.via, pb)
      : orthogonalRoute(pa, pb);
    const edge = renderEdge({ id: r.c.id, from: r.c.from, to: r.c.to, points, label: r.c.label, variant: r.c.variant, labelAt: r.c.labelAt, labelDx: r.c.labelDx, labelDy: r.c.labelDy });
    body.push(edge.svg);
    layout.edges.push({ id: r.c.id, from: r.c.from, to: r.c.to, points });
    if (edge.label) layout.labels.push(edge.label);
  }

  for (const c of doc.components) {
    const box = boxes.get(c.id);
    body.push(renderNode({ ...c, ...box }));
    layout.nodes.push({ ...box, type: c.type, label: c.label, sublabel: c.sublabel });
  }

  const all = [...boxes.values(), ...boundaryBoxes];
  const viewBox = doc.meta.viewBox
    ? { x: 0, y: 0, w: doc.meta.viewBox[0], h: doc.meta.viewBox[1] }
    : normalize(boundsOf(all, 40));

  return { svg: wrapSvg({ viewBox, body: body.join('\n'), title: doc.meta.title }), layout, viewBox };
}

function normalize(b) {
  return { x: Math.floor(b.x), y: Math.floor(b.y), w: Math.ceil(b.w), h: Math.ceil(b.h) };
}
function bump(map, key) { map.set(key, (map.get(key) || 0) + 1); }
function push(map, key, value) { if (!map.has(key)) map.set(key, []); map.get(key).push(value); }
function center(b) { return [b.x + b.w / 2, b.y + b.h / 2]; }

// Distribute n ports along one side; a single edge stays centered.
function spreadRatio(n, i) {
  if (n <= 1) return 0.5;
  const step = Math.min(0.28, 0.6 / n);
  return 0.5 + step * (i - (n - 1) / 2);
}

// Authored waypoints are hints, not full geometry: insert the missing corner
// whenever two consecutive points are not axis-aligned so every leg stays
// horizontal or vertical.
function orthogonalizeVia(pa, via, pb) {
  const pts = [[pa.x, pa.y]];
  const horizontalStart = pa.side === 'left' || pa.side === 'right';
  const raw = [...via, [pb.x, pb.y]];
  raw.forEach((p, i) => {
    const last = pts[pts.length - 1];
    const aligned = Math.abs(last[0] - p[0]) < 0.01 || Math.abs(last[1] - p[1]) < 0.01;
    if (!aligned) {
      const isLast = i === raw.length - 1;
      const horizontalEnd = pb.side === 'left' || pb.side === 'right';
      const bendHorizontalFirst = i === 0 ? horizontalStart : isLast ? !horizontalEnd : true;
      pts.push(bendHorizontalFirst ? [p[0], last[1]] : [last[0], p[1]]);
    }
    pts.push(p);
  });
  return dedupe(pts);
}
