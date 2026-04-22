using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Upgrades coverage_level for LaTeX kernel / common tokens that the
    /// parser auto-inserted as 'unsupported' on first encounter. Without
    /// this, a user who imports a basic .tex file sees `\documentclass`,
    /// `\usepackage`, `\includegraphics`, `equation`, `tabularx`, etc.
    /// flagged as unsupported on the Coverage tab even though Lilia has
    /// first-class handling for every one of them.
    ///
    /// Safe to re-run: updates-in-place only touch rows whose
    /// coverage_level is currently 'unsupported', so already-cataloged
    /// entries keep their existing (possibly richer) metadata.
    /// </summary>
    public partial class UpgradeKernelTokenCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE latex_tokens SET coverage_level = 'full',
                        notes = NULL,
                        updated_at = NOW()
 WHERE package_slug IS NULL
   AND coverage_level = 'unsupported'
   AND (name, kind) IN (
         ('documentclass',   'command'),
         ('usepackage',      'command'),
         ('includegraphics', 'command'),
         ('equation',        'environment'),
         ('tabularx',        'environment')
       );

UPDATE latex_tokens SET coverage_level = 'full',
                        notes = 'Length unit used by table / figure width arguments.',
                        updated_at = NOW()
 WHERE package_slug IS NULL
   AND coverage_level = 'unsupported'
   AND name = 'linewidth' AND kind = 'command';

-- Map the environments to their corresponding block types so the
-- Coverage tab's mapsToBlockType column is populated.
UPDATE latex_tokens SET maps_to_block_type = 'equation'
 WHERE package_slug IS NULL AND name = 'equation' AND kind = 'environment';

UPDATE latex_tokens SET maps_to_block_type = 'table'
 WHERE package_slug IS NULL AND name = 'tabularx' AND kind = 'environment';

UPDATE latex_tokens SET maps_to_block_type = 'figure'
 WHERE package_slug IS NULL AND name = 'includegraphics' AND kind = 'command';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op — restoring to 'unsupported' would regress the UX and
            // there's no value in reversing a data upgrade.
        }
    }
}
