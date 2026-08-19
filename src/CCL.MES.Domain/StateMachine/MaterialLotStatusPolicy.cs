using CCL.MES.Domain.Entities;

namespace CCL.MES.Domain.StateMachine;

/// <summary>
/// A1 — kết quả một phán quyết vòng đời lô. <see cref="ErrorCode"/> dùng chuỗi
/// <c>lot.*</c>, KHÔNG mở rộng <see cref="WoErrorCode"/>: enum đó là của state
/// machine WO, còn quét vật tư không phải một transition của WO. Đúng tiền lệ
/// <c>prepress.*</c> / <c>semi.*</c>.
/// </summary>
public sealed record MaterialLotVerdict
{
    public bool Allowed { get; init; }
    public string? ErrorCode { get; init; }
    public string? MessageEn { get; init; }

    public static MaterialLotVerdict Allow() => new() { Allowed = true };

    public static MaterialLotVerdict Deny(string code, string messageEn) =>
        new() { Allowed = false, ErrorCode = code, MessageEn = messageEn };
}

/// <summary>
/// A1 — luật vòng đời lô nguyên vật liệu, dạng HÀM THUẦN (không I/O).
///
/// <para><b>Vì sao tách khỏi service.</b> Cùng lý do
/// <c>OqcSignaturePolicy</c> được tách khỏi <c>WoQcReviewController</c>: luật
/// an toàn chất lượng phải kiểm được bằng unit test chạy trong vài mili-giây,
/// chứ không phải dựng <c>WebApplicationFactory</c> + DB + auth cho mỗi ca
/// biên. Service quyết định ĐỌC/GHI gì; class này quyết định ĐƯỢC hay KHÔNG.</para>
/// </summary>
public static class MaterialLotStatusPolicy
{
    // ── Mã lỗi (chuỗi lot.*, xem chú thích ở MaterialLotVerdict) ──
    public const string NotFound      = "lot.not_found";
    public const string NotReleased   = "lot.not_released";
    public const string Rejected      = "lot.rejected";
    public const string Expired       = "lot.expired";
    public const string PartMismatch  = "lot.part_mismatch";
    public const string Depleted      = "lot.depleted";
    public const string InvalidRequest= "lot.invalid_request";
    public const string Conflict      = "lot.conflict";
    public const string Forbidden     = "lot.forbidden";
    public const string InvalidStatus = "lot.invalid_status";
    public const string SameSigner    = "lot.same_signer";
    public const string NotExpired    = "lot.not_expired";
    public const string NotRetested   = "lot.not_retested";
    public const string AlreadyReversed = "lot.already_reversed";

    /// <summary>
    /// Chuẩn hoá khoá tự nhiên chuỗi — LỚP 3 của ba lớp (xem
    /// <see cref="MaterialLot"/>). Chỉ <c>Trim</c>: KHÔNG upper-case, vì
    /// so khớp không phân biệt hoa thường đã do <c>COLLATE NOCASE</c> ở cột lo,
    /// và ghi đè kiểu chữ sẽ làm mất đúng chuỗi in trên nhãn nhà cung cấp.
    /// </summary>
    public static string Normalize(string? raw) => (raw ?? "").Trim();

    /// <summary>
    /// Thứ tự kiểm CỐ ĐỊNH khi quét lô lên một dòng BOM (§5 hợp đồng):
    /// <c>part mismatch → Rejected → Expired → status≠Released → đủ số lượng</c>.
    ///
    /// <para><b>part mismatch kiểm TRƯỚC status</b> — quét nhầm lô của vật tư
    /// khác là lỗi phổ biến nhất trên sàn. Nếu kiểm status trước thì operator
    /// bỏ máy đi tìm QC, trong khi vấn đề thật chỉ là cầm nhầm cuộn. Thứ tự này
    /// có test khoá lại (<c>Part_mismatch_is_reported_before_status</c>) —
    /// đảo nó là đổi hành vi trên sàn, không phải dọn code.</para>
    /// </summary>
    /// <param name="bomMaterialCode">Mã vật tư của dòng BOM đang quét.</param>
    /// <param name="now">Thời điểm so với <c>ExpiryAt</c> — truyền vào để test
    /// được, không gọi <c>DateTime.UtcNow</c> bên trong hàm thuần.</param>
    public static MaterialLotVerdict CanConsume(
        MaterialLot lot, string? bomMaterialCode, double qtyRequested, DateTime now)
    {
        // 1. Part mismatch — TRƯỚC status. So khớp không phân biệt hoa thường
        //    để khớp đúng ngữ nghĩa NOCASE của cột.
        if (!string.IsNullOrWhiteSpace(bomMaterialCode)
            && !string.Equals(Normalize(lot.PartNo), Normalize(bomMaterialCode),
                              StringComparison.OrdinalIgnoreCase))
        {
            return MaterialLotVerdict.Deny(PartMismatch,
                $"Lot '{lot.LotNo}' belongs to part '{lot.PartNo}', not '{bomMaterialCode}'.");
        }

        // 2. Rejected — terminal, nói rõ ngay để operator không chờ QC vô ích.
        if (Is(lot, MaterialLotStatus.Rejected))
            return MaterialLotVerdict.Deny(Rejected, $"Lot '{lot.LotNo}' was rejected by IQC.");

        // 3. Expired — trạng thái GHI SẴN hoặc ngày hết hạn đã qua. Hai điều
        //    kiện, không phải một: một lô có thể còn Status='Released' mà
        //    ExpiryAt đã lùi vào quá khứ vì chưa ai chạy job quét hạn.
        if (Is(lot, MaterialLotStatus.Expired) || (lot.ExpiryAt is not null && lot.ExpiryAt < now))
            return MaterialLotVerdict.Deny(Expired, $"Lot '{lot.LotNo}' is past its expiry date.");

        // 4. Mọi trạng thái còn lại khác Released (Quarantine, Consumed).
        if (!Is(lot, MaterialLotStatus.Released))
            return MaterialLotVerdict.Deny(NotReleased,
                $"Lot '{lot.LotNo}' is '{lot.Status}', not Released.");

        // 5. Đủ số lượng.
        return CheckQuantity(lot, qtyRequested);
    }

