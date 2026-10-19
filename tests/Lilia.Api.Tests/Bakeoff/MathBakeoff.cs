using System.Diagnostics;
using System.Globalization;
using System.Text;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lilia.Api.Tests.Bakeoff;

/// <summary>
/// Which model reads maths best, for what (18 Oct; the user: "how is mathpix used, does it help, LLM
/// helps, what about tokens"). Not part of CI: it calls the real model and costs money, so it runs
/// only with LILIA_MATH_BAKEOFF=1 and a key (ops/math-bakeoff.sh, which never prints it).
///
/// <para>30 equations are printed by TeX; each is read as a clean image, as a phone photo
/// (tilted, off-white, blurred, noisy, JPEG), and as that photo at under half the size, through the same prompt, check and retry as Ask Lilia
/// (<see cref="MathAskRunner"/>). A reading counts as right when TeX prints it exactly as it printed
/// the original, whatever its spelling (\dfrac or \frac, spaces). Tokens, cost and time are kept.</para>
/// </summary>
[Collection("Integration")]
[Trait("Category", "Bakeoff")]
public class MathBakeoff : IntegrationTestBase
{
    private readonly ITestOutputHelper _out;
    public MathBakeoff(TestDatabaseFixture fixture, ITestOutputHelper output) : base(fixture) => _out = output;

    public static readonly string[] Truth =
    {
        @"x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}",
        @"\int_0^\infty e^{-x^2}\,dx = \frac{\sqrt{\pi}}{2}",
        @"\sum_{n=1}^{\infty} \frac{1}{n^2} = \frac{\pi^2}{6}",
        @"e^{i\pi} + 1 = 0",
        @"\nabla \cdot \mathbf{E} = \frac{\rho}{\varepsilon_0}",
        @"\frac{\partial u}{\partial t} = \alpha \frac{\partial^2 u}{\partial x^2}",
        @"\begin{pmatrix} a & b \\ c & d \end{pmatrix}^{-1} = \frac{1}{ad - bc} \begin{pmatrix} d & -b \\ -c & a \end{pmatrix}",
        @"f(x) = \begin{cases} x^2 & x \ge 0 \\ -x & x < 0 \end{cases}",
        @"\lim_{x \to 0} \frac{\sin x}{x} = 1",
        @"\mathbb{E}[X] = \sum_{i} x_i \, p(x_i)",
        @"P(A \mid B) = \frac{P(B \mid A)\,P(A)}{P(B)}",
        @"\hat{\beta} = (X^\top X)^{-1} X^\top y",
        @"\oint_C \mathbf{F} \cdot d\mathbf{r} = \iint_S (\nabla \times \mathbf{F}) \cdot d\mathbf{S}",
        @"i\hbar \frac{\partial}{\partial t} \Psi = \hat{H} \Psi",
        @"\binom{n}{k} = \frac{n!}{k!\,(n-k)!}",
        @"\sqrt[3]{x^3 + y^3}",
        @"\begin{aligned} a + b &= c \\ 2a - b &= 0 \end{aligned}",
        @"\forall \varepsilon > 0 \; \exists \delta > 0 : |x - a| < \delta \Rightarrow |f(x) - f(a)| < \varepsilon",
        @"A \cup (B \cap C) = (A \cup B) \cap (A \cup C)",
        @"\mathcal{L}(\theta) = -\sum_{i=1}^{N} y_i \log \hat{y}_i",
        @"\det(A - \lambda I) = 0",
        @"F = G \frac{m_1 m_2}{r^2}",
        @"\left( \sum_{k=1}^n a_k b_k \right)^2 \le \left( \sum_{k=1}^n a_k^2 \right) \left( \sum_{k=1}^n b_k^2 \right)",
        @"\vec{v} = \frac{d\vec{r}}{dt}",
        @"\int_a^b f'(x)\,dx = f(b) - f(a)",
        @"\sigma = \sqrt{\frac{1}{N} \sum_{i=1}^{N} (x_i - \mu)^2}",
        @"\mathrm{softmax}(z)_i = \frac{e^{z_i}}{\sum_j e^{z_j}}",
        @"\|x\|_2 = \sqrt{x_1^2 + \cdots + x_n^2}",
        @"\lfloor x \rfloor \le x < \lfloor x \rfloor + 1",
        @"a^2 + b^2 = c^2",
    };

