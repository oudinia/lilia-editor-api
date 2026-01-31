using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("teams");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(t => t.Slug).HasColumnName("slug").HasMaxLength(255);
        builder.Property(t => t.Image).HasColumnName("image");
        builder.Property(t => t.OwnerId).HasColumnName("owner_id").HasMaxLength(255).IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(t => t.Slug).IsUnique();
        builder.HasIndex(t => t.OwnerId);

        builder.HasOne(t => t.Owner)
            .WithMany(u => u.OwnedTeams)
            .HasForeignKey(t => t.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
