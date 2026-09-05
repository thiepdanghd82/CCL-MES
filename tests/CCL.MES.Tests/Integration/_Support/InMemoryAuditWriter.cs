using CCL.MES.Application.Audit;

namespace CCL.MES.Tests.Integration._Support;

/// <summary>
/// Phase 9 T2a — capture-only <see cref="IAuditWriter"/> for tests that
/// need to assert which audit events fired (Spec lifecycle, Drawings
/// approvals, Purge skip rows). No DB write — append to in-memory list.
///
/// <para>
/// Port of <c>scripts/VerifyDrawingsUpload</c> InMemoryAuditWriter
/// extended with capture so tests can <c>Assert.Contains</c> the
/// expected action / target / detail JSON.
/// </para>
/// </summary>
public sealed class InMemoryAuditWriter : IAuditWriter
{
    public readonly List<AuditRow> Rows = new();

    public Task EmitAsync(
        string action,
        string actor,
        string actorRole,
        string? targetType = null,
        string? targetId = null,
        string? detail = null,
        string? source = null)
    {
        Rows.Add(new AuditRow(action, actor, actorRole, targetType, targetId, detail, source, DateTime.UtcNow));
        return Task.CompletedTask;
    }

    public IEnumerable<AuditRow> ByAction(string action) => Rows.Where(r => r.Action == action);
    public IEnumerable<AuditRow> ByTarget(string targetType, string targetId) =>
        Rows.Where(r => r.TargetType == targetType && r.TargetId == targetId);
}

public sealed record AuditRow(
    string Action,
    string Actor,
    string ActorRole,
    string? TargetType,
    string? TargetId,
    string? Detail,
    string? Source,
    DateTime EmittedAtUtc);
