using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P10.7a-2.1 — DbSeeder.SeedRecoveryDataAsync coverage.
///
/// Surface contract:
///   - 6 Recovery-kind reason codes (REC-OP-WEDGE / REC-HW-FAULT /
///     REC-MIG-LAG / REC-DATA-DRIFT / REC-IPQC-OVR / REC-TEST-RESET)
///     materialise after first call.
///   - "sys-recovery" user materialises with Role=Sys / IsActive=false
///     / non-PBKDF2 PasswordHash so login cannot succeed even if the
///     IsActive guard regresses.
///   - Re-running on a seeded DB is a NOOP (per-row idempotency).
///   - Re-running on a DB that already has Pause + Scrap codes (the
///     legacy SeedReasonCodesAsync output) still seeds the Recovery
///     codes (different .Any() gate scope).
/// </summary>
// IDisposable (not IClassFixture) so each [Fact] gets a fresh DB. The
// six fixtures mutate ReasonCodes / Users — sharing a single fixture
// makes the ordering of [Fact] discovery decide the assertions, which
// is exactly the spurious-failure pattern Rule 6 trains us out of.
public sealed class DbSeederRecoveryTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public DbSeederRecoveryTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    private static readonly string[] ExpectedRecoveryCodes = new[]
    {
        "REC-OP-WEDGE",
        "REC-HW-FAULT",
        "REC-MIG-LAG",
        "REC-DATA-DRIFT",
        "REC-IPQC-OVR",
        "REC-TEST-RESET",
    };

    [Fact]
    public async Task First_seed_creates_six_recovery_reason_codes()
    {
        using var db = _fx.NewContext();
        await DbSeeder.SeedRecoveryDataAsync(db);

        var codes = await db.ReasonCodes
            .Where(r => r.Kind == ReasonCodeKind.Recovery)
            .OrderBy(r => r.Sort)
            .ToListAsync();

        Assert.Equal(ExpectedRecoveryCodes.Length, codes.Count);
        Assert.Equal(ExpectedRecoveryCodes, codes.Select(c => c.Code).ToArray());

        // Every Recovery code carries an EN + VN label so the UI does
        // not have to fall back per-language.
        foreach (var c in codes)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.LabelEn), $"{c.Code}: EN label empty");
            Assert.False(string.IsNullOrWhiteSpace(c.LabelVi), $"{c.Code}: VN label empty");
        }
    }

    [Fact]
    public async Task First_seed_creates_sys_recovery_user()
    {
        using var db = _fx.NewContext();
        await DbSeeder.SeedRecoveryDataAsync(db);

        var user = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Username == DbSeeder.SysRecoveryUsername);

        Assert.NotNull(user);
        Assert.Equal(UserRole.Sys, user!.Role);
        Assert.False(user.IsActive);
        Assert.False(user.MustChangePassword);
        Assert.Equal("system", user.Department);
        Assert.False(string.IsNullOrEmpty(user.DisplayName));

        // PasswordHash is a sentinel literal, NOT a PBKDF2 base64
        // string. Guards against "what if IsActive=false guard is lifted
        // accidentally" — hash never decodes to a valid password.
        Assert.Equal("!SYS-RECOVERY-LOCKED-NEVER-LOGIN!", user.PasswordHash);
    }

    [Fact]
    public async Task Re_seed_is_noop_for_reason_codes()
    {
        using var db = _fx.NewContext();
        await DbSeeder.SeedRecoveryDataAsync(db);
        var countAfterFirst = await db.ReasonCodes
            .CountAsync(r => r.Kind == ReasonCodeKind.Recovery);

        await DbSeeder.SeedRecoveryDataAsync(db);
        await DbSeeder.SeedRecoveryDataAsync(db);
        var countAfterThird = await db.ReasonCodes
            .CountAsync(r => r.Kind == ReasonCodeKind.Recovery);

        Assert.Equal(ExpectedRecoveryCodes.Length, countAfterFirst);
        Assert.Equal(countAfterFirst, countAfterThird);
    }

    [Fact]
    public async Task Re_seed_is_noop_for_sys_user()
    {
        using var db = _fx.NewContext();
        await DbSeeder.SeedRecoveryDataAsync(db);
        var idAfterFirst = await db.Users.AsNoTracking()
            .Where(u => u.Username == DbSeeder.SysRecoveryUsername)
            .Select(u => u.Id)
            .SingleAsync();

        await DbSeeder.SeedRecoveryDataAsync(db);
        await DbSeeder.SeedRecoveryDataAsync(db);

        var count = await db.Users
            .CountAsync(u => u.Username == DbSeeder.SysRecoveryUsername);
        var idAfterThird = await db.Users.AsNoTracking()
            .Where(u => u.Username == DbSeeder.SysRecoveryUsername)
            .Select(u => u.Id)
            .SingleAsync();

        Assert.Equal(1, count);
        Assert.Equal(idAfterFirst, idAfterThird);
    }

    [Fact]
    public async Task Partial_pre_existing_recovery_code_only_fills_missing()
    {
        using var db = _fx.NewContext();

        // Pre-seed one Recovery code by hand to simulate operator-loaded
        // partial state (e.g. someone added REC-OP-WEDGE manually before
        // the seed ran).
        db.ReasonCodes.Add(new global::CCL.MES.Domain.Entities.ReasonCode
        {
            Code    = "REC-OP-WEDGE",
            LabelEn = "Manually pre-seeded",
            LabelVi = "Đã seed thủ công",
            Kind    = ReasonCodeKind.Recovery,
            Sort    = 999,
        });
        await db.SaveChangesAsync();

        await DbSeeder.SeedRecoveryDataAsync(db);

        var codes = await db.ReasonCodes
            .Where(r => r.Kind == ReasonCodeKind.Recovery)
            .OrderBy(r => r.Code)
            .ToListAsync();

        // 6 expected; pre-existing REC-OP-WEDGE not overwritten + 5 new ones added.
        Assert.Equal(ExpectedRecoveryCodes.Length, codes.Count);
        var preExisting = codes.Single(c => c.Code == "REC-OP-WEDGE");
        Assert.Equal("Manually pre-seeded", preExisting.LabelEn);
        Assert.Equal(999, preExisting.Sort);
    }

    [Fact]
    public async Task Recovery_codes_coexist_with_pause_and_scrap_codes()
    {
        using var db = _fx.NewContext();

        // Drop in a Pause + a Scrap row to mimic a DB that already ran
        // SeedReasonCodesAsync.
        db.ReasonCodes.Add(new global::CCL.MES.Domain.Entities.ReasonCode
        {
            Code = "ML-MAT", LabelEn = "Material", LabelVi = "Vật liệu",
            Kind = ReasonCodeKind.Pause, Sort = 10,
        });
        db.ReasonCodes.Add(new global::CCL.MES.Domain.Entities.ReasonCode
        {
            Code = "SC-COLOR", LabelEn = "Colour", LabelVi = "Lệch màu",
            Kind = ReasonCodeKind.Scrap, Sort = 10,
        });
        await db.SaveChangesAsync();

        await DbSeeder.SeedRecoveryDataAsync(db);

        var byKind = await db.ReasonCodes
            .GroupBy(r => r.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToListAsync();

        Assert.Contains(byKind, x => x.Kind == ReasonCodeKind.Pause && x.Count == 1);
        Assert.Contains(byKind, x => x.Kind == ReasonCodeKind.Scrap && x.Count == 1);
        Assert.Contains(byKind, x => x.Kind == ReasonCodeKind.Recovery && x.Count == ExpectedRecoveryCodes.Length);
    }

    [Fact]
    public void Sys_role_is_not_in_the_assignable_whitelist()
    {
        // Defence-in-depth: the seed plants Role=Sys directly, but the
        // AccountControl Create/Update wire path goes through IsValid
        // which MUST reject Sys. Locks down a stray admin trying to
        // create a fresh Sys user via the API.
        Assert.False(UserRole.IsValid(UserRole.Sys));
        Assert.True(UserRole.IsSystemAccount(UserRole.Sys));
        Assert.False(UserRole.IsSystemAccount(UserRole.Admin));
        Assert.False(UserRole.IsSystemAccount(null));
        Assert.False(UserRole.IsSystemAccount(""));
        Assert.DoesNotContain(UserRole.Sys, UserRole.All);
    }
}
