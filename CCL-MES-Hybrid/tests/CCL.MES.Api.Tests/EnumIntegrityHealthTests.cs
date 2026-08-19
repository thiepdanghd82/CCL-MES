using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CCL.MES.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Factory riêng cho tầng 3: pin DB của chính nó và TẮT cache của
/// <c>EnumIntegrityMonitor</c> để mỗi lần gọi /health/ready là một lần quét
/// thật. Không dùng chung <c>MesApiFactory</c> vì cache 300s mặc định (đúng cho
/// production) sẽ khiến test đọc lại ảnh chụp lúc boot — tức là test một hằng
/// số chứ không test cơ chế.
/// </summary>
public sealed class EnumIntegrityHealthFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string TmpRoot { get; } = Path.Combine(
        Path.GetTempPath(), $"ccl-mes-enum-health-{Guid.NewGuid():N}");

    public string DbPath => Path.Combine(TmpRoot, "test.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(TmpRoot);
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
        builder.UseSetting("Jwt:SigningKey", new string('K', 64));
        builder.UseSetting("Health:EnumIntegrityCacheSeconds", "0");
        // Lớp test này CỐ Ý bật lại preflight lúc boot (mặc định tắt trong môi
        // trường Test) — nó là lớp duy nhất phủ đường boot, và nó không chứa
        // test soak nào để phải tranh khoá ghi với.
        builder.UseSetting("Health:EnumIntegrityPreflightOnBoot", "true");
        builder.UseEnvironment("Test");
    }

    public async Task InitializeAsync()
    {
        _ = Services;
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        await db.Database.MigrateAsync();
    }

    public new Task DisposeAsync()
    {
        base.Dispose();
        try { if (Directory.Exists(TmpRoot)) Directory.Delete(TmpRoot, recursive: true); }
        catch { /* best effort */ }
        return Task.CompletedTask;
    }

    public async Task RawWriteAsync(string sql)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}

/// <summary>
/// TẦNG 3 của gate-enum-integrity — tầng duy nhất nhìn thấy DỮ LIỆU LIVE.
///
/// Hai điều được khoá lại ở đây, và chúng là hai vế của cùng một quyết định
/// thiết kế:
///   1. Dữ liệu nhiễm KHÔNG được hạ HTTP status của /health/ready. Trả 503 ở
///      endpoint mà load balancer dùng sẽ biến sự cố 41% route thành mất toàn
///      bộ dịch vụ — cơ chế canh tự gây thiệt hại lớn hơn cái nó canh.
///   2. Nhưng nó PHẢI hiện ra ở TRƯỜNG dataIntegrity. Hỏng im lặng là lý do
///      defect 2026-08-19 sống 30 ngày.
/// </summary>
public sealed class EnumIntegrityHealthTests : IClassFixture<EnumIntegrityHealthFactory>
{
    private readonly EnumIntegrityHealthFactory _fx;

    public EnumIntegrityHealthTests(EnumIntegrityHealthFactory fx) => _fx = fx;

    [Fact]
    public async Task Ready_reports_ok_on_a_clean_database()
    {
        var body = await ReadyAsync();

        Assert.True(body.RootElement.GetProperty("ready").GetBoolean());
        var integrity = body.RootElement.GetProperty("dataIntegrity");
        Assert.Equal("ok", integrity.GetProperty("status").GetString());
        Assert.Equal("health.enumIntegrity.ok", integrity.GetProperty("messageKey").GetString());
        Assert.True(integrity.GetProperty("columnsScanned").GetInt32() >= 37);
        Assert.Equal(0, integrity.GetProperty("badRows").GetInt64());
    }

