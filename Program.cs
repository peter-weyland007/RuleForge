using Microsoft.EntityFrameworkCore;
using RuleForge.Contracts.Characters;
using RuleForge.Contracts.Campaigns;
using RuleForge.Contracts.Bestiary;
using RuleForge.Contracts.Encounters;
using RuleForge.Contracts.Parties;
using RuleForge.Data;
using RuleForge.Domain.Characters;
using RuleForge.Domain.Campaigns;
using RuleForge.Domain.Bestiary;
using RuleForge.Domain.Encounters;
using RuleForge.Domain.Common;
using RuleForge.Domain.Parties;
using MudBlazor.Services;
using RuleForge.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddHttpClient();

var configuredProvider = builder.Configuration["Database:Provider"]
    ?? Environment.GetEnvironmentVariable("RULEFORGE_DB_PROVIDER");
var dbProvider = string.IsNullOrWhiteSpace(configuredProvider)
    ? (builder.Environment.IsDevelopment() ? "sqlite" : "postgres")
    : configuredProvider.Trim().ToLowerInvariant();
var isSqliteProvider = dbProvider == "sqlite";
var destructiveInit = (builder.Configuration["Database:DestructiveInit"]
    ?? Environment.GetEnvironmentVariable("RULEFORGE_DB_DESTRUCTIVE")
    ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (isSqliteProvider)
    {
        var sqliteConn = builder.Configuration.GetConnectionString("Sqlite")
            ?? builder.Configuration["Database:Sqlite"]
            ?? Environment.GetEnvironmentVariable("RULEFORGE_SQLITE_PATH")
            ?? "Data Source=ruleforge.db";

        o.UseSqlite(sqliteConn);
    }
    else
    {
        var pgConn = builder.Configuration.GetConnectionString("Postgres")
            ?? builder.Configuration.GetConnectionString("DefaultConnection")
            ?? builder.Configuration["Database:ConnectionString"]
            ?? Environment.GetEnvironmentVariable("RULEFORGE_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=ruleforge;Username=ruleforge;Password=ruleforge";

        o.UseNpgsql(pgConn);
    }
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (destructiveInit)
    {
        db.Database.EnsureDeleted();
    }

    db.Database.EnsureCreated();

    if (isSqliteProvider)
    {
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS Campaigns (
            CampaignId INTEGER NOT NULL CONSTRAINT PK_Campaigns PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Description TEXT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Campaigns_Name ON Campaigns (Name);
    """);

    try
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE Campaigns ADD COLUMN Description TEXT NULL;");
    }
    catch { }

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS Creatures (
            CreatureId INTEGER NOT NULL CONSTRAINT PK_Creatures PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Description TEXT NULL,
            ArmorClass INTEGER NULL,
            HitPoints INTEGER NULL,
            InitiativeModifier INTEGER NULL,
            Speed TEXT NULL,
            ChallengeRating TEXT NULL,
            ExperiencePoints INTEGER NULL,
            PassivePerception INTEGER NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_Creatures_Name ON Creatures (Name);
    """);

    try { db.Database.ExecuteSqlRaw("ALTER TABLE Creatures ADD COLUMN ArmorClass INTEGER NULL;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Creatures ADD COLUMN HitPoints INTEGER NULL;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Creatures ADD COLUMN InitiativeModifier INTEGER NULL;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Creatures ADD COLUMN Speed TEXT NULL;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Creatures ADD COLUMN ChallengeRating TEXT NULL;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Creatures ADD COLUMN ExperiencePoints INTEGER NULL;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Characters ADD COLUMN PartyId INTEGER NOT NULL DEFAULT 0;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Characters ADD COLUMN PlayerName TEXT NULL;"); } catch { }

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS Parties (
            PartyId INTEGER NOT NULL CONSTRAINT PK_Parties PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Description TEXT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Parties ADD COLUMN CampaignId INTEGER NOT NULL DEFAULT 0;"); } catch { }
    try { db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_Parties_CampaignId_Name ON Parties (CampaignId, Name);"); } catch { }
    try { db.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS IX_Parties_Name;"); } catch { }

    try { db.Database.ExecuteSqlRaw("ALTER TABLE Creatures ADD COLUMN PassivePerception INTEGER NULL;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Creatures ADD COLUMN Strength INTEGER NULL;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Creatures ADD COLUMN Dexterity INTEGER NULL;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Creatures ADD COLUMN Constitution INTEGER NULL;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Creatures ADD COLUMN Intelligence INTEGER NULL;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Creatures ADD COLUMN Wisdom INTEGER NULL;"); } catch { }
    try { db.Database.ExecuteSqlRaw("ALTER TABLE Creatures ADD COLUMN Charisma INTEGER NULL;"); } catch { }

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS Encounters (
            EncounterId INTEGER NOT NULL CONSTRAINT PK_Encounters PRIMARY KEY AUTOINCREMENT,
            CampaignId INTEGER NOT NULL,
            Name TEXT NOT NULL,
            EncounterType INTEGER NOT NULL,
            Description TEXT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_Encounters_Name ON Encounters (Name);
    """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS EncounterParticipants (
            EncounterParticipantId INTEGER NOT NULL CONSTRAINT PK_EncounterParticipants PRIMARY KEY AUTOINCREMENT,
            EncounterId INTEGER NOT NULL,
            ParticipantType INTEGER NOT NULL,
            SourceId INTEGER NOT NULL,
            NameSnapshot TEXT NOT NULL,
            ArmorClassSnapshot INTEGER NULL,
            HitPointsCurrent INTEGER NULL,
            InitiativeModifierSnapshot INTEGER NULL,
            DateCreatedUtc TEXT NOT NULL,
            CONSTRAINT FK_EncounterParticipants_Encounters_EncounterId FOREIGN KEY (EncounterId) REFERENCES Encounters (EncounterId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_EncounterParticipants_EncounterId ON EncounterParticipants (EncounterId);
    """);
    }
}

app.MapGet("/api/characters", async (AppDbContext db) =>
{
    var rows = await db.Characters
        .Where(x => x.DateDeletedUtc == null)
        .OrderBy(x => x.Name)
        .Select(x => new CharacterResponse
        {
            CharacterId = x.CharacterId,
            CampaignId = x.CampaignId == 0 ? null : x.CampaignId,
            PartyId = x.PartyId == 0 ? null : x.PartyId,
            CharacterType = x.CharacterType,
            Name = x.Name,
            OwnerAppUserId = x.OwnerAppUserId,
            PlayerName = x.PlayerName,
            ArmorClass = x.ArmorClass,
            HitPointsCurrent = x.HitPointsCurrent,
            HitPointsMax = x.HitPointsMax,
            TempHitPoints = x.TempHitPoints,
            InitiativeModifier = x.InitiativeModifier,
            Speed = x.Speed,
            Strength = x.Strength,
            Dexterity = x.Dexterity,
            Constitution = x.Constitution,
            Intelligence = x.Intelligence,
            Wisdom = x.Wisdom,
            Charisma = x.Charisma,
            ProficiencyBonus = x.ProficiencyBonus,
            Level = x.Level,
            ClassName = x.ClassName,
            PassivePerception = x.PassivePerception,
            Conditions = x.Conditions,
            Notes = x.Notes,
            DateCreatedUtc = x.DateCreatedUtc,
            DateModifiedUtc = x.DateModifiedUtc
        })
        .ToListAsync();

    return Results.Ok(rows);
});

app.MapGet("/api/characters/{id:int}", async (int id, AppDbContext db) =>
{
    var row = await db.Characters
        .Where(x => x.CharacterId == id && x.DateDeletedUtc == null)
        .Select(x => new CharacterResponse
        {
            CharacterId = x.CharacterId,
            CampaignId = x.CampaignId == 0 ? null : x.CampaignId,
            PartyId = x.PartyId == 0 ? null : x.PartyId,
            CharacterType = x.CharacterType,
            Name = x.Name,
            OwnerAppUserId = x.OwnerAppUserId,
            PlayerName = x.PlayerName,
            ArmorClass = x.ArmorClass,
            HitPointsCurrent = x.HitPointsCurrent,
            HitPointsMax = x.HitPointsMax,
            TempHitPoints = x.TempHitPoints,
            InitiativeModifier = x.InitiativeModifier,
            Speed = x.Speed,
            Strength = x.Strength,
            Dexterity = x.Dexterity,
            Constitution = x.Constitution,
            Intelligence = x.Intelligence,
            Wisdom = x.Wisdom,
            Charisma = x.Charisma,
            ProficiencyBonus = x.ProficiencyBonus,
            Level = x.Level,
            ClassName = x.ClassName,
            PassivePerception = x.PassivePerception,
            Conditions = x.Conditions,
            Notes = x.Notes,
            DateCreatedUtc = x.DateCreatedUtc,
            DateModifiedUtc = x.DateModifiedUtc
        })
        .FirstOrDefaultAsync();

    return row is null ? Results.NotFound() : Results.Ok(row);
});

app.MapPost("/api/characters", async (UpsertCharacterRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name is required.");

    var row = new Character
    {
        CampaignId = req.CampaignId ?? 0,
        PartyId = req.PartyId ?? 0,
        CharacterType = req.CharacterType,
        Name = req.Name.Trim(),
        OwnerAppUserId = req.OwnerAppUserId,
        PlayerName = string.IsNullOrWhiteSpace(req.PlayerName) ? null : req.PlayerName.Trim(),
        ArmorClass = req.ArmorClass,
        HitPointsCurrent = req.HitPointsCurrent,
        HitPointsMax = req.HitPointsMax,
        TempHitPoints = req.TempHitPoints,
        InitiativeModifier = req.InitiativeModifier,
        Speed = req.Speed,
        Strength = req.Strength,
        Dexterity = req.Dexterity,
        Constitution = req.Constitution,
        Intelligence = req.Intelligence,
        Wisdom = req.Wisdom,
        Charisma = req.Charisma,
        ProficiencyBonus = req.ProficiencyBonus,
        Level = req.Level,
        ClassName = req.ClassName,
        PassivePerception = req.PassivePerception,
        Conditions = req.Conditions,
        Notes = req.Notes,
        DateCreatedUtc = DateTime.UtcNow,
        DateModifiedUtc = DateTime.UtcNow
    };

    db.Characters.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(new { row.CharacterId });
});

app.MapPut("/api/characters/{id:int}", async (int id, UpsertCharacterRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name is required.");

    var row = await db.Characters.FirstOrDefaultAsync(x => x.CharacterId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    row.CampaignId = req.CampaignId ?? 0;
    row.PartyId = req.PartyId ?? 0;
    row.CharacterType = req.CharacterType;
    row.Name = req.Name.Trim();
    row.OwnerAppUserId = req.OwnerAppUserId;
    row.PlayerName = string.IsNullOrWhiteSpace(req.PlayerName) ? null : req.PlayerName.Trim();
    row.ArmorClass = req.ArmorClass;
    row.HitPointsCurrent = req.HitPointsCurrent;
    row.HitPointsMax = req.HitPointsMax;
    row.TempHitPoints = req.TempHitPoints;
    row.InitiativeModifier = req.InitiativeModifier;
    row.Speed = req.Speed;
    row.Strength = req.Strength;
    row.Dexterity = req.Dexterity;
    row.Constitution = req.Constitution;
    row.Intelligence = req.Intelligence;
    row.Wisdom = req.Wisdom;
    row.Charisma = req.Charisma;
    row.ProficiencyBonus = req.ProficiencyBonus;
    row.Level = req.Level;
    row.ClassName = req.ClassName;
    row.PassivePerception = req.PassivePerception;
    row.Strength = req.Strength;
    row.Dexterity = req.Dexterity;
    row.Constitution = req.Constitution;
    row.Intelligence = req.Intelligence;
    row.Wisdom = req.Wisdom;
    row.Charisma = req.Charisma;
    row.Conditions = req.Conditions;
    row.Notes = req.Notes;
    row.DateModifiedUtc = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(new { row.CharacterId });
});

app.MapDelete("/api/characters/{id:int}", async (int id, AppDbContext db) =>
{
    var row = await db.Characters.FirstOrDefaultAsync(x => x.CharacterId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.NoContent();
});

app.MapGet("/api/campaigns", async (AppDbContext db) =>
{
    var rows = await db.Campaigns
        .Where(x => x.DateDeletedUtc == null)
        .OrderBy(x => x.Name)
        .Select(x => new CampaignResponse
        {
            CampaignId = x.CampaignId,
            Name = x.Name,
            Description = x.Description
        })
        .ToListAsync();

    return Results.Ok(rows);
});

app.MapGet("/api/campaigns/{id:int}", async (int id, AppDbContext db) =>
{
    var row = await db.Campaigns
        .Where(x => x.CampaignId == id && x.DateDeletedUtc == null)
        .Select(x => new CampaignResponse
        {
            CampaignId = x.CampaignId,
            Name = x.Name,
            Description = x.Description
        })
        .FirstOrDefaultAsync();

    return row is null ? Results.NotFound() : Results.Ok(row);
});

app.MapPost("/api/campaigns", async (UpsertCampaignRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Title is required.");

    var row = new Campaign
    {
        Name = TitleNormalization.ToPascalTitle(req.Name),
        Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
        DateCreatedUtc = DateTime.UtcNow,
        DateModifiedUtc = DateTime.UtcNow
    };

    db.Campaigns.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(new { row.CampaignId });
});

app.MapPut("/api/campaigns/{id:int}", async (int id, UpsertCampaignRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Title is required.");

    var row = await db.Campaigns.FirstOrDefaultAsync(x => x.CampaignId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    row.Name = TitleNormalization.ToPascalTitle(req.Name);
    row.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
    row.DateModifiedUtc = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(new { row.CampaignId });
});

app.MapDelete("/api/campaigns/{id:int}", async (int id, AppDbContext db) =>
{
    var row = await db.Campaigns.FirstOrDefaultAsync(x => x.CampaignId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.NoContent();
});

app.MapGet("/api/creatures", async (AppDbContext db) =>
{
    var rows = await db.Creatures
        .Where(x => x.DateDeletedUtc == null)
        .OrderBy(x => x.Name)
        .Select(x => new CreatureResponse
        {
            CreatureId = x.CreatureId,
            Name = x.Name,
            Description = x.Description,
            ArmorClass = x.ArmorClass,
            HitPoints = x.HitPoints,
            InitiativeModifier = x.InitiativeModifier,
            Speed = x.Speed,
            ChallengeRating = x.ChallengeRating,
            ExperiencePoints = x.ExperiencePoints,
            PassivePerception = x.PassivePerception,
            Strength = x.Strength,
            Dexterity = x.Dexterity,
            Constitution = x.Constitution,
            Intelligence = x.Intelligence,
            Wisdom = x.Wisdom,
            Charisma = x.Charisma
        })
        .ToListAsync();

    return Results.Ok(rows);
});

app.MapGet("/api/creatures/{id:int}", async (int id, AppDbContext db) =>
{
    var row = await db.Creatures
        .Where(x => x.CreatureId == id && x.DateDeletedUtc == null)
        .Select(x => new CreatureResponse
        {
            CreatureId = x.CreatureId,
            Name = x.Name,
            Description = x.Description,
            ArmorClass = x.ArmorClass,
            HitPoints = x.HitPoints,
            InitiativeModifier = x.InitiativeModifier,
            Speed = x.Speed,
            ChallengeRating = x.ChallengeRating,
            ExperiencePoints = x.ExperiencePoints,
            PassivePerception = x.PassivePerception,
            Strength = x.Strength,
            Dexterity = x.Dexterity,
            Constitution = x.Constitution,
            Intelligence = x.Intelligence,
            Wisdom = x.Wisdom,
            Charisma = x.Charisma
        })
        .FirstOrDefaultAsync();

    return row is null ? Results.NotFound() : Results.Ok(row);
});

app.MapPost("/api/creatures", async (UpsertCreatureRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Creature name is required.");

    var row = new Creature
    {
        Name = TitleNormalization.ToPascalTitle(req.Name),
        Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
        ArmorClass = req.ArmorClass,
        HitPoints = req.HitPoints,
        InitiativeModifier = req.InitiativeModifier,
        Speed = string.IsNullOrWhiteSpace(req.Speed) ? null : req.Speed.Trim(),
        ChallengeRating = string.IsNullOrWhiteSpace(req.ChallengeRating) ? null : req.ChallengeRating.Trim(),
        ExperiencePoints = req.ExperiencePoints,
        PassivePerception = req.PassivePerception,
        Strength = req.Strength,
        Dexterity = req.Dexterity,
        Constitution = req.Constitution,
        Intelligence = req.Intelligence,
        Wisdom = req.Wisdom,
        Charisma = req.Charisma,
        DateCreatedUtc = DateTime.UtcNow,
        DateModifiedUtc = DateTime.UtcNow
    };

    db.Creatures.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(new { row.CreatureId });
});

app.MapPut("/api/creatures/{id:int}", async (int id, UpsertCreatureRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Creature name is required.");

    var row = await db.Creatures.FirstOrDefaultAsync(x => x.CreatureId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    row.Name = TitleNormalization.ToPascalTitle(req.Name);
    row.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
    row.ArmorClass = req.ArmorClass;
    row.HitPoints = req.HitPoints;
    row.InitiativeModifier = req.InitiativeModifier;
    row.Speed = string.IsNullOrWhiteSpace(req.Speed) ? null : req.Speed.Trim();
    row.ChallengeRating = string.IsNullOrWhiteSpace(req.ChallengeRating) ? null : req.ChallengeRating.Trim();
    row.ExperiencePoints = req.ExperiencePoints;
    row.PassivePerception = req.PassivePerception;
    row.Strength = req.Strength;
    row.Dexterity = req.Dexterity;
    row.Constitution = req.Constitution;
    row.Intelligence = req.Intelligence;
    row.Wisdom = req.Wisdom;
    row.Charisma = req.Charisma;
    row.DateModifiedUtc = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(new { row.CreatureId });
});

app.MapDelete("/api/creatures/{id:int}", async (int id, AppDbContext db) =>
{
    var row = await db.Creatures.FirstOrDefaultAsync(x => x.CreatureId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.NoContent();
});


app.MapGet("/api/parties", async (AppDbContext db) =>
{
    var rows = await db.Parties
        .Where(x => x.DateDeletedUtc == null)
        .OrderBy(x => x.Name)
        .Select(x => new PartyResponse
        {
            PartyId = x.PartyId,
            Name = x.Name,
            Description = x.Description,
            CampaignId = x.CampaignId == 0 ? null : x.CampaignId,
            MemberCount = db.Characters.Count(c => c.DateDeletedUtc == null && c.PartyId == x.PartyId && c.CharacterType == CharacterType.PC)
        })
        .ToListAsync();
    return Results.Ok(rows);
});

app.MapGet("/api/parties/{id:int}", async (int id, AppDbContext db) =>
{
    var row = await db.Parties.Where(x => x.PartyId == id && x.DateDeletedUtc == null)
        .Select(x => new PartyResponse { PartyId = x.PartyId, Name = x.Name, Description = x.Description, CampaignId = x.CampaignId == 0 ? null : x.CampaignId, MemberCount = db.Characters.Count(c => c.DateDeletedUtc == null && c.PartyId == x.PartyId && c.CharacterType == CharacterType.PC) })
        .FirstOrDefaultAsync();
    return row is null ? Results.NotFound() : Results.Ok(row);
});

app.MapPost("/api/parties", async (UpsertPartyRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Party name is required.");
    var row = new Party { Name = TitleNormalization.ToPascalTitle(req.Name), Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(), CampaignId = req.CampaignId ?? 0, DateCreatedUtc = DateTime.UtcNow, DateModifiedUtc = DateTime.UtcNow };
    db.Parties.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(new { row.PartyId });
});

app.MapPut("/api/parties/{id:int}", async (int id, UpsertPartyRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Party name is required.");
    var row = await db.Parties.FirstOrDefaultAsync(x => x.PartyId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    row.Name = TitleNormalization.ToPascalTitle(req.Name);
    row.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
    row.CampaignId = req.CampaignId ?? 0;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { row.PartyId });
});

app.MapDelete("/api/parties/{id:int}", async (int id, AppDbContext db) =>
{
    var row = await db.Parties.FirstOrDefaultAsync(x => x.PartyId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.NoContent();
});


app.MapGet("/api/parties/{id:int}/members", async (int id, AppDbContext db) =>
{
    var rows = await db.Characters
        .Where(x => x.DateDeletedUtc == null && x.CharacterType == CharacterType.PC && x.PartyId == id)
        .OrderBy(x => x.Name)
        .Select(x => new
        {
            x.CharacterId,
            x.Name,
            x.ClassName,
            x.Level,
            x.ArmorClass,
            x.HitPointsCurrent,
            x.HitPointsMax
        })
        .ToListAsync();

    return Results.Ok(rows);
});

app.MapGet("/api/encounters/options", async (AppDbContext db) =>
{
    var campaigns = await db.Campaigns.Where(x => x.DateDeletedUtc == null)
        .OrderBy(x => x.Name)
        .Select(x => new EncounterOptionItem { Id = x.CampaignId, Name = x.Name })
        .ToListAsync();

    var parties = await db.Parties.Where(x => x.DateDeletedUtc == null)
        .OrderBy(x => x.Name)
        .Select(x => new EncounterOptionItem { Id = x.PartyId, Name = x.Name })
        .ToListAsync();

    var characters = await db.Characters.Where(x => x.DateDeletedUtc == null)
        .OrderBy(x => x.Name)
        .Select(x => new EncounterOptionItem
        {
            Id = x.CharacterId,
            Name = x.Name,
            ArmorClass = x.ArmorClass,
            HitPoints = x.HitPointsCurrent ?? x.HitPointsMax,
            InitiativeModifier = x.InitiativeModifier,
            ParticipantType = (int)x.CharacterType,
            PartyId = x.PartyId == 0 ? null : x.PartyId
        })
        .ToListAsync();

    var creatures = await db.Creatures.Where(x => x.DateDeletedUtc == null)
        .OrderBy(x => x.Name)
        .Select(x => new EncounterOptionItem
        {
            Id = x.CreatureId,
            Name = x.Name,
            ArmorClass = x.ArmorClass,
            HitPoints = x.HitPoints,
            InitiativeModifier = x.InitiativeModifier,
            ParticipantType = 3
        })
        .ToListAsync();

    return Results.Ok(new EncounterOptionsResponse { Campaigns = campaigns, Parties = parties, Characters = characters, Creatures = creatures });
});

app.MapGet("/api/encounters", async (AppDbContext db) =>
{
    var rows = await db.Encounters
        .Include(x => x.Participants)
        .Where(x => x.DateDeletedUtc == null)
        .OrderBy(x => x.Name)
        .Select(x => new EncounterResponse
        {
            EncounterId = x.EncounterId,
            CampaignId = x.CampaignId == 0 ? null : x.CampaignId,
            Name = x.Name,
            EncounterType = (int)x.EncounterType,
            Description = x.Description,
            Participants = x.Participants.Select(p => new EncounterParticipantResponse
            {
                EncounterParticipantId = p.EncounterParticipantId,
                ParticipantType = (int)p.ParticipantType,
                SourceId = p.SourceId,
                NameSnapshot = p.NameSnapshot,
                ArmorClassSnapshot = p.ArmorClassSnapshot,
                HitPointsCurrent = p.HitPointsCurrent,
                InitiativeModifierSnapshot = p.InitiativeModifierSnapshot
            }).ToList()
        })
        .ToListAsync();

    return Results.Ok(rows);
});

app.MapGet("/api/encounters/{id:int}", async (int id, AppDbContext db) =>
{
    var row = await db.Encounters
        .Include(x => x.Participants)
        .Where(x => x.EncounterId == id && x.DateDeletedUtc == null)
        .Select(x => new EncounterResponse
        {
            EncounterId = x.EncounterId,
            CampaignId = x.CampaignId == 0 ? null : x.CampaignId,
            Name = x.Name,
            EncounterType = (int)x.EncounterType,
            Description = x.Description,
            Participants = x.Participants.Select(p => new EncounterParticipantResponse
            {
                EncounterParticipantId = p.EncounterParticipantId,
                ParticipantType = (int)p.ParticipantType,
                SourceId = p.SourceId,
                NameSnapshot = p.NameSnapshot,
                ArmorClassSnapshot = p.ArmorClassSnapshot,
                HitPointsCurrent = p.HitPointsCurrent,
                InitiativeModifierSnapshot = p.InitiativeModifierSnapshot
            }).ToList()
        })
        .FirstOrDefaultAsync();

    return row is null ? Results.NotFound() : Results.Ok(row);
});

app.MapPost("/api/encounters", async (UpsertEncounterRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Encounter name is required.");

    var row = new Encounter
    {
        CampaignId = req.CampaignId ?? 0,
        Name = TitleNormalization.ToPascalTitle(req.Name),
        EncounterType = Enum.IsDefined(typeof(EncounterType), req.EncounterType) ? (EncounterType)req.EncounterType : EncounterType.Combat,
        Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
        DateCreatedUtc = DateTime.UtcNow,
        DateModifiedUtc = DateTime.UtcNow,
        Participants = req.Participants.Select(p => new EncounterParticipant
        {
            ParticipantType = Enum.IsDefined(typeof(EncounterParticipantType), p.ParticipantType) ? (EncounterParticipantType)p.ParticipantType : EncounterParticipantType.Creature,
            SourceId = p.SourceId,
            NameSnapshot = p.NameSnapshot,
            ArmorClassSnapshot = p.ArmorClassSnapshot,
            HitPointsCurrent = p.HitPointsCurrent,
            InitiativeModifierSnapshot = p.InitiativeModifierSnapshot,
            DateCreatedUtc = DateTime.UtcNow
        }).ToList()
    };

    db.Encounters.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(new { row.EncounterId });
});

app.MapPut("/api/encounters/{id:int}", async (int id, UpsertEncounterRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Encounter name is required.");

    var row = await db.Encounters.Include(x => x.Participants).FirstOrDefaultAsync(x => x.EncounterId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    row.CampaignId = req.CampaignId ?? 0;
    row.Name = TitleNormalization.ToPascalTitle(req.Name);
    row.EncounterType = Enum.IsDefined(typeof(EncounterType), req.EncounterType) ? (EncounterType)req.EncounterType : EncounterType.Combat;
    row.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
    row.CampaignId = req.CampaignId ?? 0;
    row.DateModifiedUtc = DateTime.UtcNow;

    db.EncounterParticipants.RemoveRange(row.Participants);
    row.Participants = req.Participants.Select(p => new EncounterParticipant
    {
        EncounterId = row.EncounterId,
        ParticipantType = Enum.IsDefined(typeof(EncounterParticipantType), p.ParticipantType) ? (EncounterParticipantType)p.ParticipantType : EncounterParticipantType.Creature,
        SourceId = p.SourceId,
        NameSnapshot = p.NameSnapshot,
        ArmorClassSnapshot = p.ArmorClassSnapshot,
        HitPointsCurrent = p.HitPointsCurrent,
        InitiativeModifierSnapshot = p.InitiativeModifierSnapshot,
        DateCreatedUtc = DateTime.UtcNow
    }).ToList();

    await db.SaveChangesAsync();
    return Results.Ok(new { row.EncounterId });
});

app.MapDelete("/api/encounters/{id:int}", async (int id, AppDbContext db) =>
{
    var row = await db.Encounters.FirstOrDefaultAsync(x => x.EncounterId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
