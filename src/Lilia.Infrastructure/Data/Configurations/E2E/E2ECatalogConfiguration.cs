using Lilia.Core.Entities.E2E;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations.E2E;

// =====================================================================
//  Catalogue layer configuration. All tables live in the `e2e` schema.
//  Snake-case columns to match the existing convention.
// =====================================================================

public class E2EModuleConfiguration : IEntityTypeConfiguration<E2EModule>
{
    public void Configure(EntityTypeBuilder<E2EModule> b)
    {
        b.ToTable("module", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(64).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.Owner).HasColumnName("owner").HasMaxLength(80);
        b.Property(x => x.Criticality).HasColumnName("criticality").HasMaxLength(4).HasDefaultValue("p1");
        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => x.Slug).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("ck_e2e_module_criticality",
            "criticality IN ('p0','p1','p2')"));
    }
}

public class E2ESurfaceConfiguration : IEntityTypeConfiguration<E2ESurface>
{
    public void Configure(EntityTypeBuilder<E2ESurface> b)
    {
        b.ToTable("surface", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.ModuleId).HasColumnName("module_id").IsRequired();
        b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(80).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.SurfaceKind).HasColumnName("surface_kind").HasMaxLength(20).IsRequired();
        b.Property(x => x.RoutePattern).HasColumnName("route_pattern").HasMaxLength(200);
        b.Property(x => x.SourceFile).HasColumnName("source_file").HasMaxLength(500);
        b.Property(x => x.TestidRoot).HasColumnName("testid_root").HasMaxLength(120);
        b.Property(x => x.Criticality).HasColumnName("criticality").HasMaxLength(4).HasDefaultValue("p1");
        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.ModuleId, x.Slug }).IsUnique();

        b.HasOne(x => x.Module)
            .WithMany(m => m.Surfaces)
            .HasForeignKey(x => x.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_e2e_surface_kind",
                "surface_kind IN ('page','modal','drawer','popover','popup','sheet','dialog','inline','overlay')");
            t.HasCheckConstraint("ck_e2e_surface_criticality",
                "criticality IN ('p0','p1','p2')");
        });
    }
}

public class E2EUIElementConfiguration : IEntityTypeConfiguration<E2EUIElement>
{
    public void Configure(EntityTypeBuilder<E2EUIElement> b)
    {
        b.ToTable("ui_element", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.SurfaceId).HasColumnName("surface_id").IsRequired();
        b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(120).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.ElementKind).HasColumnName("element_kind").HasMaxLength(40).IsRequired();
        b.Property(x => x.AccessibleName).HasColumnName("accessible_name").HasMaxLength(200);
        b.Property(x => x.Role).HasColumnName("role").HasMaxLength(40);
        b.Property(x => x.VisibleText).HasColumnName("visible_text").HasMaxLength(200);
        b.Property(x => x.DefaultSelector).HasColumnName("default_selector").HasMaxLength(500);
        b.Property(x => x.ProducesBlockTypeId).HasColumnName("produces_block_type_id");
        b.Property(x => x.TriggersSurfaceId).HasColumnName("triggers_surface_id");
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.SurfaceId, x.Slug }).IsUnique();

        b.HasOne(x => x.Surface)
            .WithMany(s => s.UIElements)
            .HasForeignKey(x => x.SurfaceId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.ProducesBlockType)
            .WithMany()
            .HasForeignKey(x => x.ProducesBlockTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.TriggersSurface)
            .WithMany()
            .HasForeignKey(x => x.TriggersSurfaceId)
            .OnDelete(DeleteBehavior.SetNull);

        b.ToTable(t => t.HasCheckConstraint("ck_e2e_ui_element_kind",
            "element_kind IN ('button','icon_button','link','input','textarea','menu_item','toggle','switch','radio','checkbox','select','tab','disclosure','sheet_handle','fab')"));
    }
}

