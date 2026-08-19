using System.Net;
using System.Net.Http.Json;
using System.Text;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// A1 — mạch lô nguyên vật liệu, nghiệm thu §6 của hợp đồng.
///
/// Mỗi ca chặn khẳng định BA thứ, không phải một: đúng HTTP status, đúng mã lỗi
/// <c>lot.*</c>, và đúng MỘT dòng audit. Thiếu vế audit thì test vẫn xanh trong
/// khi "ai đã cố nạp lô chưa Released" biến mất — mà đó chính là dữ liệu điều
/// tra chất lượng A1 sinh ra để giữ.
/// </summary>
public sealed class MaterialLotScanTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public MaterialLotScanTests(MesApiFactory fx) => _fx = fx;

    // ── Helpers ────────────────────────────────────────────────────

    private async Task<HttpClient> ClientAsync(string user, string role = UserRole.Operator)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", role);
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, "P@ss!1");
        return c;
    }

    private static HttpRequestMessage Post(string path, string body, bool idem = true, string? key = null)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, path)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (idem) r.Headers.TryAddWithoutValidation("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return r;
    }

    /// <summary>WO + 1 dòng BOM (bom_line_idx = 0) với mã vật tư cho trước.</summary>
    private async Task<long> SeedWoAsync(string tag, string materialCode, string phase = "PREPRESS")
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var cust = new Customer { Code = "C-" + tag, Name = tag };
        db.Customers.Add(cust); await db.SaveChangesAsync();
        var prod = new Product { ProductCode = "P-" + tag, Name = tag, CustomerId = cust.Id };
        db.Products.Add(prod); await db.SaveChangesAsync();
        var wo = new WorkOrder
        {
            WoNo = "WO-A1-" + tag, CustomerId = cust.Id, ProductId = prod.Id, ProductName = tag,
            TargetQty = 1000, Uom = "pcs", CurrentStep = ProcessStepCode.PrePressCheck,
            MesPhase = phase, Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo); await db.SaveChangesAsync();
        db.WoMaterials.Add(new WoMaterial
        {
            WorkOrderId = wo.Id, BomLineIdx = 0, MaterialCode = materialCode,
            MaterialDescription = materialCode, QtyRequired = 100, Uom = "m2",
        });
        await db.SaveChangesAsync();
        return wo.Id;
    }

    private async Task<long> SeedLotAsync(
        string lotNo, string partNo, string status = nameof(MaterialLotStatus.Released),
        double qty = 100, DateTime? expiry = null, long? iqcId = null)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var lot = new MaterialLot
        {
            LotNo = lotNo, PartNo = partNo, ReceivedAt = DateTime.UtcNow,
            QtyReceived = qty, QtyAvailable = qty, Status = status, ExpiryAt = expiry,
            IqcInspectionId = iqcId, Uom = "m2",
        };
        db.MaterialLots.Add(lot); await db.SaveChangesAsync();
        return lot.Id;
    }

    private async Task<int> AuditCountAsync(string action, string? targetId = null)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var q = db.AuditLogs.AsNoTracking().Where(a => a.Action == action);
        if (targetId is not null) q = q.Where(a => a.TargetId == targetId);
        return await q.CountAsync();
    }

    private static string ConsumeUrl(long woId, int idx = 0)
        => $"/api/v2/work-orders/{woId}/materials/{idx}/consume";

    private static string ConsumeBody(string lotNo, double qty = 10)
        => $"{{\"lotNo\":\"{lotNo}\",\"qtyUsed\":{qty.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";

    // ── Happy path ─────────────────────────────────────────────────

    [Fact]
    public async Task Consume_happy_path_writes_one_row_decrements_lot_and_emits_one_audit()
    {
        var wo = await SeedWoAsync("HAPPY", "PVC-50");
        var lotId = await SeedLotAsync("LOT-HAPPY-1", "PVC-50", qty: 100);
        var c = await ClientAsync("op-a1-happy");

        var before = await AuditCountAsync("MATERIAL_LOT_CONSUME", lotId.ToString());
        var resp = await c.SendAsync(Post(ConsumeUrl(wo), ConsumeBody("LOT-HAPPY-1", 12.5)));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = (await resp.Content.ReadFromJsonAsync<MaterialLotSetResponse>())!;
        Assert.True(body.Ok);
        Assert.Equal(87.5, body.QtyAvailableAfter);
        Assert.NotNull(body.ConsumptionId);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.Equal(1, await db.WoMaterialConsumptions.CountAsync(x => x.MaterialLotId == lotId));
        Assert.Equal(87.5, await db.MaterialLots.Where(l => l.Id == lotId)
            .Select(l => l.QtyAvailable).SingleAsync());
        Assert.Equal(before + 1, await AuditCountAsync("MATERIAL_LOT_CONSUME", lotId.ToString()));
    }

    [Fact]
    public async Task Consume_sets_fk_on_wo_material_and_mirrors_canonical_lot_no()
    {
        var wo = await SeedWoAsync("MIRROR", "PVC-51");
        var lotId = await SeedLotAsync("LOT-MIRROR-1", "PVC-51");
        var c = await ClientAsync("op-a1-mirror");

        // Operator gõ chữ thường; cột mirror phải nhận kiểu chữ CỦA LÔ.
        var resp = await c.SendAsync(Post(ConsumeUrl(wo), ConsumeBody("lot-mirror-1", 5)));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var row = await db.WoMaterials.SingleAsync(m => m.WorkOrderId == wo && m.BomLineIdx == 0);
        Assert.Equal("LOT-MIRROR-1", row.LotNo);
        Assert.Equal(lotId, db.Entry(row).Property<long?>("MaterialLotId").CurrentValue);
    }

    // ── Sáu ca chặn: HTTP + mã lỗi + đúng 1 dòng audit ─────────────

    [Fact]
    public async Task Consume_unknown_lot_returns_404_lot_not_found_with_audit()
    {
        var wo = await SeedWoAsync("NF", "PVC-52");
        var c = await ClientAsync("op-a1-nf");
        var before = await AuditCountAsync("MATERIAL_LOT_SCAN_DENIED", wo.ToString());

        var resp = await c.SendAsync(Post(ConsumeUrl(wo), ConsumeBody("LOT-DOES-NOT-EXIST")));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Contains("lot.not_found", await resp.Content.ReadAsStringAsync());
        Assert.Equal(before + 1, await AuditCountAsync("MATERIAL_LOT_SCAN_DENIED", wo.ToString()));
    }

    [Theory]
    [InlineData(nameof(MaterialLotStatus.Quarantine), "lot.not_released")]
    [InlineData(nameof(MaterialLotStatus.Rejected), "lot.rejected")]
    public async Task Consume_blocked_status_returns_422_with_code_and_one_audit(
        string status, string expectedCode)
    {
        // Cờ EnforceReleased mặc định TẮT nên hai ca này bình thường chỉ cảnh
        // báo. Test dựng service với cờ BẬT để khẳng định luật khi đã siết.
        var wo = await SeedWoAsync("ST" + status, "PVC-53");
        var lotId = await SeedLotAsync("LOT-ST-" + status, "PVC-53", status: status);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var svc = EnforcingService(db);

        var before = await db.AuditLogs.CountAsync(a => a.Action == "MATERIAL_LOT_SCAN_DENIED");
        var r = await svc.ConsumeAsync(wo, 0, "LOT-ST-" + status, 5, null, "qc1", UserRole.Qc);

        Assert.False(r.Ok);
        Assert.Equal(422, r.HttpStatus);
        Assert.Equal(expectedCode, r.ErrorCode);
        Assert.Equal(before + 1, await db.AuditLogs.CountAsync(a => a.Action == "MATERIAL_LOT_SCAN_DENIED"));
        Assert.Equal(0, await db.WoMaterialConsumptions.CountAsync(x => x.MaterialLotId == lotId));
    }

    [Fact]
    public async Task Consume_part_mismatch_returns_422_even_when_flag_off()
    {
        // Sai vật tư KHÔNG được nới trong grace period: đó là cầm nhầm cuộn,
        // không phải "kho chưa kịp làm IQC".
        var wo = await SeedWoAsync("PM", "PVC-54");
        await SeedLotAsync("LOT-PM-1", "SOMETHING-ELSE");
        var c = await ClientAsync("op-a1-pm");
        var before = await AuditCountAsync("MATERIAL_LOT_SCAN_DENIED", wo.ToString());

        var resp = await c.SendAsync(Post(ConsumeUrl(wo), ConsumeBody("LOT-PM-1")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<MaterialLotSetResponse>())!;
        Assert.Equal("lot.part_mismatch", body.ErrorCode);
        Assert.Equal(before + 1, await AuditCountAsync("MATERIAL_LOT_SCAN_DENIED", wo.ToString()));
    }

    [Fact]
    public async Task Consume_depleted_lot_returns_422_even_when_flag_off()
    {
        var wo = await SeedWoAsync("DP", "PVC-55");
        await SeedLotAsync("LOT-DP-1", "PVC-55", qty: 0);
        var c = await ClientAsync("op-a1-dp");

        var resp = await c.SendAsync(Post(ConsumeUrl(wo), ConsumeBody("LOT-DP-1")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<MaterialLotSetResponse>())!;
        Assert.Equal("lot.depleted", body.ErrorCode);
    }

    [Fact]
    public async Task Consume_invalid_request_returns_422_and_emits_no_audit()
    {
        // Cú pháp sai = chưa chạm nghiệp vụ ⇒ KHÔNG để lại vết (§5).
        var wo = await SeedWoAsync("IR", "PVC-56");
        var c = await ClientAsync("op-a1-ir");
        var before = await AuditCountAsync("MATERIAL_LOT_SCAN_DENIED", wo.ToString());

        var resp = await c.SendAsync(Post(ConsumeUrl(wo), "{\"lotNo\":\"\",\"qtyUsed\":0}"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<MaterialLotSetResponse>())!;
        Assert.Equal("lot.invalid_request", body.ErrorCode);
        Assert.Equal(before, await AuditCountAsync("MATERIAL_LOT_SCAN_DENIED", wo.ToString()));
    }

    [Fact]
    public async Task Consume_without_idempotency_key_returns_400()
    {
        var wo = await SeedWoAsync("NOIDEM", "PVC-57");
        await SeedLotAsync("LOT-NOIDEM-1", "PVC-57");
        var c = await ClientAsync("op-a1-noidem");

        var resp = await c.SendAsync(Post(ConsumeUrl(wo), ConsumeBody("LOT-NOIDEM-1"), idem: false));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Khoá thứ tự kiểm (§5) ──────────────────────────────────────

    [Fact]
    public async Task Part_mismatch_is_reported_before_status()
    {
        // Lô VỪA sai vật tư VỪA đang Quarantine. Phải trả part_mismatch —
        // nếu trả not_released thì operator bỏ máy đi tìm QC trong khi vấn đề
        // thật chỉ là cầm nhầm cuộn.
        var wo = await SeedWoAsync("ORDER", "PVC-58");
        await SeedLotAsync("LOT-ORDER-1", "OTHER-PART",
            status: nameof(MaterialLotStatus.Quarantine));

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var r = await EnforcingService(db)
            .ConsumeAsync(wo, 0, "LOT-ORDER-1", 5, null, "op1", UserRole.Operator);

        Assert.Equal("lot.part_mismatch", r.ErrorCode);
    }

    [Fact]
    public async Task Expiry_in_past_blocks_even_when_status_is_Released()
    {
        var wo = await SeedWoAsync("EXP", "PVC-59");
        await SeedLotAsync("LOT-EXP-1", "PVC-59",
            status: nameof(MaterialLotStatus.Released),
            expiry: DateTime.UtcNow.AddDays(-1));

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var r = await EnforcingService(db)
            .ConsumeAsync(wo, 0, "LOT-EXP-1", 5, null, "op1", UserRole.Operator);

        Assert.Equal("lot.expired", r.ErrorCode);
    }

    // ── Khoá tự nhiên chuỗi: NOCASE + TRIM ─────────────────────────

    [Theory]
    [InlineData("LOT-CASE-1")]
    [InlineData("lot-case-1")]
    [InlineData("LoT-CaSe-1")]
    public async Task Lot_lookup_is_case_insensitive(string typed)
    {
        var wo = await SeedWoAsync("CASE" + typed.GetHashCode().ToString("X"), "PVC-60");
        await SeedLotAsync("LOT-CASE-1" + typed.GetHashCode().ToString("X"), "PVC-60");
        // dùng đúng lô riêng cho từng biến thể để test chạy song song được
        var lotNo = "LOT-CASE-1" + typed.GetHashCode().ToString("X");
        var probe = typed.StartsWith("LOT", StringComparison.Ordinal) ? lotNo
            : typed.StartsWith("lot", StringComparison.Ordinal) ? lotNo.ToLowerInvariant()
            : lotNo.ToLowerInvariant().Replace("lot", "LoT");

        var c = await ClientAsync("op-case-" + typed.GetHashCode().ToString("X"));
        var resp = await c.SendAsync(Post(ConsumeUrl(wo), ConsumeBody(probe, 1)));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Lot_lookup_trims_whitespace()
    {
        var wo = await SeedWoAsync("TRIM", "PVC-61");
        await SeedLotAsync("LOT-TRIM-1", "PVC-61");
        var c = await ClientAsync("op-a1-trim");

        var resp = await c.SendAsync(Post(ConsumeUrl(wo),
            "{\"lotNo\":\"   LOT-TRIM-1  \",\"qtyUsed\":1}"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Duplicate_lot_differing_only_in_case_is_rejected_by_schema()
    {
        // Ba lớp siết khoá chuỗi nằm ở SCHEMA, không ở C# — test này chứng minh
        // chính cái schema đó chặn, kể cả khi ai đó vòng qua service.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        db.MaterialLots.Add(new MaterialLot
        {
            LotNo = "LOT-DUP-1", PartNo = "PVC-62", ReceivedAt = DateTime.UtcNow,
            QtyReceived = 1, QtyAvailable = 1, Status = nameof(MaterialLotStatus.Quarantine),
        });
        await db.SaveChangesAsync();

        db.MaterialLots.Add(new MaterialLot
        {
            LotNo = "lot-dup-1", PartNo = "pvc-62", ReceivedAt = DateTime.UtcNow,
            QtyReceived = 1, QtyAvailable = 1, Status = nameof(MaterialLotStatus.Quarantine),
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ── Đ4 — mỗi lần quét là một dòng ──────────────────────────────

    [Fact]
    public async Task Same_idempotency_key_twice_creates_one_row()
    {
        var wo = await SeedWoAsync("IDEM1", "PVC-63");
        var lotId = await SeedLotAsync("LOT-IDEM-1", "PVC-63", qty: 100);
        var c = await ClientAsync("op-a1-idem1");
        var key = Guid.NewGuid().ToString();

        var r1 = await c.SendAsync(Post(ConsumeUrl(wo), ConsumeBody("LOT-IDEM-1", 10), key: key));
        var r2 = await c.SendAsync(Post(ConsumeUrl(wo), ConsumeBody("LOT-IDEM-1", 10), key: key));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.True(r2.Headers.Contains("Idempotency-Replayed"));

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.Equal(1, await db.WoMaterialConsumptions.CountAsync(x => x.MaterialLotId == lotId));
        Assert.Equal(90, await db.MaterialLots.Where(l => l.Id == lotId)
            .Select(l => l.QtyAvailable).SingleAsync());
    }

    [Fact]
    public async Task Different_idempotency_keys_create_two_rows_both_visible_in_trace()
    {
        // Đ4 có ý thức: hồ sơ chi tiết hơn, chống bấm nhầm yếu hơn. Hai lần quét
        // khác key = hai dòng, và CẢ HAI phải hiện trong bảng truy xuất.
        var wo = await SeedWoAsync("IDEM2", "PVC-64");
        var lotId = await SeedLotAsync("LOT-IDEM-2", "PVC-64", qty: 100);
        var c = await ClientAsync("op-a1-idem2");

        await c.SendAsync(Post(ConsumeUrl(wo), ConsumeBody("LOT-IDEM-2", 10)));
        await c.SendAsync(Post(ConsumeUrl(wo), ConsumeBody("LOT-IDEM-2", 10)));

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.Equal(2, await db.WoMaterialConsumptions.CountAsync(x => x.MaterialLotId == lotId));

        var view = await c.GetFromJsonAsync<MaterialGenealogyView>(
            $"/api/v2/work-orders/{wo}/material-genealogy");
        Assert.Equal(2, view!.Rows.Count);
        // Con số tiêu hao phải là SUM, KHÔNG phải dòng cuối (§2 hệ quả 3).
        Assert.All(view.Rows, r => Assert.Equal(20, r.QtyUsedTotalForLot));
    }

    // ── Đ3 — đảo tiêu thụ + gia hạn hai chữ ký ─────────────────────

    [Fact]
    public async Task Reversal_restores_lot_to_Released_when_qty_returns_above_zero()
    {
        var wo = await SeedWoAsync("REV", "PVC-65");
        var lotId = await SeedLotAsync("LOT-REV-1", "PVC-65", qty: 10);
        var op = await ClientAsync("op-a1-rev");

        var consume = await op.SendAsync(Post(ConsumeUrl(wo), ConsumeBody("LOT-REV-1", 10)));
        var body = (await consume.Content.ReadFromJsonAsync<MaterialLotSetResponse>())!;
        Assert.Equal(nameof(MaterialLotStatus.Consumed), body.LotStatus);

        var sup = await ClientAsync("sup-a1-rev", UserRole.Supervisor);
        var rev = await sup.SendAsync(Post(
            $"/api/v2/material-consumptions/{body.ConsumptionId}/reverse",
            "{\"reason\":\"quét nhầm dòng BOM\"}"));
        Assert.Equal(HttpStatusCode.OK, rev.StatusCode);
        var revBody = (await rev.Content.ReadFromJsonAsync<MaterialLotSetResponse>())!;
        Assert.Equal(nameof(MaterialLotStatus.Released), revBody.LotStatus);
        Assert.Equal(10, revBody.QtyAvailableAfter);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        // Append-only: dòng cũ vẫn còn, chỉ được ĐÁNH DẤU.
        var c0 = await db.WoMaterialConsumptions.SingleAsync(x => x.MaterialLotId == lotId);
        Assert.NotNull(c0.ReversedAt);
        Assert.Equal(10, c0.QtyUsed);
        Assert.Equal("sup-a1-rev", c0.ReversedBy);
    }

    [Fact]
    public async Task Only_supervisor_can_reverse()
    {
        var wo = await SeedWoAsync("REVRBAC", "PVC-66");
        await SeedLotAsync("LOT-REVRBAC-1", "PVC-66", qty: 10);
        var op = await ClientAsync("op-a1-revrbac");
        var consume = await op.SendAsync(Post(ConsumeUrl(wo), ConsumeBody("LOT-REVRBAC-1", 5)));
        var body = (await consume.Content.ReadFromJsonAsync<MaterialLotSetResponse>())!;

        var denied = await op.SendAsync(Post(
            $"/api/v2/material-consumptions/{body.ConsumptionId}/reverse",
            "{\"reason\":\"thử\"}"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("lot.forbidden", await denied.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Expired_lot_extension_requires_two_distinct_signers()
    {
        var lotId = await SeedLotAsync("LOT-EXT-1", "PVC-67",
            status: nameof(MaterialLotStatus.Expired), expiry: DateTime.UtcNow.AddDays(-5));
        var qc1 = await ClientAsync("qc-a1-ext-1", UserRole.Qc);

        // Chữ ký 1 — QC kiểm lại (Expired → Quarantine ghi RetestedBy).
        var retest = await qc1.SendAsync(Post($"/api/v2/material-lots/{lotId}/status",
            "{\"status\":\"Quarantine\",\"reason\":\"kiểm lại sau hết hạn\"}"));
        Assert.Equal(HttpStatusCode.OK, retest.StatusCode);

        // Đưa lô về Expired để đúng tiền đề gia hạn.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var l = await db.MaterialLots.SingleAsync(x => x.Id == lotId);
            l.Status = nameof(MaterialLotStatus.Expired);
            await db.SaveChangesAsync();
            Assert.Equal("qc-a1-ext-1", l.RetestedBy);
        }

        // Cùng người ký chữ ký 2 ⇒ TỪ CHỐI.
        var same = await qc1.SendAsync(Post($"/api/v2/material-lots/{lotId}/extend-expiry",
            $"{{\"expiryExtendedTo\":\"{DateTime.UtcNow.AddDays(30):O}\"}}"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, same.StatusCode);
        var sameBody = (await same.Content.ReadFromJsonAsync<MaterialLotSetResponse>())!;
        Assert.Equal("lot.same_signer", sameBody.ErrorCode);

        // Người KHÁC ký chữ ký 2 ⇒ chấp nhận, lô về Released.
        var qc2 = await ClientAsync("qc-a1-ext-2", UserRole.Qc);
        var other = await qc2.SendAsync(Post($"/api/v2/material-lots/{lotId}/extend-expiry",
            $"{{\"expiryExtendedTo\":\"{DateTime.UtcNow.AddDays(30):O}\"}}"));
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        var okBody = (await other.Content.ReadFromJsonAsync<MaterialLotSetResponse>())!;
        Assert.Equal(nameof(MaterialLotStatus.Released), okBody.LotStatus);
        Assert.Equal(1, await AuditCountAsync("MATERIAL_LOT_EXPIRY_EXTENDED", lotId.ToString()));
    }

    // ── RBAC ───────────────────────────────────────────────────────

    [Fact]
    public async Task Operator_can_consume_but_cannot_change_lot_status()
    {
        var wo = await SeedWoAsync("RBAC", "PVC-68");
        var lotId = await SeedLotAsync("LOT-RBAC-1", "PVC-68");
        var op = await ClientAsync("op-a1-rbac");

        var consume = await op.SendAsync(Post(ConsumeUrl(wo), ConsumeBody("LOT-RBAC-1", 1)));
        Assert.Equal(HttpStatusCode.OK, consume.StatusCode);

        var setStatus = await op.SendAsync(Post($"/api/v2/material-lots/{lotId}/status",
            "{\"status\":\"Released\"}"));
        Assert.Equal(HttpStatusCode.Forbidden, setStatus.StatusCode);
    }

    // ── Concurrency ────────────────────────────────────────────────

    [Fact]
    public async Task Concurrent_consume_of_same_lot_yields_exactly_one_winner()
    {
        // Tồn CHỈ ĐỦ CHO MỘT người: N operator cùng quét một cuộn, mỗi người xin
        // trọn số tồn. Đây mới là kịch bản thật của "bán vượt tồn" — nếu để tồn
        // dư thì mọi request đều hợp lệ và test không chứng minh được gì.
        const int N = 8;
        var wo = await SeedWoAsync("RACE", "PVC-69");
        var lotId = await SeedLotAsync("LOT-RACE-1", "PVC-69", qty: 10);

        var clients = new List<HttpClient>();
        for (var i = 0; i < N; i++) clients.Add(await ClientAsync($"op-race-{i}"));

        var gate = new TaskCompletionSource();
        var tasks = clients.Select(async c =>
        {
            await gate.Task;
            return await c.SendAsync(Post(ConsumeUrl(wo), ConsumeBody("LOT-RACE-1", 10)));
        }).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        var ok = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflict = results.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        var depleted = results.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);

        // ĐÚNG MỘT người thắng. Người thua nhận 409 (đọc số tồn cũ rồi thua cuộc
        // đua ghi — optimistic lock trên RowVersion) HOẶC 422 lot.depleted (đọc
        // sau khi người thắng đã commit). Cả hai đều là từ chối hợp lệ; điều
        // KHÔNG được phép là một request thứ hai trả 200.
        Assert.Equal(1, ok);
        Assert.Equal(N, ok + conflict + depleted);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.Equal(1, await db.WoMaterialConsumptions.CountAsync(x => x.MaterialLotId == lotId));
        // Bất biến cuối: tồn không bao giờ âm.
        var left = await db.MaterialLots.Where(l => l.Id == lotId)
            .Select(l => l.QtyAvailable).SingleAsync();
        Assert.Equal(0, left);
    }

    // ── Grace period (cờ mặc định TẮT) ─────────────────────────────

    [Fact]
    public async Task Grace_period_records_the_scan_and_returns_a_warning_instead_of_422()
    {
        // Đây là số liệu Henry cần TRƯỚC khi lật cờ: bao nhiêu ca sẽ bị chặn.
        var wo = await SeedWoAsync("GRACE", "PVC-70");
        var lotId = await SeedLotAsync("LOT-GRACE-1", "PVC-70",
            status: nameof(MaterialLotStatus.Quarantine));
        var c = await ClientAsync("op-a1-grace");

        var resp = await c.SendAsync(Post(ConsumeUrl(wo), ConsumeBody("LOT-GRACE-1", 5)));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<MaterialLotSetResponse>())!;
        Assert.True(body.Ok);
        Assert.Equal("lot.not_released", body.Warning);
        Assert.False(body.Enforced);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.Equal(1, await db.WoMaterialConsumptions.CountAsync(x => x.MaterialLotId == lotId));
    }

    // ── Backfill ───────────────────────────────────────────────────

    [Fact]
    public async Task Backfill_is_idempotent_when_run_twice()
    {
        var wo = await SeedWoAsync("BF1", "BF-PART-1");
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var m = await db.WoMaterials.SingleAsync(x => x.WorkOrderId == wo);
            m.LotNo = "LOT-BF-1";
            m.QtyLoaded = 7;
            await db.SaveChangesAsync();
        }

        using var s1 = _fx.Services.CreateScope();
        var db1 = s1.ServiceProvider.GetRequiredService<MesDbContext>();
        var svc = new MaterialLotBackfillService(db1, new NoopAudit());

        var r1 = await svc.RunAsync();
        var lots1 = await db1.MaterialLots.CountAsync();
        var cons1 = await db1.WoMaterialConsumptions.CountAsync(c => c.CreatedBy == "backfill-a1");

        var r2 = await svc.RunAsync();
        var lots2 = await db1.MaterialLots.CountAsync();
        var cons2 = await db1.WoMaterialConsumptions.CountAsync(c => c.CreatedBy == "backfill-a1");

        Assert.True(r1.ConsumptionsCreated >= 1);
        Assert.Equal(0, r2.ConsumptionsCreated);       // lần hai không sinh gì
        Assert.Equal(r1.Candidates, r2.Skipped);       // và bỏ qua đúng bằng số ứng viên
        Assert.Equal(lots1, lots2);
        Assert.Equal(cons1, cons2);
    }

    // ── Backfill qua ENDPOINT (AdminOnly) ──────────────────────────
    //
    // Trước PR này MaterialLotBackfillService chỉ được dựng TRONG TEST: không
    // endpoint, không CLI, không hosted service ⇒ backfill không có đường chạy
    // trên môi trường thật. Ba test dưới khoá đường gọi đó lại.

    private const string BackfillUrl = "/api/v2/material-lots/backfill";

    /// <summary>Ghi hàng loạt trên toàn bộ dữ liệu lịch sử — operator không có
    /// cửa. 403 phải đến TỪ policy, trước cả khi chạm service.</summary>
    [Fact]
    public async Task Backfill_endpoint_denies_operator_with_403()
    {
        var op = await ClientAsync("op-a1-bf-403", UserRole.Operator);

        var before = await AuditCountAsync("MATERIAL_LOT_STATUS_SET", "backfill-a1");
        var resp = await op.SendAsync(Post(BackfillUrl, "{}"));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        // Bị chặn ⇒ service không chạy ⇒ không có dòng audit nào sinh thêm.
        Assert.Equal(before, await AuditCountAsync("MATERIAL_LOT_STATUS_SET", "backfill-a1"));
    }

    /// <summary>Admin → 200 + đủ số liệu, trong đó <c>quarantined</c> là con số
    /// dùng để quyết ngày lật cờ <c>Mes:MaterialLot:EnforceReleased</c>.</summary>
    [Fact]
    public async Task Backfill_endpoint_as_admin_returns_counts_including_quarantined()
    {
        var wo = await SeedWoAsync("BFEP1", "BF-PART-EP1");
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var m = await db.WoMaterials.SingleAsync(x => x.WorkOrderId == wo);
            m.LotNo = "LOT-BF-EP1";     // không khớp phiếu IQC nào ⇒ Đ6 Quarantine
            m.QtyLoaded = 9;
            await db.SaveChangesAsync();
        }

        var admin = await ClientAsync("admin-a1-bf-ok", UserRole.Admin);
        var auditBefore = await AuditCountAsync("MATERIAL_LOT_STATUS_SET", "backfill-a1");

        var resp = await admin.SendAsync(Post(BackfillUrl, "{}"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = (await resp.Content.ReadFromJsonAsync<MaterialLotBackfillResponse>())!;
        Assert.True(body.Candidates >= 1);
        Assert.True(body.ConsumptionsCreated >= 1);
        Assert.True(body.Quarantined >= 1, "lô không khớp IQC phải ra Quarantine (Đ6)");

        // Số liệu trong thân phản hồi phải khớp thứ THẬT SỰ nằm dưới DB.
        using var s = _fx.Services.CreateScope();
        var db2 = s.ServiceProvider.GetRequiredService<MesDbContext>();
        var lot = await db2.MaterialLots.SingleAsync(l => l.LotNo == "LOT-BF-EP1");
        Assert.Equal(nameof(MaterialLotStatus.Quarantine), lot.Status);

        var cons = await db2.WoMaterialConsumptions
            .SingleAsync(c => c.MaterialLotId == lot.Id && c.CreatedBy == "backfill-a1");
        Assert.Equal(9, cons.QtyUsed);

        // Mạch lô neo vào KHOÁ SỐ, không còn dựa vào chuỗi.
        var mat = await db2.WoMaterials.SingleAsync(x => x.WorkOrderId == wo);
        Assert.Equal(lot.Id, db2.Entry(mat).Property<long?>("MaterialLotId").CurrentValue);

        // Đúng MỘT dòng audit cho cả lần chạy — hợp đồng chỉ liệt kê 5 mã, endpoint
        // KHÔNG được thêm mã thứ sáu hay emit lần thứ hai.
        Assert.Equal(auditBefore + 1, await AuditCountAsync("MATERIAL_LOT_STATUS_SET", "backfill-a1"));
    }

    /// <summary>Gọi hai lần → lần hai không tạo thêm dòng nào. Dùng HAI
    /// Idempotency-Key KHÁC nhau có chủ đích: nếu dùng cùng key thì middleware
    /// phát lại response cũ và test sẽ xanh mà không chứng minh được gì về dấu
    /// <c>backfill-a1</c> ở tầng service (§2 hệ quả 2).</summary>
    [Fact]
    public async Task Backfill_endpoint_twice_creates_no_extra_rows()
    {
        var wo = await SeedWoAsync("BFEP2", "BF-PART-EP2");
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var m = await db.WoMaterials.SingleAsync(x => x.WorkOrderId == wo);
            m.LotNo = "LOT-BF-EP2";
            await db.SaveChangesAsync();
        }

        var admin = await ClientAsync("admin-a1-bf-twice", UserRole.Admin);

        var r1 = await admin.SendAsync(Post(BackfillUrl, "{}", key: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var b1 = (await r1.Content.ReadFromJsonAsync<MaterialLotBackfillResponse>())!;
        var (lots1, cons1) = await BackfillRowCountsAsync();

        var r2 = await admin.SendAsync(Post(BackfillUrl, "{}", key: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var b2 = (await r2.Content.ReadFromJsonAsync<MaterialLotBackfillResponse>())!;
        var (lots2, cons2) = await BackfillRowCountsAsync();

        Assert.True(b1.ConsumptionsCreated >= 1);
        Assert.Equal(0, b2.ConsumptionsCreated);      // lần hai không sinh gì
        Assert.Equal(0, b2.LotsCreated);
        Assert.Equal(0, b2.Quarantined);
        Assert.Equal(b2.Candidates, b2.Skipped);      // bỏ qua đúng bằng số ứng viên
        Assert.Equal(lots1, lots2);                   // rowcount không đổi
        Assert.Equal(cons1, cons2);
    }

    /// <summary>(số MaterialLots, số dòng tiêu hao mang dấu backfill).</summary>
    private async Task<(int Lots, int Consumptions)> BackfillRowCountsAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        return (await db.MaterialLots.CountAsync(),
                await db.WoMaterialConsumptions.CountAsync(c => c.CreatedBy == "backfill-a1"));
    }

    [Fact]
    public async Task Unresolved_lot_becomes_Quarantine()
    {
        var wo = await SeedWoAsync("BF2", "BF-PART-2");
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var m = await db.WoMaterials.SingleAsync(x => x.WorkOrderId == wo);
        m.LotNo = "LOT-BF-UNRESOLVED";
        await db.SaveChangesAsync();

        await new MaterialLotBackfillService(db, new NoopAudit()).RunAsync();

        var lot = await db.MaterialLots.SingleAsync(l => l.LotNo == "LOT-BF-UNRESOLVED");
        Assert.Equal(nameof(MaterialLotStatus.Quarantine), lot.Status);
        Assert.Null(lot.IqcInspectionId);
    }

    [Fact]
    public async Task Resolved_lot_inherits_status_from_iqc_result()
    {
        var wo = await SeedWoAsync("BF3", "BF-PART-3");
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

        db.IqcInspections.Add(new IqcInspection
        {
            PartNo = "BF-PART-3", BatchNumber = "B1", LotNumber = "LOT-BF-PASS",
            ReceivedDate = DateTime.UtcNow, Quantity = 100, Result = QcResult.Pass,
        });
        await db.SaveChangesAsync();

        var m = await db.WoMaterials.SingleAsync(x => x.WorkOrderId == wo);
        m.LotNo = "LOT-BF-PASS";
        await db.SaveChangesAsync();

        await new MaterialLotBackfillService(db, new NoopAudit()).RunAsync();

        var lot = await db.MaterialLots.SingleAsync(l => l.LotNo == "LOT-BF-PASS");
        Assert.Equal(nameof(MaterialLotStatus.Released), lot.Status);
        Assert.NotNull(lot.IqcInspectionId);
    }

    // ── Hạ tầng test ───────────────────────────────────────────────

    /// <summary>Service với cờ <c>EnforceReleased</c> BẬT — để test luật sau
    /// khi Henry lật cờ, không phải hành vi grace period mặc định.</summary>
    private static MaterialLotScanService EnforcingService(MesDbContext db) =>
        new(db, new NoopAudit(db),
            Microsoft.Extensions.Options.Options.Create(
                new MaterialLotOptions { EnforceReleased = true }));

    private sealed class NoopAudit : CCL.MES.Application.Audit.IAuditWriter
    {
        private readonly MesDbContext? _db;
        public NoopAudit(MesDbContext? db = null) => _db = db;

        public async Task EmitAsync(string action, string actor, string actorRole,
            string? targetType = null, string? targetId = null, string? detail = null,
            string source = "Web")
        {
            if (_db is null) return;
            _db.AuditLogs.Add(new AuditLog
            {
                Timestamp = DateTime.UtcNow, ActorUsername = actor, ActorRole = actorRole,
                Action = action, TargetType = targetType, TargetId = targetId,
                Detail = detail, Source = source,
            });
            await _db.SaveChangesAsync();
        }
    }
}
