using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(r => r.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
        builder.Property(r => r.Description).HasColumnName("description");
        builder.Property(r => r.Permissions).HasColumnName("permissions")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb");

        builder.HasIndex(r => r.Name).IsUnique();

        // Seed data
        var ownerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var editorId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var viewerId = Guid.Parse("00000000-0000-0000-0000-000000000003");

        builder.HasData(
            new Role
            {
                Id = ownerId,
                Name = RoleNames.Owner,
                Description = "Full control",
                Permissions = new List<string> { Permissions.Read, Permissions.Write, Permissions.Delete, Permissions.Manage, Permissions.Transfer }
            },
            new Role
            {
                Id = editorId,
                Name = RoleNames.Editor,
                Description = "Can edit content",
                Permissions = new List<string> { Permissions.Read, Permissions.Write }
            },
            new Role
            {
                Id = viewerId,
                Name = RoleNames.Viewer,
                Description = "Read-only access",
                Permissions = new List<string> { Permissions.Read }
            }
        );
    }
}
