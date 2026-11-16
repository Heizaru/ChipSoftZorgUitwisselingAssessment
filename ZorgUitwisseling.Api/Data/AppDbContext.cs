using Microsoft.EntityFrameworkCore;
using ZorgUitwisseling.Api.Domain;

namespace ZorgUitwisseling.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Referral> Referrals => Set<Referral>();
    public DbSet<MedicalDocument> Documents => Set<MedicalDocument>();
    public DbSet<InformationRequest> InformationRequests => Set<InformationRequest>();
    public DbSet<ExchangeLog> ExchangeLogs => Set<ExchangeLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Referral>(entity =>
        {
            entity.Property(r => r.Status).HasConversion<string>();
            entity.HasOne<Organization>().WithMany().HasForeignKey(r => r.SenderOrganizationId);
            entity.HasOne<Organization>().WithMany().HasForeignKey(r => r.ReceiverOrganizationId);
        });

        modelBuilder.Entity<MedicalDocument>(entity =>
        {
            entity.Property(d => d.Type).HasConversion<string>();
            entity.HasOne(d => d.InformationRequest)
                .WithOne(r => r.ResponseDocument)
                .HasForeignKey<MedicalDocument>(d => d.InformationRequestId);
        });

        modelBuilder.Entity<InformationRequest>(entity =>
        {
            entity.Property(r => r.RequestedDocumentType).HasConversion<string>();
            entity.Property(r => r.Status).HasConversion<string>();
        });

        modelBuilder.Entity<ExchangeLog>(entity =>
        {
            entity.Property(l => l.Action).HasConversion<string>();
            entity.Property(l => l.DocumentType).HasConversion<string>();
        });
    }
}
