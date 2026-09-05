using System.Text.RegularExpressions;
using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.Services;

/// <summary>
/// P12 bước 2a · P13 bước 4 — dựng bộ hạng mục kiểm cho một lô NVL về.
///
/// <para>Hàm THUẦN: không đụng DB, không đụng thời gian. Mọi luật hội tụ ở đây
/// nên test được hết mà không cần dựng ticket.</para>
///
/// <para><b>Hai nguồn, không phải một.</b></para>
/// <list type="number">
///   <item><b>Theo MÃ</b> — <c>MotherCode → IqcMaterialSpec.MaterialCode →
///     IqcSpecItem[]</c>. Không khớp spec nào ⇒ lùi về <b>ma trận tiêu chuẩn</b>
///     (13 hạng mục <c>InDefaultMatrix</c>).</item>
///   <item><b>Theo NHÓM</b> (P13) — bộ chuẩn của Roll / Pcs / Chem / Tool, áp
///     cho mọi lô của nhóm bất kể mã. Đây là 13 cột đếm lỗi của sheet Roll,
///     9 của sheet PCS… Không spec per-mã nào kê chúng (đo: 0/7.212 dòng), nên
///     thiếu nguồn này thì 30 hạng mục đếm lỗi vĩnh viễn không tới được phiếu.
///     Xem <see cref="IqcCategoryRule.IsCategoryStandard"/>.</item>
/// </list>
///
/// <para>Khoá nối là <c>MotherCode</c>, KHÔNG phải mã IFS — đo trên live
/// 2026-08-28: <c>PartNo</c> (300xxxxx) khớp <c>MaterialCodeIfs</c> (7xxxxxxx)
/// đúng <b>0</b> dòng, hai hệ đánh số khác hẳn.</para>
/// </summary>
public static class IqcCheckResolver
{
    /// <summary>Một hạng mục đã dựng, sẵn sàng đóng băng vào ticket.</summary>
    /// <param name="Kind">Ghi nhận kiểu gì — quyết định ô nhập và luật chấm.</param>
    /// <param name="MeasureCount">Số phép đo phải nhập (chỉ <c>Measure</c>).</param>
    /// <param name="FromCategoryStandard">Đến từ bộ chuẩn của NHÓM, không phải
    /// tiêu chuẩn riêng của mã. Người kiểm cần phân biệt được hai thứ đó.</param>
    public sealed record Item(
        string ItemKey,
        int Seq,
        string GroupCode,
        string GroupLabelVi,
        string? GroupLabelEn,
        string LabelVi,
        string? LabelEn,
        string? AcceptanceVi,
        string? AcceptanceEn,
        string? MethodVi,
        string? MethodEn,
        string? SourceFrequency,
        bool FromDefaultMatrix,
        bool AcceptanceUnspecified,
        int Sort,
        IqcCheckKind Kind = IqcCheckKind.Verdict,
        int MeasureCount = 0,
        double? LimitLow = null,
        double? LimitUp = null,
        string? LimitUnit = null,
        string? LimitLabel = null,
        bool TearIsPass = false,
        bool FromCategoryStandard = false);

    /// <summary>Kết quả resolve cho một mã nguyên liệu.</summary>
    /// <param name="SpecNo">Spec đã khớp, hoặc <c>null</c> khi dùng ma trận.</param>
    /// <param name="FromDefaultMatrix">Phần theo-mã đến từ ma trận tiêu chuẩn.</param>
    public sealed record Result(
        string? SpecNo,
        bool FromDefaultMatrix,
        IReadOnlyList<Item> Items);

    public static readonly Result Empty = new(null, false, Array.Empty<Item>());

