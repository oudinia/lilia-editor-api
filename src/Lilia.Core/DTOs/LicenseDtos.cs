namespace Lilia.Core.DTOs;

public record LicenseStatusDto(
    string Edition,
    string[] Features,
    int MaxSnapshots
);
