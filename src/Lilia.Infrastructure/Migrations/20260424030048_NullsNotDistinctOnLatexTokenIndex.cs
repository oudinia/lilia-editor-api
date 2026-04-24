using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Hardens the `(name, kind, package_slug)` unique index on
    /// latex_tokens with `NULLS NOT DISTINCT` (Postgres 15+, we're on 18).
    ///
    /// Why: Postgres's default is `NULLS DISTINCT`, which means two rows
    /// with the same (name, kind) and NULL package_slug don't conflict
    /// on the unique index. Migrations using
    /// `INSERT … ON CONFLICT (name, kind, package_slug) DO UPDATE` on
    /// kernel-scope rows therefore silently create duplicates instead of
    /// updating the existing row. Exactly this bit us with the
    /// pass-through env catalog-ging (spacing was inserted twice because
    /// the original unsupported row's NULL did not match the new row's
    /// NULL for the index's purposes).
    ///
    /// With NULLS NOT DISTINCT, kernel-scope rows now collide correctly,
    /// both at INSERT time and during `ON CONFLICT` resolution. Future
    /// catalog migrations using the upsert pattern stay idempotent
    /// without needing per-row NOT EXISTS guards.
    ///
    /// Safe to apply now: verified by query at migration-design time
    /// that no remaining kernel-scope dupes exist. Any future collision
    /// would be a catalog-authoring bug worth failing on.
    /// </summary>
    public partial class NullsNotDistinctOnLatexTokenIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop + recreate the index with NULLS NOT DISTINCT. Doing
            // this as raw SQL because EF's CreateIndex fluent API
            // doesn't expose the PG-specific nulls-distinct option.
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ux_latex_token_name_kind_pkg;
CREATE UNIQUE INDEX ux_latex_token_name_kind_pkg
  ON latex_tokens (name, kind, package_slug) NULLS NOT DISTINCT;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ux_latex_token_name_kind_pkg;
CREATE UNIQUE INDEX ux_latex_token_name_kind_pkg
  ON latex_tokens (name, kind, package_slug);
");
        }
    }
}
