using CCL.MES.EnumIntegrity;
using CCL.MES.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Một DB SQLite thật, migrate bằng đúng chuỗi migration production, dùng chung
/// cho cả lớp test. Dùng DB thật chứ không phải InMemory là bắt buộc: bug class
/// này chỉ tồn tại ở đường ĐỌC của relational value converter — provider
/// InMemory không có converter, nên nó sẽ báo xanh cho đúng thứ đang hỏng.
/// </summary>
public sealed class EnumIntegrityDbFixture : IAsyncLifetime
{
    public string Root { get; } = Path.Combine(
        Path.GetTempPath(), $"ccl-mes-enum-integrity-{Guid.NewGuid():N}");

    public string DbPath => Path.Combine(Root, "test.db");

    public DbContextOptions<MesDbContext> Options => new DbContextOptionsBuilder<MesDbContext>()
        .UseSqlite($"Data Source={DbPath}")
        .Options;

    public MesDbContext NewContext() => new(Options);

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Root);
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        // Cha của WorkOrder — chỉ để thoả FK, không phải dữ liệu nghiệp vụ.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Customers (Id, Code, Name, CreatedAt)
            VALUES (1, 'ENUMGATE', 'enum-integrity fixture', '2026-08-19 00:00:00');
            INSERT INTO Products (Id, ProductCode, Name, CustomerId, CreatedAt)
            VALUES (1, 'ENUMGATE-P', 'enum-integrity fixture', 1, '2026-08-19 00:00:00');
            """);
    }

    public Task DisposeAsync()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
        catch { /* best effort */ }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Ghi bằng SQL THÔ — cố ý. Đây là chính xác cách rác đi vào DB sản xuất
    /// ngày 2026-08-19 (0 bản ghi audit cho 11 WO trong khi cùng cửa sổ thời
    /// gian có 926 audit khác ⇒ ghi ngoài đường có audit của app). Nếu test
    /// seed qua EF thì converter chiều GHI sẽ chặn, và ta test nhầm thứ.
    /// </summary>
    public async Task RawWriteAsync(string sql)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    /// <summary>WO tối thiểu đủ thoả NOT NULL, ghi thẳng bằng SQL thô.</summary>
    public async Task SeedRawWorkOrderAsync(int id, string currentStep, string status = "Finished")
    {
        await RawWriteAsync($"""
            INSERT INTO WorkOrders
                (Id, WoNo, CustomerId, ProductId, ProductName, Uom, TargetQty, ProducedQty,
                 Priority, MaterialsReady, RohsOk, SetupConfirmed,
                 CurrentStep, Status, MesPhase, CreatedAt)
            VALUES
                ({id}, 'WO-TEST-{id}', 1, 1, 'enum-integrity fixture', 'PCS', 100, 0,
                 0, 0, 0, 0,
                 '{currentStep}', '{status}', 'SHIPPED', '2026-08-19 00:00:00');
            """);
    }
}

/// <summary>
/// TẦNG 1 của gate-enum-integrity — regression trong logic ĐỌC.
///
/// Sự cố 2026-08-19: <c>WorkOrders.CurrentStep='Done'</c> × 11, giá trị không
/// có trong <c>ProcessStepCode</c>. <c>MesDbContext.cs:89</c> cấu hình
/// <c>HasConversion&lt;string&gt;()</c> ⇒ chiều đọc ném trong shaper của EF ⇒
/// MỌI truy vấn materialise <c>WorkOrder</c> đều chết. 10 route API hỏng, route
/// danh sách mất toàn bộ 27 WO cho mọi người dùng.
/// </summary>
public sealed class EnumIntegrityTests : IClassFixture<EnumIntegrityDbFixture>
{
    private readonly EnumIntegrityDbFixture _fx;

    public EnumIntegrityTests(EnumIntegrityDbFixture fx) => _fx = fx;

    // ── A. Sự thật nền: giá trị ngoài enum GIẾT truy vấn ────────────────────

    [Fact]
    public async Task Raw_sql_Done_makes_every_WorkOrder_query_throw()
    {
        await _fx.SeedRawWorkOrderAsync(9001, "Done");
        try
        {
            await using var db = _fx.NewContext();

            // Đây là hình dạng của route DANH SÁCH đã mất toàn bộ 27 WO:
            // một dòng độc giết cả truy vấn, không riêng dòng của nó.
            var ex = await Assert.ThrowsAnyAsync<Exception>(
                () => db.WorkOrders.AsNoTracking().ToListAsync());

            Assert.Contains("Done", Flatten(ex), StringComparison.Ordinal);
        }
        finally
        {
            await _fx.RawWriteAsync("DELETE FROM WorkOrders WHERE Id = 9001;");
        }
    }

    [Fact]
    public async Task Projection_to_DTO_survives_the_same_bad_row()
    {
        // Vì sao chỉ 10/24 route chết: endpoint chiếu sang DTO sống sót vì EF
        // không sinh SQL đọc cột đó. Test này khoá lại lời giải thích ấy —
        // nếu nó đổ, RCA trong runbook đã sai và phải điều tra lại.
        await _fx.SeedRawWorkOrderAsync(9002, "Done");
        try
        {
            await using var db = _fx.NewContext();
            var numbers = await db.WorkOrders.AsNoTracking()
                .Select(w => w.WoNo)
                .ToListAsync();
            Assert.Contains("WO-TEST-9002", numbers);
        }
        finally
        {
            await _fx.RawWriteAsync("DELETE FROM WorkOrders WHERE Id = 9002;");
        }
    }

    // ── B. Scanner: khám phá cột bằng reflection, không hard-code ───────────

    [Fact]
    public void Scanner_discovers_enum_string_columns_by_reflection()
    {
        var columns = EnumIntegrityScanner.DiscoverColumns();

        // Đo 2026-08-19 trên live: 37 cột. Khẳng định NGƯỠNG DƯỚI chứ không
        // phải con số cứng — cả điểm của thiết kế là enum thêm về sau TỰ ĐỘNG
        // được canh, nên một cột mới phải làm test này xanh hơn, không phải đỏ.
        Assert.True(columns.Count >= 37,
            $"chỉ khám phá được {columns.Count} cột enum-string, chờ >= 37");

        Assert.Contains(columns, c => c.Table == "WorkOrders" && c.Column == "CurrentStep");
        Assert.Contains(columns, c => c.Table == "WorkOrders" && c.Column == "Status");

        // Không hard-code: mọi cột đều phải trỏ tới một kiểu enum thật.
        Assert.All(columns, c => Assert.True(c.EnumType.IsEnum));
    }

    // ── C. Bắt được hạng ỒN ÀO (converter ném) ──────────────────────────────

    [Fact]
    public async Task Scan_flags_Done_as_a_throwing_violation()
    {
        await _fx.SeedRawWorkOrderAsync(9003, "Done");
        await _fx.SeedRawWorkOrderAsync(9004, "Done");
        try
        {
            var result = await ScanAsync();

            var v = Assert.Single(result.Violations,
                x => x.Table == "WorkOrders" && x.Column == "CurrentStep" && x.Value == "Done");
            Assert.Equal(EnumViolationKind.Throws, v.Kind);
            Assert.Equal(2, v.RowCount);
            Assert.Equal(2, result.BadRows);
            Assert.False(result.IsClean);
        }
        finally
        {
            await _fx.RawWriteAsync("DELETE FROM WorkOrders WHERE Id IN (9003, 9004);");
        }
    }

    // ── D. Bắt được hạng IM LẶNG (không ném, nhưng IsDefined=False) ─────────

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    public async Task Scan_flags_silently_undefined_values(string bad)
    {
        // '' và '0' KHÔNG ném — chúng map thành ProcessStepCode(0), một giá trị
        // enum không định nghĩa (ProcessStepCode bắt đầu từ 1). Không có 500 nào
        // để ai đó đi điều tra: badge trống, switch rơi vào default, báo cáo
        // lệch. Hạng này nguy hiểm HƠN hạng 'Done'.
        await _fx.SeedRawWorkOrderAsync(9005, bad);
        try
        {
            var result = await ScanAsync();

            var v = Assert.Single(result.Violations,
                x => x.Table == "WorkOrders" && x.Column == "CurrentStep" && x.Value == bad);
            Assert.Equal(EnumViolationKind.Undefined, v.Kind);
            Assert.Contains("IsDefined=False", v.Format(), StringComparison.Ordinal);

            // Và chứng minh nó THẬT SỰ im lặng: truy vấn KHÔNG ném.
            await using var db = _fx.NewContext();
            var row = await db.WorkOrders.AsNoTracking().SingleAsync(w => w.Id == 9005);
            Assert.False(Enum.IsDefined(row.CurrentStep));
        }
        finally
        {
            await _fx.RawWriteAsync("DELETE FROM WorkOrders WHERE Id = 9005;");
        }
    }

    // ── E. 0 báo động giả — thứ EF map được thì KHÔNG báo ───────────────────

    [Theory]
    [InlineData("closed")]   // khác hoa thường
    [InlineData("CLOSED")]   // khác hoa thường
    [InlineData("8")]        // dạng số của ProcessStepCode.Closed
    public async Task Scan_does_not_flag_values_EF_can_map(string acceptable)
    {
        // Một gate báo động giả sẽ bị người ta tắt trong hai tuần. Ba giá trị
        // này EF ĐỌC ĐƯỢC (đo thực tế), nên gate PHẢI im lặng về chúng.
        await _fx.SeedRawWorkOrderAsync(9006, acceptable);
        try
        {
            var result = await ScanAsync();

            Assert.DoesNotContain(result.Violations,
                x => x.Table == "WorkOrders" && x.Column == "CurrentStep");

            // Và chứng minh vì sao im lặng là ĐÚNG: EF thật sự đọc ra Closed.
            await using var db = _fx.NewContext();
            var row = await db.WorkOrders.AsNoTracking().SingleAsync(w => w.Id == 9006);
            Assert.Equal(CCL.MES.Domain.ProcessStepCode.Closed, row.CurrentStep);
        }
        finally
        {
            await _fx.RawWriteAsync("DELETE FROM WorkOrders WHERE Id = 9006;");
        }
    }

    // ── F. Trộn từ vựng GIỮA các enum — chính là bug class gốc ──────────────

    [Fact]
    public async Task Scan_flags_vocabulary_borrowed_from_a_different_enum()
    {
        // 'Shipped' là thành viên của MesPhase, KHÔNG phải của WoStatus
        // (Draft · InProgress · OnHold · Finished · Closed · Cancelled).
        // Đúng cơ chế đã đẻ ra 'Done': từ vựng mượn của một state machine khác,
        // ghi thẳng bằng SQL. Gate phải bắt được cả khi cột nạn nhân đổi.
        await _fx.SeedRawWorkOrderAsync(9007, "Closed", status: "Shipped");
        try
        {
            var result = await ScanAsync();

            var v = Assert.Single(result.Violations,
                x => x.Table == "WorkOrders" && x.Column == "Status" && x.Value == "Shipped");
            Assert.Equal("WoStatus", v.EnumType);
            Assert.Equal(EnumViolationKind.Throws, v.Kind);
        }
        finally
        {
            await _fx.RawWriteAsync("DELETE FROM WorkOrders WHERE Id = 9007;");
        }
    }

    // ── G. DB sạch phải PASS, và phải quét được thật ────────────────────────

    [Fact]
    public async Task Clean_database_passes_and_actually_scans_every_column()
    {
        var result = await ScanAsync();

        Assert.True(result.IsClean, string.Join(" | ", result.Violations.Select(v => v.Format())));

        // "scanned 0/N" là KHÔNG KẾT LUẬN ĐƯỢC, không phải PASS. Nếu không
        // khẳng định điều này thì một DB thiếu bảng sẽ báo xanh vĩnh viễn.
        Assert.False(EnumIntegrityReport.IsInconclusive(result));
        Assert.Equal(result.ColumnsDiscovered, result.ColumnsScanned);
        Assert.Empty(result.Skipped);
    }

    private async Task<EnumIntegrityResult> ScanAsync()
    {
        await using var db = _fx.NewContext();
        return await EnumIntegrityScanner.ScanAsync(db);
    }

    private static string Flatten(Exception ex)
    {
        var text = ex.Message;
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            text += " | " + inner.Message;
        return text;
    }
}
