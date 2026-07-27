namespace CCL.MES.Shared.Routing;

/// <summary>P11-2 — 1 leg trong scan picker / dashboard.</summary>
public sealed class LegRow
{
    public long LegId { get; set; }
    public int Sequence { get; set; }
    public string LegKind { get; set; } = "";
    public string Method { get; set; } = "";
    public string ProcessLine { get; set; } = "";
    public string LegPhase { get; set; } = "";
    public string SurfaceProfile { get; set; } = "";
    public string InputSource { get; set; } = "";
    public long? SpecRevisionId { get; set; }
    public int QtyDoneCached { get; set; }
    public int QtyNgCached { get; set; }
    public DateTime? LegDoneAt { get; set; }
    /// <summary>Base64 RowVersion của leg — dùng làm If-Match cho advance/rework.</summary>
    public string LegETag { get; set; } = "";
    /// <summary>Leg là node terminal của DAG (join gate đọc các leg này).</summary>
    public bool IsTerminal { get; set; }
    /// <summary>SOFT dependency chưa xong — advisory, không chặn.</summary>
    public bool SoftWaiting { get; set; }
    /// <summary>HARD dependency chưa xong — leg không vào RUNNING được.</summary>
    public bool HardBlocked { get; set; }

    // P11 per-leg readiness (scoped WoLegId) — each leg has its OWN materialised
    // Pre-press + IPQC surface. These summarise it for the scan-picker card so a
    // worker sees "this leg's checks" without opening a separate dashboard.
    /// <summary>WoMaterial rows materialised for this leg (0 = none / BOM absent).</summary>
    public int MaterialsTotal { get; set; }
    /// <summary>WoMaterial rows for this leg with Status = Ok.</summary>
    public int MaterialsOk { get; set; }
    /// <summary>WoIpqcCheckItem rows materialised for this leg (by leg.ProcessLine).</summary>
    public int IpqcItemsTotal { get; set; }
    /// <summary>WoIpqcCheckItem rows for this leg with Status = Ok.</summary>
    public int IpqcItemsOk { get; set; }
}

/// <summary>P11-2 — 1 cạnh DAG.</summary>
public sealed class LegEdgeRow
{
    public long LegId { get; set; }
    public long DependsOnLegId { get; set; }
    public string DependencyGate { get; set; } = "";
    public int RequiredQty { get; set; }
}

/// <summary>P11-2 — read view: WO + legs + edges cho scan picker.</summary>
public sealed class LegsView
{
    public long WoId { get; set; }
    public string WoNo { get; set; } = "";
    public string MesPhase { get; set; } = "";
    public string WoETag { get; set; } = "";
    public int TargetQty { get; set; }
    public List<LegRow> Legs { get; set; } = new();
    public List<LegEdgeRow> Edges { get; set; } = new();
}

/// <summary>P11-2 — kết quả materialize (fork PREPRESS → SPLIT).</summary>
public sealed class LegMaterializeResponse
{
    public bool Ok { get; set; }
    public string? ErrorCode { get; set; }
    public bool Forked { get; set; }
    public int LegCount { get; set; }
    public string MesPhase { get; set; } = "";
    public string WoETag { get; set; } = "";
    /// <summary>Op routing không map được — người duyệt cần xử lý (không đoán).</summary>
    public List<string> Unmapped { get; set; } = new();
}

/// <summary>P11-2 — body advance 1 leg sang phase kế tiếp.</summary>
public sealed class LegAdvanceRequest
{
    public string ToPhase { get; set; } = "";
}

/// <summary>P11-2 — body rework 1 leg về PREPRESS (Q10).</summary>
public sealed class LegReworkRequest
{
    public string Reason { get; set; } = "";
}

/// <summary>P11-2 — kết quả mutate 1 leg (advance/rework).</summary>
public sealed class LegSetResponse
{
    public bool Ok { get; set; }
    public string? ErrorCode { get; set; }
    public long LegId { get; set; }
    public string LegPhase { get; set; } = "";
    public string LegETag { get; set; } = "";
    /// <summary>MesPhase của WO sau mutate (đổi thành FQC_PENDING nếu join).</summary>
    public string WoMesPhase { get; set; } = "";
    /// <summary>Advance này đã kích hoạt join SPLIT → FQC_PENDING.</summary>
    public bool Joined { get; set; }
    /// <summary>SOFT dependency chưa xong (advisory).</summary>
    public bool SoftWaiting { get; set; }
}
