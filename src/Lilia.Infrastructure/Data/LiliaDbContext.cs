using Lilia.Core.Entities;
using Lilia.Core.Entities.E2E;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Infrastructure.Data;

public class LiliaDbContext : DbContext
{
    public LiliaDbContext(DbContextOptions<LiliaDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Team> Teams => Set<Team>();
    // TeamMembers DbSet removed when team_members table was
    // retired (RetireTeamMembersTable migration). GroupMembers
    // is the single source of truth now.
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<BlockGroup> BlockGroups => Set<BlockGroup>();
    public DbSet<BlockGroupMembership> BlockGroupMemberships => Set<BlockGroupMembership>();
    public DbSet<BibliographyEntry> BibliographyEntries => Set<BibliographyEntry>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<DocumentLabel> DocumentLabels => Set<DocumentLabel>();
    public DbSet<DocumentCollaborator> DocumentCollaborators => Set<DocumentCollaborator>();
    public DbSet<DocumentGroup> DocumentGroups => Set<DocumentGroup>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    // Templates are now documents with is_template = true — no separate table
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Formula> Formulas => Set<Formula>();
    public DbSet<Snippet> Snippets => Set<Snippet>();
    public DbSet<DraftBlock> DraftBlocks => Set<DraftBlock>();

    // Auth tables (Better Auth)
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Passkey> Passkeys => Set<Passkey>();
    public DbSet<Verification> Verifications => Set<Verification>();
    public DbSet<TwoFactor> TwoFactors => Set<TwoFactor>();

    // Organization & billing
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<AiChat> AiChats => Set<AiChat>();

    // Document features
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<CommentReply> CommentReplies => Set<CommentReply>();
    public DbSet<DocumentSnapshot> DocumentSnapshots => Set<DocumentSnapshot>();
    public DbSet<ConversionAudit> ConversionAudits => Set<ConversionAudit>();
    public DbSet<SyncHistory> SyncHistories => Set<SyncHistory>();

    // Audit
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // LaTeX compilation telemetry
    public DbSet<LaTeXCompilationEvent> LaTeXCompilationEvents => Set<LaTeXCompilationEvent>();

    // Notifications
    public DbSet<Notification> Notifications => Set<Notification>();

    // Pending invites
    public DbSet<DocumentPendingInvite> DocumentPendingInvites => Set<DocumentPendingInvite>();

    // Block Studio
    public DbSet<BlockPreview> BlockPreviews => Set<BlockPreview>();
    public DbSet<StudioSession> StudioSessions => Set<StudioSession>();

    // Feedback
    public DbSet<Feedback> Feedback => Set<Feedback>();

    // Import review
    public DbSet<ImportDefinition> ImportDefinitions => Set<ImportDefinition>();
    public DbSet<ImportReviewSession> ImportReviewSessions => Set<ImportReviewSession>();
    public DbSet<ImportBlockReview> ImportBlockReviews => Set<ImportBlockReview>();

    // Mirror realm (FT-IMP-001 stages 6 + 7) — rev_* tables coexist with
    // the legacy import_block_reviews during migration. Stage 8
    // (idempotent checkout) wires finalize against these.
    public DbSet<RevDocument> RevDocuments => Set<RevDocument>();
    public DbSet<RevBlock> RevBlocks => Set<RevBlock>();
    public DbSet<RevAsset> RevAssets => Set<RevAsset>();
    public DbSet<RevBibliographyEntry> RevBibliographyEntries => Set<RevBibliographyEntry>();
    public DbSet<ImportArchiveStats> ImportArchiveStats => Set<ImportArchiveStats>();
    public DbSet<ImportReviewCollaborator> ImportReviewCollaborators => Set<ImportReviewCollaborator>();
    public DbSet<ImportBlockComment> ImportBlockComments => Set<ImportBlockComment>();
    public DbSet<ImportReviewActivity> ImportReviewActivities => Set<ImportReviewActivity>();
    public DbSet<ImportDiagnostic> ImportDiagnostics => Set<ImportDiagnostic>();
    public DbSet<ImportStructuralFinding> ImportStructuralFindings => Set<ImportStructuralFinding>();
    public DbSet<ImportTelemetryEvent> ImportTelemetryEvents => Set<ImportTelemetryEvent>();

    public DbSet<LatexInsertionEvent> LatexInsertionEvents => Set<LatexInsertionEvent>();
    public DbSet<BlockValidation> BlockValidations => Set<BlockValidation>();
    public DbSet<AiRequest> AiRequests => Set<AiRequest>();

    // Entitlement / #59 — entity shells registered here; middleware + quota
    // enforcement lands in a follow-up commit once the XeLaTeX Docker deploy
    // is confirmed ACTIVE (DO supersede race, see memory).
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<UserPlan> UserPlans => Set<UserPlan>();
    public DbSet<AiCreditLedger> AiCreditLedger => Set<AiCreditLedger>();

    // LaTeX catalog (v1, Phase 1). The parser consults these tables at
    // import time to decide how to handle each token; unknown tokens
    // auto-insert with coverage_level='unsupported' so we accumulate
    // observability on what users throw at us. Seeded via migration.
    public DbSet<LatexPackage> LatexPackages => Set<LatexPackage>();
    public DbSet<LatexToken> LatexTokens => Set<LatexToken>();
    public DbSet<LatexDocumentClass> LatexDocumentClasses => Set<LatexDocumentClass>();
    public DbSet<LatexTokenUsage> LatexTokenUsages => Set<LatexTokenUsage>();

    // Typst translation catalog — parallel to the LaTeX side. Captures
    // the LaTeX → Typst translation rules we ship in TypstExportService
    // plus the known gaps that force a silent fallback to pdflatex.
    // Joined to import_telemetry_events at report time for coverage.
    public DbSet<TypstTranslationHandler> TypstTranslationHandlers => Set<TypstTranslationHandler>();
    public DbSet<TypstTranslationGap> TypstTranslationGaps => Set<TypstTranslationGap>();

    // E2E scenario database — see lilia-docs/launch-readiness/
    // 2026-05-18-e2e-scenario-db.md for the design.
    // Catalogue layer.
    public DbSet<E2EModule> E2EModules => Set<E2EModule>();
    public DbSet<E2ESurface> E2ESurfaces => Set<E2ESurface>();
    public DbSet<E2EUIElement> E2EUIElements => Set<E2EUIElement>();
    public DbSet<E2ESelectorCandidate> E2ESelectorCandidates => Set<E2ESelectorCandidate>();
    public DbSet<E2EBlockType> E2EBlockTypes => Set<E2EBlockType>();
    public DbSet<E2EBlockAction> E2EBlockActions => Set<E2EBlockAction>();
    public DbSet<E2EEntryPoint> E2EEntryPoints => Set<E2EEntryPoint>();
    // Scenario layer.
    public DbSet<E2EScenario> E2EScenarios => Set<E2EScenario>();
    public DbSet<E2EScenarioVersion> E2EScenarioVersions => Set<E2EScenarioVersion>();
    public DbSet<E2EScenarioStep> E2EScenarioSteps => Set<E2EScenarioStep>();
    public DbSet<E2ETag> E2ETags => Set<E2ETag>();
    public DbSet<E2EScenarioTag> E2EScenarioTags => Set<E2EScenarioTag>();
    public DbSet<E2EScenarioCoverageLink> E2EScenarioCoverageLinks => Set<E2EScenarioCoverageLink>();
    // Run-time layer.
    public DbSet<E2ETestRun> E2ETestRuns => Set<E2ETestRun>();
    public DbSet<E2EScenarioResult> E2EScenarioResults => Set<E2EScenarioResult>();
    public DbSet<E2EUIInteractionEvent> E2EUIInteractionEvents => Set<E2EUIInteractionEvent>();
    public DbSet<E2EScenarioHealthState> E2EScenarioHealthStates => Set<E2EScenarioHealthState>();
    public DbSet<E2EScenarioInsight> E2EScenarioInsights => Set<E2EScenarioInsight>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // --- LaTeX catalog ---
        modelBuilder.Entity<LatexPackage>(e =>
        {
            e.ToTable("latex_packages", t =>
            {
                t.HasCheckConstraint("ck_latex_package_coverage", "coverage_level IN ('full','partial','shimmed','none','unsupported')");
                t.HasCheckConstraint("ck_latex_package_category", "category IN ('math','graphics','bibliography','layout','language','font','cv','presentation','code','table','reference','utility')");
            });
            e.HasKey(x => x.Slug);
            e.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(80);
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(40).IsRequired();
            e.Property(x => x.CoverageLevel).HasColumnName("coverage_level").HasMaxLength(20).IsRequired();
            e.Property(x => x.CoverageNotes).HasColumnName("coverage_notes");
            e.Property(x => x.CtanUrl).HasColumnName("ctan_url").HasMaxLength(500);
            e.Property(x => x.Version).HasColumnName("version").HasMaxLength(40);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.Category).HasDatabaseName("ix_latex_package_category");
            e.HasIndex(x => x.CoverageLevel).HasDatabaseName("ix_latex_package_coverage");
        });

