namespace CCL.MES.Application.Services;

/// <summary>
/// P10.7e-3 FIX (Henry RCA on PR #123) — embedded default QC profile JSON.
///
/// 7e-1 shipped the Q3 data-driven schema (WoQcChecks.ProfileSnapshotJson
/// + WoQcCheckItem.ItemKey) but DbSeeder did NOT seed the default
/// MES_FQC_PROFILE (12 items) / MES_OQC_PROFILE (28 items). The
/// WoQcReviewController happily lazy-materialised a Pending check row
/// with ProfileSnapshotJson = "{}" — operator UI then rendered 0 items
/// and the judgment row was perma-disabled.
///
/// checkpoint-7e-2.sh masked the gap because step 6 directly INSERTed a
/// check row with hard-coded ProfileSnapshotJson, bypassing the real
/// materialisation path. L23 codifies the prevention: checkpoint MUST
/// walk the real operator GET path.
///
/// Profile shapes mirror the SpecHub prototype 1:1:
///   MES_FQC_PROFILE — `spechub-prototype.html:15938-16045`
///     12 items in 4 sections (sample_qty + appearance + dimensions +
///     judgment).
///   MES_OQC_PROFILE — `spechub-prototype.html:16047-16270`
///     28 items in 5 sections (info + dimensions + appearance +
///     rohs_halogen + judgment) sourced from form CCL-10-F6 (R04).
///
/// Pattern is per-kind (mirrors ReasonCodes seed convention from L17).
/// Boot probe emits `[seed] qc_profiles fqc=12 oqc=28` per L20.
/// </summary>
public static class QcProfileSeed
{
    /// <summary>Profile kinds: "FQC" or "OQC". Mirrors
    /// WoQcReviewController KindFqc/KindOqc constants.</summary>
    public const string KindFqc = "FQC";
    public const string KindOqc = "OQC";

    /// <summary>Resolve the default profile JSON for a kind. Returns
    /// the canonical SpecHub shape (sections + items). Empty string on
    /// unknown kind — caller falls through to "{}" empty snapshot.</summary>
    public static string? GetDefaultProfileJson(string kind) => kind?.ToUpperInvariant() switch
    {
        KindFqc => FqcProfileJson,
        KindOqc => OqcProfileJson,
        _       => null,
    };

