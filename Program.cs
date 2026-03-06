using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
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

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ruleforge.auth";
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/";
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }
                ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }
                ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();


var dbProvider = (builder.Configuration["Database:Provider"]
    ?? Environment.GetEnvironmentVariable("RULEFORGE_DB_PROVIDER")
    ?? "sqlite").Trim().ToLowerInvariant();

var isPostgres = dbProvider is "postgres" or "postgresql" or "npgsql";
var isSqlite = !isPostgres;

var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ruleforge");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "ruleforge.db");

builder.Services.AddDbContext<AppDbContext>(opt =>
{
    if (isPostgres)
    {
        var pg = builder.Configuration.GetConnectionString("Default")
            ?? builder.Configuration["Database:ConnectionString"]
            ?? Environment.GetEnvironmentVariable("RULEFORGE_POSTGRES_CONNECTION")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL");

        if (string.IsNullOrWhiteSpace(pg))
            throw new InvalidOperationException("Postgres selected but no connection string configured. Set ConnectionStrings:Default or RULEFORGE_POSTGRES_CONNECTION.");

        opt.UseNpgsql(pg);
    }
    else
    {
        opt.UseSqlite($"Data Source={dbPath}");
    }
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (ctx, next) =>
{
    static bool IsAdminPath(PathString path) =>
        path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/api/admin", StringComparison.OrdinalIgnoreCase);

    if (IsAdminPath(ctx.Request.Path))
    {
        if (ctx.User?.Identity?.IsAuthenticated != true)
        {
            if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                await ctx.ChallengeAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            ctx.Response.Redirect("/login");
            return;
        }

        if (!ctx.User.IsInRole("Admin"))
        {
            if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync("Forbidden");
                return;
            }

            ctx.Response.Redirect("/");
            return;
        }
    }

    await next();
});

app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var errorUid = $"RF-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            int? userId = null;
            var idRaw = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(idRaw, out var uid)) userId = uid;

            db.AppErrors.Add(new AppError
            {
                ErrorUid = errorUid,
                Path = ctx.Request.Path,
                Method = ctx.Request.Method,
                UserId = userId,
                Message = ex.Message,
                StackTrace = ex.ToString(),
                DateCreatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        catch { }

        ctx.Response.StatusCode = 500;
        ctx.Response.Headers["X-Error-Id"] = errorUid;
        var msg = $"An internal error occurred. Error ID: {errorUid}";
        await ctx.Response.WriteAsync(msg);
    }
});

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

    if (!isSqlite)
    {
        await db.Database.MigrateAsync();
        await EnsureSeedAdminAccountAsync(db);
        await SeedStarterDataAsync(db, app.Environment.ContentRootPath);
    }
    else
    {
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
        CREATE TABLE IF NOT EXISTS Campaigns (
            CampaignId INTEGER PRIMARY KEY AUTOINCREMENT,
            Title TEXT NOT NULL,
            Description TEXT NULL,
            OwnerAppUserId INTEGER NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);

    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Campaigns_Title_Unique_Active ON Campaigns (Title) WHERE DateDeletedUtc IS NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Campaigns ADD COLUMN OwnerAppUserId INTEGER NULL;"); } catch (SqliteException) { }

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS CampaignCollaborators (
            CampaignCollaboratorId INTEGER PRIMARY KEY AUTOINCREMENT,
            CampaignId INTEGER NOT NULL,
            AppUserId INTEGER NOT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS CampaignPlayers (
            CampaignPlayerId INTEGER PRIMARY KEY AUTOINCREMENT,
            CampaignId INTEGER NOT NULL,
            AppUserId INTEGER NOT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);

    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_CampaignCollaborators_Unique_Active ON CampaignCollaborators (CampaignId, AppUserId) WHERE DateDeletedUtc IS NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_CampaignPlayers_Unique_Active ON CampaignPlayers (CampaignId, AppUserId) WHERE DateDeletedUtc IS NULL;"); } catch (SqliteException) { }

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS Creatures (
            CreatureId INTEGER PRIMARY KEY AUTOINCREMENT,
            GameSystemId INTEGER NOT NULL,
            Name TEXT NOT NULL,
            Slug TEXT NOT NULL,
            Alias TEXT NULL,
            CreatureType TEXT NULL,
            Size TEXT NULL,
            Alignment TEXT NULL,
            ArmorClass INTEGER NULL,
            HitPoints INTEGER NULL,
            Speed TEXT NULL,
            Strength INTEGER NULL,
            Dexterity INTEGER NULL,
            Constitution INTEGER NULL,
            Intelligence INTEGER NULL,
            Wisdom INTEGER NULL,
            Charisma INTEGER NULL,
            ChallengeRating TEXT NULL,
            ProficiencyBonus INTEGER NULL,
            Description TEXT NULL,
            Traits TEXT NULL,
            Actions TEXT NULL,
            SourceType INTEGER NOT NULL,
            OwnerAppUserId INTEGER NULL,
            SourceMaterialId INTEGER NULL,
            CampaignId INTEGER NULL,
            SourcePage INTEGER NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);
    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Creatures_System_Slug_Unique_Active ON Creatures (GameSystemId, Slug) WHERE DateDeletedUtc IS NULL;"); } catch (SqliteException) { }

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS CreatureAbilities (
            CreatureAbilityId INTEGER PRIMARY KEY AUTOINCREMENT,
            CreatureId INTEGER NOT NULL,
            AbilityType TEXT NOT NULL,
            Name TEXT NULL,
            Description TEXT NOT NULL,
            SortOrder INTEGER NOT NULL DEFAULT 0,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);
    try { await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_CreatureAbilities_Creature ON CreatureAbilities (CreatureId);"); } catch (SqliteException) { }

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
            OwnerAppUserId INTEGER NULL,
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
            DamageDice TEXT NULL,
            DamageType TEXT NULL,
            VersatileDamageDice TEXT NULL,
            ArmorClass INTEGER NULL,
            StrengthRequirement INTEGER NULL,
            StealthDisadvantage INTEGER NOT NULL DEFAULT 0,
            RangeNormal INTEGER NULL,
            RangeLong INTEGER NULL,
            SourceMaterialId INTEGER NULL,
            CampaignId INTEGER NULL,
            SourceBook TEXT NULL,
            SourcePage INTEGER NULL,
            IsConsumable INTEGER NOT NULL DEFAULT 0,
            ChargesCurrent INTEGER NULL,
            ChargesMax INTEGER NULL,
            RechargeRule TEXT NULL,
            UsesPerDay INTEGER NULL,
            ArmorCategory TEXT NULL,
            WeaponPropertyLight INTEGER NOT NULL DEFAULT 0,
            WeaponPropertyHeavy INTEGER NOT NULL DEFAULT 0,
            WeaponPropertyFinesse INTEGER NOT NULL DEFAULT 0,
            WeaponPropertyThrown INTEGER NOT NULL DEFAULT 0,
            WeaponPropertyTwoHanded INTEGER NOT NULL DEFAULT 0,
            WeaponPropertyLoading INTEGER NOT NULL DEFAULT 0,
            WeaponPropertyReach INTEGER NOT NULL DEFAULT 0,
            WeaponPropertyAmmunition INTEGER NOT NULL DEFAULT 0,
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
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN DamageDice TEXT NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN DamageType TEXT NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN VersatileDamageDice TEXT NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN ArmorClass INTEGER NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN StrengthRequirement INTEGER NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN StealthDisadvantage INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN RangeNormal INTEGER NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN RangeLong INTEGER NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN SourceBook TEXT NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN SourcePage INTEGER NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN IsConsumable INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN ChargesCurrent INTEGER NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN ChargesMax INTEGER NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN RechargeRule TEXT NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN UsesPerDay INTEGER NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN ArmorCategory TEXT NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN WeaponPropertyLight INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN WeaponPropertyHeavy INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN WeaponPropertyFinesse INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN WeaponPropertyThrown INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN WeaponPropertyTwoHanded INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN WeaponPropertyLoading INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN WeaponPropertyReach INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN WeaponPropertyAmmunition INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) { }

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
        CREATE TABLE IF NOT EXISTS SourceMaterials (
            SourceMaterialId INTEGER PRIMARY KEY AUTOINCREMENT,
            GameSystemId INTEGER NOT NULL,
            Code TEXT NOT NULL,
            Title TEXT NOT NULL,
            Publisher TEXT NULL,
            IsOfficial INTEGER NOT NULL DEFAULT 1,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);
    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_SourceMaterials_System_Code_Unique_Active ON SourceMaterials (GameSystemId, Code) WHERE DateDeletedUtc IS NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN SourceMaterialId INTEGER NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN CampaignId INTEGER NULL;"); } catch (SqliteException) { }

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


    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS AppUsers (
            AppUserId INTEGER PRIMARY KEY AUTOINCREMENT,
            Email TEXT NOT NULL,
            Username TEXT NOT NULL,
            PasswordHash TEXT NOT NULL,
            PasswordSalt TEXT NOT NULL,
            Role TEXT NOT NULL,
            IsActive INTEGER NOT NULL DEFAULT 1,
            IsSystemAccount INTEGER NOT NULL DEFAULT 0,
            MustChangePassword INTEGER NOT NULL DEFAULT 0,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS AppErrors (
            AppErrorId INTEGER PRIMARY KEY AUTOINCREMENT,
            ErrorUid TEXT NOT NULL,
            Path TEXT NULL,
            Method TEXT NULL,
            UserId INTEGER NULL,
            Message TEXT NULL,
            StackTrace TEXT NULL,
            DateCreatedUtc TEXT NOT NULL
        );
    """);
    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_AppErrors_ErrorUid ON AppErrors (ErrorUid);"); } catch (SqliteException) { }

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS FriendRequests (
            FriendRequestId INTEGER PRIMARY KEY AUTOINCREMENT,
            FromAppUserId INTEGER NOT NULL,
            ToAppUserId INTEGER NOT NULL,
            Status TEXT NOT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateResolvedUtc TEXT NULL
        );
    """);

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS Friends (
            FriendId INTEGER PRIMARY KEY AUTOINCREMENT,
            UserAId INTEGER NOT NULL,
            UserBId INTEGER NOT NULL,
            DateCreatedUtc TEXT NOT NULL
        );
    """);
    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Friends_Pair_Unique ON Friends (UserAId, UserBId);"); } catch (SqliteException) { }

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS FeatureRequests (
            FeatureRequestId INTEGER PRIMARY KEY AUTOINCREMENT,
            Title TEXT NOT NULL,
            Description TEXT NULL,
            Status TEXT NOT NULL,
            Priority TEXT NULL,
            RequestedBy TEXT NULL,
            Entity TEXT NULL,
            SortOrder INTEGER NOT NULL DEFAULT 0,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
    """);
    try { await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_FeatureRequests_Status_Sort ON FeatureRequests (Status, SortOrder, FeatureRequestId);"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE FeatureRequests ADD COLUMN Entity TEXT NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("UPDATE FeatureRequests SET Entity = 'Creature' WHERE DateDeletedUtc IS NULL AND (Entity IS NULL OR Entity = '') AND Title LIKE 'Bestiary%';"); } catch (SqliteException) { }

    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE AppUsers ADD COLUMN Username TEXT NOT NULL DEFAULT '';"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_AppUsers_Email_Unique_Active ON AppUsers (Email) WHERE DateDeletedUtc IS NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_AppUsers_Username_Unique_Active ON AppUsers (Username) WHERE DateDeletedUtc IS NULL;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE AppUsers ADD COLUMN IsSystemAccount INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE AppUsers ADD COLUMN MustChangePassword INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("UPDATE AppUsers SET Role='Users' WHERE DateDeletedUtc IS NULL AND Role IS NOT NULL AND Role NOT IN ('Admin','Users');"); } catch (SqliteException) { }
    try { await db.Database.ExecuteSqlRawAsync("UPDATE AppUsers SET Username = lower(substr(Email,1,instr(Email,'@')-1)) WHERE DateDeletedUtc IS NULL AND (Username IS NULL OR Username='') AND instr(Email,'@')>1;"); } catch (SqliteException) { }

    try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Items ADD COLUMN OwnerAppUserId INTEGER NULL;"); } catch (SqliteException) { }

    await EnsureSeedAdminAccountAsync(db);
    await SeedStarterDataAsync(db, app.Environment.ContentRootPath);
    }

}

var api = app.MapGroup("/api").WithTags("Core");

api.MapGet("/health", () => Results.Ok(new { ok = true, service = "RuleForge", utc = DateTime.UtcNow }));


api.MapPost("/auth/register", async (RegisterRequest req, AppDbContext db, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        return await LoggedBadRequestAsync(db, http, "Email, username, and password are required.");

    var email = req.Email.Trim().ToLowerInvariant();
    var username = req.Username.Trim().ToLowerInvariant();
    if (req.Password.Length < 8) return await LoggedBadRequestAsync(db, http, "Password must be at least 8 characters.");

    var exists = await db.AppUsers.AnyAsync(u => u.DateDeletedUtc == null && u.Email == email);
    if (exists) return await LoggedBadRequestAsync(db, http, "Email is already registered.");
    var usernameExists = await db.AppUsers.AnyAsync(u => u.DateDeletedUtc == null && u.Username == username);
    if (usernameExists) return await LoggedBadRequestAsync(db, http, "Username is already registered.");

    var userCount = await db.AppUsers.CountAsync(u => u.DateDeletedUtc == null);
    var role = userCount == 0 ? "Admin" : "Users";

    var now = DateTime.UtcNow;
    var (hash, salt) = HashPassword(req.Password);
    var user = new AppUser
    {
        Email = email,
        Username = username,
        PasswordHash = hash,
        PasswordSalt = salt,
        Role = role,
        IsActive = true,
        IsSystemAccount = false,
        MustChangePassword = false,
        DateCreatedUtc = now,
        DateModifiedUtc = now
    };
    db.AppUsers.Add(user);
    await db.SaveChangesAsync();

    return Results.Ok(new { user.AppUserId, user.Email, user.Username, user.Role, user.IsActive, user.MustChangePassword });
}).WithTags("Auth");

api.MapPost("/auth/login", async (LoginRequest req, AppDbContext db, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(req.Identifier) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest("Identifier and password are required.");

    var ident = req.Identifier.Trim().ToLowerInvariant();
    var user = await db.AppUsers.FirstOrDefaultAsync(u => u.DateDeletedUtc == null && (u.Email == ident || u.Username == ident));
    if (user is null || !user.IsActive) return Results.Unauthorized();

    if (!VerifyPassword(req.Password, user.PasswordHash, user.PasswordSalt)) return Results.Unauthorized();

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.AppUserId.ToString()),
        new(ClaimTypes.Email, user.Email),
        new(ClaimTypes.Role, user.Role),
        new(ClaimTypes.Name, user.Username),
        new("username", user.Username),
        new("must_change_password", user.MustChangePassword ? "true" : "false")
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

    return Results.Ok(new { user.AppUserId, user.Email, user.Username, user.Role, user.MustChangePassword });
}).WithTags("Auth");

api.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { ok = true });
}).WithTags("Auth");


api.MapPost("/auth/change-password", async (ChangePasswordRequest req, AppDbContext db, HttpContext http) =>
{
    if (http.User?.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!int.TryParse(idRaw, out var userId)) return Results.Unauthorized();

    var user = await db.AppUsers.FirstOrDefaultAsync(u => u.AppUserId == userId && u.DateDeletedUtc == null);
    if (user is null || !user.IsActive) return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
        return Results.BadRequest("CurrentPassword and NewPassword are required.");

    if (!VerifyPassword(req.CurrentPassword, user.PasswordHash, user.PasswordSalt))
        return Results.BadRequest("Current password is invalid.");

    if (req.NewPassword.Length < 8)
        return Results.BadRequest("New password must be at least 8 characters.");

    var (hash, salt) = HashPassword(req.NewPassword);
    user.PasswordHash = hash;
    user.PasswordSalt = salt;
    user.MustChangePassword = false;
    user.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { ok = true });
}).WithTags("Auth");

