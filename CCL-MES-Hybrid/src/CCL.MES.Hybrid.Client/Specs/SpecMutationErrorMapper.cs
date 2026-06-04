using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Hybrid.Client.Specs;

/// <summary>
/// P10.5c-1 — Pure mapper from server-side Spec mutation error codes onto
/// operator-facing Vietnamese strings. Lives in the client lib (not in
/// the Shared assembly) because the VN strings are MAUI-local until the
/// resx infrastructure lands in P10.6 (Q12 i18n inline VN).
///
/// Server-side codes (from <c>CCL.MES.Application.SpecService</c> +
/// the controller mapping):
///   - <c>duplicate_spec_code</c>     — Create / Copy
///   - <c>validation</c>              — Create / Copy generic
///   - <c>not_found</c>               — Approve / Update / Trash / Restore / Copy source / Revise source / Supersede target
///   - <c>trashed</c>                 — Edit / Revise / Supersede on trashed source
///   - <c>immutable_status</c>        — Update on non-Draft
///   - <c>invalid_source_status</c>   — Revise on non-Approved/Released
///   - <c>reason_required</c>         — Revise missing reason
///   - <c>invalid_status</c>          — Supersede on non-Approved/Released
///   - <c>confirm_mismatch</c>        — Supersede confirmation SpecCode mismatch
///   - <c>already_trashed</c>         — Trash on already-trashed
///   - <c>active_work_orders</c>      — Trash blocked by active WO refs (count in Details)
///   - <c>not_trashed</c>             — Restore on non-trashed
///
/// P10.5c-2 — Spec xlsx import error codes:
///   - <c>import.no_file</c>                  — multipart 'file' part missing
///   - <c>import.invalid_extension</c>        — non-.xlsx upload
///   - <c>import.oversize</c>                 — &gt; 10 MB cap
///   - <c>import.invalid_content</c>          — content sniff failed (not a ZIP/xlsx)
///   - <c>import.parse_error</c>              — xlsx parser threw (corrupt / wrong layout)
///   - <c>import.no_parsed_payload</c>        — save received empty ParsedJson
///   - <c>import.invalid_parsed_payload</c>   — save received non-deserializable ParsedJson
///   - <c>import.invalid_mode</c>             — unknown save mode
///   - <c>import.spec_code_override_required</c> — SaveAsCopy without override
///   - <c>import.duplicate_ref_no</c>         — RefNo dup raced past preview
///   - <c>import.validation</c>               — legacy SaveAsync InvalidOp (Customer/PartNo)
///
/// P10.5e-1 — Drawings upload + download error codes:
///   - <c>drawing.no_file</c>             — multipart 'file' part missing
///   - <c>drawing.oversize</c>            — &gt; 10 MB cap (blob store + API)
///   - <c>drawing.invalid_extension</c>   — not in pdf/png/jpg/jpeg/svg/gif/webp/dwg/dxf/ai
///   - <c>drawing.invalid_kind</c>        — kind query param not a valid DrawingKind enum
///   - <c>drawing.forbidden</c>           — role gate (UploadAsync requires Admin/Engineer)
///   - <c>drawing.validation</c>          — legacy UploadAsync InvalidOp catch-all
///   - <c>drawing.not_found</c>           — version id missing OR cross-revision attempt
///   - <c>drawing.blob_missing</c>        — DB row exists but disk blob lost
///
/// P10.5e-2 — 3-role decide error codes:
///   - <c>drawing.invalid_role</c>        — Role query param not Npi/Production/Qc
///   - <c>drawing.invalid_decision</c>    — Decision not Approved/Rejected
///   - <c>drawing.department_mismatch</c> — CanActAs gate rejected (403)
///   - <c>drawing.comment_required</c>    — Reject without comment
///   - <c>drawing.invalid_state</c>       — Cannot decide on Superseded version
/// </summary>
public static class SpecMutationErrorMapper
{
    /// <summary>Map an <see cref="ApiException"/> raised from a Spec
    /// mutation call into a single Vietnamese error message ready for
    /// inline banner display. Falls back to MessageEn when the code is
    /// unrecognised so future server-side additions don't surface as a
    /// blank banner.</summary>
    public static string ToVietnameseMessage(ApiException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return ToVietnameseMessage(ex.ApiError);
    }

