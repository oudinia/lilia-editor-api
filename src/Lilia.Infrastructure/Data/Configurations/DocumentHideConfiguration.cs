using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class DocumentHideConfiguration : IEntityTypeConfiguration<DocumentHide>
{
    public void Configure(EntityTypeBuilder<DocumentHide> builder)
    {
        builder.ToTable("document_hides");

        builder.HasKey(h => new { h.UserId, h.DocumentId });
        builder.Property(h => h.UserId).HasColumnName("user_id").HasMaxLength(255);
        builder.Property(h => h.DocumentId).HasColumnName("document_id");
        builder.Property(h => h.HiddenAt).HasColumnName("hidden_at");
        builder.HasIndex(h => h.DocumentId);

        builder.HasOne(h => h.Document)
            .WithMany()
            .HasForeignKey(h => h.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
