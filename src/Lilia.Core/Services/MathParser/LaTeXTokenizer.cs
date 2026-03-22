namespace Lilia.Core.Services.MathParser;

public enum TokenType
{
    Command,        // \alpha, \frac, \sin, etc.
    OpenBrace,      // {
    CloseBrace,     // }
    OpenBracket,    // [
    CloseBracket,   // ]
    Superscript,    // ^
    Subscript,      // _
    Ampersand,      // &
    Newline,        // \\
    Number,         // 0-9 and .
    Letter,         // a-z, A-Z
    Operator,       // + - * /
    Relation,       // = < >
    OpenParen,      // (
    CloseParen,     // )
    Pipe,           // |
    Comma,          // ,
    Semicolon,      // ;
    Exclamation,    // !
    Whitespace,     // spaces/tabs
    Other,          // anything else
    End             // end of input
}

public readonly record struct Token(TokenType Type, string Value, int Position);

/// <summary>
/// Lexer for LaTeX math strings. Produces a flat stream of tokens.
/// </summary>
public class LaTeXTokenizer
{
    private readonly string _input;
    private int _pos;

    public LaTeXTokenizer(string input)
    {
        _input = input;
        _pos = 0;
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (_pos < _input.Length)
        {
            var ch = _input[_pos];

            switch (ch)
            {
                case '\\':
                    tokens.Add(ReadCommand());
                    break;
                case '{':
                    tokens.Add(new Token(TokenType.OpenBrace, "{", _pos++));
                    break;
                case '}':
                    tokens.Add(new Token(TokenType.CloseBrace, "}", _pos++));
                    break;
                case '[':
                    tokens.Add(new Token(TokenType.OpenBracket, "[", _pos++));
                    break;
                case ']':
                    tokens.Add(new Token(TokenType.CloseBracket, "]", _pos++));
                    break;
                case '^':
                    tokens.Add(new Token(TokenType.Superscript, "^", _pos++));
                    break;
                case '_':
                    tokens.Add(new Token(TokenType.Subscript, "_", _pos++));
                    break;
                case '&':
                    tokens.Add(new Token(TokenType.Ampersand, "&", _pos++));
                    break;
                case '(':
                    tokens.Add(new Token(TokenType.OpenParen, "(", _pos++));
                    break;
                case ')':
                    tokens.Add(new Token(TokenType.CloseParen, ")", _pos++));
                    break;
                case '|':
                    tokens.Add(new Token(TokenType.Pipe, "|", _pos++));
                    break;
                case ',':
                    tokens.Add(new Token(TokenType.Comma, ",", _pos++));
                    break;
                case ';':
                    tokens.Add(new Token(TokenType.Semicolon, ";", _pos++));
                    break;
                case '!':
                    tokens.Add(new Token(TokenType.Exclamation, "!", _pos++));
                    break;
                case '+' or '-' or '*' or '/':
                    tokens.Add(new Token(TokenType.Operator, ch.ToString(), _pos++));
                    break;
                case '=' or '<' or '>':
                    tokens.Add(new Token(TokenType.Relation, ch.ToString(), _pos++));
                    break;
                case ' ' or '\t' or '\r' or '\n':
                    tokens.Add(ReadWhitespace());
                    break;
                default:
                    if (char.IsDigit(ch) || (ch == '.' && _pos + 1 < _input.Length && char.IsDigit(_input[_pos + 1])))
                    {
                        tokens.Add(ReadNumber());
                    }
                    else if (char.IsLetter(ch))
                    {
                        tokens.Add(new Token(TokenType.Letter, ch.ToString(), _pos++));
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Other, ch.ToString(), _pos++));
                    }
                    break;
            }
        }

        tokens.Add(new Token(TokenType.End, "", _pos));
        return tokens;
    }

    private Token ReadCommand()
    {
        var start = _pos;
        _pos++; // skip backslash

        if (_pos >= _input.Length)
            return new Token(TokenType.Command, "\\", start);

        var ch = _input[_pos];

        // \\ is a newline command
        if (ch == '\\')
        {
            _pos++;
            return new Token(TokenType.Newline, "\\\\", start);
        }

        // Single non-letter commands: \, \; \: \! \{ \} \| \  etc.
        if (!char.IsLetter(ch))
        {
            _pos++;
            return new Token(TokenType.Command, "\\" + ch, start);
        }

        // Multi-letter command: read while letters
        var cmdStart = _pos;
        while (_pos < _input.Length && char.IsLetter(_input[_pos]))
            _pos++;

        return new Token(TokenType.Command, _input[start.._pos], start);
    }

    private Token ReadNumber()
    {
        var start = _pos;
        var hasDot = false;

        while (_pos < _input.Length)
        {
            var ch = _input[_pos];
            if (char.IsDigit(ch))
            {
                _pos++;
            }
            else if (ch == '.' && !hasDot)
            {
                hasDot = true;
                _pos++;
            }
            else
            {
                break;
            }
        }

        return new Token(TokenType.Number, _input[start.._pos], start);
    }

    private Token ReadWhitespace()
    {
        var start = _pos;
        while (_pos < _input.Length && _input[_pos] is ' ' or '\t' or '\r' or '\n')
            _pos++;
        return new Token(TokenType.Whitespace, _input[start.._pos], start);
    }
}
