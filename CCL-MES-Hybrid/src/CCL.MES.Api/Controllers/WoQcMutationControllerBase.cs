using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Api.Services;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.WoQcReview;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// A2 thin-controller lát 7 — WoQc-specific mutation surface. The generic
/// concurrency MECHANISM (prelude / conflict builder / ETag normaliser /
/// actor + error helpers) now lives in <see cref="WoMutationControllerBase"/>
/// and is shared with <see cref="IpqcReviewController"/>. This layer keeps the
/// WoQc-TYPED conflict response (WoQcSetResponse) via thin wrappers that PRESERVE
/// the old call signatures used by WoQcReviewController + WoQcPhotoController,
/// plus the QC-check helpers (kind normalisation / materialisation / Q4 profile
/// resolution) — NO behaviour change: byte-identical error codes / audit detail
/// / ETag headers / JSON bodies.
/// </summary>
public abstract class WoQcMutationControllerBase : WoMutationControllerBase
{
    protected const string KindFqc = "FQC";
    protected const string KindOqc = "OQC";

    protected WoQcMutationControllerBase(IMesDbContext db, IAuditWriter audit)
        : base(db, audit)
    {
    }

    // Thin wrappers keep the pre-A2-generic signatures WoQcReviewController +
    // WoQcPhotoController already call, binding the WoQc-typed 409 body via the
    // onConflict factory. The MECHANISM (audit + ETag header + read pattern)
    // is byte-identical to the inlined originals — see WoMutationControllerBase.
    protected Task<(IActionResult? Error, WorkOrder? WoForUpdate)> PreludeAsync(
        long id, string actor, string role, string attemptedAction)
        => base.PreludeAsync(id, actor, role, attemptedAction,
            (wo, etag) => Conflict(new WoQcSetResponse
            {
                Ok = false,
                ErrorCode = "wo.state_conflict",
                ETag = etag,
                MesPhase = wo?.MesPhase ?? "",
            }));

    protected Task<IActionResult> HandleWoStateConflictAsync(
        long woId, string actor, string role, string attemptedAction,
        CancellationToken ct = default)
        => base.HandleWoStateConflictAsync(woId, actor, role, attemptedAction,
            (wo, etag) => Conflict(new WoQcSetResponse
            {
                Ok = false,
                ErrorCode = "wo.state_conflict",
                ETag = etag,
                MesPhase = wo?.MesPhase ?? "",
            }), ct);

    // ═══════════════════════════════════════════════════════════════
    // Shared QC-mutation helpers (moved VERBATIM from WoQcReviewController
    // so a WoQcPhotoController slice reuses kind normalisation + check
    // materialisation + Q4 3-level profile resolution — NO behaviour change).
    // ═══════════════════════════════════════════════════════════════

    protected static string? NormaliseKind(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToUpperInvariant();
        return s switch { "FQC" => KindFqc, "OQC" => KindOqc, _ => null };
    }

    protected async Task<WoQcCheck> GetOrCreateCheckAsync(long woId, string kind, long? productId = null)
    {
        var check = await _db.WoQcChecks
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.WorkOrderId == woId && c.QcKind == kind);
        if (check is not null)
        {
            // P10.7e-3 FIX — heal empty snapshot on mutation path too.
            if ((string.IsNullOrWhiteSpace(check.ProfileSnapshotJson) || check.ProfileSnapshotJson == "{}")
                && productId.HasValue)
            {
                var snap = await ResolveProfileSnapshotAsync(productId.Value, kind, CancellationToken.None);
                if (snap != "{}") check.ProfileSnapshotJson = snap;
            }
            return check;
        }

        var resolved = productId.HasValue
            ? await ResolveProfileSnapshotAsync(productId.Value, kind, CancellationToken.None)
            : (QcProfileSeed.GetDefaultProfileJson(kind) ?? "{}");
        check = new WoQcCheck
        {
            WorkOrderId = woId,
            QcKind = kind,
            ProfileSnapshotJson = resolved,
            Judgment = WoQcJudgment.Pending,
        };
        _db.WoQcChecks.Add(check);
        return check;
    }

    /// <summary>P10.7e-3 FIX — Q4 3-level profile resolution chain:
    ///   L1: Product.QcProfileOverride (per-product override JSON;
    ///       shape must include "kind" matching FQC / OQC)
    ///   L2: QcProfileSeed.GetDefaultProfileJson(kind) (system default)
    ///   L3: "{}" empty (only when both levels miss; checks render an
    ///       empty banner so IT notices and seeds the profile).
    /// Frozen at materialise time per Q3 — profile edits don't
    /// retroactively change rows already in flight.</summary>
    protected async Task<string> ResolveProfileSnapshotAsync(long productId, string kind, CancellationToken ct)
    {
        // L1 override JSON read stays here (EF async); the 3-level pure
        // resolution lives in QcProfileResolver (A2 thin-controller, L47).
        string? overrideJson = null;
        if (productId > 0)
        {
            overrideJson = await _db.Products.AsNoTracking()
                .Where(p => p.Id == productId)
                .Select(p => p.QcProfileOverride)
                .FirstOrDefaultAsync(ct);
        }
        return QcProfileResolver.ResolveSnapshot(overrideJson, kind);
    }
}
