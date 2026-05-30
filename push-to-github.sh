#!/usr/bin/env bash
# =============================================================
#  Đẩy project CCL-MES lên GitHub
#  Cách dùng:
#    1. Tạo 1 repository RỖNG trên https://github.com/new
#       (KHÔNG tích "Add README", "Add .gitignore", "license")
#       Đặt tên ví dụ: CCL-MES
#    2. Mở Terminal, cd vào thư mục project này, rồi chạy:
#         bash push-to-github.sh https://github.com/<user>/CCL-MES.git
#       (thay <user> bằng username GitHub của bạn)
# =============================================================
set -e

REMOTE_URL="$1"
if [ -z "$REMOTE_URL" ]; then
  echo "❌ Thiếu URL repo. Ví dụ:"
  echo "   bash push-to-github.sh https://github.com/henry/CCL-MES.git"
  exit 1
fi

# Đảm bảo đang ở thư mục có .sln
if [ ! -f CCL.MES.sln ]; then
  echo "❌ Hãy chạy script này TRONG thư mục project (nơi có file CCL.MES.sln)."
  exit 1
fi

# Khởi tạo git nếu chưa có
if [ ! -d .git ]; then
  git init
  git branch -M main
fi

# Cấu hình tác giả (đổi nếu muốn)
git config user.name  "$(git config user.name  || echo 'Henry')"
git config user.email "$(git config user.email || echo 'thiepdangthe@gmail.com')"

git add -A
git commit -m "CCL-MES MVP: WO/Spec/QC/OEE/Work Instruction/Dashboard" || echo "(không có thay đổi mới để commit)"
git branch -M main

# Gắn remote (ghi đè nếu đã có)
git remote remove origin 2>/dev/null || true
git remote add origin "$REMOTE_URL"

echo "🚀 Đang push lên $REMOTE_URL ..."
git push -u origin main

echo "✅ Hoàn tất! Mở repo của bạn trên GitHub để kiểm tra."
