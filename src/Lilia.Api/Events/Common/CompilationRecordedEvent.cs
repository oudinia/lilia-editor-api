namespace Lilia.Api.Events.Common;

/// <summary>
/// Published after every LaTeX compile — validate, block validate, document
/// validate — so the outcome can be recorded without the request waiting for it.
///
/// <para><b>What this replaces.</b> The controller previously did the write
/// itself inside <c>_ = Task.Run(async () => …)</c>, opening its own DI scope to
/// get a DbContext because the request-scoped one raced with the request's own
/// <c>SaveChangesAsync</c> — "A second operation was started on this context
/// instance" — and the comment recording that race sat right above the code
/// that caused it. Handing the work to a message removes the whole class of
/// problem: the handler gets a fresh scope from the framework, not from a
/// hand-rolled one, and nothing about it runs on the request's thread.</para>
///
/// <para><b>Carries values, not references.</b> Everything a handler needs is
/// copied in, including the user id, which must be read from
/// <c>HttpContext.User</c> before publishing — <c>HttpContext</c> is not safe to
/// touch once the request has ended, and that was true of the Task.Run version
/// too.</para>
///
/// <para><b>Idempotency contract:</b> handlers must tolerate duplicate delivery.
/// This is telemetry — a duplicated row is a rounding error, and nothing reads
/// these rows expecting exactly-once. Local queues are fire-once today anyway;
/// the contract exists so that turning on durability later cannot break it.</para>
/// </summary>
/// <param name="EventType">
/// Which compile path produced this — <c>validate</c>, <c>validate_block</c>,
/// <c>validate_document</c>. Kept a string rather than an enum because it is
/// stored as one and queried as one.
/// </param>
public record CompilationRecordedEvent(
    string EventType,
    bool Success,
    int WarningCount,
    // int, not long — matches LaTeXCompilationEvent.DurationMs, and a compile
    // that takes more than 24 days to time out is not the problem to solve here.
    int DurationMs,
    Guid? DocumentId = null,
    Guid? BlockId = null,
    string? BlockType = null,
    string? ErrorRaw = null,
    string? ErrorCategory = null,
    string? ErrorToken = null,
    int? ErrorLine = null,
    string? UserId = null);
