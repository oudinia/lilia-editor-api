using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class UserPlanConfiguration : IEntityTypeConfiguration<UserPlan>
{
    public void Configure(EntityTypeBuilder<UserPlan> builder)
    {
        builder.ToTable("user_plans", t =>
        {
            t.HasCheckConstraint("ck_user_plan_status",
                "status IN ('active','trial','past_due','cancelled')");
        });

        builder.HasKey(up => up.Id);
        builder.Property(up => up.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(up => up.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(up => up.PlanId).HasColumnName("plan_id").IsRequired();
        builder.Property(up => up.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("active").IsRequired();
        builder.Property(up => up.StartedAt).HasColumnName("started_at").HasDefaultValueSql("NOW()");
        builder.Property(up => up.EndsAt).HasColumnName("ends_at");
        builder.Property(up => up.CurrentPeriodStart).HasColumnName("current_period_start");
        builder.Property(up => up.CurrentPeriodEnd).HasColumnName("current_period_end");
        builder.Property(up => up.ExternalRef).HasColumnName("external_ref").HasMaxLength(120);
        builder.Property(up => up.CancelAtPeriodEnd).HasColumnName("cancel_at_period_end").HasDefaultValue(false);
        builder.Property(up => up.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(up => up.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        // Partial unique index: at most one active plan per user. Trial
        // and cancelled rows can coexist so history is preserved.
        builder.HasIndex(up => up.UserId)
            .HasDatabaseName("ux_user_plan_one_active_per_user")
            .IsUnique()
            .HasFilter("status = 'active'");

        builder.HasIndex(up => up.UserId).HasDatabaseName("ix_user_plan_user");
        builder.HasIndex(up => up.ExternalRef).HasDatabaseName("ix_user_plan_external_ref");

        builder.HasOne(up => up.User)
            .WithMany()
            .HasForeignKey(up => up.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(up => up.Plan)
            .WithMany(p => p.UserPlans)
            .HasForeignKey(up => up.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
