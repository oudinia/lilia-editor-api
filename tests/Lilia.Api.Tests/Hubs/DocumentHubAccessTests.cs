using System.Security.Claims;
using FluentAssertions;
using Lilia.Api.Hubs;
using Lilia.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lilia.Api.Tests.Hubs;

/// <summary>
/// A document's SignalR room carries its live changes (Ask Lilia, apps over MCP, 12 Oct): only
/// someone who may read the document joins it. It took anyone signed in before.
/// </summary>
public class DocumentHubAccessTests
{
    private static (DocumentHub Hub, Mock<IGroupManager> Groups) HubFor(string userId, Guid docId, bool mayRead)
    {
        var documents = new Mock<IDocumentService>();
        documents.Setup(d => d.HasAccessAsync(docId, userId, Lilia.Core.Entities.Permissions.Read)).ReturnsAsync(mayRead);
        var presence = new Mock<IPresenceService>();
        presence.Setup(p => p.GetUsers(It.IsAny<string>())).Returns(new List<UserPresence>());
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", userId) }, "Test")));
        context.SetupGet(c => c.ConnectionId).Returns("conn-1");
        context.SetupGet(c => c.UserIdentifier).Returns(userId);
        var groups = new Mock<IGroupManager>();
        var clients = new Mock<IHubCallerClients>();
        var proxy = new Mock<ISingleClientProxy>();
        clients.SetupGet(c => c.Caller).Returns(proxy.Object);
        clients.Setup(c => c.OthersInGroup(It.IsAny<string>())).Returns(proxy.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
        var hub = new DocumentHub(presence.Object, documents.Object, NullLogger<DocumentHub>.Instance)
        {
            Context = context.Object,
            Groups = groups.Object,
            Clients = clients.Object,
        };
        return (hub, groups);
    }

    [Fact]
    public async Task Someone_who_cannot_read_the_document_does_not_join_its_room()
    {
        var doc = Guid.NewGuid();
        var (hub, groups) = HubFor("stranger", doc, mayRead: false);
        var join = () => hub.JoinDocument(doc.ToString(), "Stranger", null);
        await join.Should().ThrowAsync<HubException>();
        groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_reader_joins()
    {
        var doc = Guid.NewGuid();
        var (hub, groups) = HubFor("reader", doc, mayRead: true);
        await hub.JoinDocument(doc.ToString(), "Reader", null);
        groups.Verify(g => g.AddToGroupAsync("conn-1", $"doc-{doc}", It.IsAny<CancellationToken>()), Times.Once);
    }
}
