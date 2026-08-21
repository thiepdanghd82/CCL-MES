using CCL.MES.Hybrid.Client.Windows;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace CCL.MES.Hybrid.Razor.Tests._Support;

/// <summary>
/// Keep-alive probe (pure-C# component — the Razor test project compiles .cs
/// only). Holds mutable instance state (<see cref="Count"/>) that survives ONLY
/// if the instance is not destroyed/recreated — used to prove the window-layer
/// keeps a window's body mounted across a route/@Body swap. Also reads the
/// cascaded <see cref="WindowContext"/> so a test can assert IsActive flows down.
/// </summary>
public sealed class CounterProbe : ComponentBase
{
    [CascadingParameter] public WindowContext? Win { get; set; }

    /// <summary>Live counter, readable by a test without DOM scraping.</summary>
    public int Count { get; private set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "counter-probe");
        builder.AddAttribute(2, "data-count", Count);

        builder.OpenElement(3, "span");
        builder.AddAttribute(4, "class", "cp-count");
        builder.AddContent(5, Count);
        builder.CloseElement();

        builder.OpenElement(6, "span");
        builder.AddAttribute(7, "class", "cp-active");
        builder.AddContent(8, Win?.IsActive.ToString() ?? "null");
        builder.CloseElement();

        builder.OpenElement(9, "button");
        builder.AddAttribute(10, "type", "button");
        builder.AddAttribute(11, "class", "cp-inc");
        builder.AddAttribute(12, "onclick",
            EventCallback.Factory.Create<MouseEventArgs>(this, () => Count++));
        builder.AddContent(13, "inc");
        builder.CloseElement();

        builder.CloseElement();
    }
}
