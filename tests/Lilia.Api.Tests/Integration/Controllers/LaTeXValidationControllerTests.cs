using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for per-block and document-level LaTeX validation.
/// These tests require pdflatex to be installed in the environment.
/// Run with: dotnet test --filter "Category=RequiresLatex"
/// Skip with: dotnet test --filter "Category!=RequiresLatex"
/// </summary>
[Collection("Integration")]
[Trait("Category", "RequiresLatex")]
public class LaTeXValidationControllerTests : IntegrationTestBase
{
    private const string UserId = "latex_test_user";

    public LaTeXValidationControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    // ── Helpers ──────────────────────────────────────────────────────

    private record ValidationResponse(bool Valid, string? Error, string[]? Warnings, Guid? BlockId);

    private async Task<ValidationResponse> ValidateBlock(Guid blockId)
    {
        var response = await Client.PostAsync($"/api/latex/block/{blockId}/validate", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ValidationResponse>())!;
    }

    private async Task<ValidationResponse> ValidateDocument(Guid documentId)
    {
        var response = await Client.PostAsync($"/api/latex/{documentId}/validate", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ValidationResponse>())!;
    }

    private async Task<(Lilia.Core.Entities.Document Doc, string UserId)> SetupDoc(string title = "Test Document")
    {
        var uid = UserId + Guid.NewGuid().ToString("N")[..8];
        await SeedUserAsync(uid);
        var doc = await SeedDocumentAsync(uid, title);
        return (doc, uid);
    }

    // ── Per-Block Validation ────────────────────────────────────────

    [Fact]
    public async Task ValidateBlock_Paragraph_ReturnsValid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Hello world."}""");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlock_HeadingLevel1_ReturnsValid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "heading", """{"text":"Introduction","level":1}""");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlock_EquationDisplay_ReturnsValid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "equation", """{"latex":"E = mc^2","displayMode":true}""");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlock_EquationInline_ReturnsValid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "equation", """{"latex":"x^2 + y^2","displayMode":false}""");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlock_EquationWithAlign_ReturnsValid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "equation",
            """{"latex":"\\begin{align}\na &= b \\\\\nc &= d\n\\end{align}","displayMode":false}""");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue("align environment should not be wrapped in $...$");
    }

    [Fact]
    public async Task ValidateBlock_EquationWithPlaceholder_ReturnsValid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "equation",
            """{"latex":"x + \\placeholder{} = y","displayMode":true}""");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue("\\placeholder{} should be stripped before compilation");
    }

    [Fact]
    public async Task ValidateBlock_TheoremDefinition_ReturnsValid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "theorem",
            """{"theoremType":"definition","title":"DLP","text":"Given a cyclic group $G$..."}""");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue("definition environment should be in the validation preamble");
    }

    [Theory]
    [InlineData("theorem", "theorem")]
    [InlineData("lemma", "lemma")]
    [InlineData("corollary", "corollary")]
    [InlineData("proposition", "proposition")]
    [InlineData("definition", "definition")]
    [InlineData("example", "example")]
    [InlineData("remark", "remark")]
    [InlineData("proof", "proof")]
    public async Task ValidateBlock_AllTheoremSubtypes_ReturnValid(string theoremType, string _)
    {
        var (doc, __) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "theorem",
            $"{{\"theoremType\":\"{theoremType}\",\"title\":\"Test\",\"text\":\"Statement.\"}}");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue($"theorem subtype '{theoremType}' should compile");
    }

    [Fact]
    public async Task ValidateBlock_Code_ReturnsValid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "code",
            """{"code":"def hello():\n    print('world')","language":"python"}""");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlock_OrderedList_ReturnsValid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "list",
            """{"items":["First item","Second item","Third item"],"listType":"ordered"}""");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlock_Blockquote_ReturnsValid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "blockquote",
            """{"text":"A wise quote from a great thinker."}""");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlock_Table_ReturnsValid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "table",
            """{"rows":[["Header 1","Header 2"],["Cell 1","Cell 2"],["Cell 3","Cell 4"]]}""");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlock_Abstract_ReturnsValid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "abstract",
            """{"text":"This paper presents a novel approach to..."}""");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlock_PageBreak_ReturnsValid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "pageBreak", "{}");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlock_TableOfContents_ReturnsValid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "tableOfContents", "{}");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue();
    }

    // ── Edge Cases ──────────────────────────────────────────────────

    [Fact]
    public async Task ValidateBlock_EquationWithUndefinedCommand_ReturnsInvalid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "equation",
            """{"latex":"\\signa + x","displayMode":true}""");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeFalse();
        result.Error.Should().Contain("Undefined control sequence");
    }

    [Fact]
    public async Task ValidateBlock_ParagraphWithSpecialChars_ReturnsValid()
    {
        var (doc, _) = await SetupDoc();
        var block = await SeedBlockAsync(doc.Id, "paragraph",
            """{"text":"Price is 100% of $50 & tax #1"}""");

        var result = await ValidateBlock(block.Id);
        result.Valid.Should().BeTrue("special characters should be escaped by RenderService");
    }

    [Fact]
    public async Task ValidateBlock_NonExistentBlock_Returns404()
    {
        var response = await Client.PostAsync($"/api/latex/block/{Guid.NewGuid()}/validate", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Document-Level Validation ───────────────────────────────────

    [Fact]
    public async Task ValidateDocument_MultipleBlockTypes_ReturnsValid()
    {
        var (doc, _) = await SetupDoc("Research Paper");
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Introduction","level":1}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"This is the introduction."}""", 1);
        await SeedBlockAsync(doc.Id, "equation", """{"latex":"E = mc^2","displayMode":true}""", 2);
        await SeedBlockAsync(doc.Id, "theorem", """{"theoremType":"theorem","title":"Main","text":"The result holds."}""", 3);
        await SeedBlockAsync(doc.Id, "theorem", """{"theoremType":"proof","title":"","text":"By induction."}""", 4);

        var result = await ValidateDocument(doc.Id);
        result.Valid.Should().BeTrue("a document with mixed block types should compile");
    }

    [Fact]
    public async Task ValidateDocument_WithAllTheoremTypes_ReturnsValid()
    {
        var (doc, _) = await SetupDoc("Theorem Test");
        var types = new[] { "theorem", "lemma", "corollary", "proposition", "definition", "example", "remark", "proof" };
        for (int i = 0; i < types.Length; i++)
        {
            await SeedBlockAsync(doc.Id, "theorem",
                $"{{\"theoremType\":\"{types[i]}\",\"title\":\"\",\"text\":\"Statement {i + 1}.\"}}", i);
        }

        var result = await ValidateDocument(doc.Id);
        result.Valid.Should().BeTrue("all theorem subtypes should be defined in the full document preamble");
    }
}
