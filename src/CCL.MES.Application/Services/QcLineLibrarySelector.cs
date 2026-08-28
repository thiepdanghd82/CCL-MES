using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.Services;

/// <summary>
/// Chọn hạng mục thư viện cho một tập QC line đã resolve từ routing.
///
/// <para>VÌ SAO CẦN LỚP NÀY: thư viện v5 là một MA TRẬN — <c>ProcessLine</c> nói
/// hạng mục THUỘC dòng sản phẩm nào (LABEL · SILK), còn 16 cờ tick-box nói nó áp
/// dụng cho PHƯƠNG PHÁP / CÔNG ĐOẠN nào (Flexo · SheetCut · Rdc · Flatbed · Slit
/// · …). Trước lớp này, cả hai chỗ materialize đều lọc bằng đúng một điều kiện
/// <c>lines.Contains(ProcessLine)</c> — tức chỉ dùng nửa đầu của ma trận.</para>
///
/// <para>Hậu quả đo được ngày 2026-08-28: routing resolve ra <c>PRESS_CNC</c>
/// (15 luật map: FBL · PPSC · RDC · ACNC · CNC · LASE · PUNC · MDRH · R2SC +
/// keyword SHEETCUT/POWER PRESS/LASER/PUNCH/DRILL) nhưng thư viện KHÔNG có dòng
/// nào <c>ProcessLine = 'PRESS_CNC'</c> ⇒ 0 hạng mục ⇒ <c>SkippedNoLibrary</c>.
/// Người đứng máy cắt mở IPQC ra và không thấy gì để kiểm — trong khi thư viện
/// CÓ SẴN 14 hạng mục bật cờ <c>SheetCut</c> đúng cho công đoạn đó.</para>
///
/// <para>ĐÓNG DẤU LINE — chi tiết dễ bỏ sót: hạng mục nạp qua đường CỜ phải được
/// đóng dấu <c>ProcessLine</c> = LINE ĐÃ RESOLVE, không phải dòng sở hữu nó trong
/// thư viện. UI chia chip TẦNG-1 theo đúng trường này
/// (<c>LABEL·DIGITAL·SILK → print</c>, <c>PRESS_CNC·FINISHING → cut</c>). Giữ
/// nguyên "LABEL" thì 14 hạng mục cắt sẽ nằm dưới chip IN — sai công đoạn, đúng
/// kiểu nhầm mà Nguyên tắc V của hiến pháp nói tới.</para>
/// </summary>
public static class QcLineLibrarySelector
{
    /// <summary>Một hạng mục đã chọn, kèm line sẽ được đóng băng lên nó.</summary>
    public readonly record struct Selection(CheckItemLibrary Row, string Line);

    /// <summary>
    /// QC line KHÔNG có dòng thư viện của riêng nó, nhưng có một cờ tick-box mô tả
    /// đúng công đoạn đó. Bảng này là chỗ DUY NHẤT khai quan hệ line → cờ.
    ///
    /// <para><c>PRESS_CNC</c> → <c>SheetCut</c>: gom mọi công đoạn CẮT (bế phẳng,
    /// RDC, power press, laser, đục lỗ, khoan, sheet-cut). Trong dữ liệu hiện tại
    /// bốn cờ <c>SheetCut</c> · <c>Rdc</c> · <c>Flatbed</c> · <c>Slit</c> bật trên
    /// ĐÚNG CÙNG 14 dòng, nên chọn cờ nào cũng ra cùng tập — lấy <c>SheetCut</c>
    /// làm cờ đại diện vì chính bảng map đã đặt tên như vậy (<c>R2SC</c> ghi chú
    /// "SheetCut(SS)", và có OpKeyword <c>SHEETCUT</c>).</para>
    ///
    /// <para>CHƯA có mục cho <c>FINISHING</c> · <c>DIGITAL</c> · <c>NONE</c> —
    /// ba line này cũng không có thư viện, nhưng KHÔNG có cờ nào mô tả đúng chúng.
    /// Thêm bừa một ánh xạ ở đây sẽ dựng sai danh mục kiểm cho người đứng máy,
    /// tệ hơn hẳn so với việc hiện "chưa có thư viện". Chờ Ops chốt.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Func<CheckItemLibrary, bool>> FlagFallback =
        new Dictionary<string, Func<CheckItemLibrary, bool>>(StringComparer.OrdinalIgnoreCase)
        {
            ["PRESS_CNC"] = r => r.SheetCut,
        };

