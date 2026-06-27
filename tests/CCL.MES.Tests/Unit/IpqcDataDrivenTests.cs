using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Phương án C — Bước 2. IPQC data-driven (shadow). Khóa: (a) rollup ưu tiên
/// Items khi có, lùi 4-slot legacy khi rỗng (PARITY); (b) materializer dựng
/// snapshot + items đúng từ thư viện; (c) SetItem theo key. KHÔNG đụng 4-slot
/// legacy (IpqcLegacyParityTests + IpqcReadinessRollupTests vẫn xanh).
/// </summary>
public sealed class IpqcDataDrivenTests
{
    private static WoIpqcCheckItem Item(string key, IpqcCheckStatus s, string line = "LABEL") =>
        new() { ItemKey = key, Status = s, ProcessLine = line };

    private static CheckItemLibrary Lib(string id, string line, string group, string code,
        string itemVi, string accVi, string? defect = null, int sort = 0) =>
        new()
        {
            ItemId = id, ProcessLine = line, GroupLabel = group, Code = code,
            ItemVi = itemVi, ItemEn = itemVi + " (EN)", AcceptanceVi = accVi, AcceptanceEn = accVi,
            Method = "Visual", Severity = "Critical", DefectCode = defect, Active = true, Sort = sort,
        };

    // ── Rollup: items-aware vs legacy fallback (PARITY) ──────────────

    [Fact]
    public void Rollup_falls_back_to_legacy_slots_when_no_items()
    {
        // 4 slot cũ: 3 Ok + 1 Pending → chưa ready (đúng hành vi legacy).
        var check = new WoIpqcCheck
        {
            MaterialStatus = IpqcCheckStatus.Ok,
            PrintAStatus = IpqcCheckStatus.Ok,
            PrintBStatus = IpqcCheckStatus.Ok,
            PrintCStatus = IpqcCheckStatus.Pending,
        };
        var legacy = IpqcReadinessRollup.Compute(check);
        var withNullItems = IpqcReadinessRollup.Compute(check, null);
        var withEmptyItems = IpqcReadinessRollup.Compute(check, Array.Empty<WoIpqcCheckItem>());
        Assert.Equal(legacy, withNullItems);
        Assert.Equal(legacy, withEmptyItems);
        Assert.False(legacy.IsReadyForJudgment);
    }

    [Fact]
    public void Rollup_uses_items_when_present_ignoring_legacy_slots()
    {
        // 4 slot legacy đều Pending NHƯNG có items đều Ok → ready + allOk.
        var check = new WoIpqcCheck(); // slots all Pending
        var items = new[] { Item("LBL-A1", IpqcCheckStatus.Ok), Item("LBL-A2", IpqcCheckStatus.Ok) };
        var (ready, allOk, anyNg) = IpqcReadinessRollup.Compute(check, items);
        Assert.True(ready);
        Assert.True(allOk);
        Assert.False(anyNg);
    }

    [Fact]
    public void Rollup_items_anyNg_blocks_goRun_consistency()
    {
        var check = new WoIpqcCheck();
        var items = new[]
        {
            Item("LBL-A1", IpqcCheckStatus.Ok),
            Item("LBL-A2", IpqcCheckStatus.Ng),
        };
        var (ready, allOk, anyNg) = IpqcReadinessRollup.Compute(check, items);
        Assert.True(ready);
        Assert.False(allOk);
        Assert.True(anyNg);
        Assert.False(IpqcReadinessRollup.IsJudgmentConsistent(check, items, IpqcJudgment.GoRun));
        Assert.True(IpqcReadinessRollup.IsJudgmentConsistent(check, items, IpqcJudgment.SpecialAccept));
        Assert.True(IpqcReadinessRollup.IsJudgmentConsistent(check, items, IpqcJudgment.StopLine));
    }

    [Fact]
    public void Rollup_items_pending_is_not_ready()
    {
        var check = new WoIpqcCheck();
        var items = new[] { Item("A", IpqcCheckStatus.Ok), Item("B", IpqcCheckStatus.Pending) };
        var (ready, _, _) = IpqcReadinessRollup.Compute(check, items);
        Assert.False(ready);
    }

