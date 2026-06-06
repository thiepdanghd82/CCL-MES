using CCL.MES.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<WorkOrderService>();
        // P10.7b-1 — PREPRESS BOM snapshot materialiser. Used by
        // WorkOrderService.CreateAsync + 7b-2 endpoint lazy-materialise path.
        services.AddScoped<PrepressBomSnapshotService>();
        services.AddScoped<SpecService>();
        services.AddScoped<SpecQcWindowService>();
        services.AddScoped<SpecQcCaptureService>();
        services.AddScoped<DrawingsService>();
        services.AddScoped<QcService>();
        services.AddScoped<OeeService>();
        services.AddScoped<WiService>();
        services.AddScoped<NpiService>();
        // Phase 6 Bước 7 — IQC pre-WO raw-material inspection.
        services.AddScoped<IqcService>();
        return services;
    }
}
