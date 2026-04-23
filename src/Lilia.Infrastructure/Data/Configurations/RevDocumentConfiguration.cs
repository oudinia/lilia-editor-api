using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class RevDocumentConfiguration : IEntityTypeConfiguration<RevDocument>
{
    public void Configure(EntityTypeBuilder<RevDocument> builder)
    {
        builder.ToTable("rev_documents");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(d => d.InstanceId).HasColumnName("instance_id").IsRequired();
        builder.Property(d => d.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
        builder.Property(d => d.Description).HasColumnName("description");
        builder.Property(d => d.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(d => d.SourceFormat).HasColumnName("source_format").HasMaxLength(30);
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(d => d.InstanceId);

        builder.HasOne(d => d.Instance)
            .WithMany()
            .HasForeignKey(d => d.InstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
