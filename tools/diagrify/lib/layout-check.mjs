// Geometry checks that run on the rendered layout, independent of the schema.
import { textWidth } from './svg.mjs';

function segmentIntersectsRect([x1, y1], [x2, y2], r, inset = 2) {
  const rx = r.x + inset; const ry = r.y + inset; const rw = r.w - inset * 2; const rh = r.h - inset * 2;
  if (rw <= 0 || rh <= 0) return false;
  const minX = Math.min(x1, x2); const maxX = Math.max(x1, x2);
  const minY = Math.min(y1, y2); const maxY = Math.max(y1, y2);
  return minX < rx + rw && maxX > rx && minY < ry + rh && maxY > ry;
}

function rectsOverlap(a, b, gap = 0) {
  return a.x < b.x + b.w + gap && a.x + a.w + gap > b.x && a.y < b.y + b.h + gap && a.y + a.h + gap > b.y;
}

export function checkLayout(layout) {
  const diagnostics = [];
  const nodesById = new Map(layout.nodes.map((n) => [n.id, n]));

  for (const edge of layout.edges) {
    for (let i = 1; i < edge.points.length; i += 1) {
      for (const node of layout.nodes) {
        if (node.id === edge.from || node.id === edge.to) continue;
        if (node.passable) continue;
        if (segmentIntersectsRect(edge.points[i - 1], edge.points[i], node)) {
          diagnostics.push({
            code: 'EDGE_CROSSES_NODE',
            subject: `edge "${edge.id}"`,
            message: `Segment ${i} of "${edge.id}" passes through unrelated node "${node.id}".`,
            evidence: { segment: [edge.points[i - 1], edge.points[i]], node: { id: node.id, x: node.x, y: node.y, w: node.w, h: node.h } },
            supportedFixes: ['Move the node out of the corridor.', 'Set explicit fromSide/toSide or route: "above"/"below" on the edge.', 'Add a via waypoint (architecture only).'],
          });
          break;
        }
      }
    }
  }

  for (const label of layout.labels) {
    for (const node of layout.nodes) {
      if (rectsOverlap(label, node, 2)) {
        diagnostics.push({
          code: 'LABEL_OVER_NODE',
          subject: `edge "${label.edgeId}" label`,
          message: `The label of "${label.edgeId}" overlaps node "${node.id}".`,
          evidence: { label: rect(label), node: rect(node) },
          supportedFixes: ['Adjust labelAt / labelDx / labelDy on the edge.', 'Move the node or shorten the label while keeping its meaning.'],
        });
      }
    }
  }
  for (let i = 0; i < layout.labels.length; i += 1) {
    for (let j = i + 1; j < layout.labels.length; j += 1) {
      const a = layout.labels[i]; const b = layout.labels[j];
      if (rectsOverlap(a, b, 2)) {
        diagnostics.push({
          code: 'LABEL_COLLISION',
          subject: `edge "${a.edgeId}" label`,
          message: `Labels of "${a.edgeId}" and "${b.edgeId}" overlap.`,
          evidence: { a: rect(a), b: rect(b) },
          supportedFixes: ['Adjust labelAt / labelDx / labelDy on one edge.', 'Increase spacing between the involved nodes.'],
        });
      }
    }
  }

  for (const node of layout.nodes) {
    const inner = node.w - 14;
    for (const [field, size] of [['label', 13], ['sublabel', 10.5]]) {
      const text = node[field];
      if (text && textWidth(text, size) > inner) {
        diagnostics.push({
          code: 'TEXT_OVERFLOW',
          subject: `node "${node.id}" ${field}`,
          message: `"${text}" is about ${textWidth(text, size)}px wide but the node offers ${inner}px.`,
          evidence: { text, measured: textWidth(text, size), available: inner },
          supportedFixes: ['Shorten the wording while keeping its meaning.', 'Move detail into a card or the sublabel.', 'Architecture only: widen the component with size.'],
        });
      }
    }
  }

  const warnings = [];
  for (const label of layout.labels) {
    for (const edge of layout.edges) {
      if (edge.id === label.edgeId) continue;
      const hit = edge.points.some((p, i) => i > 0 && segmentIntersectsRect(edge.points[i - 1], p, label, -1));
      if (hit) {
        warnings.push({ code: 'LABEL_OVER_EDGE', subject: `edge "${label.edgeId}" label`, message: `The label of "${label.edgeId}" sits on top of edge "${edge.id}".`, supportedFixes: ['Adjust labelAt / labelDx / labelDy so the label clears the other route.'] });
        break;
      }
    }
  }
  const degree = new Map();
  for (const e of layout.edges) {
    degree.set(e.from, (degree.get(e.from) || 0) + 1);
    degree.set(e.to, (degree.get(e.to) || 0) + 1);
  }
  for (const n of layout.nodes) {
    if (!n.passable && !degree.has(n.id) && nodesById.size > 1 && !n.detached) {
      warnings.push({ code: 'ISOLATED_NODE', subject: `node "${n.id}"`, message: `"${n.id}" has no relationships.`, supportedFixes: ['Connect it, or remove it if it adds no meaning.'] });
    }
  }
  return { diagnostics, warnings };
}

function rect(r) {
  return { x: r.x, y: r.y, w: r.w, h: r.h };
}
