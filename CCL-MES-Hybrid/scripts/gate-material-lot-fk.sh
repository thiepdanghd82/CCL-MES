#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# A1 gate — truy xuất nguồn gốc phải đi bằng KHOÁ SỐ, không bằng chuỗi lô.
#
# Bài học sinh ra gate này (đo trên live 2026-08-19, TRƯỚC A1):
#   WoMaterials tổng 82 · có LotNo khác rỗng 5 · khớp RawMaterials.PartNo 0
#   · khớp IqcInspections.LotNumber 0
# Nghĩa là khoá tự nhiên chuỗi giữa hai bảng = KHÔNG có khoá. Chuỗi chỉ được
# tồn tại như NHÃN HIỂN THỊ; mọi phép nối phải qua FK.
#
# Kiểm 4 thứ:
#   (A) HARD FAIL 0 — nối theo chuỗi lô ở bất kỳ đâu (SQL hoặc LINQ).
#   (B) RATCHET     — ghi `.LotNo =` TRONG CONTROLLER HTTP. Baseline đo
#       2026-08-19 = 1 (PrepressController.cs:133 — đúng dòng hợp đồng §0 nêu
#       tên), hạ xuống 0 trong PR A1.
#       Vì sao chỉ đếm trong controller: §3.3 giữ LotNo là MIRROR mà "server ghi
#       từ MaterialLot.LotNo", nên vẫn PHẢI còn đúng một chỗ ghi ở tầng
#       Application. Đếm cả repo rồi đòi 0 là bất khả thi — sẽ đẻ ra mẹo lách
#       gate chứ không đẻ ra code tốt hơn. Ranh giới đúng: controller nhận chuỗi
#       thô từ operator, nên nó là nơi TUYỆT ĐỐI không được ghi thẳng.
#   (B2) HARD FAIL 0 — để (B) không thành trò đánh tráo chỗ: ở tầng Application,
#       file nào ghi `.LotNo =` thì file đó PHẢI cũng ghi `MaterialLotId`. Mirror
#       không neo vào FK chính là bệnh cũ, chỉ đổi địa chỉ.
#   (C) HARD FAIL 0 — entity lô có mặt mà model snapshot thiếu NOCASE trên
#       LotNo/PartNo/SupplierLotNo. L28 đã tái phạm một lần (SemiLots.LotNo);
#       gate này để không có lần thứ ba.
#
# Tested: PASS trên cây hiện tại; FAIL với cả 4 loại vi phạm (--self-test).
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

# Đo 2026-08-19: PrepressController.cs:133 `row.LotNo = req?.LotNo ?? row.LotNo;`
# A1 gỡ dòng đó ⇒ hạ baseline về 0 trong CÙNG PR (đúng luật ratchet).
BASELINE_CTRL_LOTNO_WRITE=0

here="$(cd "$(dirname "$0")" && pwd)"
HYBRID="$here/.."
ROOT="$(cd "$HYBRID/.." && pwd)"

CTRL="$HYBRID/src/CCL.MES.Api/Controllers"
APP="$ROOT/src/CCL.MES.Application"
SNAP="$ROOT/src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs"

