using System.Diagnostics;
using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The PDF of an unchanged document is not compiled again (the user, 18 Oct): a compile took 4–5 s
/// every time it was asked for. The key is what the engine is given, so any change compiles anew.
/// </summary>
public class CompileCacheTests
{
    private const string Tex = "\\documentclass{article}\\begin{document}Cached zebra.\\end{document}";

    private static CompileCache.Entry E(int pdfBytes) => new(new byte[pdfBytes], "log", "aux");

    [Fact]
    public void The_key_changes_with_the_source_the_engine_the_mode_and_every_file_beside_it()
    {
        var dir = Directory.CreateTempSubdirectory("lilia-cc-").FullName;
        try
        {
            var baseKey = CompileCache.Key(Tex, "pdflatex", true, dir);
            CompileCache.Key(Tex, "pdflatex", true, dir).Should().Be(baseKey, "the same input is the same compile");
            CompileCache.Key(Tex + " ", "pdflatex", true, dir).Should().NotBe(baseKey, "an edit");
            CompileCache.Key(Tex, "xelatex", true, dir).Should().NotBe(baseKey, "another engine");
            CompileCache.Key(Tex, "pdflatex", false, dir).Should().NotBe(baseKey, "strict and tolerant differ");

            Directory.CreateDirectory(Path.Combine(dir, "figures"));
            File.WriteAllBytes(Path.Combine(dir, "figures", "a.png"), [1, 2, 3]);
            var withFigure = CompileCache.Key(Tex, "pdflatex", true, dir);
            withFigure.Should().NotBe(baseKey, "a figure was added");
            File.WriteAllBytes(Path.Combine(dir, "figures", "a.png"), [1, 2, 4]);
            CompileCache.Key(Tex, "pdflatex", true, dir).Should().NotBe(withFigure, "the figure changed, under the same name");
            File.Move(Path.Combine(dir, "figures", "a.png"), Path.Combine(dir, "figures", "b.png"));
            CompileCache.Key(Tex, "pdflatex", true, dir).Should().NotBe(withFigure, "the figure was renamed");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void It_keeps_to_its_size_dropping_the_least_recently_used()
    {
        var cache = new CompileCache(maxBytes: 3000);
        cache.Put("a", E(1000));
        cache.Put("b", E(1000));
        cache.Get("a").Should().NotBeNull(); // a is now the most recent
        cache.Put("c", E(1000));
        cache.Bytes.Should().BeLessThanOrEqualTo(3000);
        cache.Get("b").Should().BeNull("b was the least recently used");
        cache.Get("a").Should().NotBeNull();
        cache.Get("c").Should().NotBeNull();
    }

    [Fact]
    public void A_PDF_too_large_for_the_cache_is_not_kept()
    {
        var cache = new CompileCache(maxBytes: 1000);
        cache.Put("big", E(5000));
        cache.Get("big").Should().BeNull();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public async Task Two_requests_for_the_same_compile_at_once_share_one_run()
    {
        var cache = new CompileCache();
        var runs = 0;
        var gate = new TaskCompletionSource();
        async Task<CompileCache.Entry> Compile()
        {
            Interlocked.Increment(ref runs);
            await gate.Task;
            return E(10);
        }
        var first = cache.GetOrCompileAsync("k", Compile);
        var second = cache.GetOrCompileAsync("k", Compile);
        gate.SetResult();
        var results = await Task.WhenAll(first, second);
        runs.Should().Be(1);
        results.Count(r => r.Hit).Should().Be(1, "one compiled, the other waited for it");
        (await cache.GetOrCompileAsync("k", Compile)).Hit.Should().BeTrue("and it is kept");
        runs.Should().Be(1);
    }

    [Fact]
    public async Task A_failed_compile_is_not_kept()
    {
        var cache = new CompileCache();
        var act = () => cache.GetOrCompileAsync("k", () => throw new InvalidOperationException("LaTeX compilation failed"));
        await act.Should().ThrowAsync<InvalidOperationException>();
        cache.Get("k").Should().BeNull();
        (await cache.GetOrCompileAsync("k", () => Task.FromResult(E(10)))).Hit.Should().BeFalse("it compiles again");
    }

    [Fact]
    public async Task The_second_compile_of_an_unchanged_document_is_served_from_the_cache()
    {
        var service = new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance);
        var tex = Tex.Replace("Cached zebra.", $"Cached zebra {Guid.NewGuid():N}.");
        var hitsBefore = CompileCache.Shared.Hits;

        var sw = Stopwatch.StartNew();
        var (first, _) = await service.RenderToPdfTolerantWithAuxAsync(tex);
        var compileMs = sw.ElapsedMilliseconds;
        sw.Restart();
        var (second, _) = await service.RenderToPdfTolerantWithAuxAsync(tex);
        var cachedMs = sw.ElapsedMilliseconds;

        second.Should().Equal(first);
        CompileCache.Shared.Hits.Should().BeGreaterThan(hitsBefore);
        cachedMs.Should().BeLessThan(Math.Max(50, compileMs / 4), $"compiling took {compileMs} ms");

        var (edited, _) = await service.RenderToPdfTolerantWithAuxAsync(tex.Replace("zebra", "yak"));
        edited.Should().NotEqual(first, "an edit compiles anew");
    }
}
