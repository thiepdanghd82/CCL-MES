#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Gate — KHÔNG file nào được mang dấu xung đột merge chưa giải quyết.
#
# VÌ SAO GATE NÀY TỒN TẠI:
#   2026-09-05, trong lúc merge 5 PR liên tiếp, một script python giải quyết
#   xung đột CSS chết vì lỗi cú pháp — nên KHÔNG ghi gì. Nhưng hai lệnh sau nó
#   (`git add` + `git commit`) vẫn chạy, và commit nguyên file app.css còn đủ ba
#   dòng dấu xung đột.
#
#   `dotnet build` XANH. Cả hai solution xanh. Vì CSS không được biên dịch, và
#   dấu xung đột trong .css chỉ là mấy selector rác mà trình duyệt bỏ qua.
#   Không một lưới an toàn nào của repo bắt được: 19 gate, 3.850 test, hai lần
#   build sạch — tất cả nói OK trên một file hỏng.
#
#   Cùng chuyện đó xảy ra được với .md, .json, .csv, .razor (Razor có thể vẫn
#   biên dịch nếu dấu rơi vào vùng markup), và với mọi file cấu hình.
#
# LUẬT: 0. Không ratchet, không ngoại lệ — một dấu xung đột sót lại KHÔNG BAO
# GIỜ là chủ ý.
#
# Cách nhận diện (thận trọng để không báo nhầm):
#   · dòng bắt đầu bằng 7 dấu `<` + khoảng trắng  → chắc chắn là dấu merge
#   · dòng bắt đầu bằng 7 dấu `>` + khoảng trắng  → chắc chắn là dấu merge
#   · dòng ĐÚNG BẰNG 7 dấu `=`                    → CHỈ tính khi file đó cũng
#     có một trong hai dấu trên; nếu không thì đó có thể là đường kẻ trang trí
#     trong markdown, và báo nhầm sẽ dạy người ta bỏ qua gate.
#
# Chỉ quét file GIT ĐANG THEO DÕI, bỏ qua nhị phân.
#
# Tested: PASS trên cây hiện tại; FAIL khi có file mang dấu — `--self-test`.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"

scan() {
  # $1 = thư mục gốc để quét
  python3 - "$1" <<'PY'
import subprocess, sys, os

root = sys.argv[1]
# Dựng chuỗi dấu bằng phép nhân để CHÍNH FILE GATE này không tự dính bẫy.
LT = "<" * 7
GT = ">" * 7
EQ = "=" * 7

try:
    files = subprocess.run(["git", "ls-files", "-z"], cwd=root, capture_output=True,
                           check=True).stdout.decode().split("\0")
except Exception as e:
    print(f"[gate:merge-markers] không liệt kê được file git: {e}", file=sys.stderr)
    sys.exit(2)

hits = []
for rel in files:
    if not rel:
        continue
    path = os.path.join(root, rel)
    if not os.path.isfile(path):
        continue
    try:
        with open(path, "rb") as fh:
            head = fh.read(8192)
        if b"\0" in head:          # nhị phân
            continue
        with open(path, encoding="utf-8", errors="replace") as fh:
            lines = fh.read().splitlines()
    except OSError:
        continue

    strong = [(i + 1, ln) for i, ln in enumerate(lines)
              if ln.startswith(LT + " ") or ln.startswith(GT + " ")]
    if not strong:
        continue
    weak = [(i + 1, ln) for i, ln in enumerate(lines) if ln.strip() == EQ]
    for n, ln in strong + weak:
        hits.append(f"{rel}:{n}: {ln[:60]}")

for h in hits:
    print(h)
print(f"__COUNT__{len(hits)}")
PY
}

if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  git -C "$tmp" init -q .
  printf 'a\n%s ours\nx\n%s\ny\n%s theirs\n' "$(printf '<%.0s' {1..7})" \
    "$(printf '=%.0s' {1..7})" "$(printf '>%.0s' {1..7})" > "$tmp/bad.css"
  printf '# tiêu đề\n%s\nvăn bản bình thường\n' "$(printf '=%.0s' {1..7})" > "$tmp/ok.md"
  git -C "$tmp" add -A >/dev/null
  out="$(scan "$tmp")"
  n="$(echo "$out" | sed -n 's/^__COUNT__//p')"
  if [ "$n" -eq 3 ] && ! echo "$out" | grep -q "ok.md"; then
    echo "[gate:merge-markers:SELF-TEST OK] bắt đúng 3 dấu ở bad.css, KHÔNG báo nhầm đường kẻ === trong ok.md"
    exit 0
  fi
  echo "[gate:merge-markers:SELF-TEST FAIL] mong 3 dấu chỉ ở bad.css, nhận:"; echo "$out"
  exit 1
fi

out="$(scan "$root")"
count="$(echo "$out" | sed -n 's/^__COUNT__//p')"
if [ "${count:-0}" -eq 0 ]; then
  echo "[gate:merge-markers:OK] không file nào mang dấu xung đột merge."
  exit 0
fi

echo "$out" | grep -v '^__COUNT__' | head -40
echo "[gate:merge-markers:FAIL] $count dòng mang dấu xung đột merge chưa giải quyết."
echo "  Luật là 0 — dấu sót lại KHÔNG BAO GIỜ là chủ ý."
echo "  Build có thể vẫn XANH: CSS/MD/JSON không được biên dịch."
exit 1
