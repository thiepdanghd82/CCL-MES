using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// P10.7g — dịch vụ nghiệp vụ cho khâu SETTING persist. Mirror
/// <see cref="IpqcCheckMaterializer"/> + WoIpqcCheckService: materialize
/// bộ hạng mục theo process áp dụng (Print/Cut) từ thư viện
/// <see cref="CheckItemLibrary"/> stage <c>Setting=true</c>, FREEZE nhãn +
/// tiêu chuẩn + nhóm lúc materialize (sửa thư viện sau KHÔNG hồi tố WO đang chạy).
///
/// <para><b>Ranh giới (thin-controller):</b> service này CHỈ mutate entity đã
/// tracked / thêm entity mới vào <see cref="_db"/>; TUYỆT ĐỐI không
/// <c>SaveChanges</c>. Controller (7c-2 atomic) sở hữu SINGLE SaveChanges +
/// audit. Concurrency đi qua WO.RowVersion (entity con không có RowVersion).</para>
/// </summary>
public sealed class SettingCheckService
{
    private readonly IMesDbContext _db;

    public SettingCheckService(IMesDbContext db) => _db = db;

    public const string ProcessPrint = "Print";
    public const string ProcessCut = "Cut";

    /// <summary>
    /// Materialize idempotent bộ <see cref="WoSettingCheckItem"/> cho WO. Chỉ tạo
    /// row còn thiếu (theo natural key WorkOrderId+ProcessKind+ItemKey), giữ nguyên
    /// row đã có (freeze). Print items nếu <paramref name="hasPrint"/>; Cut items
    /// nếu <paramref name="hasCut"/>. KHÔNG SaveChanges.
    /// </summary>
    /// <returns>số row mới thêm (đã Add vào tracker, chưa persist).</returns>
    public async Task<int> MaterializeAsync(
        long woId, bool hasPrint, bool hasCut, CancellationToken ct = default)
    {
        var existingKeys = await _db.WoSettingCheckItems.AsNoTracking()
            .Where(i => i.WorkOrderId == woId)
            .Select(i => new { i.ProcessKind, i.ItemKey })
            .ToListAsync(ct);
        var have = new HashSet<(string, string)>(
            existingKeys.Select(k => (k.ProcessKind, k.ItemKey)));

        // Thư viện SETTING (stage Setting=true, Active, base ProductCode=null).
        var lib = await _db.CheckItemLibraries.AsNoTracking()
            .Where(c => c.Active && c.Setting && c.ProductCode == null)
            .ToListAsync(ct);
        var byItemId = lib.ToDictionary(c => c.ItemId, StringComparer.Ordinal);

        var added = 0;
        foreach (var seed in SettingLibrarySeed.Items())
        {
            if (seed.ProcessKind == ProcessPrint && !hasPrint) continue;
            if (seed.ProcessKind == ProcessCut && !hasCut) continue;
            if (have.Contains((seed.ProcessKind, seed.ItemId))) continue;

            // Freeze nhãn/tiêu chuẩn/nhóm — ưu tiên thư viện DB (nếu Ops sửa),
            // fallback seed để materialize không phụ thuộc thứ tự boot-seed.
            byItemId.TryGetValue(seed.ItemId, out var libRow);
            var labelVi = string.IsNullOrWhiteSpace(libRow?.ItemVi) ? seed.ItemVi : libRow!.ItemVi;
            var stdVi = string.IsNullOrWhiteSpace(libRow?.AcceptanceVi) ? seed.AcceptanceVi : libRow!.AcceptanceVi;
            var group = string.IsNullOrWhiteSpace(libRow?.GroupLabel) ? seed.GroupLabel : libRow!.GroupLabel;

            _db.WoSettingCheckItems.Add(new WoSettingCheckItem
            {
                WorkOrderId = woId,
                ProcessKind = seed.ProcessKind,
                ItemKey = seed.ItemId,
                Label = labelVi,
                Standard = stdVi,
                GroupLabel = group,
                Applicable = true,
                Status = PrepressCheckStatus.Pending,
                AdHoc = false,
                Sort = (seed.ProcessKind == ProcessPrint ? 0 : 1000) + seed.Sort,
            });
            added++;
        }

        return added;
    }

    /// <summary>Đánh OK/NG (+ defect + note) cho 1 hạng mục đã tracked. Mutate
    /// tại chỗ; KHÔNG SaveChanges. Applicable=false ghi lại nhưng loại khỏi guard.</summary>
    public static void SetStatus(
        WoSettingCheckItem item, PrepressCheckStatus status,
        string? defectCode, string? ngNote, bool applicable,
        string actor, DateTime now)
    {
        item.Applicable = applicable;
        item.Status = status;
        item.DefectCode = status == PrepressCheckStatus.Ng ? defectCode : null;
        item.NgNote = status == PrepressCheckStatus.Ng ? ngNote : null;
        item.ConfirmedBy = actor;
        item.ConfirmedAt = now;
    }

    /// <summary>F4 — thêm hạng mục ad-hoc per-WO (chỉ sống với WO này).
    /// KHÔNG SaveChanges. Trả entity đã Add để controller emit audit.</summary>
    public WoSettingCheckItem AddAdHocItem(
        long woId, string processKind, string label, string? standard,
        int sort, string actor)
    {
        var item = new WoSettingCheckItem
        {
            WorkOrderId = woId,
            ProcessKind = processKind,
            ItemKey = $"adhoc-{Guid.NewGuid():N}"[..12],
            Label = label,
            Standard = standard,
            GroupLabel = null,
            Applicable = true,
            Status = PrepressCheckStatus.Pending,
            AdHoc = true,
            Sort = sort,
            CreatedBy = actor,
        };
        _db.WoSettingCheckItems.Add(item);
        return item;
    }

    /// <summary>QC-add-new — thêm defect option per-product (nhớ LOT sau).
    /// KHÔNG SaveChanges. Trả entity đã Add.</summary>
    public CheckItemDefectOption AddDefectOption(
        string itemId, string defectCode, string labelVi, string labelEn,
        string productCode, int sort, string actor)
    {
        var opt = new CheckItemDefectOption
        {
            ItemId = itemId,
            DefectCode = defectCode,
            LabelVi = labelVi,
            LabelEn = labelEn,
            ProductCode = productCode,
            Active = true,
            Sort = sort,
            CreatedBy = actor,
        };
        _db.CheckItemDefectOptions.Add(opt);
        return opt;
    }

    /// <summary>
    /// Pure rollup — Ready = mọi hạng mục <see cref="WoSettingCheckItem.Applicable"/>
    /// của process ÁP DỤNG (Print nếu hasPrint, Cut nếu hasCut) đã == Ok.
    /// Hạng mục N/A (Applicable=false) bị loại khỏi guard. Không I/O.
    /// </summary>
    public static bool Rollup(
        IEnumerable<WoSettingCheckItem> items, bool hasPrint, bool hasCut)
    {
        var applicable = items.Where(i =>
            i.Applicable
            && ((hasPrint && i.ProcessKind == ProcessPrint)
                || (hasCut && i.ProcessKind == ProcessCut)))
            .ToList();

        // Không có process nào áp dụng (safe fallback both-true đã bảo đảm ≥1
        // process ở SettingProcessScope) → chưa materialize → chưa sẵn sàng.
        if (applicable.Count == 0) return false;
        return applicable.All(i => i.Status == PrepressCheckStatus.Ok);
    }
}
