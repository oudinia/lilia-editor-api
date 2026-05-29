using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class DiagnosticCaptureConfiguration : IEntityTypeConfiguration<DiagnosticCapture>
{
    public void Configure(EntityTypeBuilder<DiagnosticCapture> builder)
    {
        builder.ToTable("diagnostic_captures");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(c => c.UserId).HasColumnName("user_id").HasMaxLength(255);
        builder.Property(c => c.RefToken).HasColumnName("ref_token").HasMaxLength(32).IsRequired();
        builder.Property(c => c.Source).HasColumnName("source").HasMaxLength(64).IsRequired();
        builder.Property(c => c.Note).HasColumnName("note").HasMaxLength(200);
        builder.Property(c => c.UserAgent).HasColumnName("user_agent").HasMaxLength(500);
        builder.Property(c => c.Url).HasColumnName("url").HasMaxLength(500);
        // Payload is the opaque JSON bundle from the client. jsonb so
        // ad-hoc admin queries (e.g. `payload->'sync'->>'state'`)
        // don't need an application-side parse.
        builder.Property(c => c.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        // RefToken must be unique so the human-friendly identifier
        // can serve as the primary lookup key.
        builder.HasIndex(c => c.RefToken).IsUnique();
        // UserId index supports the "show me my captures" filter
        // (the only place ordinary users can read).
        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => c.CreatedAt);
    }
}
