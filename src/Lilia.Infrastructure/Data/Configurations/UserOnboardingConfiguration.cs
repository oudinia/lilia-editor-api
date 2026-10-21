using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class UserOnboardingConfiguration : IEntityTypeConfiguration<UserOnboarding>
{
    public void Configure(EntityTypeBuilder<UserOnboarding> builder)
    {
        builder.ToTable("user_onboarding");

        builder.HasKey(o => o.UserId);
        builder.Property(o => o.UserId).HasColumnName("user_id").HasMaxLength(255);
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(o => o.VerifiedAt).HasColumnName("verified_at");
        builder.Property(o => o.WelcomeSentAt).HasColumnName("welcome_sent_at");
        builder.Property(o => o.FirstDocumentAt).HasColumnName("first_document_at");
        builder.Property(o => o.FirstDocumentRoute).HasColumnName("first_document_route").HasMaxLength(20);
        builder.Property(o => o.TourStepReached).HasColumnName("tour_step_reached");
        builder.Property(o => o.TourSeenAt).HasColumnName("tour_seen_at");
        builder.Property(o => o.TourRestarts).HasColumnName("tour_restarts").HasDefaultValue(0);
        builder.Property(o => o.FirstValidatePassAt).HasColumnName("first_validate_pass_at");
        builder.Property(o => o.FirstExportAt).HasColumnName("first_export_at");
        builder.Property(o => o.FollowUpSentAt).HasColumnName("follow_up_sent_at");
        builder.Property(o => o.TipsOptOut).HasColumnName("tips_opt_out").HasDefaultValue(false);
        builder.Property(o => o.TipsToken).HasColumnName("tips_token").HasMaxLength(64);
        builder.HasIndex(o => o.TipsToken).IsUnique().HasFilter("tips_token IS NOT NULL");
        builder.Property(o => o.TimeZone).HasColumnName("time_zone").HasMaxLength(64);
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(o => o.CreatedAt);

        builder.HasOne(o => o.User)
            .WithOne()
            .HasForeignKey<UserOnboarding>(o => o.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
