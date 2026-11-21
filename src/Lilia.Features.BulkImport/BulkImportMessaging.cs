using Lilia.Api.Features.BulkImport.Contracts;
using Wolverine;
using Wolverine.RabbitMQ;

namespace Lilia.Api.Features.BulkImport;

/// <summary>
/// Where bulk import's messages go (21 Nov 2026; the owner chose RabbitMQ). With <c>BulkImport:RabbitMq</c> set (an amqp
/// URI with its vhost: <c>lilia</c> on production, <c>lilia-test</c> on the test copy), to RabbitMQ queues
/// <c>bulk-import.split</c>, <c>bulk-import.ocr</c> and <c>bulk-import.place</c>, made if missing (AutoProvision), each
/// listened to with a durable inbox. Without it (CI, tests, dev), to durable local queues of the same names (Postgres,
/// as the imports queue). The same handlers either way.
/// <list type="bullet">
/// <item>split: 2 at once (CPU and disk: a 95 MB PDF cut into chunk files);</item>
/// <item>ocr: <c>BulkImport:MaxParallelChunks</c> at once (4): the calls to Mathpix in flight in this process;</item>
/// <item>place: one at a time, so a document's blocks never interleave (and a lock per document across processes).</item>
/// </list>
/// </summary>
public static class BulkImportMessaging
{
    public const string SplitQueue = "bulk-import.split";
    public const string OcrQueue = "bulk-import.ocr";
    public const string PlaceQueue = "bulk-import.place";

    public static void Apply(WolverineOptions opts, BulkImportOptions options)
    {
        var parallelChunks = Math.Max(1, options.MaxParallelChunks);
        if (!string.IsNullOrWhiteSpace(options.RabbitMq))
        {
            opts.UseRabbitMq(new Uri(options.RabbitMq)).AutoProvision();
            opts.PublishMessage<SplitBulkImportFile>().ToRabbitQueue(SplitQueue);
            opts.PublishMessage<ReadBulkImportChunk>().ToRabbitQueue(OcrQueue);
            opts.PublishMessage<PlaceBulkImportChunk>().ToRabbitQueue(PlaceQueue);
            opts.ListenToRabbitQueue(SplitQueue).PreFetchCount(2).MaximumParallelMessages(2).UseDurableInbox();
            opts.ListenToRabbitQueue(OcrQueue).PreFetchCount((ushort)parallelChunks).MaximumParallelMessages(parallelChunks).UseDurableInbox();
            opts.ListenToRabbitQueue(PlaceQueue).PreFetchCount(1).Sequential().UseDurableInbox();
            return;
        }
        opts.PublishMessage<SplitBulkImportFile>().ToLocalQueue(SplitQueue).MaximumParallelMessages(2);
        opts.PublishMessage<ReadBulkImportChunk>().ToLocalQueue(OcrQueue).MaximumParallelMessages(parallelChunks);
        opts.PublishMessage<PlaceBulkImportChunk>().ToLocalQueue(PlaceQueue).Sequential();
    }

    /// <summary>The settings, as the services and Wolverine see them, bound once at start.</summary>
    public static BulkImportOptions Read(IConfiguration configuration) =>
        configuration.GetSection(BulkImportOptions.Section).Get<BulkImportOptions>() ?? new BulkImportOptions();
}
