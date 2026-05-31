using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application;

/// <summary>Trừu tượng hóa DbContext để tầng Application không phụ thuộc Infrastructure.</summary>
public interface IMesDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<Product> Products { get; }
    DbSet<Spec> Specs { get; }
    DbSet<SpecVersion> SpecVersions { get; }
    DbSet<SpecParameter> SpecParameters { get; }
    DbSet<WorkOrder> WorkOrders { get; }
    DbSet<WoStatusHistory> WoStatusHistories { get; }
    DbSet<QcInspection> QcInspections { get; }
    DbSet<QcResultDetail> QcResultDetails { get; }
    DbSet<Machine> Machines { get; }
    DbSet<DowntimeReason> DowntimeReasons { get; }
    DbSet<ProductionLog> ProductionLogs { get; }
    DbSet<WorkInstruction> WorkInstructions { get; }
    DbSet<WiStepDetail> WiStepDetails { get; }
    DbSet<WorkCenter> WorkCenters { get; }
    DbSet<RawMaterial> RawMaterials { get; }
    DbSet<RoutingOperation> RoutingOperations { get; }
    DbSet<ManufacturingStructure> ManufacturingStructures { get; }
    DbSet<User> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
