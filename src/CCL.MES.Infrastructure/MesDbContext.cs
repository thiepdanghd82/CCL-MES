using CCL.MES.Application;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Infrastructure;

public class MesDbContext : DbContext, IMesDbContext
{
    public MesDbContext(DbContextOptions<MesDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Spec> Specs => Set<Spec>();
    public DbSet<SpecVersion> SpecVersions => Set<SpecVersion>();
    public DbSet<SpecParameter> SpecParameters => Set<SpecParameter>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WoStatusHistory> WoStatusHistories => Set<WoStatusHistory>();
    public DbSet<QcInspection> QcInspections => Set<QcInspection>();
    public DbSet<QcResultDetail> QcResultDetails => Set<QcResultDetail>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<DowntimeReason> DowntimeReasons => Set<DowntimeReason>();
    public DbSet<ProductionLog> ProductionLogs => Set<ProductionLog>();
    public DbSet<WorkInstruction> WorkInstructions => Set<WorkInstruction>();
    public DbSet<WiStepDetail> WiStepDetails => Set<WiStepDetail>();
    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();
    public DbSet<RawMaterial> RawMaterials => Set<RawMaterial>();
    public DbSet<RoutingOperation> RoutingOperations => Set<RoutingOperation>();
    public DbSet<ManufacturingStructure> ManufacturingStructures => Set<ManufacturingStructure>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Lưu enum dạng chuỗi cho dễ đọc trong DB
        b.Entity<WorkOrder>().Property(x => x.CurrentStep).HasConversion<string>();
        b.Entity<WorkOrder>().Property(x => x.Status).HasConversion<string>();
        b.Entity<SpecVersion>().Property(x => x.Status).HasConversion<string>();
        b.Entity<QcInspection>().Property(x => x.Type).HasConversion<string>();
        b.Entity<QcInspection>().Property(x => x.Result).HasConversion<string>();
        b.Entity<WoStatusHistory>().Property(x => x.FromStep).HasConversion<string>();
        b.Entity<WoStatusHistory>().Property(x => x.ToStep).HasConversion<string>();
        b.Entity<Machine>().Property(x => x.CurrentState).HasConversion<string>();
        b.Entity<ProductionLog>().Property(x => x.EventType).HasConversion<string>();
        b.Entity<WorkInstruction>().Property(x => x.Status).HasConversion<string>();
        b.Entity<WorkInstruction>().Property(x => x.ProcessStep).HasConversion<string>();

        b.Entity<WorkOrder>().HasIndex(x => x.WoNo).IsUnique();
        b.Entity<Machine>().HasIndex(x => x.Code).IsUnique();

        // Index cho tra cứu nhanh các bảng NPI dữ liệu lớn
        b.Entity<WorkCenter>().HasIndex(x => x.Code);
        b.Entity<RawMaterial>().HasIndex(x => x.PartNo);
        b.Entity<RoutingOperation>().HasIndex(x => x.PartNo);
        b.Entity<ManufacturingStructure>().HasIndex(x => x.ParentPart);

        // Tính toán read-only -> không map vào DB
        b.Entity<WorkOrder>().Ignore("LastQc");
        b.Entity<ProductionLog>().Ignore(p => p.DurationMinutes);
    }
}