    private sealed record Row(string Model, int Formula, string Variant, bool Read, bool Prints, bool Same,
        int Attempts, int InputTokens, int OutputTokens, decimal CostUsd, long Ms, string Latex);

    [Fact]
    public async Task Read_the_set_with_each_model()
    {
        if (Environment.GetEnvironmentVariable("LILIA_MATH_BAKEOFF") != "1") return;
        var key = Environment.GetEnvironmentVariable("AI__Anthropic__ApiKey");
        Assert.False(string.IsNullOrWhiteSpace(key), "The bake-off needs AI__Anthropic__ApiKey (ops/math-bakeoff.sh).");
        var models = (Environment.GetEnvironmentVariable("LILIA_MATH_BAKEOFF_MODELS") ?? "claude-haiku-5-5,claude-sonnet-5,claude-sonnet-5-5")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var limit = int.TryParse(Environment.GetEnvironmentVariable("LILIA_MATH_BAKEOFF_LIMIT"), out var l) ? l : Truth.Length;
        var outDir = Environment.GetEnvironmentVariable("LILIA_MATH_BAKEOFF_OUT") ?? Path.Combine(Path.GetTempPath(), "lilia-math-bakeoff");
        Directory.CreateDirectory(outDir);

        var tex = Fixture.Factory.Services.GetRequiredService<ILaTeXRenderService>();
        var preamble = Lilia.Engines.MathMacros.PreambleShortcuts;
        async Task<byte[]?> Print(string latex)
        {
            try { return await tex.RenderBlockToPngAsync($"\\[ {latex} \\]", preamble, 200); }
            catch { return null; }
        }
        async Task<IReadOnlyList<string?>> Check(IReadOnlyList<MathAskReplyFormula> fs, CancellationToken _)
        {
            var errors = new List<string?>();
            foreach (var f in fs)
            {
                var r = await tex.ValidateAsync(Lilia.Engines.LaTeXPreamble.WrapForValidation(AskLiliaService.Body(f))
                    .Replace("\\begin{document}", preamble + "\n\\begin{document}"));
                errors.Add(r.Valid ? null : (r.ParsedError?.ErrorRaw ?? r.Error ?? "error").Split('\n')[0]);
            }
            return errors;
        }

        // The images, once.
        var images = new List<(int Formula, string Variant, byte[] Bytes, string Media, byte[] Printed)>();
        for (var i = 0; i < Math.Min(limit, Truth.Length); i++)
        {
            var printed = await Print(Truth[i]) ?? throw new InvalidOperationException($"Formula {i + 1} does not print.");
            images.Add((i, "clean", printed, "image/png", printed));
            images.Add((i, "photo", Photo(printed, seed: i), "image/jpeg", printed));
            await File.WriteAllBytesAsync(Path.Combine(outDir, $"f{i + 1:00}-clean.png"), printed);
            await File.WriteAllBytesAsync(Path.Combine(outDir, $"f{i + 1:00}-photo.jpg"), images[^1].Bytes);
            // The same photo at under half the size: fewer image tokens, and how far reading holds up.
            images.Add((i, "small", Photo(printed, seed: i, scale: 0.45f), "image/jpeg", printed));
            await File.WriteAllBytesAsync(Path.Combine(outDir, $"f{i + 1:00}-small.jpg"), images[^1].Bytes);
            // (Enlarging the small photo before sending it was measured on 19 Oct: Sonnet 5.5 69% → 70%,
            // Haiku 43% → 57%, for 40% more tokens. Not worth it with Sonnet 5.5, so not done.)
        }
        var only = (Environment.GetEnvironmentVariable("LILIA_MATH_BAKEOFF_VARIANTS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        if (only.Count > 0) images.RemoveAll(x => !only.Contains(x.Variant));

        // Real photos, when given: each image beside a .tex of the same name holding what it shows.
        var realDir = Environment.GetEnvironmentVariable("LILIA_MATH_BAKEOFF_PHOTOS");
        var realNames = new Dictionary<int, string>();
        if (!string.IsNullOrWhiteSpace(realDir) && Directory.Exists(realDir))
        {
            var k = 100;
            foreach (var file in Directory.GetFiles(realDir).Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).Order())
            {
                var truthFile = Path.ChangeExtension(file, ".tex");
                if (!File.Exists(truthFile)) { _out.WriteLine($"{Path.GetFileName(file)}: no .tex beside it, skipped"); continue; }
                var printedTruth = await Print((await File.ReadAllTextAsync(truthFile)).Trim());
                if (printedTruth is null) { _out.WriteLine($"{Path.GetFileName(truthFile)} does not print, skipped"); continue; }
                var media = file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
                images.Add((k, "real", await File.ReadAllBytesAsync(file), media, printedTruth));
                realNames[k++] = Path.GetFileName(file);
            }
        }

        var rows = new List<Row>();
        foreach (var model in models)
        {
            using var chat = new Anthropic.AnthropicClient { ApiKey = key }.AsIChatClient(model);
            foreach (var (formula, variant, bytes, media, printed) in images)
            {
                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System, MathAsk.SystemPrompt(MathAsk.Shortcuts(null), null, ProficiencyGuidance.For(Proficiency.Intermediate))),
                    new(ChatRole.User, new List<AIContent> { new TextContent(MathAsk.UserMessage(null, null)), new DataContent(bytes, media) }),
                };
                var clock = Stopwatch.StartNew();
                MathAskRun run;
                try { run = await new MathAskRunner(chat, Check).RunAsync(messages, new ChatOptions { ModelId = model, MaxOutputTokens = 2048 }, default); }
                catch (Exception e) { _out.WriteLine($"{model} f{formula + 1} {variant}: {e.Message}"); continue; }
                var ms = clock.ElapsedMilliseconds;
                var read = run.Proposal.Formulas.FirstOrDefault();
                var same = read is not null && Same(printed, await Print(read.Latex));
                var cost = AiArchitectPricing.ComputeCostUsd(model, run.InputTokens, run.OutputTokens);
                rows.Add(new Row(model, formula + 1, variant, read is not null, read?.Ok == true, same,
                    run.Proposal.Attempts, run.InputTokens, run.OutputTokens, cost, ms, read?.Latex ?? ""));
                _out.WriteLine($"{model} f{formula + 1} {variant}: same={same} prints={read?.Ok} in={run.InputTokens} out={run.OutputTokens} {ms} ms");
            }
        }

