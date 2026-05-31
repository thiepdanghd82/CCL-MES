#!/usr/bin/env bash
# =============================================================
#  ef-migrate.sh — EF Core Migrations cho SQLite (dev) hoặc SQL Server (prod)
#
#  Phase 5 — từ giờ SQLite cũng đi qua Migrations (thay cho EnsureCreated()).
#  App startup gọi DbInitializer.InitializeAsync() tự động baseline + Migrate
#  cho cả 2 provider; script này chỉ cần khi muốn:
#    - Tạo migration mới (vd sau khi thêm entity / field)
#    - Áp migration thủ công từ CLI (không qua app)
#
#  Yêu cầu: dotnet-ef đã cài (1 lần): dotnet tool install --global dotnet-ef
#
#  Cách dùng:
#    bash ef-migrate.sh --sqlite              # dev (mặc định)
#    bash ef-migrate.sh --sqlserver           # prod
#    bash ef-migrate.sh --sqlite add MyChange # tạo migration mới
# =============================================================
set -e

MODE="--sqlite"
EXTRA_CMD=""
EXTRA_NAME=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --sqlite|--sqlserver) MODE="$1"; shift ;;
    add) EXTRA_CMD="add"; EXTRA_NAME="$2"; shift 2 ;;
    *) echo "Unknown arg: $1"; exit 2 ;;
  esac
done

if [ "$MODE" = "--sqlserver" ]; then
  export MES_PROVIDER=SqlServer
  # export MES_CONNSTR="Server=localhost;Database=CCL_MES;Trusted_Connection=True;TrustServerCertificate=True"
  TARGET="SQL Server"
else
  export MES_PROVIDER=Sqlite
  # export MES_CONNSTR="Data Source=ccl_mes.db"
  TARGET="SQLite"
fi

INFRA=src/CCL.MES.Infrastructure
WEB=src/CCL.MES.Web

if ! dotnet ef --version >/dev/null 2>&1; then
  echo "Chưa có dotnet-ef. Cài bằng:  dotnet tool install --global dotnet-ef --version 10.*"
  exit 1
fi

if [ -n "$EXTRA_CMD" ] && [ "$EXTRA_CMD" = "add" ]; then
  if [ -z "$EXTRA_NAME" ]; then
    echo "Phải đặt tên migration:  bash ef-migrate.sh --sqlite add <Name>"
    exit 2
  fi
  echo "Tạo migration $EXTRA_NAME ($TARGET)..."
  dotnet ef migrations add "$EXTRA_NAME" -p "$INFRA" -s "$WEB" -o Migrations
  echo "Xong. Restart app để DbInitializer áp dụng."
  exit 0
fi

# Tạo Init nếu chưa có
if [ ! -d "$INFRA/Migrations" ]; then
  echo "Chưa có Migrations/. Tạo Init ($TARGET)..."
  dotnet ef migrations add Init -p "$INFRA" -s "$WEB" -o Migrations
fi

echo "Áp dụng migrations vào $TARGET..."
dotnet ef database update -p "$INFRA" -s "$WEB"

echo "Xong. Khi chạy app, DbInitializer cũng tự áp ($TARGET = '$MES_PROVIDER' trong appsettings.json)."
