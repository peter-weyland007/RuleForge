using Microsoft.EntityFrameworkCore;
using RuleForge.Domain.Characters;
using RuleForge.Domain.Campaigns;
using RuleForge.Domain.Bestiary;
using RuleForge.Domain.Encounters;
using RuleForge.Domain.Parties;

namespace RuleForge.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Creature> Creatures => Set<Creature>();
    public DbSet<Encounter> Encounters => Set<Encounter>();
    public DbSet<EncounterParticipant> EncounterParticipants => Set<EncounterParticipant>();
    public DbSet<Party> Parties => Set<Party>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var c = modelBuilder.Entity<Character>();
        c.HasKey(x => x.CharacterId);
        c.Property(x => x.Name).HasMaxLength(120).IsRequired();
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
    }
}
