using CCL.MES.Api.Auth;

namespace CCL.MES.Api.Tests.Unit;

/// <summary>
/// Pure unit coverage for the in-memory refresh-token store. Faster than
/// integration tests; locks in the invariants the auth controller relies
/// on (revoke flag survives, family revocation is fan-out, purge drops
/// expired only).
/// </summary>
public sealed class InMemoryRefreshTokenStoreTests
{
    [Fact]
    public void Find_unknown_token_returns_null()
    {
        var store = new InMemoryRefreshTokenStore();
        Assert.Null(store.Find("nope"));
    }

    [Fact]
    public void Store_then_find_returns_same_info()
    {
        var store = new InMemoryRefreshTokenStore();
        var info = new RefreshTokenInfo(
            UserId: 42,
            ExpiresAt: DateTime.UtcNow.AddDays(7),
            FamilyId: Guid.NewGuid(),
            Revoked: false);
        store.Store("abc", info);

        var found = store.Find("abc");
        Assert.NotNull(found);
        Assert.Equal(42, found!.UserId);
        Assert.False(found.Revoked);
    }

    [Fact]
    public void Revoke_marks_token_revoked_but_keeps_it_findable()
    {
        var store = new InMemoryRefreshTokenStore();
        store.Store("abc", new RefreshTokenInfo(42, DateTime.UtcNow.AddDays(7), Guid.NewGuid(), Revoked: false));
        store.Revoke("abc");

        var found = store.Find("abc");
        Assert.NotNull(found);
        Assert.True(found!.Revoked);
    }

    [Fact]
    public void RevokeFamily_marks_every_token_in_family_revoked()
    {
        var store = new InMemoryRefreshTokenStore();
        var family = Guid.NewGuid();
        var other = Guid.NewGuid();
        var exp = DateTime.UtcNow.AddDays(7);

        store.Store("a", new RefreshTokenInfo(1, exp, family, Revoked: false));
        store.Store("b", new RefreshTokenInfo(1, exp, family, Revoked: false));
        store.Store("c", new RefreshTokenInfo(2, exp, other,  Revoked: false));

        store.RevokeFamily(family);

        Assert.True(store.Find("a")!.Revoked);
        Assert.True(store.Find("b")!.Revoked);
        Assert.False(store.Find("c")!.Revoked); // different family — untouched
    }

    [Fact]
    public void PurgeExpired_drops_only_expired_entries()
    {
        var store = new InMemoryRefreshTokenStore();
        var now = DateTime.UtcNow;
        store.Store("fresh",   new RefreshTokenInfo(1, now.AddMinutes(10),  Guid.NewGuid(), Revoked: false));
        store.Store("stale",   new RefreshTokenInfo(1, now.AddMinutes(-10), Guid.NewGuid(), Revoked: false));

        store.PurgeExpired(now);

        Assert.NotNull(store.Find("fresh"));
        Assert.Null(store.Find("stale"));
    }
}
