using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// QC Library admin (Bước 1) — shared importer/upsert for the check-item
/// library. CSV-file seed (DbSeeder) AND UI .xlsx import both flow through
/// <see cref="UpsertAsync"/> so the idempotent upsert + ReasonCode expansion
/// live in ONE place (no divergence). <see cref="ImportAsync"/> adds the
/// enum validation (strict ProcessLine; Severity/Group lenient per decision)
/// + per-row error reporting on top of the upsert.
///
/// <para><b>Idempotent</b> (upsert by natural key <c>ItemId</c>; re-run with
/// the same rows → 0 net change). <b>Freeze-safe</b>: only mutates
/// CheckItemLibraries/ReasonCodes master data — never touches a WO's frozen
/// <c>ItemsProfileSnapshotJson</c>, so editing the library does NOT retro-
/// affect already-materialized work orders.</para>
/// </summary>
public static class CheckItemLibraryImporter
{
    /// <summary>The only ProcessLine values a row may carry (strict — reject others).</summary>
    public static readonly string[] AllowedLines =
        { "LABEL", "DIGITAL", "SILK", "PRESS_CNC", "FINISHING" };

    public readonly record struct UpsertResult(int Inserted, int Updated, int ReasonAdded);

    public sealed record ImportResult(
        int Parsed, int Inserted, int Updated, int Skipped, IReadOnlyList<string> Errors);

    /// <summary>
    /// Validate (strict ProcessLine) + idempotent upsert a parsed library file.
    /// Parse-level skips (missing columns / required fields) are carried through
    /// into <see cref="ImportResult.Errors"/> alongside the enum rejections.
    /// </summary>
    public static async Task<ImportResult> ImportAsync(
        IMesDbContext db, QcCheckLibraryCsv.ParseResult parsed, string actor, CancellationToken ct = default)
    {
        var errors = new List<string>(parsed.Skipped);
        var valid = new List<QcCheckLibraryRow>();
        foreach (var r in parsed.Rows)
        {
            if (!AllowedLines.Contains(r.ProcessLine, StringComparer.OrdinalIgnoreCase))
                errors.Add($"ItemId='{r.ItemId}': ProcessLine '{r.ProcessLine}' không hợp lệ " +
                           $"(phải ∈ {string.Join("/", AllowedLines)})");
            else
                valid.Add(r);
        }

        var up = await UpsertAsync(db, valid, actor, ct);
        var skipped = parsed.Skipped.Count + (parsed.Rows.Count - valid.Count);
        return new ImportResult(parsed.Rows.Count, up.Inserted, up.Updated, skipped, errors);
    }

    /// <summary>
    /// Idempotent upsert by <c>ItemId</c> + expand ReasonCode(Scrap) for new
    /// DefectCodes. Commits in a single SaveChanges. Used by DbSeeder (boot CSV
    /// seed) and the UI importer. NO ProcessLine validation here — callers that
    /// take untrusted input use <see cref="ImportAsync"/>.
    /// </summary>
    public static async Task<UpsertResult> UpsertAsync(
        IMesDbContext db, IReadOnlyList<QcCheckLibraryRow> rows, string actor = "seed",
        CancellationToken ct = default)
    {
        var existing = await db.CheckItemLibraries.ToListAsync(ct);
        var byItemId = existing.ToDictionary(x => x.ItemId, StringComparer.Ordinal);

        int inserted = 0, updated = 0, sort = 0;
        foreach (var r in rows)
        {
            sort += 10;
            if (byItemId.TryGetValue(r.ItemId, out var cur))
            {
                if (ApplyRow(cur, r, sort))
                {
                    cur.UpdatedAt = DateTime.UtcNow;
                    cur.UpdatedBy = actor;
                    cur.RowVersion = Guid.NewGuid().ToString("N"); // bump concurrency token on real change
                    updated++;
                }
            }
            else
            {
                var e = new CheckItemLibrary { ItemId = r.ItemId, CreatedBy = actor };
                ApplyRow(e, r, sort);
                db.CheckItemLibraries.Add(e);
                byItemId[r.ItemId] = e;
                inserted++;
            }
        }

        // Expand ReasonCode (Scrap) for each new DefectCode in the library.
        var existingScrap = new HashSet<string>(
            await db.ReasonCodes.Where(c => c.Kind == ReasonCodeKind.Scrap).Select(c => c.Code).ToListAsync(ct),
            StringComparer.Ordinal);
        var defectCodes = rows
            .Select(r => r.DefectCode?.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .Select(c => c!)
            .Distinct(StringComparer.Ordinal)
            .Where(c => !existingScrap.Contains(c))
            .ToList();
        int reasonSort = 200;
        foreach (var code in defectCodes)
        {
            db.ReasonCodes.Add(new ReasonCode
            {
                Code = code, LabelEn = code, LabelVi = code,
                Kind = ReasonCodeKind.Scrap, Sort = reasonSort += 10, CreatedBy = actor,
            });
        }

        await db.SaveChangesAsync(ct);
        return new UpsertResult(inserted, updated, defectCodes.Count);
    }

    /// <summary>Copy row → entity; returns true if any field changed (drives the
    /// idempotent "update only on real change" counter). Shared by seed + import.</summary>
    public static bool ApplyRow(CheckItemLibrary e, QcCheckLibraryRow r, int sort)
    {
        bool changed = false;
        void Set(string cur, string val, Action<string> set) { if (!string.Equals(cur, val, StringComparison.Ordinal)) { set(val); changed = true; } }
        void SetN(string? cur, string? val, Action<string?> set) { if (!string.Equals(cur, val, StringComparison.Ordinal)) { set(val); changed = true; } }

        Set(e.ProcessLine, r.ProcessLine, v => e.ProcessLine = v);
        Set(e.GroupLabel, r.GroupLabel, v => e.GroupLabel = v);
        Set(e.Code, r.Code, v => e.Code = v);
        Set(e.ItemVi, r.ItemVi, v => e.ItemVi = v);
        Set(e.ItemEn, r.ItemEn, v => e.ItemEn = v);
        Set(e.AcceptanceVi, r.AcceptanceVi, v => e.AcceptanceVi = v);
        Set(e.AcceptanceEn, r.AcceptanceEn, v => e.AcceptanceEn = v);
        SetN(e.Method, r.Method, v => e.Method = v);
        SetN(e.Severity, r.Severity, v => e.Severity = v);
        SetN(e.Aql, r.Aql, v => e.Aql = v);
        SetN(e.Sampling, r.Sampling, v => e.Sampling = v);
        SetN(e.CheckType, r.CheckType, v => e.CheckType = v);
        SetN(e.DefectCode, r.DefectCode, v => e.DefectCode = v);
        SetN(e.ParetoPct, r.ParetoPct, v => e.ParetoPct = v);
        SetN(e.ShortForm, r.ShortForm, v => e.ShortForm = v);
        SetN(e.IsoRef, r.IsoRef, v => e.IsoRef = v);
        SetN(e.AppliesWhen, r.AppliesWhen, v => e.AppliesWhen = v);
        SetN(e.Note, r.Note, v => e.Note = v);
        if (e.Sort != sort) { e.Sort = sort; changed = true; }
        return changed;
    }
}
