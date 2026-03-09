using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class DocumentPendingInviteConfiguration : IEntityTypeConfiguration<DocumentPendingInvite>
{
    public void Configure(EntityTypeBuilder<DocumentPendingInvite> builder)
    {
        builder.ToTable("document_pending_invites");

        builder.HasKey(pi => pi.Id);
        builder.Property(pi => pi.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(pi => pi.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(pi => pi.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        builder.Property(pi => pi.Role).HasColumnName("role").HasMaxLength(50).IsRequired();
        builder.Property(pi => pi.InvitedBy).HasColumnName("invited_by").HasMaxLength(255).IsRequired();
        builder.Property(pi => pi.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("pending");
        builder.Property(pi => pi.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(pi => pi.ExpiresAt).HasColumnName("expires_at");

        builder.HasIndex(pi => pi.Email);
        builder.HasIndex(pi => new { pi.DocumentId, pi.Email }).IsUnique()
            .HasFilter("status = 'pending'");

        builder.HasOne(pi => pi.Document)
            .WithMany()
            .HasForeignKey(pi => pi.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pi => pi.Inviter)
            .WithMany()
            .HasForeignKey(pi => pi.InvitedBy)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
