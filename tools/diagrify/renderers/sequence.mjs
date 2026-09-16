import { esc, renderNode, textWidth, wrapSvg, round } from '../lib/svg.mjs';

const PAD = 28;
const P_W = 132;
const P_H = 54;
const COL_GAP = 190;
const ROW_H = 48;
const HEAD_GAP = 30;

export function renderSequence(doc) {
  const px = new Map();
  doc.participants.forEach((p, i) => px.set(p.id, PAD + P_W / 2 + i * COL_GAP));
  const width = PAD * 2 + P_W + (doc.participants.length - 1) * COL_GAP;

  const segments = doc.segments || [];
  const order = new Map(doc.messages.map((m, i) => [m.id, i]));
  // Reserve extra rows for segment headers.
  const extraBefore = new Map();
  for (const s of segments) extraBefore.set(s.fromMessage, (extraBefore.get(s.fromMessage) || 0) + 1);

  const msgY = new Map();
  let y = PAD + P_H + HEAD_GAP + ROW_H / 2;
  for (const m of doc.messages) {
    y += (extraBefore.get(m.id) || 0) * 26;
    msgY.set(m.id, y);
    y += ROW_H + (m.note ? 14 : 0);
  }
  const bottomY = y + 8;
  const height = bottomY + P_H + PAD;

  const body = [];
  const layout = { nodes: [], edges: [], labels: [] };

  // Lifelines
  for (const p of doc.participants) {
    const x = px.get(p.id);
    body.push(`<line class="lifeline" x1="${x}" y1="${PAD + P_H}" x2="${x}" y2="${bottomY}"/>`);
  }

  // Segments (alt / loop / opt)
  segments.forEach((s, i) => {
    const y1 = msgY.get(s.fromMessage) - 32;
    const y2 = msgY.get(s.toMessage) + 18;
    const x1 = PAD + 4 + i * 6;
    const w = width - PAD * 2 - 8 - i * 12;
    const kind = s.kind || 'alt';
    const tagW = textWidth(kind.toUpperCase(), 10) + 14;
    body.push(`<g class="segment s-${esc(kind)}"><rect x="${x1}" y="${y1}" width="${w}" height="${y2 - y1}" rx="8"/><rect class="segment-tag" x="${x1}" y="${y1}" width="${tagW}" height="18" rx="6"/><text class="segment-kind" x="${x1 + tagW / 2}" y="${y1 + 9.5}" text-anchor="middle" dominant-baseline="middle">${esc(kind.toUpperCase())}</text><text class="segment-label" x="${x1 + tagW + 8}" y="${y1 + 9.5}" dominant-baseline="middle">${esc(s.label)}</text></g>`);
  });

  // Activations
  for (const act of doc.activations || []) {
    const x = px.get(act.participant);
    const y1 = msgY.get(act.fromMessage) - 6;
    const y2 = msgY.get(act.toMessage) + 6;
    body.push(`<rect class="activation" x="${x - 6}" y="${y1}" width="12" height="${y2 - y1}" rx="3"/>`);
  }

  // Messages
  for (const m of doc.messages) {
    const x1 = px.get(m.from); const x2 = px.get(m.to); const yy = msgY.get(m.id);
    const kind = m.kind || 'sync';
    const variant = m.variant || (kind === 'return' ? 'dashed' : kind === 'async' ? 'dashed' : 'default');
    let path; let points; let lx; let ly;
    if (m.from === m.to) {
      points = [[x1 + 6, yy - 10], [x1 + 44, yy - 10], [x1 + 44, yy + 12], [x1 + 8, yy + 12]];
      lx = x1 + 52 + textWidth(m.label, 11) / 2; ly = yy + 1;
    } else {
      const dir = x2 > x1 ? 1 : -1;
      points = [[x1 + 6 * dir, yy], [x2 - 6 * dir, yy]];
      lx = (x1 + x2) / 2; ly = yy - 13;
    }
    path = points.map((p, i) => `${i ? 'L' : 'M'}${round(p[0])} ${round(p[1])}`).join(' ');
    const len = round(points.reduce((acc, p, i) => (i ? acc + Math.hypot(p[0] - points[i - 1][0], p[1] - points[i - 1][1]) : 0), 0));
    const tw = textWidth(m.label, 11) + 10;
    body.push(`<g class="edge message e-${esc(variant)} k-${esc(kind)}" data-id="${esc(m.id)}" data-from="${esc(m.from)}" data-to="${esc(m.to)}">`);
    body.push(`<path class="edge-hit" d="${path}"/>`);
    body.push(`<path class="edge-line" d="${path}" marker-end="url(#arrow-${esc(variant)})" style="--len:${len}"/>`);
    body.push(`<g class="edge-label"><rect x="${round(lx - tw / 2)}" y="${ly - 9}" width="${tw}" height="18" rx="4"/><text x="${round(lx)}" y="${ly + 0.5}" text-anchor="middle" dominant-baseline="middle">${esc(m.label)}</text></g>`);
    if (m.note) body.push(`<text class="message-note" x="${round((x1 + x2) / 2)}" y="${yy + 14}" text-anchor="middle" dominant-baseline="middle">${esc(m.note)}</text>`);
    body.push('</g>');
    layout.edges.push({ id: m.id, from: m.from, to: m.to, points });
  }

  // Participants (top and bottom)
  for (const p of doc.participants) {
    const x = px.get(p.id) - P_W / 2;
    body.push(renderNode({ ...p, x, y: PAD, w: P_W, h: P_H }));
    body.push(renderNode({ ...p, id: `${p.id}__mirror`, x, y: bottomY, w: P_W, h: P_H }).replace('class="node ', 'class="node mirror '));
    layout.nodes.push({ id: p.id, x, y: PAD, w: P_W, h: P_H, type: p.type, passable: true, label: p.label, sublabel: p.sublabel });
  }

  const viewBox = { x: 0, y: 0, w: width, h: height };
  return { svg: wrapSvg({ viewBox, body: body.join('\n'), title: doc.meta.title }), layout, viewBox };
}
