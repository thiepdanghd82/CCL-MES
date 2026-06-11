using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Specs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.5c-1 — 8 Spec mutation endpoints. Coverage:
///   · success path (Create → 201, Approve / Update / Supersede /
///     Trash / Restore → 200, Copy / Revise → 201)
///   · auth gate (no token = 401)
///   · role gate (Supervisor / QC = 403 — NpiSpecWrite is Admin/Engineer)
///   · key validation paths (duplicate_spec_code, reason_required,
///     confirm_mismatch, immutable_status, not_trashed, not_found,
///     active_work_orders)
///   · device-pairing audit row when X-Device-Id header is on the
///     request (W4 pattern carried into Spec mutation surface)
/// </summary>
public sealed class SpecMutationsTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public SpecMutationsTests(MesApiFactory fx) => _fx = fx;

    private async Task<long> SeedProductAsync(string code = "PRD-MUT")
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var customer = new Customer { Code = "CUST-" + code, Name = "Customer " + code };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var product = new Product { ProductCode = code, Name = "Product " + code, CustomerId = customer.Id };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private async Task<HttpClient> EngineerClientAsync(string username = "eng-mut")
    {
        await _fx.SeedUserAsync(username, "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, username, "P@ss!");
        return client;
    }

    private async Task<HttpClient> SupervisorClientAsync(string username = "sup-mut")
    {
        await _fx.SeedUserAsync(username, "P@ss!", UserRole.Supervisor);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, username, "P@ss!");
        return client;
    }

    private async Task<SpecMutationResponse> CreateSpecAsync(HttpClient client, long productId, string code)
    {
        var resp = await client.PostAsJsonAsync("/api/v2/specs", new CreateSpecMutation
        {
            ProductId = productId,
            SpecCode = code,
            Title = "Created " + code,
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<SpecMutationResponse>())!;
    }

    [Fact]
    public async Task Anonymous_returns_401_on_create()
    {
        var anon = _fx.CreateClient();
        var resp = await anon.PostAsJsonAsync("/api/v2/specs", new CreateSpecMutation
        {
            ProductId = 1, SpecCode = "X", Title = "X",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Supervisor_returns_403_on_create()
    {
        var client = await SupervisorClientAsync();
        var pid = await SeedProductAsync("PRD-SUP");
        var resp = await client.PostAsJsonAsync("/api/v2/specs", new CreateSpecMutation
        {
            ProductId = pid, SpecCode = "SUP-001", Title = "Should fail",
        });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Engineer_create_returns_201_and_emits_device_audit_row()
    {
        var client = await EngineerClientAsync("eng-create");
        var pid = await SeedProductAsync("PRD-CR");
        client.DefaultRequestHeaders.Add("X-Device-Id", "0193a1d9-cccc-dddd-eeee-ffff00001111");

        var resp = await client.PostAsJsonAsync("/api/v2/specs", new CreateSpecMutation
        {
            ProductId = pid, SpecCode = "CR-001", Title = "Created spec",
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SpecMutationResponse>();
        Assert.NotNull(body);
        Assert.True(body!.Id > 0);
        Assert.Equal("CR-001", body.SpecCode);
        Assert.Equal("A", body.Revision);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var deviceAudit = db.AuditLogs
            .Where(a => a.Action == "SPEC_CREATE_DEVICE" && a.TargetId == "0193a1d9-cccc-dddd-eeee-ffff00001111")
            .ToList();
        Assert.NotEmpty(deviceAudit);
        Assert.Contains(deviceAudit, a => a.Detail != null && a.Detail.Contains("CR-001"));
    }

    [Fact]
    public async Task Create_second_same_product_RevisionCode_A_violates_unique_constraint()
    {
        // The legacy schema enforces (ProductId, RevisionCode) UNIQUE;
        // since fresh Create defaults RevisionCode='A', a second Create
        // on the same product hits the DB unique constraint. CreateAsync
        // doesn't catch DbUpdateException so the controller surfaces it
        // as a 500. This is parity with the legacy CCL.MES.Web SpecsController
        // — explicit duplicate-detection is only in Copy/Revise paths.
        // The test pins this behaviour so a future controller-side guard
        // (catch DbUpdateException → 422 duplicate) shows up as a
        // failed test rather than a silent contract change.
        var client = await EngineerClientAsync("eng-dup");
        var pid = await SeedProductAsync("PRD-DUP");

        await CreateSpecAsync(client, pid, "DUP-001");

        var dup = await client.PostAsJsonAsync("/api/v2/specs", new CreateSpecMutation
        {
            ProductId = pid, SpecCode = "DUP-002", Title = "Second spec same product",
        });
        Assert.Equal(HttpStatusCode.InternalServerError, dup.StatusCode);
    }

    [Fact]
    public async Task Approve_then_returns_200_with_status_Approved()
    {
        var client = await EngineerClientAsync("eng-app");
        var pid = await SeedProductAsync("PRD-AP");
        var created = await CreateSpecAsync(client, pid, "AP-001");

        var approveResp = await client.PostAsync($"/api/v2/specs/revisions/{created.Id}/approve", content: null);
        Assert.Equal(HttpStatusCode.OK, approveResp.StatusCode);
        var body = await approveResp.Content.ReadFromJsonAsync<SpecMutationResponse>();
        Assert.Equal("Approved", body!.Status);
    }

    [Fact]
    public async Task Approve_unknown_id_returns_404()
    {
        var client = await EngineerClientAsync("eng-app404");
        var resp = await client.PostAsync("/api/v2/specs/revisions/999999/approve", content: null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Copy_creates_new_draft_with_unique_spec_code()
    {
        var client = await EngineerClientAsync("eng-cp");
        var pid = await SeedProductAsync("PRD-CP");
        var created = await CreateSpecAsync(client, pid, "CP-001");

        var copyResp = await client.PostAsJsonAsync($"/api/v2/specs/{created.Id}/copy", new CopySpecMutation
        {
            ProductId = pid,
            SpecCode = "CP-001-COPY",
            Title = "Copied spec",
        });
        Assert.Equal(HttpStatusCode.Created, copyResp.StatusCode);
        var body = await copyResp.Content.ReadFromJsonAsync<SpecMutationResponse>();
        Assert.NotEqual(created.Id, body!.Id);
        Assert.Equal("CP-001-COPY", body.SpecCode);
    }

    [Fact]
    public async Task Copy_duplicate_returns_422_duplicate_spec_code()
    {
        // Two distinct products so each Create succeeds (unique constraint
        // is on (ProductId, RevisionCode)). The Copy then targets product B
        // with the spec code that's already taken on product B (after the
        // first source spec on B is created).
        var client = await EngineerClientAsync("eng-cp-dup");
        var pidA = await SeedProductAsync("PRD-CP2-A");
        var pidB = await SeedProductAsync("PRD-CP2-B");
        var source = await CreateSpecAsync(client, pidA, "CPD-SRC");
        var existing = await CreateSpecAsync(client, pidB, "CPD-EXISTS");

        var resp = await client.PostAsJsonAsync($"/api/v2/specs/{source.Id}/copy", new CopySpecMutation
        {
            ProductId = pidB,
            SpecCode = "CPD-EXISTS",  // already taken on product B
            Title = "Will fail",
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<SpecMutationError>();
        Assert.Equal("duplicate_spec_code", err!.Code);
    }

    [Fact]
    public async Task Update_on_draft_returns_200()
    {
        var client = await EngineerClientAsync("eng-upd");
        var pid = await SeedProductAsync("PRD-UPD");
        var created = await CreateSpecAsync(client, pid, "UPD-001");

        var resp = await client.PutAsJsonAsync($"/api/v2/specs/{created.Id}", new UpdateSpecMutation
        {
            Title = "Updated title",
            InspectionLevel = "A166",
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SpecMutationResponse>();
        Assert.Equal("Updated title", body!.Title);
    }

    [Fact]
    public async Task Update_inline_print_params_material_and_colors_persist()
    {
        // P10.10 — inline-edit write path: material scalars + print params +
        // structured colour-row replacement all persist on a Draft spec.
        var client = await EngineerClientAsync("eng-inline");
        var pid = await SeedProductAsync("PRD-INL");
        var created = await CreateSpecAsync(client, pid, "INL-001");

        // Seed a Material row so the material scalars are patchable.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            db.Add(new SpecMaterial { ProductRevisionId = created.Id, SubstrateType = "OLD" });
            await db.SaveChangesAsync();
        }

        var resp = await client.PutAsJsonAsync($"/api/v2/specs/{created.Id}", new UpdateSpecMutation
        {
            SubstrateType = "PET SH01",
            AdhesiveType = "PERMANENT",
            ThicknessUm = 25,
            PrintingCavity = 3,
            LengthPitchMm = 545.0,
            ProductSizeWmm = 526.2,
            ProductSizeHmm = 104.0,
            RemarksText = "Inline edited remark",
            Colors = new[]
            {
                new SpecColorRowMutation { Color = "RED 186 C", InkCode = "HI178", PlateCode = "SP1263-1" },
                new SpecColorRowMutation { Color = "DENSE BLACK", InkCode = "VI20", PlateCode = "SP1263-5" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var rev = await db.ProductRevisions
                .Include(x => x.Material)
                .Include(x => x.Print).ThenInclude(p => p!.Colors)
                .FirstAsync(x => x.Id == created.Id);

            Assert.Equal("PET SH01", rev.Material!.SubstrateType);
            Assert.Equal("PERMANENT", rev.Material.AdhesiveType);
            Assert.Equal(25, rev.Material.ThicknessUm);
            Assert.Equal(3, rev.Print!.Cavity);
            Assert.Equal(545.0, rev.Print.PitchMm);
            Assert.Equal(526.2, rev.Print.ProductSizeWmm);
            Assert.Equal(104.0, rev.Print.ProductSizeHmm);
            Assert.Equal("Inline edited remark", rev.Print.RemarksText);
            Assert.Equal(2, rev.Print.Colors.Count);
            Assert.Equal(2, rev.Print.NumColors);
            var ordered = rev.Print.Colors.OrderBy(c => c.Seq).ToList();
            Assert.Equal(1, ordered[0].Seq);
            Assert.Equal("RED 186 C", ordered[0].Color);
            Assert.Equal("HI178", ordered[0].InkCode);
            Assert.Equal("DENSE BLACK", ordered[1].Color);
        }
    }

    [Fact]
    public async Task Update_on_approved_returns_422_immutable_status()
    {
        var client = await EngineerClientAsync("eng-imm");
        var pid = await SeedProductAsync("PRD-IMM");
        var created = await CreateSpecAsync(client, pid, "IMM-001");
        // Approve to push past Draft
        await client.PostAsync($"/api/v2/specs/revisions/{created.Id}/approve", content: null);

        var resp = await client.PutAsJsonAsync($"/api/v2/specs/{created.Id}", new UpdateSpecMutation
        {
            Title = "Should fail",
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<SpecMutationError>();
        Assert.Equal("immutable_status", err!.Code);
        Assert.Equal("Approved", err.CurrentStatus);
    }

    [Fact]
    public async Task Revise_requires_reason_min_5_chars()
    {
        var client = await EngineerClientAsync("eng-rev-r");
        var pid = await SeedProductAsync("PRD-RV-R");
        var created = await CreateSpecAsync(client, pid, "RVR-001");
        await client.PostAsync($"/api/v2/specs/revisions/{created.Id}/approve", content: null);

        var resp = await client.PostAsJsonAsync($"/api/v2/specs/{created.Id}/revise", new ReviseSpecMutation
        {
            Reason = "x",  // too short
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<SpecMutationError>();
        Assert.Equal("reason_required", err!.Code);
    }

    [Fact]
    public async Task Revise_on_approved_creates_new_draft()
    {
        var client = await EngineerClientAsync("eng-rev");
        var pid = await SeedProductAsync("PRD-RV");
        var created = await CreateSpecAsync(client, pid, "RV-001");
        await client.PostAsync($"/api/v2/specs/revisions/{created.Id}/approve", content: null);

        var resp = await client.PostAsJsonAsync($"/api/v2/specs/{created.Id}/revise", new ReviseSpecMutation
        {
            Reason = "Customer requested new pitch dimension",
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SpecMutationResponse>();
        Assert.NotEqual(created.Id, body!.Id);
        Assert.Equal(created.Id, body.ParentId);
    }

    [Fact]
    public async Task Supersede_confirm_mismatch_returns_422()
    {
        var client = await EngineerClientAsync("eng-sup-m");
        var pid = await SeedProductAsync("PRD-SUM");
        var created = await CreateSpecAsync(client, pid, "SUM-001");
        await client.PostAsync($"/api/v2/specs/revisions/{created.Id}/approve", content: null);

        var resp = await client.PostAsJsonAsync($"/api/v2/specs/{created.Id}/supersede", new SupersedeSpecMutation
        {
            ConfirmSpecCode = "WRONG-CODE",
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<SpecMutationError>();
        Assert.Equal("confirm_mismatch", err!.Code);
    }

    [Fact]
    public async Task Supersede_with_correct_code_flips_to_Superseded()
    {
        var client = await EngineerClientAsync("eng-sup-ok");
        var pid = await SeedProductAsync("PRD-SUO");
        var created = await CreateSpecAsync(client, pid, "SUO-001");
        await client.PostAsync($"/api/v2/specs/revisions/{created.Id}/approve", content: null);

        var resp = await client.PostAsJsonAsync($"/api/v2/specs/{created.Id}/supersede", new SupersedeSpecMutation
        {
            ConfirmSpecCode = "SUO-001",
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SpecMutationResponse>();
        Assert.Equal("Superseded", body!.Status);
    }

    [Fact]
    public async Task Trash_then_Restore_round_trips()
    {
        var client = await EngineerClientAsync("eng-tr");
        var pid = await SeedProductAsync("PRD-TR");
        var created = await CreateSpecAsync(client, pid, "TR-001");

        var trashResp = await client.PostAsync($"/api/v2/specs/{created.Id}/trash", content: null);
        Assert.Equal(HttpStatusCode.OK, trashResp.StatusCode);
        var trashed = await trashResp.Content.ReadFromJsonAsync<SpecMutationResponse>();
        Assert.True(trashed!.IsTrashed);

        var restoreResp = await client.PostAsync($"/api/v2/specs/{created.Id}/restore", content: null);
        Assert.Equal(HttpStatusCode.OK, restoreResp.StatusCode);
        var restored = await restoreResp.Content.ReadFromJsonAsync<SpecMutationResponse>();
        Assert.False(restored!.IsTrashed);
    }

    [Fact]
    public async Task Restore_on_non_trashed_returns_422_not_trashed()
    {
        var client = await EngineerClientAsync("eng-rs-nt");
        var pid = await SeedProductAsync("PRD-RNT");
        var created = await CreateSpecAsync(client, pid, "RNT-001");

        var resp = await client.PostAsync($"/api/v2/specs/{created.Id}/restore", content: null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<SpecMutationError>();
        Assert.Equal("not_trashed", err!.Code);
    }

    [Fact]
    public async Task Trash_already_trashed_returns_422_already_trashed()
    {
        var client = await EngineerClientAsync("eng-tr-at");
        var pid = await SeedProductAsync("PRD-TAT");
        var created = await CreateSpecAsync(client, pid, "TAT-001");
        await client.PostAsync($"/api/v2/specs/{created.Id}/trash", content: null);

        var resp = await client.PostAsync($"/api/v2/specs/{created.Id}/trash", content: null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<SpecMutationError>();
        Assert.Equal("already_trashed", err!.Code);
    }

    [Fact]
    public async Task Trash_with_active_work_order_returns_422_active_work_orders()
    {
        var client = await EngineerClientAsync("eng-tr-wo");
        var pid = await SeedProductAsync("PRD-TWO");
        var created = await CreateSpecAsync(client, pid, "TWO-001");

        // Plant an active WO referencing this product revision.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var product = await db.Products.FirstAsync(p => p.Id == pid);
            db.WorkOrders.Add(new WorkOrder
            {
                WoNo = "WO-TR-BLOCK",
                CustomerId = product.CustomerId,
                ProductId = product.Id,
                ProductName = product.Name,
                ProductRevisionId = created.Id,
                TargetQty = 100,
                Uom = "pcs",
                Status = WoStatus.InProgress,
                CurrentStep = ProcessStepCode.Running,
            });
            await db.SaveChangesAsync();
        }

        var resp = await client.PostAsync($"/api/v2/specs/{created.Id}/trash", content: null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<SpecMutationError>();
        Assert.Equal("active_work_orders", err!.Code);
        Assert.True((err.ActiveWoCount ?? 0) >= 1);
    }
}
