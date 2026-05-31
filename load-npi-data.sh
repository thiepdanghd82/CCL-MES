#!/usr/bin/env bash
# =============================================================
#  load-npi-data.sh — Nạp dữ liệu NPI (WorkCenter / RawMaterials /
#  Engineer Routine / Engineer Structure) từ folder Data vào SQLite.
#
#  THỨ TỰ CHẠY (quan trọng):
#    1. rm -f src/CCL.MES.Web/ccl_mes.db        # xóa DB cũ (để EF tạo bảng NPI mới)
#    2. dotnet run --project src/CCL.MES.Web    # chạy 1 lần cho EF tạo schema -> Ctrl+C để dừng
#    3. bash load-npi-data.sh                   # nạp dữ liệu (script này)
#    4. dotnet run --project src/CCL.MES.Web    # chạy lại để xem trên web
# =============================================================
set -e

# Đường dẫn folder Data (sửa nếu khác)
DATA_DIR="${1:-/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CCL-CMES/Data}"
DB="${2:-src/CCL.MES.Web/ccl_mes.db}"

if [ ! -f "$DB" ]; then
  echo "❌ Chưa thấy $DB."
  echo "   Hãy chạy app 1 lần trước để EF Core tạo bảng:  dotnet run --project src/CCL.MES.Web"
  exit 1
fi

# Cài openpyxl nếu thiếu (đọc Raw Materials.xlsx)
python3 -c "import openpyxl" 2>/dev/null || pip install openpyxl --break-system-packages --quiet

python3 tools/import_npi.py --data "$DATA_DIR" --db "$DB"
echo ""
echo "👉 Giờ chạy lại app:  dotnet run --project src/CCL.MES.Web"
echo "   rồi mở các tab NPI Data trên menu."
