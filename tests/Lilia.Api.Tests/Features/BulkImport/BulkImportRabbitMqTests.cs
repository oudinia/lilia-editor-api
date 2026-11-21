using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Features.BulkImport;
using Lilia.Api.Features.BulkImport.Contracts;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Import.Interfaces;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wolverine.Runtime;

namespace Lilia.Api.Tests.Features.BulkImport;

/// <summary>
/// Bulk import through a real RabbitMQ (optional, 21 Nov 2026). Runs only when <c>LILIA_TEST_RABBITMQ</c> holds an amqp
/// URI with its vhost (the server's test vhost is <c>lilia-test</c>); CI and every other run have no broker and pass it
/// at once. The host listens to the three queues on RabbitMQ (made if missing), and a dropped PDF becomes its document.
/// </summary>
public class BulkImportRabbitMqTests(TestDatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task A_dropped_pdf_goes_through_rabbitmq_queues_to_its_document()
    {
        var uri = Environment.GetEnvironmentVariable("LILIA_TEST_RABBITMQ");
        if (string.IsNullOrWhiteSpace(uri)) return; // no broker on this run

        var mathpix = new FakeMathpixClient();
        using var host = Fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("BulkImport:RabbitMq", uri);
            b.UseSetting("Import:PdfSitePagesPerMonth", "1000000");
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMathpixClient>();
                services.AddSingleton<IMathpixClient>(mathpix);
                services.RemoveAll<BulkImportOptions>();
                services.AddSingleton(new BulkImportOptions { RabbitMq = uri, PollSeconds = 0.01, SweepEverySeconds = 0 });
            });
        });

        var userId = $"bulk_rmq_{Guid.NewGuid():N}"[..30];
        await SeedUserAsync(userId);
        await using (var db = CreateDbContext())
        {
            var pro = await db.Plans.SingleAsync(p => p.Slug == "pro");
            db.UserPlans.Add(new UserPlan { Id = Guid.NewGuid(), UserId = userId, PlanId = pro.Id, Status = "active", StartedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var author = host.CreateClient();
        author.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        author.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");

        var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
        runtime.Endpoints.ActiveListeners().Select(l => l.Uri.ToString())
            .Should().Contain(u => u.StartsWith("rabbitmq://") && u.Contains(BulkImportMessaging.OcrQueue));

        var batchId = (await (await author.PostAsync("/api/bulk-imports", null)).Content.ReadFromJsonAsync<BulkImportCreatedDto>())!.BatchId;
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(BulkImportTestKit.Pdf(12, salt: Guid.NewGuid().ToString()));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "rabbit.pdf");
        (await author.PostAsync($"/api/bulk-imports/{batchId}/files", form)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        BulkImportBatchDto batch;
        var until = DateTime.UtcNow.AddSeconds(120);
        do
        {
            await Task.Delay(500);
            batch = (await author.GetFromJsonAsync<BulkImportBatchDto>($"/api/bulk-imports/{batchId}"))!;
        } while (batch.Status != "done" && DateTime.UtcNow < until);

        batch.Status.Should().Be("done");
        batch.PagesDone.Should().Be(12);
        mathpix.Submits.Should().Be(2);
    }
}
