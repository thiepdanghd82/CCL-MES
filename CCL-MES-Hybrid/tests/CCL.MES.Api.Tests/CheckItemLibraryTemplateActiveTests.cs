using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.CheckLibrary;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Hai năng lực viết lại trên nền v5 sau khi PR #127 (mô hình cũ) bị đóng:
///
///   GET  /check-item-library/template        file MẪU nhập liệu
///   PATCH /check-item-library/{itemId}/active bật/tắt KHÔNG xoá
///
/// <para><b>Vì sao "ngưng dùng" quan trọng hơn "xoá" với master data:</b> WO cũ và
/// snapshot QC đã đóng băng còn tham chiếu tới hạng mục kiểm. Xoá nó đi là làm hồ
/// sơ chất lượng cũ trỏ vào khoảng không — đúng thứ khách hàng sẽ hỏi khi audit.</para>
/// </summary>
public sealed class CheckItemLibraryTemplateActiveTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public CheckItemLibraryTemplateActiveTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> ClientAsync(string user, string role = UserRole.Qc)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", role);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private async Task<string> SeedItemAsync(string id, bool active = true)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        if (!await db.CheckItemLibraries.AnyAsync(c => c.ItemId == id))
        {
            db.CheckItemLibraries.Add(new CheckItemLibrary
            {
                ItemId = id, ProcessLine = "Label", GroupLabel = "A", Code = id,
                ItemVi = "mục " + id, ItemEn = "item " + id,
                Ipqc = true, Active = active, CreatedBy = "seed",
            });
            await db.SaveChangesAsync();
        }
        return id;
    }

    // ── TEMPLATE ────────────────────────────────────────────────────────

    [Fact]
    public async Task Template_header_is_identical_to_export_header()
    {
        // Bất biến quan trọng nhất: mẫu và bản xuất phải CÙNG thứ tự cột, nếu không
        // người dùng điền theo mẫu rồi import sẽ lệch cột mà không có lỗi rõ ràng.
        var client = await ClientAsync("lib-tpl-hdr");
        await SeedItemAsync("TPL-1");

        var tpl = await (await client.GetAsync("/api/v2/check-item-library/template")).Content.ReadAsStringAsync();
        var exp = await (await client.GetAsync("/api/v2/check-item-library/export")).Content.ReadAsStringAsync();

        static string FirstLine(string s) => s.TrimStart('﻿').Split('\n')[0].TrimEnd('\r');
        Assert.Equal(FirstLine(exp), FirstLine(tpl));
        Assert.StartsWith("ItemID,Line,", FirstLine(tpl));
    }

    [Fact]
    public async Task Template_carries_one_example_row_showing_the_tick_convention()
    {
        // Quy ước ● / · không tự hiển nhiên — người điền lần đầu không đoán được
        // phải gõ ký tự gì vào 15 cột phương pháp. Một dòng mẫu rẻ hơn một trang HDSD.
        var client = await ClientAsync("lib-tpl-row");
        var body = await (await client.GetAsync("/api/v2/check-item-library/template")).Content.ReadAsStringAsync();

        var lines = body.TrimStart('﻿').Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);              // header + đúng 1 dòng ví dụ
        Assert.Contains("●", lines[1]);
        Assert.Contains("·", lines[1]);
    }

    [Fact]
    public async Task Template_contains_no_real_data()
    {
        // Mẫu KHÔNG được lộ dữ liệu thật — nó là file phát cho người nhập liệu.
        var client = await ClientAsync("lib-tpl-clean");
        await SeedItemAsync("SECRET-ITEM-1");

        var body = await (await client.GetAsync("/api/v2/check-item-library/template")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("SECRET-ITEM-1", body);
    }

    // ── ACTIVE ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Deactivate_keeps_the_row_and_flips_the_flag()
    {
        var client = await ClientAsync("lib-off", UserRole.Supervisor);
        var id = await SeedItemAsync("ACT-1");

        var res = await client.PatchAsync($"/api/v2/check-item-library/{id}/active?active=false", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<CheckLibraryItemDto>();
        Assert.False(dto!.Active);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var row = await db.CheckItemLibraries.FirstOrDefaultAsync(c => c.ItemId == id);
        Assert.NotNull(row);                 // ← KHÔNG bị xoá, đó là toàn bộ điểm
        Assert.False(row!.Active);
    }

    [Fact]
    public async Task Reactivate_restores_the_flag()
    {
        var client = await ClientAsync("lib-on", UserRole.Supervisor);
        var id = await SeedItemAsync("ACT-2", active: false);

        var res = await client.PatchAsync($"/api/v2/check-item-library/{id}/active?active=true", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True((await res.Content.ReadFromJsonAsync<CheckLibraryItemDto>())!.Active);
    }

    [Fact]
    public async Task Deactivate_emits_an_audit_row()
    {
        var client = await ClientAsync("lib-audit", UserRole.Supervisor);
        var id = await SeedItemAsync("ACT-3");

        await client.PatchAsync($"/api/v2/check-item-library/{id}/active?active=false", null);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var audit = await db.AuditLogs
            .Where(a => a.TargetType == "CheckItemLibrary" && a.TargetId == id)
            .OrderByDescending(a => a.Id).FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Contains("\"active\":false", audit!.Detail);
        Assert.Contains("\"via\":\"patch\"", audit.Detail);
    }

    [Fact]
    public async Task Setting_the_same_value_twice_writes_no_second_audit_row()
    {
        // Idempotent: bấm hai lần không được sinh hai dòng vết — nhiễu audit làm
        // người điều tra sự cố mất thời gian phân biệt thao tác thật.
        var client = await ClientAsync("lib-idem", UserRole.Supervisor);
        var id = await SeedItemAsync("ACT-4");

        await client.PatchAsync($"/api/v2/check-item-library/{id}/active?active=false", null);
        await client.PatchAsync($"/api/v2/check-item-library/{id}/active?active=false", null);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var n = await db.AuditLogs.CountAsync(a =>
            a.TargetType == "CheckItemLibrary" && a.TargetId == id && a.Detail!.Contains("\"via\":\"patch\""));
        Assert.Equal(1, n);
    }

    [Fact]
    public async Task Unknown_item_is_404_not_a_silent_no_op()
    {
        var client = await ClientAsync("lib-404", UserRole.Supervisor);
        var res = await client.PatchAsync("/api/v2/check-item-library/KHONG-TON-TAI/active?active=false", null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Operator_cannot_deactivate_master_data()
    {
        var client = await ClientAsync("lib-op", UserRole.Operator);
        var id = await SeedItemAsync("ACT-5");
        var res = await client.PatchAsync($"/api/v2/check-item-library/{id}/active?active=false", null);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