        var csv = new StringBuilder("model,formula,variant,read,prints,same,attempts,input_tokens,output_tokens,cost_usd,ms,latex\n");
        foreach (var r in rows)
            csv.AppendLine(string.Join(",", r.Model, r.Formula, r.Variant, r.Read, r.Prints, r.Same, r.Attempts, r.InputTokens, r.OutputTokens,
                r.CostUsd.ToString(CultureInfo.InvariantCulture), r.Ms, "\"" + r.Latex.Replace("\"", "\"\"").Replace("\n", " ") + "\""));
        await File.WriteAllTextAsync(Path.Combine(outDir, "results.csv"), csv.ToString());

        var variants = rows.Select(r => r.Variant).Distinct().ToList();
        var md = new StringBuilder("| Model | Images | Right (prints the same) | " + string.Join(" | ", variants.Select(v => char.ToUpper(v[0]) + v[1..]))
            + " | Prints | Input tokens / image | Output tokens / image | Cost / image | Time / image |\n|" + string.Concat(Enumerable.Repeat("---|", 9 + variants.Count)) + "\n");
        foreach (var g in rows.GroupBy(r => r.Model))
        {
            var n = g.Count();
            string Pct(Func<Row, bool> p, IEnumerable<Row> of) { var s = of.ToList(); return s.Count == 0 ? "-" : $"{100.0 * s.Count(p) / s.Count:0}%"; }
            md.AppendLine($"| {g.Key} | {n} | {Pct(r => r.Same, g)} | " + string.Join(" | ", variants.Select(v => Pct(r => r.Same, g.Where(r => r.Variant == v))))
                + $" | {Pct(r => r.Prints, g)} | {g.Average(r => r.InputTokens):0} | {g.Average(r => r.OutputTokens):0} | ${g.Average(r => r.CostUsd):0.0000} | {g.Average(r => r.Ms) / 1000:0.0} s |");
        }
        foreach (var (k, name) in realNames) md.AppendLine($"\nReal photo {k}: {name}");
        await File.WriteAllTextAsync(Path.Combine(outDir, "summary.md"), md.ToString());
        _out.WriteLine(md.ToString());
    }

    /// <summary>A clean print made to look like a phone photo of a page.</summary>
    internal static byte[] Photo(byte[] png, int seed, float scale = 1f)
    {
        using var full = SKBitmap.Decode(png);
        using var src = scale == 1f ? full.Copy()
            : full.Resize(new SKImageInfo(Math.Max(8, (int)(full.Width * scale)), Math.Max(8, (int)(full.Height * scale))), new SKSamplingOptions(SKCubicResampler.Mitchell));
        var w = src.Width + 120; var h = src.Height + 100;
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        var c = surface.Canvas;
        c.Clear(new SKColor(238, 232, 218));
        c.Save();
        c.Translate(w / 2f, h / 2f);
        c.RotateDegrees(seed % 2 == 0 ? 2.5f : -2f);
        c.Scale(0.92f);
        using var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(1.1f, 1.1f), BlendMode = SKBlendMode.Multiply };
        using var img = SKImage.FromBitmap(src);
        c.DrawImage(img, -src.Width / 2f, -src.Height / 2f, blur);
        c.Restore();
        // Uneven light, then grain.
        using var shade = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h),
            new[] { new SKColor(255, 255, 255, 0), new SKColor(60, 50, 30, 50) }, SKShaderTileMode.Clamp) };
        c.DrawRect(0, 0, w, h, shade);
        var rnd = new Random(seed);
        using var dot = new SKPaint();
        for (var k = 0; k < w * h / 25; k++)
        {
            var v = (byte)rnd.Next(60, 200);
            dot.Color = new SKColor(v, v, v, (byte)rnd.Next(10, 45));
            c.DrawPoint(rnd.Next(w), rnd.Next(h), dot);
        }
        using var snap = surface.Snapshot();
        using var data = snap.Encode(SKEncodedImageFormat.Jpeg, 50);
        return data.ToArray();
    }

    /// <summary>Two prints are the same when they have the same size and at most 0.5% of pixels differ.</summary>
    internal static bool Same(byte[] a, byte[]? b)
    {
        if (b is null) return false;
        using var x = SKBitmap.Decode(a);
        using var y = SKBitmap.Decode(b);
        if (Math.Abs(x.Width - y.Width) > 2 || Math.Abs(x.Height - y.Height) > 2) return false;
        int w = Math.Min(x.Width, y.Width), h = Math.Min(x.Height, y.Height), diff = 0;
        for (var j = 0; j < h; j++)
            for (var i = 0; i < w; i++)
            {
                var p = x.GetPixel(i, j); var q = y.GetPixel(i, j);
                if (Math.Abs(p.Red + p.Green + p.Blue - q.Red - q.Green - q.Blue) > 192) diff++;
            }
        return diff <= w * h * 0.005;
    }
}
