/* Build: Vòng chất lượng CCL-CMES — Đánh giá module QC theo ISO 9001:2015
   Nguồn nội dung: CCL-MES-Hybrid/docs/QMS-ISO9001-GAP-ASSESSMENT-2026-08-21.md */

const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, AlignmentType,
  Table, TableRow, TableCell, WidthType, BorderStyle, ShadingType,
  PageBreak, Header, Footer, PageNumber, InternalHyperlink, Bookmark,
  LevelFormat, VerticalAlign, TabStopType,
  PositionalTab, PositionalTabAlignment, PositionalTabLeader, PositionalTabRelativeTo,
} = require('docx');
const fs = require('fs');

// ── Bảng màu: lấy nguyên từ CCL iX (ix.css) ────────────────────────────
const C = {
  navy:     '0E1A2F',
  navy2:    '17253F',
  accent:   '1D4ED8',
  accentInk:'1E3A8A',
  accentTint:'E8EEFF',
  ink:      '16233A',
  ink2:     '33415A',
  inkMut:   '64748B',
  line:     'DFE6F2',
  lineSoft: 'EDF1F8',
  surface2: 'F7F9FD',
  okInk:    '12693C', okTint:    'E4F6EC',
  warnInk:  '8A5A06', warnTint:  'FDF2DD',
  alarmInk: 'A8201C', alarmTint: 'FDECEB',
  infoInk:  '1E3A8A', infoTint:  'E8EEFF',
  idleInk:  '55627A', idleTint:  'EEF1F7',
  white:    'FFFFFF',
};

const SANS = 'Calibri';
const MONO = 'Consolas';

// A4 = 11906 × 16838 DXA · lề trái/phải 1134 (2 cm) ⇒ vùng nội dung 9638
const W = 9638;

// ── Tiện ích ───────────────────────────────────────────────────────────
const NO_BORDER = { style: BorderStyle.NONE, size: 0, color: 'FFFFFF' };
const noBorders = { top: NO_BORDER, bottom: NO_BORDER, left: NO_BORDER, right: NO_BORDER,
                    insideHorizontal: NO_BORDER, insideVertical: NO_BORDER };
const hair = (color = C.line) => ({ style: BorderStyle.SINGLE, size: 4, color });

/**
 * Consolas (và phần lớn font đơn cách trên Windows) KHÔNG dựng được dấu
 * tiếng Việt — "hồ sơ" ra "hô`sơ", "khiếu" ra "khiêú". Vì vậy chữ đơn cách
 * CHỈ được áp cho chuỗi không có ký tự Latin có dấu. Ký hiệu ·  →  ≤  Δ
 * nằm ngoài dải này nên vẫn giữ được font đơn cách.
 */
const VN_RE = /[À-ɏḀ-ỿ]/;
const hasVN = (s) => VN_RE.test(s);
const monoFontFor = (s) => (hasVN(s) ? SANS : MONO);

