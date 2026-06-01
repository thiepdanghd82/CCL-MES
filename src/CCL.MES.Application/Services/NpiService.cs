using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>Kết quả truy vấn có phân trang.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;
}

/// <summary>
/// Truy vấn dữ liệu NPI (WorkCenter / RawMaterial / Routing / Structure)
/// với tìm kiếm toàn cục + phân trang phía server (chịu được hàng chục nghìn dòng).
/// </summary>
public class NpiService
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;

    // Phase 8 — single ctor để DI ko phải resolve 2-ctor ambiguity.
    // IAuditWriter đã registered từ Phase 6 Bước 5 (Program.cs:183).
    public NpiService(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    // Phase 6 Bước 2B — local PageAsync removed; consolidated into
    // PagingHelper.PageAsync. Behavior identical; helper is public static.

    public Task<PagedResult<WorkCenter>> WorkCentersAsync(string? search, int page, int pageSize)
    {
        var q = _db.WorkCenters.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            // Phase 7 hạng mục 5 — search expand từ 3 → 4 field thêm
            // ShiftPattern (Q8 chốt; operator filter "A+B+C 24/7 cell").
            q = q.Where(x => x.Code.Contains(s)
                || x.Description.Contains(s)
                || (x.Area != null && x.Area.Contains(s))
                || (x.ShiftPattern != null && x.ShiftPattern.Contains(s)));
        }
        return PagingHelper.PageAsync(q.OrderBy(x => x.Code), page, pageSize);
    }

    public Task<PagedResult<RawMaterial>> RawMaterialsAsync(string? search, int page, int pageSize)
    {
        var q = _db.RawMaterials.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            // Phase 7 hạng mục 3 — search expand từ 4 → 6 field
            // (thêm StatusCode + CountryOfOrigin, khớp CMES raw-materials
            // filter). Operator dùng StatusCode để lọc active vs obsolete.
            q = q.Where(x => x.PartNo.Contains(s)
                || (x.PartDescription != null && x.PartDescription.Contains(s))
                || (x.SupplierName != null && x.SupplierName.Contains(s))
                || (x.SupplierId != null && x.SupplierId.Contains(s))
                || (x.StatusCode != null && x.StatusCode.Contains(s))
                || (x.CountryOfOrigin != null && x.CountryOfOrigin.Contains(s)));
        }
        return PagingHelper.PageAsync(q.OrderBy(x => x.PartNo), page, pageSize);
    }

    public Task<PagedResult<RoutingOperation>> RoutingAsync(string? search, int page, int pageSize)
    {
        var q = _db.RoutingOperations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            // Phase 7 hạng mục 2 — search expand từ 4 → 7 field
            // (thêm WorkCenterDescription + LaborClass + OpNo, khớp CMES
            // engineer-routine.service.list filter). OpNo rất hữu ích khi
            // operator nhớ "Op No" cụ thể trên printed routing card.
            q = q.Where(x => x.PartNo.Contains(s)
                || (x.PartDescription != null && x.PartDescription.Contains(s))
                || (x.Operation != null && x.Operation.Contains(s))
                || (x.WorkCenterNo != null && x.WorkCenterNo.Contains(s))
                || (x.WorkCenterDescription != null && x.WorkCenterDescription.Contains(s))
                || (x.LaborClass != null && x.LaborClass.Contains(s))
                || (x.OpNo != null && x.OpNo.Contains(s)));
        }
        return PagingHelper.PageAsync(q.OrderBy(x => x.PartNo).ThenBy(x => x.OpNo), page, pageSize);
    }

    public Task<PagedResult<ManufacturingStructure>> StructuresAsync(string? search, int page, int pageSize)
    {
        var q = _db.ManufacturingStructures.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            // Phase 7 hạng mục 1 — search expand từ 4 → 6 field (thêm Planner +
            // StructureType, khớp CMES service.list bên tham chiếu).
            q = q.Where(x => x.ParentPart.Contains(s)
                || (x.ParentDescription != null && x.ParentDescription.Contains(s))
                || x.ComponentPart.Contains(s)
                || (x.ComponentDescription != null && x.ComponentDescription.Contains(s))
                || (x.Planner != null && x.Planner.Contains(s))
                || (x.StructureType != null && x.StructureType.Contains(s)));
        }
        return PagingHelper.PageAsync(q.OrderBy(x => x.ParentPart).ThenBy(x => x.ComponentPart), page, pageSize);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Phase 8 — Work Center context-menu CRUD (Open/Edit/Copy/Toggle Active)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Phase 8 hotfix — usage stats only, query by Code (string match
    /// vào RoutingOperations.WorkCenterNo). Trả empty stats nếu opCount=0
    /// (thay vì null) để UI không bị "not found" trap. Modal sẽ pass row
    /// entity TRỰC TIẾP từ grid (đã có sẵn trong memory) → không cần re-fetch.
    /// </summary>
    public async Task<WorkCenterUsageStats> WorkCenterUsageAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new WorkCenterUsageStats(0, 0, null, null, null, new List<TopPartUsage>());

        var ops = _db.RoutingOperations.AsNoTracking().Where(o => o.WorkCenterNo == code);

        var opCount = await ops.CountAsync();
        if (opCount == 0)
            return new WorkCenterUsageStats(0, 0, null, null, null, new List<TopPartUsage>());

        var distinctParts = await ops.Select(o => o.PartNo).Distinct().CountAsync();
        var avgMachSetup = await ops.AverageAsync(o => (double?)o.MachineSetupTime);
        var avgLaborSetup = await ops.AverageAsync(o => (double?)o.LaborSetupTime);
        var avgMachRun = await ops.AverageAsync(o => (double?)o.MachineRunTime);

        var topParts = await ops
            .GroupBy(o => o.PartNo)
            .Select(g => new TopPartUsage(g.Key, g.Count()))
            .OrderByDescending(t => t.OpCount)
            .Take(5)
            .ToListAsync();

        return new WorkCenterUsageStats(opCount, distinctParts, avgMachSetup, avgLaborSetup, avgMachRun, topParts);
    }

    /// <summary>
    /// Phase 8 — keep WorkCenterDetailAsync(id) cho compat (e.g. test
    /// scripts hay external API caller). Internal modal flow giờ pass
    /// row entity trực tiếp + dùng WorkCenterUsageAsync.
    /// </summary>
    public async Task<WorkCenterDetailDto?> WorkCenterDetailAsync(long id)
    {
        var row = await _db.WorkCenters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return null;
        var usage = await WorkCenterUsageAsync(row.Code);
        return new WorkCenterDetailDto(row, usage);
    }

    /// <summary>
    /// Phase 8 — Edit Work Center via context menu. Diff old vs new để
    /// audit detail chỉ ghi field thay đổi. Code unique check thủ công
    /// (DB không có UNIQUE INDEX trên Code; chỉ HasIndex non-unique).
    /// </summary>
    public async Task<WorkCenter?> UpdateWorkCenterAsync(long id, UpdateWorkCenterRequest r, string? user)
    {
        var row = await _db.WorkCenters.FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return null;

        if (!string.Equals(row.Code, r.Code, StringComparison.OrdinalIgnoreCase))
        {
            var clash = await _db.WorkCenters.AnyAsync(x => x.Code == r.Code && x.Id != id);
            if (clash) throw new InvalidOperationException($"WC Code '{r.Code}' already exists.");
        }

        var changes = new Dictionary<string, object?>();
        if (row.Code != r.Code) { changes["code"] = new[] { row.Code, r.Code }; row.Code = r.Code; }
        if (row.Description != r.Description) { changes["description"] = new[] { row.Description, r.Description }; row.Description = r.Description; }
        if (row.Area != r.Area) { changes["area"] = new[] { row.Area, r.Area }; row.Area = r.Area; }
        if (row.IdealSpeedPcsH != r.IdealSpeedPcsH) { changes["ideal_speed_pcs_h"] = new object?[] { row.IdealSpeedPcsH, r.IdealSpeedPcsH }; row.IdealSpeedPcsH = r.IdealSpeedPcsH; }
        if (row.ShiftPattern != r.ShiftPattern) { changes["shift_pattern"] = new[] { row.ShiftPattern, r.ShiftPattern }; row.ShiftPattern = r.ShiftPattern; }
        if (row.Active != r.Active) { changes["active"] = new object?[] { row.Active, r.Active }; row.Active = r.Active; }

        if (changes.Count == 0) return row;

        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = user;
        await _db.SaveChangesAsync();

        await _audit.EmitAsync(
            AuditAction.WcUpdate, user ?? "anonymous", actorRole: "",
            targetType: "WorkCenter", targetId: row.Id.ToString(),
            detail: JsonSerializer.Serialize(new { wc_id = row.Id, code = row.Code, changes }));
        return row;
    }

    /// <summary>
    /// Phase 8 — Copy Work Center via context menu. Tạo row mới với Code
    /// mới (operator điền) + clone Description/Area/IdealSpeedPcsH/
    /// ShiftPattern/Active từ source. Audit WC_COPY ghi mapping src → new.
    /// </summary>
    public async Task<WorkCenter?> CopyWorkCenterAsync(long srcId, UpdateWorkCenterRequest r, string? user)
    {
        var src = await _db.WorkCenters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == srcId);
        if (src is null) return null;

        var clash = await _db.WorkCenters.AnyAsync(x => x.Code == r.Code);
        if (clash) throw new InvalidOperationException($"WC Code '{r.Code}' already exists.");

        var newRow = new WorkCenter
        {
            Code = r.Code,
            Description = r.Description,
            Area = r.Area,
            IdealSpeedPcsH = r.IdealSpeedPcsH,
            ShiftPattern = r.ShiftPattern,
            Active = r.Active ?? true,
            CreatedBy = user,
        };
        _db.WorkCenters.Add(newRow);
        await _db.SaveChangesAsync();

        await _audit.EmitAsync(
            AuditAction.WcCopy, user ?? "anonymous", actorRole: "",
            targetType: "WorkCenter", targetId: newRow.Id.ToString(),
            detail: JsonSerializer.Serialize(new {
                src_id = src.Id, src_code = src.Code,
                new_id = newRow.Id, new_code = newRow.Code,
            }));
        return newRow;
    }

    /// <summary>
    /// Phase 8 — Toggle Active flag via context menu. Audit WC_ACTIVE_TOGGLE
    /// ghi from → to. Idempotent: gọi với active = current value chỉ no-op.
    /// </summary>
    public async Task<WorkCenter?> SetActiveAsync(long id, bool active, string? user)
    {
        var row = await _db.WorkCenters.FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return null;
        if (row.Active == active) return row;

        var from = row.Active;
        row.Active = active;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = user;
        await _db.SaveChangesAsync();

        await _audit.EmitAsync(
            AuditAction.WcActiveToggle, user ?? "anonymous", actorRole: "",
            targetType: "WorkCenter", targetId: row.Id.ToString(),
            detail: JsonSerializer.Serialize(new { wc_id = row.Id, code = row.Code, from, to = active }));
        return row;
    }
}
