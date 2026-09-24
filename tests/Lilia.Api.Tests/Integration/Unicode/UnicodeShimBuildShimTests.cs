using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Integration.Unicode;

/// <summary>
/// Characterisation of <c>BuildShim</c>, the function the validator relies on to
/// make literal Unicode compile. Written to pass against the code as it was
/// before <c>Classify</c> was extracted from it, and run there first — so the
/// extraction is proven behaviour-preserving rather than assumed to be.
///
/// <para>Nothing tested this service before. It decides whether a document with
/// a Greek letter in it compiles.</para>
///
/// <para>On real Postgres, through the service's real loader. The in-memory
/// provider cannot map this context.</para>
/// </summary>
[Collection("Integration")]
public class UnicodeShimBuildShimTests : IntegrationTestBase
{
    public UnicodeShimBuildShimTests(TestDatabaseFixture fixture) : base(fixture) { }

    /// <summary>γ and ×, as the production catalog maps them. The database is
    /// shared across the collection, so rows are added only when absent.</summary>
    private async Task<UnicodeShimService> Standard()
    {
        await using (var db = CreateDbContext())
        {
            foreach (var (cp, repl) in new[] { (0x03B3, @"\ensuremath{\gamma}"), (0x00D7, @"\ensuremath{\times}") })
            {
                if (await db.LatexUnicodeChars.AnyAsync(u => u.Codepoint == cp)) continue;
                db.LatexUnicodeChars.Add(new LatexUnicodeChar
                {
                    Id = Guid.NewGuid(), Codepoint = cp, Character = char.ConvertFromUtf32(cp), Replacement = repl,
                });
            }
            await db.SaveChangesAsync();
        }

        var service = new UnicodeShimService(
            Fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<UnicodeShimService>.Instance);
        await service.PreloadAsync();
        return service;
    }

    [Fact]
    public async Task A_mapped_character_is_shimmed()
    {
        var shim = (await Standard()).BuildShim("The rate γ is small.");

        shim.Shim.Should().Contain(@"\newunicodechar{γ}{\ensuremath{\gamma}}");
        shim.Shim.Should().StartWith("\\usepackage{textcomp}\n\\usepackage{newunicodechar}\n");
        shim.UnmappedCodepoints.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unmapped_character_is_reported_and_not_shimmed()
    {
        // ℵ: measured failing to compile on 24 Sep — "Unicode character ℵ
        // (U+2135) not set up for use with LaTeX".
        var shim = (await Standard()).BuildShim("The cardinal ℵ.");

        shim.UnmappedCodepoints.Should().Equal(0x2135);
        shim.Shim.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Café")]      // U+00E9
    [InlineData("Straße")]    // U+00DF
    [InlineData("Łódź")]      // U+0141 — Latin Extended-A
    [InlineData("½")]         // U+00BD
    public async Task Accented_latin_is_never_reported_as_unmapped(string text)
    {
        // Measured compiling on 24 Sep. Whether a character here is also in the
        // map depends on what else seeded the catalog, so the assertion is the
        // property that holds either way: it is never called a failure. A paste
        // check that flagged these would false-alarm on most European names.
        var shim = (await Standard()).BuildShim(text);

        shim.UnmappedCodepoints.Should().BeEmpty();
    }

    [Fact]
    public async Task The_catalog_takes_precedence_over_inputenc()
    {
        // Found by the test above failing on the original code: another test had
        // seeded ½ → \textonehalf{}, and the mapped branch is checked before the
        // inputenc range. So a Latin-1 character in the catalog is shimmed, not
        // left alone. Pinned here so the order cannot flip unnoticed.
        await using (var db = CreateDbContext())
        {
            if (!await db.LatexUnicodeChars.AnyAsync(u => u.Codepoint == 0x00BD))
            {
                db.LatexUnicodeChars.Add(new LatexUnicodeChar
                {
                    Id = Guid.NewGuid(), Codepoint = 0x00BD, Character = "½", Replacement = @"\textonehalf{}",
                });
                await db.SaveChangesAsync();
            }
        }

        var shim = (await Standard()).BuildShim("Half is ½.");

        shim.Shim.Should().Contain("{½}");
        shim.UnmappedCodepoints.Should().BeEmpty();
    }

    [Fact]
    public async Task Plain_ascii_needs_nothing()
    {
        var shim = (await Standard()).BuildShim("Nothing unusual here.");

        shim.HasShim.Should().BeFalse();
        shim.HasUnmapped.Should().BeFalse();
    }

    [Fact]
    public async Task Mapped_and_unmapped_together_are_both_reported()
    {
        var shim = (await Standard()).BuildShim("γ × ℵ");

        shim.Shim.Should().Contain("{γ}").And.Contain("{×}");
        shim.UnmappedCodepoints.Should().Equal(0x2135);
    }

    [Fact]
    public async Task Output_order_is_by_codepoint_so_the_source_is_stable()
    {
        // × is U+00D7, γ is U+03B3: × comes first regardless of text order.
        var shim = (await Standard()).BuildShim("γ then ×");

        shim.Shim.IndexOf("{×}", StringComparison.Ordinal)
            .Should().BeLessThan(shim.Shim.IndexOf("{γ}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_surrogate_pair_is_one_character_not_two()
    {
        var shim = (await Standard()).BuildShim("Nice 🙂");

        shim.UnmappedCodepoints.Should().Equal(0x1F642);
    }
}

/// <summary>
/// <c>Classify</c> is what <c>BuildShim</c> is now built on, and what the paste
/// check calls. The point of extracting it rather than writing a second
/// classifier is that the two cannot disagree — this is the test that says so.
/// </summary>
[Collection("Integration")]
public class UnicodeClassifyParityTests : IntegrationTestBase
{
    public UnicodeClassifyParityTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData("γ × — ≈ →")]
    [InlineData("ℵ 中 🙂")]
    [InlineData("Café, Straße, Łódź, ½")]
    [InlineData("γ and ℵ and é")]
    [InlineData("plain ascii")]
    public async Task The_paste_check_and_the_validator_agree(string text)
    {
        var service = new UnicodeShimService(
            Fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<UnicodeShimService>.Instance);
        await service.PreloadAsync();

        var classified = service.Classify(text);
        var shim = service.BuildShim(text);

        shim.UnmappedCodepoints.Should().Equal(classified.Unmapped);
        foreach (var (cp, repl) in classified.Shimmed)
            shim.Shim.Should().Contain($"{{{char.ConvertFromUtf32(cp)}}}{{{repl}}}");
    }
}
