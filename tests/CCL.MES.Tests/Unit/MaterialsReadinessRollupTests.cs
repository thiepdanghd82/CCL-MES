using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7b-1 — pure-helper coverage for the materials-readiness rollup.
/// Lessons applied:
///   * Legacy parity: "no snapshot" → HasSnapshot=false → caller leaves
///     WorkOrder.MaterialsReady bool untouched (zero-diff for pre-7b WOs).
///   * Forward state: every surface (materials + plate + cutter) must
///     be present AND OK for AllOk=true. Missing ANY surface → false.
/// </summary>
public sealed class MaterialsReadinessRollupTests
{
    private static WoMaterial Mat(PrepressCheckStatus s, int idx = 0) =>
        new() { BomLineIdx = idx, MaterialCode = "M" + idx, Status = s };

    private static WoPlateCheck Plate(PrepressCheckStatus s) =>
        new() { Status = s };

    private static WoCutterCheck Cutter(PrepressCheckStatus s) =>
        new() { Status = s };

    private static WoMaterial SpecialAccepted(int idx = 0) =>
        new()
        {
            BomLineIdx = idx, MaterialCode = "M" + idx,
            Status = PrepressCheckStatus.Ok,
            // Dấu của Special Accept: Ok mà VẪN có mã lý do. Đường Ok thường
            // luôn xoá NgReasonCode về null nên cặp này không sinh ra kiểu khác.
            NgReasonCode = "SC-COLOR", NgNote = "PD leader chấp nhận",
        };

    // ── CỔNG LÔ (mục 2) ─────────────────────────────────────────────

    /// <summary>
    /// Dòng Ok nhưng lô đang Rejected ⇒ WO KHÔNG được coi là sẵn sàng. Đây là
    /// ca "lô bị IQC đánh trượt SAU khi Pre-press đã gắn" — gắn một lần là ảnh
    /// chụp, rollup mới là chỗ soi lại.
    /// </summary>
    [Theory]
    [InlineData(nameof(MaterialLotStatus.Rejected))]
    [InlineData(nameof(MaterialLotStatus.Quarantine))]
    [InlineData(nameof(MaterialLotStatus.Expired))]
    [InlineData(null)]                                   // chưa gắn lô nào
    public void Dong_Ok_nhung_lo_khong_Released_thi_KHONG_san_sang(string? lotStatus)
    {
        var mats = new[] { Mat(PrepressCheckStatus.Ok) };

        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(
            mats, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok),
            _ => lotStatus);

