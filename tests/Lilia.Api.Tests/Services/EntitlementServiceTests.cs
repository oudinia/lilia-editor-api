using FluentAssertions;
using Lilia.Tools.Api.Services;
using Microsoft.Extensions.Configuration;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Who is allowed to do the paid things, while no payment provider is attached.
///
/// <para>The gate is enforced now so it isn't retrofitted later. These pin the two
/// properties that matter regardless of how tiers eventually get granted: an
/// anonymous caller can never hold a paid tier, and tiers contain the ones below
/// them rather than being unrelated flags.</para>
/// </summary>
public class EntitlementServiceTests
{
    private static EntitlementService Build(params (string key, string[] ids)[] grants)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (key, ids) in grants)
            for (var i = 0; i < ids.Length; i++)
                values[$"{key}:{i}"] = ids[i];

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new EntitlementService(configuration);
    }

    [Fact]
    public void Anonymous_callers_are_free()
    {
        var e = Build().Resolve(null);

        e.Tier.Should().Be(ToolTier.Free);
        e.CanUseExportPresets.Should().BeFalse();
        e.CanSaveStyles.Should().BeFalse();
    }

    [Fact]
    public void An_empty_user_id_is_not_treated_as_a_user()
    {
        // A blank "sub" claim must not accidentally match a granted empty string.
        Build(("Tools:Entitlements:LiliaPass", [""])).Resolve("").Tier.Should().Be(ToolTier.Free);
    }

    [Fact]
    public void Signing_in_alone_does_not_grant_anything()
    {
        Build().Resolve("user-with-no-purchase").Tier.Should().Be(ToolTier.Free);
    }

    [Fact]
    public void A_document_pass_unlocks_export_presets_but_not_saved_styles()
    {
        var e = Build(("Tools:Entitlements:DocumentPass", ["user-1"])).Resolve("user-1");

        e.Tier.Should().Be(ToolTier.DocumentPass);
        e.CanUseExportPresets.Should().BeTrue();
        e.CanSaveStyles.Should().BeFalse("styles that outlive a document are the yearly tier");
    }

    [Fact]
    public void A_lilia_pass_contains_the_document_pass()
    {
        var e = Build(("Tools:Entitlements:LiliaPass", ["user-2"])).Resolve("user-2");

        e.Tier.Should().Be(ToolTier.LiliaPass);
        e.CanUseExportPresets.Should().BeTrue();
        e.CanSaveStyles.Should().BeTrue();
    }

    [Fact]
    public void The_higher_grant_wins_when_a_user_appears_in_both()
    {
        var e = Build(
            ("Tools:Entitlements:DocumentPass", ["user-3"]),
            ("Tools:Entitlements:LiliaPass", ["user-3"])).Resolve("user-3");

        e.Tier.Should().Be(ToolTier.LiliaPass);
    }

    [Fact]
    public void User_ids_are_matched_exactly()
    {
        var svc = Build(("Tools:Entitlements:LiliaPass", ["User-4"]));

        svc.Resolve("user-4").Tier.Should().Be(ToolTier.Free, "case must not be forgiving for an identity");
        svc.Resolve("User-4").Tier.Should().Be(ToolTier.LiliaPass);
    }
}
