using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddE2eScenarioSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "e2e");

            // pg_trgm — powers the trigram GIN index on scenario.title
            // for fuzzy search in the admin UI.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.CreateTable(
                name: "block_action",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    expected_surface_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_block_action", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "block_type",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    latex_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    scenario_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_exercised_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_block_type", x => x.id);
                    table.CheckConstraint("ck_e2e_block_type_category", "category IN ('text','structure','media','code','reference','math')");
                });

            migrationBuilder.CreateTable(
                name: "module",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    owner = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    criticality = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false, defaultValue: "p1"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_module", x => x.id);
                    table.CheckConstraint("ck_e2e_module_criticality", "criticality IN ('p0','p1','p2')");
                });

            migrationBuilder.CreateTable(
                name: "tag",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "test_run",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    trigger_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "manual"),
                    branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    commit_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    environment = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    total = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    passed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    failed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    skipped = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    reporter_meta = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_test_run", x => x.id);
                    table.CheckConstraint("ck_e2e_run_trigger_kind", "trigger_kind IN ('ci','manual','scheduled','hosted')");
                });

            migrationBuilder.CreateTable(
                name: "surface",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    surface_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    route_pattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    source_file = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    testid_root = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    criticality = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false, defaultValue: "p1"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_surface", x => x.id);
                    table.CheckConstraint("ck_e2e_surface_criticality", "criticality IN ('p0','p1','p2')");
                    table.CheckConstraint("ck_e2e_surface_kind", "surface_kind IN ('page','modal','drawer','popover','popup','sheet','dialog','inline','overlay')");
                    table.ForeignKey(
                        name: "FK_surface_module_module_id",
                        column: x => x.module_id,
                        principalSchema: "e2e",
                        principalTable: "module",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ui_element",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    surface_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    element_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    accessible_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    visible_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    default_selector = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    produces_block_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    triggers_surface_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ui_element", x => x.id);
                    table.CheckConstraint("ck_e2e_ui_element_kind", "element_kind IN ('button','icon_button','link','input','textarea','menu_item','toggle','switch','radio','checkbox','select','tab','disclosure','sheet_handle','fab')");
                    table.ForeignKey(
                        name: "FK_ui_element_block_type_produces_block_type_id",
                        column: x => x.produces_block_type_id,
                        principalSchema: "e2e",
                        principalTable: "block_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ui_element_surface_surface_id",
                        column: x => x.surface_id,
                        principalSchema: "e2e",
                        principalTable: "surface",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ui_element_surface_triggers_surface_id",
                        column: x => x.triggers_surface_id,
                        principalSchema: "e2e",
                        principalTable: "surface",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "entry_point",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    target_surface_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    opener_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    opener_element_id = table.Column<Guid>(type: "uuid", nullable: true),
                    shortcut_keys = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    criticality = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false, defaultValue: "p1"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entry_point", x => x.id);
                    table.CheckConstraint("ck_e2e_entry_point_criticality", "criticality IN ('p0','p1','p2')");
                    table.CheckConstraint("ck_e2e_entry_point_opener_kind", "opener_kind IN ('toolbar_button','command_palette','keyboard_shortcut','url_state','context_menu','right_click','auto_open','deep_link','direct_mount','drag_drop','long_press')");
                    table.ForeignKey(
                        name: "FK_entry_point_surface_target_surface_id",
                        column: x => x.target_surface_id,
                        principalSchema: "e2e",
                        principalTable: "surface",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_entry_point_ui_element_opener_element_id",
                        column: x => x.opener_element_id,
                        principalSchema: "e2e",
                        principalTable: "ui_element",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "selector_candidate",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ui_element_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    selector = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    accessible_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    visible_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tag_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(3,2)", nullable: false, defaultValue: 1.0m),
                    last_matched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_missed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_selector_candidate", x => x.id);
                    table.CheckConstraint("ck_e2e_selector_confidence", "confidence BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_selector_candidate_ui_element_ui_element_id",
                        column: x => x.ui_element_id,
                        principalSchema: "e2e",
                        principalTable: "ui_element",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scenario",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_surface_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_point_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criticality = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false, defaultValue: "p1"),
                    detail_level = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false, defaultValue: "l1"),
                    review_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "draft"),
                    execution_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "integration"),
                    template = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "standard"),
                    automation_content = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    estimate_seconds = table.Column<int>(type: "integer", nullable: true),
                    estimate_forecast_seconds = table.Column<int>(type: "integer", nullable: true),
                    milestone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    exploratory_mission = table.Column<string>(type: "text", nullable: true),
                    exploratory_goals = table.Column<string>(type: "text", nullable: true),
                    current_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    updated_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario", x => x.id);
                    table.CheckConstraint("ck_e2e_scenario_criticality", "criticality IN ('p0','p1','p2')");
                    table.CheckConstraint("ck_e2e_scenario_detail_level", "detail_level IN ('l1','l2','l3')");
                    table.CheckConstraint("ck_e2e_scenario_execution_mode", "execution_mode IN ('integration','component')");
                    table.CheckConstraint("ck_e2e_scenario_review_state", "review_state IN ('draft','approved','quarantined','deprecated')");
                    table.CheckConstraint("ck_e2e_scenario_template", "template IN ('standard','exploratory','parametrised','accessibility')");
                    table.ForeignKey(
                        name: "FK_scenario_entry_point_entry_point_id",
                        column: x => x.entry_point_id,
                        principalSchema: "e2e",
                        principalTable: "entry_point",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_scenario_module_module_id",
                        column: x => x.module_id,
                        principalSchema: "e2e",
                        principalTable: "module",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scenario_surface_target_surface_id",
                        column: x => x.target_surface_id,
                        principalSchema: "e2e",
                        principalTable: "surface",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "scenario_coverage_link",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    scenario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layer = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    target_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_coverage_link", x => x.id);
                    table.CheckConstraint("ck_e2e_coverage_layer", "layer IN ('l1','l2','l3')");
                    table.CheckConstraint("ck_e2e_coverage_target_kind", "target_kind IN ('ui_element','block_type','module','surface','entry_point')");
                    table.ForeignKey(
                        name: "FK_scenario_coverage_link_scenario_scenario_id",
                        column: x => x.scenario_id,
                        principalSchema: "e2e",
                        principalTable: "scenario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scenario_health_state",
                schema: "e2e",
                columns: table => new
                {
                    scenario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flake_score = table.Column<decimal>(type: "numeric(4,3)", nullable: false, defaultValue: 0m),
                    health_state = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "green"),
                    state_changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    computed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_health_state", x => x.scenario_id);
                    table.CheckConstraint("ck_e2e_health_state", "health_state IN ('green','watching','quarantined','investigated_as_bug')");
                    table.ForeignKey(
                        name: "FK_scenario_health_state_scenario_scenario_id",
                        column: x => x.scenario_id,
                        principalSchema: "e2e",
                        principalTable: "scenario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scenario_tag",
                schema: "e2e",
                columns: table => new
                {
                    scenario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_tag", x => new { x.scenario_id, x.tag_id });
                    table.ForeignKey(
                        name: "FK_scenario_tag_scenario_scenario_id",
                        column: x => x.scenario_id,
                        principalSchema: "e2e",
                        principalTable: "scenario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scenario_tag_tag_tag_id",
                        column: x => x.tag_id,
                        principalSchema: "e2e",
                        principalTable: "tag",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scenario_version",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    scenario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    detail_level = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    preconditions = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    steps = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    expected_outcomes = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    generation_provenance = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "human"),
                    generated_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    parent_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_version", x => x.id);
                    table.CheckConstraint("ck_e2e_scenario_version_detail_level", "detail_level IN ('l1','l2','l3')");
                    table.CheckConstraint("ck_e2e_scenario_version_provenance", "generation_provenance IN ('human','llm_draft','llm_repaired','imported','session_recording')");
                    table.ForeignKey(
                        name: "FK_scenario_version_scenario_scenario_id",
                        column: x => x.scenario_id,
                        principalSchema: "e2e",
                        principalTable: "scenario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scenario_version_scenario_version_parent_version_id",
                        column: x => x.parent_version_id,
                        principalSchema: "e2e",
                        principalTable: "scenario_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "scenario_insight",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    scenario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(3,2)", nullable: false, defaultValue: 0m),
                    auto_apply_eligible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "open"),
                    suggested_change = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    evidence_run_ids = table.Column<Guid[]>(type: "uuid[]", nullable: true),
                    prior_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    applied_then_uncovered_bug = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false, defaultValue: "claude"),
                    applied_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    applied_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    rolled_back_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rolled_back_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_insight", x => x.id);
                    table.CheckConstraint("ck_e2e_insight_confidence", "confidence BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_e2e_insight_kind", "kind IN ('selector_drift','behavioral_drift','flake_cluster','coverage_gap','promotion_proposal','probable_product_bug','duplicate_scenario','ghost_element')");
                    table.CheckConstraint("ck_e2e_insight_status", "status IN ('open','pending_review','applied','dismissed','rolled_back')");
                    table.ForeignKey(
                        name: "FK_scenario_insight_scenario_scenario_id",
                        column: x => x.scenario_id,
                        principalSchema: "e2e",
                        principalTable: "scenario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scenario_insight_scenario_version_prior_version_id",
                        column: x => x.prior_version_id,
                        principalSchema: "e2e",
                        principalTable: "scenario_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "scenario_result",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    test_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scenario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scenario_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    detail_level_run = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    browser = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    browser_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    viewport_w = table.Column<int>(type: "integer", nullable: true),
                    viewport_h = table.Column<int>(type: "integer", nullable: true),
                    locale = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    feature_flags = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    backend_build_sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    test_order_index = table.Column<int>(type: "integer", nullable: true),
                    prior_session_residue_detected = table.Column<bool>(type: "boolean", nullable: true),
                    failure_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    failure_message = table.Column<string>(type: "text", nullable: true),
                    failure_stack = table.Column<string>(type: "text", nullable: true),
                    is_retry_pass = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    retry_of_id = table.Column<Guid>(type: "uuid", nullable: true),
                    retry_attempt = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    parametrisation = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    trace_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    screenshot_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    video_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    duration_z_score = table.Column<decimal>(type: "numeric(6,3)", nullable: true),
                    screenshot_diff_isolated = table.Column<bool>(type: "boolean", nullable: true),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_result", x => x.id);
                    table.CheckConstraint("ck_e2e_result_detail_level", "detail_level_run IN ('l1','l2','l3')");
                    table.CheckConstraint("ck_e2e_result_failure_kind", "failure_kind IS NULL OR failure_kind IN ('selector_not_found','timeout_navigation','timeout_visibility','assertion_failed','navigation_failed','infrastructure_error','visual_regression','console_error')");
                    table.CheckConstraint("ck_e2e_result_outcome", "outcome IN ('pass','fail','skip','timed_out','interrupted')");
                    table.ForeignKey(
                        name: "FK_scenario_result_scenario_result_retry_of_id",
                        column: x => x.retry_of_id,
                        principalSchema: "e2e",
                        principalTable: "scenario_result",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_scenario_result_scenario_scenario_id",
                        column: x => x.scenario_id,
                        principalSchema: "e2e",
                        principalTable: "scenario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scenario_result_scenario_version_scenario_version_id",
                        column: x => x.scenario_version_id,
                        principalSchema: "e2e",
                        principalTable: "scenario_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scenario_result_test_run_test_run_id",
                        column: x => x.test_run_id,
                        principalSchema: "e2e",
                        principalTable: "test_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scenario_step",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    scenario_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    step_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    target_ui_element_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    technical_assertion = table.Column<string>(type: "text", nullable: true),
                    user_visible_outcome = table.Column<string>(type: "text", nullable: true),
                    shared_step_id = table.Column<Guid>(type: "uuid", nullable: true),
                    additional_info = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_step", x => x.id);
                    table.CheckConstraint("ck_e2e_step_action_kind", "action_kind IS NULL OR action_kind IN ('click','dblclick','type','press','select','check','uncheck','focus','blur','hover','drag','drop','navigate','wait_for','expect')");
                    table.CheckConstraint("ck_e2e_step_kind", "step_kind IN ('setup','action','wait','assert','teardown')");
                    table.ForeignKey(
                        name: "FK_scenario_step_scenario_step_shared_step_id",
                        column: x => x.shared_step_id,
                        principalSchema: "e2e",
                        principalTable: "scenario_step",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_scenario_step_scenario_version_scenario_version_id",
                        column: x => x.scenario_version_id,
                        principalSchema: "e2e",
                        principalTable: "scenario_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scenario_step_ui_element_target_ui_element_id",
                        column: x => x.target_ui_element_id,
                        principalSchema: "e2e",
                        principalTable: "ui_element",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ui_interaction_event",
                schema: "e2e",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scenario_result_id = table.Column<Guid>(type: "uuid", nullable: true),
                    real_user_session_hash = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ui_element_id = table.Column<Guid>(type: "uuid", nullable: true),
                    surface_id = table.Column<Guid>(type: "uuid", nullable: true),
                    interaction_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ui_interaction_event", x => x.id);
                    table.CheckConstraint("ck_e2e_uie_interaction_kind", "interaction_kind IN ('click','dblclick','type','focus','select','navigate','submit','drag','hover','keypress')");
                    table.CheckConstraint("ck_e2e_uie_source", "source IN ('test','real_user')");
                    table.ForeignKey(
                        name: "FK_ui_interaction_event_scenario_result_scenario_result_id",
                        column: x => x.scenario_result_id,
                        principalSchema: "e2e",
                        principalTable: "scenario_result",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ui_interaction_event_surface_surface_id",
                        column: x => x.surface_id,
                        principalSchema: "e2e",
                        principalTable: "surface",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ui_interaction_event_ui_element_ui_element_id",
                        column: x => x.ui_element_id,
                        principalSchema: "e2e",
                        principalTable: "ui_element",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_block_action_slug",
                schema: "e2e",
                table: "block_action",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_block_type_slug",
                schema: "e2e",
                table: "block_type",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entry_point_opener_element_id",
                schema: "e2e",
                table: "entry_point",
                column: "opener_element_id");

            migrationBuilder.CreateIndex(
                name: "IX_entry_point_target_surface_id_slug",
                schema: "e2e",
                table: "entry_point",
                columns: new[] { "target_surface_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_module_slug",
                schema: "e2e",
                table: "module",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scenario_automation_content",
                schema: "e2e",
                table: "scenario",
                column: "automation_content",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scenario_criticality",
                schema: "e2e",
                table: "scenario",
                column: "criticality");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_current_version_id",
                schema: "e2e",
                table: "scenario",
                column: "current_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_entry_point_id",
                schema: "e2e",
                table: "scenario",
                column: "entry_point_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_module_id",
                schema: "e2e",
                table: "scenario",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_review_state",
                schema: "e2e",
                table: "scenario",
                column: "review_state");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_slug",
                schema: "e2e",
                table: "scenario",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scenario_target_surface_id",
                schema: "e2e",
                table: "scenario",
                column: "target_surface_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_coverage_link_scenario_id_layer_target_kind_target~",
                schema: "e2e",
                table: "scenario_coverage_link",
                columns: new[] { "scenario_id", "layer", "target_kind", "target_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scenario_coverage_link_target_kind_target_id",
                schema: "e2e",
                table: "scenario_coverage_link",
                columns: new[] { "target_kind", "target_id" });

            migrationBuilder.CreateIndex(
                name: "IX_scenario_insight_kind",
                schema: "e2e",
                table: "scenario_insight",
                column: "kind");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_insight_prior_version_id",
                schema: "e2e",
                table: "scenario_insight",
                column: "prior_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_insight_scenario_id",
                schema: "e2e",
                table: "scenario_insight",
                column: "scenario_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_insight_status",
                schema: "e2e",
                table: "scenario_insight",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_result_outcome",
                schema: "e2e",
                table: "scenario_result",
                column: "outcome");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_result_recorded_at",
                schema: "e2e",
                table: "scenario_result",
                column: "recorded_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_scenario_result_retry_of_id",
                schema: "e2e",
                table: "scenario_result",
                column: "retry_of_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_result_scenario_id",
                schema: "e2e",
                table: "scenario_result",
                column: "scenario_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_result_scenario_version_id",
                schema: "e2e",
                table: "scenario_result",
                column: "scenario_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_result_test_run_id",
                schema: "e2e",
                table: "scenario_result",
                column: "test_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_step_scenario_version_id_sort_order",
                schema: "e2e",
                table: "scenario_step",
                columns: new[] { "scenario_version_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_scenario_step_shared_step_id",
                schema: "e2e",
                table: "scenario_step",
                column: "shared_step_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_step_target_ui_element_id",
                schema: "e2e",
                table: "scenario_step",
                column: "target_ui_element_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_tag_tag_id",
                schema: "e2e",
                table: "scenario_tag",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_version_parent_version_id",
                schema: "e2e",
                table: "scenario_version",
                column: "parent_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_version_scenario_id_version_number",
                schema: "e2e",
                table: "scenario_version",
                columns: new[] { "scenario_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_selector_candidate_ui_element_id_ordinal",
                schema: "e2e",
                table: "selector_candidate",
                columns: new[] { "ui_element_id", "ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_surface_module_id_slug",
                schema: "e2e",
                table: "surface",
                columns: new[] { "module_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tag_slug",
                schema: "e2e",
                table: "tag",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_test_run_started_at",
                schema: "e2e",
                table: "test_run",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "IX_ui_element_produces_block_type_id",
                schema: "e2e",
                table: "ui_element",
                column: "produces_block_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_ui_element_surface_id_slug",
                schema: "e2e",
                table: "ui_element",
                columns: new[] { "surface_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ui_element_triggers_surface_id",
                schema: "e2e",
                table: "ui_element",
                column: "triggers_surface_id");

            migrationBuilder.CreateIndex(
                name: "IX_ui_interaction_event_occurred_at",
                schema: "e2e",
                table: "ui_interaction_event",
                column: "occurred_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_ui_interaction_event_scenario_result_id",
                schema: "e2e",
                table: "ui_interaction_event",
                column: "scenario_result_id");

            migrationBuilder.CreateIndex(
                name: "IX_ui_interaction_event_source_real_user_session_hash",
                schema: "e2e",
                table: "ui_interaction_event",
                columns: new[] { "source", "real_user_session_hash" });

            migrationBuilder.CreateIndex(
                name: "IX_ui_interaction_event_surface_id",
                schema: "e2e",
                table: "ui_interaction_event",
                column: "surface_id");

            migrationBuilder.CreateIndex(
                name: "IX_ui_interaction_event_ui_element_id",
                schema: "e2e",
                table: "ui_interaction_event",
                column: "ui_element_id");

            migrationBuilder.AddForeignKey(
                name: "FK_scenario_scenario_version_current_version_id",
                schema: "e2e",
                table: "scenario",
                column: "current_version_id",
                principalSchema: "e2e",
                principalTable: "scenario_version",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // Trigram fuzzy-search index on scenario.title for the admin
            // UI's search box. Not expressible via EF fluent config.
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS idx_e2e_scenario_title_trgm
                ON e2e.scenario USING gin (title gin_trgm_ops);
            ");

            // Materialised view: per-element coverage. Refreshed nightly
            // by the Claude routine. Joins coverage links + interaction
            // events so the admin can see both test-driven and real-user
            // exercise counts (the closer-to-the-user axis).
            migrationBuilder.Sql(@"
                CREATE MATERIALIZED VIEW e2e.ui_element_coverage AS
                SELECT
                    e.id AS ui_element_id,
                    e.surface_id,
                    COUNT(DISTINCT cov.scenario_id) AS scenario_count,
                    ARRAY_AGG(DISTINCT cov.scenario_id)
                        FILTER (WHERE cov.scenario_id IS NOT NULL) AS scenario_ids,
                    ARRAY_AGG(DISTINCT cov.layer)
                        FILTER (WHERE cov.layer IS NOT NULL) AS layers_covered,
                    MAX(uie.occurred_at) FILTER (WHERE uie.source = 'test')
                        AS last_test_exercise,
                    MAX(uie.occurred_at) FILTER (WHERE uie.source = 'real_user')
                        AS last_user_exercise,
                    COUNT(*) FILTER (WHERE uie.source = 'real_user')
                        AS real_user_interaction_count,
                    CASE WHEN COUNT(DISTINCT cov.scenario_id) > 1
                        THEN true ELSE false END AS is_duplicate
                FROM e2e.ui_element e
                LEFT JOIN e2e.scenario_coverage_link cov
                       ON cov.target_kind = 'ui_element'
                      AND cov.target_id   = e.id
                LEFT JOIN e2e.ui_interaction_event uie
                       ON uie.ui_element_id = e.id
                WHERE e.is_active = true
                GROUP BY e.id, e.surface_id;

                CREATE UNIQUE INDEX idx_uec_element
                    ON e2e.ui_element_coverage(ui_element_id);
            ");

            // Materialised view: per-scenario health summary over the
            // last 14 days of results. Powers the flake_score state
            // machine in the Claude routine.
            migrationBuilder.Sql(@"
                CREATE MATERIALIZED VIEW e2e.scenario_health AS
                WITH recent AS (
                    SELECT *
                    FROM e2e.scenario_result
                    WHERE recorded_at > now() - interval '14 days'
                )
                SELECT
                    s.id AS scenario_id,
                    COUNT(r.*) FILTER (WHERE r.outcome = 'fail')::numeric
                        / NULLIF(COUNT(r.*), 0) AS failure_rate,
                    COUNT(r.*) FILTER (WHERE r.is_retry_pass = true)::numeric
                        / NULLIF(COUNT(r.*), 0) AS retry_pass_rate,
                    (
                        SELECT mode() WITHIN GROUP (ORDER BY r2.failure_kind)
                        FROM recent r2
                        WHERE r2.scenario_id = s.id AND r2.outcome = 'fail'
                    ) AS dominant_failure_kind,
                    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY r.duration_ms)
                        AS duration_p50,
                    PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY r.duration_ms)
                        AS duration_p95,
                    STDDEV(r.duration_ms) / NULLIF(AVG(r.duration_ms), 0)
                        AS duration_cv,
                    COALESCE(
                        0.35 * (COUNT(r.*) FILTER (WHERE r.outcome = 'fail')::numeric
                                  / NULLIF(COUNT(r.*), 0)), 0)
                    + COALESCE(
                        0.25 * (COUNT(r.*) FILTER (WHERE r.is_retry_pass = true)::numeric
                                  / NULLIF(COUNT(r.*), 0)), 0)
                        AS partial_flake_score,
                    COUNT(r.*) AS sample_size
                FROM e2e.scenario s
                LEFT JOIN recent r ON r.scenario_id = s.id
                GROUP BY s.id;

                CREATE UNIQUE INDEX idx_sh_scenario
                    ON e2e.scenario_health(scenario_id);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop matviews + extra index before EF tears down tables.
            migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS e2e.scenario_health;");
            migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS e2e.ui_element_coverage;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS e2e.idx_e2e_scenario_title_trgm;");

            migrationBuilder.DropForeignKey(
                name: "FK_entry_point_surface_target_surface_id",
                schema: "e2e",
                table: "entry_point");

            migrationBuilder.DropForeignKey(
                name: "FK_scenario_surface_target_surface_id",
                schema: "e2e",
                table: "scenario");

            migrationBuilder.DropForeignKey(
                name: "FK_ui_element_surface_surface_id",
                schema: "e2e",
                table: "ui_element");

            migrationBuilder.DropForeignKey(
                name: "FK_ui_element_surface_triggers_surface_id",
                schema: "e2e",
                table: "ui_element");

            migrationBuilder.DropForeignKey(
                name: "FK_entry_point_ui_element_opener_element_id",
                schema: "e2e",
                table: "entry_point");

            migrationBuilder.DropForeignKey(
                name: "FK_scenario_entry_point_entry_point_id",
                schema: "e2e",
                table: "scenario");

            migrationBuilder.DropForeignKey(
                name: "FK_scenario_module_module_id",
                schema: "e2e",
                table: "scenario");

            migrationBuilder.DropForeignKey(
                name: "FK_scenario_scenario_version_current_version_id",
                schema: "e2e",
                table: "scenario");

            migrationBuilder.DropTable(
                name: "block_action",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "scenario_coverage_link",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "scenario_health_state",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "scenario_insight",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "scenario_step",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "scenario_tag",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "selector_candidate",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "ui_interaction_event",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "tag",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "scenario_result",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "test_run",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "surface",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "ui_element",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "block_type",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "entry_point",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "module",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "scenario_version",
                schema: "e2e");

            migrationBuilder.DropTable(
                name: "scenario",
                schema: "e2e");
        }
    }
}