        modelBuilder.Entity<LatexToken>(e =>
        {
            e.ToTable("latex_tokens", t =>
            {
                t.HasCheckConstraint("ck_latex_token_kind", "kind IN ('command','environment','declaration','length','counter')");
                t.HasCheckConstraint("ck_latex_token_coverage", "coverage_level IN ('full','partial','shimmed','none','unsupported')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            e.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
            e.Property(x => x.PackageSlug).HasColumnName("package_slug").HasMaxLength(80);
            e.Property(x => x.Arity).HasColumnName("arity");
            e.Property(x => x.OptionalArity).HasColumnName("optional_arity");
            e.Property(x => x.ExpectsBody).HasColumnName("expects_body").HasDefaultValue(false);
            e.Property(x => x.SemanticCategory).HasColumnName("semantic_category").HasMaxLength(40);
            e.Property(x => x.MapsToBlockType).HasColumnName("maps_to_block_type").HasMaxLength(40);
            e.Property(x => x.CoverageLevel).HasColumnName("coverage_level").HasMaxLength(20).IsRequired();
            e.Property(x => x.HandlerKind).HasColumnName("handler_kind").HasMaxLength(40);
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.InsertTemplate).HasColumnName("insert_template");
            e.Property(x => x.AliasOf).HasColumnName("alias_of");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.Package).WithMany(p => p.Tokens).HasForeignKey(x => x.PackageSlug).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Alias).WithMany().HasForeignKey(x => x.AliasOf).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.Name, x.Kind, x.PackageSlug }).IsUnique().HasDatabaseName("ux_latex_token_name_kind_pkg");
            e.HasIndex(x => x.Name).HasDatabaseName("ix_latex_token_name");
            e.HasIndex(x => x.CoverageLevel).HasDatabaseName("ix_latex_token_coverage");
            e.HasIndex(x => x.PackageSlug).HasDatabaseName("ix_latex_token_package").HasFilter("package_slug IS NOT NULL");
        });

        modelBuilder.Entity<LatexDocumentClass>(e =>
        {
            e.ToTable("latex_document_classes", t =>
            {
                t.HasCheckConstraint("ck_latex_class_category", "category IN ('cv','article','report','book','presentation','letter','memoir','other')");
                t.HasCheckConstraint("ck_latex_class_coverage", "coverage_level IN ('full','partial','shimmed','none','unsupported')");
                t.HasCheckConstraint("ck_latex_class_engine", "default_engine IS NULL OR default_engine IN ('pdflatex','xelatex','lualatex')");
            });
            e.HasKey(x => x.Slug);
            e.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(80);
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(40).IsRequired();
            e.Property(x => x.CoverageLevel).HasColumnName("coverage_level").HasMaxLength(20).IsRequired();
            e.Property(x => x.DefaultEngine).HasColumnName("default_engine").HasMaxLength(20);
            e.Property(x => x.RequiredPackages).HasColumnName("required_packages").HasColumnType("jsonb");
            e.Property(x => x.ShimName).HasColumnName("shim_name").HasMaxLength(80);
            e.Property(x => x.AllowedBlocks).HasColumnName("allowed_blocks").HasColumnType("jsonb");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.Category).HasDatabaseName("ix_latex_class_category");
        });

        modelBuilder.Entity<LatexTokenUsage>(e =>
        {
            e.ToTable("latex_token_usage");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.TokenId).HasColumnName("token_id");
            e.Property(x => x.SessionId).HasColumnName("session_id");
            e.Property(x => x.Count).HasColumnName("count").HasDefaultValue(1);
            e.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").HasDefaultValueSql("NOW()");
            e.HasOne(x => x.Token).WithMany().HasForeignKey(x => x.TokenId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TokenId, x.SessionId }).IsUnique().HasDatabaseName("ux_latex_token_usage_token_session");
            e.HasIndex(x => x.SessionId).HasDatabaseName("ix_latex_token_usage_session");
            e.HasIndex(x => x.LastSeenAt).HasDatabaseName("ix_latex_token_usage_last_seen");
        });

        // --- Typst translation catalog ---
        modelBuilder.Entity<TypstTranslationHandler>(e =>
        {
            e.ToTable("typst_translation_handlers", t =>
            {
                t.HasCheckConstraint("ck_typst_handler_status",
                    "status IN ('active','deprecated','planned')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.HandlerKey).HasColumnName("handler_key").HasMaxLength(80).IsRequired();
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(40).IsRequired();
            e.Property(x => x.SourcePattern).HasColumnName("source_pattern").IsRequired();
            e.Property(x => x.TypstEmit).HasColumnName("typst_emit").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired().HasDefaultValue("active");
            e.Property(x => x.ShippedAt).HasColumnName("shipped_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.ShippedIn).HasColumnName("shipped_in").HasMaxLength(40);
            e.Property(x => x.Notes).HasColumnName("notes");
            e.HasIndex(x => x.HandlerKey).IsUnique().HasDatabaseName("ux_typst_handler_key");
            e.HasIndex(x => new { x.Category, x.Status }).HasDatabaseName("ix_typst_handler_category_status");
        });

        modelBuilder.Entity<TypstTranslationGap>(e =>
        {
            e.ToTable("typst_translation_gaps", t =>
            {
                t.HasCheckConstraint("ck_typst_gap_mitigation",
                    "mitigation_status IN ('none','workaround','scheduled','shipped')");
                t.HasCheckConstraint("ck_typst_gap_severity",
                    "blocking_severity IN ('info','warn','error')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.GapKey).HasColumnName("gap_key").HasMaxLength(80).IsRequired();
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(40).IsRequired();
            e.Property(x => x.SamplePattern).HasColumnName("sample_pattern").IsRequired();
            e.Property(x => x.TypstErrorShape).HasColumnName("typst_error_shape").HasMaxLength(120);
            e.Property(x => x.MitigationStatus).HasColumnName("mitigation_status").HasMaxLength(20).IsRequired().HasDefaultValue("none");
            e.Property(x => x.BlockingSeverity).HasColumnName("blocking_severity").HasMaxLength(20).IsRequired().HasDefaultValue("info");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.GapKey).IsUnique().HasDatabaseName("ux_typst_gap_key");
            e.HasIndex(x => new { x.MitigationStatus, x.BlockingSeverity }).HasDatabaseName("ix_typst_gap_mitigation_severity");
        });

        // --- Block grouping primitive (LILIA-136) ---
        // M:N between blocks and named groups, scoped to a document and a
        // single "dimension". First dimension is "layout" (multi-column
        // regions). Designed to host other dimensions later (review tags,
        // counter scopes, style presets, source attribution) without
        // schema churn — they all live in the same two tables.
        modelBuilder.Entity<BlockGroup>(e =>
        {
            e.ToTable("block_groups");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.DocumentId).HasColumnName("document_id").IsRequired();
            e.Property(x => x.Dimension).HasColumnName("dimension").HasMaxLength(40).IsRequired();
            e.Property(x => x.Attributes).HasColumnName("attributes").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

            e.HasOne(x => x.Document)
                .WithMany()
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.DocumentId).HasDatabaseName("ix_block_group_document");
            e.HasIndex(x => new { x.DocumentId, x.Dimension })
                .HasDatabaseName("ix_block_group_document_dimension");
        });

        modelBuilder.Entity<BlockGroupMembership>(e =>
        {
            e.ToTable("block_group_memberships");
            // Composite key — a block can be in a given group at most once.
            // The "one-group-per-dimension-per-block" rule is enforced at
            // the service layer (cheaper than a trigger; race window is
            // tolerable for a single-writer editor).
            e.HasKey(x => new { x.BlockId, x.GroupId });
            e.Property(x => x.BlockId).HasColumnName("block_id");
            e.Property(x => x.GroupId).HasColumnName("group_id");

            e.HasOne(x => x.Block)
                .WithMany()
                .HasForeignKey(x => x.BlockId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Group)
                .WithMany(g => g.Memberships)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.BlockId).HasDatabaseName("ix_block_group_member_block");
            e.HasIndex(x => x.GroupId).HasDatabaseName("ix_block_group_member_group");
        });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LiliaDbContext).Assembly);
    }
}
