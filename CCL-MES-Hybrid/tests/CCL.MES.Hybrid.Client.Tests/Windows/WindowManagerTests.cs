using CCL.MES.Hybrid.Client.Windows;

namespace CCL.MES.Hybrid.Client.Tests.Windows;

/// <summary>
/// Behaviour lock for <see cref="WindowManager"/> — every rule Henry chốt for
/// P2-PR1: soft-cap-8 BLOCK (no auto-close), session-only, dedupe-by-key →
/// focus, minimize stops-active-role, maximize/restore toggle, close promotes
/// next, and Changed fires exactly once per mutation.
/// </summary>
public sealed class WindowManagerTests
{
    // A harmless self-contained type to stand in for a body component in unit
    // tests (the manager only ever stores the Type, never instantiates it).
    private sealed class DummyBody { }
    private static Type Body => typeof(DummyBody);

    private static WindowManager NewMgr() => new();

    private static OpenWindow OpenOne(WindowManager m, string key, string title = "T")
        => m.Open(key, title, icon: null, contentType: Body)!;

    // ---- Open (new) ------------------------------------------------------

    [Fact]
    public void Open_new_window_increments_count_sets_active_and_top_zorder()
    {
        var m = NewMgr();
        var a = OpenOne(m, "k1");
        var b = OpenOne(m, "k2");

        Assert.Equal(2, m.Windows.Count);
        Assert.Same(b, m.Active);
        Assert.True(b.IsActive);
        Assert.False(a.IsActive);
        Assert.True(b.ZOrder > a.ZOrder); // newest on top
    }

    [Fact]
    public void Windows_keeps_stable_open_order_not_zorder_order()
    {
        var m = NewMgr();
        var a = OpenOne(m, "k1");
        var b = OpenOne(m, "k2");
        m.Focus(a.Id); // a now has the highest z-order

        // List order is still open-order (a, b) even though a is on top.
        Assert.Equal(new[] { a.Id, b.Id }, m.Windows.Select(w => w.Id));
        Assert.True(a.ZOrder > b.ZOrder);
    }

    // ---- Open (dedupe) ---------------------------------------------------

    [Fact]
    public void Open_duplicate_key_focuses_existing_and_does_not_add()
    {
        var m = NewMgr();
        var first = OpenOne(m, "same");
        OpenOne(m, "other");

        var again = m.Open("same", "T", null, Body);

        Assert.Equal(2, m.Windows.Count);   // no new window
        Assert.Same(first, again);          // returned the existing one
        Assert.Same(first, m.Active);       // focused
        Assert.True(first.ZOrder > m.Windows.Single(w => w.Key == "other").ZOrder);
    }

    [Fact]
    public void Open_duplicate_key_while_minimized_restores_it()
    {
        var m = NewMgr();
        var w = OpenOne(m, "dup");
        m.Minimize(w.Id);
        Assert.Equal(WindowState.Minimized, w.State);

        var again = m.Open("dup", "T", null, Body);

        Assert.Same(w, again);
        Assert.Equal(WindowState.Normal, w.State);
        Assert.True(w.IsActive);
    }

    // ---- Soft cap --------------------------------------------------------

    [Fact]
    public void SoftCap_is_eight()
    {
        Assert.Equal(8, NewMgr().SoftCap);
    }

    [Fact]
    public void Open_ninth_returns_null_and_does_not_close_or_add()
    {
        var m = NewMgr();
        for (var i = 0; i < 8; i++) OpenOne(m, $"k{i}");
        Assert.Equal(8, m.Windows.Count);
        var topBefore = m.Active!.Id;

        var overflow = m.Open("k8", "T", null, Body);

        Assert.Null(overflow);              // blocked
        Assert.Equal(8, m.Windows.Count);   // nothing auto-closed
        Assert.Equal(topBefore, m.Active!.Id); // untouched
    }

    // ---- Focus -----------------------------------------------------------

    [Fact]
    public void Focus_minimized_restores_to_normal_and_tops_zorder()
    {
        var m = NewMgr();
        var a = OpenOne(m, "a");
        var b = OpenOne(m, "b");
        m.Minimize(a.Id);

        m.Focus(a.Id);

        Assert.Equal(WindowState.Normal, a.State);
        Assert.True(a.IsActive);
        Assert.False(b.IsActive);
        Assert.True(a.ZOrder > b.ZOrder);
    }

    // ---- Minimize --------------------------------------------------------

