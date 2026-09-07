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

    [Fact]
    public async Task Enrich_chem_materialises_CD_HSF_and_packaging()
    {
        await using var db = _fx.NewContext();
        var checks = new IqcHistoryLedgerChecks(
            WarehouseInDate: "2026-01-05",
            ExpiryText: "1 Năm từ ngày nhập kho",
            Pefc: null, PefcLevel: null,
            PackagingSpec: "1",
            PackagingPass: true,
            PackagingInspector: null,
            VisualSampleQty: 3,
            VisualDefects:
            [
                new IqcLedgerDefectCell("CD-01", 0),
                new IqcLedgerDefectCell("CD-02", 0),
                new IqcLedgerDefectCell("CD-03", 1),
            ],
            VisualPass: false,
            VisualInspector: "Hải",
            WidthNominal: null, WidthLow: null, WidthUp: null,
            WidthSamples: Array.Empty<double?>(), WidthSampleTexts: Array.Empty<string?>(), WidthPass: null,
            LengthNominal: null, LengthLow: null, LengthUp: null,
            LengthSamples: Array.Empty<double?>(), LengthSampleTexts: Array.Empty<string?>(),
            LengthPass: null, LengthSpec: null,
            ThicknessSpec: null, ThicknessSamples: Array.Empty<double?>(), ThicknessPass: null,
            DimensionInspector: null,
            FuncSpec: null, FuncPass: null, FuncInspector: null,
            LabSpec: null, LabSheets: Array.Empty<double?>(), LabPass: null, LabInspector: null,
            HsfPass: true, CoaPass: true);

        var rows = new List<IqcHistoryLedgerRow>
        {
            new("Chem", 3, 1, new DateTime(2026, 1, 5), "NCC", "30120017", null,
                "OPAQUE WHITE", "PO1", 15, "kg", "NG", "Hải", Checks: checks),
        };

        var svc = new IqcHistoryLedgerImportService(db);
        var r = await svc.ImportAsync(rows, "test", commit: true, enrichDetails: true);
        Assert.Equal(1, r.Inserted);
        Assert.Equal(1, r.DetailsUpserted);

        var keys = await db.IqcResultDetails.Select(d => d.ItemKey).OrderBy(k => k).ToListAsync();
        Assert.Contains("NQ-01", keys);
        Assert.Contains("NQ-06", keys);
        Assert.Contains("CD-01", keys);
        Assert.Contains("CD-03", keys);
        Assert.Contains("MT-02", keys);
        Assert.Contains("DOC-COA", keys);

        var cd03 = await db.IqcResultDetails.SingleAsync(d => d.ItemKey == "CD-03");
        Assert.False(cd03.Pass);
        var hsf = await db.IqcResultDetails.SingleAsync(d => d.ItemKey == "MT-02");
        Assert.True(hsf.Pass);
    }

    [Fact]
    public async Task Enrich_tools_materialises_TD_HSF_and_label()
    {
        await using var db = _fx.NewContext();
        var checks = new IqcHistoryLedgerChecks(
            WarehouseInDate: "2025-12-24",
            ExpiryText: null,
            Pefc: null, PefcLevel: null,
            PackagingSpec: null,
            PackagingPass: true,
            PackagingInspector: null,
            VisualSampleQty: 1,
            VisualDefects:
            [
                new IqcLedgerDefectCell("TD-01", 0),
                new IqcLedgerDefectCell("TD-02", 0),
                new IqcLedgerDefectCell("TD-03", 2),
                new IqcLedgerDefectCell("TD-04", 0),
                new IqcLedgerDefectCell("TD-05", 0),
            ],
            VisualPass: false,
            VisualInspector: "Hải",
            WidthNominal: null, WidthLow: null, WidthUp: null,
            WidthSamples: Array.Empty<double?>(), WidthSampleTexts: Array.Empty<string?>(), WidthPass: null,
            LengthNominal: null, LengthLow: null, LengthUp: null,
            LengthSamples: Array.Empty<double?>(), LengthSampleTexts: Array.Empty<string?>(),
            LengthPass: null, LengthSpec: null,
            ThicknessSpec: null, ThicknessSamples: Array.Empty<double?>(), ThicknessPass: null,
            DimensionInspector: null,
            FuncSpec: null, FuncPass: null, FuncInspector: null,
            LabSpec: null, LabSheets: Array.Empty<double?>(), LabPass: null, LabInspector: null,
            HsfPass: true, CoaPass: null);

        var rows = new List<IqcHistoryLedgerRow>
        {
            new("Tool", 3, 1, new DateTime(2026, 1, 3), "NCC", "CT4344", null,
                "Cutter VF00044P", "VN2512148", 1, "ea", "NG", "Hải", Checks: checks),
        };

        var svc = new IqcHistoryLedgerImportService(db);
        var r = await svc.ImportAsync(rows, "test", commit: true, enrichDetails: true);
        Assert.Equal(1, r.Inserted);
        Assert.Equal(1, r.DetailsUpserted);

        var keys = await db.IqcResultDetails.Select(d => d.ItemKey).OrderBy(k => k).ToListAsync();
        Assert.Contains("NQ-01", keys);
        Assert.DoesNotContain("NQ-06", keys); // Tool chỉ Tem, không đóng gói
        Assert.Contains("TD-01", keys);
        Assert.Contains("TD-03", keys);
        Assert.Contains("TD-05", keys);
        Assert.Contains("MT-02", keys);
        Assert.DoesNotContain("DOC-COA", keys);

        var td03 = await db.IqcResultDetails.SingleAsync(d => d.ItemKey == "TD-03");
        Assert.Equal(2, td03.DefectCount);
        Assert.False(td03.Pass);
        var hsf = await db.IqcResultDetails.SingleAsync(d => d.ItemKey == "MT-02");
        Assert.True(hsf.Pass);

        var again = await svc.ImportAsync(rows, "test", commit: true, enrichDetails: true);
        Assert.Equal(0, again.Inserted);
        Assert.Equal(1, again.AlreadyPresent);
    }
}
