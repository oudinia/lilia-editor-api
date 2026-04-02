using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class FeedbackConfiguration : IEntityTypeConfiguration<Feedback>
{
    public void Configure(EntityTypeBuilder<Feedback> builder)
    {
        builder.ToTable("feedback");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(f => f.UserId).HasColumnName("user_id").HasMaxLength(255);
        builder.Property(f => f.Type).HasColumnName("type").HasMaxLength(50).IsRequired().HasDefaultValue("general");
        builder.Property(f => f.Message).HasColumnName("message").IsRequired();
        builder.Property(f => f.Page).HasColumnName("page").HasMaxLength(500);
        builder.Property(f => f.BlockType).HasColumnName("block_type").HasMaxLength(100);
        builder.Property(f => f.BlockId).HasColumnName("block_id").HasMaxLength(255);
        builder.Property(f => f.DocumentId).HasColumnName("document_id").HasMaxLength(255);
        builder.Property(f => f.Status).HasColumnName("status").HasMaxLength(50).IsRequired().HasDefaultValue("new");
        builder.Property(f => f.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(f => f.Response).HasColumnName("response");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(f => f.UserId);
        builder.HasIndex(f => new { f.Status, f.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(f => f.Type);

        builder.HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
