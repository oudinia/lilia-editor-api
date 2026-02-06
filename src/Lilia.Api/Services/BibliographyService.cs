using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public partial class BibliographyService : IBibliographyService
{
    private readonly LiliaDbContext _context;
    private readonly HttpClient _httpClient;

    public BibliographyService(LiliaDbContext context, IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<List<BibliographyEntryDto>> GetEntriesAsync(Guid documentId)
    {
        var entries = await _context.BibliographyEntries
            .Where(e => e.DocumentId == documentId)
            .OrderBy(e => e.CiteKey)
            .ToListAsync();

        return entries.Select(MapToDto).ToList();
    }

    public async Task<BibliographyEntryDto?> GetEntryAsync(Guid documentId, Guid entryId)
    {
        var entry = await _context.BibliographyEntries
            .FirstOrDefaultAsync(e => e.DocumentId == documentId && e.Id == entryId);

        return entry == null ? null : MapToDto(entry);
    }

    public async Task<BibliographyEntryDto> CreateEntryAsync(Guid documentId, CreateBibliographyEntryDto dto)
    {
        var entry = new BibliographyEntry
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            CiteKey = dto.CiteKey,
            EntryType = dto.EntryType,
            Data = JsonDocument.Parse(dto.Data.GetRawText()),
            FormattedText = FormatCitation(dto.EntryType, dto.Data),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.BibliographyEntries.Add(entry);
        await _context.SaveChangesAsync();

        return MapToDto(entry);
    }

    public async Task<BibliographyEntryDto?> UpdateEntryAsync(Guid documentId, Guid entryId, UpdateBibliographyEntryDto dto)
    {
        var entry = await _context.BibliographyEntries
            .FirstOrDefaultAsync(e => e.DocumentId == documentId && e.Id == entryId);

        if (entry == null) return null;

        if (dto.CiteKey != null) entry.CiteKey = dto.CiteKey;
        if (dto.EntryType != null) entry.EntryType = dto.EntryType;
        if (dto.Data.HasValue) entry.Data = JsonDocument.Parse(dto.Data.Value.GetRawText());

        entry.FormattedText = FormatCitation(entry.EntryType, entry.Data.RootElement);
        entry.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return MapToDto(entry);
    }

    public async Task<bool> DeleteEntryAsync(Guid documentId, Guid entryId)
    {
        var entry = await _context.BibliographyEntries
            .FirstOrDefaultAsync(e => e.DocumentId == documentId && e.Id == entryId);

        if (entry == null) return false;

        _context.BibliographyEntries.Remove(entry);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<List<BibliographyEntryDto>> ImportBibTexAsync(Guid documentId, string bibTexContent)
    {
        var entries = ParseBibTex(bibTexContent);
        var results = new List<BibliographyEntryDto>();

        foreach (var (citeKey, entryType, data) in entries)
        {
            // Check if entry already exists
            var existing = await _context.BibliographyEntries
                .FirstOrDefaultAsync(e => e.DocumentId == documentId && e.CiteKey == citeKey);

            if (existing != null)
            {
                existing.EntryType = entryType;
                existing.Data = JsonDocument.Parse(JsonSerializer.Serialize(data));
                existing.FormattedText = FormatCitation(entryType, existing.Data.RootElement);
                existing.UpdatedAt = DateTime.UtcNow;
                results.Add(MapToDto(existing));
            }
            else
            {
                var entry = new BibliographyEntry
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    CiteKey = citeKey,
                    EntryType = entryType,
                    Data = JsonDocument.Parse(JsonSerializer.Serialize(data)),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                entry.FormattedText = FormatCitation(entry.EntryType, entry.Data.RootElement);
                _context.BibliographyEntries.Add(entry);
                results.Add(MapToDto(entry));
            }
        }

        await _context.SaveChangesAsync();
        return results;
    }

    public async Task<string> ExportBibTexAsync(Guid documentId)
    {
        var entries = await _context.BibliographyEntries
            .Where(e => e.DocumentId == documentId)
            .ToListAsync();

        var sb = new StringBuilder();

        foreach (var entry in entries)
        {
            sb.AppendLine($"@{entry.EntryType}{{{entry.CiteKey},");

            var data = entry.Data.RootElement;
            foreach (var prop in data.EnumerateObject())
            {
                var value = prop.Value.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    sb.AppendLine($"  {prop.Name} = {{{value}}},");
                }
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task<DoiLookupResultDto?> LookupDoiAsync(string doi)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.crossref.org/works/{Uri.EscapeDataString(doi)}");
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var message = doc.RootElement.GetProperty("message");

            var data = new Dictionary<string, string>();

            if (message.TryGetProperty("title", out var title) && title.GetArrayLength() > 0)
                data["title"] = title[0].GetString() ?? "";

            if (message.TryGetProperty("author", out var authors))
            {
                var authorList = new List<string>();
                foreach (var author in authors.EnumerateArray())
                {
                    var family = author.TryGetProperty("family", out var f) ? f.GetString() ?? "" : "";
                    var given = author.TryGetProperty("given", out var g) ? g.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(family))
                        authorList.Add($"{family}, {given}");
                }
                data["author"] = string.Join(" and ", authorList);
            }

            if (message.TryGetProperty("published-print", out var published) ||
                message.TryGetProperty("published-online", out published))
            {
                if (published.TryGetProperty("date-parts", out var dateParts) && dateParts.GetArrayLength() > 0)
                {
                    var parts = dateParts[0];
                    if (parts.GetArrayLength() > 0)
                        data["year"] = parts[0].GetInt32().ToString();
                }
            }

            if (message.TryGetProperty("container-title", out var journal) && journal.GetArrayLength() > 0)
                data["journal"] = journal[0].GetString() ?? "";

            if (message.TryGetProperty("volume", out var volume))
                data["volume"] = volume.GetString() ?? "";

            if (message.TryGetProperty("page", out var page))
                data["pages"] = page.GetString() ?? "";

            data["doi"] = doi;

            var entryType = message.TryGetProperty("type", out var type) && type.GetString() == "journal-article"
                ? "article"
                : "misc";

            var citeKey = GenerateCiteKey(data);

            return new DoiLookupResultDto(citeKey, entryType, JsonDocument.Parse(JsonSerializer.Serialize(data)).RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static List<(string CiteKey, string EntryType, Dictionary<string, string> Data)> ParseBibTex(string content)
    {
        var results = new List<(string, string, Dictionary<string, string>)>();
        var entryPattern = EntryRegex();
        var fieldPattern = FieldRegex();

        foreach (Match entryMatch in entryPattern.Matches(content))
        {
            var entryType = entryMatch.Groups[1].Value.ToLower();
            var citeKey = entryMatch.Groups[2].Value;
            var fieldsContent = entryMatch.Groups[3].Value;

            var data = new Dictionary<string, string>();
            foreach (Match fieldMatch in fieldPattern.Matches(fieldsContent))
            {
                var fieldName = fieldMatch.Groups[1].Value.ToLower();
                var fieldValue = fieldMatch.Groups[2].Value.Trim('{', '}', '"');
                data[fieldName] = fieldValue;
            }

            results.Add((citeKey, entryType, data));
        }

        return results;
    }

    private static string FormatCitation(string entryType, JsonElement data)
    {
        var sb = new StringBuilder();

        var authors = data.TryGetProperty("author", out var a) ? a.GetString() : "";
        var title = data.TryGetProperty("title", out var t) ? t.GetString() : "";
        var year = data.TryGetProperty("year", out var y) ? y.GetString() : "";

        if (!string.IsNullOrEmpty(authors))
        {
            var authorList = authors.Split(" and ");
            if (authorList.Length > 2)
                sb.Append($"{authorList[0].Split(',')[0]} et al.");
            else
                sb.Append(string.Join(" & ", authorList.Select(a => a.Split(',')[0])));
            sb.Append(" ");
        }

        if (!string.IsNullOrEmpty(year))
            sb.Append($"({year}). ");

        if (!string.IsNullOrEmpty(title))
            sb.Append($"{title}. ");

        if (entryType == "article")
        {
            var journal = data.TryGetProperty("journal", out var j) ? j.GetString() : "";
            if (!string.IsNullOrEmpty(journal))
                sb.Append($"*{journal}*");

            var volume = data.TryGetProperty("volume", out var v) ? v.GetString() : "";
            if (!string.IsNullOrEmpty(volume))
                sb.Append($", {volume}");

            var pages = data.TryGetProperty("pages", out var p) ? p.GetString() : "";
            if (!string.IsNullOrEmpty(pages))
                sb.Append($", {pages}");

            sb.Append('.');
        }

        return sb.ToString();
    }

    private static string GenerateCiteKey(Dictionary<string, string> data)
    {
        var author = data.GetValueOrDefault("author", "");
        var year = data.GetValueOrDefault("year", "");

        var firstAuthor = author.Split(" and ").FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim() ?? "unknown";
        return $"{firstAuthor.ToLower()}{year}";
    }

    private static BibliographyEntryDto MapToDto(BibliographyEntry e)
    {
        return new BibliographyEntryDto(
            e.Id,
            e.DocumentId,
            e.CiteKey,
            e.EntryType,
            e.Data.RootElement,
            e.FormattedText,
            e.CreatedAt,
            e.UpdatedAt
        );
    }

    [GeneratedRegex(@"@(\w+)\s*\{\s*([^,]+)\s*,\s*([\s\S]*?)\s*\}", RegexOptions.Multiline)]
    private static partial Regex EntryRegex();

    [GeneratedRegex(@"(\w+)\s*=\s*(?:\{([^}]*)\}|""([^""]*)"")", RegexOptions.Multiline)]
    private static partial Regex FieldRegex();

    public async Task<DoiLookupResultDto?> LookupIsbnAsync(string isbn)
    {
        try
        {
            // Normalize ISBN (remove hyphens and spaces)
            var normalizedIsbn = isbn.Replace("-", "").Replace(" ", "");

            // Use Open Library API (free, no API key required)
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://openlibrary.org/api/books?bibkeys=ISBN:{normalizedIsbn}&format=json&jscmd=data"
            );
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            // Open Library returns object with ISBN as key
            var key = $"ISBN:{normalizedIsbn}";
            if (!doc.RootElement.TryGetProperty(key, out var bookData))
            {
                return null;
            }

            var data = new Dictionary<string, string>();

            // Title
            if (bookData.TryGetProperty("title", out var title))
            {
                data["title"] = title.GetString() ?? "";
            }

            // Authors
            if (bookData.TryGetProperty("authors", out var authors))
            {
                var authorList = new List<string>();
                foreach (var author in authors.EnumerateArray())
                {
                    if (author.TryGetProperty("name", out var name))
                    {
                        var authorName = name.GetString() ?? "";
                        // Try to convert "First Last" to "Last, First"
                        var parts = authorName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            authorList.Add($"{parts[^1]}, {string.Join(" ", parts[..^1])}");
                        }
                        else
                        {
                            authorList.Add(authorName);
                        }
                    }
                }
                data["author"] = string.Join(" and ", authorList);
            }

            // Publishers
            if (bookData.TryGetProperty("publishers", out var publishers))
            {
                var publisherList = new List<string>();
                foreach (var pub in publishers.EnumerateArray())
                {
                    if (pub.TryGetProperty("name", out var pubName))
                    {
                        publisherList.Add(pubName.GetString() ?? "");
                    }
                }
                if (publisherList.Count > 0)
                {
                    data["publisher"] = publisherList[0];
                }
            }

            // Year
            if (bookData.TryGetProperty("publish_date", out var publishDate))
            {
                var dateStr = publishDate.GetString() ?? "";
                // Extract year from various formats like "2020", "January 2020", "2020-01-01"
                var yearMatch = System.Text.RegularExpressions.Regex.Match(dateStr, @"\b(19|20)\d{2}\b");
                if (yearMatch.Success)
                {
                    data["year"] = yearMatch.Value;
                }
            }

            // Number of pages
            if (bookData.TryGetProperty("number_of_pages", out var pages))
            {
                data["pages"] = pages.GetInt32().ToString();
            }

            // ISBN
            data["isbn"] = normalizedIsbn;

            // URL
            if (bookData.TryGetProperty("url", out var url))
            {
                data["url"] = url.GetString() ?? "";
            }

            var citeKey = GenerateCiteKey(data);

            return new DoiLookupResultDto(citeKey, "book", JsonDocument.Parse(JsonSerializer.Serialize(data)).RootElement);
        }
        catch
        {
            return null;
        }
    }

    public async Task<DoiLookupResultDto?> LookupArxivAsync(string arxivId)
    {
        try
        {
            // Normalize arXiv ID (remove "arXiv:" prefix if present)
            var normalizedId = arxivId
                .Replace("arXiv:", "")
                .Replace("arxiv:", "")
                .Trim();

            // Use arXiv API (Atom feed)
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://export.arxiv.org/api/query?id_list={Uri.EscapeDataString(normalizedId)}"
            );

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var xml = await response.Content.ReadAsStringAsync();

            // Parse XML response
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            var ns = System.Xml.Linq.XNamespace.Get("http://www.w3.org/2005/Atom");

            var entry = doc.Descendants(ns + "entry").FirstOrDefault();
            if (entry == null) return null;

            var data = new Dictionary<string, string>();

            // Title
            var title = entry.Element(ns + "title")?.Value;
            if (!string.IsNullOrEmpty(title))
            {
                // Clean up title (remove newlines and extra spaces)
                data["title"] = Regex.Replace(title, @"\s+", " ").Trim();
            }

            // Authors
            var authors = entry.Elements(ns + "author")
                .Select(a => a.Element(ns + "name")?.Value)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            if (authors.Count > 0)
            {
                // Convert "First Last" to "Last, First"
                var formattedAuthors = authors.Select(a =>
                {
                    var parts = a!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        return $"{parts[^1]}, {string.Join(" ", parts[..^1])}";
                    }
                    return a;
                });
                data["author"] = string.Join(" and ", formattedAuthors);
            }

            // Abstract
            var summary = entry.Element(ns + "summary")?.Value;
            if (!string.IsNullOrEmpty(summary))
            {
                data["abstract"] = Regex.Replace(summary, @"\s+", " ").Trim();
            }

            // Published date
            var published = entry.Element(ns + "published")?.Value;
            if (!string.IsNullOrEmpty(published) && DateTime.TryParse(published, out var pubDate))
            {
                data["year"] = pubDate.Year.ToString();
                data["month"] = pubDate.ToString("MMMM").ToLower();
            }

            // arXiv ID and URL
            data["arxivid"] = normalizedId;
            data["eprint"] = normalizedId;
            data["url"] = $"https://arxiv.org/abs/{normalizedId}";

            // Primary category
            var primaryCategory = entry.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "primary_category")?
                .Attribute("term")?.Value;
            if (!string.IsNullOrEmpty(primaryCategory))
            {
                data["primaryclass"] = primaryCategory;
            }

            // DOI if available
            var doiLink = entry.Elements(ns + "link")
                .FirstOrDefault(l => l.Attribute("title")?.Value == "doi");
            if (doiLink != null)
            {
                var doiHref = doiLink.Attribute("href")?.Value;
                if (!string.IsNullOrEmpty(doiHref))
                {
                    var doiMatch = Regex.Match(doiHref, @"10\.\d{4,}/[^\s]+");
                    if (doiMatch.Success)
                    {
                        data["doi"] = doiMatch.Value;
                    }
                }
            }

            var citeKey = GenerateCiteKey(data);

            return new DoiLookupResultDto(citeKey, "article", JsonDocument.Parse(JsonSerializer.Serialize(data)).RootElement);
        }
        catch
        {
            return null;
        }
    }
}
