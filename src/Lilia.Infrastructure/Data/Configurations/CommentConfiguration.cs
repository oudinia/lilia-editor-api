using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    /// <summary>
    /// The table as Sharing's own context maps it too (2 Nov 2026, the bounded contexts, step 4): table,
    /// columns, keys, indexes and the links inside Sharing. The links out of Sharing (documents, people,
    /// groups, blocks) are this context's only, for the migrations.
    /// </summary>
    public static void Shape(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("comments");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(c => c.BlockId).HasColumnName("block_id");
        builder.Property(c => c.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(c => c.Content).HasColumnName("content").IsRequired();
        builder.Property(c => c.Resolved).HasColumnName("resolved").HasDefaultValue(false);
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(c => c.DocumentId);
        builder.HasIndex(c => c.BlockId);
        builder.HasIndex(c => c.UserId);
    }

    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        Shape(builder);

        builder.HasOne(c => c.Document)
            .WithMany(d => d.Comments)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Block)
            .WithMany(b => b.Comments)
            .HasForeignKey(c => c.BlockId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(c => c.User)
            .WithMany(u => u.Comments)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
