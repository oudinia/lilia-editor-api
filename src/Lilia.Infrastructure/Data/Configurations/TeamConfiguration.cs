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

        builder.HasIndex(t => t.TeamCode).IsUnique();
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.HasIndex(t => t.OwnerId);

        builder.HasOne(t => t.Owner)
            .WithMany(u => u.OwnedTeams)
            .HasForeignKey(t => t.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("team_members", t =>
        {
            t.HasCheckConstraint("ck_team_member_role", "role IN ('owner','admin','member','viewer')");
        });

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(m => m.TeamId).HasColumnName("team_id").IsRequired();
        builder.Property(m => m.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(m => m.Role).HasColumnName("role").HasMaxLength(20).HasDefaultValue("member").IsRequired();
        builder.Property(m => m.InvitedBy).HasColumnName("invited_by").HasMaxLength(255);
        builder.Property(m => m.JoinedAt).HasColumnName("joined_at").HasDefaultValueSql("NOW()");

        // Each user appears at most once per team — the join is uniquely
        // identified by (TeamId, UserId).
        builder.HasIndex(m => new { m.TeamId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);

        builder.HasOne(m => m.Team)
            .WithMany(t => t.Members)
            .HasForeignKey(m => m.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.User)
            .WithMany(u => u.TeamMemberships)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Inviter)
            .WithMany()
            .HasForeignKey(m => m.InvitedBy)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