    /// <summary>Line nào có đường lùi theo cờ (dùng cho test + chẩn đoán).</summary>
    public static bool HasFlagFallback(string? line) =>
        !string.IsNullOrWhiteSpace(line) && FlagFallback.ContainsKey(line.Trim());

    /// <summary>
    /// Chọn hạng mục cho <paramref name="lines"/>, theo THỨ TỰ của chúng.
    ///
    /// <para>Với mỗi line: ưu tiên dòng thư viện có <c>ProcessLine</c> khớp; nếu
    /// line đó không có dòng nào và có đường lùi theo cờ thì lấy theo cờ.</para>
    ///
    /// <para><b>Khử trùng theo <c>ItemId</c>, LINE ĐẦU TIÊN THẮNG.</b> Bắt buộc:
    /// <c>WoIpqcCheckItems</c> có unique index <c>(WoIpqcCheckId, ItemKey)</c> nên
    /// một hạng mục không thể xuất hiện hai lần trong cùng một check. Hệ quả cần
    /// biết: WO có cả LABEL lẫn PRESS_CNC thì 14 hạng mục cắt đã nằm sẵn trong
    /// LABEL ⇒ chúng ở lại chip IN, chip CẮT không mọc thêm. Chỉ WO KHÔNG có
    /// LABEL (vd chạy SILK, hoặc thuần cắt) mới thấy chúng dưới chip CẮT.</para>
    /// </summary>
    public static IReadOnlyList<Selection> Select(
        IEnumerable<CheckItemLibrary>? rows,
        IReadOnlyList<string>? lines)
    {
        var all = (rows ?? Array.Empty<CheckItemLibrary>()).ToList();
        var order = (lines ?? Array.Empty<string>())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .ToList();

        var picked = new List<Selection>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in order)
        {
            var direct = all.Where(r => string.Equals(r.ProcessLine?.Trim(), line, StringComparison.OrdinalIgnoreCase));
            // Dòng ALL (nhóm E·RoHS) KHÔNG khớp `direct` vì ProcessLine của nó
            // là "ALL", không phải tên line — nên nó không làm `direct.Any()`
            // thành true và không cướp mất đường lùi theo cờ của PRESS_CNC.
            // Chúng được kèm riêng SAU vòng lặp này.
            var chosen = direct.Any() || !FlagFallback.TryGetValue(line, out var flag)
                ? direct
                : all.Where(flag);

            foreach (var r in chosen)
            {
                if (string.IsNullOrWhiteSpace(r.ItemId) || !seen.Add(r.ItemId)) continue;
                picked.Add(new Selection(r, line));
            }
        }

        // Hạng mục LIÊN-DÒNG (ProcessLine = "ALL", vd nhóm E·RoHS của OQC): áp
        // cho mọi dòng sản phẩm. Đóng dấu bằng line ĐẦU TIÊN để chúng nằm gọn
        // một nhóm thay vì rải theo công đoạn — người vận hành đo XRF một lần
        // cho cả lô, không đo lại ở từng máy.
        //
        // CHỈ kèm khi ĐÃ chọn được hạng mục thật (`picked.Count > 0`). Nếu không,
        // WO chỉ resolve ra line chưa có thư viện (FINISHING · DIGITAL) sẽ nhận
        // một profile CHỈ CÓ RoHS và không một hạng mục kiểm nào — tệ hơn hẳn
        // việc trả rỗng để phía gọi lùi về danh mục cũ. RoHS đi KÈM một danh
        // mục, nó không tự mình là danh mục.
        if (picked.Count > 0)
        {
            foreach (var r in all.Where(r => string.Equals(
                         r.ProcessLine?.Trim(), RohsLibrarySeed.AllLines, StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(r.ItemId) || !seen.Add(r.ItemId)) continue;
                picked.Add(new Selection(r, order[0]));
            }
        }

        return picked;
    }
}
