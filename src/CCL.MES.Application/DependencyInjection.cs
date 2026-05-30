using CCL.MES.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<WorkOrderService>();
        services.AddScoped<SpecService>();
        services.AddScoped<QcService>();
        services.AddScoped<OeeService>();
        services.AddScoped<WiService>();
        return services;
    }
}