    /// <summary>
    /// Ràng buộc SỐ LƯỢNG, tách riêng có chủ ý.
    ///
    /// <para><b>Vì sao phải gọi được độc lập.</b> Grace period nới ba mã trạng
    /// thái (<c>not_released</c> / <c>expired</c> / <c>rejected</c>) vì chúng
    /// phản ánh "kho chưa kịp nhập liệu IQC". Nhưng <c>CanConsume</c> trả về
    /// NGAY ở bước trạng thái, nên một lô đã <c>Consumed</c> cũng ra
    /// <c>not_released</c> — và nếu nới luôn thì mỗi lần quét lại trừ tiếp một
    /// cuộn đã hết, không giới hạn. Đã bắt được đúng lỗi này bằng test
    /// <c>Concurrent_consume_of_same_lot_yields_exactly_one_winner</c>
    /// (8 request, tồn 10, có 6 request trả 200). Cờ grace period nới việc
    /// <i>chưa có kết luận chất lượng</i>, KHÔNG nới việc <i>cuộn đã hết</i>.</para>
    /// </summary>
    public static MaterialLotVerdict CheckQuantity(MaterialLot lot, double qtyRequested)
    {
        if (lot.QtyAvailable <= 0)
            return MaterialLotVerdict.Deny(Depleted, $"Lot '{lot.LotNo}' has no quantity left.");
        if (qtyRequested > lot.QtyAvailable)
            return MaterialLotVerdict.Deny(Depleted,
                $"Lot '{lot.LotNo}' has {lot.QtyAvailable} left; {qtyRequested} requested.");
        return MaterialLotVerdict.Allow();
    }

    /// <summary>
    /// Trạng thái lô SAU khi đảo một lần tiêu thụ (Đ3). Trả lại số lượng ⇒
    /// <c>Consumed</c> quay về <c>Released</c>. <c>Rejected</c> là terminal:
    /// trả hàng về không làm nó tốt lên. <c>Expired</c> cũng giữ nguyên — hạn
    /// dùng không lùi lại được bằng thao tác kho.
    /// </summary>
    public static string StatusAfterReversal(MaterialLot lot, double qtyAvailableAfter)
    {
        if (Is(lot, MaterialLotStatus.Rejected)) return nameof(MaterialLotStatus.Rejected);
        if (Is(lot, MaterialLotStatus.Expired))  return nameof(MaterialLotStatus.Expired);
        if (Is(lot, MaterialLotStatus.Quarantine)) return nameof(MaterialLotStatus.Quarantine);
        return qtyAvailableAfter > 0
            ? nameof(MaterialLotStatus.Released)
            : nameof(MaterialLotStatus.Consumed);
    }

    /// <summary>
    /// Gia hạn lô hết hạn — <b>HAI VAI KHÁC NHAU</b> (Đ3). Người kiểm lại
    /// (<see cref="MaterialLot.RetestedBy"/>) phải khác người duyệt gia hạn.
    /// So khớp KHÔNG phân biệt hoa thường: cùng một người đăng nhập bằng
    /// "QC01" hay "qc01" vẫn là một người — so khớp phân biệt hoa thường thì
    /// luật tách vai vượt qua được chỉ bằng cách gõ khác kiểu chữ (đúng bài học
    /// đã ghi trong <c>OqcSignaturePolicy.CheckDistinct</c>).
    /// </summary>
    public static MaterialLotVerdict CanExtendExpiry(
        MaterialLot lot, string approver, DateTime newExpiry, DateTime now)
    {
        if (Is(lot, MaterialLotStatus.Rejected))
            return MaterialLotVerdict.Deny(Rejected, "A rejected lot cannot be extended.");

        var isExpired = Is(lot, MaterialLotStatus.Expired)
                        || (lot.ExpiryAt is not null && lot.ExpiryAt < now);
        if (!isExpired)
            return MaterialLotVerdict.Deny(NotExpired,
                "Extension applies only to a lot that is already expired.");

        if (string.IsNullOrWhiteSpace(lot.RetestedBy))
            return MaterialLotVerdict.Deny(NotRetested,
                "QC must record a re-test before the expiry can be extended.");

        if (string.Equals(lot.RetestedBy, approver, StringComparison.OrdinalIgnoreCase))
            return MaterialLotVerdict.Deny(SameSigner,
                "The approver of an extension must differ from the re-tester (Đ3).");

        if (newExpiry <= now)
            return MaterialLotVerdict.Deny(InvalidRequest, "The new expiry must be in the future.");

        return MaterialLotVerdict.Allow();
    }

    /// <summary>Tên trạng thái hợp lệ ⇒ chuỗi chuẩn; sai ⇒ null.</summary>
    public static string? ParseStatus(string? raw) =>
        Enum.TryParse<MaterialLotStatus>((raw ?? "").Trim(), ignoreCase: true, out var v)
            ? v.ToString()
            : null;

    private static bool Is(MaterialLot lot, MaterialLotStatus s) =>
        string.Equals(lot.Status, s.ToString(), StringComparison.OrdinalIgnoreCase);
}
