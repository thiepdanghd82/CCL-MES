using System.Collections.Generic;
using System.Linq;
using Bunit;
using CCL.MES.Hybrid.Razor.Shared;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// The generic row-action menu: renders items/dividers/disabled, invokes an
/// item's OnClick then closes, and closes on the scrim. Positioning + a11y
/// roving are JS (context-menu.js) and covered by the manual checklist.
/// </summary>
public sealed class RowContextMenuTests : TestContext
{
    public RowContextMenuTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private static IReadOnlyList<ContextMenuItem> Items(TestContext ctx, System.Action onCopy, System.Action onDelete)
        => new[]
        {
            new ContextMenuItem { Label = "Copy", Icon = "⎘", OnClick = EventCallback.Factory.Create(ctx, onCopy) },
            new ContextMenuItem { Label = "Nope", Disabled = true, OnClick = EventCallback.Factory.Create(ctx, () => { }) },
            ContextMenuItem.Divider,
            new ContextMenuItem { Label = "Delete", Icon = "🗑", Danger = true, OnClick = EventCallback.Factory.Create(ctx, onDelete) },
        };

    [Fact]
    public void Renders_items_divider_and_disabled()
    {
        var cut = RenderComponent<RowContextMenu>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Items, Items(this, () => { }, () => { })));

        Assert.Equal(3, cut.FindAll(".row-ctx-item").Count);          // 3 buttons (divider excluded)
        Assert.Single(cut.FindAll(".row-ctx-divider"));
        Assert.Single(cut.FindAll(".row-ctx-item-danger"));           // Delete
        Assert.True(cut.FindAll(".row-ctx-item")[1].HasAttribute("disabled"));   // "Nope"
        Assert.Equal("menu", cut.Find(".row-ctx-menu").GetAttribute("role"));
    }

    [Fact]
    public void Closed_renders_nothing()
    {
        var cut = RenderComponent<RowContextMenu>(p => p.Add(x => x.Open, false));
        Assert.Empty(cut.FindAll(".row-ctx-menu"));
    }

    [Fact]
    public void Selecting_item_invokes_onclick_then_closes()
    {
        var copied = false; var closed = false;
        var cut = RenderComponent<RowContextMenu>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Items, Items(this, () => copied = true, () => { }))
            .Add(x => x.OnClose, () => closed = true));

        cut.FindAll(".row-ctx-item").First(b => b.TextContent.Contains("Copy")).Click();
        Assert.True(copied);
        Assert.True(closed);
    }

    [Fact]
    public void Scrim_click_closes()
    {
        var closed = false;
        var cut = RenderComponent<RowContextMenu>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Items, Items(this, () => { }, () => { }))
            .Add(x => x.OnClose, () => closed = true));

        cut.Find(".row-ctx-scrim").Click();
        Assert.True(closed);
    }
}
