using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CCL.MES.Hybrid;

public partial class App : Application
{
    public App(IServiceProvider services)
    {
        InitializeComponent();
        MainPage = new MainPage();

        // MAUI does not auto-start IHostedService implementations the way a
        // generic Host would. Kick them off here so the P10.3 W4 background
        // device heartbeat actually runs. Failures are logged + swallowed —
        // a broken hosted service must NOT crash the shell.
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var hs in services.GetServices<IHostedService>())
                {
                    await hs.StartAsync(CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                var logger = services.GetService<ILoggerFactory>()?.CreateLogger("HostedServiceStartup");
                logger?.LogError(ex, "Failed to start hosted services");
                Console.WriteLine("[boot] HostedService start FAIL: " + ex);
            }
        });
    }
}
