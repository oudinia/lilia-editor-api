using FluentAssertions;
using Lilia.Admin.Data;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Admin.Tests;

/// <summary>
/// The console's Operations page (the owner, 11 Nov 2026: operations weighted by complexity, "even an admin section"):
/// use by kind and account over a period, and the weights, which an admin changes with a logged action.
/// </summary>
[Collection("Admin")]
public class AdminOperationsTests(AdminFixture fx)
{
    private static AdminOperations Operations(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<AdminOperations>();

    [Fact]
    public async Task The_overview_counts_operations_and_points_by_kind_and_account()
    {
        var id = $"adm-ops-{Guid.NewGuid():N}"[..28];
        await using (var db = fx.CreateDbContext())
        {
            var now = DateTime.UtcNow;
            db.Users.Add(new User { Id = id, Email = $"{id}@lilia.test", Name = "Ops", CreatedAt = now, UpdatedAt = now });
            db.Operations.AddRange(
                new Operation { Id = Guid.NewGuid(), UserId = id, Kind = "export.pdf", Format = "pdf", Source = "connector", Points = 5, CreatedAt = now },
                new Operation { Id = Guid.NewGuid(), UserId = id, Kind = "edit.small", Format = "", Source = "connector", Points = 1, CreatedAt = now });
            await db.SaveChangesAsync();
        }
        using var scope = fx.Factory.Services.CreateScope();
        var o = await Operations(scope).OverviewAsync(7);

        o.Kinds.Should().Contain(k => k.Code == "export.pdf" && k.Points == 5).And.Contain(k => k.Code == "import" && k.PointsPerUnit == 1);
        o.TopAccounts.Should().Contain(a => a.UserId == id && a.Count == 2 && a.Points == 6);
    }

    [Fact]
    public async Task A_weight_change_is_saved_and_logged_and_a_bad_one_refused()
    {
        using var scope = fx.Factory.Services.CreateScope();
        var ops = Operations(scope);
        (await ops.SetWeightAsync("check", -1, null, null, true, "admin@lilia.test")).Should().NotBeNull();
        (await ops.SetWeightAsync("check", 3, 1, null, true, "admin@lilia.test")).Should().NotBeNull("points per unit need a unit size");

        (await ops.SetWeightAsync("check", 2, null, null, true, "admin@lilia.test")).Should().BeNull();
        await using var db = fx.CreateDbContext();
        (await db.OperationKinds.AsNoTracking().SingleAsync(k => k.Code == "check")).Points.Should().Be(2);
        (await db.AdminActions.AsNoTracking().AnyAsync(a => a.Action == "operation_weight" && a.AdminEmail == "admin@lilia.test")).Should().BeTrue();

        (await ops.SetWeightAsync("check", 3, null, null, true, "admin@lilia.test")).Should().BeNull("put back for the other tests");
    }
}
