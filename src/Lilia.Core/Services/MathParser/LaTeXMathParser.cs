using Lilia.Core.Models.MathAst;

namespace Lilia.Core.Services.MathParser;

/// <summary>
/// Recursive descent parser that converts a LaTeX math string into a MathNode AST.
/// Never loses information: unknown constructs are wrapped in RawNode.
/// </summary>
public class LaTeXMathParser
{
    private List<Token> _tokens = [];
    private int _pos;

    private static readonly HashSet<string> GreekLetters =
    [
        "\\alpha", "\\beta", "\\gamma", "\\delta", "\\epsilon", "\\varepsilon",
        "\\zeta", "\\eta", "\\theta", "\\vartheta", "\\iota", "\\kappa",
        "\\lambda", "\\mu", "\\nu", "\\xi", "\\pi", "\\varpi",
        "\\rho", "\\varrho", "\\sigma", "\\varsigma", "\\tau", "\\upsilon",
        "\\phi", "\\varphi", "\\chi", "\\psi", "\\omega",
        "\\Gamma", "\\Delta", "\\Theta", "\\Lambda", "\\Xi", "\\Pi",
        "\\Sigma", "\\Upsilon", "\\Phi", "\\Psi", "\\Omega",
        "\\infty", "\\partial", "\\nabla", "\\ell", "\\hbar",
        "\\forall", "\\exists", "\\nexists", "\\emptyset", "\\varnothing",
        "\\aleph", "\\beth", "\\wp", "\\Re", "\\Im"
    ];

    private static readonly HashSet<string> BigOperators =
    [
        "\\sum", "\\prod", "\\coprod", "\\int", "\\iint", "\\iiint",
        "\\oint", "\\bigcup", "\\bigcap", "\\bigsqcup", "\\bigvee",
        "\\bigwedge", "\\bigoplus", "\\bigotimes", "\\bigodot", "\\lim",
        "\\limsup", "\\liminf", "\\sup", "\\inf", "\\max", "\\min"
    ];

    private static readonly HashSet<string> FunctionNames =
    [
        "\\sin", "\\cos", "\\tan", "\\cot", "\\sec", "\\csc",
        "\\arcsin", "\\arccos", "\\arctan",
        "\\sinh", "\\cosh", "\\tanh", "\\coth",
        "\\log", "\\ln", "\\lg", "\\exp", "\\det", "\\dim",
        "\\ker", "\\hom", "\\deg", "\\gcd", "\\arg", "\\mod"
    ];

    private static readonly HashSet<string> RelationCommands =
    [
        "\\leq", "\\le", "\\geq", "\\ge", "\\neq", "\\ne",
        "\\approx", "\\equiv", "\\sim", "\\simeq", "\\cong",
        "\\propto", "\\ll", "\\gg", "\\subset", "\\supset",
        "\\subseteq", "\\supseteq", "\\in", "\\notin", "\\ni",
        "\\prec", "\\succ", "\\preceq", "\\succeq", "\\perp",
        "\\parallel", "\\mid", "\\nmid", "\\vdash", "\\models",
        "\\triangleleft", "\\triangleright"
    ];

    private static readonly HashSet<string> OperatorCommands =
    [
        "\\times", "\\cdot", "\\div", "\\pm", "\\mp",
        "\\star", "\\circ", "\\bullet", "\\oplus", "\\otimes",
        "\\wedge", "\\vee", "\\cap", "\\cup", "\\setminus",
        "\\land", "\\lor", "\\neg", "\\lnot", "\\to", "\\rightarrow",
        "\\leftarrow", "\\Rightarrow", "\\Leftarrow", "\\Leftrightarrow",
        "\\mapsto", "\\implies", "\\iff"
    ];

    private static readonly HashSet<string> AccentCommands =
    [
        "\\hat", "\\bar", "\\tilde", "\\vec", "\\dot", "\\ddot",
        "\\acute", "\\grave", "\\breve", "\\check", "\\widehat",
        "\\widetilde", "\\overline", "\\underline", "\\overbrace", "\\underbrace"
    ];

