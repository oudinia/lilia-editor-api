using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// The knowledge base speaks English, French and Spanish, at three levels (the user,
    /// 12 Oct: "user documentation at different levels and a knowledge base for AI").
    ///
    /// - <c>lang</c>: one row per article and language; the key becomes (slug, lang).
    /// - <c>steps</c> / <c>beginner</c> / <c>expert</c>: the article's level sections —
    ///   the steps everyone follows, "what this means" in plain words, and the LaTeX
    ///   underneath. Ask Lilia picks by the author's level; the help page shows all three.
    /// - The search vector stems with the row's own language.
    ///
    /// Raw SQL like <c>AddKnowledgeBase</c>: the table stays out of the EF model.
    /// </summary>
    public partial class KbLanguagesAndLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE kb_articles ADD COLUMN lang character varying(5) NOT NULL DEFAULT 'en';
                ALTER TABLE kb_articles ADD COLUMN steps    text NOT NULL DEFAULT '';
                ALTER TABLE kb_articles ADD COLUMN beginner text NOT NULL DEFAULT '';
                ALTER TABLE kb_articles ADD COLUMN expert   text NOT NULL DEFAULT '';
                ALTER TABLE kb_articles ADD CONSTRAINT ck_kb_lang CHECK (lang IN ('en','fr','es'));

                ALTER TABLE kb_articles DROP CONSTRAINT kb_articles_pkey;
                ALTER TABLE kb_articles ADD PRIMARY KEY (slug, lang);

                DROP INDEX IF EXISTS ix_kb_articles_search;
                ALTER TABLE kb_articles DROP COLUMN search_vector;
                ALTER TABLE kb_articles ADD COLUMN search_vector tsvector GENERATED ALWAYS AS (
                    setweight(to_tsvector(CASE lang WHEN 'fr' THEN 'french'::regconfig WHEN 'es' THEN 'spanish'::regconfig ELSE 'english'::regconfig END, coalesce(title,    '')), 'A') ||
                    setweight(to_tsvector(CASE lang WHEN 'fr' THEN 'french'::regconfig WHEN 'es' THEN 'spanish'::regconfig ELSE 'english'::regconfig END, coalesce(keywords, '')), 'A') ||
                    setweight(to_tsvector(CASE lang WHEN 'fr' THEN 'french'::regconfig WHEN 'es' THEN 'spanish'::regconfig ELSE 'english'::regconfig END, coalesce(summary,  '')), 'B') ||
                    setweight(to_tsvector(CASE lang WHEN 'fr' THEN 'french'::regconfig WHEN 'es' THEN 'spanish'::regconfig ELSE 'english'::regconfig END, coalesce(body,     '')), 'C')
                ) STORED;
                CREATE INDEX ix_kb_articles_search ON kb_articles USING GIN (search_vector);
                CREATE INDEX ix_kb_articles_lang   ON kb_articles (lang);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM kb_articles WHERE lang <> 'en';
                DROP INDEX IF EXISTS ix_kb_articles_lang;
                DROP INDEX IF EXISTS ix_kb_articles_search;
                ALTER TABLE kb_articles DROP COLUMN search_vector;
                ALTER TABLE kb_articles DROP CONSTRAINT kb_articles_pkey;
                ALTER TABLE kb_articles ADD PRIMARY KEY (slug);
                ALTER TABLE kb_articles DROP CONSTRAINT ck_kb_lang;
                ALTER TABLE kb_articles DROP COLUMN expert;
                ALTER TABLE kb_articles DROP COLUMN beginner;
                ALTER TABLE kb_articles DROP COLUMN steps;
                ALTER TABLE kb_articles DROP COLUMN lang;
                ALTER TABLE kb_articles ADD COLUMN search_vector tsvector GENERATED ALWAYS AS (
                    setweight(to_tsvector('english', coalesce(title,    '')), 'A') ||
                    setweight(to_tsvector('english', coalesce(keywords, '')), 'A') ||
                    setweight(to_tsvector('english', coalesce(summary,  '')), 'B') ||
                    setweight(to_tsvector('english', coalesce(body,     '')), 'C')
                ) STORED;
                CREATE INDEX ix_kb_articles_search ON kb_articles USING GIN (search_vector);
                """);
        }
    }
}
