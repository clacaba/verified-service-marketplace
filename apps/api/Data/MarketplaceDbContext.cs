using Marketplace.Api.Providers;
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
    public DbSet<ProviderProfile> ProviderProfiles => Set<ProviderProfile>();
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<ProviderCategory> ProviderCategories => Set<ProviderCategory>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ProviderProfile>().HasIndex(x => x.OrganizationId).IsUnique();
        builder.Entity<ProviderProfile>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId);
        builder.Entity<ProviderProfile>().Property(x => x.DisplayName).HasMaxLength(150);
        builder.Entity<ProviderProfile>().Property(x => x.ProviderType).HasMaxLength(20);
        builder.Entity<ProviderProfile>().Property(x => x.Biography).HasMaxLength(4000);
        builder.Entity<ProviderProfile>().Property(x => x.ServiceArea).HasMaxLength(250);
        builder.Entity<ProviderProfile>().Property(x => x.Version).IsConcurrencyToken();
        builder.Entity<ServiceCategory>().Property(x => x.Name).HasMaxLength(100);
        builder.Entity<ServiceCategory>().HasData(
            new ServiceCategory { Id = Guid.Parse("10000000-0000-0000-0000-000000000001"), Name = "Cleaning" },
            new ServiceCategory { Id = Guid.Parse("10000000-0000-0000-0000-000000000002"), Name = "Plumbing" },
            new ServiceCategory { Id = Guid.Parse("10000000-0000-0000-0000-000000000003"), Name = "Electrical" },
            new ServiceCategory { Id = Guid.Parse("10000000-0000-0000-0000-000000000004"), Name = "Appliance repair" });
        builder.Entity<ProviderCategory>().HasKey(x => new { x.ProfileId, x.CategoryId });
        builder.Entity<ProviderProfile>().HasMany(x => x.Categories).WithOne().HasForeignKey(x => x.ProfileId);
        builder.Entity<ProviderCategory>().HasOne<ServiceCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
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