    private static readonly HashSet<string> TextCommands =
    [
        "\\text", "\\mathrm", "\\textbf", "\\textit", "\\textrm",
        "\\mathbf", "\\mathit", "\\mathsf", "\\mathtt", "\\mathcal",
        "\\mathfrak", "\\mathbb", "\\mathscr", "\\boldsymbol", "\\operatorname"
    ];

    private static readonly HashSet<string> SpaceCommands =
    [
        "\\quad", "\\qquad", "\\,", "\\;", "\\:", "\\!", "\\ ",
        "\\enspace", "\\thinspace", "\\medspace", "\\thickspace",
        "\\hspace", "\\hfill"
    ];

    private static readonly HashSet<string> MatrixEnvironments =
    [
        "pmatrix", "bmatrix", "vmatrix", "Vmatrix", "Bmatrix", "matrix",
        "smallmatrix", "cases", "array", "aligned", "gathered"
    ];

    private static readonly Dictionary<string, string> LeftDelimiters = new()
    {
        ["\\langle"] = "\u27E8",
        ["\\lfloor"] = "\u230A",
        ["\\lceil"] = "\u2308",
        ["\\lvert"] = "|",
        ["\\lVert"] = "\u2016",
    };

    private static readonly Dictionary<string, string> RightDelimiters = new()
    {
        ["\\rangle"] = "\u27E9",
        ["\\rfloor"] = "\u230B",
        ["\\rceil"] = "\u2309",
        ["\\rvert"] = "|",
        ["\\rVert"] = "\u2016",
    };

    /// <summary>
    /// Parse a LaTeX math string into a MathNode AST.
    /// </summary>
    public MathNode Parse(string latex)
    {
        if (string.IsNullOrWhiteSpace(latex))
            return new GroupNode();

        var tokenizer = new LaTeXTokenizer(latex);
        _tokens = tokenizer.Tokenize();
        _pos = 0;

        var nodes = ParseSequence();

        return nodes.Count == 1 ? nodes[0] : new GroupNode { Children = nodes };
    }

    private Token Peek() => _pos < _tokens.Count ? _tokens[_pos] : new Token(TokenType.End, "", 0);
    private Token Advance() => _tokens[_pos++];

    private bool Match(TokenType type)
    {
        if (Peek().Type != type) return false;
        _pos++;
        return true;
    }

    /// <summary>
    /// Parse a flat sequence of nodes until we hit a stopping token.
    /// </summary>
    private List<MathNode> ParseSequence()
    {
        var nodes = new List<MathNode>();

        while (Peek().Type != TokenType.End)
        {
            var token = Peek();

            // Stop at tokens that indicate end of a group/context
            if (token.Type is TokenType.CloseBrace or TokenType.CloseBracket or TokenType.Ampersand)
                break;

            // Stop at \\ (row separator in matrices)
            if (token.Type == TokenType.Newline)
                break;

            // Stop at \right (delimiter closer)
            if (token.Type == TokenType.Command && token.Value == "\\right")
                break;

            // Stop at \end
            if (token.Type == TokenType.Command && token.Value == "\\end")
                break;

            var node = ParseAtom();
            if (node != null)
            {
                // Check for subscript/superscript after the atom
                node = ParsePostfix(node);
                nodes.Add(node);
            }
        }

        return nodes;
    }

