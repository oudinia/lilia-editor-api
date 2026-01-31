using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface IUserService
{
    Task<UserDto?> GetUserAsync(string userId);
    Task<UserDto> CreateOrUpdateUserAsync(CreateOrUpdateUserDto dto);
    Task<UserDto?> GetUserByEmailAsync(string email);
}
