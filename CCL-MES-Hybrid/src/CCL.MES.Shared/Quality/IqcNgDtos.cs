namespace CCL.MES.Shared.Quality;

/// <summary>
/// P13 bước 6 — một vụ NG nguyên liệu như UI nhìn thấy.
///
/// <para>Enum đi qua dây dưới dạng CHUỖI (<c>"Open"</c>, <c>"Production"</c>,
/// <c>"Replacement"</c>…) đúng khuôn <c>IqcCheckItemDto.Kind</c>: client chỉ so
/// chuỗi, không phải giữ một bản sao enum luôn có nguy cơ lệch số thứ tự với
/// server.</para>
/// </summary>
public sealed class IqcNgListItem
{
    public long Id { get; set; }

    /// <summary><c>null</c> với vụ phát hiện ở sản xuất — 38% số vụ thật.</summary>
    public long? IqcInspectionId { get; set; }
    public long? MaterialLotId { get; set; }

    /// <summary>Mã IFS — khoá nối ĐO ĐƯỢC với sheet NG (122/146 = 84%).</summary>
    public string? PartNo { get; set; }

    /// <summary>Số lô của NHÀ CUNG CẤP, nguyên văn. Đây là thứ dùng khi làm
    /// việc với NCC, nên hiện thẳng chứ không giấu sau lô nội bộ.</summary>
    public string? SupplierLotNo { get; set; }

    public string? SupplierName { get; set; }
    public string? MaterialName { get; set; }
    public string? PoNo { get; set; }

    public DateTime DetectedAt { get; set; }

    /// <summary><c>Unknown</c> · <c>Iqc</c> · <c>Production</c>.</summary>
    public string DetectedStage { get; set; } = "Unknown";

    public string? DefectName { get; set; }
    public string? DefectCode { get; set; }

    // Ba đơn vị SONG SONG — kho đếm cuộn, NCC tính m², sản xuất tính mét.
    // Ép về một đơn vị là làm mất số của hai bên kia.
    public double? NgQty { get; set; }
    public string? NgUom { get; set; }
    public double? NgAreaM2 { get; set; }
    public int? NgRolls { get; set; }

    /// <summary><c>Open</c> · <c>Claimed</c> · <c>SupplierConfirmed</c> ·
    /// <c>Settled</c> · <c>ClosedNoClaim</c>.</summary>
    public string Status { get; set; } = "Open";

    public DateTime? ClaimedAt { get; set; }
    public string? ClaimRef { get; set; }

    /// <summary><c>None</c> · <c>Replacement</c> · <c>CreditNote</c> ·
    /// <c>Return</c> · <c>Scrap</c>.</summary>
    public string Settlement { get; set; } = "None";

    public DateTime? SettledAt { get; set; }
    public string? SupplierNote { get; set; }
    public string? Remark { get; set; }

    /// <summary>Dòng nạp từ file master lịch sử, không do người dùng nhập trong
    /// app. Có nó thì không ai nhầm số liệu chép tay với số liệu app sinh.</summary>
    public string? ImportSource { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Trang danh sách vụ NG.</summary>
public sealed class IqcNgListResponse
{
    public IReadOnlyList<IqcNgListItem> Items { get; set; } = Array.Empty<IqcNgListItem>();

    /// <summary>Đếm theo trạng thái, KHÔNG bị ảnh hưởng bởi bộ lọc đang chọn —
    /// dải chip phải hiện tổng của mọi trạng thái, nếu không thì chọn một chip
    /// xong các chip khác về 0 và người dùng tưởng dữ liệu biến mất.</summary>
    public Dictionary<string, int> CountByStatus { get; set; } = new();
}

/// <summary>Kết quả một thao tác ghi trên vụ NG.</summary>
public sealed class IqcNgMutationResponse
{
    public long Id { get; set; }
    public string Status { get; set; } = "Open";
}

/// <summary>Body ghi nhận một vụ NG mới.</summary>
public sealed class CreateIqcNgBody
{
    public long? IqcInspectionId { get; set; }
    public long? MaterialLotId { get; set; }
    public string? PartNo { get; set; }
    public string? SupplierLotNo { get; set; }
    public string? SupplierName { get; set; }
    public string? MaterialName { get; set; }
    public string? PoNo { get; set; }

    /// <summary>Bỏ trống ⇒ server lấy hôm nay. Ngày ở tương lai bị server chặn:
    /// đó là dữ liệu hỏng, không phải một lựa chọn.</summary>
    public DateTime? DetectedAt { get; set; }

    public string? DetectedStage { get; set; }
    public string? DefectName { get; set; }
    public string? DefectCode { get; set; }
    public double? NgQty { get; set; }
    public string? NgUom { get; set; }
    public double? NgAreaM2 { get; set; }
    public int? NgRolls { get; set; }
    public string? Remark { get; set; }
}

/// <summary>Body gửi claim cho NCC.</summary>
public sealed class IqcNgClaimBody
{
    /// <summary>Số hồ sơ claim tự do — "CCL COMPLAINT 20260407", "CCL#260203 8D".
    /// 95 chuỗi phân biệt trên 138 dòng thật: đây KHÔNG phải một danh mục.</summary>
    public string? ClaimRef { get; set; }
    public DateTime? ClaimedAt { get; set; }
}

/// <summary>Body NCC xác nhận, đang chờ xử lý.</summary>
public sealed class IqcNgSupplierConfirmBody
{
    /// <summary>Trả lời của NCC, nguyên văn.</summary>
    public string? Note { get; set; }
}

/// <summary>Body NCC đã đền xong.</summary>
public sealed class IqcNgSettleBody
{
    /// <summary><c>Replacement</c> · <c>CreditNote</c> · <c>Return</c> ·
    /// <c>Scrap</c>. Bắt buộc — "đã xử lý" mà không biết bù hàng hay trừ tiền
    /// thì kế toán không đối chiếu được.</summary>
    public string? Settlement { get; set; }
    public DateTime? SettledAt { get; set; }
    public string? Note { get; set; }
}

/// <summary>Body khép vụ mà không đòi được.</summary>
public sealed class IqcNgCloseBody
{
    /// <summary>Bắt buộc. Không có nó thì sáu tháng sau không ai biết là NCC từ
    /// chối, hay là mình quên đòi.</summary>
    public string? Reason { get; set; }
}
