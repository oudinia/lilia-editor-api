using System.Text.RegularExpressions;
using FluentAssertions;

namespace Lilia.Api.Tests.Shared;

/// <summary>
/// The slices' first rule (2 Nov 2026, the owner: folders now, a test that keeps them apart): code outside
/// a slice uses it only through its contracts, never its internals. A slice is a folder of
/// <c>src/Lilia.Api/Features/</c>; its contracts are its interfaces and what sits in its <c>Dtos/</c> or
/// <c>Contracts/</c> folder. Everything else it declares (services, controllers, handlers) is internal.
///
/// <para>Read from the source, as the test selection reads it, so a use inside a method body counts too.
/// <c>Program.cs</c> is the composition root: it registers every slice's services and may name them.</para>
/// </summary>
public class SliceBoundaryTests
{
    private static readonly Regex Decl = new(@"\b(class|record|struct|interface|enum)\s+([A-Z]\w*)", RegexOptions.Compiled);
    private static readonly Regex Comment = new(@"//[^\n]*|/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Word = new(@"\b[A-Z]\w*\b", RegexOptions.Compiled);

    [Fact]
    public void No_code_outside_a_slice_names_its_internals()
    {
        var api = Path.Combine(RepoRoot(), "src", "Lilia.Api");
        var features = Path.Combine(api, "Features");
        var files = Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToDictionary(f => Path.GetRelativePath(api, f).Replace('\\', '/'), f => Comment.Replace(File.ReadAllText(f), ""));

        string? SliceOf(string rel) => rel.StartsWith("Features/") ? rel.Split('/')[1] : null;

        // Each slice's internal types: what it declares, less its contracts.
        var internals = new Dictionary<string, string>(); // type -> slice
        var declaredTwice = new HashSet<string>();
        foreach (var (rel, text) in files)
        {
            foreach (Match m in Decl.Matches(text))
            {
                var name = m.Groups[2].Value;
                if (!internals.TryAdd(name, SliceOf(rel) ?? "")) declaredTwice.Add(name);
                var slice = SliceOf(rel);
                var contract = m.Groups[1].Value == "interface" || rel.Contains("/Dtos/") || rel.Contains("/Contracts/");
                if (slice is null || contract) internals[name] = "";
            }
        }

        var crossings = new List<string>();
        foreach (var (rel, text) in files)
        {
            if (rel == "Program.cs") continue;
            var own = SliceOf(rel);
            foreach (var word in Word.Matches(text).Select(m => m.Value).Distinct())
            {
                // A name declared twice (a nested Row, Options) can't be told apart: not judged.
                if (declaredTwice.Contains(word) || !internals.TryGetValue(word, out var slice) || slice == "") continue;
                if (slice != own) crossings.Add($"{rel} uses {word}, internal to Features/{slice}");
            }
        }

        crossings.Should().BeEmpty("a slice is used through its interfaces or Dtos/, so it can change inside freely");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src", "Lilia.Api", "Lilia.Api.csproj"))) return dir.FullName;
        throw new InvalidOperationException("the repository's src/Lilia.Api was not found above " + AppContext.BaseDirectory);
    }
}
