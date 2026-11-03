using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Features.Sharing;

/// <summary>
/// Sharing reads only its own tables (2 Nov 2026, the bounded contexts, step 2), and only Sharing reads
/// them (step 3). A document's title and
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
    public void No_navigation_crosses_Sharings_line()
    {
        // Step 5 (3 Nov 2026): a share, comment, invite or notification holds the ids of a document, a person, a
        // group or a block, and those hold nothing of Sharing's. The foreign keys stay in the database; only the
        // code links went. Roles are shared with Teams (a member's team role), so they are left out of this.
        static Type ElementOf(Type t) => t.IsGenericType && t != typeof(string) ? t.GetGenericArguments()[0] : t;
        static bool IsEntity(Type t) => t.Namespace == "Lilia.Core.Entities";
        IEnumerable<string> NavigationsOf(Type t, Func<Type, bool> target) => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetGetMethod()!.IsVirtual && IsEntity(ElementOf(p.PropertyType)) && target(ElementOf(p.PropertyType)))
            .Select(p => $"{t.Name}.{p.Name}");

        var sharing = Owned.Where(t => t != typeof(Role)).ToHashSet();
        var others = typeof(Document).Assembly.GetTypes().Where(t => IsEntity(t) && t.IsClass && !Owned.Contains(t)).ToList();
        others.Should().Contain([typeof(Document), typeof(User), typeof(Lilia.Core.Entities.Group), typeof(Block)], "the entities are read as expected");

        sharing.SelectMany(t => NavigationsOf(t, e => !Owned.Contains(e))).Should().BeEmpty("Sharing's entities keep ids, not navigations, out of it");
        others.SelectMany(t => NavigationsOf(t, sharing.Contains)).Should().BeEmpty("no other entity navigates into Sharing's");
    }

    [Fact]
    public void No_code_outside_Sharing_names_its_tables()
    {
        // Step 3 (2 Nov 2026): access, the document list, people search, sign-up, account deletion, the
        // onboarding email and the import hints ask ISharingDirectory or ISharingAccounts instead.
        var own = typeof(LiliaDbContext).GetProperties()
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                        && Owned.Contains(p.PropertyType.GetGenericArguments()[0]) && p.PropertyType.GetGenericArguments()[0] != typeof(Role))
            .Select(p => p.Name)
            .ToHashSet();
        own.Should().Contain(["DocumentCollaborators", "DocumentPendingInvites", "DocumentGroups", "Comments"], "the context is read as expected");
        // On any value, the names nothing else uses: a DbSet reached another way, or a navigation such as
        // document.DocumentGroups. (Collaborators and Comments are also the import review's and DOCX's
        // names; step 5 removes those navigations, and the compiler then holds the line.)
        var distinct = own.Where(n => n.StartsWith("Document") || n == "CommentReplies").Append("DocumentCollaborations").ToHashSet();

        var src = Path.Combine(RepoRoot(), "src");
        string[] exempt = ["Lilia.Features.Sharing", "Lilia.Infrastructure", "Lilia.Core"];
        var outside = Directory.EnumerateDirectories(src).Where(d => !exempt.Contains(Path.GetFileName(d))).ToList();
        var found = outside.SelectMany(d => Uses(new Regex(@"\b(?:_context|_db|db|context|dbContext)\s*\.\s*(\w+)\b"), own, d))
            .Concat(outside.SelectMany(d => Uses(new Regex(@"\b[a-z_]\w*\s*\??\.\s*(\w+)\b"), distinct, d)))
            .Distinct()
            .ToList();
        found.Should().BeEmpty("Sharing's tables are read and written by Sharing; the others ask it");
    }

    private static List<string> Uses(Regex pattern, IReadOnlySet<string> names, string? root = null)
    {
        var dir = root ?? Path.Combine(RepoRoot(), "src", "Lilia.Features.Sharing");
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")) continue;
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
