using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class OperationCreditConfiguration : IEntityTypeConfiguration<OperationCredit>
{
    public void Configure(EntityTypeBuilder<OperationCredit> builder)
    {
        builder.ToTable("operation_credits", t =>
        {
            t.HasCheckConstraint("ck_operation_credit_reason", "reason IN ('purchase','spend','refund','adjustment')");
        });
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(c => c.Delta).HasColumnName("delta").IsRequired();
        builder.Property(c => c.Reason).HasColumnName("reason").HasMaxLength(20).IsRequired();
        builder.Property(c => c.Tool).HasColumnName("tool").HasMaxLength(60);
        builder.Property(c => c.StripeSessionId).HasColumnName("stripe_session_id").HasMaxLength(255);
        builder.Property(c => c.RefundOf).HasColumnName("refund_of");
        builder.Property(c => c.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(c => c.UserId).HasDatabaseName("ix_operation_credits_user");
        // A Checkout session credits once, whatever the webhook does.
        builder.HasIndex(c => c.StripeSessionId).IsUnique().HasFilter("stripe_session_id IS NOT NULL")
            .HasDatabaseName("ux_operation_credits_stripe_session");
        // A spend is refunded once.
        builder.HasIndex(c => c.RefundOf).IsUnique().HasFilter("refund_of IS NOT NULL")
            .HasDatabaseName("ux_operation_credits_refund_of");

        builder.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
