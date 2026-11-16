using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// 16 Nov 2026 (the document sweep): a CV's PDF printed \maketitle above its Personal information block, the document's
/// name and the date as on an article. A Personal information block with no Title block is the header now.
/// </summary>
public class CvHeaderTests
{
    private static Block B(string type, object content, int order) => new()
    {
        Id = Guid.NewGuid(), Type = type, SortOrder = order, Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static Block Person(int order = 0) => B("personalInfo", new { name = "Ada Lovelace", headline = "Analyst" }, order);
    private static Block Title(int order = 0) => B("title", new { title = "My CV", author = "Ada", date = "" }, order);
    private static Block Para(int order) => B("paragraph", new { text = "Text." }, order);

    private static Document Doc(string category, params Block[] blocks)
    {
        var doc = new Document { Id = Guid.NewGuid(), Title = "T-073 · classic, cv", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11, DocumentCategory = category };
        foreach (var b in blocks) b.DocumentId = doc.Id;
        doc.Blocks = blocks.ToList();
        return doc;
    }

    [Fact]
    public void Personal_information_without_a_Title_block_is_the_header()
    {
        CvHeader.ReplacesTitle([Person(), Para(1)]).Should().BeTrue();
        CvHeader.ReplacesTitle([Title(), Person(1)]).Should().BeFalse("a Title block the author added keeps \\maketitle");
        CvHeader.ReplacesTitle([Para(0)]).Should().BeFalse("an article keeps it");
    }

    [Fact]
    public void The_PDF_of_a_CV_has_no_maketitle_but_keeps_its_metadata()
    {
        var latex = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(Doc("cv", Person(), Para(1)));
        latex.Should().NotContain(@"\maketitle").And.Contain(@"\title{");
    }

    [Fact]
    public void An_article_and_a_CV_with_a_Title_block_keep_maketitle()
    {
        var render = new RenderService(null!, NullLogger<RenderService>.Instance);
        render.RenderToLatex(Doc("article", Para(0))).Should().Contain(@"\maketitle");
        render.RenderToLatex(Doc("cv", Title(), Person(1))).Should().Contain(@"\maketitle");
    }

    [Fact]
    public void The_downloaded_tex_of_a_CV_has_no_maketitle()
    {
        var doc = Doc("cv", Person(), Para(1));
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance).BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions());
        tex.Should().NotContain(@"\maketitle");
        var article = Doc("article", Para(0));
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance).BuildSingleFileLatex(article, article.Blocks.ToList(), [], new LaTeXExportOptions())
            .Should().Contain(@"\maketitle");
    }
}
