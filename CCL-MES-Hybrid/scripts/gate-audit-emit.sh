#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# L43 gate — mọi đường ghi dữ liệu phải để lại vết, và vết đó không được rò bí mật.
#
# Kiểm 2 thứ, CẢ HAI hard-fail ở 0 (cây hiện tại đang sạch — giữ nguyên vậy):
#   (A) RÒ BÍ MẬT — tham số `detail:` của audit không được chứa
#       password/hash/token/cookie/secret/apiKey/connectionString. AuditLogs
#       xuất được ra CSV + đọc được ở Settings → System Log, nên bất cứ thứ gì
#       vào detail coi như đã rời vùng bảo mật.
#   (B) MUTATION IM LẶNG — controller có SaveChangesAsync mà không hề tham chiếu
#       audit writer ⇒ ghi dữ liệu không để lại vết ⇒ sự cố không điều tra được.
#
# Xem skill cmes-audit-emit. Lưu ý: AuditLogs ≠ WoTraceSnapshot (vết vận hành
# vs bằng chứng chất lượng) — đừng gộp hai thứ.
#
# Tested: PASS trên cây hiện tại; FAIL khi inject detail chứa "password"
# và khi inject controller ghi DB không audit (--self-test).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
API="$here/../src/CCL.MES.Api"
[ -d "$API" ] || { echo "[gate:audit] không thấy $API"; exit 2; }

scan() {
  python3 - "$1" <<'PY'
import re,sys,glob,os,json
api=sys.argv[1]
BAD=re.compile(r'password|pwd|passwordhash|\bhash\b|\bsalt\b|token|cookie|authorization|bearer|secret|apikey|connectionstring',re.I)
leaks=[]
for f in glob.glob(os.path.join(api,'**','*.cs'),recursive=True):
    t=open(f,encoding='utf-8').read()
    for m in re.finditer(r'detail\s*:\s*([^\n]+)',t):
        if BAD.search(m.group(1)):
            leaks.append(os.path.basename(f)+': '+m.group(1).strip()[:60])
silent=[]
for f in glob.glob(os.path.join(api,'Controllers','*.cs')):
    t=open(f,encoding='utf-8').read()
    if 'SaveChangesAsync' in t and not re.search(r'EmitAsync|IAuditWriter|AuditEmitHelper|_audit',t):
        silent.append(os.path.basename(f))
print(json.dumps({"nleak":len(leaks),"leaks":leaks[:5],"nsilent":len(silent),"silent":silent[:5]}))
PY
}

if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  mkdir -p "$tmp/Controllers"; cp "$API"/Controllers/*.cs "$tmp/Controllers"/
  printf '\n// selftest-leak\nvoid Z(){ _audit.EmitAsync(action:"X", detail: JsonSerializer.Serialize(new{ passwordHash = h })); }\n' >> "$tmp/Controllers/HealthController.cs"
  printf 'class SilentCtl { void W(){ _db.SaveChangesAsync(); } }\n' > "$tmp/Controllers/SelfTestSilentController.cs"
  r="$(scan "$tmp")"
  nl="$(python3 -c "import json,sys;print(json.loads('''$r''')['nleak'])")"
  ns="$(python3 -c "import json,sys;print(json.loads('''$r''')['nsilent'])")"
  if [ "$nl" -gt 0 ] && [ "$ns" -gt 0 ]; then
    echo "[gate:audit] self-test OK (rò bí mật=$nl, mutation im lặng=$ns đều bị bắt)"; exit 0
  fi
  echo "[gate:audit] self-test FAILED — leak=$nl silent=$ns (cần cả hai >0)"; exit 1
fi

out="$(scan "$API")"
eval "$(python3 -c "
import json
d=json.loads('''$out''')
print(f'NLEAK={d[\"nleak\"]}; NSILENT={d[\"nsilent\"]}')
print('LEAKS=\"'+' | '.join(d['leaks'])+'\"'); print('SILENT=\"'+','.join(d['silent'])+'\"')
")"

echo "[gate:audit] detail chứa từ khoá bí mật   = $NLEAK (bắt buộc 0)"
echo "[gate:audit] controller ghi DB không audit = $NSILENT (bắt buộc 0)"

rc=0
if [ "$NLEAK" -gt 0 ]; then
  echo "[gate:audit:FAIL] audit detail rò bí mật:"; echo "  $LEAKS"
  echo "  Serialize field tường minh, đừng ném cả entity vào detail."
  rc=1
fi
if [ "$NSILENT" -gt 0 ]; then
  echo "[gate:audit:FAIL] ghi DB không để lại vết: $SILENT"
  echo "  Mọi mutation emit qua IAuditWriter.EmitAsync sau khi SaveChangesAsync thành công."
  rc=1
fi
[ $rc -eq 0 ] && echo "[gate:audit:OK] mọi mutation có vết, detail sạch."
exit $rc
