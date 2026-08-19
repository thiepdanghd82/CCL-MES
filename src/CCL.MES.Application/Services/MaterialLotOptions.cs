namespace CCL.MES.Application.Services;

/// <summary>
/// A1 — cờ grace period của mạch lô. Bind từ <c>Mes:MaterialLot</c> hoặc env
/// <c>MES_MATERIAL_LOT_ENFORCE_RELEASED</c>.
///
/// <para><b>Vì sao mặc định TẮT.</b> Bật ngay nghĩa là sáng hôm sau operator
/// quét cuộn màng và bị chặn 422 vì kho chưa kịp tạo lô trong hệ — nhà máy
/// dừng, và không ai biết trước sẽ dừng bao nhiêu ca. Khi tắt: vẫn resolve lô,
/// vẫn ghi tiêu thụ, nhưng trả 200 + <c>warning</c> thay cho 422; audit vẫn
/// emit với <c>enforced:false</c>. Nhờ đó <b>đo được chính xác bao nhiêu ca sẽ
/// bị chặn trước khi chặn thật</b>. Ngày lật cờ là quyết định của Henry, không
/// phải của agent.</para>
///
/// <para>Đây cũng là đường rollback mềm của A1: tắt cờ ⇒ hai bảng mới nằm im,
/// đường đọc cũ không đổi.</para>
/// </summary>
public sealed class MaterialLotOptions
{
    /// <summary>
    /// <c>true</c> ⇒ lô không Released bị CHẶN (422). <c>false</c> (mặc định)
    /// ⇒ chỉ cảnh báo, vẫn ghi.
    /// </summary>
    public bool EnforceReleased { get; set; }

    /// <summary>Dấu vết trạng thái cờ đóng vào audit row để về sau phân biệt
    /// "từ chối khi đã siết" với "cho qua trong grace period".</summary>
    public string FlagState => EnforceReleased ? "enforce=on" : "enforce=off";
}

/// <summary>
/// A1 — parse cờ với kỷ luật <b>default-OFF</b>. Lưu ý đây là chiều NGƯỢC với
/// <c>IpqcDualSigOptionsLoader</c> / <c>WoQcSigPolicyOptionsLoader</c> (L20
/// default-ON): hai cái kia canh luật 4-mắt, gõ sai một ký tự mà tắt mất luật
/// là mất an toàn. Cờ này ngược lại — gõ sai mà tự BẬT thì dừng nhà máy. Nên
/// chỉ token ON tường minh (<c>true/1/on/yes</c>) mới bật; mọi thứ khác (null,
/// rỗng, gõ nhầm) giữ TẮT.
/// </summary>
public static class MaterialLotOptionsLoader
{
    public static bool ParseEnforceReleased(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return raw.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "on" or "yes" => true,
            _                              => false,
        };
    }
}
