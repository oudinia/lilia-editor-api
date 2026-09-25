using FluentAssertions;
using Lilia.Api.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Which characters the paste chip may offer <i>Look up</i> for. Only a symbol
/// can have a command; for script and emoji a lookup would dead-end.
/// </summary>
public class UnicodeKindTests
{
    [Theory]
    [InlineData("ℵ", "symbol")]   // Letterlike Symbols: a letter by category, \aleph in use
    [InlineData("ℏ", "symbol")]
    [InlineData("𝔸", "symbol")]   // Mathematical Alphanumerics: \mathbb{A}
    [InlineData("ά", "symbol")]   // Greek is pasted as maths
    [InlineData("∯", "symbol")]
    [InlineData("✓", "symbol")]   // dingbats: many have commands
    [InlineData("₿", "symbol")]
    [InlineData("中", "script")]
    [InlineData("。", "script")]  // CJK punctuation comes with Chinese text; no command sets it
    [InlineData("ｱ", "script")]   // halfwidth katakana
    [InlineData("한", "script")]
    [InlineData("ש", "script")]
    [InlineData("ж", "script")]
    [InlineData("ạ", "script")]   // Vietnamese: Latin, but past what inputenc handles
    [InlineData("٣", "script")]   // Arabic-Indic digit
    [InlineData("🙂", "emoji")]
    [InlineData("🇫", "emoji")]   // regional indicator
    [InlineData("🏽", "emoji")]   // skin tone
    [InlineData("‍", "emoji")]
    [InlineData("️", "emoji")]
    public void Classifies(string ch, string kind)
    {
        UnicodeKind.Of(char.ConvertToUtf32(ch, 0)).Should().Be(kind);
    }
}
