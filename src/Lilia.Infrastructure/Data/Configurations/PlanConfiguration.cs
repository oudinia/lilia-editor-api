using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans", t =>
        {
            t.HasCheckConstraint("ck_plan_slug",
                "slug IN ('free','student','pro','team','epub','compliance_pro','enterprise')");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(p => p.Slug).HasColumnName("slug").HasMaxLength(30).IsRequired();
        builder.Property(p => p.DisplayName).HasColumnName("display_name").HasMaxLength(80).IsRequired();
        builder.Property(p => p.MonthlyPrice).HasColumnName("monthly_price").HasColumnType("numeric(10,2)");
        builder.Property(p => p.YearlyPrice).HasColumnName("yearly_price").HasColumnType("numeric(10,2)");
        builder.Property(p => p.Caps).HasColumnName("caps").HasColumnType("jsonb").IsRequired();
        builder.Property(p => p.Features).HasColumnName("features").HasColumnType("jsonb").IsRequired();
        builder.Property(p => p.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(p => p.Slug).IsUnique().HasDatabaseName("ux_plan_slug");
    }
}
