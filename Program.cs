using MudBlazor.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RuleForge.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ruleforge");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "ruleforge.db");

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite($"Data Source={dbPath}"));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "RuleForge API v1");
    c.RoutePrefix = "swagger";
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS GameSystems (
            GameSystemId INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Slug TEXT NOT NULL,
            Alias TEXT NULL,
            Description TEXT NULL,
            SourceType INTEGER NOT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);

    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE GameSystems ADD COLUMN SourceType INTEGER NOT NULL DEFAULT 1;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE GameSystems ADD COLUMN Slug TEXT NOT NULL DEFAULT '';"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE GameSystems ADD COLUMN Alias TEXT NULL;"); } catch (SqliteException) { }

    // Best-effort unique index for active rows.
    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_GameSystems_Slug_Unique_Active ON GameSystems (Slug) WHERE DateDeletedUtc IS NULL;"); } catch (SqliteException) { }

    // Backfill blank slugs deterministically.
    var missingSlugs = await db.GameSystems
        .Where(gs => gs.DateDeletedUtc == null && (gs.Slug == null || gs.Slug == ""))
        .OrderBy(gs => gs.GameSystemId)
        .ToListAsync();

    foreach (var gs in missingSlugs)
    {
        var baseSlug = Slugify(gs.Name);
        gs.Slug = await GenerateUniqueSlugAsync(db, baseSlug, gs.GameSystemId);
        gs.DateModifiedUtc = DateTime.UtcNow;
    }

    if (missingSlugs.Count > 0)
        await db.SaveChangesAsync();

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS ItemTypeDefinitions (
            ItemTypeDefinitionId INTEGER PRIMARY KEY AUTOINCREMENT,
            GameSystemId INTEGER NOT NULL,
            Name TEXT NOT NULL,
            Slug TEXT NOT NULL,
            Description TEXT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS Items (
            ItemId INTEGER PRIMARY KEY AUTOINCREMENT,
            GameSystemId INTEGER NOT NULL,
            Name TEXT NOT NULL,
            Slug TEXT NOT NULL,
            Alias TEXT NULL,
            ItemTypeDefinitionId INTEGER NULL,
            Description TEXT NULL,
            Rarity TEXT NULL,
            CostAmount REAL NULL,
            CostCurrency TEXT NULL,
            Weight REAL NULL,
            Quantity INTEGER NOT NULL DEFAULT 1,
            Tags TEXT NULL,
            Effect TEXT NULL,
            RequiresAttunement INTEGER NOT NULL DEFAULT 0,
            AttunementRequirement TEXT NULL,
            SourceType INTEGER NOT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);

    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_ItemTypeDefinitions_System_Slug_Unique_Active ON ItemTypeDefinitions (GameSystemId, Slug) WHERE DateDeletedUtc IS NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Items_System_Slug_Unique_Active ON Items (GameSystemId, Slug) WHERE DateDeletedUtc IS NULL;"); } catch (SqliteException) { }

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS RarityDefinitions (
            RarityDefinitionId INTEGER PRIMARY KEY AUTOINCREMENT,
            GameSystemId INTEGER NOT NULL,
            Name TEXT NOT NULL,
            Slug TEXT NOT NULL,
            Description TEXT NULL,
            SortOrder INTEGER NOT NULL DEFAULT 0,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);

    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_RarityDefinitions_System_Slug_Unique_Active ON RarityDefinitions (GameSystemId, Slug) WHERE DateDeletedUtc IS NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN RarityDefinitionId INTEGER NULL;"); } catch (SqliteException) { }

    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN CostAmount REAL NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN CostCurrency TEXT NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN Weight REAL NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN Quantity INTEGER NOT NULL DEFAULT 1;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN Tags TEXT NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN Effect TEXT NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN RequiresAttunement INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN AttunementRequirement TEXT NULL;"); } catch (SqliteException) { }

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS CurrencyDefinitions (
            CurrencyDefinitionId INTEGER PRIMARY KEY AUTOINCREMENT,
            GameSystemId INTEGER NOT NULL,
            Name TEXT NOT NULL,
            Code TEXT NOT NULL,
            Symbol TEXT NULL,
            Description TEXT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);
    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_CurrencyDefinitions_System_Code_Unique_Active ON CurrencyDefinitions (GameSystemId, Code) WHERE DateDeletedUtc IS NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN CurrencyDefinitionId INTEGER NULL;"); } catch (SqliteException) { }

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS TagDefinitions (
            TagDefinitionId INTEGER PRIMARY KEY AUTOINCREMENT,
            GameSystemId INTEGER NOT NULL,
            Name TEXT NOT NULL,
            Slug TEXT NOT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS ItemTags (
            ItemTagId INTEGER PRIMARY KEY AUTOINCREMENT,
            ItemId INTEGER NOT NULL,
            TagDefinitionId INTEGER NOT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);

    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_TagDefinitions_System_Slug_Unique_Active ON TagDefinitions (GameSystemId, Slug) WHERE DateDeletedUtc IS NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_ItemTags_Item_Tag_Unique_Active ON ItemTags (ItemId, TagDefinitionId) WHERE DateDeletedUtc IS NULL;"); } catch (SqliteException) { }

    await SeedStarterDataAsync(db);

}

var api = app.MapGroup("/api").WithTags("Core");

api.MapGet("/health", () => Results.Ok(new { ok = true, service = "RuleForge", utc = DateTime.UtcNow }));

api.MapGet("/notes", async (AppDbContext db) =>
    await db.Notes.Where(n => n.DateDeletedUtc == null).OrderBy(n => n.NoteId).ToListAsync())
    .WithTags("Notes");

api.MapPost("/notes", async (CreateNoteRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Title))
        return Results.BadRequest("Title is required.");

    var now = DateTime.UtcNow;
    var note = new Note
    {
        Title = req.Title.Trim(),
        Body = req.Body,
        DateCreatedUtc = now,
        DateModifiedUtc = now
    };

    db.Notes.Add(note);
    await db.SaveChangesAsync();
    return Results.Ok(note);
}).WithTags("Notes");

api.MapGet("/game-systems", async (AppDbContext db) =>
    await db.GameSystems
        .Where(gs => gs.DateDeletedUtc == null)
        .OrderBy(gs => gs.Name)
        .Select(gs => new
        {
            gs.GameSystemId,
            gs.Name,
            gs.Slug,
            Alias = string.IsNullOrWhiteSpace(gs.Alias) ? null : gs.Alias,
            gs.Description,
            gs.SourceType,
            gs.DateCreatedUtc,
            gs.DateModifiedUtc,
            gs.DateDeletedUtc
        })
        .ToListAsync())
    .WithTags("Game Systems");

