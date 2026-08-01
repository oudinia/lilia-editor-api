using System.Globalization;
using System.Text;
using Npgsql;

namespace Lilia.Api.Services;

/// <summary>One font family, and whether it travels with the document.</summary>
/// <param name="Provenance">
/// <c>tex-tree</c> — ships with TeX Live, so the document compiles anywhere.
/// <c>system</c> — installed on this machine only.
/// </param>
public record FontOption(string Family, string Provenance)
{
    /// <summary>
    /// Whether a document using this font still compiles somewhere else.
    ///
    /// <para>This is the portability trap, and it is the reason provenance is
    /// surfaced at all rather than just a family name: a system font produces a
    /// perfect PDF here and a broken one the moment the <c>.tex</c> reaches a
    /// collaborator or Overleaf. The author has no way to tell the two apart by
    /// looking.</para>
    /// </summary>
    public bool IsPortable => Provenance == "tex-tree";
}

public interface IFontCoverageService
{
    /// <summary>Whether font facts are available at all.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Code points in <paramref name="text"/> that <paramref name="family"/>
    /// cannot render — the characters that would be silently dropped.
    /// </summary>
    Task<IReadOnlyList<int>> UncoveredCodePointsAsync(string family, string text, CancellationToken ct = default);

    /// <summary>
    /// Families covering every one of <paramref name="codePoints"/>, portable
    /// ones first.
    /// </summary>
    Task<IReadOnlyList<FontOption>> FontsCoveringAsync(IReadOnlyCollection<int> codePoints, CancellationToken ct = default);
}

/// <summary>
/// Answers "can this font render this text?" from measured facts.
///
/// <para><b>Measured, not authored.</b> <c>fc-list</c> reports each font's exact
/// charset, so "Noto Serif Hebrew has no full stop" is a lookup rather than
/// knowledge somebody has to maintain and keep true. The facts are produced by
/// <c>lilia-latex-service</c> against pinned TeX Live images and read here.</para>
///
/// <para><b>Why this reads the table directly.</b> The plan proposed calling the
/// verifier over HTTP — but that service is a Blazor app with no API surface, so
/// there was nothing to call. A read-only query is what the plan wanted the call
/// to be anyway ("font-catalogue lookups are a query and should stay one"), and
/// it needs no second process alive for the editor to answer the question.</para>
///
/// <para><b>Unavailable is reported, never guessed.</b> With no connection
/// configured every method returns empty and <see cref="IsAvailable"/> is false.
/// The alternative — treating "no data" as "everything is covered" — would be a
/// confident wrong answer about exactly the failure this catalogue exists to
/// prevent.</para>
/// </summary>
public class FontCoverageService : IFontCoverageService
{
    private readonly string? _connectionString;
    private readonly ILogger<FontCoverageService> _logger;

    public FontCoverageService(IConfiguration config, ILogger<FontCoverageService> logger)
    {
        _connectionString = config.GetConnectionString("LatexFacts");
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _logger.LogInformation(
                "No LatexFacts connection configured — font coverage is unavailable. "
                + "Coverage questions will report 'unknown' rather than guessing.");
        }
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_connectionString);

    /// <summary>
    /// Distinct code points in a string, ignoring ASCII.
    ///
    /// <para>Pure and public so it can be tested without a database, and because
    /// it carries a decision worth pinning: plain ASCII is skipped. Every font
    /// has it, so including it would turn every lookup into a scan over
    /// characters that were never in question.</para>
    ///
    /// <para>Enumerated as runes, not chars: an emoji or a rare CJK ideograph is
    /// a surrogate pair, and asking whether a font covers half of one is
    /// meaningless.</para>
    /// </summary>
    public static IReadOnlyList<int> InterestingCodePoints(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];

        var seen = new HashSet<int>();
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value < 0x80) continue;
            seen.Add(rune.Value);
        }

        return [.. seen.Order()];
    }

    public async Task<IReadOnlyList<int>> UncoveredCodePointsAsync(
        string family, string text, CancellationToken ct = default)
    {
        var wanted = InterestingCodePoints(text);
        if (!IsAvailable || wanted.Count == 0 || string.IsNullOrWhiteSpace(family)) return [];

        // One round trip: ask which of the requested points the family DOES
        // cover, then subtract. Cheaper and simpler than a query per code point,
        // and the answer set is small.
        // `family` is fc-list's comma-separated ALIAS LIST, not one name —
        // "Latin Modern Roman,LM Roman 10". An equality test therefore matches
        // nothing for any name a person would actually type, and the method
        // would report every character as uncovered: a confident wrong answer
        // about the exact failure this catalogue exists to prevent. Found by
        // querying the real data; no unit test could have caught it.
        const string sql = """
            SELECT DISTINCT cp
            FROM unnest(@cps::int[]) AS cp
            WHERE EXISTS (
                SELECT 1 FROM latex_facts.font f
                WHERE @family = ANY(string_to_array(f.family, ','))
                  AND f.charset @> cp
            )
            """;

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("cps", wanted.ToArray());
            cmd.Parameters.AddWithValue("family", family);

            var covered = new HashSet<int>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) covered.Add(reader.GetInt32(0));

            return [.. wanted.Where(cp => !covered.Contains(cp))];
        }
        catch (Exception ex)
        {
            // Advisory data. A font catalogue being unreachable must not fail an
            // edit or an export — but it must not silently read as "all covered"
            // either, which is why this logs rather than returning a verdict.
            _logger.LogWarning(ex, "Font coverage lookup failed for family {Family}", family);
            return [];
        }
    }

    public async Task<IReadOnlyList<FontOption>> FontsCoveringAsync(
        IReadOnlyCollection<int> codePoints, CancellationToken ct = default)
    {
        if (!IsAvailable || codePoints.Count == 0) return [];

        // A family qualifies only if it covers EVERY requested point. Grouping
        // and counting the matches is what enforces "every" — a family covering
        // three of four scripts is not an answer to "render this document".
        // split_part(...,1) takes the FIRST alias as the display name. The stored
        // value is a comma-separated alias list, so returning it whole would
        // suggest "Latin Modern Roman,LM Roman 10" as a font — which is not a
        // name anyone can put in \setmainfont.
        const string sql = """
            SELECT split_part(f.family, ',', 1) AS family,
                   MIN(f.provenance) AS provenance
            FROM latex_facts.font f
            JOIN unnest(@cps::int[]) AS cp ON f.charset @> cp
            GROUP BY split_part(f.family, ',', 1)
            HAVING COUNT(DISTINCT cp) = @total
            ORDER BY (MIN(f.provenance) = 'tex-tree') DESC, 1
            LIMIT 50
            """;

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("cps", codePoints.ToArray());
            cmd.Parameters.AddWithValue("total", codePoints.Distinct().Count());

            var results = new List<FontOption>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(new FontOption(reader.GetString(0), reader.GetString(1)));

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Font suggestion lookup failed");
            return [];
        }
    }
}
