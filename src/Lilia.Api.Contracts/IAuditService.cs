namespace Lilia.Api.Services;

/// <summary>Records who did what (admin_actions). Implemented by AuditService in Lilia.Api.</summary>
public interface IAuditService
{
    Task LogAsync(string action, string entityType, string? entityId = null, object? details = null);
}
