using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class ImportReviewCollaboratorConfiguration : IEntityTypeConfiguration<ImportReviewCollaborator>
{
    public void Configure(EntityTypeBuilder<ImportReviewCollaborator> builder)
    {
        builder.ToTable("import_review_collaborators");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(c => c.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(c => c.Role).HasColumnName("role").HasMaxLength(20).HasDefaultValue("reviewer").IsRequired();
        builder.Property(c => c.InvitedBy).HasColumnName("invited_by").HasMaxLength(255);
        builder.Property(c => c.InvitedAt).HasColumnName("invited_at").HasDefaultValueSql("NOW()");
        builder.Property(c => c.LastActiveAt).HasColumnName("last_active_at");

        builder.HasIndex(c => c.SessionId);
        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => new { c.SessionId, c.UserId }).IsUnique();

        builder.HasOne(c => c.Session)
            .WithMany(s => s.Collaborators)
            .HasForeignKey(c => c.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Inviter)
            .WithMany()
            .HasForeignKey(c => c.InvitedBy)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
