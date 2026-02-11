using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class ImportBlockCommentConfiguration : IEntityTypeConfiguration<ImportBlockComment>
{
    public void Configure(EntityTypeBuilder<ImportBlockComment> builder)
    {
        builder.ToTable("import_block_comments");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(c => c.BlockId).HasColumnName("block_id").HasMaxLength(255).IsRequired();
        builder.Property(c => c.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(c => c.Content).HasColumnName("content").IsRequired();
        builder.Property(c => c.Resolved).HasColumnName("resolved").HasDefaultValue(false).IsRequired();
        builder.Property(c => c.ResolvedBy).HasColumnName("resolved_by").HasMaxLength(255);
        builder.Property(c => c.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(c => c.SessionId);
        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => new { c.SessionId, c.BlockId });

        builder.HasOne(c => c.Session)
            .WithMany(s => s.Comments)
            .HasForeignKey(c => c.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Resolver)
            .WithMany()
            .HasForeignKey(c => c.ResolvedBy)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