/** Mini-markdown inline: **đậm** và `mã` — lồng nhau được. */
function runs(text, base = {}) {
  const out = [];
  const re = /(\*\*[\s\S]+?\*\*|`[^`]+`)/g;
  let last = 0, m;
  while ((m = re.exec(text)) !== null) {
    if (m.index > last) out.push(new TextRun({ ...base, text: text.slice(last, m.index) }));
    const tok = m[0];
    if (tok.startsWith('**')) {
      // Đệ quy: cho phép `mã` nằm trong **đậm**.
      out.push(...runs(tok.slice(2, -2), { ...base, bold: true, color: base.color || C.ink }));
    } else {
      const t = tok.slice(1, -1);
      out.push(new TextRun({
        ...base, text: t, font: monoFontFor(t),
        size: hasVN(t) ? (base.size || 21) : (base.size || 21) - 2,
        color: base.color || C.accentInk,
      }));
    }
    last = m.index + tok.length;
  }
  if (last < text.length) out.push(new TextRun({ ...base, text: text.slice(last) }));
  return out;
}

const P = (text, opt = {}) => new Paragraph({
  children: runs(text, { font: SANS, size: opt.size || 21, color: opt.color || C.ink2 }),
  spacing: { after: opt.after === undefined ? 140 : opt.after, line: 276 },
  alignment: opt.align,
  indent: opt.indent,
});

const SPACER = (h = 120) => new Paragraph({ text: '', spacing: { after: h } });

/** `brk` dùng pageBreakBefore chứ KHÔNG chèn PageBreak rời — chèn rời sinh
 *  trang trắng khi mục trước vừa khít đáy trang (đã gặp ở trang 7 bản dựng 1). */
const H1 = (num, text, brk = false) => new Paragraph({
  heading: HeadingLevel.HEADING_1,
  spacing: { before: brk ? 0 : 380, after: 180 },
  pageBreakBefore: brk,
  keepNext: true,
  border: { bottom: { style: BorderStyle.SINGLE, size: 12, color: C.ink } },
  children: [
    new TextRun({ text: num + '  ', font: MONO, size: 20, bold: true, color: C.accent }),
    new TextRun({ text, font: SANS, size: 30, bold: true, color: C.ink }),
  ],
});

const H2 = (text) => new Paragraph({
  heading: HeadingLevel.HEADING_2,
  spacing: { before: 280, after: 130 },
  children: [new TextRun({ text, font: SANS, size: 24, bold: true, color: C.ink })],
});

const H3 = (text) => new Paragraph({
  heading: HeadingLevel.HEADING_3,
  spacing: { before: 220, after: 110 },
  children: [new TextRun({ text, font: SANS, size: 22, bold: true, color: C.accentInk })],
});

const BUL = (text, level = 0) => new Paragraph({
  numbering: { reference: 'bullets', level },
  children: runs(text, { font: SANS, size: 21, color: C.ink2 }),
  spacing: { after: 70, line: 264 },
});

const CHK = (text) => new Paragraph({
  numbering: { reference: 'checks', level: 0 },
  children: runs(text, { font: SANS, size: 21, color: C.ink2 }),
  spacing: { after: 70, line: 264 },
});

const NUM = (text) => new Paragraph({
  numbering: { reference: 'ordered', level: 0 },
  children: runs(text, { font: SANS, size: 21, color: C.ink2 }),
  spacing: { after: 70, line: 264 },
});

/** Ô bảng. */
function cell(content, w, opt = {}) {
  const raw = Array.isArray(content) ? '' : String(content);
  const kids = Array.isArray(content) ? content : [
    new Paragraph({
      children: runs(raw, {
        // Cột đơn cách chỉ giữ được font mono khi nội dung không có dấu.
        font: opt.mono && !hasVN(raw) ? MONO : SANS,
        size: opt.size || 18,
        bold: opt.bold,
        color: opt.color || C.ink2,
      }),
      alignment: opt.align,
      spacing: { after: 0, line: 250 },
    }),
  ];
  return new TableCell({
    children: kids,
    width: { size: w, type: WidthType.DXA },
    shading: opt.fill ? { type: ShadingType.CLEAR, fill: opt.fill, color: 'auto' } : undefined,
    margins: { top: 90, bottom: 90, left: 130, right: 130 },
    verticalAlign: VerticalAlign.TOP,
    borders: opt.borders,
    columnSpan: opt.span,
  });
}

/** Bảng dữ liệu chuẩn: header nền nhạt, hairline giữa các dòng. */
function TABLE(headers, rows, widths, opt = {}) {
  const aligns = opt.aligns || [];
  const monos = opt.monos || [];
  const head = new TableRow({
    tableHeader: true,
    children: headers.map((h, i) => cell(h, widths[i], {
      bold: true, fill: C.surface2, color: C.inkMut, size: 17, align: aligns[i],
    })),
  });
  const body = rows.map((r) => new TableRow({
    children: r.map((c, i) => {
      // Cho phép truyền [text, {opt}] để tô màu ô riêng lẻ
      if (Array.isArray(c)) return cell(c[0], widths[i], { size: 18, align: aligns[i], mono: monos[i], ...c[1] });
      return cell(c, widths[i], { size: 18, align: aligns[i], mono: monos[i] });
    }),
  }));
  return new Table({
    columnWidths: widths,
    width: { size: widths.reduce((a, b) => a + b, 0), type: WidthType.DXA },
    rows: [head, ...body],
    borders: {
      top: hair(), bottom: hair(), left: hair(), right: hair(),
      insideHorizontal: hair(C.lineSoft), insideVertical: hair(C.lineSoft),
    },
  });
}

/** Hộp chú ý — một ô, viền trên dày màu theo mức. */
function CALLOUT(title, lines, tone = 'accent') {
  const map = {
    accent: [C.accent, C.accentTint, C.accentInk],
    warn:   [C.warnInk, C.warnTint, C.warnInk],
    alarm:  [C.alarmInk, C.alarmTint, C.alarmInk],
    ok:     [C.okInk, C.okTint, C.okInk],
  };
  const [bar, fill, ink] = map[tone];
  const kids = [
    new Paragraph({
      children: [new TextRun({ text: title.toUpperCase(), font: SANS, size: 16, bold: true, color: ink, characterSpacing: 20 })],
      spacing: { after: 110 },
    }),
    ...lines.map((t, i) => new Paragraph({
      children: runs(t, { font: SANS, size: 20, color: C.ink2 }),
      spacing: { after: i === lines.length - 1 ? 0 : 110, line: 270 },
    })),
  ];
  return new Table({
    columnWidths: [W],
    width: { size: W, type: WidthType.DXA },
    rows: [new TableRow({
      children: [new TableCell({
        children: kids,
        width: { size: W, type: WidthType.DXA },
        shading: { type: ShadingType.CLEAR, fill, color: 'auto' },
        margins: { top: 160, bottom: 160, left: 180, right: 180 },
        borders: {
          top: { style: BorderStyle.SINGLE, size: 18, color: bar },
          bottom: hair(C.line), left: hair(C.line), right: hair(C.line),
        },
      })],
    })],
  });
}

/** Khối phát hiện: tiêu đề + hàng meta + bảng 2 cột Bằng chứng | Rủi ro. */
function FINDING(id, title, tone, clause, evidence, risk) {
  const toneMap = { alarm: [C.alarmInk, C.alarmTint, 'NẶNG'], warn: [C.warnInk, C.warnTint, 'NHẸ'] };
  const [ink, tint, label] = toneMap[tone];
  const half = Math.floor(W / 2);
  return [
    new Paragraph({
      heading: HeadingLevel.HEADING_3,
      spacing: { before: 300, after: 60 },
      keepNext: true,
      border: { top: { style: BorderStyle.SINGLE, size: 8, color: C.line } },
      children: [
        new TextRun({ text: id + ' — ', font: MONO, size: 22, bold: true, color: ink }),
        new TextRun({ text: title, font: SANS, size: 22, bold: true, color: C.ink }),
      ],
    }),
    new Paragraph({
      spacing: { after: 130 },
      keepNext: true,
      children: [
        new TextRun({ text: '  ' + label + '  ', font: SANS, size: 15, bold: true, color: ink,
                      shading: { type: ShadingType.CLEAR, fill: tint, color: 'auto' }, characterSpacing: 20 }),
        new TextRun({ text: '   Điều ' + clause, font: MONO, size: 17, color: C.inkMut }),
      ],
    }),
    new Table({
      columnWidths: [half, W - half],
      width: { size: W, type: WidthType.DXA },
      rows: [new TableRow({
        cantSplit: true,
        children: [
          new TableCell({
            width: { size: half, type: WidthType.DXA },
            margins: { top: 120, bottom: 120, left: 0, right: 200 },
            borders: { top: NO_BORDER, bottom: NO_BORDER, left: NO_BORDER, right: hair(C.lineSoft) },
            children: [
              new Paragraph({ children: [new TextRun({ text: 'BẰNG CHỨNG', font: SANS, size: 15, bold: true, color: C.inkMut, characterSpacing: 24 })], spacing: { after: 90 } }),
              ...evidence,
            ],
          }),
          new TableCell({
            width: { size: W - half, type: WidthType.DXA },
            margins: { top: 120, bottom: 120, left: 200, right: 0 },
            borders: { top: NO_BORDER, bottom: NO_BORDER, left: NO_BORDER, right: NO_BORDER },
            children: [
              new Paragraph({ children: [new TextRun({ text: 'RỦI RO KHI AUDIT', font: SANS, size: 15, bold: true, color: C.inkMut, characterSpacing: 24 })], spacing: { after: 90 } }),
              ...risk,
            ],
          }),
        ],
      })],
    }),
  ];
}

/** Khối mã / SQL — nền nhạt, chữ đơn cách. */
function CODE(lines, width = W) {
  return new Table({
    columnWidths: [width],
    width: { size: width, type: WidthType.DXA },
    rows: [new TableRow({
      children: [new TableCell({
        width: { size: width, type: WidthType.DXA },
        shading: { type: ShadingType.CLEAR, fill: C.surface2, color: 'auto' },
        margins: { top: 130, bottom: 130, left: 150, right: 150 },
        borders: { top: hair(), bottom: hair(), left: hair(), right: hair() },
        children: lines.map((l, i) => {
          const isComment = /^\s*(--|#|\/\/)/.test(l);
          const vn = hasVN(l);        // dòng có dấu ⇒ phải rơi về font có chân dấu
          return new Paragraph({
            children: [new TextRun({
              text: l === '' ? ' ' : l,
              font: vn ? SANS : MONO,
              size: vn ? 17 : 16,
              italics: vn && isComment,
              color: isComment ? C.inkMut : C.ink2,
            })],
            spacing: { after: i === lines.length - 1 ? 0 : 20, line: 240 },
          });
        }),
      })],
    })],
  });
}

/** Ô nhỏ trên dải chỉ số trang bìa. */
function scoreTile(num, label, color) {
  return [
    new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { after: 40 },
      children: [new TextRun({ text: num, font: SANS, size: 44, bold: true, color })],
    }),
    new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { after: 0 },
      children: [new TextRun({ text: label.toUpperCase(), font: SANS, size: 14, bold: true, color: C.inkMut, characterSpacing: 20 })],
    }),
  ];
}

// ═══════════════════════════════════════════════════════════════════════
// TRANG BÌA
// ═══════════════════════════════════════════════════════════════════════
const coverBand = new Table({
  columnWidths: [W],
  width: { size: W, type: WidthType.DXA },
  borders: noBorders,
  rows: [new TableRow({
    children: [new TableCell({
      width: { size: W, type: WidthType.DXA },
      shading: { type: ShadingType.CLEAR, fill: C.navy, color: 'auto' },
      margins: { top: 560, bottom: 560, left: 420, right: 420 },
      borders: noBorders,
      children: [
        new Paragraph({
          spacing: { after: 260 },
          children: [new TextRun({
            text: 'CCL DESIGN VIETNAM · HẢI DƯƠNG · ĐÁNH GIÁ HỆ THỐNG CHẤT LƯỢNG',
            font: SANS, size: 15, bold: true, color: '93A7C9', characterSpacing: 30,
          })],
        }),
        new Paragraph({
          spacing: { after: 120 },
          children: [new TextRun({ text: 'Vòng chất lượng CCL-CMES', font: SANS, size: 56, bold: true, color: 'E8EEFB' })],
        }),
        new Paragraph({
          spacing: { after: 300 },
          children: [new TextRun({ text: 'Đánh giá module QC theo ISO 9001:2015', font: SANS, size: 26, color: '93A7C9' })],
        }),
        new Paragraph({
          spacing: { after: 0 },
          border: { top: { style: BorderStyle.SINGLE, size: 6, color: '22314F' } },
          children: [new TextRun({ text: '', size: 2 })],
        }),
        new Paragraph({
          spacing: { before: 220, after: 0, line: 300 },
          children: runs(
            'CCL-CMES đã xây xong nửa **KIỂM SOÁT** của ISO 9001: bằng chứng bất biến, vết audit, chữ ký nhiều vai, kiểm soát phiên bản spec. Nửa **CẢI TIẾN** chưa tồn tại — hệ dừng ở Ok/NG, không lưu giá trị đo, không có hồ sơ sự không phù hợp, không thi hành AQL đã khai báo, không có sổ thiết bị đo.',
            { font: SANS, size: 20, color: 'C3D0E8' }
          ),
        }),
      ],
    })],
  })],
});

const metaRows = [
  ['Ngày lập', '21 · 08 · 2026'],
  ['Chuẩn đối chiếu', 'ISO 9001:2015 (+ Amd 1:2024)'],
  ['Phạm vi', 'IQC · IPQC · FQC · OQC · Thư viện hạng mục · Truy xuất · iCRA'],
  ['Nguồn bằng chứng', 'Mã nguồn nhánh main + bản sao CHỈ-ĐỌC của data/ccl_mes.db'],
  ['Tính toàn vẹn dữ liệu', 'Không có dòng dữ liệu nào bị thay đổi trong quá trình đánh giá'],
  ['Trạng thái', 'DRAFT — chờ Henry duyệt'],
];

const coverMeta = new Table({
  columnWidths: [2400, W - 2400],
  width: { size: W, type: WidthType.DXA },
  borders: {
    top: NO_BORDER, bottom: NO_BORDER, left: NO_BORDER, right: NO_BORDER,
    insideHorizontal: hair(C.lineSoft), insideVertical: NO_BORDER,
  },
  rows: metaRows.map(([k, v]) => new TableRow({
    children: [
      cell(k.toUpperCase(), 2400, { bold: true, size: 15, color: C.inkMut }),
      cell(v, W - 2400, { size: 19, color: C.ink }),
    ],
  })),
});

const scoreStrip = new Table({
  columnWidths: [1928, 1928, 1928, 1927, 1927],
  width: { size: W, type: WidthType.DXA },
  borders: {
    top: hair(), bottom: hair(), left: NO_BORDER, right: NO_BORDER,
    insideHorizontal: NO_BORDER, insideVertical: hair(C.lineSoft),
  },
  rows: [new TableRow({
    children: [
      new TableCell({ width: { size: 1928, type: WidthType.DXA }, margins: { top: 200, bottom: 200 }, children: scoreTile('6', 'Không phù hợp nặng', C.alarmInk) }),
      new TableCell({ width: { size: 1928, type: WidthType.DXA }, margins: { top: 200, bottom: 200 }, children: scoreTile('9', 'Không phù hợp nhẹ', C.warnInk) }),
      new TableCell({ width: { size: 1928, type: WidthType.DXA }, margins: { top: 200, bottom: 200 }, children: scoreTile('7', 'Điểm mạnh giữ nguyên', C.okInk) }),
      new TableCell({ width: { size: 1927, type: WidthType.DXA }, margins: { top: 200, bottom: 200 }, children: scoreTile('60', 'Bảng DB khảo sát', C.ink) }),
      new TableCell({ width: { size: 1927, type: WidthType.DXA }, margins: { top: 200, bottom: 200 }, children: scoreTile('2.521', 'Dòng audit đọc', C.ink) }),
    ],
  })],
});

// ═══════════════════════════════════════════════════════════════════════
// NỘI DUNG
// ═══════════════════════════════════════════════════════════════════════
const body = [];
const add = (...x) => body.push(...x.flat());

// —— Trang bìa ——
add(coverBand, SPACER(320), scoreStrip, SPACER(320), coverMeta);
add(new Paragraph({ children: [new PageBreak()] }));

// —— Mục lục ——
// TĨNH, không dùng trường TOC của Word: trường chỉ được Word điền khi mở, còn
// LibreOffice / Preview / Pages render ra TRANG TRẮNG (đã gặp ở bản dựng 1).
// Số trang lấy từ lần render trước và được kiểm lại sau mỗi lần dựng.
add(new Paragraph({
  spacing: { after: 240 },
  border: { bottom: { style: BorderStyle.SINGLE, size: 12, color: C.ink } },
  children: [new TextRun({ text: 'MỤC LỤC', font: SANS, size: 26, bold: true, color: C.ink, characterSpacing: 30 })],
}));

// Số trang ĐÃ KIỂM bằng pdftotext trên bản render (xem ghi chú cuối tệp).
const TOC = [
  ['00', 'Đính chính chuẩn tham chiếu', 3],
  ['01', 'Tóm tắt điều hành', 3],
  ['02', 'Phạm vi & phương pháp', 4],
  ['03', 'Bản đồ module QC hiện có', 4],
  ['04', 'Đối chiếu điều khoản ISO 9001:2015', 6],
  ['05', 'Phát hiện — không phù hợp NẶNG', 7],
  ['06', 'Phát hiện — không phù hợp NHẸ', 10],
  ['07', 'Điểm mạnh phải giữ', 11],
  ['08', 'Ba phương án cải tiến', 12],
  ['09', 'Kế hoạch triển khai 5 đợt', 14],
  ['10', 'KPI & tiêu chí nghiệm thu', 17],
  ['11', 'Rủi ro & STOP-gate', 17],
  ['PL-A', 'Phụ lục — bằng chứng SQL', 18],
  ['PL-B', 'Phụ lục — schema đề xuất', 19],
];

TOC.forEach(([num, title, page]) => add(new Paragraph({
  spacing: { after: 130, line: 264 },
  tabStops: [{ type: TabStopType.RIGHT, position: W, leader: 'dot' }],
  children: [
    new TextRun({ text: num.padEnd(6, ' '), font: MONO, size: 19, bold: true, color: C.accent }),
    new TextRun({ text: title, font: SANS, size: 21, color: C.ink }),
    new TextRun({ text: '\t', font: SANS, size: 21 }),
    new TextRun({ text: String(page), font: SANS, size: 21, bold: true, color: C.ink }),
  ],
})));

add(SPACER(260));
add(new Paragraph({
  spacing: { after: 0 },
  children: [new TextRun({
    text: 'Mã phát hiện: M1–M6 = không phù hợp nặng  ·  m7–m15 = không phù hợp nhẹ. Mỗi phát hiện kèm đường dẫn tệp : số dòng hoặc câu truy vấn tái tạo được.',
    font: SANS, size: 17, color: C.inkMut, italics: true,
  })],
}));

// ── 00 · Đính chính chuẩn tham chiếu ──────────────────────────────────
add(H1('00', 'Đính chính chuẩn tham chiếu', true));
add(CALLOUT('Cần thống nhất trước khi đọc tiếp', [
  '**Không tồn tại ISO 9001:2005.** Các phiên bản ISO 9001 đã phát hành là 1987 · 1994 · 2000 · 2008 · **2015**. Bản 2015 là bản hiện hành và là bản duy nhất còn chứng nhận được, kèm sửa đổi Amd 1:2024 bổ sung yêu cầu xem xét biến đổi khí hậu ở điều 4.1/4.2.',
  'Số hiệu ":2005" nhiều khả năng là nhớ nhầm sang **ISO 9000:2005 — Cơ sở và từ vựng**, tài liệu định nghĩa thuật ngữ chứ không đặt ra yêu cầu chứng nhận, và bản thân nó cũng đã bị ISO 9000:2015 thay thế.',
  'Toàn bộ báo cáo này đối chiếu theo **ISO 9001:2015**. Nếu CCL Design Vietnam đang giữ chứng chỉ theo một chuẩn khác, xin gửi số hiệu chứng chỉ để soát lại phần điều khoản — phần phát hiện kỹ thuật không đổi.',
], 'warn'));
add(SPACER(220));
add(P('Với ngành in nhãn / die-cut, ISO 9001 chỉ là nền. Các chuẩn thực sự bị khách hàng viện dẫn khi audit dây chuyền nhãn nằm ở lớp bên trên, và báo cáo có dẫn chiếu tới khi phát hiện chạm vào chúng:'));
add(TABLE(
  ['Chuẩn', 'Nội dung', 'Phát hiện'],
  [
    ['ISO 2859-1', 'Lấy mẫu kiểm tra theo thuộc tính (AQL) — bảng cỡ mẫu, Ac/Re', 'M5'],
    ['ISO/IEC 15416', 'Chấm điểm chất lượng in mã vạch 1D (grade A–F)', 'M1, M2'],
    ['ISO 13655 · CIE ΔE00', 'Đo màu phản xạ, sai khác màu', 'M1, M2'],
    ['ISO 12647-2/-6', 'Kiểm soát quá trình in offset / flexo', 'M2, m12'],
    ['ISO 15378', 'GMP cho bao bì cấp 1 dược phẩm', 'M3, M4, M6, m11'],
    ['IATF 16949', 'QMS ô tô — nếu CCL Hải Dương có khách automotive', 'M1, M5, Đợt 4'],
    ['21 CFR Part 11', 'Hồ sơ & chữ ký điện tử (khách dược Mỹ)', 'm11'],
  ],
  [2100, 5538, 2000],
  { monos: [true, false, true] }
));
add(SPACER(80));
add(P('Cần xác nhận phạm vi áp dụng với QA CCL trước Đợt 1.', { size: 17, color: C.inkMut }));

// ── 01 · Tóm tắt điều hành ────────────────────────────────────────────
add(H1('01', 'Tóm tắt điều hành'));
add(P('CCL-CMES là một MES có kỷ luật kỹ thuật trên mức trung bình rõ rệt cho một hệ nội bộ: bằng chứng đóng băng append-only có hash SHA-256, vết audit chụp cả vai trò tại thời điểm hành động, luật ba chữ ký OQC tách vai được viết thành hàm thuần và phủ unit test, vòng đời lô nguyên vật liệu có cách ly và hai chữ ký khi gia hạn. Đó là những thứ mà phần lớn MES tự xây **không** có, và chúng đúng tinh thần điều 7.5.3 và 8.5.2 của ISO 9001.'));
add(P('Nhưng khi đặt cạnh yêu cầu của ISO 9001, hệ đang **lệch hẳn về một phía**. Nó ghi lại rất tốt việc đã kiểm, nhưng gần như không ghi lại được kiểm ra cái gì và sau đó làm gì:'));
add(BUL('**Không có giá trị đo.** Bảng hạng mục kiểm chỉ có ba trạng thái `Pending / Ok / Ng`. Không có cột nào chứa số đo. Nghĩa là hệ không chứng minh được sự phù hợp với tiêu chí chấp nhận (điều 8.6), chỉ chứng minh được rằng có người đã bấm nút.'));
add(BUL('**Ngưỡng đã viết nhưng chưa nối.** `QcThresholdResolver` — chuỗi giải ngưỡng ba tầng — có đủ mã và 13 ca unit test, nhưng **không một chỗ nào trong mã production gọi tới nó**. Ngưỡng ΔE ≤ 2 hiện là một dòng chữ trong tài liệu, không phải một cái cổng.'));
add(BUL('**Vòng không phù hợp chưa đóng.** Màn hình iCRA (CAPA) đang là dữ liệu giả cứng trong mã giao diện. Không có bảng `NonConformance`, không có disposition, không có CAPA, không có SPC. Đây chính là điều 8.7 và 10.2 — hai điều khoản mà đánh giá viên hỏi đầu tiên.'));
add(BUL('**AQL khai báo nhưng không thi hành.** Cả 59 hạng mục thư viện đều có ô AQL và kế hoạch lấy mẫu, nhưng không dòng mã nào đọc chúng. Logic readiness cho phép Pass ngay cả khi có hạng mục NG, không tính số chấp nhận, không lưu cỡ mẫu thực tế.'));
add(BUL('**Không có sổ thiết bị đo.** Tìm toàn bộ mã nguồn: 0 kết quả cho "calibration". Một nhà in nhãn dùng quang phổ kế, thước cặp và máy soi mã vạch mà không có lịch hiệu chuẩn là điểm bị bắt chắc chắn ở điều 7.1.5.2.'));
add(SPACER(160));
add(CALLOUT('Kết luận', [
  'Ở trạng thái hôm nay, CCL-CMES **không đủ để một mình đứng ra chịu một cuộc audit khách hàng** về điều 8.6, 8.7 và 10.2. Hệ vẫn là một MES tốt và một kho bằng chứng đáng tin; nó thiếu đúng lớp biến bằng chứng thành công cụ kiểm soát và cải tiến.',
  'Tin tốt: **không có phát hiện nào đòi đập đi làm lại.** Mô hình đóng băng, khoá `DefectCode` trong thư viện v5, và cấu trúc snapshot hiện có đã là móng đúng. Toàn bộ 15 điểm thiếu đều đóng được bằng cách **thêm**, không phải **sửa** — và dự án đã tự nhận diện hai trong số đó ở hạng mục C1/C2 của backlog cải tiến.',
], 'alarm'));

// ── 02 · Phạm vi & phương pháp ────────────────────────────────────────
add(H1('02', 'Phạm vi & phương pháp'));
add(P('Đánh giá dựa trên hai nguồn bằng chứng độc lập, không dựa vào tài liệu mô tả:'));
add(NUM('**Mã nguồn** — `src/CCL.MES.Domain`, `src/CCL.MES.Application`, `CCL-MES-Hybrid/src/CCL.MES.Api`, `CCL-MES-Hybrid/src/CCL.MES.Hybrid.Razor`. Mọi khẳng định về "có / không có" đều kèm đường dẫn tệp và số dòng.'));
add(NUM('**Cơ sở dữ liệu vận hành** — `data/ccl_mes.db` (18,6 MB, cập nhật 21/08/2026). Đã sao ra thư mục tạm và chỉ chạy `SELECT`; DB gốc không bị mở ghi, không bị đổi một byte nào. 60 bảng nghiệp vụ được đếm và lấy mẫu.'));
add(SPACER(100));
add(P('Việc đọc dữ liệu thật là chủ ý. Một module có thể tồn tại đầy đủ trong mã mà chưa từng được dùng — và với ISO, **một quy trình không có hồ sơ thì không tồn tại**. Nhiều phát hiện dưới đây chỉ lộ ra khi đếm dòng, không lộ ra khi đọc mã.'));
add(P('**Ngoài phạm vi:** đánh giá nội bộ (9.2), xem xét của lãnh đạo (9.3), bối cảnh tổ chức (4.1–4.4), chính sách và mục tiêu chất lượng (5.2, 6.2). Đây là quy trình cấp doanh nghiệp; báo cáo chỉ nêu ranh giới, không kết luận chúng thiếu.'));

// ── 03 · Bản đồ module ────────────────────────────────────────────────
add(H1('03', 'Bản đồ module QC hiện có'));
add(P('Cột **Vận hành thực** lấy từ số dòng trong DB — không lấy từ tài liệu. Đây là chỗ khoảng cách giữa "đã code" và "đang dùng" lộ ra rõ nhất.'));
add(TABLE(
  ['Module', 'Thực thể chính', 'Dòng DB', 'Vận hành thực'],
  [
    ['IQC — Kiểm nhập', 'IqcInspection / IqcResultDetail', '25 / 7', ['Mới khởi động — 23/25 phiếu còn Pending', { color: C.warnInk }]],
    ['Lô NVL — Truy xuất vật tư', 'MaterialLot', '27', ['Chưa thông — 27/27 còn Quarantine', { color: C.warnInk, bold: true }]],
    ['IPQC — Kiểm đầu chuyền', 'WoIpqcCheck / WoIpqcCheckItem', '7 / 117', ['Đang chạy', { color: C.okInk }]],
    ['FQC / OQC — Kiểm cuối & xuất', 'WoQcCheck / WoQcCheckItem / WoQcPhoto', '8 / 83 / 0', ['Chạy, chưa dùng ảnh', { color: C.warnInk }]],
    ['Thư viện hạng mục v5', 'CheckItemLibrary', '59', ['Dùng, chưa phân tầng công đoạn', { color: C.warnInk }]],
    ['Kế hoạch kiểm theo spec', 'SpecQcWindow / QcCriterion / SpecQcCapture', '0 / 0 / 0', ['BỎ HOANG', { color: C.alarmInk, bold: true }]],
    ['Truy xuất', 'WoTraceSnapshot / WoTraceIndex', '17', ['Đang chạy', { color: C.okInk }]],
    ['iCRA / CAPA', '— không có thực thể —', '—', ['DỮ LIỆU GIẢ (QmsMock.Icra)', { color: C.alarmInk, bold: true }]],
    ['Kiểm soát tài liệu', 'ProductRevision / Drawing', '340', ['Đang chạy tốt', { color: C.okInk }]],
    ['Vết audit', 'AuditLog', '2.521', ['Đang chạy tốt', { color: C.okInk }]],
  ],
  [2200, 3100, 1100, 3238],
  { monos: [false, true, false, false], aligns: [null, null, AlignmentType.RIGHT, null] }
));
add(SPACER(80));
add(P('Số liệu truy vấn ngày 21/08/2026 trên bản sao chỉ-đọc của `data/ccl_mes.db`.', { size: 17, color: C.inkMut }));
add(SPACER(180));
add(CALLOUT('Ba con số đáng chú ý nhất', [
  '**0 / 0 / 0** cho bộ kế hoạch kiểm theo spec. Đây là thứ ISO 9001 điều 8.1 và 8.5.1(c) yêu cầu — "xác định hoạt động theo dõi và đo lường ở các giai đoạn thích hợp để kiểm tra xác nhận tiêu chí đã được đáp ứng". Mã đã có, có cỡ mẫu, có tần suất, có hành động khi loại, có 5 kiểu tiêu chí. Nó chỉ chưa có ai nhập dữ liệu vào. **Hệ quả: QC hiện chạy theo dòng sản phẩm, không chạy theo yêu cầu của khách hàng cho mã hàng đó.**',
  '**0** ảnh bằng chứng QC, trên 83 hạng mục FQC/OQC đã kết luận.',
  '**27/27** lô nguyên vật liệu còn ở trạng thái cách ly. Nếu con số này giữ nguyên khi go-live thì hoặc IQC chưa được vận hành, hoặc luật chặn tiêu thụ đang bị đi vòng ở đâu đó — cần xác minh trước, vì đó là điều 8.5.2.',
]));

// ── 04 · Đối chiếu điều khoản ─────────────────────────────────────────
add(H1('04', 'Đối chiếu điều khoản ISO 9001:2015', true));
add(P('Chỉ liệt kê các điều khoản mà một MES/QMS số có thể gánh. Điều khoản thuộc cấp doanh nghiệp được đánh dấu **Ngoài hệ**.'));

const LV = {
  ok:    ['ĐẠT', C.okInk],
  part:  ['MỘT PHẦN', C.warnInk],
  no:    ['CHƯA ĐẠT', C.alarmInk],
  out:   ['NGOÀI HỆ', C.idleInk],
};
const lv = (k) => [LV[k][0], { color: LV[k][1], bold: true, size: 16 }];

add(TABLE(
  ['Điều', 'Yêu cầu', 'Mức', 'Ghi chú'],
  [
    ['7.1.5.1', 'Nguồn lực theo dõi & đo lường phù hợp', lv('no'), 'Không có đăng ký thiết bị đo'],
    ['7.1.5.2', 'Liên kết chuẩn đo lường — hiệu chuẩn, nhận biết, bảo vệ', lv('no'), 'M6 — 0 kết quả tìm kiếm toàn mã nguồn'],
    ['7.2', 'Năng lực người thực hiện công việc ảnh hưởng chất lượng', lv('no'), 'm9 — chỉ có chuỗi vai trò'],
    ['7.5.2', 'Tạo lập & cập nhật thông tin dạng văn bản', lv('ok'), 'ProductRevision 5 trạng thái + phả hệ'],
    ['7.5.3', 'Kiểm soát thông tin — bảo vệ khỏi sửa đổi ngoài ý muốn', lv('ok'), 'Snapshot append-only + hash + audit trail'],
    ['8.1', 'Hoạch định & kiểm soát tác nghiệp — tiêu chí chấp nhận', lv('part'), 'm7 — kế hoạch kiểm theo spec bỏ trống'],
    ['8.3', 'Thiết kế & phát triển (NPI)', lv('ok'), 'Module NPI + duyệt bản vẽ 3 vai'],
    ['8.4.1', 'Đánh giá, lựa chọn, theo dõi kết quả nhà cung cấp', lv('no'), 'm10 — nhà cung cấp là chuỗi tự do'],
    ['8.5.1(c)', 'Theo dõi & đo lường ở giai đoạn thích hợp', lv('part'), 'Có cổng kiểm, không có số đo'],
    ['8.5.1(e)', 'Người có năng lực, kể cả trình độ được yêu cầu', lv('part'), 'RBAC có, chứng nhận năng lực không'],
    ['8.5.2', 'Nhận biết & truy xuất nguồn gốc', lv('ok'), 'Lô NVL khoá số + snapshot đóng băng'],
    ['8.5.6', 'Kiểm soát thay đổi', lv('ok'), 'Revision + ChangeSummary + không hồi tố'],
    [['8.6', { bold: true, color: C.ink }], ['Thông qua sản phẩm — bằng chứng phù hợp tiêu chí + truy được người thông qua', { bold: true, color: C.ink }], lv('part'), ['Người ký: đạt. Bằng chứng phù hợp: chưa (M1, M2, M5)', { bold: true, color: C.ink }]],
    [['8.7.1', { bold: true, color: C.ink }], ['Kiểm soát đầu ra không phù hợp — cách ly, thông báo khách, nhượng bộ', { bold: true, color: C.ink }], lv('no'), ['M3, M4 — không có hồ sơ NC, không có disposition', { bold: true, color: C.ink }]],
    [['8.7.2', { bold: true, color: C.ink }], ['Lưu hồ sơ: mô tả NC, hành động, nhượng bộ, người quyết định', { bold: true, color: C.ink }], lv('no'), ['M4 — nhượng bộ chỉ có văn bản tự do', { bold: true, color: C.ink }]],
    ['9.1.1', 'Theo dõi, đo lường, phân tích, đánh giá', lv('part'), 'Có OEE, không có dữ liệu chất lượng phân tích được'],
    ['9.1.2', 'Sự thoả mãn khách hàng', lv('out'), 'm14 — không có module khiếu nại'],
    ['9.1.3', 'Phân tích & đánh giá dữ liệu', lv('no'), 'M1 + m12 — thiếu biến số & nguyên nhân phế'],
    ['9.2 · 9.3', 'Đánh giá nội bộ · xem xét của lãnh đạo', lv('out'), 'Quy trình cấp doanh nghiệp'],
    [['10.2', { bold: true, color: C.ink }], ['Sự không phù hợp & hành động khắc phục', { bold: true, color: C.ink }], lv('no'), ['M3 — iCRA là dữ liệu giả', { bold: true, color: C.ink }]],
    ['10.3', 'Cải tiến liên tục', lv('no'), 'Không có SPC, không có xu hướng lỗi'],
  ],
  [1300, 3500, 1300, 3538],
  { monos: [true, false, false, false], aligns: [null, null, AlignmentType.CENTER, null] }
));
add(SPACER(80));
add(P('21 điều khoản đối chiếu: **6 đạt · 6 đạt một phần · 8 chưa đạt · 3 ngoài phạm vi hệ thống**.', { size: 17, color: C.inkMut }));

// ── 05 · Phát hiện nặng ───────────────────────────────────────────────
add(H1('05', 'Phát hiện — không phù hợp NẶNG', true));
add(P('"Nặng" ở đây nghĩa là: nếu khách hàng audit dây chuyền nhãn vào tuần sau, đây là những điểm sẽ bị ghi nhận thành finding chính thức.'));

const ev = (t) => new Paragraph({ children: runs(t, { font: SANS, size: 19, color: C.ink2 }), spacing: { after: 100, line: 264 } });
const evb = (t) => new Paragraph({ numbering: { reference: 'bullets', level: 0 }, children: runs(t, { font: SANS, size: 19, color: C.ink2 }), spacing: { after: 60, line: 258 } });

add(FINDING('M1', 'Hệ không lưu giá trị đo — mọi kết quả kiểm chỉ là Ok / NG', 'alarm', '8.6 · 8.5.1(c) · 9.1.3',
  [
    ev('Cấu trúc bảng thực tế trong DB vận hành (`PRAGMA table_info`):'),
    CODE(['WoIpqcCheckItems + WoQcCheckItems', '', 'ItemKey · Status · NgReasonCode', 'NgNote  · Sort   · PhotoBlobId', '', '-- KHÔNG có cột nào chứa số đo'], 4600),
    new Paragraph({ text: '', spacing: { after: 100 } }),
    ev('`Status` là enum `{ Pending, Ok, Ng }` (`src/CCL.MES.Domain/Enums.cs`). Thực thể cũ `QcResultDetail` **có** trường `MeasuredValue` — nhưng bảng `QcInspections` hiện **0 dòng**, tức nhánh đó đã chết.'),
  ],
  [
    ev('Đánh giá viên hỏi: "Cho tôi xem bằng chứng lô này đạt tiêu chí màu ΔE ≤ 2." Hệ trả về một ô tick xanh. **Ô tick không phải bằng chứng phù hợp — nó là bằng chứng có người bấm nút.**'),
    ev('Hệ quả dây chuyền: không có biến số thì không có SPC, không có năng lực quá trình, không có phân tích xu hướng, không có MSA. Toàn bộ điều 9.1.3 và 10.3 mất nền. Với khách automotive theo IATF 16949, đây là điểm chặn.'),
  ]));

add(FINDING('M2', 'Chuỗi giải ngưỡng đã viết đủ, có test, nhưng chưa nối vào đường ghi', 'alarm', '8.5.1(c) · 8.6 · (kéo theo 7.5.2)',
  [
    ev('Tìm toàn bộ kho mã (loại trừ `obj/`, `bin/`, worktree) cho `QcThresholdResolver`:'),
    evb('13 lời gọi — **tất cả** trong `tests/CCL.MES.Tests/Unit/QcThresholdResolverTests.cs`'),
    evb('1 tham chiếu `<see cref>` trong chú thích XML (`MasterData.cs:26`)'),
    evb('1 định nghĩa lớp (`QcThresholdResolver.cs:28`)'),
    evb('**0 lời gọi trong mã production**'),
    new Paragraph({ text: '', spacing: { after: 80 } }),
    ev('Cột `Product.QcProfileOverride` **có** được đọc tại `WoQcMutationControllerBase.cs:214` — nhưng để chọn bộ hạng mục nào hiện ra, không phải để so sánh ngưỡng.'),
  ],
  [
    ev('Tài liệu thiết kế mô tả một chuỗi ngưỡng ba tầng (ghi đè theo mã hàng → hồ sơ đóng băng → mặc định). Đánh giá viên đọc tài liệu, rồi yêu cầu chứng minh nó đang chạy. Không chứng minh được — vì máy không so sánh gì cả; người kiểm tự nhìn và tự quyết.'),
    ev('Đây là dạng phát hiện tệ nhất: **tài liệu mô tả một kiểm soát không tồn tại**. Nó chuyển finding từ "thiếu kiểm soát" sang "tài liệu không phản ánh thực tế", đụng luôn điều 7.5.2.'),
  ]));

add(FINDING('M3', 'Vòng không phù hợp chưa tồn tại — iCRA là dữ liệu giả', 'alarm', '8.7.1 · 10.2 · 10.3',
  [
    ev('`CCL-MES-Hybrid/src/CCL.MES.Hybrid.Razor/Pages/IcraModule.razor:27` lặp trên `QmsMock.Icra` — một danh sách tĩnh khai báo cứng trong `CCL.MES.Hybrid.Client/Qms/QmsUiModels.cs:46`. Chú thích trong chính tệp đó ghi: "Static mock (QmsMock)".'),
    ev('Trong 60 bảng của DB: không có `NonConformances`, không có `Dispositions`, không có `Capa`. Tìm "capa", "nonconform", "corrective action" trong `*.cs`: chỉ trúng tài liệu và định nghĩa agent, không trúng thực thể nào.'),
  ],
  [
    ev('Điều 8.7 và 10.2 nằm trong nhóm câu hỏi mở màn của mọi cuộc audit. Một màn hình hiển thị dữ liệu giả **tệ hơn** không có màn hình: nó tạo ấn tượng có quy trình, và khi đánh giá viên xin hồ sơ NC thật thì phát hiện ra không có gì phía sau. Đó là finding về **tính chính trực của hệ thống**, không chỉ về tính năng thiếu.'),
    ev('**Khuyến nghị tức thời:** hoặc đóng vòng thật, hoặc gỡ màn hình iCRA khỏi menu trước khi bàn giao. Không để tồn tại ở dạng hiện nay.'),
  ]));

add(FINDING('M4', 'Nhượng bộ có chữ ký nhưng thiếu bốn yếu tố mà 8.7.2 bắt buộc', 'alarm', '8.7.1(c)(d) · 8.7.2',
  [
    ev('Hai bản ghi nhượng bộ thật trong DB (SPECIAL_ACCEPT → QA duyệt):'),
    CODE([
      'WorkOrderId         = 3',
      'Judgment            = SpecialAccept',
      'SpecialAcceptReason = "Lô gấp giao trong ngày,',
      '                       ΔE 2.3 chấp nhận được"',
      'IpqcSubmittedBy     = ipqc-test-checkpoint',
      'QaOutcome           = Approve',
      'QaApprovedBy        = qa-test-checkpoint',
    ], 4600),
    new Paragraph({ text: '', spacing: { after: 100 } }),
    ev('Cơ chế tách vai hoạt động đúng (`WO_QA_APPROVE_DENIED` emit khi vi phạm — `IpqcReviewController.cs:506`). Nhưng hồ sơ thiếu bốn thứ:'),
    evb('Số lượng bị ảnh hưởng bởi nhượng bộ'),
    evb('Mã lỗi có cấu trúc (ΔE 2.3 nằm trong văn bản tự do)'),
    evb('Khách hàng đã được thông báo hay chưa — **8.7.1(c)**'),
    evb('Phạm vi & hiệu lực của nhượng bộ (một lô? một đơn? tới ngày nào?)'),
  ],
  [
    ev('Điều 8.7.2 liệt kê rành mạch bốn thứ phải lưu: mô tả sự không phù hợp, hành động đã thực hiện, nhượng bộ đã nhận được, và người quyết định. Hệ hiện có "người quyết định" và một câu văn tự do; ba yếu tố còn lại không truy vấn được, không thống kê được.'),
    ev('Với khách dược theo ISO 15378, việc thả hàng lệch spec mà không có bằng chứng đã thông báo khách là điểm bị bắt nặng — có thể dẫn tới yêu cầu thu hồi.'),
  ]));

add(FINDING('M5', 'AQL khai báo đủ 59/59 hạng mục nhưng không dòng mã nào thi hành', 'alarm', '8.6 · ISO 2859-1',
  [
    ev('Thư viện lấp đầy 100%: 59/59 hạng mục có `Aql` và `Sampling` (ví dụ `"FAI 100% + AQL 0.65"`), 59/59 có `Severity` ba bậc — **20 Critical · 35 Major · 4 Minor**.'),
    ev('Nhưng tìm `Aql` trong mã: chỉ trúng `DbSeeder.cs:539` (nạp dữ liệu) và các tệp migration. **Không có bộ tính cỡ mẫu, không có số chấp nhận Ac/Re, không có cột lưu cỡ mẫu thực tế.**'),
    ev('Chú thích trong `FqcReadinessRollup.cs` tự thừa nhận: "Pass STILL allowed… operator may flag minor NGs without failing the lot" — nhưng không có ngưỡng nào giới hạn số NG đó.'),
    ev('**Lỗi dữ liệu kèm theo:** giá trị lưu là `"0,65"` và `"1,5"` — dấu phẩy thập phân. Không parse được bằng invariant culture; phải chuẩn hoá trước khi dùng.'),
  ],
  [
    ev('Người kiểm có thể đánh NG 10 hạng mục Critical và vẫn bấm Pass cho cả lô, chỉ cần gõ một dòng lý do. **Không có gì trong hệ ngăn việc đó.**'),
    ev('Đây là kiểu finding khiến khách hàng mất niềm tin nhanh nhất: kế hoạch lấy mẫu được in ra trên phiếu nhưng không ràng buộc quyết định. Về hình thức là có kiểm soát; về thực chất là không.'),
  ]));

add(FINDING('M6', 'Không có sổ thiết bị đo và lịch hiệu chuẩn', 'alarm', '7.1.5.1 · 7.1.5.2',
  [
    ev('Tìm "calibrat" / "hiệu chuẩn" trong toàn bộ `*.cs` và `*.razor`: **0 kết quả**.'),
    ev('Thực thể `Machine` chỉ có `Code`, `Name`, `Type`, `CurrentState`, `IdealCycleTimeSec` — hoàn toàn là thiết bị sản xuất, không có khái niệm thiết bị đo.'),
    ev('Thư viện hạng mục có cột `Method` mô tả "phương pháp · dụng cụ kiểm" dạng văn bản, nhưng không nối tới một thiết bị cụ thể nào.'),
  ],
  [
    ev('Điều 7.1.5.2 yêu cầu: thiết bị đo phải được hiệu chuẩn hoặc kiểm định theo chuẩn có liên kết, được nhận biết trạng thái, và **khi phát hiện thiết bị không phù hợp thì phải xác định giá trị của các kết quả đo trước đó**.'),
    ev('Vế cuối là vế đắt nhất. Nếu quang phổ kế lệch chuẩn, CCL phải trả lời được "những lô nào đã được đo bằng thiết bị này kể từ lần hiệu chuẩn đạt gần nhất". Hôm nay hệ không trả lời được, vì không có liên kết phép-đo ↔ thiết bị nào cả.'),
  ]));

// ── 06 · Phát hiện nhẹ ────────────────────────────────────────────────
add(H1('06', 'Phát hiện — không phù hợp NHẸ', true));
add(TABLE(
  ['Mã', 'Phát hiện', 'Điều', 'Bằng chứng cốt lõi'],
  [
    ['m7', '**Kế hoạch kiểm theo spec bị bỏ hoang.** SpecQcWindow có sẵn cỡ mẫu, tần suất, QcRejectAction, 5 kiểu tiêu chí. QC thực tế chạy theo dòng SP, không theo yêu cầu khách hàng của mã hàng.', '8.1 · 8.5.1(c)', 'SpecQcWindows 0 · QcCriteria 0 · SpecQcCaptures 0'],
    ['m8', '**Thư viện không phân biệt công đoạn.** 59/59 hạng mục tick đồng thời IPQC + FQC + OQC → cùng một bộ item materialize cho cả ba cổng. Và 0/59 có phạm vi theo mã hàng.', '8.1', 'GROUP BY Ipqc,Fqc,Oqc → một nhóm (1,1,1), n=59'],
    ['m9', '**Không có hồ sơ năng lực người kiểm.** Quyền ký chỉ dựa chuỗi Role; không có chứng nhận, ngày hiệu lực, giới hạn phạm vi (ai được ký OQC cho khách dược?).', '7.2 · 8.5.1(e)', 'User: chỉ Role + Department'],
    ['m10', '**Không đánh giá nhà cung cấp.** SupplierName là chuỗi tự do trên phiếu IQC và lô. Không có thực thể nhà cung cấp, không có điểm, không có tái đánh giá định kỳ.', '8.4.1', 'grep "class Supplier" → 0 kết quả'],
    ['m11', '**Chữ ký điện tử không xác thực lại tại thời điểm ký.** Duyệt OQC chỉ dựa JWT + policy QcEdit; trong khi xoá một bản vẽ lại bắt nhập username + mật khẩu. Hành động rủi ro cao hơn đang được bảo vệ yếu hơn.', '7.5.3 · 21 CFR 11', 'WoQcReviewController.cs:528 vs DrawingsApiController.cs:479'],
    ['m12', '**Phế ghi số lượng nhưng không ghi nguyên nhân.** ProductionLog.RejectQty là int trần, không có khoá ngoại tới mã lỗi. 53 mã ReasonCode tồn tại nhưng phế không gắn được vào mã nào.', '9.1.3 · 10.3', 'ProductionLog có DowntimeReasonId, KHÔNG có ScrapReasonId'],
    ['m13', '**Chưa có hồ sơ chất lượng xuất một nút.** Snapshot đã đóng băng đủ (17 dòng, 4 phase) nhưng chưa gộp thành một PDF cho audit khách hàng. Hiện phải ghép tay.', '8.6 · 7.5.3', 'Endpoint summary-report có, chưa thành gói hồ sơ'],
    ['m14', '**Không có khiếu nại khách hàng / hàng trả về.** Không có đường vào cho NC nguồn ngoại; vòng cải tiến vì thế chỉ nhìn được lỗi nội bộ.', '9.1.2 · 10.2', 'grep "complaint | khiếu nại" → 0 kết quả'],
    ['m15', '**Ảnh bằng chứng không bắt buộc khi NG.** Hạ tầng ảnh đã đủ (SHA-256, giới hạn 5 MiB, kiểm MIME) nhưng chưa có luật nào bắt phải có ảnh khi đánh NG.', '8.7.2', 'WoQcPhotos 0 dòng / 83 hạng mục đã kết luận'],
  ],
  [700, 4400, 1500, 3038],
  { monos: [true, false, true, true] }
));

// ── 07 · Điểm mạnh ────────────────────────────────────────────────────
add(H1('07', 'Điểm mạnh phải giữ', true));
add(P('Bảy điểm dưới đây là tài sản. Mọi phương án cải tiến phải **không** làm hỏng chúng — đặc biệt là tính bất biến của bằng chứng.'));
add(TABLE(
  ['Cơ chế', 'Vì sao đó là điểm mạnh theo ISO', 'Điều'],
  [
    ['**Snapshot đóng băng append-only** — WoTraceSnapshot có hash SHA-256, version tăng dần, không bao giờ upsert', 'Bằng chứng không thể bị sửa lùi. Sửa master data hôm nay không đổi hồ sơ của lô đã xuất tháng trước — đúng nguyên tắc "bảo vệ khỏi sửa đổi ngoài ý muốn".', '7.5.3'],
    ['**Đóng băng bộ hạng mục vào phiếu** — ProfileSnapshotJson / ItemsProfileSnapshotJson', 'Người kiểm thấy đúng bộ tiêu chí họ đã cam kết kiểm; đánh giá viên thấy đúng tiêu chí đã áp dụng tại thời điểm đó, không phải tiêu chí hôm nay.', '8.6 · 8.5.6'],
    ['**Ba chữ ký OQC tách vai** — OqcSignaturePolicy, hàm thuần, so khớp không phân biệt hoa thường', 'Một người không thể tự mình đẩy lô hàng ra khỏi nhà máy. Luật viết thành hàm thuần nên kiểm được bằng unit test — kiểm soát chứng minh được, không phải kiểm soát tuyên bố.', '8.6'],
    ['**Vết audit chụp vai trò tại thời điểm** — AuditLog.ActorRole, whitelist làm sạch, append-only', 'Đổi vai trò người dùng về sau không viết lại lịch sử. Đây là chi tiết mà nhiều hệ tự xây làm sai.', '7.5.3'],
    ['**Vòng đời lô NVL có cách ly** — Quarantine → Released / Rejected / Expired, hai chữ ký khi gia hạn hạn dùng', 'Cách ly vật lý được mô hình hoá bằng trạng thái, và quyết định rủi ro cao hơn (gia hạn lô quá hạn) đòi hai vai khác nhau — đúng tinh thần 8.7.1(b).', '8.5.2 · 8.7.1'],
    ['**Kiểm soát bản sửa đổi sản phẩm** — 5 trạng thái, ngày hiệu lực, con trỏ phả hệ, tóm tắt thay đổi', 'Kiểm soát tài liệu đúng chuẩn, hiếm khi thấy làm chỉn chu ở MES tự xây. 340 dòng dữ liệu thật cho thấy nó đang được dùng.', '7.5.2 · 8.5.6'],
    ['**Cổng CI 8 lớp + ratchet** — gate-all.sh: audit-emit, thin-controller, i18n parity, design token…', 'Kỷ luật kỹ thuật này chính là thứ khiến các cải tiến bên dưới khả thi. Cơ chế chặn tái phát đã có sẵn để gắn luật chất lượng mới vào.', '4.4'],
  ],
  [3100, 5238, 1300],
  { monos: [false, false, true] }
));

// ── 08 · Phương án ────────────────────────────────────────────────────
add(H1('08', 'Ba phương án cải tiến', true));

add(H3('PA-1 · Mua ngoài — dùng module QMS của ERP'));
add(P('Để CMES làm đúng phần MES; NC / CAPA / hiệu chuẩn / khiếu nại chuyển hết sang module QMS của IFS hoặc một phần mềm QMS thương mại.'));
add(BUL('**Được:** có ngay quy trình đã được audit nhiều nơi; không tốn công dựng; nhà cung cấp chịu trách nhiệm cập nhật theo chuẩn.'));
add(BUL('**Mất:** người kiểm phải nhập hai lần; **đứt liên kết bằng chứng** — NC nằm ở hệ này, snapshot đóng băng nằm ở hệ kia, ghép tay khi audit; chi phí license định kỳ; và tích hợp ngược lại chính là hạng mục C3 chưa làm.'));

add(H3('PA-2 · Làm hết trong CMES'));
add(P('Thêm toàn bộ NC / disposition / CAPA / SPC / hiệu chuẩn / năng lực / nhà cung cấp / khiếu nại / đánh giá nội bộ vào CMES.'));
add(BUL('**Được:** một nguồn sự thật; NC gắn thẳng vào lô, WO, snapshot, ảnh — hồ sơ audit tự khép kín.'));
add(BUL('**Mất:** phạm vi phình rất nhanh; đánh giá nội bộ và xem xét lãnh đạo là quy trình tổ chức, ép vào phần mềm sản xuất sẽ tạo ra một module không ai dùng; kéo dài lộ trình và đẩy rủi ro sang mốc go-live.'));

add(H3('PA-3 · Lai — CMES giữ bằng chứng cấp lô, QMS doanh nghiệp giữ quy trình'));
add(P('Ranh giới đặt ở **đơn vị bằng chứng**: cái gì gắn với một lô / một WO / một phép đo thì ở CMES; cái gì là quy trình cấp tổ chức thì ở ngoài, nối bằng mã tham chiếu hai chiều.'));
add(BUL('**Trong CMES:** giá trị đo · so ngưỡng bằng máy · engine AQL · hồ sơ NC · disposition · nhượng bộ đầy đủ · sổ thiết bị đo · SPC theo mã lỗi · gói hồ sơ chất lượng một nút.'));
add(BUL('**Ngoài CMES:** CAPA cấp hệ thống (8D) · đánh giá nội bộ · xem xét lãnh đạo · khiếu nại khách hàng · đánh giá nhà cung cấp — CMES chỉ giữ mã tham chiếu và dữ liệu đầu vào cho chúng.'));

add(H3('Chấm điểm'));
const sc = (n, best) => [String(n), { align: AlignmentType.CENTER, bold: best, color: best ? C.okInk : C.ink2 }];
add(TABLE(
  ['Tiêu chí', 'PA-1 Mua ngoài', 'PA-2 Làm hết', 'PA-3 Lai'],
  [
    ['Giữ được tính bất biến của bằng chứng', sc(2), sc(5, true), sc(5, true)],
    ['Đóng được finding nặng trước go-live', sc(2), sc(2), sc(4, true)],
    ['Chi phí & công sức', sc(2), sc(1), sc(4, true)],
    ['Rủi ro với mốc go-live', sc(3), sc(1), sc(4, true)],
    ['Gánh nặng vận hành cho người kiểm', sc(1), sc(4), sc(4)],
    [['TỔNG', { bold: true, color: C.ink }], ['10', { align: AlignmentType.CENTER, bold: true, color: C.ink }], ['13', { align: AlignmentType.CENTER, bold: true, color: C.ink }], ['21', { align: AlignmentType.CENTER, bold: true, color: C.okInk, size: 22 }]],
  ],
  [4238, 1800, 1800, 1800],
  { aligns: [null, AlignmentType.CENTER, AlignmentType.CENTER, AlignmentType.CENTER] }
));
add(SPACER(80));
add(P('Thang 1–5, cao là tốt.', { size: 17, color: C.inkMut }));
add(SPACER(180));
add(CALLOUT('Khuyến nghị — chọn PA-3', [
  'Lý do quyết định không phải điểm số mà là một nguyên tắc: **bằng chứng phải nằm cùng chỗ với dữ liệu sinh ra nó.** Giá trị đo, mã lỗi, ảnh, chữ ký và snapshot đóng băng phải ở chung một giao dịch — tách ra hai hệ là tự tạo ra khe hở mà đánh giá viên sẽ tìm đúng vào đó.',
  '**Cái mất phải chấp nhận:** CAPA cấp hệ thống, đánh giá nội bộ và xem xét lãnh đạo **không** vào CMES ở giai đoạn này. CMES chỉ cung cấp số liệu đầu vào cho chúng. Cần nói rõ ranh giới này trong sổ tay chất lượng, nếu không đánh giá viên sẽ đi tìm chúng trong CMES và ghi là thiếu.',
], 'ok'));

// ── 09 · Kế hoạch ─────────────────────────────────────────────────────
add(H1('09', 'Kế hoạch triển khai 5 đợt', true));
add(P('Thứ tự các đợt **là** thứ tự phụ thuộc kỹ thuật, không phải thứ tự ưu tiên kinh doanh: không có giá trị đo (Đợt 1) thì không có SPC (Đợt 4); không có hồ sơ NC (Đợt 2) thì không có CAPA (Đợt 4). Mỗi đợt gắn sẵn work-class · agent · skill theo vòng lặp 6 pha của dự án.'));

function PHASE(tag, dur, title, intro, items, accept, routing) {
  const out = [
    new Paragraph({
      heading: HeadingLevel.HEADING_2,
      spacing: { before: 320, after: 40 },
      border: { top: { style: BorderStyle.SINGLE, size: 8, color: C.line } },
      children: [
        new TextRun({ text: tag + '  ', font: MONO, size: 22, bold: true, color: C.accent }),
        new TextRun({ text: title, font: SANS, size: 24, bold: true, color: C.ink }),
      ],
    }),
    new Paragraph({
      spacing: { after: 130 },
      children: [
        new TextRun({ text: dur, font: SANS, size: 18, color: C.inkMut }),
        new TextRun({ text: '     ' + routing, font: MONO, size: 16, color: C.accentInk }),
      ],
    }),
  ];
  if (intro) out.push(P(intro));
  out.push(...items.map((t) => CHK(t)));
  if (accept) {
    out.push(SPACER(60));
    out.push(new Paragraph({
      children: runs('**Nghiệm thu:** ' + accept, { font: SANS, size: 20, color: C.ink2 }),
      spacing: { after: 120, line: 268 },
      shading: { type: ShadingType.CLEAR, fill: C.accentTint, color: 'auto' },
      indent: { left: 160, right: 160 },
      border: {
        top: { style: BorderStyle.SINGLE, size: 2, color: C.accentTint },
        bottom: { style: BorderStyle.SINGLE, size: 2, color: C.accentTint },
      },
    }));
  }
  return out;
}

add(PHASE('ĐỢT 0', '1–2 tuần · làm ngay, trước bàn giao', 'Dọn điểm gây hiểu nhầm',
  'Không thêm năng lực, chỉ loại bỏ những thứ khiến hệ trông như có kiểm soát mà thực ra không có. Rẻ, nhanh, giảm rủi ro audit ngay lập tức.',
  [
    'Gỡ màn hình **iCRA** khỏi menu, hoặc gắn nhãn "chưa triển khai" rõ ràng — **M3**',
    'Chuẩn hoá dữ liệu AQL `"0,65"` → `0.65` dạng số — **M5**',
    'Phân tầng lại cờ công đoạn trong thư viện: hạng mục nào thật sự thuộc IPQC / FQC / OQC — **m8**',
    'Bắt buộc ảnh khi đánh NG (hạ tầng đã có, chỉ thêm luật) — **m15**',
    'Xác minh vì sao 27/27 lô còn Quarantine và 23/25 phiếu IQC còn Pending',
  ], null, 'W4 · W5   ·   mes-quality-architect   ·   skill cmes-audit-emit'));

add(PHASE('ĐỢT 1', '4–6 tuần', 'Bằng chứng đo được',
  'Đóng M1 và M2 — hai finding gốc mà bốn finding khác phụ thuộc vào.',
  [
    'Thêm vào `WoIpqcCheckItem` và `WoQcCheckItem`: `MeasuredValue` (double?), `Uom`, `LowerLimit` / `UpperLimit` / `Target` — **ba giới hạn phải được đóng băng vào dòng** lúc materialize, cùng cơ chế với `ProfileSnapshotJson`, để sửa ngưỡng về sau không hồi tố lô đã kiểm',
    'Nối `QcThresholdResolver` vào đường ghi thật: **server** so sánh và quyết định Ok/NG, không để người kiểm tự quyết. **Cấm ghi `Status = Ok` khi giá trị đo nằm ngoài giới hạn**',
    'Thêm `CheckType` vào hợp đồng: hạng mục `Measure` bắt buộc có số; hạng mục `Visual` giữ nguyên Ok/NG',
    'Migration additive thuần — WO đang chạy không bị ảnh hưởng',
  ],
  'mở một WO thật, nhập ΔE = 2.4 vào hạng mục có ngưỡng 2.0 → hệ tự đặt NG, từ chối Pass, và dán được output thật của lệnh kiểm chứng.',
  'W1 · W4   ·   mes-quality-architect + cmes-implementer   ·   skill cmes-migration-abc'));

add(PHASE('ĐỢT 2', '6–8 tuần', 'Đóng vòng không phù hợp',
  'Đóng M3 và M4. Đây là hạng mục C1 đã nằm sẵn trong IMPROVEMENT-BACKLOG.md của dự án.',
  [
    '`NonConformance` — nguồn phát sinh (IQC / IPQC / FQC / OQC / khiếu nại), `DefectCode` (khoá đã sẵn trong thư viện v5), số lượng ảnh hưởng, mức nghiêm trọng, lô / WO / leg liên quan, người phát hiện, thời điểm',
    '`Disposition` — `{ Rework, Scrap, UseAsIs, Return, Regrade }`, người quyết định, lý do, số lượng theo từng hướng xử lý. Enum `QcRejectAction` đã có sẵn trong domain làm điểm khởi đầu',
    '**Nâng cấp nhượng bộ** — bổ sung 4 trường mà 8.7.2 đòi: số lượng, mã lỗi có cấu trúc, cờ đã-thông-báo-khách + tham chiếu, phạm vi & hiệu lực',
    '**Cách ly bằng trạng thái** — WO / lô có NC mở không được advance cho tới khi có disposition. Luật đặt trong domain policy, không đặt trong controller',
    'Mọi disposition emit audit row; **không** ghi đè snapshot đã đóng băng — NC là dòng mới, không phải bản vá dòng cũ',
  ],
  'một lô NG đi trọn đường NC → disposition → đóng, và truy vấn được "tất cả NC mở theo mã lỗi trong tháng". Snapshot cũ không đổi một byte.',
  'W1 · W2 · W4   ·   mes-quality-architect + mes-process-architect   ·   skill cmes-state-contract'));

add(PHASE('ĐỢT 3', '5–6 tuần', 'Thi hành AQL & gói hồ sơ chất lượng',
  'Đóng M5 và m13. Hạng mục sau chính là C2 — mục duy nhất trong backlog bán được cho khách hàng.',
  [
    '**Engine lấy mẫu ISO 2859-1**: cỡ lô → chữ cái mã cỡ mẫu → cỡ mẫu → số chấp nhận Ac / số loại Re, theo từng bậc nghiêm trọng (Critical 0.65 · Major 1.5 · Minor 4.0 — **chốt lại với QA**)',
    'Lưu **cỡ mẫu thực tế** và **số khuyết tật đếm được theo bậc** vào phiếu — hôm nay hai số này không tồn tại ở đâu cả',
    'Sửa `WoQcReadinessRollup`: Pass chỉ khi số khuyết tật ≤ Ac của bậc tương ứng. Vượt Ac → chỉ còn hai lối: Reject, hoặc nhượng bộ có hồ sơ đầy đủ theo Đợt 2',
    '**Quality Record Pack** — một WO → một PDF: spec revision đã dùng, snapshot routing, lô NVL đã quét, toàn bộ giá trị đo, chữ ký, ảnh, NC và disposition. Nội dung lấy **hoàn toàn** từ snapshot đóng băng, không JOIN dữ liệu sống (L29)',
  ],
  'lô 50.000 nhãn, AQL 1.5, cỡ mẫu do hệ tính → đếm 3 khuyết tật Major → hệ chặn Pass và nêu đúng Ac. Và: một WO → một PDF, không ghép tay.',
  'W4 · W5   ·   mes-quality-architect + cmes-shopfloor-ux   ·   skill cmes-spec-print'));

add(PHASE('ĐỢT 4', '6–8 tuần', 'Thiết bị đo, năng lực & cải tiến',
  'Đóng M6, m9, m12 và mở đường cho điều 9.1.3 / 10.3.',
  [
    '**Sổ thiết bị đo** — mã, loại, độ phân giải, chu kỳ hiệu chuẩn, ngày hiệu chuẩn gần nhất, ngày đến hạn, chứng chỉ. Mỗi giá trị đo ghi kèm `MeasuringDeviceId`. **Chặn ký khi thiết bị quá hạn**, và truy vấn ngược được "những lô nào đã đo bằng thiết bị này kể từ lần hiệu chuẩn đạt gần nhất" — đây là vế đắt nhất của 7.1.5.2',
    '**Hồ sơ năng lực** — chứng nhận theo công đoạn và theo khách hàng, có ngày hiệu lực; hết hạn thì mất quyền ký, không phải mất quyền đăng nhập',
    '**Nguyên nhân phế** — thêm `ScrapReasonId` vào `ProductionLog`, nối 53 mã `ReasonCode` đã có',
    '**SPC** — biểu đồ p / u theo `DefectCode` × dòng sản phẩm × thời gian; biểu đồ X̄-R cho các hạng mục đo được (ΔE, kích thước die-cut). Pareto lỗi theo tháng làm đầu vào cho xem xét lãnh đạo',
    '**Móc nối CAPA** — CMES **không** chứa 8D; nó mở NC, gắn mã CAPA từ hệ QMS doanh nghiệp, và cung cấp số liệu hiệu lực (lỗi cùng mã có tái diễn sau ngày đóng CAPA không)',
  ],
  'ký OQC bị chặn khi quang phổ kế quá hạn hiệu chuẩn; biểu đồ Pareto lỗi 30 ngày dựng được từ dữ liệu thật, không phải mock.',
  'W1 · W4 · W6   ·   mes-quality-architect   ·   skill cmes-rbac-matrix'));

add(SPACER(220));
add(CALLOUT('Về mốc go-live 30/07/2026', [
  'Mốc này đã qua so với ngày lập báo cáo. Nếu hệ đã go-live, thứ tự trên vẫn giữ nguyên nhưng **Đợt 0 trở thành việc phải làm ngay** — đặc biệt là gỡ màn hình iCRA giả và xác minh 27 lô còn Quarantine.',
  'Nếu chưa go-live, cân nhắc đưa Đợt 0 và phần chặn-ghi-Ok-khi-ngoài-ngưỡng của Đợt 1 vào trước khi mở cho vận hành thật: **sửa hợp đồng dữ liệu sau khi có hàng nghìn phiếu đắt hơn nhiều lần.**',
], 'warn'));

// ── 10 · KPI ──────────────────────────────────────────────────────────
add(H1('10', 'KPI & tiêu chí nghiệm thu', true));
add(P('Nguyên tắc lấy từ chính kỷ luật của dự án: **không có output thật thì chưa xong.** Mỗi chỉ số dưới đây phải đo được bằng một câu truy vấn, không bằng cảm nhận.'));
add(TABLE(
  ['Chỉ số', 'Hôm nay', 'Sau Đợt 3', 'Cách đo'],
  [
    ['Tỷ lệ hạng mục kiểm loại Measure có giá trị đo', '0%', '100%', 'MeasuredValue IS NOT NULL trên CheckType=\'Measure\''],
    ['Quyết định Ok/NG do máy tính, không do người tự đặt', '0%', '100%', 'Audit detail có threshold_applied'],
    ['Phiếu QC có cỡ mẫu thực tế', '0%', '100%', 'SampleSizeActual IS NOT NULL'],
    ['Hạng mục NG có ảnh bằng chứng', 'n/a', '100%', 'Status=\'Ng\' AND PhotoBlobId IS NULL → phải bằng 0'],
    ['NC có disposition trong 24h', '—', '≥ 95%', 'Chênh lệch ClosedAt − OpenedAt'],
    ['Nhượng bộ có đủ 4 trường theo 8.7.2', '0%', '100%', 'Ràng buộc CHECK ở schema, không chỉ ở UI'],
    ['Thời gian dựng hồ sơ audit cho 1 WO', 'ghép tay', '< 30 giây', 'Một nút → một PDF'],
    ['Giá trị đo gắn thiết bị còn hạn hiệu chuẩn', '0%', '100%', 'Sau Đợt 4 — join sổ thiết bị'],
  ],
  [3400, 1200, 1300, 3738],
  { monos: [false, false, false, true], aligns: [null, AlignmentType.RIGHT, AlignmentType.RIGHT, null] }
));

// ── 11 · Rủi ro ───────────────────────────────────────────────────────
add(H1('11', 'Rủi ro & STOP-gate'));
add(H3('Rủi ro triển khai'));
add(BUL('**Gánh nặng nhập liệu ở xưởng.** Bắt nhập số đo cho 34 hạng mục × 3 công đoạn là cách chắc chắn để người kiểm bịa số. Giảm thiểu: làm m8 (phân tầng công đoạn) **trước** Đợt 1, và chỉ bắt buộc số đo ở hạng mục `CheckType=\'Measure\'` — theo dữ liệu hiện tại phần lớn hạng mục là Visual.'));
add(BUL('**Sửa hợp đồng dữ liệu sau khi có dữ liệu thật.** Càng để lâu càng đắt. Ba giới hạn phải đóng băng vào dòng ngay từ Đợt 1, không thêm sau.'));
add(BUL('**Số AQL sai bậc.** Bậc AQL là quyết định **thương mại**, không phải quyết định kỹ thuật — sai bậc thì hoặc chặn oan hàng tốt, hoặc thả hàng lỗi. Phải có chữ ký của QA trước khi code Đợt 3.'));
add(BUL('**Phạm vi phình sang QMS doanh nghiệp.** Ranh giới PA-3 phải được ghi vào sổ tay chất lượng, không chỉ ghi trong báo cáo này.'));