api.MapGet("/auth/me", (HttpContext http) =>
{
    if (http.User?.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    return Results.Ok(new
    {
        id = http.User.FindFirstValue(ClaimTypes.NameIdentifier),
        email = http.User.FindFirstValue(ClaimTypes.Email),
        username = http.User.FindFirstValue("username"),
        role = http.User.FindFirstValue(ClaimTypes.Role),
        mustChangePassword = string.Equals(http.User.FindFirstValue("must_change_password"), "true", StringComparison.OrdinalIgnoreCase)
    });
}).WithTags("Auth");



api.MapGet("/admin/users", async (AppDbContext db) =>
    await db.AppUsers
        .Where(u => u.DateDeletedUtc == null)
        .OrderBy(u => u.Email)
        .Select(u => new { u.AppUserId, u.Email, u.Username, u.Role, u.IsActive, u.IsSystemAccount, u.MustChangePassword, u.DateCreatedUtc, u.DateModifiedUtc })
        .ToListAsync())
    .WithTags("Admin");


api.MapPost("/admin/users", async (CreateUserAdminRequest req, AppDbContext db, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        return await LoggedBadRequestAsync(db, http, "Email, username, and password are required.");

    var email = req.Email.Trim().ToLowerInvariant();
    var username = req.Username.Trim().ToLowerInvariant();
    if (req.Password.Length < 8) return await LoggedBadRequestAsync(db, http, "Password must be at least 8 characters.");

    var role = (req.Role ?? "").Trim();
    if (role != "Admin" && role != "Users") return await LoggedBadRequestAsync(db, http, "Role must be Admin or Users.");

    var exists = await db.AppUsers.AnyAsync(u => u.DateDeletedUtc == null && u.Email == email);
    if (exists) return await LoggedBadRequestAsync(db, http, "Email is already registered.");
    var usernameExists = await db.AppUsers.AnyAsync(u => u.DateDeletedUtc == null && u.Username == username);
    if (usernameExists) return await LoggedBadRequestAsync(db, http, "Username is already registered.");

    var (hash, salt) = HashPassword(req.Password);
    var now = DateTime.UtcNow;
    var user = new AppUser
    {
        Email = email,
        Username = username,
        PasswordHash = hash,
        PasswordSalt = salt,
        Role = role,
        IsActive = req.IsActive,
        IsSystemAccount = false,
        MustChangePassword = req.MustChangePassword,
        DateCreatedUtc = now,
        DateModifiedUtc = now
    };
    db.AppUsers.Add(user);
    await db.SaveChangesAsync();

    return Results.Ok(new { user.AppUserId, user.Email, user.Username, user.Role, user.IsActive, user.IsSystemAccount, user.MustChangePassword });
}).WithTags("Admin");

api.MapGet("/admin/users/{appUserId:int}", async (int appUserId, AppDbContext db) =>
{
    var u = await db.AppUsers.FirstOrDefaultAsync(x => x.AppUserId == appUserId && x.DateDeletedUtc == null);
    return u is null ? Results.NotFound() : Results.Ok(new { u.AppUserId, u.Email, u.Username, u.Role, u.IsActive, u.IsSystemAccount, u.MustChangePassword });
}).WithTags("Admin");

api.MapPut("/admin/users/{appUserId:int}", async (int appUserId, UpdateUserAdminRequest req, AppDbContext db, HttpContext http) =>
{
    var u = await db.AppUsers.FirstOrDefaultAsync(x => x.AppUserId == appUserId && x.DateDeletedUtc == null);
    if (u is null) return Results.NotFound();

    var role = (req.Role ?? "").Trim();
    if (role != "Admin" && role != "Users") return await LoggedBadRequestAsync(db, http, "Role must be Admin or Users.");

    if (u.IsSystemAccount)
    {
        if (role != "Admin") return await LoggedBadRequestAsync(db, http, "System account role cannot be changed.");
        if (!req.IsActive) return await LoggedBadRequestAsync(db, http, "System account cannot be deactivated.");
    }

    u.Role = role;
    u.IsActive = req.IsActive;
    u.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { u.AppUserId, u.Email, u.Role, u.IsActive, u.IsSystemAccount });
}).WithTags("Admin");

api.MapGet("/admin/feature-requests", async (AppDbContext db) =>
    await db.FeatureRequests
        .Where(x => x.DateDeletedUtc == null)
        .OrderBy(x => x.Status)
        .ThenBy(x => x.SortOrder)
        .ThenBy(x => x.FeatureRequestId)
        .Select(x => new
        {
            x.FeatureRequestId,
            x.Title,
            x.Description,
            x.Status,
            x.Priority,
            x.RequestedBy,
            x.Entity,
            x.SortOrder,
            x.DateCreatedUtc,
            x.DateModifiedUtc
        })
        .ToListAsync())
    .WithTags("Admin");

api.MapPost("/admin/feature-requests", async (UpsertFeatureRequest req, AppDbContext db, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(req.Title)) return await LoggedBadRequestAsync(db, http, "Title is required.");
    var now = DateTime.UtcNow;
    var row = new FeatureRequest
    {
        Title = req.Title.Trim(),
        Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
        Status = NormalizeFeatureStatus(req.Status),
        Priority = string.IsNullOrWhiteSpace(req.Priority) ? null : req.Priority.Trim(),
        RequestedBy = string.IsNullOrWhiteSpace(req.RequestedBy) ? null : req.RequestedBy.Trim(),
        Entity = string.IsNullOrWhiteSpace(req.Entity) ? null : req.Entity.Trim(),
        SortOrder = req.SortOrder ?? 0,
        DateCreatedUtc = now,
        DateModifiedUtc = now
    };
    db.FeatureRequests.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(row);
}).WithTags("Admin");

api.MapPut("/admin/feature-requests/{featureRequestId:int}", async (int featureRequestId, UpsertFeatureRequest req, AppDbContext db, HttpContext http) =>
{
    var row = await db.FeatureRequests.FirstOrDefaultAsync(x => x.FeatureRequestId == featureRequestId && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(req.Title)) return await LoggedBadRequestAsync(db, http, "Title is required.");

    row.Title = req.Title.Trim();
    row.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
    row.Status = NormalizeFeatureStatus(req.Status);
    row.Priority = string.IsNullOrWhiteSpace(req.Priority) ? null : req.Priority.Trim();
    row.RequestedBy = string.IsNullOrWhiteSpace(req.RequestedBy) ? null : req.RequestedBy.Trim();
    row.Entity = string.IsNullOrWhiteSpace(req.Entity) ? null : req.Entity.Trim();
    row.SortOrder = req.SortOrder ?? row.SortOrder;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(row);
}).WithTags("Admin");

api.MapDelete("/admin/feature-requests/{featureRequestId:int}", async (int featureRequestId, AppDbContext db) =>
{
    var row = await db.FeatureRequests.FirstOrDefaultAsync(x => x.FeatureRequestId == featureRequestId && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { ok = true });
}).WithTags("Admin");


api.MapGet("/creatures", async (int? gameSystemId, AppDbContext db) =>
{
    var q = db.Creatures.Where(c => c.DateDeletedUtc == null);
    if (gameSystemId.HasValue)
        q = q.Where(c => c.GameSystemId == gameSystemId.Value);

    return await q
        .OrderBy(c => c.Name)
        .Select(c => new
        {
            c.CreatureId, c.GameSystemId, c.Name, c.Slug, c.Alias, c.CreatureType, c.ChallengeRating,
            c.SourceType, c.OwnerAppUserId, c.SourceMaterialId, c.CampaignId
        })
        .ToListAsync();
})
    .WithTags("Creatures");

api.MapGet("/creatures/{creatureId:int}", async (int creatureId, AppDbContext db) =>
{
    var row = await db.Creatures.FirstOrDefaultAsync(c => c.CreatureId == creatureId && c.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var abilities = await db.CreatureAbilities
        .Where(a => a.CreatureId == creatureId && a.DateDeletedUtc == null)
        .OrderBy(a => a.SortOrder)
        .Select(a => new { a.AbilityType, a.Name, a.Description, a.SortOrder })
        .ToListAsync();

    return Results.Ok(new
    {
        row.CreatureId, row.GameSystemId, row.Name, row.Slug, row.Alias,
        row.CreatureType, row.Size, row.Alignment, row.ArmorClass, row.HitPoints,
        row.Speed, row.Strength, row.Dexterity, row.Constitution, row.Intelligence,
        row.Wisdom, row.Charisma, row.ChallengeRating, row.ProficiencyBonus,
        row.Description, row.SourceType, row.OwnerAppUserId, row.SourceMaterialId,
        row.CampaignId, row.SourcePage,
        TraitsList = abilities.Where(a => a.AbilityType == "Trait").Select(a => new { a.Name, a.Description, a.SortOrder }).ToList(),
        ActionsList = abilities.Where(a => a.AbilityType == "Action").Select(a => new { a.Name, a.Description, a.SortOrder }).ToList(),
        ReactionsList = abilities.Where(a => a.AbilityType == "Reaction").Select(a => new { a.Name, a.Description, a.SortOrder }).ToList(),
        LegendaryActionsList = abilities.Where(a => a.AbilityType == "LegendaryAction").Select(a => new { a.Name, a.Description, a.SortOrder }).ToList()
    });
}).WithTags("Creatures");


api.MapGet("/creatures/{creatureId:int}/export", async (int creatureId, AppDbContext db) =>
{
    var row = await db.Creatures.FirstOrDefaultAsync(c => c.CreatureId == creatureId && c.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var abilities = await db.CreatureAbilities
        .Where(a => a.CreatureId == creatureId && a.DateDeletedUtc == null)
        .OrderBy(a => a.SortOrder)
        .Select(a => new { a.AbilityType, a.Name, a.Description, a.SortOrder })
        .ToListAsync();

    var payload = new
    {
        Version = 1,
        ExportedUtc = DateTime.UtcNow,
        Creature = new
        {
            row.GameSystemId, row.Name, row.Alias, row.CreatureType, row.Size, row.Alignment,
            row.ArmorClass, row.HitPoints, row.Speed, row.Strength, row.Dexterity, row.Constitution,
            row.Intelligence, row.Wisdom, row.Charisma, row.ChallengeRating, row.ProficiencyBonus,
            row.Description, row.SourceType, row.OwnerAppUserId, row.SourceMaterialId, row.CampaignId, row.SourcePage,
            TraitsList = abilities.Where(a => a.AbilityType == "Trait").Select(a => new CreatureAbilityInput(a.Name, a.Description, a.SortOrder)).ToList(),
            ActionsList = abilities.Where(a => a.AbilityType == "Action").Select(a => new CreatureAbilityInput(a.Name, a.Description, a.SortOrder)).ToList(),
            ReactionsList = abilities.Where(a => a.AbilityType == "Reaction").Select(a => new CreatureAbilityInput(a.Name, a.Description, a.SortOrder)).ToList(),
            LegendaryActionsList = abilities.Where(a => a.AbilityType == "LegendaryAction").Select(a => new CreatureAbilityInput(a.Name, a.Description, a.SortOrder)).ToList()
        }
    };

    return Results.Ok(payload);
}).WithTags("Creatures");

api.MapPost("/creatures/import", async (CreatureImportRequest req, AppDbContext db, HttpContext http) =>
{
    if (http.User?.Identity?.IsAuthenticated != true) return Results.Unauthorized();

    var items = req.Creatures?.Where(c => c is not null).ToList() ?? new();
    if (items.Count == 0 && req.Creature is not null) items.Add(req.Creature);
    if (items.Count == 0) return await LoggedBadRequestAsync(db, http, "No creatures in import payload.");

    var createdIds = new List<int>();
    foreach (var c in items)
    {
        if (string.IsNullOrWhiteSpace(c.Name)) return await LoggedBadRequestAsync(db, http, "Imported creature Name is required.");
        var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == c.GameSystemId && gs.DateDeletedUtc == null);
        if (!gsExists) return await LoggedBadRequestAsync(db, http, $"Imported creature '{c.Name}' has invalid GameSystemId.");

        if (c.SourceType == SourceType.Official)
        {
            if (!c.SourceMaterialId.HasValue) return await LoggedBadRequestAsync(db, http, $"Imported creature '{c.Name}' requires SourceMaterialId for Official source.");
            if (c.CampaignId.HasValue) return await LoggedBadRequestAsync(db, http, $"Imported creature '{c.Name}' cannot use CampaignId for Official source.");
        }
        else if (c.SourceMaterialId.HasValue && c.CampaignId.HasValue)
        {
            return await LoggedBadRequestAsync(db, http, $"Imported creature '{c.Name}' must use SourceMaterialId or CampaignId, not both.");
        }

        if (c.SourceMaterialId.HasValue)
        {
            var sourceExists = await db.SourceMaterials.AnyAsync(sm => sm.SourceMaterialId == c.SourceMaterialId.Value && sm.GameSystemId == c.GameSystemId && sm.DateDeletedUtc == null);
            if (!sourceExists) return await LoggedBadRequestAsync(db, http, $"Imported creature '{c.Name}' has invalid SourceMaterialId.");
        }

        int? ownerUserId = null;
        if (c.SourceType != SourceType.Official)
        {
            if (c.OwnerAppUserId.HasValue)
            {
                var ownerExists = await db.AppUsers.AnyAsync(u => u.DateDeletedUtc == null && u.IsActive && u.AppUserId == c.OwnerAppUserId.Value);
                if (!ownerExists) return await LoggedBadRequestAsync(db, http, $"Imported creature '{c.Name}' has invalid OwnerAppUserId.");
                ownerUserId = c.OwnerAppUserId.Value;
            }
            else
            {
                var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(idRaw, out var currentUserId)) ownerUserId = currentUserId;
            }
        }

        var now = DateTime.UtcNow;
        var title = ToTitleCase(c.Name.Trim());
        var slug = await GenerateUniqueCreatureSlugAsync(db, c.GameSystemId, Slugify(title));

        var row = new Creature
        {
            GameSystemId = c.GameSystemId,
            Name = title,
            Slug = slug,
            Alias = c.Alias,
            CreatureType = c.CreatureType,
            Size = c.Size,
            Alignment = c.Alignment,
            ArmorClass = c.ArmorClass,
            HitPoints = c.HitPoints,
            Speed = c.Speed,
            Strength = c.Strength,
            Dexterity = c.Dexterity,
            Constitution = c.Constitution,
            Intelligence = c.Intelligence,
            Wisdom = c.Wisdom,
            Charisma = c.Charisma,
            ChallengeRating = c.ChallengeRating,
            ProficiencyBonus = c.ProficiencyBonus,
            Description = c.Description,
            SourceType = c.SourceType,
            OwnerAppUserId = ownerUserId,
            SourceMaterialId = c.SourceMaterialId,
            CampaignId = c.SourceType == SourceType.Official ? null : c.CampaignId,
            SourcePage = c.SourcePage,
            DateCreatedUtc = now,
            DateModifiedUtc = now
        };
        db.Creatures.Add(row);
        await db.SaveChangesAsync();

        await SyncCreatureAbilitiesAsync(db, row.CreatureId, c.TraitsList, c.ActionsList, c.ReactionsList, c.LegendaryActionsList);
        await db.SaveChangesAsync();
        createdIds.Add(row.CreatureId);
    }

    return Results.Ok(new { Imported = createdIds.Count, CreatureIds = createdIds });
}).WithTags("Creatures");

