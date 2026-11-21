using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The retest of the launch bot's three notebooks after API #677 (21 Nov 2026): B compiled, A and C still stopped:
/// "\mathrm allowed only in math mode" and "Missing $ inserted" (A l.604), "Limit controls must follow a math operator"
/// (A l.844), "Display math should end with $$" (A l.847), a printed <c>\textbackslash{}right</c> (A), "Missing $ inserted"
/// (C l.515) and "Extra }, or forgotten $" from <c>\textbackslash qquad</c> (C l.681). #677 read a list of commands with
/// flat arguments; Mathpix's text holds any maths: <c>\left … \right</c>, spacing, <c>\limits</c>, nested and escaped
/// arguments, and maths left open.
///
/// <para>Fuzz-style: maths commands × how Mathpix writes them in text (escaped as <c>\textbackslash name</c>,
/// <c>\textbackslash{}name</c>, <c>\textbackslash{} name</c>, or bare, with braced, unbraced, spaced or next-line
/// arguments) × where (a paragraph, a heading, a list item, bold text), plus the shapes the retest named. Each goes through
/// the bulk import's real placement (Mathpix's tex.zip, the LaTeX import, the scan cleanup); every block's LaTeX must have no
/// <c>\textbackslash</c>, its dollars paired, its braces balanced and no maths-only command outside maths; and the whole
/// document compiles with no LaTeX error (RequiresTeX).</para>
/// </summary>
public class ScanMathTextFuzzTests : IntegrationTestBase
{
    private static readonly RenderService Render = new(null!, NullLogger<RenderService>.Instance);
    private readonly string _userId = $"mathfuzz-{Guid.NewGuid():N}"[..28];

