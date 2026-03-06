using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuleForge.Migrations
{
    /// <inheritdoc />
    public partial class BaselineSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppErrors",
                columns: table => new
                {
                    AppErrorId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ErrorUid = table.Column<string>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: true),
                    Method = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    StackTrace = table.Column<string>(type: "TEXT", nullable: true),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppErrors", x => x.AppErrorId);
                });

            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    AppUserId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordSalt = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystemAccount = table.Column<bool>(type: "INTEGER", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.AppUserId);
                });

            migrationBuilder.CreateTable(
                name: "CampaignCollaborators",
                columns: table => new
                {
                    CampaignCollaboratorId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CampaignId = table.Column<int>(type: "INTEGER", nullable: false),
                    AppUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignCollaborators", x => x.CampaignCollaboratorId);
                });

            migrationBuilder.CreateTable(
                name: "CampaignPlayers",
                columns: table => new
                {
                    CampaignPlayerId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CampaignId = table.Column<int>(type: "INTEGER", nullable: false),
                    AppUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignPlayers", x => x.CampaignPlayerId);
                });

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    CampaignId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    OwnerAppUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.CampaignId);
                });

            migrationBuilder.CreateTable(
                name: "CreatureAbilities",
                columns: table => new
                {
                    CreatureAbilityId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatureId = table.Column<int>(type: "INTEGER", nullable: false),
                    AbilityType = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatureAbilities", x => x.CreatureAbilityId);
                });

            migrationBuilder.CreateTable(
                name: "Creatures",
                columns: table => new
                {
                    CreatureId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameSystemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", nullable: false),
                    Alias = table.Column<string>(type: "TEXT", nullable: true),
                    CreatureType = table.Column<string>(type: "TEXT", nullable: true),
                    Size = table.Column<string>(type: "TEXT", nullable: true),
                    Alignment = table.Column<string>(type: "TEXT", nullable: true),
                    ArmorClass = table.Column<int>(type: "INTEGER", nullable: true),
                    HitPoints = table.Column<int>(type: "INTEGER", nullable: true),
                    Speed = table.Column<string>(type: "TEXT", nullable: true),
                    Strength = table.Column<int>(type: "INTEGER", nullable: true),
                    Dexterity = table.Column<int>(type: "INTEGER", nullable: true),
                    Constitution = table.Column<int>(type: "INTEGER", nullable: true),
                    Intelligence = table.Column<int>(type: "INTEGER", nullable: true),
                    Wisdom = table.Column<int>(type: "INTEGER", nullable: true),
                    Charisma = table.Column<int>(type: "INTEGER", nullable: true),
                    ChallengeRating = table.Column<string>(type: "TEXT", nullable: true),
                    ProficiencyBonus = table.Column<int>(type: "INTEGER", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerAppUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceMaterialId = table.Column<int>(type: "INTEGER", nullable: true),
                    CampaignId = table.Column<int>(type: "INTEGER", nullable: true),
                    SourcePage = table.Column<int>(type: "INTEGER", nullable: true),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Creatures", x => x.CreatureId);
                });

            migrationBuilder.CreateTable(
                name: "CurrencyDefinitions",
                columns: table => new
                {
                    CurrencyDefinitionId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameSystemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyDefinitions", x => x.CurrencyDefinitionId);
                });

            migrationBuilder.CreateTable(
                name: "FeatureRequests",
                columns: table => new
                {
                    FeatureRequestId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<string>(type: "TEXT", nullable: true),
                    RequestedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Entity = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureRequests", x => x.FeatureRequestId);
                });

            migrationBuilder.CreateTable(
                name: "FriendRequests",
                columns: table => new
                {
                    FriendRequestId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FromAppUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ToAppUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateResolvedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FriendRequests", x => x.FriendRequestId);
                });

            migrationBuilder.CreateTable(
                name: "Friends",
                columns: table => new
                {
                    FriendId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserAId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserBId = table.Column<int>(type: "INTEGER", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Friends", x => x.FriendId);
                });

            migrationBuilder.CreateTable(
                name: "GameSystems",
                columns: table => new
                {
                    GameSystemId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", nullable: false),
                    Alias = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSystems", x => x.GameSystemId);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameSystemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", nullable: false),
                    Alias = table.Column<string>(type: "TEXT", nullable: true),
                    ItemTypeDefinitionId = table.Column<int>(type: "INTEGER", nullable: true),
                    OwnerAppUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    RarityDefinitionId = table.Column<int>(type: "INTEGER", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Rarity = table.Column<string>(type: "TEXT", nullable: true),
                    CostAmount = table.Column<decimal>(type: "TEXT", nullable: true),
                    CurrencyDefinitionId = table.Column<int>(type: "INTEGER", nullable: true),
                    CostCurrency = table.Column<string>(type: "TEXT", nullable: true),
                    Weight = table.Column<decimal>(type: "TEXT", nullable: true),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: true),
                    Effect = table.Column<string>(type: "TEXT", nullable: true),
                    RequiresAttunement = table.Column<bool>(type: "INTEGER", nullable: false),
                    AttunementRequirement = table.Column<string>(type: "TEXT", nullable: true),
                    DamageDice = table.Column<string>(type: "TEXT", nullable: true),
                    DamageType = table.Column<string>(type: "TEXT", nullable: true),
                    VersatileDamageDice = table.Column<string>(type: "TEXT", nullable: true),
                    ArmorClass = table.Column<int>(type: "INTEGER", nullable: true),
                    StrengthRequirement = table.Column<int>(type: "INTEGER", nullable: true),
                    StealthDisadvantage = table.Column<bool>(type: "INTEGER", nullable: false),
                    RangeNormal = table.Column<int>(type: "INTEGER", nullable: true),
                    RangeLong = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceMaterialId = table.Column<int>(type: "INTEGER", nullable: true),
                    CampaignId = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceBook = table.Column<string>(type: "TEXT", nullable: true),
                    SourcePage = table.Column<int>(type: "INTEGER", nullable: true),
                    IsConsumable = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChargesCurrent = table.Column<int>(type: "INTEGER", nullable: true),
                    ChargesMax = table.Column<int>(type: "INTEGER", nullable: true),
                    RechargeRule = table.Column<string>(type: "TEXT", nullable: true),
                    UsesPerDay = table.Column<int>(type: "INTEGER", nullable: true),
                    ArmorCategory = table.Column<string>(type: "TEXT", nullable: true),
                    WeaponPropertyLight = table.Column<bool>(type: "INTEGER", nullable: false),
                    WeaponPropertyHeavy = table.Column<bool>(type: "INTEGER", nullable: false),
                    WeaponPropertyFinesse = table.Column<bool>(type: "INTEGER", nullable: false),
                    WeaponPropertyThrown = table.Column<bool>(type: "INTEGER", nullable: false),
                    WeaponPropertyTwoHanded = table.Column<bool>(type: "INTEGER", nullable: false),
                    WeaponPropertyLoading = table.Column<bool>(type: "INTEGER", nullable: false),
                    WeaponPropertyReach = table.Column<bool>(type: "INTEGER", nullable: false),
                    WeaponPropertyAmmunition = table.Column<bool>(type: "INTEGER", nullable: false),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.ItemId);
                });

            migrationBuilder.CreateTable(
                name: "ItemTags",
                columns: table => new
                {
                    ItemTagId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTags", x => x.ItemTagId);
                });

            migrationBuilder.CreateTable(
                name: "ItemTypeDefinitions",
                columns: table => new
                {
                    ItemTypeDefinitionId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameSystemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTypeDefinitions", x => x.ItemTypeDefinitionId);
                });

            migrationBuilder.CreateTable(
                name: "Notes",
                columns: table => new
                {
                    NoteId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.NoteId);
                });

            migrationBuilder.CreateTable(
                name: "RarityDefinitions",
                columns: table => new
                {
                    RarityDefinitionId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameSystemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RarityDefinitions", x => x.RarityDefinitionId);
                });

            migrationBuilder.CreateTable(
                name: "SourceMaterials",
                columns: table => new
                {
                    SourceMaterialId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameSystemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Publisher = table.Column<string>(type: "TEXT", nullable: true),
                    IsOfficial = table.Column<bool>(type: "INTEGER", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceMaterials", x => x.SourceMaterialId);
                });

            migrationBuilder.CreateTable(
                name: "TagDefinitions",
                columns: table => new
                {
                    TagDefinitionId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameSystemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagDefinitions", x => x.TagDefinitionId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppErrors");

            migrationBuilder.DropTable(
                name: "AppUsers");

            migrationBuilder.DropTable(
                name: "CampaignCollaborators");

            migrationBuilder.DropTable(
                name: "CampaignPlayers");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropTable(
                name: "CreatureAbilities");

            migrationBuilder.DropTable(
                name: "Creatures");

            migrationBuilder.DropTable(
                name: "CurrencyDefinitions");

            migrationBuilder.DropTable(
                name: "FeatureRequests");

            migrationBuilder.DropTable(
                name: "FriendRequests");

            migrationBuilder.DropTable(
                name: "Friends");

            migrationBuilder.DropTable(
                name: "GameSystems");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "ItemTags");

            migrationBuilder.DropTable(
                name: "ItemTypeDefinitions");

            migrationBuilder.DropTable(
                name: "Notes");

            migrationBuilder.DropTable(
                name: "RarityDefinitions");

            migrationBuilder.DropTable(
                name: "SourceMaterials");

            migrationBuilder.DropTable(
                name: "TagDefinitions");
        }
    }
}
