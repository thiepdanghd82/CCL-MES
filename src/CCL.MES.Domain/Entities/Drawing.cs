namespace CCL.MES.Domain.Entities;

/// <summary>
/// Phase 8 PR #28 — Drawing master record per "slot" of a ProductRevision.
/// SpecHub `artwork` → CCL-MES `Drawing` (Vietnamese-friendly naming).
///
/// PR #28 ADD bảng sẵn — Drawing/Version/Approval entities; UI + blob upload
/// ở PR #31 (drawings MVP) + PR #32 (3-role approval chain).
/// IBlobStore abstraction trong `src/CCL.MES.Application/Storage/IBlobStore.cs`.
/// </summary>
public class Drawing : BaseEntity
{
    public long ProductRevisionId { get; set; }
    public ProductRevision? ProductRevision { get; set; }

    /// <summary>Logical drawing slot: customer drawing / NPI layout / IPQC reference / FQC checksheet…</summary>
    public DrawingKind Kind { get; set; }

    public string Title { get; set; } = "";

    /// <summary>FK to current (active) DrawingVersion. NULL khi chưa có version uploaded.</summary>
    public long? CurrentVersionId { get; set; }
    public DrawingVersion? CurrentVersion { get; set; }

    public DrawingStatus Status { get; set; } = DrawingStatus.Draft;

    public List<DrawingVersion> Versions { get; set; } = new();
}

/// <summary>
/// Versioned content — mỗi lần upload file mới sau khi current version approved
/// tạo row mới ở đây. ChangeReason bắt buộc cho v2+ (service-layer enforce).
/// </summary>
public class DrawingVersion : BaseEntity
{
    public long DrawingId { get; set; }
    public Drawing? Drawing { get; set; }

    public int VersionNo { get; set; }
    public string FileName { get; set; } = "";

    /// <summary>Blob store key — filesystem path or BLOB id, abstracted via IBlobStore.</summary>
    public string StorageKey { get; set; } = "";

    public string FileHash { get; set; } = "";     // sha256
    public long FileSize { get; set; }

    /// <summary>Pre-rendered JPEG cho fast preview. Optional (PR #31 MVP có thể skip).</summary>
    public string? PreviewKey { get; set; }

    /// <summary>BẮT BUỘC NOT NULL cho version_no &gt;= 2 (enforced ở service layer).</summary>
    public string? ChangeReason { get; set; }

    public DrawingVersionStatus Status { get; set; } = DrawingVersionStatus.Draft;
    public long? SupersededByVersionId { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string? UploadedBy { get; set; }

    public List<DrawingApproval> Approvals { get; set; } = new();
}

/// <summary>
/// Per-version approval chain — 3 role: NPI / Production / QC.
/// Mỗi version có riêng chain mới (không inherit từ version cũ).
/// </summary>
public class DrawingApproval : BaseEntity
{
    public long DrawingVersionId { get; set; }
    public DrawingVersion? DrawingVersion { get; set; }

    public DrawingApprovalRole Role { get; set; }
    public DrawingApprovalStatus Status { get; set; } = DrawingApprovalStatus.Pending;
    public string? ActedBy { get; set; }
    public DateTime? ActedAt { get; set; }
    public string? Comment { get; set; }
}