        Assert.True(hasSnap);
        Assert.False(allOk);
    }

    [Fact]
    public void Dong_Ok_va_lo_Released_thi_san_sang()
    {
        var mats = new[] { Mat(PrepressCheckStatus.Ok) };

        var (_, allOk) = MaterialsReadinessRollup.Compute(
            mats, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok),
            _ => nameof(MaterialLotStatus.Released));

        Assert.True(allOk);
    }

    /// <summary>
    /// Special Accept ĐƯỢC TÍNH LÀ ĐẠT dù lô xấu. Nó là đường xả đã có chữ ký
    /// (Engineer/Supervisor + lý do + audit); chặn thêm lần nữa ở rollup là
    /// double-gate và làm chính đường xả đó thành vô dụng.
    /// </summary>
    [Fact]
    public void Special_Accept_van_tinh_la_dat_du_lo_Rejected()
    {
        var mats = new[] { SpecialAccepted() };

        var (_, allOk) = MaterialsReadinessRollup.Compute(
            mats, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok),
            _ => nameof(MaterialLotStatus.Rejected));

        Assert.True(allOk);
    }

    /// <summary>Một dòng xấu là đủ chặn cả WO.</summary>
    [Fact]
    public void Mot_dong_lo_xau_giua_cac_dong_tot_van_chan_ca_WO()
    {
        var mats = new[] { Mat(PrepressCheckStatus.Ok, 0), Mat(PrepressCheckStatus.Ok, 1) };

        var (_, allOk) = MaterialsReadinessRollup.Compute(
            mats, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok),
            m => m.BomLineIdx == 1 ? nameof(MaterialLotStatus.Rejected)
                                   : nameof(MaterialLotStatus.Released));

        Assert.False(allOk);
    }

    /// <summary>
    /// Bán thành phẩm TỰ LÀM được miễn cổng lô: IQC chỉ phủ hàng MUA, nên
    /// chúng vĩnh viễn không có MaterialLot. Đo 2026-09-10: 848/1687 mã thiếu
    /// phiếu IQC là bán thành phẩm — không miễn thì ~một NỬA số dòng BOM chỉ
    /// còn đường Special Accept, tức bắt người vận hành ký khống mỗi ca.
    /// </summary>
    [Theory]
    [InlineData(nameof(MaterialLotStatus.Rejected))]
    [InlineData(null)]
    public void Ban_thanh_pham_duoc_mien_cong_lo(string? lotStatus)
    {
        var mats = new[] { Mat(PrepressCheckStatus.Ok) };

        var (_, allOk) = MaterialsReadinessRollup.Compute(
            mats, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok),
            _ => lotStatus, _ => true);          // ← là mã cha trong BOM

        Assert.True(allOk);
    }

    /// <summary>Miễn KHÔNG lan sang vật tư mua: cùng lô xấu, cùng WO, chỉ khác
    /// bản chất mã — dòng mua vẫn phải chặn.</summary>
    [Fact]
    public void Mien_khong_lan_sang_vat_tu_mua()
    {
        var mats = new[] { Mat(PrepressCheckStatus.Ok, 0), Mat(PrepressCheckStatus.Ok, 1) };

        var (_, allOk) = MaterialsReadinessRollup.Compute(
            mats, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok),
            _ => nameof(MaterialLotStatus.Rejected),
            m => m.BomLineIdx == 0);            // chỉ dòng 0 là bán thành phẩm

        Assert.False(allOk);                    // dòng 1 là vật tư mua ⇒ vẫn chặn
    }

    /// <summary>Miễn cổng lô KHÔNG có nghĩa miễn xác nhận: chưa Ok thì vẫn chưa
    /// sẵn sàng, dù là bán thành phẩm.</summary>
    [Fact]
    public void Ban_thanh_pham_chua_Ok_thi_van_chua_san_sang()
    {
        var mats = new[] { Mat(PrepressCheckStatus.Pending) };

        var (_, allOk) = MaterialsReadinessRollup.Compute(
            mats, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok),
            _ => null, _ => true);

        Assert.False(allOk);
    }

    /// <summary>Quá tải 3 tham số giữ NGUYÊN hợp đồng cũ — không soi lô. WO
    /// trước 7b không có dữ liệu lô, ép cổng lô lên chúng là chặn oan.</summary>
    [Fact]
    public void Qua_tai_ba_tham_so_KHONG_soi_lo()
    {
        var mats = new[] { Mat(PrepressCheckStatus.Ok) };

        var (_, allOk) = MaterialsReadinessRollup.Compute(
            mats, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok));

        Assert.True(allOk);   // không có lô nào, vẫn đạt — đúng như trước
    }

    // ── No snapshot → legacy bool stays authoritative ───────────────

    [Fact]
    public void No_rows_at_all_returns_HasSnapshot_false()
    {
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(null, null, null);
        Assert.False(hasSnap);
        Assert.False(allOk);
    }

    [Fact]
    public void Empty_materials_list_with_null_plate_and_cutter_returns_HasSnapshot_false()
    {
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(Array.Empty<WoMaterial>(), null, null);
        Assert.False(hasSnap);
        Assert.False(allOk);
    }

    // ── Partial snapshots → HasSnapshot=true, AllOk=false ──────────

    [Fact]
    public void Only_plate_present_returns_HasSnapshot_true_AllOk_false()
    {
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(null, Plate(PrepressCheckStatus.Ok), null);
        Assert.True(hasSnap);
        Assert.False(allOk);
    }

    [Fact]
    public void Materials_OK_but_no_plate_or_cutter_returns_AllOk_false()
    {
        var rows = new[] { Mat(PrepressCheckStatus.Ok, 0), Mat(PrepressCheckStatus.Ok, 1) };
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(rows, null, null);
        Assert.True(hasSnap);
        Assert.False(allOk);
    }

    [Fact]
    public void All_three_surfaces_present_but_one_material_pending_returns_AllOk_false()
    {
        var rows = new[]
        {
            Mat(PrepressCheckStatus.Ok, 0),
            Mat(PrepressCheckStatus.Pending, 1),    // ← bites
        };
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(
            rows, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok));
        Assert.True(hasSnap);
        Assert.False(allOk);
    }

    [Fact]
    public void All_three_surfaces_present_but_one_material_NG_returns_AllOk_false()
    {
        var rows = new[]
        {
            Mat(PrepressCheckStatus.Ok, 0),
            Mat(PrepressCheckStatus.Ng, 1),
        };
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(
            rows, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok));
        Assert.True(hasSnap);
        Assert.False(allOk);
    }

    [Fact]
    public void Plate_NG_makes_rollup_false_even_if_materials_and_cutter_OK()
    {
        var rows = new[] { Mat(PrepressCheckStatus.Ok, 0) };
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(
            rows, Plate(PrepressCheckStatus.Ng), Cutter(PrepressCheckStatus.Ok));
        Assert.True(hasSnap);
        Assert.False(allOk);
    }

    [Fact]
    public void Cutter_PENDING_makes_rollup_false_even_if_materials_and_plate_OK()
    {
        var rows = new[] { Mat(PrepressCheckStatus.Ok, 0) };
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(
            rows, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Pending));
        Assert.True(hasSnap);
        Assert.False(allOk);
    }

    // ── All OK → AllOk=true ────────────────────────────────────────

    [Fact]
    public void All_surfaces_OK_returns_AllOk_true()
    {
        var rows = new[]
        {
            Mat(PrepressCheckStatus.Ok, 0),
            Mat(PrepressCheckStatus.Ok, 1),
            Mat(PrepressCheckStatus.Ok, 2),
        };
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(
            rows, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok));
        Assert.True(hasSnap);
        Assert.True(allOk);
    }

    // ── Edge: 0 materials rows but plate + cutter OK → still false ──
    //    Operational case: legacy WO with empty BOM (no MS rows).
    //    Operator must explicitly add materials rows via 7b-2 endpoint
    //    OR confirm there genuinely are no materials (empty BOM
    //    products — rare). Default = require at least one materials row.

    [Fact]
    public void Empty_materials_with_plate_and_cutter_OK_returns_AllOk_false()
    {
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(
            Array.Empty<WoMaterial>(), Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok));
        Assert.True(hasSnap);
        Assert.False(allOk);
    }
}