    /// <summary>
    /// Tiêu chuẩn dạng KHUÔN MẪU chưa điền — file spec gốc để trống bằng một
    /// dãy chữ X: <c>"FTM: XXX"</c> · <c>"Loại Bút, Qủa nặng: XXX"</c>.
    ///
    /// <para>Đo được trên thư viện: <b>521/5 961 dòng</b> còn placeholder —
    /// <c>CU-01</c> (độ cứng bút chì) tới <b>414/429 = 96%</b>. Đây là vấn đề
    /// của DỮ LIỆU NGUỒN, không phải của import: 809 file spec gốc vốn để trống
    /// chỗ đó.</para>
    ///
    /// <para>Đẩy nguyên văn lên màn hình là hỏi người kiểm <i>"đạt hay không đạt
    /// so với XXX?"</i> rồi bắt họ ký. Đó là chữ ký lên một tiêu chí trống.
    /// Hạng mục vẫn HIỆN (Henry chốt 2026-08-28) nhưng mang cờ
    /// <see cref="Item.AcceptanceUnspecified"/> để UI hiện "chưa xác định — hỏi
    /// QA" và KHÔNG tính vào điều kiện đủ để kết luận lô.</para>
    /// </summary>
    private static readonly Regex Placeholder = new(@"X{2,}", RegexOptions.Compiled);

    public static bool IsUnspecified(string? acceptance) =>
        !string.IsNullOrWhiteSpace(acceptance) && Placeholder.IsMatch(acceptance);

    /// <summary>
    /// Dựng bộ hạng mục cho <paramref name="motherCode"/> trong nhóm
    /// <paramref name="category"/>.
    /// </summary>
    /// <param name="motherCode">
    /// <c>RawMaterials.MotherCode</c> — vd <c>336-H1a</c>. Rỗng ⇒ chỉ còn bộ
    /// chuẩn của nhóm: không đoán bừa tiêu chuẩn theo-mã, vì dựng sai còn tệ
    /// hơn không dựng.
    /// </param>
    /// <param name="category">
    /// Nhóm vật liệu, suy bằng <see cref="IqcCategoryRule.Resolve"/>. Tham số
    /// BẮT BUỘC chứ không có mặc định: bỏ quên nó thì phiếu mất im lặng toàn bộ
    /// ô đếm lỗi, và không gì trên màn hình nói cho ai biết.
    /// </param>
    public static Result Resolve(
        string? motherCode,
        IqcMaterialCategory category,
        IEnumerable<IqcMaterialSpec>? specs,
        IEnumerable<IqcSpecItem>? specItems,
        IEnumerable<IqcCheckItemLibrary>? library)
    {
        var lib = (library ?? Array.Empty<IqcCheckItemLibrary>())
            .Where(x => x.Active)
            .ToDictionary(x => x.ItemId, StringComparer.OrdinalIgnoreCase);
        if (lib.Count == 0) return Empty;

        var code = (motherCode ?? "").Trim();

        // ── nguồn 1: theo MÃ ──────────────────────────────────────────────
        string? specNo = null; var fromMatrix = false;
        var byCode = new List<Item>();
        if (code.Length > 0)
        {
            var spec = (specs ?? Array.Empty<IqcMaterialSpec>())
                .Where(s => s.Active)
                .FirstOrDefault(s => string.Equals(s.MaterialCode?.Trim(), code, StringComparison.OrdinalIgnoreCase));

            var byCodeResult = spec is null
                ? FromMatrix(lib)
                : FromSpec(spec, specItems ?? Array.Empty<IqcSpecItem>(), lib);
            specNo = byCodeResult.SpecNo;
            fromMatrix = byCodeResult.FromDefaultMatrix;
            // Hạng mục theo-mã vẫn phải hợp nhóm: một hạng mục gắn Category=Chem
            // lọt vào phiếu cuộn là bộ hạng mục sai.
            byCode = byCodeResult.Items
                .Where(i => IqcCategoryRule.AppliesTo(lib[i.ItemKey].Category, category))
                .ToList();
        }

        // ── nguồn 2: bộ chuẩn của NHÓM ────────────────────────────────────
        var byCategory = category == IqcMaterialCategory.Any
            ? new List<Item>()
            : lib.Values
                .Where(l => IqcCategoryRule.IsCategoryStandard(l.Category) && l.Category == category)
                .Select(l => FromLibrary(l, fromDefaultMatrix: false, fromCategoryStandard: true))
                .ToList();

        if (byCode.Count == 0 && byCategory.Count == 0) return Empty;

        // Gộp, khử trùng theo (mã hạng mục, thứ tự tiêu chí). Tiêu chuẩn theo-MÃ
        // thắng nếu trùng: nó cụ thể hơn bộ chuẩn của nhóm.
        var seen = new HashSet<(string, int)>();
        var merged = new List<Item>();
        foreach (var i in byCode.Concat(byCategory))
            if (seen.Add((i.ItemKey.ToUpperInvariant(), i.Seq))) merged.Add(i);

        var sort = 0;
        var ordered = merged
            .OrderBy(i => lib[i.ItemKey].Sort)
            .ThenBy(i => i.Seq)
            .Select(i => i with { Sort = sort += 10 })
            .ToList();

        return new Result(specNo, fromMatrix, ordered);
    }

