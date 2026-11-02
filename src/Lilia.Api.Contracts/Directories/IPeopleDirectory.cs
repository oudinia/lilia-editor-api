namespace Lilia.Api.Directories;

/// <summary>
/// What another context may know about a person (2 Nov 2026, the bounded contexts, step 2): name, email,
/// picture and language, asked by id or email, never by reading the users table. Accounts implements it.
/// </summary>
public interface IPeopleDirectory
{
    Task<PersonCard?> FindAsync(string userId, CancellationToken ct = default);

    /// <summary>The account holding exactly this address, as the users table stores it.</summary>
    Task<PersonCard?> FindByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>The people found among <paramref name="userIds"/>, by id; the missing ones are left out.</summary>
    Task<IReadOnlyDictionary<string, PersonCard>> FindManyAsync(IEnumerable<string> userIds, CancellationToken ct = default);
}

public sealed record PersonCard(string Id, string? Name, string Email, string? Image, string? Locale)
{
    /// <summary>The name, else the email, else "Someone": how a sharing message names a person.</summary>
    public string Called => !string.IsNullOrWhiteSpace(Name) ? Name! : !string.IsNullOrWhiteSpace(Email) ? Email : "Someone";
}
