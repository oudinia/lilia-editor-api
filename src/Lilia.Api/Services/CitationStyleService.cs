using System.Text;
using System.Text.Json;

namespace Lilia.Api.Services;

/// <summary>
/// Service for formatting citations in various academic styles.
/// </summary>
public interface ICitationStyleService
{
    string FormatCitation(string entryType, JsonElement data, CitationStyle style);
    string FormatBibliography(IEnumerable<(string entryType, JsonElement data)> entries, CitationStyle style);
    IEnumerable<CitationStyleInfo> GetAvailableStyles();
}

public enum CitationStyle
{
    APA,        // American Psychological Association (7th ed)
    MLA,        // Modern Language Association (9th ed)
    Chicago,    // Chicago Manual of Style (17th ed) - Author-Date
    IEEE,       // Institute of Electrical and Electronics Engineers
    Harvard,    // Harvard Referencing
    Vancouver   // Vancouver (medical/scientific)
}

public record CitationStyleInfo(string Id, string Name, string Description);

public class CitationStyleService : ICitationStyleService
{
    public IEnumerable<CitationStyleInfo> GetAvailableStyles()
    {
        return new[]
        {
            new CitationStyleInfo("apa", "APA 7th Edition", "American Psychological Association - commonly used in psychology, education, and social sciences"),
            new CitationStyleInfo("mla", "MLA 9th Edition", "Modern Language Association - commonly used in humanities"),
            new CitationStyleInfo("chicago", "Chicago 17th Edition", "Chicago Manual of Style (Author-Date) - commonly used in history and some sciences"),
            new CitationStyleInfo("ieee", "IEEE", "Institute of Electrical and Electronics Engineers - commonly used in engineering and computer science"),
            new CitationStyleInfo("harvard", "Harvard", "Harvard Referencing - commonly used in UK and Australia"),
            new CitationStyleInfo("vancouver", "Vancouver", "Vancouver style - commonly used in medicine and health sciences"),
        };
    }

    public string FormatCitation(string entryType, JsonElement data, CitationStyle style)
    {
        return style switch
        {
            CitationStyle.APA => FormatAPA(entryType, data),
            CitationStyle.MLA => FormatMLA(entryType, data),
            CitationStyle.Chicago => FormatChicago(entryType, data),
            CitationStyle.IEEE => FormatIEEE(entryType, data),
            CitationStyle.Harvard => FormatHarvard(entryType, data),
            CitationStyle.Vancouver => FormatVancouver(entryType, data),
            _ => FormatAPA(entryType, data)
        };
    }

    public string FormatBibliography(IEnumerable<(string entryType, JsonElement data)> entries, CitationStyle style)
    {
        var formatted = entries
            .Select(e => FormatCitation(e.entryType, e.data, style))
            .OrderBy(c => c);

        return string.Join("\n\n", formatted);
    }

    #region APA Style (7th Edition)

