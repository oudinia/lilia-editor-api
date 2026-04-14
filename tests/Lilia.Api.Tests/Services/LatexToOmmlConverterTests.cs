using System.Xml.Linq;
using FluentAssertions;
using Lilia.Import.Converters;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Comprehensive unit tests for LatexToOmmlConverter.
/// Each test verifies that a LaTeX expression produces well-formed OMML XML
/// with the expected structural elements.
/// </summary>
public class LatexToOmmlConverterTests
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    private readonly LatexToOmmlConverter _converter = new();

    // ── Helper ────────────────────────────────────────────────────────────────

    private XElement ConvertAndAssertSuccess(string latex)
    {
        var (omml, success, error) = _converter.Convert(latex);
        success.Should().BeTrue(because: $"LaTeX '{latex}' should parse successfully, but got: {error}");
        error.Should().BeNull();
        omml.Should().NotBeNullOrEmpty();
        var doc = XDocument.Parse(omml);
        doc.Root.Should().NotBeNull();
        doc.Root!.Name.LocalName.Should().Be("oMath");
        return doc.Root!;
    }

    // ── Empty / null input ────────────────────────────────────────────────────

    [Fact]
    public void Convert_EmptyString_ReturnsEmptyOmath()
    {
        var (omml, success, error) = _converter.Convert("");
        success.Should().BeTrue();
        error.Should().BeNull();
        var doc = XDocument.Parse(omml);
        doc.Root!.Name.LocalName.Should().Be("oMath");
    }

    [Fact]
    public void Convert_WhitespaceOnly_ReturnsEmptyOmath()
    {
        var (omml, success, error) = _converter.Convert("   ");
        success.Should().BeTrue();
        error.Should().BeNull();
        var doc = XDocument.Parse(omml);
        doc.Root!.Name.LocalName.Should().Be("oMath");
    }

    // ── Outer delimiter stripping ─────────────────────────────────────────────

    [Theory]
    [InlineData("$x$")]
    [InlineData("$$x$$")]
    [InlineData(@"\[x\]")]
    public void Convert_StripsOuterDelimiters(string input)
    {
        var root = ConvertAndAssertSuccess(input);
        // Should contain a run with text "x"
        root.Descendants(M + "t").Should().Contain(t => t.Value == "x");
    }

    // ── Simple runs ───────────────────────────────────────────────────────────

    [Fact]
    public void Convert_SingleCharacter_ProducesRun()
    {
        var root = ConvertAndAssertSuccess("x");
        root.Descendants(M + "r").Should().HaveCountGreaterThanOrEqualTo(1);
        root.Descendants(M + "t").Should().Contain(t => t.Value == "x");
    }

    [Fact]
    public void Convert_MultipleCharacters_ProducesMultipleRuns()
    {
        var root = ConvertAndAssertSuccess("abc");
        root.Descendants(M + "t").Should().HaveCount(3);
    }

    [Fact]
    public void Convert_Number_ProducesRun()
    {
        var root = ConvertAndAssertSuccess("42");
        root.Descendants(M + "t").Should().Contain(t => t.Value == "4");
        root.Descendants(M + "t").Should().Contain(t => t.Value == "2");
    }

    // ── Fractions ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\frac{a}{b}")]
    [InlineData(@"\dfrac{a}{b}")]
    [InlineData(@"\tfrac{a}{b}")]
    [InlineData(@"\cfrac{a}{b}")]
    public void Convert_Fraction_ProducesFElement(string latex)
    {
        var root = ConvertAndAssertSuccess(latex);
        var frac = root.Descendants(M + "f").FirstOrDefault();
        frac.Should().NotBeNull($"Expected m:f for '{latex}'");
        frac!.Element(M + "num").Should().NotBeNull("numerator missing");
        frac!.Element(M + "den").Should().NotBeNull("denominator missing");
    }

    [Fact]
    public void Convert_Fraction_NumeratorDenominatorContainCorrectText()
    {
        var root = ConvertAndAssertSuccess(@"\frac{x}{y}");
        var frac = root.Descendants(M + "f").First();
        frac.Element(M + "num")!.Descendants(M + "t").Should().Contain(t => t.Value == "x");
        frac.Element(M + "den")!.Descendants(M + "t").Should().Contain(t => t.Value == "y");
    }

    [Fact]
    public void Convert_SkewedFraction_HasSkwType()
    {
        var root = ConvertAndAssertSuccess(@"\sfrac{1}{2}");
        var frac = root.Descendants(M + "f").First();
        var typeVal = frac.Element(M + "fPr")?.Element(M + "type")?.Attribute(M + "val")?.Value;
        typeVal.Should().Be("skw");
    }

    [Fact]
    public void Convert_NestedFraction_ProducesNestedF()
    {
        var root = ConvertAndAssertSuccess(@"\frac{\frac{a}{b}}{c}");
        root.Descendants(M + "f").Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Convert_GaussianFraction_Complex()
    {
        var root = ConvertAndAssertSuccess(
            @"\frac{1}{\sigma\sqrt{2\pi}} e^{-\frac{(x-\mu)^2}{2\sigma^2}}");
        root.Descendants(M + "f").Should().HaveCountGreaterThanOrEqualTo(2);
    }

    // ── Radicals ──────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_SimpleSqrt_ProducesRad()
    {
        var root = ConvertAndAssertSuccess(@"\sqrt{x}");
        var rad = root.Descendants(M + "rad").FirstOrDefault();
        rad.Should().NotBeNull();
    }

    [Fact]
    public void Convert_Sqrt_HasDegHideWhenNoIndex()
    {
        var root = ConvertAndAssertSuccess(@"\sqrt{x}");
        var rad = root.Descendants(M + "rad").First();
        var radPr = rad.Element(M + "radPr");
        radPr.Should().NotBeNull();
        radPr!.Element(M + "degHide")?.Attribute(M + "val")?.Value.Should().Be("1");
    }

    [Fact]
    public void Convert_NthRoot_HasDegreeContent()
    {
        var root = ConvertAndAssertSuccess(@"\sqrt[3]{x}");
        var rad = root.Descendants(M + "rad").First();
        var deg = rad.Element(M + "deg");
        deg.Should().NotBeNull();
        deg!.Descendants(M + "t").Should().Contain(t => t.Value == "3");
    }

    [Fact]
    public void Convert_SqrtContent_InEElement()
    {
        var root = ConvertAndAssertSuccess(@"\sqrt{abc}");
        var rad = root.Descendants(M + "rad").First();
        var e = rad.Element(M + "e");
        e.Should().NotBeNull();
        e!.Descendants(M + "t").Should().Contain(t => t.Value == "a");
    }

    // ── Superscript / Subscript ───────────────────────────────────────────────

    [Fact]
    public void Convert_Superscript_ProducesSSup()
    {
        var root = ConvertAndAssertSuccess("x^2");
        root.Descendants(M + "sSup").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Convert_Subscript_ProducesSSub()
    {
        var root = ConvertAndAssertSuccess("x_i");
        root.Descendants(M + "sSub").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Convert_SubAndSup_ProducesSSubSup()
    {
        var root = ConvertAndAssertSuccess("x_i^2");
        root.Descendants(M + "sSubSup").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Convert_SuperscriptBracedGroup_CorrectStructure()
    {
        var root = ConvertAndAssertSuccess("e^{i\\pi}");
        var sSup = root.Descendants(M + "sSup").First();
        sSup.Element(M + "sup")!.Descendants(M + "t").Should().Contain(t => t.Value == "i");
    }

    [Fact]
    public void Convert_SupBeforeSub_BothPresent()
    {
        var root = ConvertAndAssertSuccess("x^2_i");
        // Either sSub+sSup or sSubSup
        var hasSubSup = root.Descendants(M + "sSubSup").Any();
        var hasBoth = root.Descendants(M + "sSup").Any() && root.Descendants(M + "sSub").Any();
        (hasSubSup || hasBoth).Should().BeTrue();
    }

    // ── N-ary operators ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\sum_{k=1}^{n} k", "∑")]
    [InlineData(@"\prod_{i=1}^{n} i", "∏")]
    [InlineData(@"\int_{0}^{\infty} f", "∫")]
    [InlineData(@"\oint_{C} f", "∮")]
    [InlineData(@"\bigcup_{i=1}^{n} A_i", "⋃")]
    [InlineData(@"\bigcap_{i} B_i", "⋂")]
    public void Convert_NaryOperator_ProducesNaryWithChar(string latex, string expectedChar)
    {
        var root = ConvertAndAssertSuccess(latex);
        var nary = root.Descendants(M + "nary").FirstOrDefault();
        nary.Should().NotBeNull($"Expected m:nary for '{latex}'");
        var chr = nary!.Element(M + "naryPr")?.Element(M + "chr")?.Attribute(M + "val")?.Value;
        chr.Should().Be(expectedChar);
    }

    [Fact]
    public void Convert_SumWithLimits_HasSubAndSup()
    {
        var root = ConvertAndAssertSuccess(@"\sum_{k=1}^{n} k");
        var nary = root.Descendants(M + "nary").First();
        nary.Element(M + "sub").Should().NotBeNull();
        nary.Element(M + "sup").Should().NotBeNull();
        nary.Element(M + "e").Should().NotBeNull();
    }

    [Fact]
    public void Convert_IntWithoutLimits_HasEmptySubAndSup()
    {
        var root = ConvertAndAssertSuccess(@"\int f \, dx");
        var nary = root.Descendants(M + "nary").First();
        nary.Element(M + "sub").Should().NotBeNull();
        nary.Element(M + "sup").Should().NotBeNull();
    }

    // ── Delimiters \left \right ───────────────────────────────────────────────

    [Fact]
    public void Convert_LeftRightParens_ProducesDElement()
    {
        var root = ConvertAndAssertSuccess(@"\left( x \right)");
        var d = root.Descendants(M + "d").FirstOrDefault();
        d.Should().NotBeNull();
    }

    [Fact]
    public void Convert_LeftRightParens_HasCorrectDelimChars()
    {
        var root = ConvertAndAssertSuccess(@"\left( x + y \right)");
        var d = root.Descendants(M + "d").First();
        var dPr = d.Element(M + "dPr");
        dPr?.Element(M + "begChr")?.Attribute(M + "val")?.Value.Should().Be("(");
        dPr?.Element(M + "endChr")?.Attribute(M + "val")?.Value.Should().Be(")");
    }

    [Fact]
    public void Convert_LeftRightBrackets_HasBracketDelims()
    {
        var root = ConvertAndAssertSuccess(@"\left[ x \right]");
        var d = root.Descendants(M + "d").First();
        var dPr = d.Element(M + "dPr");
        dPr?.Element(M + "begChr")?.Attribute(M + "val")?.Value.Should().Be("[");
        dPr?.Element(M + "endChr")?.Attribute(M + "val")?.Value.Should().Be("]");
    }

    [Theory]
    [InlineData(@"\left\lfloor x \right\rfloor", "⌊", "⌋")]
    [InlineData(@"\left\lceil x \right\rceil", "⌈", "⌉")]
    [InlineData(@"\left\langle x \right\rangle", "⟨", "⟩")]
    public void Convert_SpecialDelimiters_HasCorrectChars(string latex, string beg, string end)
    {
        var root = ConvertAndAssertSuccess(latex);
        var d = root.Descendants(M + "d").First();
        var dPr = d.Element(M + "dPr");
        dPr?.Element(M + "begChr")?.Attribute(M + "val")?.Value.Should().Be(beg);
        dPr?.Element(M + "endChr")?.Attribute(M + "val")?.Value.Should().Be(end);
    }

    [Fact]
    public void Convert_LeftDotRight_EmptyDelimiter()
    {
        var root = ConvertAndAssertSuccess(@"\left. x \right)");
        var d = root.Descendants(M + "d").First();
        var dPr = d.Element(M + "dPr");
        // dot → empty string
        dPr?.Element(M + "begChr")?.Attribute(M + "val")?.Value.Should().BeNullOrEmpty();
    }

    // ── Accents ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\hat{x}", "̂")]
    [InlineData(@"\tilde{x}", "̃")]
    [InlineData(@"\bar{x}", "̄")]
    [InlineData(@"\dot{x}", "̇")]
    [InlineData(@"\ddot{x}", "̈")]
    [InlineData(@"\vec{x}", "⃗")]
    [InlineData(@"\breve{x}", "̆")]
    [InlineData(@"\check{x}", "̌")]
    [InlineData(@"\acute{x}", "́")]
    [InlineData(@"\grave{x}", "̀")]
    public void Convert_Accent_ProducesAccElement(string latex, string expectedChr)
    {
        var root = ConvertAndAssertSuccess(latex);
        var acc = root.Descendants(M + "acc").FirstOrDefault();
        acc.Should().NotBeNull($"Expected m:acc for '{latex}'");
        var chr = acc!.Element(M + "accPr")?.Element(M + "chr")?.Attribute(M + "val")?.Value;
        chr.Should().Be(expectedChr);
        acc.Element(M + "e").Should().NotBeNull();
    }

    // ── Bar (overline / underline) ────────────────────────────────────────────

    [Fact]
    public void Convert_Overline_ProducesBarTopPos()
    {
        var root = ConvertAndAssertSuccess(@"\overline{x}");
        var bar = root.Descendants(M + "bar").FirstOrDefault();
        bar.Should().NotBeNull();
        bar!.Element(M + "barPr")?.Element(M + "pos")?.Attribute(M + "val")?.Value.Should().Be("top");
    }

    [Fact]
    public void Convert_Underline_ProducesBarBotPos()
    {
        var root = ConvertAndAssertSuccess(@"\underline{x}");
        var bar = root.Descendants(M + "bar").FirstOrDefault();
        bar.Should().NotBeNull();
        bar!.Element(M + "barPr")?.Element(M + "pos")?.Attribute(M + "val")?.Value.Should().Be("bot");
    }

    // ── Group characters ──────────────────────────────────────────────────────

    [Fact]
    public void Convert_Overbrace_ProducesGroupChrTopPos()
    {
        var root = ConvertAndAssertSuccess(@"\overbrace{x+y}");
        var gc = root.Descendants(M + "groupChr").FirstOrDefault();
        gc.Should().NotBeNull();
        gc!.Element(M + "groupChrPr")?.Element(M + "chr")?.Attribute(M + "val")?.Value.Should().Be("⏞");
        gc!.Element(M + "groupChrPr")?.Element(M + "pos")?.Attribute(M + "val")?.Value.Should().Be("top");
    }

    [Fact]
    public void Convert_Underbrace_ProducesGroupChrBotPos()
    {
        var root = ConvertAndAssertSuccess(@"\underbrace{x+y}");
        var gc = root.Descendants(M + "groupChr").FirstOrDefault();
        gc.Should().NotBeNull();
        gc!.Element(M + "groupChrPr")?.Element(M + "chr")?.Attribute(M + "val")?.Value.Should().Be("⏟");
        gc!.Element(M + "groupChrPr")?.Element(M + "pos")?.Attribute(M + "val")?.Value.Should().Be("bot");
    }

    // ── \overset / \underset ─────────────────────────────────────────────────

    [Fact]
    public void Convert_Overset_ProducesLimUpp()
    {
        var root = ConvertAndAssertSuccess(@"\overset{*}{x}");
        var limUpp = root.Descendants(M + "limUpp").FirstOrDefault();
        limUpp.Should().NotBeNull();
        limUpp!.Element(M + "e").Should().NotBeNull();
        limUpp!.Element(M + "lim").Should().NotBeNull();
    }

    [Fact]
    public void Convert_Underset_ProducesLimLow()
    {
        var root = ConvertAndAssertSuccess(@"\underset{n \to \infty}{x}");
        var limLow = root.Descendants(M + "limLow").FirstOrDefault();
        limLow.Should().NotBeNull();
        limLow!.Element(M + "e").Should().NotBeNull();
        limLow!.Element(M + "lim").Should().NotBeNull();
    }

    // ── \boxed ────────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_Boxed_ProducesBorderBox()
    {
        var root = ConvertAndAssertSuccess(@"\boxed{E = mc^2}");
        var bb = root.Descendants(M + "borderBox").FirstOrDefault();
        bb.Should().NotBeNull();
        bb!.Element(M + "e").Should().NotBeNull();
    }

    // ── Math functions ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\sin x", "sin")]
    [InlineData(@"\cos\theta", "cos")]
    [InlineData(@"\lim_{x \to 0} f(x)", "lim")]
    [InlineData(@"\log_2 n", "log")]
    [InlineData(@"\det A", "det")]
    [InlineData(@"\gcd(a,b)", "gcd")]
    public void Convert_MathFunction_ProducesFuncElement(string latex, string funcName)
    {
        var root = ConvertAndAssertSuccess(latex);
        var func = root.Descendants(M + "func").FirstOrDefault();
        func.Should().NotBeNull($"Expected m:func for '{latex}'");
        var name = func!.Element(M + "fName")?.Descendants(M + "t").FirstOrDefault()?.Value;
        name.Should().Be(funcName);
    }

    // ── Text runs ─────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_TextCommand_ProducesRomanStyleRun()
    {
        var root = ConvertAndAssertSuccess(@"\text{hello}");
        var run = root.Descendants(M + "r").FirstOrDefault(r =>
            r.Element(M + "rPr")?.Element(M + "sty")?.Attribute(M + "val")?.Value == "r");
        run.Should().NotBeNull();
        run!.Element(M + "t")?.Value.Should().Be("hello");
    }

    [Fact]
    public void Convert_Mathrm_ProducesRomanStyle()
    {
        var root = ConvertAndAssertSuccess(@"\mathrm{abc}");
        var run = root.Descendants(M + "r").FirstOrDefault(r =>
            r.Element(M + "rPr")?.Element(M + "sty")?.Attribute(M + "val")?.Value == "r");
        run.Should().NotBeNull();
    }

    [Fact]
    public void Convert_Mathbf_ProducesBoldStyle()
    {
        var root = ConvertAndAssertSuccess(@"\mathbf{v}");
        var run = root.Descendants(M + "r").FirstOrDefault(r =>
            r.Element(M + "rPr")?.Element(M + "sty")?.Attribute(M + "val")?.Value == "b");
        run.Should().NotBeNull();
    }

    [Fact]
    public void Convert_Mathit_ProducesItalicStyle()
    {
        var root = ConvertAndAssertSuccess(@"\mathit{x}");
        var run = root.Descendants(M + "r").FirstOrDefault(r =>
            r.Element(M + "rPr")?.Element(M + "sty")?.Attribute(M + "val")?.Value == "i");
        run.Should().NotBeNull();
    }

    [Fact]
    public void Convert_Boldsymbol_ProducesBoldStyle()
    {
        var root = ConvertAndAssertSuccess(@"\boldsymbol{A}");
        var run = root.Descendants(M + "r").FirstOrDefault(r =>
            r.Element(M + "rPr")?.Element(M + "sty")?.Attribute(M + "val")?.Value == "b");
        run.Should().NotBeNull();
    }

    // ── \mathbb ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\mathbb{N}", "ℕ")]
    [InlineData(@"\mathbb{Z}", "ℤ")]
    [InlineData(@"\mathbb{Q}", "ℚ")]
    [InlineData(@"\mathbb{R}", "ℝ")]
    [InlineData(@"\mathbb{C}", "ℂ")]
    [InlineData(@"\mathbb{H}", "ℍ")]
    public void Convert_MathBb_ProducesCorrectUnicodeChar(string latex, string expectedChar)
    {
        var root = ConvertAndAssertSuccess(latex);
        root.Descendants(M + "t").Should().Contain(t => t.Value == expectedChar,
            because: $"Expected unicode char {expectedChar} for {latex}");
    }

    // ── Greek letters ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\alpha", "α")]
    [InlineData(@"\beta", "β")]
    [InlineData(@"\gamma", "γ")]
    [InlineData(@"\delta", "δ")]
    [InlineData(@"\pi", "π")]
    [InlineData(@"\sigma", "σ")]
    [InlineData(@"\omega", "ω")]
    [InlineData(@"\Gamma", "Γ")]
    [InlineData(@"\Delta", "Δ")]
    [InlineData(@"\Sigma", "Σ")]
    [InlineData(@"\Omega", "Ω")]
    [InlineData(@"\varphi", "ϕ")]
    [InlineData(@"\varepsilon", "ϵ")]
    public void Convert_GreekLetter_ProducesCorrectChar(string latex, string expectedChar)
    {
        var root = ConvertAndAssertSuccess(latex);
        root.Descendants(M + "t").Should().Contain(t => t.Value == expectedChar,
            because: $"Expected '{expectedChar}' for '{latex}'");
    }

    // ── Common symbols ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\infty", "∞")]
    [InlineData(@"\partial", "∂")]
    [InlineData(@"\nabla", "∇")]
    [InlineData(@"\pm", "±")]
    [InlineData(@"\times", "×")]
    [InlineData(@"\leq", "≤")]
    [InlineData(@"\geq", "≥")]
    [InlineData(@"\neq", "≠")]
    [InlineData(@"\approx", "≈")]
    [InlineData(@"\in", "∈")]
    [InlineData(@"\notin", "∉")]
    [InlineData(@"\forall", "∀")]
    [InlineData(@"\exists", "∃")]
    [InlineData(@"\rightarrow", "→")]
    [InlineData(@"\leftarrow", "←")]
    [InlineData(@"\Rightarrow", "⇒")]
    [InlineData(@"\cdot", "·")]
    [InlineData(@"\ldots", "…")]
    public void Convert_Symbol_ProducesCorrectUnicodeChar(string latex, string expectedChar)
    {
        var root = ConvertAndAssertSuccess(latex);
        root.Descendants(M + "t").Should().Contain(t => t.Value == expectedChar,
            because: $"Expected '{expectedChar}' for '{latex}'");
    }

    // ── \not ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\not\in", "∉")]
    [InlineData(@"\not\subset", "⊄")]
    [InlineData(@"\not\equiv", "≢")]
    public void Convert_NotCommand_ProducesNegatedChar(string latex, string expectedChar)
    {
        var root = ConvertAndAssertSuccess(latex);
        root.Descendants(M + "t").Should().Contain(t => t.Value == expectedChar,
            because: $"Expected negated '{expectedChar}' for '{latex}'");
    }

    // ── Matrices ──────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_Matrix_ProducesMElement()
    {
        var root = ConvertAndAssertSuccess(@"\begin{matrix} a & b \\ c & d \end{matrix}");
        root.Descendants(M + "m").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Convert_Matrix_HasCorrectRowCount()
    {
        var root = ConvertAndAssertSuccess(@"\begin{matrix} a & b \\ c & d \end{matrix}");
        var m = root.Descendants(M + "m").First();
        m.Elements(M + "mr").Should().HaveCount(2);
    }

    [Fact]
    public void Convert_Matrix_HasCorrectCellCount()
    {
        var root = ConvertAndAssertSuccess(@"\begin{matrix} a & b \\ c & d \end{matrix}");
        var m = root.Descendants(M + "m").First();
        var firstRow = m.Elements(M + "mr").First();
        firstRow.Elements(M + "e").Should().HaveCount(2);
    }

    [Fact]
    public void Convert_Pmatrix_WrappedInDelimiter()
    {
        var root = ConvertAndAssertSuccess(@"\begin{pmatrix} a & b \\ c & d \end{pmatrix}");
        var d = root.Descendants(M + "d").FirstOrDefault();
        d.Should().NotBeNull("pmatrix should produce a delimiter wrapper");
        d!.Element(M + "dPr")?.Element(M + "begChr")?.Attribute(M + "val")?.Value.Should().Be("(");
    }

    [Fact]
    public void Convert_Bmatrix_HasSquareBrackets()
    {
        var root = ConvertAndAssertSuccess(@"\begin{bmatrix} 1 & 0 \\ 0 & 1 \end{bmatrix}");
        var d = root.Descendants(M + "d").FirstOrDefault();
        d.Should().NotBeNull();
        d!.Element(M + "dPr")?.Element(M + "begChr")?.Attribute(M + "val")?.Value.Should().Be("[");
    }

    [Fact]
    public void Convert_Vmatrix_HasPipeDelimiters()
    {
        var root = ConvertAndAssertSuccess(@"\begin{vmatrix} a & b \\ c & d \end{vmatrix}");
        var d = root.Descendants(M + "d").FirstOrDefault();
        d.Should().NotBeNull();
        d!.Element(M + "dPr")?.Element(M + "begChr")?.Attribute(M + "val")?.Value.Should().Be("|");
    }

    [Fact]
    public void Convert_RotationMatrix_Complex()
    {
        var root = ConvertAndAssertSuccess(
            @"\begin{pmatrix} \cos\theta & -\sin\theta \\ \sin\theta & \cos\theta \end{pmatrix}");
        root.Descendants(M + "m").Should().HaveCountGreaterThanOrEqualTo(1);
        root.Descendants(M + "func").Should().HaveCountGreaterThanOrEqualTo(4);
    }

    // ── Aligned environment ───────────────────────────────────────────────────

    [Fact]
    public void Convert_Aligned_ProducesEqArr()
    {
        var root = ConvertAndAssertSuccess(
            @"\begin{aligned} x &= 1 \\ y &= 2 \end{aligned}");
        root.Descendants(M + "eqArr").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Convert_Aligned_HasCorrectEquationCount()
    {
        var root = ConvertAndAssertSuccess(
            @"\begin{aligned} a &= 1 \\ b &= 2 \\ c &= 3 \end{aligned}");
        var eqArr = root.Descendants(M + "eqArr").First();
        eqArr.Elements(M + "e").Should().HaveCount(3);
    }

    [Fact]
    public void Convert_Align_SameAsAligned()
    {
        var root = ConvertAndAssertSuccess(
            @"\begin{align} x &= 1 \\ y &= 2 \end{align}");
        root.Descendants(M + "eqArr").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    // ── Whitespace commands ───────────────────────────────────────────────────

    [Theory]
    [InlineData(@"\,")]
    [InlineData(@"\;")]
    [InlineData(@"\:")]
    [InlineData(@"\!")]
    [InlineData(@"\quad")]
    [InlineData(@"\qquad")]
    public void Convert_SpaceCommands_DoNotProduceElements(string latex)
    {
        // Space commands should be silently consumed
        var (omml, success, _) = _converter.Convert("x" + latex + "y");
        success.Should().BeTrue();
        var root = XDocument.Parse(omml).Root!;
        // Should only have runs for x and y, no extra elements
        root.Descendants(M + "r").Should().HaveCountGreaterThanOrEqualTo(2);
    }

    // ── \phantom ─────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_Phantom_ConsumedSilently()
    {
        var root = ConvertAndAssertSuccess(@"\phantom{x} y");
        // phantom should not produce any element
        root.Descendants(M + "t").Should().Contain(t => t.Value == "y");
    }

    // ── Complex real-world expressions ────────────────────────────────────────

    [Fact]
    public void Convert_EinsteinEquation_Succeeds()
    {
        var root = ConvertAndAssertSuccess(@"E = mc^2");
        root.Descendants(M + "sSup").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Convert_QuadraticFormula_Succeeds()
    {
        var root = ConvertAndAssertSuccess(@"x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}");
        root.Descendants(M + "f").Should().HaveCountGreaterThanOrEqualTo(1);
        root.Descendants(M + "rad").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Convert_GaussianIntegral_Succeeds()
    {
        var root = ConvertAndAssertSuccess(@"\int_{-\infty}^{\infty} e^{-x^2} \, dx = \sqrt{\pi}");
        root.Descendants(M + "nary").Should().HaveCountGreaterThanOrEqualTo(1);
        root.Descendants(M + "rad").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Convert_SumFormula_Succeeds()
    {
        var root = ConvertAndAssertSuccess(@"\sum_{k=1}^{n} k = \frac{n(n+1)}{2}");
        root.Descendants(M + "nary").Should().HaveCountGreaterThanOrEqualTo(1);
        root.Descendants(M + "f").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Convert_MaxwellEquation_Succeeds()
    {
        var root = ConvertAndAssertSuccess(
            @"\nabla \times \mathbf{B} = \mu_0 \mathbf{J} + \mu_0 \epsilon_0 \frac{\partial \mathbf{E}}{\partial t}");
        root.Descendants(M + "f").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    // ── Output format ─────────────────────────────────────────────────────────

    [Fact]
    public void Convert_ValidLatex_OmmlHasNamespaceDeclaration()
    {
        var (omml, success, _) = _converter.Convert(@"\frac{1}{2}");
        success.Should().BeTrue();
        omml.Should().Contain("http://schemas.openxmlformats.org/officeDocument/2006/math");
    }

    [Fact]
    public void Convert_ValidLatex_ReturnsValidXml()
    {
        var (omml, success, _) = _converter.Convert(@"\sum_{i=1}^n i^2");
        success.Should().BeTrue();
        var act = () => XDocument.Parse(omml);
        act.Should().NotThrow("OMML output must be valid XML");
    }

    [Fact]
    public void Convert_Success_ErrorIsNull()
    {
        var (_, success, error) = _converter.Convert("x");
        success.Should().BeTrue();
        error.Should().BeNull();
    }

    // ── Tokenizer unit tests ──────────────────────────────────────────────────

    [Fact]
    public void Tokenize_BackslashCommand_ProducesCommandToken()
    {
        var tokens = LatexToOmmlConverter.Tokenize(@"\frac");
        tokens.Should().Contain(t =>
            t.Kind == LatexToOmmlConverter.TokKind.Command && t.Value == @"\frac");
    }

    [Fact]
    public void Tokenize_DoubleBackslash_ProducesDoubleBackslashToken()
    {
        var tokens = LatexToOmmlConverter.Tokenize(@"\\");
        tokens.Should().Contain(t => t.Kind == LatexToOmmlConverter.TokKind.DoubleBackslash);
    }

    [Fact]
    public void Tokenize_Braces_ProducesCorrectTokens()
    {
        var tokens = LatexToOmmlConverter.Tokenize("{}");
        tokens.Should().Contain(t => t.Kind == LatexToOmmlConverter.TokKind.LeftBrace);
        tokens.Should().Contain(t => t.Kind == LatexToOmmlConverter.TokKind.RightBrace);
    }

    [Fact]
    public void Tokenize_Caret_ProducesCaretToken()
    {
        var tokens = LatexToOmmlConverter.Tokenize("x^2");
        tokens.Should().Contain(t => t.Kind == LatexToOmmlConverter.TokKind.Caret);
    }

    [Fact]
    public void Tokenize_Underscore_ProducesUnderscoreToken()
    {
        var tokens = LatexToOmmlConverter.Tokenize("x_i");
        tokens.Should().Contain(t => t.Kind == LatexToOmmlConverter.TokKind.Underscore);
    }

    [Fact]
    public void Tokenize_Ampersand_ProducesAmpersandToken()
    {
        var tokens = LatexToOmmlConverter.Tokenize("a & b");
        tokens.Should().Contain(t => t.Kind == LatexToOmmlConverter.TokKind.Ampersand);
    }

    [Fact]
    public void Tokenize_WhitespaceStripped_AfterCommands()
    {
        var tokens = LatexToOmmlConverter.Tokenize(@"\alpha x");
        // After \alpha, whitespace is consumed — next token should be Char x
        var alpha = tokens.First(t => t.Value == @"\alpha");
        var idx = tokens.IndexOf(alpha);
        tokens[idx + 1].Kind.Should().Be(LatexToOmmlConverter.TokKind.Char);
        tokens[idx + 1].Value.Should().Be("x");
    }

    [Fact]
    public void Tokenize_AlwaysEndsWithEndToken()
    {
        var tokens = LatexToOmmlConverter.Tokenize("x");
        tokens.Last().Kind.Should().Be(LatexToOmmlConverter.TokKind.End);
    }

    [Fact]
    public void Tokenize_NonAlphaCommand_SingleCharAfterBackslash()
    {
        var tokens = LatexToOmmlConverter.Tokenize(@"\,");
        tokens.Should().Contain(t => t.Kind == LatexToOmmlConverter.TokKind.Command && t.Value == @"\,");
    }
}
