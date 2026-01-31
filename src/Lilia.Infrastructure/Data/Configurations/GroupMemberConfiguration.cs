using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.ToTable("group_members");

        builder.HasKey(gm => gm.Id);
        builder.Property(gm => gm.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(gm => gm.GroupId).HasColumnName("group_id").IsRequired();
        builder.Property(gm => gm.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(gm => gm.RoleId).HasColumnName("role_id").IsRequired();
        builder.Property(gm => gm.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(gm => gm.GroupId);
        builder.HasIndex(gm => gm.UserId);
        builder.HasIndex(gm => new { gm.GroupId, gm.UserId }).IsUnique();

        builder.HasOne(gm => gm.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(gm => gm.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(gm => gm.User)
            .WithMany(u => u.GroupMemberships)
            .HasForeignKey(gm => gm.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(gm => gm.Role)
            .WithMany(r => r.GroupMembers)
            .HasForeignKey(gm => gm.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
