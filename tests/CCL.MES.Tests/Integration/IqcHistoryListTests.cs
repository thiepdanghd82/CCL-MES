using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// Sổ lịch sử IQC — chỉ Pass/Fail; chip sheet map Excel Roll/PCS/Chem/Tool.
/// </summary>
public sealed class IqcHistoryListTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public IqcHistoryListTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    private static IqcService Svc(MesDbContext db)
    {
        var audit = new InMemoryAuditWriter();
        var lots = new MaterialLotScanService(
            db, audit, Microsoft.Extensions.Options.Options.Create(new MaterialLotOptions()));
        return new IqcService(db, audit, lots);
    }

    [Theory]
    [InlineData(IqcGroup.Materials, IqcMaterialCategory.Roll, "Roll")]
    [InlineData(IqcGroup.Materials, IqcMaterialCategory.Pcs, "PCS")]
    [InlineData(IqcGroup.Chemical, IqcMaterialCategory.Any, "Chem")]
    [InlineData(IqcGroup.Tools, IqcMaterialCategory.Any, "Tool")]
    [InlineData(IqcGroup.Materials, IqcMaterialCategory.Any, "Materials")]
    public void ToExcelSheet_maps_group_and_category(string group, IqcMaterialCategory cat, string expected)
        => Assert.Equal(expected, IqcService.ToExcelSheet(group, cat));

    [Fact]
    public async Task ListHistory_excludes_pending_and_filters_sheet()
    {
        await using var db = _fx.NewContext();
        db.IqcInspections.AddRange(
            new IqcInspection
            {
                PartNo = "P-ROLL", ReceiptNo = "IQC-R1", Group = IqcGroup.Materials,
                MaterialCategory = IqcMaterialCategory.Roll, Result = QcResult.Pass,
                ReceivedDate = new DateTime(2026, 1, 1), ApprovedAt = new DateTime(2026, 1, 2),
                ApprovedBy = "qc", Quantity = 1, UomQty = "rolls",
            },
            new IqcInspection
            {
                PartNo = "P-PCS", ReceiptNo = "IQC-P1", Group = IqcGroup.Materials,
                MaterialCategory = IqcMaterialCategory.Pcs, Result = QcResult.Fail,
                ReceivedDate = new DateTime(2026, 1, 3), ApprovedAt = new DateTime(2026, 1, 4),
                ApprovedBy = "qc", Quantity = 10, UomQty = "pcs",
            },
            new IqcInspection
            {
                PartNo = "P-PEND", ReceiptNo = "IQC-X", Group = IqcGroup.Materials,
                MaterialCategory = IqcMaterialCategory.Roll, Result = QcResult.Pending,
                ReceivedDate = new DateTime(2026, 1, 5), Quantity = 1,
            },
            new IqcInspection
            {
                PartNo = "P-CHEM", ReceiptNo = "IQC-C1", Group = IqcGroup.Chemical,
                MaterialCategory = IqcMaterialCategory.Chem, Result = QcResult.Pass,
                ReceivedDate = new DateTime(2026, 1, 6), ApprovedAt = new DateTime(2026, 1, 7),
                ApprovedBy = "qc", Quantity = 5, UomQty = "kg",
            });
        await db.SaveChangesAsync();

        var all = await Svc(db).ListHistoryAsync(null, null, null, null, 1, 50);
        Assert.Equal(3, all.Total);
        Assert.DoesNotContain(all.Items, x => x.Result == "Pending");

        var roll = await Svc(db).ListHistoryAsync("Roll", null, null, null, 1, 50);
        Assert.Equal(1, roll.Total);
        Assert.Equal("Roll", roll.Items[0].Sheet);
        Assert.Equal("IQC-R1", roll.Items[0].ReceiptNo);

        var chem = await Svc(db).ListHistoryAsync("Chem", null, null, null, 1, 50);
        Assert.Equal(1, chem.Total);
        Assert.Equal("Chem", chem.Items[0].Sheet);
    }
}