api.MapPost("/creatures", async (CreateCreatureRequest req, AppDbContext db, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return await LoggedBadRequestAsync(db, http, "Name is required.");
    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return await LoggedBadRequestAsync(db, http, "GameSystemId is invalid.");

    if (req.SourceType == SourceType.Official)
    {
        if (!req.SourceMaterialId.HasValue) return await LoggedBadRequestAsync(db, http, "Official creatures require SourceMaterialId.");
        if (req.CampaignId.HasValue) return await LoggedBadRequestAsync(db, http, "Official creatures cannot use CampaignId.");
    }
    else if (req.SourceMaterialId.HasValue && req.CampaignId.HasValue)
    {
        return await LoggedBadRequestAsync(db, http, "Use either SourceMaterialId or CampaignId, not both.");
    }

    if (req.SourceMaterialId.HasValue)
    {
        var sourceExists = await db.SourceMaterials.AnyAsync(sm => sm.SourceMaterialId == req.SourceMaterialId.Value && sm.GameSystemId == req.GameSystemId && sm.DateDeletedUtc == null);
        if (!sourceExists) return await LoggedBadRequestAsync(db, http, "SourceMaterialId is invalid for this system.");
    }

    if (req.CampaignId.HasValue)
    {
        var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var currentUserId)) return Results.Unauthorized();
        var allowed = await db.Campaigns.AnyAsync(c => c.CampaignId == req.CampaignId.Value && c.DateDeletedUtc == null &&
            (c.OwnerAppUserId == currentUserId || db.CampaignCollaborators.Any(cc => cc.CampaignId == c.CampaignId && cc.AppUserId == currentUserId && cc.DateDeletedUtc == null)));
        if (!allowed) return await LoggedBadRequestAsync(db, http, "CampaignId is invalid or inaccessible.");
    }

    int? ownerUserId = null;
    if (req.SourceType != SourceType.Official)
    {
        if (req.OwnerAppUserId.HasValue)
        {
            var ownerExists = await db.AppUsers.AnyAsync(u => u.DateDeletedUtc == null && u.IsActive && u.AppUserId == req.OwnerAppUserId.Value);
            if (!ownerExists) return await LoggedBadRequestAsync(db, http, "OwnerAppUserId is invalid.");
            ownerUserId = req.OwnerAppUserId.Value;
        }
        else
        {
            var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(idRaw, out var currentUserId)) ownerUserId = currentUserId;
        }
    }

    var now = DateTime.UtcNow;
    var title = ToTitleCase(req.Name.Trim());
    var slug = await GenerateUniqueCreatureSlugAsync(db, req.GameSystemId, Slugify(title));
    var row = new Creature
    {
        GameSystemId = req.GameSystemId,
        Name = title,
        Slug = slug,
        Alias = req.Alias,
        CreatureType = req.CreatureType,
        Size = req.Size,
        Alignment = req.Alignment,
        ArmorClass = req.ArmorClass,
        HitPoints = req.HitPoints,
        Speed = req.Speed,
        Strength = req.Strength,
        Dexterity = req.Dexterity,
        Constitution = req.Constitution,
        Intelligence = req.Intelligence,
        Wisdom = req.Wisdom,
        Charisma = req.Charisma,
        ChallengeRating = req.ChallengeRating,
        ProficiencyBonus = req.ProficiencyBonus,
        Description = req.Description,
                SourceType = req.SourceType,
        OwnerAppUserId = ownerUserId,
        SourceMaterialId = req.SourceMaterialId,
        CampaignId = req.SourceType == SourceType.Official ? null : req.CampaignId,
        SourcePage = req.SourcePage,
        DateCreatedUtc = now,
        DateModifiedUtc = now
    };
    db.Creatures.Add(row);
    await db.SaveChangesAsync();

    await SyncCreatureAbilitiesAsync(db, row.CreatureId, req.TraitsList, req.ActionsList, req.ReactionsList, req.LegendaryActionsList);
    await db.SaveChangesAsync();

    return Results.Ok(row);
}).WithTags("Creatures");

api.MapPut("/creatures/{creatureId:int}", async (int creatureId, CreateCreatureRequest req, AppDbContext db, HttpContext http) =>
{
    var row = await db.Creatures.FirstOrDefaultAsync(c => c.CreatureId == creatureId && c.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(req.Name)) return await LoggedBadRequestAsync(db, http, "Name is required.");

    if (req.SourceType == SourceType.Official)
    {
        if (!req.SourceMaterialId.HasValue) return await LoggedBadRequestAsync(db, http, "Official creatures require SourceMaterialId.");
        if (req.CampaignId.HasValue) return await LoggedBadRequestAsync(db, http, "Official creatures cannot use CampaignId.");
    }
    else if (req.SourceMaterialId.HasValue && req.CampaignId.HasValue)
    {
        return await LoggedBadRequestAsync(db, http, "Use either SourceMaterialId or CampaignId, not both.");
    }

    if (req.SourceMaterialId.HasValue)
    {
        var sourceExists = await db.SourceMaterials.AnyAsync(sm => sm.SourceMaterialId == req.SourceMaterialId.Value && sm.GameSystemId == req.GameSystemId && sm.DateDeletedUtc == null);
        if (!sourceExists) return await LoggedBadRequestAsync(db, http, "SourceMaterialId is invalid for this system.");
    }

    if (req.CampaignId.HasValue)
    {
        var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var currentUserId)) return Results.Unauthorized();
        var allowed = await db.Campaigns.AnyAsync(c => c.CampaignId == req.CampaignId.Value && c.DateDeletedUtc == null &&
            (c.OwnerAppUserId == currentUserId || db.CampaignCollaborators.Any(cc => cc.CampaignId == c.CampaignId && cc.AppUserId == currentUserId && cc.DateDeletedUtc == null)));
        if (!allowed) return await LoggedBadRequestAsync(db, http, "CampaignId is invalid or inaccessible.");
    }

    int? ownerUserId = row.OwnerAppUserId;
    if (req.SourceType == SourceType.Official)
    {
        ownerUserId = null;
    }
    else
    {
        if (req.OwnerAppUserId.HasValue)
        {
            var ownerExists = await db.AppUsers.AnyAsync(u => u.DateDeletedUtc == null && u.IsActive && u.AppUserId == req.OwnerAppUserId.Value);
            if (!ownerExists) return await LoggedBadRequestAsync(db, http, "OwnerAppUserId is invalid.");
            ownerUserId = req.OwnerAppUserId.Value;
        }
        else if (!ownerUserId.HasValue)
        {
            var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(idRaw, out var currentUserId)) ownerUserId = currentUserId;
        }
    }

    row.GameSystemId = req.GameSystemId;
    row.Name = ToTitleCase(req.Name.Trim());
    row.Alias = req.Alias;
    row.CreatureType = req.CreatureType;
    row.Size = req.Size;
    row.Alignment = req.Alignment;
    row.ArmorClass = req.ArmorClass;
    row.HitPoints = req.HitPoints;
    row.Speed = req.Speed;
    row.Strength = req.Strength;
    row.Dexterity = req.Dexterity;
    row.Constitution = req.Constitution;
    row.Intelligence = req.Intelligence;
    row.Wisdom = req.Wisdom;
    row.Charisma = req.Charisma;
    row.ChallengeRating = req.ChallengeRating;
    row.ProficiencyBonus = req.ProficiencyBonus;
    row.Description = req.Description;
        row.SourceType = req.SourceType;
    row.OwnerAppUserId = ownerUserId;
    row.SourceMaterialId = req.SourceMaterialId;
    row.CampaignId = req.SourceType == SourceType.Official ? null : req.CampaignId;
    row.SourcePage = req.SourcePage;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    await SyncCreatureAbilitiesAsync(db, row.CreatureId, req.TraitsList, req.ActionsList, req.ReactionsList, req.LegendaryActionsList);
    await db.SaveChangesAsync();

    return Results.Ok(row);
}).WithTags("Creatures");

api.MapDelete("/creatures/{creatureId:int}", async (int creatureId, AppDbContext db) =>
{
    var row = await db.Creatures.FirstOrDefaultAsync(c => c.CreatureId == creatureId && c.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.NoContent();
}).WithTags("Creatures");

api.MapGet("/campaigns/accessible", async (AppDbContext db, HttpContext http) =>
{
    if (http.User?.Identity?.IsAuthenticated != true) return Results.Unauthorized();

    var isAdmin = string.Equals(http.User.FindFirstValue(ClaimTypes.Role), "Admin", StringComparison.OrdinalIgnoreCase);
    if (isAdmin)
    {
        var allRows = await db.Campaigns
            .Where(c => c.DateDeletedUtc == null)
            .OrderBy(c => c.Title)
            .Select(c => new { c.CampaignId, c.Title, c.Description })
            .ToListAsync();

        return Results.Ok(allRows);
    }

    var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!int.TryParse(idRaw, out var userId)) return Results.Unauthorized();

    var rows = await db.Campaigns
        .Where(c => c.DateDeletedUtc == null &&
               (c.OwnerAppUserId == userId ||
                db.CampaignCollaborators.Any(cc => cc.CampaignId == c.CampaignId && cc.AppUserId == userId && cc.DateDeletedUtc == null)))
        .OrderBy(c => c.Title)
        .Select(c => new { c.CampaignId, c.Title, c.Description })
        .ToListAsync();

    return Results.Ok(rows);
}).WithTags("Campaigns");

api.MapGet("/campaigns", async (AppDbContext db) =>
    await db.Campaigns
        .Where(c => c.DateDeletedUtc == null)
        .OrderBy(c => c.Title)
        .Select(c => new
        {
            c.CampaignId,
            c.Title,
            c.Description,
            c.OwnerAppUserId,
            OwnerUsername = db.AppUsers.Where(u => u.AppUserId == c.OwnerAppUserId && u.DateDeletedUtc == null).Select(u => u.Username).FirstOrDefault(),
            CollaboratorCount = db.CampaignCollaborators.Count(cc => cc.CampaignId == c.CampaignId && cc.DateDeletedUtc == null),
            PlayerCount = db.CampaignPlayers.Count(cp => cp.CampaignId == c.CampaignId && cp.DateDeletedUtc == null)
        })
        .ToListAsync())
    .WithTags("Campaigns");

api.MapGet("/campaigns/{campaignId:int}", async (int campaignId, AppDbContext db) =>
{
    var row = await db.Campaigns.FirstOrDefaultAsync(c => c.CampaignId == campaignId && c.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var collaborators = await db.CampaignCollaborators.Where(cc => cc.CampaignId == campaignId && cc.DateDeletedUtc == null).Select(cc => cc.AppUserId).ToListAsync();
    var players = await db.CampaignPlayers.Where(cp => cp.CampaignId == campaignId && cp.DateDeletedUtc == null).Select(cp => cp.AppUserId).ToListAsync();

    var ownerUsername = await db.AppUsers.Where(u => u.AppUserId == row.OwnerAppUserId && u.DateDeletedUtc == null).Select(u => u.Username).FirstOrDefaultAsync();

    return Results.Ok(new
    {
        row.CampaignId,
        row.Title,
        row.Description,
        row.OwnerAppUserId,
        OwnerUsername = ownerUsername,
        CollaboratorUserIds = collaborators,
        PlayerUserIds = players
    });
}).WithTags("Campaigns");

api.MapPost("/campaigns", async (UpsertCampaignRequest req, AppDbContext db, HttpContext http) =>
{
    if (http.User?.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Title)) return await LoggedBadRequestAsync(db, http, "Title is required.");
    var title = ToTitleCase(req.Title.Trim());

    var dup = await db.Campaigns.AnyAsync(c => c.DateDeletedUtc == null && c.Title == title);
    if (dup) return await LoggedBadRequestAsync(db, http, "Campaign title already exists.");

    int? ownerUserId = req.OwnerAppUserId;
    if (!ownerUserId.HasValue)
    {
        var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var uid)) return Results.Unauthorized();
        ownerUserId = uid;
    }

    if (ownerUserId.HasValue)
    {
        var exists = await db.AppUsers.AnyAsync(u => u.AppUserId == ownerUserId.Value && u.DateDeletedUtc == null && u.IsActive);
        if (!exists) return Results.BadRequest("OwnerAppUserId is invalid.");
    }

    var now = DateTime.UtcNow;
    var row = new Campaign
    {
        Title = title,
        Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
        OwnerAppUserId = ownerUserId,
        DateCreatedUtc = now,
        DateModifiedUtc = now
    };
    db.Campaigns.Add(row);
    await db.SaveChangesAsync();

    await SyncCampaignMembershipsAsync(db, row.CampaignId, req.CollaboratorUserIds, req.PlayerUserIds);
    await db.SaveChangesAsync();

    return Results.Ok(row);
}).WithTags("Campaigns");

