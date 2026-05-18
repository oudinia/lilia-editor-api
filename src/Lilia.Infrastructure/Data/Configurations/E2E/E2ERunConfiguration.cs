using Lilia.Core.Entities.E2E;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations.E2E;

// =====================================================================
//  Run-time layer: test runs, scenario results, UI interaction events
//  (test + real-user), nightly health rollup, insight queue.
// =====================================================================

public class E2ETestRunConfiguration : IEntityTypeConfiguration<E2ETestRun>
{
    public void Configure(EntityTypeBuilder<E2ETestRun> b)
    {
        b.ToTable("test_run", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.StartedAt).HasColumnName("started_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.EndedAt).HasColumnName("ended_at");
        b.Property(x => x.TriggerKind).HasColumnName("trigger_kind").HasMaxLength(20).HasDefaultValue("manual");
        b.Property(x => x.Branch).HasColumnName("branch").HasMaxLength(200);
        b.Property(x => x.CommitSha).HasColumnName("commit_sha").HasMaxLength(64);
        b.Property(x => x.Environment).HasColumnName("environment").HasColumnType("jsonb");
        b.Property(x => x.Total).HasColumnName("total").HasDefaultValue(0);
        b.Property(x => x.Passed).HasColumnName("passed").HasDefaultValue(0);
        b.Property(x => x.Failed).HasColumnName("failed").HasDefaultValue(0);
        b.Property(x => x.Skipped).HasColumnName("skipped").HasDefaultValue(0);
        b.Property(x => x.DurationMs).HasColumnName("duration_ms");
        b.Property(x => x.ReporterMeta).HasColumnName("reporter_meta").HasColumnType("jsonb");

        b.HasIndex(x => x.StartedAt);

        b.ToTable(t => t.HasCheckConstraint("ck_e2e_run_trigger_kind",
            "trigger_kind IN ('ci','manual','scheduled','hosted')"));
    }
}

public class E2EScenarioResultConfiguration : IEntityTypeConfiguration<E2EScenarioResult>
{
    public void Configure(EntityTypeBuilder<E2EScenarioResult> b)
    {
        b.ToTable("scenario_result", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.TestRunId).HasColumnName("test_run_id").IsRequired();
        b.Property(x => x.ScenarioId).HasColumnName("scenario_id").IsRequired();
        b.Property(x => x.ScenarioVersionId).HasColumnName("scenario_version_id").IsRequired();
        b.Property(x => x.DetailLevelRun).HasColumnName("detail_level_run").HasMaxLength(4).IsRequired();

        b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(20).IsRequired();
        b.Property(x => x.DurationMs).HasColumnName("duration_ms").HasDefaultValue(0);

        b.Property(x => x.Browser).HasColumnName("browser").HasMaxLength(40);
        b.Property(x => x.BrowserVersion).HasColumnName("browser_version").HasMaxLength(40);
        b.Property(x => x.ViewportW).HasColumnName("viewport_w");
        b.Property(x => x.ViewportH).HasColumnName("viewport_h");
        b.Property(x => x.Locale).HasColumnName("locale").HasMaxLength(20);
        b.Property(x => x.FeatureFlags).HasColumnName("feature_flags").HasColumnType("jsonb");
        b.Property(x => x.BackendBuildSha).HasColumnName("backend_build_sha").HasMaxLength(64);
        b.Property(x => x.TestOrderIndex).HasColumnName("test_order_index");
        b.Property(x => x.PriorSessionResidueDetected).HasColumnName("prior_session_residue_detected");

        b.Property(x => x.FailureKind).HasColumnName("failure_kind").HasMaxLength(40);
        b.Property(x => x.FailureMessage).HasColumnName("failure_message");
        b.Property(x => x.FailureStack).HasColumnName("failure_stack");

        b.Property(x => x.IsRetryPass).HasColumnName("is_retry_pass").HasDefaultValue(false);
        b.Property(x => x.RetryOfId).HasColumnName("retry_of_id");
        b.Property(x => x.RetryAttempt).HasColumnName("retry_attempt").HasDefaultValue(0);
        b.Property(x => x.Parametrisation).HasColumnName("parametrisation").HasColumnType("jsonb");

        b.Property(x => x.TracePath).HasColumnName("trace_path").HasMaxLength(500);
        b.Property(x => x.ScreenshotPath).HasColumnName("screenshot_path").HasMaxLength(500);
        b.Property(x => x.VideoPath).HasColumnName("video_path").HasMaxLength(500);
        b.Property(x => x.DurationZScore).HasColumnName("duration_z_score").HasColumnType("numeric(6,3)");
        b.Property(x => x.ScreenshotDiffIsolated).HasColumnName("screenshot_diff_isolated");

        b.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => x.ScenarioId);
        b.HasIndex(x => x.TestRunId);
        b.HasIndex(x => x.Outcome);
        b.HasIndex(x => x.RecordedAt).IsDescending();

        b.HasOne(x => x.TestRun)
            .WithMany(r => r.Results)
            .HasForeignKey(x => x.TestRunId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Scenario)
            .WithMany(s => s.Results)
            .HasForeignKey(x => x.ScenarioId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ScenarioVersion)
            .WithMany()
            .HasForeignKey(x => x.ScenarioVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.RetryOf)
            .WithMany()
            .HasForeignKey(x => x.RetryOfId)
            .OnDelete(DeleteBehavior.SetNull);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_e2e_result_detail_level",
                "detail_level_run IN ('l1','l2','l3')");
            t.HasCheckConstraint("ck_e2e_result_outcome",
                "outcome IN ('pass','fail','skip','timed_out','interrupted')");
            t.HasCheckConstraint("ck_e2e_result_failure_kind",
                "failure_kind IS NULL OR failure_kind IN ('selector_not_found','timeout_navigation','timeout_visibility','assertion_failed','navigation_failed','infrastructure_error','visual_regression','console_error')");
        });
    }
}

