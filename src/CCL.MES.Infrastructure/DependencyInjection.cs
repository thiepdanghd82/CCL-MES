using CCL.MES.Application;
using CCL.MES.Application.SpecImport;
using CCL.MES.Infrastructure.SpecImport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Đăng ký tầng dữ liệu. Chọn provider qua cấu hình "Database:Provider"
    /// = "Sqlite" (mặc định, dev) hoặc "SqlServer" (production).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Database:Provider"] ?? "Sqlite";
        var cs = config.GetConnectionString("Default")
                 ?? (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                        ? "Server=localhost;Database=CCL_MES;Trusted_Connection=True;TrustServerCertificate=True"
                        : "Data Source=ccl_mes.db");

        services.AddDbContext<MesDbContext>(o =>
        {
            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                o.UseSqlServer(cs, b => b.MigrationsAssembly("CCL.MES.Infrastructure"));
            else
                o.UseSqlite(cs, b => b.MigrationsAssembly("CCL.MES.Infrastructure"));
        });

        services.AddScoped<IMesDbContext>(sp => sp.GetRequiredService<MesDbContext>());

        // Phase 8 PR #31a — silkscreen xlsx parser (ClosedXML impl).
        // PR #31b — flexo parser + category-keyed factory dispatch.
        services.AddSingleton<SilkscreenXlsxParser>();
        services.AddSingleton<FlexoXlsxParser>();
        services.AddSingleton<ISpecXlsxParserFactory, SpecXlsxParserFactory>();
        // Giữ ISpecXlsxParser registration cho backward-compat callers (PR #31a)
        // — resolve về silkscreen default (caller mới phải dùng factory thay vì
        // trực tiếp inject ISpecXlsxParser).
        services.AddSingleton<ISpecXlsxParser>(sp => sp.GetRequiredService<SilkscreenXlsxParser>());
        return services;
    }
}
