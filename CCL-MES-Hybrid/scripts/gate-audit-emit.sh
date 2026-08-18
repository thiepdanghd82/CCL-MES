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
#   (C) CONFLICT IM LẶNG (L45) — mỗi `catch (DbUpdateConcurrencyException)` phải
#       emit audit, trực tiếp trong khối HOẶC qua handler nó gọi. Đây là nhánh hai
#       operator tranh nhau ghi cùng một WO giữa ca — đúng kịch bản mà
#       P10.7-WO-STATE-CONTRACT sinh ra để bảo vệ. Đo được TRƯỚC khi vá: wire luôn
#       đúng 9 × 409 (tất định) nhưng audit chỉ 3–9 dòng, chờ 3s không hội tụ ⇒ MẤT.
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
conflicts=[]
for f in glob.glob(os.path.join(api,'Controllers','*.cs')):
    t=open(f,encoding='utf-8').read()
    if 'SaveChangesAsync' in t and not re.search(r'EmitAsync|IAuditWriter|AuditEmitHelper|_audit',t):
        silent.append(os.path.basename(f))
    lines=t.splitlines()
    for i,ln in enumerate(lines):
        if 'catch (DbUpdateConcurrencyException' not in ln:
            continue
        blk=chr(10).join(lines[i:i+45])
        ok='EmitAsync' in blk
        m=re.search(r'(\w*HandleConcurrency\w*)\s*\(', blk)
        if not ok and m:
            hm=re.search(r'private async Task<IActionResult> '+m.group(1)+r'\(.*?'+chr(10)+r'    \}', t, re.S)
            ok=bool(hm) and 'EmitAsync' in hm.group(0)
        if not ok:
            conflicts.append(os.path.basename(f)+':'+str(i+1))
print(json.dumps({"nleak":len(leaks),"leaks":leaks[:5],"nsilent":len(silent),"silent":silent[:5],
                  "nconflict":len(conflicts),"conflicts":conflicts[:6]}))
PY
}

if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  mkdir -p "$tmp/Controllers"; cp "$API"/Controllers/*.cs "$tmp/Controllers"/
  printf '\n// selftest-leak\nvoid Z(){ _audit.EmitAsync(action:"X", detail: JsonSerializer.Serialize(new{ passwordHash = h })); }\n' >> "$tmp/Controllers/HealthController.cs"
  printf 'class SilentCtl { void W(){ _db.SaveChangesAsync(); } }\n' > "$tmp/Controllers/SelfTestSilentController.cs"
  printf 'class QuietConflictCtl { void W(){ try { X(); } catch (DbUpdateConcurrencyException) { return Conflict(); } } }\n' > "$tmp/Controllers/SelfTestQuietConflictController.cs"
  r="$(scan "$tmp")"
  nl="$(python3 -c "import json,sys;print(json.loads('''$r''')['nleak'])")"
  ns="$(python3 -c "import json,sys;print(json.loads('''$r''')['nsilent'])")"
  nc="$(python3 -c "import json,sys;print(json.loads('''$r''')['nconflict'])")"
  if [ "$nl" -gt 0 ] && [ "$ns" -gt 0 ] && [ "$nc" -gt 0 ]; then
    echo "[gate:audit] self-test OK (rò bí mật=$nl, mutation im lặng=$ns, conflict im lặng=$nc — bắt đủ 3)"; exit 0
  fi
  echo "[gate:audit] self-test FAILED — leak=$nl silent=$ns conflict=$nc (cần cả ba >0)"; exit 1
fi

out="$(scan "$API")"
eval "$(python3 -c "
import json
d=json.loads('''$out''')
print(f'NLEAK={d[\"nleak\"]}; NSILENT={d[\"nsilent\"]}; NCONFLICT={d[\"nconflict\"]}')
print('LEAKS=\"'+' | '.join(d['leaks'])+'\"'); print('SILENT=\"'+','.join(d['silent'])+'\"')
print('CONFLICTS=\"'+','.join(d['conflicts'])+'\"')
")"

echo "[gate:audit] detail chứa từ khoá bí mật   = $NLEAK (bắt buộc 0)"
echo "[gate:audit] controller ghi DB không audit = $NSILENT (bắt buộc 0)"
echo "[gate:audit] catch concurrency không audit  = $NCONFLICT (bắt buộc 0)"

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
if [ "$NCONFLICT" -gt 0 ]; then
  echo "[gate:audit:FAIL] catch DbUpdateConcurrencyException trả 409 mà không để lại vết: $CONFLICTS"
  echo "  Emit AuditAction.WoStateConflict SAU ChangeTracker.Clear() (L45)."
  echo "  Đây là nhánh hai operator tranh nhau ghi cùng WO — mất vết ở đây là mất đúng thứ cần truy."
  rc=1
fi
[ $rc -eq 0 ] && echo "[gate:audit:OK] mọi mutation + mọi conflict đều có vết, detail sạch."
exit $rc
