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
