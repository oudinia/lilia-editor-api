using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Engines;

public partial class BibliographyService : IBibliographyService
{
    private readonly LiliaDbContext _context;
    private readonly HttpClient _httpClient;
    private readonly ILogger<BibliographyService> _logger;

    public BibliographyService(LiliaDbContext context, IHttpClientFactory httpClientFactory, ILogger<BibliographyService> logger)
    {
        _context = context;
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
    }

    public async Task<List<BibliographyEntryDto>> GetEntriesAsync(Guid documentId)
    {
        var entries = await _context.BibliographyEntries
            .Where(e => e.DocumentId == documentId)
            .OrderBy(e => e.CiteKey)
            .ToListAsync();
        // And the ones written in bibliography blocks, as the document itself lists them.
        var blocks = await _context.Blocks.Where(b => b.DocumentId == documentId && b.Type == "bibliography").ToListAsync();
        return entries.Select(MapToDto)
            .Concat(BibliographyBlocks.Unstored(documentId, blocks, entries.Select(e => e.CiteKey))
                .Select(e => MapToDto(e) with { InBlock = true }))
            .ToList();
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

    public async Task<List<BibliographyEntryDto>> ImportBibTexAsync(Guid documentId, string bibTexContent, bool overwrite = true)
    {
        var entries = ParseBibTex(bibTexContent);
        var results = new List<BibliographyEntryDto>();

        foreach (var (citeKey, entryType, data) in entries)
        {
            // Check if entry already exists
            var existing = await _context.BibliographyEntries
                .FirstOrDefaultAsync(e => e.DocumentId == documentId && e.CiteKey == citeKey);

            if (existing != null && !overwrite)
            {
                // Kept as it is; the caller counts it as skipped (it is not returned).
                continue;
            }
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
            sb.AppendLine(ToBibTex(entry.EntryType, entry.CiteKey, entry.Data.RootElement)).AppendLine();
        return sb.ToString();
    }

    /// <summary>One entry as BibTeX (<c>@type{key, field = {value}, …}</c>); empty fields are left out.</summary>
    public static string ToBibTex(string entryType, string citeKey, JsonElement data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"@{entryType}{{{citeKey},");
        if (data.ValueKind == JsonValueKind.Object)
            foreach (var prop in data.EnumerateObject())
            {
                var value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                if (!string.IsNullOrEmpty(value)) sb.AppendLine($"  {prop.Name} = {{{value}}},");
            }
        sb.Append('}');
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


            if (message.TryGetProperty("volume", out var volume))
                data["volume"] = volume.GetString() ?? "";

            // The issue too (#127: Crossref had it), and the page range with BibTeX's en dash.
            if (message.TryGetProperty("issue", out var issue) && issue.GetString() is { Length: > 0 } number)
                data["number"] = number;

            if (message.TryGetProperty("page", out var page))
                data["pages"] = PageRange(page.GetString() ?? "");

            data["doi"] = doi;

            var entryType = CrossrefKind(message, data);

            var citeKey = GenerateCiteKey(data);

            return new DoiLookupResultDto(citeKey, entryType, JsonDocument.Parse(JsonSerializer.Serialize(data)).RootElement);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DOI lookup failed for {Doi}", doi);
            return null;
        }
    }

    /// <summary>
    /// Through <see cref="BibTexParser"/>, which balances braces. The regex this
    /// replaces matched each entry lazily up to its FIRST closing brace — the end
    /// of the first braced field — so every braced field was cut off and the
    /// entry was stored with a cite key and a type and nothing else. Found 26 Sep
    /// by the e2e bibliography spec; the panel's import had also never reached
    /// this code (it sent the wrong field name).
    /// </summary>
    private static List<(string CiteKey, string EntryType, Dictionary<string, string> Data)> ParseBibTex(string content) =>
        BibTexParser.Parse(content)
            .Select(e => (e.CiteKey, e.EntryType, new Dictionary<string, string>(e.Fields, StringComparer.OrdinalIgnoreCase)))
            .ToList();

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

