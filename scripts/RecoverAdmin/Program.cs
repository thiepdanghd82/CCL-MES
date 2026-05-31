using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// Phase 6 Bước 4 — admin recovery script. Console only; lifeline when
// every admin is disabled / has lost their password / role got demoted
// past where the Account Control tab can fix from the web UI.
//
// Usage (run from repo root):
//   cd scripts/RecoverAdmin
//   dotnet run -- --reset <username> --new-password <pwd>
//   dotnet run -- --create <username> --password <pwd>
//
// Both flows REQUIRE typing CONFIRM-RECOVER at the interactive prompt
// so a typo in a shell history doesn't accidentally rotate prod creds.
// Audit row is appended to scripts/RecoverAdmin/recover.audit.log with
// timestamp + OS user + action so a later incident review can match
// the change to a person.
//
// Trust boundary = OS user with write access to the SQLite file. On
// production setups, chmod 600 on ccl_mes.db keeps the recovery surface
// to the deploy account.

if (args.Length < 4)
{
    PrintUsage();
    return 2;
}

var mode = args[0];
var username = args[1];
var pwdFlag = args[2];
var newPassword = args[3];

if (mode != "--reset" && mode != "--create")
{
    PrintUsage();
    return 2;
}
if (pwdFlag != "--new-password" && pwdFlag != "--password")
{
    PrintUsage();
    return 2;
}
if (string.IsNullOrWhiteSpace(username))
{
    Console.Error.WriteLine("Username required.");
    return 2;
}
if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 4)
{
    Console.Error.WriteLine("Password must be at least 4 characters.");
    return 2;
}

Console.Write("Type CONFIRM-RECOVER to proceed: ");
var confirm = Console.ReadLine();
if (confirm != "CONFIRM-RECOVER")
{
    Console.Error.WriteLine("Aborted (confirmation did not match).");
    return 3;
}

// Resolve DB path: relative to the SQLite connection-string convention
// the Web project uses ("Data Source=ccl_mes.db" → cwd-relative). The
// script runs from scripts/RecoverAdmin so look up the path explicitly.
var dbPath = Environment.GetEnvironmentVariable("MES_DB_PATH")
             ?? Path.GetFullPath(Path.Combine("..", "..", "src", "CCL.MES.Web", "ccl_mes.db"));
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"DB file not found: {dbPath}");
    Console.Error.WriteLine("Set MES_DB_PATH=<absolute path> to override.");
    return 4;
}

Console.WriteLine($"Operating on DB: {dbPath}");

var options = new DbContextOptionsBuilder<MesDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;
using var db = new MesDbContext(options);
var hasher = new PasswordHasher<User>();

if (mode == "--reset")
{
    var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (existing is null)
    {
        Console.Error.WriteLine($"User '{username}' not found. Use --create instead.");
        return 5;
    }
    existing.Role = UserRole.Admin;
    existing.IsActive = true;
    existing.MustChangePassword = true;
    existing.PasswordHash = hasher.HashPassword(existing, newPassword);
    existing.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    AuditLog($"RESET username={username} → role=Admin, IsActive=true, MustChangePassword=true");
    Console.WriteLine($"OK: User '{username}' is now Admin + active + must-change-password.");
    return 0;
}

// --create
var dup = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
if (dup is not null)
{
    Console.Error.WriteLine($"User '{username}' already exists. Use --reset to recover.");
    return 6;
}
var fresh = new User
{
    Username = username,
    Role = UserRole.Admin,
    DisplayName = $"Recovery: {username}",
    IsActive = true,
    MustChangePassword = true,
};
fresh.PasswordHash = hasher.HashPassword(fresh, newPassword);
db.Users.Add(fresh);
await db.SaveChangesAsync();
AuditLog($"CREATE username={username} role=Admin, IsActive=true, MustChangePassword=true");
Console.WriteLine($"OK: User '{username}' created as Admin + must-change-password.");
return 0;

static void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  dotnet run -- --reset <username> --new-password <pwd>");
    Console.Error.WriteLine("  dotnet run -- --create <username> --password <pwd>");
    Console.Error.WriteLine("Env:");
    Console.Error.WriteLine("  MES_DB_PATH  Override DB file (default: ../../src/CCL.MES.Web/ccl_mes.db)");
}

static void AuditLog(string detail)
{
    try
    {
        var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}\tos-user={Environment.UserName}\thost={Environment.MachineName}\t{detail}";
        File.AppendAllText("recover.audit.log", line + Environment.NewLine);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Warning: audit log write failed — {ex.Message}");
    }
}
