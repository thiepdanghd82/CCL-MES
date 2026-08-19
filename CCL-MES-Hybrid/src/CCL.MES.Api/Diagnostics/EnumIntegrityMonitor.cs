using CCL.MES.EnumIntegrity;
using CCL.MES.Infrastructure;

namespace CCL.MES.Api.Diagnostics;

/// <summary>
/// Trạng thái toàn vẹn enum tại một thời điểm. Bất biến, an toàn để bắn ra
/// /health/ready và để log lúc boot.
/// </summary>
/// <param name="Status">
/// <c>ok</c> — đã quét được và sạch ·
/// <c>degraded</c> — CÓ giá trị nằm ngoài enum ·
/// <c>unknown</c> — KHÔNG kết luận được (DB lạc hậu migration, khoá, lỗi).
/// Ba trạng thái chứ không phải hai: gộp "không kiểm được" vào "ok" chính là
/// cách một cơ chế canh trở thành đồ trang trí.
/// </param>
public sealed record EnumIntegritySnapshot(
    string Status,
    string MessageKey,
    DateTimeOffset CheckedAtUtc,
    int ColumnsScanned,
    int ColumnsDiscovered,
    int BadColumns,
    long BadRows,
    IReadOnlyList<string> Violations,
    string? Error)
{
    public const string StatusOk = "ok";
    public const string StatusDegraded = "degraded";
    public const string StatusUnknown = "unknown";

    public bool IsDegraded => Status == StatusDegraded;
}

