using Microsoft.EntityFrameworkCore;
using RuleForge.Domain.Characters;

namespace RuleForge.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Character> Characters => Set<Character>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var c = modelBuilder.Entity<Character>();
        c.HasKey(x => x.CharacterId);
        c.Property(x => x.Name).HasMaxLength(120).IsRequired();
        c.HasIndex(x => new { x.CampaignId, x.Name });
    }
}
