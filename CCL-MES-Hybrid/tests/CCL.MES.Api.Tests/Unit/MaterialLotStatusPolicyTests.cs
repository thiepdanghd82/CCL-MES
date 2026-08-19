using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Api.Tests.Unit;

/// <summary>
/// A1 — luật vòng đời lô ở dạng hàm thuần. Chạy trong vài mili-giây, không DB,
/// không HTTP; nhờ vậy mọi tổ hợp trạng thái × hạn dùng × vai đều phủ được chứ
/// không chỉ vài ca "đẹp".
/// </summary>
public sealed class MaterialLotStatusPolicyTests
{
    private static readonly DateTime Now = new(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc);

    private static MaterialLot Lot(
        string status = nameof(MaterialLotStatus.Released),
        string partNo = "PVC-50", double qty = 100, DateTime? expiry = null,
        string? retestedBy = null) => new()
        {
            Id = 1, LotNo = "LOT-1", PartNo = partNo, Status = status,
            QtyAvailable = qty, QtyReceived = qty, ExpiryAt = expiry, RetestedBy = retestedBy,
        };

    // ── Thứ tự kiểm (§5) ───────────────────────────────────────────

    [Fact]
    public void Part_mismatch_wins_over_every_status()
    {
        // Quét nhầm lô của vật tư khác là lỗi phổ biến nhất trên sàn. Nếu status
        // được báo trước, operator đi tìm QC trong khi vấn đề thật là cầm nhầm
        // cuộn. Bốn trạng thái, cùng một kết luận.
        foreach (var st in new[] { "Quarantine", "Rejected", "Expired", "Consumed" })
        {
            var v = MaterialLotStatusPolicy.CanConsume(
                Lot(status: st, partNo: "OTHER"), "PVC-50", 1, Now);
            Assert.Equal(MaterialLotStatusPolicy.PartMismatch, v.ErrorCode);
        }
    }

    [Fact]
    public void Rejected_is_reported_before_expiry()
    {
        var v = MaterialLotStatusPolicy.CanConsume(
            Lot(status: "Rejected", expiry: Now.AddDays(-10)), "PVC-50", 1, Now);
        Assert.Equal(MaterialLotStatusPolicy.Rejected, v.ErrorCode);
    }

    [Fact]
    public void Expiry_in_past_blocks_a_Released_lot()
    {
        var v = MaterialLotStatusPolicy.CanConsume(
            Lot(expiry: Now.AddSeconds(-1)), "PVC-50", 1, Now);
        Assert.Equal(MaterialLotStatusPolicy.Expired, v.ErrorCode);
    }

    [Fact]
    public void Quarantine_reports_not_released()
    {
        var v = MaterialLotStatusPolicy.CanConsume(Lot(status: "Quarantine"), "PVC-50", 1, Now);
        Assert.Equal(MaterialLotStatusPolicy.NotReleased, v.ErrorCode);
    }

    [Theory]
    [InlineData(0, 1)]      // hết sạch
    [InlineData(5, 5.1)]    // xin nhiều hơn tồn
    public void Insufficient_quantity_reports_depleted(double available, double requested)
    {
        var v = MaterialLotStatusPolicy.CanConsume(Lot(qty: available), "PVC-50", requested, Now);
        Assert.Equal(MaterialLotStatusPolicy.Depleted, v.ErrorCode);
    }

    [Fact]
    public void Released_lot_with_stock_is_allowed()
    {
        Assert.True(MaterialLotStatusPolicy.CanConsume(Lot(), "PVC-50", 100, Now).Allowed);
    }

    [Fact]
    public void CheckQuantity_blocks_an_exhausted_lot_independently_of_status()
    {
        // Gọi độc lập được là điều kiện để grace period KHÔNG nới nhầm ràng buộc
        // tồn kho: lô 'Consumed' vốn ra not_released ở CanConsume, còn hàm này
        // nói thẳng là hết hàng.
        var v = MaterialLotStatusPolicy.CheckQuantity(Lot(status: "Consumed", qty: 0), 1);
        Assert.Equal(MaterialLotStatusPolicy.Depleted, v.ErrorCode);
    }

    [Fact]
    public void Part_match_ignores_letter_case()
    {
        // Cùng ngữ nghĩa với COLLATE NOCASE ở cột — nếu hàm này phân biệt hoa
        // thường thì DB và C# bất đồng, và bất đồng đó chỉ lộ ra trên sàn.
        Assert.True(MaterialLotStatusPolicy.CanConsume(Lot(partNo: "pvc-50"), "PVC-50", 1, Now).Allowed);
    }

    // ── Chuẩn hoá (lớp 3) ──────────────────────────────────────────

