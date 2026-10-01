using Marketplace.Api.Identity;
using Marketplace.Api.Organizations;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace Marketplace.Api.Data;

public sealed class MarketplaceDbContext(DbContextOptions<MarketplaceDbContext> options) : IdentityDbContext<Account>(options)
{
    public DbSet<AccountSession> AccountSessions => Set<AccountSession>();
    public DbSet<VerificationEmail> VerificationEmails => Set<VerificationEmail>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<OrganizationInvitation> OrganizationInvitations => Set<OrganizationInvitation>();
    public DbSet<OrganizationAuditEvent> OrganizationAuditEvents => Set<OrganizationAuditEvent>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Account>().Property(x => x.DisplayName).HasMaxLength(150);
        builder.Entity<Account>().Property(x => x.AccountType).HasMaxLength(20);
        builder.Entity<Account>().HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.Entity<AccountSession>().HasOne<Account>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<AccountSession>().HasIndex(x => x.UserId);
        builder.Entity<VerificationEmail>().HasIndex(x => x.NextAttemptAt);
        builder.Entity<VerificationEmail>().Property(x => x.Subject).HasMaxLength(200);
        builder.Entity<Organization>().Property(x => x.Name).HasMaxLength(150);
        builder.Entity<Organization>().Property(x => x.ProviderType).HasMaxLength(20);
        builder.Entity<OrganizationMembership>().HasKey(x => new { x.OrganizationId, x.UserId });
        builder.Entity<OrganizationMembership>().Property(x => x.Role).HasMaxLength(20);
        builder.Entity<OrganizationMembership>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId);
        builder.Entity<OrganizationMembership>().HasOne<Account>().WithMany().HasForeignKey(x => x.UserId);
        builder.Entity<OrganizationInvitation>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId);
        builder.Entity<OrganizationInvitation>().HasIndex(x => x.TokenHash).IsUnique();
        builder.Entity<OrganizationInvitation>().Property(x => x.TokenHash).HasMaxLength(64);
        builder.Entity<OrganizationInvitation>().Property(x => x.Email).HasMaxLength(254);
        builder.Entity<OrganizationInvitation>().Property(x => x.NormalizedEmail).HasMaxLength(254);
        builder.Entity<OrganizationInvitation>().Property(x => x.Role).HasMaxLength(20);
        builder.Entity<OrganizationInvitation>().Property(x => x.Version).IsConcurrencyToken();
        builder.Entity<OrganizationAuditEvent>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId);
        builder.Entity<OrganizationAuditEvent>().HasOne<Account>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OrganizationAuditEvent>().HasIndex(x => new { x.OrganizationId, x.OccurredAt });
    }
}