    private string FormatAPA(string entryType, JsonElement data)
    {
        var sb = new StringBuilder();

        var authors = GetString(data, "author");
        var year = GetString(data, "year");
        var title = GetString(data, "title");

        // Authors
        if (!string.IsNullOrEmpty(authors))
        {
            sb.Append(FormatAuthorsAPA(authors));
            sb.Append(" ");
        }

        // Year
        if (!string.IsNullOrEmpty(year))
        {
            sb.Append($"({year}). ");
        }

        // Title
        if (!string.IsNullOrEmpty(title))
        {
            if (entryType == "article")
            {
                sb.Append($"{title}. ");
            }
            else
            {
                sb.Append($"*{title}*. ");
            }
        }

        // Type-specific formatting
        switch (entryType)
        {
            case "article":
                var journal = GetString(data, "journal");
                var volume = GetString(data, "volume");
                var issue = GetString(data, "number") ?? GetString(data, "issue");
                var pages = GetString(data, "pages");
                var doi = GetString(data, "doi");

                if (!string.IsNullOrEmpty(journal))
                {
                    sb.Append($"*{journal}*");
                    if (!string.IsNullOrEmpty(volume))
                    {
                        sb.Append($", *{volume}*");
                        if (!string.IsNullOrEmpty(issue))
                        {
                            sb.Append($"({issue})");
                        }
                    }
                    if (!string.IsNullOrEmpty(pages))
                    {
                        sb.Append($", {pages}");
                    }
                    sb.Append(". ");
                }

                if (!string.IsNullOrEmpty(doi))
                {
                    sb.Append($"https://doi.org/{doi}");
                }
                break;

            case "book":
                var publisher = GetString(data, "publisher");
                var edition = GetString(data, "edition");

                if (!string.IsNullOrEmpty(edition))
                {
                    sb.Append($"({edition} ed.). ");
                }
                if (!string.IsNullOrEmpty(publisher))
                {
                    sb.Append($"{publisher}.");
                }
                break;

            case "inproceedings":
            case "conference":
                var booktitle = GetString(data, "booktitle");
                var confPages = GetString(data, "pages");

                if (!string.IsNullOrEmpty(booktitle))
                {
                    sb.Append($"In *{booktitle}*");
                    if (!string.IsNullOrEmpty(confPages))
                    {
                        sb.Append($" (pp. {confPages})");
                    }
                    sb.Append(".");
                }
                break;

            default:
                var url = GetString(data, "url");
                if (!string.IsNullOrEmpty(url))
                {
                    sb.Append($" {url}");
                }
                break;
        }

        return sb.ToString().Trim();
    }