api.MapGet("/game-systems/{gameSystemId:int}", async (int gameSystemId, AppDbContext db) =>
{
    var row = await db.GameSystems.FirstOrDefaultAsync(gs => gs.GameSystemId == gameSystemId && gs.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    return Results.Ok(new
    {
        row.GameSystemId,
        row.Name,
        row.Slug,
        Alias = string.IsNullOrWhiteSpace(row.Alias) ? null : row.Alias,
        row.Description,
        row.SourceType,
        row.DateCreatedUtc,
        row.DateModifiedUtc,
        row.DateDeletedUtc
    });
}).WithTags("Game Systems");

api.MapPost("/game-systems", async (UpsertGameSystemRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest("Name is required.");

    var now = DateTime.UtcNow;
    var name = ToTitleCase(req.Name);
    var slug = await GenerateUniqueSlugAsync(db, Slugify(name));

    var row = new GameSystem
    {
        Name = name,
        Slug = slug,
        Alias = string.IsNullOrWhiteSpace(req.Alias) ? string.Empty : req.Alias.Trim(),
        Description = req.Description,
        SourceType = req.SourceType,
        DateCreatedUtc = now,
        DateModifiedUtc = now
    };

    db.GameSystems.Add(row);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        row.GameSystemId,
        row.Name,
        row.Slug,
        Alias = string.IsNullOrWhiteSpace(row.Alias) ? null : row.Alias,
        row.Description,
        row.SourceType,
        row.DateCreatedUtc,
        row.DateModifiedUtc,
        row.DateDeletedUtc
    });
}).WithTags("Game Systems");

api.MapPut("/game-systems/{gameSystemId:int}", async (int gameSystemId, UpsertGameSystemRequest req, AppDbContext db) =>
{
    var row = await db.GameSystems.FirstOrDefaultAsync(gs => gs.GameSystemId == gameSystemId && gs.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name is required.");

    var name = ToTitleCase(req.Name);
    var slug = await GenerateUniqueSlugAsync(db, Slugify(name), gameSystemId);

    row.Name = name;
    row.Slug = slug;
    row.Alias = string.IsNullOrWhiteSpace(req.Alias) ? string.Empty : req.Alias.Trim();
    row.Description = req.Description;
    row.SourceType = req.SourceType;
    row.DateModifiedUtc = DateTime.UtcNow;

    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        row.GameSystemId,
        row.Name,
        row.Slug,
        Alias = string.IsNullOrWhiteSpace(row.Alias) ? null : row.Alias,
        row.Description,
        row.SourceType,
        row.DateCreatedUtc,
        row.DateModifiedUtc,
        row.DateDeletedUtc
    });
}).WithTags("Game Systems");





api.MapGet("/item-types", async (int gameSystemId, AppDbContext db) =>
    await db.ItemTypeDefinitions
        .Where(x => x.DateDeletedUtc == null && x.GameSystemId == gameSystemId)
        .OrderBy(x => x.Name)
        .ToListAsync())
    .WithTags("Item Types");

api.MapPost("/item-types", async (CreateItemTypeRequest req, AppDbContext db) =>
{
    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name is required.");

    var now = DateTime.UtcNow;
    var slug = await GenerateUniqueItemTypeSlugAsync(db, req.GameSystemId, Slugify(req.Name));
    var row = new ItemTypeDefinition
    {
        GameSystemId = req.GameSystemId,
        Name = ToTitleCase(req.Name),
        Slug = slug,
        Description = req.Description,
        DateCreatedUtc = now,
        DateModifiedUtc = now
    };
    db.ItemTypeDefinitions.Add(row);
    await db.SaveChangesAsync();

    return Results.Ok(row);
}).WithTags("Item Types");



api.MapGet("/admin/item-types", async (int gameSystemId, AppDbContext db) =>
    await db.ItemTypeDefinitions
        .Where(x => x.DateDeletedUtc == null && x.GameSystemId == gameSystemId)
        .OrderBy(x => x.Name)
        .ToListAsync())
    .WithTags("Admin");

api.MapGet("/admin/item-types/{itemTypeDefinitionId:int}", async (int itemTypeDefinitionId, AppDbContext db) =>
{
    var row = await db.ItemTypeDefinitions.FirstOrDefaultAsync(x => x.ItemTypeDefinitionId == itemTypeDefinitionId && x.DateDeletedUtc == null);
    return row is null ? Results.NotFound() : Results.Ok(row);
}).WithTags("Admin");

api.MapPost("/admin/item-types", async (UpsertItemTypeRequest req, AppDbContext db) =>
{
    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name is required.");

    var now = DateTime.UtcNow;
    var slug = await GenerateUniqueItemTypeSlugAsync(db, req.GameSystemId, Slugify(req.Name));
    var row = new ItemTypeDefinition
    {
        GameSystemId = req.GameSystemId,
        Name = ToTitleCase(req.Name),
        Slug = slug,
        Description = req.Description,
        DateCreatedUtc = now,
        DateModifiedUtc = now
    };
    db.ItemTypeDefinitions.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(row);
}).WithTags("Admin");

