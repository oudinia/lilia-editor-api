using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class AdminActionConfiguration : IEntityTypeConfiguration<AdminAction>
{
    public void Configure(EntityTypeBuilder<AdminAction> builder)
    {
        builder.ToTable("admin_actions", t => t.HasCheckConstraint("ck_admin_action_action",
            "action IN ('grant_credits','set_plan','suspend','unsuspend','resend_verification','feedback_status')"));
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.AdminEmail).HasColumnName("admin_email").HasMaxLength(255).IsRequired();
        builder.Property(a => a.Action).HasColumnName("action").HasMaxLength(40).IsRequired();
        builder.Property(a => a.TargetUserId).HasColumnName("target_user_id").HasMaxLength(255);
        builder.Property(a => a.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        builder.Property(a => a.Details).HasColumnName("details").HasColumnType("jsonb");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.HasIndex(a => new { a.TargetUserId, a.CreatedAt }).HasDatabaseName("ix_admin_actions_target");
        builder.HasIndex(a => a.CreatedAt).HasDatabaseName("ix_admin_actions_created");
    }
}
