#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# L40 gate — luật nghiệp vụ KHÔNG sống trong controller HTTP.
#
# Đo hai con số trên src/CCL.MES.Api/Controllers/:
#   (1) số lần `SaveChangesAsync` gọi thẳng trong controller
#   (2) số controller dài hơn MAX_LOC dòng
#
# Cả hai là RATCHET ĐI XUỐNG: code mới không được làm tệ hơn baseline, và mỗi
# PR chạm vùng cũ nên kéo baseline xuống (rồi sửa BASELINE trong cùng PR).
#
# Vì sao: logic trong controller → không test được nếu không dựng
# WebApplicationFactory, không tái dùng được cho background job / ERP adapter,
# và hai endpoint "gần giống nhau" sẽ phân kỳ. Xem skill cmes-thin-controller.
#
# Tested: PASS trên cây hiện tại; FAIL khi thêm một SaveChangesAsync vào
# controller (chạy với --self-test).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

BASELINE_SAVE=18     # đo 2026-08-18 (22); A2 rút commit-save vào
                     # Services/WoMutationExecutor: RunningSurface+Prepress → 20,
                     # WoQcReview+Ipqc → 18 (2026-08-23)
BASELINE_FAT=8       # controller > MAX_LOC dòng
MAX_LOC=400

here="$(cd "$(dirname "$0")" && pwd)"
DIR="$here/../src/CCL.MES.Api/Controllers"
[ -d "$DIR" ] || { echo "[gate:thin] không thấy $DIR"; exit 2; }

count_save() { grep -ho "SaveChangesAsync" "$1"/*.cs 2>/dev/null | wc -l | tr -d ' '; }
count_fat()  { wc -l "$1"/*.cs 2>/dev/null | awk -v m="$MAX_LOC" '$1>m && $2!="total"' | wc -l | tr -d ' '; }

if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  cp "$DIR"/*.cs "$tmp"/
  before="$(count_save "$tmp")"
  printf '\n// selftest\nclass X { void Y() { _db.SaveChangesAsync(); } }\n' >> "$tmp/HealthController.cs"
  after="$(count_save "$tmp")"
  [ "$after" -gt "$before" ] \
    && { echo "[gate:thin] self-test OK (thêm SaveChangesAsync bị bắt: $before -> $after)"; exit 0; } \
    || { echo "[gate:thin] self-test FAILED — detector không bắt được"; exit 1; }
fi

save="$(count_save "$DIR")"
fat="$(count_fat "$DIR")"
echo "[gate:thin] SaveChangesAsync trong controller = $save (baseline $BASELINE_SAVE)"
echo "[gate:thin] controller > ${MAX_LOC} dòng          = $fat (baseline $BASELINE_FAT)"

rc=0
if [ "$save" -gt "$BASELINE_SAVE" ]; then
  echo "[gate:thin:FAIL] controller mới ghi DB trực tiếp."
  echo "  Đẩy transaction xuống Application service; controller chỉ bind/authorize/gọi/map lỗi."
  rc=1
fi
if [ "$fat" -gt "$BASELINE_FAT" ]; then
  echo "[gate:thin:FAIL] có thêm controller vượt ${MAX_LOC} dòng."
  echo "  Tách luật ra Domain policy (SignaturePolicy / QcGate / LegAdvancePolicy)."
  rc=1
fi
[ $rc -eq 0 ] && echo "[gate:thin:OK] tầng API không dày thêm."
exit $rc
