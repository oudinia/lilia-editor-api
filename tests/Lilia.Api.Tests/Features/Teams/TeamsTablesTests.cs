using System.Text.RegularExpressions;
using FluentAssertions;

namespace Lilia.Api.Tests.Features.Teams;

/// <summary>
/// Only Teams reads its tables (3 Nov 2026, Teams' bounded context, step 3): documents, access, people search
/// and plan limits ask <c>ITeamDirectory</c> which teams a person is in, their team roles, their peers and an
/// owner's seats, as Sharing does. And Teams reads only its own (step 4): people, their default team and a
/// team's documents come from <c>IPeopleDirectory</c>, <c>ITeamAccounts</c> and <c>IDocumentDirectory</c>.
/// Read from the source, as <c>SharingTablesTests</c> reads Sharing's.
/// </summary>
public class TeamsTablesTests
{
    /// <summary>Teams' own tables, as the database context names them.</summary>
    private static readonly HashSet<string> Own = ["Teams", "Groups", "GroupMembers"];

    private static readonly Regex Comment = new(@"//[^\n]*|/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex TableUse = new(@"\b(?:_context|_db|db|context|dbContext)\s*\.\s*(\w+)\b", RegexOptions.Compiled);

    [Fact]
    public void Teams_names_no_table_of_another_context()
    {
        // Roles are shared with Sharing (the roles a share or a membership grants).
        var others = typeof(Lilia.Infrastructure.Data.LiliaDbContext).GetProperties()
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(Microsoft.EntityFrameworkCore.DbSet<>))
            .Select(p => p.Name)
            .Where(n => !Own.Contains(n) && n != "Roles")
            .ToHashSet();
        others.Should().Contain(["Users", "Documents", "Blocks"], "the context is read as expected");

        var teams = Path.Combine(RepoRoot(), "src", "Lilia.Api", "Features", "Teams");
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(teams, "*.cs", SearchOption.AllDirectories))
        {
            var lines = Comment.Replace(File.ReadAllText(file), "").Split('\n');
            for (var i = 0; i < lines.Length; i++)
                foreach (Match m in TableUse.Matches(lines[i]))
                    if (others.Contains(m.Groups[1].Value))
                        found.Add($"{Path.GetRelativePath(teams, file)}:{i + 1} {m.Value}");
        }

        found.Should().BeEmpty("Teams asks the people and document directories, and Accounts for the default team");
    }

    [Fact]
    public void No_navigation_crosses_Teams_line()
    {
        // Step 5 (3 Nov 2026): a team, its groups and members hold the ids of people and documents, and those
        // hold only a team's id. The foreign keys stay in the database. Roles are shared with Sharing.
        static Type ElementOf(Type t) => t.IsGenericType && t != typeof(string) ? t.GetGenericArguments()[0] : t;
        static bool IsEntity(Type t) => t.Namespace == "Lilia.Core.Entities";
        Type[] teams = [typeof(Lilia.Core.Entities.Team), typeof(Lilia.Core.Entities.Group), typeof(Lilia.Core.Entities.GroupMember)];
        var shared = typeof(Lilia.Core.Entities.Role);
        IEnumerable<string> NavigationsOf(Type t, Func<Type, bool> target) => t
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.GetGetMethod()!.IsVirtual && IsEntity(ElementOf(p.PropertyType)) && target(ElementOf(p.PropertyType)))
            .Select(p => $"{t.Name}.{p.Name}");

        var others = typeof(Lilia.Core.Entities.Document).Assembly.GetTypes()
            .Where(t => IsEntity(t) && t.IsClass && !teams.Contains(t) && t != shared).ToList();
        others.Should().Contain([typeof(Lilia.Core.Entities.Document), typeof(Lilia.Core.Entities.User)], "the entities are read as expected");

        teams.SelectMany(t => NavigationsOf(t, e => !teams.Contains(e) && e != shared)).Should().BeEmpty("Teams' entities keep ids, not links, out of it");
        others.SelectMany(t => NavigationsOf(t, teams.Contains)).Should().BeEmpty("no other entity links into Teams'");
    }

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
