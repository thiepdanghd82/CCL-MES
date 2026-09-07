using Microsoft.EntityFrameworkCore;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>Nạp ledger Excel → IqcInspections (identity + Pass/Fail).</summary>
public sealed class IqcHistoryLedgerImportTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public IqcHistoryLedgerImportTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    [Theory]
    [InlineData("OK", QcResult.Pass)]
    [InlineData("ng", QcResult.Fail)]
    [InlineData("", null)]
    public void ParseJudgment_maps_ok_ng(string raw, QcResult? expected)
        => Assert.Equal(expected, IqcHistoryLedgerImportService.ParseJudgment(raw));

    [Fact]
    public async Task Import_is_idempotent_and_skips_pcs_continuation()
    {
        await using var db = _fx.NewContext();
        var rows = new List<IqcHistoryLedgerRow>
        {
            new("Roll", 3, 1, new DateTime(2026, 1, 2), "NCC", "3001", "MOTHER", "Tape", "PO1", 10, "rolls", "OK", "Hải"),
            new("PCS", 10, 5, new DateTime(2026, 1, 5), "NCC", "3002", null, "Panel", "PO2", 80, "pcs", "NG", "Hải"),
            new("PCS", 11, null, new DateTime(2026, 1, 5), "NCC", "3002", null, "Panel", "PO2", 80, "pcs", "OK", "Hải"),
            new("Chem", 20, 1, new DateTime(2026, 1, 6), "NCC", "3012", null, "Ink", "PO3", 15, "kg", "OK", "Hải"),
        };

        var svc = new IqcHistoryLedgerImportService(db);
        var first = await svc.ImportAsync(rows, "test", commit: true);
        Assert.Equal(3, first.Inserted);
        Assert.Equal(1, first.RowsSkippedPcsContinuation);
        Assert.Equal(3, await db.IqcInspections.CountAsync());

        var second = await svc.ImportAsync(rows, "test", commit: true);
        Assert.Equal(0, second.Inserted);
        Assert.Equal(3, second.AlreadyPresent);
        Assert.Equal(3, await db.IqcInspections.CountAsync());

        var hist = await new IqcService(db,
            new InMemoryAuditWriter(),
            new MaterialLotScanService(db, new InMemoryAuditWriter(),
                Microsoft.Extensions.Options.Options.Create(new MaterialLotOptions())))
            .ListHistoryAsync("Roll", null, null, null, 1, 50);
        Assert.Equal(1, hist.Total);
        Assert.Equal("XLS-ROLL-00003", hist.Items[0].ReceiptNo);
    }
}
