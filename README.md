# RuleForge

RuleForge is a .NET 8 app with SQLite-backed data for tabletop systems and items.

## Seed Data (portable across machines)

On startup, RuleForge reads:

- `SeedData/seed-data.json`

If records are missing, it seeds them into the database (idempotent behavior).

This means when you clone/pull this repo on another machine and run the app, baseline records are created automatically.

### Seed file shape

Top-level keys:

- `gameSystems`
- `itemTypes`
- `rarities`
- `currencies`
- `items`

### Minimal example

```json
{
  "gameSystems": [
    {
      "name": "Dungeons & Dragons 5e",
      "slug": "dungeons-dragons-5e",
      "alias": "D&D 5e",
      "description": "Fifth edition fantasy tabletop RPG.",
      "sourceType": "Official"
    }
  ],
  "itemTypes": [
    {
      "gameSystemSlug": "dungeons-dragons-5e",
      "name": "Weapon",
      "slug": "weapon",
      "description": "Items intended for combat attacks."
    }
  ],
  "rarities": [
    {
      "gameSystemSlug": "dungeons-dragons-5e",
      "name": "Common",
      "slug": "common",
      "sortOrder": 1
    }
  ],
  "currencies": [
    {
      "gameSystemSlug": "dungeons-dragons-5e",
      "code": "gp",
      "name": "Gold Piece",
      "symbol": "gp"
    }
  ],
  "items": [
    {
      "gameSystemSlug": "dungeons-dragons-5e",
      "name": "Longsword",
      "slug": "longsword",
      "itemTypeSlug": "weapon",
      "raritySlug": "common",
      "costAmount": 15,
      "currencyCode": "gp",
      "weight": 3,
      "quantity": 1,
      "sourceType": "Official"
    }
  ]
}
```

### Notes

- `sourceType` supports: `Official`, `ThirdParty`, `Homebrew`.
- Slugs are used for matching; keep them stable once in use.
- Seeding only inserts missing rows; it does not overwrite existing records.
- For item links:
  - `itemTypeSlug` must match an item type in the same system.
  - `raritySlug` must match a rarity in the same system.
  - `currencyCode` must match a currency in the same system.

## Run

```bash
dotnet run
```

For remote access on your network/tailscale:

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5233 dotnet run --no-launch-profile
```


## Postgres local dev (migration track)

### 1) Start local Postgres

```bash
docker compose -f docker-compose.postgres.yml up -d
```

### 2) Run RuleForge against Postgres

```bash
scripts/run-ruleforge-postgres-local.sh
```

This sets:

- `RULEFORGE_DB_PROVIDER=postgres`
- `RULEFORGE_POSTGRES_CONNECTION=Host=127.0.0.1;Port=5432;Database=ruleforge;Username=ruleforge;Password=ruleforge_dev_password;...`

### 3) EF migration commands (local tool)

Because this project uses a custom MSBuild extensions path, include the flag below:

```bash
dotnet tool run dotnet-ef migrations list --context AppDbContext --msbuildprojectextensionspath /tmp/ruleforge_msbuild
```

Add new migration:

```bash
dotnet tool run dotnet-ef migrations add <Name> --context AppDbContext --msbuildprojectextensionspath /tmp/ruleforge_msbuild
```

### 4) Stop local Postgres

```bash
docker compose -f docker-compose.postgres.yml down
```


### 5) Migrate existing SQLite data into Postgres (dry-run capable)

```bash
RULEFORGE_PGURL='postgresql://ruleforge:ruleforge_dev_password@127.0.0.1:5432/ruleforge' RULEFORGE_SQLITE_PATH="$HOME/.ruleforge/ruleforge.db" scripts/migrate_sqlite_to_postgres.py
```

The script:

- truncates target Postgres tables (in migration order)
- copies table data from SQLite
- converts integer-backed booleans to Postgres booleans where needed
- resets identity sequences
- prints SQLite vs Postgres row-count verification per table
