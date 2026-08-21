using CCL.MES.Hybrid.Client.Windows;

namespace CCL.MES.Hybrid.Client.Tests.Windows;

/// <summary>
/// Locks the host-populated <see cref="WindowRegistry"/> contract: register,
/// resolve, and replace-by-key idempotency. The concrete PR1 page TYPES are
/// registered host-side (Razor project) — here we use a stand-in type since
/// the Client library cannot reference the Razor page types (dependency runs
/// Razor → Client, not the reverse).
/// </summary>
public sealed class WindowRegistryTests
{
    private sealed class DummyBody { }

    [Fact]
    public void Register_then_resolve_returns_entry()
    {
        var r = new WindowRegistry();
        var e = new WindowRegistryEntry(
            WindowRegistryKeys.QcHistory, typeof(DummyBody),
            WindowRegistryKeys.TitleKeys.QcHistory);
        r.Register(e);

        Assert.Same(e, r.Resolve(WindowRegistryKeys.QcHistory));
        Assert.Single(r.Entries);
    }

    [Fact]
    public void Resolve_unknown_key_returns_null()
    {
        var r = new WindowRegistry();
        Assert.Null(r.Resolve("/nope"));
    }

    [Fact]
    public void Register_same_key_replaces_and_does_not_grow()
    {
        var r = new WindowRegistry();
        r.Register(new WindowRegistryEntry("/k", typeof(DummyBody), "t1"));
        r.Register(new WindowRegistryEntry("/k", typeof(DummyBody), "t2"));

        Assert.Single(r.Entries);
        Assert.Equal("t2", r.Resolve("/k")!.TitleKey);
    }

    [Fact]
    public void QcLibrary_roles_mirror_the_page_authorize_list()
    {
        Assert.Equal(
            new[] { "Admin", "Supervisor", "Engineer", "QC" },
            WindowRegistryKeys.QcLibraryRoles);
    }
}
