using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CCL.MES.Api.Services;

/// <summary>
/// Freezes a DEAD, immutable snapshot of one WO phase into WoTraceSnapshots.
/// The literal values are read from the source entities ONCE (at confirm)
/// and serialised into PayloadJson; Traceability never re-reads the source.
/// Idempotent by ContentHash (confirm/retry with identical content = NOOP);
/// a genuine re-confirm appends a new Version. One shared entry point keeps
/// the serialize logic in a single place (no drift across confirm sites).
/// </summary>
public interface ITraceFreezeService
{
    Task FreezeAsync(long woId, string phase, string actor, CancellationToken ct = default);
}

public sealed class TraceFreezeService : ITraceFreezeService
{
    // camelCase to match the wire contract the client deserialises.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly ITraceIndexService _index;
    private readonly ILogger<TraceFreezeService> _log;

    public TraceFreezeService(IMesDbContext db, IAuditWriter audit, ITraceIndexService index, ILogger<TraceFreezeService> log)
    {
        _db = db; _audit = audit; _index = index; _log = log;
    }

    // Lightweight WO projection — reads only the fields the snapshot needs.
    // Historical note: this projection was originally a workaround for bad
    // CurrentStep rows ('Done') that threw on load. Those rows were repaired
    // 2026-08-19 (see docs/RUNBOOK-CURRENTSTEP-REPAIR-2026-08-19.md), so the
    // workaround motive is gone — the projection stays because it is simply
    // the cheaper read.
    private sealed record WoLite(long Id, string WoNo, long ProductId, long CustomerId,
        int TargetQty, string Uom, string? MesPhase, string ProductName);

    public async Task FreezeAsync(long woId, string phase, string actor, CancellationToken ct = default)
    {
        var wo = await _db.WorkOrders.AsNoTracking().Where(w => w.Id == woId)
            .Select(w => new WoLite(w.Id, w.WoNo, w.ProductId, w.CustomerId, w.TargetQty, w.Uom, w.MesPhase, w.ProductName))
            .FirstOrDefaultAsync(ct);
        if (wo is null) { _log.LogWarning("[trace] freeze skipped — WO {WoId} not found", woId); return; }

        TracePayload payload = phase switch
        {
            TracePhase.Product => await BuildProductAsync(wo, ct),
            TracePhase.Ipqc    => await BuildIpqcAsync(wo, ct),
            TracePhase.Fqc     => await BuildQcAsync(wo, "FQC", TracePhase.Fqc, ct),
            TracePhase.Oqc     => await BuildQcAsync(wo, "OQC", TracePhase.Oqc, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown trace phase"),
        };

        // ContentHash covers the CONTENT only (phase/woNo/variant/header/items),
        // NOT the frozen timestamp/actor — so a confirm retry with unchanged
        // data hashes identically and is a NOOP (idempotent).
        var contentJson = JsonSerializer.Serialize(
            new { payload.Phase, payload.WoNo, payload.Variant, payload.Header, payload.Items }, Json);
        var hash = Sha256Hex(contentJson);

        var latest = await _db.WoTraceSnapshots.AsNoTracking()
            .Where(s => s.WoId == woId && s.Phase == phase)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(ct);

        if (latest is not null && latest.ContentHash == hash)
            return; // idempotent — identical content already frozen

        var version = (latest?.Version ?? 0) + 1;
        var now = DateTime.UtcNow;
        var stamped = payload with { FrozenAtUtc = now, FrozenBy = actor };

        _db.WoTraceSnapshots.Add(new WoTraceSnapshot
        {
            WoId = woId, WoNo = wo.WoNo, Phase = phase,
            Version = version, SchemaVersion = 1,
            FrozenAtUtc = now, FrozenBy = actor,
            ContentHash = hash,
            PayloadJson = JsonSerializer.Serialize(stamped, Json),
            CreatedAt = now, CreatedBy = actor,
        });
        await _db.SaveChangesAsync(ct);

        await _audit.EmitAsync(
            action: AuditForPhase(phase),
            actor: actor, actorRole: "",
            targetType: "WorkOrder", targetId: woId.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                wo_no = wo.WoNo, phase, version, item_count = payload.Items.Count,
            }));

