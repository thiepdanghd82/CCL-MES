using CCL.MES.Application;
using CCL.MES.Application.SpecExport;
using CCL.MES.Application.SpecImport;
using CCL.MES.Application.WorkOrderExport;
using CCL.MES.Infrastructure.SpecExport;
using CCL.MES.Infrastructure.SpecImport;
using CCL.MES.Infrastructure.WorkOrderExport;
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

        // Phase 8 PR #31c — list exporters (3 formats). Stateless impl, safe
        // as Singleton. CSV impl lives in Application (pure .NET); xlsx + pdf
        // impl in Infrastructure (ClosedXML / PdfSharp deps).
        services.AddSingleton<CsvSpecListExporter>();
        services.AddSingleton<XlsxSpecListExporter>();
        services.AddSingleton<PdfSpecListExporter>();
        // PR #31d — single-spec detail sheet PDF exporter (reuse PDF builder layer)
        services.AddSingleton<PdfSpecSheetExporter>();
        // Resolve by format string — controller pattern. ISpecListExporter
        // multi-impl đăng ký để future PR thêm exporter (JSON, PDF detail, …)
        // chỉ cần thêm dòng dưới.
        services.AddSingleton<ISpecListExporter>(sp => sp.GetRequiredService<CsvSpecListExporter>());
        services.AddSingleton<ISpecListExporter>(sp => sp.GetRequiredService<XlsxSpecListExporter>());
        services.AddSingleton<ISpecListExporter>(sp => sp.GetRequiredService<PdfSpecListExporter>());

        // Phase 8 PR #32c — Work Order list exporters. Same Singleton + concrete
        // + interface-multi-impl pattern as Spec exporters above. Stateless,
        // re-uses ClosedXML from Infrastructure; no new dep added.
        services.AddSingleton<CsvWorkOrderListExporter>();
        services.AddSingleton<XlsxWorkOrderListExporter>();
        services.AddSingleton<IWorkOrderListExporter>(sp => sp.GetRequiredService<CsvWorkOrderListExporter>());
        services.AddSingleton<IWorkOrderListExporter>(sp => sp.GetRequiredService<XlsxWorkOrderListExporter>());

        return services;
    }
}
