using System.Security.Claims;
using System.Text.Json;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;

namespace Lilia.Api.Services;

public interface IAuditService
{
    Task LogAsync(string action, string entityType, string? entityId = null, object? details = null);
}

public class AuditService : IAuditService
{
    private readonly LiliaDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditService> _logger;

    public AuditService(LiliaDbContext dbContext, IHttpContextAccessor httpContextAccessor, ILogger<AuditService> logger)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task LogAsync(string action, string entityType, string? entityId = null, object? details = null)
    {
        try
        {
            var context = _httpContextAccessor.HttpContext;
            var userId = context?.User.FindFirst("sub")?.Value
                ?? context?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "unknown";

            var ipAddress = context?.Connection.RemoteIpAddress?.ToString();
            var userAgent = context?.Request.Headers.UserAgent.ToString();

            JsonDocument? detailsDoc = null;
            if (details != null)
            {
                var json = JsonSerializer.Serialize(details);
                detailsDoc = JsonDocument.Parse(json);
            }

            var auditLog = new AuditLog
            {
                UserId = userId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = detailsDoc,
                IpAddress = ipAddress,
                UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent,
            };

            _dbContext.AuditLogs.Add(auditLog);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Audit logging should never break the request
            _logger.LogWarning(ex, "Failed to write audit log for action {Action}", action);
        }
    }
}