    /// <summary>
    /// Parse a single atom (number, variable, command, group, etc.)
    /// </summary>
    private MathNode? ParseAtom()
    {
        var token = Peek();

        switch (token.Type)
        {
            case TokenType.Number:
                Advance();
                return new NumberNode { Value = token.Value };

            case TokenType.Letter:
                Advance();
                return new VariableNode { Name = token.Value };

            case TokenType.Operator:
                Advance();
                return new OperatorNode { Symbol = token.Value };

            case TokenType.Relation:
                Advance();
                return new RelationNode { Symbol = token.Value };

            case TokenType.OpenBrace:
                return ParseBraceGroup();

            case TokenType.OpenParen:
                Advance();
                return ParseDelimiter("(", ")");

            case TokenType.CloseParen:
                // Stray close paren — emit as raw
                Advance();
                return new RawNode { Latex = ")" };

            case TokenType.OpenBracket:
                Advance();
                return ParseDelimiter("[", "]");

            case TokenType.Pipe:
                Advance();
                return new OperatorNode { Symbol = "|" };

            case TokenType.Comma:
                Advance();
                return new OperatorNode { Symbol = "," };

            case TokenType.Semicolon:
                Advance();
                return new OperatorNode { Symbol = ";" };

            case TokenType.Exclamation:
                Advance();
                return new OperatorNode { Symbol = "!" };

            case TokenType.Whitespace:
                Advance();
                return null; // skip whitespace

            case TokenType.Newline:
                // Should be handled at caller level; emit raw if encountered unexpectedly
                Advance();
                return new RawNode { Latex = "\\\\" };

            case TokenType.Command:
                return ParseCommand();

            default:
                Advance();
                return new RawNode { Latex = token.Value };
        }
    }

    private MathNode ParseCommand()
    {
        var token = Advance();
        var cmd = token.Value;

        // Greek letters and common symbols
        if (GreekLetters.Contains(cmd))
            return new SymbolNode { Name = cmd };

        // Big operators (before function names, since \lim is in both)
        if (BigOperators.Contains(cmd))
            return ParseBigOperator(cmd);

        // Named functions
        if (FunctionNames.Contains(cmd))
            return ParseFunction(cmd);

        // Relations
        if (RelationCommands.Contains(cmd))
            return new RelationNode { Symbol = cmd };

        // Operators
        if (OperatorCommands.Contains(cmd))
            return new OperatorNode { Symbol = cmd };

        // Accents
        if (AccentCommands.Contains(cmd))
            return ParseAccent(cmd);

        // Text commands
        if (TextCommands.Contains(cmd))
            return ParseText(cmd);

        // Spacing
        if (SpaceCommands.Contains(cmd))
            return new SpaceNode { Size = cmd };

        // Specific constructs
        return cmd switch
        {
            "\\frac" or "\\dfrac" or "\\tfrac" or "\\cfrac" => ParseFraction(),
            "\\sqrt" => ParseSqrt(),
            "\\left" => ParseLeftRight(),
            "\\begin" => ParseEnvironment(),
            "\\not" => ParseNot(),
            "\\binom" or "\\dbinom" or "\\tbinom" => ParseBinom(),
            "\\overset" => ParseOverUnderSet("overset"),
            "\\underset" => ParseOverUnderSet("underset"),
            "\\stackrel" => ParseOverUnderSet("stackrel"),
            "\\phantom" or "\\hphantom" or "\\vphantom" => ParsePhantom(cmd),
            "\\color" => ParseColor(),
            "\\boxed" => ParseBoxed(),
            _ => HandleUnknownCommand(cmd)
        };
    }

    private MathNode HandleUnknownCommand(string cmd)
    {
        // If followed by a brace group, consume it as argument and wrap as raw
        if (Peek().Type == TokenType.OpenBrace)
        {
            var startPos = _pos - 1;
            var arg = ParseBraceGroupRaw();
            return new RawNode { Latex = cmd + "{" + arg + "}" };
        }
        return new RawNode { Latex = cmd };
    }

    /// <summary>
    /// Parse a brace-delimited group { ... } into a GroupNode (or single child).
    /// </summary>
    private MathNode ParseBraceGroup()
    {
        Match(TokenType.OpenBrace); // consume {
        var children = ParseSequence();
        Match(TokenType.CloseBrace); // consume }

        if (children.Count == 1)
            return children[0];
        return new GroupNode { Children = children };
    }

