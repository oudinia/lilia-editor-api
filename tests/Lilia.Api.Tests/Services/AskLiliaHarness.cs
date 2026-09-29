using System.Text.Json;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Engines;
using Lilia.Import.Interfaces;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Builds an <see cref="AskLiliaService"/> with mocked collaborators so the
/// tool functions can be called directly. Ask Lilia's model cannot be called
/// in tests (no key), so the tools are the unit.
/// </summary>
internal sealed class AskLiliaHarness
{
    public Mock<IDocumentService> Documents { get; } = new();
    public Mock<IBlockService> Blocks { get; } = new();
    public Mock<IVersionService> Versions { get; } = new();
    public Mock<IRenderService> Render { get; } = new();
    public Mock<ILmlTextParser> Lml { get; } = new();
    /// <summary>The real parser: the tool is only worth testing against what Import LaTeX really reads.</summary>
    public ILatexParser LatexParser { get; } = new Lilia.Import.Services.LatexParser();
    public List<(string Group, string Method, object?[] Args)> Sent { get; } = new();
    public LiliaDbContext Db { get; } = new(
        new DbContextOptionsBuilder<LiliaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public AskLiliaService Service { get; }

    public AskLiliaHarness()
    {
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<Lilia.Api.Hubs.DocumentHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        Proxy = proxy;

        Service = new AskLiliaService(
            Mock.Of<IChatClient>(), Mock.Of<IEntitlementService>(), Mock.Of<IAiCatalogService>(),
            Mock.Of<IAskLiliaRouter>(), Mock.Of<IKbService>(),
            Documents.Object, Blocks.Object, Render.Object, Versions.Object, Lml.Object, LatexParser,
            hub.Object, Db, Options.Create(new AiOptions()),
            new ConfigurationBuilder().Build(), NullLogger<AskLiliaService>.Instance);
    }

    public Mock<IClientProxy> Proxy { get; }

    public static DocumentDto Doc(params BlockDto[] blocks) =>
        ((DocumentDto)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(DocumentDto)))
        with
        {
            Id = Guid.NewGuid(), Title = "T", Role = "owner",
            Blocks = blocks.ToList(), Bibliography = new(), Labels = new(),
        };

    public static BlockDto Block(Guid id, string type = "paragraph") =>
        new(id, Guid.Empty, type, JsonDocument.Parse("{\"text\":\"hi\"}").RootElement, 0, null, 0, DateTime.UtcNow, DateTime.UtcNow);

    public IList<AITool> Tools(DocumentDto doc, bool allowWrite, List<string>? changed = null, Action? meta = null) =>
        Service.BuildDocumentTools(new AskLiliaService.LiveDocument(doc), doc.Id, "u1", allowWrite,
            changed ?? new List<string>(), meta ?? (() => { }), default);

    public static AIFunction Fn(IList<AITool> tools, string name) =>
        tools.OfType<AIFunction>().Single(t => t.Name == name);

    public static async Task<JsonElement> Call(AIFunction f, params (string Name, object? Value)[] args)
    {
        var a = new AIFunctionArguments();
        foreach (var (n, v) in args) a[n] = v;
        var result = await f.InvokeAsync(a);
        return result is JsonElement je ? je : JsonSerializer.SerializeToElement(result);
    }
}
