namespace RuleForge.Contracts.Items;

public class ItemResponse
{
    public int ItemId { get; set; }
    public int? OwnerAppUserId { get; set; }
    public bool IsSystem { get; set; }
    public bool IsUserVariant { get; set; }
    public string? OwnerUsername { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ItemType { get; set; }
    public int Rarity { get; set; }
    public decimal? Weight { get; set; }
    public decimal? CostAmount { get; set; }
    public string? CostCurrency { get; set; }
    public bool RequiresAttunement { get; set; }
    public int? SourceType { get; set; }
    public string? Source { get; set; }
    public string? Tags { get; set; }

    public string? WeaponCategory { get; set; }
    public string? DamageDice { get; set; }
    public string? DamageType { get; set; }
    public string? Properties { get; set; }
    public int? RangeNormal { get; set; }
    public int? RangeMax { get; set; }
    public bool IsMagicWeapon { get; set; }
    public int? AttackBonus { get; set; }
    public int? DamageBonus { get; set; }

    public string? ArmorCategory { get; set; }
    public int? ArmorClassBase { get; set; }
    public int? DexCap { get; set; }
    public int? StrengthRequirement { get; set; }
    public bool StealthDisadvantage { get; set; }
    public bool IsMagicArmor { get; set; }
    public int? ArmorBonus { get; set; }

    public int? Charges { get; set; }
    public int? MaxCharges { get; set; }
    public string? RechargeRule { get; set; }
    public string? ConsumableEffect { get; set; }

    public int? Quantity { get; set; }
    public bool Stackable { get; set; }
    public string? Notes { get; set; }
}

public class UpsertItemRequest
{
    public int? OwnerAppUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ItemType { get; set; }
    public int Rarity { get; set; }
    public decimal? Weight { get; set; }
    public decimal? CostAmount { get; set; }
    public string? CostCurrency { get; set; }
    public bool RequiresAttunement { get; set; }
    public int? SourceType { get; set; }
    public string? Source { get; set; }
    public string? Tags { get; set; }

    public string? WeaponCategory { get; set; }
    public string? DamageDice { get; set; }
    public string? DamageType { get; set; }
    public string? Properties { get; set; }
    public int? RangeNormal { get; set; }
    public int? RangeMax { get; set; }
    public bool IsMagicWeapon { get; set; }
    public int? AttackBonus { get; set; }
    public int? DamageBonus { get; set; }

    public string? ArmorCategory { get; set; }
    public int? ArmorClassBase { get; set; }
    public int? DexCap { get; set; }
    public int? StrengthRequirement { get; set; }
    public bool StealthDisadvantage { get; set; }
    public bool IsMagicArmor { get; set; }
    public int? ArmorBonus { get; set; }

    public int? Charges { get; set; }
    public int? MaxCharges { get; set; }
    public string? RechargeRule { get; set; }
    public string? ConsumableEffect { get; set; }

    public int? Quantity { get; set; }
    public bool Stackable { get; set; }
    public string? Notes { get; set; }
}