    [Theory]
    [InlineData("  LOT-1  ", "LOT-1")]
    [InlineData("\tLOT-1\n", "LOT-1")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalize_trims_only(string? raw, string expected)
        => Assert.Equal(expected, MaterialLotStatusPolicy.Normalize(raw));

    [Fact]
    public void Normalize_does_not_change_letter_case()
    {
        // Cố ý: kiểu chữ trên nhãn nhà cung cấp là dữ liệu, không phải nhiễu.
        // So khớp không phân biệt hoa thường đã do NOCASE ở cột đảm nhiệm.
        Assert.Equal("LoT-AbC", MaterialLotStatusPolicy.Normalize(" LoT-AbC "));
    }

    // ── Đảo tiêu thụ (Đ3) ──────────────────────────────────────────

    [Fact]
    public void Reversal_returns_Consumed_lot_to_Released_when_qty_above_zero()
        => Assert.Equal(nameof(MaterialLotStatus.Released),
            MaterialLotStatusPolicy.StatusAfterReversal(Lot(status: "Consumed"), 5));

    [Fact]
    public void Reversal_keeps_Consumed_when_qty_still_zero()
        => Assert.Equal(nameof(MaterialLotStatus.Consumed),
            MaterialLotStatusPolicy.StatusAfterReversal(Lot(status: "Consumed"), 0));

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Expired")]
    [InlineData("Quarantine")]
    public void Reversal_never_upgrades_a_terminal_or_held_lot(string status)
        => Assert.Equal(status, MaterialLotStatusPolicy.StatusAfterReversal(Lot(status: status), 99));

    // ── Gia hạn hai chữ ký (Đ3) ────────────────────────────────────

    [Fact]
    public void Extension_requires_the_lot_to_be_expired()
    {
        var v = MaterialLotStatusPolicy.CanExtendExpiry(
            Lot(retestedBy: "qc1"), "qc2", Now.AddDays(30), Now);
        Assert.Equal(MaterialLotStatusPolicy.NotExpired, v.ErrorCode);
    }

    [Fact]
    public void Extension_requires_a_recorded_retest()
    {
        var v = MaterialLotStatusPolicy.CanExtendExpiry(
            Lot(status: "Expired", expiry: Now.AddDays(-1)), "qc2", Now.AddDays(30), Now);
        Assert.Equal(MaterialLotStatusPolicy.NotRetested, v.ErrorCode);
    }

    [Theory]
    [InlineData("qc1")]
    [InlineData("QC1")]     // đổi kiểu chữ KHÔNG được lách luật tách vai
    [InlineData("Qc1")]
    public void Extension_rejects_the_same_person_signing_twice(string approver)
    {
        var v = MaterialLotStatusPolicy.CanExtendExpiry(
            Lot(status: "Expired", expiry: Now.AddDays(-1), retestedBy: "qc1"),
            approver, Now.AddDays(30), Now);
        Assert.Equal(MaterialLotStatusPolicy.SameSigner, v.ErrorCode);
    }

    [Fact]
    public void Extension_accepts_two_distinct_signers()
    {
        var v = MaterialLotStatusPolicy.CanExtendExpiry(
            Lot(status: "Expired", expiry: Now.AddDays(-1), retestedBy: "qc1"),
            "qc2", Now.AddDays(30), Now);
        Assert.True(v.Allowed);
    }

    [Fact]
    public void Extension_rejects_a_new_expiry_in_the_past()
    {
        var v = MaterialLotStatusPolicy.CanExtendExpiry(
            Lot(status: "Expired", expiry: Now.AddDays(-1), retestedBy: "qc1"),
            "qc2", Now.AddDays(-1), Now);
        Assert.Equal(MaterialLotStatusPolicy.InvalidRequest, v.ErrorCode);
    }

    [Fact]
    public void Rejected_lot_can_never_be_extended()
    {
        var v = MaterialLotStatusPolicy.CanExtendExpiry(
            Lot(status: "Rejected", expiry: Now.AddDays(-1), retestedBy: "qc1"),
            "qc2", Now.AddDays(30), Now);
        Assert.Equal(MaterialLotStatusPolicy.Rejected, v.ErrorCode);
    }

    // ── Parse trạng thái ───────────────────────────────────────────

    [Theory]
    [InlineData("released", "Released")]
    [InlineData("  QUARANTINE ", "Quarantine")]
    [InlineData("Expired", "Expired")]
    public void ParseStatus_accepts_any_case_and_returns_canonical(string raw, string expected)
        => Assert.Equal(expected, MaterialLotStatusPolicy.ParseStatus(raw));

    [Theory]
    [InlineData("bogus")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseStatus_rejects_anything_else(string? raw)
        => Assert.Null(MaterialLotStatusPolicy.ParseStatus(raw));

    // ── Cờ grace period ────────────────────────────────────────────

    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("ON", true)]
    [InlineData("yes", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("truu", false)]   // gõ nhầm KHÔNG được tự bật ⇒ không dừng nhà máy
    public void EnforceReleased_defaults_off_and_only_explicit_on_tokens_enable_it(
        string? raw, bool expected)
        => Assert.Equal(expected, MaterialLotOptionsLoader.ParseEnforceReleased(raw));
}
