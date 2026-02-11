using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasMaxLength(255);
        builder.Property(s => s.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(s => s.Token).HasColumnName("token").IsRequired();
        builder.Property(s => s.ExpiresAt).HasColumnName("expires_at");
        builder.Property(s => s.IpAddress).HasColumnName("ip_address").HasMaxLength(255);
        builder.Property(s => s.UserAgent).HasColumnName("user_agent");
        builder.Property(s => s.ImpersonatedBy).HasColumnName("impersonated_by").HasMaxLength(255);
        builder.Property(s => s.ActiveOrganizationId).HasColumnName("active_organization_id").HasMaxLength(255);
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(s => s.Token).IsUnique();
        builder.HasIndex(s => s.UserId);

        builder.HasOne(s => s.User)
            .WithMany(u => u.Sessions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