api.MapPut("/campaigns/{campaignId:int}", async (int campaignId, UpsertCampaignRequest req, AppDbContext db, HttpContext http) =>
{
    if (http.User?.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var row = await db.Campaigns.FirstOrDefaultAsync(c => c.CampaignId == campaignId && c.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(req.Title)) return await LoggedBadRequestAsync(db, http, "Title is required.");

    var title = ToTitleCase(req.Title.Trim());
    var dup = await db.Campaigns.AnyAsync(c => c.DateDeletedUtc == null && c.Title == title && c.CampaignId != campaignId);
    if (dup) return await LoggedBadRequestAsync(db, http, "Campaign title already exists.");

    if (req.OwnerAppUserId.HasValue)
    {
        var ownerExists = await db.AppUsers.AnyAsync(u => u.AppUserId == req.OwnerAppUserId.Value && u.DateDeletedUtc == null && u.IsActive);
        if (!ownerExists) return await LoggedBadRequestAsync(db, http, "OwnerAppUserId is invalid.");
    }

    row.Title = title;
    row.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
    row.OwnerAppUserId = req.OwnerAppUserId;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    await SyncCampaignMembershipsAsync(db, row.CampaignId, req.CollaboratorUserIds, req.PlayerUserIds);
    await db.SaveChangesAsync();

    return Results.Ok(row);
}).WithTags("Campaigns");

api.MapDelete("/campaigns/{campaignId:int}", async (int campaignId, AppDbContext db, HttpContext http) =>
{
    if (http.User?.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var row = await db.Campaigns.FirstOrDefaultAsync(c => c.CampaignId == campaignId && c.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.NoContent();
}).WithTags("Campaigns");


api.MapGet("/admin/errors", async (string? q, int? limit, AppDbContext db) =>
{
    var query = db.AppErrors.AsQueryable();
    if (!string.IsNullOrWhiteSpace(q))
    {
        q = q.Trim();
        query = query.Where(e => e.ErrorUid.Contains(q) || (e.Path ?? "").Contains(q) || (e.Message ?? "").Contains(q));
    }

    var take = Math.Clamp(limit ?? 100, 1, 500);
    var rows = await query.OrderByDescending(e => e.DateCreatedUtc).Take(take)
        .Select(e => new { e.AppErrorId, e.ErrorUid, e.Path, e.Method, e.UserId, e.Message, e.DateCreatedUtc })
        .ToListAsync();
    return Results.Ok(rows);
}).WithTags("Admin");

api.MapGet("/admin/errors/{errorUid}", async (string errorUid, AppDbContext db) =>
{
    var e = await db.AppErrors.FirstOrDefaultAsync(x => x.ErrorUid == errorUid);
    return e is null ? Results.NotFound() : Results.Ok(e);
}).WithTags("Admin");

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

api.MapGet("/users", async (AppDbContext db) =>
    await db.AppUsers
        .Where(u => u.DateDeletedUtc == null && u.IsActive)
        .OrderBy(u => u.Username)
        .Select(u => new { u.AppUserId, u.Username, u.Email })
        .ToListAsync())
    .WithTags("Users");



api.MapPost("/friends/requests", async (SendFriendRequestRequest req, AppDbContext db, HttpContext http) =>
{
    if (http.User?.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!int.TryParse(idRaw, out var fromUserId)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.ToUsername)) return await LoggedBadRequestAsync(db, http, "ToUsername is required.");

    var toUsername = req.ToUsername.Trim().ToLowerInvariant();
    var toUser = await db.AppUsers.FirstOrDefaultAsync(u => u.DateDeletedUtc == null && u.IsActive && u.Username == toUsername);
    if (toUser is null) return await LoggedBadRequestAsync(db, http, "Target user not found.");
    if (toUser.AppUserId == fromUserId) return await LoggedBadRequestAsync(db, http, "Cannot friend yourself.");

    var a = Math.Min(fromUserId, toUser.AppUserId);
    var b = Math.Max(fromUserId, toUser.AppUserId);

    var alreadyFriends = await db.Friends.AnyAsync(f => f.UserAId == a && f.UserBId == b);
    if (alreadyFriends) return await LoggedBadRequestAsync(db, http, "You are already friends.");

    var pending = await db.FriendRequests.AnyAsync(fr => fr.Status == "Pending" && ((fr.FromAppUserId == fromUserId && fr.ToAppUserId == toUser.AppUserId) || (fr.FromAppUserId == toUser.AppUserId && fr.ToAppUserId == fromUserId)));
    if (pending) return await LoggedBadRequestAsync(db, http, "A pending friend request already exists.");

    db.FriendRequests.Add(new FriendRequest { FromAppUserId = fromUserId, ToAppUserId = toUser.AppUserId, Status = "Pending", DateCreatedUtc = DateTime.UtcNow });
    await db.SaveChangesAsync();
    return Results.Ok(new { ok = true });
}).WithTags("Friends");

api.MapGet("/friends/requests/incoming", async (AppDbContext db, HttpContext http) =>
{
    if (http.User?.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!int.TryParse(idRaw, out var userId)) return Results.Unauthorized();

    var rows = await db.FriendRequests
        .Where(fr => fr.Status == "Pending" && fr.ToAppUserId == userId)
        .OrderByDescending(fr => fr.DateCreatedUtc)
        .Select(fr => new
        {
            fr.FriendRequestId,
            fr.DateCreatedUtc,
            FromUser = db.AppUsers.Where(u => u.AppUserId == fr.FromAppUserId).Select(u => new { u.AppUserId, u.Username, u.Email }).FirstOrDefault()
        })
        .ToListAsync();

    return Results.Ok(rows);
}).WithTags("Friends");

api.MapPost("/friends/requests/{friendRequestId:int}/approve", async (int friendRequestId, AppDbContext db, HttpContext http) =>
{
    if (http.User?.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!int.TryParse(idRaw, out var userId)) return Results.Unauthorized();

    var fr = await db.FriendRequests.FirstOrDefaultAsync(x => x.FriendRequestId == friendRequestId && x.Status == "Pending");
    if (fr is null) return Results.NotFound();
    if (fr.ToAppUserId != userId) return Results.Forbid();

    fr.Status = "Approved";
    fr.DateResolvedUtc = DateTime.UtcNow;

    var a = Math.Min(fr.FromAppUserId, fr.ToAppUserId);
    var b = Math.Max(fr.FromAppUserId, fr.ToAppUserId);
    var exists = await db.Friends.AnyAsync(f => f.UserAId == a && f.UserBId == b);
    if (!exists) db.Friends.Add(new Friend { UserAId = a, UserBId = b, DateCreatedUtc = DateTime.UtcNow });

    await db.SaveChangesAsync();
    return Results.Ok(new { ok = true });
}).WithTags("Friends");

api.MapPost("/friends/requests/{friendRequestId:int}/decline", async (int friendRequestId, AppDbContext db, HttpContext http) =>
{
    if (http.User?.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!int.TryParse(idRaw, out var userId)) return Results.Unauthorized();

    var fr = await db.FriendRequests.FirstOrDefaultAsync(x => x.FriendRequestId == friendRequestId && x.Status == "Pending");
    if (fr is null) return Results.NotFound();
    if (fr.ToAppUserId != userId) return Results.Forbid();

    fr.Status = "Declined";
    fr.DateResolvedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { ok = true });
}).WithTags("Friends");

api.MapGet("/friends", async (AppDbContext db, HttpContext http) =>
{
    if (http.User?.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!int.TryParse(idRaw, out var userId)) return Results.Unauthorized();

    var links = await db.Friends
        .Where(f => f.UserAId == userId || f.UserBId == userId)
        .ToListAsync();

    var ids = links.Select(f => f.UserAId == userId ? f.UserBId : f.UserAId).Distinct().ToList();
    var users = await db.AppUsers
        .Where(u => u.DateDeletedUtc == null && u.IsActive && ids.Contains(u.AppUserId))
        .OrderBy(u => u.Username)
        .Select(u => new { u.AppUserId, u.Username, u.Email })
        .ToListAsync();

    return Results.Ok(users);
}).WithTags("Friends");

api.MapGet("/users/{username}/summary", async (string username, AppDbContext db) =>
{
    var uname = username.Trim().ToLowerInvariant();
    var user = await db.AppUsers
        .Where(x => x.DateDeletedUtc == null && x.IsActive && x.Username == uname)
        .Select(x => new { x.AppUserId, x.Username, x.Email, x.Role })
        .FirstOrDefaultAsync();

    if (user is null) return Results.NotFound();

    var ownerCampaigns = await db.Campaigns
        .Where(c => c.DateDeletedUtc == null && c.OwnerAppUserId == user.AppUserId)
        .OrderBy(c => c.Title)
        .Select(c => new { c.CampaignId, c.Title, Relationship = "Owner" })
        .ToListAsync();

    var collaboratorCampaigns = await db.CampaignCollaborators
        .Where(cc => cc.DateDeletedUtc == null && cc.AppUserId == user.AppUserId)
        .Join(db.Campaigns.Where(c => c.DateDeletedUtc == null), cc => cc.CampaignId, c => c.CampaignId,
            (cc, c) => new { c.CampaignId, c.Title, Relationship = "Collaborator" })
        .OrderBy(x => x.Title)
        .ToListAsync();

    var playerCampaigns = await db.CampaignPlayers
        .Where(cp => cp.DateDeletedUtc == null && cp.AppUserId == user.AppUserId)
        .Join(db.Campaigns.Where(c => c.DateDeletedUtc == null), cp => cp.CampaignId, c => c.CampaignId,
            (cp, c) => new { c.CampaignId, c.Title, Relationship = "Player" })
        .OrderBy(x => x.Title)
        .ToListAsync();

    var campaigns = ownerCampaigns
        .Concat(collaboratorCampaigns)
        .Concat(playerCampaigns)
        .GroupBy(x => x.CampaignId)
        .Select(g => new { g.Key, g.First().Title, Relationships = g.Select(x => x.Relationship).Distinct().ToList() })
        .OrderBy(x => x.Title)
        .ToList();

    var items = await db.Items
        .Where(i => i.DateDeletedUtc == null && i.OwnerAppUserId == user.AppUserId)
        .OrderBy(i => i.Name)
        .Select(i => new { i.ItemId, i.Name, i.Slug, Relationship = "Owner" })
        .ToListAsync();

    var friendCount = await db.Friends.CountAsync(f => f.UserAId == user.AppUserId || f.UserBId == user.AppUserId);

    return Results.Ok(new
    {
        user,
        campaignCount = campaigns.Count,
        itemCount = items.Count,
        friendCount,
        campaigns,
        items
    });
}).WithTags("Users");

api.MapGet("/users/{username}", async (string username, AppDbContext db) =>
{
    var u = await db.AppUsers
        .Where(x => x.DateDeletedUtc == null && x.IsActive && x.Username == username.ToLower())
        .Select(x => new { x.AppUserId, x.Username, x.Email, x.Role })
        .FirstOrDefaultAsync();
    return u is null ? Results.NotFound() : Results.Ok(u);
})
    .WithTags("Users");

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
    if (row is null) return Results.NotFound();

    var tagLinks = await db.ItemTags
        .Where(it => it.DateDeletedUtc == null && it.ItemId == row.ItemId)
        .ToListAsync();

    var tagIds = tagLinks.Select(t => t.TagDefinitionId).Distinct().ToList();
    var tags = await db.TagDefinitions
        .Where(t => t.DateDeletedUtc == null && tagIds.Contains(t.TagDefinitionId))
        .ToListAsync();

    return Results.Ok(new
    {
        row.ItemId,
        row.GameSystemId,
        row.Name,
        row.Slug,
        row.Alias,
        row.ItemTypeDefinitionId,
        row.OwnerAppUserId,
        row.RarityDefinitionId,
        row.Description,
        row.CostAmount,
        row.CurrencyDefinitionId,
        row.CostCurrency,
        row.Weight,
        row.Quantity,
        row.Tags,
        row.Effect,
        row.RequiresAttunement,
        row.AttunementRequirement,
        row.DamageDice,
        row.DamageType,
        row.VersatileDamageDice,
        row.ArmorClass,
        row.StrengthRequirement,
        row.StealthDisadvantage,
        row.RangeNormal,
        row.RangeLong,
        row.SourceMaterialId,
        row.CampaignId,
        row.SourceBook,
        row.SourcePage,
        row.IsConsumable,
        row.ChargesCurrent,
        row.ChargesMax,
        row.RechargeRule,
        row.UsesPerDay,
        row.ArmorCategory,
        row.WeaponPropertyLight,
        row.WeaponPropertyHeavy,
        row.WeaponPropertyFinesse,
        row.WeaponPropertyThrown,
        row.WeaponPropertyTwoHanded,
        row.WeaponPropertyLoading,
        row.WeaponPropertyReach,
        row.WeaponPropertyAmmunition,
        row.SourceType,
        row.DateCreatedUtc,
        row.DateModifiedUtc,
        row.DateDeletedUtc,
        TagDefinitionIds = tagLinks.Select(l => l.TagDefinitionId).ToList(),
        TagNames = tags.Select(t => t.Name).ToList()
    });
}).WithTags("Items");

api.MapGet("/items", async (int gameSystemId, AppDbContext db) =>
{
    var items = await db.Items.Where(x => x.DateDeletedUtc == null && x.GameSystemId == gameSystemId).OrderBy(x => x.Name).ToListAsync();
    var itemIds = items.Select(i => i.ItemId).ToList();
    var tagLinks = await db.ItemTags.Where(it => it.DateDeletedUtc == null && itemIds.Contains(it.ItemId)).ToListAsync();
    var tagIds = tagLinks.Select(t => t.TagDefinitionId).Distinct().ToList();
    var tags = await db.TagDefinitions.Where(t => t.DateDeletedUtc == null && tagIds.Contains(t.TagDefinitionId)).ToListAsync();

    var outRows = items.Select(i => new {
        i.ItemId,i.GameSystemId,i.Name,i.Slug,i.Alias,i.ItemTypeDefinitionId,i.OwnerAppUserId,i.RarityDefinitionId,i.Description,
        i.CostAmount,i.CurrencyDefinitionId,i.CostCurrency,i.Weight,i.Quantity,i.Tags,
        i.DamageDice,i.DamageType,i.VersatileDamageDice,i.ArmorClass,i.StrengthRequirement,i.StealthDisadvantage,i.RangeNormal,i.RangeLong,i.SourceMaterialId,i.CampaignId,i.SourceBook,i.SourcePage,
        i.IsConsumable,i.ChargesCurrent,i.ChargesMax,i.RechargeRule,i.UsesPerDay,
        i.ArmorCategory,i.WeaponPropertyLight,i.WeaponPropertyHeavy,i.WeaponPropertyFinesse,i.WeaponPropertyThrown,i.WeaponPropertyTwoHanded,i.WeaponPropertyLoading,i.WeaponPropertyReach,i.WeaponPropertyAmmunition,
        i.SourceType,i.DateCreatedUtc,i.DateModifiedUtc,i.DateDeletedUtc,
        TagDefinitionIds = tagLinks.Where(l=>l.ItemId==i.ItemId).Select(l=>l.TagDefinitionId).ToList(),
        TagNames = tags.Where(t=>tagLinks.Any(l=>l.ItemId==i.ItemId && l.TagDefinitionId==t.TagDefinitionId)).Select(t=>t.Name).ToList()
    }).ToList();

    return Results.Ok(outRows);
}).WithTags("Items");

api.MapPost("/items", async (CreateItemRequest req, AppDbContext db, HttpContext http) =>
{
    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name is required.");

    string? itemTypeName = null;
    if (req.ItemTypeDefinitionId.HasValue)
    {
        var itemType = await db.ItemTypeDefinitions.FirstOrDefaultAsync(t => t.ItemTypeDefinitionId == req.ItemTypeDefinitionId.Value && t.GameSystemId == req.GameSystemId && t.DateDeletedUtc == null);
        if (itemType is null) return Results.BadRequest("ItemTypeDefinitionId is invalid for this system.");
        itemTypeName = itemType.Name;
    }

    if (req.RarityDefinitionId.HasValue)
    {
        var rarityExists = await db.RarityDefinitions.AnyAsync(r => r.RarityDefinitionId == req.RarityDefinitionId.Value && r.GameSystemId == req.GameSystemId && r.DateDeletedUtc == null);
        if (!rarityExists) return await LoggedBadRequestAsync(db, http, "RarityDefinitionId is invalid for this system.");
    }

    if (req.CurrencyDefinitionId.HasValue)
    {
        var currencyExists = await db.CurrencyDefinitions.AnyAsync(c => c.CurrencyDefinitionId == req.CurrencyDefinitionId.Value && c.GameSystemId == req.GameSystemId && c.DateDeletedUtc == null);
        if (!currencyExists) return await LoggedBadRequestAsync(db, http, "CurrencyDefinitionId is invalid for this system.");
    }

    if (req.SourceMaterialId.HasValue)
    {
        var sourceExists = await db.SourceMaterials.AnyAsync(sm => sm.SourceMaterialId == req.SourceMaterialId.Value && sm.GameSystemId == req.GameSystemId && sm.DateDeletedUtc == null);
        if (!sourceExists) return await LoggedBadRequestAsync(db, http, "SourceMaterialId is invalid for this system.");
    }

    if (req.CampaignId.HasValue)
    {
        var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var currentUserId)) return Results.Unauthorized();

        var campaignAllowed = await db.Campaigns.AnyAsync(c => c.CampaignId == req.CampaignId.Value && c.DateDeletedUtc == null &&
            (c.OwnerAppUserId == currentUserId || db.CampaignCollaborators.Any(cc => cc.CampaignId == c.CampaignId && cc.AppUserId == currentUserId && cc.DateDeletedUtc == null)));
        if (!campaignAllowed) return await LoggedBadRequestAsync(db, http, "CampaignId is invalid or inaccessible.");
    }

    // Source exclusivity
    if (req.SourceType == SourceType.Official)
    {
        if (!req.SourceMaterialId.HasValue)
            return await LoggedBadRequestAsync(db, http, "Official items require SourceMaterialId.");
        if (req.CampaignId.HasValue)
            return await LoggedBadRequestAsync(db, http, "Official items cannot use CampaignId.");
    }
    else
    {
        if (req.SourceMaterialId.HasValue && req.CampaignId.HasValue)
            return await LoggedBadRequestAsync(db, http, "Use either SourceMaterialId or CampaignId, not both.");
    }

    var validationError = ValidateItemRequest(req);
    if (validationError is not null) return await LoggedBadRequestAsync(db, http, validationError);
    var typeValidationError = ValidateItemRequestByType(req, itemTypeName);
    if (typeValidationError is not null) return await LoggedBadRequestAsync(db, http, typeValidationError);

    int? ownerUserId = null;
    if (req.SourceType != SourceType.Official)
    {
        if (req.OwnerAppUserId.HasValue)
        {
            var ownerExists = await db.AppUsers.AnyAsync(u => u.DateDeletedUtc == null && u.IsActive && u.AppUserId == req.OwnerAppUserId.Value);
            if (!ownerExists) return await LoggedBadRequestAsync(db, http, "OwnerAppUserId is invalid.");
            ownerUserId = req.OwnerAppUserId.Value;
        }
        else
        {
            var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(idRaw, out var currentUserId)) ownerUserId = currentUserId;
        }
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
        OwnerAppUserId = ownerUserId,
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
        DamageDice = string.IsNullOrWhiteSpace(req.DamageDice) ? null : req.DamageDice.Trim(),
        DamageType = string.IsNullOrWhiteSpace(req.DamageType) ? null : req.DamageType.Trim(),
        VersatileDamageDice = string.IsNullOrWhiteSpace(req.VersatileDamageDice) ? null : req.VersatileDamageDice.Trim(),
        ArmorClass = req.ArmorClass,
        StrengthRequirement = req.StrengthRequirement,
        StealthDisadvantage = req.StealthDisadvantage,
        RangeNormal = req.RangeNormal,
        RangeLong = req.RangeLong,
        SourceMaterialId = req.SourceMaterialId,
        CampaignId = req.SourceType == SourceType.Official ? null : req.CampaignId,
        SourceBook = string.IsNullOrWhiteSpace(req.SourceBook) ? null : req.SourceBook.Trim(),
        SourcePage = req.SourcePage,
        IsConsumable = req.IsConsumable,
        ChargesCurrent = req.ChargesCurrent,
        ChargesMax = req.ChargesMax,
        RechargeRule = string.IsNullOrWhiteSpace(req.RechargeRule) ? null : req.RechargeRule.Trim(),
        UsesPerDay = req.UsesPerDay,
        ArmorCategory = string.IsNullOrWhiteSpace(req.ArmorCategory) ? null : req.ArmorCategory.Trim(),
        WeaponPropertyLight = req.WeaponPropertyLight,
        WeaponPropertyHeavy = req.WeaponPropertyHeavy,
        WeaponPropertyFinesse = req.WeaponPropertyFinesse,
        WeaponPropertyThrown = req.WeaponPropertyThrown,
        WeaponPropertyTwoHanded = req.WeaponPropertyTwoHanded,
        WeaponPropertyLoading = req.WeaponPropertyLoading,
        WeaponPropertyReach = req.WeaponPropertyReach,
        WeaponPropertyAmmunition = req.WeaponPropertyAmmunition,
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

