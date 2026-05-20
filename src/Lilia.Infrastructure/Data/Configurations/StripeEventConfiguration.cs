using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class StripeEventConfiguration : IEntityTypeConfiguration<StripeEvent>
{
    public void Configure(EntityTypeBuilder<StripeEvent> builder)
    {
        builder.ToTable("stripe_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.StripeEventId).HasColumnName("stripe_event_id").HasMaxLength(64).IsRequired();
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(e => e.ReceivedAt).HasColumnName("received_at").HasDefaultValueSql("NOW()");
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        builder.Property(e => e.Error).HasColumnName("error");

        // Webhook dedup: Stripe delivers at-least-once → one row per event.
        builder.HasIndex(e => e.StripeEventId).IsUnique().HasDatabaseName("ux_stripe_event_id");
    }
}
