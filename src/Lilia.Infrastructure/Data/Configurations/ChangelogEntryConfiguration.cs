using System.Text.Json;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class ChangelogEntryConfiguration : IEntityTypeConfiguration<ChangelogEntry>
{
    public void Configure(EntityTypeBuilder<ChangelogEntry> builder)
    {
        builder.ToTable("changelog_entries", t =>
        {
            t.HasCheckConstraint("ck_changelog_kind", "kind IN ('fix','feature')");
            t.HasCheckConstraint("ck_changelog_status", "status IN ('shipped','known')");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.EntryDate).HasColumnName("entry_date").IsRequired();
        builder.Property(e => e.Area).HasColumnName("area").HasMaxLength(40).IsRequired();
        builder.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(20).HasDefaultValue("fix").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("shipped").IsRequired();
        builder.Property(e => e.Verified).HasColumnName("verified").HasDefaultValue(false);
        builder.Property(e => e.ShotUrl).HasColumnName("shot_url");
        builder.Property(e => e.Sort).HasColumnName("sort").HasDefaultValue(0);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        var dictConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<Dictionary<string, string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new());

        var dictComparer = new ValueComparer<Dictionary<string, string>>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null) ?? new());

        builder.Property(e => e.Title).HasColumnName("title").HasColumnType("jsonb")
            .HasConversion(dictConverter).Metadata.SetValueComparer(dictComparer);
        builder.Property(e => e.Detail).HasColumnName("detail").HasColumnType("jsonb")
            .HasConversion(dictConverter).Metadata.SetValueComparer(dictComparer);

        // Listing order: newest date first, then Sort desc.
        builder.HasIndex(e => new { e.EntryDate, e.Sort }).HasDatabaseName("ix_changelog_date_sort");
    }
}
