using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

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
                    AppErrorId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ErrorUid = table.Column<string>(type: "text", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: true),
                    Method = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: true),
                    StackTrace = table.Column<string>(type: "text", nullable: true),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppErrors", x => x.AppErrorId);
                });

            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    AppUserId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    PasswordSalt = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystemAccount = table.Column<bool>(type: "boolean", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "boolean", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.AppUserId);
                });

            migrationBuilder.CreateTable(
                name: "CampaignCollaborators",
                columns: table => new
                {
                    CampaignCollaboratorId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CampaignId = table.Column<int>(type: "integer", nullable: false),
                    AppUserId = table.Column<int>(type: "integer", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignCollaborators", x => x.CampaignCollaboratorId);
                });

            migrationBuilder.CreateTable(
                name: "CampaignPlayers",
                columns: table => new
                {
                    CampaignPlayerId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CampaignId = table.Column<int>(type: "integer", nullable: false),
                    AppUserId = table.Column<int>(type: "integer", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignPlayers", x => x.CampaignPlayerId);
                });

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    CampaignId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    OwnerAppUserId = table.Column<int>(type: "integer", nullable: true),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.CampaignId);
                });

            migrationBuilder.CreateTable(
                name: "CreatureAbilities",
                columns: table => new
                {
                    CreatureAbilityId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatureId = table.Column<int>(type: "integer", nullable: false),
                    AbilityType = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatureAbilities", x => x.CreatureAbilityId);
                });

            migrationBuilder.CreateTable(
                name: "Creatures",
                columns: table => new
                {
                    CreatureId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameSystemId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Alias = table.Column<string>(type: "text", nullable: true),
                    CreatureType = table.Column<string>(type: "text", nullable: true),
                    Size = table.Column<string>(type: "text", nullable: true),
                    Alignment = table.Column<string>(type: "text", nullable: true),
                    ArmorClass = table.Column<int>(type: "integer", nullable: true),
                    HitPoints = table.Column<int>(type: "integer", nullable: true),
                    Speed = table.Column<string>(type: "text", nullable: true),
                    Strength = table.Column<int>(type: "integer", nullable: true),
                    Dexterity = table.Column<int>(type: "integer", nullable: true),
                    Constitution = table.Column<int>(type: "integer", nullable: true),
                    Intelligence = table.Column<int>(type: "integer", nullable: true),
                    Wisdom = table.Column<int>(type: "integer", nullable: true),
                    Charisma = table.Column<int>(type: "integer", nullable: true),
                    ChallengeRating = table.Column<string>(type: "text", nullable: true),
                    ProficiencyBonus = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SourceType = table.Column<int>(type: "integer", nullable: false),
                    OwnerAppUserId = table.Column<int>(type: "integer", nullable: true),
                    SourceMaterialId = table.Column<int>(type: "integer", nullable: true),
                    CampaignId = table.Column<int>(type: "integer", nullable: true),
                    SourcePage = table.Column<int>(type: "integer", nullable: true),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Creatures", x => x.CreatureId);
                });

            migrationBuilder.CreateTable(
                name: "CurrencyDefinitions",
                columns: table => new
                {
                    CurrencyDefinitionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameSystemId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Symbol = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyDefinitions", x => x.CurrencyDefinitionId);
                });

            migrationBuilder.CreateTable(
                name: "FeatureRequests",
                columns: table => new
                {
                    FeatureRequestId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Priority = table.Column<string>(type: "text", nullable: true),
                    RequestedBy = table.Column<string>(type: "text", nullable: true),
                    Entity = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureRequests", x => x.FeatureRequestId);
                });

            migrationBuilder.CreateTable(
                name: "FriendRequests",
                columns: table => new
                {
                    FriendRequestId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FromAppUserId = table.Column<int>(type: "integer", nullable: false),
                    ToAppUserId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateResolvedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FriendRequests", x => x.FriendRequestId);
                });

            migrationBuilder.CreateTable(
                name: "Friends",
                columns: table => new
                {
                    FriendId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserAId = table.Column<int>(type: "integer", nullable: false),
                    UserBId = table.Column<int>(type: "integer", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Friends", x => x.FriendId);
                });

            migrationBuilder.CreateTable(
                name: "GameSystems",
                columns: table => new
                {
                    GameSystemId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Alias = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SourceType = table.Column<int>(type: "integer", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSystems", x => x.GameSystemId);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameSystemId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Alias = table.Column<string>(type: "text", nullable: true),
                    ItemTypeDefinitionId = table.Column<int>(type: "integer", nullable: true),
                    OwnerAppUserId = table.Column<int>(type: "integer", nullable: true),
                    RarityDefinitionId = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Rarity = table.Column<string>(type: "text", nullable: true),
                    CostAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    CurrencyDefinitionId = table.Column<int>(type: "integer", nullable: true),
                    CostCurrency = table.Column<string>(type: "text", nullable: true),
                    Weight = table.Column<decimal>(type: "numeric", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Tags = table.Column<string>(type: "text", nullable: true),
                    Effect = table.Column<string>(type: "text", nullable: true),
                    RequiresAttunement = table.Column<bool>(type: "boolean", nullable: false),
                    AttunementRequirement = table.Column<string>(type: "text", nullable: true),
                    DamageDice = table.Column<string>(type: "text", nullable: true),
                    DamageType = table.Column<string>(type: "text", nullable: true),
                    VersatileDamageDice = table.Column<string>(type: "text", nullable: true),
                    ArmorClass = table.Column<int>(type: "integer", nullable: true),
                    StrengthRequirement = table.Column<int>(type: "integer", nullable: true),
                    StealthDisadvantage = table.Column<bool>(type: "boolean", nullable: false),
                    RangeNormal = table.Column<int>(type: "integer", nullable: true),
                    RangeLong = table.Column<int>(type: "integer", nullable: true),
                    SourceMaterialId = table.Column<int>(type: "integer", nullable: true),
                    CampaignId = table.Column<int>(type: "integer", nullable: true),
                    SourceBook = table.Column<string>(type: "text", nullable: true),
                    SourcePage = table.Column<int>(type: "integer", nullable: true),
                    IsConsumable = table.Column<bool>(type: "boolean", nullable: false),
                    ChargesCurrent = table.Column<int>(type: "integer", nullable: true),
                    ChargesMax = table.Column<int>(type: "integer", nullable: true),
                    RechargeRule = table.Column<string>(type: "text", nullable: true),
                    UsesPerDay = table.Column<int>(type: "integer", nullable: true),
                    ArmorCategory = table.Column<string>(type: "text", nullable: true),
                    WeaponPropertyLight = table.Column<bool>(type: "boolean", nullable: false),
                    WeaponPropertyHeavy = table.Column<bool>(type: "boolean", nullable: false),
                    WeaponPropertyFinesse = table.Column<bool>(type: "boolean", nullable: false),
                    WeaponPropertyThrown = table.Column<bool>(type: "boolean", nullable: false),
                    WeaponPropertyTwoHanded = table.Column<bool>(type: "boolean", nullable: false),
                    WeaponPropertyLoading = table.Column<bool>(type: "boolean", nullable: false),
                    WeaponPropertyReach = table.Column<bool>(type: "boolean", nullable: false),
                    WeaponPropertyAmmunition = table.Column<bool>(type: "boolean", nullable: false),
                    SourceType = table.Column<int>(type: "integer", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.ItemId);
                });

            migrationBuilder.CreateTable(
                name: "ItemTags",
                columns: table => new
                {
                    ItemTagId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    TagDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTags", x => x.ItemTagId);
                });

            migrationBuilder.CreateTable(
                name: "ItemTypeDefinitions",
                columns: table => new
                {
                    ItemTypeDefinitionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameSystemId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTypeDefinitions", x => x.ItemTypeDefinitionId);
                });

            migrationBuilder.CreateTable(
                name: "Notes",
                columns: table => new
                {
                    NoteId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: true),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.NoteId);
                });

            migrationBuilder.CreateTable(
                name: "RarityDefinitions",
                columns: table => new
                {
                    RarityDefinitionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameSystemId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RarityDefinitions", x => x.RarityDefinitionId);
                });

            migrationBuilder.CreateTable(
                name: "SourceMaterials",
                columns: table => new
                {
                    SourceMaterialId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameSystemId = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Publisher = table.Column<string>(type: "text", nullable: true),
                    IsOfficial = table.Column<bool>(type: "boolean", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceMaterials", x => x.SourceMaterialId);
                });

            migrationBuilder.CreateTable(
                name: "TagDefinitions",
                columns: table => new
                {
                    TagDefinitionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameSystemId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    DateCreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateDeletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