api.MapPut("/items/{itemId:int}", async (int itemId, CreateItemRequest req, AppDbContext db, HttpContext http) =>
{
    var row = await db.Items.FirstOrDefaultAsync(x => x.ItemId == itemId && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name is required.");

    string? itemTypeName = null;
    if (req.ItemTypeDefinitionId.HasValue)
    {
        var itemType = await db.ItemTypeDefinitions.FirstOrDefaultAsync(t => t.ItemTypeDefinitionId == req.ItemTypeDefinitionId.Value && t.GameSystemId == req.GameSystemId && t.DateDeletedUtc == null);
        if (itemType is null) return Results.BadRequest("ItemTypeDefinitionId is invalid for this system.");
        itemTypeName = itemType.Name;
    }

    if (req.RarityDefinitionId.HasValue)
    {
        var rarityExists = await db.RarityDefinitions.AnyAsync(r => r.RarityDefinitionId == req.RarityDefinitionId.Value && r.GameSystemId == req.GameSystemId && r.DateDeletedUtc == null);
        if (!rarityExists) return await LoggedBadRequestAsync(db, http, "RarityDefinitionId is invalid for this system.");
    }

    if (req.CurrencyDefinitionId.HasValue)
    {
        var currencyExists = await db.CurrencyDefinitions.AnyAsync(c => c.CurrencyDefinitionId == req.CurrencyDefinitionId.Value && c.GameSystemId == req.GameSystemId && c.DateDeletedUtc == null);
        if (!currencyExists) return await LoggedBadRequestAsync(db, http, "CurrencyDefinitionId is invalid for this system.");
    }

    if (req.SourceMaterialId.HasValue)
    {
        var sourceExists = await db.SourceMaterials.AnyAsync(sm => sm.SourceMaterialId == req.SourceMaterialId.Value && sm.GameSystemId == req.GameSystemId && sm.DateDeletedUtc == null);
        if (!sourceExists) return await LoggedBadRequestAsync(db, http, "SourceMaterialId is invalid for this system.");
    }

    if (req.CampaignId.HasValue)
    {
        var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var currentUserId)) return Results.Unauthorized();

        var campaignAllowed = await db.Campaigns.AnyAsync(c => c.CampaignId == req.CampaignId.Value && c.DateDeletedUtc == null &&
            (c.OwnerAppUserId == currentUserId || db.CampaignCollaborators.Any(cc => cc.CampaignId == c.CampaignId && cc.AppUserId == currentUserId && cc.DateDeletedUtc == null)));
        if (!campaignAllowed) return await LoggedBadRequestAsync(db, http, "CampaignId is invalid or inaccessible.");
    }

    // Source exclusivity
    if (req.SourceType == SourceType.Official)
    {
        if (!req.SourceMaterialId.HasValue)
            return await LoggedBadRequestAsync(db, http, "Official items require SourceMaterialId.");
        if (req.CampaignId.HasValue)
            return await LoggedBadRequestAsync(db, http, "Official items cannot use CampaignId.");
    }
    else
    {
        if (req.SourceMaterialId.HasValue && req.CampaignId.HasValue)
            return await LoggedBadRequestAsync(db, http, "Use either SourceMaterialId or CampaignId, not both.");
    }

    var validationError = ValidateItemRequest(req);
    if (validationError is not null) return await LoggedBadRequestAsync(db, http, validationError);
    var typeValidationError = ValidateItemRequestByType(req, itemTypeName);
    if (typeValidationError is not null) return await LoggedBadRequestAsync(db, http, typeValidationError);

    int? ownerUserId = row.OwnerAppUserId;
    if (req.SourceType == SourceType.Official)
    {
        ownerUserId = null;
    }
    else
    {
        if (req.OwnerAppUserId.HasValue)
        {
            var ownerExists = await db.AppUsers.AnyAsync(u => u.DateDeletedUtc == null && u.IsActive && u.AppUserId == req.OwnerAppUserId.Value);
            if (!ownerExists) return await LoggedBadRequestAsync(db, http, "OwnerAppUserId is invalid.");
            ownerUserId = req.OwnerAppUserId.Value;
        }
        else if (!ownerUserId.HasValue)
        {
            var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(idRaw, out var currentUserId)) ownerUserId = currentUserId;
        }
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
    row.OwnerAppUserId = ownerUserId;
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
    row.DamageDice = string.IsNullOrWhiteSpace(req.DamageDice) ? null : req.DamageDice.Trim();
    row.DamageType = string.IsNullOrWhiteSpace(req.DamageType) ? null : req.DamageType.Trim();
    row.VersatileDamageDice = string.IsNullOrWhiteSpace(req.VersatileDamageDice) ? null : req.VersatileDamageDice.Trim();
    row.ArmorClass = req.ArmorClass;
    row.StrengthRequirement = req.StrengthRequirement;
    row.StealthDisadvantage = req.StealthDisadvantage;
    row.RangeNormal = req.RangeNormal;
    row.RangeLong = req.RangeLong;
    row.SourceMaterialId = req.SourceMaterialId;
    row.CampaignId = req.SourceType == SourceType.Official ? null : req.CampaignId;
    row.SourceBook = string.IsNullOrWhiteSpace(req.SourceBook) ? null : req.SourceBook.Trim();
    row.SourcePage = req.SourcePage;
    row.IsConsumable = req.IsConsumable;
    row.ChargesCurrent = req.ChargesCurrent;
    row.ChargesMax = req.ChargesMax;
    row.RechargeRule = string.IsNullOrWhiteSpace(req.RechargeRule) ? null : req.RechargeRule.Trim();
    row.UsesPerDay = req.UsesPerDay;
    row.ArmorCategory = string.IsNullOrWhiteSpace(req.ArmorCategory) ? null : req.ArmorCategory.Trim();
    row.WeaponPropertyLight = req.WeaponPropertyLight;
    row.WeaponPropertyHeavy = req.WeaponPropertyHeavy;
    row.WeaponPropertyFinesse = req.WeaponPropertyFinesse;
    row.WeaponPropertyThrown = req.WeaponPropertyThrown;
    row.WeaponPropertyTwoHanded = req.WeaponPropertyTwoHanded;
    row.WeaponPropertyLoading = req.WeaponPropertyLoading;
    row.WeaponPropertyReach = req.WeaponPropertyReach;
    row.WeaponPropertyAmmunition = req.WeaponPropertyAmmunition;
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








api.MapGet("/source-materials", async (int gameSystemId, AppDbContext db) =>
    await db.SourceMaterials
        .Where(x => x.DateDeletedUtc == null && x.GameSystemId == gameSystemId)
        .OrderBy(x => x.Code)
        .Select(x => new { x.SourceMaterialId, x.GameSystemId, x.Code, x.Title, x.Publisher, x.IsOfficial })
        .ToListAsync())
    .WithTags("Source Materials");

api.MapPost("/source-materials", async (UpsertSourceMaterialRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Title))
        return Results.BadRequest("Code and Title are required.");

    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");

    var code = req.Code.Trim().ToUpperInvariant();
    var existing = await db.SourceMaterials.FirstOrDefaultAsync(x => x.DateDeletedUtc == null && x.GameSystemId == req.GameSystemId && x.Code == code);
    var now = DateTime.UtcNow;

    if (existing is null)
    {
        existing = new SourceMaterial
        {
            GameSystemId = req.GameSystemId,
            Code = code,
            Title = req.Title.Trim(),
            Publisher = string.IsNullOrWhiteSpace(req.Publisher) ? null : req.Publisher.Trim(),
            IsOfficial = req.IsOfficial,
            DateCreatedUtc = now,
            DateModifiedUtc = now
        };
        db.SourceMaterials.Add(existing);
    }
    else
    {
        existing.Title = req.Title.Trim();
        existing.Publisher = string.IsNullOrWhiteSpace(req.Publisher) ? null : req.Publisher.Trim();
        existing.IsOfficial = req.IsOfficial;
        existing.DateModifiedUtc = now;
    }

    await db.SaveChangesAsync();
    return Results.Ok(existing);
}).WithTags("Source Materials");

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


api.MapGet("/admin/source-materials", async (int gameSystemId, AppDbContext db) =>
    await db.SourceMaterials
        .Where(sm => sm.DateDeletedUtc == null && sm.GameSystemId == gameSystemId)
        .OrderBy(sm => sm.Code)
        .ToListAsync())
    .WithTags("Admin");

api.MapGet("/admin/source-materials/{sourceMaterialId:int}", async (int sourceMaterialId, AppDbContext db) =>
{
    var row = await db.SourceMaterials.FirstOrDefaultAsync(sm => sm.SourceMaterialId == sourceMaterialId && sm.DateDeletedUtc == null);
    return row is null ? Results.NotFound() : Results.Ok(row);
}).WithTags("Admin");

api.MapPost("/admin/source-materials", async (UpsertSourceMaterialRequest req, AppDbContext db) =>
{
    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");
    if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Title))
        return Results.BadRequest("Code and Title are required.");

    var code = req.Code.Trim().ToUpperInvariant();
    var dup = await db.SourceMaterials.AnyAsync(sm => sm.DateDeletedUtc == null && sm.GameSystemId == req.GameSystemId && sm.Code == code);
    if (dup) return Results.BadRequest("Source material code already exists for this system.");

    var now = DateTime.UtcNow;
    var row = new SourceMaterial
    {
        GameSystemId = req.GameSystemId,
        Code = code,
        Title = req.Title.Trim(),
        Publisher = string.IsNullOrWhiteSpace(req.Publisher) ? null : req.Publisher.Trim(),
        IsOfficial = req.IsOfficial,
        DateCreatedUtc = now,
        DateModifiedUtc = now
    };
    db.SourceMaterials.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(row);
}).WithTags("Admin");