    // ── Materializer ─────────────────────────────────────────────────

    [Fact]
    public void Materializer_builds_items_and_snapshot_in_line_order()
    {
        var rows = new[]
        {
            Lib("PCC-B1", "PRESS_CNC", "B·Cắt", "B1", "Đường cắt", "Đúng dao", "CUTLINE", 20),
            Lib("LBL-A1", "LABEL", "A·Ngoại quan", "A1", "Nội dung", "Đúng nội dung", "CONTENT", 10),
            Lib("LBL-A2", "LABEL", "A·Ngoại quan", "A2", "Màu sắc", "ΔE≤2", "COLOR", 20),
        };
        var result = IpqcLibraryMaterializer.Build(rows, new[] { "LABEL", "PRESS_CNC" });

        // Items theo thứ tự line (LABEL trước PRESS_CNC), key = ItemId.
        Assert.Equal(new[] { "LBL-A1", "LBL-A2", "PCC-B1" }, result.Items.Select(i => i.ItemKey));
        Assert.All(result.Items, i => Assert.Equal(IpqcCheckStatus.Pending, i.Status));
        Assert.Equal("LABEL", result.Items[0].ProcessLine);
        Assert.Equal("Nội dung", result.Items[0].Label);
        Assert.Equal("Đúng nội dung", result.Items[0].AcceptanceCriteria);
        Assert.Equal("CONTENT", result.Items[0].DefectCode);

        // Snapshot là JSON hợp lệ, đếm item = 3, có "lines".
        Assert.Equal(3, QcProfileSeed.CountItems(result.ProfileSnapshotJson));
        Assert.Contains("LABEL,PRESS_CNC", result.ProfileSnapshotJson);
        using var doc = System.Text.Json.JsonDocument.Parse(result.ProfileSnapshotJson);
        Assert.True(doc.RootElement.TryGetProperty("sections", out _));
    }

    [Fact]
    public void Materializer_skips_inactive_rows()
    {
        var active = Lib("LBL-A1", "LABEL", "A", "A1", "x", "y");
        var inactive = Lib("LBL-A9", "LABEL", "A", "A9", "z", "w");
        inactive.Active = false;
        var result = IpqcLibraryMaterializer.Build(new[] { active, inactive }, new[] { "LABEL" });
        Assert.Single(result.Items);
        Assert.Equal("LBL-A1", result.Items[0].ItemKey);
    }

    [Fact]
    public void Materializer_empty_yields_no_items()
    {
        var result = IpqcLibraryMaterializer.Build(Array.Empty<CheckItemLibrary>(), Array.Empty<string>());
        Assert.Empty(result.Items);
        Assert.Equal(0, QcProfileSeed.CountItems(result.ProfileSnapshotJson));
    }

    // ── SetItem ──────────────────────────────────────────────────────

    [Fact]
    public void SetItem_sets_status_and_clears_ng_on_ok()
    {
        var check = new WoIpqcCheck();
        check.Items.Add(Item("LBL-A1", IpqcCheckStatus.Pending));
        var ok = WoIpqcCheckService.SetItem(check, "lbl-a1", IpqcCheckStatus.Ng,
            "CONTENT", "sai nội dung", "qc1", DateTime.UnixEpoch);
        Assert.True(ok);
        var it = check.Items.First();
        Assert.Equal(IpqcCheckStatus.Ng, it.Status);
        Assert.Equal("CONTENT", it.NgReasonCode);
        // chuyển sang OK → clear NG
        WoIpqcCheckService.SetItem(check, "LBL-A1", IpqcCheckStatus.Ok, null, null, "qc1", DateTime.UnixEpoch);
        Assert.Equal(IpqcCheckStatus.Ok, it.Status);
        Assert.Null(it.NgReasonCode);
        Assert.Null(it.NgNote);
    }

    [Fact]
    public void SetItem_returns_false_for_unknown_key()
    {
        var check = new WoIpqcCheck();
        check.Items.Add(Item("LBL-A1", IpqcCheckStatus.Pending));
        Assert.False(WoIpqcCheckService.SetItem(check, "NOPE", IpqcCheckStatus.Ok,
            null, null, "qc1", DateTime.UnixEpoch));
    }
}
