#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# gate-error-code-localised — mã lỗi nào server phát ra thì màn hình phải có
# CÂU cho người đọc. Không mã nào được rơi vào nhánh mặc định
# "Unknown error code (…)".
#
# SỰ CỐ 2026-09-14:
#   Thiệp bấm "Cho chạy" và nhận: "Unknown error code
#   (ipqc.material_divergence_unresolved)". Luật nghiệp vụ chạy đúng — vật tư
#   lệch dữ liệu IQC thì phải có kỹ sư phê duyệt trước — nhưng người đứng máy
#   không có cách nào biết mình cần LÀM GÌ. Một mã lỗi hiện trần ra màn hình
#   là lỗi ngang với không có thông báo.
#
#   Quét toàn bộ: 4 mã nữa cũng đang câm (leg.not_found · leg.invalid_phase ·
#   setting.incomplete · leg.ipqc_incomplete) trên các màn khác.
#
# VÌ SAO GATE i18n CŨ KHÔNG BẮT ĐƯỢC:
#   `gate-i18n-parity` canh `TranslationCatalog` — chuỗi giao diện. Mã lỗi lại
#   nằm trong các lớp `*ErrorLocaliser`, một đường hoàn toàn khác, và không ai
#   canh. Đó là lý do lỗ hổng sống được nhiều tháng.
#
# CÁCH ĐO (cố ý thô mà chắc):
#   · mã = chuỗi `"<vùng>.<tên>"` xuất hiện trong controller/policy
#   · có câu = chuỗi ấy xuất hiện trong localiser tương ứng
#   Không cố phân tích cú pháp C#; chỉ cần mã có mặt ở cả hai phía.
#
# Ba trạng thái, cố ý không phải hai:
#   exit 0  mọi mã đều có câu
#   exit 1  có mã câm
#   exit 2  không kết luận được (thiếu python3 / thiếu file)
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

run() {  # $1 = thư mục gốc repo
  python3 - "$1" <<'PY'
import re, glob, sys, os
root = sys.argv[1]
api = os.path.join(root, "CCL-MES-Hybrid/src/CCL.MES.Api")
cli = os.path.join(root, "CCL-MES-Hybrid/src/CCL.MES.Hybrid.Client")

# (nguồn phát mã, localiser phải phủ)
PAIRS = [
    (["IpqcReviewController.cs", "WoIpqcMaterialController.cs", "IpqcSignaturePolicy.cs"],
     "IpqcReviewErrorLocaliser.cs"),
    (["PrepressController.cs"],       "PrepressErrorLocaliser.cs"),
    (["RunningSurfaceController.cs"], "RunningSurfaceErrorLocaliser.cs"),
    (["WoQcReviewController.cs"],     "WoQcReviewErrorLocaliser.cs"),
    (["RoutingController.cs"],        "RoutingErrorLocaliser.cs"),
]
CODE = re.compile(r'"((?:ipqc|wo|qa|material|leg|prepress|setting|fqc|oqc|run)\.[a-z_]+)"')

def find(base, name):
    hits = glob.glob(f"{base}/**/{name}", recursive=True)
    return hits[0] if hits else None

bad = tot = 0
for srcs, loc in PAIRS:
    lp = find(cli, loc)
    if lp is None:
        print(f"  ? {loc} — không thấy localiser, bỏ qua")
        continue
    ltext = open(lp).read()
    codes = set()
    for s in srcs:
        sp = find(api, s)
        if sp:
            codes |= set(CODE.findall(open(sp).read()))
    missing = sorted(c for c in codes if f'"{c}"' not in ltext)
    tot += len(codes); bad += len(missing)
    mark = "✓" if not missing else "✗"
    print(f"  {mark} {loc:<34} {len(codes)-len(missing)}/{len(codes)}")
    for m in missing:
        print(f"        câm: {m}")
print(f"TOTAL {tot-bad}/{tot}")
sys.exit(1 if bad else 0)
PY
}

# ── self-test: dựng một cặp giả thiếu câu, gate phải bắt ────────────────────
if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  a="$tmp/CCL-MES-Hybrid/src/CCL.MES.Api/Controllers"
  c="$tmp/CCL-MES-Hybrid/src/CCL.MES.Hybrid.Client/Prepress"
  mkdir -p "$a" "$c"
  printf 'return Invalid("prepress.dat_cam", "x");\nreturn Invalid("prepress.co_cau", "y");\n' \
    > "$a/PrepressController.cs"

  # ❶ localiser thiếu một mã ⇒ phải FAIL
  printf '"prepress.co_cau" => "có câu",\n' > "$c/PrepressErrorLocaliser.cs"
  if run "$tmp" >/dev/null 2>&1; then
    echo "[gate:error-code-localised] self-test FAILED — không bắt được mã câm"; exit 1
  fi

  # ❷ localiser đủ ⇒ phải PASS (chống kêu oan)
  printf '"prepress.co_cau" => "có câu",\n"prepress.dat_cam" => "cũng có câu",\n' \
    > "$c/PrepressErrorLocaliser.cs"
  if ! run "$tmp" >/dev/null 2>&1; then
    echo "[gate:error-code-localised] self-test FAILED — báo oan localiser đã đủ"; exit 1
  fi

  echo "[gate:error-code-localised] self-test OK — bắt mã câm, không kêu oan localiser đủ."
  exit 0
fi

# ── chạy thật ───────────────────────────────────────────────────────────────
command -v python3 >/dev/null 2>&1 || { echo "[gate:error-code-localised:FAIL] không có python3."; exit 2; }
[ -d "$ROOT/CCL-MES-Hybrid/src/CCL.MES.Api" ] || { echo "[gate:error-code-localised:FAIL] không thấy cây nguồn."; exit 2; }

out="$(run "$ROOT")"; rc=$?
echo "$out" | grep -v '^TOTAL'
line="$(echo "$out" | grep '^TOTAL')"

if [ "$rc" -ne 0 ]; then
  echo "[gate:error-code-localised:FAIL] ${line#TOTAL } mã có câu — số còn lại hiện trần mã lỗi ra màn hình."
  echo "  Người đứng máy đọc \"Unknown error code (ipqc.material_divergence_unresolved)\" thì"
  echo "  không biết phải làm gì — ngang với không có thông báo."
  echo "  Thêm câu vào *ErrorLocaliser tương ứng, CẢ HAI nhánh (LocaliseApiError + LocaliseSetError)."
  exit 1
fi

echo "[gate:error-code-localised:OK] ${line#TOTAL } mã lỗi đều có câu cho người đọc."
exit 0
