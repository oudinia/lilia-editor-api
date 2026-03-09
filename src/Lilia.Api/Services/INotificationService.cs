using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface INotificationService
{
    Task CreateAsync(string userId, string type, string title, string? message = null, string? link = null);
    Task<int> GetUnreadCountAsync(string userId);
    Task<PaginatedResult<NotificationDto>> ListAsync(string userId, int page = 1, int pageSize = 20);
    Task<bool> MarkReadAsync(string userId, Guid notificationId);
    Task MarkAllReadAsync(string userId);
}
