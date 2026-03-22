using Lilia.Core.Models.MathAst;

namespace Lilia.Core.Interfaces;

public interface IMathAstService
{
    /// <summary>
    /// Parse a LaTeX math string into a MathNode AST.
    /// </summary>
    MathNode Parse(string latex);

    /// <summary>
    /// Convert a MathNode AST to Typst math syntax.
    /// </summary>
    string ToTypst(MathNode node);

    /// <summary>
    /// Validate a MathNode AST and return any warnings.
    /// </summary>
    List<string> Validate(MathNode node);
}
