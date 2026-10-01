using System.Net.Mail;
using Marketplace.Api.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
namespace Marketplace.Api.Identity;

public sealed class VerificationEmailWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<VerificationEmailWorker> logger) : BackgroundService
{
    public const string Purpose = "Marketplace.VerificationEmail.v1";
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>(); var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose); var now = DateTime.UtcNow; var emails = await db.VerificationEmails.Where(x => x.SentAt == null && x.Attempts < 6 && x.NextAttemptAt <= now).OrderBy(x => x.CreatedAt).Take(10).ToListAsync(stop);
                foreach (var email in emails) { try { using var smtp = new SmtpClient(config["Email:Host"] ?? "localhost", config.GetValue<int?>("Email:Port") ?? 1025); using var message = new MailMessage(config["Email:From"] ?? "noreply@marketplace.example", email.Recipient, email.Subject, protector.Unprotect(email.ProtectedBody)); await smtp.SendMailAsync(message, stop); email.SentAt = DateTime.UtcNow; email.ProtectedBody = ""; } catch (Exception) when (!stop.IsCancellationRequested) { email.Attempts++; email.NextAttemptAt = DateTime.UtcNow.AddSeconds(Math.Pow(2, email.Attempts) * 10); logger.LogWarning("Email retry scheduled. Event {EventId}", email.Id); } await db.SaveChangesAsync(stop); }
            }
            catch (Exception) when (!stop.IsCancellationRequested) { logger.LogWarning("Verification email queue unavailable."); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stop); } catch (OperationCanceledException) { break; }
        }
    }
}
