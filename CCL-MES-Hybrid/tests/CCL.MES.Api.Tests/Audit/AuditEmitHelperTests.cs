using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using CCL.MES.Infrastructure;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Audit;
using CCL.MES.Api.Observability;
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
public sealed class AuditEmitHelperTests : IDisposable
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

    // ComputeShiftCode coverage removed in Đợt 1 C3 alongside the function.
    // It was the only caller the function ever had. Shift derivation returns
    // in Đợt 3 on a data-driven ShiftCalendar and will be tested against
    // that, not against a hardcoded UTC+7 06/14/22 split.

    // ══════════════════════════════════════════════════════════════════
    // AuditLogs.Source phải nói ĐÚNG kênh phát sinh sự kiện
    //
    // Nằm chung lớp này CÓ CHỦ Ý: xUnit cho mỗi LỚP một làn song song, và
    // thêm một làn nữa đã làm ĐỎ 5 test của MaterialLotScanTests +
    // BackupControllerTests (chúng dùng chung một file SQLite qua
    // MesApiFactory và không an toàn khi tăng đồng thời). Test ở đây không
    // đụng dữ liệu của ai — nó chỉ không được phép thêm làn.
    // ══════════════════════════════════════════════════════════════════

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"ccl-audit-src-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<MesDbContext> _opt;

    public AuditEmitHelperTests()
    {
        _opt = new DbContextOptionsBuilder<MesDbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options;
        using var db = new MesDbContext(_opt);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* dọn best-effort */ }
    }

    private (ApiAuditWriter Writer, MesDbContext Db) Make()
    {
        var db = new MesDbContext(_opt);
        var writer = new ApiAuditWriter(
            db,
            new HttpContextAccessor(),
            new MesRequestContext(),
            NullLogger<ApiAuditWriter>.Instance);
        return (writer, db);
    }

    private async Task<string?> SourceOfAsync(string actor)
    {
        await using var db = new MesDbContext(_opt);
        return await db.AuditLogs.AsNoTracking()
            .Where(x => x.ActorUsername == actor)
            .Select(x => x.Source).SingleAsync();
    }

    [Fact]
    public async Task Caller_khong_noi_gi_thi_writer_dong_dau_TRANSPORT_cua_no()
    {
        // Đây là đường đi của gần như mọi lệnh ghi: service giữ IAuditWriter,
        // không truyền source, và không biết nó đang chạy sau transport nào.
        var (w, db) = Make();
        await using (db)
            await w.EmitAsync(AuditAction.QcLibraryImport, "src-default", UserRole.Admin,
                targetType: "Test", targetId: "t1");

        Assert.Equal("Api", await SourceOfAsync("src-default"));
    }

    [Fact]
    public async Task Nguon_KHAC_transport_van_truyen_tuong_minh_duoc()
    {
        // Công cụ dòng lệnh và tác vụ nền chạy QUA writer của API nhưng KHÔNG
        // phải là API. Repo đang có 3 chỗ truyền tường minh ("Console",
        // "Scheduler") — bản vá không được nuốt mất khả năng đó.
        var (w, db) = Make();
        await using (db)
            await w.EmitAsync(AuditAction.QcLibraryImport, "src-console", UserRole.Admin,
                targetType: "Test", targetId: "t2", detail: null, source: "Console");

        Assert.Equal("Console", await SourceOfAsync("src-console"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rong_hay_null_deu_la_KHONG_NOI_GI_chu_khong_phai_nguon_ten_rong(string? src)
    {
        // Lưu chuỗi rỗng thì cột nguồn mất nghĩa mà vẫn "hợp lệ" — đúng kiểu
        // hỏng mà không ai phát hiện.
        var actor = "src-blank-" + (src is null ? "null" : src.Length.ToString());
        var (w, db) = Make();
        await using (db)
            await w.EmitAsync(AuditAction.QcLibraryImport, actor, UserRole.Admin,
                targetType: "Test", targetId: "t3", detail: null, source: src);

        Assert.Equal("Api", await SourceOfAsync(actor));
    }

    [Fact]
    public void Interface_KHONG_duoc_khai_mac_dinh_la_mot_chuoi_cu_the()
    {
        // Cơ chế chặn tái phát THẬT SỰ: mấy test trên chỉ bắt được lỗi sau khi
        // ai đó đã gây ra nó trên một đường ghi cụ thể. Test này chặn ngay tại
        // HÌNH DẠNG hợp đồng — hễ có người đặt lại mặc định thành một chuỗi,
        // nó đỏ, bất kể đường ghi nào.
        var m = typeof(CCL.MES.Application.Audit.IAuditWriter)
            .GetMethod(nameof(CCL.MES.Application.Audit.IAuditWriter.EmitAsync))!;
        var p = m.GetParameters().Single(x => x.Name == "source");

        Assert.True(p.HasDefaultValue);
        Assert.Null(p.DefaultValue);   // null = "writer tự quyết transport"
    }
}
