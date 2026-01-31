using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class UserPreferencesConfiguration : IEntityTypeConfiguration<UserPreferences>
{
    public void Configure(EntityTypeBuilder<UserPreferences> builder)
    {
        builder.ToTable("user_preferences");

        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).HasColumnName("user_id").HasMaxLength(255);
        builder.Property(p => p.Theme).HasColumnName("theme").HasMaxLength(20).HasDefaultValue("system");
        builder.Property(p => p.DefaultFontFamily).HasColumnName("default_font_family").HasMaxLength(100);
        builder.Property(p => p.DefaultFontSize).HasColumnName("default_font_size");
        builder.Property(p => p.DefaultPaperSize).HasColumnName("default_paper_size").HasMaxLength(50);
        builder.Property(p => p.AutoSaveEnabled).HasColumnName("auto_save_enabled").HasDefaultValue(true);
        builder.Property(p => p.AutoSaveInterval).HasColumnName("auto_save_interval").HasDefaultValue(2000);
        builder.Property(p => p.KeyboardShortcuts).HasColumnName("keyboard_shortcuts").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasOne(p => p.User)
            .WithOne(u => u.Preferences)
            .HasForeignKey<UserPreferences>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