add(H3('STOP-gate — dừng và hỏi Henry trước khi làm'));
add(BUL('Bất kỳ thay đổi nào khiến `WoTraceSnapshot` bị ghi đè hoặc cập nhật tại chỗ — **đây là lằn ranh không được vượt**'));
add(BUL('Thêm trạng thái WO mới cho luồng NC/disposition → phải sửa `P10.7-WO-STATE-CONTRACT.md` và có chữ ký trước, rồi mới code'));
add(BUL('Chạy migration lên DB live'));
add(BUL('Chốt bậc AQL mà chưa có xác nhận của QA CCL'));
add(BUL('Đụng vào `src/CCL.MES.*` khi baseline còn ở chế độ chỉ-đọc'));

// ── Phụ lục A ─────────────────────────────────────────────────────────
add(H1('PL-A', 'Phụ lục — bằng chứng SQL', true));
add(P('Chạy lại trên bản sao chỉ-đọc của `data/ccl_mes.db`. Toàn bộ số liệu trong báo cáo tái tạo được từ đây.'));
add(CODE([
  '-- M1 · không có cột giá trị đo',
  'PRAGMA table_info(WoIpqcCheckItems);',
  'PRAGMA table_info(WoQcCheckItems);',
  '',
  '-- m7 · kế hoạch kiểm theo spec bị bỏ hoang',
  'SELECT (SELECT COUNT(*) FROM SpecQcWindows)  AS windows,',
  '       (SELECT COUNT(*) FROM QcCriteria)     AS criteria,',
  '       (SELECT COUNT(*) FROM SpecQcCaptures) AS captures;',
  '-- → 0 | 0 | 0',
  '',
  '-- m8 · thư viện không phân biệt công đoạn',
  'SELECT Ipqc, Fqc, Oqc, COUNT(*) n FROM CheckItemLibraries',
  'GROUP BY Ipqc, Fqc, Oqc;',
  '-- → 1 | 1 | 1 | 59   (một nhóm duy nhất)',
  '',
  '-- M5 · AQL lấp đầy 100% nhưng lưu dạng chuỗi dấu phẩy',
  'SELECT ItemId, Severity, Aql, Sampling, CheckType',
  'FROM CheckItemLibraries LIMIT 8;',
  '-- → "0,65" · "FAI 100% + AQL 0.65" · Visual',
  '',
  '-- M4 · hồ sơ nhượng bộ thực tế',
  'SELECT WorkOrderId, SpecialAcceptReason, IpqcSubmittedBy,',
  '       QaOutcome, QaReason, QaApprovedBy',
  'FROM WoIpqcChecks WHERE Judgment = \'SpecialAccept\';',
  '',
  '-- m15 · ảnh bằng chứng',
  'SELECT COUNT(*) FROM WoQcPhotos;          -- → 0',
  'SELECT Status, COUNT(*) FROM WoQcCheckItems GROUP BY Status;',
  '',
  '-- Lô NVL còn cách ly',
  'SELECT Status, COUNT(*) FROM MaterialLots GROUP BY Status;',
  '-- → Quarantine | 27',
  '',
  '-- Phiếu IQC chưa kết luận',
  'SELECT Result, COUNT(*) FROM IqcInspections GROUP BY Result;',
  '-- → Pending 23 | Pass 1 | Fail 1',
]));
add(SPACER(200));
add(P('Kiểm chứng **M2** bằng tìm kiếm mã nguồn:'));
add(CODE([
  'grep -rn "QcThresholdResolver" --include="*.cs" . \\',
  '  | grep -v "/obj/" | grep -v "/bin/"',
  '',
  '# 13 lời gọi — tất cả trong QcThresholdResolverTests.cs',
  '# 1 tham chiếu <see cref> trong chú thích XML',
  '# 1 định nghĩa lớp',
  '# 0 lời gọi trong mã production',
]));