scan() {
  python3 - "$1" "$2" "$3" "$4" <<'PY'
import re, sys, os, glob, json

root, ctrl, app, snap = sys.argv[1:5]

def files(base, exts):
    out = []
    for ext in exts:
        out += glob.glob(os.path.join(base, '**', '*' + ext), recursive=True)
    return [f for f in out if '/obj/' not in f and '/bin/' not in f]

# ── (A) nối theo chuỗi lô ────────────────────────────────────────────
# SQL:  ON a.LotNo = b.LotNo   ·   LINQ join: on x.LotNo equals y.LotNo
# LINQ ==: x.LotNo == y.LotNo
JOIN_SQL   = re.compile(r'ON\s+\S*LotNo\s*=\s*\S*LotNo', re.I)
JOIN_EQUALS= re.compile(r'\bon\s+\S*\.LotNo\s+equals\s+\S*\.LotNo')
JOIN_LINQ  = re.compile(r'\.LotNo\s*==\s*(\S+)\.LotNo')
# Tra cứu theo khoá tự nhiên KHÔNG phải là nối bảng: `lot.LotNo == req.LotNo`
# là đúng nghiệp vụ quét mã vạch — vế phải là DTO/tham số, không phải bảng thứ
# hai. Chỉ loại đúng các tên nhận-request quen thuộc; mọi thứ khác vẫn bị bắt.
REQUESTISH = re.compile(
    r'^(req|request|cmd|command|dto|body|input|payload|model|args?|filter|q)$', re.I)
joins = []
for f in files(root, ('.cs', '.razor', '.sql')):
    for i, ln in enumerate(open(f, encoding='utf-8', errors='ignore'), 1):
        s = ln.strip()
        if s.startswith('//') or s.startswith('--') or s.startswith('*') or s.startswith('///'):
            continue
        hit = JOIN_SQL.search(ln) or JOIN_EQUALS.search(ln)
        if not hit:
            m = JOIN_LINQ.search(ln)
            hit = bool(m) and not REQUESTISH.match(m.group(1).lstrip('(!'))
        if hit:
            joins.append(f'{os.path.relpath(f, root)}:{i}')

# ── (B) ghi .LotNo = trong controller ────────────────────────────────
WRITE = re.compile(r'\.LotNo\s*=(?!=)')
ctrl_writes = []
for f in glob.glob(os.path.join(ctrl, '*.cs')):
    for i, ln in enumerate(open(f, encoding='utf-8', errors='ignore'), 1):
        s = ln.strip()
        if s.startswith('//') or s.startswith('///'):
            continue
        if WRITE.search(ln):
            ctrl_writes.append(f'{os.path.basename(f)}:{i}')

# ── (B2) mirror ở Application phải neo vào FK ────────────────────────
unanchored = []
for f in files(app, ('.cs',)):
    txt = open(f, encoding='utf-8', errors='ignore').read()
    code = '\n'.join(l for l in txt.splitlines()
                     if not l.strip().startswith(('//', '///', '*')))
    if WRITE.search(code) and 'MaterialLotId' not in code:
        unanchored.append(os.path.relpath(f, root))

# ── (C) NOCASE trên khoá tự nhiên chuỗi của lô ───────────────────────
nocase_missing = []
snap_txt = open(snap, encoding='utf-8', errors='ignore').read() if os.path.exists(snap) else ''
if 'MaterialLot' in snap_txt:
    # cắt đúng khối entity MaterialLot trong snapshot
    m = re.search(r'\.Entity\("CCL\.MES\.Domain\.Entities\.MaterialLot".*?ToTable\("MaterialLots"',
                  snap_txt, re.S)
    block = m.group(0) if m else ''
    for col in ('LotNo', 'PartNo', 'SupplierLotNo'):
        pm = re.search(r'b\.Property<string>\("' + col + r'"\)(.*?);', block, re.S)
        if not pm or 'NOCASE' not in pm.group(1):
            nocase_missing.append('MaterialLots.' + col)
else:
    nocase_missing.append('(entity MaterialLot chưa có trong snapshot)')

print(json.dumps({
    "njoin": len(joins),        "joins": joins[:6],
    "nctrl": len(ctrl_writes),  "ctrl": ctrl_writes[:6],
    "nunanchored": len(unanchored), "unanchored": unanchored[:6],
    "nnocase": len(nocase_missing),  "nocase": nocase_missing[:6],
}))
PY
}

# ── self-test: cây tạm + inject đủ 4 loại vi phạm ────────────────────
if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  mkdir -p "$tmp/root/src/CCL.MES.Application" \
           "$tmp/root/src/CCL.MES.Infrastructure/Migrations" \
           "$tmp/ctrl"

  # (A) join theo chuỗi lô
  printf 'var q = "SELECT 1 FROM a JOIN b ON a.LotNo = b.LotNo";\n' \
    > "$tmp/root/src/CCL.MES.Application/SelfTestJoin.cs"
  # (B) controller ghi thẳng
  printf 'class C { void W(){ row.LotNo = req.LotNo; } }\n' > "$tmp/ctrl/SelfTestController.cs"
  # (B2) mirror không neo FK
  printf 'class S { void W(){ row.LotNo = lot.LotNo; } }\n' \
    > "$tmp/root/src/CCL.MES.Application/SelfTestUnanchored.cs"
  # (C) snapshot thiếu NOCASE
  cat > "$tmp/root/src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs" <<'EOF'
