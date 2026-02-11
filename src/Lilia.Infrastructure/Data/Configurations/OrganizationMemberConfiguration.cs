using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class OrganizationMemberConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    public void Configure(EntityTypeBuilder<OrganizationMember> builder)
    {
        builder.ToTable("organization_members");

        builder.HasKey(om => om.Id);
        builder.Property(om => om.Id).HasColumnName("id").HasMaxLength(255);
        builder.Property(om => om.OrganizationId).HasColumnName("organization_id").HasMaxLength(255).IsRequired();
        builder.Property(om => om.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(om => om.Role).HasColumnName("role").HasMaxLength(50).HasDefaultValue("member").IsRequired();
        builder.Property(om => om.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(om => om.OrganizationId);
        builder.HasIndex(om => om.UserId);
        builder.HasIndex(om => new { om.OrganizationId, om.UserId }).IsUnique();

        builder.HasOne(om => om.Organization)
            .WithMany(o => o.Members)
            .HasForeignKey(om => om.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(om => om.User)
            .WithMany(u => u.OrganizationMemberships)
            .HasForeignKey(om => om.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
