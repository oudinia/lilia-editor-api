using FluentAssertions;
using Lilia.Api.Mcp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The connector's export links are signed, not stored (MCP-71, 12 Oct: links held in memory died
/// at every API restart, and the API restarts after every merge).
/// </summary>
public class ConnectorTransfersTests
{
    private static readonly IDataProtectionProvider Keys = new EphemeralDataProtectionProvider();

    private static ConnectorTransfers Site(string origin) => new(
        new MemoryCache(new MemoryCacheOptions()), Keys,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:PublicOrigin"] = origin }).Build());

    [Fact]
    public void An_export_link_still_works_after_a_restart()
    {
        var doc = Guid.NewGuid();
        var (token, _) = Site("https://editor.liliaeditor.com").IssueExport("user-1", doc, "pdf");
        // A restart: new memory, same keys on disk.
        var ticket = Site("https://editor.liliaeditor.com").Export(token);
        ticket.Should().NotBeNull();
        ticket!.UserId.Should().Be("user-1");
        ticket.DocumentId.Should().Be(doc);
        ticket.Format.Should().Be("pdf");
    }

    [Fact]
    public void A_test_copy_link_is_not_valid_on_production()
    {
        var (token, _) = Site("https://test.liliaeditor.com").IssueExport("user-1", Guid.NewGuid(), "tex");
        Site("https://editor.liliaeditor.com").Export(token).Should().BeNull();
    }

    [Fact]
    public void A_damaged_or_made_up_link_is_refused()
    {
        var site = Site("https://editor.liliaeditor.com");
        var (token, _) = site.IssueExport("user-1", Guid.NewGuid(), "zip");
        site.Export(token[..^4] + "AAAA").Should().BeNull();
        site.Export(new string('a', 48)).Should().BeNull();
        site.Export(null).Should().BeNull();
    }

    [Fact]
    public void An_import_link_survives_a_restart_and_belongs_to_its_author_only()
    {
        var (token, _) = Site("https://editor.liliaeditor.com").IssueImport("user-1", "Claude");
        var after = Site("https://editor.liliaeditor.com");
        after.Import(token, "user-1").Should().NotBeNull().And.Match<ConnectorTransfers.ImportTicket>(t => t.App == "Claude" && t.Status == "waiting");
        after.Import(token, "user-2").Should().BeNull();
    }
}
