using Microsoft.AspNetCore.Authentication;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using RuleForge.Contracts.Characters;
using RuleForge.Contracts.Campaigns;
using RuleForge.Contracts.Bestiary;
using RuleForge.Contracts.Encounters;
using RuleForge.Contracts.Parties;
using RuleForge.Contracts.Quests;
using RuleForge.Contracts.Users;
using RuleForge.Contracts.Items;
using RuleForge.Contracts.Marketplace;
using RuleForge.Contracts.Common;
using RuleForge.Data;
using RuleForge.Domain.Characters;
using RuleForge.Domain.Campaigns;
using RuleForge.Domain.Bestiary;
using RuleForge.Domain.Encounters;
using RuleForge.Domain.Common;
using RuleForge.Domain.Parties;
using RuleForge.Domain.Quests;
using RuleForge.Domain.Users;
using RuleForge.Domain.Items;
using RuleForge.Domain.Marketplace;
using MudBlazor.Services;
using RuleForge.Components;

var builder = WebApplication.CreateBuilder(args);
var localSeqSettings = LoadLocalSeqSettings(builder.Environment.ContentRootPath);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    var seqUrl = Environment.GetEnvironmentVariable("RULEFORGE_SEQ_URL")
        ?? localSeqSettings.ServerUrl
        ?? context.Configuration["Logging:Seq:ServerUrl"];
    var seqApiKey = Environment.GetEnvironmentVariable("RULEFORGE_SEQ_API_KEY")
        ?? localSeqSettings.ApiKey
        ?? context.Configuration["Logging:Seq:ApiKey"];
    var appName = localSeqSettings.AppName
        ?? context.Configuration["Logging:Seq:AppName"]
        ?? "RuleForge";

    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithProperty("Application", appName)
        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName);

    if (!string.IsNullOrWhiteSpace(seqUrl))
    {
        loggerConfiguration.WriteTo.Seq(seqUrl, apiKey: string.IsNullOrWhiteSpace(seqApiKey) ? null : seqApiKey);
    }
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddHttpClient();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    ctx.Response.ContentType = "application/json";
                    return ctx.Response.WriteAsync(JsonSerializer.Serialize(new ApiError("AUTH_REQUIRED", "Authentication required.", ctx.HttpContext.TraceIdentifier)));
                }
                ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    ctx.Response.ContentType = "application/json";
                    return ctx.Response.WriteAsync(JsonSerializer.Serialize(new ApiError("FORBIDDEN", "You do not have access.", ctx.HttpContext.TraceIdentifier)));
                }
                ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

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

app.Use(async (ctx, next) =>
{
    var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
    var username = ctx.User.Identity?.Name ?? "anonymous";

    using (LogContext.PushProperty("TraceId", ctx.TraceIdentifier))
    using (LogContext.PushProperty("RequestPath", ctx.Request.Path.Value ?? string.Empty))
    using (LogContext.PushProperty("RequestMethod", ctx.Request.Method))
    using (LogContext.PushProperty("UserId", userId))
    using (LogContext.PushProperty("Username", username))
    {
        await next();
    }
});

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, elapsed, ex) =>
    {
        if (ex is not null || httpContext.Response.StatusCode >= 500) return LogEventLevel.Error;
        if (httpContext.Response.StatusCode >= 400) return LogEventLevel.Warning;
        return LogEventLevel.Information;
    };

    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
        diagnosticContext.Set("RemoteIpAddress", httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
        diagnosticContext.Set("UserId", httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous");
        diagnosticContext.Set("Username", httpContext.User.Identity?.Name ?? "anonymous");
    };
});

// API exception envelope middleware
app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        Log.Error(ex,
            "Unhandled API exception for {Method} {Path}. TraceId: {TraceId}",
            ctx.Request.Method,
            ctx.Request.Path.Value,
            ctx.TraceIdentifier);

        if (!ctx.Request.Path.StartsWithSegments("/api")) throw;
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/json";
        var code = ex is DbUpdateException ? "DB_UPDATE_ERROR" : "UNHANDLED_ERROR";
        var msg = ex is DbUpdateException ? "A database error occurred while saving." : "An unexpected error occurred.";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new ApiError(code, msg, ctx.TraceIdentifier)));
    }
});

app.Use(async (ctx, next) =>
{
    if (!ctx.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    var originalBody = ctx.Response.Body;
    await using var responseBuffer = new MemoryStream();
    ctx.Response.Body = responseBuffer;

    try
    {
        await next();

        responseBuffer.Position = 0;
        var responseText = await new StreamReader(responseBuffer, Encoding.UTF8, leaveOpen: true).ReadToEndAsync();
        responseBuffer.Position = 0;

        if (ctx.Response.StatusCode >= 400)
        {
            var contentType = ctx.Response.ContentType ?? string.Empty;
            string? bodySummary = null;

            if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                bodySummary = SummarizeApiErrorBody(responseText);
            }
            else if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            {
                bodySummary = TruncateForLog(responseText);
            }

            Log.Warning(
                "API request returned {StatusCode} for {Method} {Path}. TraceId: {TraceId}. ResponseBody: {ResponseBody}",
                ctx.Response.StatusCode,
                ctx.Request.Method,
                ctx.Request.Path.Value,
                ctx.TraceIdentifier,
                bodySummary ?? string.Empty);
        }

        await responseBuffer.CopyToAsync(originalBody);
    }
    finally
    {
        ctx.Response.Body = originalBody;
    }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePages(async statusContext =>
{
    var http = statusContext.HttpContext;
    if (http.Request.Path.StartsWithSegments("/api"))
    {
        http.Response.ContentType = "application/json";
        var traceId = http.TraceIdentifier;
        var payload = http.Response.StatusCode switch
        {
            StatusCodes.Status404NotFound => new ApiError("NOT_FOUND", "The requested API resource was not found.", traceId),
            StatusCodes.Status401Unauthorized => new ApiError("AUTH_REQUIRED", "Authentication required.", traceId),
            StatusCodes.Status403Forbidden => new ApiError("FORBIDDEN", "You do not have access.", traceId),
            _ => new ApiError("HTTP_ERROR", "The request could not be completed.", traceId)
        };

        await http.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
});
app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.UseForwardedHeaders();

app.MapStaticAssets();

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/swagger"))
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            await ctx.ChallengeAsync();
            return;
        }

        if (!ctx.User.IsInRole("Admin"))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
    }

    await next();
});

app.UseSwagger();
app.UseSwaggerUI();


