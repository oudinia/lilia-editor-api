using Lilia.Core.Capabilities;
using Npgsql;

namespace Lilia.Api.Services.Capabilities;

/// <summary>
/// Answers about LaTeX commands from measured facts rather than a hand-authored
/// catalogue.
/// </summary>
/// <remarks>
/// <para><b>Why this exists alongside <see cref="LatexTokenProvider"/>.</b> The
/// token catalogue has 293 rows and classified <b>none</b> of the 37 commands
/// our own documents use — it describes LaTeX <i>imports</i> (environments,
/// headings, citations) while documents need <i>maths</i>. Two disjoint
/// vocabularies, so this is not a replacement: both are registered, and the
/// resolver merges whatever each can answer.</para>
///
/// <para><c>latex_facts.command_support</c> holds 374 commands harvested from
/// 20,602 TeX.SE posts and established by compiling: 309 compile under the
/// preamble we emit, 65 do not.</para>
///
/// <para><b>No target parameter, and that is measured rather than assumed.</b>
/// All 374 were compiled under pdflatex, xelatex and lualatex with zero real
/// disagreements. The three that appeared to differ compile identically in
/// isolation. A per-engine column would be three copies of one answer — unlike
/// code points, where the engine decides everything.</para>
///
/// <para>Typst is a different matter and gets <see cref="Support.Unknown"/>:
/// nothing here was compiled with Typst, and a LaTeX measurement says nothing
/// about it.</para>
/// </remarks>
public sealed class CommandSupportProvider : ICapabilityProvider
{
    private readonly string? _connectionString;
    private readonly ILogger<CommandSupportProvider> _logger;

    public CommandSupportProvider(IConfiguration config, ILogger<CommandSupportProvider> logger)
    {
        // Same catalogue database as font coverage — the measured-facts store,
        // deliberately separate from the app database that holds hand-authored
        // catalogues. When the two disagree, the one that compiled wins.
        _connectionString = config.GetConnectionString("LatexFacts");
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _logger.LogInformation(
                "No LatexFacts connection configured — measured command support is unavailable. "
                + "Commands will report 'unknown' rather than being assumed fine.");
        }
    }

    public string Name => "command_support";

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_connectionString);

    public bool Handles(Requirement requirement) => requirement is CommandRequirement;

    public async Task<IReadOnlyList<CapabilityVerdict>> ResolveAsync(
        IReadOnlyList<Requirement> requirements, RenderTarget target, CancellationToken ct = default)
    {
        var commands = requirements.OfType<CommandRequirement>().ToList();
        if (commands.Count == 0) return [];

        if (!IsAvailable)
        {
            return [.. commands.Select(c => CapabilityVerdict.Unknown(
                c, Name, "the measured-facts catalogue is not configured, so support could not be checked"))];
        }

        if (target is RenderTarget.Typst)
        {
            return [.. commands.Select(c => CapabilityVerdict.Unknown(
                c, Name, "these facts were measured by compiling LaTeX and say nothing about Typst"))];
        }

        // Rows store the command with its leading backslash, which is the
        // normalised form.
        var names = commands.Select(c => c.Normalised).Distinct().ToArray();

        const string sql = """
            SELECT command, support, evidence, corpus_posts
            FROM latex_facts.command_support
            WHERE command = ANY(@commands)
            """;

        Dictionary<string, (string Support, string? Evidence, int? Posts)> rows;
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("commands", names);

            rows = new Dictionary<string, (string, string?, int?)>(StringComparer.Ordinal);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows[reader.GetString(0)] = (
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Advisory data on a separate server. Unreachable must report
            // "could not check", never an empty result that reads as "fine".
            _logger.LogWarning(ex, "[Capabilities] Measured command lookup failed");
            return [.. commands.Select(c => CapabilityVerdict.Unknown(
                c, Name, $"measured-facts lookup failed: {ex.Message}"))];
        }

        return [.. commands.Select(command =>
        {
            if (!rows.TryGetValue(command.Normalised, out var row))
            {
                // Absent means it was not in the harvested set — used in fewer
                // than three posts across 20,602. That is no evidence either
                // way, and reporting it as a problem would bury the real
                // entries under a long tail of rare commands.
                return CapabilityVerdict.Unknown(command, Name, "not in the measured set");
            }

            if (row.Support == "full")
            {
                var reach = row.Posts is > 0 ? $", used in {row.Posts} corpus posts" : "";
                return new CapabilityVerdict(command, Support.Full, Name,
                    $"compiles under the preamble we emit{reach}");
            }

            // Deliberately Unknown, not None. Failing every probe is not proof
            // of absence: the command may need a package we do not load, or —
            // most often in the corpus — be a macro the document itself
            // defines, which no catalogue can ever contain.
            return CapabilityVerdict.Unknown(command, Name, row.Evidence);
        })];
    }
}
