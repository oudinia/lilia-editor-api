using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class ImportDefinitionConfiguration : IEntityTypeConfiguration<ImportDefinition>
{
    public void Configure(EntityTypeBuilder<ImportDefinition> builder)
    {
        builder.ToTable("import_definitions");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(d => d.OwnerId).HasColumnName("owner_id").HasMaxLength(255).IsRequired();
        builder.Property(d => d.SourceFileName).HasColumnName("source_file_name").HasMaxLength(500).IsRequired();
        builder.Property(d => d.SourceFormat).HasColumnName("source_format").HasMaxLength(30).HasDefaultValue("tex").IsRequired();
        builder.Property(d => d.RawSource).HasColumnName("raw_source");
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(d => d.OwnerId);
        builder.HasIndex(d => d.CreatedAt);

        builder.HasOne(d => d.Owner)
            .WithMany()
            .HasForeignKey(d => d.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