    /// <summary>
    /// Read raw content inside braces, handling nesting. Returns the raw string.
    /// </summary>
    private string ParseBraceGroupRaw()
    {
        if (!Match(TokenType.OpenBrace))
            return "";

        var depth = 1;
        var start = _pos;

        while (_pos < _tokens.Count && depth > 0)
        {
            if (_tokens[_pos].Type == TokenType.OpenBrace) depth++;
            else if (_tokens[_pos].Type == TokenType.CloseBrace) depth--;

            if (depth > 0) _pos++;
        }

        // Collect token values from start to _pos
        var raw = string.Join("", _tokens.Skip(start).Take(_pos - start).Select(t => t.Value));

        if (_pos < _tokens.Count && _tokens[_pos].Type == TokenType.CloseBrace)
            _pos++; // consume closing }

        return raw;
    }

    /// <summary>
    /// Parse a required brace-group argument for a command.
    /// </summary>
    private MathNode ParseRequiredArg()
    {
        if (Peek().Type == TokenType.OpenBrace)
            return ParseBraceGroup();

        // Single token argument (e.g. x in \hat x)
        var node = ParseAtom();
        return node ?? new GroupNode();
    }

    private MathNode ParseFraction()
    {
        var numerator = ParseRequiredArg();
        var denominator = ParseRequiredArg();
        return new FractionNode { Numerator = numerator, Denominator = denominator };
    }

    private MathNode ParseSqrt()
    {
        MathNode? index = null;

        // Optional index: \sqrt[3]{x}
        if (Peek().Type == TokenType.OpenBracket)
        {
            Advance(); // consume [
            var indexNodes = ParseSequence();
            Match(TokenType.CloseBracket); // consume ]
            index = indexNodes.Count == 1 ? indexNodes[0] : new GroupNode { Children = indexNodes };
        }

        var radicand = ParseRequiredArg();
        return new SqrtNode { Radicand = radicand, Index = index };
    }

    private MathNode ParseBigOperator(string op)
    {
        var node = new BigOperatorNode { Operator = op };

        // Check for _ and ^ after the big operator
        while (Peek().Type is TokenType.Subscript or TokenType.Superscript or TokenType.Whitespace)
        {
            if (Peek().Type == TokenType.Whitespace) { Advance(); continue; }

            if (Peek().Type == TokenType.Subscript)
            {
                Advance();
                node.Lower = ParseRequiredArg();
            }
            else if (Peek().Type == TokenType.Superscript)
            {
                Advance();
                node.Upper = ParseRequiredArg();
            }
        }

        return node;
    }

    private MathNode ParseFunction(string name)
    {
        var funcNode = new FunctionNode { Name = name };

        // Skip whitespace
        while (Peek().Type == TokenType.Whitespace) Advance();

        // If followed by ^/_ we don't consume argument (let postfix handle it)
        if (Peek().Type is TokenType.Superscript or TokenType.Subscript)
            return funcNode;

        // If followed by ( or {, consume as argument
        if (Peek().Type == TokenType.OpenParen)
        {
            Advance(); // skip (
            var content = ParseSequence();
            Match(TokenType.CloseParen); // skip )
            funcNode.Argument = content.Count == 1 ? content[0] : new GroupNode { Children = content };
        }
        else if (Peek().Type == TokenType.OpenBrace)
        {
            funcNode.Argument = ParseBraceGroup();
        }

        return funcNode;
    }

    private MathNode ParseAccent(string accent)
    {
        var baseNode = ParseRequiredArg();
        return new AccentNode { Accent = accent, Base = baseNode };
    }

    private MathNode ParseText(string cmd)
    {
        if (Peek().Type == TokenType.OpenBrace)
        {
            var raw = ParseBraceGroupRaw();
            return new TextNode { Text = raw };
        }
        return new RawNode { Latex = cmd };
    }

