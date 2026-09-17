#!/usr/bin/env bash
#
# CI gate — TRẠNG THÁI PHẢI SỐNG LÂU HƠN PHIÊN LÀM VIỆC: không để ở /tmp.
#
# L65 dạy bài này một lần với BACKUP: P12 làm đúng Phase A nhưng đặt ba bản
# backup ở /tmp, vài ngày sau macOS dọn sạch — migration thì đã nằm trên DB
# thật, còn bằng chứng thì không còn.
#
# 17-09-2026 phát hiện bài đó tái xuất ở mặt khác: LOG sản xuất của API đặt tại
# `/tmp/ccl-api-5100.log`. Hệ chạy ba ca, và sự cố tiếp theo — đúng lúc máy vừa
# khởi động lại — sẽ không còn gì để điều tra. Gate này chặn cả hai mặt.
#
# PHÂN BIỆT (quan trọng, không thì gate vô dụng vì báo động giả):
#   /tmp cho thứ VỨT ĐI  → HỢP LỆ. `mktemp`, DB thiết kế của migration Phase B
#                          (`/tmp/*-design.db` — skill cmes-migration-abc BẮT
#                          BUỘC như vậy), file nháp của self-test.
#   /tmp cho thứ PHẢI GIỮ → ĐỎ. log · backup · snapshot · DB live.
#
# Nhận diện: dòng GÁN BIẾN tên LOG/BACKUP/BAK/SNAPSHOT/DB/DATA trỏ vào /tmp.
# Muốn miễn trừ một dòng: thêm chú thích `# ok-tmp: <lý do>` ngay trên dòng đó.
#
# HẠN CHẾ, nói thẳng: chỉ soi phép gán biến trong script vận hành. Một đường
# dẫn /tmp ghép thẳng vào lệnh sẽ lọt. Lưới thứ hai là review.
#
# Usage: bash scripts/gate-no-tmp-state.sh              (exit 0 = pass, 1 = fail)
#        bash scripts/gate-no-tmp-state.sh --self-test
set -uo pipefail

here="$(cd "$(dirname "$0")" && pwd)"

BASELINE=0

# MỘT biểu thức nhận diện, khai một lần để self-test chạy đúng detector thật.
RX='^[[:space:]]*(LOG|LOGFILE|LOG_DIR|BACKUP|BACKUP_DIR|BAK|SNAPSHOT|DB|DBFILE|DATA|DATA_DIR)[A-Z_]*=["'"'"']?/tmp/'

scan() {  # $1 = thư mục chứa script
  local d="$1"
  # Bỏ qua CHÍNH FILE GATE: fixture của self-test nằm trong heredoc của nó, và
  # gate tự soi mình thì báo động giả vĩnh viễn. (Phát hiện ngay lần chạy đầu.)
  grep -rInE "$RX" --include='*.sh' "$d" 2>/dev/null \
    | grep -v '/gate-no-tmp-state\.sh:' \
    | while IFS=: read -r f n line; do
    # miễn trừ: dòng NGAY TRÊN có `# ok-tmp:`
    prev="$(sed -n "$((n-1))p" "$f" 2>/dev/null)"
    case "$prev" in *"# ok-tmp:"*) continue;; esac
    echo "$f:$n:$(echo "$line" | sed 's/^[[:space:]]*//')"
  done
}

# ── self-test ─────────────────────────────────────────────────────────────────
if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT

  # (1) HỢP LỆ — phải im lặng cả ba
  cat > "$tmp/ok.sh" <<'EOS'
work="$(mktemp -d)"
MES_CONNSTR="Data Source=/tmp/fkcascade-design.db"
# ok-tmp: file nháp của self-test, vứt đi được
LOG=/tmp/selftest-scratch.log
EOS
  # (2) VI PHẠM — phải bắt
  cat > "$tmp/bad.sh" <<'EOS'
LOG="/tmp/ccl-api-5100.log"
EOS

  ok="$(scan "$tmp" | grep '/ok.sh:' || true)"
  bad="$(scan "$tmp" | grep '/bad.sh:' || true)"
  if [ -n "$ok" ]; then
    echo "[gate:no-tmp-state] self-test FAILED — báo động giả trên dòng HỢP LỆ:"
    printf '%s\n' "$ok" | sed 's/^/    /'
    exit 1
  fi
  if [ -z "$bad" ]; then
    echo "[gate:no-tmp-state] self-test FAILED — LOG=/tmp/... mà detector không bắt."
    exit 1
  fi
  echo "[gate:no-tmp-state] self-test OK — bắt đúng LOG=/tmp/…, và KHÔNG báo động giả trên"
  echo "                    mktemp, DB thiết kế của migration, hay dòng đã khai # ok-tmp."
  exit 0
fi

# ── quét thật ─────────────────────────────────────────────────────────────────
out="$(scan "$here")"
count="$(printf '%s' "$out" | grep -c . || true)"

if [ "$count" -gt "$BASELINE" ]; then
  echo "[gate:no-tmp-state] trạng thái phải giữ mà đặt ở /tmp: $count (baseline $BASELINE)"
  printf '%s\n' "$out" | sed 's/^/  /'
  echo
  echo "  → macOS dọn /tmp. Log/backup/DB đặt ở đó là mất đúng lúc cần nhất (L65)."
  echo "    Chuyển sang data/ (đã .gitignore), hoặc khai '# ok-tmp: <lý do>' ngay trên dòng đó."
  exit 1
fi

echo "[gate:no-tmp-state] trạng thái phải giữ mà đặt ở /tmp: $count (baseline $BASELINE)"
echo "[gate:no-tmp-state:OK] log · backup · DB đều nằm ngoài /tmp."
exit 0
