namespace Lilia.Api.Events.Common;

/// <summary>Build an author's "export everything" archive (Settings → Your data).</summary>
public record RunAccountExportEvent(Guid JobId);
