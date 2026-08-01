namespace Lilia.Api.Events.Common;

/// <summary>
/// Asks for a queued document export to be produced.
///
/// <para><b>What this replaces.</b> <c>JobService.CreateExportJobAsync</c> created
/// the <c>Job</c> row as PROCESSING and then did the whole export <i>inline on
/// the request thread</i>. Three things followed, each contradicting what the row
/// claimed: the caller blocked for the entire export, nothing retried despite
/// <c>RetryCount</c> and <c>MaxRetries</c>, and a restart mid-export left the row
/// PROCESSING forever with no worker attached to it.</para>
///
/// <para><b>No API contract change.</b> <c>POST api/lilia/jobs/export</c> already
/// returned job info for tracking, and <c>GET api/lilia/jobs/{id}/result</c>
/// already polled for the outcome. The endpoints were written for an async
/// export; only the execution was synchronous.</para>
///
/// <para><b>Carries the id alone.</b> Everything the handler needs is on the row,
/// committed before this is published — target format, document, user. Nothing
/// from the request context is captured.</para>
///
/// <para><b>Idempotency:</b> <c>RunExportJobAsync</c> refuses to start from a
/// terminal status, so a redelivery cannot double-write the result.</para>
/// </summary>
public record RunExportJobEvent(Guid JobId);
