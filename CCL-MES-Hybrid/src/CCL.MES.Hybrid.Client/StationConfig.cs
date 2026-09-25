using Microsoft.Extensions.Configuration;

namespace CCL.MES.Hybrid.Client;

/// <summary>
/// Địa chỉ server theo TỪNG MÁY xưởng, không phải build lại app (2026-09-25,
/// bước 3 của kế hoạch mở API ra LAN — xưởng có cả máy Windows lẫn Mac).
///
/// <para><b>Thứ tự, nguồn sau đè nguồn trước:</b>
/// (1) <c>appsettings.json</c> đóng gói trong app — mặc định <c>127.0.0.1</c>;
/// (2) file của MÁY <see cref="MachineFilePath"/>;
/// (3) biến môi trường <c>CCL_MES_CclApi__BaseUrl</c>.</para>
///
/// <para><b>Vì sao file nằm ở thư mục cấp MÁY</b> (<c>/Library/Application
/// Support</c>, <c>C:\ProgramData</c>) chứ không phải thư mục người dùng: ghi vào
/// đó cần quyền quản trị. Người dùng thường mà trỏ được app sang một server lạ
/// là trao luôn mật khẩu của mọi người đăng nhập trên máy đó cho server ấy.</para>
/// </summary>
public static class StationConfig
{
    public const string FileName = "appsettings.local.json";
    public const string EnvPrefix = "CCL_MES_";
    public const string BaseUrlKey = "CclApi:BaseUrl";

    /// <summary>File cấu hình cấp máy; <c>null</c> trên nền tảng không hỗ trợ.</summary>
    public static string? MachineFilePath(bool isWindows, bool isMac, string? programData = null) =>
        isWindows
            ? Path.Combine(programData ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                           "CCL MES", FileName)
        : isMac
            ? Path.Combine("/Library", "Application Support", "CCL MES", FileName)
            : null;

    /// <summary>Thêm (2) và (3) SAU nguồn đóng gói, để chúng đè lên.</summary>
    public static IConfigurationBuilder AddStationOverrides(
        this IConfigurationBuilder builder, string? machineFile)
    {
        if (!string.IsNullOrWhiteSpace(machineFile))
            builder.AddJsonFile(machineFile, optional: true, reloadOnChange: false);
        builder.AddEnvironmentVariables(EnvPrefix);
        return builder;
    }

    /// <summary>
    /// BaseUrl phải là URL tuyệt đối <c>http</c>/<c>https</c>. Sai thì trả lý do —
    /// caller rơi về giá trị đóng gói thay vì để <c>new Uri(...)</c> nổ ở lần gọi
    /// API đầu tiên, lúc người đứng máy chỉ thấy "không kết nối được".
    /// </summary>
    public static string? ValidateBaseUrl(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "empty"
        : !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var u) ? "not an absolute URL"
        : u.Scheme is not ("http" or "https") ? $"scheme '{u.Scheme}' is not http/https"
        : null;

    /// <summary>
    /// Dựng cấu hình cuối cùng: đóng gói → file máy → biến môi trường; BaseUrl sai
    /// thì rơi về giá trị đóng gói.
    ///
    /// <para><b>Mỗi lần build một <see cref="MemoryStream"/> MỚI.</b>
    /// <c>AddJsonStream</c> chỉ đọc được stream MỘT lần; build lại cùng builder
    /// làm app sập lúc khởi động với "Stream was not readable" — đo được 25-09 khi
    /// bản đầu tiên của hàm này build hai lần trong <c>MauiProgram</c>.</para>
    /// </summary>
    public static (IConfigurationRoot Config, string Source, string? Error) Build(
        byte[]? bundledJson, string? machineFile)
    {
        IConfigurationBuilder Fresh(bool withOverrides)
        {
            var b = new ConfigurationBuilder();
            if (bundledJson is not null)
                b.AddJsonStream(new MemoryStream(bundledJson, writable: false));
            if (withOverrides) b.AddStationOverrides(machineFile);
            return b;
        }

        var config = Fresh(withOverrides: true).Build();
        var source = DescribeBaseUrlSource(config, machineFile);
        if (ValidateBaseUrl(config[BaseUrlKey]) is not { } why)
            return (config, source, null);

        // Sai địa chỉ ⇒ bỏ hẳn nguồn đè, dùng bản đóng gói — KHÔNG để new Uri(...)
        // nổ ở lần gọi API đầu tiên, lúc người đứng máy chỉ thấy "không kết nối được".
        var fallback = Fresh(withOverrides: false).Build();
        return (fallback, "bundled appsettings.json (fallback)", $"{source}: {why}");
    }

    /// <summary>Nguồn nào đang quyết định BaseUrl — để log lúc khởi động.</summary>
    public static string DescribeBaseUrlSource(IConfigurationRoot root, string? machineFile)
    {
        foreach (var p in root.Providers.Reverse())
            if (p.TryGet(BaseUrlKey, out _))
                return p switch
                {
                    Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationProvider
                        => $"env {EnvPrefix}CclApi__BaseUrl",
                    Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider
                        => $"file {machineFile}",
                    _ => "bundled appsettings.json",
                };
        return "default (ApiClientOptions)";
    }
}
