using Microsoft.EntityFrameworkCore;
using RuleForge.Domain.Characters;
using RuleForge.Domain.Campaigns;
using RuleForge.Domain.Bestiary;
using RuleForge.Domain.Encounters;
using RuleForge.Domain.Parties;
using RuleForge.Domain.Quests;

namespace RuleForge.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Creature> Creatures => Set<Creature>();
    public DbSet<Encounter> Encounters => Set<Encounter>();
    public DbSet<EncounterParticipant> EncounterParticipants => Set<EncounterParticipant>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<Quest> Quests => Set<Quest>();
    public DbSet<QuestNode> QuestNodes => Set<QuestNode>();
    public DbSet<QuestChoice> QuestChoices => Set<QuestChoice>();

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

        var q = modelBuilder.Entity<Quest>();
        q.HasKey(x => x.QuestId);
        q.Property(x => x.Title).HasMaxLength(180).IsRequired();
        q.Property(x => x.Summary).HasMaxLength(4000);
        q.HasIndex(x => new { x.CampaignId, x.Title });

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
    }
}
