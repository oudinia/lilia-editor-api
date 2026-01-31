using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("groups");

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(g => g.TeamId).HasColumnName("team_id").IsRequired();
        builder.Property(g => g.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(g => g.Description).HasColumnName("description");
        builder.Property(g => g.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
        builder.Property(g => g.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(g => g.TeamId);

        builder.HasOne(g => g.Team)
            .WithMany(t => t.Groups)
            .HasForeignKey(g => g.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
