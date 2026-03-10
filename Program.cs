using Microsoft.EntityFrameworkCore;
using RuleForge.Contracts.Characters;
using RuleForge.Data;
using RuleForge.Domain.Characters;
using MudBlazor.Services;
using RuleForge.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddHttpClient();
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite("Data Source=ruleforge.db"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/api/characters", async (AppDbContext db) =>
{
    var rows = await db.Characters
        .Where(x => x.DateDeletedUtc == null)
        .OrderBy(x => x.Name)
        .Select(x => new CharacterResponse
        {
            CharacterId = x.CharacterId,
            CampaignId = x.CampaignId,
            CharacterType = x.CharacterType,
            Name = x.Name,
            OwnerAppUserId = x.OwnerAppUserId,
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

app.MapPost("/api/characters", async (UpsertCharacterRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("Name is required.");

    var row = new Character
    {
        CampaignId = req.CampaignId,
        CharacterType = req.CharacterType,
        Name = req.Name.Trim(),
        OwnerAppUserId = req.OwnerAppUserId,
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


app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
