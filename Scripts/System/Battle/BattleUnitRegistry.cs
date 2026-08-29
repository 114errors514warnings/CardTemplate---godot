// BattleUnitRegistry.cs
// 单位查询、目标解析、单位/卡牌标签拼接。
// 不依赖 Godot，可直接单测。
// 注意：目标解析中"随机"用传入的 Random，保证可测。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CardSimulator;

public sealed class BattleUnitRegistry
{
    private readonly BattleSytem battle;
    private readonly Random random;

    public BattleUnitRegistry(BattleSytem battle, Random random = null)
    {
        this.battle = battle ?? throw new ArgumentNullException(nameof(battle));
        this.random = random;
    }

    public List<CharacterInstance> GetAlivePlayers()
    {
        if (battle.Players == null || battle.Players.Count == 0)
        {
            return new List<CharacterInstance>();
        }
        return battle.Players.Values
            .Where(player => player != null && player.HP > 0)
            .OrderBy(player => player.UniqueInGameId)
            .ToList();
    }

    public List<CharacterInstance> GetOrderedPlayers()
    {
        if (battle.Players == null || battle.Players.Count == 0)
        {
            return new List<CharacterInstance>();
        }
        return battle.Players.Values.OrderBy(player => player.UniqueInGameId).ToList();
    }

    public bool TryGetPlayerByUniqueId(int uniqueInGameId, out CharacterInstance player)
    {
        player = null;
        return battle.Players != null
            && battle.Players.TryGetValue(uniqueInGameId, out player)
            && player != null;
    }

    public bool TryGetUnitByUniqueId(int uniqueInGameId, out IUnitInstance unit)
    {
        unit = null;
        if (TryGetPlayerByUniqueId(uniqueInGameId, out CharacterInstance player))
        {
            unit = player;
            return true;
        }
        if (battle.Monsters != null
            && battle.Monsters.TryGetValue(uniqueInGameId, out MonsterInstance monster)
            && monster != null)
        {
            unit = monster;
            return true;
        }
        return false;
    }

    public List<IUnitInstance> GetEnemyUnits(IUnitInstance source)
    {
        var result = new List<IUnitInstance>();
        if (source == null)
        {
            return result;
        }

        if (source is CharacterInstance)
        {
            if (battle.Monsters != null)
            {
                foreach (MonsterInstance monster in battle.Monsters.Values)
                {
                    if (monster != null && monster.HP > 0)
                    {
                        result.Add(monster);
                    }
                }
            }
            return result;
        }

        foreach (CharacterInstance player in GetAlivePlayers())
        {
            result.Add(player);
        }
        return result;
    }

    public List<IUnitInstance> GetAllUnits()
    {
        var result = new List<IUnitInstance>();
        foreach (CharacterInstance player in GetAlivePlayers())
        {
            result.Add(player);
        }
        if (battle.Monsters != null)
        {
            foreach (MonsterInstance monster in battle.Monsters.Values)
            {
                if (monster != null && monster.HP > 0)
                {
                    result.Add(monster);
                }
            }
        }
        return result;
    }

    public List<IUnitInstance> GetAllyUnits(IUnitInstance source)
    {
        var allies = new List<IUnitInstance>();
        if (source == null)
        {
            return allies;
        }

        if (source is CharacterInstance)
        {
            foreach (CharacterInstance player in GetAlivePlayers())
            {
                if (player != null && player.UniqueInGameId != source.UniqueInGameId)
                {
                    allies.Add(player);
                }
            }
            return allies;
        }

        if (battle.Monsters != null)
        {
            foreach (MonsterInstance monster in battle.Monsters.Values)
            {
                if (monster != null && monster.HP > 0 && monster.UniqueInGameId != source.UniqueInGameId)
                {
                    allies.Add(monster);
                }
            }
        }
        return allies;
    }

    public IUnitInstance ResolveRandomAlivePlayerTarget()
    {
        List<CharacterInstance> alive = GetAlivePlayers();
        if (alive.Count == 0)
        {
            return null;
        }
        Random rng = random ?? BattleSytem.RandomGenerator;
        return alive[rng.Next(alive.Count)];
    }

    /// <summary>
    /// 根据 EffectTargetType 解析效果的目标列表。
    /// </summary>
    public List<IUnitInstance> ResolveEffectTargets(
        IUnitInstance source,
        IUnitInstance selected,
        EffectTargetType targetType)
    {
        var result = new List<IUnitInstance>();
        switch (targetType)
        {
            case EffectTargetType.Self:
                if (source != null) result.Add(source);
                break;
            case EffectTargetType.SelectedTarget:
                if (selected != null && selected.HP > 0) result.Add(selected);
                break;
            case EffectTargetType.AllEnemies:
                result.AddRange(GetEnemyUnits(source));
                break;
            case EffectTargetType.AllAllies:
                result.AddRange(GetAllyUnits(source));
                break;
            case EffectTargetType.AllUnits:
                result.AddRange(GetAllUnits());
                break;
            case EffectTargetType.Auto:
                Random rng = random ?? BattleSytem.RandomGenerator;
                List<IUnitInstance> enemies = GetEnemyUnits(source);
                if (enemies.Count > 0)
                {
                    result.Add(enemies[rng.Next(enemies.Count)]);
                }
                break;
            default:
                break;
        }
        return result;
    }

    public string BuildUnitLabel(IUnitInstance unit)
    {
        if (unit == null) return "无";
        Unit typedUnit = unit as Unit;
        string name = typedUnit?.Name ?? unit.GetType().Name;
        return $"{name}(UniqueInGameId={unit.UniqueInGameId})";
    }

    public string BuildCardLabel(Card card)
    {
        if (card == null) return "无";
        string cardName = string.IsNullOrWhiteSpace(card.CardName) ? $"CardId={card.CardId}" : card.CardName;
        string uniqueInGameId = string.IsNullOrWhiteSpace(card.UniqueInGameId) ? "未生成" : card.UniqueInGameId;
        return $"{cardName}(CardId={card.CardId}, UniqueInGameId={uniqueInGameId})";
    }

    public string FormatUniqueInGameId(int uniqueInGameId) => uniqueInGameId.ToString("D7");
}
