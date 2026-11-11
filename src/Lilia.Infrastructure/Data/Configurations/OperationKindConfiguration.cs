using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class OperationKindConfiguration : IEntityTypeConfiguration<OperationKind>
{
    public void Configure(EntityTypeBuilder<OperationKind> builder)
    {
        builder.ToTable("operation_kinds", t =>
        {
            t.HasCheckConstraint("ck_operation_kind_category", "category IN ('help','read','edit','document','check','export','import','ai')");
            t.HasCheckConstraint("ck_operation_kind_points", "points >= 0 AND (points_per_unit IS NULL OR (points_per_unit >= 0 AND unit_size > 0))");
        });
        builder.HasKey(k => k.Code);
        builder.Property(k => k.Code).HasColumnName("code").HasMaxLength(60);
        builder.Property(k => k.Category).HasColumnName("category").HasMaxLength(20).IsRequired();
        builder.Property(k => k.Label).HasColumnName("label").HasMaxLength(120).IsRequired();
        builder.Property(k => k.Points).HasColumnName("points");
        builder.Property(k => k.PointsPerUnit).HasColumnName("points_per_unit");
        builder.Property(k => k.UnitSize).HasColumnName("unit_size");
        builder.Property(k => k.UnitName).HasColumnName("unit_name").HasMaxLength(20);
        builder.Property(k => k.Active).HasColumnName("active").HasDefaultValue(true);
        builder.Property(k => k.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
    }
}
