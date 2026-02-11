using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class AiChatConfiguration : IEntityTypeConfiguration<AiChat>
{
    public void Configure(EntityTypeBuilder<AiChat> builder)
    {
        builder.ToTable("ai_chats");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasMaxLength(255);
        builder.Property(a => a.OrganizationId).HasColumnName("organization_id").HasMaxLength(255);
        builder.Property(a => a.UserId).HasColumnName("user_id").HasMaxLength(255);
        builder.Property(a => a.Title).HasColumnName("title").HasMaxLength(500);
        builder.Property(a => a.Messages).HasColumnName("messages").HasColumnType("jsonb");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne(a => a.Organization)
            .WithMany(o => o.AiChats)
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.User)
            .WithMany(u => u.AiChats)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
