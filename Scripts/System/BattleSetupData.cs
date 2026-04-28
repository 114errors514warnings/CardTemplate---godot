using Godot;
using System.Collections.Generic;

/// <summary>
/// 战斗初始化配置：保存角色ID与怪物ID数量映射，便于BattleSytem直接读取。
/// </summary>
[GlobalClass]
public partial class BattleSetupData : Resource
{
    public const int MaxMonsterCapacity = 10;

    [Export]
    public int CharacterId { get; set; } = 1001;

    [Export]
    public Godot.Collections.Dictionary<int, int> MonsterIds { get; set; } = new Godot.Collections.Dictionary<int, int>();

    [Export]
    public Godot.Collections.Dictionary<int, int> CharacterCardIds { get; set; } = new Godot.Collections.Dictionary<int, int>();

    public void EnsureMonsterDictionaryInitialized()
    {
        MonsterIds ??= new Godot.Collections.Dictionary<int, int>();
    }

    public void EnsureCharacterCardDictionaryInitialized()
    {
        CharacterCardIds ??= new Godot.Collections.Dictionary<int, int>();
    }

    public int GetMonsterIdCount(int monsterId)
    {
        EnsureMonsterDictionaryInitialized();
        return MonsterIds.TryGetValue(monsterId, out int count) ? count : 0;
    }

    public int GetRemainingMonsterCapacity()
    {
        int remaining = MaxMonsterCapacity - GetTotalMonsterCount();
        return remaining > 0 ? remaining : 0;
    }

    public int AddMonsterId(int monsterId, int count = 1)
    {
        if (count <= 0)
        {
            return 0;
        }

        EnsureMonsterDictionaryInitialized();

        int remainingCapacity = GetRemainingMonsterCapacity();
        if (remainingCapacity <= 0)
        {
            return 0;
        }

        int addCount = count < remainingCapacity ? count : remainingCapacity;
        MonsterIds[monsterId] = GetMonsterIdCount(monsterId) + addCount;
        return addCount;
    }

    public int RemoveMonsterId(int monsterId, int count = 1)
    {
        if (count <= 0)
        {
            return 0;
        }

        EnsureMonsterDictionaryInitialized();
        if (!MonsterIds.TryGetValue(monsterId, out int currentCount))
        {
            return 0;
        }

        int removedCount = count < currentCount ? count : currentCount;
        int remainCount = currentCount - removedCount;
        if (remainCount > 0)
        {
            MonsterIds[monsterId] = remainCount;
        }
        else
        {
            MonsterIds.Remove(monsterId);
        }

        return removedCount;
    }

    public int GetTotalMonsterCount()
    {
        EnsureMonsterDictionaryInitialized();

        int totalCount = 0;
        foreach (int monsterId in MonsterIds.Keys)
        {
            totalCount += MonsterIds[monsterId];
        }

        return totalCount;
    }

    public List<int> GetMonsterIdList()
    {
        EnsureMonsterDictionaryInitialized();

        List<int> result = new List<int>();
        foreach (int monsterId in MonsterIds.Keys)
        {
            int count = MonsterIds[monsterId];
            for (int index = 0; index < count; index++)
            {
                result.Add(monsterId);
            }
        }

        return result;
    }

    public int GetCharacterCardIdCount(int cardId)
    {
        EnsureCharacterCardDictionaryInitialized();
        return CharacterCardIds.TryGetValue(cardId, out int count) ? count : 0;
    }

    public int AddCharacterCardId(int cardId, int count = 1)
    {
        if (count <= 0)
        {
            return 0;
        }

        EnsureCharacterCardDictionaryInitialized();
        CharacterCardIds[cardId] = GetCharacterCardIdCount(cardId) + count;
        return count;
    }

    public int RemoveCharacterCardId(int cardId, int count = 1)
    {
        if (count <= 0)
        {
            return 0;
        }

        EnsureCharacterCardDictionaryInitialized();
        if (!CharacterCardIds.TryGetValue(cardId, out int currentCount))
        {
            return 0;
        }

        int removedCount = count < currentCount ? count : currentCount;
        int remainCount = currentCount - removedCount;
        if (remainCount > 0)
        {
            CharacterCardIds[cardId] = remainCount;
        }
        else
        {
            CharacterCardIds.Remove(cardId);
        }

        return removedCount;
    }

    public int GetTotalCharacterCardCount()
    {
        EnsureCharacterCardDictionaryInitialized();

        int totalCount = 0;
        foreach (int cardId in CharacterCardIds.Keys)
        {
            totalCount += CharacterCardIds[cardId];
        }

        return totalCount;
    }

    public List<int> GetCharacterCardIdList()
    {
        EnsureCharacterCardDictionaryInitialized();

        List<int> result = new List<int>();
        foreach (int cardId in CharacterCardIds.Keys)
        {
            int count = CharacterCardIds[cardId];
            for (int index = 0; index < count; index++)
            {
                result.Add(cardId);
            }
        }

        return result;
    }
}
