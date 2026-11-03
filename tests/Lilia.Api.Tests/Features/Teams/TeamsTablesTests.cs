using System.Text.RegularExpressions;
using FluentAssertions;

namespace Lilia.Api.Tests.Features.Teams;

/// <summary>
/// Only Teams reads its tables (3 Nov 2026, Teams' bounded context, step 3): documents, access, people search
/// and plan limits ask <c>ITeamDirectory</c> which teams a person is in, their team roles, their peers and an
/// owner's seats, as Sharing does. Read from the source, as <c>SharingTablesTests</c> reads Sharing's.
/// </summary>
public class TeamsTablesTests
{
    /// <summary>Teams' own tables, as the database context names them.</summary>
    private static readonly HashSet<string> Own = ["Teams", "Groups", "GroupMembers"];

    private static readonly Regex Comment = new(@"//[^\n]*|/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex TableUse = new(@"\b(?:_context|_db|db|context|dbContext)\s*\.\s*(\w+)\b", RegexOptions.Compiled);

    [Fact]
    public void No_code_outside_Teams_names_its_tables()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var teams = Path.Combine(src, "Lilia.Api", "Features", "Teams");
        string[] exempt = ["Lilia.Infrastructure", "Lilia.Core"];

        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            if (exempt.Contains(rel.Split(Path.DirectorySeparatorChar)[0])) continue;
            if (file.StartsWith(teams + Path.DirectorySeparatorChar)) continue;
            if (rel.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            var lines = Comment.Replace(File.ReadAllText(file), "").Split('\n');
            for (var i = 0; i < lines.Length; i++)
                foreach (Match m in TableUse.Matches(lines[i]))
                    if (Own.Contains(m.Groups[1].Value))
                        found.Add($"{rel}:{i + 1} {m.Value}");
        }

        found.Should().BeEmpty("teams, groups and their members are Teams' to read; the others ask ITeamDirectory");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src", "Lilia.Api", "Lilia.Api.csproj"))) return dir.FullName;
        throw new InvalidOperationException("the repository's src/Lilia.Api was not found above " + AppContext.BaseDirectory);
    }
}
