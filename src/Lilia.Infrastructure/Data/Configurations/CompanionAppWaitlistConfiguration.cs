using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class CompanionAppWaitlistConfiguration : IEntityTypeConfiguration<CompanionAppWaitlist>
{
    public void Configure(EntityTypeBuilder<CompanionAppWaitlist> b)
    {
        b.ToTable("companion_app_waitlist");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(120);
        b.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        b.Property(x => x.Locale).HasColumnName("locale").HasMaxLength(10).HasDefaultValue("en");
        b.Property(x => x.UserAgent).HasColumnName("user_agent").HasMaxLength(500);
        b.Property(x => x.Source).HasColumnName("source").HasMaxLength(40).HasDefaultValue("banner");
        b.Property(x => x.SignedUpAt).HasColumnName("signed_up_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.NotifiedAt).HasColumnName("notified_at");
        b.Property(x => x.UnsubscribedAt).HasColumnName("unsubscribed_at");

        // Citext-style case-insensitive uniqueness via lower(email) on
        // a partial unique index — done in raw SQL in the migration so
        // EF doesn't try to enforce it through its model.
        b.HasIndex(x => x.Email);
        b.HasIndex(x => x.SignedUpAt).IsDescending();
    }
}
