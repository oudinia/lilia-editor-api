using Lilia.Core.Entities;
using Lilia.Infrastructure.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Features.Teams.Data;

/// <summary>
/// Teams' own database context (3 Nov 2026, Teams' bounded context, step 4): its tables only — teams, their
/// groups and the groups' members — and the roles a membership grants (Sharing's too). People and documents are
/// not in its model: Teams asks their directories for them, and Accounts for a person's default team.
///
/// <para>Same database, same tables, as Sharing's context: each table's shape comes from its configuration's
/// <c>Shape</c>, which the main context uses as well; the main context keeps the links and the migrations. It
/// runs on the request's own connection (see Program.cs), so a team and the person's pointer to it can be
/// written in one transaction.</para>
/// </summary>
public sealed class TeamsDbContext(DbContextOptions<TeamsDbContext> options) : DbContext(options)
{
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<Role> Roles => Set<Role>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Other contexts' records: their navigations on Teams' entities are left out of this model.
        modelBuilder.Ignore<DocumentCollaborator>();
        modelBuilder.Ignore<DocumentGroup>();

        TeamConfiguration.Shape(modelBuilder.Entity<Team>());
        GroupConfiguration.Shape(modelBuilder.Entity<Group>());
        GroupMemberConfiguration.Shape(modelBuilder.Entity<GroupMember>());
        RoleConfiguration.Shape(modelBuilder.Entity<Role>());
    }
}
