import { esc, renderNode, renderEdge, orthogonalRoute, portOf, dedupe, wrapSvg, textWidth } from '../lib/svg.mjs';

const LANE_HEAD_W = 150;
const COL_W = 200;
const NODE_W = 136;
const NODE_H = 56;
const LANE_H = 120;
const PHASE_H = 34;
const PAD = 24;

export function renderWorkflow(doc) {
  const laneIndex = new Map(doc.lanes.map((l, i) => [l.id, i]));
  const maxCol = Math.max(...doc.nodes.map((n) => n.col), ...(doc.phases || []).map((p) => p.toCol), ...(doc.groups || []).map((g) => g.toCol));
  const hasPhases = Boolean(doc.phases?.length);
  const top = PAD + (hasPhases ? PHASE_H + 12 : 0);
  const gridX = PAD + LANE_HEAD_W;
  const gridW = (maxCol + 1) * COL_W;
  const gridH = doc.lanes.length * LANE_H;

  const colX = (col) => gridX + col * COL_W + (COL_W - NODE_W) / 2;
  const laneY = (lane) => top + laneIndex.get(lane) * LANE_H;

  const boxes = new Map();
  for (const n of doc.nodes) {
    boxes.set(n.id, { id: n.id, x: colX(n.col), y: laneY(n.lane) + (LANE_H - NODE_H) / 2, w: NODE_W, h: NODE_H, col: n.col, lane: n.lane });
  }

  const body = [];
  const layout = { nodes: [], edges: [], labels: [] };

  // Lanes
  doc.lanes.forEach((lane, i) => {
    const y = top + i * LANE_H;
    body.push(`<g class="lane l-${esc(lane.variant || 'default')}${i % 2 ? ' lane-alt' : ''}">`);
    body.push(`<rect class="lane-bg" x="${PAD}" y="${y}" width="${LANE_HEAD_W + gridW}" height="${LANE_H}"/>`);
    body.push(`<rect class="lane-head" x="${PAD}" y="${y}" width="${LANE_HEAD_W}" height="${LANE_H}"/>`);
    body.push(`<text class="lane-label" x="${PAD + LANE_HEAD_W / 2}" y="${y + LANE_H / 2}" text-anchor="middle" dominant-baseline="middle">${esc(lane.label)}</text>`);
    body.push('</g>');
  });
  body.push(`<rect class="lane-frame" x="${PAD}" y="${top}" width="${LANE_HEAD_W + gridW}" height="${gridH}" rx="10"/>`);

  // Phases
  for (const p of doc.phases || []) {
    const x = gridX + p.fromCol * COL_W + 6;
    const w = (p.toCol - p.fromCol + 1) * COL_W - 12;
    body.push(`<g class="phase v-${esc(p.variant || 'default')}"><rect x="${x}" y="${PAD}" width="${w}" height="${PHASE_H}" rx="8"/><text x="${x + w / 2}" y="${PAD + PHASE_H / 2}" text-anchor="middle" dominant-baseline="middle">${esc(p.label)}</text></g>`);
  }

  // Groups
  for (const g of doc.groups || []) {
    const x = gridX + g.fromCol * COL_W + 10;
    const w = (g.toCol - g.fromCol + 1) * COL_W - 20;
    const y = laneY(g.lane) + 8;
    body.push(`<g class="group v-${esc(g.variant || 'default')}"><rect x="${x}" y="${y}" width="${w}" height="${LANE_H - 16}" rx="10"/><text x="${x + 10}" y="${y + 12}" dominant-baseline="middle">${esc(g.label)}</text></g>`);
  }

  // Edges
  const mainSet = new Set();
  for (let i = 1; i < (doc.mainPath || []).length; i += 1) mainSet.add(`${doc.mainPath[i - 1]}>${doc.mainPath[i]}`);
  const obstacles = [...boxes.values()];
  const pairs = new Set(doc.edges.map((e) => `${e.from}>${e.to}`));
  const stack = new Map();
  for (const e of doc.edges) {
    const a = boxes.get(e.from); const b = boxes.get(e.to);
    const { points, labelSide, labelSegment, labelDx = 0, labelDy = 0 } = routeGridEdge(a, b, e.route || 'auto', obstacles, { laneY, colW: COL_W, nodeW: NODE_W, laneH: LANE_H, hasReverse: pairs.has(`${e.to}>${e.from}`), labelWidth: e.label ? textWidth(e.label, 11) + 10 : 0, stack });
    const role = e.role || (mainSet.has(`${e.from}>${e.to}`) ? 'main' : undefined);
    const variant = e.variant || (role === 'main' ? 'emphasis' : 'default');
    const edge = renderEdge({ id: e.id, from: e.from, to: e.to, points, label: e.label, variant, role, labelSide, labelSegment, labelAt: e.labelAt, labelDx: (e.labelDx ?? 0) + labelDx, labelDy: (e.labelDy ?? 0) + labelDy });
    body.push(edge.svg);
    layout.edges.push({ id: e.id, from: e.from, to: e.to, points });
    if (edge.label) layout.labels.push(edge.label);
  }

  // Nodes
  for (const n of doc.nodes) {
    const box = boxes.get(n.id);
    body.push(renderNode({ ...n, ...box }));
    layout.nodes.push({ id: n.id, x: box.x, y: box.y, w: box.w, h: box.h, type: n.type, label: n.label, sublabel: n.sublabel });
  }

  const viewBox = { x: 0, y: 0, w: gridX + gridW + PAD, h: top + gridH + PAD };
  return { svg: wrapSvg({ viewBox, body: body.join('\n'), title: doc.meta.title }), layout, viewBox };
}

