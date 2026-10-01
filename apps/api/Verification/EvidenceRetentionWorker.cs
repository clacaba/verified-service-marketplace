using Marketplace.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Verification;

public sealed class EvidenceRetentionWorker(IServiceScopeFactory scopes, ILogger<EvidenceRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Sweep(stoppingToken); }
            catch (Exception) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning("Evidence retention sweep failed; retrying."); }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
    public async Task Sweep(CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var now = DateTime.UtcNow;
        var expired = await db.VerificationDocuments.Where(x => x.DeletedAt == null && x.ExpiresAt <= now).Take(100).ToListAsync(ct);
        foreach (var document in expired)
        {
            document.DeletedAt = DateTime.UtcNow;
            document.Version = Guid.NewGuid();
            db.EvidenceDeletions.Add(new EvidenceDeletion { ObjectKey = document.ObjectKey });
        }
        await db.SaveChangesAsync(ct);
        var dueAt = DateTime.UtcNow;
        var pending = await db.EvidenceDeletions.Where(x => x.DueAt <= dueAt).Take(100).ToListAsync(ct);
        var store = scope.ServiceProvider.GetRequiredService<IEvidenceStore>();
        foreach (var job in pending)
        {
            try { await store.Delete(job.ObjectKey, ct); db.EvidenceDeletions.Remove(job); }
            catch (Exception) when (!ct.IsCancellationRequested) { job.Attempts++; job.DueAt = DateTime.UtcNow.AddMinutes(Math.Min(60, 1 << Math.Min(job.Attempts, 5))); logger.LogWarning("Evidence deletion {JobId} will retry.", job.Id); }
            await db.SaveChangesAsync(ct);
        }
    }
}
