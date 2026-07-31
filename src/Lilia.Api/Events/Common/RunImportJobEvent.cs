namespace Lilia.Api.Events.Common;

/// <summary>
/// Asks for a queued LaTeX import to be parsed and staged.
///
/// <para><b>What this replaces.</b> <c>ImportsController</c> did the work inside
/// <c>_ = Task.Run(async () => …)</c>, opening a hand-rolled DI scope so the
/// captured DbContext would not outlive the request. The <c>Job</c> row it
/// operates on has always <i>declared</i> the behaviour that call site did not
/// implement — <c>Status</c> PENDING/PROCESSING, <c>RetryCount</c>,
/// <c>MaxRetries = 3</c> — so a restart left work stranded as PROCESSING
/// forever, retried by nothing. The table was a queue schema with no queue
/// behind it.</para>
///
/// <para><b>Carries ids, not state.</b> Both rows are committed before this is
/// published, so the handler re-reads them in its own scope. Nothing about the
/// request context is captured, and the message stays small enough to be worth
/// persisting.</para>
///
/// <para><b>Idempotency is real here, not a formality.</b> Under the old call
/// site delivery was exactly-once, so re-running was impossible. On a durable
/// queue a message can arrive again after a restart or a retry, and re-running
/// this one would parse the source a second time and bulk-insert a second set of
/// blocks — the document would silently double. <c>LatexImportJobExecutor</c>
/// therefore refuses to start from a terminal job status.</para>
/// </summary>
/// <param name="JobId">The <c>Job</c> row tracking status, progress and retries.</param>
/// <param name="SessionId">The <c>ImportReviewSession</c> holding the raw source.</param>
public record RunImportJobEvent(Guid JobId, Guid SessionId);
