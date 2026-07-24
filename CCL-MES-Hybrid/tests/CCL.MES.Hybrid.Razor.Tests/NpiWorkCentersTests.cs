using System.Linq;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Files;
using CCL.MES.Hybrid.Client.Grid;
using CCL.MES.Hybrid.Client.Npi;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Tests._Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// bUnit for the Work Centers write surface: Add/Edit/Import buttons are
/// Admin-only (AuthorizeView), the add modal is a centred Modal (L34), and
/// Save calls the create endpoint.
/// </summary>
public sealed class NpiWorkCentersTests : TestContext
{
    private readonly RecordingApi _api = new();

    private TestAuthorizationContext Auth(string role)
    {
        _api.WorkCenters = new NpiPagedRaw<NpiWorkCenter>
        {
            Items = new[] { new NpiWorkCenter { Id = 5, Code = "PR-01", Description = "Press", Active = true } },
            Total = 1, Page = 1, PageSize = 20,
        };
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddI18n();
        Services.AddSingleton<IGridPreferenceStore>(new InMemoryGridPreferenceStore());
        Services.AddSingleton<IFilePickerService>(new StubFilePickerService());
        JSInterop.Mode = JSRuntimeMode.Loose;
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized(role.ToLowerInvariant() + "-user");
        auth.SetRoles(role);
        return auth;
    }

    [Fact]
    public void Admin_sees_write_buttons_and_kebab_no_actions_column()
    {
        Auth("Admin");
        var cut = RenderComponent<NpiWorkCenters>();
        cut.WaitForState(() => cut.FindAll("tbody tr").Count > 0);

        var m = cut.Markup;
        Assert.Contains("+ Thêm", m);
        Assert.Contains("⭱ Nhập", m);
        Assert.Contains("⭳ Xuất CSV", m);
        Assert.Single(cut.FindAll("button.row-kebab"));      // ⋯ entry point
        Assert.DoesNotContain(">Hành động<", m);             // no inline Actions column
    }

    [Fact]
    public void Non_admin_sees_export_but_no_kebab_or_menu()
    {
        Auth("QC");
        var cut = RenderComponent<NpiWorkCenters>();
        cut.WaitForState(() => cut.FindAll("tbody tr").Count > 0);

        var m = cut.Markup;
        Assert.Contains("⭳ Xuất CSV", m);      // export open to viewers
        Assert.DoesNotContain("+ Thêm", m);
        Assert.Empty(cut.FindAll("button.row-kebab"));

        // Right-click a row → still nothing (no permitted items).
        cut.Find("tbody tr").TriggerEvent("oncontextmenu", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        Assert.Empty(cut.FindAll(".row-ctx-menu"));
    }

    [Fact]
    public void Right_click_and_kebab_open_the_same_menu()
    {
        Auth("Admin");
        var cut = RenderComponent<NpiWorkCenters>();
        cut.WaitForState(() => cut.FindAll("tbody tr").Count > 0);

        // Right-click.
        cut.Find("tbody tr").TriggerEvent("oncontextmenu", new Microsoft.AspNetCore.Components.Web.MouseEventArgs { ClientX = 30, ClientY = 40 });
        var menu = cut.Find(".row-ctx-menu");
        var labels = cut.FindAll(".row-ctx-menu .row-ctx-label").Select(l => l.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "Sao chép", "Sửa", "Xoá" }, labels);

        // Scrim closes it; kebab re-opens the same menu.
        cut.Find(".row-ctx-scrim").Click();
        Assert.Empty(cut.FindAll(".row-ctx-menu"));
        cut.Find("button.row-kebab").Click();
        Assert.Single(cut.FindAll(".row-ctx-menu"));
    }

    [Fact]
    public void Menu_edit_opens_modal_and_copy_prefills_blank_code()
    {
        Auth("Admin");
        var cut = RenderComponent<NpiWorkCenters>();
        cut.WaitForState(() => cut.FindAll("tbody tr").Count > 0);

        // Edit → centred modal titled with the row code.
        cut.Find("button.row-kebab").Click();
        cut.FindAll(".row-ctx-item").First(b => b.TextContent.Contains("Sửa")).Click();
        Assert.Contains("Sửa trung tâm sản xuất — PR-01", cut.Markup);

        // Copy → Add modal, Code field blank (must enter a new unique code).
        cut.Find(".op-btn").TextContent.Contains("Huỷ");
        cut.FindAll(".op-btn").First(b => b.TextContent.Contains("Huỷ")).Click();
        cut.Find("button.row-kebab").Click();
        cut.FindAll(".row-ctx-item").First(b => b.TextContent.Contains("Sao chép")).Click();
        Assert.Contains("Thêm trung tâm sản xuất", cut.Markup);
        Assert.Equal("", cut.Find(".wc-form input").GetAttribute("value") ?? "");
    }

    [Fact]
    public void Add_opens_centred_modal_and_save_calls_create()
    {
        Auth("Admin");
        NpiWorkCenterUpsert? sent = null;
        _api.CreateWorkCenterImpl = body => { sent = body; return new NpiWorkCenter { Id = 9, Code = body.Code }; };

        var cut = RenderComponent<NpiWorkCenters>();
        cut.WaitForState(() => cut.FindAll("tbody tr").Count > 0);

        cut.FindAll("button").First(b => b.TextContent.Contains("+ Thêm")).Click();

        // Centred Modal (NOT a floating window — transactional form, L34).
        Assert.Single(cut.FindAll(".modal-scrim"));
        Assert.Empty(cut.FindAll(".trace-win"));
        Assert.Contains("Thêm trung tâm sản xuất", cut.Markup);

        cut.Find(".wc-form input").Change("PR-99");        // Code field
        cut.FindAll(".op-btn").First(b => b.TextContent.Contains("Lưu")).Click();

        cut.WaitForAssertion(() => Assert.NotNull(sent));
        Assert.Equal("PR-99", sent!.Code);
    }
}
