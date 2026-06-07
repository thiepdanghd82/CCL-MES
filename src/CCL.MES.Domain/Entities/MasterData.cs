namespace CCL.MES.Domain.Entities;

public class Customer : BaseEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<Product> Products { get; set; } = new();
}

public class Product : BaseEntity
{
    public string ProductCode { get; set; } = "";
    public string Name { get; set; } = "";
    public long CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>
    /// P10.7e-1 Q4 — per-product QC threshold + item-enable override.
    /// Optional JSON keyed by item key from the resolved
    /// process profile. Shape:
    /// <code>
    ///   { "&lt;item_key&gt;": { "threshold": &lt;number&gt;, "enabled": true|false },
    ///     ... }
    /// </code>
    /// Resolution chain (highest wins) at
    /// <see cref="CCL.MES.Application.Services.QcThresholdResolver.Resolve"/>:
    ///   1. Product.QcProfileOverride[itemKey].threshold (this column)
    ///   2. ProcessProfile[itemKey].threshold (frozen in
    ///      <c>WoQcCheck.ProfileSnapshotJson</c>)
    ///   3. Item-definition hardcoded default
    /// Stored as TEXT NULL in SQLite (legacy rows continue to read null
    /// + fall through to profile defaults). Admin-only edit surface
    /// deferred to P10.7-BACKLOG OOS-C (profile-management UI). For
    /// v1 ops seed via SQL update.
    /// </summary>
    public string? QcProfileOverride { get; set; }
}
