using Lilia.Import.Interfaces;

namespace Lilia.Api.Services;

/// <summary>
/// Bridges IEquationImageRenderer (Lilia.Import) → ILaTeXRenderService (Lilia.Api).
/// Used as the PNG fallback when OMML conversion fails for complex equations.
/// </summary>
public class LaTeXEquationImageRenderer(ILaTeXRenderService latexRenderService) : IEquationImageRenderer
{
    public async Task<byte[]?> RenderToPngAsync(string latexFragment, bool displayMode = true)
    {
        try
        {
            var wrapped = displayMode
                ? $"\\[{latexFragment}\\]"
                : $"${latexFragment}$";

            return await latexRenderService.RenderBlockToPngAsync(wrapped, dpi: 200);
        }
        catch
        {
            return null;
        }
    }
}
