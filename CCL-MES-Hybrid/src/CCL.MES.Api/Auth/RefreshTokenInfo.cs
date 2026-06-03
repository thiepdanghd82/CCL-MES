namespace CCL.MES.Api.Auth;

/// <summary>
/// Server-side record paired with an opaque refresh-token string in
/// <see cref="IRefreshTokenStore"/>. P10.1 keeps these in-memory; persistent
/// storage lands in a separate PR before pilot operators rely on shift-long
/// sessions surviving server restarts.
/// </summary>
/// <param name="UserId">Owning user (legacy <c>User.Id</c>).</param>
/// <param name="ExpiresAt">UTC instant after which this token is rejected.</param>
/// <param name="FamilyId">Lineage marker. Every refresh-rotation keeps the
/// same FamilyId; if a revoked token in this family is reused, the whole
/// family gets revoked defensively (leaked-token detection).</param>
/// <param name="Revoked">True once the token has been rotated or explicitly
/// invalidated on logout. Stays in store with this flag until cleanup so
/// the re-use-detection window survives.</param>
public sealed record RefreshTokenInfo(
    long UserId,
    DateTime ExpiresAt,
    Guid FamilyId,
    bool Revoked);
