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
/// QC Library smart platform — Label/Silk sub-tabs, grouped tick-box grid,
/// tick edit → PUT, right-click menu (Info/Copy/Delete), Add new. RBAC: QC can
/// edit; Engineer is read-only (no tick edit / no add).
/// </summary>
public sealed class QcLibrarySmartTests : TestContext
{
    private readonly RecordingApi _api = new();

    private static CheckLibraryItemDto Item(string id, string line, string grp, bool flexo = false) => new()
    {
        ItemId = id, ProcessLine = line, GroupLabel = grp, Code = id,
        ItemVi = $"Nội dung {id}", Flexo = flexo, Ipqc = true, Active = true,
    };

    private IRenderedComponent<QcLibrary> Render(string role = "QC")
    {
        _api.GetCheckLibraryImpl = (line, _, _, _) => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CheckLibraryItemDto>>(
            new[] { Item("LBL-A1", "LABEL", "A·Ngoại quan", flexo: true), Item("LBL-B1", "LABEL", "B·Kích thước") }
                .Where(i => line == null || i.ProcessLine == line).ToList());
        _api.GetCheckLibraryLinesImpl = _ => System.Threading.Tasks.Task.FromResult<IReadOnlyList<CheckLibraryLineDto>>(
            new[] { new CheckLibraryLineDto { ProcessLine = "LABEL", Count = 34 }, new CheckLibraryLineDto { ProcessLine = "SILK", Count = 25 } });

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
    public void Renders_label_and_silk_subtabs_with_counts()
    {
        var cut = Render();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='qclib-tab-label']")));
        Assert.Single(cut.FindAll("[data-testid='qclib-tab-label']"));
        Assert.Single(cut.FindAll("[data-testid='qclib-tab-silk']"));
        Assert.Contains("34", cut.Find("[data-testid='qclib-tab-label']").TextContent);
        // Default LABEL tab selected.
        Assert.Contains("qclib-tab-on", cut.Find("[data-testid='qclib-tab-label']").GetAttribute("class"));
    }

    [Fact]
    public void Groups_rows_and_renders_16_tick_columns()
    {
        var cut = Render();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='qclib-row-LBL-A1']")));
        // Two group section rows (A + B) inside ONE table.
        Assert.Single(cut.FindAll("[data-testid='qclib-group-A·Ngoại quan']"));
        Assert.Single(cut.FindAll("[data-testid='qclib-group-B·Kích thước']"));
        Assert.Single(cut.FindAll("[data-testid='qclib-grid']"));   // single table
        // 16 tick checkboxes on the A1 row.
        var row = cut.Find("[data-testid='qclib-row-LBL-A1']");
        Assert.Equal(16, row.QuerySelectorAll("input[type=checkbox]").Length);
    }

    [Fact]
    public void Header_is_banded_print_cut_stage_over_16_columns()
    {
        var cut = Render();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".qclib-band")));
        // Band row spans: PRINT=5, CUT=8, STAGE=3 over the 16 tick columns.
        Assert.Equal("5", cut.Find(".qclib-band-print").GetAttribute("colspan"));
        Assert.Equal("8", cut.Find(".qclib-band-cut").GetAttribute("colspan"));
        Assert.Equal("3", cut.Find(".qclib-band-stage").GetAttribute("colspan"));
        Assert.Equal(16, cut.FindAll(".qclib-cols th").Count);   // one header row of 16 labels (not per-group)
    }

    [Fact]
    public void Toggling_a_tick_puts_upsert()
    {
        var cut = Render();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='qclib-tick-LBL-A1-RDC']")));

        cut.Find("[data-testid='qclib-tick-LBL-A1-RDC']").Change(true);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(_api.UpsertCheckLibraryCalls, c => c.ItemId == "LBL-A1" && c.Dto.Rdc);
        });
    }

    [Fact]
    public void Right_click_menu_has_info_copy_delete_for_editor()
    {
        var cut = Render("QC");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='qclib-row-LBL-A1']")));

        cut.Find("[data-testid='qclib-row-LBL-A1'] .row-kebab").Click();

        var menu = cut.Markup;   // default language = Vietnamese
        Assert.Contains("Chi tiết", menu);
        Assert.Contains("Sao chép", menu);
        Assert.Contains("Xoá", menu);
    }

    [Fact]
    public void Engineer_is_read_only_no_ticks_editable_no_add()
    {
        var cut = Render("Engineer");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='qclib-row-LBL-A1']")));

        // No Add button, no kebab; tick checkboxes disabled.
        Assert.Empty(cut.FindAll("[data-testid='qclib-add']"));
        Assert.Empty(cut.FindAll(".row-kebab"));
        Assert.All(cut.FindAll("[data-testid='qclib-row-LBL-A1'] input[type=checkbox]"),
            cb => Assert.NotNull(cb.GetAttribute("disabled")));
    }

    [Fact]
    public void Add_new_saves_via_upsert()
    {
        var cut = Render("QC");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='qclib-add']")));

        cut.Find("[data-testid='qclib-add']").Click();
        cut.Find("[data-testid='qclib-add-itemid']").Change("LBL-Z9");
        cut.Find("[data-testid='qclib-add-save']").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains(_api.UpsertCheckLibraryCalls, c => c.ItemId == "LBL-Z9" && c.Dto.ProcessLine == "LABEL"));
    }

    [Fact]
    public void Switching_to_silk_reloads_filtered()
    {
        var cut = Render();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='qclib-tab-silk']")));

        cut.Find("[data-testid='qclib-tab-silk']").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("qclib-tab-on", cut.Find("[data-testid='qclib-tab-silk']").GetAttribute("class")));
    }
}
