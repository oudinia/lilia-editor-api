using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class UserService : IUserService
{
    private readonly LiliaDbContext _context;

    public UserService(LiliaDbContext context)
    {
        _context = context;
    }

    public async Task<UserDto?> GetUserAsync(string userId)
    {
        var user = await _context.Users.FindAsync(userId);
        return user == null ? null : MapToDto(user);
    }

    public async Task<UserDto> CreateOrUpdateUserAsync(CreateOrUpdateUserDto dto)
    {
        var user = await _context.Users.FindAsync(dto.Id);

        if (user == null)
        {
            user = new User
            {
                Id = dto.Id,
                Email = dto.Email,
                Name = dto.Name,
                Image = dto.Image,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.Users.Add(user);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Race condition: another request created the user first
                // Detach the failed entity and fetch the existing one
                _context.Entry(user).State = EntityState.Detached;
                user = await _context.Users.FindAsync(dto.Id);
                if (user != null)
                {
                    user.Email = dto.Email;
                    user.Name = dto.Name;
                    user.Image = dto.Image;
                    user.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }
        }
        else
        {
            user.Email = dto.Email;
            user.Name = dto.Name;
            user.Image = dto.Image;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return MapToDto(user!);
    }

    public async Task<UserDto?> GetUserByEmailAsync(string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        return user == null ? null : MapToDto(user);
    }

    private static UserDto MapToDto(User u)
    {
        return new UserDto(u.Id, u.Email, u.Name, u.Image, u.CreatedAt);
    }
}
