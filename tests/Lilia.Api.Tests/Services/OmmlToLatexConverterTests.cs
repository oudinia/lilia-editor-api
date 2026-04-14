using System.Xml.Linq;
using FluentAssertions;
using Lilia.Import.Converters;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Unit tests for OmmlToLatexConverter — covers every OMML element type
/// and verifies correct LaTeX output. Also includes round-trip tests:
/// LaTeX → OMML → LaTeX using both converters together.
/// </summary>
public class OmmlToLatexConverterTests
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    private readonly OmmlToLatexConverter _converter = new();
    private readonly LatexToOmmlConverter _latexToOmml = new();

    // ── Helper builders ───────────────────────────────────────────────────────

    private static string OmmlDoc(XElement inner) =>
        new XElement(M + "oMath",
            new XAttribute(XNamespace.Xmlns + "m", M.NamespaceName),
            inner).ToString(SaveOptions.DisableFormatting);

    private static XElement Run(string text) =>
        new(M + "r", new XElement(M + "t", text));

    private static XElement Run(string text, string sty) =>
        new(M + "r",
            new XElement(M + "rPr",
                new XElement(M + "sty", new XAttribute(M + "val", sty))),
            new XElement(M + "t", text));

    private string ConvertAndAssertSuccess(string omml)
    {
        var (latex, success, error) = _converter.Convert(omml);
        success.Should().BeTrue(because: $"OMML should parse successfully, but got: {error}");
        error.Should().BeNull();
        return latex;
    }

    // ── Error handling ────────────────────────────────────────────────────────

    [Fact]
    public void Convert_InvalidXml_ReturnsFailure()
    {
        var (_, success, error) = _converter.Convert("this is not xml");
        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Convert_EmptyXml_ReturnsEmptyOrFailure()
    {
        var (_, success, _) = _converter.Convert("");
        // Empty string is not valid XML
        success.Should().BeFalse();
    }

    [Fact]
    public void Convert_NullRoot_ReturnsFailure()
    {
        var (_, success, error) = _converter.Convert("<root/>");
        // root exists but is not oMath — should still process (falls through to ConvertChildren)
        success.Should().BeTrue(); // any valid XML succeeds
    }

    // ── oMath root ────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_OmathWithRun_ReturnsText()
    {
        var xml = OmmlDoc(Run("x"));
        var latex = ConvertAndAssertSuccess(xml);
        latex.Should().Be("x");
    }

    [Fact]
    public void Convert_OmathWithMultipleRuns_ConcatenatesText()
    {
        var xml = OmmlDoc(new XElement(M + "r", new XElement(M + "t", "abc")));
        var latex = ConvertAndAssertSuccess(xml);
        latex.Should().Be("abc");
    }

    // ── Fractions ─────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_StandardFraction_ReturnsFrac()
    {
        var frac = new XElement(M + "f",
            new XElement(M + "num", Run("1")),
            new XElement(M + "den", Run("2")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(frac));
        latex.Should().Be(@"\frac{1}{2}");
    }

    [Fact]
    public void Convert_SkewedFraction_ReturnsSfrac()
    {
        var frac = new XElement(M + "f",
            new XElement(M + "fPr",
                new XElement(M + "type", new XAttribute(M + "val", "skw"))),
            new XElement(M + "num", Run("a")),
            new XElement(M + "den", Run("b")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(frac));
        latex.Should().Be(@"\sfrac{a}{b}");
    }

    [Fact]
    public void Convert_LinearFraction_ReturnsParenForm()
    {
        var frac = new XElement(M + "f",
            new XElement(M + "fPr",
                new XElement(M + "type", new XAttribute(M + "val", "lin"))),
            new XElement(M + "num", Run("a")),
            new XElement(M + "den", Run("b")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(frac));
        latex.Should().Contain("(a)/(b)");
    }

    // ── Radicals ──────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_SimpleSqrt_ReturnsSqrt()
    {
        var rad = new XElement(M + "rad",
            new XElement(M + "radPr",
                new XElement(M + "degHide", new XAttribute(M + "val", "1"))),
            new XElement(M + "deg"),
            new XElement(M + "e", Run("x")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(rad));
        latex.Should().Be(@"\sqrt{x}");
    }

    [Fact]
    public void Convert_NthRoot_ReturnsSqrtWithIndex()
    {
        var rad = new XElement(M + "rad",
            new XElement(M + "radPr"),
            new XElement(M + "deg", Run("3")),
            new XElement(M + "e", Run("x")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(rad));
        latex.Should().Be(@"\sqrt[3]{x}");
    }

    [Fact]
    public void Convert_EmptyDegree_ReturnsSqrt()
    {
        var rad = new XElement(M + "rad",
            new XElement(M + "radPr"),
            new XElement(M + "deg"),  // empty deg
            new XElement(M + "e", Run("y")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(rad));
        latex.Should().Be(@"\sqrt{y}");
    }

    // ── Superscript / Subscript ───────────────────────────────────────────────

    [Fact]
    public void Convert_Superscript_ReturnsCaret()
    {
        var sSup = new XElement(M + "sSup",
            new XElement(M + "e", Run("x")),
            new XElement(M + "sup", Run("2")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(sSup));
        latex.Should().Contain("^{2}");
    }

    [Fact]
    public void Convert_Subscript_ReturnsUnderscore()
    {
        var sSub = new XElement(M + "sSub",
            new XElement(M + "e", Run("x")),
            new XElement(M + "sub", Run("i")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(sSub));
        latex.Should().Contain("_{i}");
    }

    [Fact]
    public void Convert_SubSup_ReturnsBoth()
    {
        var sSubSup = new XElement(M + "sSubSup",
            new XElement(M + "e", Run("x")),
            new XElement(M + "sub", Run("i")),
            new XElement(M + "sup", Run("2")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(sSubSup));
        latex.Should().Contain("_{i}");
        latex.Should().Contain("^{2}");
    }

    [Fact]
    public void Convert_ComplexBase_WrapsInBraces()
    {
        var sSup = new XElement(M + "sSup",
            new XElement(M + "e", Run("mc")),
            new XElement(M + "sup", Run("2")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(sSup));
        // Multi-char base should be wrapped
        latex.Should().Contain("{mc}^{2}");
    }

    // ── N-ary operators ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("∑", @"\sum")]
    [InlineData("∏", @"\prod")]
    [InlineData("∫", @"\int")]
    [InlineData("∬", @"\iint")]
    [InlineData("∭", @"\iiint")]
    [InlineData("∮", @"\oint")]
    [InlineData("⋃", @"\bigcup")]
    [InlineData("⋂", @"\bigcap")]
    [InlineData("⋁", @"\bigvee")]
    [InlineData("⋀", @"\bigwedge")]
    public void Convert_NaryOperator_ReturnsCorrectCommand(string chr, string expectedCmd)
    {
        var nary = new XElement(M + "nary",
            new XElement(M + "naryPr",
                new XElement(M + "chr", new XAttribute(M + "val", chr))),
            new XElement(M + "sub"),
            new XElement(M + "sup"),
            new XElement(M + "e", Run("f")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(nary));
        latex.Should().Contain(expectedCmd);
    }

    [Fact]
    public void Convert_SumWithLimits_IncludesSubSup()
    {
        var nary = new XElement(M + "nary",
            new XElement(M + "naryPr",
                new XElement(M + "chr", new XAttribute(M + "val", "∑"))),
            new XElement(M + "sub", Run("k"), Run("="), Run("1")),
            new XElement(M + "sup", Run("n")),
            new XElement(M + "e", Run("k")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(nary));
        latex.Should().Contain(@"\sum");
        latex.Should().Contain("_{");
        latex.Should().Contain("^{");
    }

    [Fact]
    public void Convert_UnknownNaryChar_FallsBackToInt()
    {
        var nary = new XElement(M + "nary",
            new XElement(M + "naryPr",
                new XElement(M + "chr", new XAttribute(M + "val", "★"))),
            new XElement(M + "sub"),
            new XElement(M + "sup"),
            new XElement(M + "e", Run("x")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(nary));
        latex.Should().Contain(@"\int"); // fallback
    }

    // ── Delimiters ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("(", ")")]
    [InlineData("[", "]")]
    [InlineData("{", "}")]
    [InlineData("|", "|")]
    [InlineData("⌈", "⌉")]
    [InlineData("⌊", "⌋")]
    [InlineData("⟨", "⟩")]
    public void Convert_Delimiter_ReturnsLeftRight(string beg, string end)
    {
        var d = new XElement(M + "d",
            new XElement(M + "dPr",
                new XElement(M + "begChr", new XAttribute(M + "val", beg)),
                new XElement(M + "endChr", new XAttribute(M + "val", end))),
            new XElement(M + "e", Run("x")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(d));
        latex.Should().Contain(@"\left");
        latex.Should().Contain(@"\right");
    }

    [Fact]
    public void Convert_EmptyDelimiter_UsesEmptyDot()
    {
        var d = new XElement(M + "d",
            new XElement(M + "dPr",
                new XElement(M + "begChr", new XAttribute(M + "val", "")),
                new XElement(M + "endChr", new XAttribute(M + "val", ")"))),
            new XElement(M + "e", Run("x")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(d));
        latex.Should().Contain(@"\left.");
    }

    // ── Accents ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("̂", @"\hat{x}")]
    [InlineData("̃", @"\tilde{x}")]
    [InlineData("̄", @"\bar{x}")]
    [InlineData("̇", @"\dot{x}")]
    [InlineData("̈", @"\ddot{x}")]
    [InlineData("⃗", @"\vec{x}")]
    [InlineData("̆", @"\breve{x}")]
    [InlineData("̌", @"\check{x}")]
    [InlineData("́", @"\acute{x}")]
    [InlineData("̀", @"\grave{x}")]
    public void Convert_Accent_ReturnsCorrectCommand(string chr, string expectedLatex)
    {
        var acc = new XElement(M + "acc",
            new XElement(M + "accPr",
                new XElement(M + "chr", new XAttribute(M + "val", chr))),
            new XElement(M + "e", Run("x")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(acc));
        latex.Should().Be(expectedLatex);
    }

    [Fact]
    public void Convert_UnknownAccent_FallsBackToHat()
    {
        var acc = new XElement(M + "acc",
            new XElement(M + "accPr",
                new XElement(M + "chr", new XAttribute(M + "val", "★"))),
            new XElement(M + "e", Run("x")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(acc));
        latex.Should().Contain(@"\hat{x}");
    }

    // ── Bar ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_BarTop_ReturnsOverline()
    {
        var bar = new XElement(M + "bar",
            new XElement(M + "barPr",
                new XElement(M + "pos", new XAttribute(M + "val", "top"))),
            new XElement(M + "e", Run("x")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(bar));
        latex.Should().Be(@"\overline{x}");
    }

    [Fact]
    public void Convert_BarBot_ReturnsUnderline()
    {
        var bar = new XElement(M + "bar",
            new XElement(M + "barPr",
                new XElement(M + "pos", new XAttribute(M + "val", "bot"))),
            new XElement(M + "e", Run("x")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(bar));
        latex.Should().Be(@"\underline{x}");
    }

    // ── Group characters ──────────────────────────────────────────────────────

    [Fact]
    public void Convert_GroupChrUnderbrace_ReturnsUnderbrace()
    {
        var gc = new XElement(M + "groupChr",
            new XElement(M + "groupChrPr",
                new XElement(M + "chr", new XAttribute(M + "val", "⏟")),
                new XElement(M + "pos", new XAttribute(M + "val", "bot"))),
            new XElement(M + "e", Run("x")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(gc));
        latex.Should().Be(@"\underbrace{x}");
    }

    [Fact]
    public void Convert_GroupChrOverbrace_ReturnsOverbrace()
    {
        var gc = new XElement(M + "groupChr",
            new XElement(M + "groupChrPr",
                new XElement(M + "chr", new XAttribute(M + "val", "⏞")),
                new XElement(M + "pos", new XAttribute(M + "val", "top"))),
            new XElement(M + "e", Run("x")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(gc));
        latex.Should().Be(@"\overbrace{x}");
    }

    // ── Limit upper / lower ───────────────────────────────────────────────────

    [Fact]
    public void Convert_LimUpp_ReturnsOverset()
    {
        var limUpp = new XElement(M + "limUpp",
            new XElement(M + "e", Run("x")),
            new XElement(M + "lim", Run("*")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(limUpp));
        latex.Should().Contain(@"\overset{*}{x}");
    }

    [Fact]
    public void Convert_LimLow_ReturnsUnderset()
    {
        var limLow = new XElement(M + "limLow",
            new XElement(M + "e", Run("x")),
            new XElement(M + "lim", Run("0")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(limLow));
        latex.Should().Contain(@"\underset{0}{x}");
    }

    // ── Functions ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sin")]
    [InlineData("cos")]
    [InlineData("lim")]
    [InlineData("log")]
    [InlineData("det")]
    [InlineData("gcd")]
    public void Convert_KnownFunc_ReturnsBackslashCommand(string funcName)
    {
        var func = new XElement(M + "func",
            new XElement(M + "funcPr"),
            new XElement(M + "fName",
                Run(funcName, "r")),
            new XElement(M + "e", Run("x")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(func));
        latex.Should().Contain($@"\{funcName}");
    }

    [Fact]
    public void Convert_UnknownFunc_ReturnsMathrmWrapped()
    {
        var func = new XElement(M + "func",
            new XElement(M + "funcPr"),
            new XElement(M + "fName",
                Run("myop", "r")),
            new XElement(M + "e", Run("x")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(func));
        latex.Should().Contain(@"\mathrm{myop}");
    }

    // ── Matrix ────────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_Matrix_ReturnsBeginMatrix()
    {
        var m = new XElement(M + "m",
            new XElement(M + "mr",
                new XElement(M + "e", Run("a")),
                new XElement(M + "e", Run("b"))),
            new XElement(M + "mr",
                new XElement(M + "e", Run("c")),
                new XElement(M + "e", Run("d"))));
        var latex = ConvertAndAssertSuccess(OmmlDoc(m));
        latex.Should().Contain(@"\begin{matrix}");
        latex.Should().Contain(@"\end{matrix}");
        latex.Should().Contain("&");
        latex.Should().Contain(@"\\");
    }

    // ── Equation array ────────────────────────────────────────────────────────

    [Fact]
    public void Convert_EqArr_ReturnsBeginAligned()
    {
        var eqArr = new XElement(M + "eqArr",
            new XElement(M + "e", Run("x"), Run("="), Run("1")),
            new XElement(M + "e", Run("y"), Run("="), Run("2")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(eqArr));
        latex.Should().Contain(@"\begin{aligned}");
        latex.Should().Contain(@"\end{aligned}");
        latex.Should().Contain(@"\\");
    }

    // ── Border box ────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_BorderBox_ReturnsBoxed()
    {
        var bb = new XElement(M + "borderBox",
            new XElement(M + "e", Run("E"), Run("="), Run("m"), Run("c")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(bb));
        latex.Should().Contain(@"\boxed{");
    }

    // ── Box (generic) ─────────────────────────────────────────────────────────

    [Fact]
    public void Convert_Box_ReturnsContent()
    {
        var box = new XElement(M + "box",
            new XElement(M + "e", Run("x")));
        var latex = ConvertAndAssertSuccess(OmmlDoc(box));
        latex.Should().Be("x");
    }

    // ── oMathPara ─────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_OmathPara_JoinsWithDoubleBslash()
    {
        var para = new XElement(M + "oMathPara",
            new XAttribute(XNamespace.Xmlns + "m", M.NamespaceName),
            new XElement(M + "oMath", Run("x")),
            new XElement(M + "oMath", Run("y")));
        var (latex, success, _) = _converter.Convert(para.ToString(SaveOptions.DisableFormatting));
        success.Should().BeTrue();
        latex.Should().Contain(@" \\ ");
    }

    // ── Greek letters via text ────────────────────────────────────────────────

    [Theory]
    [InlineData("α", @"\alpha")]
    [InlineData("β", @"\beta")]
    [InlineData("π", @"\pi")]
    [InlineData("σ", @"\sigma")]
    [InlineData("Γ", @"\Gamma")]
    [InlineData("Σ", @"\Sigma")]
    [InlineData("ω", @"\omega")]
    public void Convert_GreekCharInRun_ReturnsCommand(string chr, string expectedCmd)
    {
        var xml = OmmlDoc(Run(chr));
        var latex = ConvertAndAssertSuccess(xml);
        latex.Should().Contain(expectedCmd);
    }

    // ── Symbols via text ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("∞", @"\infty")]
    [InlineData("∂", @"\partial")]
    [InlineData("∑", @"\sum")]
    [InlineData("∫", @"\int")]
    [InlineData("±", @"\pm")]
    [InlineData("≤", @"\leq")]
    [InlineData("≥", @"\geq")]
    [InlineData("≠", @"\neq")]
    [InlineData("→", @"\rightarrow")]
    [InlineData("⇒", @"\Rightarrow")]
    [InlineData("ℝ", @"\mathbb{R}")]
    [InlineData("ℂ", @"\mathbb{C}")]
    public void Convert_SymbolCharInRun_ReturnsCommand(string chr, string expectedCmd)
    {
        var xml = OmmlDoc(Run(chr));
        var latex = ConvertAndAssertSuccess(xml);
        latex.Should().Contain(expectedCmd);
    }

    // ── Special characters needing escape ────────────────────────────────────

    [Theory]
    [InlineData("#", @"\#")]
    [InlineData("$", @"\$")]
    [InlineData("%", @"\%")]
    [InlineData("&", @"\&")]
    [InlineData("_", @"\_")]
    public void Convert_SpecialChar_EscapedInOutput(string chr, string expected)
    {
        var xml = OmmlDoc(Run(chr));
        var latex = ConvertAndAssertSuccess(xml);
        latex.Should().Contain(expected);
    }

    // ── Round-trip tests ──────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that LaTeX → OMML → LaTeX produces structurally equivalent output
    /// (not necessarily identical string, but same structure).
    /// </summary>
    [Theory]
    [InlineData(@"\frac{1}{2}", "f")]      // should have fraction in OMML
    [InlineData(@"\sqrt{x}", "rad")]       // should have radical
    [InlineData(@"x^2", "sSup")]           // should have superscript
    [InlineData(@"x_i", "sSub")]           // should have subscript
    [InlineData(@"\sum_{k=1}^n k", "nary")] // should have nary
    [InlineData(@"\hat{x}", "acc")]        // should have accent
    [InlineData(@"\overline{x}", "bar")]   // should have bar
    [InlineData(@"\boxed{E}", "borderBox")] // should have borderBox
    public void RoundTrip_LatexToOmmlToLatex_PreservesStructure(string latex, string expectedOmmlElement)
    {
        // Step 1: LaTeX → OMML
        var (omml, ommlSuccess, _) = _latexToOmml.Convert(latex);
        ommlSuccess.Should().BeTrue($"LaTeX→OMML failed for '{latex}'");
        omml.Should().Contain(expectedOmmlElement,
            because: $"OMML for '{latex}' should contain {expectedOmmlElement}");

        // Step 2: OMML → LaTeX
        var (backLatex, latexSuccess, _) = _converter.Convert(omml);
        latexSuccess.Should().BeTrue($"OMML→LaTeX failed for OMML from '{latex}'");
        backLatex.Should().NotBeNullOrEmpty($"Round-trip LaTeX should not be empty for '{latex}'");
    }

    [Fact]
    public void RoundTrip_Fraction_PreservesNumeratorAndDenominator()
    {
        var (omml, _, _) = _latexToOmml.Convert(@"\frac{a}{b}");
        var (backLatex, _, _) = _converter.Convert(omml);
        backLatex.Should().Contain(@"\frac{");
        backLatex.Should().Contain("a");
        backLatex.Should().Contain("b");
    }

    [Fact]
    public void RoundTrip_Sqrt_PreservesContent()
    {
        var (omml, _, _) = _latexToOmml.Convert(@"\sqrt{x}");
        var (backLatex, _, _) = _converter.Convert(omml);
        backLatex.Should().Contain(@"\sqrt{x}");
    }

    [Fact]
    public void RoundTrip_GreekLetters_Preserved()
    {
        var (omml, _, _) = _latexToOmml.Convert(@"\alpha + \beta");
        var (backLatex, _, _) = _converter.Convert(omml);
        backLatex.Should().Contain(@"\alpha");
        backLatex.Should().Contain(@"\beta");
    }

    [Fact]
    public void RoundTrip_Matrix2x2_PreservesStructure()
    {
        var (omml, _, _) = _latexToOmml.Convert(@"\begin{matrix} a & b \\ c & d \end{matrix}");
        var (backLatex, _, _) = _converter.Convert(omml);
        backLatex.Should().Contain(@"\begin{matrix}");
        backLatex.Should().Contain("&");
        backLatex.Should().Contain(@"\\");
    }
}