    /// <summary>
    /// Parse \left...\right delimiter pairs.
    /// </summary>
    private MathNode ParseLeftRight()
    {
        var leftDelim = ReadDelimiterToken();

        var content = ParseSequence();

        // Expect \right
        string rightDelim;
        if (Peek().Type == TokenType.Command && Peek().Value == "\\right")
        {
            Advance();
            rightDelim = ReadDelimiterToken();
        }
        else
        {
            rightDelim = "."; // missing \right, use invisible
        }

        var contentNode = content.Count == 1 ? content[0] : new GroupNode { Children = content };
        return new DelimiterNode { Left = leftDelim, Right = rightDelim, Content = contentNode };
    }

    private string ReadDelimiterToken()
    {
        var token = Peek();

        switch (token.Type)
        {
            case TokenType.OpenParen:
                Advance();
                return "(";
            case TokenType.CloseParen:
                Advance();
                return ")";
            case TokenType.OpenBracket:
                Advance();
                return "[";
            case TokenType.CloseBracket:
                Advance();
                return "]";
            case TokenType.Pipe:
                Advance();
                return "|";
            case TokenType.Other when token.Value == ".":
                Advance();
                return ".";
            case TokenType.Command:
                Advance();
                if (token.Value == "\\{") return "{";
                if (token.Value == "\\}") return "}";
                if (LeftDelimiters.TryGetValue(token.Value, out var ld)) return token.Value;
                if (RightDelimiters.TryGetValue(token.Value, out var rd)) return token.Value;
                return token.Value;
            default:
                Advance();
                return token.Value;
        }
    }

    /// <summary>
    /// Parse \begin{env}...\end{env} environments.
    /// </summary>
    private MathNode ParseEnvironment()
    {
        // Read environment name
        var envName = ParseBraceGroupRaw();

        if (MatrixEnvironments.Contains(envName))
        {
            return ParseMatrix(envName);
        }

        // Unknown environment — collect everything until \end{envName} as raw
        return ParseUnknownEnvironment(envName);
    }

    private MathNode ParseMatrix(string envName)
    {
        var rows = new List<List<MathNode>>();
        var currentRow = new List<MathNode>();

        while (Peek().Type != TokenType.End)
        {
            // Check for \end
            if (Peek().Type == TokenType.Command && Peek().Value == "\\end")
            {
                Advance();
                ParseBraceGroupRaw(); // consume {envName}
                break;
            }

            // Row separator \\
            if (Peek().Type == TokenType.Newline)
            {
                Advance();
                rows.Add(currentRow);
                currentRow = [];
                continue;
            }

            // Cell separator &
            if (Peek().Type == TokenType.Ampersand)
            {
                Advance();
                // Current cell is done, the content before & was already added
                // Start collecting next cell
                continue;
            }

            // Parse cell content
            var cellNodes = ParseSequence();
            var cellNode = cellNodes.Count == 1 ? cellNodes[0] : new GroupNode { Children = cellNodes };
            currentRow.Add(cellNode);
        }

        // Add last row if not empty
        if (currentRow.Count > 0)
            rows.Add(currentRow);

        return new MatrixNode { Rows = rows, MatrixType = envName };
    }

    private MathNode ParseUnknownEnvironment(string envName)
    {
        // Collect raw tokens until \end{envName}
        var parts = new List<string> { "\\begin{" + envName + "}" };
        var depth = 1;

        while (_pos < _tokens.Count && depth > 0)
        {
            var t = _tokens[_pos];

            if (t.Type == TokenType.Command && t.Value == "\\begin")
            {
                parts.Add(t.Value);
                _pos++;
                var inner = ParseBraceGroupRaw();
                parts.Add("{" + inner + "}");
                depth++;
                continue;
            }

            if (t.Type == TokenType.Command && t.Value == "\\end")
            {
                _pos++;
                var inner = ParseBraceGroupRaw();
                depth--;
                if (depth == 0)
                {
                    parts.Add("\\end{" + inner + "}");
                    break;
                }
                parts.Add("\\end{" + inner + "}");
                continue;
            }

            parts.Add(t.Value);
            _pos++;
        }

        return new RawNode { Latex = string.Join("", parts) };
    }

