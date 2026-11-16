using Lilia.Core.Entities;

namespace Lilia.Engines;

/// <summary>
/// A CV's header is its Personal information block. The PDF also printed \maketitle above it: the document's name and
/// the date, as on an article ("T-073 · classic, cv …", "November 16, 2026", then "Ada Lovelace"), on CVs made in Lilia
/// and imported ones alike (seen 16 Nov 2026, the document sweep). With a Personal information block and no Title block,
/// \maketitle is left out; \title and \author stay, for the PDF's metadata. A Title block the author added keeps it.
/// </summary>
public static class CvHeader
{
    /// <summary>The Personal information block is the header: there is one, and no Title block.</summary>
    public static bool ReplacesTitle(IEnumerable<Block> blocks)
    {
        var personal = false;
        foreach (var b in blocks)
        {
            if (string.Equals(b.Type, "title", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(b.Type, "personalInfo", StringComparison.OrdinalIgnoreCase)) personal = true;
        }
        return personal;
    }
}
