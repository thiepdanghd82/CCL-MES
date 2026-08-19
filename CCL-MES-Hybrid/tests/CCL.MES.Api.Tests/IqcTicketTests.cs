using System.Net;
using System.Net.Http.Json;
using System.Text;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// feat/iqc-ticket — nghiệm thu POST /api/v2/iqc.
///
/// Mỗi ca khẳng định đúng HTTP status + đúng hệ quả DB (phiếu + lô Quarantine
/// + audit IQC_CREATE). Phủ: happy 201, Code IFS sai case vẫn matched, Code IFS
/// trùng → ambiguous (không auto-fill), không match → vẫn lưu (unmatched),
/// Operator → 403, chống trùng ReceiptNo (N=10 song song), bất biến cache mô tả.
/// </summary>
public sealed class IqcTicketTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public IqcTicketTests(MesApiFactory fx) => _fx = fx;

    // ── Helpers ────────────────────────────────────────────────────

    private async Task<HttpClient> ClientAsync(string user, string role)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", role);
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, "P@ss!1");
        return c;
    }

    private static HttpRequestMessage Post(string path, object body, bool idem = true, string? key = null)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(body);
        var r = new HttpRequestMessage(HttpMethod.Post, path)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (idem) r.Headers.TryAddWithoutValidation("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return r;
    }

    private async Task SeedRawAsync(string partNo, string? desc = null, string? supplier = null)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        db.RawMaterials.Add(new RawMaterial { PartNo = partNo, PartDescription = desc, SupplierName = supplier });
        await db.SaveChangesAsync();
    }

    private static object Body(string codeIfs, string lotBatchNo, double qty = 100,
        string? maker = null, string? supplier = null) => new
    {
        codeIfs, lotBatchNo, quantity = qty, makerName = maker, supplierName = supplier,
    };

    // ── Happy path ─────────────────────────────────────────────────

    [Fact]
    public async Task Create_happy_returns_201_with_receipt_no_and_opens_quarantine_lot()
    {
        await SeedRawAsync("IFS-AB-200", "Keo AB-200 hai thành phần", "Đại Phát");
        var c = await ClientAsync("qc-iqc-happy", UserRole.Qc);

        var resp = await c.SendAsync(Post("/api/v2/iqc", Body("IFS-AB-200", "LOT-260819-01")));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = (await resp.Content.ReadFromJsonAsync<CreateIqcTicketResponse>())!;
        Assert.StartsWith("IQC-", body.ReceiptNo);
        Assert.Matches(@"^IQC-\d{6}-\d{4}$", body.ReceiptNo);
        Assert.Equal("matched", body.MatchStatus);
        Assert.Equal("Keo AB-200 hai thành phần", body.MaterialDescription);
        Assert.NotNull(body.MaterialLotId);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

        // Phiếu ↔ lô nối đúng cặp Quarantine (query khép kín).
        var pair = await (from i in db.IqcInspections
                          join l in db.MaterialLots on i.Id equals l.IqcInspectionId
                          where i.Id == body.IqcInspectionId
                          select new { i.ReceiptNo, l.LotNo, l.Status, l.IqcInspectionId }).SingleAsync();
        Assert.Equal(body.ReceiptNo, pair.ReceiptNo);
        Assert.Equal("LOT-260819-01", pair.LotNo);
        Assert.Equal(nameof(MaterialLotStatus.Quarantine), pair.Status);

        // Inspector server-stamp (không client khai).
        var insp = await db.IqcInspections.AsNoTracking().SingleAsync(x => x.Id == body.IqcInspectionId);
        Assert.Equal("qc-iqc-happy", insp.InspectorId);

        // Audit IQC_CREATE có match_status.
        var audit = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "IQC_CREATE" && a.TargetId == body.IqcInspectionId.ToString())
            .SingleAsync();
        Assert.Contains("matched", audit.Detail);
        Assert.Contains(body.ReceiptNo, audit.Detail);
    }

    // ── Code IFS NOCASE match (quyết định #3) ──────────────────────

    [Fact]
    public async Task Create_code_ifs_wrong_case_still_matches()
    {
        await SeedRawAsync("IFS-CASE-01", "Mực UV xanh");
        var c = await ClientAsync("qc-iqc-case", UserRole.Qc);

        var resp = await c.SendAsync(Post("/api/v2/iqc", Body("ifs-case-01", "LOT-CASE-1")));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<CreateIqcTicketResponse>())!;
        Assert.Equal("matched", body.MatchStatus);
        Assert.Equal("Mực UV xanh", body.MaterialDescription);
    }

    // ── Ambiguous (>1 match) — không auto-fill ─────────────────────

    [Fact]
    public async Task Create_ambiguous_code_ifs_saves_without_autofill()
    {
        await SeedRawAsync("IFS-DUP-9", "desc A");
        await SeedRawAsync("IFS-DUP-9", "desc B");   // 2 bản ghi trùng PartNo
        var c = await ClientAsync("qc-iqc-amb", UserRole.Qc);

        var resp = await c.SendAsync(Post("/api/v2/iqc", Body("IFS-DUP-9", "LOT-AMB-1")));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<CreateIqcTicketResponse>())!;
        Assert.Equal("ambiguous", body.MatchStatus);
        Assert.Null(body.MaterialDescription);   // KHÔNG auto-fill mù

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var insp = await db.IqcInspections.AsNoTracking().SingleAsync(x => x.Id == body.IqcInspectionId);
        Assert.Null(insp.RawMaterialId);
        Assert.Equal("IFS-DUP-9", insp.CodeIfs);
    }

    // ── Unmatched (0 match) — quyết định #2: vẫn lưu ───────────────

    [Fact]
    public async Task Create_unmatched_code_ifs_still_saves_with_null_fk()
    {
        var c = await ClientAsync("qc-iqc-unm", UserRole.Qc);

        var resp = await c.SendAsync(Post("/api/v2/iqc", Body("IFS-NOPE-404", "LOT-UNM-1")));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<CreateIqcTicketResponse>())!;
        Assert.Equal("unmatched", body.MatchStatus);
        Assert.Null(body.MaterialDescription);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var insp = await db.IqcInspections.AsNoTracking().SingleAsync(x => x.Id == body.IqcInspectionId);
        Assert.Null(insp.RawMaterialId);
        Assert.Equal("IFS-NOPE-404", insp.CodeIfs);   // giữ text Code IFS
        // Lô unresolved vẫn mở (Quarantine, PartNo = Code IFS).
        var lot = await db.MaterialLots.AsNoTracking().SingleAsync(l => l.IqcInspectionId == insp.Id);
        Assert.Equal(nameof(MaterialLotStatus.Quarantine), lot.Status);
    }

    // ── RBAC — Operator/Engineer → 403 ─────────────────────────────

    [Theory]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Engineer)]
    public async Task Create_forbidden_for_non_editor_roles(string role)
    {
        var c = await ClientAsync($"u-iqc-403-{role}", role);
        var resp = await c.SendAsync(Post("/api/v2/iqc", Body("IFS-X", "LOT-403")));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Idempotency-Key bắt buộc ───────────────────────────────────

    [Fact]
    public async Task Create_without_idempotency_key_is_400()
    {
        var c = await ClientAsync("qc-iqc-noidem", UserRole.Qc);
        var resp = await c.SendAsync(Post("/api/v2/iqc", Body("IFS-Y", "LOT-NOIDEM"), idem: false));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Input guard 422 ────────────────────────────────────────────

    [Fact]
    public async Task Create_missing_code_ifs_is_422()
    {
        var c = await ClientAsync("qc-iqc-422", UserRole.Qc);
        var resp = await c.SendAsync(Post("/api/v2/iqc", Body("", "LOT-422")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ── Chống trùng ReceiptNo — N=10 song song, distinct, 0 trùng ──

    [Fact]
    [Trait("Category", "Soak")]
    public async Task Create_concurrent_N10_yields_distinct_receipt_numbers()
    {
        await SeedRawAsync("IFS-SOAK", "soak desc");
        var c = await ClientAsync("qc-iqc-soak", UserRole.Qc);

        var tasks = Enumerable.Range(0, 10).Select(i =>
            c.SendAsync(Post("/api/v2/iqc", Body("IFS-SOAK", $"LOT-SOAK-{i:D2}")))).ToArray();
        var responses = await Task.WhenAll(tasks);

        var created = new List<string>();
        foreach (var r in responses)
        {
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            var b = (await r.Content.ReadFromJsonAsync<CreateIqcTicketResponse>())!;
            created.Add(b.ReceiptNo);
        }
        Assert.Equal(10, created.Count);
        Assert.Equal(10, created.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ── feat/iqc-search-by-desc — GET /iqc/search-material ─────────

    [Fact]
    public async Task SearchMaterial_by_description_fans_out_distinct_code_ifs()
    {
        // 14 distinct PartNo (mother "NITTO 5000NS"); one PartNo duplicated to
        // prove DISTINCT-by-PartNo collapses it to a single row.
        for (var i = 0; i < 14; i++)
            await SeedRawAsync($"NITTO-5000NS-{i:D2}", $"NITTO 5000NS variant {i}");
        await SeedRawAsync("NITTO-5000NS-00", "NITTO 5000NS variant 0 (dup PartNo)");
        await SeedRawAsync("BW-0112N-01", "BW-0112N unrelated");   // must NOT match

        var c = await ClientAsync("qc-search-fan", UserRole.Qc);
        var resp = await c.GetAsync("/api/v2/iqc/search-material?desc=" + Uri.EscapeDataString("NITTO 5000NS") + "&page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = (await resp.Content.ReadFromJsonAsync<IqcMaterialSearchResponse>())!;
        Assert.False(body.TooShort);
        Assert.Equal(14, body.Total);
        Assert.Equal(14, body.Items.Select(x => x.CodeIfs).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(body.Items, x => Assert.StartsWith("NITTO-5000NS-", x.CodeIfs));
    }

    [Fact]
    public async Task SearchMaterial_collapses_whitespace_so_double_space_matches()
    {
        await SeedRawAsync("WS-01", "NITTO 5000NS single space");
        var c = await ClientAsync("qc-search-ws", UserRole.Qc);

        var resp = await c.GetAsync("/api/v2/iqc/search-material?desc=" + Uri.EscapeDataString("NITTO  5000NS"));
        var body = (await resp.Content.ReadFromJsonAsync<IqcMaterialSearchResponse>())!;
        Assert.False(body.TooShort);
        Assert.Contains(body.Items, x => x.CodeIfs == "WS-01");
    }

    [Fact]
    public async Task SearchMaterial_short_desc_returns_tooShort_and_empty()
    {
        var c = await ClientAsync("qc-search-short", UserRole.Qc);
        var resp = await c.GetAsync("/api/v2/iqc/search-material?desc=NI");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<IqcMaterialSearchResponse>())!;
        Assert.True(body.TooShort);
        Assert.Empty(body.Items);
        Assert.Equal(0, body.Total);
    }

    [Fact]
    public async Task SearchMaterial_paginates_total_across_pages()
    {
        for (var i = 0; i < 25; i++)
            await SeedRawAsync($"PAGE-{i:D2}", $"PAGINATE ME row {i}");
        var c = await ClientAsync("qc-search-page", UserRole.Qc);

        var p1 = (await (await c.GetAsync("/api/v2/iqc/search-material?desc=PAGINATE%20ME&page=1&pageSize=10"))
            .Content.ReadFromJsonAsync<IqcMaterialSearchResponse>())!;
        Assert.Equal(25, p1.Total);
        Assert.Equal(10, p1.Items.Count);

        var p3 = (await (await c.GetAsync("/api/v2/iqc/search-material?desc=PAGINATE%20ME&page=3&pageSize=10"))
            .Content.ReadFromJsonAsync<IqcMaterialSearchResponse>())!;
        Assert.Equal(25, p3.Total);
        Assert.Equal(5, p3.Items.Count);   // remainder page
    }

    [Fact]
    public async Task SearchMaterial_forbidden_without_qc_read()
    {
        var c = await ClientAsync("op-search-403", UserRole.Operator);
        var resp = await c.GetAsync("/api/v2/iqc/search-material?desc=anything");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── A2a multi-create — pick N codes → N tickets, distinct lots ─

    [Fact]
    public async Task Multi_create_three_codes_yields_three_tickets_and_lots()
    {
        await SeedRawAsync("MC-A", "multi A");
        await SeedRawAsync("MC-B", "multi B");
        await SeedRawAsync("MC-C", "multi C");
        var c = await ClientAsync("qc-multi-ok", UserRole.Qc);

        // Client does the fan-out; server contract is unchanged (one POST each,
        // distinct lot suffix). This asserts the SAME endpoint services N calls.
        var codes = new[] { "MC-A", "MC-B", "MC-C" };
        var receipts = new List<string>();
        for (var i = 0; i < codes.Length; i++)
        {
            var resp = await c.SendAsync(Post("/api/v2/iqc", Body(codes[i], $"LOT-MCOK-{(i + 1):D2}")));
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var b = (await resp.Content.ReadFromJsonAsync<CreateIqcTicketResponse>())!;
            receipts.Add(b.ReceiptNo);
        }
        Assert.Equal(3, receipts.Distinct().Count());

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var lots = await db.MaterialLots.AsNoTracking()
            .Where(l => l.LotNo.StartsWith("LOT-MCOK-"))
            .Select(l => new { l.LotNo, l.Status }).ToListAsync();
        Assert.Equal(3, lots.Count);
        Assert.Equal(3, lots.Select(x => x.LotNo).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(lots, x => Assert.Equal(nameof(MaterialLotStatus.Quarantine), x.Status));
    }

    [Fact]
    public async Task Multi_create_distinct_lots_persist_independently_across_scopes()
    {
        // A2a fans out N INDEPENDENT HTTP requests (one DbContext scope each).
        // This proves the distinct-lot siblings each persist in isolation — the
        // guarantee the client loop relies on when it reports "N ok / M failed".
        // NOTE: the SINGLE-request duplicate-lot path (same lot in one POST) is
        // a PRE-EXISTING latent bug outside this PR's scope — CreateLotAsync
        // catches the unique-index DbUpdateException but leaves the failed
        // MaterialLot tracked, so the idempotency middleware's trailing
        // SaveChanges re-throws → 500 instead of a clean 409. Reported, not
        // fixed here (freeze/create path is locked for feat/iqc-search-by-desc).
        await SeedRawAsync("MC-INDEP", "multi indep");
        var c = await ClientAsync("qc-multi-indep", UserRole.Qc);

        for (var i = 1; i <= 3; i++)
        {
            var resp = await c.SendAsync(Post("/api/v2/iqc", Body("MC-INDEP", $"LOT-INDEP-{i:D2}")));
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        }

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var lots = await db.MaterialLots.AsNoTracking()
            .Where(l => l.LotNo.StartsWith("LOT-INDEP-"))
            .Select(l => l.LotNo).Distinct().ToListAsync();
        Assert.Equal(3, lots.Count);
    }

    // ── Bất biến — cache mô tả không đổi khi catalog rename (PA-A) ─

    [Fact]
    public async Task Create_caches_description_immutable_across_catalog_rename()
    {
        await SeedRawAsync("IFS-IMMUT", "Mô tả GỐC");
        var c = await ClientAsync("qc-iqc-immut", UserRole.Qc);

        var resp = await c.SendAsync(Post("/api/v2/iqc", Body("IFS-IMMUT", "LOT-IMMUT-1")));
        var body = (await resp.Content.ReadFromJsonAsync<CreateIqcTicketResponse>())!;
        Assert.Equal("Mô tả GỐC", body.MaterialDescription);

        // Rename catalog description sau khi tạo phiếu.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var rm = await db.RawMaterials.SingleAsync(x => x.PartNo == "IFS-IMMUT");
            rm.PartDescription = "Mô tả ĐÃ ĐỔI";
            await db.SaveChangesAsync();
        }

        // GET /iqc/{id} vẫn trả mô tả CŨ (snapshot bất biến).
        var get = await c.GetAsync($"/api/v2/iqc/{body.IqcInspectionId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var raw = await get.Content.ReadAsStringAsync();
        Assert.Contains("Mô tả GỐC", raw);
        Assert.DoesNotContain("Mô tả ĐÃ ĐỔI", raw);
    }
}