public class E2EUIInteractionEventConfiguration : IEntityTypeConfiguration<E2EUIInteractionEvent>
{
    public void Configure(EntityTypeBuilder<E2EUIInteractionEvent> b)
    {
        b.ToTable("ui_interaction_event", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
        b.Property(x => x.ScenarioResultId).HasColumnName("scenario_result_id");
        b.Property(x => x.RealUserSessionHash).HasColumnName("real_user_session_hash").HasMaxLength(32);
        b.Property(x => x.UIElementId).HasColumnName("ui_element_id");
        b.Property(x => x.SurfaceId).HasColumnName("surface_id");
        b.Property(x => x.InteractionKind).HasColumnName("interaction_kind").HasMaxLength(20).IsRequired();
        b.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");

        b.HasIndex(x => x.UIElementId);
        b.HasIndex(x => x.SurfaceId);
        b.HasIndex(x => new { x.Source, x.RealUserSessionHash });
        b.HasIndex(x => x.OccurredAt).IsDescending();

        b.HasOne(x => x.ScenarioResult)
            .WithMany(r => r.InteractionEvents)
            .HasForeignKey(x => x.ScenarioResultId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.UIElement)
            .WithMany()
            .HasForeignKey(x => x.UIElementId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Surface)
            .WithMany()
            .HasForeignKey(x => x.SurfaceId)
            .OnDelete(DeleteBehavior.SetNull);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_e2e_uie_source",
                "source IN ('test','real_user')");
            t.HasCheckConstraint("ck_e2e_uie_interaction_kind",
                "interaction_kind IN ('click','dblclick','type','focus','select','navigate','submit','drag','hover','keypress')");
        });
    }
}

public class E2EScenarioHealthStateConfiguration : IEntityTypeConfiguration<E2EScenarioHealthState>
{
    public void Configure(EntityTypeBuilder<E2EScenarioHealthState> b)
    {
        b.ToTable("scenario_health_state", "e2e");
        b.HasKey(x => x.ScenarioId);
        b.Property(x => x.ScenarioId).HasColumnName("scenario_id");
        b.Property(x => x.FlakeScore).HasColumnName("flake_score").HasColumnType("numeric(4,3)").HasDefaultValue(0m);
        b.Property(x => x.HealthState).HasColumnName("health_state").HasMaxLength(40).HasDefaultValue("green");
        b.Property(x => x.StateChangedAt).HasColumnName("state_changed_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.ComputedAt).HasColumnName("computed_at").HasDefaultValueSql("NOW()");

        b.HasOne(x => x.Scenario)
            .WithMany()
            .HasForeignKey(x => x.ScenarioId)
            .OnDelete(DeleteBehavior.Cascade);

        b.ToTable(t => t.HasCheckConstraint("ck_e2e_health_state",
            "health_state IN ('green','watching','quarantined','investigated_as_bug')"));
    }
}

public class E2EScenarioInsightConfiguration : IEntityTypeConfiguration<E2EScenarioInsight>
{
    public void Configure(EntityTypeBuilder<E2EScenarioInsight> b)
    {
        b.ToTable("scenario_insight", "e2e");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.ScenarioId).HasColumnName("scenario_id").IsRequired();
        b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(40).IsRequired();
        b.Property(x => x.Confidence).HasColumnName("confidence").HasColumnType("numeric(3,2)").HasDefaultValue(0m);
        b.Property(x => x.AutoApplyEligible).HasColumnName("auto_apply_eligible").HasDefaultValue(false);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("open");
        b.Property(x => x.SuggestedChange).HasColumnName("suggested_change").HasColumnType("jsonb");
        b.Property(x => x.EvidenceRunIds).HasColumnName("evidence_run_ids").HasColumnType("uuid[]");
        b.Property(x => x.PriorVersionId).HasColumnName("prior_version_id");
        b.Property(x => x.AppliedThenUncoveredBug).HasColumnName("applied_then_uncovered_bug").HasDefaultValue(false);
        b.Property(x => x.Message).HasColumnName("message").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(120).HasDefaultValue("claude");
        b.Property(x => x.AppliedAt).HasColumnName("applied_at");
        b.Property(x => x.AppliedBy).HasColumnName("applied_by").HasMaxLength(120);
        b.Property(x => x.RolledBackAt).HasColumnName("rolled_back_at");
        b.Property(x => x.RolledBackBy).HasColumnName("rolled_back_by").HasMaxLength(120);

        b.HasIndex(x => x.ScenarioId);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.Kind);

        b.HasOne(x => x.Scenario)
            .WithMany(s => s.Insights)
            .HasForeignKey(x => x.ScenarioId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.PriorVersion)
            .WithMany()
            .HasForeignKey(x => x.PriorVersionId)
            .OnDelete(DeleteBehavior.SetNull);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_e2e_insight_kind",
                "kind IN ('selector_drift','behavioral_drift','flake_cluster','coverage_gap','promotion_proposal','probable_product_bug','duplicate_scenario','ghost_element')");
            t.HasCheckConstraint("ck_e2e_insight_status",
                "status IN ('open','pending_review','applied','dismissed','rolled_back')");
            t.HasCheckConstraint("ck_e2e_insight_confidence",
                "confidence BETWEEN 0 AND 1");
        });
    }
}
