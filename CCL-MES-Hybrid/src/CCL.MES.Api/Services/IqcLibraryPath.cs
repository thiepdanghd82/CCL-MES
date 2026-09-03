namespace CCL.MES.Api.Services;

/// <summary>
/// P12 — tìm thư mục chứa ba file CSV master của thư viện IQC.
///
/// <para>Đi <b>ngược</b> từ ContentRootPath lên tới khi thấy
/// <c>CCL-MES-Hybrid/docs/iqc-library</c>. Không hardcode đường dẫn: API chạy
/// từ nhiều chỗ khác nhau (dev · CI · bundle app), và bài học DB-path ở
/// P10.7d-4 cho thấy giả định "chạy từ thư mục nào" luôn sai ở đúng cái máy
/// mình không thử.</para>
///
/// <para>Ghi đè bằng biến môi trường <c>MES_IQC_LIBRARY_DIR</c> khi cần trỏ
/// tới bản master khác (vd bản Ops vừa cập nhật).</para>
/// </summary>
public static class IqcLibraryPath
{
    public const string EnvVar = "MES_IQC_LIBRARY_DIR";

    public static string? Resolve(string contentRoot)
    {
        var env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;

        var d = new DirectoryInfo(contentRoot);
        while (d is not null)
        {
            var p = Path.Combine(d.FullName, "CCL-MES-Hybrid", "docs", "iqc-library");
            if (Directory.Exists(p)) return p;
            d = d.Parent;
        }
        return null;
    }
}