    // ── đường CÓ spec: tiêu chuẩn RIÊNG của nguyên liệu đó ────────────────
    private static Result FromSpec(
        IqcMaterialSpec spec,
        IEnumerable<IqcSpecItem> specItems,
        IReadOnlyDictionary<string, IqcCheckItemLibrary> lib)
    {
        var rows = specItems
            .Where(x => x.Active
                     && string.Equals(x.SpecNo, spec.SpecNo, StringComparison.OrdinalIgnoreCase)
                     && lib.ContainsKey(x.ItemId))
            .OrderBy(x => lib[x.ItemId].Sort)
            .ThenBy(x => x.Seq)
            .ToList();

        // Spec tồn tại nhưng không dòng chi tiết nào ⇒ vẫn lùi về ma trận, thay
        // vì trả bộ rỗng làm người kiểm nhìn màn hình trắng.
        if (rows.Count == 0) return FromMatrix(lib);

        var sort = 0;
        var items = rows.Select(x =>
        {
            var l = lib[x.ItemId];
            return new Item(
                x.ItemId, x.Seq, l.GroupCode, l.GroupLabelVi, l.GroupLabelEn,
                l.ItemVi, l.ItemEn,
                x.AcceptanceVi, x.AcceptanceEn, x.MethodVi, x.MethodEn,
                x.SourceFrequency,
                FromDefaultMatrix: false,
                AcceptanceUnspecified: IsUnspecified(x.AcceptanceVi),
                Sort: sort += 10,
                Kind: l.Kind,
                MeasureCount: l.Kind == IqcCheckKind.Measure ? l.MeasureCount : 0,
                // Ngưỡng số lấy từ DÒNG SPEC, không lấy từ thư viện: thư viện
                // giữ khuôn hạng mục, spec giữ con số của riêng mã này.
                LimitLow: x.LimitLow,
                LimitUp: x.LimitUp,
                LimitUnit: x.LimitUnit,
                LimitLabel: x.LimitLabel,
                TearIsPass: x.TearIsPass);
        }).ToList();

        return new Result(spec.SpecNo, false, items);
    }

    // ── đường KHÔNG spec: ma trận tiêu chuẩn 13 hạng mục ──────────────────
    private static Result FromMatrix(IReadOnlyDictionary<string, IqcCheckItemLibrary> lib)
    {
        var sort = 0;
        var items = lib.Values
            .Where(x => x.InDefaultMatrix)
            .OrderBy(x => x.Sort)
            .Select(l => FromLibrary(l, fromDefaultMatrix: true, fromCategoryStandard: false)
                         with { Sort = sort += 10 })
            .ToList();

        return new Result(null, FromDefaultMatrix: items.Count > 0, items);
    }

    /// <summary>Dựng hạng mục thuần từ thư viện — dùng cho ma trận mặc định và
    /// cho bộ chuẩn của nhóm. Không có dòng spec ⇒ KHÔNG có ngưỡng số ⇒ máy
    /// nhường người chấm, chứ không bịa ra một cận nào.</summary>
    private static Item FromLibrary(
        IqcCheckItemLibrary l, bool fromDefaultMatrix, bool fromCategoryStandard) =>
        new(l.ItemId, Seq: 1, l.GroupCode, l.GroupLabelVi, l.GroupLabelEn,
            l.ItemVi, l.ItemEn,
            l.DefaultAcceptanceVi, l.DefaultAcceptanceEn,
            l.DefaultMethodVi, l.DefaultMethodEn,
            SourceFrequency: null,
            FromDefaultMatrix: fromDefaultMatrix,
            AcceptanceUnspecified: IsUnspecified(l.DefaultAcceptanceVi),
            Sort: 0,
            Kind: l.Kind,
            MeasureCount: l.Kind == IqcCheckKind.Measure ? l.MeasureCount : 0,
            FromCategoryStandard: fromCategoryStandard);
}
