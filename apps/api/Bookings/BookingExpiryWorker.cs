using Marketplace.Api.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Bookings;

public sealed class BookingExpiryWorker(IServiceScopeFactory scopes, ILogger<BookingExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); await Sweep(scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>(), scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning(error, "Booking expiry sweep failed; retrying."); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
    public static async Task Sweep(MarketplaceDbContext db, IDataProtectionProvider protection)
    {
        var now = DateTime.UtcNow;
        var rows = await db.Bookings.Where(b => b.Status == "PendingProviderConfirmation" && b.ExpiresAt <= now).OrderBy(b => b.ExpiresAt).Take(100).ToListAsync();
        foreach (var b in rows) await BookingEndpoints.Transition(b, "Expired", "system", "Provider confirmation deadline elapsed.", db, protection);
        try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { /* another decision/sweep won; atomic rollback, next sweep retries */ }
    }
}