modelBuilder.Entity("CCL.MES.Domain.Entities.MaterialLot", b =>
    {
        b.Property<string>("LotNo")
            .HasMaxLength(64);
        b.ToTable("MaterialLots"
EOF

  out="$(scan "$tmp/root" "$tmp/ctrl" "$tmp/root/src/CCL.MES.Application" \
              "$tmp/root/src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs")"
  read -r nj nc nu nn <<<"$(python3 -c "
import json; d=json.loads('''$out''')
print(d['njoin'], d['nctrl'], d['nunanchored'], d['nnocase'])")"
  if [ "$nj" -gt 0 ] && [ "$nc" -gt 0 ] && [ "$nu" -gt 0 ] && [ "$nn" -gt 0 ]; then
    echo "[gate:matlot] self-test OK (join=$nj ctrl-write=$nc unanchored=$nu nocase-missing=$nn — bắt đủ 4)"
    exit 0
  fi
  echo "[gate:matlot] self-test FAILED — join=$nj ctrl=$nc unanchored=$nu nocase=$nn (cần cả bốn >0)"
  exit 1
fi

[ -d "$CTRL" ] || { echo "[gate:matlot] không thấy $CTRL"; exit 2; }

out="$(scan "$ROOT" "$CTRL" "$APP" "$SNAP")"
eval "$(python3 -c "
import json
d=json.loads('''$out''')
print(f'NJOIN={d[\"njoin\"]}; NCTRL={d[\"nctrl\"]}; NUNANCHORED={d[\"nunanchored\"]}; NNOCASE={d[\"nnocase\"]}')
print('JOINS=\"'+' | '.join(d['joins'])+'\"')
print('CTRLW=\"'+' | '.join(d['ctrl'])+'\"')
print('UNANCH=\"'+' | '.join(d['unanchored'])+'\"')
print('NOCASEM=\"'+' | '.join(d['nocase'])+'\"')
")"

echo "[gate:matlot] nối theo chuỗi lô (JOIN/==)   = $NJOIN (bắt buộc 0)"
echo "[gate:matlot] ghi .LotNo= trong controller  = $NCTRL (baseline $BASELINE_CTRL_LOTNO_WRITE)"
echo "[gate:matlot] mirror không neo MaterialLotId = $NUNANCHORED (bắt buộc 0)"
echo "[gate:matlot] cột khoá lô thiếu NOCASE      = $NNOCASE (bắt buộc 0)"

rc=0
if [ "$NJOIN" -gt 0 ]; then
  echo "[gate:matlot:FAIL] có phép nối theo CHUỖI lô: $JOINS"
  echo "  Khoá tự nhiên chuỗi giữa hai bảng = không có khoá. Nối qua MaterialLotId."
  rc=1
fi
if [ "$NCTRL" -gt "$BASELINE_CTRL_LOTNO_WRITE" ]; then
  echo "[gate:matlot:FAIL] controller ghi thẳng LotNo: $CTRLW"
  echo "  Đẩy qua MaterialLotScanService: chuẩn hoá → resolve về MaterialLot → set FK → mirror."
  rc=1
fi
if [ "$NUNANCHORED" -gt 0 ]; then
  echo "[gate:matlot:FAIL] mirror LotNo không neo vào FK: $UNANCH"
  echo "  Ghi LotNo mà không set MaterialLotId chỉ là chuyển chỗ bệnh cũ."
  rc=1
fi
if [ "$NNOCASE" -gt 0 ]; then
  echo "[gate:matlot:FAIL] thiếu COLLATE NOCASE: $NOCASEM"
  echo "  L28 đã tái phạm một lần (SemiLots.LotNo): 'LOT-001' và 'lot-001' thành HAI lô."
  echo "  Đặt UseCollation(\"NOCASE\") trên CỘT trong MesDbContext, đừng rải EF.Functions.Collate()."
  rc=1
fi
[ $rc -eq 0 ] && echo "[gate:matlot:OK] mạch lô đi bằng khoá số, khoá chuỗi có NOCASE."
exit $rc
