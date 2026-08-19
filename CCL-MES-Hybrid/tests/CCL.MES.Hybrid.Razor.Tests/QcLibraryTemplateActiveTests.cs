using System.Linq;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Files;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.CheckLibrary;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// UI cho hai năng lực viết lại trên nền v5 (thay cho PR #127 đã đóng):
/// nút <b>Tải mẫu</b> và mục menu <b>Ngưng dùng / Dùng lại</b>.
/// </summary>
public sealed class QcLibraryTemplateActiveTests : TestContext
{
    private readonly RecordingApi _api = new();

    private static CheckLibraryItemDto Item(string id, bool active = true) => new()
    {
        ItemId = id, ProcessLine = "LABEL", GroupLabel = "A·Ngoại quan", Code = id,
        ItemVi = $"Nội dung {id}", Ipqc = true, Active = active,
    };

    private IRenderedComponent<QcLibrary> Render(string role = "QC", bool active = true)
    {
        _api.GetCheckLibraryImpl = (_, _, _, _) =>
            System.Threading.Tasks.Task.FromResult<IReadOnlyList<CheckLibraryItemDto>>(new[] { Item("LBL-A1", active) });
        _api.GetCheckLibraryLinesImpl = _ =>
            System.Threading.Tasks.Task.FromResult<IReadOnlyList<CheckLibraryLineDto>>(
                new[] { new CheckLibraryLineDto { ProcessLine = "LABEL", Count = 1 } });

        var session = new StubAuthSession();
        session.SetUser("u", role);
        Services.AddSingleton<IAuthSession>(session);
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddI18n();
        Services.AddSingleton<IFilePickerService>(new StubFilePickerService());
        Services.AddSingleton<IFileSaver>(new StubFileSaver());
        JSInterop.Mode = JSRuntimeMode.Loose;
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("u");
        auth.SetRoles(role);
        return RenderComponent<QcLibrary>();
    }

    [Fact]
    public void Template_button_calls_the_template_api_not_export()
    {
        // Hai nút cạnh nhau, rất dễ nối nhầm — test khoá đúng cái nào gọi cái nào.
        var cut = Render();
        cut.Find("[data-testid='qclib-template']").Click();

        var path = Assert.Single(_api.TemplateDownloadCalls);
        Assert.EndsWith("qc-library-template.csv", path);
    }

    [Fact]
    public void Active_row_offers_deactivate_and_calls_api_with_false()
    {
        var cut = Render(active: true);
        cut.Find("tbody tr .row-kebab").Click();

        Assert.Contains("Ngưng dùng", cut.Markup);
        cut.FindAll("[role='menuitem']").First(b => b.TextContent.Contains("Ngưng dùng")).Click();

        var call = Assert.Single(_api.SetActiveCalls);
        Assert.Equal("LBL-A1", call.ItemId);
        Assert.False(call.Active);
    }

    [Fact]
    public void Inactive_row_offers_reactivate_and_calls_api_with_true()
    {
        var cut = Render(active: false);
        cut.Find("tbody tr .row-kebab").Click();

        Assert.Contains("Dùng lại", cut.Markup);
        cut.FindAll("[role='menuitem']").First(b => b.TextContent.Contains("Dùng lại")).Click();

        Assert.True(Assert.Single(_api.SetActiveCalls).Active);
    }

    [Fact]
    public void Deactivate_is_not_styled_as_a_destructive_action()
    {
        // "Ngưng dùng" hoàn tác được, "Xoá" thì không — hai thứ không được trông
        // giống nhau, nếu không người dùng sẽ ngần ngại dùng cái an toàn.
        var cut = Render();
        cut.Find("tbody tr .row-kebab").Click();

        var deactivate = cut.FindAll("[role='menuitem']").First(b => b.TextContent.Contains("Ngưng dùng"));
        var delete = cut.FindAll("[role='menuitem']").First(b => b.TextContent.Contains("Xoá"));
        Assert.DoesNotContain("danger", deactivate.GetAttribute("class") ?? "");
        Assert.Contains("danger", delete.GetAttribute("class") ?? "");
    }

    [Fact]
    public void Read_only_role_sees_no_deactivate_item()
    {
        // RBAC-by-omission: Engineer chỉ đọc ⇒ không dựng mục sửa master data.
        var cut = Render(role: "Engineer");
        Assert.DoesNotContain("Ngưng dùng", cut.Markup);
        Assert.Empty(_api.SetActiveCalls);
    }
}