export function routeGridEdge(a, b, route, obstacles, { laneY, colW, nodeW, laneH, hasReverse = false, labelWidth = 0, stack = new Map() }) {
  const forward = b.col > a.col;
  const sameLane = a.lane === b.lane;
  const sameCol = a.col === b.col;
  const gapHalf = (colW - nodeW) / 2;

  if (route === 'auto') {
    if (sameCol && !sameLane && !(hasReverse && b.y < a.y)) {
      const down = b.y > a.y;
      return { points: orthogonalRoute(portOf(a, down ? 'bottom' : 'top'), portOf(b, down ? 'top' : 'bottom'), 12), labelSide: 'above' };
    }
    if (forward && sameLane && b.col - a.col === 1) {
      // A label wider than the column gap is lifted above both node tops.
      const lift = labelWidth > colW - nodeW - 6 ? 28 : 0;
      return { points: orthogonalRoute(portOf(a, 'right'), portOf(b, 'left'), 12), labelSide: 'above', labelDy: -lift };
    }
    if (forward) {
      const gapAfterA = a.x + a.w + gapHalf;
      const gapBeforeB = b.x - gapHalf;
      const candidates = [
        dedupe([[a.x + a.w, a.y + a.h / 2], [gapAfterA, a.y + a.h / 2], [gapAfterA, b.y + b.h / 2], [b.x, b.y + b.h / 2]]),
        dedupe([[a.x + a.w, a.y + a.h / 2], [gapBeforeB, a.y + a.h / 2], [gapBeforeB, b.y + b.h / 2], [b.x, b.y + b.h / 2]]),
      ];
      const clean = candidates.find((pts) => !crossesAny(pts, obstacles, a, b));
      if (clean) return { points: clean, labelSide: 'above' };
      route = b.y >= a.y ? 'below' : 'above';
    } else {
      route = b.y < a.y ? 'above' : 'below';
    }
  }

  // Corridor routes: leave the source vertically into the free strip of its
  // own lane, travel horizontally, then drop through a column gap into the
  // target. Column gaps and lane strips never contain nodes.
  const useBelow = route === 'below';
  const laneA = laneY(a.lane);
  // Several corridor routes in the same lane strip are staggered by 8px so
  // their horizontal legs never lie on top of each other.
  const stackKey = `${a.lane}:${useBelow ? 'below' : 'above'}`;
  const stackIndex = Math.min(stack.get(stackKey) || 0, 2);
  stack.set(stackKey, stackIndex + 1);
  const corridorY = useBelow ? laneA + laneH - 12 - stackIndex * 8 : laneA + 12 + stackIndex * 8;
  const exitSide = useBelow ? 'bottom' : 'top';
  const start = portOf(a, exitSide, b.col >= a.col ? 0.7 : 0.3);
  if (sameLane) {
    const end = portOf(b, exitSide, b.col > a.col ? 0.3 : 0.7);
    return { points: dedupe([[start.x, start.y], [start.x, corridorY], [end.x, corridorY], [end.x, end.y]]), labelSide: useBelow ? 'below' : 'above' };
  }
  const enterFromLeft = b.col > a.col || (sameCol && useBelow);
  const gapX = enterFromLeft ? b.x - gapHalf - 6 : b.x + b.w + gapHalf + 6;
  const end = portOf(b, enterFromLeft ? 'left' : 'right');
  const points = dedupe([[start.x, start.y], [start.x, corridorY], [gapX, corridorY], [gapX, end.y], [end.x, end.y]]);
  // Prefer the vertical leg inside the column gap for the label when it is
  // long enough to carry one; the lane strip is shared with other labels.
  const gapLeg = points.findIndex((p, i) => i > 0 && Math.abs(p[0] - points[i - 1][0]) < 0.01 && Math.abs(p[1] - points[i - 1][1]) >= 40 && i > 1) - 1;
  const gapFits = labelWidth <= colW - nodeW - 6;
  const gapCenter = enterFromLeft ? b.x - gapHalf : b.x + b.w + gapHalf;
  const useGap = gapLeg >= 0 && gapFits;
  return { points, labelSide: useBelow ? 'below' : 'above', labelSegment: useGap ? gapLeg : undefined, labelDx: useGap ? gapCenter - gapX : 0 };
}

function crossesAny(points, obstacles, a, b) {
  for (let i = 1; i < points.length; i += 1) {
    const [x1, y1] = points[i - 1]; const [x2, y2] = points[i];
    for (const o of obstacles) {
      if (o.id === a.id || o.id === b.id) continue;
      const minX = Math.min(x1, x2); const maxX = Math.max(x1, x2);
      const minY = Math.min(y1, y2); const maxY = Math.max(y1, y2);
      if (minX < o.x + o.w && maxX > o.x && minY < o.y + o.h && maxY > o.y) return true;
    }
  }
  return false;
}
