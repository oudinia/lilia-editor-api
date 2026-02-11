using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Infrastructure.Data;

public class LiliaDbContext : DbContext
{
    public LiliaDbContext(DbContextOptions<LiliaDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<BibliographyEntry> BibliographyEntries => Set<BibliographyEntry>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<DocumentLabel> DocumentLabels => Set<DocumentLabel>();
    public DbSet<DocumentCollaborator> DocumentCollaborators => Set<DocumentCollaborator>();
    public DbSet<DocumentGroup> DocumentGroups => Set<DocumentGroup>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Formula> Formulas => Set<Formula>();
    public DbSet<Snippet> Snippets => Set<Snippet>();

    // Auth tables (Better Auth)
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Passkey> Passkeys => Set<Passkey>();
    public DbSet<Verification> Verifications => Set<Verification>();
    public DbSet<TwoFactor> TwoFactors => Set<TwoFactor>();

    // Organization & billing
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<AiChat> AiChats => Set<AiChat>();

    // Document features
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<CommentReply> CommentReplies => Set<CommentReply>();
    public DbSet<DocumentSnapshot> DocumentSnapshots => Set<DocumentSnapshot>();
    public DbSet<ConversionAudit> ConversionAudits => Set<ConversionAudit>();
    public DbSet<SyncHistory> SyncHistories => Set<SyncHistory>();

    // Import review
    public DbSet<ImportReviewSession> ImportReviewSessions => Set<ImportReviewSession>();
    public DbSet<ImportBlockReview> ImportBlockReviews => Set<ImportBlockReview>();
    public DbSet<ImportReviewCollaborator> ImportReviewCollaborators => Set<ImportReviewCollaborator>();
    public DbSet<ImportBlockComment> ImportBlockComments => Set<ImportBlockComment>();
    public DbSet<ImportReviewActivity> ImportReviewActivities => Set<ImportReviewActivity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LiliaDbContext).Assembly);
    }
}
