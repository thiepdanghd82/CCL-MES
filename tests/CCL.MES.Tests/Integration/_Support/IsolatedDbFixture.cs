using CCL.MES.Application;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Tests.Integration._Support;

/// <summary>
/// Phase 9 T2a — isolated SQLite test fixture per Henry's hard constraint
/// "isolated /tmp SQLite (KHÔNG đụng live ccl_mes.db — bài học A→B→C)".
///
/// <para>
/// Each fixture instance allocates a fresh <c>/tmp/ccl-mes-test-&lt;guid&gt;/test.db</c>
/// + applies <c>Database.MigrateAsync()</c> against the live prod migration
/// chain. Same provider as prod (Sqlite — not EF InMemory) so every test
/// catches relational quirks (cascade vs RESTRICT, JSON-as-text vs nvarchar,
/// SQLite vs SqlServer collation drift).
/// </para>
///
/// <para>
/// <b>Usage</b>:
/// <code>
/// public sealed class FooTests : IClassFixture&lt;IsolatedDbFixture&gt;
/// {
///     private readonly IsolatedDbFixture _fx;
///     public FooTests(IsolatedDbFixture fx) { _fx = fx; }
///
///     [Fact]
///     public async Task Bar()
///     {
///         using var db = _fx.NewContext();
///         // ... test against db
///     }
/// }
/// </code>
/// One <see cref="IsolatedDbFixture"/> = one SQLite file shared by all
/// methods in a class. Use <see cref="NewContext"/> per test method so
/// EF ChangeTracker pollution doesn't bleed across tests. If you need a
/// completely fresh DB per method, build a new <see cref="IsolatedDbFixture"/>
/// in the test ctor (xUnit gives a fresh class instance per <c>[Fact]</c>
/// when class implements <see cref="IDisposable"/> directly).
/// </para>
///
/// <para>
/// <b>Seed</b>: bare minimum (1 customer + 1 product + 1 draft revision)
/// so a typical "exists a valid spec to operate on" test has a row to
/// touch without fighting FKs. Tests needing additional fixtures use
/// <see cref="SeedRevisionAsync"/> / <see cref="SeedWorkOrderAsync"/>
/// helpers.
/// </para>
/// </summary>
public sealed class IsolatedDbFixture : IDisposable
{
    public string TmpRoot { get; }
    public string DbPath  { get; }
    public DbContextOptions<MesDbContext> Options { get; }

    /// <summary>Seeded ids (deterministic per fixture instance).</summary>
    public long SeedCustomerId { get; private set; }
    public long SeedProductId  { get; private set; }
    public long SeedRevisionId { get; private set; }

    public IsolatedDbFixture()
    {
        TmpRoot = Path.Combine(Path.GetTempPath(), $"ccl-mes-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(TmpRoot);
        DbPath = Path.Combine(TmpRoot, "test.db");

        Options = new DbContextOptionsBuilder<MesDbContext>()
            .UseSqlite($"Data Source={DbPath}")
            .Options;

        // Migrate + seed inline so consumers can hit the DB without further setup.
        using var db = new MesDbContext(Options);
        db.Database.Migrate();
        SeedMinimal(db);
    }

    /// <summary>
    /// Build a fresh DbContext bound to the fixture DB. Always wrap in
    /// <c>using</c> so EF disposes the connection pool entry — SQLite
    /// keeps a write-lock per open connection.
    /// </summary>
    public MesDbContext NewContext() => new(Options);

    /// <summary>Type-erased context for code that depends on the abstraction.</summary>
    public IMesDbContext NewDb() => NewContext();

    private void SeedMinimal(MesDbContext db)
    {
        var customer = new Customer { Code = "TST", Name = "Test Co" };
        var product  = new Product  { ProductCode = "TST-001", Name = "Test Product", Customer = customer };
        customer.Products.Add(product);
        db.Customers.Add(customer);
        db.SaveChanges();

        var rev = new ProductRevision
        {
            ProductId    = product.Id,
            SpecCode     = "SPEC-TST-001",
            Title        = "Baseline test spec",
            RevisionCode = "A",
            Status       = ProductRevisionStatus.Draft,
        };
        db.ProductRevisions.Add(rev);
        db.SaveChanges();

        SeedCustomerId = customer.Id;
        SeedProductId  = product.Id;
        SeedRevisionId = rev.Id;
    }

    /// <summary>
    /// Append another revision under the seeded product. Caller picks
    /// the rev code so callers can exercise NextAvailableRev collision
    /// scenarios. Returns the new revision id.
    /// </summary>
    public async Task<long> SeedRevisionAsync(
        string specCode,
        string revisionCode,
        ProductRevisionStatus status = ProductRevisionStatus.Draft,
        bool isTrashed = false,
        DateTime? trashedAt = null,
        long? productId = null)
    {
        using var db = NewContext();
        var rev = new ProductRevision
        {
            ProductId    = productId ?? SeedProductId,
            SpecCode     = specCode,
            Title        = $"Seeded {specCode} {revisionCode}",
            RevisionCode = revisionCode,
            Status       = status,
            IsTrashed    = isTrashed,
            TrashedAt    = trashedAt,
        };
        db.ProductRevisions.Add(rev);
        await db.SaveChangesAsync();
        return rev.Id;
    }

    /// <summary>
    /// Seed a WorkOrder bound to a revision so tests can probe the
    /// active-WO blocker on Trash + Purge paths.
    /// </summary>
    public async Task<long> SeedWorkOrderAsync(
        long revisionId,
        WoStatus status = WoStatus.InProgress,
        string woNo = "WO-TEST-001")
    {
        using var db = NewContext();
        var rev = await db.ProductRevisions.AsNoTracking().FirstAsync(r => r.Id == revisionId);
        var prod = await db.Products.AsNoTracking().FirstAsync(p => p.Id == rev.ProductId);
        var wo = new WorkOrder
        {
            WoNo              = woNo,
            CustomerId        = prod.CustomerId,
            ProductId         = prod.Id,
            ProductName       = prod.Name,
            ProductRevisionId = revisionId,
            Status            = status,
            CurrentStep       = ProcessStepCode.PrePressCheck,
            TargetQty         = 100,
            Uom               = "pcs",
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        return wo.Id;
    }

    public void Dispose()
    {
        // Tear down the fixture root. SQLite holds an exclusive lock so
        // try a small retry-with-GC before giving up — best-effort either
        // way since /tmp is OS-cleaned periodically.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(TmpRoot)) Directory.Delete(TmpRoot, recursive: true);
                return;
            }
            catch (IOException)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            catch { return; }
        }
    }
}
