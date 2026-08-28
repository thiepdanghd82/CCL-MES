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
/// P12 bước 2b — nghiệm thu <c>/api/v2/iqc/specs</c> qua WIRE.
///
/// <para>Khoá tầng thứ BA của phân quyền: service đã chặn (IqcSpecEditTests),
/// UI đã ẩn affordance (IqcSpecEditorTests) — ở đây khoá policy trên đường
/// HTTP. Ẩn nút không phải phân quyền; ba tầng phải cùng nói một điều.</para>
/// </summary>
public sealed class IqcSpecControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public IqcSpecControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> ClientAsync(string user, string role)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", role);
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, "P@ss!1");
        return c;
    }

    private static HttpRequestMessage Mk(
        HttpMethod m, string path, object? body = null, bool idem = true)
    {
        var r = new HttpRequestMessage(m, path);
        if (body is not null)
            r.Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (idem) r.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        return r;
    }

    /// <summary>Một hạng mục thư viện có thật để thêm được.</summary>
    private async Task SeedLibraryItemAsync(string itemId)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        if (await db.IqcCheckItemLibraries.AnyAsync(x => x.ItemId == itemId)) return;
        db.IqcCheckItemLibraries.Add(new IqcCheckItemLibrary
        {
            ItemId = itemId, GroupCode = "NQ", GroupLabelVi = "Ngoại quan",
            ItemVi = "Tem nhãn " + itemId, Sort = 20, Active = true,
        });
        await db.SaveChangesAsync();
    }

    // ── đọc ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ma_chua_co_spec_van_tra_200_chu_khong_404()
    {
        // "Chưa có tiêu chuẩn riêng" là một CÂU TRẢ LỜI, không phải lỗi — 404 ở
        // đây làm UI hiện màn lỗi cho 590 mã hoàn toàn hợp lệ.
        await SeedLibraryItemAsync("NQ-01");
        var qc = await ClientAsync("qc-p12b-read", UserRole.Qc);

        var resp = await qc.GetAsync("/api/v2/iqc/specs/MA-CHUA-CO-SPEC-XYZ");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var v = await resp.Content.ReadFromJsonAsync<IqcSpecEditResponse>();
        Assert.Null(v!.SpecNo);
        Assert.NotEmpty(v.Library);        // vẫn đủ hạng mục để chọn thêm
    }

    [Fact]
    public async Task Operator_KHONG_doc_duoc_tieu_chuan()
    {
        var op = await ClientAsync("op-p12b-read", UserRole.Operator);
        var resp = await op.GetAsync("/api/v2/iqc/specs/336-H1a");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Chua_dang_nhap_thi_401()
    {
        var anon = _fx.CreateClient();
        var resp = await anon.GetAsync("/api/v2/iqc/specs/336-H1a");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── ghi: policy Engineer+ ────────────────────────────────────────────

    [Fact]
    public async Task Engineer_them_duoc_hang_muc_va_server_tu_tao_spec_cuc_bo()
    {
        await SeedLibraryItemAsync("NQ-01");
        var eng = await ClientAsync("eng-p12b-add", UserRole.Engineer);

        var resp = await eng.SendAsync(Mk(HttpMethod.Post,
            "/api/v2/iqc/specs/MA-P12B-ADD/items",
            new { itemId = "NQ-01", acceptanceVi = "tem rõ chữ", methodVi = "soi mắt" }));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var spec = await db.IqcMaterialSpecs.SingleAsync(x => x.MaterialCode == "MA-P12B-ADD");
        Assert.StartsWith("MES-SPEC-", spec.SpecNo);
        Assert.True(await db.IqcSpecItems.AnyAsync(x => x.SpecNo == spec.SpecNo && x.ItemId == "NQ-01"));
    }

    [Theory]
    [InlineData(UserRole.Qc)]
    [InlineData(UserRole.Operator)]
    public async Task Vai_khong_du_quyen_thi_403_khi_THEM(string role)
    {
        await SeedLibraryItemAsync("NQ-01");
        var c = await ClientAsync($"u-p12b-add-{role.ToLowerInvariant()}", role);

        var resp = await c.SendAsync(Mk(HttpMethod.Post,
            "/api/v2/iqc/specs/MA-P12B-403/items", new { itemId = "NQ-01" }));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task QC_KHONG_go_duoc_hang_muc()
    {
        // Người kiểm tự hạ chuẩn cho lô mình đang cầm là đúng thứ phải chặn.
        await SeedLibraryItemAsync("NQ-01");
        var eng = await ClientAsync("eng-p12b-del", UserRole.Engineer);
        await eng.SendAsync(Mk(HttpMethod.Post,
            "/api/v2/iqc/specs/MA-P12B-DEL/items", new { itemId = "NQ-01" }));

        long itemId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var spec = await db.IqcMaterialSpecs.SingleAsync(x => x.MaterialCode == "MA-P12B-DEL");
            itemId = await db.IqcSpecItems.Where(x => x.SpecNo == spec.SpecNo)
                .Select(x => x.Id).FirstAsync();
        }

        var qc = await ClientAsync("qc-p12b-del", UserRole.Qc);
        var resp = await qc.SendAsync(Mk(HttpMethod.Delete, $"/api/v2/iqc/specs/items/{itemId}"));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        using var s2 = _fx.Services.CreateScope();
        var db2 = s2.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.True(await db2.IqcSpecItems.Where(x => x.Id == itemId).Select(x => x.Active).FirstAsync());
    }

    [Fact]
    public async Task Xoa_la_xoa_MEM_dong_van_con_trong_DB()
    {
        await SeedLibraryItemAsync("NQ-01");
        var eng = await ClientAsync("eng-p12b-soft", UserRole.Engineer);
        await eng.SendAsync(Mk(HttpMethod.Post,
            "/api/v2/iqc/specs/MA-P12B-SOFT/items", new { itemId = "NQ-01" }));

        long itemId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var spec = await db.IqcMaterialSpecs.SingleAsync(x => x.MaterialCode == "MA-P12B-SOFT");
            itemId = await db.IqcSpecItems.Where(x => x.SpecNo == spec.SpecNo)
                .Select(x => x.Id).FirstAsync();
        }

        var del = await eng.SendAsync(Mk(HttpMethod.Delete, $"/api/v2/iqc/specs/items/{itemId}"));
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);

        using var s2 = _fx.Services.CreateScope();
        var db2 = s2.ServiceProvider.GetRequiredService<MesDbContext>();
        var row = await db2.IqcSpecItems.FirstAsync(x => x.Id == itemId);
        Assert.False(row.Active);          // tắt, KHÔNG biến mất

        // Bật lại được.
        var back = await eng.SendAsync(Mk(HttpMethod.Post, $"/api/v2/iqc/specs/items/{itemId}/restore"));
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
    }

    // ── hợp đồng header + validate ───────────────────────────────────────

    [Fact]
    public async Task Thieu_Idempotency_Key_thi_400()
    {
        await SeedLibraryItemAsync("NQ-01");
        var eng = await ClientAsync("eng-p12b-idem", UserRole.Engineer);

        var resp = await eng.SendAsync(Mk(HttpMethod.Post,
            "/api/v2/iqc/specs/MA-P12B-IDEM/items", new { itemId = "NQ-01" }, idem: false));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("wo.idempotency_key_required", err!.Code);
    }

    [Fact]
    public async Task Hang_muc_ngoai_thu_vien_thi_422_va_KHONG_de_lai_spec_mo_coi()
    {
        var eng = await ClientAsync("eng-p12b-lib", UserRole.Engineer);

        var resp = await eng.SendAsync(Mk(HttpMethod.Post,
            "/api/v2/iqc/specs/MA-P12B-ORPHAN/items", new { itemId = "TU-BIA-99" }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("iqc.item_not_in_library", err!.Code);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.False(await db.IqcMaterialSpecs.AnyAsync(x => x.MaterialCode == "MA-P12B-ORPHAN"));
    }

    [Fact]
    public async Task Go_hang_muc_khong_ton_tai_thi_404()
    {
        var eng = await ClientAsync("eng-p12b-404", UserRole.Engineer);
        var resp = await eng.SendAsync(Mk(HttpMethod.Delete, "/api/v2/iqc/specs/items/999999"));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("iqc.spec_item_not_found", err!.Code);
    }
}