api.MapPut("/admin/source-materials/{sourceMaterialId:int}", async (int sourceMaterialId, UpsertSourceMaterialRequest req, AppDbContext db) =>
{
    var row = await db.SourceMaterials.FirstOrDefaultAsync(sm => sm.SourceMaterialId == sourceMaterialId && sm.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var gsExists = await db.GameSystems.AnyAsync(gs => gs.GameSystemId == req.GameSystemId && gs.DateDeletedUtc == null);
    if (!gsExists) return Results.BadRequest("GameSystemId is invalid.");
    if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Title))
        return Results.BadRequest("Code and Title are required.");

    var code = req.Code.Trim().ToUpperInvariant();
    var dup = await db.SourceMaterials.AnyAsync(sm => sm.DateDeletedUtc == null && sm.GameSystemId == req.GameSystemId && sm.Code == code && sm.SourceMaterialId != sourceMaterialId);
    if (dup) return Results.BadRequest("Source material code already exists for this system.");

    row.GameSystemId = req.GameSystemId;
    row.Code = code;
    row.Title = req.Title.Trim();
    row.Publisher = string.IsNullOrWhiteSpace(req.Publisher) ? null : req.Publisher.Trim();
    row.IsOfficial = req.IsOfficial;
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






static async Task<string> GenerateUniqueCreatureSlugAsync(AppDbContext db, int gameSystemId, string baseSlug)
{
    var slug = string.IsNullOrWhiteSpace(baseSlug) ? "creature" : baseSlug;
    var candidate = slug;
    var i = 2;
    while (await db.Creatures.AnyAsync(c => c.DateDeletedUtc == null && c.GameSystemId == gameSystemId && c.Slug == candidate))
    {
        candidate = $"{slug}-{i++}";
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


static async Task SeedStarterDataAsync(AppDbContext db, string contentRootPath)
{
    var seedPath = Path.Combine(contentRootPath, "SeedData", "seed-data.json");
    if (!File.Exists(seedPath)) return;

    SeedDataFile? seed;
    try
    {
        await using var stream = File.OpenRead(seedPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        seed = await JsonSerializer.DeserializeAsync<SeedDataFile>(stream, options);
    }
    catch
    {
        return;
    }

    if (seed is null) return;
    var now = DateTime.UtcNow;

    var systemsBySlug = new Dictionary<string, GameSystem>();

    foreach (var gs in seed.GameSystems)
    {
        if (string.IsNullOrWhiteSpace(gs.Name)) continue;

        var desiredSlug = string.IsNullOrWhiteSpace(gs.Slug) ? Slugify(gs.Name) : Slugify(gs.Slug);
        if (string.IsNullOrWhiteSpace(desiredSlug)) desiredSlug = "game-system";

        var existing = await db.GameSystems.FirstOrDefaultAsync(x => x.DateDeletedUtc == null && x.Slug == desiredSlug);
        if (existing is null)
        {
            var uniqueSlug = await GenerateUniqueSlugAsync(db, desiredSlug);
            existing = new GameSystem
            {
                Name = gs.Name.Trim(),
                Slug = uniqueSlug,
                Alias = gs.Alias,
                Description = gs.Description,
                SourceType = gs.SourceType,
                DateCreatedUtc = now,
                DateModifiedUtc = now
            };
            db.GameSystems.Add(existing);
            await db.SaveChangesAsync();
        }

        systemsBySlug[existing.Slug] = existing;
        if (!systemsBySlug.ContainsKey(desiredSlug))
            systemsBySlug[desiredSlug] = existing;
    }

    async Task<GameSystem?> ResolveSystemAsync(string systemSlug)
    {
        var key = Slugify(systemSlug);
        if (systemsBySlug.TryGetValue(key, out var gs)) return gs;

        gs = await db.GameSystems.FirstOrDefaultAsync(x => x.DateDeletedUtc == null && x.Slug == key);
        if (gs is not null) systemsBySlug[key] = gs;
        return gs;
    }

    foreach (var it in seed.ItemTypes)
    {
        if (string.IsNullOrWhiteSpace(it.Name) || string.IsNullOrWhiteSpace(it.GameSystemSlug)) continue;

        var system = await ResolveSystemAsync(it.GameSystemSlug);
        if (system is null) continue;

        var desiredSlug = string.IsNullOrWhiteSpace(it.Slug) ? Slugify(it.Name) : Slugify(it.Slug);
        if (string.IsNullOrWhiteSpace(desiredSlug)) desiredSlug = "item-type";

        var existingType = await db.ItemTypeDefinitions.FirstOrDefaultAsync(x => x.DateDeletedUtc == null && x.GameSystemId == system.GameSystemId && x.Slug == desiredSlug);
        if (existingType is null)
        {
            db.ItemTypeDefinitions.Add(new ItemTypeDefinition
            {
                GameSystemId = system.GameSystemId,
                Name = it.Name.Trim(),
                Slug = await GenerateUniqueItemTypeSlugAsync(db, system.GameSystemId, desiredSlug),
                Description = it.Description,
                DateCreatedUtc = now,
                DateModifiedUtc = now
            });
        }
        else
        {
            existingType.Name = it.Name.Trim();
            existingType.Description = it.Description;
            existingType.DateModifiedUtc = now;
        }
    }

    foreach (var r in seed.Rarities)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.GameSystemSlug)) continue;

        var system = await ResolveSystemAsync(r.GameSystemSlug);
        if (system is null) continue;

        var desiredSlug = string.IsNullOrWhiteSpace(r.Slug) ? Slugify(r.Name) : Slugify(r.Slug);
        if (string.IsNullOrWhiteSpace(desiredSlug)) desiredSlug = "rarity";

        var existingRarity = await db.RarityDefinitions.FirstOrDefaultAsync(x => x.DateDeletedUtc == null && x.GameSystemId == system.GameSystemId && x.Slug == desiredSlug);
        if (existingRarity is null)
        {
            db.RarityDefinitions.Add(new RarityDefinition
            {
                GameSystemId = system.GameSystemId,
                Name = r.Name.Trim(),
                Slug = await GenerateUniqueRaritySlugAsync(db, system.GameSystemId, desiredSlug),
                Description = r.Description,
                SortOrder = r.SortOrder,
                DateCreatedUtc = now,
                DateModifiedUtc = now
            });
        }
        else
        {
            existingRarity.Name = r.Name.Trim();
            existingRarity.Description = r.Description;
            existingRarity.SortOrder = r.SortOrder;
            existingRarity.DateModifiedUtc = now;
        }
    }

    foreach (var c in seed.Currencies)
    {
        if (string.IsNullOrWhiteSpace(c.Code) || string.IsNullOrWhiteSpace(c.GameSystemSlug)) continue;

        var system = await ResolveSystemAsync(c.GameSystemSlug);
        if (system is null) continue;

        var code = c.Code.Trim().ToLowerInvariant();
        var existingCurrency = await db.CurrencyDefinitions.FirstOrDefaultAsync(x => x.DateDeletedUtc == null && x.GameSystemId == system.GameSystemId && x.Code == code);
        if (existingCurrency is null)
        {
            db.CurrencyDefinitions.Add(new CurrencyDefinition
            {
                GameSystemId = system.GameSystemId,
                Name = string.IsNullOrWhiteSpace(c.Name) ? code.ToUpperInvariant() : c.Name.Trim(),
                Code = code,
                Symbol = c.Symbol,
                Description = c.Description,
                DateCreatedUtc = now,
                DateModifiedUtc = now
            });
        }
        else
        {
            existingCurrency.Name = string.IsNullOrWhiteSpace(c.Name) ? code.ToUpperInvariant() : c.Name.Trim();
            existingCurrency.Symbol = c.Symbol;
            existingCurrency.Description = c.Description;
            existingCurrency.DateModifiedUtc = now;
        }
    }

    foreach (var sm in seed.SourceMaterials)
    {
        if (string.IsNullOrWhiteSpace(sm.Code) || string.IsNullOrWhiteSpace(sm.Title) || string.IsNullOrWhiteSpace(sm.GameSystemSlug)) continue;

        var system = await ResolveSystemAsync(sm.GameSystemSlug);
        if (system is null) continue;

        var code = sm.Code.Trim().ToUpperInvariant();
        var existing = await db.SourceMaterials.FirstOrDefaultAsync(x => x.DateDeletedUtc == null && x.GameSystemId == system.GameSystemId && x.Code == code);
        if (existing is null)
        {
            db.SourceMaterials.Add(new SourceMaterial
            {
                GameSystemId = system.GameSystemId,
                Code = code,
                Title = sm.Title.Trim(),
                Publisher = string.IsNullOrWhiteSpace(sm.Publisher) ? null : sm.Publisher.Trim(),
                IsOfficial = sm.IsOfficial,
                DateCreatedUtc = now,
                DateModifiedUtc = now
            });
        }
        else
        {
            existing.Title = sm.Title.Trim();
            existing.Publisher = string.IsNullOrWhiteSpace(sm.Publisher) ? null : sm.Publisher.Trim();
            existing.IsOfficial = sm.IsOfficial;
            existing.DateModifiedUtc = now;
        }
    }

    foreach (var t in seed.Tags)
    {
        if (string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.GameSystemSlug)) continue;

        var system = await ResolveSystemAsync(t.GameSystemSlug);
        if (system is null) continue;

        var desiredSlug = string.IsNullOrWhiteSpace(t.Slug) ? Slugify(t.Name) : Slugify(t.Slug);
        if (string.IsNullOrWhiteSpace(desiredSlug)) desiredSlug = "tag";

        var existingTag = await db.TagDefinitions.FirstOrDefaultAsync(x => x.DateDeletedUtc == null && x.GameSystemId == system.GameSystemId && x.Slug == desiredSlug);
        if (existingTag is null)
        {
            db.TagDefinitions.Add(new TagDefinition
            {
                GameSystemId = system.GameSystemId,
                Name = t.Name.Trim(),
                Slug = await GenerateUniqueTagSlugAsync(db, system.GameSystemId, desiredSlug),
                DateCreatedUtc = now,
                DateModifiedUtc = now
            });
        }
        else
        {
            existingTag.Name = t.Name.Trim();
            existingTag.DateModifiedUtc = now;
        }
    }

    await db.SaveChangesAsync();

    foreach (var i in seed.Items)
    {
        if (string.IsNullOrWhiteSpace(i.Name) || string.IsNullOrWhiteSpace(i.GameSystemSlug)) continue;

        var system = await ResolveSystemAsync(i.GameSystemSlug);
        if (system is null) continue;

        var desiredSlug = string.IsNullOrWhiteSpace(i.Slug) ? Slugify(i.Name) : Slugify(i.Slug);
        if (string.IsNullOrWhiteSpace(desiredSlug)) desiredSlug = "item";

        var existingItem = await db.Items.FirstOrDefaultAsync(x => x.DateDeletedUtc == null && x.GameSystemId == system.GameSystemId && x.Slug == desiredSlug);

        int? itemTypeId = null;
        if (!string.IsNullOrWhiteSpace(i.ItemTypeSlug))
        {
            var itemTypeSlug = Slugify(i.ItemTypeSlug);
            itemTypeId = await db.ItemTypeDefinitions
                .Where(x => x.DateDeletedUtc == null && x.GameSystemId == system.GameSystemId && x.Slug == itemTypeSlug)
                .Select(x => (int?)x.ItemTypeDefinitionId)
                .FirstOrDefaultAsync();
        }

        int? rarityId = null;
        if (!string.IsNullOrWhiteSpace(i.RaritySlug))
        {
            var raritySlug = Slugify(i.RaritySlug);
            rarityId = await db.RarityDefinitions
                .Where(x => x.DateDeletedUtc == null && x.GameSystemId == system.GameSystemId && x.Slug == raritySlug)
                .Select(x => (int?)x.RarityDefinitionId)
                .FirstOrDefaultAsync();
        }

        int? currencyId = null;
        if (!string.IsNullOrWhiteSpace(i.CurrencyCode))
        {
            var currencyCode = i.CurrencyCode.Trim().ToLowerInvariant();
            currencyId = await db.CurrencyDefinitions
                .Where(x => x.DateDeletedUtc == null && x.GameSystemId == system.GameSystemId && x.Code == currencyCode)
                .Select(x => (int?)x.CurrencyDefinitionId)
                .FirstOrDefaultAsync();
        }

        int? sourceMaterialId = null;
        if (!string.IsNullOrWhiteSpace(i.SourceCode))
        {
            var sourceCode = i.SourceCode.Trim().ToUpperInvariant();
            sourceMaterialId = await db.SourceMaterials
                .Where(x => x.DateDeletedUtc == null && x.GameSystemId == system.GameSystemId && x.Code == sourceCode)
                .Select(x => (int?)x.SourceMaterialId)
                .FirstOrDefaultAsync();
        }

        if (existingItem is null)
        {
            db.Items.Add(new Item
            {
                GameSystemId = system.GameSystemId,
                Name = i.Name.Trim(),
                Slug = await GenerateUniqueItemSlugAsync(db, system.GameSystemId, desiredSlug),
                Alias = i.Alias,
                ItemTypeDefinitionId = itemTypeId,
                OwnerAppUserId = null,
                RarityDefinitionId = rarityId,
                Description = i.Description,
                CostAmount = i.CostAmount,
                CurrencyDefinitionId = currencyId,
                CostCurrency = i.CurrencyCode,
                Weight = i.Weight,
                Quantity = i.Quantity <= 0 ? 1 : i.Quantity,
                Effect = i.Effect,
                RequiresAttunement = i.RequiresAttunement,
                AttunementRequirement = i.AttunementRequirement,
                DamageDice = i.DamageDice,
                DamageType = i.DamageType,
                VersatileDamageDice = i.VersatileDamageDice,
                ArmorClass = i.ArmorClass,
                StrengthRequirement = i.StrengthRequirement,
                StealthDisadvantage = i.StealthDisadvantage,
                RangeNormal = i.RangeNormal,
                RangeLong = i.RangeLong,
                SourceMaterialId = sourceMaterialId,
                SourceBook = i.SourceBook,
                SourcePage = i.SourcePage,
                IsConsumable = i.IsConsumable,
                ChargesCurrent = i.ChargesCurrent,
                ChargesMax = i.ChargesMax,
                RechargeRule = i.RechargeRule,
                UsesPerDay = i.UsesPerDay,
                ArmorCategory = i.ArmorCategory,
                WeaponPropertyLight = i.WeaponPropertyLight,
                WeaponPropertyHeavy = i.WeaponPropertyHeavy,
                WeaponPropertyFinesse = i.WeaponPropertyFinesse,
                WeaponPropertyThrown = i.WeaponPropertyThrown,
                WeaponPropertyTwoHanded = i.WeaponPropertyTwoHanded,
                WeaponPropertyLoading = i.WeaponPropertyLoading,
                WeaponPropertyReach = i.WeaponPropertyReach,
                WeaponPropertyAmmunition = i.WeaponPropertyAmmunition,
                SourceType = i.SourceType,
                DateCreatedUtc = now,
                DateModifiedUtc = now
            });
            await db.SaveChangesAsync();
            existingItem = await db.Items.FirstOrDefaultAsync(x => x.DateDeletedUtc == null && x.GameSystemId == system.GameSystemId && x.Slug == desiredSlug);
        }
        else
        {
            existingItem.Name = i.Name.Trim();
            existingItem.Alias = i.Alias;
            existingItem.ItemTypeDefinitionId = itemTypeId;
            existingItem.OwnerAppUserId = null;
            existingItem.RarityDefinitionId = rarityId;
            existingItem.Description = i.Description;
            existingItem.CostAmount = i.CostAmount;
            existingItem.CurrencyDefinitionId = currencyId;
            existingItem.CostCurrency = i.CurrencyCode;
            existingItem.Weight = i.Weight;
            existingItem.Quantity = i.Quantity <= 0 ? 1 : i.Quantity;
            existingItem.Effect = i.Effect;
            existingItem.RequiresAttunement = i.RequiresAttunement;
            existingItem.AttunementRequirement = i.AttunementRequirement;
            existingItem.DamageDice = i.DamageDice;
            existingItem.DamageType = i.DamageType;
            existingItem.VersatileDamageDice = i.VersatileDamageDice;
            existingItem.ArmorClass = i.ArmorClass;
            existingItem.StrengthRequirement = i.StrengthRequirement;
            existingItem.StealthDisadvantage = i.StealthDisadvantage;
            existingItem.RangeNormal = i.RangeNormal;
            existingItem.RangeLong = i.RangeLong;
            existingItem.SourceMaterialId = sourceMaterialId;
            existingItem.SourceBook = i.SourceBook;
            existingItem.SourcePage = i.SourcePage;
            existingItem.IsConsumable = i.IsConsumable;
            existingItem.ChargesCurrent = i.ChargesCurrent;
            existingItem.ChargesMax = i.ChargesMax;
            existingItem.RechargeRule = i.RechargeRule;
            existingItem.UsesPerDay = i.UsesPerDay;
            existingItem.ArmorCategory = i.ArmorCategory;
            existingItem.WeaponPropertyLight = i.WeaponPropertyLight;
            existingItem.WeaponPropertyHeavy = i.WeaponPropertyHeavy;
            existingItem.WeaponPropertyFinesse = i.WeaponPropertyFinesse;
            existingItem.WeaponPropertyThrown = i.WeaponPropertyThrown;
            existingItem.WeaponPropertyTwoHanded = i.WeaponPropertyTwoHanded;
            existingItem.WeaponPropertyLoading = i.WeaponPropertyLoading;
            existingItem.WeaponPropertyReach = i.WeaponPropertyReach;
            existingItem.WeaponPropertyAmmunition = i.WeaponPropertyAmmunition;
            existingItem.SourceType = i.SourceType;
            existingItem.DateModifiedUtc = now;
        }

        if (existingItem is not null)
        {
            var desiredTagSlugs = (i.TagSlugs ?? new List<string>())
                .Select(Slugify)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            var desiredTagIds = await db.TagDefinitions
                .Where(t => t.DateDeletedUtc == null && t.GameSystemId == system.GameSystemId && desiredTagSlugs.Contains(t.Slug))
                .Select(t => t.TagDefinitionId)
                .ToListAsync();

            var activeLinks = await db.ItemTags
                .Where(it => it.DateDeletedUtc == null && it.ItemId == existingItem.ItemId)
                .ToListAsync();

            foreach (var link in activeLinks.Where(l => !desiredTagIds.Contains(l.TagDefinitionId)))
                link.DateDeletedUtc = now;

            var activeTagIds = activeLinks.Where(l => l.DateDeletedUtc == null).Select(l => l.TagDefinitionId).ToHashSet();
            foreach (var tagId in desiredTagIds.Where(id => !activeTagIds.Contains(id)))
            {
                db.ItemTags.Add(new ItemTag
                {
                    ItemId = existingItem.ItemId,
                    TagDefinitionId = tagId,
                    DateCreatedUtc = now
                });
            }
        }
    }

    // Seed starter feature backlog for scoped planning (idempotent)
    {
        var seedFeatures = new[]
        {
            new { Title = "Bestiary Phase 3: statblock gap fields (senses/saves/skills/languages)", Description = "Add core missing MM-style fields on creatures with minimal schema churn.", Status = "Backlog", Priority = "High", RequestedBy = "Product", Entity = "Creature" },
            new { Title = "Bestiary Phase 3: damage and condition interactions", Description = "Add vulnerabilities/resistances/immunities/condition immunities for creature statblocks.", Status = "Backlog", Priority = "High", RequestedBy = "Product", Entity = "Creature" },
            new { Title = "Bestiary: structured spellcasting model", Description = "Support innate/spellcasting blocks with slots/DC/attack bonus in structured form.", Status = "Backlog", Priority = "Medium", RequestedBy = "Product", Entity = "Creature" },
            new { Title = "Bestiary: lair actions and regional effects", Description = "Extend creature abilities for high-tier monsters and encounter context.", Status = "Backlog", Priority = "Medium", RequestedBy = "Product", Entity = "Creature" },
            new { Title = "Bestiary: export/import schema docs and templates", Description = "Provide copy-paste examples and schema docs to reduce malformed import payloads.", Status = "Backlog", Priority = "Low", RequestedBy = "Product", Entity = "Creature" },
            new { Title = "Bestiary: server-side filtered list endpoint", Description = "Promote current client filters to API query filters if dataset grows.", Status = "Backlog", Priority = "Low", RequestedBy = "Product", Entity = "Creature" }
        };

        var order = 0;
        foreach (var f in seedFeatures)
        {
            var title = f.Title.Trim();
            var existing = await db.FeatureRequests.FirstOrDefaultAsync(x => x.DateDeletedUtc == null && x.Title == title);
            if (existing is null)
            {
                db.FeatureRequests.Add(new FeatureRequest
                {
                    Title = title,
                    Description = f.Description,
                    Status = NormalizeFeatureStatus(f.Status),
                    Priority = f.Priority,
                    RequestedBy = f.RequestedBy,
                    Entity = f.Entity,
                    SortOrder = order++,
                    DateCreatedUtc = now,
                    DateModifiedUtc = now
                });
            }
        }

        await db.SaveChangesAsync();
    }

    // Seed starter bestiary entries (idempotent)
    {
        var dnd = await ResolveSystemAsync("dungeons-dragons-5e");
        if (dnd is not null)
        {
            var defaultSourceId = await db.SourceMaterials
                .Where(sm => sm.DateDeletedUtc == null && sm.GameSystemId == dnd.GameSystemId && sm.IsOfficial)
                .OrderBy(sm => sm.SourceMaterialId)
                .Select(sm => (int?)sm.SourceMaterialId)
                .FirstOrDefaultAsync();

            async Task UpsertSeedCreatureAsync(
                string name,
                string creatureType,
                string size,
                string alignment,
                int armorClass,
                int hitPoints,
                string speed,
                int str,
                int dex,
                int con,
                int intel,
                int wis,
                int cha,
                string challengeRating,
                int proficiencyBonus,
                string description,
                List<CreatureAbilityInput> traits,
                List<CreatureAbilityInput> actions,
                List<CreatureAbilityInput>? reactions = null,
                List<CreatureAbilityInput>? legendary = null)
            {
                var slug = Slugify(name);
                var row = await db.Creatures.FirstOrDefaultAsync(c => c.DateDeletedUtc == null && c.GameSystemId == dnd.GameSystemId && c.Slug == slug);
                if (row is null)
                {
                    row = new Creature
                    {
                        GameSystemId = dnd.GameSystemId,
                        Name = name,
                        Slug = await GenerateUniqueCreatureSlugAsync(db, dnd.GameSystemId, slug),
                        CreatureType = creatureType,
                        Size = size,
                        Alignment = alignment,
                        ArmorClass = armorClass,
                        HitPoints = hitPoints,
                        Speed = speed,
                        Strength = str,
                        Dexterity = dex,
                        Constitution = con,
                        Intelligence = intel,
                        Wisdom = wis,
                        Charisma = cha,
                        ChallengeRating = challengeRating,
                        ProficiencyBonus = proficiencyBonus,
                        Description = description,
                        SourceType = SourceType.Official,
                        OwnerAppUserId = null,
                        SourceMaterialId = defaultSourceId,
                        DateCreatedUtc = now,
                        DateModifiedUtc = now
                    };
                    db.Creatures.Add(row);
                    await db.SaveChangesAsync();
                }
                else
                {
                    row.Name = name;
                    row.CreatureType = creatureType;
                    row.Size = size;
                    row.Alignment = alignment;
                    row.ArmorClass = armorClass;
                    row.HitPoints = hitPoints;
                    row.Speed = speed;
                    row.Strength = str;
                    row.Dexterity = dex;
                    row.Constitution = con;
                    row.Intelligence = intel;
                    row.Wisdom = wis;
                    row.Charisma = cha;
                    row.ChallengeRating = challengeRating;
                    row.ProficiencyBonus = proficiencyBonus;
                    row.Description = description;
                    row.SourceType = SourceType.Official;
                    row.OwnerAppUserId = null;
                    if (!row.SourceMaterialId.HasValue) row.SourceMaterialId = defaultSourceId;
                    row.DateModifiedUtc = now;
                    await db.SaveChangesAsync();
                }

                await SyncCreatureAbilitiesAsync(db, row.CreatureId, traits, actions, reactions, legendary);
                await db.SaveChangesAsync();
            }

            await UpsertSeedCreatureAsync(
                name: "Goblin",
                creatureType: "Humanoid (goblinoid)",
                size: "Small",
                alignment: "Neutral Evil",
                armorClass: 15,
                hitPoints: 7,
                speed: "30 ft.",
                str: 8,
                dex: 14,
                con: 10,
                intel: 10,
                wis: 8,
                cha: 8,
                challengeRating: "1/4",
                proficiencyBonus: 2,
                description: "Small, sneaky humanoids who rely on speed, numbers, and dirty tricks.",
                traits: new()
                {
                    new CreatureAbilityInput("Nimble Escape", "The goblin can take the Disengage or Hide action as a bonus action on each of its turns.", 0)
                },
                actions: new()
                {
                    new CreatureAbilityInput("Scimitar", "Melee Weapon Attack: +4 to hit, reach 5 ft., one target. Hit: 5 (1d6 + 2) slashing damage.", 0),
                    new CreatureAbilityInput("Shortbow", "Ranged Weapon Attack: +4 to hit, range 80/320 ft., one target. Hit: 5 (1d6 + 2) piercing damage.", 1)
                });

            await UpsertSeedCreatureAsync(
                name: "Orc",
                creatureType: "Humanoid (orc)",
                size: "Medium",
                alignment: "Chaotic Evil",
                armorClass: 13,
                hitPoints: 15,
                speed: "30 ft.",
                str: 16,
                dex: 12,
                con: 16,
                intel: 7,
                wis: 11,
                cha: 10,
                challengeRating: "1/2",
                proficiencyBonus: 2,
                description: "Savage raiders driven by brutality, strength, and the will to conquer.",
                traits: new()
                {
                    new CreatureAbilityInput("Aggressive", "As a bonus action, the orc can move up to its speed toward a hostile creature that it can see.", 0)
                },
                actions: new()
                {
                    new CreatureAbilityInput("Greataxe", "Melee Weapon Attack: +5 to hit, reach 5 ft., one target. Hit: 9 (1d12 + 3) slashing damage.", 0),
                    new CreatureAbilityInput("Javelin", "Melee or Ranged Weapon Attack: +5 to hit, reach 5 ft. or range 30/120 ft., one target. Hit: 6 (1d6 + 3) piercing damage.", 1)
                });

            await UpsertSeedCreatureAsync(
                name: "Owlbear",
                creatureType: "Monstrosity",
                size: "Large",
                alignment: "Unaligned",
                armorClass: 13,
                hitPoints: 59,
                speed: "40 ft.",
                str: 20,
                dex: 12,
                con: 17,
                intel: 3,
                wis: 12,
                cha: 7,
                challengeRating: "3",
                proficiencyBonus: 2,
                description: "A hulking blend of bear and owl, notoriously ferocious and territorial.",
                traits: new()
                {
                    new CreatureAbilityInput("Keen Sight and Smell", "The owlbear has advantage on Wisdom (Perception) checks that rely on sight or smell.", 0)
                },
                actions: new()
                {
                    new CreatureAbilityInput("Multiattack", "The owlbear makes two attacks: one with its beak and one with its claws.", 0),
                    new CreatureAbilityInput("Beak", "Melee Weapon Attack: +7 to hit, reach 5 ft., one creature. Hit: 10 (1d10 + 5) piercing damage.", 1),
                    new CreatureAbilityInput("Claws", "Melee Weapon Attack: +7 to hit, reach 5 ft., one target. Hit: 14 (2d8 + 5) slashing damage.", 2)
                });
        }
    }

    await db.SaveChangesAsync();
}


