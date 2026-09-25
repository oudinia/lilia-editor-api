using System.Diagnostics;
using System.Text.Json;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Typst;

/// <summary>
/// Builds documents for the Typst suite and compiles the result.
///
/// <para>The oracle here is the compiler, not a string match. Asserting that
/// "frac" appears in the output proves the regex fired; asserting that typst
/// compiles the document proves the output is Typst. The difference is the
/// whole point — a pass-through of LaTeX maths contains all the right
/// substrings and does not compile.</para>
/// </summary>
internal static class TypstHarness
{
    internal static Document Doc(string title = "A Document") => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = "typst-suite",
        Title = title,
        Language = "en",
        PaperSize = "a4",
        FontFamily = "serif",
        FontSize = 11,
        Columns = 1,
    };

    internal static Block Block(string type, object content, int sortOrder = 0) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        SortOrder = sortOrder,
    };

    internal static Block Para(string text, int sortOrder = 0) =>
        Block("paragraph", new { text }, sortOrder);

    internal sealed record CompileResult(bool Ok, string Error)
    {
        /// <summary>The first line typst complained about, for a readable assertion message.</summary>
        public string FirstProblem =>
            Error.Split('\n').FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase))
                ?.Trim() ?? Error.Split('\n').FirstOrDefault()?.Trim() ?? "";
    }

    /// <summary>
    /// Compile Typst source and read the PDF back as text (pdftotext) — for
    /// asserting what a reader would see, not just that it compiled. Null text
    /// when the compile failed; the error says why.
    /// </summary>
    internal static (string? Text, string Error) CompileToText(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"typst-text-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "main.typ"), source);
            var (code, _, err) = Run("typst", dir, "compile", "main.typ", "out.pdf");
            var pdf = Path.Combine(dir, "out.pdf");
            if (code != 0 || !File.Exists(pdf)) return (null, err);
            var (_, text, _) = Run("pdftotext", dir, "-layout", "out.pdf", "-");
            return (text, err);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static (int Code, string Out, string Err) Run(string exe, string dir, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = dir, RedirectStandardError = true, RedirectStandardOutput = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"{exe} did not start — is it on PATH?");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(60_000);
        return (p.ExitCode, stdout.Result, stderr);
    }

    /// <summary>Compile Typst source, and say why not if it fails.</summary>
    internal static CompileResult Compile(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"typst-suite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var tex = Path.Combine(dir, "main.typ");
            File.WriteAllText(tex, source);

            var psi = new ProcessStartInfo("typst")
            {
                WorkingDirectory = dir,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            psi.ArgumentList.Add("compile");
            psi.ArgumentList.Add("main.typ");
            psi.ArgumentList.Add("out.pdf");

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("typst did not start — is it on PATH?");
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(60_000);

            var pdf = Path.Combine(dir, "out.pdf");
            var ok = process.ExitCode == 0 && File.Exists(pdf) && new FileInfo(pdf).Length > 0;
            return new CompileResult(ok, stderr);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
