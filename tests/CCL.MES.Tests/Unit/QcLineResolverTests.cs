using CCL.MES.Application.Services;
using Xunit;
using Op = CCL.MES.Application.Services.QcLineResolver.RoutingOp;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Phương án C — Bước 3/6 (data-driven). Resolver giờ tra bảng map
/// (<see cref="ProcessLineMapSeed"/> qua <see cref="QcLineResolver.MapFromSeed"/>) —
/// KHÔNG keyword hardcode. Khóa phân loại bằng routing THẬT 8064xxxx + đóng finding #7
/// (mã WC 'SS01'/'SSX' KHÔNG còn rơi vào SILK).
/// </summary>
public sealed class QcLineResolverTests
{
    private static readonly IReadOnlyList<QcLineResolver.MapEntry> Map = QcLineResolver.MapFromSeed();

    private static string Classify(string? op, string wc, string? wcDesc = null) =>
        QcLineResolver.Classify(new Op(null, op, wc, wcDesc), Map);

    // ── Phân loại theo WorkCenterPrefix (máy thật) ──────────────────

    [Theory]
    [InlineData("GFL01", QcLineResolver.Label)]
    [InlineData("BFL01", QcLineResolver.Label)]
    [InlineData("IDG01", QcLineResolver.Digital)]
    [InlineData("ASS08", QcLineResolver.Silk)]
    [InlineData("MSS01", QcLineResolver.Silk)]
    [InlineData("R2SC3", QcLineResolver.Silk)]      // SheetCut(SS) — quyết định #5
    [InlineData("FBL02", QcLineResolver.PressCnc)]
    [InlineData("PPSC1", QcLineResolver.PressCnc)]
    [InlineData("RDC12", QcLineResolver.PressCnc)]
    [InlineData("PUNC1", QcLineResolver.PressCnc)]
    [InlineData("MDRH1", QcLineResolver.PressCnc)]
    [InlineData("LAML1", QcLineResolver.Label)]
    [InlineData("LAMR3", QcLineResolver.Label)]
    [InlineData("MAGSS", QcLineResolver.Silk)]      // longest-match MAGSS→SILK, không phải MAG→LABEL
    [InlineData("FXPP1", QcLineResolver.None)]
    [InlineData("OVS1", QcLineResolver.None)]
    [InlineData("UVS1", QcLineResolver.None)]
    [InlineData("MAN1", QcLineResolver.None)]
    [InlineData("MAN2", QcLineResolver.None)]
    [InlineData("MAN3", QcLineResolver.None)]
    public void Classify_by_workcenter_prefix(string wc, string expected)
        => Assert.Equal(expected, Classify("any op", wc));

    // ── Finding #7: 'SS' rộng đã bị gỡ → SS01/SSX KHÔNG còn là SILK ──

    [Theory]
    [InlineData("SS01")]
    [InlineData("SSX")]
    [InlineData("SS7")]
    public void Bare_SS_prefix_no_longer_classifies_silk(string wc)
        => Assert.Equal(QcLineResolver.Unmapped, Classify("mystery op", wc));

    // ── OpKeyword dự phòng (WC prefix lạ, khớp Operation/WC-desc) ────

    [Fact]
    public void Indigo_keyword_classifies_digital_when_wc_prefix_unknown()
        => Assert.Equal(QcLineResolver.Digital, Classify("(HP INDIGO) PRINT", "ZZZ9", "unknown rig"));

    [Fact]
    public void Silk_wcdesc_keyword_classifies_silk_when_wc_prefix_unknown()
        => Assert.Equal(QcLineResolver.Silk, Classify("print", "ZZZ9", "SS(Sheet)"));

    // ── WC hoàn toàn lạ → Unmapped (loud, không đoán) ───────────────

    [Fact]
    public void Unknown_workcenter_and_op_is_unmapped()
        => Assert.Equal(QcLineResolver.Unmapped, Classify("QUANTUM TELEPORT", "NGF1", "NextGen rig"));

    [Fact]
    public void Empty_map_yields_unmapped()
        => Assert.Equal(QcLineResolver.Unmapped,
            QcLineResolver.Classify(new Op(null, "x", "GFL01", null), System.Array.Empty<QcLineResolver.MapEntry>()));

    // ── Resolve toàn routing thật ───────────────────────────────────

    private static QcLineResolver.Resolution Resolve(params Op[] ops) => QcLineResolver.Resolve(ops, Map);

    [Fact]
    public void Resolve_80644935_label_flexo_plus_cut()
    {
        var r = Resolve(
            new Op("10", "PRE- PREPARE", "FXPP1", "Pre-press"),
            new Op("20", "(GALLUS) PRINT", "GFL01", "Flexo (Gallus 4C)"),
            new Op("27", "(BROTECH) PRINT", "BFL01", "Flexo (Brotech)"),
            new Op("30", "(RDC) LAM.&Cut", "RDC12", "RDC12(350)"),
            new Op("50", "FQC & PACKING", "MAN1", "FQC & Packaging"),
            new Op("60", "OQC Inspection", "MAN2", "OQC"));
        Assert.Equal(new[] { QcLineResolver.Label, QcLineResolver.PressCnc }, r.Lines);
        Assert.Empty(r.Unmapped);
    }

    [Fact]
    public void Resolve_silk_plus_cut_prepress_is_none_not_unmapped()
    {
        var r = Resolve(
            new Op("10", "PRE- PREPARE", "FXPP1", "Pre-press"),
            new Op("20", "SILK SEMI_AUTO SHEET", "ASS08", "SS-Auto(Sheet)"),
            new Op("40", "BAKING (SHEET)", "OVS1", "Oven drying"),
            new Op("260", "(PRESS) CUT", "PPSC1", "Power press"),
            new Op("280", "FQC & PACKING", "MAN1", "FQC & Packaging"),
            new Op("290", "OQC Inspection", "MAN2", "OQC"));
        Assert.Contains(QcLineResolver.Silk, r.Lines);
        Assert.Contains(QcLineResolver.PressCnc, r.Lines);
        Assert.Empty(r.Unmapped); // pre-press/baking/FQC/OQC = NONE, không Unmapped
    }

    [Fact]
    public void Resolve_collects_unmapped_for_unknown_machine()
    {
        var r = Resolve(
            new Op("10", "(GALLUS) PRINT", "GFL01", "Flexo"),
            new Op("20", "MYSTERY", "NGF1", "NextGen"));
        Assert.Equal(new[] { QcLineResolver.Label }, r.Lines);
        var u = Assert.Single(r.Unmapped);
        Assert.Contains("NGF1", u);
    }

    [Fact]
    public void Lines_order_is_stable_label_digital_silk_presscnc()
    {
        var r = Resolve(
            new Op("1", "(PRESS) CUT", "PPSC1", "Power press"),
            new Op("2", "SILK", "ASS08", "SS-Auto"),
            new Op("3", "INDIGO", "IDG01", "Indigo6800"),
            new Op("4", "FLEXO", "GFL01", "Flexo"));
        Assert.Equal(
            new[] { QcLineResolver.Label, QcLineResolver.Digital, QcLineResolver.Silk, QcLineResolver.PressCnc },
            r.Lines);
    }
}
