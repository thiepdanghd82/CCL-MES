#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# L42 gate — chuỗi hiển thị phải qua TranslationCatalog, đủ VI + EN.
#
# Kiểm 3 thứ:
#   (A) KEY TRÙNG — HARD FAIL (0). Dictionary.Add trùng key → THROW lúc khởi
#       tạo catalog → app chết ngay khi mở. Bắt tĩnh ở CI rẻ hơn nhiều.
#   (B) CHUỖI RỖNG — HARD FAIL (0). Add(key,"","...") = thiếu ngôn ngữ.
#   (C) CHUỖI TIẾNG VIỆT NẰM TRẦN TRONG .razor — RATCHET. Đây là chuỗi bỏ quên
#       không qua catalog; EN sẽ thấy tiếng Việt. Baseline = nợ hiện có.
#
# Xem skill cmes-i18n-parity. i18n là thuế của mọi task chạm UI.
#
# Tested: PASS trên cây hiện tại; FAIL khi inject key trùng (--self-test).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

BASELINE_RAW_VI=99    # dòng .razor còn chuỗi tiếng Việt trần, đo 2026-08-18

here="$(cd "$(dirname "$0")" && pwd)"
ROOT="$here/.."
LOC="$ROOT/src/CCL.MES.Hybrid.Client/Localization"
RAZOR="$ROOT/src/CCL.MES.Hybrid.Razor"
[ -d "$LOC" ] || { echo "[gate:i18n] không thấy $LOC"; exit 2; }

scan() {
  python3 - "$1" "$2" <<'PY'
import re,sys,glob,os,json
loc,razor = sys.argv[1],sys.argv[2]
keys={}; empty=[]
for f in glob.glob(os.path.join(loc,'TranslationCatalog*.cs')):
    for m in re.finditer(r'Add\(\s*"([^"]+)"\s*,\s*"([^"]*)"\s*,\s*"([^"]*)"', open(f,encoding='utf-8').read()):
        k,vi,en=m.groups()
        keys.setdefault(k,[]).append(os.path.basename(f))
        if not vi.strip() or not en.strip(): empty.append(k)
dups={k:v for k,v in keys.items() if len(v)>1}
VI=re.compile(r'[ăâđêôơưĂÂĐÊÔƠƯáàảãạấầẩẫậắằẳẵặéèẻẽẹếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵ]')
raw=0
for f in glob.glob(os.path.join(razor,'**','*.razor'),recursive=True):
    for ln in open(f,encoding='utf-8'):
        s=ln.strip()
        if s.startswith(('@*','//','/*','*')): continue
        if VI.search(ln): raw+=1
print(json.dumps({"keys":len(keys),"dups":list(dups)[:5],"ndups":len(dups),"empty":empty[:5],"nempty":len(empty),"raw":raw}))
PY
}

if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  mkdir -p "$tmp/loc" "$tmp/razor"; cp "$LOC"/TranslationCatalog*.cs "$tmp/loc"/
  k="$(grep -ho 'Add("[^"]*"' "$LOC/TranslationCatalog.Nav.cs" | head -1 | sed 's/Add("//;s/"//')"
  printf '\npublic sealed partial class TranslationCatalog { void SelfTest() { Add("%s", "x", "y"); } }\n' "$k" >> "$tmp/loc/TranslationCatalog.Nav.cs"
  n="$(scan "$tmp/loc" "$tmp/razor" | python3 -c 'import json,sys;print(json.load(sys.stdin)["ndups"])')"
  [ "$n" -gt 0 ] \
    && { echo "[gate:i18n] self-test OK (key trùng '$k' bị bắt: ndups=$n)"; exit 0; } \
    || { echo "[gate:i18n] self-test FAILED — không bắt được key trùng"; exit 1; }
fi

out="$(scan "$LOC" "$RAZOR")"
eval "$(python3 -c "
import json,sys
d=json.loads('''$out''')
print(f'KEYS={d[\"keys\"]}; NDUPS={d[\"ndups\"]}; NEMPTY={d[\"nempty\"]}; RAW={d[\"raw\"]}')
print('DUPS=\"'+','.join(d['dups'])+'\"'); print('EMPTIES=\"'+','.join(d['empty'])+'\"')
")"

echo "[gate:i18n] key trong catalog        = $KEYS"
echo "[gate:i18n] key trùng                = $NDUPS (bắt buộc 0)"
echo "[gate:i18n] key thiếu VI hoặc EN     = $NEMPTY (bắt buộc 0)"
echo "[gate:i18n] dòng .razor còn VI trần  = $RAW (baseline $BASELINE_RAW_VI)"

rc=0
if [ "$NDUPS" -gt 0 ]; then
  echo "[gate:i18n:FAIL] key trùng: $DUPS"
  echo "  Dictionary.Add trùng key → throw lúc khởi tạo catalog → app chết khi mở."
  rc=1
fi
if [ "$NEMPTY" -gt 0 ]; then
  echo "[gate:i18n:FAIL] key thiếu ngôn ngữ: $EMPTIES"
  echo "  Mỗi Add(key, vi, en) phải đủ cả hai, cùng commit."
  rc=1
fi
if [ "$RAW" -gt "$BASELINE_RAW_VI" ]; then
  echo "[gate:i18n:FAIL] có chuỗi tiếng Việt mới nằm trần trong .razor."
  echo "  Đưa vào TranslationCatalog (kể cả aria-label / title / placeholder)."
  rc=1
fi
[ $rc -eq 0 ] && echo "[gate:i18n:OK] catalog lành, không có chuỗi cứng mới."
exit $rc