api.MapPut("/admin/item-types/{itemTypeDefinitionId:int}", async (int itemTypeDefinitionId, UpsertItemTypeRequest req, AppDbContext db) =>
{
    var row = await db.ItemTypeDefinitions.FirstOrDefaultAsync(x => x.ItemTypeDefinitionId == itemTypeDefinitionId && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name is required.");

    row.GameSystemId = req.GameSystemId;
    row.Name = ToTitleCase(req.Name);
    row.Slug = await GenerateUniqueItemTypeSlugAsync(db, req.GameSystemId, Slugify(req.Name));
    row.Description = req.Description;
    row.DateModifiedUtc = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(row);
}).WithTags("Admin");

api.MapDelete("/admin/item-types/{itemTypeDefinitionId:int}", async (int itemTypeDefinitionId, AppDbContext db) =>
{
    var row = await db.ItemTypeDefinitions.FirstOrDefaultAsync(x => x.ItemTypeDefinitionId == itemTypeDefinitionId && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok();
}).WithTags("Admin");



api.MapGet("/rarities", async (int gameSystemId, AppDbContext db) =>
    await db.RarityDefinitions
        .Where(x => x.DateDeletedUtc == null && x.GameSystemId == gameSystemId)
        .OrderBy(x => x.Name)
        .ToListAsync())
    .WithTags("Rarities");

api.MapGet("/admin/rarities", async (int gameSystemId, AppDbContext db) =>
    await db.RarityDefinitions
        .Where(x => x.DateDeletedUtc == null && x.GameSystemId == gameSystemId)
        .OrderBy(x => x.Name)
        .ToListAsync())
    .WithTags("Admin");

api.MapGet("/admin/rarities/{rarityDefinitionId:int}", async (int rarityDefinitionId, AppDbContext db) =>
{
    var row = await db.RarityDefinitions.FirstOrDefaultAsync(x => x.RarityDefinitionId == rarityDefinitionId && x.DateDeletedUtc == null);
    return row is null ? Results.NotFound() : Results.Ok(row);
}).WithTags("Admin");

api.MapPost("/admin/rarities", async (UpsertRarityRequest req, AppDbContext db) =>
{
    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name is required.");

    var now = DateTime.UtcNow;
    var slug = await GenerateUniqueRaritySlugAsync(db, req.GameSystemId, Slugify(req.Name));
    var row = new RarityDefinition
    {
        GameSystemId = req.GameSystemId,
        Name = ToTitleCase(req.Name),
        Slug = slug,
        Description = req.Description,
        DateCreatedUtc = now,
        DateModifiedUtc = now
    };
    db.RarityDefinitions.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(row);
}).WithTags("Admin");

api.MapPut("/admin/rarities/{rarityDefinitionId:int}", async (int rarityDefinitionId, UpsertRarityRequest req, AppDbContext db) =>
{
    var row = await db.RarityDefinitions.FirstOrDefaultAsync(x => x.RarityDefinitionId == rarityDefinitionId && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name is required.");

    row.GameSystemId = req.GameSystemId;
    row.Name = ToTitleCase(req.Name);
    row.Slug = await GenerateUniqueRaritySlugAsync(db, req.GameSystemId, Slugify(req.Name));
    row.Description = req.Description;
    row.DateModifiedUtc = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(row);
}).WithTags("Admin");

api.MapDelete("/admin/rarities/{rarityDefinitionId:int}", async (int rarityDefinitionId, AppDbContext db) =>
{
    var row = await db.RarityDefinitions.FirstOrDefaultAsync(x => x.RarityDefinitionId == rarityDefinitionId && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok();
}).WithTags("Admin");

api.MapGet("/items/{itemId:int}", async (int itemId, AppDbContext db) =>
{
    var row = await db.Items.FirstOrDefaultAsync(x => x.ItemId == itemId && x.DateDeletedUtc == null);
    return row is null ? Results.NotFound() : Results.Ok(row);
}).WithTags("Items");

api.MapGet("/items", async (int gameSystemId, AppDbContext db) =>
{
    var items = await db.Items.Where(x => x.DateDeletedUtc == null && x.GameSystemId == gameSystemId).OrderBy(x => x.Name).ToListAsync();
    var itemIds = items.Select(i => i.ItemId).ToList();
    var tagLinks = await db.ItemTags.Where(it => it.DateDeletedUtc == null && itemIds.Contains(it.ItemId)).ToListAsync();
    var tagIds = tagLinks.Select(t => t.TagDefinitionId).Distinct().ToList();
    var tags = await db.TagDefinitions.Where(t => t.DateDeletedUtc == null && tagIds.Contains(t.TagDefinitionId)).ToListAsync();

    var outRows = items.Select(i => new {
        i.ItemId,i.GameSystemId,i.Name,i.Slug,i.Alias,i.ItemTypeDefinitionId,i.RarityDefinitionId,i.Description,
        i.CostAmount,i.CurrencyDefinitionId,i.CostCurrency,i.Weight,i.Quantity,i.Tags,i.SourceType,i.DateCreatedUtc,i.DateModifiedUtc,i.DateDeletedUtc,
        TagDefinitionIds = tagLinks.Where(l=>l.ItemId==i.ItemId).Select(l=>l.TagDefinitionId).ToList(),
        TagNames = tags.Where(t=>tagLinks.Any(l=>l.ItemId==i.ItemId && l.TagDefinitionId==t.TagDefinitionId)).Select(t=>t.Name).ToList()
    }).ToList();

    return Results.Ok(outRows);
}).WithTags("Items");

api.MapPost("/items", async (CreateItemRequest req, AppDbContext db) =>
{
    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name is required.");

    if (req.ItemTypeDefinitionId.HasValue)
    {
        var typeExists = await db.ItemTypeDefinitions.AnyAsync(t => t.ItemTypeDefinitionId == req.ItemTypeDefinitionId.Value && t.GameSystemId == req.GameSystemId && t.DateDeletedUtc == null);
        if (!typeExists) return Results.BadRequest("ItemTypeDefinitionId is invalid for this system.");
    }

    if (req.RarityDefinitionId.HasValue)
    {
        var rarityExists = await db.RarityDefinitions.AnyAsync(r => r.RarityDefinitionId == req.RarityDefinitionId.Value && r.GameSystemId == req.GameSystemId && r.DateDeletedUtc == null);
        if (!rarityExists) return Results.BadRequest("RarityDefinitionId is invalid for this system.");
    }

    if (req.CurrencyDefinitionId.HasValue)
    {
        var currencyExists = await db.CurrencyDefinitions.AnyAsync(c => c.CurrencyDefinitionId == req.CurrencyDefinitionId.Value && c.GameSystemId == req.GameSystemId && c.DateDeletedUtc == null);
        if (!currencyExists) return Results.BadRequest("CurrencyDefinitionId is invalid for this system.");
    }

    var now = DateTime.UtcNow;
    var slug = await GenerateUniqueItemSlugAsync(db, req.GameSystemId, Slugify(req.Name));
    var row = new Item
    {
        GameSystemId = req.GameSystemId,
        Name = ToTitleCase(req.Name),
        Slug = slug,
        Alias = string.IsNullOrWhiteSpace(req.Alias) ? string.Empty : req.Alias.Trim(),
        ItemTypeDefinitionId = req.ItemTypeDefinitionId,
        Description = req.Description,
        RarityDefinitionId = req.RarityDefinitionId,
        CostAmount = req.CostAmount,
        CurrencyDefinitionId = req.CurrencyDefinitionId,
        CostCurrency = string.IsNullOrWhiteSpace(req.CostCurrency) ? null : req.CostCurrency.Trim().ToUpperInvariant(),
        Weight = req.Weight,
        Quantity = req.Quantity <= 0 ? 1 : req.Quantity,
        Tags = string.IsNullOrWhiteSpace(req.Tags) ? null : req.Tags.Trim(),
        Effect = string.IsNullOrWhiteSpace(req.Effect) ? null : req.Effect.Trim(),
        RequiresAttunement = req.RequiresAttunement,
        AttunementRequirement = string.IsNullOrWhiteSpace(req.AttunementRequirement) ? null : req.AttunementRequirement.Trim(),
        SourceType = req.SourceType,
        DateCreatedUtc = now,
        DateModifiedUtc = now
    };
    db.Items.Add(row);
    await db.SaveChangesAsync();

    if (req.TagDefinitionIds is { Count: > 0 })
    {
        var validTagIds = await db.TagDefinitions.Where(t=>t.DateDeletedUtc==null && t.GameSystemId==req.GameSystemId && req.TagDefinitionIds.Contains(t.TagDefinitionId)).Select(t=>t.TagDefinitionId).ToListAsync();
        foreach (var tagId in validTagIds.Distinct())
            db.ItemTags.Add(new ItemTag{ ItemId=row.ItemId, TagDefinitionId=tagId, DateCreatedUtc=DateTime.UtcNow});
        await db.SaveChangesAsync();
    }

    return Results.Ok(row);
}).WithTags("Items");

api.MapPut("/items/{itemId:int}", async (int itemId, CreateItemRequest req, AppDbContext db) =>
{
    var row = await db.Items.FirstOrDefaultAsync(x => x.ItemId == itemId && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name is required.");

    if (req.ItemTypeDefinitionId.HasValue)
    {
        var typeExists = await db.ItemTypeDefinitions.AnyAsync(t => t.ItemTypeDefinitionId == req.ItemTypeDefinitionId.Value && t.GameSystemId == req.GameSystemId && t.DateDeletedUtc == null);
        if (!typeExists) return Results.BadRequest("ItemTypeDefinitionId is invalid for this system.");
    }

    if (req.RarityDefinitionId.HasValue)
    {
        var rarityExists = await db.RarityDefinitions.AnyAsync(r => r.RarityDefinitionId == req.RarityDefinitionId.Value && r.GameSystemId == req.GameSystemId && r.DateDeletedUtc == null);
        if (!rarityExists) return Results.BadRequest("RarityDefinitionId is invalid for this system.");
    }

    var newName = ToTitleCase(req.Name);
    var nameChanged = !string.Equals(row.Name, newName, StringComparison.Ordinal);

    row.GameSystemId = req.GameSystemId;
    row.Name = newName;
    if (nameChanged)
    {
        row.Slug = await GenerateUniqueItemSlugAsync(db, req.GameSystemId, Slugify(req.Name));
    }
    row.Alias = string.IsNullOrWhiteSpace(req.Alias) ? string.Empty : req.Alias.Trim();
    row.ItemTypeDefinitionId = req.ItemTypeDefinitionId;
    row.RarityDefinitionId = req.RarityDefinitionId;
    row.Description = req.Description;
    row.CostAmount = req.CostAmount;
    row.CurrencyDefinitionId = req.CurrencyDefinitionId;
    row.CostCurrency = string.IsNullOrWhiteSpace(req.CostCurrency) ? null : req.CostCurrency.Trim().ToUpperInvariant();
    row.Weight = req.Weight;
    row.Quantity = req.Quantity <= 0 ? 1 : req.Quantity;
    row.Tags = string.IsNullOrWhiteSpace(req.Tags) ? null : req.Tags.Trim();
    row.Effect = string.IsNullOrWhiteSpace(req.Effect) ? null : req.Effect.Trim();
    row.RequiresAttunement = req.RequiresAttunement;
    row.AttunementRequirement = string.IsNullOrWhiteSpace(req.AttunementRequirement) ? null : req.AttunementRequirement.Trim();
    row.SourceType = req.SourceType;
    row.DateModifiedUtc = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(row);
}).WithTags("Items");



api.MapDelete("/items/{itemId:int}", async (int itemId, AppDbContext db) =>
{
    var row = await db.Items.FirstOrDefaultAsync(x => x.ItemId == itemId && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok();
}).WithTags("Items");

api.MapDelete("/game-systems/{gameSystemId:int}", async (int gameSystemId, AppDbContext db) =>
{
    var row = await db.GameSystems.FirstOrDefaultAsync(gs => gs.GameSystemId == gameSystemId && gs.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var hasChildren = await db.ItemTypeDefinitions.AnyAsync(x => x.GameSystemId == gameSystemId && x.DateDeletedUtc == null)
        || await db.RarityDefinitions.AnyAsync(x => x.GameSystemId == gameSystemId && x.DateDeletedUtc == null)
        || await db.Items.AnyAsync(x => x.GameSystemId == gameSystemId && x.DateDeletedUtc == null);

    if (hasChildren)
        return Results.BadRequest("Cannot delete Game System with dependent records. Use merge/reassign first.");

    var now = DateTime.UtcNow;
    row.DateDeletedUtc = now;
    row.DateModifiedUtc = now;
    await db.SaveChangesAsync();

    return Results.Ok();
}).WithTags("Game Systems");




api.MapPost("/admin/game-systems/merge", async (MergeGameSystemsRequest req, AppDbContext db) =>
{
    if (req.FromGameSystemId == req.ToGameSystemId)
        return Results.BadRequest("From and To systems must be different.");

    var from = await db.GameSystems.FirstOrDefaultAsync(x => x.GameSystemId == req.FromGameSystemId && x.DateDeletedUtc == null);
    var to = await db.GameSystems.FirstOrDefaultAsync(x => x.GameSystemId == req.ToGameSystemId && x.DateDeletedUtc == null);
    if (from is null || to is null) return Results.BadRequest("Invalid source/target game system.");

    await db.Database.ExecuteSqlRawAsync("UPDATE ItemTypeDefinitions SET GameSystemId = {0}, DateModifiedUtc = {1} WHERE GameSystemId = {2} AND DateDeletedUtc IS NULL;", req.ToGameSystemId, DateTime.UtcNow, req.FromGameSystemId);
    await db.Database.ExecuteSqlRawAsync("UPDATE RarityDefinitions SET GameSystemId = {0}, DateModifiedUtc = {1} WHERE GameSystemId = {2} AND DateDeletedUtc IS NULL;", req.ToGameSystemId, DateTime.UtcNow, req.FromGameSystemId);
    await db.Database.ExecuteSqlRawAsync("UPDATE Items SET GameSystemId = {0}, DateModifiedUtc = {1} WHERE GameSystemId = {2} AND DateDeletedUtc IS NULL;", req.ToGameSystemId, DateTime.UtcNow, req.FromGameSystemId);

    from.DateDeletedUtc = DateTime.UtcNow;
    from.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(new { merged = true, from = req.FromGameSystemId, to = req.ToGameSystemId });
}).WithTags("Admin");







api.MapGet("/tags", async (int gameSystemId, AppDbContext db) =>
    await db.TagDefinitions.Where(t => t.DateDeletedUtc == null && t.GameSystemId == gameSystemId).OrderBy(t => t.Name).ToListAsync())
    .WithTags("Tags");

api.MapGet("/admin/tags", async (int gameSystemId, AppDbContext db) =>
    await db.TagDefinitions.Where(t => t.DateDeletedUtc == null && t.GameSystemId == gameSystemId).OrderBy(t => t.Name).ToListAsync())
    .WithTags("Admin");

api.MapGet("/admin/tags/{tagDefinitionId:int}", async (int tagDefinitionId, AppDbContext db) =>
{
    var row = await db.TagDefinitions.FirstOrDefaultAsync(t => t.TagDefinitionId == tagDefinitionId && t.DateDeletedUtc == null);
    return row is null ? Results.NotFound() : Results.Ok(row);
}).WithTags("Admin");

api.MapPost("/admin/tags", async (UpsertTagRequest req, AppDbContext db) =>
{
    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name is required.");

    var name = ToTitleCase(req.Name);
    var slug = await GenerateUniqueTagSlugAsync(db, req.GameSystemId, Slugify(name));
    var row = new TagDefinition { GameSystemId=req.GameSystemId, Name=name, Slug=slug, DateCreatedUtc=DateTime.UtcNow, DateModifiedUtc=DateTime.UtcNow };
    db.TagDefinitions.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(row);
}).WithTags("Admin");



api.MapPost("/admin/tags/migrate-from-items", async (int gameSystemId, AppDbContext db) =>
{
    var items = await db.Items.Where(i => i.DateDeletedUtc == null && i.GameSystemId == gameSystemId && i.Tags != null && i.Tags != "").ToListAsync();
    var createdTags = 0;
    var linked = 0;

    foreach (var item in items)
    {
        var parts = item.Tags!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => ToTitleCase(x))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var name in parts)
        {
            var slug = Slugify(name);
            var tag = await db.TagDefinitions.FirstOrDefaultAsync(t => t.DateDeletedUtc == null && t.GameSystemId == gameSystemId && t.Slug == slug);
            if (tag is null)
            {
                tag = new TagDefinition { GameSystemId = gameSystemId, Name = name, Slug = await GenerateUniqueTagSlugAsync(db, gameSystemId, slug), DateCreatedUtc = DateTime.UtcNow, DateModifiedUtc = DateTime.UtcNow };
                db.TagDefinitions.Add(tag);
                await db.SaveChangesAsync();
                createdTags++;
            }

            var exists = await db.ItemTags.AnyAsync(it => it.DateDeletedUtc == null && it.ItemId == item.ItemId && it.TagDefinitionId == tag.TagDefinitionId);
            if (!exists)
            {
                db.ItemTags.Add(new ItemTag { ItemId = item.ItemId, TagDefinitionId = tag.TagDefinitionId, DateCreatedUtc = DateTime.UtcNow });
                linked++;
            }
        }
    }

    if (linked > 0) await db.SaveChangesAsync();
    return Results.Ok(new { createdTags, linked });
}).WithTags("Admin");



api.MapPut("/admin/tags/{tagDefinitionId:int}", async (int tagDefinitionId, UpsertTagRequest req, AppDbContext db) =>
{
    var row = await db.TagDefinitions.FirstOrDefaultAsync(t => t.TagDefinitionId == tagDefinitionId && t.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name is required.");

    var name = ToTitleCase(req.Name);
    var slug = await GenerateUniqueTagSlugAsync(db, req.GameSystemId, Slugify(name));

    row.GameSystemId = req.GameSystemId;
    row.Name = name;
    row.Slug = slug;
    row.DateModifiedUtc = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(row);
}).WithTags("Admin");

api.MapGet("/currencies", async (int gameSystemId, AppDbContext db) =>
    await db.CurrencyDefinitions
        .Where(c => c.DateDeletedUtc == null && c.GameSystemId == gameSystemId)
        .OrderBy(c => c.Code)
        .ToListAsync())
    .WithTags("Currencies");

api.MapGet("/admin/currencies", async (int gameSystemId, AppDbContext db) =>
    await db.CurrencyDefinitions
        .Where(c => c.DateDeletedUtc == null && c.GameSystemId == gameSystemId)
        .OrderBy(c => c.Code)
        .ToListAsync())
    .WithTags("Admin");

api.MapGet("/admin/currencies/{currencyDefinitionId:int}", async (int currencyDefinitionId, AppDbContext db) =>
{
    var row = await db.CurrencyDefinitions.FirstOrDefaultAsync(c => c.CurrencyDefinitionId == currencyDefinitionId && c.DateDeletedUtc == null);
    return row is null ? Results.NotFound() : Results.Ok(row);
}).WithTags("Admin");

api.MapPost("/admin/currencies", async (UpsertCurrencyRequest req, AppDbContext db) =>
{
    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");
    if (string.IsNullOrWhiteSpace(req.Code)) return Results.BadRequest("Code is required.");

    var code = req.Code.Trim().ToUpperInvariant();
    var exists = await db.CurrencyDefinitions.AnyAsync(c => c.DateDeletedUtc == null && c.GameSystemId == req.GameSystemId && c.Code == code);
    if (exists) return Results.BadRequest("Currency code already exists for this system.");

    var now = DateTime.UtcNow;
    var row = new CurrencyDefinition
    {
        GameSystemId = req.GameSystemId,
        Name = string.IsNullOrWhiteSpace(req.Name) ? code : ToTitleCase(req.Name),
        Code = code,
        Symbol = req.Symbol,
        Description = req.Description,
        DateCreatedUtc = now,
        DateModifiedUtc = now
    };
    db.CurrencyDefinitions.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(row);
}).WithTags("Admin");



api.MapPut("/admin/currencies/{currencyDefinitionId:int}", async (int currencyDefinitionId, UpsertCurrencyRequest req, AppDbContext db) =>
{
    var row = await db.CurrencyDefinitions.FirstOrDefaultAsync(c => c.CurrencyDefinitionId == currencyDefinitionId && c.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");
    if (string.IsNullOrWhiteSpace(req.Code)) return Results.BadRequest("Code is required.");

    var code = req.Code.Trim().ToUpperInvariant();
    var dup = await db.CurrencyDefinitions.AnyAsync(c => c.DateDeletedUtc == null && c.GameSystemId == req.GameSystemId && c.Code == code && c.CurrencyDefinitionId != currencyDefinitionId);
    if (dup) return Results.BadRequest("Currency code already exists for this system.");

    row.GameSystemId = req.GameSystemId;
    row.Code = code;
    row.Name = string.IsNullOrWhiteSpace(req.Name) ? code : ToTitleCase(req.Name);
    row.Symbol = req.Symbol;
    row.Description = req.Description;
    row.DateModifiedUtc = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(row);
}).WithTags("Admin");

api.MapPost("/admin/currency-migration/map-to-definitions", async (int gameSystemId, AppDbContext db) =>
{
    var currencies = await db.CurrencyDefinitions.Where(c => c.DateDeletedUtc == null && c.GameSystemId == gameSystemId).ToListAsync();
    var byCode = currencies.ToDictionary(c => c.Code.ToUpperInvariant(), c => c.CurrencyDefinitionId);

    var items = await db.Items.Where(i => i.DateDeletedUtc == null && i.GameSystemId == gameSystemId && i.CurrencyDefinitionId == null && i.CostCurrency != null && i.CostCurrency != "").ToListAsync();
    var mapped = 0;
    foreach (var i in items)
    {
        var code = i.CostCurrency!.Trim().ToUpperInvariant();
        if (byCode.TryGetValue(code, out var cid))
        {
            i.CurrencyDefinitionId = cid;
            i.DateModifiedUtc = DateTime.UtcNow;
            mapped++;
        }
    }
    if (mapped > 0) await db.SaveChangesAsync();
    return Results.Ok(new { mapped });
}).WithTags("Admin");

api.MapGet("/admin/currency-migration/preview", async (AppDbContext db) =>
{
    var rows = await db.Items
        .Where(i => i.DateDeletedUtc == null && i.CostCurrency != null && i.CostCurrency != "")
        .GroupBy(i => i.CostCurrency!.Trim().ToUpper())
        .Select(g => new { currency = g.Key, count = g.Count() })
        .OrderByDescending(x => x.count)
        .ToListAsync();

    return Results.Ok(rows);
}).WithTags("Admin");

api.MapPost("/admin/currency-migration/normalize", async (AppDbContext db) =>
{
    var items = await db.Items.Where(i => i.DateDeletedUtc == null && i.CostCurrency != null && i.CostCurrency != "").ToListAsync();
    var changed = 0;
    foreach (var i in items)
    {
        var before = i.CostCurrency ?? string.Empty;
        var after = before.Trim().ToUpperInvariant();
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            i.CostCurrency = after;
            i.DateModifiedUtc = DateTime.UtcNow;
            changed++;
        }
    }

    if (changed > 0)
        await db.SaveChangesAsync();

    return Results.Ok(new { normalized = changed });
}).WithTags("Admin");

api.MapGet("/admin/orphans", async (AppDbContext db) =>
{
    var activeSystemIds = await db.GameSystems
        .Where(gs => gs.DateDeletedUtc == null)
        .Select(gs => gs.GameSystemId)
        .ToListAsync();

    var systems = await db.GameSystems
        .Select(gs => new { gs.GameSystemId, gs.Name, gs.DateDeletedUtc })
        .ToListAsync();

    var orphanItemTypes = await db.ItemTypeDefinitions
        .Where(x => x.DateDeletedUtc == null && !activeSystemIds.Contains(x.GameSystemId))
        .Select(x => new { Kind = "item-type", Id = x.ItemTypeDefinitionId, x.Name, x.GameSystemId })
        .ToListAsync();

    var orphanRarities = await db.RarityDefinitions
        .Where(x => x.DateDeletedUtc == null && !activeSystemIds.Contains(x.GameSystemId))
        .Select(x => new { Kind = "rarity", Id = x.RarityDefinitionId, x.Name, x.GameSystemId })
        .ToListAsync();

    var orphanItems = await db.Items
        .Where(x => x.DateDeletedUtc == null && !activeSystemIds.Contains(x.GameSystemId))
        .Select(x => new { Kind = "item", Id = x.ItemId, x.Name, x.GameSystemId })
        .ToListAsync();

    var all = orphanItemTypes.Cast<dynamic>().Concat(orphanRarities).Concat(orphanItems)
        .Select(x =>
        {
            var src = systems.FirstOrDefault(s => s.GameSystemId == (int)x.GameSystemId);
            return new
            {
                kind = (string)x.Kind,
                id = (int)x.Id,
                name = (string)x.Name,
                sourceGameSystemId = (int)x.GameSystemId,
                sourceGameSystemName = src?.Name,
                sourceGameSystemDeletedUtc = src?.DateDeletedUtc
            };
        })
        .OrderBy(x => x.kind)
        .ThenBy(x => x.name)
        .ToList();

    return Results.Ok(new { total = all.Count, rows = all });
}).WithTags("Admin");



api.MapPost("/admin/orphans/reassign-one", async (ReassignOneOrphanRequest req, AppDbContext db) =>
{
    var targetExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.ToGameSystemId && gs.DateDeletedUtc == null);
    if (!targetExists) return Results.BadRequest("Target game system is invalid.");

    switch (req.Kind)
    {
        case "item":
            var item = await db.Items.FirstOrDefaultAsync(x => x.ItemId == req.Id && x.DateDeletedUtc == null);
            if (item is null) return Results.NotFound();
            item.GameSystemId = req.ToGameSystemId;
            item.DateModifiedUtc = DateTime.UtcNow;
            break;
        case "item-type":
            var type = await db.ItemTypeDefinitions.FirstOrDefaultAsync(x => x.ItemTypeDefinitionId == req.Id && x.DateDeletedUtc == null);
            if (type is null) return Results.NotFound();
            type.GameSystemId = req.ToGameSystemId;
            type.DateModifiedUtc = DateTime.UtcNow;
            break;
        case "rarity":
            var rarity = await db.RarityDefinitions.FirstOrDefaultAsync(x => x.RarityDefinitionId == req.Id && x.DateDeletedUtc == null);
            if (rarity is null) return Results.NotFound();
            rarity.GameSystemId = req.ToGameSystemId;
            rarity.DateModifiedUtc = DateTime.UtcNow;
            break;
        default:
            return Results.BadRequest("Invalid kind.");
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { reassigned = true });
}).WithTags("Admin");

api.MapDelete("/admin/orphans/{kind}/{id:int}", async (string kind, int id, AppDbContext db) =>
{
    switch (kind)
    {
        case "item":
            var item = await db.Items.FirstOrDefaultAsync(x => x.ItemId == id && x.DateDeletedUtc == null);
            if (item is null) return Results.NotFound();
            db.Items.Remove(item);
            break;
        case "item-type":
            var type = await db.ItemTypeDefinitions.FirstOrDefaultAsync(x => x.ItemTypeDefinitionId == id && x.DateDeletedUtc == null);
            if (type is null) return Results.NotFound();
            db.ItemTypeDefinitions.Remove(type);
            break;
        case "rarity":
            var rarity = await db.RarityDefinitions.FirstOrDefaultAsync(x => x.RarityDefinitionId == id && x.DateDeletedUtc == null);
            if (rarity is null) return Results.NotFound();
            db.RarityDefinitions.Remove(rarity);
            break;
        default:
            return Results.BadRequest("Invalid kind.");
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { deleted = true });
}).WithTags("Admin");

api.MapPost("/admin/orphans/reassign", async (ReassignOrphansRequest req, AppDbContext db) =>
{
    var targetExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.ToGameSystemId && gs.DateDeletedUtc == null);
    if (!targetExists) return Results.BadRequest("Target game system is invalid.");

    await db.Database.ExecuteSqlRawAsync("UPDATE ItemTypeDefinitions SET GameSystemId = {0}, DateModifiedUtc = {1} WHERE GameSystemId = {2} AND DateDeletedUtc IS NULL;", req.ToGameSystemId, DateTime.UtcNow, req.FromGameSystemId);
    await db.Database.ExecuteSqlRawAsync("UPDATE RarityDefinitions SET GameSystemId = {0}, DateModifiedUtc = {1} WHERE GameSystemId = {2} AND DateDeletedUtc IS NULL;", req.ToGameSystemId, DateTime.UtcNow, req.FromGameSystemId);
    await db.Database.ExecuteSqlRawAsync("UPDATE Items SET GameSystemId = {0}, DateModifiedUtc = {1} WHERE GameSystemId = {2} AND DateDeletedUtc IS NULL;", req.ToGameSystemId, DateTime.UtcNow, req.FromGameSystemId);

    return Results.Ok(new { reassigned = true, from = req.FromGameSystemId, to = req.ToGameSystemId });
}).WithTags("Admin");

api.MapGet("/admin/game-systems/deleted", async (AppDbContext db) =>
    await db.GameSystems
        .Where(gs => gs.DateDeletedUtc != null)
        .OrderByDescending(gs => gs.DateDeletedUtc)
        .Select(gs => new
        {
            gs.GameSystemId,
            gs.Name,
            gs.Slug,
            Alias = string.IsNullOrWhiteSpace(gs.Alias) ? null : gs.Alias,
            gs.Description,
            gs.SourceType,
            gs.DateCreatedUtc,
            gs.DateModifiedUtc,
            gs.DateDeletedUtc
        })
        .ToListAsync())
    .WithTags("Admin");

api.MapDelete("/admin/game-systems/{gameSystemId:int}/purge", async (int gameSystemId, AppDbContext db) =>
{
    var row = await db.GameSystems.FirstOrDefaultAsync(gs => gs.GameSystemId == gameSystemId && gs.DateDeletedUtc != null);
    if (row is null) return Results.NotFound();

    db.GameSystems.Remove(row);
    await db.SaveChangesAsync();
    return Results.Ok();
}).WithTags("Admin");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();



static string ToTitleCase(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return string.Empty;
    var txt = System.Globalization.CultureInfo.InvariantCulture.TextInfo;
    var lower = value.Trim().ToLowerInvariant();
    return txt.ToTitleCase(lower);
}

static string Slugify(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return string.Empty;
    var chars = value.Trim().ToLowerInvariant();
    var sb = new System.Text.StringBuilder();
    var prevDash = false;

    foreach (var ch in chars)
    {
        if (char.IsLetterOrDigit(ch)) { sb.Append(ch); prevDash = false; }
        else if (!prevDash) { sb.Append('-'); prevDash = true; }
    }

    return sb.ToString().Trim('-');
}

static async Task<string> GenerateUniqueSlugAsync(AppDbContext db, string baseSlug, int? currentGameSystemId = null)
{
    var root = string.IsNullOrWhiteSpace(baseSlug) ? "game-system" : baseSlug;
    var candidate = root;
    var i = 2;

    while (await db.GameSystems.AnyAsync(gs =>
               gs.DateDeletedUtc == null &&
               gs.Slug == candidate &&
               (!currentGameSystemId.HasValue || gs.GameSystemId != currentGameSystemId.Value)))
    {
        candidate = $"{root}-{i}";
        i++;
    }

    return candidate;
}



static async Task<string> GenerateUniqueItemTypeSlugAsync(AppDbContext db, int gameSystemId, string baseSlug)
{
    var root = string.IsNullOrWhiteSpace(baseSlug) ? "item-type" : baseSlug;
    var candidate = root;
    var i = 2;

    while (await db.ItemTypeDefinitions.AnyAsync(x => x.DateDeletedUtc == null && x.GameSystemId == gameSystemId && x.Slug == candidate))
    {
        candidate = $"{root}-{i}";
        i++;
    }

    return candidate;
}





static async Task<string> GenerateUniqueTagSlugAsync(AppDbContext db, int gameSystemId, string baseSlug)
{
    var root = string.IsNullOrWhiteSpace(baseSlug) ? "tag" : baseSlug;
    var candidate = root;
    var i = 2;
    while (await db.TagDefinitions.AnyAsync(x => x.DateDeletedUtc == null && x.GameSystemId == gameSystemId && x.Slug == candidate))
    {
        candidate = $"{root}-{i}";
        i++;
    }
    return candidate;
}

static async Task<string> GenerateUniqueRaritySlugAsync(AppDbContext db, int gameSystemId, string baseSlug)
{
    var root = string.IsNullOrWhiteSpace(baseSlug) ? "rarity" : baseSlug;
    var candidate = root;
    var i = 2;
    while (await db.RarityDefinitions.AnyAsync(x => x.DateDeletedUtc == null && x.GameSystemId == gameSystemId && x.Slug == candidate))
    {
        candidate = $"{root}-{i}";
        i++;
    }
    return candidate;
}

static async Task<string> GenerateUniqueItemSlugAsync(AppDbContext db, int gameSystemId, string baseSlug)
{
    var root = string.IsNullOrWhiteSpace(baseSlug) ? "item" : baseSlug;
    var candidate = root;
    var i = 2;

    while (await db.Items.AnyAsync(x => x.DateDeletedUtc == null && x.GameSystemId == gameSystemId && x.Slug == candidate))
    {
        candidate = $"{root}-{i}";
        i++;
    }

    return candidate;
}


static async Task SeedStarterDataAsync(AppDbContext db)
{
    var now = DateTime.UtcNow;

    var systemsToSeed = new[]
    {
        new { Name = "Dungeons & Dragons 5e", Alias = "D&D 5e", Description = "Fifth edition fantasy tabletop RPG.", SourceType = SourceType.Official },
        new { Name = "Pathfinder 2e", Alias = "PF2e", Description = "Second edition fantasy tabletop RPG.", SourceType = SourceType.Official }
    };

    foreach (var seed in systemsToSeed)
    {
        var slug = Slugify(seed.Name);
        var existing = await db.GameSystems.FirstOrDefaultAsync(gs => gs.DateDeletedUtc == null && gs.Slug == slug);
        if (existing is null)
        {
            db.GameSystems.Add(new GameSystem
            {
                Name = seed.Name,
                Slug = await GenerateUniqueSlugAsync(db, slug),
                Alias = seed.Alias,
                Description = seed.Description,
                SourceType = seed.SourceType,
                DateCreatedUtc = now,
                DateModifiedUtc = now
            });
        }
    }

    await db.SaveChangesAsync();

    async Task EnsureItemAsync(string systemSlug, string itemName, string? description, decimal? costAmount, string? costCurrency, decimal? weight, SourceType sourceType = SourceType.Official)
    {
        var system = await db.GameSystems.FirstOrDefaultAsync(gs => gs.DateDeletedUtc == null && gs.Slug == systemSlug);
        if (system is null) return;

        var itemSlug = Slugify(itemName);
        var existing = await db.Items.FirstOrDefaultAsync(i => i.DateDeletedUtc == null && i.GameSystemId == system.GameSystemId && i.Slug == itemSlug);
        if (existing is not null) return;

        db.Items.Add(new Item
        {
            GameSystemId = system.GameSystemId,
            Name = itemName,
            Slug = await GenerateUniqueItemSlugAsync(db, system.GameSystemId, itemSlug),
            Description = description,
            CostAmount = costAmount,
            CostCurrency = costCurrency,
            Weight = weight,
            Quantity = 1,
            SourceType = sourceType,
            DateCreatedUtc = now,
            DateModifiedUtc = now
        });
    }

    await EnsureItemAsync("dungeons-dragons-5e", "Healing Potion", "A common red potion that restores hit points.", 50m, "gp", 0.5m);
    await EnsureItemAsync("dungeons-dragons-5e", "Longsword", "A versatile martial melee weapon.", 15m, "gp", 3m);
    await EnsureItemAsync("pathfinder-2e", "Minor Healing Potion", "A basic consumable that restores a small amount of HP.", 4m, "gp", null);
    await EnsureItemAsync("pathfinder-2e", "Explorer's Clothing", "Simple but practical adventuring clothes.", 1m, "gp", null);

    await db.SaveChangesAsync();
}


public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<GameSystem> GameSystems => Set<GameSystem>();
    public DbSet<ItemTypeDefinition> ItemTypeDefinitions => Set<ItemTypeDefinition>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<RarityDefinition> RarityDefinitions => Set<RarityDefinition>();
    public DbSet<CurrencyDefinition> CurrencyDefinitions => Set<CurrencyDefinition>();
    public DbSet<TagDefinition> TagDefinitions => Set<TagDefinition>();
    public DbSet<ItemTag> ItemTags => Set<ItemTag>();
}

public sealed class Note
{
    public int NoteId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public DateTime DateCreatedUtc { get; set; }
    public DateTime DateModifiedUtc { get; set; }
    public DateTime? DateDeletedUtc { get; set; }
}

public sealed record CreateNoteRequest(string Title, string? Body);

public enum SourceType
{
    Official = 1,
    ThirdParty = 2,
    Homebrew = 3
}

public sealed class GameSystem
{
    public int GameSystemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public string? Description { get; set; }
    public SourceType SourceType { get; set; } = SourceType.Official;
    public DateTime DateCreatedUtc { get; set; }
    public DateTime DateModifiedUtc { get; set; }
    public DateTime? DateDeletedUtc { get; set; }
}

public sealed record UpsertGameSystemRequest(string Name, string? Description, SourceType SourceType = SourceType.Official, string? Alias = null);


public sealed class ItemTypeDefinition
{
    public int ItemTypeDefinitionId { get; set; }
    public int GameSystemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime DateCreatedUtc { get; set; }
    public DateTime DateModifiedUtc { get; set; }
    public DateTime? DateDeletedUtc { get; set; }
}

public sealed class Item
{
    public int ItemId { get; set; }
    public int GameSystemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public int? ItemTypeDefinitionId { get; set; }
    public int? RarityDefinitionId { get; set; }
    public string? Description { get; set; }
    public string? Rarity { get; set; }
    public decimal? CostAmount { get; set; }
    public int? CurrencyDefinitionId { get; set; }
    public string? CostCurrency { get; set; }
    public decimal? Weight { get; set; }
    public int Quantity { get; set; } = 1;
    public string? Tags { get; set; }
    public string? Effect { get; set; }
    public bool RequiresAttunement { get; set; }
    public string? AttunementRequirement { get; set; }
    public SourceType SourceType { get; set; } = SourceType.Official;
    public DateTime DateCreatedUtc { get; set; }
    public DateTime DateModifiedUtc { get; set; }
    public DateTime? DateDeletedUtc { get; set; }
}

public sealed record CreateItemTypeRequest(int GameSystemId, string Name, string? Description);
public sealed record CreateItemRequest(int GameSystemId, string Name, int? ItemTypeDefinitionId, int? RarityDefinitionId, string? Description, decimal? CostAmount = null, int? CurrencyDefinitionId = null, string? CostCurrency = null, decimal? Weight = null, int Quantity = 1, string? Tags = null, string? Effect = null, bool RequiresAttunement = false, string? AttunementRequirement = null, List<int>? TagDefinitionIds = null, SourceType SourceType = SourceType.Official, string? Alias = null);

public sealed record UpsertItemTypeRequest(int GameSystemId, string Name, string? Description);


public sealed class RarityDefinition
{
    public int RarityDefinitionId { get; set; }
    public int GameSystemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public DateTime DateCreatedUtc { get; set; }
    public DateTime DateModifiedUtc { get; set; }
    public DateTime? DateDeletedUtc { get; set; }
}

public sealed record UpsertRarityRequest(int GameSystemId, string Name, string? Description);

public sealed record MergeGameSystemsRequest(int FromGameSystemId, int ToGameSystemId);
public sealed record ReassignOrphansRequest(int FromGameSystemId, int ToGameSystemId);
public sealed record ReassignOneOrphanRequest(string Kind, int Id, int ToGameSystemId);

public sealed class CurrencyDefinition
{
    public int CurrencyDefinitionId { get; set; }
    public int GameSystemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string? Description { get; set; }
    public DateTime DateCreatedUtc { get; set; }
    public DateTime DateModifiedUtc { get; set; }
    public DateTime? DateDeletedUtc { get; set; }
}

public sealed record UpsertCurrencyRequest(int GameSystemId, string Code, string? Name = null, string? Symbol = null, string? Description = null);

public sealed class TagDefinition { public int TagDefinitionId { get; set; } public int GameSystemId { get; set; } public string Name { get; set; } = string.Empty; public string Slug { get; set; } = string.Empty; public DateTime DateCreatedUtc { get; set; } public DateTime DateModifiedUtc { get; set; } public DateTime? DateDeletedUtc { get; set; } }
public sealed class ItemTag { public int ItemTagId { get; set; } public int ItemId { get; set; } public int TagDefinitionId { get; set; } public DateTime DateCreatedUtc { get; set; } public DateTime? DateDeletedUtc { get; set; } }
public sealed record UpsertTagRequest(int GameSystemId, string Name);
