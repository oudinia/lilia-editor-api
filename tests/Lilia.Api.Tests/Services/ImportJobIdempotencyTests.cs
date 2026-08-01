using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// P2.4 stage 1 — the guard that makes queueing an import safe.
///
/// <para>Under the old <c>_ = Task.Run(…)</c> call site this rule was
/// unnecessary: in-process fire-and-forget delivers exactly once, so a second
/// run was impossible. On a durable queue a message can arrive again — after a
/// restart, or on retry — and re-running an import parses the source a second
/// time and bulk-inserts a second set of blocks. <b>The document would silently
/// double.</b></para>
///
/// <para>So this is not a formality bolted onto the migration; it is the
/// precondition for it. The plan's own warning was that the six fire-and-forget
/// sites "may run later, out of order, or twice — each needs checking, not
/// assuming", and checking this one found no guard at all.</para>
/// </summary>
public class ImportJobIdempotencyTests
{
    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Cancelled)]
    public void A_finished_job_refuses_to_run_again(string status)
    {
        LatexImportJobExecutor.IsAlreadyDone(status).Should().BeTrue(
            "re-running would parse the source again and insert a second set of blocks");
    }

    [Fact]
    public void A_pending_job_runs()
    {
        LatexImportJobExecutor.IsAlreadyDone(JobStatus.Pending).Should().BeFalse();
    }

    [Fact]
    public void A_processing_job_is_allowed_to_run_again()
    {
        // The deliberate one. PROCESSING is indistinguishable from a run that
        // died mid-flight — and resuming a crashed import is the entire reason
        // for putting it on a durable queue. Treating it as terminal would
        // recreate exactly the bug being fixed: work stranded as PROCESSING
        // forever, retried by nothing.
        LatexImportJobExecutor.IsAlreadyDone(JobStatus.Processing).Should().BeFalse(
            "a crashed run leaves PROCESSING behind, and resuming it is the point");
    }

    [Fact]
    public void A_failed_job_is_allowed_to_run_again()
    {
        // Failure is what RetryCount / MaxRetries on the Job row exist for.
        LatexImportJobExecutor.IsAlreadyDone(JobStatus.Failed).Should().BeFalse();
    }

    [Fact]
    public void A_missing_job_does_not_block_the_run()
    {
        // Null means the row was not found. Refusing here would silently drop
        // work; letting it proceed surfaces the real problem from the executor,
        // which already fails loudly on a missing session.
        LatexImportJobExecutor.IsAlreadyDone(null).Should().BeFalse();
    }

    [Fact]
    public void An_unrecognised_status_does_not_block_the_run()
    {
        LatexImportJobExecutor.IsAlreadyDone("SOMETHING_NEW").Should().BeFalse();
    }
}
