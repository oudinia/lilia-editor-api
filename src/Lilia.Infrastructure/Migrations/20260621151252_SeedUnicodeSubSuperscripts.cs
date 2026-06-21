using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedUnicodeSubSuperscripts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Gap-closing rows surfaced by unmapped_unicode_char telemetry:
            // subscript/superscript digits and a few math extras seen in prose.
            migrationBuilder.Sql(@"INSERT INTO latex_unicode_map (id, codepoint, character, replacement, category, mode, coverage_level, created_at, updated_at) VALUES
(gen_random_uuid(),8320,'₀','\ensuremath{_0}','subscript','math','full',now(),now()),
(gen_random_uuid(),8321,'₁','\ensuremath{_1}','subscript','math','full',now(),now()),
(gen_random_uuid(),8322,'₂','\ensuremath{_2}','subscript','math','full',now(),now()),
(gen_random_uuid(),8323,'₃','\ensuremath{_3}','subscript','math','full',now(),now()),
(gen_random_uuid(),8324,'₄','\ensuremath{_4}','subscript','math','full',now(),now()),
(gen_random_uuid(),8325,'₅','\ensuremath{_5}','subscript','math','full',now(),now()),
(gen_random_uuid(),8326,'₆','\ensuremath{_6}','subscript','math','full',now(),now()),
(gen_random_uuid(),8327,'₇','\ensuremath{_7}','subscript','math','full',now(),now()),
(gen_random_uuid(),8328,'₈','\ensuremath{_8}','subscript','math','full',now(),now()),
(gen_random_uuid(),8329,'₉','\ensuremath{_9}','subscript','math','full',now(),now()),
(gen_random_uuid(),8330,'₊','\ensuremath{_+}','subscript','math','full',now(),now()),
(gen_random_uuid(),8331,'₋','\ensuremath{_-}','subscript','math','full',now(),now()),
(gen_random_uuid(),8345,'ₙ','\ensuremath{_n}','subscript','math','full',now(),now()),
(gen_random_uuid(),8304,'⁰','\ensuremath{^0}','superscript','math','full',now(),now()),
(gen_random_uuid(),8308,'⁴','\ensuremath{^4}','superscript','math','full',now(),now()),
(gen_random_uuid(),8309,'⁵','\ensuremath{^5}','superscript','math','full',now(),now()),
(gen_random_uuid(),8310,'⁶','\ensuremath{^6}','superscript','math','full',now(),now()),
(gen_random_uuid(),8311,'⁷','\ensuremath{^7}','superscript','math','full',now(),now()),
(gen_random_uuid(),8312,'⁸','\ensuremath{^8}','superscript','math','full',now(),now()),
(gen_random_uuid(),8313,'⁹','\ensuremath{^9}','superscript','math','full',now(),now()),
(gen_random_uuid(),8314,'⁺','\ensuremath{^+}','superscript','math','full',now(),now()),
(gen_random_uuid(),8315,'⁻','\ensuremath{^-}','superscript','math','full',now(),now()),
(gen_random_uuid(),8305,'ⁱ','\ensuremath{^i}','superscript','math','full',now(),now()),
(gen_random_uuid(),8319,'ⁿ','\ensuremath{^n}','superscript','math','full',now(),now()),
(gen_random_uuid(),8722,'−','\ensuremath{-}','math','math','full',now(),now()),
(gen_random_uuid(),8242,'′','\ensuremath{\prime}','math','math','full',now(),now()),
(gen_random_uuid(),8243,'″','\ensuremath{\prime\prime}','math','math','full',now(),now()),
(gen_random_uuid(),8776,'≈','\ensuremath{\approx}','math','math','full',now(),now()),
(gen_random_uuid(),8801,'≡','\ensuremath{\equiv}','math','math','full',now(),now())
ON CONFLICT (codepoint) DO NOTHING;");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM latex_unicode_map WHERE codepoint IN (8320,8321,8322,8323,8324,8325,8326,8327,8328,8329,8330,8331,8345,8304,8308,8309,8310,8311,8312,8313,8314,8315,8305,8319,8722,8242,8243,8776,8801);");

        }
    }
}
