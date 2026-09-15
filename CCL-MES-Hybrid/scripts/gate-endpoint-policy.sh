#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# gate-endpoint-policy — mọi endpoint GHI phải TUYÊN BỐ ý đồ phân quyền.
# Hoặc `[Authorize(Policy=...)]`, hoặc một dòng `// RBAC-OPEN: <lý do>`.
# Im lặng không còn là một lựa chọn.
#
# LUẬT (skill cmes-rbac-matrix, luật vàng #1):
#   "Mọi endpoint mutation phải có [Authorize(Policy=...)] tường minh. Dựa vào
#    FallbackPolicy = RequireAuthenticatedUser nghĩa là 'ai đăng nhập cũng ghi
#    được' — sai gần như luôn."
#
#   "Gần như luôn" — nên có ngoại lệ thật: đăng nhập, tự đổi mật khẩu của chính
#   mình, heartbeat thiết bị, và các chỗ đã gác ở tầng service. Ngoại lệ được
#   phép, NHƯNG phải NÓI RA. Cái nguy hiểm không phải endpoint mở — mà là
#   endpoint mở mà không ai biết nó mở.
#
# SỰ CỐ 2026-09-14 (A4 → A5):
#   Rà tay khi tách vai kỹ sư thì thấy `QcSpecController.UpsertStage` +
#   `CreateCapture` — soạn KẾ HOẠCH QC, tức tiêu chí nghiệm thu sau đó lái
#   ngưỡng IPQC xuống hồ sơ WO — chỉ có `[Authorize]` trần ⇒ Operator cũng ghi
#   được. Quét tiếp: 34 endpoint ghi cùng cảnh. Không ai phân biệt được "cố ý
#   mở" với "quên gác", vì cả hai trông giống hệt nhau trong mã.
#
# CÁCH ĐO (cố ý thô mà chắc):
#   endpoint ghi = [HttpPost|Put|Delete|Patch]. Đạt khi:
#     · có Authorize(Policy=...) trong 4 dòng quanh nó, HOẶC
#     · có Authorize(Policy=...) ở cấp class, HOẶC
#     · có `// RBAC-OPEN:` trong 4 dòng ngay trên
#
# Ratchet: baseline chỉ được GIẢM.
#
# Ba trạng thái, cố ý không phải hai:
#   exit 0  mọi endpoint đã tuyên bố (hoặc ≤ baseline)
#   exit 1  số endpoint im lặng VƯỢT baseline
#   exit 2  không kết luận được
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# 2026-09-15: sau đợt A5 — 18 endpoint gắn ShopFloorWrite, 14 ghi RBAC-OPEN,
# 2 gắn QcPlanWrite. Không còn cái nào im lặng.
BASELINE=0

scan() {
  python3 - "$1" <<'PY'
import re, glob, os, sys
root = sys.argv[1]
d = os.path.join(root, "CCL-MES-Hybrid/src/CCL.MES.Api/Controllers")
if not os.path.isdir(d):
    print("NODIR"); sys.exit(0)
silent = []
for f in sorted(glob.glob(d + "/*.cs")):
    src = open(f).read().splitlines()
    cls = False
    for i, l in enumerate(src):
        if re.match(r'\s*public\s+(sealed\s+)?class\s+\w*Controller', l):
            j = i - 1
            while j >= 0 and (src[j].strip().startswith('[') or src[j].strip() == ''):
                if 'Authorize(Policy' in src[j]: cls = True
                j -= 1
            break
    if cls:
        continue
    for i, l in enumerate(src):
        if not re.search(r'\[Http(Post|Put|Delete|Patch)', l):
            continue
        win = "\n".join(src[max(0, i - 4):i + 5])
        if 'Authorize(Policy' in win or 'RBAC-OPEN' in win:
            continue
        m = re.search(r'public [^\n]*?(\w+)\(', "\n".join(src[i:i + 8]))
        silent.append(f"{os.path.basename(f)}:{i+1}  {m.group(1) if m else '?'}")
print(f"COUNT {len(silent)}")
for s in silent:
    print("  ✗ " + s)
PY
}

# ── self-test ───────────────────────────────────────────────────────────────
if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  d="$tmp/CCL-MES-Hybrid/src/CCL.MES.Api/Controllers"; mkdir -p "$d"
  fail=0

  # ❶ endpoint ghi im lặng ⇒ phải đếm được
  cat > "$d/XController.cs" <<'EOF'
public sealed class XController : ControllerBase
{
    [HttpPost("a")]
    public async Task<IActionResult> ImLang() => Ok();
}
EOF
  [ "$(scan "$tmp" | grep '^COUNT' | cut -d' ' -f2)" = "1" ] \
    || { echo "[gate:endpoint-policy] self-test FAILED — không đếm được endpoint im lặng"; fail=1; }

  # ❷ có RBAC-OPEN ⇒ đạt
  cat > "$d/XController.cs" <<'EOF'
public sealed class XController : ControllerBase
{
    [HttpPost("a")]
    // RBAC-OPEN: cố ý mở vì lý do này.
    public async Task<IActionResult> CoLyDo() => Ok();
}
EOF
  [ "$(scan "$tmp" | grep '^COUNT' | cut -d' ' -f2)" = "0" ] \
    || { echo "[gate:endpoint-policy] self-test FAILED — báo oan endpoint đã ghi lý do"; fail=1; }

  # ❸ có policy ⇒ đạt
  cat > "$d/XController.cs" <<'EOF'
public sealed class XController : ControllerBase
{
    [HttpPost("a"), Authorize(Policy = "Something")]
    public async Task<IActionResult> CoPolicy() => Ok();
}
EOF
  [ "$(scan "$tmp" | grep '^COUNT' | cut -d' ' -f2)" = "0" ] \
    || { echo "[gate:endpoint-policy] self-test FAILED — báo oan endpoint có policy"; fail=1; }

  [ "$fail" -eq 0 ] && echo "[gate:endpoint-policy] self-test OK — bắt endpoint im lặng, không kêu oan hai ca đã tuyên bố."
  exit "$fail"
fi

# ── chạy thật ───────────────────────────────────────────────────────────────
command -v python3 >/dev/null 2>&1 || { echo "[gate:endpoint-policy:FAIL] không có python3."; exit 2; }

out="$(scan "$ROOT")"
[ "$out" = "NODIR" ] && { echo "[gate:endpoint-policy:FAIL] không thấy thư mục Controllers."; exit 2; }

n="$(echo "$out" | grep '^COUNT' | cut -d' ' -f2)"
[ -n "$n" ] || { echo "[gate:endpoint-policy:FAIL] không đọc được kết quả quét."; exit 2; }

echo "$out" | grep -v '^COUNT'
echo "[gate:endpoint-policy] endpoint ghi chưa tuyên bố phân quyền: $n (baseline $BASELINE)"

if [ "$n" -gt "$BASELINE" ]; then
  echo "[gate:endpoint-policy:FAIL] có endpoint GHI không nói gì về phân quyền."
  echo "  Chọn một trong hai, đừng để im lặng:"
  echo "    · gắn [Authorize(Policy = \"...\")] — xem ma trận trong Program.cs"
  echo "    · hoặc ghi // RBAC-OPEN: <lý do cố ý mở> ngay trên method"
  echo "  Endpoint mở không nguy hiểm bằng endpoint mở mà không ai biết nó mở."
  exit 1
fi

echo "[gate:endpoint-policy:OK] mọi endpoint ghi đều đã tuyên bố ý đồ phân quyền."
exit 0
