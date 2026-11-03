using Lilia.Core.Entities;
using Lilia.Infrastructure.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Features.Sharing.Data;

/// <summary>
/// Sharing's own database context (2 Nov 2026, the bounded contexts, step 4): its tables only — the shares
/// with people and with groups, the invitations, comments and their replies, notifications, and the roles
/// it grants (which Teams reads too). Documents, people, groups and blocks are not in its model: Sharing
/// asks their directories for them.
///
/// <para>Same database, same tables. Each table's shape comes from its configuration's <c>Shape</c>, which
/// the main context uses as well, so the two can't drift; the main context keeps the links out of Sharing
/// and the migrations. It runs on the request's own connection (see Program.cs), so a request still uses
/// one connection and Sharing can join a transaction the main context began.</para>
/// </summary>
public sealed class SharingDbContext(DbContextOptions<SharingDbContext> options) : DbContext(options)
{
    public DbSet<DocumentCollaborator> DocumentCollaborators => Set<DocumentCollaborator>();
    public DbSet<DocumentGroup> DocumentGroups => Set<DocumentGroup>();
    public DbSet<DocumentPendingInvite> DocumentPendingInvites => Set<DocumentPendingInvite>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<CommentReply> CommentReplies => Set<CommentReply>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Role> Roles => Set<Role>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Other contexts' records: their navigations on Sharing's entities are left out of this model.
        modelBuilder.Ignore<Document>();
        modelBuilder.Ignore<User>();
        modelBuilder.Ignore<Group>();
        modelBuilder.Ignore<GroupMember>();
        modelBuilder.Ignore<Block>();

        DocumentCollaboratorConfiguration.Shape(modelBuilder.Entity<DocumentCollaborator>());
        DocumentGroupConfiguration.Shape(modelBuilder.Entity<DocumentGroup>());
        DocumentPendingInviteConfiguration.Shape(modelBuilder.Entity<DocumentPendingInvite>());
        CommentConfiguration.Shape(modelBuilder.Entity<Comment>());
        CommentReplyConfiguration.Shape(modelBuilder.Entity<CommentReply>());
        NotificationConfiguration.Shape(modelBuilder.Entity<Notification>());
        RoleConfiguration.Shape(modelBuilder.Entity<Role>());
    }
}
