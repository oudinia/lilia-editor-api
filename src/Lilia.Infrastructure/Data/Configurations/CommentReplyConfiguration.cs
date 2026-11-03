using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class CommentReplyConfiguration : IEntityTypeConfiguration<CommentReply>
{
    /// <summary>
    /// The table as Sharing's own context maps it too (2 Nov 2026, the bounded contexts, step 4): table,
    /// columns, keys, indexes and the links inside Sharing. The links out of Sharing (documents, people,
    /// groups, blocks) are this context's only, for the migrations.
    /// </summary>
    public static void Shape(EntityTypeBuilder<CommentReply> builder)
    {
        builder.ToTable("comment_replies");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(r => r.CommentId).HasColumnName("comment_id").IsRequired();
        builder.Property(r => r.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(r => r.Content).HasColumnName("content").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(r => r.CommentId);

        builder.HasOne(r => r.Comment)
            .WithMany(c => c.Replies)
            .HasForeignKey(r => r.CommentId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<CommentReply> builder)
    {
        Shape(builder);

        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
