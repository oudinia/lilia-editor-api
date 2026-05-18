using Lilia.Core.Entities.E2E;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations.E2E;

// =====================================================================
//  Scenario layer configuration. Versioned snapshots + step rows +
//  tags + coverage links.
// =====================================================================

public class E2EScenarioConfiguration : IEntityTypeConfiguration<E2EScenario>
{
    public void Configure(EntityTypeBuilder<E2EScenario> b)
    {
        b.ToTable("scenario", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(200).IsRequired();
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.ModuleId).HasColumnName("module_id").IsRequired();
        b.Property(x => x.TargetSurfaceId).HasColumnName("target_surface_id");
        b.Property(x => x.EntryPointId).HasColumnName("entry_point_id");

        b.Property(x => x.Criticality).HasColumnName("criticality").HasMaxLength(4).HasDefaultValue("p1");
        b.Property(x => x.DetailLevel).HasColumnName("detail_level").HasMaxLength(4).HasDefaultValue("l1");
        b.Property(x => x.ReviewState).HasColumnName("review_state").HasMaxLength(20).HasDefaultValue("draft");
        b.Property(x => x.ExecutionMode).HasColumnName("execution_mode").HasMaxLength(20).HasDefaultValue("integration");
        b.Property(x => x.Template).HasColumnName("template").HasMaxLength(20).HasDefaultValue("standard");

        b.Property(x => x.AutomationContent).HasColumnName("automation_content").HasMaxLength(64).IsRequired();
        b.Property(x => x.EstimateSeconds).HasColumnName("estimate_seconds");
        b.Property(x => x.EstimateForecastSeconds).HasColumnName("estimate_forecast_seconds");
        b.Property(x => x.Milestone).HasColumnName("milestone").HasMaxLength(80);

        b.Property(x => x.ExploratoryMission).HasColumnName("exploratory_mission");
        b.Property(x => x.ExploratoryGoals).HasColumnName("exploratory_goals");

        b.Property(x => x.CurrentVersionId).HasColumnName("current_version_id");

        b.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        b.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(120);
        b.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasMaxLength(120);

        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => x.AutomationContent).IsUnique();
        b.HasIndex(x => x.ModuleId);
        b.HasIndex(x => x.TargetSurfaceId);
        b.HasIndex(x => x.EntryPointId);
        b.HasIndex(x => x.ReviewState);
        b.HasIndex(x => x.Criticality);

        b.HasOne(x => x.Module)
            .WithMany(m => m.Scenarios)
            .HasForeignKey(x => x.ModuleId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.TargetSurface)
            .WithMany(s => s.Scenarios)
            .HasForeignKey(x => x.TargetSurfaceId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.EntryPoint)
            .WithMany()
            .HasForeignKey(x => x.EntryPointId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.CurrentVersion)
            .WithMany()
            .HasForeignKey(x => x.CurrentVersionId)
            .OnDelete(DeleteBehavior.SetNull);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_e2e_scenario_criticality",
                "criticality IN ('p0','p1','p2')");
            t.HasCheckConstraint("ck_e2e_scenario_detail_level",
                "detail_level IN ('l1','l2','l3')");
            t.HasCheckConstraint("ck_e2e_scenario_review_state",
                "review_state IN ('draft','approved','quarantined','deprecated')");
            t.HasCheckConstraint("ck_e2e_scenario_execution_mode",
                "execution_mode IN ('integration','component')");
            t.HasCheckConstraint("ck_e2e_scenario_template",
                "template IN ('standard','exploratory','parametrised','accessibility')");
        });
    }
}

public class E2EScenarioVersionConfiguration : IEntityTypeConfiguration<E2EScenarioVersion>
{
    public void Configure(EntityTypeBuilder<E2EScenarioVersion> b)
    {
        b.ToTable("scenario_version", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.ScenarioId).HasColumnName("scenario_id").IsRequired();
        b.Property(x => x.VersionNumber).HasColumnName("version_number").IsRequired();
        b.Property(x => x.DetailLevel).HasColumnName("detail_level").HasMaxLength(4).IsRequired();
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.Preconditions).HasColumnName("preconditions").HasColumnType("jsonb");
        b.Property(x => x.Steps).HasColumnName("steps").HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        b.Property(x => x.ExpectedOutcomes).HasColumnName("expected_outcomes").HasColumnType("jsonb");
        b.Property(x => x.GenerationProvenance).HasColumnName("generation_provenance").HasMaxLength(40).HasDefaultValue("human");
        b.Property(x => x.GeneratedBy).HasColumnName("generated_by").HasMaxLength(120);
        b.Property(x => x.ParentVersionId).HasColumnName("parent_version_id");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.ScenarioId, x.VersionNumber }).IsUnique();

        b.HasOne(x => x.Scenario)
            .WithMany(s => s.Versions)
            .HasForeignKey(x => x.ScenarioId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.ParentVersion)
            .WithMany()
            .HasForeignKey(x => x.ParentVersionId)
            .OnDelete(DeleteBehavior.SetNull);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_e2e_scenario_version_detail_level",
                "detail_level IN ('l1','l2','l3')");
            t.HasCheckConstraint("ck_e2e_scenario_version_provenance",
                "generation_provenance IN ('human','llm_draft','llm_repaired','imported','session_recording')");
        });
    }
}