    public static string ToVietnameseMessage(ApiError err)
    {
        ArgumentNullException.ThrowIfNull(err);
        return err.Code switch
        {
            "duplicate_spec_code"   => "Mã Spec đã tồn tại — chọn mã khác.",
            "validation"            => string.IsNullOrWhiteSpace(err.MessageEn) ? "Dữ liệu chưa hợp lệ." : $"Dữ liệu chưa hợp lệ: {err.MessageEn}",
            "not_found"             => "Không tìm thấy Spec (có thể đã bị xoá).",
            "trashed"               => "Spec đang ở Thùng rác — khôi phục trước khi thực hiện.",
            "immutable_status"      => CurrentStatusSuffix("Chỉ Bản nháp mới được sửa.", err),
            "invalid_source_status" => CurrentStatusSuffix("Chỉ Spec đã Duyệt hoặc Phát hành mới có thể tạo Revise.", err),
            "reason_required"       => "Lý do Revise phải có ít nhất 5 ký tự.",
            "invalid_status"        => CurrentStatusSuffix("Chỉ Spec đã Duyệt hoặc Phát hành mới có thể đánh dấu Thay thế.", err),
            "confirm_mismatch"      => "Mã Spec xác nhận chưa đúng — gõ lại chính xác mã hiện tại.",
            "already_trashed"       => "Spec đã ở Thùng rác.",
            "active_work_orders"    => ActiveWoSuffix(err),
            "not_trashed"           => "Spec không ở Thùng rác — không cần khôi phục.",
            "auth.invalid_credentials" or "auth.bad_claim" => "Phiên đăng nhập đã hết hạn — đăng nhập lại.",
            "http.non_success"      => $"Lỗi máy chủ (HTTP {err.MessageEn}).",
            // P10.5c-2 — Spec xlsx import codes.
            "import.no_file"                  => "Chưa chọn file để tải lên.",
            "import.invalid_extension"        => "Chỉ hỗ trợ file .xlsx.",
            "import.oversize"                 => "File vượt quá 10 MB — chọn file nhỏ hơn.",
            "import.invalid_content"          => "File không phải định dạng xlsx hợp lệ.",
            "import.parse_error"              => string.IsNullOrWhiteSpace(err.MessageEn) ? "Không đọc được file xlsx — sai layout hoặc file hỏng." : $"Không đọc được file xlsx: {err.MessageEn}",
            "import.no_parsed_payload"        => "Phiên xem trước đã hết hạn — chọn lại file.",
            "import.invalid_parsed_payload"   => "Dữ liệu xem trước không hợp lệ — chọn lại file.",
            "import.invalid_mode"             => "Lựa chọn lưu không hợp lệ — thử lại.",
            "import.spec_code_override_required" => "Phải nhập Mã Spec mới khi chọn Lưu thành bản sao.",
            "import.duplicate_ref_no"         => "Đã tồn tại Spec với cùng REF NO — chọn Thay thế hoặc Lưu thành bản sao.",
            "import.validation"               => string.IsNullOrWhiteSpace(err.MessageEn) ? "Dữ liệu chưa hợp lệ — kiểm tra Customer / Part No." : $"Dữ liệu chưa hợp lệ: {err.MessageEn}",
            // P10.5e-1 — Drawings upload + download codes.
            "drawing.no_file"             => "Chưa chọn file bản vẽ để tải lên.",
            "drawing.oversize"            => "File bản vẽ vượt quá 10 MB — chọn file nhỏ hơn.",
            "drawing.invalid_extension"   => "Định dạng file không hợp lệ — chỉ hỗ trợ PDF / PNG / JPG / SVG / GIF / WEBP / DWG / DXF / AI.",
            "drawing.invalid_kind"        => "Loại bản vẽ không hợp lệ.",
            "drawing.forbidden"           => "Tài khoản không có quyền upload bản vẽ (cần Admin hoặc Engineer).",
            "drawing.validation"          => string.IsNullOrWhiteSpace(err.MessageEn) ? "Bản vẽ chưa hợp lệ." : $"Bản vẽ chưa hợp lệ: {err.MessageEn}",
            "drawing.not_found"           => "Không tìm thấy bản vẽ (có thể đã bị xoá).",
            "drawing.blob_missing"        => "File bản vẽ không còn trên máy chủ — vui lòng upload lại.",
            // P10.5e-2 — Decide chain.
            "drawing.invalid_role"        => "Vai trò chip không hợp lệ — chỉ chấp nhận NPI / Sản xuất / QC.",
            "drawing.invalid_decision"    => "Quyết định không hợp lệ — chỉ chấp nhận Approve / Reject.",
            "drawing.department_mismatch" => "Tài khoản không có quyền duyệt chip này — chuyển cho đúng Phòng/Role được phép.",
            "drawing.comment_required"    => "Phải nhập lý do khi Reject.",
            "drawing.invalid_state"       => "Không duyệt được vì version đã ở trạng thái Thay thế.",
            _                       => string.IsNullOrWhiteSpace(err.MessageEn) ? $"Lỗi không xác định ({err.Code})." : err.MessageEn,
        };
    }

    /// <summary>Map by <c>code</c> + the optional <c>currentStatus</c> detail
    /// without needing a full ApiException — useful for unit tests that
    /// reconstruct the mapping table.</summary>
    public static string ToVietnameseMessage(string code, string? currentStatus = null, int? activeWoCount = null, string? messageEn = null)
    {
        var details = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(currentStatus)) details["currentStatus"] = currentStatus!;
        if (activeWoCount is not null) details["activeWoCount"] = activeWoCount.Value.ToString();
        return ToVietnameseMessage(new ApiError
        {
            Code = code,
            MessageEn = messageEn ?? "",
            Details = details.Count == 0 ? null : details,
        });
    }

    private static string CurrentStatusSuffix(string baseMessage, ApiError err)
    {
        if (err.Details is not null && err.Details.TryGetValue("currentStatus", out var status) && !string.IsNullOrWhiteSpace(status))
            return $"{baseMessage} (Hiện tại: {status})";
        return baseMessage;
    }

    private static string ActiveWoSuffix(ApiError err)
    {
        if (err.Details is not null && err.Details.TryGetValue("activeWoCount", out var raw) && int.TryParse(raw, out var count))
            return $"Không thể xoá: {count} Work Order đang sử dụng Spec này.";
        return "Không thể xoá: vẫn còn Work Order đang sử dụng Spec này.";
    }
}
