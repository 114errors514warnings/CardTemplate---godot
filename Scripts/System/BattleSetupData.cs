using Godot;
using System.Collections.Generic;

/// <summary>
/// 战斗初始化配置：保存角色ID与怪物ID数量映射，便于BattleSytem直接读取。
/// </summary>
[GlobalClass]
public partial class BattleSetupData : Resource
{
    public const int MaxMonsterCapacity = 10;
    public const int MaxCharacterCapacity = 3;

    [Export]
    public int CharacterId { get; set; } = 1001;

    [Export]
    public Godot.Collections.Array<int> CharacterOrder { get; set; } = new Godot.Collections.Array<int>();

    [Export]
    public Godot.Collections.Dictionary<int, int> CharacterIds { get; set; } = new Godot.Collections.Dictionary<int, int>();

    [Export]
    public Godot.Collections.Dictionary<int, int> MonsterIds { get; set; } = new Godot.Collections.Dictionary<int, int>();

    [Export]
    public Godot.Collections.Dictionary<int, int> CharacterCardIds { get; set; } = new Godot.Collections.Dictionary<int, int>();

    public void EnsureMonsterDictionaryInitialized()
    {
        MonsterIds ??= new Godot.Collections.Dictionary<int, int>();
    }

    public void EnsureCharacterDictionaryInitialized()
    {
        CharacterIds ??= new Godot.Collections.Dictionary<int, int>();
    }

    public void EnsureCharacterOrderInitialized()
    {
        CharacterOrder ??= new Godot.Collections.Array<int>();
        EnsureCharacterDictionaryInitialized();

        if (CharacterOrder.Count > 0)
        {
            SyncCharacterDictionaryFromOrder();
            return;
        }

        foreach (int characterId in CharacterIds.Keys)
        {
            int count = CharacterIds[characterId];
            for (int index = 0; index < count; index++)
            {
                CharacterOrder.Add(characterId);
            }
        }

        if (CharacterOrder.Count == 0 && CharacterId > 0)
        {
            CharacterOrder.Add(CharacterId);
        }

        SyncCharacterDictionaryFromOrder();
    }

    private void SyncCharacterDictionaryFromOrder()
    {
        EnsureCharacterDictionaryInitialized();
        CharacterIds.Clear();

        for (int index = 0; index < CharacterOrder.Count; index++)
        {
            int characterId = CharacterOrder[index];
            CharacterIds[characterId] = CharacterIds.TryGetValue(characterId, out int count) ? count + 1 : 1;
        }

        CharacterId = CharacterOrder.Count > 0 ? CharacterOrder[0] : 0;
    }

    public void EnsureCharacterCardDictionaryInitialized()
    {
        CharacterCardIds ??= new Godot.Collections.Dictionary<int, int>();
    }

    public int GetCharacterIdCount(int characterId)
    {
        EnsureCharacterOrderInitialized();
        return CharacterIds.TryGetValue(characterId, out int count) ? count : 0;
    }

    public int GetRemainingCharacterCapacity()
    {
        int remaining = MaxCharacterCapacity - GetTotalCharacterCount();
        return remaining > 0 ? remaining : 0;
    }

    public int AddCharacterId(int characterId, int count = 1)
    {
        if (count <= 0)
        {
            return 0;
        }

        EnsureCharacterOrderInitialized();

        int remainingCapacity = GetRemainingCharacterCapacity();
        if (remainingCapacity <= 0)
        {
            return 0;
        }

        int addCount = count < remainingCapacity ? count : remainingCapacity;
        for (int index = 0; index < addCount; index++)
        {
            CharacterOrder.Add(characterId);
        }

        SyncCharacterDictionaryFromOrder();
        return addCount;
    }

    public int RemoveCharacterId(int characterId, int count = 1)
    {
        if (count <= 0)
        {
            return 0;
        }

        EnsureCharacterOrderInitialized();
        int currentCount = GetCharacterIdCount(characterId);
        if (currentCount <= 0)
        {
            return 0;
        }

        int removedCount = count < currentCount ? count : currentCount;
        int remainToRemove = removedCount;
        for (int index = CharacterOrder.Count - 1; index >= 0 && remainToRemove > 0; index--)
        {
            if (CharacterOrder[index] != characterId)
            {
                continue;
            }

            CharacterOrder.RemoveAt(index);
            remainToRemove--;
        }

        SyncCharacterDictionaryFromOrder();
        return removedCount;
    }

    public bool SetCharacterIdAt(int index, int characterId)
    {
        EnsureCharacterOrderInitialized();
        if (index < 0 || index >= MaxCharacterCapacity)
        {
            return false;
        }

        while (CharacterOrder.Count <= index)
        {
            CharacterOrder.Add(0);
        }

        CharacterOrder[index] = characterId;
        for (int currentIndex = CharacterOrder.Count - 1; currentIndex >= 0; currentIndex--)
        {
            if (CharacterOrder[currentIndex] > 0)
            {
                break;
            }

            CharacterOrder.RemoveAt(currentIndex);
        }

        SyncCharacterDictionaryFromOrder();
        return true;
    }

    public bool TryGetCharacterIdAt(int index, out int characterId)
    {
        EnsureCharacterOrderInitialized();
        characterId = 0;
        if (index < 0 || index >= CharacterOrder.Count)
        {
            return false;
        }

        characterId = CharacterOrder[index];
        return characterId > 0;
    }

    public int RemoveLastCharacter()
    {
        EnsureCharacterOrderInitialized();
        if (CharacterOrder.Count == 0)
        {
            return 0;
        }

        int removedCharacterId = CharacterOrder[CharacterOrder.Count - 1];
        CharacterOrder.RemoveAt(CharacterOrder.Count - 1);
        SyncCharacterDictionaryFromOrder();
        return removedCharacterId;
    }

    public int GetTotalCharacterCount()
    {
        EnsureCharacterOrderInitialized();
        return CharacterOrder.Count;
    }

    public List<int> GetCharacterIdList()
    {
        EnsureCharacterOrderInitialized();

        List<int> result = new List<int>();
        for (int index = 0; index < CharacterOrder.Count; index++)
        {
            int characterId = CharacterOrder[index];
            if (characterId > 0)
            {
                result.Add(characterId);
            }
        }

        return result;
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