    [Fact]
    public void Minimize_active_moves_active_to_next_highest_zorder()
    {
        var m = NewMgr();
        var a = OpenOne(m, "a");
        var b = OpenOne(m, "b"); // b active + top
        Assert.Same(b, m.Active);

        m.Minimize(b.Id);

        Assert.Equal(WindowState.Minimized, b.State);
        Assert.False(b.IsActive);
        Assert.Same(a, m.Active);   // fell through to the next non-minimized
        Assert.True(a.IsActive);
    }

    [Fact]
    public void Minimize_last_active_leaves_no_active()
    {
        var m = NewMgr();
        var only = OpenOne(m, "solo");
        m.Minimize(only.Id);

        Assert.Null(m.Active);
        Assert.False(only.IsActive);
    }

    // ---- Maximize / Restore ---------------------------------------------

    [Fact]
    public void Maximize_sets_maximized_and_focuses()
    {
        var m = NewMgr();
        var a = OpenOne(m, "a");
        var b = OpenOne(m, "b");

        m.Maximize(a.Id);

        Assert.Equal(WindowState.Maximized, a.State);
        Assert.True(a.IsActive);
        Assert.False(b.IsActive);
        Assert.True(a.ZOrder > b.ZOrder);
    }

    [Fact]
    public void Restore_from_maximized_returns_to_normal()
    {
        var m = NewMgr();
        var a = OpenOne(m, "a");
        m.Maximize(a.Id);

        m.Restore(a.Id);

        Assert.Equal(WindowState.Normal, a.State);
    }

    [Fact]
    public void Restore_from_minimized_returns_to_normal()
    {
        var m = NewMgr();
        var a = OpenOne(m, "a");
        m.Minimize(a.Id);

        m.Restore(a.Id);

        Assert.Equal(WindowState.Normal, a.State);
    }

    // ---- Close -----------------------------------------------------------

    [Fact]
    public void Close_removes_and_promotes_next_active()
    {
        var m = NewMgr();
        var a = OpenOne(m, "a");
        var b = OpenOne(m, "b"); // active
        m.Close(b.Id);

        Assert.Single(m.Windows);
        Assert.Same(a, m.Active);
        Assert.True(a.IsActive);
    }

    [Fact]
    public void Close_non_active_keeps_active_unchanged()
    {
        var m = NewMgr();
        var a = OpenOne(m, "a");
        var b = OpenOne(m, "b"); // active
        m.Close(a.Id);

        Assert.Single(m.Windows);
        Assert.Same(b, m.Active);
    }

    [Fact]
    public void Close_last_window_leaves_no_active()
    {
        var m = NewMgr();
        var a = OpenOne(m, "a");
        m.Close(a.Id);

        Assert.Empty(m.Windows);
        Assert.Null(m.Active);
    }

    // ---- Changed event ---------------------------------------------------

    [Fact]
    public void Changed_fires_exactly_once_per_mutation()
    {
        var m = NewMgr();
        var count = 0;
        m.Changed += () => count++;

        var a = OpenOne(m, "a");            // 1
        var b = OpenOne(m, "b");            // 2
        m.Open("a", "T", null, Body);       // 3 (dedupe-focus still a mutation)
        m.Focus(b.Id);                      // 4
        m.Minimize(b.Id);                   // 5
        m.Maximize(a.Id);                   // 6
        m.Restore(a.Id);                    // 7
        m.Close(a.Id);                      // 8

        Assert.Equal(8, count);
    }

    [Fact]
    public void Changed_does_not_fire_for_unknown_id()
    {
        var m = NewMgr();
        OpenOne(m, "a");
        var count = 0;
        m.Changed += () => count++;

        m.Focus("nope");
        m.Minimize("nope");
        m.Maximize("nope");
        m.Restore("nope");
        m.Close("nope");

        Assert.Equal(0, count);
    }

    // ---- Guards ----------------------------------------------------------

    [Fact]
    public void Open_rejects_empty_key()
    {
        var m = NewMgr();
        Assert.Throws<ArgumentException>(() => m.Open("", "T", null, Body));
    }

    [Fact]
    public void Parameters_and_icon_flow_through_to_window()
    {
        var m = NewMgr();
        var pars = new Dictionary<string, object> { ["WoNo"] = "WO-26-3685" };
        var w = m.Open("trace:WO-26-3685", "Trace", "🔍", Body, pars)!;

        Assert.Equal("🔍", w.Icon);
        Assert.Same(pars, w.Parameters);
        Assert.Equal("trace:WO-26-3685", w.Key);
    }
}