    /// <summary>Item count for boot probe + tests. Source of truth so a
    /// future profile edit auto-updates the probe assertion.</summary>
    public static int CountItems(string profileJson)
    {
        if (string.IsNullOrWhiteSpace(profileJson) || profileJson == "{}") return 0;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(profileJson);
            if (!doc.RootElement.TryGetProperty("sections", out var sections)) return 0;
            var n = 0;
            foreach (var section in sections.EnumerateArray())
            {
                if (section.TryGetProperty("items", out var items))
                    n += items.GetArrayLength();
            }
            return n;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>FQC default — 12 items in 4 sections. Frozen at
    /// check-creation time per Q3 contract (admin edits never
    /// retroactively change a check in flight).</summary>
    public const string FqcProfileJson = """
{
  "icon": "🔍",
  "name": "Final Quality Check (FQC)",
  "color": "#0ea5e9",
  "sampling": "AQL Level II · 0.4 (per lot size)",
  "sections": [
    {
      "id": "sample_qty",
      "title": "Sample size",
      "items": [
        { "key": "lot_size",     "label": "Tổng lô (Lot size)",         "unit": "pcs", "method": "Đọc từ Run output" },
        { "key": "sample_size",  "label": "Cỡ mẫu kiểm",                "unit": "pcs", "method": "AQL II / 0.4 lookup table" }
      ]
    },
    {
      "id": "appearance",
      "title": "Ngoại quan (Sample inspection)",
      "items": [
        { "key": "no_tear",       "label": "Không rách / nếp / bẩn / mẻ", "spec": "OK",         "method": "Visual" },
        { "key": "color_match",   "label": "Màu sắc đúng mẫu chuẩn",      "spec": "OK",         "method": "Visual + Spectro nếu nghi ngờ" },
        { "key": "joint_check",   "label": "Vị trí mối nối",              "spec": "OK",         "method": "Visual" },
        { "key": "logo_print",    "label": "Logo & vùng in",              "spec": "Theo spec",  "method": "Visual" },
        { "key": "other_defects", "label": "Lỗi khác (báo IPQC)",         "spec": "0",          "method": "Visual" }
      ]
    },
    {
      "id": "dimensions",
      "title": "Kích thước (1-6 chiều, từ bản vẽ)",
      "items": [
        { "key": "dim_1", "label": "Dim 1", "spec": "Theo bản vẽ", "unit": "mm", "method": "Caliper / Projector" },
        { "key": "dim_2", "label": "Dim 2", "spec": "Theo bản vẽ", "unit": "mm", "method": "Caliper / Projector" },
        { "key": "dim_3", "label": "Dim 3", "spec": "Theo bản vẽ", "unit": "mm", "method": "Caliper / Projector" },
        { "key": "dim_4", "label": "Dim 4", "spec": "Theo bản vẽ", "unit": "mm", "method": "Caliper / Projector" }
      ]
    },
    {
      "id": "judgment",
      "title": "Phán định",
      "items": [
        { "key": "accept", "label": "Chấp nhận / Loại bỏ", "spec": "Accept / Reject", "method": "Phán định cuối" }
      ]
    }
  ]
}
""";

    /// <summary>OQC default — 28 items in 5 sections (form CCL-10-F6 R04).
    /// Includes 8-element RoHS heavy-metal panel + 3-signature judgment
    /// section per the CCL Vietnam outgoing-inspection paper form.
    /// </summary>
    public const string OqcProfileJson = """
{
  "icon": "📦",
  "name": "Outgoing Quality Check (OQC)",
  "color": "#dc2626",
  "form": "CCL-10-F6 (R04)",
  "sections": [
    {
      "id": "info",
      "title": "Order Info",
      "items": [
        { "key": "customer",  "label": "Khách hàng (Customer)",       "method": "From PO" },
        { "key": "part_no",   "label": "Mã sản phẩm (Part No)",       "method": "From PO" },
        { "key": "part_name", "label": "Tên sản phẩm (Part Name)",    "method": "From spec" },
        { "key": "lot_no",    "label": "Lô số (Lot No)",              "method": "From WO" },
        { "key": "qty",       "label": "Số lượng (Quantity)",         "unit": "pcs", "method": "From WO" },
        { "key": "do_no",     "label": "Số phân phối (DO No)",        "method": "From DO" },
        { "key": "po_no",     "label": "Số đặt hàng (PO No)",         "method": "From PO" }
      ]
    },
    {
      "id": "dimensions",
      "title": "Kích thước (Dimension) — chỉ kiểm D/R cho hàng MP",
      "items": [
        { "key": "dim_1", "label": "Dim 1",            "spec": "± 0.5", "unit": "mm", "method": "Thước lá" },
        { "key": "dim_2", "label": "Dim 2",            "spec": "± 0.5", "unit": "mm", "method": "Thước lá" },
        { "key": "dim_3", "label": "Dim 3 (optional)", "spec": "± 0.5", "unit": "mm", "method": "Thước lá" },
        { "key": "dim_4", "label": "Dim 4 (optional)", "spec": "± 0.5", "unit": "mm", "method": "Thước lá" }
      ]
    },
    {
      "id": "appearance",
      "title": "Bề ngoài (Appearance) · AQL II / 0.4",
      "items": [
        { "key": "no_tear",       "label": "Không rách / nếp / bẩn / mẻ", "spec": "OK",         "method": "Visual" },
        { "key": "color_match",   "label": "Màu sắc",                     "spec": "OK",         "method": "Visual" },
        { "key": "joint_check",   "label": "Vị trí mối nối",              "spec": "OK",         "method": "Visual" },
        { "key": "logo_print",    "label": "Logo & vùng in",              "spec": "Theo spec",  "method": "Visual" },
        { "key": "other_defects", "label": "Lỗi khác",                    "spec": "0",          "method": "Visual" }
      ]
    },
    {
      "id": "rohs_halogen",
      "title": "RoHS & Halogen Compliance (ppm)",
      "items": [
        { "key": "cr_ppm", "label": "Cr (Chromium)",  "spec": "< 100",   "unit": "ppm", "method": "XRF / Lab cert" },
        { "key": "cl_ppm", "label": "Cl (Chlorine)",  "spec": "< 800",   "unit": "ppm", "method": "XRF / Lab cert" },
        { "key": "s_ppm",  "label": "S (Sulphur)",    "spec": "< 10000", "unit": "ppm", "method": "XRF / Lab cert" },
        { "key": "cd_ppm", "label": "Cd (Cadmium)",   "spec": "< 20",    "unit": "ppm", "method": "XRF / Lab cert" },
        { "key": "hg_ppm", "label": "Hg (Mercury)",   "spec": "< 100",   "unit": "ppm", "method": "XRF / Lab cert" },
        { "key": "pb_ppm", "label": "Pb (Lead)",      "spec": "< 100",   "unit": "ppm", "method": "XRF / Lab cert" },
        { "key": "sn_ppm", "label": "Sn (Tin)",       "spec": "< 800",   "unit": "ppm", "method": "XRF / Lab cert" },
        { "key": "sb_ppm", "label": "Sb (Antimony)",  "spec": "< 700",   "unit": "ppm", "method": "XRF / Lab cert" }
      ]
    },
    {
      "id": "judgment",
      "title": "Phán định 3-chữ-ký (Judgment)",
      "items": [
        { "key": "accept_reject", "label": "Chấp nhận / Loại bỏ",      "spec": "Accept / Reject", "method": "Phán định cuối" },
        { "key": "prepared_by",   "label": "Người kiểm (Prepared)",    "spec": "QC user",         "method": "Signature" },
        { "key": "confirm_by",    "label": "Xác nhận (Confirm)",       "spec": "QC Lead",         "method": "Signature" },
        { "key": "approved_by",   "label": "Phê duyệt (Approved)",     "spec": "QA Manager",      "method": "Signature" }
      ]
    }
  ]
}
""";

    /// <summary>Default item keys (in canonical declared order) per kind.
    /// Powers the controller's GET-time item synthesis so a freshly-
    /// materialised check returns N profile items as Pending — operator
    /// has something to tap, judgment row gates on actual completion.</summary>
    public static IReadOnlyList<string> GetDefaultItemKeys(string kind)
    {
        var json = GetDefaultProfileJson(kind);
        if (string.IsNullOrEmpty(json)) return Array.Empty<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("sections", out var sections))
                return Array.Empty<string>();
            var keys = new List<string>();
            foreach (var section in sections.EnumerateArray())
            {
                if (!section.TryGetProperty("items", out var items)) continue;
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("key", out var k) && k.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var key = k.GetString();
                        if (!string.IsNullOrEmpty(key)) keys.Add(key);
                    }
                }
            }
            return keys;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
