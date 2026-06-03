using CCL.MES.Hybrid.Client.Auth;

namespace CCL.MES.Hybrid.Client.Tests;

public sealed class InMemoryTokenStoreTests
{
    [Fact]
    public async Task Get_returns_null_initially()
    {
        var store = new InMemoryTokenStore();
        Assert.Null(await store.GetAccessTokenAsync());
        Assert.Null(await store.GetRefreshTokenAsync());
    }

    [Fact]
    public async Task Save_then_get_returns_same_values()
    {
        var store = new InMemoryTokenStore();
        await store.SaveAsync("A", "R");
        Assert.Equal("A", await store.GetAccessTokenAsync());
        Assert.Equal("R", await store.GetRefreshTokenAsync());
    }

    [Fact]
    public async Task Clear_erases_both_tokens()
    {
        var store = new InMemoryTokenStore();
        await store.SaveAsync("A", "R");
        await store.ClearAsync();
        Assert.Null(await store.GetAccessTokenAsync());
        Assert.Null(await store.GetRefreshTokenAsync());
    }

    [Fact]
    public async Task Save_replaces_previous_pair()
    {
        var store = new InMemoryTokenStore();
        await store.SaveAsync("A1", "R1");
        await store.SaveAsync("A2", "R2");
        Assert.Equal("A2", await store.GetAccessTokenAsync());
        Assert.Equal("R2", await store.GetRefreshTokenAsync());
    }
}
