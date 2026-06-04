using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Specs;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.5c-3 — Planner chip filter on <c>GET /api/v1/specs</c>.
/// Seeds 5 revisions (one per planner code) and asserts the
/// <c>?planner=</c> query param scopes the returned list to that
/// planner only.
/// </summary>
public sealed class SpecListPlannerFilterTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public SpecListPlannerFilterTests(MesApiFactory fx) => _fx = fx;

    private async Task SeedFiveAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var customer = new Customer { Code = "CUST-PLN", Name = "Planner Test Customer" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var specs = new[]
        {
            ("PRD-SILK",   "SILKSCREEN"),
            ("PRD-FLEXO",  "FLEXO"),
            ("PRD-LETTER", "LETTERPRESS"),
            ("PRD-INDIGO", "INDIGO"),
            ("PRD-DIE",    "FLATBED_CUT"),
        };
        foreach (var (code, processCode) in specs)
        {
            var product = new Product { ProductCode = code, Name = code, CustomerId = customer.Id };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            var rev = new ProductRevision
            {
                ProductId = product.Id,
                SpecCode = code,
                Title = $"Title for {code}",
                RevisionCode = "A",
                Status = ProductRevisionStatus.Draft,
                Print = new SpecPrint { ProcessCode = processCode, NumColors = 1 },
            };
            db.ProductRevisions.Add(rev);
            await db.SaveChangesAsync();
        }
    }

    private async Task<HttpClient> EngineerClientAsync(string username = "eng-plnflt")
    {
        await _fx.SeedUserAsync(username, "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, username, "P@ss!");
        return client;
    }

    [Theory]
    [InlineData("SILK",   "SILKSCREEN")]
    [InlineData("FLEXO",  "FLEXO")]
    [InlineData("LETTER", "LETTERPRESS")]
    [InlineData("INDIGO", "INDIGO")]
    [InlineData("DIECUT", "FLATBED_CUT")]
    public async Task Planner_filter_returns_only_matching_process(string plannerCode, string expectedProcessCode)
    {
        await SeedFiveAsync();
        var client = await EngineerClientAsync($"eng-plnflt-{plannerCode}");

        var resp = await client.GetAsync($"/api/v2/specs?planner={plannerCode}&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<PagedResponse>();
        Assert.NotNull(body);

        // All returned items in the page must have the expected planner-
        // mapped ProcessCode. Sibling tests may have polluted the DB
        // with other planners — those should be filtered out cleanly.
        Assert.NotEmpty(body!.Items);
        Assert.All(body.Items, item => Assert.Equal(expectedProcessCode, item.ProcessCode));
    }

    [Fact]
    public async Task No_planner_param_returns_full_list_unfiltered_by_planner()
    {
        await SeedFiveAsync();
        var client = await EngineerClientAsync("eng-plnflt-none");

        // Total is the unfiltered count; cap the page so we can scan the
        // process-code set without paginating manually.
        var resp = await client.GetAsync("/api/v2/specs?pageSize=500");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<PagedResponse>();
        Assert.NotNull(body);
        // Should see at least 5 distinct planner-mapped process codes in
        // a single page (matches the seed shape).
        var distinctProc = body!.Items.Select(i => i.ProcessCode).Where(p => p is not null).Distinct().ToList();
        Assert.True(distinctProc.Count >= 5,
            $"Expected ≥5 distinct ProcessCode values, got {distinctProc.Count}");
    }

    [Fact]
    public async Task Unknown_planner_code_falls_back_to_no_filter()
    {
        await SeedFiveAsync();
        var client = await EngineerClientAsync("eng-plnflt-unknown");

        var resp = await client.GetAsync("/api/v2/specs?planner=WHATEVER_NEW&pageSize=500");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<PagedResponse>();
        Assert.NotNull(body);
        // Unknown planner is silently ignored — page returns the
        // unfiltered list (same as no planner param at all).
        var distinctProc = body!.Items.Select(i => i.ProcessCode).Where(p => p is not null).Distinct().ToList();
        Assert.True(distinctProc.Count >= 5);
    }

    [Fact]
    public async Task Planner_filter_combines_with_search()
    {
        await SeedFiveAsync();
        var client = await EngineerClientAsync("eng-plnflt-search");

        // Search by the customer name shared across the seed — combine
        // with a specific planner. Result should be 1 row.
        var resp = await client.GetAsync(
            "/api/v2/specs?planner=FLEXO&search=Planner+Test+Customer&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<PagedResponse>();
        Assert.NotNull(body);
        Assert.All(body!.Items, item => Assert.Equal("FLEXO", item.ProcessCode));
    }

    private sealed record PagedResponse
    {
        public List<Item> Items { get; init; } = new();
        public int Total { get; init; }
        public int Page { get; init; }
        public int PageSize { get; init; }
    }

    private sealed record Item
    {
        public long Id { get; init; }
        public string SpecCode { get; init; } = "";
        public string? ProcessCode { get; init; }
        public string Title { get; init; } = "";
    }
}
