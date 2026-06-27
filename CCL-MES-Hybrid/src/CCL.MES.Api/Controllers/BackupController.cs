using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Api.Services;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Shared;
using CCL.MES.Shared.Backup;
using CCL.MES.Shared.Envelopes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.6h — Admin Backup/Restore endpoints.
///
/// Surfaces:
///   GET   /api/v2/backup           — list snapshots (read-only)
///   POST  /api/v2/backup           — create new snapshot (online SQLite backup)
///   GET   /api/v2/backup/{name}    — download a snapshot
///   POST  /api/v2/backup/restore   — upload a snapshot + restore live DB
///
/// Every endpoint requires <c>AdminOnly</c>. Anonymous → 401
/// (FallbackPolicy), authenticated non-Admin → 403. Both states are
/// covered by <see cref="RouteDiscoveryCanaryTests"/> so a future PR
/// that accidentally drops the policy attribute fails CI.
///
/// Audit emit:
///   BACKUP_CREATE  on every successful snapshot. detail JSON:
///     { file, size_bytes, sha256 }
///   BACKUP_RESTORE on every successful restore. detail JSON:
///     { source_size_bytes, pre_restore_snapshot, sha256 }
///   Failure paths emit no audit row — the request never reached a
///   write step; the controller logs to the API log instead.
/// </summary>
[ApiController]
[Route(ApiVersion.Prefix + "/backup")]
[Authorize(Policy = "AdminOnly")]
public sealed class BackupController : ControllerBase
{
    private readonly BackupApiService _svc;
    private readonly BackupSchedulerService _scheduler;
    private readonly IAuditWriter _audit;
    private const long MaxRestoreBytes = 512L * 1024 * 1024;   // 512 MB hard cap

    public BackupController(BackupApiService svc, BackupSchedulerService scheduler, IAuditWriter audit)
    {
        _svc = svc;
        _scheduler = scheduler;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!_svc.IsSqlite)
            return UnprocessableEntity(new ApiError
            {
                Code = "backup.sqlserver_unsupported",
                MessageEn = "Backup is supported only on the SQLite provider.",
            });
        var list = await _svc.ListAsync(ct);
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create()
    {
        if (!_svc.IsSqlite)
            return UnprocessableEntity(new ApiError
            {
                Code = "backup.sqlserver_unsupported",
                MessageEn = "Backup is supported only on the SQLite provider.",
            });

        var snap = _svc.CreateSnapshot();
        if (snap is null)
            return UnprocessableEntity(new ApiError
            {
                Code = "backup.create_failed",
                MessageEn = "Snapshot creation failed — check server logs.",
            });

        await _audit.EmitAsync(
            action: AuditAction.BackupCreate,
            actor: ActorName(),
            actorRole: ActorRole(),
            targetType: "Backup",
            targetId: snap.FileName,
            detail: JsonSerializer.Serialize(new
            {
                file = snap.FileName,
                size_bytes = snap.SizeBytes,
                sha256 = snap.Sha256,
            }));

        return Ok(snap);
    }

    [HttpGet("{fileName}")]
    public IActionResult Download(string fileName)
    {
        var path = _svc.ResolveDownloadPath(fileName);
        if (path is null)
            return NotFound(new ApiError
            {
                Code = "backup.not_found",
                MessageEn = "Snapshot not found or filename is invalid.",
            });
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, "application/octet-stream", fileName);
    }

    [HttpPost("restore")]
    [RequestSizeLimit(MaxRestoreBytes)]
    public async Task<IActionResult> Restore([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (!_svc.IsSqlite)
            return UnprocessableEntity(new ApiError
            {
                Code = "backup.sqlserver_unsupported",
                MessageEn = "Restore is supported only on the SQLite provider.",
            });
        if (file is null || file.Length == 0)
            return UnprocessableEntity(new ApiError
            {
                Code = "backup.empty_upload",
                MessageEn = "No file uploaded or file is empty.",
            });
        if (file.Length > MaxRestoreBytes)
            return UnprocessableEntity(new ApiError
            {
                Code = "backup.too_large",
                MessageEn = $"File exceeds {MaxRestoreBytes / (1024 * 1024)} MB limit.",
            });

        await using var stream = file.OpenReadStream();
        var result = await _svc.RestoreAsync(stream, ct);

        switch (result.Outcome)
        {
            case RestoreOutcome.Success:
                await _audit.EmitAsync(
                    action: AuditAction.BackupRestore,
                    actor: ActorName(),
                    actorRole: ActorRole(),
                    targetType: "Backup",
                    targetId: result.PreRestoreSnapshot ?? "",
                    detail: JsonSerializer.Serialize(new
                    {
                        source_size_bytes = result.RestoredBytes,
                        pre_restore_snapshot = result.PreRestoreSnapshot,
                    }));
                return Ok(result);

            case RestoreOutcome.EmptyUpload:
                return UnprocessableEntity(new ApiError
                {
                    Code = "backup.empty_upload",
                    MessageEn = "Upload stream was empty.",
                });

            case RestoreOutcome.InvalidHeader:
                return UnprocessableEntity(new ApiError
                {
                    Code = "backup.invalid_header",
                    MessageEn = "File is not a valid SQLite database (header mismatch).",
                });

            case RestoreOutcome.SchemaMismatch:
                return UnprocessableEntity(new ApiError
                {
                    Code = "backup.schema_mismatch",
                    MessageEn = "Database schema does not match — wrong file?",
                });

            case RestoreOutcome.SqlServerUnsupported:
                return UnprocessableEntity(new ApiError
                {
                    Code = "backup.sqlserver_unsupported",
                    MessageEn = "Restore is supported only on the SQLite provider.",
                });

            case RestoreOutcome.Error:
            default:
                return UnprocessableEntity(new ApiError
                {
                    Code = "backup.restore_failed",
                    MessageEn = "Restore failed — check server logs. Pre-restore snapshot may exist for rollback.",
                });
        }
    }

    // ── Scheduled backup (P-Backup) ─────────────────────────────────

    /// <summary>GET /api/v2/backup/schedule — current scheduler status.</summary>
    [HttpGet("schedule")]
    public IActionResult GetSchedule() => Ok(_scheduler.GetStatus());

    /// <summary>
    /// PUT /api/v2/backup/schedule — edit the schedule (enable/hour/retention/
    /// min-keep). Persists to backup-schedule.json + re-arms the worker.
    /// </summary>
    [HttpPut("schedule")]
    public async Task<IActionResult> SetSchedule([FromBody] BackupScheduleUpdateRequest req)
    {
        try
        {
            var status = await _scheduler.SetScheduleAsync(req, ActorName(), _audit);
            return Ok(status);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return UnprocessableEntity(new ApiError
            {
                Code = "backup.invalid_schedule",
                MessageEn = ex.Message,
            });
        }
    }

    /// <summary>
    /// POST /api/v2/backup/run-now — trigger one backup cycle immediately
    /// (snapshot + blob tarball + verify + prune). Audits BACKUP_CYCLE.
    /// </summary>
    [HttpPost("run-now")]
    public async Task<IActionResult> RunNow(CancellationToken ct)
    {
        if (!_svc.IsSqlite)
            return UnprocessableEntity(new ApiError
            {
                Code = "backup.sqlserver_unsupported",
                MessageEn = "Backup is supported only on the SQLite provider.",
            });
        var result = await _scheduler.RunBackupCycleAsync(force: true, ct);
        return Ok(result);
    }

    private string ActorName() => User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
    private string ActorRole() => User.FindFirstValue(ClaimTypes.Role) ?? "";
}
