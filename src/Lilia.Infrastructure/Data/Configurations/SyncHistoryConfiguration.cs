using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class SyncHistoryConfiguration : IEntityTypeConfiguration<SyncHistory>
{
    public void Configure(EntityTypeBuilder<SyncHistory> builder)
    {
        builder.ToTable("sync_history");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(s => s.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(s => s.Action).HasColumnName("action").HasMaxLength(50).IsRequired();
        builder.Property(s => s.SyncVersion).HasColumnName("sync_version").IsRequired();
        builder.Property(s => s.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(s => s.DocumentId);
        builder.HasIndex(s => s.UserId);

        builder.HasOne(s => s.Document)
            .WithMany()
            .HasForeignKey(s => s.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.User)
            .WithMany(u => u.SyncHistories)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
