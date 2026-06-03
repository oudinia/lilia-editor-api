using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("teams", t =>
        {
            t.HasCheckConstraint("ck_team_plan", "plan IN ('free','pro','team')");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(t => t.TeamCode).HasColumnName("team_code").HasMaxLength(64).IsRequired();
        builder.Property(t => t.Slug).HasColumnName("slug").HasMaxLength(255);
        builder.Property(t => t.Image).HasColumnName("image");
        builder.Property(t => t.OwnerId).HasColumnName("owner_id").HasMaxLength(255).IsRequired();
        builder.Property(t => t.Plan).HasColumnName("plan").HasMaxLength(20).HasDefaultValue("free").IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(t => t.IsPlayground).HasColumnName("is_playground").HasDefaultValue(false).IsRequired();

        builder.HasIndex(t => t.TeamCode).IsUnique();
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.HasIndex(t => t.OwnerId);

        builder.HasOne(t => t.Owner)
            .WithMany(u => u.OwnedTeams)
            .HasForeignKey(t => t.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

// TeamMemberConfiguration removed when the team_members table
// was retired in the RetireTeamMembersTable migration. Team
// membership is now exclusively GroupMember (see
// GroupMemberConfiguration). The Team-level Members nav was
// dropped from the Team entity.
