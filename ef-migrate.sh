#!/usr/bin/env bash
# =============================================================
#  ef-migrate.sh — Tạo & áp dụng EF Core Migrations cho SQL Server
#  Yêu cầu: đã cài dotnet-ef  (cài 1 lần: dotnet tool install --global dotnet-ef)
#
#  Cách dùng:
#    1. Sửa connection string trong src/CCL.MES.Web/appsettings.SqlServer.json
#       (hoặc đặt biến MES_CONNSTR bên dưới)
#    2. Chạy:  bash ef-migrate.sh
# =============================================================
set -e

export MES_PROVIDER=SqlServer
# Tuỳ chọn: bỏ comment và sửa nếu muốn override connection string
# export MES_CONNSTR="Server=localhost;Database=CCL_MES;Trusted_Connection=True;TrustServerCertificate=True"

INFRA=src/CCL.MES.Infrastructure
WEB=src/CCL.MES.Web

if ! dotnet ef --version >/dev/null 2>&1; then
  echo "❌ Chưa có dotnet-ef. Cài bằng:  dotnet tool install --global dotnet-ef"
  exit 1
fi

# Tạo migration Init nếu chưa có
if [ ! -d "$INFRA/Migrations" ]; then
  echo "📦 Tạo migration Init..."
  dotnet ef migrations add Init -p "$INFRA" -s "$WEB"
fi

echo "🚀 Áp dụng migration vào SQL Server..."
dotnet ef database update -p "$INFRA" -s "$WEB"

echo "✅ Xong! Database CCL_MES đã sẵn sàng."
echo "   Nhớ đặt Database:Provider = SqlServer trong appsettings.json khi chạy app."
