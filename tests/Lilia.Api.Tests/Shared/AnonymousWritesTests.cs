using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FluentAssertions;
using Lilia.Api.Features.Platform.Controllers;
using Lilia.Api.Features.Platform.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lilia.Api.Tests.Shared;

/// <summary>
/// Every endpoint that changes something without sign-in is listed here, with what guards it (3 Nov 2026).
/// A temporary debug controller, open to anyone in production, let any caller create an import session in
/// any account, list the latest LaTeX imports of all users, delete sessions and read stack traces; nothing
/// listed it. A new anonymous write now fails this test until someone writes down why it is safe.
/// </summary>
public class AnonymousWritesTests : IntegrationTestBase
{
    /// <summary>Controller.Action → what makes it safe without sign-in.</summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        ["AuthEmailController.SendPasswordReset"] = "before sign-in by nature; the email goes to the account's own address",
        ["AuthEmailController.SendMagicLink"] = "before sign-in by nature; the email goes to the account's own address",
        ["AuthEmailController.ResendVerification"] = "before sign-in by nature; the email goes to the account's own address",
        ["StytchWebhookController.Email"] = "Stytch's signed webhook (svix signature)",
        ["BillingController.Webhook"] = "the payment provider's signed webhook",
        ["TipsController.OneClick"] = "the random token from the tips email",
        ["TeamsController.TestWelcomeEmail"] = "the X-Lilia-Test-Key shared secret; refused when none is configured",
        ["DevToolsController.TriggerStytchVerification"] = "404 outside Development",
        ["E2EController.StartRun"] = "404 in Production (the reporter posts to the test copy)",
        ["E2EController.RecordResult"] = "404 in Production",
        ["E2EController.FinalizeRun"] = "404 in Production",
        ["E2EController.CoverageEvents"] = "404 in Production",
        ["FeedbackController.CreateFeedback"] = "feedback from the public site",
        ["WaitlistController.Signup"] = "the public waitlist form",
        ["LogController.IngestBatch"] = "client error logs, before sign-in too",
        ["LoremController.Generate"] = "computes text; stores nothing",
        ["LmlController.ToLatex"] = "a conversion; stores nothing",
        ["LmlController.ToHtml"] = "a conversion; stores nothing",
        ["LmlController.ToMarkdown"] = "a conversion; stores nothing",
        ["LmlController.ConvertAll"] = "a conversion; stores nothing",
        ["ConvertController.ConvertDocxToLatex"] = "the public conversion API, rate limited by IP",
        ["ConvertController.ConvertDocxToHtml"] = "the public conversion API, rate limited by IP",
        ["ConvertController.ConvertDocxToMarkdown"] = "the public conversion API, rate limited by IP",
        ["ConvertController.ConvertDocxToPdf"] = "the public conversion API, rate limited by IP",
        ["ConvertController.ConvertLatexToDocx"] = "the public conversion API, rate limited by IP",
        ["ConvertController.ConvertMarkdownToLatex"] = "the public conversion API, rate limited by IP",
    };

    public AnonymousWritesTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public void Every_write_without_sign_in_is_listed_with_its_guard()
    {
        var api = typeof(Program).Assembly;
        var assemblies = api.GetReferencedAssemblies()
            .Where(a => a.Name!.StartsWith("Lilia.Features.", StringComparison.Ordinal))
            .Select(Assembly.Load)
            .Append(api);
        var controllers = assemblies.SelectMany(a => a.GetTypes())
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();
        controllers.Should().Contain(typeof(E2EController), "the controllers are read as expected");

        var writes = new[] { "POST", "PUT", "PATCH", "DELETE" };
        var found = (
            from c in controllers
            from m in c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            where m.GetCustomAttributes<HttpMethodAttribute>().Any(h => h.HttpMethods.Any(writes.Contains))
            // ASP.NET Core: [AllowAnonymous] anywhere wins over [Authorize]; and with no fallback policy
            // (Program.cs sets none) an action with no [Authorize] on it or its class is open.
            let anonymous = m.IsDefined(typeof(AllowAnonymousAttribute)) || c.IsDefined(typeof(AllowAnonymousAttribute), true)
                || (!m.IsDefined(typeof(AuthorizeAttribute)) && !c.IsDefined(typeof(AuthorizeAttribute), true))
            where anonymous
            select $"{c.Name}.{m.Name}").ToHashSet();

        found.Except(Allowed.Keys).Should().BeEmpty("a write without sign-in needs its guard written down here");
        Allowed.Keys.Except(found).Should().BeEmpty("a listed write that is gone or now needs sign-in leaves the list");
    }

    [Fact]
    public async Task The_debug_import_surface_is_gone()
    {
        var anonymous = CreateAnonymousClient();
        foreach (var path in new[] { "run-import", "self-test", "cleanup", "ui-preview" })
            (await anonymous.PostAsJsonAsync($"/api/public/latex-coverage/diag/{path}", new { rawSource = "\\section{x}", ownerId = "someone" }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, path);
        (await anonymous.GetAsync("/api/public/latex-coverage/diag/state")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public async Task E2E_records_are_refused_in_production_only(string environment, bool refused)
    {
        var svc = new Mock<IE2EService>();
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environment);
        var controller = new E2EController(svc.Object, NullLogger<E2EController>.Instance, env.Object);

        var results = new IActionResult[]
        {
            (await controller.StartRun(null!, default)).Result!,
            await controller.RecordResult(null!, default),
            await controller.FinalizeRun(Guid.NewGuid(), null!, default),
            await controller.CoverageEvents(new() { Events = [] }, default),
        };

        if (refused)
        {
            results.Should().AllBeOfType<NotFoundResult>();
            svc.Invocations.Should().BeEmpty();
        }
        else
        {
            results.Should().NotContain(r => r is NotFoundResult);
        }
    }
}
