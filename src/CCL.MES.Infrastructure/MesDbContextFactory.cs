using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CCL.MES.Infrastructure;

/// <summary>
/// Factory dùng cho công cụ dòng lệnh EF Core (dotnet ef migrations / database update)
/// khi chạy ngoài runtime của Web. Đọc biến môi trường để chọn provider:
///   MES_PROVIDER = Sqlite | SqlServer   (mặc định Sqlite)
///   MES_CONNSTR  = chuỗi kết nối tuỳ chọn
/// Ví dụ tạo migration cho SQL Server:
///   MES_PROVIDER=SqlServer dotnet ef migrations add Init -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web
/// </summary>
public class MesDbContextFactory : IDesignTimeDbContextFactory<MesDbContext>
{
    public MesDbContext CreateDbContext(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("MES_PROVIDER") ?? "Sqlite";
        var cs = Environment.GetEnvironmentVariable("MES_CONNSTR")
                 ?? (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                        ? "Server=localhost;Database=CCL_MES;Trusted_Connection=True;TrustServerCertificate=True"
                        : "Data Source=ccl_mes.db");

        var options = new DbContextOptionsBuilder<MesDbContext>();
        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            options.UseSqlServer(cs, b => b.MigrationsAssembly("CCL.MES.Infrastructure"));
        else
            options.UseSqlite(cs, b => b.MigrationsAssembly("CCL.MES.Infrastructure"));

        return new MesDbContext(options.Options);
    }
}
