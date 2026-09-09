// QcResult sống thẳng ở namespace CCL.MES.Domain (Enums.cs), không có sub-namespace.

namespace CCL.MES.Domain.StateMachine;

/// <summary>
/// IPQC ↔ IQC — chốt MỘT kết luận IQC cho một cặp (mã NVL, số lô).
///
/// VÌ SAO CẦN: người đứng máy chỉ cầm cuộn có MÃ + SỐ LÔ in trên đó, không có
/// số phiếu nhận. Nhưng cặp (mã, lô) KHÔNG phải khoá duy nhất trong dữ liệu
/// thật — đo trên DB ngày 2026-09-09, bảng IqcInspections 5334 phiếu:
///   · 667/1600 mã (42%)  có NHIỀU HƠN MỘT lô — nhiều nhất 28 lô/mã
///   · 822/1672 lô (49%)  dùng cho NHIỀU HƠN MỘT mã — có lô nằm dưới 79 mã
///   · 4819 cặp (mã,lô), trong đó 387 cặp có >1 phiếu
///   · 34 cặp cho KẾT QUẢ MÂU THUẪN — vừa Pass vừa Fail
/// (Chỉ bộ ba (mã, lô, ReceiptNo) mới duy nhất: 5319 nhóm / 5319 dòng.)
///
/// LUẬT CHỐT (Thiệp chọn 2026-09-09, phương án 2 — "hễ có Fail thì báo Fail"):
/// khi một cặp (mã, lô) ra nhiều phiếu trái ngược nhau thì lấy Fail. Chọn theo
/// hướng CHẶN chứ không theo hướng cho qua: báo nhầm Pass cho lô đã Fail là
/// thả hàng lỗi vào chuyền, còn báo nhầm Fail chỉ tốn một lần IPQC xem lại.
/// Hai phương án bị loại và lý do:
///   · "lấy phiếu nhận gần nhất"  → đúng 34 cặp kia có nguy cơ nuốt mất Fail
///   · "hiện hết cho IPQC chọn"   → chính xác nhất nhưng thêm một bước cho
///                                   người vận hành; để dành nếu sau này cần
///
/// Thứ tự ưu tiên: Fail ≻ Pending ≻ Pass. Pending đứng trên Pass vì phiếu chưa
/// kết luận thì KHÔNG được coi là đã đạt — cùng một tinh thần chặn.
///
/// Thuần, không EF, không DB — mọi input do caller giải sẵn.
/// </summary>
public static class IqcLotVerdict
{
    /// <summary>
    /// Một phiếu IQC đã rút gọn còn đúng phần cần để chốt kết luận.
    /// Mang CẢ <paramref name="CodeIfs"/> lẫn <paramref name="PartNo"/> và khớp
    /// theo bất kỳ cột nào trúng: đo được hai cột bằng nhau ở 5331/5334 dòng
    /// (lệch 3), nhưng CodeIfs CÓ null còn PartNo thì không (0 dòng rỗng) — cược
    /// vào một cột là im lặng bỏ sót đúng nhóm dòng cột kia đang gánh.
    /// </summary>
    public readonly record struct Ticket(
        long Id,
        string? CodeIfs,
        string? PartNo,
        string? LotNumber,
        string? BatchNumber,
        string? ReceiptNo,
        QcResult Result,
        DateTime ReceivedDate);

    /// <summary>
    /// Kết luận cho một cặp (mã, lô).
    /// <paramref name="GoverningReceiptNo"/> là phiếu ĐẠI DIỆN — phiếu mang
    /// đúng <paramref name="Result"/> đã chốt, nhận muộn nhất trong nhóm đó.
    /// Đưa số phiếu ra ngoài để màn IPQC hiển thị được nguồn, và để người xem
    /// truy ngược được vì sao lô này bị chặn.
    /// </summary>
    public readonly record struct Verdict(
        QcResult Result,
        long GoverningId,
        string? GoverningReceiptNo,
        int TicketCount,
        bool HasConflict);

    /// <summary>Số lô hiệu lực của một phiếu: LotNumber, rỗng thì lùi về
    /// BatchNumber. Hai trường này gần như luôn bằng nhau (đo được chỉ 3/5334
    /// dòng khác nhau) nhưng LotNumber là trường được nhập, nên nó thắng.</summary>
    public static string? EffectiveLot(Ticket t)
        => string.IsNullOrWhiteSpace(t.LotNumber) ? Norm(t.BatchNumber) : Norm(t.LotNumber);

    /// <summary>
    /// Chốt kết luận cho (<paramref name="materialCode"/>, <paramref name="lotNo"/>)
    /// từ tập phiếu <paramref name="tickets"/>. Trả null khi không có phiếu nào
    /// khớp — caller phân biệt được "chưa có dữ liệu IQC" với "có và đạt".
    /// So khớp KHÔNG phân biệt hoa thường và bỏ khoảng trắng thừa, vì mã/lô đến
    /// từ nhiều đường nhập (quét, dán Excel, gõ tay).
    /// </summary>
    public static Verdict? Resolve(string? materialCode, string? lotNo, IEnumerable<Ticket> tickets)
    {
        var code = Norm(materialCode);
        var lot = Norm(lotNo);
        if (code is null || lot is null) return null;

        var hit = new List<Ticket>();
        foreach (var t in tickets)
        {
            if (!Same(Norm(t.CodeIfs), code) && !Same(Norm(t.PartNo), code)) continue;
            if (!Same(EffectiveLot(t), lot)) continue;
            hit.Add(t);
        }
        if (hit.Count == 0) return null;

        // Fail ≻ Pending ≻ Pass. KHÔNG dùng Max/Min trên enum: thứ tự khai báo
        // của QcResult là Pending(0) Pass(1) Fail(2), trùng hợp đúng chiều hôm
        // nay — nhưng ai chèn thêm một giá trị vào giữa là luật đổi âm thầm.
        var result = hit.Any(t => t.Result == QcResult.Fail) ? QcResult.Fail
                   : hit.Any(t => t.Result == QcResult.Pending) ? QcResult.Pending
                   : QcResult.Pass;

        var governing = hit.Where(t => t.Result == result)
                           .OrderByDescending(t => t.ReceivedDate)
                           .ThenByDescending(t => Norm(t.ReceiptNo) ?? "", StringComparer.Ordinal)
                           .First();

        return new Verdict(
            Result: result,
            GoverningId: governing.Id,
            GoverningReceiptNo: Norm(governing.ReceiptNo),
            TicketCount: hit.Count,
            HasConflict: hit.Select(t => t.Result).Distinct().Count() > 1);
    }

    private static string? Norm(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static bool Same(string? a, string? b)
        => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
