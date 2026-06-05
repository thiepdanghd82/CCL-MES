using System.Text.Json;
using CCL.MES.Api.Audit;
using Xunit;

namespace CCL.MES.Api.Tests.Audit;

/// <summary>
/// P10.7a-1 — <see cref="AuditEmitHelper"/> enforces the canonical
/// JSON envelope (wo_id, wo_no, shift_code, from_phase, to_phase, ok)
/// + reason-field length cap + UTC→VN shift-code derivation per
/// contract §7.2 / §7.3 / §4.4.
/// </summary>
public sealed class AuditEmitHelperTests
{
    // ── BuildDetail: required envelope keys ──────────────────────────

    [Fact]
    public void BuildDetail_emits_all_six_required_keys_even_when_null()
    {
        var json = AuditEmitHelper.BuildDetail(
            woId: 42,
            woNo: "WO-26-2852",
            shiftCode: null,
            fromPhase: null,
            toPhase: null,
            ok: true);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(42, root.GetProperty("wo_id").GetInt64());
        Assert.Equal("WO-26-2852", root.GetProperty("wo_no").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("shift_code").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("from_phase").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("to_phase").ValueKind);
        Assert.True(root.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void BuildDetail_merges_extra_keys_on_top()
    {
        var extra = new Dictionary<string, object?>
        {
            ["op_user_id"] = 5,
            ["duration_sec"] = 142,
        };
        var json = AuditEmitHelper.BuildDetail(
            woId: 1, woNo: "WO-A",
            shiftCode: "A", fromPhase: "SETTING", toPhase: "IPQC_WAIT",
            ok: true, extra: extra);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(5,   doc.RootElement.GetProperty("op_user_id").GetInt64());
        Assert.Equal(142, doc.RootElement.GetProperty("duration_sec").GetInt64());
        Assert.Equal("A", doc.RootElement.GetProperty("shift_code").GetString());
    }

    // ── Reason-field truncation per §7.3 ─────────────────────────────

    [Fact]
    public void Reason_field_truncated_at_500_chars()
    {
        var longReason = new string('x', 600);
        var extra = new Dictionary<string, object?>
        {
            ["reason"] = longReason,
        };
        var json = AuditEmitHelper.BuildDetail(
            woId: 1, woNo: "WO-A",
            shiftCode: "A", fromPhase: "IPQC_WAIT", toPhase: "QA_PENDING",
            ok: true, extra: extra);

        using var doc = JsonDocument.Parse(json);
        var stored = doc.RootElement.GetProperty("reason").GetString();
        Assert.NotNull(stored);
        Assert.Equal(500, stored!.Length);
    }

    [Fact]
    public void Note_field_also_truncated()
    {
        var longNote = new string('y', 800);
        var extra = new Dictionary<string, object?>
        {
            ["note"] = longNote,
        };
        var json = AuditEmitHelper.BuildDetail(
            woId: 1, woNo: "WO-A",
            shiftCode: "A", fromPhase: "RUNNING", toPhase: "PAUSED",
            ok: true, extra: extra);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(500, doc.RootElement.GetProperty("note").GetString()!.Length);
    }

    [Fact]
    public void Non_reason_string_fields_left_untouched()
    {
        var longCode = new string('z', 600);
        var extra = new Dictionary<string, object?>
        {
            ["reason_code"] = longCode,
        };
        var json = AuditEmitHelper.BuildDetail(
            woId: 1, woNo: "WO-A",
            shiftCode: "B", fromPhase: "RUNNING", toPhase: "PAUSED",
            ok: true, extra: extra);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(600, doc.RootElement.GetProperty("reason_code").GetString()!.Length);
    }

    // ── ComputeShiftCode — VN-local 3-shift derivation ───────────────

    // CCL plant is UTC+7. Shift A 06:00-14:00 / B 14:00-22:00 / C 22:00-06:00.
    [Theory]
    [InlineData(2026, 06, 05, 00, 00, "A")]  // 07:00 VN = Shift A
    [InlineData(2026, 06, 05, 02, 00, "A")]  // 09:00 VN = Shift A
    [InlineData(2026, 06, 05, 06, 00, "A")]  // 13:00 VN = Shift A
    [InlineData(2026, 06, 05, 07, 00, "B")]  // 14:00 VN = Shift B
    [InlineData(2026, 06, 05, 14, 00, "B")]  // 21:00 VN = Shift B
    [InlineData(2026, 06, 05, 15, 00, "C")]  // 22:00 VN = Shift C
    [InlineData(2026, 06, 05, 22, 00, "C")]  // 05:00 next-day VN = Shift C
    [InlineData(2026, 06, 05, 23, 30, "A")]  // 06:30 next-day VN = Shift A starts at 06
    public void ComputeShiftCode_picks_correct_shift_for_VN_local_time(
        int y, int mo, int d, int h, int mi, string expected)
    {
        var utc = new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc);
        Assert.Equal(expected, AuditEmitHelper.ComputeShiftCode(utc));
    }
}
