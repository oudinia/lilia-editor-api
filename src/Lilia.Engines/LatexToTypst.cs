using System.Text;

namespace Lilia.Engines;

/// <summary>
/// LaTeX maths → Typst maths.
///
/// <para>A scanner, not a pile of rewrite rules. The generator it replaces
/// applied sixteen regexes in a fixed order and passed through whatever none
/// of them matched — which Typst then read as its own escapes, so
/// <c>\nabla</c> arrived as the variable <c>abla</c>. Order-dependent regex
/// rewriting also cannot see nesting: <c>\frac{\frac{a}{b}}{c}</c> needs the
/// inner braces resolved first, which is a parser's job.</para>
///
/// <para>Substitutions live in <see cref="LatexToTypstSymbols"/> as data.
/// What stays here is the part that needs judgement: arguments, nesting,
/// sub- and superscripts, delimiters and matrices.</para>
///
/// <para>Anything unrecognised is reported through <c>unmapped</c> rather than
/// dropped. The previous generator emitted an empty <c>$  $</c> for an
/// equation it could not express — a document that compiles perfectly with a
/// blank space where the mathematics was, which is the worst way to fail.</para>
/// </summary>
public static class LatexToTypst
{
    public static string Convert(string latex) => Convert(latex, out _);

    /// <param name="unmapped">
    /// Commands with no known Typst equivalent, in the order met. A caller
    /// that cares — the export telemetry does — can decide whether to trust
    /// the result or fall back to LaTeX.
    /// </param>
    public static string Convert(string latex, out IReadOnlyList<string> unmapped)
    {
        var found = new List<string>();
        unmapped = found;
        if (string.IsNullOrWhiteSpace(latex)) return "";

        var scanner = new Scanner(latex, found);
        return scanner.ReadUntilEnd().Trim();
    }

    private sealed class Scanner(string src, List<string> unmapped)
    {
        private int _i;

        private bool AtEnd => _i >= src.Length;
        private char Current => src[_i];

        /// <summary>
        /// Append a Typst token, inserting a separator only where one is
        /// needed.
        ///
        /// <para>Padding every symbol with spaces looks harmless and is not:
        /// "integral " followed by "_(0)" gives <c>integral _(0)</c>, which
        /// Typst reads as an integral and then a separate subscript on
        /// nothing. Two adjacent identifiers, on the other hand, must be
        /// separated or <c>alpha beta</c> becomes the single unknown variable
        /// <c>alphabeta</c>.</para>
        /// </summary>
        private static void AppendToken(StringBuilder sb, string token)
        {
            if (token.Length == 0) return;

            var prev = sb.Length > 0 ? sb[^1] : '\0';
            var needsGap = IsWordChar(prev) && IsWordChar(token[0]);
            if (needsGap) sb.Append(' ');
            sb.Append(token);
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '.';

        internal string ReadUntilEnd()
        {
            var sb = new StringBuilder();
            while (!AtEnd) ReadOne(sb);
            return sb.ToString();
        }

        /// <summary>Read to the matching close brace, exclusive.</summary>
        private string ReadGroup()
        {
            SkipWhitespace();
            if (AtEnd) return "";

            if (Current != '{')
            {
                // A single token is a legitimate argument: \frac12, x^2.
                var sb1 = new StringBuilder();
                ReadOne(sb1);
                return sb1.ToString().Trim();
            }

            _i++;                                   // {
            var depth = 1;
            var start = _i;
            while (!AtEnd && depth > 0)
            {
                if (Current == '\\' && _i + 1 < src.Length) { _i += 2; continue; }
                if (Current == '{') depth++;
                else if (Current == '}') depth--;
                _i++;
            }

            var inner = src[start..(depth == 0 ? _i - 1 : _i)];
            return Convert(inner, out var nested) is var converted && nested.Count > 0
                ? Record(converted, nested)
                : converted;
        }

        private string Record(string converted, IReadOnlyList<string> nested)
        {
            unmapped.AddRange(nested);
            return converted;
        }

        /// <summary>An optional [n] argument, as \sqrt[3]{x} takes.</summary>
        private string? ReadOptional()
        {
            SkipWhitespace();
            if (AtEnd || Current != '[') return null;
            var close = src.IndexOf(']', _i);
            if (close < 0) return null;
            var inner = src[(_i + 1)..close];
            _i = close + 1;
            return Convert(inner, out _);
        }

        private void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(Current)) _i++;
        }

        private void ReadOne(StringBuilder sb)
        {
            var c = Current;

            switch (c)
            {
                case '\\':
                    ReadCommand(sb);
                    return;

                case '{':
                    // A bare group is grouping only; Typst uses parentheses.
                    sb.Append('(').Append(ReadGroup()).Append(')');
                    return;

                case '}':
                    _i++;                            // unbalanced; drop it rather than emit it
                    return;

                case '^':
                case '_':
                {
                    _i++;
                    // 90^\circ is degrees. \circ alone is composition, which is
                    // what the symbol table says — correct there, wrong here.
                    SkipWhitespace();
                    if (c == '^' && src.AsSpan(_i).StartsWith("\\circ"))
                    {
                        _i += 5;
                        sb.Append("^(degree)");
                        return;
                    }
                    var script = ReadGroup();
                    // A single atom needs no parentheses: c^2, not c^(2).
                    // Anything longer does — Typst attaches only the next
                    // token, so x^(10) is right and x^ab would mean (x^a)b.
                    sb.Append(c);
                    if (script.Length == 1 && char.IsLetterOrDigit(script[0])) sb.Append(script);
                    else sb.Append('(').Append(script).Append(')');
                    return;
                }

                case '&':
                    _i++;
                    sb.Append(',');                  // matrix column separator
                    return;

                case '~':
                    _i++;
                    sb.Append(" ");
                    return;

                default:
                    _i++;
                    AppendToken(sb, c.ToString());
                    return;
            }
        }

