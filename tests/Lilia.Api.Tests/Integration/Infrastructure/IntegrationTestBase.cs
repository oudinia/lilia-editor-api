using System.Text.Json;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Integration.Infrastructure;

[Collection("Integration")]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly TestDatabaseFixture Fixture;
    protected readonly HttpClient Client;

    protected IntegrationTestBase(TestDatabaseFixture fixture)
    {
        Fixture = fixture;
        Client = fixture.Factory.CreateClient();
    }

    protected HttpClient CreateAnonymousClient()
    {
        var client = Fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        return client;
    }

    protected IDisposable AuthenticateAs(string userId, string? email = null, string? name = null)
    {
        return TestAuthHandler.SetClaims(userId, email, name);
    }

    protected HttpClient CreateClientAs(string userId, string? email = null, string? name = null)
    {
        var client = Fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Test-Email", email ?? $"{userId}@lilia.test");
        client.DefaultRequestHeaders.Add("X-Test-Name", name ?? userId);
        return client;
    }

    protected LiliaDbContext CreateDbContext()
    {
        var scope = Fixture.Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
    }

    protected async Task<User> SeedUserAsync(string userId, string? email = null, string? name = null)
    {
        await using var db = CreateDbContext();
        var user = new User
        {
            Id = userId,
            Email = email ?? $"{userId}@lilia.test",
            Name = name ?? userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    protected async Task<Document> SeedDocumentAsync(string ownerId, string? title = null)
    {
        await using var db = CreateDbContext();
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Title = title ?? "Test Document",
            Language = "en",
            PaperSize = "a4",
            FontFamily = "serif",
            FontSize = 12,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    protected async Task<Block> SeedBlockAsync(Guid documentId, string? type = null, string? contentJson = null, int sortOrder = 0)
    {
        await using var db = CreateDbContext();
        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Type = type ?? "paragraph",
            Content = JsonDocument.Parse(contentJson ?? """{"text":"Hello"}"""),
            SortOrder = sortOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Blocks.Add(block);
        await db.SaveChangesAsync();
        return block;
    }

    protected async Task<BibliographyEntry> SeedBibliographyEntryAsync(Guid documentId, string? citeKey = null, string? entryType = null, string? dataJson = null)
    {
        await using var db = CreateDbContext();
        var entry = new BibliographyEntry
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            CiteKey = citeKey ?? "smith2024",
            EntryType = entryType ?? "article",
            Data = JsonDocument.Parse(dataJson ?? """{"title":"Test Article","author":"Smith, John","year":"2024"}"""),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.BibliographyEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    protected async Task<Label> SeedLabelAsync(string userId, string? name = null, string? color = null)
    {
        await using var db = CreateDbContext();
        var label = new Label
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name ?? "Test Label",
            Color = color ?? "#FF0000",
            CreatedAt = DateTime.UtcNow
        };
        db.Labels.Add(label);
        await db.SaveChangesAsync();
        return label;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        TestAuthHandler.ClearClaims();
        await CleanupTestDataAsync();
    }

    private async Task CleanupTestDataAsync()
    {
        await using var db = CreateDbContext();

        // Delete test data while preserving system-seeded templates/formulas/snippets.
        // We delete in dependency order to avoid FK violations.
        await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM comment_replies;
            DELETE FROM comments;
            DELETE FROM import_block_comments;
            DELETE FROM import_review_activities;
            DELETE FROM import_block_reviews;
            DELETE FROM import_review_collaborators;
            DELETE FROM import_review_sessions;
            DELETE FROM document_snapshots;
            DELETE FROM conversion_audits;
            DELETE FROM sync_history;
            DELETE FROM ai_chats;
            DELETE FROM purchases;
            DELETE FROM invitations;
            DELETE FROM organization_members;
            DELETE FROM organizations;
            DELETE FROM jobs;
            DELETE FROM assets;
            DELETE FROM document_versions;
            DELETE FROM document_labels;
            DELETE FROM document_collaborators;
            DELETE FROM document_groups;
            DELETE FROM bibliography_entries;
            DELETE FROM blocks;
            DELETE FROM documents;
            DELETE FROM labels;
            DELETE FROM user_preferences;
            DELETE FROM templates WHERE is_system = false;
            DELETE FROM formulas WHERE is_system = false;
            DELETE FROM snippets WHERE is_system = false;
            DELETE FROM passkeys;
            DELETE FROM two_factors;
            DELETE FROM sessions;
            DELETE FROM accounts;
            DELETE FROM group_members;
            DELETE FROM groups;
            DELETE FROM roles;
            DELETE FROM teams;
            DELETE FROM verifications;
            UPDATE templates SET user_id = NULL;
            UPDATE formulas SET user_id = NULL;
            UPDATE snippets SET user_id = NULL;
            DELETE FROM users;
            """);
    }
}
