using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLatexUnicodeMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_telemetry_event_kind",
                table: "import_telemetry_events");

            migrationBuilder.CreateTable(
                name: "latex_unicode_map",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    codepoint = table.Column<int>(type: "integer", nullable: false),
                    character = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    replacement = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "math"),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "other"),
                    package_slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    coverage_level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "full"),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_latex_unicode_map", x => x.id);
                    table.CheckConstraint("ck_latex_unicode_coverage", "coverage_level IN ('full','shimmed','none')");
                    table.CheckConstraint("ck_latex_unicode_mode", "mode IN ('math','text','either')");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_telemetry_event_kind",
                table: "import_telemetry_events",
                sql: "event_kind IN ('unknown_env','unhandled_token','silent_fallback','cell_cleanup_applied','partial_parse','expected_leak_hit','cmd_passthrough','unsupported_block_emitted','parser_warning','unmapped_unicode_char')");

            migrationBuilder.CreateIndex(
                name: "ix_latex_unicode_category",
                table: "latex_unicode_map",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ux_latex_unicode_codepoint",
                table: "latex_unicode_map",
                column: "codepoint",
                unique: true);

            // Seed the curated Unicode→LaTeX coverage catalog (Greek, math
            // operators/relations/arrows, typography). DB is authoritative; this
            // is the initial set and grows over time.
            migrationBuilder.Sql(@"INSERT INTO latex_unicode_map (id, codepoint, character, replacement, category, mode, coverage_level, created_at, updated_at) VALUES
(gen_random_uuid(),945,'α','\ensuremath{\alpha}','greek','math','full',now(),now()),
(gen_random_uuid(),946,'β','\ensuremath{\beta}','greek','math','full',now(),now()),
(gen_random_uuid(),947,'γ','\ensuremath{\gamma}','greek','math','full',now(),now()),
(gen_random_uuid(),948,'δ','\ensuremath{\delta}','greek','math','full',now(),now()),
(gen_random_uuid(),949,'ε','\ensuremath{\varepsilon}','greek','math','full',now(),now()),
(gen_random_uuid(),950,'ζ','\ensuremath{\zeta}','greek','math','full',now(),now()),
(gen_random_uuid(),951,'η','\ensuremath{\eta}','greek','math','full',now(),now()),
(gen_random_uuid(),952,'θ','\ensuremath{\theta}','greek','math','full',now(),now()),
(gen_random_uuid(),953,'ι','\ensuremath{\iota}','greek','math','full',now(),now()),
(gen_random_uuid(),954,'κ','\ensuremath{\kappa}','greek','math','full',now(),now()),
(gen_random_uuid(),955,'λ','\ensuremath{\lambda}','greek','math','full',now(),now()),
(gen_random_uuid(),956,'μ','\ensuremath{\mu}','greek','math','full',now(),now()),
(gen_random_uuid(),957,'ν','\ensuremath{\nu}','greek','math','full',now(),now()),
(gen_random_uuid(),958,'ξ','\ensuremath{\xi}','greek','math','full',now(),now()),
(gen_random_uuid(),960,'π','\ensuremath{\pi}','greek','math','full',now(),now()),
(gen_random_uuid(),961,'ρ','\ensuremath{\rho}','greek','math','full',now(),now()),
(gen_random_uuid(),963,'σ','\ensuremath{\sigma}','greek','math','full',now(),now()),
(gen_random_uuid(),964,'τ','\ensuremath{\tau}','greek','math','full',now(),now()),
(gen_random_uuid(),965,'υ','\ensuremath{\upsilon}','greek','math','full',now(),now()),
(gen_random_uuid(),966,'φ','\ensuremath{\varphi}','greek','math','full',now(),now()),
(gen_random_uuid(),967,'χ','\ensuremath{\chi}','greek','math','full',now(),now()),
(gen_random_uuid(),968,'ψ','\ensuremath{\psi}','greek','math','full',now(),now()),
(gen_random_uuid(),969,'ω','\ensuremath{\omega}','greek','math','full',now(),now()),
(gen_random_uuid(),1013,'ϵ','\ensuremath{\epsilon}','greek','math','full',now(),now()),
(gen_random_uuid(),977,'ϑ','\ensuremath{\vartheta}','greek','math','full',now(),now()),
(gen_random_uuid(),981,'ϕ','\ensuremath{\phi}','greek','math','full',now(),now()),
(gen_random_uuid(),1009,'ϱ','\ensuremath{\varrho}','greek','math','full',now(),now()),
(gen_random_uuid(),962,'ς','\ensuremath{\varsigma}','greek','math','full',now(),now()),
(gen_random_uuid(),982,'ϖ','\ensuremath{\varpi}','greek','math','full',now(),now()),
(gen_random_uuid(),915,'Γ','\ensuremath{\Gamma}','greek','math','full',now(),now()),
(gen_random_uuid(),916,'Δ','\ensuremath{\Delta}','greek','math','full',now(),now()),
(gen_random_uuid(),920,'Θ','\ensuremath{\Theta}','greek','math','full',now(),now()),
(gen_random_uuid(),923,'Λ','\ensuremath{\Lambda}','greek','math','full',now(),now()),
(gen_random_uuid(),926,'Ξ','\ensuremath{\Xi}','greek','math','full',now(),now()),
(gen_random_uuid(),928,'Π','\ensuremath{\Pi}','greek','math','full',now(),now()),
(gen_random_uuid(),931,'Σ','\ensuremath{\Sigma}','greek','math','full',now(),now()),
(gen_random_uuid(),933,'Υ','\ensuremath{\Upsilon}','greek','math','full',now(),now()),
(gen_random_uuid(),934,'Φ','\ensuremath{\Phi}','greek','math','full',now(),now()),
(gen_random_uuid(),936,'Ψ','\ensuremath{\Psi}','greek','math','full',now(),now()),
(gen_random_uuid(),937,'Ω','\ensuremath{\Omega}','greek','math','full',now(),now()),
(gen_random_uuid(),215,'×','\ensuremath{\times}','math','math','full',now(),now()),
(gen_random_uuid(),247,'÷','\ensuremath{\div}','math','math','full',now(),now()),
(gen_random_uuid(),177,'±','\ensuremath{\pm}','math','math','full',now(),now()),
(gen_random_uuid(),8723,'∓','\ensuremath{\mp}','math','math','full',now(),now()),
(gen_random_uuid(),183,'·','\ensuremath{\cdot}','math','math','full',now(),now()),
(gen_random_uuid(),8727,'∗','\ensuremath{\ast}','math','math','full',now(),now()),
(gen_random_uuid(),8902,'⋆','\ensuremath{\star}','math','math','full',now(),now()),
(gen_random_uuid(),8804,'≤','\ensuremath{\leq}','math','math','full',now(),now()),
(gen_random_uuid(),8805,'≥','\ensuremath{\geq}','math','math','full',now(),now()),
(gen_random_uuid(),8800,'≠','\ensuremath{\neq}','math','math','full',now(),now()),
(gen_random_uuid(),8776,'≈','\ensuremath{\approx}','math','math','full',now(),now()),
(gen_random_uuid(),8773,'≅','\ensuremath{\cong}','math','math','full',now(),now()),
(gen_random_uuid(),8801,'≡','\ensuremath{\equiv}','math','math','full',now(),now()),
(gen_random_uuid(),8771,'≃','\ensuremath{\simeq}','math','math','full',now(),now()),
(gen_random_uuid(),8733,'∝','\ensuremath{\propto}','math','math','full',now(),now()),
(gen_random_uuid(),8764,'∼','\ensuremath{\sim}','math','math','full',now(),now()),
(gen_random_uuid(),8734,'∞','\ensuremath{\infty}','math','math','full',now(),now()),
(gen_random_uuid(),8706,'∂','\ensuremath{\partial}','math','math','full',now(),now()),
(gen_random_uuid(),8711,'∇','\ensuremath{\nabla}','math','math','full',now(),now()),
(gen_random_uuid(),8730,'√','\ensuremath{\surd}','math','math','full',now(),now()),
(gen_random_uuid(),8721,'∑','\ensuremath{\sum}','math','math','full',now(),now()),
(gen_random_uuid(),8719,'∏','\ensuremath{\prod}','math','math','full',now(),now()),
(gen_random_uuid(),8747,'∫','\ensuremath{\int}','math','math','full',now(),now()),
(gen_random_uuid(),8750,'∮','\ensuremath{\oint}','math','math','full',now(),now()),
(gen_random_uuid(),8712,'∈','\ensuremath{\in}','math','math','full',now(),now()),
(gen_random_uuid(),8713,'∉','\ensuremath{\notin}','math','math','full',now(),now()),
(gen_random_uuid(),8715,'∋','\ensuremath{\ni}','math','math','full',now(),now()),
(gen_random_uuid(),8834,'⊂','\ensuremath{\subset}','math','math','full',now(),now()),
(gen_random_uuid(),8838,'⊆','\ensuremath{\subseteq}','math','math','full',now(),now()),
(gen_random_uuid(),8835,'⊃','\ensuremath{\supset}','math','math','full',now(),now()),
(gen_random_uuid(),8839,'⊇','\ensuremath{\supseteq}','math','math','full',now(),now()),
(gen_random_uuid(),8746,'∪','\ensuremath{\cup}','math','math','full',now(),now()),
(gen_random_uuid(),8745,'∩','\ensuremath{\cap}','math','math','full',now(),now()),
(gen_random_uuid(),8709,'∅','\ensuremath{\emptyset}','math','math','full',now(),now()),
(gen_random_uuid(),8704,'∀','\ensuremath{\forall}','math','math','full',now(),now()),
(gen_random_uuid(),8707,'∃','\ensuremath{\exists}','math','math','full',now(),now()),
(gen_random_uuid(),172,'¬','\ensuremath{\neg}','math','math','full',now(),now()),
(gen_random_uuid(),8743,'∧','\ensuremath{\wedge}','math','math','full',now(),now()),
(gen_random_uuid(),8744,'∨','\ensuremath{\vee}','math','math','full',now(),now()),
(gen_random_uuid(),8853,'⊕','\ensuremath{\oplus}','math','math','full',now(),now()),
(gen_random_uuid(),8855,'⊗','\ensuremath{\otimes}','math','math','full',now(),now()),
(gen_random_uuid(),8594,'→','\ensuremath{\rightarrow}','math','math','full',now(),now()),
(gen_random_uuid(),8592,'←','\ensuremath{\leftarrow}','math','math','full',now(),now()),
(gen_random_uuid(),8596,'↔','\ensuremath{\leftrightarrow}','math','math','full',now(),now()),
(gen_random_uuid(),8658,'⇒','\ensuremath{\Rightarrow}','math','math','full',now(),now()),
(gen_random_uuid(),8656,'⇐','\ensuremath{\Leftarrow}','math','math','full',now(),now()),
(gen_random_uuid(),8660,'⇔','\ensuremath{\Leftrightarrow}','math','math','full',now(),now()),
(gen_random_uuid(),8614,'↦','\ensuremath{\mapsto}','math','math','full',now(),now()),
(gen_random_uuid(),8736,'∠','\ensuremath{\angle}','math','math','full',now(),now()),
(gen_random_uuid(),8741,'∥','\ensuremath{\parallel}','math','math','full',now(),now()),
(gen_random_uuid(),8869,'⊥','\ensuremath{\perp}','math','math','full',now(),now()),
(gen_random_uuid(),8810,'≪','\ensuremath{\ll}','math','math','full',now(),now()),
(gen_random_uuid(),8811,'≫','\ensuremath{\gg}','math','math','full',now(),now()),
(gen_random_uuid(),8968,'⌈','\ensuremath{\lceil}','math','math','full',now(),now()),
(gen_random_uuid(),8969,'⌉','\ensuremath{\rceil}','math','math','full',now(),now()),
(gen_random_uuid(),8970,'⌊','\ensuremath{\lfloor}','math','math','full',now(),now()),
(gen_random_uuid(),8971,'⌋','\ensuremath{\rfloor}','math','math','full',now(),now()),
(gen_random_uuid(),8726,'∖','\ensuremath{\setminus}','math','math','full',now(),now()),
(gen_random_uuid(),8728,'∘','\ensuremath{\circ}','math','math','full',now(),now()),
(gen_random_uuid(),8857,'⊙','\ensuremath{\odot}','math','math','full',now(),now()),
(gen_random_uuid(),8796,'≜','\ensuremath{\triangleq}','math','math','full',now(),now()),
(gen_random_uuid(),8756,'∴','\ensuremath{\therefore}','math','math','full',now(),now()),
(gen_random_uuid(),8757,'∵','\ensuremath{\because}','math','math','full',now(),now()),
(gen_random_uuid(),8212,'—','\textemdash{}','typography','text','full',now(),now()),
(gen_random_uuid(),8211,'–','\textendash{}','typography','text','full',now(),now()),
(gen_random_uuid(),8230,'…','\ldots{}','typography','text','full',now(),now()),
(gen_random_uuid(),8226,'•','\textbullet{}','typography','text','full',now(),now()),
(gen_random_uuid(),176,'°','\textdegree{}','typography','text','full',now(),now()),
(gen_random_uuid(),167,'§','\S{}','typography','text','full',now(),now()),
(gen_random_uuid(),182,'¶','\P{}','typography','text','full',now(),now()),
(gen_random_uuid(),8224,'†','\dag{}','typography','text','full',now(),now()),
(gen_random_uuid(),8225,'‡','\ddag{}','typography','text','full',now(),now()),
(gen_random_uuid(),8220,'“','``','typography','text','full',now(),now()),
(gen_random_uuid(),8221,'”','''''','typography','text','full',now(),now()),
(gen_random_uuid(),8216,'‘','`','typography','text','full',now(),now()),
(gen_random_uuid(),8217,'’','''','typography','text','full',now(),now()),
(gen_random_uuid(),171,'«','\guillemotleft{}','typography','text','full',now(),now()),
(gen_random_uuid(),187,'»','\guillemotright{}','typography','text','full',now(),now()),
(gen_random_uuid(),169,'©','\textcopyright{}','typography','text','full',now(),now()),
(gen_random_uuid(),174,'®','\textregistered{}','typography','text','full',now(),now()),
(gen_random_uuid(),8482,'™','\texttrademark{}','typography','text','full',now(),now()),
(gen_random_uuid(),8364,'€','\texteuro{}','typography','text','full',now(),now()),
(gen_random_uuid(),163,'£','\pounds{}','typography','text','full',now(),now()),
(gen_random_uuid(),165,'¥','\textyen{}','typography','text','full',now(),now()),
(gen_random_uuid(),162,'¢','\textcent{}','typography','text','full',now(),now()),
(gen_random_uuid(),189,'½','\textonehalf{}','typography','text','full',now(),now()),
(gen_random_uuid(),188,'¼','\textonequarter{}','typography','text','full',now(),now()),
(gen_random_uuid(),190,'¾','\textthreequarters{}','typography','text','full',now(),now()),
(gen_random_uuid(),185,'¹','\textsuperscript{1}','typography','text','full',now(),now()),
(gen_random_uuid(),178,'²','\textsuperscript{2}','typography','text','full',now(),now()),
(gen_random_uuid(),179,'³','\textsuperscript{3}','typography','text','full',now(),now()),
(gen_random_uuid(),8242,'′','\textquotesingle{}','typography','text','full',now(),now()),
(gen_random_uuid(),8243,'″','\textquotedbl{}','typography','text','full',now(),now()),
(gen_random_uuid(),160,' ','~','typography','text','full',now(),now()),
(gen_random_uuid(),8201,' ','\,','typography','text','full',now(),now()),
(gen_random_uuid(),8239,' ','\,','typography','text','full',now(),now())
ON CONFLICT (codepoint) DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "latex_unicode_map");

            migrationBuilder.DropCheckConstraint(
                name: "ck_telemetry_event_kind",
                table: "import_telemetry_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_telemetry_event_kind",
                table: "import_telemetry_events",
                sql: "event_kind IN ('unknown_env','unhandled_token','silent_fallback','cell_cleanup_applied','partial_parse','expected_leak_hit','cmd_passthrough','unsupported_block_emitted','parser_warning')");
        }
    }
}
