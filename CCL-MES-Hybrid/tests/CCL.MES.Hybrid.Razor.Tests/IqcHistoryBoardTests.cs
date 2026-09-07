using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Razor.Shared.Iqc;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Quality;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>Tab History — chip sheet Excel + list Pass/Fail.</summary>
public sealed class IqcHistoryBoardTests : TestContext
{
    private readonly RecordingApi _api = new();
    private readonly StubAuthSession _session = new();

    private void Wire()
    {
        _session.SetUser("qc-user", "QC");
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddSingleton<IAuthSession>(_session);
        Services.AddI18n();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("qc-user");
        auth.SetRoles("QC");
    }

    [Fact]
    public void Lists_approved_rows_and_sheet_chips()
    {
        Wire();
        _api.ListIqcHistoryImpl = (_, _, _, _, _, _) => Task.FromResult(new IqcHistoryListResponse
        {
            Page = 1, PageSize = 50, Total = 1,
            Items = new List<IqcHistoryListItem>
            {
                new()
                {
                    Id = 9, ReceiptNo = "IQC-R9", Sheet = "Roll", Group = "Materials",
                    MaterialCategory = "Roll", Result = "Pass",
                    ReceivedDate = new DateTime(2026, 3, 1),
                    ApprovedAt = new DateTime(2026, 3, 2), ApprovedBy = "qc-user",
                    Quantity = 2, Uom = "rolls",
                },
            },
        });

        var cut = RenderComponent<IqcHistoryBoard>(p => p.Add(x => x.DebounceMs, 0));

        Assert.NotNull(cut.Find("[data-testid=iqc-history]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-history-sheet-roll]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-history-row-9]"));
        Assert.Single(_api.ListIqcHistoryCalls);
    }

    [Fact]
    public void Sheet_chip_reloads_with_filter()
    {
        Wire();
        _api.ListIqcHistoryImpl = (_, _, _, _, _, _) =>
            Task.FromResult(new IqcHistoryListResponse { Page = 1, PageSize = 50, Total = 0 });

        var cut = RenderComponent<IqcHistoryBoard>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-history-sheet-chem]").Click();

        Assert.Equal(2, _api.ListIqcHistoryCalls.Count);
        Assert.Equal("Chem", _api.ListIqcHistoryCalls[1].Sheet);
    }

    [Fact]
    public void Date_inputs_reload_with_from_to()
    {
        Wire();
        _api.ListIqcHistoryImpl = (_, _, _, _, _, _) =>
            Task.FromResult(new IqcHistoryListResponse { Page = 1, PageSize = 50, Total = 0 });

        var cut = RenderComponent<IqcHistoryBoard>(p => p.Add(x => x.DebounceMs, 0));
        cut.Find("[data-testid=iqc-history-from]").Change("2026-01-01");
        cut.Find("[data-testid=iqc-history-to]").Change("2026-01-31");

        Assert.True(_api.ListIqcHistoryCalls.Count >= 3);
        var last = _api.ListIqcHistoryCalls[^1];
        Assert.Equal(new DateTime(2026, 1, 1), last.From);
        Assert.Equal(new DateTime(2026, 2, 1), last.To);
    }
}
