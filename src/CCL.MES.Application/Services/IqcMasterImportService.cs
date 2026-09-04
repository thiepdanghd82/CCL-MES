using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>Đếm kết quả một lần import. Chạy lại lần hai phải ra 0 ở MỌI cột
/// insert/update — đó là định nghĩa idempotent của repo này.</summary>
public readonly record struct IqcMasterImportResult(
    int RowsRead,
    int RowsSkippedNoCode,
    int CodesWithDuplicateSpecs,
    int SpecsInserted,
    int SpecsEnriched,
    int ItemsInserted,
    int ItemsUpdated,
    int LimitsParsed,
    int LimitsUnparsed);

/// <summary>
/// P13 bước 3 — rót tiêu chuẩn từ sheet <c>Raw</c> của file master IQC vào
/// thư viện spec đang chạy.
///
/// <para><b>Hai đường khác nhau cho hai loại mã</b> (đo 2026-09-04: 1.028 mã mẹ
/// trong file, 356 đã có spec trong app, 672 chưa):</para>
/// <list type="bullet">
///   <item>Mã ĐÃ có spec → <b>làm giàu</b>: thêm/cập nhật 3 hạng mục tiêu chuẩn
///   và <c>TestMethod</c>, GIỮ NGUYÊN <c>Approval</c> và mọi thứ người dùng đã
///   soạn. Đẩy một spec đã duyệt về "chờ duyệt" chỉ vì file ngoài nhắc tới nó
///   là xoá công người khác.</item>
///   <item>Mã CHƯA có → <b>tạo mới</b> với <c>Approval = PendingQc</c>
///   (Henry chốt 2026-09-04).</item>
/// </list>
///
/// <para><b>Vì sao đây là công cụ chạy tay chứ không phải seeder lúc boot.</b>
/// Đây là một lần nạp dữ liệu, không phải dữ liệu nền của app. Nhét vào boot
/// thì (a) mỗi lần khởi động phải đọc 2.320 dòng Excel, (b) con số 459/5961 mà
/// <c>IqcLibrarySeederTests</c> đang khoá sẽ đổi, và (c) không ai còn cơ hội
/// xem trước rồi mới quyết — trong khi file nguồn tên là "version 1 - Copy".</para>
/// </summary>
public sealed class IqcMasterImportService
{
    private readonly IMesDbContext _db;
    public IqcMasterImportService(IMesDbContext db) => _db = db;

    /// <summary>
    /// Chạy import. <paramref name="commit"/> = <c>false</c> thì ĐẾM đầy đủ
    /// nhưng KHÔNG lưu — đó là chế độ mặc định của công cụ gọi tới.
    /// </summary>
    public async Task<IqcMasterImportResult> ImportAsync(
        IReadOnlyList<IqcMasterRow> rows, string actor, bool commit,
        CancellationToken ct = default)
    {
        var read = rows.Count;
        var skipped = 0;
        int specIns = 0, specEnr = 0, itemIns = 0, itemUpd = 0, parsed = 0, unparsed = 0;

        // Nạp một lượt, so trong bộ nhớ. 1.028 mã × 3 hạng mục mà truy vấn từng
        // dòng là ~3.000 vòng round-trip.
        // Bảng spec CÓ mã trùng — đo trên live 2026-09-04: 448 mã phân biệt trên
        // 459 dòng, 7 mã có nhiều spec (SFG-APB2M000102 có SÁU). Không phải do
        // hoa/thường. ToDictionary thẳng sẽ NỔ, và đó là cách tệ nhất để phát
        // hiện: import chết giữa chừng thay vì nói ra vấn đề.
        //
        // Chọn spec có SpecNo NHỎ NHẤT — tất định, chạy lại ra cùng kết quả.
        // Ghi vào MỘT spec là đủ: resolver gộp hạng mục từ MỌI spec của một mã
        // (IqcService.cs:854-863), nên rải cùng một tiêu chuẩn ra cả 6 spec chỉ
        // tạo ra 6 bản sao cho resolver phải hợp nhất lại.
        var allSpecs = await _db.IqcMaterialSpecs.ToListAsync(ct);
        var grouped = allSpecs
            .GroupBy(x => x.MaterialCode.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToList();
        var ambiguous = grouped.Count(g => g.Count() > 1);
        var specByCode = grouped.ToDictionary(
            g => g.Key,
            g => g.OrderBy(x => x.SpecNo, StringComparer.Ordinal).First(),
            StringComparer.Ordinal);
        var itemsByKey = await _db.IqcSpecItems
            .ToDictionaryAsync(
                x => (x.SpecNo.ToUpperInvariant(), x.ItemId.ToUpperInvariant(), x.Seq), x => x, ct);

        var now = DateTime.UtcNow;

        // Gộp trước theo mã mẹ: file master có 2.320 dòng cho 1.028 mã, tức là
        // cùng một mã xuất hiện nhiều lần. Không gộp thì dòng sau ghi đè dòng
        // trước và kết quả phụ thuộc thứ tự đọc file.
        var byCode = new Dictionary<string, IqcMasterRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var code = (r.MotherCode ?? "").Trim();
            if (code.Length == 0) { skipped++; continue; }

            // Dòng SAU chỉ thay dòng trước khi nó khai được NHIỀU hơn — file
            // master có mã lặp lại với ô trống, lấy dòng cuối là mất tiêu chuẩn.
            if (byCode.TryGetValue(code, out var prev) && Filled(prev) >= Filled(r)) continue;
            byCode[code] = r;
        }

        foreach (var (code, row) in byCode)
        {
            var key = code.ToUpperInvariant();
            IqcMaterialSpec spec;

            if (specByCode.TryGetValue(key, out var existing))
            {
                spec = existing;
                var changed = false;
                // CHỈ đụng TestMethod. Approval, Active, SpecNo, Revision,
                // SupplierName là của người dùng — file ngoài không có quyền.
                if (!string.IsNullOrWhiteSpace(row.TestMethod)
                    && !string.Equals(spec.TestMethod, row.TestMethod, StringComparison.Ordinal))
                {
                    spec.TestMethod = row.TestMethod;
                    changed = true;
                }
                if (changed)
                {
                    spec.UpdatedAt = now; spec.UpdatedBy = actor;
                    specEnr++;
                }
            }
            else
            {
                spec = new IqcMaterialSpec
                {
                    SpecNo = IqcMasterItemMap.SpecNoFor(code),
                    MaterialCode = code,
                    SupplierName = row.SupplierName,
                    TestMethod = row.TestMethod,
                    Approval = IqcSpecApproval.PendingQc,   // CHỈ đặt lúc INSERT
                    ImportSource = IqcMasterItemMap.Source,
                    Active = true,
                    CreatedAt = now, CreatedBy = actor,
                };
                _db.IqcMaterialSpecs.Add(spec);
                specByCode[key] = spec;
                specIns++;
            }

            foreach (var (itemId, specText) in IqcMasterItemMap.ItemsOf(row))
            {
                var lim = IqcSpecLimitParser.Parse(specText);
                if (lim is null) unparsed++; else parsed++;

                var ik = (spec.SpecNo.ToUpperInvariant(), itemId.ToUpperInvariant(), 1);
                if (itemsByKey.TryGetValue(ik, out var item))
                {
                    if (ApplyLimits(item, specText, lim, row.TestMethod))
                    {
                        item.UpdatedAt = now; item.UpdatedBy = actor;
                        itemUpd++;
                    }
                }
                else
                {
                    item = new IqcSpecItem
                    {
                        SpecNo = spec.SpecNo, ItemId = itemId, Seq = 1,
                        Active = true, CreatedAt = now, CreatedBy = actor,
                    };
                    ApplyLimits(item, specText, lim, row.TestMethod);
                    _db.IqcSpecItems.Add(item);
                    itemsByKey[ik] = item;
                    itemIns++;
                }
            }
        }

        if (commit && (specIns + specEnr + itemIns + itemUpd) > 0)
            await _db.SaveChangesAsync(ct);

        return new IqcMasterImportResult(
            read, skipped, ambiguous, specIns, specEnr, itemIns, itemUpd, parsed, unparsed);
    }