// ── Phụ lục B ─────────────────────────────────────────────────────────
add(H1('PL-B', 'Phụ lục — schema đề xuất', true));
add(P('Hình dạng đích, **không** phải mã cuối cùng. Thiết kế chi tiết là việc của `mes-quality-architect` ở pha ANALYZE của từng đợt.'));
add(CODE([
  '// ── Đợt 1 · bổ sung vào CẢ HAI bảng hạng mục ───────────────',
  'WoIpqcCheckItem / WoQcCheckItem',
  '  + MeasuredValue   double?',
  '  + Uom             string?',
  '  + LowerLimit      double?   // ĐÓNG BĂNG lúc materialize',
  '  + UpperLimit      double?   // KHÔNG đọc lại từ master data',
  '  + Target          double?',
  '  + CheckType       string    // Visual | Measure | Functional',
  '  + MeasuredBy      string?',
  '  + MeasuredAt      DateTime?',
  '',
  '// ── Đợt 2 · vòng không phù hợp ─────────────────────────────',
  'NonConformance',
  '  Source            // IQC | IPQC | FQC | OQC | COMPLAINT',
  '  DefectCode        // khoá đã sẵn trong thư viện v5',
  '  Severity          // Critical | Major | Minor',
  '  QtyAffected · Uom',
  '  WorkOrderId? · WoLegId? · MaterialLotId? · CheckItemId?',
  '  DetectedBy · DetectedAt · Description',
  '  Status            // Open | Dispositioned | Closed',
  '',
  'Disposition',
  '  NonConformanceId',
  '  Action            // Rework | Scrap | UseAsIs | Return | Regrade',
  '  QtyByAction · DecidedBy · DecidedAt · Reason',
  '  // khi Action = UseAsIs → BẮT BUỘC 4 trường 8.7.2:',
  '  CustomerNotified      bool',
  '  CustomerRef           string?',
  '  ConcessionScope       string     // lô | đơn hàng | tới ngày…',
  '  ConcessionValidUntil  DateTime?',
  '',
  '// ── Đợt 3 · lấy mẫu ────────────────────────────────────────',
  'WoQcCheck',
  '  + LotSize · SampleSizeActual',
  '  + InspectionLevel · AqlBySeverity',
  '  + DefectsFound_Critical / _Major / _Minor',
  '  + AcceptNumber · RejectNumber      // tính từ ISO 2859-1',
  '',
  '// ── Đợt 4 · thiết bị đo & năng lực ─────────────────────────',
  'MeasuringDevice',
  '  Code · Name · Type · Resolution · Uom',
  '  CalibrationIntervalDays',
  '  LastCalibratedAt · NextDueAt · CertificateRef',
  '  Status            // Active | OutOfCalibration | Retired',
  '',
  'InspectorQualification',
  '  UserId · Stage · ProcessLine · CustomerCode?',
  '  CertifiedAt · ValidUntil · CertifiedBy',
]));
add(SPACER(200));
add(CALLOUT('Một nguyên tắc cho toàn bộ schema trên', [
  'Mọi giới hạn, ngưỡng, bậc AQL và cỡ mẫu **phải được đóng băng vào dòng dữ liệu tại thời điểm tạo phiếu**, đúng cách mà `ProfileSnapshotJson` đang làm. Không đọc lại từ master data khi hiển thị hồ sơ cũ.',
  'Đây là thứ phân biệt một hệ chịu được audit với một hệ chỉ đẹp trên dashboard — và dự án đã làm đúng nó ở phần bộ hạng mục; phần ngưỡng chỉ cần đi theo cùng khuôn.',
]));

