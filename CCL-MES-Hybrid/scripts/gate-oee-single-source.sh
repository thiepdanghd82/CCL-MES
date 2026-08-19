#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Đợt 1 C3 gate — OEE có ĐÚNG MỘT nguồn tốc độ, và null luôn kèm lý do.
#
# Kiểm 2 thứ, CẢ HAI hard-fail ở 0 (không phải ratchet — không có nợ hợp lệ nào
# ở đây, cây hiện tại đang sạch và phải giữ nguyên vậy):
#
#   (A) HAI NGUỒN TỐC ĐỘ — `IdealCycleTimeSec` không được xuất hiện trong bất kỳ
#       đường tính OEE nào của cây Hybrid. Nguồn chuẩn duy nhất là
#       `WorkCenter.IdealSpeedPcsH`, ideal cycle được DẪN XUẤT: 3600/speed
#       (ShopOrdersController.cs:154). Trước Đợt 1, WoQcReviewController đọc
#       `Machine.IdealCycleTimeSec` — cùng một WO cho hai con số hiệu suất khác
#       nhau tuỳ endpoint nào được hỏi.
#
#   (B) NULL IM LẶNG — mỗi đường trả `performance` phải trả kèm lý do khi null.
#       Đây là bug thật, không phải chuyện thẩm mỹ: chỉ 5/43 work center có
#       `IdealSpeedPcsH > 0`, nên 19/27 WO mất chỉ số mà không ai biết, vì null
#       vừa có nghĩa "không áp dụng" vừa có nghĩa "chúng tôi không biết".
#       B1 — record `WoSummaryOee` phải khai báo `PerformanceUnavailableReason`.
#       B2 — mọi initializer `new WoSummaryOee { … }` phải gán trường đó.
#       B3 — file nào dựng `WoSummaryOee` phải đi qua `OeePerformance.Compute`,
#            hàm duy nhất bảo đảm bất biến "đúng một trong hai non-null".
#
# PHẠM VI: chỉ cây `CCL-MES-Hybrid/`. `src/CCL.MES.*` là baseline READ-ONLY —
# `CCL.MES.Application/Services/OeeService.cs:119` vẫn dùng
# `machine.IdealCycleTimeSec` và chúng ta KHÔNG được sửa nó. Consumer Hybrid duy
# nhất của service đó (`OeeController`) đã bị xoá ở Đợt 1 C3, nên bề mặt API
# hiện hành không còn chạm nhánh ấy. Gate canh cây mình sửa được.
#
# Dòng chú thích được bỏ qua trước khi so khớp — một comment giải thích vì sao
# `IdealCycleTimeSec` bị cấm là tài liệu, không phải vi phạm.
#
# Tested: PASS trên cây hiện tại; FAIL khi inject (1) IdealCycleTimeSec vào
# controller, (2) xoá PerformanceUnavailableReason khỏi DTO, (3) initializer
# WoSummaryOee thiếu reason — chạy với --self-test.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
ROOT="$here/.."
[ -d "$ROOT/src" ] || { echo "[gate:oee] không thấy $ROOT/src"; exit 2; }