    private string FormatAuthorsAPA(string authors)
    {
        var authorList = authors.Split(" and ", StringSplitOptions.TrimEntries);
        var formatted = new List<string>();

        foreach (var author in authorList)
        {
            var parts = author.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                // "Last, First" format
                var lastName = parts[0];
                var initials = string.Join(". ", parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => n.Length > 0 ? n[0].ToString().ToUpper() : ""));
                formatted.Add($"{lastName}, {initials}.");
            }
            else
            {
                formatted.Add(author);
            }
        }

        if (formatted.Count == 1)
            return formatted[0];
        if (formatted.Count == 2)
            return $"{formatted[0]} & {formatted[1]}";
        if (formatted.Count > 20)
            return $"{string.Join(", ", formatted.Take(19))}, ... {formatted.Last()}";

        return $"{string.Join(", ", formatted.Take(formatted.Count - 1))}, & {formatted.Last()}";
    }

    #endregion

    #region MLA Style (9th Edition)

    private string FormatMLA(string entryType, JsonElement data)
    {
        var sb = new StringBuilder();

        var authors = GetString(data, "author");
        var title = GetString(data, "title");
        var year = GetString(data, "year");

        // Authors
        if (!string.IsNullOrEmpty(authors))
        {
            sb.Append(FormatAuthorsMLA(authors));
            sb.Append(" ");
        }

        // Title
        if (!string.IsNullOrEmpty(title))
        {
            if (entryType == "article")
            {
                sb.Append($"\"{title}.\" ");
            }
            else
            {
                sb.Append($"*{title}*. ");
            }
        }

        // Type-specific formatting
        switch (entryType)
        {
            case "article":
                var journal = GetString(data, "journal");
                var volume = GetString(data, "volume");
                var issue = GetString(data, "number") ?? GetString(data, "issue");
                var pages = GetString(data, "pages");

                if (!string.IsNullOrEmpty(journal))
                {
                    sb.Append($"*{journal}*, ");
                    if (!string.IsNullOrEmpty(volume))
                    {
                        sb.Append($"vol. {volume}, ");
                        if (!string.IsNullOrEmpty(issue))
                        {
                            sb.Append($"no. {issue}, ");
                        }
                    }
                    if (!string.IsNullOrEmpty(year))
                    {
                        sb.Append($"{year}, ");
                    }
                    if (!string.IsNullOrEmpty(pages))
                    {
                        sb.Append($"pp. {pages}.");
                    }
                }
                break;

            case "book":
                var publisher = GetString(data, "publisher");
                if (!string.IsNullOrEmpty(publisher))
                {
                    sb.Append($"{publisher}, ");
                }
                if (!string.IsNullOrEmpty(year))
                {
                    sb.Append($"{year}.");
                }
                break;

            default:
                if (!string.IsNullOrEmpty(year))
                {
                    sb.Append($"{year}.");
                }
                break;
        }

        return sb.ToString().Trim();
    }

    private string FormatAuthorsMLA(string authors)
    {
        var authorList = authors.Split(" and ", StringSplitOptions.TrimEntries);

        if (authorList.Length == 1)
        {
            return FormatSingleAuthorMLA(authorList[0]) + ".";
        }
        if (authorList.Length == 2)
        {
            return $"{FormatSingleAuthorMLA(authorList[0])}, and {FormatAuthorFirstNameFirst(authorList[1])}.";
        }
        // 3+ authors: First author, et al.
        return $"{FormatSingleAuthorMLA(authorList[0])}, et al.";
    }

    private string FormatSingleAuthorMLA(string author)
    {
        var parts = author.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return $"{parts[0]}, {parts[1]}";
        }
        return author;
    }

    private string FormatAuthorFirstNameFirst(string author)
    {
        var parts = author.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return $"{parts[1]} {parts[0]}";
        }
        return author;
    }

    #endregion

    #region Chicago Style (Author-Date)

    private string FormatChicago(string entryType, JsonElement data)
    {
        var sb = new StringBuilder();

        var authors = GetString(data, "author");
        var year = GetString(data, "year");
        var title = GetString(data, "title");

        // Authors
        if (!string.IsNullOrEmpty(authors))
        {
            sb.Append(FormatAuthorsChicago(authors));
            sb.Append(" ");
        }

        // Year
        if (!string.IsNullOrEmpty(year))
        {
            sb.Append($"{year}. ");
        }

        // Title
        if (!string.IsNullOrEmpty(title))
        {
            if (entryType == "article")
            {
                sb.Append($"\"{title}.\" ");
            }
            else
            {
                sb.Append($"*{title}*. ");
            }
        }

        // Type-specific
        switch (entryType)
        {
            case "article":
                var journal = GetString(data, "journal");
                var volume = GetString(data, "volume");
                var issue = GetString(data, "number") ?? GetString(data, "issue");
                var pages = GetString(data, "pages");
                var doi = GetString(data, "doi");

                if (!string.IsNullOrEmpty(journal))
                {
                    sb.Append($"*{journal}* ");
                    if (!string.IsNullOrEmpty(volume))
                    {
                        sb.Append($"{volume}");
                        if (!string.IsNullOrEmpty(issue))
                        {
                            sb.Append($", no. {issue}");
                        }
                    }
                    if (!string.IsNullOrEmpty(pages))
                    {
                        sb.Append($": {pages}");
                    }
                    sb.Append(". ");
                }
                if (!string.IsNullOrEmpty(doi))
                {
                    sb.Append($"https://doi.org/{doi}");
                }
                break;

            case "book":
                var location = GetString(data, "address") ?? GetString(data, "location");
                var publisher = GetString(data, "publisher");
                if (!string.IsNullOrEmpty(location) && !string.IsNullOrEmpty(publisher))
                {
                    sb.Append($"{location}: {publisher}.");
                }
                else if (!string.IsNullOrEmpty(publisher))
                {
                    sb.Append($"{publisher}.");
                }
                break;
        }

        return sb.ToString().Trim();
    }

    private string FormatAuthorsChicago(string authors)
    {
        var authorList = authors.Split(" and ", StringSplitOptions.TrimEntries);
        var formatted = authorList.Select(FormatSingleAuthorMLA).ToList();

        if (formatted.Count == 1)
            return formatted[0] + ".";
        if (formatted.Count <= 3)
            return string.Join(", ", formatted.Take(formatted.Count - 1)) + ", and " + formatted.Last() + ".";

        return formatted[0] + " et al.";
    }

    #endregion

    #region IEEE Style

    private string FormatIEEE(string entryType, JsonElement data)
    {
        var sb = new StringBuilder();

        var authors = GetString(data, "author");
        var title = GetString(data, "title");
        var year = GetString(data, "year");

        // Authors
        if (!string.IsNullOrEmpty(authors))
        {
            sb.Append(FormatAuthorsIEEE(authors));
            sb.Append(", ");
        }

        // Title
        if (!string.IsNullOrEmpty(title))
        {
            sb.Append($"\"{title},\" ");
        }

        // Type-specific
        switch (entryType)
        {
            case "article":
                var journal = GetString(data, "journal");
                var volume = GetString(data, "volume");
                var issue = GetString(data, "number") ?? GetString(data, "issue");
                var pages = GetString(data, "pages");

                if (!string.IsNullOrEmpty(journal))
                {
                    sb.Append($"*{journal}*");
                    if (!string.IsNullOrEmpty(volume))
                    {
                        sb.Append($", vol. {volume}");
                        if (!string.IsNullOrEmpty(issue))
                        {
                            sb.Append($", no. {issue}");
                        }
                    }
                    if (!string.IsNullOrEmpty(pages))
                    {
                        sb.Append($", pp. {pages}");
                    }
                    if (!string.IsNullOrEmpty(year))
                    {
                        sb.Append($", {year}");
                    }
                    sb.Append(".");
                }
                break;

            case "book":
                var publisher = GetString(data, "publisher");
                var location = GetString(data, "address") ?? GetString(data, "location");

                if (!string.IsNullOrEmpty(location))
                {
                    sb.Append($"{location}: ");
                }
                if (!string.IsNullOrEmpty(publisher))
                {
                    sb.Append($"{publisher}");
                }
                if (!string.IsNullOrEmpty(year))
                {
                    sb.Append($", {year}");
                }
                sb.Append(".");
                break;

            case "inproceedings":
            case "conference":
                var booktitle = GetString(data, "booktitle");
                var confPages = GetString(data, "pages");

                if (!string.IsNullOrEmpty(booktitle))
                {
                    sb.Append($"in *{booktitle}*");
                    if (!string.IsNullOrEmpty(year))
                    {
                        sb.Append($", {year}");
                    }
                    if (!string.IsNullOrEmpty(confPages))
                    {
                        sb.Append($", pp. {confPages}");
                    }
                    sb.Append(".");
                }
                break;

            default:
                if (!string.IsNullOrEmpty(year))
                {
                    sb.Append($"{year}.");
                }
                break;
        }

        return sb.ToString().Trim();
    }

    private string FormatAuthorsIEEE(string authors)
    {
        var authorList = authors.Split(" and ", StringSplitOptions.TrimEntries);
        var formatted = new List<string>();

        foreach (var author in authorList)
        {
            var parts = author.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                // "First I. Last" format
                var lastName = parts[0];
                var initials = string.Join(". ", parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => n.Length > 0 ? n[0].ToString().ToUpper() : "")) + ".";
                formatted.Add($"{initials} {lastName}");
            }
            else
            {
                formatted.Add(author);
            }
        }

        if (formatted.Count <= 3)
            return string.Join(", ", formatted.Take(formatted.Count - 1)) +
                   (formatted.Count > 1 ? " and " + formatted.Last() : formatted[0]);

        return string.Join(", ", formatted.Take(3)) + ", *et al.*";
    }

    #endregion

    #region Harvard Style

    private string FormatHarvard(string entryType, JsonElement data)
    {
        // Harvard is similar to APA but with slight differences
        var sb = new StringBuilder();

        var authors = GetString(data, "author");
        var year = GetString(data, "year");
        var title = GetString(data, "title");

        if (!string.IsNullOrEmpty(authors))
        {
            sb.Append(FormatAuthorsHarvard(authors));
            sb.Append(" ");
        }

        if (!string.IsNullOrEmpty(year))
        {
            sb.Append($"({year}) ");
        }

        if (!string.IsNullOrEmpty(title))
        {
            if (entryType == "article")
            {
                sb.Append($"'{title}', ");
            }
            else
            {
                sb.Append($"*{title}*, ");
            }
        }

        switch (entryType)
        {
            case "article":
                var journal = GetString(data, "journal");
                var volume = GetString(data, "volume");
                var issue = GetString(data, "number");
                var pages = GetString(data, "pages");

                if (!string.IsNullOrEmpty(journal))
                {
                    sb.Append($"*{journal}*");
                    if (!string.IsNullOrEmpty(volume))
                    {
                        sb.Append($", {volume}");
                        if (!string.IsNullOrEmpty(issue))
                        {
                            sb.Append($"({issue})");
                        }
                    }
                    if (!string.IsNullOrEmpty(pages))
                    {
                        sb.Append($", pp. {pages}");
                    }
                    sb.Append(".");
                }
                break;

            case "book":
                var publisher = GetString(data, "publisher");
                var location = GetString(data, "address");
                if (!string.IsNullOrEmpty(location))
                {
                    sb.Append($"{location}: ");
                }
                if (!string.IsNullOrEmpty(publisher))
                {
                    sb.Append($"{publisher}.");
                }
                break;
        }

        return sb.ToString().Trim();
    }

    private string FormatAuthorsHarvard(string authors)
    {
        var authorList = authors.Split(" and ", StringSplitOptions.TrimEntries);
        var formatted = authorList.Select(a =>
        {
            var parts = a.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                var initials = string.Join(".", parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => n.Length > 0 ? n[0].ToString().ToUpper() : ""));
                return $"{parts[0]}, {initials}.";
            }
            return a;
        }).ToList();

        if (formatted.Count == 1)
            return formatted[0];
        if (formatted.Count == 2)
            return $"{formatted[0]} and {formatted[1]}";
        if (formatted.Count > 3)
            return $"{formatted[0]} et al.";

        return $"{string.Join(", ", formatted.Take(formatted.Count - 1))} and {formatted.Last()}";
    }

    #endregion

    #region Vancouver Style

    private string FormatVancouver(string entryType, JsonElement data)
    {
        var sb = new StringBuilder();

        var authors = GetString(data, "author");
        var title = GetString(data, "title");
        var year = GetString(data, "year");

        if (!string.IsNullOrEmpty(authors))
        {
            sb.Append(FormatAuthorsVancouver(authors));
            sb.Append(" ");
        }

        if (!string.IsNullOrEmpty(title))
        {
            sb.Append($"{title}. ");
        }

        switch (entryType)
        {
            case "article":
                var journal = GetString(data, "journal");
                var volume = GetString(data, "volume");
                var issue = GetString(data, "number");
                var pages = GetString(data, "pages");

                if (!string.IsNullOrEmpty(journal))
                {
                    sb.Append($"{journal}. ");
                    if (!string.IsNullOrEmpty(year))
                    {
                        sb.Append($"{year}");
                    }
                    if (!string.IsNullOrEmpty(volume))
                    {
                        sb.Append($";{volume}");
                        if (!string.IsNullOrEmpty(issue))
                        {
                            sb.Append($"({issue})");
                        }
                    }
                    if (!string.IsNullOrEmpty(pages))
                    {
                        sb.Append($":{pages}");
                    }
                    sb.Append(".");
                }
                break;

            case "book":
                var location = GetString(data, "address");
                var publisher = GetString(data, "publisher");

                if (!string.IsNullOrEmpty(location))
                {
                    sb.Append($"{location}: ");
                }
                if (!string.IsNullOrEmpty(publisher))
                {
                    sb.Append($"{publisher}; ");
                }
                if (!string.IsNullOrEmpty(year))
                {
                    sb.Append($"{year}.");
                }
                break;
        }

        return sb.ToString().Trim();
    }

    private string FormatAuthorsVancouver(string authors)
    {
        var authorList = authors.Split(" and ", StringSplitOptions.TrimEntries);
        var formatted = new List<string>();

        foreach (var author in authorList.Take(6))
        {
            var parts = author.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                var initials = string.Join("", parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(n => n.Length > 0 ? n[0].ToString().ToUpper() : ""));
                formatted.Add($"{parts[0]} {initials}");
            }
            else
            {
                formatted.Add(author);
            }
        }

        if (authorList.Length > 6)
        {
            formatted.Add("et al");
        }

        return string.Join(", ", formatted) + ".";
    }

    #endregion

    #region Helpers

    private static string? GetString(JsonElement data, string propertyName)
    {
        if (data.TryGetProperty(propertyName, out var prop))
        {
            return prop.GetString();
        }
        return null;
    }

    #endregion
}
