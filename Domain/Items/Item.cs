namespace RuleForge.Domain.Items;

public sealed class Item
{
    public int ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? OwnerAppUserId { get; set; }
    public string? Description { get; set; }
    public ItemType ItemType { get; set; } = ItemType.Other;
    public ItemRarity Rarity { get; set; } = ItemRarity.Common;
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

    public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime DateModifiedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DateDeletedUtc { get; set; }
}
