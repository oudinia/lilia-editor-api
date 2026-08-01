using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The cache key for the Typst live-preview path.
///
/// <para>Keyed on a hash of the generated source rather than on the document
/// id. That is what makes the cache correct with no invalidation logic at all:
/// an edit changes the source, which changes the hash, which is a different
/// key. There is nothing to invalidate, so there is no way to serve a stale
/// preview — the failure a docId-keyed cache has to be defended against on
/// every write path.</para>
///
/// <para>Building the source is in-memory string work; compiling it is a
/// process spawn. Hashing the cheap half to decide whether to do the expensive
/// half is the entire point.</para>
/// </summary>
public class TypstPreviewEtagTests
{
    private const string Source = "#set page(paper: \"a4\")\n= Hello\n";

    [Fact]
    public void The_same_document_gets_the_same_tag()
    {
        // Without this the cache never hits and the ETag never matches, so
        // every request recompiles — exactly the behaviour being removed.
        PreviewController.ComputeEtag(Source, TypstOutputFormat.Svg)
            .Should().Be(PreviewController.ComputeEtag(Source, TypstOutputFormat.Svg));
    }

    [Fact]
    public void An_edit_gets_a_different_tag()
    {
        // The whole invalidation strategy. If an edit could collide with the
        // previous version, the user would be shown their old document.
        PreviewController.ComputeEtag(Source, TypstOutputFormat.Svg)
            .Should().NotBe(PreviewController.ComputeEtag(Source + "more", TypstOutputFormat.Svg));
    }

    [Fact]
    public void A_one_character_edit_is_enough()
    {
        // Typing is the use case; a single keystroke has to be visible.
        PreviewController.ComputeEtag("= Hello", TypstOutputFormat.Svg)
            .Should().NotBe(PreviewController.ComputeEtag("= Hellp", TypstOutputFormat.Svg));
    }

    [Fact]
    public void The_output_format_is_part_of_the_tag()
    {
        // Otherwise asking for SVG and then PDF of an unedited document would
        // match on the second request, and the caller would be handed SVG
        // bytes labelled application/pdf.
        PreviewController.ComputeEtag(Source, TypstOutputFormat.Svg)
            .Should().NotBe(PreviewController.ComputeEtag(Source, TypstOutputFormat.Pdf));
    }

    [Theory]
    [InlineData(TypstOutputFormat.Svg)]
    [InlineData(TypstOutputFormat.Pdf)]
    [InlineData(TypstOutputFormat.Png)]
    public void Every_format_produces_a_syntactically_valid_etag(TypstOutputFormat format)
    {
        // An unquoted ETag is not an ETag — proxies and browsers are entitled
        // to ignore it, which would silently disable revalidation and leave
        // the feature looking like it works while doing nothing.
        var etag = PreviewController.ComputeEtag(Source, format);

        etag.Should().StartWith("\"").And.EndWith("\"");
        etag.Trim('"').Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public void An_empty_document_still_gets_a_tag()
    {
        // A document with no blocks is a real state — a new document — and it
        // should be cacheable like any other rather than throwing.
        PreviewController.ComputeEtag("", TypstOutputFormat.Svg)
            .Should().MatchRegex("^\"[0-9a-f]{32}\"$");
    }

    [Fact]
    public void Unicode_content_hashes_without_loss()
    {
        // Multilingual documents are one of the reasons Typst is the fast tier
        // at all. Two different non-ASCII documents must not collide.
        PreviewController.ComputeEtag("= 中文", TypstOutputFormat.Svg)
            .Should().NotBe(PreviewController.ComputeEtag("= العربية", TypstOutputFormat.Svg));
    }
}
