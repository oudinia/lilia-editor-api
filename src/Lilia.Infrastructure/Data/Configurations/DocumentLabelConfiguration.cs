using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class DocumentLabelConfiguration : IEntityTypeConfiguration<DocumentLabel>
{
    public void Configure(EntityTypeBuilder<DocumentLabel> builder)
    {
        builder.ToTable("document_labels");

        builder.HasKey(dl => new { dl.DocumentId, dl.LabelId });
        builder.Property(dl => dl.DocumentId).HasColumnName("document_id");
        builder.Property(dl => dl.LabelId).HasColumnName("label_id");

        builder.HasOne(dl => dl.Document)
            .WithMany(d => d.DocumentLabels)
            .HasForeignKey(dl => dl.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(dl => dl.Label)
            .WithMany(l => l.DocumentLabels)
            .HasForeignKey(dl => dl.LabelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
