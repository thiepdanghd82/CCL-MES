namespace CCL.MES.Shared.CheckLibrary;

/// <summary>
/// Re-model v5 — 1 hạng mục thư viện kiểm với MA TRẬN TICK-BOX (16 cờ) + mô tả
/// đầy đủ. Dùng cho smart platform tab QC Library (grid tick-box sửa được).
/// </summary>
public sealed record CheckLibraryItemDto
{
    public string ItemId { get; init; } = "";
    public string ProcessLine { get; init; } = "";
    public string? ProductCode { get; init; }
    public string GroupLabel { get; init; } = "";
    public string Code { get; init; } = "";

    // 16 cờ ma trận (C~R).
    public bool BlankLabel { get; init; }
    public bool Flexo { get; init; }
    public bool LetterPress { get; init; }
    public bool HpIndigo { get; init; }
    public bool SilkScreen { get; init; }
    public bool Flatbed { get; init; }
    public bool Rdc { get; init; }
    public bool Laminate { get; init; }
    public bool Zebra { get; init; }
    public bool SheetCut { get; init; }
    public bool PunchHole { get; init; }
    public bool DrillHole { get; init; }
    public bool Slit { get; init; }
    public bool Ipqc { get; init; }
    public bool Fqc { get; init; }
    public bool Oqc { get; init; }

    // Mô tả / tiêu chuẩn.
    public string ItemVi { get; init; } = "";
    public string ItemEn { get; init; } = "";
    public string AcceptanceVi { get; init; } = "";
    public string AcceptanceEn { get; init; } = "";
    public string? Method { get; init; }
    public string? Severity { get; init; }
    public string? Aql { get; init; }
    public string? Sampling { get; init; }
    public string? CheckType { get; init; }
    public string? DefectCode { get; init; }
    public string? IsoRef { get; init; }
    public string? AppliesWhen { get; init; }
    public string? Note { get; init; }

    public bool Active { get; init; }
    public int Sort { get; init; }
}

/// <summary>Tổng quan 1 process line: đếm hạng mục + phân bố theo stage.</summary>
public sealed record CheckLibraryLineDto
{
    public string ProcessLine { get; init; } = "";
    public int Count { get; init; }
    public int IpqcCount { get; init; }
    public int FqcCount { get; init; }
    public int OqcCount { get; init; }
}

/// <summary>1 luật map process→QC line (data-driven, quyết định #5).</summary>
public sealed record ProcessLineMapDto
{
    public string MatchType { get; init; } = "";
    public string MatchValue { get; init; } = "";
    public string QcLine { get; init; } = "";
    public int Sort { get; init; }
    public bool Active { get; init; }
    public string? Note { get; init; }
}