    /// <summary>
    /// The BibTeX type of a Crossref work, with its venue where that type prints it (launch-bot #131, 17 Nov 2026: a
    /// conference paper and a book chapter were @misc with the venue in journal, and ACM's PDF printed no venue).
    /// A journal article: journal. A conference paper: @inproceedings, booktitle (and the series when Crossref names two
    /// containers, as for LNCS). A book chapter: @incollection the same way. A book: @book. The publisher for all but articles.
    /// </summary>
    internal static string CrossrefKind(JsonElement work, Dictionary<string, string> data)
    {
        var type = work.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        var containers = work.TryGetProperty("container-title", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : [];
        var publisher = work.TryGetProperty("publisher", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
        void Venue()
        {
            if (containers.Count == 0) return;
            data["booktitle"] = containers[^1];
            if (containers.Count > 1) data["series"] = containers[0];
        }
        switch (type)
        {
            case "journal-article":
                if (containers.Count > 0) data["journal"] = containers[0];
                return "article";
            case "proceedings-article":
                Venue();
                if (publisher.Length > 0) data["publisher"] = publisher;
                return "inproceedings";
            case "book-chapter" or "book-section" or "book-part" or "reference-entry":
                Venue();
                if (publisher.Length > 0) data["publisher"] = publisher;
                return "incollection";
            case "book" or "monograph" or "edited-book" or "reference-book":
                if (publisher.Length > 0) data["publisher"] = publisher;
                if (containers.Count > 0) data["series"] = containers[0];
                return "book";
            default:
                if (containers.Count > 0) data["howpublished"] = containers[0];
                if (publisher.Length > 0) data["publisher"] = publisher;
                return "misc";
        }
    }

    /// <summary>
    /// A title's capitals kept, as BibTeX needs: styles lower-case a title but what is in braces, and "On the Einstein
    /// Podolsky Rosen paradox" printed "einstein podolsky rosen" (launch-bot #153, 17 Nov 2026). Each word after the
    /// first that has a capital (a name, an acronym: Einstein, DNA, LaTeX) is braced. A title already holding braces is
    /// left as it is. Only where BibTeX reads it (references.bib): the entry itself keeps the title as written.
    /// </summary>
    public static string ProtectCapitals(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Contains('{')) return title;
        var first = true;
        return Regex.Replace(title, @"[^\s]+", m =>
        {
            var word = m.Value;
            var keep = !first && word.Any(char.IsUpper) && !word.StartsWith('$') && !word.Contains('\\');
            first = false;
            return keep ? "{" + word + "}" : word;
        });
    }

    /// <summary>"215-225" as BibTeX writes a range, "215--225"; anything else as it is.</summary>
    public static string PageRange(string pages) => Regex.Replace(pages.Trim(), @"^(\w+)\s*[-\u2013]\s*(\w+)$", "$1--$2");

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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ISBN lookup failed for {Isbn}", isbn);
            return null;
        }
    }

    public async Task<List<DoiLookupResultDto>> SearchByTitleAsync(string query, int maxResults = 10)
    {
        try
        {
            var url = $"https://api.crossref.org/works?query={Uri.EscapeDataString(query)}&rows={maxResults}&select=DOI,title,author,published-print,container-title,volume,page,type";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("Lilia-Editor/1.0 (mailto:contact@liliaeditor.com)");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return [];

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.GetProperty("message").GetProperty("items");

            var results = new List<DoiLookupResultDto>();

            foreach (var item in items.EnumerateArray())
            {
                var data = new Dictionary<string, string>();

                if (item.TryGetProperty("title", out var title) && title.GetArrayLength() > 0)
                    data["title"] = title[0].GetString() ?? "";

                if (item.TryGetProperty("author", out var authors))
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

                if (item.TryGetProperty("published-print", out var published))
                {
                    if (published.TryGetProperty("date-parts", out var dateParts) && dateParts.GetArrayLength() > 0)
                    {
                        var parts = dateParts[0];
                        if (parts.GetArrayLength() > 0)
                            data["year"] = parts[0].GetInt32().ToString();
                    }
                }

                if (item.TryGetProperty("container-title", out var journal) && journal.GetArrayLength() > 0)
                    data["journal"] = journal[0].GetString() ?? "";

                if (item.TryGetProperty("volume", out var volume))
                    data["volume"] = volume.GetString() ?? "";

                if (item.TryGetProperty("page", out var page))
                    data["pages"] = page.GetString() ?? "";

                if (item.TryGetProperty("DOI", out var doi))
                    data["doi"] = doi.GetString() ?? "";

                var entryType = item.TryGetProperty("type", out var type) && type.GetString() == "journal-article"
                    ? "article"
                    : "misc";

                var citeKey = GenerateCiteKey(data);

                results.Add(new DoiLookupResultDto(citeKey, entryType, JsonDocument.Parse(JsonSerializer.Serialize(data)).RootElement));
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Title search failed for query {Query}", query);
            return [];
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

            // No abstract: a reference doesn't print it, and it filled the entry (launch-bot #127, 17 Nov 2026).

            // Published date
            var published = entry.Element(ns + "published")?.Value;
            if (!string.IsNullOrEmpty(published) && DateTime.TryParse(published, out var pubDate))
            {
                data["year"] = pubDate.Year.ToString();
                data["month"] = pubDate.ToString("MMMM").ToLower();
            }

            // arXiv ID and URL. A preprint is @misc with archivePrefix, so the styles print "arXiv:1612.01514 [cs.DC]"
            // where an @article with no journal printed "(2016)." (launch-bot #127).
            data["eprint"] = normalizedId;
            data["archiveprefix"] = "arXiv";
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

            return new DoiLookupResultDto(citeKey, "misc", JsonDocument.Parse(JsonSerializer.Serialize(data)).RootElement);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "arXiv lookup failed for {ArxivId}", arxivId);
            return null;
        }
    }
}
