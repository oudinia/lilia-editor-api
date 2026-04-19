using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class AiCreditLedgerConfiguration : IEntityTypeConfiguration<AiCreditLedger>
{
    public void Configure(EntityTypeBuilder<AiCreditLedger> builder)
    {
        builder.ToTable("ai_credit_ledger", t =>
        {
            t.HasCheckConstraint("ck_ai_credit_reason",
                "reason IN ('grant','spend','adjustment','refund')");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(l => l.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(l => l.Delta).HasColumnName("delta").IsRequired();
        builder.Property(l => l.Reason).HasColumnName("reason").HasMaxLength(20).IsRequired();
        builder.Property(l => l.AiRequestId).HasColumnName("ai_request_id");
        builder.Property(l => l.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(l => l.UserId).HasDatabaseName("ix_ai_credit_user");
        builder.HasIndex(l => new { l.UserId, l.CreatedAt }).HasDatabaseName("ix_ai_credit_user_time");

        builder.HasOne(l => l.User)
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.AiRequest)
            .WithMany()
            .HasForeignKey(l => l.AiRequestId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
