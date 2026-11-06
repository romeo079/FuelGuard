using FuelGuard.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FuelGuard.Infrastructure;

public sealed class FuelGuardDbContext(DbContextOptions<FuelGuardDbContext> options)
    : IdentityDbContext<FuelGuardUser, FuelGuardRole, string>(options)
{
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<StationExit> StationExits => Set<StationExit>();
    public DbSet<UserStationAssignment> UserStationAssignments => Set<UserStationAssignment>();
    public DbSet<Attendant> Attendants => Set<Attendant>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Pump> Pumps => Set<Pump>();
    public DbSet<FuelTransaction> FuelTransactions => Set<FuelTransaction>();
    public DbSet<FuelTransactionItem> FuelTransactionItems => Set<FuelTransactionItem>();
    public DbSet<PaymentRecord> PaymentRecords => Set<PaymentRecord>();
    public DbSet<PaymentVerificationLog> PaymentVerificationLogs => Set<PaymentVerificationLog>();
    public DbSet<ExitAuthorisation> ExitAuthorisations => Set<ExitAuthorisation>();
    public DbSet<ExitScanRecord> ExitScanRecords => Set<ExitScanRecord>();
    public DbSet<ExitEvent> ExitEvents => Set<ExitEvent>();
    public DbSet<HardwareDevice> HardwareDevices => Set<HardwareDevice>();
    public DbSet<HardwareStatusLog> HardwareStatusLogs => Set<HardwareStatusLog>();
    public DbSet<POSConfiguration> POSConfigurations => Set<POSConfiguration>();
    public DbSet<POSIntegrationLog> POSIntegrationLogs => Set<POSIntegrationLog>();
    public DbSet<PrinterConfiguration> PrinterConfigurations => Set<PrinterConfiguration>();
    public DbSet<ANPRRecord> ANPRRecords => Set<ANPRRecord>();
    public DbSet<IncidentReport> IncidentReports => Set<IncidentReport>();
    public DbSet<SupervisorOverride> SupervisorOverrides => Set<SupervisorOverride>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Station>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32).IsRequired();
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            e.Property(x => x.VatRate).HasPrecision(5, 4);
        });
        builder.Entity<StationExit>(e =>
        {
            e.HasIndex(x => new { x.StationId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32);
            e.HasOne(x => x.Station).WithMany(x => x.Exits).HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Pump>(e =>
        {
            e.HasIndex(x => new { x.StationId, x.Number }).IsUnique();
            e.HasOne(x => x.Station).WithMany(x => x.Pumps).HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<UserStationAssignment>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.StationId }).IsUnique();
            e.HasOne<FuelGuardUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Station).WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Attendant>(e =>
        {
            e.HasIndex(x => new { x.StationId, x.EmployeeNumber }).IsUnique();
            e.HasOne(x => x.Station).WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FuelGuardUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Vehicle>(e =>
        {
            e.HasIndex(x => new { x.StationId, x.RegistrationNormalized });
            e.Property(x => x.RegistrationNormalized).HasMaxLength(16).IsRequired();
            e.Property(x => x.RegistrationDisplay).HasMaxLength(16);
            e.HasOne(x => x.Station).WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<FuelTransaction>(e =>
        {
            e.HasIndex(x => new { x.StationId, x.Reference }).IsUnique();
            e.HasIndex(x => new { x.StationId, x.PosTransactionReference }).IsUnique().HasFilter("[PosTransactionReference] IS NOT NULL");
            e.HasIndex(x => new { x.StationId, x.CreatedAtUtc });
            e.Property(x => x.Reference).HasMaxLength(48).IsRequired();
            e.Property(x => x.PosTransactionReference).HasMaxLength(80);
            e.Property(x => x.FuelType).HasMaxLength(40).IsRequired();
            e.Property(x => x.RequestedAmount).HasPrecision(18, 2);
            e.Property(x => x.RequestedLitres).HasPrecision(18, 3);
            e.Property(x => x.FinalLitres).HasPrecision(18, 3);
            e.Property(x => x.FinalAmount).HasPrecision(18, 2);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne(x => x.Station).WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Pump).WithMany().HasForeignKey(x => x.PumpId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Vehicle).WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<FuelTransactionItem>(e =>
        {
            e.Property(x => x.Litres).HasPrecision(18, 3);
            e.Property(x => x.UnitPrice).HasPrecision(18, 4);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne(x => x.FuelTransaction).WithMany(x => x.Items).HasForeignKey(x => x.FuelTransactionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PaymentRecord>(e =>
        {
            e.HasIndex(x => new { x.Provider, x.ProviderEventId }).IsUnique().HasFilter("[ProviderEventId] IS NOT NULL");
            e.HasIndex(x => new { x.Provider, x.ProviderTransactionReference });
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.CurrencyCode).HasMaxLength(3);
            e.HasOne(x => x.FuelTransaction).WithMany(x => x.Payments).HasForeignKey(x => x.FuelTransactionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PaymentVerificationLog>(e =>
        {
            e.HasIndex(x => new { x.FuelTransactionId, x.ProviderEventId }).IsUnique();
            e.Property(x => x.ProviderEventId).HasMaxLength(100);
            e.HasOne<FuelTransaction>().WithMany().HasForeignKey(x => x.FuelTransactionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PaymentRecord>().WithMany().HasForeignKey(x => x.PaymentRecordId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ExitAuthorisation>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.FuelTransactionId, x.CreatedAtUtc });
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.HasOne(x => x.FuelTransaction).WithMany().HasForeignKey(x => x.FuelTransactionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.StationExit).WithMany().HasForeignKey(x => x.StationExitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Station>().WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ExitScanRecord>(e =>
        {
            e.HasIndex(x => new { x.StationId, x.CreatedAtUtc });
            e.Property(x => x.TokenFingerprint).HasMaxLength(64);
            e.HasOne<ExitAuthorisation>().WithMany().HasForeignKey(x => x.ExitAuthorisationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ExitEvent>(e => e.HasIndex(x => new { x.StationId, x.CreatedAtUtc }));
        builder.Entity<HardwareDevice>(e =>
        {
            e.HasIndex(x => new { x.StationId, x.DeviceCode }).IsUnique();
            e.Property(x => x.AdapterName).HasMaxLength(80);
        });
        builder.Entity<HardwareStatusLog>(e => e.HasIndex(x => new { x.StationId, x.CreatedAtUtc }));
        builder.Entity<POSConfiguration>(e => e.HasIndex(x => x.StationId).IsUnique());
        builder.Entity<POSIntegrationLog>(e => e.HasIndex(x => new { x.StationId, x.CreatedAtUtc }));
        builder.Entity<PrinterConfiguration>(e => e.HasIndex(x => x.StationId).IsUnique());
        builder.Entity<ANPRRecord>(e =>
        {
            e.Property(x => x.Confidence).HasPrecision(5, 4);
            e.HasIndex(x => new { x.StationId, x.CreatedAtUtc });
        });
        builder.Entity<IncidentReport>(e => e.HasIndex(x => new { x.StationId, x.Status, x.CreatedAtUtc }));
        builder.Entity<SupervisorOverride>(e => e.HasIndex(x => new { x.StationId, x.CreatedAtUtc }));
        builder.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => new { x.StationId, x.CreatedAtUtc });
            e.Property(x => x.DetailsJson).HasMaxLength(4000);
        });
        builder.Entity<SystemSetting>(e => e.HasIndex(x => new { x.StationId, x.Key }).IsUnique());
        builder.Entity<Notification>(e => e.HasIndex(x => new { x.StationId, x.ReadAtUtc, x.CreatedAtUtc }));
        builder.Entity<MaintenanceRecord>(e => e.HasIndex(x => new { x.StationId, x.Status }));
        builder.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.HasOne<FuelGuardUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAuditFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAuditFields()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAtUtc = now;
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
                if (entry.Entity is FuelTransaction transaction)
                    transaction.Version = Guid.NewGuid();
            }
        }
    }
}
