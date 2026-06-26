using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// The Lilia knowledge base — a per-tool / per-concept, screenshot-free help
    /// catalog the AI ("Ask Lilia") can search and point authors to, and the public
    /// site can render. DB-driven like the other catalogs (latex_tokens, tools), with
    /// Postgres full-text search (a STORED tsvector + GIN) so the AI can discover the
    /// right article by intent. Content is authored as embedded `Kb/*.md` files and
    /// upserted into this table at startup (see <c>KbSeeder</c>); the table is the
    /// queryable, FTS-ranked store.
    ///
    /// Raw SQL (not an EF entity) on purpose: keeps the article store out of the EF
    /// model so the generated tsvector column + GIN live in one place and future model
    /// snapshots aren't entangled with FTS internals EF can't express cleanly.
    /// </summary>
    public partial class AddKnowledgeBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE kb_articles (
                    slug          character varying(120) PRIMARY KEY,
                    title         character varying(200) NOT NULL,
                    summary       character varying(500) NOT NULL DEFAULT '',
                    body          text                   NOT NULL DEFAULT '',
                    tool_slug     character varying(120) NULL,
                    skill_id      character varying(120) NULL,
                    audience      character varying(20)  NOT NULL DEFAULT 'all',
                    tags          text[]                 NOT NULL DEFAULT '{}',
                    keywords      text                   NOT NULL DEFAULT '',
                    sort_order    integer                NOT NULL DEFAULT 0,
                    enabled       boolean                NOT NULL DEFAULT true,
                    created_at    timestamp with time zone NOT NULL DEFAULT now(),
                    updated_at    timestamp with time zone NOT NULL DEFAULT now(),
                    search_vector tsvector GENERATED ALWAYS AS (
                        setweight(to_tsvector('english', coalesce(title,    '')), 'A') ||
                        setweight(to_tsvector('english', coalesce(keywords, '')), 'A') ||
                        setweight(to_tsvector('english', coalesce(summary,  '')), 'B') ||
                        setweight(to_tsvector('english', coalesce(body,     '')), 'C')
                    ) STORED,
                    CONSTRAINT ck_kb_audience CHECK (audience IN ('all','beginner','intermediate','advanced'))
                );
                CREATE INDEX ix_kb_articles_search  ON kb_articles USING GIN (search_vector);
                CREATE INDEX ix_kb_articles_tool    ON kb_articles (tool_slug) WHERE tool_slug IS NOT NULL;
                CREATE INDEX ix_kb_articles_enabled ON kb_articles (enabled);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS kb_articles;");
        }
    }
}
