using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

public class UserService : IUserService
{
    private readonly LiliaDbContext _context;
    private readonly IDocumentService _documentService;
    private readonly ILogger<UserService> _logger;

    public UserService(LiliaDbContext context, IDocumentService documentService, ILogger<UserService> logger)
    {
        _context = context;
        _documentService = documentService;
        _logger = logger;
    }

    public async Task<UserDto?> GetUserAsync(string userId)
    {
        var user = await _context.Users.FindAsync(userId);
        return user == null ? null : MapToDto(user);
    }

    public async Task<UserDto> CreateOrUpdateUserAsync(CreateOrUpdateUserDto dto)
    {
        // Look up by ID first, but also fall back to email — handles the case
        // where the same human user signs in via two identity providers that
        // mint different `sub` claims for the same email address. Without
        // this, the second sign-in races on `users_email_key` and 500s.
        var user = await _context.Users.FindAsync(dto.Id)
            ?? (string.IsNullOrEmpty(dto.Email)
                ? null
                : await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email));
        var isNewUser = user == null;

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
                // Race: another request inserted a user with the same id OR
                // the same email between our lookup and SaveChanges. Detach
                // the failed entity and re-fetch by id, then by email.
                _context.Entry(user).State = EntityState.Detached;
                user = await _context.Users.FindAsync(dto.Id);
                if (user == null && !string.IsNullOrEmpty(dto.Email))
                {
                    user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
                }
                if (user != null)
                {
                    user.Email = dto.Email;
                    user.Name = dto.Name;
                    user.Image = dto.Image;
                    user.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    // Race recovered — the row already existed, so skip the
                    // starter-document clone on this request to avoid
                    // duplicating it for the same user.
                    isNewUser = false;
                }
                else
                {
                    // Couldn't recover — re-throw so the caller sees a real
                    // error instead of a silent null.
                    throw;
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

        // Clone starter documents for new users
        if (isNewUser)
        {
            try
            {
                var count = await _documentService.CloneStarterDocumentsAsync(dto.Id);
                if (count > 0)
                    _logger.LogInformation("[Onboarding] Cloned {Count} starter documents for new user {UserId}", count, dto.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Onboarding] Failed to clone starter documents for user {UserId}", dto.Id);
            }
        }

        // Accept any pending invites for this email
        if (!string.IsNullOrEmpty(dto.Email))
        {
            await AcceptPendingInvitesAsync(dto.Id, dto.Email);
        }

        return MapToDto(user!);
    }

    private async Task AcceptPendingInvitesAsync(string userId, string email)
    {
        var pendingInvites = await _context.DocumentPendingInvites
            .Where(pi => pi.Email == email && pi.Status == "pending" && pi.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();

        foreach (var invite in pendingInvites)
        {
            // Find the role by name
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == invite.Role);
            if (role == null) continue;

            // Check if already a collaborator
            var existing = await _context.DocumentCollaborators
                .AnyAsync(dc => dc.DocumentId == invite.DocumentId && dc.UserId == userId);
            if (existing)
            {
                invite.Status = "accepted";
                continue;
            }

            var collaborator = new DocumentCollaborator
            {
                Id = Guid.NewGuid(),
                DocumentId = invite.DocumentId,
                UserId = userId,
                RoleId = role.Id,
                InvitedBy = invite.InvitedBy,
                CreatedAt = DateTime.UtcNow
            };
            _context.DocumentCollaborators.Add(collaborator);
            invite.Status = "accepted";
        }

        if (pendingInvites.Count > 0)
        {
            await _context.SaveChangesAsync();
        }
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
