using System.Text.RegularExpressions;
using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.Services;

/// <summary>
/// P12 bước 2a — dựng bộ hạng mục kiểm cho một lô NVL về, từ mã nguyên liệu.
///
/// <para>Hàm THUẦN: không đụng DB, không đụng thời gian. Mọi luật của 2a hội tụ
/// ở đây nên test được hết mà không cần dựng ticket.</para>
///
/// <para><b>Đường đi:</b> <c>MotherCode → IqcMaterialSpec.MaterialCode → IqcSpecItem[]</c>.
/// Không khớp spec nào ⇒ lùi về <b>ma trận tiêu chuẩn</b> (13 hạng mục
/// <c>InDefaultMatrix</c>). Khoá nối là <c>MotherCode</c>, KHÔNG phải mã IFS —
/// đo trên live 2026-08-28: <c>PartNo</c> (300xxxxx) khớp
/// <c>MaterialCodeIfs</c> (7xxxxxxx) đúng <b>0</b> dòng, hai hệ đánh số khác hẳn.</para>
/// </summary>
public static class IqcCheckResolver
{
    /// <summary>Một hạng mục đã dựng, sẵn sàng đóng băng vào ticket.</summary>
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
        int Sort);

    /// <summary>Kết quả resolve cho một mã nguyên liệu.</summary>
    /// <param name="SpecNo">Spec đã khớp, hoặc <c>null</c> khi dùng ma trận.</param>
    /// <param name="FromDefaultMatrix">Toàn bộ bộ này đến từ ma trận tiêu chuẩn.</param>
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
    /// Dựng bộ hạng mục cho <paramref name="motherCode"/>.
    /// </summary>
    /// <param name="motherCode">
    /// <c>RawMaterials.MotherCode</c> — vd <c>336-H1a</c>. Rỗng ⇒ <see cref="Empty"/>:
    /// không đoán bừa, vì dựng sai bộ hạng mục còn tệ hơn không dựng.
    /// </param>
    public static Result Resolve(
        string? motherCode,
        IEnumerable<IqcMaterialSpec>? specs,
        IEnumerable<IqcSpecItem>? specItems,
        IEnumerable<IqcCheckItemLibrary>? library)
    {
        var lib = (library ?? Array.Empty<IqcCheckItemLibrary>())
            .Where(x => x.Active)
            .ToDictionary(x => x.ItemId, StringComparer.OrdinalIgnoreCase);
        if (lib.Count == 0) return Empty;
        if (string.IsNullOrWhiteSpace(motherCode)) return Empty;

        var code = motherCode.Trim();
        var spec = (specs ?? Array.Empty<IqcMaterialSpec>())
            .Where(s => s.Active)
            .FirstOrDefault(s => string.Equals(s.MaterialCode?.Trim(), code, StringComparison.OrdinalIgnoreCase));

        return spec is null
            ? FromMatrix(lib)
            : FromSpec(spec, specItems ?? Array.Empty<IqcSpecItem>(), lib);
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
                Sort: sort += 10);
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
            .Select(l => new Item(
                l.ItemId, Seq: 1, l.GroupCode, l.GroupLabelVi, l.GroupLabelEn,
                l.ItemVi, l.ItemEn,
                l.DefaultAcceptanceVi, l.DefaultAcceptanceEn,
                l.DefaultMethodVi, l.DefaultMethodEn,
                SourceFrequency: null,
                FromDefaultMatrix: true,
                AcceptanceUnspecified: IsUnspecified(l.DefaultAcceptanceVi),
                Sort: sort += 10))
            .ToList();

        return new Result(null, FromDefaultMatrix: items.Count > 0, items);
    }
}
