using System;
using System.Collections.Generic;
using System.Linq;

namespace CardSimulator.Battlefield;

/// <summary>
/// 怪物意图效果段的**目标类型落地**（P2-25，2026-10-07）。
///
/// 效果段写法 `&lt;EffectType&gt;;&lt;EffectTargetType&gt;;&lt;参数…&gt;` 的第二位决定这条效果落在谁身上。
/// 此前 `ExecuteEnemyIntentionEffect` 除 `Damage` 段之外一律按「本意图的索敌目标」结算，于是：
/// `3;1;…`（`Self`，如 3123「自身加攻」）会变成给玩家加攻（反向增益）、
/// `3;3;…`（`AllEnemies`，如 3125「导师威压（全体）」）只打到 1 名角色；`3;5;…`（`AllAllies`）与
/// `AllyRange` 半径口径只在 `Shield` 段有特判，友方增益没有载体。
///
/// 阵营口径按「施法方 ↔ 其余单位」解析，并与卡牌那条已经验证过的空间解析
/// （<see cref="BattlefieldEffectTargetScope"/>）**同源**，避免两套实现再次分叉：
/// `Self` = 施法怪物本人；`SelectedTarget` / `Auto` = 本意图的索敌目标；`AllEnemies` = 对方阵营
/// （角色 + 护送目标）；`AllAllies` = 其余存活怪物（`AllAllies` 语义本身不含自身）；`AllUnits` = 全部。
///
/// `TargetPolicy = AllyRange` + `AreaRadius` 的半径约束对**友方侧**目标生效（与 `Shield` 段同口径；
/// `AreaRadius = 0` 即「只剩自身」，由调用方按需把施法者补回来）；敌方目标不受半径影响，
/// 以免改变既有 `3;0;…` / `3;3;…` 行的行为。
/// </summary>
public static class EnemyIntentEffectTargets
{
    /// <summary>解析效果段第二位的目标类型：缺段 / 非法值按 `Auto` 处理（与 `Card.ParseEffectTargetType` 同口径）。</summary>
    public static EffectTargetType ParseTargetType(int[] effect)
    {
        if (effect == null || effect.Length <= 2)
        {
            return EffectTargetType.Auto;
        }

        EffectTargetType parsed = (EffectTargetType)effect[1];
        return Enum.IsDefined(typeof(EffectTargetType), parsed) ? parsed : EffectTargetType.Auto;
    }

    /// <summary>
    /// 把一段效果解析成场上目标。<paramref name="placements"/> 传整张战场
    /// （`Occupancy.Placements.Values`）；返回顺序稳定（按传入顺序），便于断言与消息输出。
    /// </summary>
    public static IReadOnlyList<BattleUnitPlacement> Resolve(EffectTargetType targetType, BattleUnitPlacement source,
        BattleUnitPlacement selected, IEnumerable<BattleUnitPlacement> placements,
        EnemyTargetPolicy targetPolicy = EnemyTargetPolicy.Nearest, int areaRadius = 0)
    {
        if (source == null || source.Unit == null)
        {
            return Array.Empty<BattleUnitPlacement>();
        }

        List<BattleUnitPlacement> field = placements?.Where(x => x != null && x.Unit != null).ToList()
            ?? new List<BattleUnitPlacement>();
        // 友方 = 同阵营的**其余**存活单位（`AllAllies` 不含自身）；敌方 = 其余阵营的存活单位。
        // 退场（Departed / Defeated）单位不进候选；HP ≤ 0 由 `TryResolve` 统一再滤一次。
        List<IUnitInstance> allies = field
            .Where(x => x.UnitId != source.UnitId && x.Role == source.Role && x.Presence == BattlefieldPresence.Active)
            .Select(x => x.Unit).ToList();
        List<IUnitInstance> enemies = field
            .Where(x => x.Role != source.Role && x.Presence == BattlefieldPresence.Active)
            .Select(x => x.Unit).ToList();

        List<IUnitInstance> resolved;
        // `TryResolve` 只认「发起者 = 自己的 source」，因此 source 双传；空间攻击展开必须关掉，
        // 否则 `Auto` / `SelectedTarget` 会退化成「全体敌人」（那是卡牌攻击段的口径）。
        using (var scope = new BattlefieldEffectTargetScope(source.Unit, selected?.Unit, enemies, allies, playerTurn: false))
        {
            if (!scope.TryResolve(source.Unit, selected?.Unit, targetType, out resolved))
            {
                return Array.Empty<BattleUnitPlacement>();
            }
        }

        var byUnit = new Dictionary<IUnitInstance, BattleUnitPlacement>();
        foreach (BattleUnitPlacement placement in field)
        {
            if (!byUnit.ContainsKey(placement.Unit)) byUnit[placement.Unit] = placement;
        }
        byUnit[source.Unit] = source;

        var targets = new List<BattleUnitPlacement>();
        foreach (IUnitInstance unit in resolved)
        {
            if (unit != null && byUnit.TryGetValue(unit, out BattleUnitPlacement placement) && !targets.Contains(placement))
            {
                targets.Add(placement);
            }
        }

        if (targetPolicy == EnemyTargetPolicy.AllyRange)
        {
            int radius = Math.Max(0, areaRadius);
            targets.RemoveAll(x => x.Role == source.Role && BattleRangeResolver.Distance(source.Coord, x.Coord) > radius);
        }

        return targets;
    }
}