public class E2ESelectorCandidateConfiguration : IEntityTypeConfiguration<E2ESelectorCandidate>
{
    public void Configure(EntityTypeBuilder<E2ESelectorCandidate> b)
    {
        b.ToTable("selector_candidate", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.UIElementId).HasColumnName("ui_element_id").IsRequired();
        b.Property(x => x.Ordinal).HasColumnName("ordinal").HasDefaultValue(0);
        b.Property(x => x.Selector).HasColumnName("selector").HasMaxLength(500).IsRequired();
        b.Property(x => x.AccessibleName).HasColumnName("accessible_name").HasMaxLength(200);
        b.Property(x => x.Role).HasColumnName("role").HasMaxLength(40);
        b.Property(x => x.VisibleText).HasColumnName("visible_text").HasMaxLength(200);
        b.Property(x => x.TagName).HasColumnName("tag_name").HasMaxLength(40);
        b.Property(x => x.Confidence).HasColumnName("confidence").HasColumnType("numeric(3,2)").HasDefaultValue(1.0m);
        b.Property(x => x.LastMatchedAt).HasColumnName("last_matched_at");
        b.Property(x => x.LastMissedAt).HasColumnName("last_missed_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.UIElementId, x.Ordinal });

        b.HasOne(x => x.UIElement)
            .WithMany(e => e.SelectorCandidates)
            .HasForeignKey(x => x.UIElementId)
            .OnDelete(DeleteBehavior.Cascade);

        b.ToTable(t => t.HasCheckConstraint("ck_e2e_selector_confidence",
            "confidence BETWEEN 0 AND 1"));
    }
}

public class E2EBlockTypeConfiguration : IEntityTypeConfiguration<E2EBlockType>
{
    public void Configure(EntityTypeBuilder<E2EBlockType> b)
    {
        b.ToTable("block_type", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        b.Property(x => x.Category).HasColumnName("category").HasMaxLength(20).IsRequired();
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.LatexRole).HasColumnName("latex_role").HasMaxLength(40);
        b.Property(x => x.ScenarioCount).HasColumnName("scenario_count").HasDefaultValue(0);
        b.Property(x => x.LastExercisedAt).HasColumnName("last_exercised_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => x.Slug).IsUnique();

        b.ToTable(t => t.HasCheckConstraint("ck_e2e_block_type_category",
            "category IN ('text','structure','media','code','reference','math')"));
    }
}

public class E2EBlockActionConfiguration : IEntityTypeConfiguration<E2EBlockAction>
{
    public void Configure(EntityTypeBuilder<E2EBlockAction> b)
    {
        b.ToTable("block_action", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(60).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.ExpectedSurfaceKind).HasColumnName("expected_surface_kind").HasMaxLength(20);

        b.HasIndex(x => x.Slug).IsUnique();
    }
}

public class E2EEntryPointConfiguration : IEntityTypeConfiguration<E2EEntryPoint>
{
    public void Configure(EntityTypeBuilder<E2EEntryPoint> b)
    {
        b.ToTable("entry_point", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.TargetSurfaceId).HasColumnName("target_surface_id").IsRequired();
        b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(80).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(200).IsRequired();
        b.Property(x => x.OpenerKind).HasColumnName("opener_kind").HasMaxLength(40).IsRequired();
        b.Property(x => x.OpenerElementId).HasColumnName("opener_element_id");
        b.Property(x => x.ShortcutKeys).HasColumnName("shortcut_keys").HasMaxLength(40);
        b.Property(x => x.Criticality).HasColumnName("criticality").HasMaxLength(4).HasDefaultValue("p1");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.TargetSurfaceId, x.Slug }).IsUnique();

        b.HasOne(x => x.TargetSurface)
            .WithMany(s => s.EntryPoints)
            .HasForeignKey(x => x.TargetSurfaceId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.OpenerElement)
            .WithMany()
            .HasForeignKey(x => x.OpenerElementId)
            .OnDelete(DeleteBehavior.SetNull);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_e2e_entry_point_opener_kind",
                "opener_kind IN ('toolbar_button','command_palette','keyboard_shortcut','url_state','context_menu','right_click','auto_open','deep_link','direct_mount','drag_drop','long_press')");
            t.HasCheckConstraint("ck_e2e_entry_point_criticality",
                "criticality IN ('p0','p1','p2')");
        });
    }
}
