using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class OperationConfiguration : IEntityTypeConfiguration<Operation>
{
    public void Configure(EntityTypeBuilder<Operation> builder)
    {
        builder.ToTable("operations", t =>
        {
            t.HasCheckConstraint("ck_operation_source", "source IN ('screens','connector')");
        });

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(o => o.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(o => o.DocumentId).HasColumnName("document_id");
        builder.Property(o => o.Kind).HasColumnName("kind").HasMaxLength(60).IsRequired();
        builder.Property(o => o.Points).HasColumnName("points").HasDefaultValue(0);
        builder.Property(o => o.Units).HasColumnName("units");
        builder.Property(o => o.Format).HasColumnName("format").HasMaxLength(20).IsRequired();
        builder.Property(o => o.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(o => new { o.UserId, o.CreatedAt }).HasDatabaseName("ix_operations_user_time");
        builder.HasIndex(o => new { o.Kind, o.CreatedAt }).HasDatabaseName("ix_operations_kind_time");
        builder.HasOne<OperationKind>().WithMany().HasForeignKey(o => o.Kind).OnDelete(DeleteBehavior.Restrict);

        // The record outlives the document (a deleted paper was still exported), not the account.
        builder.HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
