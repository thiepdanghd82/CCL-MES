using System.Text;
using Microsoft.Extensions.Configuration;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// Địa chỉ server theo từng máy xưởng (2026-09-25) — thứ tự đè: đóng gói →
/// file cấp máy → biến môi trường. Test biến môi trường chạy tuần tự vì biến
/// môi trường là của cả tiến trình.
/// </summary>
[Collection(nameof(StationConfigEnvCollection))]
public sealed class StationConfigTests : IDisposable
{
    private const string EnvVar = StationConfig.EnvPrefix + "CclApi__BaseUrl";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccl-station-" + Guid.NewGuid().ToString("N"));

    public StationConfigTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable(EnvVar, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, null);
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
    }

    private static string Json(string url) => "{\"CclApi\":{\"BaseUrl\":\"" + url + "\"}}";

    private static ConfigurationBuilder Bundled(string url)
    {
        var b = new ConfigurationBuilder();
        b.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(Json(url))));
        return b;
    }

    private string MachineFile(string? url)
    {
        var p = Path.Combine(_dir, StationConfig.FileName);
        if (url is not null) File.WriteAllText(p, Json(url));
        return p;
    }

    [Fact]
    public void No_override_keeps_bundled_value()
    {
        var file = MachineFile(null);                    // path given but file absent
        var root = Bundled("http://127.0.0.1:5100").AddStationOverrides(file).Build();

        Assert.Equal("http://127.0.0.1:5100", root[StationConfig.BaseUrlKey]);
        Assert.Equal("bundled appsettings.json", StationConfig.DescribeBaseUrlSource(root, file));
    }

    [Fact]
    public void Machine_file_overrides_bundled()
    {
        var file = MachineFile("http://10.102.3.87:5100");
        var root = Bundled("http://127.0.0.1:5100").AddStationOverrides(file).Build();

        Assert.Equal("http://10.102.3.87:5100", root[StationConfig.BaseUrlKey]);
        Assert.Equal($"file {file}", StationConfig.DescribeBaseUrlSource(root, file));
    }

    [Fact]
    public void Environment_variable_overrides_machine_file()
    {
        var file = MachineFile("http://10.102.3.87:5100");
        Environment.SetEnvironmentVariable(EnvVar, "http://10.0.0.9:5100");
        var root = Bundled("http://127.0.0.1:5100").AddStationOverrides(file).Build();

        Assert.Equal("http://10.0.0.9:5100", root[StationConfig.BaseUrlKey]);
        Assert.StartsWith("env ", StationConfig.DescribeBaseUrlSource(root, file));
    }

    [Theory]
    [InlineData("http://10.102.3.87:5100", null)]
    [InlineData("https://mes.ccl.local", null)]
    [InlineData("", "empty")]
    [InlineData("10.102.3.87:5100", "not an absolute URL")]  // thiếu http:// — lỗi gõ hay gặp nhất
    [InlineData("not a url", "not an absolute URL")]
    [InlineData("ftp://10.0.0.1", "scheme 'ftp' is not http/https")]
    public void ValidateBaseUrl(string value, string? expected)
        => Assert.Equal(expected, StationConfig.ValidateBaseUrl(value));

    [Fact]
    public void Build_applies_valid_machine_override()
    {
        var file = MachineFile("http://10.102.3.87:5100");
        var (cfg, source, error) = StationConfig.Build(Encoding.UTF8.GetBytes(Json("http://127.0.0.1:5100")), file);

        Assert.Equal("http://10.102.3.87:5100", cfg[StationConfig.BaseUrlKey]);
        Assert.Equal($"file {file}", source);
        Assert.Null(error);
    }

    [Fact]
    public void Build_invalid_override_falls_back_to_bundled_without_crashing()
    {
        // Nhánh này build HAI lần. Bản đầu dùng chung một stream cho cả hai lần
        // và app sập lúc khởi động: "Stream was not readable" (đo 2026-09-25).
        var file = MachineFile("10.102.3.87:5100");               // thiếu http:// — lỗi gõ hay gặp
        var (cfg, source, error) = StationConfig.Build(Encoding.UTF8.GetBytes(Json("http://127.0.0.1:5100")), file);

        Assert.Equal("http://127.0.0.1:5100", cfg[StationConfig.BaseUrlKey]);
        Assert.Equal("bundled appsettings.json (fallback)", source);
        Assert.Contains("not an absolute URL", error);
    }

    [Fact]
    public void Machine_file_lives_in_admin_only_locations()
    {
        Assert.Equal(Path.Combine("/Library", "Application Support", "CCL MES", StationConfig.FileName),
            StationConfig.MachineFilePath(isWindows: false, isMac: true));
        Assert.Equal(Path.Combine(@"C:\ProgramData", "CCL MES", StationConfig.FileName),
            StationConfig.MachineFilePath(isWindows: true, isMac: false, programData: @"C:\ProgramData"));
        Assert.Null(StationConfig.MachineFilePath(isWindows: false, isMac: false));
    }
}

[CollectionDefinition(nameof(StationConfigEnvCollection), DisableParallelization = true)]
public sealed class StationConfigEnvCollection { }