static string NormalizeFeatureStatus(string? status)
{
    var s = (status ?? "Backlog").Trim();
    if (s.Equals("InProgress", StringComparison.OrdinalIgnoreCase)) return "In Progress";
    if (s.Equals("In Progress", StringComparison.OrdinalIgnoreCase)) return "In Progress";
    if (s.Equals("Done", StringComparison.OrdinalIgnoreCase)) return "Done";
    return "Backlog";
}

static string? ValidateItemRequest(CreateItemRequest req)
{
    if (req.Quantity <= 0) return "Quantity must be at least 1.";
    if (req.CostAmount.HasValue && req.CostAmount.Value < 0) return "CostAmount cannot be negative.";
    if (req.Weight.HasValue && req.Weight.Value < 0) return "Weight cannot be negative.";

    if (req.ArmorClass.HasValue && req.ArmorClass.Value < 0) return "ArmorClass cannot be negative.";
    if (req.StrengthRequirement.HasValue && req.StrengthRequirement.Value < 0) return "StrengthRequirement cannot be negative.";
    if (req.RangeNormal.HasValue && req.RangeNormal.Value < 0) return "RangeNormal cannot be negative.";
    if (req.RangeLong.HasValue && req.RangeLong.Value < 0) return "RangeLong cannot be negative.";
    if (req.RangeNormal.HasValue && req.RangeLong.HasValue && req.RangeLong.Value < req.RangeNormal.Value)
        return "RangeLong must be greater than or equal to RangeNormal.";

    if (req.SourcePage.HasValue && req.SourcePage.Value <= 0) return "SourcePage must be greater than 0.";

    if (req.ChargesCurrent.HasValue && req.ChargesCurrent.Value < 0) return "ChargesCurrent cannot be negative.";
    if (req.ChargesMax.HasValue && req.ChargesMax.Value < 0) return "ChargesMax cannot be negative.";
    if (req.ChargesCurrent.HasValue && req.ChargesMax.HasValue && req.ChargesCurrent.Value > req.ChargesMax.Value)
        return "ChargesCurrent cannot exceed ChargesMax.";

    if (req.UsesPerDay.HasValue && req.UsesPerDay.Value < 0) return "UsesPerDay cannot be negative.";

    if (!string.IsNullOrWhiteSpace(req.ArmorCategory))
    {
        var allowed = new[] { "Light", "Medium", "Heavy", "Shield" };
        if (!allowed.Contains(req.ArmorCategory.Trim(), StringComparer.OrdinalIgnoreCase))
            return "ArmorCategory must be Light, Medium, Heavy, or Shield.";
    }

    return null;
}

static string? ValidateItemRequestByType(CreateItemRequest req, string? itemTypeName)
{
    var type = itemTypeName?.Trim().ToLowerInvariant() ?? string.Empty;
    var isWeapon = type.Contains("weapon");
    var isArmor = type.Contains("armor") || type.Contains("shield");
    var isConsumable = type.Contains("potion") || type.Contains("consumable");
    var isMagic = isConsumable || type.Contains("wondrous") || type.Contains("ring") || type.Contains("wand") || type.Contains("rod") || type.Contains("staff") || type.Contains("scroll");

    if (isWeapon)
    {
        if (string.IsNullOrWhiteSpace(req.DamageDice)) return "Weapons require DamageDice.";
        if (string.IsNullOrWhiteSpace(req.DamageType)) return "Weapons require DamageType.";
    }

    if (isArmor)
    {
        if (!req.ArmorClass.HasValue) return "Armor/Shield items require ArmorClass.";
        if (string.IsNullOrWhiteSpace(req.ArmorCategory)) return "Armor/Shield items require ArmorCategory.";
    }

    // Attunement can apply to weapons/armor when magical; do not hard-block by type.

    return null;
}



static async Task EnsureSeedAdminAccountAsync(AppDbContext db)
{
    const string email = "admin@local";
    const string tempPassword = "ChangeMe123!";

    var existing = await db.AppUsers.FirstOrDefaultAsync(u => u.Email == email && u.DateDeletedUtc == null);
    if (existing is null)
    {
        var (hash, salt) = HashPassword(tempPassword);
        var now = DateTime.UtcNow;
        db.AppUsers.Add(new AppUser
        {
            Email = email,
            Username = "admin",
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = "Admin",
            IsActive = true,
            IsSystemAccount = true,
            MustChangePassword = true,
            DateCreatedUtc = now,
            DateModifiedUtc = now
        });
        await db.SaveChangesAsync();
    }
    else
    {
        var changed = false;
        if (!string.Equals(existing.Username, "admin", StringComparison.OrdinalIgnoreCase)) { existing.Username = "admin"; changed = true; }
        if (!existing.IsSystemAccount) { existing.IsSystemAccount = true; changed = true; }
        if (!string.Equals(existing.Role, "Admin", StringComparison.OrdinalIgnoreCase)) { existing.Role = "Admin"; changed = true; }
        if (!existing.IsActive) { existing.IsActive = true; changed = true; }
        if (changed)
        {
            existing.DateModifiedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }
}

static (string hash, string salt) HashPassword(string password)
{
    var saltBytes = RandomNumberGenerator.GetBytes(16);
    var hashBytes = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, 100_000, HashAlgorithmName.SHA256, 32);
    return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
}

