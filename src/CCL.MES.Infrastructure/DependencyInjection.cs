using CCL.MES.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString("Default") ?? "Data Source=ccl_mes.db";
        services.AddDbContext<MesDbContext>(o => o.UseSqlite(cs));
        services.AddScoped<IMesDbContext>(sp => sp.GetRequiredService<MesDbContext>());
        return services;
    }
}
