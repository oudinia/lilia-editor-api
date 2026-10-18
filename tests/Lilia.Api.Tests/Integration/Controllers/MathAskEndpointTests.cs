using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Api.Tests.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Ask Lilia reading maths through <c>POST /api/ai/ask</c> with <c>math</c> (18 Oct), end to end: the
/// photo reaches the model as an image, the document's shortcuts are offered to it, and each formula
/// is checked by real TeX with the document's preamble. The model is scripted (no AI is called).
/// </summary>
[Collection("Integration")]
public class MathAskEndpointTests : FourCallersTestBase, IDisposable
{
    private readonly ScriptedChatClient _chat = new();
    private readonly WebApplicationFactory<Program> _factory;

    // A 1×1 PNG: the model is scripted, so what it shows does not matter, only that it is sent as an image.
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    public MathAskEndpointTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _factory = fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("AI:Anthropic:ApiKey", "sk-test-not-a-key");
            b.UseSetting("AI:Enabled", "true");
            b.ConfigureTestServices(s =>
            {
                foreach (var d in s.Where(d => d.ServiceType == typeof(IChatClient)).ToList()) s.Remove(d);
                s.AddSingleton<IChatClient>(_chat);
            });
        });
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient Caller(string userId)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", userId);
        return c;
    }

    private static object Photo(Guid? doc, object? math = null) => new
    {
        message = "",
        documentId = doc?.ToString(),
        attachments = new[] { new { name = "board.png", mediaType = "image/png", dataBase64 = Png } },
        math = math ?? new { },
    };

    // Unique per run, so the check's cache cannot answer from an earlier run.
    private static string Tag() => $@"\text{{{Guid.NewGuid():N}}}";

    [Fact]
    public async Task A_stranger_is_refused_and_the_model_is_not_called()
    {
        var s = await SeedSharedDocumentAsync();
        using var c = Caller(StrangerId);
        var res = await c.PostAsJsonAsync("/api/ai/ask", Photo(s.DocumentId));
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await res.Content.ReadAsStringAsync()).Should().Contain("no-access");
        _chat.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_to_read_is_refused_before_the_model()
    {
        using var c = Caller(OwnerId);
        var res = await c.PostAsJsonAsync("/api/ai/ask", new { message = "hello", math = new { } });
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await res.Content.ReadAsStringAsync()).Should().Contain("nothing-to-read");
        _chat.Calls.Should().BeEmpty();
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task A_photo_is_read_with_the_documents_shortcuts_and_checked_by_its_preamble()
    {
        var s = await SeedSharedDocumentAsync();
        await using (var db = CreateDbContext())
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE documents SET custom_preamble = {0} WHERE id = {1}", @"\newcommand{\Prob}{\mathbb{P}}", s.DocumentId);
        using var c = Caller(ViewerId);   // reading writes nothing: a viewer may too
        var tag = Tag();
        _chat.Enqueue($"Name: Union bound\nDisplay: equation\n```latex\n\\Prob(A \\cup B) \\le \\Prob(A) + \\Prob(B) {tag}\n```");

        var res = await c.PostAsJsonAsync("/api/ai/ask", Photo(s.DocumentId, new { afterBlockId = s.BlockId }));

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        var f = body.GetProperty("math").GetProperty("formulas")[0];
        f.GetProperty("ok").GetBoolean().Should().BeTrue("\\Prob is the document's own, and its preamble is the check's");
        f.GetProperty("name").GetString().Should().Be("Union bound");
        f.GetProperty("display").GetBoolean().Should().BeTrue();
        body.GetProperty("math").GetProperty("attempts").GetInt32().Should().Be(1);

        var sent = _chat.Calls.Single();
        sent.First().Text.Should().Contain(@"\Prob = \mathbb{P}").And.Contain("Private text", "the paragraph before, for its notation");
        sent.Last().Contents.OfType<DataContent>().Should().ContainSingle(d => d.MediaType == "image/png");
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task Without_a_document_Lilias_shortcuts_check_and_an_unprintable_formula_is_retried()
    {
        using var c = Caller(OwnerId);
        var tag = Tag();
        _chat.Enqueue(
            $"```latex\n\\abs{{x}} + \\nosuchmacro{{y}} {tag}\n```",
            $"```latex\n\\abs{{x}} + y {tag}\n```");

        var res = await c.PostAsJsonAsync("/api/ai/ask", Photo(null));

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var math = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("math");
        math.GetProperty("attempts").GetInt32().Should().Be(2);
        math.GetProperty("formulas")[0].GetProperty("ok").GetBoolean().Should().BeTrue("\\abs is Lilia's own shortcut");
        _chat.Calls[1].Last().Text.Should().Contain("formula 1:").And.Contain("Undefined control sequence");
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task Pasted_text_is_read_too_and_inline_maths_is_checked_in_a_sentence()
    {
        using var c = Caller(OwnerId);
        var tag = Tag();
        _chat.Enqueue($"Display: inline\n```latex\nx^2 + y^2 = r^2 {tag}\n```");

        var res = await c.PostAsJsonAsync("/api/ai/ask", new { message = "", math = new { text = "x² + y² = r²" } });

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var f = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("math").GetProperty("formulas")[0];
        f.GetProperty("display").GetBoolean().Should().BeFalse();
        f.GetProperty("ok").GetBoolean().Should().BeTrue();
        _chat.Calls.Single().Last().Text.Should().Contain("Pasted:\nx² + y² = r²");
    }
}
