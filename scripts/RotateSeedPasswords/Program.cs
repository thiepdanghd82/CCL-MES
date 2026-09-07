using System.Security.Cryptography;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// Kiểm định 2026-09-07 R3 — xoay mật khẩu 4 tài khoản seed demo trên live
// mà KHÔNG nâng quyền Admin (khác RecoverAdmin --reset).
//
// Usage (repo root hoặc scripts/RotateSeedPasswords):
//   MES_DB_PATH=/abs/data/ccl_mes.db \
//     dotnet run --project scripts/RotateSeedPasswords -- \
//       --confirm CONFIRM-ROTATE-SEED
//
// In mật khẩu tạm MỘT LẦN ra stdout rồi buộc MustChangePassword=true.
// Không ghi mật khẩu vào audit log.

var confirm = GetArg(args, "--confirm");
if (confirm != "CONFIRM-ROTATE-SEED")
{
    Console.Error.WriteLine(
        "Usage: MES_DB_PATH=/abs/ccl_mes.db dotnet run -- --confirm CONFIRM-ROTATE-SEED");
    Console.Error.WriteLine("Aborted (missing or wrong --confirm).");
    return 2;
}

var targets = new[] { "supervisor", "engineer", "qc", "operator" };
var dbPath = Environment.GetEnvironmentVariable("MES_DB_PATH")
             ?? Path.GetFullPath(Path.Combine(
                 AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "ccl_mes.db"));
// When run via `dotnet run --project`, BaseDirectory is bin/Debug/net10.0 —
// walk to repo data/. Prefer explicit MES_DB_PATH.
if (!File.Exists(dbPath))
{
    dbPath = Path.GetFullPath(Path.Combine(
        Directory.GetCurrentDirectory(), "data", "ccl_mes.db"));
}
if (!File.Exists(dbPath))
{
    // scripts/RotateSeedPasswords cwd
    dbPath = Path.GetFullPath(Path.Combine("..", "..", "data", "ccl_mes.db"));
}
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"DB file not found. Set MES_DB_PATH=<absolute path>.");
    return 4;
}

Console.WriteLine($"Operating on DB: {dbPath}");
Console.WriteLine("Targets: " + string.Join(", ", targets));
Console.WriteLine();

var options = new DbContextOptionsBuilder<MesDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;
await using var db = new MesDbContext(options);
var hasher = new PasswordHasher<User>();

var issued = new List<(string User, string TempPwd)>();
foreach (var username in targets)
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (user is null)
    {
        Console.Error.WriteLine($"SKIP — user '{username}' not found.");
        continue;
    }

    var temp = GeneratePassword(20);
    user.PasswordHash = hasher.HashPassword(user, temp);
    user.MustChangePassword = true;
    user.IsActive = true;
    // Giữ nguyên Role — đây không phải RecoverAdmin.
    user.UpdatedAt = DateTime.UtcNow;
    issued.Add((username, temp));
}

if (issued.Count == 0)
{
    Console.Error.WriteLine("No users updated.");
    return 5;
}

await db.SaveChangesAsync();
AuditLog($"ROTATE users={string.Join(",", issued.Select(x => x.User))} MustChangePassword=true");

Console.WriteLine("═══ MẬT KHẨU TẠM (chỉ hiện một lần — lưu chỗ an toàn) ═══");
foreach (var (user, pwd) in issued)
    Console.WriteLine($"{user}\t{pwd}");
Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine("OK: MustChangePassword=true. Lần đăng nhập sau phải đổi MK.");
return 0;

static string? GetArg(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i] == name) return args[i + 1];
    return null;
}

static string GeneratePassword(int bytes)
{
    // URL-safe, đủ entropy, tránh ký tự dễ nhầm khi đọc to.
    var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
        .Replace('+', 'x').Replace('/', 'y').TrimEnd('=');
    return raw[..Math.Min(24, raw.Length)];
}

static void AuditLog(string detail)
{
    try
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rotate-seed.audit.log");
        // Prefer script folder when running from source.
        var alt = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(), "rotate-seed.audit.log"));
        var dest = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "RotateSeedPasswords.csproj"))
            ? alt : path;
        File.AppendAllText(dest,
            $"{DateTime.UtcNow:o}\t{Environment.UserName}@{Environment.MachineName}\t{detail}{Environment.NewLine}");
    }
    catch
    {
        // Audit file failure must not undo the password rotate.
    }
}