    public ScanMathTextFuzzTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await SeedUserAsync(_userId);
        await using var db = CreateDbContext();
        var pro = db.Plans.Single(p => p.Slug == "pro");
        db.UserPlans.Add(new UserPlan { Id = Guid.NewGuid(), UserId = _userId, PlanId = pro.Id, Status = "active", StartedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    // ── The cases ────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Maths commands and the arguments they take.</summary>
    internal static readonly (string Name, string[] Args)[] Commands =
    [
        ("alpha", []), ("beta", []), ("Gamma", []), ("varphi", []), ("hbar", []), ("ell", []), ("infty", []), ("partial", []),
        ("nabla", []), ("cdot", []), ("times", []), ("pm", []), ("leq", []), ("geq", []), ("neq", []), ("approx", []),
        ("equiv", []), ("to", []), ("rightarrow", []), ("Rightarrow", []), ("mapsto", []), ("in", []), ("subset", []),
        ("cup", []), ("cap", []), ("forall", []), ("exists", []), ("emptyset", []), ("circ", []), ("sin", []), ("log", []),
        ("exp", []), ("det", []), ("sum", []), ("prod", []), ("int", []), ("oint", []), ("lim", []), ("max", []),
        ("mathrm", ["d"]), ("mathbf", ["v"]), ("mathbb", ["R"]), ("mathcal", ["L"]), ("mathit", ["x"]), ("boldsymbol", ["F"]),
        ("operatorname", ["rot"]), ("vec", ["x"]), ("hat", ["n"]), ("bar", ["z"]), ("tilde", ["u"]), ("dot", ["q"]),
        ("ddot", ["q"]), ("overline", ["AB"]), ("widehat", ["f"]), ("sqrt", ["2"]),
        ("frac", ["1", "2"]), ("dfrac", ["a", "b"]), ("binom", ["n", "k"]),
    ];

    private static readonly string[] Operators = ["sum", "prod", "int", "oint", "lim", "max"];

    /// <summary>How Mathpix may write a command and its arguments in text.</summary>
    internal static IEnumerable<(string Form, string Written)> Forms(string name, string[] args)
    {
        string Esc(string a) => @"\{" + a + @"\}";
        var escArgs = string.Concat(args.Select(Esc));
        var bareArgs = string.Concat(args.Select(a => "{" + a + "}"));
        var scripts = Operators.Contains(name);
        yield return ("escaped", @"\textbackslash " + name + escArgs + (scripts ? @"\_\{i=1\}\textasciicircum{}\{n\}" : ""));
        yield return ("escaped{}", @"\textbackslash{}" + name + escArgs + (scripts ? @"\_\{i\}" : ""));
        yield return ("escaped{} spaced", @"\textbackslash{} " + name + " " + string.Concat(args.Select(a => @"\{ " + a + @" \} ")).TrimEnd());
        yield return ("bare", @"\" + name + bareArgs + (scripts ? "_{i=1}^{n}" : ""));
        if (args.Length > 0)
        {
            yield return ("bare unbraced", @"\" + name + " " + string.Concat(args.Select(a => a[..1])));
            yield return ("bare spaced", @"\" + name + string.Concat(args.Select(a => "{ " + a + " }")));
            yield return ("bare next line", @"\" + name + "\n" + bareArgs);
        }
        else yield return ("bare next line", @"\" + name + "\nx");
    }

    /// <summary>Where the maths stands.</summary>
    internal static readonly (string Where, Func<string, string> Wrap)[] Contexts =
    [
        ("paragraph", m => $"Before {m} after the text.\n\n"),
        ("heading", m => $"\\section*{{Heading {m} end}}\n\n"),
        ("list", m => $"\\begin{{itemize}}\n\\item Item {m} end\n\\end{{itemize}}\n\n"),
        ("bold", m => $"\\textbf{{Bold {m}}} after.\n\n"),
    ];

    /// <summary>The shapes the retest named, and their neighbours.</summary>
    internal static readonly (string Name, string Tex)[] Shapes =
    [
        ("escaped left right parens", @"Then \textbackslash left( x+1 \textbackslash right) is positive."),
        ("escaped left right braces", @"The set \textbackslash left\textbackslash \{ x \textbackslash mid x>0 \textbackslash right\textbackslash \} is open."),
        ("escaped left right brackets", @"On \textbackslash left[0,1\textbackslash right] the function."),
        ("escaped left right bars", @"The norm \textbackslash left| v \textbackslash right| is one."),
        ("escaped right alone", @"so that x \textbackslash right) holds."),
        ("escaped left alone", @"where \textbackslash left( a + b the rest."),
        ("bare left right", @"Then \left( \frac{a}{b} \right)^2 is positive."),
        ("escaped qquad", @"a = b \textbackslash qquad c = d \textbackslash quad e."),
        ("escaped qquad after maths", @"\textbackslash mathrm\{d\} x \textbackslash qquad \textbackslash frac\{1\}\{2\} done."),
        ("escaped thin spaces", @"\textbackslash int f(x) \textbackslash , \textbackslash mathrm\{d\} x \textbackslash ; ok."),
        ("escaped limits", @"\textbackslash sum\textbackslash limits\_\{i=1\}\textasciicircum{}\{n\} a\_i is finite."),
        ("escaped stray limits", @"Then x\textbackslash limits\_\{0\} and \textbackslash alpha\textbackslash nolimits here."),
        ("maths stray limits", @"Then $x\limits_{0} + \int\limits_0^1 f + \mathrm{d}\nolimits$ here."),
        ("nested escaped arguments", @"\textbackslash frac\{\textbackslash mathrm\{d\} y\}\{\textbackslash mathrm\{d\} x\} = \textbackslash sqrt\{\textbackslash frac\{a\}\{b\}\}."),
        ("escaped then maths", @"Value \textbackslash alpha$=1$ and $x$\textbackslash beta here."),
        ("display closed by one dollar", "Display:\n$$\na+b=c\n$\nafter it."),
        ("display left open before a blank line", "Display:\n$$\na+b=c\n\nAfter it."),
        ("display with escaped close", "Display:\n$$ x^2 \\$\\$ and text.\n\nNext."),
        ("inline left open", "Inline $x+y and the rest of the line.\n\nNext."),
        ("bracket display left open", "Display \\[ a+b\n\nNext."),
        ("stray closers", @"Text \) and \] stray."),
        ("mathrm in text forms", "Bare \\mathrm d and \\mathrm{ d } and \\mathrm dx and \\mathrm\n{d} end."),
        ("mathrm in section", "\\section*{Of \\mathrm{d} x and \\textbackslash mathrm\\{d\\} y}"),
        ("mathrm in bold", "\\textbf{The \\mathrm{d} x term} after."),
        ("text in escaped maths", @"\textbackslash frac\{\textbackslash text\{if \} x\}\{2\} here."),
        ("escaped section is text", @"Type \textbackslash section to begin."),
        ("subscripts escaped", @"Let \textbackslash alpha\_\{ij\} and \textbackslash beta\textasciicircum{}2 be."),
        ("Cyrillic around", @"Пусть \textbackslash alpha\_1 = \textbackslash frac\{1\}\{2\} и \textbackslash qquad дальше."),
    ];

    /// <summary>Every case as a body of LaTeX, each in its own paragraph, heading, list or bold text.</summary>
    internal static IEnumerable<(string Name, string Tex)> Cases()
    {
        foreach (var (name, args) in Commands)
            foreach (var (form, written) in Forms(name, args))
                foreach (var (where, wrap) in Contexts)
                    yield return ($"{name} {form} in {where}", wrap(written));
        foreach (var (name, tex) in Shapes) yield return (name, tex + "\n\n");
    }

    private static string Document(IEnumerable<(string Name, string Tex)> cases)
    {
        var sb = new StringBuilder("\\documentclass[10pt]{article}\n\\usepackage{amsmath}\n\\usepackage{amssymb}\n\\begin{document}\n\n");
        foreach (var (_, tex) in cases) sb.Append(tex);
        return sb.Append("\\end{document}\n").ToString();
    }

    // ── Exact outputs of the pass ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"Then \textbackslash left( x+1 \textbackslash right) is.", @"Then $\left( x+1 \right)$ is.")]
    [InlineData(@"The set \textbackslash left\textbackslash \{ x \textbackslash right\textbackslash \} is.", @"The set $\left\{ x \right\}$ is.")]
    [InlineData(@"so x \textbackslash right) holds.", @"so x $\left. \right)$ holds.")]
    [InlineData(@"a = b \textbackslash qquad c", @"a = b \qquad c")]
    [InlineData(@"\textbackslash mathrm\{d\} x \textbackslash qquad \textbackslash frac\{1\}\{2\} done.", @"$\mathrm{d} x \qquad \frac{1}{2}$ done.")]
    [InlineData(@"\textbackslash frac\{\textbackslash mathrm\{d\} y\}\{\textbackslash mathrm\{d\} x\} here", @"$\frac{\mathrm{d} y}{\mathrm{d} x}$ here")]
    [InlineData(@"\textbackslash sum\textbackslash limits\_\{i=1\}\textasciicircum{}\{n\} a\_i is", @"$\sum \limits_{i=1}^{n} a_i$ is")]
    [InlineData(@"Then x\textbackslash limits\_\{0\} here", @"Then x$_{0}$ here")]
    [InlineData(@"Then $x\limits_{0} + \int\limits_0^1 f$ here", @"Then $x_{0} + \int \limits_0^1 f$ here")]
    [InlineData("Bare \\mathrm d and \\mathrm dx end.", "Bare $\\mathrm d$ and $\\mathrm dx$ end.")]
    [InlineData("A \\mathrm\n{d} end.", "A $\\mathrm {d}$ end.")]
    [InlineData("$$\na+b\n$\nafter", "$$\na+b\n$$\nafter")]
    [InlineData("$$\na+b\n\nAfter", "$$\na+b$$\n\nAfter")]
    [InlineData("Inline $x+y and more\n\nNext", "Inline $x+y and more$\n\nNext")]
    [InlineData(@"Text \) and \] stray.", @"Text  and  stray.")]
    [InlineData(@"Value \textbackslash alpha$=1$ here", @"Value $\alpha=1$ here")]
    [InlineData(@"and $x$\textbackslash beta here", @"and $x\beta$ here")]
    [InlineData("and $$x$$\\alpha here", "and $$x$$ $\\alpha$ here")]
    [InlineData(@"Type \textbackslash section to begin.", @"Type \textbackslash section to begin.")]
    public void The_pass_sets_maths_in_text_as_maths_and_balances_it(string text, string expected) =>
        OcrMathText.MathCommandsAsMaths(text, out _).Should().Be(expected);

    private static string OneParagraph() =>
        string.Join(" and ", Commands.SelectMany(c => Forms(c.Name, c.Args)).Select(f => f.Written.Replace("\n", " ")))
        + " and " + string.Join(" and ", Shapes.Where(s => !s.Tex.Contains('\n') && !s.Tex.Contains("section")).Select(s => s.Tex));

    [Fact]
    public async Task One_paragraph_holding_every_command_in_every_form_reads_as_LaTeX_TeX_can_read()
    {
        var tex = OcrMathText.MathCommandsAsMaths(Document([("all", "Everything: " + OneParagraph() + "\n\n")]), out _);
        var doc = await new LatexParser().ParseTextAsync(tex);
        foreach (var p in doc.Elements.OfType<Lilia.Import.Models.ImportParagraph>())
        {
            var latex = Render.RenderBlockToLatex(PublisherClassTestsB("paragraph", new { text = p.Text }));
            Problem(latex).Should().BeNull(latex);
        }
    }

    private static Block PublisherClassTestsB(string type, object content) =>
        Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests.B(type, content);

    // ── Every case through the real placement ────────────────────────────────────────────────────────────────────

    private async Task<(IServiceScope Scope, Guid DocumentId)> PlaceAsync(string tex)
    {
        var documentId = Guid.NewGuid();
        var placeholder = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO documents (id, owner_id, title, created_at, updated_at)
                VALUES ({documentId}, {_userId}, {"Maths in text"}, {DateTime.UtcNow}, {DateTime.UtcNow})");
            db.Blocks.Add(new Block
            {
                Id = placeholder, DocumentId = documentId, Type = "paragraph", SortOrder = 0,
                Content = JsonDocument.Parse("""{"text":"Pages 1–10: being read…"}"""),
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var id = MathpixTexZipTests.PdfId;
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry($"{id}/{id}.tex").Open(), Encoding.UTF8))
            w.Write(tex);
        var scope = Fixture.Factory.Services.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<Lilia.Api.Services.IPdfImportPipeline>();
        await pipeline.PlaceLatexProjectAsync(_userId, "notebook.pdf", documentId, placeholder, applyPreamble: true, ms.ToArray(), null);
        return (scope, documentId);
    }

    private static readonly Regex MathsRegion = new(@"(?<!\\)\$\$.+?(?<!\\)\$\$|(?<![\\$])\$(?!\$).+?(?<!\\)\$|\\\(.+?\\\)|\\\[.+?\\\]", RegexOptions.Singleline);

    /// <summary>What is wrong with a block's LaTeX for TeX, or null.</summary>
    internal static string? Problem(string latex)
    {
        // An escaped command text has (\textbackslash section) is text, printed as written; any other is maths.
        if (latex.Replace(@"\textbackslash{}section", "").Contains("textbackslash", StringComparison.Ordinal)) return "\\textbackslash is printed";
        var dollars = Regex.Matches(latex, @"(?<!\\)\$").Count;
        if (dollars % 2 != 0) return "the dollars are not paired";
        var depth = 0;
        for (var i = 0; i < latex.Length; i++)
        {
            if (latex[i] == '\\') { i++; continue; }
            if (latex[i] == '{') depth++;
            else if (latex[i] == '}' && --depth < 0) return "a } closes nothing";
        }
        if (depth != 0) return "a { is never closed";
        var text = MathsRegion.Replace(latex, " ");
        var names = string.Join("|", Commands.Select(c => c.Name).Concat(["left", "right", "limits", "nolimits"]));
        if (Regex.Match(text, @"(?<!\\)\\(" + names + @")(?![A-Za-z])") is { Success: true } m) return $"\\{m.Groups[1].Value} is outside maths";
        return null;
    }

    [Fact]
    public async Task Every_case_imports_as_LaTeX_TeX_can_read()
    {
        var cases = Cases().ToList();
        cases.Count.Should().BeGreaterThan(1000);
        var (scope, documentId) = await PlaceAsync(Document(cases));
        using var _ = scope;
        await using var db = CreateDbContext();
        var blocks = await db.Blocks.AsNoTracking().Where(b => b.DocumentId == documentId).OrderBy(b => b.SortOrder).ToListAsync();
        blocks.Should().HaveCountGreaterThan(cases.Count / 2);
        var problems = blocks
            .Select(b => (Block: b, Latex: Render.RenderBlockToLatex(b)))
            .Select(x => (x.Block, x.Latex, Problem: Problem(x.Latex)))
            .Where(x => x.Problem is not null)
            .Select(x => $"{x.Problem}: {x.Latex.Trim()}  ← {x.Block.Content.RootElement.GetRawText()}")
            .ToList();
        problems.Should().BeEmpty();
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task For_real_a_document_holding_every_case_compiles_with_no_LaTeX_error()
    {
        var (scope, documentId) = await PlaceAsync(Document(Cases()));
        using var _ = scope;
        var exports = scope.ServiceProvider.GetRequiredService<Lilia.Api.Services.IDocumentExportService>();
        var (pdf, engine, problems) = await exports.ExportToPdfWithProblemsAsync(documentId, "latex", null, false);
        Lilia.Api.Services.PdfPages.LooksLikePdf(pdf).Should().BeTrue();
        problems.Should().BeEmpty($"every case compiles ({engine})");
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task For_real_one_paragraph_holding_every_command_in_every_form_compiles()
    {
        // All of them side by side in one paragraph: a run that swallowed its neighbour would show here.
        var paragraph = OneParagraph();
        var (scope, documentId) = await PlaceAsync(Document([("all", "Everything: " + paragraph + "\n\n")]));
        using var _ = scope;
        var exports = scope.ServiceProvider.GetRequiredService<Lilia.Api.Services.IDocumentExportService>();
        var (pdf, engine, problems) = await exports.ExportToPdfWithProblemsAsync(documentId, "latex", null, false);
        Lilia.Api.Services.PdfPages.LooksLikePdf(pdf).Should().BeTrue();
        problems.Should().BeEmpty($"the paragraph compiles ({engine})");
    }
}
