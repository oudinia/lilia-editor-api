using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class DocumentGroupConfiguration : IEntityTypeConfiguration<DocumentGroup>
{
    public void Configure(EntityTypeBuilder<DocumentGroup> builder)
    {
        builder.ToTable("document_groups");

        builder.HasKey(dg => dg.Id);
        builder.Property(dg => dg.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(dg => dg.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(dg => dg.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(dg => dg.RoleId).HasColumnName("role_id").IsRequired();
        builder.Property(dg => dg.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(dg => dg.DocumentId);
        builder.HasIndex(dg => new { dg.DocumentId, dg.GroupId }).IsUnique();

        builder.HasOne(dg => dg.Document)
            .WithMany(d => d.DocumentGroups)
            .HasForeignKey(dg => dg.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(dg => dg.Group)
            .WithMany(g => g.DocumentGroups)
            .HasForeignKey(dg => dg.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(dg => dg.Role)
            .WithMany(r => r.DocumentGroups)
            .HasForeignKey(dg => dg.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
