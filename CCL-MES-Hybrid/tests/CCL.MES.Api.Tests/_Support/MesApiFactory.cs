using System.Net.Http.Headers;
using System.Net.Http.Json;
using CCL.MES.Application;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Api.Tests._Support;

/// <summary>
/// Mirrors the Phase 9 <c>IsolatedDbFixture</c> pattern for the API: each
/// factory instance pins its own <c>/tmp/ccl-mes-api-test-&lt;guid&gt;/test.db</c>
/// + applies <c>Database.MigrateAsync()</c> against the live prod migration
/// chain. WebApplicationFactory boots <c>Program</c> with our overrides
/// applied to configuration before the host builds.
///
/// Each test class declares <c>IClassFixture&lt;MesApiFactory&gt;</c>;
/// xUnit reuses one factory per class. Tests get an
/// <see cref="HttpClient"/> via <see cref="CreateClient()"/> and seed
/// users + log in via the helpers exposed below.
/// </summary>
public sealed class MesApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string TmpRoot { get; }
    public string DbPath  { get; }

    /// <summary>HS256 signing key used by the factory — 64 random bytes
    /// hex-encoded so it's well over the 32-byte HS256 minimum.</summary>
    public string SigningKey { get; } = new string('K', 64);

    public MesApiFactory()
    {
        TmpRoot = Path.Combine(Path.GetTempPath(), $"ccl-mes-api-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(TmpRoot);
        DbPath = Path.Combine(TmpRoot, "test.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Pin DB + JWT signing key BEFORE Program.cs reads configuration.
        //
        // UseSetting writes to the in-memory configuration source that
        // Program.cs reads from. Crucially we do NOT touch
        // MES_DATA_DIR / MES_DB_PATH — those are process-wide env vars
        // and would leak between parallel xUnit factory instances, causing
        // two factories to migrate the same SQLite file and trip the
        // "table already exists" guard.
        //
        // Program.cs now prefers an explicit ConnectionStrings:Default
        // over the env var path, so this single UseSetting fully pins
        // the DB location for this factory's process scope.
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseEnvironment("Test");
    }

    public async Task InitializeAsync()
    {
        // Trigger host build first so Services are populated.
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

    /// <summary>
    /// Insert a user into the seeded DB with a freshly-hashed password.
    /// Returns the persisted entity (id populated).
    /// </summary>
    public async Task<User> SeedUserAsync(
        string username, string password, string role,
        string? displayName = null, string? department = null, bool isActive = true)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var user = new User
        {
            Username = username,
            Role = role,
            DisplayName = displayName ?? username,
            Department = department ?? "",
            IsActive = isActive,
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Login + attach the access token to the supplied <see cref="HttpClient"/>
    /// as a Bearer header. Returns the full response so tests can inspect
    /// the refresh token.
    /// </summary>
    public async Task<LoginResponse> LoginAndAuthenticateAsync(
        HttpClient client, string username, string password)
    {
        var login = await client.PostAsJsonAsync("/api/v2/auth/login",
            new LoginRequest { Username = username, Password = password });
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("Login response was empty.");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload.AccessToken);
        return payload;
    }
}
