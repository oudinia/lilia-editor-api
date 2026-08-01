namespace Lilia.Api.Events.Common;

/// <summary>
/// A document's blocks changed, so an auto-version may be due.
///
/// <para><b>What this replaces.</b> Two identical <c>_ = Task.Run(…)</c> call
/// sites — <c>BlocksController.UpdateBlock</c> and
/// <c>StudioController.UpdateBlock</c> — each opening a hand-rolled DI scope.
/// The comment at both explained why: the request scope disposes its DbContext
/// as soon as the method returns, so capturing the service would hit
/// <i>"Npgsql: A command is already in progress"</i>. A message removes the
/// reason for the workaround rather than repeating it a third time.</para>
///
/// <para><b>Idempotency was already handled, and was checked rather than
/// assumed.</b> <c>VersionService.CreateAutoVersionAsync</c> throttles to one
/// auto-version per document per five minutes, by querying the newest auto-save
/// row before doing anything. A duplicate delivery inside that window is a
/// genuine no-op. That is the opposite finding from the import job, which had no
/// guard at all — which is exactly why the plan says each site needs checking
/// rather than a mechanical repeat.</para>
/// </summary>
/// <param name="DocumentId">Document whose blocks changed.</param>
/// <param name="UserId">Who to attribute the version to.</param>
public record DocumentEditedEvent(Guid DocumentId, string UserId);
