using System.Globalization;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lilia.Api.Tests.Features.Sharing;

/// <summary>
/// Sharing's messages resolve from their new project (2 Nov 2026, the Sharing slice as its own project). The
/// .resx files moved with it; a lookup that misses doesn't fail, it prints the key ("DocumentNotFound") in
/// the invite results and the notifications. This holds the names in place, in English and French.
/// </summary>
public class SharingMessagesTests : IntegrationTestBase
{
    public SharingMessagesTests(TestDatabaseFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData("en", "DocumentNotFound")]
    [InlineData("fr", "DocumentNotFound")]
    [InlineData("en", "NotificationShared")]
    public void A_message_is_found_in_its_language(string culture, string key)
    {
        var localizer = Fixture.Factory.Services.GetRequiredService<IStringLocalizer<SharedMessages>>();
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var s = localizer[key];
            s.ResourceNotFound.Should().BeFalse($"\"{key}\" must come from SharedMessages.{culture}.resx");
            s.Value.Should().NotBe(key);
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void French_and_english_differ()
    {
        var localizer = Fixture.Factory.Services.GetRequiredService<IStringLocalizer<SharedMessages>>();
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("fr");
            localizer["DocumentNotFound"].Value.Should().Be("Document introuvable");
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }
}
