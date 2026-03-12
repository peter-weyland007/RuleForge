using Microsoft.EntityFrameworkCore;
using RuleForge.Domain.Characters;
using RuleForge.Domain.Campaigns;
using RuleForge.Domain.Bestiary;
using RuleForge.Domain.Encounters;
using RuleForge.Domain.Parties;
using RuleForge.Domain.Quests;
using RuleForge.Domain.Users;
using RuleForge.Domain.Marketplace;
using RuleForge.Domain.Items;
using RuleForge.Domain.Common;

namespace RuleForge.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignShare> CampaignShares => Set<CampaignShare>();
    public DbSet<Creature> Creatures => Set<Creature>();
    public DbSet<CreatureShare> CreatureShares => Set<CreatureShare>();
    public DbSet<Encounter> Encounters => Set<Encounter>();
    public DbSet<EncounterParticipant> EncounterParticipants => Set<EncounterParticipant>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<PartyShare> PartyShares => Set<PartyShare>();
    public DbSet<Quest> Quests => Set<Quest>();
    public DbSet<QuestShare> QuestShares => Set<QuestShare>();
    public DbSet<QuestNode> QuestNodes => Set<QuestNode>();
    public DbSet<QuestChoice> QuestChoices => Set<QuestChoice>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<CharacterShare> CharacterShares => Set<CharacterShare>();
    public DbSet<ItemShare> ItemShares => Set<ItemShare>();
    public DbSet<FriendLink> FriendLinks => Set<FriendLink>();
    public DbSet<MarketplaceListing> MarketplaceListings => Set<MarketplaceListing>();
    public DbSet<MarketplaceListingVersion> MarketplaceListingVersions => Set<MarketplaceListingVersion>();
    public DbSet<MarketplaceImport> MarketplaceImports => Set<MarketplaceImport>();
    public DbSet<MarketplaceAuditEvent> MarketplaceAuditEvents => Set<MarketplaceAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var c = modelBuilder.Entity<Character>();
        c.HasKey(x => x.CharacterId);
        c.Property(x => x.Name).HasMaxLength(120).IsRequired();
        c.Property(x => x.PlayerName).HasMaxLength(120);
        c.HasIndex(x => new { x.CampaignId, x.Name });

        var cp = modelBuilder.Entity<Campaign>();
        cp.HasKey(x => x.CampaignId);
        cp.Property(x => x.Name).HasMaxLength(120).IsRequired();
        cp.Property(x => x.Description).HasMaxLength(2000);
        cp.HasIndex(x => x.Name).IsUnique();
        cp.Property(x => x.OwnerAppUserId);
        cp.HasIndex(x => x.OwnerAppUserId);

        var cr = modelBuilder.Entity<Creature>();
        cr.HasKey(x => x.CreatureId);
        cr.Property(x => x.Name).HasMaxLength(120).IsRequired();
        cr.Property(x => x.Description).HasMaxLength(4000);
        cr.Property(x => x.Speed).HasMaxLength(80);
        cr.Property(x => x.ChallengeRating).HasMaxLength(32);
        cr.Property(x => x.Strength);
        cr.Property(x => x.Dexterity);
        cr.Property(x => x.Constitution);
        cr.Property(x => x.Intelligence);
        cr.Property(x => x.Wisdom);
        cr.Property(x => x.Charisma);
        cr.HasIndex(x => x.Name);

        var e = modelBuilder.Entity<Encounter>();
        e.HasKey(x => x.EncounterId);
        e.Property(x => x.Name).HasMaxLength(160).IsRequired();
        e.Property(x => x.Description).HasMaxLength(4000);
        e.HasMany(x => x.Participants).WithOne(x => x.Encounter!).HasForeignKey(x => x.EncounterId).OnDelete(DeleteBehavior.Cascade);

        var ep = modelBuilder.Entity<EncounterParticipant>();
        ep.HasKey(x => x.EncounterParticipantId);
        ep.Property(x => x.NameSnapshot).HasMaxLength(120).IsRequired();

        var pty = modelBuilder.Entity<Party>();
        pty.HasKey(x => x.PartyId);
        pty.Property(x => x.Name).HasMaxLength(120).IsRequired();
        pty.Property(x => x.Description).HasMaxLength(2000);
        pty.HasIndex(x => new { x.CampaignId, x.Name }).IsUnique();
        pty.Property(x => x.OwnerAppUserId);
        pty.HasIndex(x => x.OwnerAppUserId);

        var q = modelBuilder.Entity<Quest>();
        q.HasKey(x => x.QuestId);
        q.Property(x => x.Title).HasMaxLength(180).IsRequired();
        q.Property(x => x.Summary).HasMaxLength(4000);
        q.HasIndex(x => new { x.CampaignId, x.Title });
        q.Property(x => x.OwnerAppUserId);
        q.HasIndex(x => x.OwnerAppUserId);

        var qn = modelBuilder.Entity<QuestNode>();
        qn.HasKey(x => x.QuestNodeId);
        qn.Property(x => x.Title).HasMaxLength(180).IsRequired();
        qn.Property(x => x.BodyMarkdown).HasMaxLength(20000);
        qn.Property(x => x.DmHints).HasMaxLength(8000);
        qn.Property(x => x.CanvasX);
        qn.Property(x => x.CanvasY);
        qn.HasIndex(x => new { x.QuestId, x.OrderIndex });

        var qc = modelBuilder.Entity<QuestChoice>();
        qc.HasKey(x => x.QuestChoiceId);
        qc.Property(x => x.Label).HasMaxLength(200).IsRequired();
        qc.Property(x => x.ConditionExpression).HasMaxLength(1000);
        qc.Property(x => x.EffectsJson).HasMaxLength(4000);
        qc.HasIndex(x => new { x.QuestId, x.FromNodeId, x.OrderIndex });

        var u = modelBuilder.Entity<AppUser>();
        u.HasKey(x => x.AppUserId);
        u.Property(x => x.Username).HasMaxLength(80).IsRequired();
        u.Property(x => x.Email).HasMaxLength(180).IsRequired();
        u.Property(x => x.PasswordHash).HasMaxLength(400).IsRequired();
        u.Property(x => x.MustChangePassword);
        u.Property(x => x.IsSystem);
        u.Property(x => x.ThemePreference).HasMaxLength(16);
        u.Property(x => x.CampaignNavExpanded);
        u.Property(x => x.CompendiumNavExpanded);
        u.HasIndex(x => x.Username).IsUnique();
        u.HasIndex(x => x.Email).IsUnique();

        var it = modelBuilder.Entity<Item>();
        it.HasKey(x => x.ItemId);
        it.Property(x => x.Name).HasMaxLength(160).IsRequired();
        it.Property(x => x.Description).HasMaxLength(8000);
        it.Property(x => x.CostCurrency).HasMaxLength(8);
        it.Property(x => x.SourceType);
        it.Property(x => x.Source).HasMaxLength(120);
        it.Property(x => x.Tags).HasMaxLength(500);
        it.Property(x => x.WeaponCategory).HasMaxLength(40);
        it.Property(x => x.DamageDice).HasMaxLength(40);
        it.Property(x => x.DamageType).HasMaxLength(40);
        it.Property(x => x.Properties).HasMaxLength(500);
        it.Property(x => x.ArmorCategory).HasMaxLength(40);
        it.Property(x => x.RechargeRule).HasMaxLength(200);
        it.Property(x => x.ConsumableEffect).HasMaxLength(2000);
        it.Property(x => x.Notes).HasMaxLength(2000);
        it.HasIndex(x => x.Name);
        it.Property(x => x.OwnerAppUserId);
        it.Property(x => x.IsSystem).HasDefaultValue(false);
        it.HasIndex(x => x.OwnerAppUserId);

        var fl = modelBuilder.Entity<FriendLink>();
        fl.HasKey(x => x.FriendLinkId);
        fl.Property(x => x.Status).IsRequired();
        fl.HasIndex(x => new { x.RequesterUserId, x.AddresseeUserId }).IsUnique();
        fl.HasIndex(x => new { x.AddresseeUserId, x.Status });

        var cs = modelBuilder.Entity<CharacterShare>();
        cs.HasKey(x => x.CharacterShareId);
        cs.HasIndex(x => new { x.CharacterId, x.SharedWithUserId }).IsUnique();

        var ishr = modelBuilder.Entity<ItemShare>();
        ishr.HasKey(x => x.ItemShareId);
        ishr.HasIndex(x => new { x.ItemId, x.SharedWithUserId }).IsUnique();

        var csh = modelBuilder.Entity<CampaignShare>();
        csh.HasKey(x => x.CampaignShareId);
        csh.HasIndex(x => new { x.CampaignId, x.SharedWithUserId }).IsUnique();

        var psh = modelBuilder.Entity<PartyShare>();
        psh.HasKey(x => x.PartyShareId);
        psh.HasIndex(x => new { x.PartyId, x.SharedWithUserId }).IsUnique();

        var qsh = modelBuilder.Entity<QuestShare>();
        qsh.HasKey(x => x.QuestShareId);
        qsh.HasIndex(x => new { x.QuestId, x.SharedWithUserId }).IsUnique();

        var ml = modelBuilder.Entity<MarketplaceListing>();
        ml.HasKey(x => x.MarketplaceListingId);
        ml.HasIndex(x => new { x.AssetType, x.State });
        ml.HasIndex(x => new { x.OwnerUserId, x.State });

        var mv = modelBuilder.Entity<MarketplaceListingVersion>();
        mv.HasKey(x => x.MarketplaceListingVersionId);
        mv.HasIndex(x => new { x.MarketplaceListingId, x.DateCreatedUtc });

        var mi = modelBuilder.Entity<MarketplaceImport>();
        mi.HasKey(x => x.MarketplaceImportId);
        mi.HasIndex(x => new { x.ImportedByUserId, x.DateImportedUtc });

        var ma = modelBuilder.Entity<MarketplaceAuditEvent>();
        ma.HasKey(x => x.MarketplaceAuditEventId);
        ma.HasIndex(x => new { x.MarketplaceListingId, x.DateUtc });

        var crs = modelBuilder.Entity<CreatureShare>();
        crs.HasKey(x => x.CreatureShareId);
        crs.HasIndex(x => new { x.CreatureId, x.SharedWithUserId }).IsUnique();


    }
}