/// <summary>
/// TẦNG 3 của gate-enum-integrity — tầng DUY NHẤT bắt được sự cố 2026-08-19.
///
/// Defect đó lọt không phải vì thiếu test code, mà vì KHÔNG AI kiểm tính toàn
/// vẹn của DỮ LIỆU LIVE. Rác vào bằng SQL trực tiếp, ngoài mọi đường có audit
/// của app — nên tầng 1 (CI test) và tầng 2 (DB fixture) về nguyên tắc không
/// thể thấy. Chỉ một phép kiểm chạy trên chính DB mà process đang phục vụ mới
/// thấy được.
///
/// Hai ràng buộc ĐỐI NGHỊCH nhau, và cách hoà giải:
///
///   • KHÔNG được chặn boot. Nhà máy đang chạy; từ chối khởi động vì một dòng
///     rác là tệ hơn cái nó phòng. Sự cố vừa rồi làm hỏng 10 route CHỨ KHÔNG
///     làm sập app — một preflight chặn boot sẽ biến sự cố 41% thành 100%.
///     ⇒ <see cref="RefreshAsync"/> KHÔNG BAO GIỜ ném; lỗi biến thành
///       <c>unknown</c> + log.
///
///   • PHẢI ồn ào. Hỏng im lặng là lý do defect này sống 30 ngày.
///     ⇒ banner WARN nhiều dòng lúc boot (cùng khuôn với probe pending
///       migration đã có sẵn ở Program.cs) + /health/ready phản ánh liên tục.
///
/// Lối thoát cho môi trường muốn nghiêm hơn: đặt
/// <c>Database:FailOnEnumIntegrityViolations=true</c> (mặc định false) để boot
/// thất bại — dùng cho staging / CI hardware, KHÔNG dùng cho line sản xuất.
///
/// Ảnh chụp được cache theo <c>Health:EnumIntegrityCacheSeconds</c> (mặc định
/// 300s). Cache chứ không phải chụp-một-lần-lúc-boot là cố ý: rác được ghi vào
/// bằng SQL trực tiếp lúc 3 giờ sáng, không phải lúc deploy. Một ảnh chụp lúc
/// boot sẽ báo "ok" mãi mãi cho tới lần khởi động lại kế tiếp. Cache 5 phút
/// cũng là thứ chặn /health/ready (ẩn danh) trở thành đòn bẩy DoS.
/// </summary>
public sealed class EnumIntegrityMonitor
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EnumIntegrityMonitor> _log;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private EnumIntegritySnapshot? _snapshot;

    public EnumIntegrityMonitor(
        IServiceScopeFactory scopes,
        ILogger<EnumIntegrityMonitor> log,
        IConfiguration config)
    {
        _scopes = scopes;
        _log = log;
        CacheWindow = TimeSpan.FromSeconds(
            Math.Max(0, config.GetValue("Health:EnumIntegrityCacheSeconds", 300)));
        ScanTimeout = TimeSpan.FromSeconds(
            Math.Clamp(config.GetValue("Health:EnumIntegrityTimeoutSeconds", 30), 1, 300));
    }

    public TimeSpan CacheWindow { get; }
    public TimeSpan ScanTimeout { get; }

    /// <summary>Ảnh chụp gần nhất, hoặc null nếu chưa quét lần nào.</summary>
    public EnumIntegritySnapshot? Last => _snapshot;

    /// <summary>
    /// Trả ảnh chụp còn hạn, hoặc quét lại. Không bao giờ ném. Đây là đường mà
    /// /health/ready đi.
    /// </summary>
    public Task<EnumIntegritySnapshot> GetAsync(CancellationToken ct = default)
    {
        var cached = _snapshot;
        if (IsFresh(cached)) return Task.FromResult(cached!);
        return ScanCoalescedAsync(honourCache: true, ct);
    }

    /// <summary>
    /// Quét lại NGAY, bỏ qua cache. Đường mà preflight lúc boot đi.
    /// KHÔNG BAO GIỜ ném — mọi lỗi thành <c>unknown</c>. Đó là hợp đồng giữ cho
    /// preflight không thể chặn boot.
    /// </summary>
    public Task<EnumIntegritySnapshot> RefreshAsync(CancellationToken ct = default) =>
        ScanCoalescedAsync(honourCache: false, ct);

    private bool IsFresh(EnumIntegritySnapshot? snapshot) =>
        snapshot is not null
        && CacheWindow > TimeSpan.Zero
        && DateTimeOffset.UtcNow - snapshot.CheckedAtUtc < CacheWindow;

    private async Task<EnumIntegritySnapshot> ScanCoalescedAsync(bool honourCache, CancellationToken ct)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Một luồng khác vừa quét xong trong lúc ta xếp hàng — dùng lại
            // ẢNH CHỤP CÒN HẠN THEO ĐÚNG CacheWindow, không theo một hằng số
            // tự chế. Cache 0 nghĩa là phía gọi CỐ Ý muốn số liệu tươi; một
            // sàn cache ẩn sẽ biến cấu hình đó thành lời nói dối.
            if (honourCache && IsFresh(_snapshot)) return _snapshot!;

            var snapshot = await ScanAsync(ct).ConfigureAwait(false);
            _snapshot = snapshot;
            return snapshot;
        }
        catch (Exception ex)
        {
            var snapshot = Unknown($"{ex.GetType().Name}: {ex.Message}");
            _snapshot = snapshot;
            return snapshot;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<EnumIntegritySnapshot> ScanAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ScanTimeout);

        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var result = await EnumIntegrityScanner.ScanAsync(db, timeout.Token).ConfigureAwait(false);

            if (EnumIntegrityReport.IsInconclusive(result))
            {
                return Unknown(
                    $"quét được 0/{result.ColumnsDiscovered} cột — DB lạc hậu migration hoặc chưa dựng",
                    result.ColumnsDiscovered);
            }

            return new EnumIntegritySnapshot(
                Status: result.IsClean
                    ? EnumIntegritySnapshot.StatusOk
                    : EnumIntegritySnapshot.StatusDegraded,
                MessageKey: result.IsClean
                    ? "health.enumIntegrity.ok"
                    : "health.enumIntegrity.degraded",
                CheckedAtUtc: DateTimeOffset.UtcNow,
                ColumnsScanned: result.ColumnsScanned,
                ColumnsDiscovered: result.ColumnsDiscovered,
                BadColumns: result.BadColumns,
                BadRows: result.BadRows,
                Violations: result.Violations.Select(v => v.Format()).ToList(),
                Error: null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unknown($"quá {ScanTimeout.TotalSeconds:F0}s — bỏ qua để không giữ boot");
        }
    }

    private static EnumIntegritySnapshot Unknown(string error, int discovered = 0) =>
        new(EnumIntegritySnapshot.StatusUnknown,
            "health.enumIntegrity.unknown",
            DateTimeOffset.UtcNow,
            ColumnsScanned: 0,
            ColumnsDiscovered: discovered,
            BadColumns: 0,
            BadRows: 0,
            Violations: Array.Empty<string>(),
            Error: error);

    /// <summary>
    /// Banner lúc boot. Cùng khuôn với probe pending-migration đã có — người
    /// vận hành đã quen đọc khối <c>═══</c> đó, và một defect chỉ được chú ý
    /// khi nó trông giống thứ họ đã được dạy phải chú ý.
    /// </summary>
    public void WriteBootBanner(EnumIntegritySnapshot snapshot)
    {
        if (snapshot.Status == EnumIntegritySnapshot.StatusOk)
        {
            _log.LogInformation(
                "[boot] Enum integrity check: clean — {Scanned}/{Discovered} enum-string column(s).",
                snapshot.ColumnsScanned, snapshot.ColumnsDiscovered);
            Console.WriteLine(
                $"[boot] Enum integrity check: clean — {snapshot.ColumnsScanned}/{snapshot.ColumnsDiscovered} enum-string column(s).");
            return;
        }

        if (snapshot.Status == EnumIntegritySnapshot.StatusUnknown)
        {
            _log.LogWarning(
                "[boot] Enum integrity check INCONCLUSIVE: {Error}. KHÔNG phải PASS.",
                snapshot.Error);
            Console.WriteLine($"[boot] Enum integrity check INCONCLUSIVE: {snapshot.Error} (KHÔNG phải PASS)");
            return;
        }

        const string header = "════════ WARNING — LIVE DATABASE HAS OUT-OF-ENUM VALUES ════════";
        _log.LogWarning(
            "Enum integrity: {BadRows} row(s) across {BadColumns} column(s) hold values outside their enum. "
            + "Queries that materialise those entities will THROW. Violations: {Violations}",
            snapshot.BadRows, snapshot.BadColumns, string.Join(" | ", snapshot.Violations));

        Console.WriteLine();
        Console.WriteLine(header);
        Console.WriteLine($"  Scanned {snapshot.ColumnsScanned}/{snapshot.ColumnsDiscovered} enum-string column(s) via EF model reflection.");
        Console.WriteLine($"  {snapshot.BadRows} row(s) in {snapshot.BadColumns} column(s) hold a value the enum does not define:");
        foreach (var v in snapshot.Violations) Console.WriteLine($"    - {v}");
        Console.WriteLine();
        Console.WriteLine("  Hậu quả: mọi truy vấn MATERIALISE entity đó sẽ ném trong shaper của EF.");
        Console.WriteLine("  Sự cố 2026-08-19: 11 dòng WorkOrders.CurrentStep='Done' làm hỏng 10 route,");
        Console.WriteLine("  trong đó route DANH SÁCH mất toàn bộ 27 WO cho MỌI người dùng.");
        Console.WriteLine();
        Console.WriteLine("  Xem CCL-MES-Hybrid/docs/RUNBOOK-CURRENTSTEP-REPAIR-2026-08-19.md.");
        Console.WriteLine("  Sửa DỮ LIỆU về một thành viên enum hợp lệ — KHÔNG thêm thành viên mới");
        Console.WriteLine("  để hợp thức hoá giá trị rác (contract impact = 1 ⇒ STOP-gate).");
        Console.WriteLine();
        Console.WriteLine("  App VẪN KHỞI ĐỘNG — chặn boot vì một dòng rác còn tệ hơn. Đặt");
        Console.WriteLine("  Database:FailOnEnumIntegrityViolations=true nếu môi trường này muốn fail-fast.");
        Console.WriteLine(new string('═', header.Length));
        Console.WriteLine();
    }
}