        // Reflect the freeze in the real-time index (frozen flags + notify).
        await _index.TouchByIdAsync(woId, ct);
    }

    private static string AuditForPhase(string phase) => phase switch
    {
        TracePhase.Product => AuditAction.WoTraceProductFreeze,
        TracePhase.Ipqc    => AuditAction.WoTraceIpqcFreeze,
        TracePhase.Fqc     => AuditAction.WoTraceFqcFreeze,
        TracePhase.Oqc     => AuditAction.WoTraceOqcFreeze,
        _ => AuditAction.WoTraceProductFreeze,
    };

    // ── Product data ────────────────────────────────────────────────
    private async Task<TracePayload> BuildProductAsync(WoLite wo, CancellationToken ct)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == wo.ProductId, ct);
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == wo.CustomerId, ct);
        var plate = await _db.WoPlateChecks.AsNoTracking().FirstOrDefaultAsync(p => p.WorkOrderId == wo.Id, ct);
        var cutter = await _db.WoCutterChecks.AsNoTracking().FirstOrDefaultAsync(c => c.WorkOrderId == wo.Id, ct);
        var mats = await _db.WoMaterials.AsNoTracking()
            .Where(m => m.WorkOrderId == wo.Id).OrderBy(m => m.BomLineIdx).ToListAsync(ct);

        // Plate + Cutter moved OUT of the meta header into the "Tools confirmed"
        // list below (section 2). The list is variable-length — today it maps
        // the two 1:1 checks, but a silkscreen code with N tools just adds more
        // elements here with NO renderer/freeze change (N-tool ready; awaiting a
        // multi-tool capture source upstream — we never fabricate tools).
        var header = new List<TraceKv>
        {
            new() { Label = "Product code", Value = product?.ProductCode ?? "" },
            new() { Label = "Part description", Value = product?.Name ?? wo.ProductName },
            new() { Label = "Customer", Value = customer?.Name ?? "" },
            new() { Label = "Target qty", Value = $"{wo.TargetQty} {wo.Uom}" },
            new() { Label = "MES status", Value = wo.MesPhase },
        };

        var tools = new List<TraceTool>();
        if (plate is not null)
            tools.Add(new TraceTool
            {
                Type = "Plate", NumberOrCode = plate.PlateNo, Status = plate.Status.ToString(),
                NgReason = plate.NgReasonCode, CheckedBy = plate.CheckedBy,
                CheckedAt = plate.CheckedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            });
        if (cutter is not null)
            tools.Add(new TraceTool
            {
                Type = "Cutter", NumberOrCode = cutter.CutterNo, Status = cutter.Status.ToString(),
                NgReason = cutter.NgReasonCode, CheckedBy = cutter.CheckedBy,
                CheckedAt = cutter.CheckedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            });

        var items = mats.Select(m => new TraceItem
        {
            Key = $"mat-{m.BomLineIdx}",
            Label = m.MaterialCode,          // Part No (No. is its own frozen field below)
            Status = m.Status.ToString(),
            NgReason = m.NgReasonCode,
            NgNote = m.NgNote,
            // The Product tab renders a FIXED layout from these keys (No. | Part No |
            // Description | QPA(m²) | Qty.Required | UoM | Part Scan | Part Description |
            // Lot | Status | NG). All values are FROZEN literals — no live link.
            Extra = new Dictionary<string, string?>
            {
                ["no"] = (m.BomLineIdx + 1).ToString(),
                ["partNo"] = m.MaterialCode,
                ["description"] = m.MaterialDescription,
                // QPA(m²) was a LIVE link in PrepressController (QtyRequired /
                // targetQty). Freeze it to a FIXED 6-dp number (culture decimal
                // separator, matching the source sheet).
                ["qpaM2"] = wo.TargetQty > 0 ? (m.QtyRequired / wo.TargetQty).ToString("0.000000") : "0",
                ["qtyRequired"] = m.QtyRequired.ToString("0.######"),
                ["uom"] = m.Uom?.ToLowerInvariant(),
                ["partScan"] = m.PartScan,
                ["partDescription"] = m.PartScanDescription,
                ["lotNo"] = m.LotNo,
            },
        }).ToList();

        return new TracePayload
        {
            Phase = TracePhase.Product, WoNo = wo.WoNo, Variant = product?.ProductCode,
            Header = header, Items = items, Tools = tools,
        };
    }

    // ── IPQC ────────────────────────────────────────────────────────
    private async Task<TracePayload> BuildIpqcAsync(WoLite wo, CancellationToken ct)
    {
        var chk = await _db.WoIpqcChecks.AsNoTracking().FirstOrDefaultAsync(c => c.WorkOrderId == wo.Id, ct);
        var items = new List<TraceItem>();
        if (chk is not null)
        {
            // 4 fixed slots — only the ones that carry a status other than the
            // implicit default still render (all 4 always inspected in practice).
            void Slot(string key, string label, IpqcCheckStatus st) =>
                items.Add(new TraceItem { Key = key, Label = label, Status = st.ToString() });
            Slot("material", "Material", chk.MaterialStatus);
            Slot("printA", "Print A (Colour)", chk.PrintAStatus);
            Slot("printB", "Print B (Registration)", chk.PrintBStatus);
            Slot("printC", "Print C (Content)", chk.PrintCStatus);

            // Plan-C data-driven items actually inspected for this WO/variant.
            var dyn = await _db.WoIpqcCheckItems.AsNoTracking()
                .Where(i => i.WoIpqcCheckId == chk.Id).OrderBy(i => i.Id).ToListAsync(ct);
            foreach (var i in dyn)
                items.Add(new TraceItem
                {
                    Key = i.ItemKey, Label = i.Label ?? i.ItemKey,
                    Status = i.Status.ToString(), NgReason = i.NgReasonCode, NgNote = i.NgNote,
                    Extra = new Dictionary<string, string?> { ["processLine"] = i.ProcessLine, ["group"] = i.GroupLabel },
                });
        }

        var header = new List<TraceKv>
        {
            new() { Label = "Judgment", Value = chk?.Judgment.ToString() ?? "Pending" },
            new() { Label = "Special-accept reason", Value = chk?.SpecialAcceptReason },
            new() { Label = "IPQC submitted by", Value = SigKv(chk?.IpqcSubmittedBy, chk?.IpqcSubmittedAt) },
            new() { Label = "QA outcome", Value = chk?.QaOutcome.ToString() },
            new() { Label = "QA reason", Value = chk?.QaReason },
            new() { Label = "QA approved by", Value = SigKv(chk?.QaApprovedBy, chk?.QaApprovedAt) },
        };
        return new TracePayload { Phase = TracePhase.Ipqc, WoNo = wo.WoNo, Variant = null, Header = header, Items = items };
    }

    // ── FQC / OQC (shared shape) ────────────────────────────────────
    private async Task<TracePayload> BuildQcAsync(WoLite wo, string kind, string phase, CancellationToken ct)
    {
        var chk = await _db.WoQcChecks.AsNoTracking()
            .FirstOrDefaultAsync(c => c.WorkOrderId == wo.Id && c.QcKind == kind, ct);
        var items = new List<TraceItem>();
        if (chk is not null)
        {
            var checkItems = await _db.WoQcCheckItems.AsNoTracking()
                .Where(i => i.WoQcCheckId == chk.Id).OrderBy(i => i.Id).ToListAsync(ct);
            foreach (var i in checkItems)
            {
                // photoCount only — NEVER bytes or blob paths.
                var photoCount = await _db.WoQcPhotos.AsNoTracking().CountAsync(p => p.WoQcCheckItemId == i.Id, ct);
                items.Add(new TraceItem
                {
                    Key = i.ItemKey, Label = i.ItemKey,
                    Status = i.Status.ToString(), NgReason = i.NgReasonCode, NgNote = i.NgNote,
                    Extra = photoCount > 0 ? new Dictionary<string, string?> { ["photoCount"] = photoCount.ToString() } : null,
                });
            }
        }

        var header = new List<TraceKv>
        {
            new() { Label = "Judgment", Value = chk?.Judgment.ToString() ?? "Pending" },
            new() { Label = "Judgment reason", Value = chk?.JudgmentReason },
            new() { Label = "Inspected by", Value = SigKv(chk?.InspectedBy, chk?.InspectedAt) },
        };
        if (kind == "OQC")
        {
            header.Add(new TraceKv { Label = "Reviewed by", Value = SigKv(chk?.ReviewedBy, chk?.ReviewedAt) });
            header.Add(new TraceKv { Label = "Approved by", Value = SigKv(chk?.ApprovedBy, chk?.ApprovedAt) });
        }
        return new TracePayload { Phase = phase, WoNo = wo.WoNo, Variant = null, Header = header, Items = items };
    }

    private static string? SigKv(string? by, DateTime? at)
        => string.IsNullOrEmpty(by) ? null : (at is null ? by : $"{by} · {at.Value:yyyy-MM-dd HH:mm}");

    private static string Sha256Hex(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes);
    }
}
