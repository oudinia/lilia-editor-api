using FluentAssertions;
using Lilia.Api.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>MCP-112 (run 3b): check_document's verdict was lost when the API restarted.</summary>
public class CheckMemoStoreTests
{
    [Fact]
    public void A_new_instance_reads_what_the_last_one_kept()
    {
        var dir = Directory.CreateTempSubdirectory("lilia-memos-").FullName;
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:CheckMemoDir"] = dir }).Build();
            var env = new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().Object;
            var id = Guid.NewGuid();
            var version = new DateTime(2026, 10, 13, 16, 39, 0, DateTimeKind.Utc);
            new CheckMemoStore(env, config, NullLogger<CheckMemoStore>.Instance)
                .Set(id, new CheckMemoStore.Memo(version, version.AddSeconds(5), Compiles: false, PrintsCorrectly: false, Error: "Undefined control sequence."));

            var restarted = new CheckMemoStore(env, config, NullLogger<CheckMemoStore>.Instance);
            restarted.Current(id, version)!.Error.Should().Be("Undefined control sequence.");
            restarted.Current(id, version.AddMinutes(1)).Should().BeNull("the document changed since");
        }
        finally { Directory.Delete(dir, true); }
    }
}
