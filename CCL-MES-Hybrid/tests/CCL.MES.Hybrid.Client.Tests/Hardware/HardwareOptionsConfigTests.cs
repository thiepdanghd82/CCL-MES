using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Hardware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CCL.MES.Hybrid.Client.Tests.Hardware;

public sealed class HardwareOptionsConfigTests
{
    [Fact]
    public void AddCclHybridClient_binds_hardware_section_from_configuration()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CclApi:BaseUrl"] = "http://127.0.0.1:5100",
                ["CclApi:Timeout"] = "00:00:30",
                ["Hardware:ScanEnabled"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<ITokenStore, InMemoryTokenStore>();
        services.AddCclHybridClient(cfg);
        var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<HardwareOptions>>().Value;
        Assert.True(opts.ScanEnabled);
    }

    [Fact]
    public void AddCclHybridClient_defaults_ScanEnabled_to_false_when_section_missing()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CclApi:BaseUrl"] = "http://127.0.0.1:5100",
                ["CclApi:Timeout"] = "00:00:30",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<ITokenStore, InMemoryTokenStore>();
        services.AddCclHybridClient(cfg);
        var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<HardwareOptions>>().Value;
        Assert.False(opts.ScanEnabled);
    }

    [Fact]
    public void AddCclHybridClient_registers_all_four_hardware_interfaces()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CclApi:BaseUrl"] = "http://127.0.0.1:5100",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<ITokenStore, InMemoryTokenStore>();
        services.AddCclHybridClient(cfg);
        var sp = services.BuildServiceProvider();

        Assert.IsType<StubBarcodeScannerService>(sp.GetRequiredService<IBarcodeScannerService>());
        Assert.IsType<StubLabelPrinterService>(sp.GetRequiredService<ILabelPrinterService>());
        Assert.IsType<StubWeighScaleService>(sp.GetRequiredService<IWeighScaleService>());
        Assert.IsType<StubIdleMonitor>(sp.GetRequiredService<IIdleMonitor>());
        Assert.IsType<InMemoryDeviceModeService>(sp.GetRequiredService<IDeviceModeService>());
    }
}
