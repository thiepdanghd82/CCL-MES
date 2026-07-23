using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Hybrid.Client.Routing;

/// <summary>
/// P11.5-3 — VN message bank cho Semi-Stock surface (SemiStockController):
/// kho bán thành phẩm + reserve/consume ở assembly leg. Mirror
/// RoutingErrorLocaliser: mọi banner operator-facing ở đây để xUnit lock
/// wording không cần boot MAUI. SemiStockDashboard.razor + LegsDashboard.razor
/// gọi trực tiếp các method static.
/// </summary>
public static class SemiStockErrorLocaliser
{
    /// <summary>Localise ApiError.Code (400/404/422 envelope) từ SemiStockController.</summary>
    public static string LocaliseApiError(int statusCode, ApiError error) =>
        error.Code switch
        {
            "semi.invalid_lot_no"         => "Mã lô (LotNo) bắt buộc.",
            "semi.invalid_kind"           => "Loại bán thành phẩm phải là PRINTED_SEMI hoặc TAPE_SEMI.",
            "semi.invalid_qty"            => "Số lượng phải lớn hơn 0.",
            "semi.not_assembly"           => "Chỉ công đoạn DÁN (assembly) mới xuất kho bán thành phẩm.",
            "semi.leg_in_line"            => "Công đoạn này là IN_LINE — dùng bán thành phẩm cùng WO, không xuất kho.",
            "semi.lot_not_found"          => "Không tìm thấy lô này trong kho — kiểm tra lại mã lô.",
            "semi.insufficient_stock"     => "Kho không đủ bán thành phẩm — cần nhập thêm lô (không giữ một phần).",
            "semi.nothing_reserved"       => "Chưa giữ lô nào cho công đoạn này — reserve trước khi hoàn tất.",
            "leg.not_found"               => "Không tìm thấy công đoạn (leg) này — tải lại danh sách.",
            "wo.idempotency_key_required" => "Thiếu khoá idempotency — báo IT.",
            _                             => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise in-band ErrorCode (200/409) từ SemiSetResponse.</summary>
    public static string LocaliseSetError(string code) => code switch
    {
        "semi.lot_exists"    => "Mã lô này đã tồn tại trong kho.",
        "semi.lot_conflict"  => "Lô vừa bị cập nhật bởi thao tác khác. Đang tải lại kho — thử lại.",
        "http.empty_body"    => "Máy chủ trả về rỗng — báo IT.",
        _                    => $"Mã lỗi không xác định ({code}).",
    };
}