// —— Ghi chú cuối ——
add(SPACER(320));
add(new Paragraph({
  border: { top: { style: BorderStyle.SINGLE, size: 6, color: C.line } },
  spacing: { before: 200, after: 100 },
  children: [new TextRun({ text: '', size: 2 })],
}));
add(P('Các số liệu vận hành phản ánh trạng thái DB tại thời điểm truy vấn và sẽ thay đổi khi hệ chạy thật; các phát hiện về mã nguồn cần soát lại sau mỗi đợt triển khai. Bậc AQL, danh mục thiết bị đo và ranh giới với QMS doanh nghiệp cần xác nhận của QA CCL Design Vietnam trước khi đưa vào thực thi.', { size: 17, color: C.inkMut }));

// ═══════════════════════════════════════════════════════════════════════
const doc = new Document({
  creator: 'CCL Design Vietnam',
  title: 'Vòng chất lượng CCL-CMES — Đánh giá module QC theo ISO 9001:2015',
  description: 'Đánh giá module QC của CCL-CMES theo ISO 9001:2015',
  styles: {
    default: {
      document: { run: { font: SANS, size: 21, color: C.ink2 } },
      heading1: { run: { font: SANS, size: 30, bold: true, color: C.ink }, paragraph: { spacing: { before: 380, after: 180 } } },
      heading2: { run: { font: SANS, size: 24, bold: true, color: C.ink }, paragraph: { spacing: { before: 280, after: 130 } } },
      heading3: { run: { font: SANS, size: 22, bold: true, color: C.accentInk }, paragraph: { spacing: { before: 220, after: 110 } } },
    },
  },
  numbering: {
    config: [
      { reference: 'bullets', levels: [
        { level: 0, format: LevelFormat.BULLET, text: '•', alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 340, hanging: 220 } } } },
        { level: 1, format: LevelFormat.BULLET, text: '–', alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 680, hanging: 220 } } } },
      ]},
      { reference: 'checks', levels: [
        { level: 0, format: LevelFormat.BULLET, text: '□', alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 340, hanging: 240 } } } },
      ]},
      { reference: 'ordered', levels: [
        { level: 0, format: LevelFormat.DECIMAL, text: '%1.', alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 400, hanging: 280 } } } },
      ]},
    ],
  },
  features: { updateFields: true },
  sections: [{
    properties: {
      page: {
        size: { width: 11906, height: 16838 },
        margin: { top: 1418, bottom: 1418, left: 1134, right: 1134, header: 680, footer: 680 },
      },
      titlePage: true,
    },
    headers: {
      first: new Header({ children: [new Paragraph({ text: '' })] }),
      default: new Header({ children: [new Paragraph({
        alignment: AlignmentType.RIGHT,
        border: { bottom: { style: BorderStyle.SINGLE, size: 4, color: C.line } },
        spacing: { after: 120 },
        children: [new TextRun({ text: 'Vòng chất lượng CCL-CMES  ·  Đánh giá module QC theo ISO 9001:2015', font: SANS, size: 15, color: C.inkMut })],
      })] }),
    },
    footers: {
      first: new Footer({ children: [new Paragraph({ text: '' })] }),
      default: new Footer({ children: [new Paragraph({
        border: { top: { style: BorderStyle.SINGLE, size: 4, color: C.line } },
        spacing: { before: 120 },
        tabStops: [{ type: TabStopType.RIGHT, position: W }],
        children: [
          new TextRun({ text: 'CCL Design Vietnam · Hải Dương · 21/08/2026 · DRAFT', font: SANS, size: 15, color: C.inkMut }),
          new TextRun({ text: '\t', font: SANS, size: 15 }),
          new TextRun({ text: 'Trang ', font: SANS, size: 15, color: C.inkMut }),
          new TextRun({ children: [PageNumber.CURRENT], font: SANS, size: 15, color: C.ink, bold: true }),
          new TextRun({ text: ' / ', font: SANS, size: 15, color: C.inkMut }),
          new TextRun({ children: [PageNumber.TOTAL_PAGES], font: SANS, size: 15, color: C.inkMut }),
        ],
      })] }),
    },
    children: body,
  }],
});

const out = process.argv[2] || 'out.docx';
Packer.toBuffer(doc).then((buf) => {
  fs.writeFileSync(out, buf);
  console.log('✓ Đã ghi ' + out + ' (' + (buf.length / 1024).toFixed(0) + ' KB, ' + body.length + ' khối nội dung)');
});
