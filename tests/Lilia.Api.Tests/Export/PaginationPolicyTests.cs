using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Export;

/// <summary>
/// P1.1 — the pagination policy emitted into every non-beamer preamble.
///
/// Two properties matter more than the individual lines:
///
/// <list type="number">
/// <item><b>Adding the feature must not re-typeset anything.</b> A document
/// whose author has expressed no preference keeps its class default, so no
/// bottom-fill command is emitted at all.</item>
/// <item><b>The widow/club penalty must follow the bottom policy.</b>
/// Forbidding widows (10000) is only safe when the page bottom may run short.
/// Under \flushbottom LaTeX stretches the page instead, which puts back the
/// gaps the policy exists to remove.</item>
/// </list>
///
/// The class defaults asserted below were measured, not assumed — \@textbottom
/// after each class loads (pdflatex/MiKTeX, 2026-07-30): article and report
/// one-column one-side are already \raggedbottom; book, any twoside and any
/// twocolumn are \flushbottom.
/// </summary>
public class PaginationPolicyTests
{
    private static Document NewDoc(Action<Document>? configure = null)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Title = "Pagination Policy Test",
            Language = "en",
            PaperSize = "a4",
            FontFamily = "serif",
            FontSize = 12,
            Columns = 1,
            ColumnSeparator = "none",
            ColumnGap = 1.5,
        };
        configure?.Invoke(doc);
        return doc;
    }

    private static string Layout(Document doc) =>
        LaTeXPreambleBuilder.BuildLayoutPreamble(doc);

    // ── Float containment ──────────────────────────────────────────────

    [Fact]
    public void Every_document_gets_placeins_so_floats_cannot_leave_their_section()
    {
        Layout(NewDoc()).Should().Contain("\\usepackage{placeins}");
    }

    [Fact]
    public void Placeins_options_go_through_PassOptionsToPackage_not_a_bracketed_usepackage()
    {
        // An imported preamble may already have loaded placeins. A bracketed
        // \usepackage[section]{placeins} would then be an option clash and fail
        // the compile; PassOptionsToPackage degrades to a no-op instead. Same
        // idiom as hyperref in LaTeXPreamble.Packages.
        var layout = Layout(NewDoc());
        layout.Should().Contain("\\PassOptionsToPackage{section}{placeins}");
        layout.Should().NotContain("\\usepackage[section]{placeins}");
    }

    // ── The no-silent-change guarantee ─────────────────────────────────

    [Fact]
    public void No_stated_preference_leaves_the_class_default_alone()
    {
        var layout = Layout(NewDoc());
        layout.Should().NotContain("\\raggedbottom");
        layout.Should().NotContain("\\flushbottom");
    }

    [Fact]
    public void Ragged_is_emitted_only_when_the_author_asked_for_it()
    {
        var layout = Layout(NewDoc(d => d.PaginationPolicy = "ragged"));
        layout.Should().Contain("\\raggedbottom");
        layout.Should().NotContain("\\flushbottom");
    }

    [Fact]
    public void Flush_is_emitted_only_when_the_author_asked_for_it()
    {
        var layout = Layout(NewDoc(d => d.PaginationPolicy = "flush"));
        layout.Should().Contain("\\flushbottom");
        layout.Should().NotContain("\\raggedbottom");
    }

    [Theory]
    [InlineData("  Ragged  ", "\\raggedbottom")]
    [InlineData("FLUSH", "\\flushbottom")]
    public void Policy_value_is_read_case_and_whitespace_insensitively(string stored, string expected)
    {
        Layout(NewDoc(d => d.PaginationPolicy = stored)).Should().Contain(expected);
    }

    [Fact]
    public void An_unrecognised_policy_value_is_treated_as_no_preference()
    {
        // The DB CHECK constraint already restricts this column, so a stray
        // value means someone wrote around it. Falling back to "leave the class
        // alone" is the conservative reading.
        var layout = Layout(NewDoc(d => d.PaginationPolicy = "justified"));
        layout.Should().NotContain("\\raggedbottom");
        layout.Should().NotContain("\\flushbottom");
    }

    // ── Penalty follows the effective bottom policy ────────────────────

    [Fact]
    public void Article_starts_ragged_so_widows_can_be_forbidden_outright()
    {
        var layout = Layout(NewDoc());
        layout.Should().Contain("\\widowpenalty=10000");
        layout.Should().Contain("\\clubpenalty=10000");
        layout.Should().Contain("\\displaywidowpenalty=10000");
    }

    [Fact]
    public void Book_starts_flush_so_widows_are_only_discouraged()
    {
        // book is two-sided by default, which puts it under \flushbottom.
        // Forbidding widows there would force page stretching instead.
        var layout = Layout(NewDoc(d => d.LatexDocumentClass = "book"));
        layout.Should().Contain("\\widowpenalty=300");
        layout.Should().NotContain("=10000");
    }

    [Fact]
    public void Twoside_starts_flush_even_for_article()
    {
        var layout = Layout(NewDoc(d => d.LatexDocumentClassOptions = "twoside"));
        layout.Should().Contain("\\widowpenalty=300");
    }

    [Fact]
    public void Explicit_oneside_overrides_a_twoside_class_default()
    {
        var layout = Layout(NewDoc(d =>
        {
            d.LatexDocumentClass = "book";
            d.LatexDocumentClassOptions = "oneside";
        }));
        layout.Should().Contain("\\widowpenalty=10000");
    }

    [Fact]
    public void Twocolumn_starts_flush_and_beats_an_explicit_oneside()
    {
        // Two-side and two-column each switch the standard classes to
        // \flushbottom independently — a one-sided two-column article is still
        // flush-bottomed, which is why twocolumn is tested before oneside.
        var layout = Layout(NewDoc(d =>
        {
            d.Columns = 2;
            d.LatexDocumentClassOptions = "oneside";
        }));
        layout.Should().Contain("\\widowpenalty=300");
    }

    [Fact]
    public void Balanced_columns_do_not_count_as_twocolumn()
    {
        // BuildClassDirective strips the twocolumn class option when balanced
        // columns are on, because multicol owns the column flow — so the page
        // is still ragged-bottomed.
        var layout = Layout(NewDoc(d =>
        {
            d.Columns = 2;
            d.BalancedColumns = true;
        }));
        layout.Should().Contain("\\widowpenalty=10000");
    }

    [Fact]
    public void An_explicit_policy_overrides_the_class_default_for_the_penalty_too()
    {
        var layout = Layout(NewDoc(d =>
        {
            d.LatexDocumentClass = "book";
            d.PaginationPolicy = "ragged";
        }));
        layout.Should().Contain("\\raggedbottom");
        layout.Should().Contain("\\widowpenalty=10000");
    }

    // ── Out-of-scope classes ──────────────────────────────────────────

    [Theory]
    [InlineData("beamer")]
    [InlineData("beamerposter")]
    public void Presentation_classes_get_no_pagination_policy_at_all(string className)
    {
        // beamer frames are not pages: they never run short, floats do not
        // migrate between them, and \raggedbottom is meaningless. Emitting the
        // policy would change existing presentations for no benefit.
        var layout = Layout(NewDoc(d =>
        {
            d.LatexDocumentClass = className;
            d.PaginationPolicy = "ragged";
        }));
        layout.Should().NotContain("placeins");
        layout.Should().NotContain("widowpenalty");
        layout.Should().NotContain("\\raggedbottom");
    }

    [Fact]
    public void An_unshipped_class_falls_back_to_article_and_still_gets_the_policy()
    {
        // mnras is not in SafeDocumentClasses, so the document compiles as
        // article — and must be treated as article for pagination too.
        var layout = Layout(NewDoc(d => d.LatexDocumentClass = "mnras"));
        layout.Should().Contain("\\usepackage{placeins}");
        layout.Should().Contain("\\widowpenalty=10000");
    }
}
