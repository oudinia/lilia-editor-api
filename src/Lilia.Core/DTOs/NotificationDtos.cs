namespace Lilia.Core.DTOs;

public record NotificationDto(
    Guid Id,
    string Type,
    string Title,
    string? Message,
    string? Link,
    bool IsRead,
    DateTime CreatedAt
);
