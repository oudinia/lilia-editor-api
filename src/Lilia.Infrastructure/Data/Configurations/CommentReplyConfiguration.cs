using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class CommentReplyConfiguration : IEntityTypeConfiguration<CommentReply>
{
    public void Configure(EntityTypeBuilder<CommentReply> builder)
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

        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
