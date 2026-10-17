using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Integration.LatexCatalog;

/// <summary>
/// A database created today — as this fixture's is — holds the kernel tokens as the migrations meant
/// them, not as the parser's 'unsupported' placeholders. Earlier migrations upgraded those rows only
/// if they already existed, so on a fresh database \documentclass, \usepackage, \frac and the Greek
/// letters came out unsupported and a plain .tex read as a third covered (e2e import report spec,
/// 15 Oct 2026). BackfillCatalogRowsMissingOnFreshDatabases writes them.
/// </summary>
[Collection("Integration")]
public class CatalogOnFreshDatabaseTests : IntegrationTestBase
{
    public CatalogOnFreshDatabaseTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<(string Coverage, string? Handler, string? Template)> Kernel(string name, string kind = "command")
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
        var rows = await db.LatexTokens
            .Where(t => t.Name == name && t.Kind == kind && t.PackageSlug == null)
            .Select(t => new { t.CoverageLevel, t.HandlerKind, t.InsertTemplate })
            .ToListAsync();
        rows.Should().ContainSingle($"one kernel row for {kind} '{name}'");
        return (rows[0].CoverageLevel, rows[0].HandlerKind, rows[0].InsertTemplate);
    }

    [Theory]
    [InlineData("documentclass")]
    [InlineData("usepackage")]
    [InlineData("linewidth")]
    public async Task Preamble_tokens_are_supported(string name)
    {
        var (coverage, handler, _) = await Kernel(name);
        coverage.Should().Be("full");
        handler.Should().Be("parser-regex");
    }

    [Fact]
    public async Task Documentclass_keeps_its_insert_template()
    {
        (await Kernel("documentclass")).Template.Should().Be("\\documentclass{|CURSOR|}");
    }

    [Theory]
    [InlineData("frac")]
    [InlineData("alpha")]
    [InlineData("sum")]
    [InlineData("Rightarrow")]
    public async Task Math_commands_render_through_katex(string name)
    {
        var (coverage, handler, _) = await Kernel(name);
        coverage.Should().Be("full");
        handler.Should().Be("math-katex");
    }

    [Fact]
    public async Task No_kernel_token_the_migrations_upgraded_is_left_unsupported()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
        var names = new[] { "documentclass", "usepackage", "frac", "sqrt", "mathbb", "newcommand", "centering", "toprule" };
        var unsupported = await db.LatexTokens
            .Where(t => t.PackageSlug == null && t.Kind == "command" && names.Contains(t.Name) && t.CoverageLevel == "unsupported")
            .Select(t => t.Name)
            .ToListAsync();
        unsupported.Should().BeEmpty();
    }
}