    /// <summary>Số ô tiêu chuẩn khai được của một dòng — dùng để chọn dòng
    /// "đầy đặn nhất" khi một mã mẹ xuất hiện nhiều lần.</summary>
    private static int Filled(IqcMasterRow r) =>
        IqcMasterItemMap.ItemsOf(r).Count() + (string.IsNullOrWhiteSpace(r.TestMethod) ? 0 : 1);

    /// <summary>Đặt tiêu chuẩn + ngưỡng số đọc được lên một hạng mục. Trả
    /// <c>true</c> khi CÓ thay đổi thật — so trước khi set để chạy lại lần hai
    /// ra 0 update (khuôn <c>DbSeeder</c>).</summary>
    private static bool ApplyLimits(
        IqcSpecItem e, string specText, IqcSpecLimit? lim, string? testMethod)
    {
        var ch = false;
        void S<T>(T cur, T next, Action<T> set)
        {
            if (!EqualityComparer<T>.Default.Equals(cur, next)) { set(next); ch = true; }
        }

        S(e.AcceptanceVi, specText, v => e.AcceptanceVi = v);
        S(e.MethodVi, string.IsNullOrWhiteSpace(testMethod) ? e.MethodVi : testMethod,
            v => e.MethodVi = v);

        S(e.LimitLow, lim?.Low, v => e.LimitLow = v);
        S(e.LimitUp, lim?.Up, v => e.LimitUp = v);

        // Tiêu chuẩn ĐỘ RỘNG trong file master là số trần ("220") — trị danh
        // nghĩa, dung sai nằm ở cột Low/Up riêng của sheet Roll chứ không có ở
        // đây. Bộ đọc cố ý trả null cho nó (đừng tự bịa ±0), NHƯNG vứt luôn con
        // số thì mất 43% dữ liệu tiêu chuẩn của file. Giữ lại làm trị danh
        // nghĩa: người kiểm thấy đích cần đạt, máy vẫn KHÔNG chấm (không có
        // cận trên/dưới ⇒ Undecidable), và sau này khai được luật dung sai thì
        // con số đã nằm sẵn ở đây.
        var nominal = lim?.Nominal ?? IqcSpecLimitParser.ParseBareNominal(specText);
        S(e.LimitNominal, nominal, v => e.LimitNominal = v);
        S(e.LimitUnit, lim?.Unit, v => e.LimitUnit = v);
        S(e.LimitLabel, lim?.Label, v => e.LimitLabel = v);
        S(e.TearIsPass, lim?.TearIsPass ?? false, v => e.TearIsPass = v);
        S(e.LimitParsed, lim is not null, v => e.LimitParsed = v);
        return ch;
    }
}