static bool VerifyPassword(string password, string hashBase64, string saltBase64)
{
    try
    {
        var saltBytes = Convert.FromBase64String(saltBase64);
        var expected = Convert.FromBase64String(hashBase64);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, 100_000, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
    catch
    {
        return false;
    }
}



static async Task SyncCreatureAbilitiesAsync(AppDbContext db, int creatureId,
    IEnumerable<CreatureAbilityInput>? traits,
    IEnumerable<CreatureAbilityInput>? actions,
    IEnumerable<CreatureAbilityInput>? reactions,
    IEnumerable<CreatureAbilityInput>? legendaryActions)
{
    var now = DateTime.UtcNow;

    static List<(string Type, CreatureAbilityInput Input)> Flatten(string type, IEnumerable<CreatureAbilityInput>? items)
        => (items ?? Enumerable.Empty<CreatureAbilityInput>())
            .Where(i => !string.IsNullOrWhiteSpace(i.Description) || !string.IsNullOrWhiteSpace(i.Name))
            .Select(i => (type, i))
            .ToList();

    var desired = new List<(string Type, CreatureAbilityInput Input)>();
    desired.AddRange(Flatten("Trait", traits));
    desired.AddRange(Flatten("Action", actions));
    desired.AddRange(Flatten("Reaction", reactions));
    desired.AddRange(Flatten("LegendaryAction", legendaryActions));

    var existing = await db.CreatureAbilities.Where(a => a.CreatureId == creatureId && a.DateDeletedUtc == null).ToListAsync();
    foreach (var e in existing) e.DateDeletedUtc = now;

    var order = 0;
    foreach (var (type, input) in desired)
    {
        db.CreatureAbilities.Add(new CreatureAbility
        {
            CreatureId = creatureId,
            AbilityType = type,
            Name = string.IsNullOrWhiteSpace(input.Name) ? null : input.Name.Trim(),
            Description = input.Description?.Trim() ?? string.Empty,
            SortOrder = order++,
            DateCreatedUtc = now,
            DateModifiedUtc = now
        });
    }
}

static async Task SyncCampaignMembershipsAsync(AppDbContext db, int campaignId, IEnumerable<int>? collaboratorIds, IEnumerable<int>? playerIds)
{
    var now = DateTime.UtcNow;

    var desiredCollaborators = (collaboratorIds ?? Enumerable.Empty<int>()).Distinct().ToHashSet();
    var desiredPlayers = (playerIds ?? Enumerable.Empty<int>()).Distinct().ToHashSet();

    var validUserIds = await db.AppUsers
        .Where(u => u.DateDeletedUtc == null && u.IsActive && (desiredCollaborators.Contains(u.AppUserId) || desiredPlayers.Contains(u.AppUserId)))
        .Select(u => u.AppUserId)
        .ToListAsync();

    desiredCollaborators.IntersectWith(validUserIds);
    desiredPlayers.IntersectWith(validUserIds);

    var existingCollabs = await db.CampaignCollaborators.Where(x => x.CampaignId == campaignId && x.DateDeletedUtc == null).ToListAsync();
    foreach (var row in existingCollabs.Where(r => !desiredCollaborators.Contains(r.AppUserId))) row.DateDeletedUtc = now;
    var existingCollabIds = existingCollabs.Where(r => r.DateDeletedUtc == null).Select(r => r.AppUserId).ToHashSet();
    foreach (var uid in desiredCollaborators.Where(id => !existingCollabIds.Contains(id)))
        db.CampaignCollaborators.Add(new CampaignCollaborator { CampaignId = campaignId, AppUserId = uid, DateCreatedUtc = now });

    var existingPlayers = await db.CampaignPlayers.Where(x => x.CampaignId == campaignId && x.DateDeletedUtc == null).ToListAsync();
    foreach (var row in existingPlayers.Where(r => !desiredPlayers.Contains(r.AppUserId))) row.DateDeletedUtc = now;
    var existingPlayerIds = existingPlayers.Where(r => r.DateDeletedUtc == null).Select(r => r.AppUserId).ToHashSet();
    foreach (var uid in desiredPlayers.Where(id => !existingPlayerIds.Contains(id)))
        db.CampaignPlayers.Add(new CampaignPlayer { CampaignId = campaignId, AppUserId = uid, DateCreatedUtc = now });
}



static async Task<IResult> LoggedBadRequestAsync(AppDbContext db, HttpContext http, string message)
{
    var errorUid = await LogHandledErrorAsync(db, http, message);
    return Results.BadRequest($"{message} Error ID: {errorUid}");
}

static async Task<string> LogHandledErrorAsync(AppDbContext db, HttpContext http, string message)
{
    var errorUid = $"RF-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
    int? userId = null;
    var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (int.TryParse(idRaw, out var uid)) userId = uid;

    db.AppErrors.Add(new AppError
    {
        ErrorUid = errorUid,
        Path = http.Request.Path,
        Method = http.Request.Method,
        UserId = userId,
        Message = message,
        StackTrace = null,
        DateCreatedUtc = DateTime.UtcNow
    });
    await db.SaveChangesAsync();
    return errorUid;
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<GameSystem> GameSystems => Set<GameSystem>();
    public DbSet<ItemTypeDefinition> ItemTypeDefinitions => Set<ItemTypeDefinition>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<RarityDefinition> RarityDefinitions => Set<RarityDefinition>();
    public DbSet<CurrencyDefinition> CurrencyDefinitions => Set<CurrencyDefinition>();
    public DbSet<SourceMaterial> SourceMaterials => Set<SourceMaterial>();
    public DbSet<TagDefinition> TagDefinitions => Set<TagDefinition>();
    public DbSet<ItemTag> ItemTags => Set<ItemTag>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignCollaborator> CampaignCollaborators => Set<CampaignCollaborator>();
    public DbSet<CampaignPlayer> CampaignPlayers => Set<CampaignPlayer>();
    public DbSet<AppError> AppErrors => Set<AppError>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<Friend> Friends => Set<Friend>();
    public DbSet<Creature> Creatures => Set<Creature>();
    public DbSet<CreatureAbility> CreatureAbilities => Set<CreatureAbility>();
    public DbSet<FeatureRequest> FeatureRequests => Set<FeatureRequest>();
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

public sealed class SeedDataFile
{
    public List<SeedGameSystem> GameSystems { get; set; } = new();
    public List<SeedItemType> ItemTypes { get; set; } = new();
    public List<SeedRarity> Rarities { get; set; } = new();
    public List<SeedCurrency> Currencies { get; set; } = new();
    public List<SeedSourceMaterial> SourceMaterials { get; set; } = new();
    public List<SeedTag> Tags { get; set; } = new();
    public List<SeedItem> Items { get; set; } = new();
}

public sealed class SeedGameSystem
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? Alias { get; set; }
    public string? Description { get; set; }
    public SourceType SourceType { get; set; } = SourceType.Official;
}

public sealed class SeedItemType
{
    public string GameSystemSlug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? Description { get; set; }
}

public sealed class SeedRarity
{
    public string GameSystemSlug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}

public sealed class SeedCurrency
{
    public string GameSystemSlug { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Symbol { get; set; }
    public string? Description { get; set; }
}

public sealed class SeedSourceMaterial
{
    public string GameSystemSlug { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Publisher { get; set; }
    public bool IsOfficial { get; set; } = true;
}

public sealed class SeedTag
{
    public string GameSystemSlug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
}

public sealed class SeedItem
{
    public string GameSystemSlug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? Alias { get; set; }
    public string? ItemTypeSlug { get; set; }
    public string? RaritySlug { get; set; }
    public List<string> TagSlugs { get; set; } = new();
    public string? Description { get; set; }
    public decimal? CostAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? Weight { get; set; }
    public int Quantity { get; set; } = 1;
    public string? Effect { get; set; }
    public bool RequiresAttunement { get; set; }
    public string? AttunementRequirement { get; set; }
    public string? DamageDice { get; set; }
    public string? DamageType { get; set; }
    public string? VersatileDamageDice { get; set; }
    public int? ArmorClass { get; set; }
    public int? StrengthRequirement { get; set; }
    public bool StealthDisadvantage { get; set; }
    public int? RangeNormal { get; set; }
    public int? RangeLong { get; set; }
    public int? SourceMaterialId { get; set; }
    public string? SourceCode { get; set; }
    public string? SourceBook { get; set; }
    public int? SourcePage { get; set; }
    public bool IsConsumable { get; set; }
    public int? ChargesCurrent { get; set; }
    public int? ChargesMax { get; set; }
    public string? RechargeRule { get; set; }
    public int? UsesPerDay { get; set; }
    public string? ArmorCategory { get; set; }
    public bool WeaponPropertyLight { get; set; }
    public bool WeaponPropertyHeavy { get; set; }
    public bool WeaponPropertyFinesse { get; set; }
    public bool WeaponPropertyThrown { get; set; }
    public bool WeaponPropertyTwoHanded { get; set; }
    public bool WeaponPropertyLoading { get; set; }
    public bool WeaponPropertyReach { get; set; }
    public bool WeaponPropertyAmmunition { get; set; }
    public SourceType SourceType { get; set; } = SourceType.Official;
}

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
    public int? OwnerAppUserId { get; set; }
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
    public string? DamageDice { get; set; }
    public string? DamageType { get; set; }
    public string? VersatileDamageDice { get; set; }
    public int? ArmorClass { get; set; }
    public int? StrengthRequirement { get; set; }
    public bool StealthDisadvantage { get; set; }
    public int? RangeNormal { get; set; }
    public int? RangeLong { get; set; }
    public int? SourceMaterialId { get; set; }
    public int? CampaignId { get; set; }
    public string? SourceBook { get; set; }
    public int? SourcePage { get; set; }
    public bool IsConsumable { get; set; }
    public int? ChargesCurrent { get; set; }
    public int? ChargesMax { get; set; }
    public string? RechargeRule { get; set; }
    public int? UsesPerDay { get; set; }
    public string? ArmorCategory { get; set; }
    public bool WeaponPropertyLight { get; set; }
    public bool WeaponPropertyHeavy { get; set; }
    public bool WeaponPropertyFinesse { get; set; }
    public bool WeaponPropertyThrown { get; set; }
    public bool WeaponPropertyTwoHanded { get; set; }
    public bool WeaponPropertyLoading { get; set; }
    public bool WeaponPropertyReach { get; set; }
    public bool WeaponPropertyAmmunition { get; set; }
    public SourceType SourceType { get; set; } = SourceType.Official;
    public DateTime DateCreatedUtc { get; set; }
    public DateTime DateModifiedUtc { get; set; }
    public DateTime? DateDeletedUtc { get; set; }
}

public sealed record CreateItemTypeRequest(int GameSystemId, string Name, string? Description);


public sealed record CreateItemRequest(int GameSystemId, string Name, int? ItemTypeDefinitionId, int? RarityDefinitionId, string? Description, decimal? CostAmount = null, int? CurrencyDefinitionId = null, string? CostCurrency = null, decimal? Weight = null, int Quantity = 1, string? Tags = null, string? Effect = null, bool RequiresAttunement = false, string? AttunementRequirement = null, string? DamageDice = null, string? DamageType = null, string? VersatileDamageDice = null, int? ArmorClass = null, int? StrengthRequirement = null, bool StealthDisadvantage = false, int? RangeNormal = null, int? RangeLong = null, int? SourceMaterialId = null, int? CampaignId = null, string? SourceBook = null, int? SourcePage = null, bool IsConsumable = false, int? ChargesCurrent = null, int? ChargesMax = null, string? RechargeRule = null, int? UsesPerDay = null, string? ArmorCategory = null, bool WeaponPropertyLight = false, bool WeaponPropertyHeavy = false, bool WeaponPropertyFinesse = false, bool WeaponPropertyThrown = false, bool WeaponPropertyTwoHanded = false, bool WeaponPropertyLoading = false, bool WeaponPropertyReach = false, bool WeaponPropertyAmmunition = false, int? OwnerAppUserId = null, List<int>? TagDefinitionIds = null, SourceType SourceType = SourceType.Official, string? Alias = null);

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
public sealed record UpsertSourceMaterialRequest(int GameSystemId, string Code, string Title, string? Publisher, bool IsOfficial = true);

public sealed record MergeGameSystemsRequest(int FromGameSystemId, int ToGameSystemId);
public sealed record ReassignOrphansRequest(int FromGameSystemId, int ToGameSystemId);
public sealed record ReassignOneOrphanRequest(string Kind, int Id, int ToGameSystemId);


public sealed class FriendRequest
{
    public int FriendRequestId { get; set; }
    public int FromAppUserId { get; set; }
    public int ToAppUserId { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime DateCreatedUtc { get; set; }
    public DateTime? DateResolvedUtc { get; set; }
}

public sealed class Friend
{
    public int FriendId { get; set; }
    public int UserAId { get; set; }
    public int UserBId { get; set; }
    public DateTime DateCreatedUtc { get; set; }
}

public sealed record SendFriendRequestRequest(string ToUsername);

public sealed class AppError
{
    public int AppErrorId { get; set; }
    public string ErrorUid { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? Method { get; set; }
    public int? UserId { get; set; }
    public string? Message { get; set; }
    public string? StackTrace { get; set; }
    public DateTime DateCreatedUtc { get; set; }
}

public sealed class CreatureAbility
{
    public int CreatureAbilityId { get; set; }
    public int CreatureId { get; set; }
    public string AbilityType { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime DateCreatedUtc { get; set; }
    public DateTime DateModifiedUtc { get; set; }
    public DateTime? DateDeletedUtc { get; set; }
}

public sealed class FeatureRequest
{
    public int FeatureRequestId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "Backlog";
    public string? Priority { get; set; }
    public string? RequestedBy { get; set; }
    public string? Entity { get; set; }
    public int SortOrder { get; set; }
    public DateTime DateCreatedUtc { get; set; }
    public DateTime DateModifiedUtc { get; set; }
    public DateTime? DateDeletedUtc { get; set; }
}

public sealed class Creature
{
    public int CreatureId { get; set; }
    public int GameSystemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public string? CreatureType { get; set; }
    public string? Size { get; set; }
    public string? Alignment { get; set; }
    public int? ArmorClass { get; set; }
    public int? HitPoints { get; set; }
    public string? Speed { get; set; }
    public int? Strength { get; set; }
    public int? Dexterity { get; set; }
    public int? Constitution { get; set; }
    public int? Intelligence { get; set; }
    public int? Wisdom { get; set; }
    public int? Charisma { get; set; }
    public string? ChallengeRating { get; set; }
    public int? ProficiencyBonus { get; set; }
    public string? Description { get; set; }
    public SourceType SourceType { get; set; } = SourceType.Official;
    public int? OwnerAppUserId { get; set; }
    public int? SourceMaterialId { get; set; }
    public int? CampaignId { get; set; }
    public int? SourcePage { get; set; }
    public DateTime DateCreatedUtc { get; set; }
    public DateTime DateModifiedUtc { get; set; }
    public DateTime? DateDeletedUtc { get; set; }
}

public sealed record CreateCreatureRequest(int GameSystemId, string Name, string? Alias = null, string? CreatureType = null, string? Size = null, string? Alignment = null, int? ArmorClass = null, int? HitPoints = null, string? Speed = null, int? Strength = null, int? Dexterity = null, int? Constitution = null, int? Intelligence = null, int? Wisdom = null, int? Charisma = null, string? ChallengeRating = null, int? ProficiencyBonus = null, string? Description = null, SourceType SourceType = SourceType.Official, int? OwnerAppUserId = null, int? SourceMaterialId = null, int? CampaignId = null, int? SourcePage = null, List<CreatureAbilityInput>? TraitsList = null, List<CreatureAbilityInput>? ActionsList = null, List<CreatureAbilityInput>? ReactionsList = null, List<CreatureAbilityInput>? LegendaryActionsList = null);
public sealed record CreatureImportRequest(CreateCreatureRequest? Creature = null, List<CreateCreatureRequest>? Creatures = null);
public sealed record UpsertFeatureRequest(string Title, string? Description = null, string? Status = null, string? Priority = null, string? RequestedBy = null, string? Entity = null, int? SortOrder = null);

public sealed record CreatureAbilityInput(string? Name, string Description, int SortOrder = 0);

public sealed class Campaign
{
    public int CampaignId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? OwnerAppUserId { get; set; }
    public DateTime DateCreatedUtc { get; set; }
    public DateTime DateModifiedUtc { get; set; }
    public DateTime? DateDeletedUtc { get; set; }
}

public sealed record UpsertCampaignRequest(string Title, string? Description, int? OwnerAppUserId = null, List<int>? CollaboratorUserIds = null, List<int>? PlayerUserIds = null);

public sealed class CampaignCollaborator
{
    public int CampaignCollaboratorId { get; set; }
    public int CampaignId { get; set; }
    public int AppUserId { get; set; }
    public DateTime DateCreatedUtc { get; set; }
    public DateTime? DateDeletedUtc { get; set; }
}

public sealed class CampaignPlayer
{
    public int CampaignPlayerId { get; set; }
    public int CampaignId { get; set; }
    public int AppUserId { get; set; }
    public DateTime DateCreatedUtc { get; set; }
    public DateTime? DateDeletedUtc { get; set; }
}

public sealed class AppUser
{
    public int AppUserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string Role { get; set; } = "Viewer";
    public bool IsActive { get; set; } = true;
    public bool IsSystemAccount { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime DateCreatedUtc { get; set; }
    public DateTime DateModifiedUtc { get; set; }
    public DateTime? DateDeletedUtc { get; set; }
}

public sealed record RegisterRequest(string Email, string Username, string Password);
public sealed record LoginRequest(string Identifier, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record UpdateUserAdminRequest(string Role, bool IsActive);
public sealed record CreateUserAdminRequest(string Email, string Username, string Password, string Role, bool IsActive = true, bool MustChangePassword = true);

public sealed class SourceMaterial
{
    public int SourceMaterialId { get; set; }
    public int GameSystemId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Publisher { get; set; }
    public bool IsOfficial { get; set; } = true;
    public DateTime DateCreatedUtc { get; set; }
    public DateTime DateModifiedUtc { get; set; }
    public DateTime? DateDeletedUtc { get; set; }
}

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
