using System.Text.Json;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;

namespace Lilia.Api.Services;

/// <summary>
/// The one rule for making a document out of another — Duplicate, Save as
/// template and Use template all follow it (Olivia, templates handoff 27 Sep:
/// "the same rule as duplicate").
///
/// <para>Every setting comes along — class, packages, preamble, columns,
/// margins, headers, spacing, engine, and whatever is added next — by copying
/// all of the original's values and then resetting only what makes a document
/// itself: identity, owner, sharing, bookkeeping. Copying a hand-picked list
/// dropped all but a few settings, and a copy that used the author's own macros
/// did not compile. Collaborators, team, labels, versions and comments are
/// relations, not values, so they never come along.</para>
/// </summary>
public static class DocumentCopy
{
    /// <summary>
    /// A new, private, non-template document owned by <paramref name="ownerId"/>
    /// with the original's settings, blocks (nesting kept) and bibliography.
    /// Not yet added to the context.
    /// </summary>
    public static Document Of(LiliaDbContext context, Document original, string ownerId, string title)
    {
        var now = DateTime.UtcNow;
        var fresh = new Document();
        var copy = new Document();
        context.Entry(copy).CurrentValues.SetValues(context.Entry(original).CurrentValues);
        copy.Id = Guid.NewGuid();
        copy.OwnerId = ownerId;
        copy.TeamId = null;                        // private to whoever made it
        copy.Title = title;
        copy.IsPublic = false;
        copy.ShareLink = null;
        copy.ShareSlug = null;
        copy.LinkExpiresAt = null;
        copy.LinkPermission = fresh.LinkPermission;
        copy.CreatedAt = now;
        copy.UpdatedAt = now;
        copy.LastOpenedAt = null;
        copy.LastAutoSavedAt = null;
        copy.DeletedAt = null;
        copy.Status = fresh.Status;
        copy.Version = fresh.Version;
        copy.CurrentVersionId = null;
        copy.IsPlayground = false;
        copy.IsTemplate = false;
        copy.TemplateName = null;
        copy.TemplateDescription = null;
        copy.TemplateCategory = null;
        copy.TemplateThumbnail = null;
        copy.IsPublicTemplate = false;
        copy.TemplateUsageCount = 0;
        copy.IsStarter = false;
        copy.IsHelpContent = false;
        copy.HelpCategory = null;
        copy.HelpOrder = 0;
        copy.HelpSlug = null;
        copy.ValidationErrorCount = 0;
        copy.ValidationWarningCount = 0;
        copy.ValidationCheckedAt = null;
        copy.LabelNumbers = null;
        copy.LabelNumbersAt = null;

        // Blocks, nesting included: a child hangs under the copy of its
        // parent, not the original's.
        var newIds = original.Blocks.ToDictionary(b => b.Id, _ => Guid.NewGuid());
        foreach (var block in original.Blocks.OrderBy(b => b.SortOrder))
        {
            copy.Blocks.Add(new Block
            {
                Id = newIds[block.Id],
                DocumentId = copy.Id,
                Type = block.Type,
                Content = JsonDocument.Parse(block.Content.RootElement.GetRawText()),
                SortOrder = block.SortOrder,
                Depth = block.Depth,
                Path = block.Path,
                ParentId = block.ParentId is { } parent && newIds.TryGetValue(parent, out var np) ? np : null,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        foreach (var entry in original.BibliographyEntries)
        {
            copy.BibliographyEntries.Add(new BibliographyEntry
            {
                Id = Guid.NewGuid(),
                DocumentId = copy.Id,
                CiteKey = entry.CiteKey,
                EntryType = entry.EntryType,
                Data = JsonDocument.Parse(entry.Data.RootElement.GetRawText()),
                FormattedText = entry.FormattedText,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        return copy;
    }
}
