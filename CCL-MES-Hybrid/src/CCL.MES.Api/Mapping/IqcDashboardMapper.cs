using CCL.MES.Application.Services;
using CCL.MES.Shared.Quality;

namespace CCL.MES.Api.Mapping;

/// <summary>Map Application dashboard counts → wire DTO (giữ IqcController mỏng).</summary>
internal static class IqcDashboardMapper
{
    public static IqcDashboardResponse ToResponse(IqcDashboardCounts d) => new()
    {
        Year = d.Year,
        Month = d.Month,
        AvailableYears = d.AvailableYears,
        Total = d.Total,
        Materials = d.Materials,
        Chemical = d.Chemical,
        Tools = d.Tools,
        Other = d.Other,
        Pending = d.Pending,
        Pass = d.Pass,
        Fail = d.Fail,
        PassRate = d.PassRate,
        FailRate = d.FailRate,
        ClaimNgLots = d.ClaimNgLots,
        TotalNqDefects = d.TotalNqDefects,
        WideOosLots = d.WideOosLots,
        VisualPareto = d.VisualPareto.Select(x => new IqcParetoRow
        {
            Defect = x.Defect,
            LabelVi = x.LabelVi,
            LabelEn = x.LabelEn,
            Count = x.Count,
            Share = x.Share,
            Cumulative = x.Cumulative,
        }).ToList(),
        MonthlyTrend = d.MonthlyTrend.Select(x => new IqcMonthlyTrendRow
        {
            Month = x.Month,
            Lots = x.Lots,
            Ng = x.Ng,
            NgRate = x.NgRate,
        }).ToList(),
        Suppliers = d.Suppliers.Select(x => new IqcSupplierStatRow
        {
            Supplier = x.Supplier,
            Lots = x.Lots,
            Ng = x.Ng,
            NgRate = x.NgRate,
            NqDefects = x.NqDefects,
        }).ToList(),
    };
}
