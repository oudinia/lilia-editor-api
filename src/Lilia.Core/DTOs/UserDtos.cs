namespace Lilia.Core.DTOs;

public record UserDto(
    string Id,
    string Email,
    string? Name,
    string? Image,
    DateTime CreatedAt
);

public record CreateOrUpdateUserDto(
    string Id,
    string Email,
    string? Name,
    string? Image
);
