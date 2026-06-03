using CCL.MES.Hybrid.Client.Grid;

namespace CCL.MES.Hybrid.Client.Tests.Grid;

/// <summary>
/// P10.5a — column visibility logic + persistence round-trips. Covers
/// the four states an operator can put a column into:
///   1. Default-visible + no override        → visible
///   2. Default-visible + hidden override    → hidden
///   3. Default-hidden + no override         → hidden
///   4. Default-hidden + show override       → visible
/// Plus the persistence contract: toggles write through to the store;
/// a fresh instance built from the same store re-reads the state.
/// </summary>
public sealed class GridColumnsTests
{
    private static IEnumerable<ColumnDef> SampleColumns() => new[]
    {
        new ColumnDef("a_visible_default", "A"),
        new ColumnDef("b_visible_default", "B"),
        new ColumnDef("c_hidden_default",  "C", DefaultVisible: false),
        new ColumnDef("d_hidden_default",  "D", DefaultVisible: false),
    };

    [Fact]
    public void Defaults_honor_DefaultVisible_flag()
    {
        var store = new InMemoryGridPreferenceStore();
        var cols = new GridColumns("grid1", SampleColumns(), store);

        Assert.True(cols.IsVisible("a_visible_default"));
        Assert.True(cols.IsVisible("b_visible_default"));
        Assert.False(cols.IsVisible("c_hidden_default"));
        Assert.False(cols.IsVisible("d_hidden_default"));
    }

    [Fact]
    public void Visible_returns_columns_in_declaration_order()
    {
        var store = new InMemoryGridPreferenceStore();
        var cols = new GridColumns("grid1", SampleColumns(), store);

        var visible = cols.Visible.Select(c => c.Id).ToList();
        Assert.Equal(new[] { "a_visible_default", "b_visible_default" }, visible);
    }

    [Fact]
    public void Toggle_default_visible_column_off_persists()
    {
        var store = new InMemoryGridPreferenceStore();
        var cols = new GridColumns("grid1", SampleColumns(), store);

        cols.Toggle("a_visible_default");

        Assert.False(cols.IsVisible("a_visible_default"));
        // A fresh instance backed by the same store reflects the saved state.
        var cols2 = new GridColumns("grid1", SampleColumns(), store);
        Assert.False(cols2.IsVisible("a_visible_default"));
        Assert.True(cols2.IsVisible("b_visible_default"));
    }

    [Fact]
    public void Toggle_default_hidden_column_on_persists()
    {
        var store = new InMemoryGridPreferenceStore();
        var cols = new GridColumns("grid1", SampleColumns(), store);

        cols.Toggle("c_hidden_default");

        Assert.True(cols.IsVisible("c_hidden_default"));
        var cols2 = new GridColumns("grid1", SampleColumns(), store);
        Assert.True(cols2.IsVisible("c_hidden_default"));
        // Other default-hidden column remains hidden.
        Assert.False(cols2.IsVisible("d_hidden_default"));
    }

    [Fact]
    public void Toggle_twice_returns_to_original_state()
    {
        var store = new InMemoryGridPreferenceStore();
        var cols = new GridColumns("grid1", SampleColumns(), store);

        cols.Toggle("a_visible_default");
        Assert.False(cols.IsVisible("a_visible_default"));
        cols.Toggle("a_visible_default");
        Assert.True(cols.IsVisible("a_visible_default"));

        cols.Toggle("c_hidden_default");
        Assert.True(cols.IsVisible("c_hidden_default"));
        cols.Toggle("c_hidden_default");
        Assert.False(cols.IsVisible("c_hidden_default"));
    }

    [Fact]
    public void ResetToDefaults_drops_every_override()
    {
        var store = new InMemoryGridPreferenceStore();
        var cols = new GridColumns("grid1", SampleColumns(), store);

        cols.Toggle("a_visible_default");  // hide a default-visible
        cols.Toggle("c_hidden_default");   // show a default-hidden
        Assert.False(cols.IsVisible("a_visible_default"));
        Assert.True(cols.IsVisible("c_hidden_default"));

        cols.ResetToDefaults();

        Assert.True(cols.IsVisible("a_visible_default"));
        Assert.False(cols.IsVisible("c_hidden_default"));
        // Persistence: fresh instance is clean.
        var cols2 = new GridColumns("grid1", SampleColumns(), store);
        Assert.True(cols2.IsVisible("a_visible_default"));
        Assert.False(cols2.IsVisible("c_hidden_default"));
    }

    [Fact]
    public void Different_grid_keys_have_independent_state()
    {
        var store = new InMemoryGridPreferenceStore();
        var colsA = new GridColumns("gridA", SampleColumns(), store);
        var colsB = new GridColumns("gridB", SampleColumns(), store);

        colsA.Toggle("a_visible_default");

        Assert.False(colsA.IsVisible("a_visible_default"));
        Assert.True(colsB.IsVisible("a_visible_default"));
    }

    [Fact]
    public void Unknown_column_id_is_ignored()
    {
        var store = new InMemoryGridPreferenceStore();
        var cols = new GridColumns("grid1", SampleColumns(), store);

        // Should not throw; should not mutate state.
        cols.Toggle("ghost_column");
        Assert.False(cols.IsVisible("ghost_column"));
        Assert.True(cols.IsVisible("a_visible_default"));
    }

    [Fact]
    public void Reload_picks_up_external_mutation()
    {
        var store = new InMemoryGridPreferenceStore();
        var cols = new GridColumns("grid1", SampleColumns(), store);

        // External actor hides a column directly via the store.
        store.SetHiddenColumns("grid1", new[] { "a_visible_default" });

        // Without reload, the existing instance shows stale state.
        Assert.True(cols.IsVisible("a_visible_default"));

        cols.Reload();
        Assert.False(cols.IsVisible("a_visible_default"));
    }

    [Fact]
    public void All_returns_declaration_order_unchanged_by_toggle()
    {
        var store = new InMemoryGridPreferenceStore();
        var cols = new GridColumns("grid1", SampleColumns(), store);

        cols.Toggle("b_visible_default");

        var all = cols.All.Select(c => c.Id).ToList();
        Assert.Equal(new[]
        {
            "a_visible_default", "b_visible_default",
            "c_hidden_default",  "d_hidden_default",
        }, all);
    }
}
