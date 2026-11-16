using FluentAssertions;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The document sweep's chunk 3 (L-368, L-397, 16 Nov 2026): in an Arabic or Hebrew document a code block was set right to
/// left: print('ok') printed as ('ok')print. Listings in a right-to-left document are now set in English (left to right).
/// Checked with lualatex on the sweep's exported document: print('ok'), the frame in place.
/// </summary>
public class RtlCodeBlockTests
{
    [Theory]
    [InlineData("ar")]
    [InlineData("he")]
    public void A_right_to_left_document_sets_its_code_left_to_right(string code)
    {
        var preamble = DocumentLanguage.Preamble(DocumentLanguage.Specs[code]);
        preamble.Should().Contain(@"\AddToHook{env/lstlisting/before}{\begin{otherlanguage}{english}}")
            .And.Contain(@"\AddToHook{env/lstlisting/after}{\end{otherlanguage}}");
        // English must be known to babel before a listing switches to it.
        preamble.IndexOf(@"{english}", StringComparison.Ordinal).Should().BeLessThan(preamble.IndexOf(@"\AddToHook", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("zh")]
    public void A_left_to_right_document_is_unchanged(string code)
        => DocumentLanguage.Preamble(DocumentLanguage.Specs[code]).Should().NotContain("env/lstlisting");
}
