using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

public class WiService
{
    private readonly IMesDbContext _db;
    public WiService(IMesDbContext db) => _db = db;

    public Task<List<WorkInstruction>> GetAllAsync() =>
        _db.WorkInstructions
            .Include(w => w.Product)
            .Include(w => w.Steps)
            .OrderBy(w => w.ProductId).ThenBy(w => w.ProcessStep)
            .ToListAsync();

    public Task<WorkInstruction?> GetForAsync(long productId, ProcessStepCode step) =>
        _db.WorkInstructions
            .Include(w => w.Steps)
            .Where(w => w.ProductId == productId && w.ProcessStep == step && w.Status == WiStatus.Approved)
            .OrderByDescending(w => w.VersionNo)
            .FirstOrDefaultAsync();

    public async Task<WorkInstruction> CreateAsync(CreateWiRequest r)
    {
        var wi = new WorkInstruction
        {
            Title = r.Title, ProductId = r.ProductId, ProcessStep = r.ProcessStep,
            MachineCode = r.MachineCode, Status = WiStatus.Draft, VersionNo = 1
        };
        int seq = 1;
        foreach (var s in r.Steps)
            wi.Steps.Add(new WiStepDetail { Sequence = seq++, Description = s.Description, ImageUrl = s.ImageUrl, WarningNote = s.WarningNote });
        _db.WorkInstructions.Add(wi);
        await _db.SaveChangesAsync();
        return wi;
    }

    public async Task<WorkInstruction?> ApproveAsync(long id)
    {
        var wi = await _db.WorkInstructions.FirstOrDefaultAsync(w => w.Id == id);
        if (wi is null) return null;
        wi.Status = WiStatus.Approved;
        wi.EffectiveDate ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return wi;
    }
}
