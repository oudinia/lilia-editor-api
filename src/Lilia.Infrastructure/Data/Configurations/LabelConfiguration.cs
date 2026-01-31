using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    public void Configure(EntityTypeBuilder<Label> builder)
    {
        builder.ToTable("labels");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(l => l.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(l => l.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(l => l.Color).HasColumnName("color").HasMaxLength(7);
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(l => l.UserId);

        builder.HasOne(l => l.User)
            .WithMany(u => u.Labels)
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
