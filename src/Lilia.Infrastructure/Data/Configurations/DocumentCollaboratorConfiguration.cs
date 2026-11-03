using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class DocumentCollaboratorConfiguration : IEntityTypeConfiguration<DocumentCollaborator>
{
    /// <summary>
    /// The table as Sharing's own context maps it too (2 Nov 2026, the bounded contexts, step 4): table,
    /// columns, keys, indexes and the links inside Sharing. The links out of Sharing (documents, people,
    /// groups, blocks) are this context's only, for the migrations.
    /// </summary>
    public static void Shape(EntityTypeBuilder<DocumentCollaborator> builder)
    {
        builder.ToTable("document_collaborators");

        builder.HasKey(dc => dc.Id);
        builder.Property(dc => dc.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(dc => dc.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(dc => dc.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(dc => dc.RoleId).HasColumnName("role_id").IsRequired();
        builder.Property(dc => dc.InvitedBy).HasColumnName("invited_by").HasMaxLength(255);
        builder.Property(dc => dc.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(dc => dc.DocumentId);
        builder.HasIndex(dc => dc.UserId);
        builder.HasIndex(dc => new { dc.DocumentId, dc.UserId }).IsUnique();

        builder.HasOne(dc => dc.Role)
            .WithMany(r => r.DocumentCollaborators)
            .HasForeignKey(dc => dc.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<DocumentCollaborator> builder)
    {
        Shape(builder);

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(dc => dc.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(dc => dc.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(dc => dc.InvitedBy)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
