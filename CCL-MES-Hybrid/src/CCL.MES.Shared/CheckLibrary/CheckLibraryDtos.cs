namespace CCL.MES.Shared.CheckLibrary;

/// <summary>Phương án C — Bước 6. 1 hạng mục thư viện (read-only view cho admin).</summary>
public sealed record CheckLibraryItemDto
{
    public string ItemId { get; init; } = "";
    public string ProcessLine { get; init; } = "";
    public string? ProductCode { get; init; }
    public string QcStage { get; init; } = "IPQC";
    public string GroupLabel { get; init; } = "";
    public string Code { get; init; } = "";
    public string ItemVi { get; init; } = "";
    public string AcceptanceVi { get; init; } = "";
    public string? Method { get; init; }
    public string? Severity { get; init; }
    public string? DefectCode { get; init; }
    public bool Active { get; init; }
    public int Sort { get; init; }
}

/// <summary>Phương án C — Bước 6. Tổng quan 1 process line (đếm hạng mục).</summary>
public sealed record CheckLibraryLineDto
{
    public string ProcessLine { get; init; } = "";
    public string QcStage { get; init; } = "";
    public int Count { get; init; }
}

/// <summary>Phương án C — Bước 6. 1 luật map process→QC line (data-driven, quyết định #5).</summary>
public sealed record ProcessLineMapDto
{
    public string MatchType { get; init; } = "";
    public string MatchValue { get; init; } = "";
    public string QcLine { get; init; } = "";
    public int Sort { get; init; }
    public bool Active { get; init; }
    public string? Note { get; init; }
}
