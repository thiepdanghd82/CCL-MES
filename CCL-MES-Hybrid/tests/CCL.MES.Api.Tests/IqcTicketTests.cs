using System.Net;
using System.Net.Http.Json;
using System.Text;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Envelopes;
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

    private async Task SeedRawAsync(string partNo, string? desc = null, string? supplier = null,
        string? motherCode = null, double? widthMm = null)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        db.RawMaterials.Add(new RawMaterial
        {
            PartNo = partNo, PartDescription = desc, SupplierName = supplier,
            MotherCode = motherCode, WidthMm = widthMm,
        });
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
    public async Task SearchMaterial_returns_representative_row_mother_width_partdesc()
    {
        // feat/iqc-materials-line-table — dòng đại diện = OrderBy Id (dòng seed
        // đầu tiên của group). Seed 2 dòng cùng PartNo với Mother/Width khác nhau;
        // kết quả phải mang giá trị của dòng đầu.
        await SeedRawAsync("LINE-ENRICH-01", "MYLAR ENRICH sheet A", motherCode: "MOTHER-A", widthMm: 320.5);
        await SeedRawAsync("LINE-ENRICH-01", "MYLAR ENRICH sheet B", motherCode: "MOTHER-B", widthMm: 999);

        var c = await ClientAsync("qc-search-enrich", UserRole.Qc);
        var resp = await c.GetAsync("/api/v2/iqc/search-material?desc=" + Uri.EscapeDataString("MYLAR ENRICH"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = (await resp.Content.ReadFromJsonAsync<IqcMaterialSearchResponse>())!;
        var row = Assert.Single(body.Items, x => x.CodeIfs == "LINE-ENRICH-01");
        Assert.Equal("MOTHER-A", row.MotherCode);
        Assert.Equal(320.5, row.WidthMm);
        Assert.Equal("MYLAR ENRICH sheet A", row.PartDescription);
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

    [Fact]
    public async Task SearchMaterial_matches_part_no_substring_not_only_description()
    {
        // Ô Standards gõ "336" phải ra mã 336T dù mô tả không chứa 336.
        await SeedRawAsync("336T", "Acrylic tape clear");
        await SeedRawAsync("PET-100", "PET film 50um");
        await SeedRawAsync("BW-0112N-01", "unrelated liner");

        var c = await ClientAsync("qc-search-partno", UserRole.Qc);
        var byCode = (await (await c.GetAsync("/api/v2/iqc/search-material?desc=336"))
            .Content.ReadFromJsonAsync<IqcMaterialSearchResponse>())!;
        Assert.False(byCode.TooShort);
        Assert.Contains(byCode.Items, x => x.CodeIfs == "336T");
        Assert.DoesNotContain(byCode.Items, x => x.CodeIfs == "PET-100");

        var byDesc = (await (await c.GetAsync("/api/v2/iqc/search-material?desc=PET"))
            .Content.ReadFromJsonAsync<IqcMaterialSearchResponse>())!;
        Assert.Contains(byDesc.Items, x => x.CodeIfs == "PET-100");
        Assert.DoesNotContain(byDesc.Items, x => x.CodeIfs == "336T");
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
    public async Task Trung_lo_tra_409_sach_chu_KHONG_phai_500()
    {
        // Henry gặp thật trên máy 2026-08-28: tạo lại phiếu với đúng lô cũ ⇒
        // màn hình chỉ báo "Không lưu được phiếu.", log ra HTTP 500 với
        // `UNIQUE constraint failed: MaterialLots.LotNo, MaterialLots.RawMaterialId`.
        //
        // Nguyên nhân: CreateLotAsync BẮT được DbUpdateException và trả 409,
        // nhưng entity MaterialLot hỏng vẫn nằm trong change-tracker ở trạng
        // thái Added. IdempotencyMiddleware ghi sổ trên CÙNG DbContext, và
        // SaveChanges của nó phát lại đúng câu INSERT đó → ném ra ngoài mọi
        // handler → 500. Người dùng mất luôn thông báo "lô đã tồn tại".
        await SeedRawAsync("MC-DUP-LOT", "trùng lô");
        var c = await ClientAsync("qc-dup-lot", UserRole.Qc);

        var first = await c.SendAsync(Post("/api/v2/iqc", Body("MC-DUP-LOT", "LOT-TRUNG")));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await c.SendAsync(Post("/api/v2/iqc", Body("MC-DUP-LOT", "LOT-TRUNG")));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var err = await second.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("lot.duplicate", err!.Code);

        // Phiếu thứ hai phải rollback SẠCH — không để lại phiếu mồ côi.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.Equal(1, await db.MaterialLots.CountAsync(l => l.LotNo == "LOT-TRUNG"));
        Assert.Equal(1, await db.IqcInspections.CountAsync(i => i.BatchNumber == "LOT-TRUNG"));
    }

    [Fact]
    public async Task Multi_create_distinct_lots_persist_independently_across_scopes()
    {
        // A2a fans out N INDEPENDENT HTTP requests (one DbContext scope each).
        // This proves the distinct-lot siblings each persist in isolation — the
        // guarantee the client loop relies on when it reports "N ok / M failed".
        // Đường TRÙNG LÔ (cùng lô, POST thứ hai) từng là bug tiềm ẩn ghi ở đây
        // là "chưa sửa": CreateLotAsync bắt DbUpdateException nhưng để entity
        // hỏng nằm lại change-tracker ⇒ SaveChanges của IdempotencyMiddleware
        // phát lại INSERT đó → 500 thay vì 409. ĐÃ SỬA — xem
        // Trung_lo_tra_409_sach_chu_KHONG_phai_500 ngay dưới.
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

    // ── feat/iqc-module-tabs — Group additive + IQC Data list + Dashboard ──

    private static object BodyG(string group, string codeIfs, string lotBatchNo, double qty = 100) => new
    {
        group, codeIfs, lotBatchNo, quantity = qty,
    };

    [Fact]
    public async Task Create_group_chemical_persists_group_and_defaults_to_materials_when_absent()
    {
        var c = await ClientAsync("qc-iqc-group", UserRole.Qc);

        // Explicit Chemical → phiếu Group=Chemical.
        var chem = await c.SendAsync(Post("/api/v2/iqc", BodyG("Chemical", "CHEM-1", "LOT-CHEM-1")));
        Assert.Equal(HttpStatusCode.Created, chem.StatusCode);
        var chemBody = (await chem.Content.ReadFromJsonAsync<CreateIqcTicketResponse>())!;
        Assert.Equal("Chemical", chemBody.Group);

        // Absent group (form Materials cũ) → server default Materials.
        var mat = await c.SendAsync(Post("/api/v2/iqc", Body("MAT-1", "LOT-MAT-1")));
        Assert.Equal(HttpStatusCode.Created, mat.StatusCode);
        var matBody = (await mat.Content.ReadFromJsonAsync<CreateIqcTicketResponse>())!;
        Assert.Equal("Materials", matBody.Group);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var chemInsp = await db.IqcInspections.AsNoTracking().SingleAsync(x => x.Id == chemBody.IqcInspectionId);
        Assert.Equal("Chemical", chemInsp.Group);
        // Audit detail carries the group.
        var audit = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "IQC_CREATE" && a.TargetId == chemBody.IqcInspectionId.ToString())
            .SingleAsync();
        Assert.Contains("Chemical", audit.Detail);
    }

    [Fact]
    public async Task Create_unknown_group_falls_back_to_materials()
    {
        var c = await ClientAsync("qc-iqc-badgroup", UserRole.Qc);
        var resp = await c.SendAsync(Post("/api/v2/iqc", BodyG("Nonsense", "BADG-1", "LOT-BADG-1")));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<CreateIqcTicketResponse>())!;
        Assert.Equal("Materials", body.Group);
    }

    [Fact]
    public async Task Tickets_list_filters_by_group_and_returns_group_in_dto()
    {
        var c = await ClientAsync("qc-iqc-list", UserRole.Qc);
        await c.SendAsync(Post("/api/v2/iqc", BodyG("Chemical", "LST-CHEM", "LOT-LST-CHEM")));
        await c.SendAsync(Post("/api/v2/iqc", BodyG("Tools", "LST-TOOL", "LOT-LST-TOOL")));

        // Filter ?group=Chemical → only Chemical rows, each carrying Group.
        var chem = await c.GetFromJsonAsync<IqcTicketListResponse>("/api/v2/iqc/tickets?group=Chemical&pageSize=100");
        Assert.NotNull(chem);
        Assert.NotEmpty(chem!.Items);
        Assert.All(chem.Items, i => Assert.Equal("Chemical", i.Group));
        Assert.Contains(chem.Items, i => i.CodeIfs == "LST-CHEM");
        Assert.DoesNotContain(chem.Items, i => i.CodeIfs == "LST-TOOL");
    }

    [Fact]
    public async Task Dashboard_counts_by_group_and_status()
    {
        var c = await ClientAsync("qc-iqc-dash", UserRole.Qc);
        await c.SendAsync(Post("/api/v2/iqc", BodyG("Chemical", "DSH-CHEM", "LOT-DSH-CHEM")));
        await c.SendAsync(Post("/api/v2/iqc", Body("DSH-MAT", "LOT-DSH-MAT")));   // Materials default

        var dash = await c.GetFromJsonAsync<IqcDashboardResponse>("/api/v2/iqc/dashboard");
        Assert.NotNull(dash);
        Assert.True(dash!.Total >= 2);
        Assert.True(dash.Chemical >= 1);
        Assert.True(dash.Materials >= 1);
        // Sum-of-parts invariant: group buckets add up to the total.
        Assert.Equal(dash.Total, dash.Materials + dash.Chemical + dash.Tools + dash.Other);
        Assert.Equal(dash.Total, dash.Pending + dash.Pass + dash.Fail);
    }

    [Theory]
    [InlineData(UserRole.Operator)]
    public async Task Tickets_and_dashboard_forbidden_for_non_qcread(string role)
    {
        var c = await ClientAsync($"u-iqc-read-403-{role}", role);
        var list = await c.GetAsync("/api/v2/iqc/tickets");
        var dash = await c.GetAsync("/api/v2/iqc/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, dash.StatusCode);
    }

    // ── P13 bước 4 — cỡ mẫu AQL + đòi lý do khi đổi ────────────────

    private async Task<IqcInspection> InspAsync(long id)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        return await db.IqcInspections.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    [Fact]
    public async Task Don_vi_dem_duoc_thi_server_de_xuat_co_mau_theo_AQL()
    {
        await SeedRawAsync("IFS-SS-01", "Màng PET", "NCC A");
        var c = await ClientAsync("qc-ss-suggest", UserRole.Qc);

        var resp = await c.SendAsync(Post("/api/v2/iqc", new
        {
            codeIfs = "IFS-SS-01", lotBatchNo = "LOT-SS-01",
            quantity = 10.0, uom = "rolls",
        }));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<CreateIqcTicketResponse>())!;

        var insp = await InspAsync(body.IqcInspectionId);
        Assert.Equal(10L, insp.LotQty);
        // Bậc 2 của bảng (9–15) cho 3 mẫu; 3 < 10 nên không bị cắt ngọn.
        Assert.Equal(3, insp.SampleSizeSuggested);
        // Client không khai cỡ mẫu ⇒ NHẬN đề xuất, không phải "đổi thành 0".
        Assert.Equal(3, insp.SampleSize);
        Assert.Null(insp.SampleSizeOverrideReason);
    }

    [Fact]
    public async Task Doi_co_mau_ma_khong_ghi_ly_do_thi_422()
    {
        await SeedRawAsync("IFS-SS-02", "Màng PET", "NCC A");
        var c = await ClientAsync("qc-ss-noreason", UserRole.Qc);

        var resp = await c.SendAsync(Post("/api/v2/iqc", new
        {
            codeIfs = "IFS-SS-02", lotBatchNo = "LOT-SS-02",
            quantity = 10.0, uom = "rolls", sampleSize = 1,
        }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadAsStringAsync();
        Assert.Contains("iqc.sample_size_reason_required", err);

        // Từ chối SỚM: không được để lại phiếu mồ côi cho người dùng dọn.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.False(await db.IqcInspections.AnyAsync(x => x.LotNumber == "LOT-SS-02"));
    }

    [Fact]
    public async Task Doi_co_mau_KEM_ly_do_thi_201_va_luu_ly_do()
    {
        await SeedRawAsync("IFS-SS-03", "Màng PET", "NCC A");
        var c = await ClientAsync("qc-ss-reason", UserRole.Qc);

        var resp = await c.SendAsync(Post("/api/v2/iqc", new
        {
            codeIfs = "IFS-SS-03", lotBatchNo = "LOT-SS-03",
            quantity = 10.0, uom = "rolls", sampleSize = 1,
            sampleSizeOverrideReason = "NCC mới, kiểm siết 1 cuộn toàn bộ chiều dài",
        }));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<CreateIqcTicketResponse>())!;

        var insp = await InspAsync(body.IqcInspectionId);
        Assert.Equal(1, insp.SampleSize);
        Assert.Equal(3, insp.SampleSizeSuggested);      // đề xuất vẫn đóng băng
        Assert.Equal("NCC mới, kiểm siết 1 cuộn toàn bộ chiều dài",
                     insp.SampleSizeOverrideReason);
    }

    [Fact]
    public async Task Don_vi_lien_tuc_thi_KHONG_de_xuat_va_KHONG_doi_ly_do()
    {
        await SeedRawAsync("IFS-SS-04", "Màng PET", "NCC A");
        var c = await ClientAsync("qc-ss-cont", UserRole.Qc);

        // 5.000 m² là 3 cuộn chứ không phải 5.000 đơn vị. App không được giả vờ
        // biết, và cũng không được đòi giải trình cho con số nó chưa từng đưa ra.
        var resp = await c.SendAsync(Post("/api/v2/iqc", new
        {
            codeIfs = "IFS-SS-04", lotBatchNo = "LOT-SS-04",
            quantity = 5000.0, uom = "m2", sampleSize = 7,
        }));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<CreateIqcTicketResponse>())!;

        var insp = await InspAsync(body.IqcInspectionId);
        Assert.Null(insp.LotQty);
        Assert.Null(insp.SampleSizeSuggested);
        Assert.Equal(7, insp.SampleSize);
        Assert.Null(insp.SampleSizeOverrideReason);
    }
}
