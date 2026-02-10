using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class FormulaConfiguration : IEntityTypeConfiguration<Formula>
{
    public void Configure(EntityTypeBuilder<Formula> builder)
    {
        builder.ToTable("formulas");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(f => f.UserId).HasColumnName("user_id").HasMaxLength(255);
        builder.Property(f => f.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(f => f.Description).HasColumnName("description");
        builder.Property(f => f.LatexContent).HasColumnName("latex_content").IsRequired();
        builder.Property(f => f.LmlContent).HasColumnName("lml_content");
        builder.Property(f => f.Category).HasColumnName("category").HasMaxLength(50).IsRequired();
        builder.Property(f => f.Subcategory).HasColumnName("subcategory").HasMaxLength(50);
        builder.Property(f => f.Tags).HasColumnName("tags").HasColumnType("jsonb");
        builder.Property(f => f.IsFavorite).HasColumnName("is_favorite").HasDefaultValue(false);
        builder.Property(f => f.IsSystem).HasColumnName("is_system").HasDefaultValue(false);
        builder.Property(f => f.UsageCount).HasColumnName("usage_count").HasDefaultValue(0);
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(f => f.UserId);
        builder.HasIndex(f => f.Category);
        builder.HasIndex(f => f.IsSystem);
        builder.HasIndex(f => new { f.UserId, f.Category });

        builder.HasOne(f => f.User)
            .WithMany(u => u.Formulas)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
