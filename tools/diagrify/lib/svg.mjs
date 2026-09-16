// Shared SVG helpers used by every renderer.

export function esc(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

export const NODE_TYPES = ['frontend', 'backend', 'database', 'cloud', 'security', 'messagebus', 'external', 'process', 'machine', 'quality', 'storage', 'people', 'document'];
export const EDGE_VARIANTS = ['default', 'emphasis', 'security', 'dashed'];
export const STATE_TYPES = ['start', 'active', 'waiting', 'success', 'failure', 'neutral'];

export const TYPE_LABELS = {
  frontend: 'Frontend', backend: 'Backend', database: 'Database', cloud: 'Cloud service',
  security: 'Security', messagebus: 'Message bus / queue', external: 'External',
  process: 'Process step', machine: 'Machine / equipment', quality: 'Quality gate',
  storage: 'Warehouse / storage', people: 'People / team', document: 'Document / record',
  start: 'Start', active: 'Active', waiting: 'Waiting', success: 'Success', failure: 'Failure', neutral: 'Neutral',
};

export const EDGE_LABELS = {
  default: 'Flow', emphasis: 'Main path', security: 'Control / exception', dashed: 'Optional / async',
};

// Approximate text width for a 12.5px monospace font (0.62em per character).
export function textWidth(text, fontSize = 12.5) {
  return Math.ceil(String(text ?? '').length * fontSize * 0.62);
}

export function renderDefs() {
  const marker = (id, cls) => `<marker id="${id}" markerWidth="9" markerHeight="7" refX="8.5" refY="3.5" orient="auto" markerUnits="userSpaceOnUse"><polygon points="0 0, 9 3.5, 0 7" class="${cls}"/></marker>`;
  return `<defs>
${marker('arrow-default', 'm-default')}
${marker('arrow-emphasis', 'm-emphasis')}
${marker('arrow-security', 'm-security')}
${marker('arrow-dashed', 'm-dashed')}
<pattern id="grid" width="40" height="40" patternUnits="userSpaceOnUse"><path d="M 40 0 L 0 0 0 40" class="c-grid"/></pattern>
</defs>`;
}

export function renderNode({ id, type, label, sublabel, tag, x, y, w, h, variant = 'default', shape = 'rect' }) {
  const lines = [];
  const cx = x + w / 2;
  const hasSub = Boolean(sublabel);
  const hasTag = Boolean(tag);
  const labelY = hasSub ? y + h / 2 - 6 : y + h / 2 + 1;
  const shapeSvg = shape === 'pill'
    ? `<rect class="node-shape" x="${x}" y="${y}" width="${w}" height="${h}" rx="${h / 2}"/>`
    : shape === 'diamond'
      ? `<path class="node-shape" d="M${cx} ${y} L${x + w} ${y + h / 2} L${cx} ${y + h} L${x} ${y + h / 2} Z"/>`
      : `<rect class="node-shape" x="${x}" y="${y}" width="${w}" height="${h}" rx="8"/>`;
  lines.push(`<g class="node n-${esc(type)} v-${esc(variant)}" data-id="${esc(id)}" tabindex="0" role="button" aria-label="${esc(label)}${hasSub ? ' — ' + esc(sublabel) : ''}">`);
  lines.push(shapeSvg);
  lines.push(`<text class="node-label" x="${cx}" y="${labelY}" text-anchor="middle" dominant-baseline="middle">${esc(label)}</text>`);
  if (hasSub) lines.push(`<text class="node-sub" x="${cx}" y="${y + h / 2 + 11}" text-anchor="middle" dominant-baseline="middle">${esc(sublabel)}</text>`);
  if (hasTag) {
    const tw = textWidth(tag, 10) + 12;
    lines.push(`<g class="node-tag"><rect x="${x + w - tw - 6}" y="${y - 9}" width="${tw}" height="16" rx="8"/><text x="${x + w - 6 - tw / 2}" y="${y - 1}" text-anchor="middle" dominant-baseline="middle">${esc(tag)}</text></g>`);
  }
  lines.push('</g>');
  return lines.join('\n');
}

export function pointsToPath(points) {
  return points.map((p, i) => `${i === 0 ? 'M' : 'L'}${round(p[0])} ${round(p[1])}`).join(' ');
}

export function round(n) {
  return Math.round(n * 100) / 100;
}

export function polylineLength(points) {
  let len = 0;
  for (let i = 1; i < points.length; i += 1) {
    len += Math.hypot(points[i][0] - points[i - 1][0], points[i][1] - points[i - 1][1]);
  }
  return len;
}

// Point at fraction t of a polyline.
export function pointAt(points, t) {
  const total = polylineLength(points);
  let target = total * t;
  for (let i = 1; i < points.length; i += 1) {
    const seg = Math.hypot(points[i][0] - points[i - 1][0], points[i][1] - points[i - 1][1]);
    if (target <= seg || i === points.length - 1) {
      const r = seg === 0 ? 0 : target / seg;
      return [points[i - 1][0] + (points[i][0] - points[i - 1][0]) * r, points[i - 1][1] + (points[i][1] - points[i - 1][1]) * r, i - 1];
    }
    target -= seg;
  }
  return [...points[0], 0];
}

// Default label anchor: the middle of the longest segment, which is the
// least likely place to collide with either endpoint node.
export function segmentCenter(points, i) {
  const a = points[i]; const b = points[i + 1];
  return [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2, i];
}

export function longestSegmentCenter(points) {
  let best = 0; let bestLen = -1;
  for (let i = 1; i < points.length; i += 1) {
    const len = Math.hypot(points[i][0] - points[i - 1][0], points[i][1] - points[i - 1][1]);
    if (len > bestLen) { bestLen = len; best = i - 1; }
  }
  const a = points[best]; const b = points[best + 1];
  return [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2, best];
}

export function renderEdge({ id, from, to, points, label, variant = 'default', labelAt, labelDx = 0, labelDy = 0, role, labelSide = 'above', labelSegment }) {
  const path = pointsToPath(points);
  const len = round(polylineLength(points));
  const out = [];
  let labelBox = null;
  out.push(`<g class="edge e-${esc(variant)}${role ? ' r-' + esc(role) : ''}" data-id="${esc(id)}" data-from="${esc(from)}" data-to="${esc(to)}">`);
  out.push(`<path class="edge-hit" d="${path}"/>`);
  out.push(`<path class="edge-line" d="${path}" marker-end="url(#arrow-${esc(variant)})" style="--len:${len}"/>`);
  if (label) {
    const [lx, ly, segIndex] = labelAt !== undefined ? pointAt(points, labelAt)
      : labelSegment !== undefined && points[labelSegment + 1] ? segmentCenter(points, labelSegment)
        : longestSegmentCenter(points);
    const seg = [points[segIndex], points[segIndex + 1] || points[segIndex]];
    const horizontal = Math.abs(seg[1][1] - seg[0][1]) < 1;
    const tw = textWidth(label, 11) + 10;
    const th = 18;
    const bx = lx + labelDx - tw / 2;
    const by = ly + labelDy - th / 2 + (horizontal ? (labelSide === 'below' ? 13 : -13) : 0);
    out.push(`<g class="edge-label"><rect x="${round(bx)}" y="${round(by)}" width="${tw}" height="${th}" rx="4"/><text x="${round(bx + tw / 2)}" y="${round(by + th / 2 + 0.5)}" text-anchor="middle" dominant-baseline="middle">${esc(label)}</text></g>`);
    labelBox = { edgeId: id, x: round(bx), y: round(by), w: tw, h: th };
  }
  out.push('</g>');
  return { svg: out.join('\n'), length: len, label: labelBox };
}

export function renderBoundary({ label, x, y, w, h, kind = 'region', variant = 'default' }) {
  return `<g class="boundary b-${esc(kind)} v-${esc(variant)}"><rect x="${x}" y="${y}" width="${w}" height="${h}" rx="12"/><text x="${x + 12}" y="${y + 16}" dominant-baseline="middle">${esc(label)}</text></g>`;
}

// Orthogonal route between two ports. Each port is {x, y, side}.
export function orthogonalRoute(a, b, stub = 22) {
  const pa = offset(a, stub);
  const pb = offset(b, stub);
  const pts = [[a.x, a.y], pa];
  const horizA = a.side === 'left' || a.side === 'right';
  const horizB = b.side === 'left' || b.side === 'right';
  if (horizA && horizB) {
    const mx = (pa[0] + pb[0]) / 2;
    pts.push([mx, pa[1]], [mx, pb[1]]);
  } else if (!horizA && !horizB) {
    const my = (pa[1] + pb[1]) / 2;
    pts.push([pa[0], my], [pb[0], my]);
  } else if (horizA && !horizB) {
    pts.push([pb[0], pa[1]]);
  } else {
    pts.push([pa[0], pb[1]]);
  }
  pts.push(pb, [b.x, b.y]);
  return dedupe(pts);
}

function offset(port, d) {
  switch (port.side) {
    case 'left': return [port.x - d, port.y];
    case 'right': return [port.x + d, port.y];
    case 'top': return [port.x, port.y - d];
    default: return [port.x, port.y + d];
  }
}

export function dedupe(points) {
  const out = [];
  for (const p of points) {
    const last = out[out.length - 1];
    if (!last || Math.abs(last[0] - p[0]) > 0.01 || Math.abs(last[1] - p[1]) > 0.01) out.push(p);
  }
  // Remove collinear middle points.
  const simplified = [out[0]];
  for (let i = 1; i < out.length - 1; i += 1) {
    const a = simplified[simplified.length - 1];
    const b = out[i];
    const c = out[i + 1];
    const collinear = (Math.abs(a[0] - b[0]) < 0.01 && Math.abs(b[0] - c[0]) < 0.01) || (Math.abs(a[1] - b[1]) < 0.01 && Math.abs(b[1] - c[1]) < 0.01);
    if (!collinear) simplified.push(b);
  }
  simplified.push(out[out.length - 1]);
  return simplified;
}

export function portOf(box, side, offsetRatio = 0.5) {
  const { x, y, w, h } = box;
  switch (side) {
    case 'left': return { x, y: y + h * offsetRatio, side };
    case 'right': return { x: x + w, y: y + h * offsetRatio, side };
    case 'top': return { x: x + w * offsetRatio, y, side };
    default: return { x: x + w * offsetRatio, y: y + h, side };
  }
}

// Choose the most natural pair of sides for two boxes.
export function autoSides(a, b) {
  const acx = a.x + a.w / 2; const acy = a.y + a.h / 2;
  const bcx = b.x + b.w / 2; const bcy = b.y + b.h / 2;
  const dx = bcx - acx; const dy = bcy - acy;
  const gapX = Math.max(0, Math.abs(dx) - (a.w + b.w) / 2);
  const gapY = Math.max(0, Math.abs(dy) - (a.h + b.h) / 2);
  if (gapX >= gapY) {
    return dx >= 0 ? ['right', 'left'] : ['left', 'right'];
  }
  return dy >= 0 ? ['bottom', 'top'] : ['top', 'bottom'];
}

export function boundsOf(boxes, pad = 0) {
  let minX = Infinity; let minY = Infinity; let maxX = -Infinity; let maxY = -Infinity;
  for (const b of boxes) {
    minX = Math.min(minX, b.x); minY = Math.min(minY, b.y);
    maxX = Math.max(maxX, b.x + b.w); maxY = Math.max(maxY, b.y + b.h);
  }
  return { x: minX - pad, y: minY - pad, w: maxX - minX + pad * 2, h: maxY - minY + pad * 2 };
}

export function wrapSvg({ viewBox, body, title }) {
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${viewBox.x} ${viewBox.y} ${viewBox.w} ${viewBox.h}" width="${viewBox.w}" height="${viewBox.h}" role="img" aria-label="${esc(title)}" class="diagram">
${renderDefs()}
<rect class="c-canvas" x="${viewBox.x}" y="${viewBox.y}" width="${viewBox.w}" height="${viewBox.h}" fill="url(#grid)"/>
${body}
</svg>`;
}
