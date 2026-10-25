using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

/// <summary>Two-step by email and trusted devices (25 Oct 2026).</summary>
public class TwoStepCodeConfiguration : IEntityTypeConfiguration<TwoStepCode>
{
    public void Configure(EntityTypeBuilder<TwoStepCode> builder)
    {
        builder.ToTable("two_step_codes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(c => c.CodeHash).HasColumnName("code_hash").HasMaxLength(64).IsRequired();
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at");
        builder.Property(c => c.Attempts).HasColumnName("attempts");
        builder.Property(c => c.UsedAt).HasColumnName("used_at");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.HasIndex(c => new { c.UserId, c.CreatedAt }).HasDatabaseName("ix_two_step_codes_user");
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TrustedDeviceConfiguration : IEntityTypeConfiguration<TrustedDevice>
{
    public void Configure(EntityTypeBuilder<TrustedDevice> builder)
    {
        builder.ToTable("trusted_devices");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(d => d.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(d => d.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.Property(d => d.Label).HasColumnName("label").HasMaxLength(120).IsRequired();
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(d => d.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(d => d.ExpiresAt).HasColumnName("expires_at");
        builder.Property(d => d.RevokedAt).HasColumnName("revoked_at");
        builder.HasIndex(d => d.TokenHash).IsUnique().HasDatabaseName("ux_trusted_devices_token");
        builder.HasIndex(d => d.UserId).HasDatabaseName("ix_trusted_devices_user");
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TwoStepSessionPassConfiguration : IEntityTypeConfiguration<TwoStepSessionPass>
{
    public void Configure(EntityTypeBuilder<TwoStepSessionPass> builder)
    {
        builder.ToTable("two_step_session_passes");
        builder.HasKey(p => p.SessionId);
        builder.Property(p => p.SessionId).HasColumnName("session_id").HasMaxLength(255);
        builder.Property(p => p.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(p => p.ExpiresAt).HasColumnName("expires_at");
        builder.HasIndex(p => p.UserId).HasDatabaseName("ix_two_step_session_passes_user");
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
