using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>
/// A credit buys the same whatever the model (3 Nov 2026): each model's credit rate in the catalog is its
/// real price at <see cref="AiArchitectPricing.UsdPerCredit"/>. The rates had been copied from the models
/// Claude 5 replaced, so Sonnet 5 cost 1.8x its price in credits and Opus 5 3.6x.
/// </summary>
public class CreditRatesTests : IntegrationTestBase
{
    public CreditRatesTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Every_models_credit_rate_is_its_price_at_the_credit_peg()
    {
        await using var db = CreateDbContext();
        var models = await db.AiModels.AsNoTracking().ToListAsync();
        models.Should().Contain(m => m.Id == "claude-sonnet-5", "the catalog is seeded by the migrations");

        foreach (var m in models)
        {
            var (input, output) = AiArchitectPricing.PricePerMillion(m.Id);
            // $ per million tokens → credits per 1,000 tokens.
            m.CreditInPerKTok.Should().BeApproximately(input / 1000m / AiArchitectPricing.UsdPerCredit, 0.0001m, $"{m.Id}'s input rate");
            m.CreditOutPerKTok.Should().BeApproximately(output / 1000m / AiArchitectPricing.UsdPerCredit, 0.0001m, $"{m.Id}'s output rate");
        }
    }

    [Fact]
    public async Task A_Sonnet_5_question_costs_its_price_in_credits()
    {
        // A catalog loaded now, from the migrated test database, as production loads it at start-up.
        var catalog = new AiCatalogService(Fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new AiOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiCatalogService>.Instance);
        await catalog.PreloadAsync();

        // A real question of 2 Nov: 50,698 tokens in, 1,696 out. It was charged 36 credits.
        var credits = catalog.CreditsFor("claude-sonnet-5", 50698, 1696);
        var dollars = AiArchitectPricing.ComputeCostUsd("claude-sonnet-5", 50698, 1696);

        credits.Should().Be(20);
        (credits * AiArchitectPricing.UsdPerCredit).Should().BeApproximately(dollars, AiArchitectPricing.UsdPerCredit,
            "a credit is worth its peg: the question's credits pay for what it cost, rounded up to one credit");
    }
}
