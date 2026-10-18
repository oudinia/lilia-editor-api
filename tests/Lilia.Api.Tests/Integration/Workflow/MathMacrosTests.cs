using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Integration.Workflow;

/// <summary>
/// One set of maths macros for the editor and the PDF (18 Oct). The editor asks the server for the
/// macros it renders with; the PDF defines the same shortcuts, after the author's own preamble.
/// </summary>
[Collection("Integration")]
public class MathMacrosTests : IntegrationTestBase
{
    private readonly string _userId = $"mathmacros-{Guid.NewGuid():N}"[..28];
    private HttpClient _client = null!;

    public MathMacrosTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await SeedUserAsync(_userId);
        _client = CreateClientAs(_userId);
    }

    private async Task<Guid> NewDocumentAsync(string title, string? customPreamble = null)
    {
        var id = (await (await _client.PostAsJsonAsync("/api/documents", new { title })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        if (customPreamble is not null)
            (await _client.PutAsJsonAsync($"/api/documents/{id}", new { customPreamble })).EnsureSuccessStatusCode();
        return id;
    }

    private async Task EquationAsync(Guid doc, string latex, int order) =>
        (await _client.PostAsJsonAsync($"/api/documents/{doc}/blocks", new { type = "equation", content = new { latex, display = true }, sortOrder = order }))
            .EnsureSuccessStatusCode();

    private static string Text(byte[] pdf)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lilia-math-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, pdf);
        try
        {
            var p = Process.Start(new ProcessStartInfo("pdftotext", $"\"{path}\" -") { RedirectStandardOutput = true, UseShellExecute = false })!;
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return text;
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Every_shortcut_the_editor_shows_compiles_in_the_pdf()
    {
        var doc = await NewDocumentAsync("Shortcuts");
        var order = 0;
        foreach (var (name, body) in MathMacros.BuiltIn)
        {
            var args = string.Concat(Enumerable.Range(1, body.Count(c => c == '#')).Select(i => $"{{x{i}}}"));
            await EquationAsync(doc, $"{name}{args}", order++);
        }
        var check = await (await _client.PostAsync($"/api/latex/{doc}/validate", null)).Content.ReadFromJsonAsync<JsonElement>();
        check.GetProperty("valid").GetBoolean().Should().BeTrue(check.GetProperty("error").ToString());

        var res = await _client.GetAsync($"/api/documents/{doc}/export/pdf?engine=pdflatex");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        Text(await res.Content.ReadAsByteArrayAsync()).Should().Contain("Tr").And.Contain("rank");
    }

    [Fact]
    public async Task The_author_s_own_definition_wins_and_still_compiles()
    {
        // \newcommand{\abs} is common; Lilia's shortcuts are provided after the author's preamble,
        // so it is not "already defined", and it is the author's that prints.
        var doc = await NewDocumentAsync("Own abs", @"\newcommand{\abs}[1]{\lvert #1 \rvert_{\mathrm{mine}}}");
        await EquationAsync(doc, @"\abs{x}", 0);
        var res = await _client.GetAsync($"/api/documents/{doc}/export/pdf?engine=pdflatex");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        Text(await res.Content.ReadAsByteArrayAsync()).Should().Contain("mine");
    }

    [Fact]
    public async Task The_editor_gets_the_shortcuts_the_unit_stand_ins_and_the_document_s_macros()
    {
        var doc = await NewDocumentAsync("Macros",
            "\\newcommand{\\R}{\\mathbb{R}}\n\\newcommand{\\abs}[1]{\\lvert #1\\rvert}\n\\DeclareMathOperator*{\\argmax}{arg\\,max}\n\\DeclareMathOperator{\\Var}{Var}");
        var macros = (await _client.GetFromJsonAsync<JsonElement>($"/api/documents/{doc}/preview/math-macros")).GetProperty("macros");
        string M(string name) => macros.GetProperty(name).GetString()!;

        M(@"\bra").Should().Be(@"\langle #1 |", "a Lilia shortcut");
        M(@"\SI").Should().Contain("#1").And.Contain("#2", "a siunitx stand-in for KaTeX");
        M(@"\R").Should().Be(@"\mathbb{R}", "the document's own");
        M(@"\abs").Should().Be(@"\lvert #1\rvert", "the document's wins over Lilia's");
        M(@"\argmax").Should().Be(@"\operatorname*{arg\,max}");
        M(@"\Var").Should().Be(@"\operatorname{Var}");
        macros.TryGetProperty(@"\Re", out _).Should().BeFalse("LaTeX's own \\Re is not redefined");

        (await CreateClientAs($"other-{Guid.NewGuid():N}"[..20]).GetAsync($"/api/documents/{doc}/preview/math-macros"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "another user's document");
    }
}
