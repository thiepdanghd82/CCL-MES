namespace CCL.MES.Shared.SettingChecks;

/// <summary>
/// P10.7g — read view của GET /work-orders/{id}/setting-checks. Single
/// round-trip: danh sách hạng mục SETTING (đã materialize theo process áp
/// dụng) + defect options per item + rollup Ready + ETag = WO.RowVersion.
/// </summary>
public sealed record SettingChecksView
{
    public long WoId { get; init; }
    public string WoNo { get; init; } = "";
    public string MesPhase { get; init; } = "";
    public string ETag { get; init; } = "";
    public string? ProductCode { get; init; }

    /// <summary>Process áp dụng cho WO (từ routing → SettingProcessScope).</summary>
    public bool HasPrint { get; init; }
    public bool HasCut { get; init; }

    /// <summary>Server-computed: mọi hạng mục Applicable của process áp dụng = Ok.
    /// Bật nút Hoàn tất Setting (/setting/done).</summary>
    public bool Ready { get; init; }

    public IReadOnlyList<SettingCheckItemView> Items { get; init; } = Array.Empty<SettingCheckItemView>();
}

/// <summary>P10.7g — 1 hạng mục SETTING trong view (+ defect drop-list).</summary>
public sealed record SettingCheckItemView
{
    public string ItemKey { get; init; } = "";
    public string ProcessKind { get; init; } = "";
    public string? Label { get; init; }
    public string? Standard { get; init; }
    public string? GroupLabel { get; init; }
    public bool Applicable { get; init; } = true;
    public string Status { get; init; } = "Pending";
    public string? DefectCode { get; init; }
    public string? NgNote { get; init; }
    public bool AdHoc { get; init; }
    public int Sort { get; init; }

    /// <summary>Defect options (base + per-product) cho hạng mục này.</summary>
    public IReadOnlyList<SettingDefectOptionView> DefectOptions { get; init; }
        = Array.Empty<SettingDefectOptionView>();
}

/// <summary>P10.7g — 1 tuỳ chọn defect trong drop-list khi NG.</summary>
public sealed record SettingDefectOptionView
{
    public string DefectCode { get; init; } = "";
    public string LabelVi { get; init; } = "";
    public string LabelEn { get; init; } = "";
    public bool PerProduct { get; init; }
    public int Sort { get; init; }
}

/// <summary>P10.7g — body PUT /work-orders/{id}/setting-checks/{itemKey}.
/// Status "Ok"|"Ng". NG bắt buộc <see cref="DefectCode"/> (thuộc drop-list
/// hạng mục) + <see cref="NgNote"/> (1-500). Applicable=false = N/A (loại
/// khỏi guard, không cần Ok).</summary>
public sealed record SetSettingItemRequest
{
    public string? Status { get; init; }
    public string? DefectCode { get; init; }
    public string? NgNote { get; init; }
    public bool Applicable { get; init; } = true;
}

/// <summary>P10.7g — body POST /work-orders/{id}/setting-checks/item (F4).
/// Engineer+ → cũng ghi master CheckItemLibrary per-product; Operator →
/// chỉ WoSettingCheckItem AdHoc=true (server tự quyết theo role).</summary>
public sealed record AddSettingItemRequest
{
    public string? ProcessKind { get; init; }
    public string? Label { get; init; }
    public string? Standard { get; init; }
}

/// <summary>P10.7g — body POST /work-orders/{id}/setting-checks/defect
/// (QC-add-new). Chỉ Engineer+ → CheckItemDefectOption per-product.</summary>
public sealed record AddSettingDefectRequest
{
    public string? ItemId { get; init; }
    public string? DefectCode { get; init; }
    public string? LabelVi { get; init; }
    public string? LabelEn { get; init; }
}

/// <summary>P10.7g — reply chung cho PUT/POST setting-checks. Mang post-write
/// state để client stage mutation kế mà không GET lại. 409 →
/// <see cref="ErrorCode"/>="wo.state_conflict" + <see cref="ETag"/> server hiện tại.</summary>
public sealed record SettingChecksSetResponse
{
    public bool Ok { get; init; }
    public string? ErrorCode { get; init; }
    public string ETag { get; init; } = "";
    public string MesPhase { get; init; } = "";

    /// <summary>Post-write rollup. Null trên endpoint không đổi trạng thái item.</summary>
    public bool? Ready { get; init; }

    /// <summary>Key của hàng vừa thêm (F4 / add-defect) để client refetch/scroll.</summary>
    public string? AddedKey { get; init; }
}