public class E2EScenarioStepConfiguration : IEntityTypeConfiguration<E2EScenarioStep>
{
    public void Configure(EntityTypeBuilder<E2EScenarioStep> b)
    {
        b.ToTable("scenario_step", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.ScenarioVersionId).HasColumnName("scenario_version_id").IsRequired();
        b.Property(x => x.SortOrder).HasColumnName("sort_order").IsRequired();
        b.Property(x => x.StepKind).HasColumnName("step_kind").HasMaxLength(20).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        b.Property(x => x.TargetUIElementId).HasColumnName("target_ui_element_id");
        b.Property(x => x.ActionKind).HasColumnName("action_kind").HasMaxLength(40);
        b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
        b.Property(x => x.TechnicalAssertion).HasColumnName("technical_assertion");
        b.Property(x => x.UserVisibleOutcome).HasColumnName("user_visible_outcome");
        b.Property(x => x.SharedStepId).HasColumnName("shared_step_id");
        b.Property(x => x.AdditionalInfo).HasColumnName("additional_info");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.ScenarioVersionId, x.SortOrder });
        b.HasIndex(x => x.TargetUIElementId);

        b.HasOne(x => x.ScenarioVersion)
            .WithMany(v => v.StepRows)
            .HasForeignKey(x => x.ScenarioVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.TargetUIElement)
            .WithMany()
            .HasForeignKey(x => x.TargetUIElementId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.SharedStep)
            .WithMany()
            .HasForeignKey(x => x.SharedStepId)
            .OnDelete(DeleteBehavior.SetNull);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_e2e_step_kind",
                "step_kind IN ('setup','action','wait','assert','teardown')");
            t.HasCheckConstraint("ck_e2e_step_action_kind",
                "action_kind IS NULL OR action_kind IN ('click','dblclick','type','press','select','check','uncheck','focus','blur','hover','drag','drop','navigate','wait_for','expect')");
        });
    }
}

public class E2ETagConfiguration : IEntityTypeConfiguration<E2ETag>
{
    public void Configure(EntityTypeBuilder<E2ETag> b)
    {
        b.ToTable("tag", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(80).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.Color).HasColumnName("color").HasMaxLength(20);

        b.HasIndex(x => x.Slug).IsUnique();
    }
}

public class E2EScenarioTagConfiguration : IEntityTypeConfiguration<E2EScenarioTag>
{
    public void Configure(EntityTypeBuilder<E2EScenarioTag> b)
    {
        b.ToTable("scenario_tag", "e2e");
        b.HasKey(x => new { x.ScenarioId, x.TagId });
        b.Property(x => x.ScenarioId).HasColumnName("scenario_id");
        b.Property(x => x.TagId).HasColumnName("tag_id");

        b.HasOne(x => x.Scenario)
            .WithMany(s => s.Tags)
            .HasForeignKey(x => x.ScenarioId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Tag)
            .WithMany()
            .HasForeignKey(x => x.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class E2EScenarioCoverageLinkConfiguration : IEntityTypeConfiguration<E2EScenarioCoverageLink>
{
    public void Configure(EntityTypeBuilder<E2EScenarioCoverageLink> b)
    {
        b.ToTable("scenario_coverage_link", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.ScenarioId).HasColumnName("scenario_id").IsRequired();
        b.Property(x => x.Layer).HasColumnName("layer").HasMaxLength(4).IsRequired();
        b.Property(x => x.TargetKind).HasColumnName("target_kind").HasMaxLength(20).IsRequired();
        b.Property(x => x.TargetId).HasColumnName("target_id").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.ScenarioId, x.Layer, x.TargetKind, x.TargetId }).IsUnique();
        b.HasIndex(x => new { x.TargetKind, x.TargetId });

        b.HasOne(x => x.Scenario)
            .WithMany(s => s.CoverageLinks)
            .HasForeignKey(x => x.ScenarioId)
            .OnDelete(DeleteBehavior.Cascade);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_e2e_coverage_layer",
                "layer IN ('l1','l2','l3')");
            t.HasCheckConstraint("ck_e2e_coverage_target_kind",
                "target_kind IN ('ui_element','block_type','module','surface','entry_point')");
        });
    }
}
