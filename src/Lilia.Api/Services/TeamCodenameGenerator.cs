using System.Security.Cryptography;

namespace Lilia.Api.Services;

/// <summary>
/// Generate research-lab-style team codenames — adjective + noun + suffix.
///
/// Pool curation principles (user direction 2026-05-14):
/// * No scientist surnames in the random pool. Pairing Curie with a
///   random adjective ("Quiet Curie") reads as flippant. If a user
///   wants to honor a scientist, that's an explicit rename, not a roll.
/// * No galaxy nicknames — they lean folk-y (Sombrero, Cigar) and
///   feel less rigorous than particles / math / elements / objects.
/// * Everything in the pool is non-personal, evocative, academic.
///
/// Combinatorics: 20 adjectives × 43 nouns × 32³ suffix = ~28M unique
/// codenames. Collisions are re-rolled by the caller (see
/// TeamService.CreateWithCodename — checks DB before insert).
/// </summary>
public interface ITeamCodenameGenerator
{
    /// <summary>
    /// Produce one codename — e.g. "Cobalt Photon A7B".
    /// Caller is responsible for uniqueness checks if needed.
    /// </summary>
    string Generate();

    /// <summary>
    /// Produce N distinct codename suggestions (no duplicates within
    /// the batch) — used by the editor's "Generate suggestion" UI to
    /// offer the user a small picker.
    /// </summary>
    IReadOnlyList<string> Suggest(int count);
}

public class TeamCodenameGenerator : ITeamCodenameGenerator
{
    // Adjectives — colors, qualities, no-personal-attachment vibes.
    private static readonly string[] Adjectives =
    [
        "Cobalt", "Indigo", "Crimson", "Onyx", "Amber", "Azure",
        "Iridescent", "Verdant", "Coral", "Auric", "Cerulean", "Bronze",
        "Sable", "Vermilion", "Marble", "Marigold", "Pearl", "Velvet",
        "Lambent", "Radiant",
    ];

    // Nouns — particles, math/geometry, elements, scientific objects.
    // Kept ~43; resizable freely (combo space stays huge).
    private static readonly string[] Nouns =
    [
        // Particles & physics quanta
        "Photon", "Quark", "Boson", "Neutrino", "Lepton", "Fermion",
        "Tachyon", "Plasma", "Phonon", "Soliton",
        // Math / geometry
        "Tessera", "Lambda", "Sigma", "Tau", "Vortex", "Spiral",
        "Fractal", "Apex", "Vertex", "Locus", "Theta", "Nexus", "Helix",
        // Elements (subset — periodic table is overkill)
        "Cobalt", "Iridium", "Titanium", "Argon", "Krypton", "Xenon",
        "Neon", "Helium",
        // Scientific objects / instruments / motifs
        "Crystal", "Prism", "Cipher", "Codex", "Atlas", "Sigil",
        "Beacon", "Compass", "Crucible", "Forge", "Lattice", "Mantle",
    ];

    // Suffix alphabet — base32 without confusables (no 0/O, no 1/I/L).
    // 32 chars, 3 positions, gives 32³ = 32 768 codes per (adj, noun)
    // pair. The 3-char shape keeps codenames pronounceable / scannable.
    private const string SuffixAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int SuffixLength = 3;

    public string Generate()
    {
        var adj = Adjectives[RandomNumberGenerator.GetInt32(Adjectives.Length)];
        var noun = Nouns[RandomNumberGenerator.GetInt32(Nouns.Length)];
        var suffix = RandomSuffix();
        return $"{adj} {noun} {suffix}";
    }

    public IReadOnlyList<string> Suggest(int count)
    {
        if (count <= 0) return Array.Empty<string>();
        // Hard cap to avoid silly request sizes; UI never asks for more
        // than a small handful at once anyway.
        count = Math.Min(count, 20);
        var set = new HashSet<string>(count);
        // Defensive ceiling: even at 20 picks we'll converge in well
        // under 100 tries because the combo space is ~28M.
        var maxTries = count * 4;
        for (int i = 0; i < maxTries && set.Count < count; i++)
        {
            set.Add(Generate());
        }
        return set.ToList();
    }

    private static string RandomSuffix()
    {
        Span<char> buf = stackalloc char[SuffixLength];
        for (int i = 0; i < SuffixLength; i++)
        {
            buf[i] = SuffixAlphabet[RandomNumberGenerator.GetInt32(SuffixAlphabet.Length)];
        }
        return new string(buf);
    }
}
