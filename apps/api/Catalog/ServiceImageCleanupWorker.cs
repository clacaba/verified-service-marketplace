using Marketplace.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Catalog;

public sealed class ServiceImageCleanupWorker(IServiceScopeFactory scopes, ILogger<ServiceImageCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Sweep(stoppingToken); }
            catch (Exception) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning("Service image cleanup will retry."); }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
    public async Task Sweep(CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var now = DateTime.UtcNow;
        var jobs = await db.ServiceImageDeletions.Where(j => j.DueAt <= now).OrderBy(j => j.DueAt).Take(100).ToListAsync(ct);
        var store = scope.ServiceProvider.GetRequiredService<IServiceImageStore>();
        foreach (var job in jobs)
        {
            try { await store.Delete(job.ObjectKey, ct); db.ServiceImageDeletions.Remove(job); }
            catch (Exception) when (!ct.IsCancellationRequested) { job.Attempts++; job.DueAt = DateTime.UtcNow.AddMinutes(Math.Min(60, 1 << Math.Min(job.Attempts, 5))); logger.LogWarning("Service image deletion {JobId} will retry.", job.Id); }
            await db.SaveChangesAsync(ct);
        }
    }
}
