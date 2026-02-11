using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").HasMaxLength(255);
        builder.Property(i => i.OrganizationId).HasColumnName("organization_id").HasMaxLength(255).IsRequired();
        builder.Property(i => i.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
        builder.Property(i => i.Role).HasColumnName("role").HasMaxLength(50);
        builder.Property(i => i.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("pending").IsRequired();
        builder.Property(i => i.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(i => i.InviterId).HasColumnName("inviter_id").HasMaxLength(255).IsRequired();
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(i => i.OrganizationId);
        builder.HasIndex(i => i.Email);

        builder.HasOne(i => i.Organization)
            .WithMany(o => o.Invitations)
            .HasForeignKey(i => i.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Inviter)
            .WithMany(u => u.SentInvitations)
            .HasForeignKey(i => i.InviterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
