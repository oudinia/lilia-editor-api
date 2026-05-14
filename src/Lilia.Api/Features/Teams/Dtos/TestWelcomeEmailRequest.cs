namespace Lilia.Api.Features.Teams.Dtos;

/// <summary>
/// Body for the ad-hoc test endpoint
/// <c>POST /api/teams/test-welcome-email</c>. No DB write — just template
/// validation. <c>Codename</c> is optional; if omitted the generator
/// produces one for the test send.
/// </summary>
public record TestWelcomeEmailRequest(string Email, string? FirstName, string? Codename);
