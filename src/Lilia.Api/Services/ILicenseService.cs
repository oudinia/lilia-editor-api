using Lilia.Core.DTOs;

namespace Lilia.Api.Services;

public interface ILicenseService
{
    Task<LicenseStatusDto> GetLicenseStatusAsync(string userId);
}