scan() {
  python3 - "$1" <<'PY'
import re, sys, os, json

root = sys.argv[1]
SKIP_DIR = ('/bin/', '/obj/')
COMMENT_START = ('//', '/*', '*', '@*', '*/')

def code_lines(text):
    """Yield (lineno, line) with whole-line comments dropped. Conservative:
    only drops lines that are *entirely* comment, so a trailing comment on a
    real code line still counts as a hit (better a false alarm than a miss)."""
    for i, ln in enumerate(text.splitlines(), 1):
        s = ln.strip()
        if s.startswith(COMMENT_START):
            continue
        yield i, ln

def walk(root):
    for dirpath, _dirs, files in os.walk(root):
        p = dirpath.replace(os.sep, '/') + '/'
        if any(sd in p for sd in SKIP_DIR):
            continue
        for f in files:
            if f.endswith(('.cs', '.razor')):
                yield os.path.join(dirpath, f)

dual_source = []       # (A)
missing_field = []     # (B1)
init_no_reason = []    # (B2)
init_no_helper = []    # (B3)

dto_seen = False

for path in walk(root):
    rel = os.path.relpath(path, root)
    try:
        text = open(path, encoding='utf-8').read()
    except (UnicodeDecodeError, OSError):
        continue

    # ── (A) second speed source anywhere in an OEE path ──
    for lineno, ln in code_lines(text):
        if 'IdealCycleTimeSec' in ln:
            dual_source.append(f'{rel}:{lineno}')

    stripped = '\n'.join(ln for _n, ln in code_lines(text))

    # ── (B1) the DTO itself must carry the reason field ──
    m = re.search(r'record\s+WoSummaryOee\b(.*?)\n\}', stripped, re.S)
    if m:
        dto_seen = True
        body = m.group(1)
        if 'Performance' in body and 'PerformanceUnavailableReason' not in body:
            missing_field.append(rel)

    # ── (B2) every construction site sets it ──
    for m in re.finditer(r'new\s+WoSummaryOee\s*\{(.*?)\}', stripped, re.S):
        lineno = stripped[:m.start()].count('\n') + 1
        if 'PerformanceUnavailableReason' not in m.group(1):
            init_no_reason.append(f'{rel}:{lineno}')
        # ── (B3) and reaches it through the one function that guarantees it ──
        if 'OeePerformance.Compute' not in stripped:
            init_no_helper.append(f'{rel}:{lineno}')

print(json.dumps({
    "dto_seen": dto_seen,
    "ndual": len(dual_source),      "dual": dual_source[:6],
    "nfield": len(missing_field),   "field": missing_field[:6],
    "ninit": len(init_no_reason),   "init": init_no_reason[:6],
    "nhelper": len(init_no_helper), "helper": init_no_helper[:6],
}))
PY
}

# ── self-test: inject each violation into a throwaway copy ───────────────────
if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  mkdir -p "$tmp/src/Dto" "$tmp/src/Controllers"

  # A clean baseline the injections then break, one at a time.
  cat > "$tmp/src/Dto/WoQcReviewDtos.cs" <<'EOF'
public sealed record WoSummaryOee
{
    public double? Availability { get; init; }
    public double? Performance { get; init; }
    public string? PerformanceUnavailableReason { get; init; }
}
EOF
  cat > "$tmp/src/Controllers/CleanController.cs" <<'EOF'
class CleanController {
    void M() {
        var perf = OeePerformance.Compute(true, 600, 3600, 300);
        var dto = new WoSummaryOee { Performance = perf.Performance, PerformanceUnavailableReason = perf.UnavailableReason };
    }
}
EOF
  base="$(scan "$tmp")"
  b_dual="$(python3 -c "import json;print(json.loads('''$base''')['ndual'])")"
  b_field="$(python3 -c "import json;print(json.loads('''$base''')['nfield'])")"
  b_init="$(python3 -c "import json;print(json.loads('''$base''')['ninit'])")"
  b_helper="$(python3 -c "import json;print(json.loads('''$base''')['nhelper'])")"

  # (A) reintroduce the second speed source
  printf 'class Dual { void M(){ var c = machine.IdealCycleTimeSec; } }\n' > "$tmp/src/Controllers/DualSourceController.cs"
  # (B2)+(B3) a construction site with neither reason nor helper
  printf 'class Silent { void M(){ var d = new WoSummaryOee { Performance = p, Quality = q }; } }\n' > "$tmp/src/Controllers/SilentNullController.cs"
  # (B1) strip the field off the DTO
  cat > "$tmp/src/Dto/WoQcReviewDtos.cs" <<'EOF'
