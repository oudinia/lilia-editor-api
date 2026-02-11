using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;

namespace Lilia.Api.Tests.Integration.Controllers;

[Collection("Integration")]
public class LabelsControllerTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";
    private const string OtherUserId = "test_user_002";

    public LabelsControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    // --- GET labels ---

    [Fact]
    public async Task GetLabels_ReturnsUserLabels()
    {
        await SeedUserAsync(UserId);
        await SeedLabelAsync(UserId, "Important", "#FF0000");
        await SeedLabelAsync(UserId, "Draft", "#00FF00");

        var response = await Client.GetAsync("/api/labels");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var labels = await response.Content.ReadFromJsonAsync<List<LabelDto>>();
        labels.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetLabels_DoesNotReturnOtherUsersLabels()
    {
        await SeedUserAsync(UserId);
        await SeedUserAsync(OtherUserId);
        await SeedLabelAsync(UserId, "Mine");
        await SeedLabelAsync(OtherUserId, "Theirs");

        var response = await Client.GetAsync("/api/labels");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var labels = await response.Content.ReadFromJsonAsync<List<LabelDto>>();
        labels.Should().HaveCount(1);
        labels![0].Name.Should().Be("Mine");
    }

    // --- POST label ---

    [Fact]
    public async Task CreateLabel_ReturnsCreated()
    {
        await SeedUserAsync(UserId);

        var response = await Client.PostAsJsonAsync("/api/labels", new
        {
            name = "New Label",
            color = "#0000FF"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var label = await response.Content.ReadFromJsonAsync<LabelDto>();
        label!.Name.Should().Be("New Label");
        label.Color.Should().Be("#0000FF");
    }

    // --- PUT label ---

    [Fact]
    public async Task UpdateLabel_UpdatesNameAndColor()
    {
        await SeedUserAsync(UserId);
        var seeded = await SeedLabelAsync(UserId, "Old Name", "#000000");

        var response = await Client.PutAsJsonAsync($"/api/labels/{seeded.Id}", new
        {
            name = "New Name",
            color = "#FFFFFF"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var label = await response.Content.ReadFromJsonAsync<LabelDto>();
        label!.Name.Should().Be("New Name");
        label.Color.Should().Be("#FFFFFF");
    }

    // --- DELETE label ---

    [Fact]
    public async Task DeleteLabel_Returns204()
    {
        await SeedUserAsync(UserId);
        var seeded = await SeedLabelAsync(UserId, "To Delete");

        var response = await Client.DeleteAsync($"/api/labels/{seeded.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // --- Add/Remove label from document ---

    [Fact]
    public async Task AddAndRemoveLabel_FromDocument()
    {
        await SeedUserAsync(UserId);
        var doc = await SeedDocumentAsync(UserId, "Labeled Doc");
        var label = await SeedLabelAsync(UserId, "My Label");

        // Add label to document
        var addResponse = await Client.PostAsync($"/api/documents/{doc.Id}/labels/{label.Id}", null);
        addResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Remove label from document
        var removeResponse = await Client.DeleteAsync($"/api/documents/{doc.Id}/labels/{label.Id}");
        removeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