        private void ReadCommand(StringBuilder sb)
        {
            _i++;                                    // backslash
            if (AtEnd) return;

            // Non-letter commands: \, \; \! \\ \{ \} \%
            if (!char.IsLetter(Current))
            {
                var punct = Current.ToString();
                _i++;

                if (punct == "\\") { sb.Append(';'); return; }          // matrix row break
                if (LatexToTypstSymbols.Spacing.TryGetValue(punct, out var space))
                {
                    if (space.Length > 0) sb.Append(' ').Append(space).Append(' ');
                    return;
                }
                sb.Append(punct);                                        // \{ \} \% — literal
                return;
            }

            var start = _i;
            while (!AtEnd && char.IsLetter(Current)) _i++;
            var name = src[start.._i];

            switch (name)
            {
                case "frac":
                case "dfrac":
                case "tfrac":
                {
                    var num = ReadGroup();
                    var den = ReadGroup();
                    sb.Append("frac(").Append(num).Append(", ").Append(den).Append(')');
                    return;
                }

                case "sqrt":
                {
                    var index = ReadOptional();
                    var body = ReadGroup();
                    sb.Append(index is null ? $"sqrt({body})" : $"root({index}, {body})");
                    return;
                }

                case "begin":
                    ReadEnvironment(sb);
                    return;

                case "end":
                    ReadGroup();                                         // consumed by ReadEnvironment
                    return;

                case "left":
                case "right":
                    // Typst sizes delimiters itself; the bare delimiter is enough.
                    SkipWhitespace();
                    if (!AtEnd)
                    {
                        if (Current == '\\') { ReadCommand(sb); return; }
                        if (Current == '.') { _i++; return; }            // \left. is invisible
                        sb.Append(Current);
                        _i++;
                    }
                    return;

                case "xrightarrow":
                case "xleftarrow":
                {
                    // The label sits above the arrow: \xrightarrow{f} → ->^(f).
                    // Six uses in one real document, which is how it was found.
                    var above = ReadOptional() ?? ReadGroup();
                    var arrow = name == "xrightarrow" ? "arrow.r.long" : "arrow.l.long";
                    AppendToken(sb, arrow);
                    if (!string.IsNullOrWhiteSpace(above)) sb.Append("^(").Append(above).Append(')');
                    return;
                }

                case "pmod":
                    sb.Append(" mod ").Append(ReadGroup());
                    return;

                case "operatorname":
                    sb.Append("op(\"").Append(ReadGroup()).Append("\")");
                    return;
            }

            if (LatexToTypstSymbols.Accents.TryGetValue(name, out var accent))
            {
                sb.Append(accent).Append('(').Append(ReadGroup()).Append(')');
                return;
            }

            if (LatexToTypstSymbols.Fonts.TryGetValue(name, out var font))
            {
                var body = ReadGroup();
                // \text{…} becomes a Typst string, which is how prose enters maths.
                sb.Append(font == "text" ? $"\"{body}\"" : $"{font}({body})");
                return;
            }

            if (LatexToTypstSymbols.Symbols.TryGetValue(name, out var symbol))
            {
                AppendToken(sb, symbol);
                return;
            }

            if (LatexToTypstSymbols.Functions.Contains(name))
            {
                AppendToken(sb, name);
                return;
            }

            // Unknown. Emit the bare name so the reader still sees something,
            // and tell the caller — dropping it silently is how an equation
            // became "$  $".
            unmapped.Add(name);
            AppendToken(sb, name);
        }

        /// <summary>\begin{pmatrix} a &amp; b \\ c &amp; d \end{pmatrix} → mat(a, b; c, d).</summary>
        private void ReadEnvironment(StringBuilder sb)
        {
            var env = ReadGroupRaw();
            var body = ReadUntilEndOf(env);

            var delim = env switch
            {
                "pmatrix" => "\"(\"",
                "bmatrix" => "\"[\"",
                "Bmatrix" => "\"{\"",
                "vmatrix" => "\"|\"",
                "Vmatrix" => "\"||\"",
                "matrix" => "#none",
                _ => null,
            };

            if (delim is not null)
            {
                sb.Append("mat(delim: ").Append(delim).Append(", ")
                  .Append(Convert(body, out var nested).Trim()).Append(')');
                unmapped.AddRange(nested);
                return;
            }

            if (env is "cases")
            {
                sb.Append("cases(").Append(Convert(body, out var nested).Trim()).Append(')');
                unmapped.AddRange(nested);
                return;
            }

            if (env is "aligned" or "align" or "align*" or "gathered" or "split")
            {
                sb.Append(Convert(body, out var nested).Trim());
                unmapped.AddRange(nested);
                return;
            }

            unmapped.Add($"begin{{{env}}}");
            sb.Append(Convert(body, out var rest).Trim());
            unmapped.AddRange(rest);
        }

        /// <summary>The literal text of a {…} group, unconverted — an environment name.</summary>
        private string ReadGroupRaw()
        {
            SkipWhitespace();
            if (AtEnd || Current != '{') return "";
            _i++;
            var start = _i;
            while (!AtEnd && Current != '}') _i++;
            var name = src[start.._i];
            if (!AtEnd) _i++;
            return name;
        }

        private string ReadUntilEndOf(string env)
        {
            var marker = $"\\end{{{env}}}";
            var at = src.IndexOf(marker, _i, StringComparison.Ordinal);
            if (at < 0)
            {
                var rest = src[_i..];
                _i = src.Length;
                return rest;
            }
            var body = src[_i..at];
            _i = at + marker.Length;
            return body;
        }
    }
}