using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (destructiveInit)
    {
        db.Database.EnsureDeleted();
    }

    db.Database.EnsureCreated();

    EnsureSkillSchema(db, isSqliteProvider);
    await SeedSkillsAsync(db);
    await EnsureCharacterSkillRowsAsync(db);

    if (!isSqliteProvider)
    {
        // PostgreSQL compatibility patching for evolving schema on existing DBs.
        TryExecuteSqlStatements(db,
            "ALTER TABLE \"Characters\" ADD COLUMN IF NOT EXISTS \"PartyId\" integer NOT NULL DEFAULT 0;",
            "ALTER TABLE \"Characters\" ADD COLUMN IF NOT EXISTS \"PlayerName\" text NULL;",
            "ALTER TABLE \"Characters\" ADD COLUMN IF NOT EXISTS \"SubclassName\" text NULL;",
            "ALTER TABLE \"Characters\" ADD COLUMN IF NOT EXISTS \"RaceName\" text NULL;",
            "ALTER TABLE \"Characters\" ADD COLUMN IF NOT EXISTS \"SubraceName\" text NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"Size\" text NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"CreatureType\" text NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"CreatureSubtype\" text NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"CreatureTypeId\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"ArmorClass\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"ArmorClassNotes\" text NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"HitPoints\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"HitDice\" text NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"InitiativeModifier\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"Speed\" text NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"WalkSpeed\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"FlySpeed\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"SwimSpeed\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"ClimbSpeed\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"BurrowSpeed\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"ChallengeRating\" text NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"ExperiencePoints\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"PassivePerception\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"BlindsightRange\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"DarkvisionRange\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"TremorsenseRange\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"TruesightRange\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"OtherSenses\" text NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"Languages\" text NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"UnderstandsButCannotSpeak\" boolean NOT NULL DEFAULT false;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"Traits\" text NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"Actions\" text NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"Strength\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"Dexterity\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"Constitution\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"Intelligence\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"Wisdom\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"Charisma\" integer NULL;",
            "ALTER TABLE \"Parties\" ADD COLUMN IF NOT EXISTS \"CampaignId\" integer NOT NULL DEFAULT 0;"
        );

        // Quest tables (for existing prod DBs that predate quests feature)
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Quests" (
                "QuestId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                "CampaignId" integer NOT NULL,
                "Title" text NOT NULL,
                "Summary" text NULL,
                "Mode" integer NOT NULL,
                "UseChoiceMode" boolean NOT NULL,
                "StartNodeId" integer NULL,
                "DateCreatedUtc" timestamp with time zone NOT NULL,
                "DateModifiedUtc" timestamp with time zone NOT NULL,
                "DateDeletedUtc" timestamp with time zone NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_Quests_CampaignId_Title" ON "Quests" ("CampaignId", "Title");

            CREATE TABLE IF NOT EXISTS "QuestNodes" (
                "QuestNodeId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                "QuestId" integer NOT NULL,
                "Title" text NOT NULL,
                "NodeType" integer NOT NULL,
                "OrderIndex" integer NOT NULL,
                "BodyMarkdown" text NULL,
                "DmHints" text NULL,
                "EncounterId" integer NULL,
                "CanvasX" double precision NULL,
                "CanvasY" double precision NULL,
                "DateCreatedUtc" timestamp with time zone NOT NULL,
                "DateModifiedUtc" timestamp with time zone NOT NULL,
                "DateDeletedUtc" timestamp with time zone NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_QuestNodes_QuestId_OrderIndex" ON "QuestNodes" ("QuestId", "OrderIndex");

            CREATE TABLE IF NOT EXISTS "QuestChoices" (
                "QuestChoiceId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                "QuestId" integer NOT NULL,
                "FromNodeId" integer NOT NULL,
                "ToNodeId" integer NOT NULL,
                "Label" text NOT NULL,
                "ConditionExpression" text NULL,
                "EffectsJson" text NULL,
                "OrderIndex" integer NOT NULL,
                "DateCreatedUtc" timestamp with time zone NOT NULL,
                "DateModifiedUtc" timestamp with time zone NOT NULL,
                "DateDeletedUtc" timestamp with time zone NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_QuestChoices_QuestId_FromNodeId_OrderIndex" ON "QuestChoices" ("QuestId", "FromNodeId", "OrderIndex");
        """);

        try { db.Database.ExecuteSqlRaw("ALTER TABLE \"QuestNodes\" ADD COLUMN IF NOT EXISTS \"CanvasX\" double precision NULL;"); } catch { }
        try { db.Database.ExecuteSqlRaw("ALTER TABLE \"QuestNodes\" ADD COLUMN IF NOT EXISTS \"CanvasY\" double precision NULL;"); } catch { }

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Users" (
                "AppUserId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                "Username" text NOT NULL,
                "Email" text NOT NULL,
                "PasswordHash" text NOT NULL,
                "Role" integer NOT NULL,
                "DateCreatedUtc" timestamp with time zone NOT NULL,
                "DateModifiedUtc" timestamp with time zone NOT NULL,
                "DateDeletedUtc" timestamp with time zone NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Username" ON "Users" ("Username");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users" ("Email");
        """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Items" (
                "ItemId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                "Name" text NOT NULL, "Description" text NULL, "ItemType" integer NOT NULL, "Rarity" integer NOT NULL,
                "Weight" numeric NULL, "CostAmount" numeric NULL, "CostCurrency" text NULL, "RequiresAttunement" boolean NOT NULL,
                "SourceType" integer NULL, "Source" text NULL, "Tags" text NULL, "WeaponCategory" text NULL, "DamageDice" text NULL, "DamageType" text NULL,
                "Properties" text NULL, "RangeNormal" integer NULL, "RangeMax" integer NULL, "IsMagicWeapon" boolean NOT NULL,
                "AttackBonus" integer NULL, "DamageBonus" integer NULL, "ArmorCategory" text NULL, "ArmorClassBase" integer NULL,
                "DexCap" integer NULL, "StrengthRequirement" integer NULL, "StealthDisadvantage" boolean NOT NULL,
                "IsMagicArmor" boolean NOT NULL, "ArmorBonus" integer NULL, "Charges" integer NULL, "MaxCharges" integer NULL,
                "RechargeRule" text NULL, "ConsumableEffect" text NULL, "Quantity" integer NULL, "Stackable" boolean NOT NULL,
                "Notes" text NULL, "DateCreatedUtc" timestamp with time zone NOT NULL, "DateModifiedUtc" timestamp with time zone NOT NULL, "DateDeletedUtc" timestamp with time zone NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_Items_Name" ON "Items" ("Name");
        """);
        TryExecuteSqlStatements(db,
            "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"MustChangePassword\" boolean NOT NULL DEFAULT false;",
            "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"IsSystem\" boolean NOT NULL DEFAULT false;",
            "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"ThemePreference\" text NULL;",
            "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"CampaignNavExpanded\" boolean NULL;",
            "ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"CompendiumNavExpanded\" boolean NULL;",
            "ALTER TABLE \"Campaigns\" ADD COLUMN IF NOT EXISTS \"OwnerAppUserId\" integer NULL;",
            "ALTER TABLE \"Parties\" ADD COLUMN IF NOT EXISTS \"OwnerAppUserId\" integer NULL;",
            "ALTER TABLE \"Quests\" ADD COLUMN IF NOT EXISTS \"OwnerAppUserId\" integer NULL;",
            "ALTER TABLE \"Items\" ADD COLUMN IF NOT EXISTS \"OwnerAppUserId\" integer NULL;",
            "ALTER TABLE \"Items\" ADD COLUMN IF NOT EXISTS \"IsSystem\" boolean NOT NULL DEFAULT false;",
            "ALTER TABLE \"Items\" ADD COLUMN IF NOT EXISTS \"SourceType\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"OwnerAppUserId\" integer NULL;",
            "ALTER TABLE \"Creatures\" ADD COLUMN IF NOT EXISTS \"IsSystem\" boolean NOT NULL DEFAULT false;"
        );
        ExecuteSqlStatements(db,
            "CREATE INDEX IF NOT EXISTS \"IX_Campaigns_OwnerAppUserId\" ON \"Campaigns\" (\"OwnerAppUserId\");",
            "CREATE INDEX IF NOT EXISTS \"IX_Parties_OwnerAppUserId\" ON \"Parties\" (\"OwnerAppUserId\");",
            "CREATE INDEX IF NOT EXISTS \"IX_Quests_OwnerAppUserId\" ON \"Quests\" (\"OwnerAppUserId\");",
            "CREATE INDEX IF NOT EXISTS \"IX_Items_OwnerAppUserId\" ON \"Items\" (\"OwnerAppUserId\");",
            "CREATE INDEX IF NOT EXISTS \"IX_Creatures_OwnerAppUserId\" ON \"Creatures\" (\"OwnerAppUserId\");"
        );
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "CharacterShares" (
                "CharacterShareId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                "CharacterId" integer NOT NULL,
                "SharedWithUserId" integer NOT NULL,
                "Permission" integer NOT NULL,
                "DateCreatedUtc" timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_CharacterShares_CharacterId_SharedWithUserId" ON "CharacterShares" ("CharacterId", "SharedWithUserId");
            CREATE TABLE IF NOT EXISTS "ItemShares" (
                "ItemShareId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                "ItemId" integer NOT NULL,
                "SharedWithUserId" integer NOT NULL,
                "Permission" integer NOT NULL,
                "DateCreatedUtc" timestamp with time zone NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ItemShares_ItemId_SharedWithUserId" ON "ItemShares" ("ItemId", "SharedWithUserId");
            CREATE TABLE IF NOT EXISTS "CampaignShares" ("CampaignShareId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "CampaignId" integer NOT NULL, "SharedWithUserId" integer NOT NULL, "Permission" integer NOT NULL, "DateCreatedUtc" timestamp with time zone NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_CampaignShares_CampaignId_SharedWithUserId" ON "CampaignShares" ("CampaignId", "SharedWithUserId");
            CREATE TABLE IF NOT EXISTS "PartyShares" ("PartyShareId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "PartyId" integer NOT NULL, "SharedWithUserId" integer NOT NULL, "Permission" integer NOT NULL, "DateCreatedUtc" timestamp with time zone NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PartyShares_PartyId_SharedWithUserId" ON "PartyShares" ("PartyId", "SharedWithUserId");
            CREATE TABLE IF NOT EXISTS "QuestShares" ("QuestShareId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "QuestId" integer NOT NULL, "SharedWithUserId" integer NOT NULL, "Permission" integer NOT NULL, "DateCreatedUtc" timestamp with time zone NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_QuestShares_QuestId_SharedWithUserId" ON "QuestShares" ("QuestId", "SharedWithUserId");

            CREATE TABLE IF NOT EXISTS "MarketplaceListings" ("MarketplaceListingId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "AssetType" integer NOT NULL, "SourceEntityId" integer NOT NULL, "OwnerUserId" integer NULL, "OwnershipType" integer NOT NULL, "State" integer NOT NULL, "Title" text NOT NULL, "Summary" text NULL, "TagsJson" text NULL, "LatestVersionId" integer NULL, "DateCreatedUtc" timestamp with time zone NOT NULL, "DateModifiedUtc" timestamp with time zone NOT NULL, "DateRemovedUtc" timestamp with time zone NULL);
            CREATE INDEX IF NOT EXISTS "IX_MarketplaceListings_AssetType_State" ON "MarketplaceListings" ("AssetType", "State");
            CREATE INDEX IF NOT EXISTS "IX_MarketplaceListings_OwnerUserId_State" ON "MarketplaceListings" ("OwnerUserId", "State");

            CREATE TABLE IF NOT EXISTS "MarketplaceListingVersions" ("MarketplaceListingVersionId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "MarketplaceListingId" integer NOT NULL, "VersionLabel" text NOT NULL, "PayloadJson" text NOT NULL, "Changelog" text NULL, "CreatedByUserId" integer NOT NULL, "DateCreatedUtc" timestamp with time zone NOT NULL);
            CREATE INDEX IF NOT EXISTS "IX_MarketplaceListingVersions_ListingId_DateCreatedUtc" ON "MarketplaceListingVersions" ("MarketplaceListingId", "DateCreatedUtc");

            CREATE TABLE IF NOT EXISTS "MarketplaceImports" ("MarketplaceImportId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "MarketplaceListingId" integer NOT NULL, "MarketplaceListingVersionId" integer NOT NULL, "ImportedByUserId" integer NOT NULL, "AssetType" integer NOT NULL, "NewEntityId" integer NOT NULL, "DateImportedUtc" timestamp with time zone NOT NULL);
            CREATE INDEX IF NOT EXISTS "IX_MarketplaceImports_ImportedByUserId_DateImportedUtc" ON "MarketplaceImports" ("ImportedByUserId", "DateImportedUtc");

            CREATE TABLE IF NOT EXISTS "MarketplaceAuditEvents" ("MarketplaceAuditEventId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "MarketplaceListingId" integer NOT NULL, "ActorUserId" integer NOT NULL, "EventType" text NOT NULL, "PayloadJson" text NULL, "DateUtc" timestamp with time zone NOT NULL);
            CREATE INDEX IF NOT EXISTS "IX_MarketplaceAuditEvents_ListingId_DateUtc" ON "MarketplaceAuditEvents" ("MarketplaceListingId", "DateUtc");
            CREATE TABLE IF NOT EXISTS "CreatureShares" ("CreatureShareId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "CreatureId" integer NOT NULL, "SharedWithUserId" integer NOT NULL, "Permission" integer NOT NULL, "DateCreatedUtc" timestamp with time zone NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_CreatureShares_CreatureId_SharedWithUserId" ON "CreatureShares" ("CreatureId", "SharedWithUserId");
            CREATE TABLE IF NOT EXISTS "CreatureTraits" ("CreatureTraitId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "CreatureId" integer NOT NULL, "Name" text NOT NULL, "Description" text NULL, "SortOrder" integer NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS "IX_CreatureTraits_CreatureId_SortOrder" ON "CreatureTraits" ("CreatureId", "SortOrder");
            CREATE TABLE IF NOT EXISTS "CreatureActions" ("CreatureActionId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "CreatureId" integer NOT NULL, "Name" text NOT NULL, "Description" text NULL, "SortOrder" integer NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS "IX_CreatureActions_CreatureId_SortOrder" ON "CreatureActions" ("CreatureId", "SortOrder");
            CREATE TABLE IF NOT EXISTS "CreatureTypes" ("CreatureTypeId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "Key" text NOT NULL, "Name" text NOT NULL, "DisplayOrder" integer NOT NULL DEFAULT 0, "IsActive" boolean NOT NULL DEFAULT true);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_CreatureTypes_Key" ON "CreatureTypes" ("Key");
            CREATE INDEX IF NOT EXISTS "IX_CreatureTypes_DisplayOrder" ON "CreatureTypes" ("DisplayOrder");
            CREATE TABLE IF NOT EXISTS "CreatureSubtypes" ("CreatureSubtypeId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "CreatureTypeId" integer NULL, "Key" text NOT NULL, "Name" text NOT NULL, "DisplayOrder" integer NOT NULL DEFAULT 0, "IsActive" boolean NOT NULL DEFAULT true);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_CreatureSubtypes_Key" ON "CreatureSubtypes" ("Key");
            CREATE INDEX IF NOT EXISTS "IX_CreatureSubtypes_CreatureTypeId_DisplayOrder" ON "CreatureSubtypes" ("CreatureTypeId", "DisplayOrder");
            CREATE TABLE IF NOT EXISTS "CreatureCreatureSubtypes" ("CreatureCreatureSubtypeId" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "CreatureId" integer NOT NULL, "CreatureSubtypeId" integer NOT NULL, "SortOrder" integer NOT NULL DEFAULT 0);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_CreatureCreatureSubtypes_CreatureId_CreatureSubtypeId" ON "CreatureCreatureSubtypes" ("CreatureId", "CreatureSubtypeId");
            CREATE INDEX IF NOT EXISTS "IX_CreatureCreatureSubtypes_CreatureSubtypeId_SortOrder" ON "CreatureCreatureSubtypes" ("CreatureSubtypeId", "SortOrder");
        """);
        ExecuteSqlStatements(db,
            "CREATE INDEX IF NOT EXISTS \"IX_Creatures_CreatureTypeId\" ON \"Creatures\" (\"CreatureTypeId\");",
            "CREATE INDEX IF NOT EXISTS \"IX_Creatures_CreatureType\" ON \"Creatures\" (\"CreatureType\");",
            "CREATE INDEX IF NOT EXISTS \"IX_Creatures_CreatureSubtype\" ON \"Creatures\" (\"CreatureSubtype\");"
        );
    }

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

    TryExecuteSqlStatements(db,
        "ALTER TABLE Campaigns ADD COLUMN Description TEXT NULL;",
        "ALTER TABLE Campaigns ADD COLUMN OwnerAppUserId INTEGER NULL;",
        "CREATE INDEX IF NOT EXISTS IX_Campaigns_OwnerAppUserId ON Campaigns (OwnerAppUserId);"
    );

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS Creatures (
            CreatureId INTEGER NOT NULL CONSTRAINT PK_Creatures PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Description TEXT NULL,
            Size TEXT NULL,
            CreatureType TEXT NULL,
            CreatureSubtype TEXT NULL,
            CreatureTypeId INTEGER NULL,
            ArmorClass INTEGER NULL,
            ArmorClassNotes TEXT NULL,
            HitPoints INTEGER NULL,
            HitDice TEXT NULL,
            InitiativeModifier INTEGER NULL,
            Speed TEXT NULL,
            WalkSpeed INTEGER NULL,
            FlySpeed INTEGER NULL,
            SwimSpeed INTEGER NULL,
            ClimbSpeed INTEGER NULL,
            BurrowSpeed INTEGER NULL,
            ChallengeRating TEXT NULL,
            ExperiencePoints INTEGER NULL,
            PassivePerception INTEGER NULL,
            BlindsightRange INTEGER NULL,
            DarkvisionRange INTEGER NULL,
            TremorsenseRange INTEGER NULL,
            TruesightRange INTEGER NULL,
            OtherSenses TEXT NULL,
            Languages TEXT NULL,
            UnderstandsButCannotSpeak INTEGER NOT NULL DEFAULT 0,
            Traits TEXT NULL,
            Actions TEXT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_Creatures_Name ON Creatures (Name);

        CREATE TABLE IF NOT EXISTS CreatureTraits (
            CreatureTraitId INTEGER NOT NULL CONSTRAINT PK_CreatureTraits PRIMARY KEY AUTOINCREMENT,
            CreatureId INTEGER NOT NULL,
            Name TEXT NOT NULL,
            Description TEXT NULL,
            SortOrder INTEGER NOT NULL DEFAULT 0,
            CONSTRAINT FK_CreatureTraits_Creatures_CreatureId FOREIGN KEY (CreatureId) REFERENCES Creatures (CreatureId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_CreatureTraits_CreatureId_SortOrder ON CreatureTraits (CreatureId, SortOrder);

        CREATE TABLE IF NOT EXISTS CreatureActions (
            CreatureActionId INTEGER NOT NULL CONSTRAINT PK_CreatureActions PRIMARY KEY AUTOINCREMENT,
            CreatureId INTEGER NOT NULL,
            Name TEXT NOT NULL,
            Description TEXT NULL,
            SortOrder INTEGER NOT NULL DEFAULT 0,
            CONSTRAINT FK_CreatureActions_Creatures_CreatureId FOREIGN KEY (CreatureId) REFERENCES Creatures (CreatureId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_CreatureActions_CreatureId_SortOrder ON CreatureActions (CreatureId, SortOrder);
    """);

    TryExecuteSqlStatements(db,
        "ALTER TABLE Creatures ADD COLUMN ArmorClass INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN HitPoints INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN InitiativeModifier INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN Speed TEXT NULL;",
        "ALTER TABLE Creatures ADD COLUMN WalkSpeed INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN FlySpeed INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN SwimSpeed INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN ClimbSpeed INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN BurrowSpeed INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN ChallengeRating TEXT NULL;",
        "ALTER TABLE Creatures ADD COLUMN ExperiencePoints INTEGER NULL;",
        "ALTER TABLE Characters ADD COLUMN PartyId INTEGER NOT NULL DEFAULT 0;",
        "ALTER TABLE Characters ADD COLUMN PlayerName TEXT NULL;",
        "ALTER TABLE Characters ADD COLUMN SubclassName TEXT NULL;",
        "ALTER TABLE Characters ADD COLUMN RaceName TEXT NULL;",
        "ALTER TABLE Characters ADD COLUMN SubraceName TEXT NULL;"
    );

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
    TryExecuteSqlStatements(db,
        "ALTER TABLE Parties ADD COLUMN CampaignId INTEGER NOT NULL DEFAULT 0;",
        "ALTER TABLE Parties ADD COLUMN OwnerAppUserId INTEGER NULL;",
        "CREATE INDEX IF NOT EXISTS IX_Parties_OwnerAppUserId ON Parties (OwnerAppUserId);",
        "CREATE UNIQUE INDEX IF NOT EXISTS IX_Parties_CampaignId_Name ON Parties (CampaignId, Name);",
        "DROP INDEX IF EXISTS IX_Parties_Name;",
        "ALTER TABLE Creatures ADD COLUMN Size TEXT NULL;",
        "ALTER TABLE Creatures ADD COLUMN CreatureType TEXT NULL;",
        "ALTER TABLE Creatures ADD COLUMN CreatureSubtype TEXT NULL;",
        "ALTER TABLE Creatures ADD COLUMN CreatureTypeId INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN ArmorClassNotes TEXT NULL;",
        "ALTER TABLE Creatures ADD COLUMN HitDice TEXT NULL;",
        "ALTER TABLE Creatures ADD COLUMN PassivePerception INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN BlindsightRange INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN DarkvisionRange INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN TremorsenseRange INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN TruesightRange INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN OtherSenses TEXT NULL;",
        "ALTER TABLE Creatures ADD COLUMN Languages TEXT NULL;",
        "ALTER TABLE Creatures ADD COLUMN UnderstandsButCannotSpeak INTEGER NOT NULL DEFAULT 0;",
        "ALTER TABLE Creatures ADD COLUMN Traits TEXT NULL;",
        "ALTER TABLE Creatures ADD COLUMN Actions TEXT NULL;",
        "ALTER TABLE Creatures ADD COLUMN Strength INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN Dexterity INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN Constitution INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN Intelligence INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN Wisdom INTEGER NULL;",
        "ALTER TABLE Creatures ADD COLUMN Charisma INTEGER NULL;"
    );

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS CreatureTypes (
            CreatureTypeId INTEGER NOT NULL CONSTRAINT PK_CreatureTypes PRIMARY KEY AUTOINCREMENT,
            Key TEXT NOT NULL,
            Name TEXT NOT NULL,
            DisplayOrder INTEGER NOT NULL DEFAULT 0,
            IsActive INTEGER NOT NULL DEFAULT 1
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_CreatureTypes_Key ON CreatureTypes (Key);
        CREATE INDEX IF NOT EXISTS IX_CreatureTypes_DisplayOrder ON CreatureTypes (DisplayOrder);

        CREATE TABLE IF NOT EXISTS CreatureSubtypes (
            CreatureSubtypeId INTEGER NOT NULL CONSTRAINT PK_CreatureSubtypes PRIMARY KEY AUTOINCREMENT,
            CreatureTypeId INTEGER NULL,
            Key TEXT NOT NULL,
            Name TEXT NOT NULL,
            DisplayOrder INTEGER NOT NULL DEFAULT 0,
            IsActive INTEGER NOT NULL DEFAULT 1
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_CreatureSubtypes_Key ON CreatureSubtypes (Key);
        CREATE INDEX IF NOT EXISTS IX_CreatureSubtypes_CreatureTypeId_DisplayOrder ON CreatureSubtypes (CreatureTypeId, DisplayOrder);

        CREATE TABLE IF NOT EXISTS CreatureCreatureSubtypes (
            CreatureCreatureSubtypeId INTEGER NOT NULL CONSTRAINT PK_CreatureCreatureSubtypes PRIMARY KEY AUTOINCREMENT,
            CreatureId INTEGER NOT NULL,
            CreatureSubtypeId INTEGER NOT NULL,
            SortOrder INTEGER NOT NULL DEFAULT 0,
            CONSTRAINT FK_CreatureCreatureSubtypes_Creatures_CreatureId FOREIGN KEY (CreatureId) REFERENCES Creatures (CreatureId) ON DELETE CASCADE,
            CONSTRAINT FK_CreatureCreatureSubtypes_CreatureSubtypes_CreatureSubtypeId FOREIGN KEY (CreatureSubtypeId) REFERENCES CreatureSubtypes (CreatureSubtypeId) ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_CreatureCreatureSubtypes_CreatureId_CreatureSubtypeId ON CreatureCreatureSubtypes (CreatureId, CreatureSubtypeId);
        CREATE INDEX IF NOT EXISTS IX_CreatureCreatureSubtypes_CreatureSubtypeId_SortOrder ON CreatureCreatureSubtypes (CreatureSubtypeId, SortOrder);
        CREATE INDEX IF NOT EXISTS IX_Creatures_CreatureTypeId ON Creatures (CreatureTypeId);
        CREATE INDEX IF NOT EXISTS IX_Creatures_CreatureType ON Creatures (CreatureType);
        CREATE INDEX IF NOT EXISTS IX_Creatures_CreatureSubtype ON Creatures (CreatureSubtype);
    """);

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
        CREATE TABLE IF NOT EXISTS Quests (
            QuestId INTEGER NOT NULL CONSTRAINT PK_Quests PRIMARY KEY AUTOINCREMENT,
            CampaignId INTEGER NOT NULL,
            Title TEXT NOT NULL,
            Summary TEXT NULL,
            Mode INTEGER NOT NULL,
            UseChoiceMode INTEGER NOT NULL,
            StartNodeId INTEGER NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_Quests_CampaignId_Title ON Quests (CampaignId, Title);

        CREATE TABLE IF NOT EXISTS QuestNodes (
            QuestNodeId INTEGER NOT NULL CONSTRAINT PK_QuestNodes PRIMARY KEY AUTOINCREMENT,
            QuestId INTEGER NOT NULL,
            Title TEXT NOT NULL,
            NodeType INTEGER NOT NULL,
            OrderIndex INTEGER NOT NULL,
            BodyMarkdown TEXT NULL,
            DmHints TEXT NULL,
            EncounterId INTEGER NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL,
            CONSTRAINT FK_QuestNodes_Quests_QuestId FOREIGN KEY (QuestId) REFERENCES Quests (QuestId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_QuestNodes_QuestId_OrderIndex ON QuestNodes (QuestId, OrderIndex);
        
        -- canvas coordinates
        

        CREATE TABLE IF NOT EXISTS QuestChoices (
            QuestChoiceId INTEGER NOT NULL CONSTRAINT PK_QuestChoices PRIMARY KEY AUTOINCREMENT,
            QuestId INTEGER NOT NULL,
            FromNodeId INTEGER NOT NULL,
            ToNodeId INTEGER NOT NULL,
            Label TEXT NOT NULL,
            ConditionExpression TEXT NULL,
            EffectsJson TEXT NULL,
            OrderIndex INTEGER NOT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL,
            CONSTRAINT FK_QuestChoices_Quests_QuestId FOREIGN KEY (QuestId) REFERENCES Quests (QuestId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_QuestChoices_QuestId_FromNodeId_OrderIndex ON QuestChoices (QuestId, FromNodeId, OrderIndex);

        CREATE TABLE IF NOT EXISTS Users (
            AppUserId INTEGER NOT NULL CONSTRAINT PK_Users PRIMARY KEY AUTOINCREMENT,
            Username TEXT NOT NULL,
            Email TEXT NOT NULL,
            PasswordHash TEXT NOT NULL,
            Role INTEGER NOT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateDeletedUtc TEXT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Username ON Users (Username);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Email ON Users (Email);

        CREATE TABLE IF NOT EXISTS Items (
            ItemId INTEGER NOT NULL CONSTRAINT PK_Items PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL, Description TEXT NULL, ItemType INTEGER NOT NULL, Rarity INTEGER NOT NULL,
            Weight TEXT NULL, CostAmount TEXT NULL, CostCurrency TEXT NULL, RequiresAttunement INTEGER NOT NULL,
            Source TEXT NULL, Tags TEXT NULL, WeaponCategory TEXT NULL, DamageDice TEXT NULL, DamageType TEXT NULL,
            Properties TEXT NULL, RangeNormal INTEGER NULL, RangeMax INTEGER NULL, IsMagicWeapon INTEGER NOT NULL,
            AttackBonus INTEGER NULL, DamageBonus INTEGER NULL, ArmorCategory TEXT NULL, ArmorClassBase INTEGER NULL,
            DexCap INTEGER NULL, StrengthRequirement INTEGER NULL, StealthDisadvantage INTEGER NOT NULL,
            IsMagicArmor INTEGER NOT NULL, ArmorBonus INTEGER NULL, Charges INTEGER NULL, MaxCharges INTEGER NULL,
            RechargeRule TEXT NULL, ConsumableEffect TEXT NULL, Quantity INTEGER NULL, Stackable INTEGER NOT NULL,
            Notes TEXT NULL, DateCreatedUtc TEXT NOT NULL, DateModifiedUtc TEXT NOT NULL, DateDeletedUtc TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_Items_Name ON Items (Name);
    """);
    TryExecuteSqlStatements(db,
        "ALTER TABLE Users ADD COLUMN MustChangePassword INTEGER NOT NULL DEFAULT 0;",
        "ALTER TABLE Users ADD COLUMN IsSystem INTEGER NOT NULL DEFAULT 0;",
        "ALTER TABLE Users ADD COLUMN ThemePreference TEXT NULL;",
        "ALTER TABLE Users ADD COLUMN CampaignNavExpanded INTEGER NULL;",
        "ALTER TABLE Users ADD COLUMN CompendiumNavExpanded INTEGER NULL;",
        "ALTER TABLE Items ADD COLUMN OwnerAppUserId INTEGER NULL;",
        "ALTER TABLE Items ADD COLUMN SourceType INTEGER NULL;",
        "ALTER TABLE Items ADD COLUMN IsSystem INTEGER NOT NULL DEFAULT 0;",
        "ALTER TABLE Creatures ADD COLUMN IsSystem INTEGER NOT NULL DEFAULT 0;",
        "ALTER TABLE Creatures ADD COLUMN OwnerAppUserId INTEGER NULL;",
        "CREATE INDEX IF NOT EXISTS IX_Creatures_OwnerAppUserId ON Creatures (OwnerAppUserId);",
        "ALTER TABLE Quests ADD COLUMN OwnerAppUserId INTEGER NULL;",
        "CREATE INDEX IF NOT EXISTS IX_Quests_OwnerAppUserId ON Quests (OwnerAppUserId);",
        "CREATE INDEX IF NOT EXISTS IX_Items_OwnerAppUserId ON Items (OwnerAppUserId);"
    );

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS CharacterShares (
            CharacterShareId INTEGER NOT NULL CONSTRAINT PK_CharacterShares PRIMARY KEY AUTOINCREMENT,
            CharacterId INTEGER NOT NULL,
            SharedWithUserId INTEGER NOT NULL,
            Permission INTEGER NOT NULL,
            DateCreatedUtc TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_CharacterShares_CharacterId_SharedWithUserId ON CharacterShares (CharacterId, SharedWithUserId);

        CREATE TABLE IF NOT EXISTS ItemShares (
            ItemShareId INTEGER NOT NULL CONSTRAINT PK_ItemShares PRIMARY KEY AUTOINCREMENT,
            ItemId INTEGER NOT NULL,
            SharedWithUserId INTEGER NOT NULL,
            Permission INTEGER NOT NULL,
            DateCreatedUtc TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_ItemShares_ItemId_SharedWithUserId ON ItemShares (ItemId, SharedWithUserId);

        CREATE TABLE IF NOT EXISTS CampaignShares (CampaignShareId INTEGER NOT NULL CONSTRAINT PK_CampaignShares PRIMARY KEY AUTOINCREMENT, CampaignId INTEGER NOT NULL, SharedWithUserId INTEGER NOT NULL, Permission INTEGER NOT NULL, DateCreatedUtc TEXT NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_CampaignShares_CampaignId_SharedWithUserId ON CampaignShares (CampaignId, SharedWithUserId);
        CREATE TABLE IF NOT EXISTS PartyShares (PartyShareId INTEGER NOT NULL CONSTRAINT PK_PartyShares PRIMARY KEY AUTOINCREMENT, PartyId INTEGER NOT NULL, SharedWithUserId INTEGER NOT NULL, Permission INTEGER NOT NULL, DateCreatedUtc TEXT NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_PartyShares_PartyId_SharedWithUserId ON PartyShares (PartyId, SharedWithUserId);
        CREATE TABLE IF NOT EXISTS QuestShares (QuestShareId INTEGER NOT NULL CONSTRAINT PK_QuestShares PRIMARY KEY AUTOINCREMENT, QuestId INTEGER NOT NULL, SharedWithUserId INTEGER NOT NULL, Permission INTEGER NOT NULL, DateCreatedUtc TEXT NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_QuestShares_QuestId_SharedWithUserId ON QuestShares (QuestId, SharedWithUserId);
    """);

    TryExecuteSqlStatements(db,
        "ALTER TABLE QuestNodes ADD COLUMN CanvasX REAL NULL;",
        "ALTER TABLE QuestNodes ADD COLUMN CanvasY REAL NULL;"
    );

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS MarketplaceListings (
            MarketplaceListingId INTEGER NOT NULL CONSTRAINT PK_MarketplaceListings PRIMARY KEY AUTOINCREMENT,
            AssetType INTEGER NOT NULL,
            SourceEntityId INTEGER NOT NULL,
            OwnerUserId INTEGER NULL,
            OwnershipType INTEGER NOT NULL,
            State INTEGER NOT NULL,
            Title TEXT NOT NULL,
            Summary TEXT NULL,
            TagsJson TEXT NULL,
            LatestVersionId INTEGER NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateModifiedUtc TEXT NOT NULL,
            DateRemovedUtc TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_MarketplaceListings_AssetType_State ON MarketplaceListings (AssetType, State);
        CREATE INDEX IF NOT EXISTS IX_MarketplaceListings_OwnerUserId_State ON MarketplaceListings (OwnerUserId, State);

        CREATE TABLE IF NOT EXISTS MarketplaceListingVersions (
            MarketplaceListingVersionId INTEGER NOT NULL CONSTRAINT PK_MarketplaceListingVersions PRIMARY KEY AUTOINCREMENT,
            MarketplaceListingId INTEGER NOT NULL,
            VersionLabel TEXT NOT NULL,
            PayloadJson TEXT NOT NULL,
            Changelog TEXT NULL,
            CreatedByUserId INTEGER NOT NULL,
            DateCreatedUtc TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_MarketplaceListingVersions_ListingId_DateCreatedUtc ON MarketplaceListingVersions (MarketplaceListingId, DateCreatedUtc);

        CREATE TABLE IF NOT EXISTS MarketplaceImports (
            MarketplaceImportId INTEGER NOT NULL CONSTRAINT PK_MarketplaceImports PRIMARY KEY AUTOINCREMENT,
            MarketplaceListingId INTEGER NOT NULL,
            MarketplaceListingVersionId INTEGER NOT NULL,
            ImportedByUserId INTEGER NOT NULL,
            AssetType INTEGER NOT NULL,
            NewEntityId INTEGER NOT NULL,
            DateImportedUtc TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_MarketplaceImports_ImportedByUserId_DateImportedUtc ON MarketplaceImports (ImportedByUserId, DateImportedUtc);

        CREATE TABLE IF NOT EXISTS CreatureShares (
            CreatureShareId INTEGER NOT NULL CONSTRAINT PK_CreatureShares PRIMARY KEY AUTOINCREMENT,
            CreatureId INTEGER NOT NULL,
            SharedWithUserId INTEGER NOT NULL,
            Permission INTEGER NOT NULL,
            DateCreatedUtc TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_CreatureShares_CreatureId_SharedWithUserId ON CreatureShares (CreatureId, SharedWithUserId);

        CREATE TABLE IF NOT EXISTS MarketplaceAuditEvents (
            MarketplaceAuditEventId INTEGER NOT NULL CONSTRAINT PK_MarketplaceAuditEvents PRIMARY KEY AUTOINCREMENT,
            MarketplaceListingId INTEGER NOT NULL,
            ActorUserId INTEGER NOT NULL,
            EventType TEXT NOT NULL,
            PayloadJson TEXT NULL,
            DateUtc TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_MarketplaceAuditEvents_ListingId_DateUtc ON MarketplaceAuditEvents (MarketplaceListingId, DateUtc);
    """);

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS FriendLinks (
            FriendLinkId INTEGER NOT NULL CONSTRAINT PK_FriendLinks PRIMARY KEY AUTOINCREMENT,
            RequesterUserId INTEGER NOT NULL,
            AddresseeUserId INTEGER NOT NULL,
            Status INTEGER NOT NULL,
            DateCreatedUtc TEXT NOT NULL,
            DateRespondedUtc TEXT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_FriendLinks_RequesterUserId_AddresseeUserId ON FriendLinks (RequesterUserId, AddresseeUserId);
        CREATE INDEX IF NOT EXISTS IX_FriendLinks_AddresseeUserId_Status ON FriendLinks (AddresseeUserId, Status);
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

    await EnsureCreatureTaxonomyAsync(db);

    // Ensure built-in system admin exists
    var adminUser = db.Users.FirstOrDefault(x => x.Username == "admin");
    if (adminUser is null)
    {
        db.Users.Add(new AppUser
        {
            Username = "admin",
            Email = "admin@local",
            PasswordHash = PasswordHasher.Hash("ChangeMe123!"),
            Role = AppRole.Admin,
            MustChangePassword = true,
            IsSystem = true,
            DateCreatedUtc = DateTime.UtcNow,
            DateModifiedUtc = DateTime.UtcNow
        });
        db.SaveChanges();
    }

}


app.MapGet("/api/items", async (HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http);
    if (userId is null) return Results.Unauthorized();
    var isAdmin = IsAdmin(http);
    var includeAll = isAdmin && showAll == true;

    var sharedIds = includeAll ? new List<int>() : await db.ItemShares.Where(x => x.SharedWithUserId == userId.Value).Select(x => x.ItemId).ToListAsync();

    var rows = await db.Items
        .Where(x => x.DateDeletedUtc == null && (includeAll || x.IsSystem || x.OwnerAppUserId == userId.Value || sharedIds.Contains(x.ItemId)))
        .OrderBy(x => x.Name)
        .Select(x => new ItemResponse
        {
            ItemId = x.ItemId, OwnerAppUserId = x.OwnerAppUserId, IsSystem = x.IsSystem, IsUserVariant = !x.IsSystem && x.SourceType == 1, OwnerUsername = db.Users.Where(u => u.AppUserId == x.OwnerAppUserId).Select(u => u.Username).FirstOrDefault(), Name = x.Name, Description = x.Description, ItemType = (int)x.ItemType, Rarity = (int)x.Rarity,
            Weight = x.Weight, CostAmount = x.CostAmount, CostCurrency = x.CostCurrency, RequiresAttunement = x.RequiresAttunement,
            SourceType = x.SourceType, Source = x.Source, Tags = x.Tags, WeaponCategory = x.WeaponCategory, DamageDice = x.DamageDice, DamageType = x.DamageType,
            Properties = x.Properties, RangeNormal = x.RangeNormal, RangeMax = x.RangeMax, IsMagicWeapon = x.IsMagicWeapon,
            AttackBonus = x.AttackBonus, DamageBonus = x.DamageBonus, ArmorCategory = x.ArmorCategory, ArmorClassBase = x.ArmorClassBase,
            DexCap = x.DexCap, StrengthRequirement = x.StrengthRequirement, StealthDisadvantage = x.StealthDisadvantage,
            IsMagicArmor = x.IsMagicArmor, ArmorBonus = x.ArmorBonus, Charges = x.Charges, MaxCharges = x.MaxCharges,
            RechargeRule = x.RechargeRule, ConsumableEffect = x.ConsumableEffect, Quantity = x.Quantity, Stackable = x.Stackable, Notes = x.Notes
        }).ToListAsync();
    return Results.Ok(rows);
}).RequireAuthorization();

app.MapGet("/api/items/{id:int}", async (int id, HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http);
    if (userId is null) return Results.Unauthorized();
    var isAdmin = IsAdmin(http);
    var includeAll = isAdmin && showAll == true;

    var row = await db.Items.FirstOrDefaultAsync(x => x.ItemId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.IsSystem && !IsAdmin(http)) return Results.Forbid();
    var canView = isAdmin || includeAll || row.IsSystem || row.OwnerAppUserId == userId.Value || await db.ItemShares.AnyAsync(x => x.ItemId == id && x.SharedWithUserId == userId.Value);
    if (!canView) return Results.Forbid();

    return Results.Ok(new ItemResponse
    {
        ItemId = row.ItemId, OwnerAppUserId = row.OwnerAppUserId, IsSystem = row.IsSystem, IsUserVariant = !row.IsSystem && row.SourceType == 1, OwnerUsername = await db.Users.Where(u => u.AppUserId == row.OwnerAppUserId).Select(u => u.Username).FirstOrDefaultAsync(), Name = row.Name, Description = row.Description, ItemType = (int)row.ItemType, Rarity = (int)row.Rarity,
        Weight = row.Weight, CostAmount = row.CostAmount, CostCurrency = row.CostCurrency, RequiresAttunement = row.RequiresAttunement,
        SourceType = row.SourceType, Source = row.Source, Tags = row.Tags, WeaponCategory = row.WeaponCategory, DamageDice = row.DamageDice, DamageType = row.DamageType,
        Properties = row.Properties, RangeNormal = row.RangeNormal, RangeMax = row.RangeMax, IsMagicWeapon = row.IsMagicWeapon,
        AttackBonus = row.AttackBonus, DamageBonus = row.DamageBonus, ArmorCategory = row.ArmorCategory, ArmorClassBase = row.ArmorClassBase,
        DexCap = row.DexCap, StrengthRequirement = row.StrengthRequirement, StealthDisadvantage = row.StealthDisadvantage,
        IsMagicArmor = row.IsMagicArmor, ArmorBonus = row.ArmorBonus, Charges = row.Charges, MaxCharges = row.MaxCharges,
        RechargeRule = row.RechargeRule, ConsumableEffect = row.ConsumableEffect, Quantity = row.Quantity, Stackable = row.Stackable, Notes = row.Notes
    });
}).RequireAuthorization();

app.MapPost("/api/items", async (UpsertItemRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http);
    if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new ApiError("ITEM_NAME_REQUIRED", "Item name is required.", http.TraceIdentifier));
    if (req.SourceType == 1 && !IsAdmin(http)) return Results.Forbid();
    var normalizedName = TitleNormalization.ToPascalTitle(req.Name);
    var normalizedSource = string.IsNullOrWhiteSpace(req.Source) ? null : req.Source.Trim();
    var duplicate = await db.Items.AnyAsync(x => x.DateDeletedUtc == null && x.OwnerAppUserId == userId.Value && x.Name == normalizedName && x.SourceType == req.SourceType && x.Source == normalizedSource);
    if (duplicate) return Results.BadRequest(new ApiError("ITEM_DUPLICATE", "Item already exists for this source.", http.TraceIdentifier));
    var row = new Item();
    ApplyItem(req, row);
    row.IsSystem = req.SourceType == 1 || row.IsSystem;
    row.IsSystem = req.SourceType == 1;
    row.OwnerAppUserId = req.SourceType == 1 ? userId.Value : userId.Value;
    row.DateCreatedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    db.Items.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(new { row.ItemId });
}).RequireAuthorization();

app.MapPut("/api/items/{id:int}", async (int id, UpsertItemRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http);
    if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new ApiError("ITEM_NAME_REQUIRED", "Item name is required.", http.TraceIdentifier));
    if (req.SourceType == 1 && !IsAdmin(http)) return Results.Forbid();
    var normalizedName = TitleNormalization.ToPascalTitle(req.Name);
    var normalizedSource = string.IsNullOrWhiteSpace(req.Source) ? null : req.Source.Trim();
    var duplicate = await db.Items.AnyAsync(x => x.DateDeletedUtc == null && x.ItemId != id && x.OwnerAppUserId == userId.Value && x.Name == normalizedName && x.SourceType == req.SourceType && x.Source == normalizedSource);
    if (duplicate) return Results.BadRequest(new ApiError("ITEM_DUPLICATE", "Item already exists for this source.", http.TraceIdentifier));
    var row = await db.Items.FirstOrDefaultAsync(x => x.ItemId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    var isOwner = row.OwnerAppUserId == userId.Value;
    var canEdit = isOwner || await db.ItemShares.AnyAsync(x => x.ItemId == id && x.SharedWithUserId == userId.Value && x.Permission == SharePermission.Edit);
    if (!canEdit && !IsAdmin(http)) return Results.Forbid();
    if (row.IsSystem && !IsAdmin(http)) return Results.Forbid();
    if (req.SourceType == 1 && !IsAdmin(http)) return Results.Forbid();
    var requestedOwnerId = req.OwnerAppUserId ?? row.OwnerAppUserId;
    if (requestedOwnerId is null) return Results.BadRequest(new ApiError("ITEM_OWNER_REQUIRED", "Item owner is required.", http.TraceIdentifier));
    var ownerChanged = requestedOwnerId != row.OwnerAppUserId;
    if (ownerChanged && row.OwnerAppUserId != userId.Value && !IsAdmin(http)) return Results.Forbid();
    var ownerExists = await db.Users.AnyAsync(x => x.AppUserId == requestedOwnerId.Value && x.DateDeletedUtc == null);
    if (!ownerExists) return Results.BadRequest(new ApiError("ITEM_OWNER_INVALID", "Selected owner was not found.", http.TraceIdentifier));

    ApplyItem(req, row);
    row.OwnerAppUserId = requestedOwnerId.Value;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { row.ItemId });
}).RequireAuthorization();

app.MapDelete("/api/items/{id:int}", async (int id, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http);
    if (userId is null) return Results.Unauthorized();
    var row = await db.Items.FirstOrDefaultAsync(x => x.ItemId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != userId.Value && !IsAdmin(http)) return Results.Forbid();
    if (row.IsSystem && !IsAdmin(http)) return Results.Forbid();
    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();



app.MapPost("/api/items/{id:int}/clone", async (int id, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var row = await db.Items.FirstOrDefaultAsync(x => x.ItemId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    var clone = new Item();
    clone.Name = row.Name;
    clone.Description = row.Description;
    clone.ItemType = row.ItemType;
    clone.Rarity = row.Rarity;
    clone.Weight = row.Weight;
    clone.CostAmount = row.CostAmount;
    clone.CostCurrency = row.CostCurrency;
    clone.RequiresAttunement = row.RequiresAttunement;
    clone.SourceType = row.SourceType;
    clone.Source = row.Source;
    clone.Tags = row.Tags;
    clone.WeaponCategory = row.WeaponCategory;
    clone.DamageDice = row.DamageDice;
    clone.DamageType = row.DamageType;
    clone.Properties = row.Properties;
    clone.RangeNormal = row.RangeNormal;
    clone.RangeMax = row.RangeMax;
    clone.IsMagicWeapon = row.IsMagicWeapon;
    clone.AttackBonus = row.AttackBonus;
    clone.DamageBonus = row.DamageBonus;
    clone.ArmorCategory = row.ArmorCategory;
    clone.ArmorClassBase = row.ArmorClassBase;
    clone.DexCap = row.DexCap;
    clone.StrengthRequirement = row.StrengthRequirement;
    clone.StealthDisadvantage = row.StealthDisadvantage;
    clone.IsMagicArmor = row.IsMagicArmor;
    clone.ArmorBonus = row.ArmorBonus;
    clone.Charges = row.Charges;
    clone.MaxCharges = row.MaxCharges;
    clone.RechargeRule = row.RechargeRule;
    clone.ConsumableEffect = row.ConsumableEffect;
    clone.Quantity = row.Quantity;
    clone.Stackable = row.Stackable;
    clone.Notes = row.Notes;
    clone.IsSystem = false;
    clone.OwnerAppUserId = userId.Value;
    clone.DateCreatedUtc = DateTime.UtcNow;
    clone.DateModifiedUtc = DateTime.UtcNow;
    db.Items.Add(clone);
    await db.SaveChangesAsync();
    return Results.Ok(new { clone.ItemId });
}).RequireAuthorization();





app.MapPost("/api/items/{id:int}/share", async (int id, ShareRecordRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http);
    if (userId is null) return Results.Unauthorized();
    var row = await db.Items.FirstOrDefaultAsync(x => x.ItemId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != userId.Value && !IsAdmin(http)) return Results.Forbid();

    var exists = await db.Users.AnyAsync(x => x.AppUserId == req.UserId && x.DateDeletedUtc == null);
    if (!exists) return Results.NotFound();
    if (req.Permission is < 0 or > 1) return Results.BadRequest("Invalid permission.");

    var link = await db.ItemShares.FirstOrDefaultAsync(x => x.ItemId == id && x.SharedWithUserId == req.UserId);
    if (link is null)
        db.ItemShares.Add(new ItemShare { ItemId = id, SharedWithUserId = req.UserId, Permission = (SharePermission)req.Permission, DateCreatedUtc = DateTime.UtcNow });
    else
        link.Permission = (SharePermission)req.Permission;

    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

app.MapDelete("/api/items/{id:int}/share/{userId:int}", async (int id, int userId, HttpContext http, AppDbContext db) =>
{
    var me = GetUserId(http);
    if (me is null) return Results.Unauthorized();
    var row = await db.Items.FirstOrDefaultAsync(x => x.ItemId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != me.Value && !IsAdmin(http)) return Results.Forbid();

    var link = await db.ItemShares.FirstOrDefaultAsync(x => x.ItemId == id && x.SharedWithUserId == userId);
    if (link is null) return Results.NotFound();
    db.ItemShares.Remove(link);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/characters", async (HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http);
    if (userId is null) return Results.Unauthorized();
    var includeAll = IsAdmin(http) && showAll == true;
    var sharedIds = includeAll ? new List<int>() : await db.CharacterShares.Where(x => x.SharedWithUserId == userId.Value).Select(x => x.CharacterId).ToListAsync();

    var rows = await db.Characters
        .Where(x => x.DateDeletedUtc == null && (includeAll || x.OwnerAppUserId == userId.Value || sharedIds.Contains(x.CharacterId)))
        .OrderBy(x => x.Name)
        .Select(x => new CharacterResponse
        {
            CharacterId = x.CharacterId,
            CampaignId = x.CampaignId == 0 ? null : x.CampaignId,
            PartyId = x.PartyId == 0 ? null : x.PartyId,
            CharacterType = x.CharacterType,
            Name = x.Name,
            OwnerAppUserId = x.OwnerAppUserId,
            OwnerUsername = db.Users.Where(u => u.AppUserId == x.OwnerAppUserId).Select(u => u.Username).FirstOrDefault(),
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
            SubclassName = x.SubclassName,
            RaceName = x.RaceName,
            SubraceName = x.SubraceName,
            PassivePerception = x.PassivePerception,
            Conditions = x.Conditions,
            Notes = x.Notes,
            DateCreatedUtc = x.DateCreatedUtc,
            DateModifiedUtc = x.DateModifiedUtc
        })
        .ToListAsync();

    return Results.Ok(rows);
}).RequireAuthorization();

app.MapGet("/api/characters/{id:int}", async (int id, HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http);
    if (userId is null) return Results.Unauthorized();
    var isAdmin = IsAdmin(http);
    var includeAll = isAdmin && showAll == true;

    var row = await db.Characters
        .Include(x => x.Skills)
        .ThenInclude(x => x.Skill)
        .FirstOrDefaultAsync(x => x.CharacterId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    var canView = isAdmin || includeAll || row.OwnerAppUserId == userId.Value || await db.CharacterShares.AnyAsync(x => x.CharacterId == id && x.SharedWithUserId == userId.Value);
    if (!canView) return Results.Forbid();

    var ownerUsername = await db.Users.Where(u => u.AppUserId == row.OwnerAppUserId).Select(u => u.Username).FirstOrDefaultAsync();
    return Results.Ok(ToCharacterResponse(row, ownerUsername));
}).RequireAuthorization();

app.MapPost("/api/characters", async (UpsertCharacterRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http);
    if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new ApiError("CHARACTER_NAME_REQUIRED", "Name is required.", http.TraceIdentifier));

    var requestedOwnerId = req.OwnerAppUserId ?? userId.Value;
    if (requestedOwnerId != userId.Value && !IsAdmin(http)) return Results.Forbid();
    var ownerExists = await db.Users.AnyAsync(x => x.AppUserId == requestedOwnerId && x.DateDeletedUtc == null);
    if (!ownerExists) return Results.BadRequest(new ApiError("CHARACTER_OWNER_INVALID", "Selected owner was not found.", http.TraceIdentifier));

    var normalizedName = TitleNormalization.ToPascalTitle(req.Name);
    var duplicate = await db.Characters.AnyAsync(x => x.DateDeletedUtc == null && x.OwnerAppUserId == requestedOwnerId && x.CampaignId == (req.CampaignId ?? 0) && x.Name == normalizedName);
    if (duplicate) return Results.BadRequest(new ApiError("CHARACTER_DUPLICATE", "Character name already exists in this campaign.", http.TraceIdentifier));

    var row = new Character
    {
        CampaignId = req.CampaignId ?? 0,
        PartyId = req.PartyId ?? 0,
        CharacterType = req.CharacterType,
        Name = normalizedName,
        OwnerAppUserId = requestedOwnerId,
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
        SubclassName = req.SubclassName,
        RaceName = req.RaceName,
        SubraceName = req.SubraceName,
        PassivePerception = req.PassivePerception,
        Conditions = req.Conditions,
        Notes = req.Notes,
        DateCreatedUtc = DateTime.UtcNow,
        DateModifiedUtc = DateTime.UtcNow,
        Skills = BuildCharacterSkills(req.Skills)
    };

    db.Characters.Add(row);
    await db.SaveChangesAsync();
    await EnsureCharacterSkillRowsForCharacterAsync(db, row.CharacterId);
    return Results.Ok(new { row.CharacterId });
}).RequireAuthorization();

app.MapPut("/api/characters/{id:int}", async (int id, UpsertCharacterRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http);
    if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new ApiError("CHARACTER_NAME_REQUIRED", "Name is required.", http.TraceIdentifier));

    var row = await db.Characters
        .Include(x => x.Skills)
        .FirstOrDefaultAsync(x => x.CharacterId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    var canEdit = row.OwnerAppUserId == userId.Value || await db.CharacterShares.AnyAsync(x => x.CharacterId == id && x.SharedWithUserId == userId.Value && x.Permission == SharePermission.Edit);
    if (!canEdit && !IsAdmin(http)) return Results.Forbid();

    var requestedOwnerId = req.OwnerAppUserId ?? row.OwnerAppUserId;
    if (requestedOwnerId is null) return Results.BadRequest(new ApiError("CHARACTER_OWNER_REQUIRED", "Character owner is required.", http.TraceIdentifier));
    var ownerChanged = requestedOwnerId != row.OwnerAppUserId;
    if (ownerChanged && row.OwnerAppUserId != userId.Value && !IsAdmin(http)) return Results.Forbid();
    var ownerExists = await db.Users.AnyAsync(x => x.AppUserId == requestedOwnerId.Value && x.DateDeletedUtc == null);
    if (!ownerExists) return Results.BadRequest(new ApiError("CHARACTER_OWNER_INVALID", "Selected owner was not found.", http.TraceIdentifier));

    var normalizedName = TitleNormalization.ToPascalTitle(req.Name);
    var duplicate = await db.Characters.AnyAsync(x => x.DateDeletedUtc == null && x.CharacterId != id && x.OwnerAppUserId == requestedOwnerId.Value && x.CampaignId == (req.CampaignId ?? 0) && x.Name == normalizedName);
    if (duplicate) return Results.BadRequest(new ApiError("CHARACTER_DUPLICATE", "Character name already exists in this campaign.", http.TraceIdentifier));

    row.CampaignId = req.CampaignId ?? 0;
    row.PartyId = req.PartyId ?? 0;
    row.CharacterType = req.CharacterType;
    row.Name = normalizedName;
    row.OwnerAppUserId = requestedOwnerId.Value;
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
    row.SubclassName = req.SubclassName;
    row.RaceName = req.RaceName;
    row.SubraceName = req.SubraceName;
    row.PassivePerception = req.PassivePerception;
    row.Conditions = req.Conditions;
    row.Notes = req.Notes;
    row.DateModifiedUtc = DateTime.UtcNow;

    SyncCharacterSkills(row, req.Skills);

    await db.SaveChangesAsync();
    await EnsureCharacterSkillRowsForCharacterAsync(db, row.CharacterId);
    return Results.Ok(new { row.CharacterId });
}).RequireAuthorization();

app.MapDelete("/api/characters/{id:int}", async (int id, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http);
    if (userId is null) return Results.Unauthorized();
    var row = await db.Characters.FirstOrDefaultAsync(x => x.CharacterId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != userId.Value && !IsAdmin(http)) return Results.Forbid();

    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/characters/{id:int}/share", async (int id, ShareRecordRequest req, HttpContext http, AppDbContext db) =>
{
    var me = GetUserId(http);
    if (me is null) return Results.Unauthorized();
    var row = await db.Characters.FirstOrDefaultAsync(x => x.CharacterId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != me.Value && !IsAdmin(http)) return Results.Forbid();

    var exists = await db.Users.AnyAsync(x => x.AppUserId == req.UserId && x.DateDeletedUtc == null);
    if (!exists) return Results.NotFound();
    if (req.Permission is < 0 or > 1) return Results.BadRequest("Invalid permission.");

    var link = await db.CharacterShares.FirstOrDefaultAsync(x => x.CharacterId == id && x.SharedWithUserId == req.UserId);
    if (link is null)
        db.CharacterShares.Add(new CharacterShare { CharacterId = id, SharedWithUserId = req.UserId, Permission = (SharePermission)req.Permission, DateCreatedUtc = DateTime.UtcNow });
    else
        link.Permission = (SharePermission)req.Permission;

    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

app.MapDelete("/api/characters/{id:int}/share/{userId:int}", async (int id, int userId, HttpContext http, AppDbContext db) =>
{
    var me = GetUserId(http);
    if (me is null) return Results.Unauthorized();
    var row = await db.Characters.FirstOrDefaultAsync(x => x.CharacterId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != me.Value && !IsAdmin(http)) return Results.Forbid();

    var link = await db.CharacterShares.FirstOrDefaultAsync(x => x.CharacterId == id && x.SharedWithUserId == userId);
    if (link is null) return Results.NotFound();
    db.CharacterShares.Remove(link);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/campaigns", async (HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var includeAll = IsAdmin(http) && showAll == true;
    var sharedIds = includeAll ? new List<int>() : await db.CampaignShares.Where(x => x.SharedWithUserId == userId.Value).Select(x => x.CampaignId).ToListAsync();
    var rows = await db.Campaigns.Where(x => x.DateDeletedUtc == null && (includeAll || x.OwnerAppUserId == userId.Value || sharedIds.Contains(x.CampaignId))).OrderBy(x => x.Name)
        .Select(x => new CampaignResponse { CampaignId = x.CampaignId, OwnerAppUserId = x.OwnerAppUserId, OwnerUsername = db.Users.Where(u => u.AppUserId == x.OwnerAppUserId).Select(u => u.Username).FirstOrDefault(), Name = x.Name, Description = x.Description, QuestCount = db.Quests.Count(q => q.DateDeletedUtc == null && q.CampaignId == x.CampaignId), EncounterCount = db.Encounters.Count(e => e.DateDeletedUtc == null && e.CampaignId == x.CampaignId) }).ToListAsync();
    return Results.Ok(rows);
}).RequireAuthorization();

app.MapGet("/api/campaigns/{id:int}", async (int id, HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var isAdmin = IsAdmin(http);
    var includeAll = isAdmin && showAll == true;
    var row = await db.Campaigns.FirstOrDefaultAsync(x => x.CampaignId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    var canView = isAdmin || includeAll || row.OwnerAppUserId == userId.Value || await db.CampaignShares.AnyAsync(x => x.CampaignId == id && x.SharedWithUserId == userId.Value);
    if (!canView) return Results.Forbid();
    var questCount = await db.Quests.CountAsync(q => q.DateDeletedUtc == null && q.CampaignId == row.CampaignId);
    var encounterCount = await db.Encounters.CountAsync(e => e.DateDeletedUtc == null && e.CampaignId == row.CampaignId);
    return Results.Ok(new CampaignResponse { CampaignId = row.CampaignId, OwnerAppUserId = row.OwnerAppUserId, OwnerUsername = await db.Users.Where(u => u.AppUserId == row.OwnerAppUserId).Select(u => u.Username).FirstOrDefaultAsync(), Name = row.Name, Description = row.Description, QuestCount = questCount, EncounterCount = encounterCount });
}).RequireAuthorization();

app.MapPost("/api/campaigns", async (UpsertCampaignRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new ApiError("CAMPAIGN_NAME_REQUIRED", "Title is required.", http.TraceIdentifier));
    var normalizedName = TitleNormalization.ToPascalTitle(req.Name);
    var exists = await db.Campaigns.AnyAsync(x => x.DateDeletedUtc == null && x.Name == normalizedName);
    if (exists) return Results.BadRequest(new ApiError("CAMPAIGN_DUPLICATE_NAME", "Campaign name already exists.", http.TraceIdentifier));

    var row = new Campaign { OwnerAppUserId = userId.Value, Name = normalizedName, Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(), DateCreatedUtc = DateTime.UtcNow, DateModifiedUtc = DateTime.UtcNow };
    db.Campaigns.Add(row);
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException ex) when ((ex.InnerException?.Message ?? ex.Message).Contains("IX_Campaigns_Name", StringComparison.OrdinalIgnoreCase) || (ex.InnerException?.Message ?? ex.Message).Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new ApiError("CAMPAIGN_DUPLICATE_NAME", "Campaign name already exists.", http.TraceIdentifier));
    }
    return Results.Ok(new { row.CampaignId });
}).RequireAuthorization();

app.MapPut("/api/campaigns/{id:int}", async (int id, UpsertCampaignRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new ApiError("CAMPAIGN_NAME_REQUIRED", "Title is required.", http.TraceIdentifier));
    var row = await db.Campaigns.FirstOrDefaultAsync(x => x.CampaignId == id && x.DateDeletedUtc == null); if (row is null) return Results.NotFound();
    var canEdit = row.OwnerAppUserId == userId.Value || await db.CampaignShares.AnyAsync(x => x.CampaignId == id && x.SharedWithUserId == userId.Value && x.Permission == SharePermission.Edit);
    if (!canEdit && !IsAdmin(http)) return Results.Forbid();
    var normalizedName = TitleNormalization.ToPascalTitle(req.Name);
    var exists = await db.Campaigns.AnyAsync(x => x.DateDeletedUtc == null && x.CampaignId != id && x.Name == normalizedName);
    if (exists) return Results.BadRequest(new ApiError("CAMPAIGN_DUPLICATE_NAME", "Campaign name already exists.", http.TraceIdentifier));

    var requestedOwnerId = req.OwnerAppUserId ?? row.OwnerAppUserId;
    if (requestedOwnerId is null) return Results.BadRequest(new ApiError("CAMPAIGN_OWNER_REQUIRED", "Campaign owner is required.", http.TraceIdentifier));
    var ownerChanged = requestedOwnerId != row.OwnerAppUserId;
    if (ownerChanged && row.OwnerAppUserId != userId.Value && !IsAdmin(http))
        return Results.Forbid();
    var ownerExists = await db.Users.AnyAsync(x => x.AppUserId == requestedOwnerId.Value && x.DateDeletedUtc == null);
    if (!ownerExists) return Results.BadRequest(new ApiError("CAMPAIGN_OWNER_INVALID", "Selected owner was not found.", http.TraceIdentifier));

    row.Name = normalizedName;
    row.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
    row.OwnerAppUserId = requestedOwnerId.Value;
    row.DateModifiedUtc = DateTime.UtcNow;
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException ex) when ((ex.InnerException?.Message ?? ex.Message).Contains("IX_Campaigns_Name", StringComparison.OrdinalIgnoreCase) || (ex.InnerException?.Message ?? ex.Message).Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new ApiError("CAMPAIGN_DUPLICATE_NAME", "Campaign name already exists.", http.TraceIdentifier));
    }
    return Results.Ok(new { row.CampaignId });
}).RequireAuthorization();

app.MapDelete("/api/campaigns/{id:int}", async (int id, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var row = await db.Campaigns.FirstOrDefaultAsync(x => x.CampaignId == id && x.DateDeletedUtc == null); if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != userId.Value && !IsAdmin(http)) return Results.Forbid();
    row.DateDeletedUtc = DateTime.UtcNow; row.DateModifiedUtc = DateTime.UtcNow; await db.SaveChangesAsync(); return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/campaigns/{id:int}/share", async (int id, ShareRecordRequest req, HttpContext http, AppDbContext db) =>
{
    var me = GetUserId(http); if (me is null) return Results.Unauthorized();
    var row = await db.Campaigns.FirstOrDefaultAsync(x => x.CampaignId == id && x.DateDeletedUtc == null); if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != me.Value && !IsAdmin(http)) return Results.Forbid();
    var link = await db.CampaignShares.FirstOrDefaultAsync(x => x.CampaignId == id && x.SharedWithUserId == req.UserId);
    if (link is null) db.CampaignShares.Add(new CampaignShare { CampaignId = id, SharedWithUserId = req.UserId, Permission = (SharePermission)req.Permission });
    else link.Permission = (SharePermission)req.Permission;
    await db.SaveChangesAsync(); return Results.Ok();
}).RequireAuthorization();

app.MapDelete("/api/campaigns/{id:int}/share/{userId:int}", async (int id, int userId, HttpContext http, AppDbContext db) =>
{
    var me = GetUserId(http); if (me is null) return Results.Unauthorized();
    var row = await db.Campaigns.FirstOrDefaultAsync(x => x.CampaignId == id && x.DateDeletedUtc == null); if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != me.Value && !IsAdmin(http)) return Results.Forbid();
    var link = await db.CampaignShares.FirstOrDefaultAsync(x => x.CampaignId == id && x.SharedWithUserId == userId); if (link is null) return Results.NotFound();
    db.CampaignShares.Remove(link); await db.SaveChangesAsync(); return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/creature-taxonomy", async (AppDbContext db) =>
{
    var types = await db.CreatureTypes
        .Where(x => x.IsActive)
        .OrderBy(x => x.DisplayOrder)
        .ThenBy(x => x.Name)
        .Select(x => new
        {
            creatureTypeId = x.CreatureTypeId,
            key = x.Key,
            name = x.Name,
            subtypes = x.Subtypes.Where(s => s.IsActive).OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
                .Select(s => new { creatureSubtypeId = s.CreatureSubtypeId, key = s.Key, name = s.Name })
                .ToList()
        })
        .ToListAsync();

    var untypedSubtypes = await db.CreatureSubtypes
        .Where(x => x.IsActive && x.CreatureTypeId == null)
        .OrderBy(x => x.DisplayOrder)
        .ThenBy(x => x.Name)
        .Select(x => new { creatureSubtypeId = x.CreatureSubtypeId, key = x.Key, name = x.Name })
        .ToListAsync();

    return Results.Ok(new { types, untypedSubtypes });
});

app.MapGet("/api/creatures", async (HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http);
    var isAdmin = IsAdmin(http);
    var includeAll = isAdmin && showAll == true;
    var sharedPermissions = userId.HasValue && !includeAll
        ? await db.CreatureShares
            .Where(x => x.SharedWithUserId == userId.Value)
            .ToDictionaryAsync(x => x.CreatureId, x => x.Permission)
        : new Dictionary<int, SharePermission>();
    var sharedIds = sharedPermissions.Keys.ToList();

    var rows = await db.Creatures
        .Include(x => x.Type)
        .Include(x => x.CreatureSubtypeLinks).ThenInclude(x => x.CreatureSubtype)
        .Include(x => x.TraitList)
        .Include(x => x.ActionList)
        .Where(x => x.DateDeletedUtc == null && (includeAll || x.IsSystem || (userId.HasValue && (x.OwnerAppUserId == userId.Value || sharedIds.Contains(x.CreatureId)))))
        .OrderBy(x => x.Name)
        .ToListAsync();

    var ownerIds = rows.Where(x => x.OwnerAppUserId.HasValue).Select(x => x.OwnerAppUserId!.Value).Distinct().ToList();
    var ownerMap = await db.Users
        .Where(u => ownerIds.Contains(u.AppUserId))
        .Select(u => new { u.AppUserId, u.Username, OwnerIsAdmin = u.Role == AppRole.Admin })
        .ToDictionaryAsync(u => u.AppUserId, u => new { u.Username, u.OwnerIsAdmin });

    var result = rows.Select(row =>
    {
        var hasOwner = ownerMap.TryGetValue(row.OwnerAppUserId ?? 0, out var owner);
        var canEdit = includeAll || isAdmin || (userId.HasValue && !row.IsSystem && row.OwnerAppUserId == userId.Value) || (sharedPermissions.TryGetValue(row.CreatureId, out var permission) && permission == SharePermission.Edit);
        return ToCreatureResponse(row, hasOwner ? owner!.Username : null, hasOwner && owner!.OwnerIsAdmin, canEdit);
    }).ToList();

    return Results.Ok(result);
});

app.MapGet("/api/creatures/{id:int}", async (int id, HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http);
    var isAdmin = IsAdmin(http);
    var includeAll = isAdmin && showAll == true;

    var row = await db.Creatures
        .Include(x => x.Type)
        .Include(x => x.CreatureSubtypeLinks).ThenInclude(x => x.CreatureSubtype)
        .Include(x => x.TraitList)
        .Include(x => x.ActionList)
        .FirstOrDefaultAsync(x => x.CreatureId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    SharePermission? sharePermission = null;
    if (includeAll || isAdmin)
    {
        sharePermission = SharePermission.Edit;
    }
    else if (userId.HasValue)
    {
        sharePermission = await db.CreatureShares
            .Where(x => x.CreatureId == id && x.SharedWithUserId == userId.Value)
            .Select(x => (SharePermission?)x.Permission)
            .FirstOrDefaultAsync();
    }

    var canView = isAdmin || includeAll || row.IsSystem || (userId.HasValue && row.OwnerAppUserId == userId.Value) || sharePermission.HasValue;
    if (!canView) return Results.Forbid();

    var owner = row.OwnerAppUserId.HasValue
        ? await db.Users.Where(u => u.AppUserId == row.OwnerAppUserId.Value).Select(u => new { u.Username, OwnerIsAdmin = u.Role == AppRole.Admin }).FirstOrDefaultAsync()
        : null;

    return Results.Ok(ToCreatureResponse(
        row,
        owner?.Username,
        owner?.OwnerIsAdmin == true,
        isAdmin || includeAll || (userId.HasValue && !row.IsSystem && row.OwnerAppUserId == userId.Value) || sharePermission == SharePermission.Edit));
});

app.MapPost("/api/creatures", async (UpsertCreatureRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new ApiError("CREATURE_NAME_REQUIRED", "Creature name is required.", http.TraceIdentifier));

    var row = new Creature
    {
        Name = NormalizeCreatureTitle(req.Name),
        Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
        Size = NormalizeCreatureSize(req.Size),
        IsSystem = req.IsSystem && IsAdmin(http),
        OwnerAppUserId = userId.Value,
        ArmorClass = req.ArmorClass,
        ArmorClassNotes = NormalizeCreatureArmorClassNotes(req.ArmorClassNotes),
        HitPoints = req.HitPoints,
        HitDice = NormalizeCreatureHitDice(req.HitDice),
        InitiativeModifier = req.InitiativeModifier,
        WalkSpeed = req.WalkSpeed,
        FlySpeed = req.FlySpeed,
        SwimSpeed = req.SwimSpeed,
        ClimbSpeed = req.ClimbSpeed,
        BurrowSpeed = req.BurrowSpeed,
        Speed = FormatCreatureSpeed(req.WalkSpeed, req.FlySpeed, req.SwimSpeed, req.ClimbSpeed, req.BurrowSpeed, req.Speed),
        ChallengeRating = req.ChallengeRating,
        ExperiencePoints = req.ExperiencePoints,
        PassivePerception = req.PassivePerception,
        BlindsightRange = req.BlindsightRange,
        DarkvisionRange = req.DarkvisionRange,
        TremorsenseRange = req.TremorsenseRange,
        TruesightRange = req.TruesightRange,
        OtherSenses = NormalizeCreatureOtherSenses(req.OtherSenses),
        Languages = NormalizeCreatureLanguages(req.Languages),
        UnderstandsButCannotSpeak = req.UnderstandsButCannotSpeak,
        Traits = null,
        Actions = null,
        Strength = req.Strength,
        Dexterity = req.Dexterity,
        Constitution = req.Constitution,
        Intelligence = req.Intelligence,
        Wisdom = req.Wisdom,
        Charisma = req.Charisma,
        DateCreatedUtc = DateTime.UtcNow,
        DateModifiedUtc = DateTime.UtcNow
    };

    ApplyCreatureEntries(row, req);
    await ResolveCreatureTaxonomyAsync(db, row, req);

    db.Creatures.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(new { row.CreatureId });
}).RequireAuthorization();

app.MapPut("/api/creatures/{id:int}", async (int id, UpsertCreatureRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new ApiError("CREATURE_NAME_REQUIRED", "Creature name is required.", http.TraceIdentifier));

    var row = await db.Creatures
        .Include(x => x.Type)
        .Include(x => x.CreatureSubtypeLinks).ThenInclude(x => x.CreatureSubtype)
        .Include(x => x.TraitList)
        .Include(x => x.ActionList)
        .FirstOrDefaultAsync(x => x.CreatureId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var canEdit = IsAdmin(http) || row.OwnerAppUserId == userId.Value || await db.CreatureShares.AnyAsync(x => x.CreatureId == id && x.SharedWithUserId == userId.Value && x.Permission == SharePermission.Edit);
    if (!canEdit) return Results.Forbid();
    if (row.IsSystem && !IsAdmin(http)) return Results.Forbid();

    var requestedOwnerId = req.OwnerAppUserId ?? row.OwnerAppUserId;
    if (requestedOwnerId is null) return Results.BadRequest(new ApiError("CREATURE_OWNER_REQUIRED", "Creature owner is required.", http.TraceIdentifier));
    var ownerChanged = requestedOwnerId != row.OwnerAppUserId;
    if (ownerChanged && row.OwnerAppUserId != userId.Value && !IsAdmin(http)) return Results.Forbid();
    var ownerExists = await db.Users.AnyAsync(x => x.AppUserId == requestedOwnerId.Value && x.DateDeletedUtc == null);
    if (!ownerExists) return Results.BadRequest(new ApiError("CREATURE_OWNER_INVALID", "Selected owner was not found.", http.TraceIdentifier));

    row.Name = NormalizeCreatureTitle(req.Name);
    row.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
    row.Size = NormalizeCreatureSize(req.Size);
    row.IsSystem = req.IsSystem && IsAdmin(http);
    row.OwnerAppUserId = requestedOwnerId.Value;
    row.ArmorClass = req.ArmorClass;
    row.ArmorClassNotes = NormalizeCreatureArmorClassNotes(req.ArmorClassNotes);
    row.HitPoints = req.HitPoints;
    row.HitDice = NormalizeCreatureHitDice(req.HitDice);
    row.InitiativeModifier = req.InitiativeModifier;
    row.WalkSpeed = req.WalkSpeed;
    row.FlySpeed = req.FlySpeed;
    row.SwimSpeed = req.SwimSpeed;
    row.ClimbSpeed = req.ClimbSpeed;
    row.BurrowSpeed = req.BurrowSpeed;
    row.Speed = FormatCreatureSpeed(req.WalkSpeed, req.FlySpeed, req.SwimSpeed, req.ClimbSpeed, req.BurrowSpeed, req.Speed);
    row.ChallengeRating = req.ChallengeRating;
    row.ExperiencePoints = req.ExperiencePoints;
    row.PassivePerception = req.PassivePerception;
    row.BlindsightRange = req.BlindsightRange;
    row.DarkvisionRange = req.DarkvisionRange;
    row.TremorsenseRange = req.TremorsenseRange;
    row.TruesightRange = req.TruesightRange;
    row.OtherSenses = NormalizeCreatureOtherSenses(req.OtherSenses);
    row.Languages = NormalizeCreatureLanguages(req.Languages);
    row.UnderstandsButCannotSpeak = req.UnderstandsButCannotSpeak;
    row.Traits = null;
    row.Actions = null;
    row.Strength = req.Strength;
    row.Dexterity = req.Dexterity;
    row.Constitution = req.Constitution;
    row.Intelligence = req.Intelligence;
    row.Wisdom = req.Wisdom;
    row.Charisma = req.Charisma;
    row.DateModifiedUtc = DateTime.UtcNow;

    ApplyCreatureEntries(row, req);
    await ResolveCreatureTaxonomyAsync(db, row, req);

    await db.SaveChangesAsync();
    return Results.Ok(new { row.CreatureId });
}).RequireAuthorization();

app.MapDelete("/api/creatures/{id:int}", async (int id, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var row = await db.Creatures.FirstOrDefaultAsync(x => x.CreatureId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var canDelete = IsAdmin(http) || row.OwnerAppUserId == userId.Value;
    if (!canDelete) return Results.Forbid();
    if (row.IsSystem && !IsAdmin(http)) return Results.Forbid();

    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/creatures/{id:int}/share", async (int id, ShareRecordRequest req, HttpContext http, AppDbContext db) =>
{
    var me = GetUserId(http); if (me is null) return Results.Unauthorized();
    var row = await db.Creatures.FirstOrDefaultAsync(x => x.CreatureId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != me.Value && !IsAdmin(http)) return Results.Forbid();

    var exists = await db.Users.AnyAsync(x => x.AppUserId == req.UserId && x.DateDeletedUtc == null);
    if (!exists) return Results.NotFound();
    if (req.Permission is < 0 or > 1) return Results.BadRequest("Invalid permission.");

    var link = await db.CreatureShares.FirstOrDefaultAsync(x => x.CreatureId == id && x.SharedWithUserId == req.UserId);
    if (link is null) db.CreatureShares.Add(new CreatureShare { CreatureId = id, SharedWithUserId = req.UserId, Permission = (SharePermission)req.Permission, DateCreatedUtc = DateTime.UtcNow });
    else link.Permission = (SharePermission)req.Permission;

    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

app.MapDelete("/api/creatures/{id:int}/share/{userId:int}", async (int id, int userId, HttpContext http, AppDbContext db) =>
{
    var me = GetUserId(http); if (me is null) return Results.Unauthorized();
    var row = await db.Creatures.FirstOrDefaultAsync(x => x.CreatureId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != me.Value && !IsAdmin(http)) return Results.Forbid();

    var link = await db.CreatureShares.FirstOrDefaultAsync(x => x.CreatureId == id && x.SharedWithUserId == userId);
    if (link is null) return Results.NotFound();
    db.CreatureShares.Remove(link);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/creatures/{id:int}/clone", async (int id, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var row = await db.Creatures
        .Include(x => x.Type)
        .Include(x => x.CreatureSubtypeLinks).ThenInclude(x => x.CreatureSubtype)
        .Include(x => x.TraitList)
        .Include(x => x.ActionList)
        .FirstOrDefaultAsync(x => x.CreatureId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var canView = row.IsSystem || row.OwnerAppUserId == userId.Value || IsAdmin(http) || await db.CreatureShares.AnyAsync(x => x.CreatureId == id && x.SharedWithUserId == userId.Value);
    if (!canView) return Results.Forbid();

    var clone = new Creature
    {
        Name = row.Name,
        Description = row.Description,
        Size = row.Size,
        CreatureType = row.CreatureType,
        CreatureSubtype = row.CreatureSubtype,
        CreatureTypeId = row.CreatureTypeId,
        IsSystem = false,
        OwnerAppUserId = userId.Value,
        ArmorClass = row.ArmorClass,
        ArmorClassNotes = row.ArmorClassNotes,
        HitPoints = row.HitPoints,
        HitDice = row.HitDice,
        InitiativeModifier = row.InitiativeModifier,
        WalkSpeed = row.WalkSpeed,
        FlySpeed = row.FlySpeed,
        SwimSpeed = row.SwimSpeed,
        ClimbSpeed = row.ClimbSpeed,
        BurrowSpeed = row.BurrowSpeed,
        Speed = row.Speed,
        ChallengeRating = row.ChallengeRating,
        ExperiencePoints = row.ExperiencePoints,
        PassivePerception = row.PassivePerception,
        Languages = row.Languages,
        UnderstandsButCannotSpeak = row.UnderstandsButCannotSpeak,
        Traits = null,
        Actions = null,
        Strength = row.Strength,
        Dexterity = row.Dexterity,
        Constitution = row.Constitution,
        Intelligence = row.Intelligence,
        Wisdom = row.Wisdom,
        Charisma = row.Charisma,
        DateCreatedUtc = DateTime.UtcNow,
        DateModifiedUtc = DateTime.UtcNow
    };
    foreach (var trait in row.TraitList.OrderBy(x => x.SortOrder))
        clone.TraitList.Add(new CreatureTrait { Name = trait.Name, Description = trait.Description, SortOrder = trait.SortOrder });
    foreach (var action in row.ActionList.OrderBy(x => x.SortOrder))
        clone.ActionList.Add(new CreatureAction { Name = action.Name, Description = action.Description, SortOrder = action.SortOrder });
    foreach (var subtypeLink in row.CreatureSubtypeLinks.OrderBy(x => x.SortOrder))
        clone.CreatureSubtypeLinks.Add(new CreatureCreatureSubtype { CreatureSubtypeId = subtypeLink.CreatureSubtypeId, SortOrder = subtypeLink.SortOrder });

    db.Creatures.Add(clone);
    await db.SaveChangesAsync();
    return Results.Ok(new { clone.CreatureId });
}).RequireAuthorization();

app.MapGet("/api/parties", async (HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var includeAll = IsAdmin(http) && showAll == true;
    var sharedIds = includeAll ? new List<int>() : await db.PartyShares.Where(x => x.SharedWithUserId == userId.Value).Select(x => x.PartyId).ToListAsync();

    var rows = await db.Parties
        .Where(x => x.DateDeletedUtc == null && (includeAll || x.OwnerAppUserId == userId.Value || sharedIds.Contains(x.PartyId)))
        .OrderBy(x => x.Name)
        .Select(x => new PartyResponse
        {
            PartyId = x.PartyId,
            OwnerAppUserId = x.OwnerAppUserId,
            Name = x.Name,
            Description = x.Description,
            CampaignId = x.CampaignId == 0 ? null : x.CampaignId,
            MemberCount = db.Characters.Count(c => c.DateDeletedUtc == null && c.PartyId == x.PartyId)
        })
        .ToListAsync();

    return Results.Ok(rows);
}).RequireAuthorization();

app.MapGet("/api/parties/{id:int}", async (int id, HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var isAdmin = IsAdmin(http);
    var includeAll = isAdmin && showAll == true;
    var row = await db.Parties.FirstOrDefaultAsync(x => x.PartyId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var canView = isAdmin || includeAll || row.OwnerAppUserId == userId.Value || await db.PartyShares.AnyAsync(x => x.PartyId == id && x.SharedWithUserId == userId.Value);
    if (!canView) return Results.Forbid();

    var memberCount = await db.Characters.CountAsync(c => c.DateDeletedUtc == null && c.PartyId == row.PartyId);
    return Results.Ok(new PartyResponse
    {
        PartyId = row.PartyId,
        OwnerAppUserId = row.OwnerAppUserId,
        Name = row.Name,
        Description = row.Description,
        CampaignId = row.CampaignId == 0 ? null : row.CampaignId,
        MemberCount = memberCount
    });
}).RequireAuthorization();

app.MapGet("/api/parties/{id:int}/members", async (int id, HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var isAdmin = IsAdmin(http);
    var includeAll = isAdmin && showAll == true;
    var row = await db.Parties.FirstOrDefaultAsync(x => x.PartyId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var canView = isAdmin || includeAll || row.OwnerAppUserId == userId.Value || await db.PartyShares.AnyAsync(x => x.PartyId == id && x.SharedWithUserId == userId.Value);
    if (!canView) return Results.Forbid();

    var members = await db.Characters
        .Where(x => x.DateDeletedUtc == null && x.PartyId == id)
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

    return Results.Ok(members);
}).RequireAuthorization();

app.MapPost("/api/parties", async (UpsertPartyRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new ApiError("PARTY_NAME_REQUIRED", "Party name is required.", http.TraceIdentifier));

    var normalizedName = TitleNormalization.ToPascalTitle(req.Name);
    var campaignId = req.CampaignId ?? 0;
    var exists = await db.Parties.AnyAsync(x => x.DateDeletedUtc == null && x.CampaignId == campaignId && x.Name == normalizedName);
    if (exists) return Results.BadRequest(new ApiError("PARTY_DUPLICATE_NAME", "Party name already exists in that campaign.", http.TraceIdentifier));

    var row = new Party
    {
        OwnerAppUserId = userId.Value,
        Name = normalizedName,
        Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
        CampaignId = campaignId,
        DateCreatedUtc = DateTime.UtcNow,
        DateModifiedUtc = DateTime.UtcNow
    };

    db.Parties.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(new { row.PartyId });
}).RequireAuthorization();

app.MapPut("/api/parties/{id:int}", async (int id, UpsertPartyRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new ApiError("PARTY_NAME_REQUIRED", "Party name is required.", http.TraceIdentifier));

    var row = await db.Parties.FirstOrDefaultAsync(x => x.PartyId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var canEdit = row.OwnerAppUserId == userId.Value || await db.PartyShares.AnyAsync(x => x.PartyId == id && x.SharedWithUserId == userId.Value && x.Permission == SharePermission.Edit);
    if (!canEdit && !IsAdmin(http)) return Results.Forbid();

    var normalizedName = TitleNormalization.ToPascalTitle(req.Name);
    var campaignId = req.CampaignId ?? 0;
    var exists = await db.Parties.AnyAsync(x => x.DateDeletedUtc == null && x.PartyId != id && x.CampaignId == campaignId && x.Name == normalizedName);
    if (exists) return Results.BadRequest(new ApiError("PARTY_DUPLICATE_NAME", "Party name already exists in that campaign.", http.TraceIdentifier));

    row.Name = normalizedName;
    row.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
    row.CampaignId = campaignId;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { row.PartyId });
}).RequireAuthorization();

app.MapDelete("/api/parties/{id:int}", async (int id, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var row = await db.Parties.FirstOrDefaultAsync(x => x.PartyId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != userId.Value && !IsAdmin(http)) return Results.Forbid();

    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/parties/{id:int}/share", async (int id, ShareRecordRequest req, HttpContext http, AppDbContext db) =>
{
    var me = GetUserId(http); if (me is null) return Results.Unauthorized();
    var row = await db.Parties.FirstOrDefaultAsync(x => x.PartyId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != me.Value && !IsAdmin(http)) return Results.Forbid();

    var exists = await db.Users.AnyAsync(x => x.AppUserId == req.UserId && x.DateDeletedUtc == null);
    if (!exists) return Results.NotFound();
    if (req.Permission is < 0 or > 1) return Results.BadRequest("Invalid permission.");

    var link = await db.PartyShares.FirstOrDefaultAsync(x => x.PartyId == id && x.SharedWithUserId == req.UserId);
    if (link is null)
        db.PartyShares.Add(new PartyShare { PartyId = id, SharedWithUserId = req.UserId, Permission = (SharePermission)req.Permission, DateCreatedUtc = DateTime.UtcNow });
    else
        link.Permission = (SharePermission)req.Permission;

    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

app.MapDelete("/api/parties/{id:int}/share/{userId:int}", async (int id, int userId, HttpContext http, AppDbContext db) =>
{
    var me = GetUserId(http); if (me is null) return Results.Unauthorized();
    var row = await db.Parties.FirstOrDefaultAsync(x => x.PartyId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != me.Value && !IsAdmin(http)) return Results.Forbid();

    var link = await db.PartyShares.FirstOrDefaultAsync(x => x.PartyId == id && x.SharedWithUserId == userId);
    if (link is null) return Results.NotFound();
    db.PartyShares.Remove(link);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/quests", async (HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var includeAll = IsAdmin(http) && showAll == true;
    var sharedIds = includeAll ? new List<int>() : await db.QuestShares.Where(x => x.SharedWithUserId == userId.Value).Select(x => x.QuestId).ToListAsync();

    var rows = await db.Quests
        .Where(x => x.DateDeletedUtc == null && (includeAll || x.OwnerAppUserId == userId.Value || sharedIds.Contains(x.QuestId)))
        .OrderBy(x => x.Title)
        .Select(x => new QuestResponse
        {
            QuestId = x.QuestId,
            OwnerAppUserId = x.OwnerAppUserId,
            CampaignId = x.CampaignId == 0 ? null : x.CampaignId,
            Title = x.Title,
            Summary = x.Summary,
            Mode = (int)x.Mode,
            UseChoiceMode = x.UseChoiceMode,
            StartNodeId = x.StartNodeId
        })
        .ToListAsync();

    return Results.Ok(rows);
}).RequireAuthorization();

app.MapGet("/api/quests/{id:int}", async (int id, HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var isAdmin = IsAdmin(http);
    var includeAll = isAdmin && showAll == true;
    var row = await db.Quests.FirstOrDefaultAsync(x => x.QuestId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var canView = isAdmin || includeAll || row.OwnerAppUserId == userId.Value || await db.QuestShares.AnyAsync(x => x.QuestId == id && x.SharedWithUserId == userId.Value);
    if (!canView) return Results.Forbid();

    var nodes = await db.QuestNodes.Where(x => x.QuestId == id && x.DateDeletedUtc == null).OrderBy(x => x.OrderIndex)
        .Select(x => new QuestNodeResponse
        {
            QuestNodeId = x.QuestNodeId,
            Title = x.Title,
            NodeType = (int)x.NodeType,
            OrderIndex = x.OrderIndex,
            BodyMarkdown = x.BodyMarkdown,
            DmHints = x.DmHints,
            EncounterId = x.EncounterId,
            CanvasX = x.CanvasX,
            CanvasY = x.CanvasY
        }).ToListAsync();

    var choices = await db.QuestChoices.Where(x => x.QuestId == id && x.DateDeletedUtc == null).OrderBy(x => x.OrderIndex)
        .Select(x => new QuestChoiceResponse
        {
            QuestChoiceId = x.QuestChoiceId,
            FromNodeId = x.FromNodeId,
            ToNodeId = x.ToNodeId,
            Label = x.Label,
            ConditionExpression = x.ConditionExpression,
            EffectsJson = x.EffectsJson,
            OrderIndex = x.OrderIndex
        }).ToListAsync();

    return Results.Ok(new QuestResponse
    {
        QuestId = row.QuestId,
        OwnerAppUserId = row.OwnerAppUserId,
        CampaignId = row.CampaignId == 0 ? null : row.CampaignId,
        Title = row.Title,
        Summary = row.Summary,
        Mode = (int)row.Mode,
        UseChoiceMode = row.UseChoiceMode,
        StartNodeId = row.StartNodeId,
        Nodes = nodes,
        Choices = choices
    });
}).RequireAuthorization();

app.MapPost("/api/quests", async (UpsertQuestRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest(new ApiError("QUEST_TITLE_REQUIRED", "Quest title is required.", http.TraceIdentifier));

    var row = new Quest
    {
        OwnerAppUserId = userId.Value,
        CampaignId = req.CampaignId ?? 0,
        Title = TitleNormalization.ToPascalTitle(req.Title),
        Summary = string.IsNullOrWhiteSpace(req.Summary) ? null : req.Summary.Trim(),
        Mode = Enum.IsDefined(typeof(QuestMode), req.Mode) ? (QuestMode)req.Mode : QuestMode.Hybrid,
        UseChoiceMode = req.UseChoiceMode,
        StartNodeId = req.StartNodeId,
        DateCreatedUtc = DateTime.UtcNow,
        DateModifiedUtc = DateTime.UtcNow
    };

    db.Quests.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(new { row.QuestId });
}).RequireAuthorization();

app.MapPut("/api/quests/{id:int}", async (int id, UpsertQuestRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest(new ApiError("QUEST_TITLE_REQUIRED", "Quest title is required.", http.TraceIdentifier));

    var row = await db.Quests.FirstOrDefaultAsync(x => x.QuestId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    var canEdit = row.OwnerAppUserId == userId.Value || await db.QuestShares.AnyAsync(x => x.QuestId == id && x.SharedWithUserId == userId.Value && x.Permission == SharePermission.Edit);
    if (!canEdit && !IsAdmin(http)) return Results.Forbid();

    row.CampaignId = req.CampaignId ?? 0;
    row.Title = TitleNormalization.ToPascalTitle(req.Title);
    row.Summary = string.IsNullOrWhiteSpace(req.Summary) ? null : req.Summary.Trim();
    row.Mode = Enum.IsDefined(typeof(QuestMode), req.Mode) ? (QuestMode)req.Mode : QuestMode.Hybrid;
    row.UseChoiceMode = req.UseChoiceMode;
    row.StartNodeId = req.StartNodeId;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { row.QuestId });
}).RequireAuthorization();

app.MapDelete("/api/quests/{id:int}", async (int id, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var row = await db.Quests.FirstOrDefaultAsync(x => x.QuestId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != userId.Value && !IsAdmin(http)) return Results.Forbid();

    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/quests/{id:int}/share", async (int id, ShareRecordRequest req, HttpContext http, AppDbContext db) =>
{
    var me = GetUserId(http); if (me is null) return Results.Unauthorized();
    var row = await db.Quests.FirstOrDefaultAsync(x => x.QuestId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != me.Value && !IsAdmin(http)) return Results.Forbid();

    var exists = await db.Users.AnyAsync(x => x.AppUserId == req.UserId && x.DateDeletedUtc == null);
    if (!exists) return Results.NotFound();
    if (req.Permission is < 0 or > 1) return Results.BadRequest("Invalid permission.");

    var link = await db.QuestShares.FirstOrDefaultAsync(x => x.QuestId == id && x.SharedWithUserId == req.UserId);
    if (link is null)
        db.QuestShares.Add(new QuestShare { QuestId = id, SharedWithUserId = req.UserId, Permission = (SharePermission)req.Permission, DateCreatedUtc = DateTime.UtcNow });
    else
        link.Permission = (SharePermission)req.Permission;

    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

app.MapDelete("/api/quests/{id:int}/share/{userId:int}", async (int id, int userId, HttpContext http, AppDbContext db) =>
{
    var me = GetUserId(http); if (me is null) return Results.Unauthorized();
    var row = await db.Quests.FirstOrDefaultAsync(x => x.QuestId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.OwnerAppUserId != me.Value && !IsAdmin(http)) return Results.Forbid();

    var link = await db.QuestShares.FirstOrDefaultAsync(x => x.QuestId == id && x.SharedWithUserId == userId);
    if (link is null) return Results.NotFound();
    db.QuestShares.Remove(link);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/quests/{id:int}/nodes", async (int id, UpsertQuestNodeRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var quest = await db.Quests.FirstOrDefaultAsync(x => x.QuestId == id && x.DateDeletedUtc == null);
    if (quest is null) return Results.NotFound();
    var canEdit = quest.OwnerAppUserId == userId.Value || await db.QuestShares.AnyAsync(x => x.QuestId == id && x.SharedWithUserId == userId.Value && x.Permission == SharePermission.Edit);
    if (!canEdit && !IsAdmin(http)) return Results.Forbid();

    var node = new QuestNode
    {
        QuestId = id,
        Title = TitleNormalization.ToPascalTitle(string.IsNullOrWhiteSpace(req.Title) ? "Untitled Node" : req.Title),
        NodeType = Enum.IsDefined(typeof(QuestNodeType), req.NodeType) ? (QuestNodeType)req.NodeType : QuestNodeType.Scene,
        OrderIndex = req.OrderIndex,
        BodyMarkdown = req.BodyMarkdown,
        DmHints = req.DmHints,
        EncounterId = req.EncounterId,
        CanvasX = req.CanvasX,
        CanvasY = req.CanvasY,
        DateCreatedUtc = DateTime.UtcNow,
        DateModifiedUtc = DateTime.UtcNow
    };

    db.QuestNodes.Add(node);
    await db.SaveChangesAsync();
    return Results.Ok(new { node.QuestNodeId });
}).RequireAuthorization();

app.MapPut("/api/quests/{questId:int}/nodes/{nodeId:int}", async (int questId, int nodeId, UpsertQuestNodeRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var quest = await db.Quests.FirstOrDefaultAsync(x => x.QuestId == questId && x.DateDeletedUtc == null);
    if (quest is null) return Results.NotFound();
    var canEdit = quest.OwnerAppUserId == userId.Value || await db.QuestShares.AnyAsync(x => x.QuestId == questId && x.SharedWithUserId == userId.Value && x.Permission == SharePermission.Edit);
    if (!canEdit && !IsAdmin(http)) return Results.Forbid();

    var node = await db.QuestNodes.FirstOrDefaultAsync(x => x.QuestNodeId == nodeId && x.QuestId == questId && x.DateDeletedUtc == null);
    if (node is null) return Results.NotFound();

    node.Title = TitleNormalization.ToPascalTitle(string.IsNullOrWhiteSpace(req.Title) ? "Untitled Node" : req.Title);
    node.NodeType = Enum.IsDefined(typeof(QuestNodeType), req.NodeType) ? (QuestNodeType)req.NodeType : QuestNodeType.Scene;
    node.OrderIndex = req.OrderIndex;
    node.BodyMarkdown = req.BodyMarkdown;
    node.DmHints = req.DmHints;
    node.EncounterId = req.EncounterId;
    node.CanvasX = req.CanvasX;
    node.CanvasY = req.CanvasY;
    node.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { node.QuestNodeId });
}).RequireAuthorization();

app.MapPost("/api/quests/{questId:int}/nodes/{nodeId:int}/position", async (int questId, int nodeId, JsonElement req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var quest = await db.Quests.FirstOrDefaultAsync(x => x.QuestId == questId && x.DateDeletedUtc == null);
    if (quest is null) return Results.NotFound();
    var canEdit = quest.OwnerAppUserId == userId.Value || await db.QuestShares.AnyAsync(x => x.QuestId == questId && x.SharedWithUserId == userId.Value && x.Permission == SharePermission.Edit);
    if (!canEdit && !IsAdmin(http)) return Results.Forbid();

    var node = await db.QuestNodes.FirstOrDefaultAsync(x => x.QuestNodeId == nodeId && x.QuestId == questId && x.DateDeletedUtc == null);
    if (node is null) return Results.NotFound();

    if (req.TryGetProperty("x", out var xProp) && xProp.ValueKind == JsonValueKind.Number) node.CanvasX = xProp.GetDouble();
    if (req.TryGetProperty("y", out var yProp) && yProp.ValueKind == JsonValueKind.Number) node.CanvasY = yProp.GetDouble();
    node.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

app.MapDelete("/api/quests/{questId:int}/nodes/{nodeId:int}", async (int questId, int nodeId, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var quest = await db.Quests.FirstOrDefaultAsync(x => x.QuestId == questId && x.DateDeletedUtc == null);
    if (quest is null) return Results.NotFound();
    if (quest.OwnerAppUserId != userId.Value && !IsAdmin(http)) return Results.Forbid();

    var node = await db.QuestNodes.FirstOrDefaultAsync(x => x.QuestNodeId == nodeId && x.QuestId == questId && x.DateDeletedUtc == null);
    if (node is null) return Results.NotFound();
    node.DateDeletedUtc = DateTime.UtcNow;
    node.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/quests/{id:int}/choices", async (int id, UpsertQuestChoiceRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var quest = await db.Quests.FirstOrDefaultAsync(x => x.QuestId == id && x.DateDeletedUtc == null);
    if (quest is null) return Results.NotFound();
    var canEdit = quest.OwnerAppUserId == userId.Value || await db.QuestShares.AnyAsync(x => x.QuestId == id && x.SharedWithUserId == userId.Value && x.Permission == SharePermission.Edit);
    if (!canEdit && !IsAdmin(http)) return Results.Forbid();

    var choice = new QuestChoice
    {
        QuestId = id,
        FromNodeId = req.FromNodeId,
        ToNodeId = req.ToNodeId,
        Label = string.IsNullOrWhiteSpace(req.Label) ? "Next" : req.Label.Trim(),
        ConditionExpression = req.ConditionExpression,
        EffectsJson = req.EffectsJson,
        OrderIndex = req.OrderIndex,
        DateCreatedUtc = DateTime.UtcNow,
        DateModifiedUtc = DateTime.UtcNow
    };

    db.QuestChoices.Add(choice);
    await db.SaveChangesAsync();
    return Results.Ok(new { choice.QuestChoiceId });
}).RequireAuthorization();

app.MapPut("/api/quests/{questId:int}/choices/{choiceId:int}", async (int questId, int choiceId, UpsertQuestChoiceRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var quest = await db.Quests.FirstOrDefaultAsync(x => x.QuestId == questId && x.DateDeletedUtc == null);
    if (quest is null) return Results.NotFound();
    var canEdit = quest.OwnerAppUserId == userId.Value || await db.QuestShares.AnyAsync(x => x.QuestId == questId && x.SharedWithUserId == userId.Value && x.Permission == SharePermission.Edit);
    if (!canEdit && !IsAdmin(http)) return Results.Forbid();

    var choice = await db.QuestChoices.FirstOrDefaultAsync(x => x.QuestChoiceId == choiceId && x.QuestId == questId && x.DateDeletedUtc == null);
    if (choice is null) return Results.NotFound();

    choice.FromNodeId = req.FromNodeId;
    choice.ToNodeId = req.ToNodeId;
    choice.Label = string.IsNullOrWhiteSpace(req.Label) ? "Next" : req.Label.Trim();
    choice.ConditionExpression = req.ConditionExpression;
    choice.EffectsJson = req.EffectsJson;
    choice.OrderIndex = req.OrderIndex;
    choice.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { choice.QuestChoiceId });
}).RequireAuthorization();

app.MapDelete("/api/quests/{questId:int}/choices/{choiceId:int}", async (int questId, int choiceId, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var quest = await db.Quests.FirstOrDefaultAsync(x => x.QuestId == questId && x.DateDeletedUtc == null);
    if (quest is null) return Results.NotFound();
    if (quest.OwnerAppUserId != userId.Value && !IsAdmin(http)) return Results.Forbid();

    var choice = await db.QuestChoices.FirstOrDefaultAsync(x => x.QuestChoiceId == choiceId && x.QuestId == questId && x.DateDeletedUtc == null);
    if (choice is null) return Results.NotFound();
    choice.DateDeletedUtc = DateTime.UtcNow;
    choice.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/encounters/options", async (HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();

    var campaignShareIds = await db.CampaignShares.Where(x => x.SharedWithUserId == userId.Value).Select(x => x.CampaignId).ToListAsync();
    var partyShareIds = await db.PartyShares.Where(x => x.SharedWithUserId == userId.Value).Select(x => x.PartyId).ToListAsync();
    var characterShareIds = await db.CharacterShares.Where(x => x.SharedWithUserId == userId.Value).Select(x => x.CharacterId).ToListAsync();
    var creatureShareIds = await db.CreatureShares.Where(x => x.SharedWithUserId == userId.Value).Select(x => x.CreatureId).ToListAsync();
    var includeAll = IsAdmin(http) && showAll == true;

    var response = new EncounterOptionsResponse
    {
        Campaigns = await db.Campaigns
            .Where(x => x.DateDeletedUtc == null && (includeAll || x.OwnerAppUserId == userId.Value || campaignShareIds.Contains(x.CampaignId)))
            .OrderBy(x => x.Name)
            .Select(x => new EncounterOptionItem { Id = x.CampaignId, Name = x.Name })
            .ToListAsync(),
        Parties = await db.Parties
            .Where(x => x.DateDeletedUtc == null && (includeAll || x.OwnerAppUserId == userId.Value || partyShareIds.Contains(x.PartyId)))
            .OrderBy(x => x.Name)
            .Select(x => new EncounterOptionItem { Id = x.PartyId, Name = x.Name, PartyId = x.PartyId })
            .ToListAsync(),
        Characters = await db.Characters
            .Where(x => x.DateDeletedUtc == null && (includeAll || x.OwnerAppUserId == userId.Value || characterShareIds.Contains(x.CharacterId)))
            .OrderBy(x => x.Name)
            .Select(x => new EncounterOptionItem { Id = x.CharacterId, Name = x.Name, ArmorClass = x.ArmorClass, HitPoints = x.HitPointsCurrent ?? x.HitPointsMax, InitiativeModifier = x.InitiativeModifier, ParticipantType = (int)(x.CharacterType == CharacterType.NPC ? EncounterParticipantType.NPC : EncounterParticipantType.PC) })
            .ToListAsync(),
        Creatures = await db.Creatures
            .Where(x => x.DateDeletedUtc == null && (includeAll || x.IsSystem || x.OwnerAppUserId == userId.Value || creatureShareIds.Contains(x.CreatureId)))
            .OrderBy(x => x.Name)
            .Select(x => new EncounterOptionItem { Id = x.CreatureId, Name = x.Name, ArmorClass = x.ArmorClass, HitPoints = x.HitPoints, InitiativeModifier = x.InitiativeModifier, ParticipantType = (int)EncounterParticipantType.Creature })
            .ToListAsync()
    };

    return Results.Ok(response);
}).RequireAuthorization();

app.MapGet("/api/encounters", async (HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();

    var includeAll = IsAdmin(http) && showAll == true;
    var campaignShareIds = includeAll ? new List<int>() : await db.CampaignShares.Where(x => x.SharedWithUserId == userId.Value).Select(x => x.CampaignId).ToListAsync();
    var accessibleCampaignIds = includeAll
        ? new List<int>()
        : await db.Campaigns
            .Where(x => x.DateDeletedUtc == null && (x.OwnerAppUserId == userId.Value || campaignShareIds.Contains(x.CampaignId)))
            .Select(x => x.CampaignId)
            .ToListAsync();

    var rows = await db.Encounters
        .Include(x => x.Participants)
        .Where(x => x.DateDeletedUtc == null && (includeAll || x.CampaignId == 0 || accessibleCampaignIds.Contains(x.CampaignId)))
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

app.MapGet("/api/encounters/{id:int}", async (int id, HttpContext http, AppDbContext db, bool? showAll) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var includeAll = IsAdmin(http) && showAll == true;

    var row = await db.Encounters
        .Include(x => x.Participants)
        .FirstOrDefaultAsync(x => x.EncounterId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (!includeAll && !await CanAccessEncounter(http, db, row, requireEdit: false)) return Results.Forbid();

    return Results.Ok(new EncounterResponse
    {
        EncounterId = row.EncounterId,
        CampaignId = row.CampaignId == 0 ? null : row.CampaignId,
        Name = row.Name,
        EncounterType = (int)row.EncounterType,
        Description = row.Description,
        Participants = row.Participants.Select(p => new EncounterParticipantResponse
        {
            EncounterParticipantId = p.EncounterParticipantId,
            ParticipantType = (int)p.ParticipantType,
            SourceId = p.SourceId,
            NameSnapshot = p.NameSnapshot,
            ArmorClassSnapshot = p.ArmorClassSnapshot,
            HitPointsCurrent = p.HitPointsCurrent,
            InitiativeModifierSnapshot = p.InitiativeModifierSnapshot
        }).ToList()
    });
}).RequireAuthorization();

app.MapPost("/api/encounters", async (UpsertEncounterRequest req, HttpContext http, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new ApiError("ENCOUNTER_NAME_REQUIRED", "Encounter name is required.", http.TraceIdentifier));

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

app.MapPut("/api/encounters/{id:int}", async (int id, UpsertEncounterRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new ApiError("ENCOUNTER_NAME_REQUIRED", "Encounter name is required.", http.TraceIdentifier));

    var row = await db.Encounters.Include(x => x.Participants).FirstOrDefaultAsync(x => x.EncounterId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (!await CanAccessEncounter(http, db, row, requireEdit: true)) return Results.Forbid();

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
}).RequireAuthorization();

app.MapDelete("/api/encounters/{id:int}", async (int id, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var row = await db.Encounters.FirstOrDefaultAsync(x => x.EncounterId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (!await CanAccessEncounter(http, db, row, requireEdit: true)) return Results.Forbid();

    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();



app.MapGet("/api/admin/soft-deleted/{entity}", async (string entity, AppDbContext db) =>
    
{
    entity = entity.Trim().ToLowerInvariant();

    return entity switch
    {
        "characters" => Results.Ok(await db.Characters.Where(x => x.DateDeletedUtc != null)
            .OrderByDescending(x => x.DateDeletedUtc)
            .Select(x => new { Id = x.CharacterId, Name = x.Name, DateDeletedUtc = x.DateDeletedUtc!.Value.ToString("u") })
            .ToListAsync()),
        "campaigns" => Results.Ok(await db.Campaigns.Where(x => x.DateDeletedUtc != null)
            .OrderByDescending(x => x.DateDeletedUtc)
            .Select(x => new { Id = x.CampaignId, Name = x.Name, DateDeletedUtc = x.DateDeletedUtc!.Value.ToString("u") })
            .ToListAsync()),
        "creatures" => Results.Ok(await db.Creatures.Where(x => x.DateDeletedUtc != null)
            .OrderByDescending(x => x.DateDeletedUtc)
            .Select(x => new { Id = x.CreatureId, Name = x.Name, DateDeletedUtc = x.DateDeletedUtc!.Value.ToString("u") })
            .ToListAsync()),
        "encounters" => Results.Ok(await db.Encounters.Where(x => x.DateDeletedUtc != null)
            .OrderByDescending(x => x.DateDeletedUtc)
            .Select(x => new { Id = x.EncounterId, Name = x.Name, DateDeletedUtc = x.DateDeletedUtc!.Value.ToString("u") })
            .ToListAsync()),
        "parties" => Results.Ok(await db.Parties.Where(x => x.DateDeletedUtc != null)
            .OrderByDescending(x => x.DateDeletedUtc)
            .Select(x => new { Id = x.PartyId, Name = x.Name, DateDeletedUtc = x.DateDeletedUtc!.Value.ToString("u") })
            .ToListAsync()),
        _ => Results.BadRequest("Unknown entity.")
    };
});

app.MapPost("/api/admin/purge-soft-deleted/{entity}/selected", async (string entity, List<int> ids, AppDbContext db) =>
    
{
    entity = entity.Trim().ToLowerInvariant();
    if (ids.Count == 0) return Results.BadRequest("No ids provided.");

    int deleted;
    switch (entity)
    {
        case "characters":
            var chars = await db.Characters.Where(x => x.DateDeletedUtc != null && ids.Contains(x.CharacterId)).ToListAsync();
            deleted = chars.Count;
            db.Characters.RemoveRange(chars);
            break;
        case "campaigns":
            var campaigns = await db.Campaigns.Where(x => x.DateDeletedUtc != null && ids.Contains(x.CampaignId)).ToListAsync();
            deleted = campaigns.Count;
            db.Campaigns.RemoveRange(campaigns);
            break;
        case "creatures":
            var creatures = await db.Creatures.Where(x => x.DateDeletedUtc != null && ids.Contains(x.CreatureId)).ToListAsync();
            deleted = creatures.Count;
            db.Creatures.RemoveRange(creatures);
            break;
        case "encounters":
            var encounters = await db.Encounters.Where(x => x.DateDeletedUtc != null && ids.Contains(x.EncounterId)).ToListAsync();
            deleted = encounters.Count;
            db.Encounters.RemoveRange(encounters);
            break;
        case "parties":
            var parties = await db.Parties.Where(x => x.DateDeletedUtc != null && ids.Contains(x.PartyId)).ToListAsync();
            deleted = parties.Count;
            db.Parties.RemoveRange(parties);
            break;
        default:
            return Results.BadRequest("Unknown entity.");
    }

    await db.SaveChangesAsync();
    return Results.Ok($"Purged {deleted} selected {entity} record(s).");
});

app.MapPost("/api/admin/purge-soft-deleted/{entity}", async (string entity, AppDbContext db) =>
    
{
    entity = entity.Trim().ToLowerInvariant();

    int deleted;
    switch (entity)
    {
        case "characters":
            var chars = await db.Characters.Where(x => x.DateDeletedUtc != null).ToListAsync();
            deleted = chars.Count;
            db.Characters.RemoveRange(chars);
            break;
        case "campaigns":
            var campaigns = await db.Campaigns.Where(x => x.DateDeletedUtc != null).ToListAsync();
            deleted = campaigns.Count;
            db.Campaigns.RemoveRange(campaigns);
            break;
        case "creatures":
            var creatures = await db.Creatures.Where(x => x.DateDeletedUtc != null).ToListAsync();
            deleted = creatures.Count;
            db.Creatures.RemoveRange(creatures);
            break;
        case "encounters":
            var encounters = await db.Encounters.Where(x => x.DateDeletedUtc != null).ToListAsync();
            deleted = encounters.Count;
            db.Encounters.RemoveRange(encounters);
            break;
        case "parties":
            var parties = await db.Parties.Where(x => x.DateDeletedUtc != null).ToListAsync();
            deleted = parties.Count;
            db.Parties.RemoveRange(parties);
            break;
        default:
            return Results.BadRequest("Unknown entity.");
    }

    await db.SaveChangesAsync();
    return Results.Ok($"Purged {deleted} soft-deleted {entity} record(s).");
});

app.MapGet("/api/admin/logging/seq", (HttpContext http) =>
{
    if (!IsAdmin(http)) return Results.Forbid();

    var local = LoadLocalSeqSettings(app.Environment.ContentRootPath);
    var effectiveServerUrl = Environment.GetEnvironmentVariable("RULEFORGE_SEQ_URL")
        ?? local.ServerUrl
        ?? app.Configuration["Logging:Seq:ServerUrl"]
        ?? string.Empty;
    var effectiveAppName = local.AppName
        ?? app.Configuration["Logging:Seq:AppName"]
        ?? "RuleForge";

    return Results.Ok(new SeqLoggingSettingsResponse(
        effectiveServerUrl,
        string.IsNullOrWhiteSpace(local.ApiKey) ? string.Empty : "********",
        effectiveAppName,
        local.ServerUrl ?? string.Empty,
        string.IsNullOrWhiteSpace(local.ApiKey) ? string.Empty : "********",
        local.AppName ?? string.Empty,
        "http://localhost:5341",
        "https://seq.example.com",
        "Changes are saved to appsettings.Local.json on this server. Restart the app after saving so the Seq sink reconnects using the new settings."
    ));
}).RequireAuthorization();

app.MapPost("/api/admin/logging/seq", async (HttpContext http, SeqLoggingSettingsUpdateRequest req) =>
{
    if (!IsAdmin(http)) return Results.Forbid();

    var normalizedUrl = req.ServerUrl?.Trim() ?? string.Empty;
    Uri? parsedUri = null;
    if (!string.IsNullOrWhiteSpace(normalizedUrl) && !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out parsedUri))
    {
        return Results.BadRequest(new ApiError("SEQ_URL_INVALID", "Seq server URL must be a valid absolute URL, for example http://localhost:5341 or https://seq.example.com.", http.TraceIdentifier));
    }

    if (!string.IsNullOrWhiteSpace(normalizedUrl) && parsedUri is not null && parsedUri.Scheme is not ("http" or "https"))
    {
        return Results.BadRequest(new ApiError("SEQ_URL_INVALID", "Seq server URL must start with http:// or https://.", http.TraceIdentifier));
    }

    var existing = LoadLocalSeqSettings(app.Environment.ContentRootPath);
    var apiKey = req.ApiKeyMode?.Equals("keep", StringComparison.OrdinalIgnoreCase) == true
        ? existing.ApiKey
        : (req.ApiKey?.Trim() ?? string.Empty);

    SaveLocalSeqSettings(app.Environment.ContentRootPath, new LocalSeqSettings(
        string.IsNullOrWhiteSpace(normalizedUrl) ? null : normalizedUrl,
        string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
        string.IsNullOrWhiteSpace(req.AppName) ? "RuleForge" : req.AppName.Trim()
    ));

    await Task.CompletedTask;
    return Results.Ok(new MessageResponse("Seq logging settings saved to appsettings.Local.json. Restart RuleForge to apply the updated sink settings."));
}).RequireAuthorization();

app.MapPost("/api/admin/logging/seq/test", async (HttpContext http, IHttpClientFactory httpClientFactory) =>
{
    if (!IsAdmin(http)) return Results.Forbid();

    var settings = LoadLocalSeqSettings(app.Environment.ContentRootPath);
    if (string.IsNullOrWhiteSpace(settings.ServerUrl))
    {
        return Results.BadRequest(new ApiError("SEQ_NOT_CONFIGURED", "Save a Seq server URL first, then run the test.", http.TraceIdentifier));
    }

    if (!Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out var serverUri) || serverUri.Scheme is not ("http" or "https"))
    {
        return Results.BadRequest(new ApiError("SEQ_URL_INVALID", "Saved Seq server URL is invalid. Update the Seq settings and try again.", http.TraceIdentifier));
    }

    var appName = string.IsNullOrWhiteSpace(settings.AppName) ? "RuleForge" : settings.AppName.Trim();
    var ingestUri = new Uri(serverUri, "/api/events/raw?clef");
    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(10);

    using var request = new HttpRequestMessage(HttpMethod.Post, ingestUri);
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    if (!string.IsNullOrWhiteSpace(settings.ApiKey))
    {
        request.Headers.Add("X-Seq-ApiKey", settings.ApiKey);
    }

    var clef = JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["@t"] = DateTimeOffset.UtcNow,
        ["@mt"] = "RuleForge Seq connection test from admin settings",
        ["@l"] = "Information",
        ["Application"] = appName,
        ["SourceContext"] = "RuleForge.Settings.SeqTest",
        ["TraceId"] = http.TraceIdentifier,
        ["Environment"] = app.Environment.EnvironmentName,
        ["User"] = http.User.Identity?.Name,
        ["TestEvent"] = true
    });

    request.Content = new StringContent(clef, Encoding.UTF8, "application/vnd.serilog.clef");

    HttpResponseMessage response;
    try
    {
        response = await client.SendAsync(request);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Seq test connection failed for {SeqServerUrl}", settings.ServerUrl);
        return Results.BadRequest(new ApiError("SEQ_TEST_FAILED", $"Could not reach Seq at {settings.ServerUrl}: {ex.Message}", http.TraceIdentifier));
    }

    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        Log.Warning("Seq test connection returned {StatusCode} for {SeqServerUrl}. Body: {ResponseBody}", (int)response.StatusCode, settings.ServerUrl, body);
        return Results.BadRequest(new ApiError("SEQ_TEST_FAILED", $"Seq returned {(int)response.StatusCode} {response.ReasonPhrase}. {body}".Trim(), http.TraceIdentifier));
    }

    Log.Information("Seq test connection succeeded for {SeqServerUrl}", settings.ServerUrl);
    return Results.Ok(new SeqLoggingTestResponse(
        $"Test event sent to {settings.ServerUrl}. Check Seq for 'RuleForge Seq connection test from admin settings'.",
        settings.ServerUrl,
        appName,
        (int)response.StatusCode
    ));
}).RequireAuthorization();


app.MapGet("/api/users", async (AppDbContext db) =>
    
{
    var rows = await db.Users
        .Where(x => x.DateDeletedUtc == null)
        .OrderBy(x => x.Username)
        .Select(x => new UserResponse
        {
            AppUserId = x.AppUserId,
            Username = x.Username,
            Email = x.Email,
            Role = (int)x.Role,
            MustChangePassword = x.MustChangePassword,
            IsSystem = x.IsSystem
        })
        .ToListAsync();
    return Results.Ok(rows);
});

app.MapGet("/api/users/{id:int}", async (int id, AppDbContext db) =>
    
{
    var row = await db.Users
        .Where(x => x.AppUserId == id && x.DateDeletedUtc == null)
        .Select(x => new UserResponse
        {
            AppUserId = x.AppUserId,
            Username = x.Username,
            Email = x.Email,
            Role = (int)x.Role,
            MustChangePassword = x.MustChangePassword,
            IsSystem = x.IsSystem
        })
        .FirstOrDefaultAsync();
    return row is null ? Results.NotFound() : Results.Ok(row);
});

app.MapPost("/api/users", async (UpsertUserRequest req, AppDbContext db) =>
    
{
    if (string.IsNullOrWhiteSpace(req.Username)) return Results.BadRequest("Username is required.");
    if (string.IsNullOrWhiteSpace(req.Email)) return Results.BadRequest("Email is required.");
    if (string.IsNullOrWhiteSpace(req.Password)) return Results.BadRequest("Password is required.");

    var row = new AppUser
    {
        Username = req.Username.Trim(),
        Email = req.Email.Trim(),
        PasswordHash = PasswordHasher.Hash(req.Password),
        Role = Enum.IsDefined(typeof(AppRole), req.Role) ? (AppRole)req.Role : AppRole.User,
        MustChangePassword = false,
        IsSystem = false,
        DateCreatedUtc = DateTime.UtcNow,
        DateModifiedUtc = DateTime.UtcNow
    };

    db.Users.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(new { row.AppUserId });
});

app.MapPut("/api/users/{id:int}", async (int id, UpsertUserRequest req, AppDbContext db) =>
    
{
    if (string.IsNullOrWhiteSpace(req.Username)) return Results.BadRequest("Username is required.");
    if (string.IsNullOrWhiteSpace(req.Email)) return Results.BadRequest("Email is required.");

    var row = await db.Users.FirstOrDefaultAsync(x => x.AppUserId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();

    row.Username = req.Username.Trim();
    row.Email = req.Email.Trim();
    row.Role = Enum.IsDefined(typeof(AppRole), req.Role) ? (AppRole)req.Role : AppRole.User;
    if (!string.IsNullOrWhiteSpace(req.Password))
        row.PasswordHash = PasswordHasher.Hash(req.Password);
    row.DateModifiedUtc = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(new { row.AppUserId });
});

app.MapDelete("/api/users/{id:int}", async (int id, AppDbContext db) =>
    
{
    var row = await db.Users.FirstOrDefaultAsync(x => x.AppUserId == id && x.DateDeletedUtc == null);
    if (row is null) return Results.NotFound();
    if (row.IsSystem) return Results.BadRequest("System users cannot be deleted.");
    row.DateDeletedUtc = DateTime.UtcNow;
    row.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.NoContent();
});



app.MapPost("/api/auth/register", async (UpsertUserRequest req, AppDbContext db, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(req.Username)) return Results.BadRequest("Username is required.");
    if (string.IsNullOrWhiteSpace(req.Email)) return Results.BadRequest("Email is required.");
    if (string.IsNullOrWhiteSpace(req.Password)) return Results.BadRequest("Password is required.");

    var username = req.Username.Trim();
    var email = req.Email.Trim();

    var exists = await db.Users.AnyAsync(x => x.DateDeletedUtc == null && (x.Username == username || x.Email == email));
    if (exists) return Results.BadRequest("Username or email already exists.");

    var row = new AppUser
    {
        Username = username,
        Email = email,
        PasswordHash = PasswordHasher.Hash(req.Password),
        Role = AppRole.User,
        MustChangePassword = false,
        IsSystem = false,
        DateCreatedUtc = DateTime.UtcNow,
        DateModifiedUtc = DateTime.UtcNow
    };

    db.Users.Add(row);
    await db.SaveChangesAsync();

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, row.AppUserId.ToString()),
        new(ClaimTypes.Name, row.Username),
        new(ClaimTypes.Role, row.Role.ToString()),
        new("must_change_password", "false")
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    return Results.Ok(new LoginResponse
    {
        AppUserId = row.AppUserId,
        Username = row.Username,
        Role = (int)row.Role,
        MustChangePassword = false
    });
});

app.MapPost("/api/auth/login", async (LoginRequest req, AppDbContext db, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest("Username and password are required.");

    var loginId = req.Username.Trim();
    var user = await db.Users.FirstOrDefaultAsync(x => x.DateDeletedUtc == null && (x.Username == loginId || x.Email == loginId));
    if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
        return Results.Unauthorized();

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.AppUserId.ToString()),
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.Role, user.Role.ToString()),
        new("must_change_password", user.MustChangePassword ? "true" : "false")
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    return Results.Ok(new LoginResponse
    {
        AppUserId = user.AppUserId,
        Username = user.Username,
        Role = (int)user.Role,
        MustChangePassword = user.MustChangePassword
    });
});

app.MapPost("/api/auth/change-password", async (ChangePasswordRequest req, AppDbContext db, HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
        return Results.BadRequest("Username, current password, and new password are required.");

    var loginId = req.Username.Trim();
    var user = await db.Users.FirstOrDefaultAsync(x => x.DateDeletedUtc == null && (x.Username == loginId || x.Email == loginId));
    if (user is null || !PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
        return Results.Unauthorized();

    user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
    user.MustChangePassword = false;
    user.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.AppUserId.ToString()),
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.Role, user.Role.ToString()),
        new("must_change_password", "false")
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    return Results.Ok();
});

app.MapPost("/api/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok();
});






app.MapPost("/api/marketplace/publish", async (PublishMarketplaceRequest req, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest(new ApiError("MARKETPLACE_TITLE_REQUIRED", "Title is required.", http.TraceIdentifier));
    if (req.AssetType is < 1 or > 2) return Results.BadRequest(new ApiError("MARKETPLACE_ASSET_INVALID", "Invalid asset type.", http.TraceIdentifier));

    string payload;
    if (req.AssetType == (int)MarketplaceAssetType.Item)
    {
        var item = await db.Items.FirstOrDefaultAsync(x => x.ItemId == req.SourceEntityId && x.DateDeletedUtc == null);
        if (item is null) return Results.NotFound();
        if (item.OwnerAppUserId != userId.Value && !IsAdmin(http)) return Results.Forbid();
        payload = JsonSerializer.Serialize(item);
    }
    else
    {
        var creature = await db.Creatures.FirstOrDefaultAsync(x => x.CreatureId == req.SourceEntityId && x.DateDeletedUtc == null);
        if (creature is null) return Results.NotFound();
        payload = JsonSerializer.Serialize(creature);
    }

    var listing = new MarketplaceListing
    {
        AssetType = req.AssetType,
        SourceEntityId = req.SourceEntityId,
        OwnerUserId = userId.Value,
        OwnershipType = (int)MarketplaceOwnershipType.Creator,
        State = (int)MarketplaceListingState.PublicCreatorOwned,
        Title = req.Title.Trim(),
        Summary = string.IsNullOrWhiteSpace(req.Summary) ? null : req.Summary.Trim(),
        TagsJson = req.Tags is null ? null : JsonSerializer.Serialize(req.Tags),
        DateCreatedUtc = DateTime.UtcNow,
        DateModifiedUtc = DateTime.UtcNow
    };
    db.MarketplaceListings.Add(listing);
    await db.SaveChangesAsync();

    var version = new MarketplaceListingVersion
    {
        MarketplaceListingId = listing.MarketplaceListingId,
        VersionLabel = "1",
        PayloadJson = payload,
        CreatedByUserId = userId.Value,
        DateCreatedUtc = DateTime.UtcNow
    };
    db.MarketplaceListingVersions.Add(version);
    await db.SaveChangesAsync();

    listing.LatestVersionId = version.MarketplaceListingVersionId;
    await db.SaveChangesAsync();

    db.MarketplaceAuditEvents.Add(new MarketplaceAuditEvent { MarketplaceListingId = listing.MarketplaceListingId, ActorUserId = userId.Value, EventType = "publish", DateUtc = DateTime.UtcNow });
    await db.SaveChangesAsync();

    return Results.Ok(new MarketplaceListingDto
    {
        MarketplaceListingId = listing.MarketplaceListingId,
        AssetType = listing.AssetType,
        SourceEntityId = listing.SourceEntityId,
        OwnerUserId = listing.OwnerUserId,
        OwnershipType = listing.OwnershipType,
        State = listing.State,
        Title = listing.Title,
        Summary = listing.Summary,
        TagsJson = listing.TagsJson,
        LatestVersionId = listing.LatestVersionId,
        DateCreatedUtc = listing.DateCreatedUtc,
        DateModifiedUtc = listing.DateModifiedUtc
    });
}).RequireAuthorization();

app.MapGet("/api/marketplace/listings", async (AppDbContext db, int? assetType, string? q, int page = 1, int pageSize = 20) =>
{
    if (page < 1) page = 1;
    if (pageSize < 1 || pageSize > 100) pageSize = 20;

    var query = db.MarketplaceListings.Where(x => x.State == (int)MarketplaceListingState.PublicCreatorOwned || x.State == (int)MarketplaceListingState.PublicCommunityOwned);
    if (assetType.HasValue) query = query.Where(x => x.AssetType == assetType.Value);
    if (!string.IsNullOrWhiteSpace(q))
    {
        var term = q.Trim();
        query = query.Where(x => x.Title.Contains(term) || (x.Summary != null && x.Summary.Contains(term)) || (x.TagsJson != null && x.TagsJson.Contains(term)));
    }

    var rows = await query.OrderByDescending(x => x.DateModifiedUtc).Skip((page - 1) * pageSize).Take(pageSize)
        .Select(x => new MarketplaceListingDto
        {
            MarketplaceListingId = x.MarketplaceListingId,
            AssetType = x.AssetType,
            SourceEntityId = x.SourceEntityId,
            OwnerUserId = x.OwnerUserId,
            OwnershipType = x.OwnershipType,
            State = x.State,
            Title = x.Title,
            Summary = x.Summary,
            TagsJson = x.TagsJson,
            LatestVersionId = x.LatestVersionId,
            DateCreatedUtc = x.DateCreatedUtc,
            DateModifiedUtc = x.DateModifiedUtc
        }).ToListAsync();
    return Results.Ok(rows);
});

app.MapGet("/api/marketplace/listings/{listingId:int}", async (int listingId, AppDbContext db) =>
{
    var listing = await db.MarketplaceListings.FirstOrDefaultAsync(x => x.MarketplaceListingId == listingId && x.State != (int)MarketplaceListingState.Removed);
    if (listing is null) return Results.NotFound();
    var version = listing.LatestVersionId.HasValue ? await db.MarketplaceListingVersions.FirstOrDefaultAsync(x => x.MarketplaceListingVersionId == listing.LatestVersionId.Value) : null;
    return Results.Ok(new MarketplaceListingDetailDto
    {
        MarketplaceListingId = listing.MarketplaceListingId,
        AssetType = listing.AssetType,
        SourceEntityId = listing.SourceEntityId,
        OwnerUserId = listing.OwnerUserId,
        OwnershipType = listing.OwnershipType,
        State = listing.State,
        Title = listing.Title,
        Summary = listing.Summary,
        TagsJson = listing.TagsJson,
        LatestVersionId = listing.LatestVersionId,
        DateCreatedUtc = listing.DateCreatedUtc,
        DateModifiedUtc = listing.DateModifiedUtc,
        PayloadJson = version?.PayloadJson ?? "{}",
        VersionLabel = version?.VersionLabel
    });
});

app.MapPost("/api/marketplace/listings/{listingId:int}/import", async (int listingId, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var listing = await db.MarketplaceListings.FirstOrDefaultAsync(x => x.MarketplaceListingId == listingId && x.State != (int)MarketplaceListingState.Removed);
    if (listing is null || !listing.LatestVersionId.HasValue) return Results.NotFound();
    var version = await db.MarketplaceListingVersions.FirstOrDefaultAsync(x => x.MarketplaceListingVersionId == listing.LatestVersionId.Value);
    if (version is null) return Results.NotFound();

    int newEntityId;
    if (listing.AssetType == (int)MarketplaceAssetType.Item)
    {
        var src = JsonSerializer.Deserialize<Item>(version.PayloadJson);
        if (src is null) return Results.BadRequest(new ApiError("MARKETPLACE_PAYLOAD_INVALID", "Invalid listing payload.", http.TraceIdentifier));
        var row = new Item();
        row.Name = src.Name;
        row.Description = src.Description;
        row.ItemType = src.ItemType;
        row.Rarity = src.Rarity;
        row.Weight = src.Weight;
        row.CostAmount = src.CostAmount;
        row.CostCurrency = src.CostCurrency;
        row.RequiresAttunement = src.RequiresAttunement;
        row.SourceType = src.SourceType;
        row.Source = src.Source;
        row.Tags = src.Tags;
        row.WeaponCategory = src.WeaponCategory;
        row.DamageDice = src.DamageDice;
        row.DamageType = src.DamageType;
        row.Properties = src.Properties;
        row.RangeNormal = src.RangeNormal;
        row.RangeMax = src.RangeMax;
        row.IsMagicWeapon = src.IsMagicWeapon;
        row.AttackBonus = src.AttackBonus;
        row.DamageBonus = src.DamageBonus;
        row.ArmorCategory = src.ArmorCategory;
        row.ArmorClassBase = src.ArmorClassBase;
        row.DexCap = src.DexCap;
        row.StrengthRequirement = src.StrengthRequirement;
        row.StealthDisadvantage = src.StealthDisadvantage;
        row.IsMagicArmor = src.IsMagicArmor;
        row.ArmorBonus = src.ArmorBonus;
        row.Charges = src.Charges;
        row.MaxCharges = src.MaxCharges;
        row.RechargeRule = src.RechargeRule;
        row.ConsumableEffect = src.ConsumableEffect;
        row.Quantity = src.Quantity;
        row.Stackable = src.Stackable;
        row.Notes = src.Notes;
        row.OwnerAppUserId = userId.Value;
        row.DateCreatedUtc = DateTime.UtcNow;
        row.DateModifiedUtc = DateTime.UtcNow;
        db.Items.Add(row);
        await db.SaveChangesAsync();
        newEntityId = row.ItemId;
    }
    else
    {
        var src = JsonSerializer.Deserialize<Creature>(version.PayloadJson);
        if (src is null) return Results.BadRequest(new ApiError("MARKETPLACE_PAYLOAD_INVALID", "Invalid listing payload.", http.TraceIdentifier));
        var row = new Creature
        {
            Name = src.Name,
            Description = src.Description,
            ArmorClass = src.ArmorClass,
            HitPoints = src.HitPoints,
            InitiativeModifier = src.InitiativeModifier,
            Speed = src.Speed,
            ChallengeRating = src.ChallengeRating,
            ExperiencePoints = src.ExperiencePoints,
            PassivePerception = src.PassivePerception,
            Languages = src.Languages,
            UnderstandsButCannotSpeak = src.UnderstandsButCannotSpeak,
            Strength = src.Strength,
            Dexterity = src.Dexterity,
            Constitution = src.Constitution,
            Intelligence = src.Intelligence,
            Wisdom = src.Wisdom,
            Charisma = src.Charisma,
            DateCreatedUtc = DateTime.UtcNow,
            DateModifiedUtc = DateTime.UtcNow
        };
        db.Creatures.Add(row);
        await db.SaveChangesAsync();
        newEntityId = row.CreatureId;
    }

    var imp = new MarketplaceImport
    {
        MarketplaceListingId = listing.MarketplaceListingId,
        MarketplaceListingVersionId = version.MarketplaceListingVersionId,
        ImportedByUserId = userId.Value,
        AssetType = listing.AssetType,
        NewEntityId = newEntityId,
        DateImportedUtc = DateTime.UtcNow
    };
    db.MarketplaceImports.Add(imp);
    db.MarketplaceAuditEvents.Add(new MarketplaceAuditEvent { MarketplaceListingId = listing.MarketplaceListingId, ActorUserId = userId.Value, EventType = "import", DateUtc = DateTime.UtcNow });
    await db.SaveChangesAsync();

    return Results.Ok(new MarketplaceImportDto
    {
        MarketplaceImportId = imp.MarketplaceImportId,
        MarketplaceListingId = imp.MarketplaceListingId,
        MarketplaceListingVersionId = imp.MarketplaceListingVersionId,
        ImportedByUserId = imp.ImportedByUserId,
        AssetType = imp.AssetType,
        NewEntityId = imp.NewEntityId,
        DateImportedUtc = imp.DateImportedUtc
    });
}).RequireAuthorization();

app.MapPost("/api/marketplace/listings/{listingId:int}/unpublish", async (int listingId, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var listing = await db.MarketplaceListings.FirstOrDefaultAsync(x => x.MarketplaceListingId == listingId);
    if (listing is null) return Results.NotFound();
    if (listing.OwnerUserId != userId.Value && !IsAdmin(http)) return Results.Forbid();
    listing.State = (int)MarketplaceListingState.Removed;
    listing.DateRemovedUtc = DateTime.UtcNow;
    listing.DateModifiedUtc = DateTime.UtcNow;
    db.MarketplaceAuditEvents.Add(new MarketplaceAuditEvent { MarketplaceListingId = listing.MarketplaceListingId, ActorUserId = userId.Value, EventType = "unpublish", DateUtc = DateTime.UtcNow });
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();



app.MapDelete("/api/marketplace/listings/{listingId:int}", async (int listingId, HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var listing = await db.MarketplaceListings.FirstOrDefaultAsync(x => x.MarketplaceListingId == listingId);
    if (listing is null) return Results.NotFound();
    if (listing.OwnerUserId != userId.Value && !IsAdmin(http)) return Results.Forbid();

    listing.State = (int)MarketplaceListingState.Removed;
    listing.DateRemovedUtc = DateTime.UtcNow;
    listing.DateModifiedUtc = DateTime.UtcNow;

    db.MarketplaceAuditEvents.Add(new MarketplaceAuditEvent
    {
        MarketplaceListingId = listing.MarketplaceListingId,
        ActorUserId = userId.Value,
        EventType = "delete_listing",
        DateUtc = DateTime.UtcNow
    });

    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/marketplace/me/listings", async (HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var rows = await db.MarketplaceListings.Where(x => x.OwnerUserId == userId.Value).OrderByDescending(x => x.DateModifiedUtc)
        .Select(x => new MarketplaceListingDto
        {
            MarketplaceListingId = x.MarketplaceListingId,
            AssetType = x.AssetType,
            SourceEntityId = x.SourceEntityId,
            OwnerUserId = x.OwnerUserId,
            OwnershipType = x.OwnershipType,
            State = x.State,
            Title = x.Title,
            Summary = x.Summary,
            TagsJson = x.TagsJson,
            LatestVersionId = x.LatestVersionId,
            DateCreatedUtc = x.DateCreatedUtc,
            DateModifiedUtc = x.DateModifiedUtc
        }).ToListAsync();
    return Results.Ok(rows);
}).RequireAuthorization();

app.MapGet("/api/marketplace/me/imports", async (HttpContext http, AppDbContext db) =>
{
    var userId = GetUserId(http); if (userId is null) return Results.Unauthorized();
    var rows = await db.MarketplaceImports.Where(x => x.ImportedByUserId == userId.Value).OrderByDescending(x => x.DateImportedUtc)
        .Select(x => new MarketplaceImportDto
        {
            MarketplaceImportId = x.MarketplaceImportId,
            MarketplaceListingId = x.MarketplaceListingId,
            MarketplaceListingVersionId = x.MarketplaceListingVersionId,
            ImportedByUserId = x.ImportedByUserId,
            AssetType = x.AssetType,
            NewEntityId = x.NewEntityId,
            DateImportedUtc = x.DateImportedUtc
        }).ToListAsync();
    return Results.Ok(rows);
}).RequireAuthorization();

app.MapGet("/api/friends", async (HttpContext http, AppDbContext db) =>
{
    var idStr = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!int.TryParse(idStr, out var userId)) return Results.Unauthorized();

    try
    {
        var links = await db.FriendLinks.Where(x => x.RequesterUserId == userId || x.AddresseeUserId == userId).ToListAsync();
        var userIds = links.SelectMany(x => new[] { x.RequesterUserId, x.AddresseeUserId }).Distinct().ToList();
        var users = await db.Users.Where(x => userIds.Contains(x.AppUserId) && x.DateDeletedUtc == null)
            .ToDictionaryAsync(x => x.AppUserId, x => x);

        var accepted = links.Where(x => x.Status == FriendRequestStatus.Accepted).ToList();
        var friends = new List<FriendSummary>();
        foreach (var l in accepted)
        {
            var friendId = l.RequesterUserId == userId ? l.AddresseeUserId : l.RequesterUserId;
            if (users.TryGetValue(friendId, out var u))
                friends.Add(new FriendSummary { AppUserId = u.AppUserId, Username = u.Username, Email = u.Email });
        }

        var incoming = links.Where(x => x.Status == FriendRequestStatus.Pending && x.AddresseeUserId == userId)
            .Select(x => new FriendRequestView
            {
                FriendLinkId = x.FriendLinkId,
                RequesterUserId = x.RequesterUserId,
                RequesterUsername = users.TryGetValue(x.RequesterUserId, out var rq) ? rq.Username : $"User#{x.RequesterUserId}",
                AddresseeUserId = x.AddresseeUserId,
                AddresseeUsername = users.TryGetValue(x.AddresseeUserId, out var ad) ? ad.Username : $"User#{x.AddresseeUserId}",
                Status = (int)x.Status,
                DateCreatedUtc = x.DateCreatedUtc
            }).ToList();

        var outgoing = links.Where(x => x.Status == FriendRequestStatus.Pending && x.RequesterUserId == userId)
            .Select(x => new FriendRequestView
            {
                FriendLinkId = x.FriendLinkId,
                RequesterUserId = x.RequesterUserId,
                RequesterUsername = users.TryGetValue(x.RequesterUserId, out var rq) ? rq.Username : $"User#{x.RequesterUserId}",
                AddresseeUserId = x.AddresseeUserId,
                AddresseeUsername = users.TryGetValue(x.AddresseeUserId, out var ad) ? ad.Username : $"User#{x.AddresseeUserId}",
                Status = (int)x.Status,
                DateCreatedUtc = x.DateCreatedUtc
            }).ToList();

        return Results.Ok(new FriendsOverviewResponse { Friends = friends, Incoming = incoming, Outgoing = outgoing });
    }
    catch
    {
        return Results.Ok(new FriendsOverviewResponse());
    }
}).RequireAuthorization();

app.MapPost("/api/friends/request", async (HttpContext http, SendFriendRequestRequest req, AppDbContext db) =>
{
    var idStr = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!int.TryParse(idStr, out var userId)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Target)) return Results.BadRequest("Target is required.");

    var target = req.Target.Trim();
    var targetUser = await db.Users.FirstOrDefaultAsync(x => x.DateDeletedUtc == null && (x.Username == target || x.Email == target));
    if (targetUser is null) return Results.NotFound();
    if (targetUser.AppUserId == userId) return Results.BadRequest("You cannot friend yourself.");

    var a = Math.Min(userId, targetUser.AppUserId);
    var b = Math.Max(userId, targetUser.AppUserId);

    var existing = await db.FriendLinks.FirstOrDefaultAsync(x =>
        (x.RequesterUserId == a && x.AddresseeUserId == b) ||
        (x.RequesterUserId == b && x.AddresseeUserId == a));

    if (existing is not null)
    {
        if (existing.Status == FriendRequestStatus.Accepted) return Results.BadRequest("Already friends.");
        if (existing.Status == FriendRequestStatus.Pending) return Results.BadRequest("Request already pending.");

        existing.RequesterUserId = userId;
        existing.AddresseeUserId = targetUser.AppUserId;
        existing.Status = FriendRequestStatus.Pending;
        existing.DateCreatedUtc = DateTime.UtcNow;
        existing.DateRespondedUtc = null;
        await db.SaveChangesAsync();
        return Results.Ok();
    }

    db.FriendLinks.Add(new FriendLink
    {
        RequesterUserId = userId,
        AddresseeUserId = targetUser.AppUserId,
        Status = FriendRequestStatus.Pending,
        DateCreatedUtc = DateTime.UtcNow
    });
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

app.MapPost("/api/friends/respond", async (HttpContext http, RespondFriendRequestRequest req, AppDbContext db) =>
{
    var idStr = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!int.TryParse(idStr, out var userId)) return Results.Unauthorized();

    var row = await db.FriendLinks.FirstOrDefaultAsync(x => x.FriendLinkId == req.FriendLinkId);
    if (row is null) return Results.NotFound();
    if (row.AddresseeUserId != userId) return Results.Forbid();
    if (row.Status != FriendRequestStatus.Pending) return Results.BadRequest("Request already handled.");

    row.Status = req.Accept ? FriendRequestStatus.Accepted : FriendRequestStatus.Declined;
    row.DateRespondedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

app.MapGet("/api/me/preferences", async (HttpContext http, AppDbContext db) =>
{
    var idStr = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!int.TryParse(idStr, out var userId)) return Results.Unauthorized();

    var u = await db.Users.FirstOrDefaultAsync(x => x.AppUserId == userId && x.DateDeletedUtc == null);
    if (u is null) return Results.Unauthorized();

    return Results.Ok(new
    {
        theme = u.ThemePreference,
        campaignNavExpanded = u.CampaignNavExpanded,
        compendiumNavExpanded = u.CompendiumNavExpanded
    });
}).RequireAuthorization();

app.MapPost("/api/me/preferences", async (HttpContext http, System.Text.Json.JsonElement body, AppDbContext db) =>
{
    var idStr = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!int.TryParse(idStr, out var userId)) return Results.Unauthorized();

    var u = await db.Users.FirstOrDefaultAsync(x => x.AppUserId == userId && x.DateDeletedUtc == null);
    if (u is null) return Results.Unauthorized();

    if (body.TryGetProperty("theme", out var t) && t.ValueKind != System.Text.Json.JsonValueKind.Null)
        u.ThemePreference = t.GetString();

    if (body.TryGetProperty("campaignNavExpanded", out var c))
        u.CampaignNavExpanded = c.ValueKind == System.Text.Json.JsonValueKind.Null ? null : c.GetBoolean();

    if (body.TryGetProperty("compendiumNavExpanded", out var m))
        u.CompendiumNavExpanded = m.ValueKind == System.Text.Json.JsonValueKind.Null ? null : m.GetBoolean();

    u.DateModifiedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok();
}).RequireAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();




static int? GetUserId(HttpContext http)
{
    var id = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    return int.TryParse(id, out var v) ? v : null;
}

static bool IsAdmin(HttpContext http) => http.User.IsInRole("Admin");

static string? FormatCreatureSpeed(int? walkSpeed, int? flySpeed, int? swimSpeed, int? climbSpeed, int? burrowSpeed, string? legacySpeed = null)
{
    List<string> parts = new();
    if (walkSpeed.HasValue) parts.Add($"{walkSpeed.Value} ft.");
    if (flySpeed.HasValue) parts.Add($"fly {flySpeed.Value} ft.");
    if (swimSpeed.HasValue) parts.Add($"swim {swimSpeed.Value} ft.");
    if (climbSpeed.HasValue) parts.Add($"climb {climbSpeed.Value} ft.");
    if (burrowSpeed.HasValue) parts.Add($"burrow {burrowSpeed.Value} ft.");
    if (parts.Count > 0) return string.Join(", ", parts);
    return string.IsNullOrWhiteSpace(legacySpeed) ? null : legacySpeed.Trim();
}

static CreatureResponse ToCreatureResponse(Creature row, string? ownerUsername, bool ownerIsAdmin, bool userCanEdit)
{
    var subtypeNames = row.CreatureSubtypeLinks
        .Where(x => x.CreatureSubtype is not null)
        .OrderBy(x => x.SortOrder)
        .ThenBy(x => x.CreatureSubtype!.Name)
        .Select(x => x.CreatureSubtype!.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    return new CreatureResponse
    {
        CreatureId = row.CreatureId,
        IsSystem = row.IsSystem,
        OwnerAppUserId = row.OwnerAppUserId,
        OwnerUsername = ownerUsername,
        OwnerIsAdmin = ownerIsAdmin,
        UserCanEdit = userCanEdit,
        Name = row.Name,
        Description = row.Description,
        Size = row.Size,
        CreatureTypeId = row.CreatureTypeId,
        CreatureType = row.Type?.Name ?? row.CreatureType,
        CreatureSubtype = subtypeNames.FirstOrDefault() ?? row.CreatureSubtype,
        CreatureSubtypeIds = row.CreatureSubtypeLinks.OrderBy(x => x.SortOrder).Select(x => x.CreatureSubtypeId).Distinct().ToList(),
        CreatureSubtypes = subtypeNames,
        ArmorClass = row.ArmorClass,
        ArmorClassNotes = row.ArmorClassNotes,
        HitPoints = row.HitPoints,
        HitDice = row.HitDice,
        InitiativeModifier = row.InitiativeModifier,
        Speed = FormatCreatureSpeed(row.WalkSpeed, row.FlySpeed, row.SwimSpeed, row.ClimbSpeed, row.BurrowSpeed, row.Speed),
        WalkSpeed = row.WalkSpeed,
        FlySpeed = row.FlySpeed,
        SwimSpeed = row.SwimSpeed,
        ClimbSpeed = row.ClimbSpeed,
        BurrowSpeed = row.BurrowSpeed,
        ChallengeRating = row.ChallengeRating,
        ExperiencePoints = row.ExperiencePoints,
        PassivePerception = row.PassivePerception,
        BlindsightRange = row.BlindsightRange,
        DarkvisionRange = row.DarkvisionRange,
        TremorsenseRange = row.TremorsenseRange,
        TruesightRange = row.TruesightRange,
        OtherSenses = row.OtherSenses,
        Languages = row.Languages,
        UnderstandsButCannotSpeak = row.UnderstandsButCannotSpeak,
        Traits = BuildCreatureEntryDtos(row.TraitList, row.Traits),
        Actions = BuildCreatureEntryDtos(row.ActionList, row.Actions),
        Strength = row.Strength,
        Dexterity = row.Dexterity,
        Constitution = row.Constitution,
        Intelligence = row.Intelligence,
        Wisdom = row.Wisdom,
        Charisma = row.Charisma
    };
}

static string NormalizeCreatureTitle(string value)
{
    var trimmed = (value ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(trimmed)) return string.Empty;

    var textInfo = CultureInfo.InvariantCulture.TextInfo;
    var words = trimmed
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(word => string.Join('-', word
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => string.IsNullOrWhiteSpace(part) ? part : textInfo.ToTitleCase(part.ToLowerInvariant()))));

    return string.Join(" ", words);
}

static string? NormalizeCreatureSize(string? value)
{
    var text = value?.Trim();
    return string.IsNullOrWhiteSpace(text) ? null : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant());
}

static string? NormalizeCreatureType(string? value)
{
    var text = value?.Trim();
    return string.IsNullOrWhiteSpace(text) ? null : text.ToLowerInvariant();
}

static string? NormalizeCreatureSubtype(string? value)
{
    var text = value?.Trim();
    return string.IsNullOrWhiteSpace(text) ? null : text.ToLowerInvariant();
}

static async Task ResolveCreatureTaxonomyAsync(AppDbContext db, Creature row, UpsertCreatureRequest req)
{
    var normalizedTypeName = NormalizeCreatureType(req.CreatureType);
    var normalizedSubtypeNames = req.CreatureSubtypeIds?.Count > 0
        ? new List<string>()
        : (new[] { NormalizeCreatureSubtype(req.CreatureSubtype) }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList());

    CreatureType? type = null;
    if (req.CreatureTypeId.HasValue)
    {
        type = await db.CreatureTypes.FirstOrDefaultAsync(x => x.CreatureTypeId == req.CreatureTypeId.Value && x.IsActive);
    }
    else if (!string.IsNullOrWhiteSpace(normalizedTypeName))
    {
        type = await db.CreatureTypes.FirstOrDefaultAsync(x => x.Key == normalizedTypeName);
    }

    row.CreatureTypeId = type?.CreatureTypeId;
    row.CreatureType = type?.Key ?? normalizedTypeName;
    row.Type = type;

    var subtypeIds = req.CreatureSubtypeIds?.Distinct().ToList() ?? new List<int>();
    var subtypeEntities = subtypeIds.Count > 0
        ? await db.CreatureSubtypes.Where(x => subtypeIds.Contains(x.CreatureSubtypeId) && x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync()
        : normalizedSubtypeNames.Count > 0
            ? await db.CreatureSubtypes.Where(x => normalizedSubtypeNames.Contains(x.Key) && x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync()
            : new List<CreatureSubtype>();

    row.CreatureSubtypeLinks.Clear();
    var subtypeSort = 0;
    foreach (var subtype in subtypeEntities)
    {
        row.CreatureSubtypeLinks.Add(new CreatureCreatureSubtype { CreatureSubtypeId = subtype.CreatureSubtypeId, CreatureSubtype = subtype, SortOrder = subtypeSort++ });
    }

    var subtypeNames = subtypeEntities.Select(x => x.Key).ToList();
    row.CreatureSubtype = subtypeNames.FirstOrDefault() ?? normalizedSubtypeNames.FirstOrDefault();
}

static async Task EnsureCreatureTaxonomyAsync(AppDbContext db)
{
    var typeSeeds = new[]
    {
        new { Key = "aberration", Name = "aberration" }, new { Key = "beast", Name = "beast" }, new { Key = "celestial", Name = "celestial" },
        new { Key = "construct", Name = "construct" }, new { Key = "dragon", Name = "dragon" }, new { Key = "elemental", Name = "elemental" },
        new { Key = "fey", Name = "fey" }, new { Key = "fiend", Name = "fiend" }, new { Key = "giant", Name = "giant" },
        new { Key = "humanoid", Name = "humanoid" }, new { Key = "monstrosity", Name = "monstrosity" }, new { Key = "ooze", Name = "ooze" },
        new { Key = "plant", Name = "plant" }, new { Key = "undead", Name = "undead" }
    };

    if (!await db.CreatureTypes.AnyAsync())
    {
        for (var i = 0; i < typeSeeds.Length; i++)
            db.CreatureTypes.Add(new CreatureType { Key = typeSeeds[i].Key, Name = typeSeeds[i].Name, DisplayOrder = i });
        await db.SaveChangesAsync();
    }

    var typeMap = await db.CreatureTypes.ToDictionaryAsync(x => x.Key, x => x);
    var subtypeSeeds = new List<(string Key, string Name, string? TypeKey)>
    {
        ("angel", "angel", "celestial"), ("any race", "any race", "humanoid"),
        ("aquatic", "aquatic", null), ("archon", "archon", "celestial"),
        ("demon", "demon", "fiend"), ("devil", "devil", "fiend"),
        ("dragonborn", "dragonborn", "humanoid"), ("dwarf", "dwarf", "humanoid"),
        ("elf", "elf", "humanoid"), ("gith", "gith", "humanoid"),
        ("gnoll", "gnoll", "humanoid"), ("gnome", "gnome", "humanoid"),
        ("goblinoid", "goblinoid", "humanoid"), ("halfling", "halfling", "humanoid"),
        ("human", "human", "humanoid"), ("kenku", "kenku", "humanoid"),
        ("kobold", "kobold", "humanoid"), ("lizardfolk", "lizardfolk", "humanoid"),
        ("lycanthrope", "lycanthrope", null), ("mind flayer", "mind flayer", "aberration"),
        ("orc", "orc", "humanoid"), ("sahuagin", "sahuagin", "humanoid"),
        ("shapechanger", "shapechanger", null), ("skeleton", "skeleton", "undead"),
        ("spirit", "spirit", null), ("titan", "titan", "giant"),
        ("vampire", "vampire", "undead"), ("warforged", "warforged", "construct"),
        ("yuan-ti", "yuan-ti", "humanoid"), ("zombie", "zombie", "undead")
    };

    var existingSubtypeKeys = await db.CreatureSubtypes.Select(x => x.Key).ToListAsync();
    var missingSubtypeSeeds = subtypeSeeds.Where(x => !existingSubtypeKeys.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).ToList();
    if (missingSubtypeSeeds.Count > 0)
    {
        var displayOrder = await db.CreatureSubtypes.CountAsync();
        foreach (var seed in missingSubtypeSeeds)
        {
            typeMap.TryGetValue(seed.TypeKey ?? string.Empty, out var parentType);
            db.CreatureSubtypes.Add(new CreatureSubtype { Key = seed.Key, Name = seed.Name, CreatureTypeId = parentType?.CreatureTypeId, DisplayOrder = displayOrder++ });
        }
        await db.SaveChangesAsync();
    }

    var subtypesByKey = await db.CreatureSubtypes.ToDictionaryAsync(x => x.Key, x => x);
    var creaturesNeedingType = await db.Creatures.Where(x => x.DateDeletedUtc == null && x.CreatureTypeId == null && x.CreatureType != null).ToListAsync();
    foreach (var creature in creaturesNeedingType)
    {
        var normalizedType = NormalizeCreatureType(creature.CreatureType);
        if (normalizedType is not null && typeMap.TryGetValue(normalizedType, out var creatureType))
            creature.CreatureTypeId = creatureType.CreatureTypeId;
        creature.CreatureType = normalizedType;
        creature.CreatureSubtype = NormalizeCreatureSubtype(creature.CreatureSubtype);
    }
    await db.SaveChangesAsync();

    var linkedCreatureIds = await db.CreatureCreatureSubtypes.Select(x => x.CreatureId).Distinct().ToListAsync();
    var creaturesNeedingSubtypeMigration = await db.Creatures
        .Where(x => x.DateDeletedUtc == null && x.CreatureSubtype != null && !linkedCreatureIds.Contains(x.CreatureId))
        .ToListAsync();
    foreach (var creature in creaturesNeedingSubtypeMigration)
    {
        var normalizedSubtype = NormalizeCreatureSubtype(creature.CreatureSubtype);
        if (normalizedSubtype is not null && subtypesByKey.TryGetValue(normalizedSubtype, out var subtype))
            db.CreatureCreatureSubtypes.Add(new CreatureCreatureSubtype { CreatureId = creature.CreatureId, CreatureSubtypeId = subtype.CreatureSubtypeId, SortOrder = 0 });
    }
    await db.SaveChangesAsync();
}

static string? NormalizeCreatureArmorClassNotes(string? value)
{
    var text = value?.Trim();
    return string.IsNullOrWhiteSpace(text) ? null : text;
}

static string? NormalizeCreatureHitDice(string? value)
{
    var text = value?.Trim();
    return string.IsNullOrWhiteSpace(text) ? null : text;
}

static string? NormalizeCreatureLanguages(string? languages)
{
    var items = (languages ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    return items.Count == 0 ? null : string.Join(", ", items);
}

static string? NormalizeCreatureOtherSenses(string? value)
{
    var text = value?.Trim();
    return string.IsNullOrWhiteSpace(text) ? null : text;
}

static List<CreatureEntryDto> BuildCreatureEntryDtos<T>(IEnumerable<T> rows, string? legacyText) where T : class
{
    var items = rows.Select(x => x switch
    {
        CreatureTrait trait => new CreatureEntryDto { Name = trait.Name, Description = trait.Description, SortOrder = trait.SortOrder },
        CreatureAction action => new CreatureEntryDto { Name = action.Name, Description = action.Description, SortOrder = action.SortOrder },
        _ => throw new InvalidOperationException("Unsupported creature entry type.")
    }).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToList();

    if (items.Count == 0 && !string.IsNullOrWhiteSpace(legacyText))
        items.Add(new CreatureEntryDto { Name = "Legacy", Description = legacyText.Trim(), SortOrder = 0 });

    return items;
}

static void ApplyCreatureEntries(Creature row, UpsertCreatureRequest req)
{
    row.TraitList.Clear();
    foreach (var entry in NormalizeCreatureEntries(req.Traits))
        row.TraitList.Add(new CreatureTrait { Name = entry.Name, Description = entry.Description, SortOrder = entry.SortOrder });

    row.ActionList.Clear();
    foreach (var entry in NormalizeCreatureEntries(req.Actions))
        row.ActionList.Add(new CreatureAction { Name = entry.Name, Description = entry.Description, SortOrder = entry.SortOrder });
}

static List<CreatureEntryDto> NormalizeCreatureEntries(IEnumerable<CreatureEntryDto>? entries)
{
    return (entries ?? Enumerable.Empty<CreatureEntryDto>())
        .Where(x => !string.IsNullOrWhiteSpace(x.Name) || !string.IsNullOrWhiteSpace(x.Description))
        .Select((x, index) => new CreatureEntryDto
        {
            Name = string.IsNullOrWhiteSpace(x.Name) ? $"Entry {index + 1}" : NormalizeCreatureTitle(x.Name),
            Description = string.IsNullOrWhiteSpace(x.Description) ? null : x.Description.Trim(),
            SortOrder = x.SortOrder == 0 ? index : x.SortOrder
        })
        .ToList();
}

static async Task<bool> CanAccessEncounter(HttpContext http, AppDbContext db, Encounter row, bool requireEdit)
{
    var userId = GetUserId(http);
    if (userId is null) return false;
    if (IsAdmin(http)) return true;
    if (row.CampaignId == 0) return true;

    var campaign = await db.Campaigns.FirstOrDefaultAsync(x => x.CampaignId == row.CampaignId && x.DateDeletedUtc == null);
    if (campaign is null) return false;
    if (campaign.OwnerAppUserId == userId.Value) return true;

    return requireEdit
        ? await db.CampaignShares.AnyAsync(x => x.CampaignId == row.CampaignId && x.SharedWithUserId == userId.Value && x.Permission == SharePermission.Edit)
        : await db.CampaignShares.AnyAsync(x => x.CampaignId == row.CampaignId && x.SharedWithUserId == userId.Value);
}

static CharacterResponse ToCharacterResponse(Character row, string? ownerUsername)
{
    var skills = BuildCharacterSkillResponses(row).OrderBy(x => x.DisplayOrder).ToList();
    return new CharacterResponse
    {
        CharacterId = row.CharacterId,
        CampaignId = row.CampaignId == 0 ? null : row.CampaignId,
        PartyId = row.PartyId == 0 ? null : row.PartyId,
        CharacterType = row.CharacterType,
        Name = row.Name,
        OwnerAppUserId = row.OwnerAppUserId,
        OwnerUsername = ownerUsername,
        PlayerName = row.PlayerName,
        ArmorClass = row.ArmorClass,
        HitPointsCurrent = row.HitPointsCurrent,
        HitPointsMax = row.HitPointsMax,
        TempHitPoints = row.TempHitPoints,
        InitiativeModifier = row.InitiativeModifier,
        Speed = row.Speed,
        Strength = row.Strength,
        Dexterity = row.Dexterity,
        Constitution = row.Constitution,
        Intelligence = row.Intelligence,
        Wisdom = row.Wisdom,
        Charisma = row.Charisma,
        ProficiencyBonus = row.ProficiencyBonus,
        Level = row.Level,
        ClassName = row.ClassName,
        SubclassName = row.SubclassName,
        RaceName = row.RaceName,
        SubraceName = row.SubraceName,
        PassivePerception = ResolvePassiveScore(skills, "perception"),
        PassiveInvestigation = ResolvePassiveScore(skills, "investigation"),
        PassiveInsight = ResolvePassiveScore(skills, "insight"),
        Skills = skills,
        Conditions = row.Conditions,
        Notes = row.Notes,
        DateCreatedUtc = row.DateCreatedUtc,
        DateModifiedUtc = row.DateModifiedUtc
    };
}

static List<CharacterSkillResponse> BuildCharacterSkillResponses(Character row)
{
    var proficiencyBonus = ResolveProficiencyBonus(row);
    return row.Skills
        .Where(x => x.Skill is not null && x.Skill.IsActive)
        .Select(x =>
        {
            var abilityModifier = GetAbilityModifier(row, x.Skill!.Ability);
            var proficiencyContribution = x.HasExpertise ? proficiencyBonus * 2 : (x.IsProficient ? proficiencyBonus : 0);
            var total = abilityModifier + proficiencyContribution + (x.BonusOverride ?? 0);
            return new CharacterSkillResponse
            {
                SkillId = x.SkillId,
                Key = x.Skill.Key,
                Name = x.Skill.Name,
                Ability = x.Skill.Ability,
                DisplayOrder = x.Skill.DisplayOrder,
                IsProficient = x.IsProficient,
                HasExpertise = x.HasExpertise,
                BonusOverride = x.BonusOverride,
                AbilityModifier = abilityModifier,
                ProficiencyContribution = proficiencyContribution,
                TotalModifier = total
            };
        })
        .ToList();
}

static List<CharacterSkill> BuildCharacterSkills(IEnumerable<UpsertCharacterSkillRequest>? skills)
{
    return (skills ?? Enumerable.Empty<UpsertCharacterSkillRequest>())
        .GroupBy(x => x.SkillId)
        .Select(g => g.Last())
        .Where(x => x.SkillId > 0)
        .Select(x => new CharacterSkill
        {
            SkillId = x.SkillId,
            IsProficient = x.IsProficient,
            HasExpertise = x.HasExpertise,
            BonusOverride = x.BonusOverride
        })
        .ToList();
}

static void SyncCharacterSkills(Character row, IEnumerable<UpsertCharacterSkillRequest>? requestedSkills)
{
    var requestedBySkillId = (requestedSkills ?? Enumerable.Empty<UpsertCharacterSkillRequest>())
        .GroupBy(x => x.SkillId)
        .Select(g => g.Last())
        .Where(x => x.SkillId > 0)
        .ToDictionary(x => x.SkillId, x => x);

    foreach (var skill in row.Skills)
    {
        if (!requestedBySkillId.TryGetValue(skill.SkillId, out var requested)) continue;
        skill.IsProficient = requested.IsProficient;
        skill.HasExpertise = requested.HasExpertise;
        skill.BonusOverride = requested.BonusOverride;
    }
}

static int ResolveProficiencyBonus(Character row)
{
    if (row.ProficiencyBonus.HasValue && row.ProficiencyBonus.Value > 0) return row.ProficiencyBonus.Value;
    var level = row.Level ?? 1;
    if (level <= 4) return 2;
    if (level <= 8) return 3;
    if (level <= 12) return 4;
    if (level <= 16) return 5;
    return 6;
}

static int GetAbilityModifier(Character row, AbilityScoreType ability)
{
    var score = ability switch
    {
        AbilityScoreType.Strength => row.Strength,
        AbilityScoreType.Dexterity => row.Dexterity,
        AbilityScoreType.Constitution => row.Constitution,
        AbilityScoreType.Intelligence => row.Intelligence,
        AbilityScoreType.Wisdom => row.Wisdom,
        AbilityScoreType.Charisma => row.Charisma,
        _ => null
    };

    return score.HasValue ? (int)Math.Floor((score.Value - 10) / 2.0) : 0;
}

static int? ResolvePassiveScore(IEnumerable<CharacterSkillResponse> skills, string key)
{
    var skill = skills.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
    return skill is null ? null : 10 + skill.TotalModifier;
}

static void EnsureSkillSchema(AppDbContext db, bool isSqliteProvider)
{
    if (isSqliteProvider)
    {
        ExecuteSqlStatements(db,
            "CREATE TABLE IF NOT EXISTS Skills (SkillId INTEGER NOT NULL CONSTRAINT PK_Skills PRIMARY KEY AUTOINCREMENT, Key TEXT NOT NULL, Name TEXT NOT NULL, Ability INTEGER NOT NULL, DisplayOrder INTEGER NOT NULL, IsActive INTEGER NOT NULL DEFAULT 1);",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Skills_Key ON Skills (Key);",
            "CREATE INDEX IF NOT EXISTS IX_Skills_DisplayOrder ON Skills (DisplayOrder);",
            "CREATE TABLE IF NOT EXISTS CharacterSkills (CharacterSkillId INTEGER NOT NULL CONSTRAINT PK_CharacterSkills PRIMARY KEY AUTOINCREMENT, CharacterId INTEGER NOT NULL, SkillId INTEGER NOT NULL, IsProficient INTEGER NOT NULL DEFAULT 0, HasExpertise INTEGER NOT NULL DEFAULT 0, BonusOverride INTEGER NULL, CONSTRAINT FK_CharacterSkills_Characters_CharacterId FOREIGN KEY (CharacterId) REFERENCES Characters (CharacterId) ON DELETE CASCADE, CONSTRAINT FK_CharacterSkills_Skills_SkillId FOREIGN KEY (SkillId) REFERENCES Skills (SkillId) ON DELETE CASCADE);",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_CharacterSkills_CharacterId_SkillId ON CharacterSkills (CharacterId, SkillId);"
        );
    }
    else
    {
        ExecuteSqlStatements(db,
            "CREATE TABLE IF NOT EXISTS \"Skills\" (\"SkillId\" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, \"Key\" text NOT NULL, \"Name\" text NOT NULL, \"Ability\" integer NOT NULL, \"DisplayOrder\" integer NOT NULL, \"IsActive\" boolean NOT NULL DEFAULT true);",
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Skills_Key\" ON \"Skills\" (\"Key\");",
            "CREATE INDEX IF NOT EXISTS \"IX_Skills_DisplayOrder\" ON \"Skills\" (\"DisplayOrder\");",
            "CREATE TABLE IF NOT EXISTS \"CharacterSkills\" (\"CharacterSkillId\" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, \"CharacterId\" integer NOT NULL, \"SkillId\" integer NOT NULL, \"IsProficient\" boolean NOT NULL DEFAULT false, \"HasExpertise\" boolean NOT NULL DEFAULT false, \"BonusOverride\" integer NULL);",
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_CharacterSkills_CharacterId_SkillId\" ON \"CharacterSkills\" (\"CharacterId\", \"SkillId\");"
        );
    }
}

static async Task SeedSkillsAsync(AppDbContext db)
{
    var seeds = new[]
    {
        new Skill { Key = "acrobatics", Name = "Acrobatics", Ability = AbilityScoreType.Dexterity, DisplayOrder = 1 },
        new Skill { Key = "animal-handling", Name = "Animal Handling", Ability = AbilityScoreType.Wisdom, DisplayOrder = 2 },
        new Skill { Key = "arcana", Name = "Arcana", Ability = AbilityScoreType.Intelligence, DisplayOrder = 3 },
        new Skill { Key = "athletics", Name = "Athletics", Ability = AbilityScoreType.Strength, DisplayOrder = 4 },
        new Skill { Key = "deception", Name = "Deception", Ability = AbilityScoreType.Charisma, DisplayOrder = 5 },
        new Skill { Key = "history", Name = "History", Ability = AbilityScoreType.Intelligence, DisplayOrder = 6 },
        new Skill { Key = "insight", Name = "Insight", Ability = AbilityScoreType.Wisdom, DisplayOrder = 7 },
        new Skill { Key = "intimidation", Name = "Intimidation", Ability = AbilityScoreType.Charisma, DisplayOrder = 8 },
        new Skill { Key = "investigation", Name = "Investigation", Ability = AbilityScoreType.Intelligence, DisplayOrder = 9 },
        new Skill { Key = "medicine", Name = "Medicine", Ability = AbilityScoreType.Wisdom, DisplayOrder = 10 },
        new Skill { Key = "nature", Name = "Nature", Ability = AbilityScoreType.Intelligence, DisplayOrder = 11 },
        new Skill { Key = "perception", Name = "Perception", Ability = AbilityScoreType.Wisdom, DisplayOrder = 12 },
        new Skill { Key = "performance", Name = "Performance", Ability = AbilityScoreType.Charisma, DisplayOrder = 13 },
        new Skill { Key = "persuasion", Name = "Persuasion", Ability = AbilityScoreType.Charisma, DisplayOrder = 14 },
        new Skill { Key = "religion", Name = "Religion", Ability = AbilityScoreType.Intelligence, DisplayOrder = 15 },
        new Skill { Key = "sleight-of-hand", Name = "Sleight of Hand", Ability = AbilityScoreType.Dexterity, DisplayOrder = 16 },
        new Skill { Key = "stealth", Name = "Stealth", Ability = AbilityScoreType.Dexterity, DisplayOrder = 17 },
        new Skill { Key = "survival", Name = "Survival", Ability = AbilityScoreType.Wisdom, DisplayOrder = 18 }
    };

    foreach (var seed in seeds)
    {
        var existing = await db.Skills.FirstOrDefaultAsync(x => x.Key == seed.Key);
        if (existing is null)
        {
            db.Skills.Add(seed);
        }
        else
        {
            existing.Name = seed.Name;
            existing.Ability = seed.Ability;
            existing.DisplayOrder = seed.DisplayOrder;
            existing.IsActive = true;
        }
    }

    await db.SaveChangesAsync();
}

static async Task EnsureCharacterSkillRowsAsync(AppDbContext db)
{
    var skillIds = await db.Skills.Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).Select(x => x.SkillId).ToListAsync();
    if (skillIds.Count == 0) return;

    var characterIds = await db.Characters.Where(x => x.DateDeletedUtc == null).Select(x => x.CharacterId).ToListAsync();
    foreach (var characterId in characterIds)
    {
        await EnsureCharacterSkillRowsForCharacterAsync(db, characterId, skillIds);
    }
}

static async Task EnsureCharacterSkillRowsForCharacterAsync(AppDbContext db, int characterId, List<int>? skillIds = null)
{
    skillIds ??= await db.Skills.Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).Select(x => x.SkillId).ToListAsync();
    if (skillIds.Count == 0) return;

    var existingSkillIds = await db.CharacterSkills.Where(x => x.CharacterId == characterId).Select(x => x.SkillId).ToListAsync();
    var missingSkillIds = skillIds.Except(existingSkillIds).ToList();
    if (missingSkillIds.Count == 0) return;

    foreach (var skillId in missingSkillIds)
    {
        db.CharacterSkills.Add(new CharacterSkill { CharacterId = characterId, SkillId = skillId });
    }

    await db.SaveChangesAsync();
}

static void ApplyItem(UpsertItemRequest req, Item row)
{
    row.Name = TitleNormalization.ToPascalTitle(req.Name);
    row.Description = req.Description;
    row.ItemType = Enum.IsDefined(typeof(ItemType), req.ItemType) ? (ItemType)req.ItemType : ItemType.Other;
    row.Rarity = Enum.IsDefined(typeof(ItemRarity), req.Rarity) ? (ItemRarity)req.Rarity : ItemRarity.Common;
    row.Weight = req.Weight;
    row.CostAmount = req.CostAmount;
    row.CostCurrency = req.CostCurrency;
    row.RequiresAttunement = req.RequiresAttunement;
    row.SourceType = req.SourceType;
    row.Source = req.Source;
    row.Tags = req.Tags;
    row.WeaponCategory = req.WeaponCategory;
    row.DamageDice = req.DamageDice;
    row.DamageType = req.DamageType;
    row.Properties = req.Properties;
    row.RangeNormal = req.RangeNormal;
    row.RangeMax = req.RangeMax;
    row.IsMagicWeapon = req.IsMagicWeapon;
    row.AttackBonus = req.AttackBonus;
    row.DamageBonus = req.DamageBonus;
    row.ArmorCategory = req.ArmorCategory;
    row.ArmorClassBase = req.ArmorClassBase;
    row.DexCap = req.DexCap;
    row.StrengthRequirement = req.StrengthRequirement;
    row.StealthDisadvantage = req.StealthDisadvantage;
    row.IsMagicArmor = req.IsMagicArmor;
    row.ArmorBonus = req.ArmorBonus;
    row.Charges = req.Charges;
    row.MaxCharges = req.MaxCharges;
    row.RechargeRule = req.RechargeRule;
    row.ConsumableEffect = req.ConsumableEffect;
    row.Quantity = req.Quantity;
    row.Stackable = req.Stackable;
    row.Notes = req.Notes;
}

static void ExecuteSqlStatements(AppDbContext db, params string[] statements)
{
    foreach (var statement in statements)
    {
        db.Database.ExecuteSqlRaw(statement);
    }
}

static void TryExecuteSqlStatements(AppDbContext db, params string[] statements)
{
    foreach (var statement in statements)
    {
        try
        {
            db.Database.ExecuteSqlRaw(statement);
        }
        catch
        {
        }
    }
}

static LocalSeqSettings LoadLocalSeqSettings(string contentRootPath)
{
    var path = Path.Combine(contentRootPath, "appsettings.Local.json");
    if (!File.Exists(path)) return new LocalSeqSettings(null, null, null);

    try
    {
        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
        var seq = root?["Logging"]?["Seq"];
        return new LocalSeqSettings(
            seq?["ServerUrl"]?.GetValue<string>(),
            seq?["ApiKey"]?.GetValue<string>(),
            seq?["AppName"]?.GetValue<string>()
        );
    }
    catch
    {
        return new LocalSeqSettings(null, null, null);
    }
}

static void SaveLocalSeqSettings(string contentRootPath, LocalSeqSettings settings)
{
    var path = Path.Combine(contentRootPath, "appsettings.Local.json");

    var root = File.Exists(path)
        ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject()
        : new JsonObject();

    var logging = root["Logging"] as JsonObject ?? new JsonObject();
    var seq = logging["Seq"] as JsonObject ?? new JsonObject();

    seq["ServerUrl"] = string.IsNullOrWhiteSpace(settings.ServerUrl) ? null : settings.ServerUrl;
    seq["ApiKey"] = string.IsNullOrWhiteSpace(settings.ApiKey) ? null : settings.ApiKey;
    seq["AppName"] = string.IsNullOrWhiteSpace(settings.AppName) ? "RuleForge" : settings.AppName;

    logging["Seq"] = seq;
    root["Logging"] = logging;

    File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}

static string SummarizeApiErrorBody(string? responseText)
{
    if (string.IsNullOrWhiteSpace(responseText)) return string.Empty;

    try
    {
        using var document = JsonDocument.Parse(responseText);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return TruncateForLog(responseText);
        }

        var root = document.RootElement;
        var pieces = new List<string>();

        if (root.TryGetProperty("Code", out var code) && code.ValueKind == JsonValueKind.String)
            pieces.Add($"Code={code.GetString()}");
        if (root.TryGetProperty("Message", out var message) && message.ValueKind == JsonValueKind.String)
            pieces.Add($"Message={message.GetString()}");
        if (root.TryGetProperty("TraceId", out var traceId) && traceId.ValueKind == JsonValueKind.String)
            pieces.Add($"TraceId={traceId.GetString()}");
        if (root.TryGetProperty("Error", out var error) && error.ValueKind == JsonValueKind.String)
            pieces.Add($"Error={error.GetString()}");
        if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
            pieces.Add($"Title={title.GetString()}");
        if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            pieces.Add($"Detail={detail.GetString()}");

        return pieces.Count > 0 ? TruncateForLog(string.Join(" | ", pieces)) : TruncateForLog(responseText);
    }
    catch
    {
        return TruncateForLog(responseText);
    }
}

static string TruncateForLog(string? value, int maxLength = 800)
{
    if (string.IsNullOrWhiteSpace(value)) return string.Empty;

    var normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
    return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "…";
}

app.Run();

sealed record ApiError(string Code, string Message, string TraceId);
sealed record MessageResponse(string Message);
sealed record SeqLoggingSettingsUpdateRequest(string? ServerUrl, string? ApiKey, string? ApiKeyMode, string? AppName);
sealed record SeqLoggingSettingsResponse(string EffectiveServerUrl, string EffectiveApiKeyMasked, string EffectiveAppName, string LocalServerUrl, string LocalApiKeyMasked, string LocalAppName, string ExampleLocalUrl, string ExampleHostedUrl, string Notes);
sealed record SeqLoggingTestResponse(string Message, string ServerUrl, string AppName, int StatusCode);
sealed record LocalSeqSettings(string? ServerUrl, string? ApiKey, string? AppName);
