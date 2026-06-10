using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Infrastructure.Persistence;

/// <summary>
/// EF Core context over SQLite: Identity tables plus the project entities.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<DeviceMembership> DeviceMemberships => Set<DeviceMembership>();

    public DbSet<DeviceInvite> DeviceInvites => Set<DeviceInvite>();

    public DbSet<TelemetryReading> Readings => Set<TelemetryReading>();

    public DbSet<VoltageEvent> VoltageEvents => Set<VoltageEvent>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Device>(device =>
        {
            device.ToTable("Devices");
            device.HasKey(d => d.Id);
            device.Property(d => d.Id).HasMaxLength(32);
            device.Property(d => d.Name).HasMaxLength(60).IsRequired();
            device.Property(d => d.PairingCodeHash).HasMaxLength(256);
            device.Property(d => d.FirmwareVersion).HasMaxLength(32);
        });

        builder.Entity<DeviceMembership>(membership =>
        {
            membership.ToTable("DeviceMemberships");
            membership.HasKey(m => m.Id);
            membership.Property(m => m.DeviceId).HasMaxLength(32).IsRequired();
            membership.Property(m => m.UserId).HasMaxLength(64).IsRequired();
            membership.Property(m => m.Role).HasConversion<int>();
            // One grant per user per device; fast lookup of a user's devices.
            membership.HasIndex(m => new { m.DeviceId, m.UserId }).IsUnique();
            membership.HasIndex(m => m.UserId);
            membership.HasOne<Device>()
                .WithMany()
                .HasForeignKey(m => m.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DeviceInvite>(invite =>
        {
            invite.ToTable("DeviceInvites");
            invite.HasKey(i => i.Id);
            invite.Property(i => i.DeviceId).HasMaxLength(32).IsRequired();
            invite.Property(i => i.CodeHash).HasMaxLength(256).IsRequired();
            invite.Property(i => i.CreatedByUserId).HasMaxLength(64).IsRequired();
            invite.HasIndex(i => new { i.DeviceId, i.ExpiresAtUtc });
            invite.HasOne<Device>()
                .WithMany()
                .HasForeignKey(i => i.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TelemetryReading>(reading =>
        {
            reading.ToTable("Readings");
            reading.HasKey(r => r.Id);
            reading.Property(r => r.DeviceId).HasMaxLength(32).IsRequired();
            // The hot query path: latest/range per device.
            reading.HasIndex(r => new { r.DeviceId, r.TimestampUtc });
            reading.HasOne<Device>()
                .WithMany()
                .HasForeignKey(r => r.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<VoltageEvent>(voltageEvent =>
        {
            voltageEvent.ToTable("VoltageEvents");
            voltageEvent.HasKey(e => e.Id);
            voltageEvent.Property(e => e.DeviceId).HasMaxLength(32).IsRequired();
            voltageEvent.Property(e => e.Type).HasConversion<int>();
            voltageEvent.HasIndex(e => new { e.DeviceId, e.StartedAtUtc });
            // Fast "open episode" lookup (EndedAtUtc IS NULL).
            voltageEvent.HasIndex(e => new { e.DeviceId, e.EndedAtUtc });
            voltageEvent.Ignore(e => e.IsOpen);
            voltageEvent.HasOne<Device>()
                .WithMany()
                .HasForeignKey(e => e.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AuditEntry>(audit =>
        {
            audit.ToTable("AuditEntries");
            audit.HasKey(a => a.Id);
            audit.Property(a => a.Action).HasMaxLength(64).IsRequired();
            audit.Property(a => a.UserId).HasMaxLength(64);
            audit.Property(a => a.UserEmail).HasMaxLength(256);
            audit.Property(a => a.DeviceId).HasMaxLength(32);
            audit.Property(a => a.Details).HasMaxLength(512);
            audit.Property(a => a.IpAddress).HasMaxLength(64);
            audit.HasIndex(a => a.TimestampUtc);
        });
    }
}