public sealed record WoSummaryOee
{
    public double? Availability { get; init; }
    public double? Performance { get; init; }
}
EOF

  r="$(scan "$tmp")"
  nd="$(python3 -c "import json;print(json.loads('''$r''')['ndual'])")"
  nf="$(python3 -c "import json;print(json.loads('''$r''')['nfield'])")"
  ni="$(python3 -c "import json;print(json.loads('''$r''')['ninit'])")"
  nh="$(python3 -c "import json;print(json.loads('''$r''')['nhelper'])")"

  echo "[gate:oee] self-test baseline sạch: dual=$b_dual field=$b_field init=$b_init helper=$b_helper"
  echo "[gate:oee] self-test sau khi inject: dual=$nd field=$nf init=$ni helper=$nh"
  if [ "$b_dual" -eq 0 ] && [ "$b_field" -eq 0 ] && [ "$b_init" -eq 0 ] && [ "$b_helper" -eq 0 ] \
     && [ "$nd" -gt 0 ] && [ "$nf" -gt 0 ] && [ "$ni" -gt 0 ] && [ "$nh" -gt 0 ]; then
    echo "[gate:oee] self-test OK (bắt đủ 4: nguồn kép, DTO thiếu trường, initializer thiếu lý do, không qua helper)"
    exit 0
  fi
  echo "[gate:oee] self-test FAILED — detector không bắt đủ vi phạm."
  exit 1
fi

out="$(scan "$ROOT")"
eval "$(python3 -c "
import json
d = json.loads('''$out''')
print(f'DTO_SEEN={int(d[\"dto_seen\"])}; NDUAL={d[\"ndual\"]}; NFIELD={d[\"nfield\"]}; NINIT={d[\"ninit\"]}; NHELPER={d[\"nhelper\"]}')
print('DUAL=\"'   + ' | '.join(d['dual'])   + '\"')
print('FIELD=\"'  + ' | '.join(d['field'])  + '\"')
print('INIT=\"'   + ' | '.join(d['init'])   + '\"')
print('HELPER=\"' + ' | '.join(d['helper']) + '\"')
")"

echo "[gate:oee] IdealCycleTimeSec trong đường OEE      = $NDUAL (bắt buộc 0)"
echo "[gate:oee] DTO thiếu PerformanceUnavailableReason = $NFIELD (bắt buộc 0)"
echo "[gate:oee] initializer trả performance không lý do = $NINIT (bắt buộc 0)"
echo "[gate:oee] initializer không qua OeePerformance    = $NHELPER (bắt buộc 0)"

rc=0
if [ "$DTO_SEEN" -eq 0 ]; then
  echo "[gate:oee:FAIL] không tìm thấy record WoSummaryOee — gate đang canh vào chỗ trống."
  echo "  DTO đổi tên/đổi chỗ thì sửa gate cùng PR, đừng để nó xanh giả."
  rc=1
fi
if [ "$NDUAL" -gt 0 ]; then
  echo "[gate:oee:FAIL] nguồn tốc độ thứ hai quay lại: $DUAL"
  echo "  Nguồn chuẩn là WorkCenter.IdealSpeedPcsH; ideal cycle = 3600/speed."
  echo "  Machine.IdealCycleTimeSec cho ra con số khác cho cùng một WO."
  rc=1
fi
if [ "$NFIELD" -gt 0 ]; then
  echo "[gate:oee:FAIL] WoSummaryOee bỏ mất PerformanceUnavailableReason: $FIELD"
  rc=1
fi
if [ "$NINIT" -gt 0 ]; then
  echo "[gate:oee:FAIL] trả performance null im lặng: $INIT"
  echo "  Chỉ 5/43 work center có tốc độ — null im lặng giấu mất 19/27 WO."
  rc=1
fi
if [ "$NHELPER" -gt 0 ]; then
  echo "[gate:oee:FAIL] dựng WoSummaryOee không qua OeePerformance.Compute: $HELPER"
  echo "  Compute() là chỗ duy nhất bảo đảm đúng một trong (Performance, Reason) non-null."
  rc=1
fi
[ $rc -eq 0 ] && echo "[gate:oee:OK] một nguồn tốc độ, và mọi null đều có lý do."
exit $rc
