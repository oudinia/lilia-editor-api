using Lilia.Api.Services;
using Lilia.Core.DTOs;

namespace Lilia.Api.Mcp;

/// <summary>
/// A PDF or a scanned image sent through the connector's <c>import_document</c>, imported in the background (PDF and scan
/// import, 20 Nov 2026). Mathpix reads a PDF in up to about 80 s (the import's cap) and the LaTeX import follows; an MCP
/// call that waits that long times out in some apps and looks broken in all of them. So <c>import_document</c> answers at
/// once with an importId, the same ticket <c>start_import</c> uses, and <c>import_status</c> reports working, then done
/// (the document) or failed (the reason). The plan's PDF pages were checked before this starts.
///
/// <para>The import runs in its own DI scope (the call's scope ends with its reply) through the same
/// <see cref="IJobService.CreateImportJobFromBase64Async"/> as the editor's upload, without the review step, so it is
/// metered as the editor's is. Its result lives in memory with the ticket: a restart loses only the status, never a
/// finished document.</para>
/// </summary>
public sealed class ConnectorScanImports(IServiceScopeFactory scopes, ConnectorTransfers transfers, ILogger<ConnectorScanImports> logger)
{
    /// <summary>Starts the import and returns its ticket, already "working". The returned task ends with the import.</summary>
    public (string ImportId, ConnectorTransfers.ImportTicket Ticket, Task Work) Start(string userId, string app, ImportRequestDto request)
    {
        var (token, ticket) = transfers.IssueImport(userId, app);
        ticket.Status = "working";
        var work = Task.Run(() => RunAsync(userId, request, ticket));
        return (token, ticket, work);
    }

    private async Task RunAsync(string userId, ImportRequestDto request, ConnectorTransfers.ImportTicket ticket)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IJobService>();
            var result = await jobs.CreateImportJobFromBase64Async(userId, request);
            if (result.Document is null)
                throw new InvalidOperationException(result.Job?.ErrorMessage ?? "the importer produced no document");
            // What the review would have shown (a boxed symbol, a figure without its image), since there is no review here:
            // import_status passes it on (the launch bot's retest of API #672, 20 Nov 2026). Never fails the import.
            try
            {
                var db = scope.ServiceProvider.GetRequiredService<Lilia.Infrastructure.Data.LiliaDbContext>();
                (ticket.Warnings, ticket.WarningsTotal) = await ConnectorTransfers.ImportWarning.ForSessionAsync(db, result.ReviewSessionId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Mcp] the warnings of import_document {File} couldn't be read", request.Filename);
            }
            ticket.DocumentId = result.Document.Id;
            ticket.Title = result.Document.Title;
            ticket.Status = "done";
            logger.LogInformation("[Mcp] import_document {File} ({Format}) → {DocId} in the background", request.Filename, request.Format, result.Document.Id);
        }
        catch (QuotaExceededException limit)
        {
            ticket.Error = limit.Message;
            ticket.Status = "failed";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Mcp] background import_document of {File} failed", request.Filename);
            // Mathpix's own errors carry a sentence for the author; anything else, its message.
            var message = ex is Lilia.Core.Exceptions.LiliaException lilia ? lilia.UserMessage : ex.Message;
            ticket.Error = message.Length > 300 ? message[..300] : message;
            ticket.Status = "failed";
        }
    }
}
