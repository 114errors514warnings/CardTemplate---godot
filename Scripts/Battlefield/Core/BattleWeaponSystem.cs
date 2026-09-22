using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CardSimulator.Battlefield;

/// <summary>Weapon default attack modes. CSV uses these enum names exactly.</summary>
public enum WeaponAttackMode { AdjacentSingle, MeleeLine, Fan, Ring, ThrowSingle, RangedLine, Thrust }

/// <summary>
/// 装备类型：近战武器 / 远程武器 / 防具。它只用于回答一个问题——**卡牌声明的特殊攻击方式能否生效**：
/// 卡牌的特殊方式属于近战型时只有近战武器才能让它生效，远程型同理；不满足就回落到武器自身的攻击方式。
/// </summary>
public enum EquipmentType { Melee, Ranged, Armor }

/// <summary>Battlefield-only weapon rules. Card ranges consume AttackRange, while normal attacks consume Mode.</summary>
public sealed record WeaponAttackSpec(string DefinitionId, int AttackRange, int HandsRequired,
    WeaponAttackMode Mode, int DefenseValue, int DamageBonus = 0, int MoveBonus = 0, int ResourceCost = 0,
    bool BlocksDefenseShield = false, EquipmentType Type = EquipmentType.Melee)
{
    public static readonly WeaponAttackSpec Unarmed = new("unarmed", 1, 0, WeaponAttackMode.AdjacentSingle, 0, 0);

    /// <summary>未显式配置 <see cref="EquipmentType"/> 时的兜底：远程直线与投掷算远程，其余算近战。</summary>
    public static EquipmentType DeriveType(WeaponAttackMode mode) =>
        mode is WeaponAttackMode.RangedLine or WeaponAttackMode.ThrowSingle ? EquipmentType.Ranged : EquipmentType.Melee;
}

public static class BattleWeaponCatalog
{
    public const string WeaponCsvPathKey = "Data.Equipment.Weapon";
    private static Dictionary<string, WeaponAttackSpec> specs;

    public static IReadOnlyDictionary<string, WeaponAttackSpec> LoadAll(bool useCache = true)
    {
        if (useCache && specs != null) return specs;
        var loaded = new Dictionary<string, WeaponAttackSpec>(StringComparer.OrdinalIgnoreCase);
        string testCopy = Path.Combine(AppContext.BaseDirectory, "Weapon.csv");
        string[] lines = File.Exists(testCopy)
            ? File.ReadAllLines(testCopy)
            : LoadCsv.LoadCSVDataLines(LoadingSystem.GetFilePathByKey(WeaponCsvPathKey));
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] fields = LoadCsv.ParseCSVFields(line);
            if (fields.Length > 0 && string.Equals(fields[0], "WeaponId", StringComparison.OrdinalIgnoreCase)) continue;
            if (fields.Length < 9 || !int.TryParse(fields[0], out int weaponId) || weaponId <= 0 ||
                string.IsNullOrWhiteSpace(fields[1]) || !int.TryParse(fields[2], out int hands) ||
                !Enum.TryParse(fields[3], true, out WeaponAttackMode mode) || !int.TryParse(fields[4], out int range) ||
                !int.TryParse(fields[5], out int defense) || !int.TryParse(fields[6], out int damage) ||
                !int.TryParse(fields[7], out int moveBonus) || !int.TryParse(fields[8], out int resourceCost) ||
                hands is < 1 or > 2 || range < 1 || defense < 0 || resourceCost < 0)
                throw new ArgumentException($"武器 CSV 行无效：{line}");
            bool blocksDefenseShield = fields.Length >= 10 && bool.TryParse(fields[9], out bool parsedBlocks) && parsedBlocks;
            if (fields.Length >= 10 && !bool.TryParse(fields[9], out _))
                throw new ArgumentException($"武器 CSV 的 BlocksDefenseShield 必须为 true 或 false：{line}");
            // EquipmentType（近战 / 远程 / 防具）：缺省时按 AttackMode 推导，保证旧数据与地图内联装备仍然可用。
            EquipmentType equipmentType = WeaponAttackSpec.DeriveType(mode);
            if (fields.Length >= 11 && !string.IsNullOrWhiteSpace(fields[10]))
            {
                string rawType = fields[10].Trim();
                if (int.TryParse(rawType, out _) || !Enum.TryParse(rawType, true, out equipmentType) || !Enum.IsDefined(equipmentType))
                    throw new ArgumentException($"武器 CSV 的 EquipmentType 必须是 Melee / Ranged / Armor：{line}");
            }
            if (!loaded.TryAdd(fields[1], new WeaponAttackSpec(fields[1], range, hands, mode, defense, damage, moveBonus, resourceCost, blocksDefenseShield, equipmentType)))
                throw new ArgumentException($"武器 DefinitionId 重复：{fields[1]}");
        }
        specs = loaded; return specs;
    }

    public static WeaponAttackSpec Resolve(GroundObject equipment) => equipment == null
        ? WeaponAttackSpec.Unarmed
        : ForDefinition(equipment.DefinitionId) is WeaponAttackSpec spec
            ? spec
            : new WeaponAttackSpec(equipment.DefinitionId, equipment.AttackRange, equipment.HandsRequired,
                WeaponAttackMode.AdjacentSingle, 0);

    public static WeaponAttackSpec ForDefinition(string definitionId) =>
        !string.IsNullOrWhiteSpace(definitionId) && LoadAll().TryGetValue(definitionId, out var spec) ? spec : null;
}

public static class BattleAttackTraceResolver
{
    public static IReadOnlyList<AxialHex> Resolve(BattleBoard board, BattleOccupancyService occupancy,
        AxialHex origin, AxialHex chosen, WeaponAttackSpec spec)
    {
        return BattleAttackSystem.ResolveFromSelectedCell(board, occupancy, origin, chosen, spec);
    }

    /// <summary>Fires or traces along one of the six centre-to-centre hex directions.</summary>
    public static IReadOnlyList<AxialHex> ResolveDirection(BattleBoard board, BattleOccupancyService occupancy,
        AxialHex origin, AxialHex direction, WeaponAttackSpec spec)
    {
        return BattleAttackSystem.ResolveFromDirection(board, occupancy, origin, direction, spec);
    }
}
