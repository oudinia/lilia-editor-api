using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Features.Sharing;

/// <summary>
/// Sharing reads only its own tables (2 Nov 2026, the bounded contexts, step 2). A document's title and
/// owner, a person's name and email, a group's name come through <c>IDocumentDirectory</c>,
/// <c>IPeopleDirectory</c> and <c>ITeamDirectory</c>, never from the documents, users or groups tables,
/// directly or through a navigation (<c>c.User.Name</c>, <c>.Include(i =&gt; i.Document)</c>).
///
/// <para>Read from the source of <c>src/Lilia.Features.Sharing</c>, as the slice boundary test reads it.
/// Which tables are Sharing's is the list below; the others are found from the database context, so a table
/// added later is covered without changing this test.</para>
/// </summary>
public class SharingTablesTests
{
    /// <summary>Sharing's own: the shares with people and with groups, invites, comments, notifications, the roles it grants.</summary>
    private static readonly Type[] Owned =
    [
        typeof(DocumentCollaborator), typeof(DocumentGroup), typeof(DocumentPendingInvite),
        typeof(Comment), typeof(CommentReply), typeof(Notification), typeof(Role),
    ];

    private static readonly Regex Comment = new(@"//[^\n]*|/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void Sharing_names_no_table_of_another_context()
    {
        var others = typeof(LiliaDbContext).GetProperties()
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                        && !Owned.Contains(p.PropertyType.GetGenericArguments()[0]))
            .Select(p => p.Name)
            .ToHashSet();
        others.Should().Contain(["Documents", "Users", "Groups", "Blocks"], "the context is read as expected");

        var found = Uses(new Regex(@"\b_(?:context|db)\s*\.\s*(\w+)\b"), others);
        found.Should().BeEmpty("Sharing asks the directories for documents, people and groups");
    }

    [Fact]
    public void Sharing_follows_no_navigation_into_another_context()
    {
        // The navigations of Sharing's entities that lead out of it: User, Inviter, Document, Block, Group…
        static Type ElementOf(Type t) => t.IsGenericType && t != typeof(string) ? t.GetGenericArguments()[0] : t;
        var leaving = Owned
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p.GetGetMethod()!.IsVirtual)
            .Where(p => ElementOf(p.PropertyType) is { Namespace: "Lilia.Core.Entities" } e && !Owned.Contains(e))
            .Select(p => p.Name)
            .ToHashSet();
        leaving.Should().Contain(["User", "Document", "Inviter", "Group"], "the entities are read as expected");

        // A member access on a lower-case name (c.User, invite.Document): not the controller's own User.
        var found = Uses(new Regex(@"\b[a-z]\w*\s*\.\s*(\w+)\b"), leaving);
        found.Should().BeEmpty("a share keeps the ids; names come from the directories");
    }

    private static List<string> Uses(Regex pattern, IReadOnlySet<string> names)
    {
        var dir = Path.Combine(RepoRoot(), "src", "Lilia.Features.Sharing");
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            var lines = Comment.Replace(File.ReadAllText(file), "").Split('\n');
            for (var i = 0; i < lines.Length; i++)
                foreach (Match m in pattern.Matches(lines[i]))
                    if (names.Contains(m.Groups[1].Value))
                        hits.Add($"{Path.GetRelativePath(dir, file)}:{i + 1} {m.Value}");
        }
        return hits;
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src", "Lilia.Api", "Lilia.Api.csproj"))) return dir.FullName;
        throw new InvalidOperationException("the repository's src/Lilia.Api was not found above " + AppContext.BaseDirectory);
    }
}