    [Fact]
    public async Task Ready_reports_degraded_when_live_data_is_contaminated_but_stays_200()
    {
        await SeedRawWorkOrderAsync(9101, "Done");
        await SeedRawWorkOrderAsync(9102, "Done");
        try
        {
            var (status, body) = await ReadyWithStatusAsync();

            // 200, không phải 503 — nhà máy vẫn phải được phục vụ.
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.True(body.RootElement.GetProperty("ready").GetBoolean());

            var integrity = body.RootElement.GetProperty("dataIntegrity");
            Assert.Equal("degraded", integrity.GetProperty("status").GetString());
            Assert.Equal("health.enumIntegrity.degraded", integrity.GetProperty("messageKey").GetString());
            Assert.Equal(2, integrity.GetProperty("badRows").GetInt64());
            Assert.Equal(1, integrity.GetProperty("badColumns").GetInt32());

            var violations = integrity.GetProperty("violations")
                .EnumerateArray().Select(v => v.GetString() ?? "").ToList();
            Assert.Contains(violations, v => v.Contains("WorkOrders.CurrentStep = 'Done' x2", StringComparison.Ordinal));
        }
        finally
        {
            await _fx.RawWriteAsync("DELETE FROM WorkOrders WHERE Id IN (9101, 9102);");
        }
    }

    [Fact]
    public async Task Ready_recovers_to_ok_after_the_data_is_repaired()
    {
        // Chuỗi PASS → FAIL → PASS ở đúng tầng 3, trên đúng đường mà người trực
        // ca sẽ nhìn. Gate chỉ đỏ được mà không xanh lại được thì không ai tin.
        await SeedRawWorkOrderAsync(9103, "Done");
        try
        {
            var dirty = await ReadyAsync();
            Assert.Equal("degraded",
                dirty.RootElement.GetProperty("dataIntegrity").GetProperty("status").GetString());

            // Sửa DỮ LIỆU về một thành viên enum hợp lệ — đúng PA-B của runbook,
            // KHÔNG thêm thành viên enum mới để hợp thức hoá giá trị rác.
            await _fx.RawWriteAsync("UPDATE WorkOrders SET CurrentStep = 'Closed' WHERE Id = 9103;");

            var clean = await ReadyAsync();
            Assert.Equal("ok",
                clean.RootElement.GetProperty("dataIntegrity").GetProperty("status").GetString());
        }
        finally
        {
            await _fx.RawWriteAsync("DELETE FROM WorkOrders WHERE Id = 9103;");
        }
    }

    [Fact]
    public async Task Ready_stays_anonymous()
    {
        // Không token — probe của load balancer không cầm token bao giờ.
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/v2/health/ready");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    private async Task<JsonDocument> ReadyAsync() => (await ReadyWithStatusAsync()).Body;

    private async Task<(HttpStatusCode Status, JsonDocument Body)> ReadyWithStatusAsync()
    {
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/v2/health/ready");
        var json = await resp.Content.ReadFromJsonAsync<JsonDocument>()
            ?? throw new InvalidOperationException("/health/ready trả body rỗng");
        return (resp.StatusCode, json);
    }

    private Task SeedRawWorkOrderAsync(int id, string currentStep) =>
        _fx.RawWriteAsync($"""
            INSERT OR IGNORE INTO Customers (Id, Code, Name, CreatedAt)
            VALUES (1, 'ENUMGATE', 'enum-integrity fixture', '2026-08-19 00:00:00');
            INSERT OR IGNORE INTO Products (Id, ProductCode, Name, CustomerId, CreatedAt)
            VALUES (1, 'ENUMGATE-P', 'enum-integrity fixture', 1, '2026-08-19 00:00:00');
            INSERT INTO WorkOrders
                (Id, WoNo, CustomerId, ProductId, ProductName, Uom, TargetQty, ProducedQty,
                 Priority, MaterialsReady, RohsOk, SetupConfirmed,
                 CurrentStep, Status, MesPhase, CreatedAt)
            VALUES
                ({id}, 'WO-HEALTH-{id}', 1, 1, 'enum-integrity fixture', 'PCS', 100, 0,
                 0, 0, 0, 0,
                 '{currentStep}', 'Finished', 'SHIPPED', '2026-08-19 00:00:00');
            """);
}