    private MathNode ParseNot()
    {
        // \not followed by a relation
        var next = ParseAtom();
        if (next is RelationNode rel)
            return new RelationNode { Symbol = "\\not" + rel.Symbol };
        if (next != null)
            return new RawNode { Latex = "\\not" + (next is RawNode rn ? rn.Latex : "") };
        return new RawNode { Latex = "\\not" };
    }

    private MathNode ParseBinom()
    {
        var top = ParseRequiredArg();
        var bottom = ParseRequiredArg();
        // Represent as a delimiter with a fraction-like content
        return new DelimiterNode
        {
            Left = "(",
            Right = ")",
            Content = new FractionNode { Numerator = top, Denominator = bottom }
        };
    }

    private MathNode ParseOverUnderSet(string cmd)
    {
        var first = ParseRequiredArg();
        var second = ParseRequiredArg();
        // Wrap as raw with semantic preservation
        return new RawNode { Latex = "\\" + cmd + "{" + NodeToLatex(first) + "}{" + NodeToLatex(second) + "}" };
    }

    private MathNode ParsePhantom(string cmd)
    {
        if (Peek().Type == TokenType.OpenBrace)
        {
            var raw = ParseBraceGroupRaw();
            return new RawNode { Latex = cmd + "{" + raw + "}" };
        }
        return new RawNode { Latex = cmd };
    }

    private MathNode ParseColor()
    {
        if (Peek().Type == TokenType.OpenBrace)
        {
            var color = ParseBraceGroupRaw();
            var content = ParseRequiredArg();
            return new RawNode { Latex = "\\color{" + color + "}" + NodeToLatex(content) };
        }
        return new RawNode { Latex = "\\color" };
    }

    private MathNode ParseBoxed()
    {
        var content = ParseRequiredArg();
        return new RawNode { Latex = "\\boxed{" + NodeToLatex(content) + "}" };
    }

    private MathNode ParseDelimiter(string left, string right)
    {
        var content = ParseSequence();

        // Consume the matching close delimiter
        if (right == ")" && Peek().Type == TokenType.CloseParen)
            Advance();
        else if (right == "]" && Peek().Type == TokenType.CloseBracket)
            Advance();

        var contentNode = content.Count == 1 ? content[0] : new GroupNode { Children = content };
        return new DelimiterNode { Left = left, Right = right, Content = contentNode };
    }

    /// <summary>
    /// Handle postfix operators: subscript _ and superscript ^.
    /// Combines into SubSuperscriptNode when both are present.
    /// </summary>
    private MathNode ParsePostfix(MathNode baseNode)
    {
        MathNode? sub = null;
        MathNode? sup = null;

        while (true)
        {
            // Skip whitespace
            while (Peek().Type == TokenType.Whitespace) Advance();

            if (Peek().Type == TokenType.Subscript && sub == null)
            {
                Advance();
                sub = ParseRequiredArg();
            }
            else if (Peek().Type == TokenType.Superscript && sup == null)
            {
                Advance();
                sup = ParseRequiredArg();
            }
            else
            {
                break;
            }
        }

        if (sub != null && sup != null)
            return new SubSuperscriptNode { Base = baseNode, Subscript = sub, Superscript = sup };
        if (sub != null)
            return new SubscriptNode { Base = baseNode, Subscript = sub };
        if (sup != null)
            return new SuperscriptNode { Base = baseNode, Exponent = sup };

        return baseNode;
    }

    /// <summary>
    /// Quick helper to re-serialize a node back to LaTeX (for RawNode wrapping).
    /// This is a best-effort reconstruction.
    /// </summary>
    private static string NodeToLatex(MathNode node)
    {
        return node switch
        {
            RawNode r => r.Latex,
            NumberNode n => n.Value,
            VariableNode v => v.Name,
            SymbolNode s => s.Name,
            OperatorNode o => o.Symbol,
            RelationNode r => r.Symbol,
            GroupNode g => string.Join("", g.Children.Select(NodeToLatex)),
            _ => "" // Best effort
        };
    }
}
