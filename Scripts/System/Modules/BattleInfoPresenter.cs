using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CardSimulator;

internal sealed class BattleInfoPresenter
{
    private readonly BattleSytem battle;

    public BattleInfoPresenter(BattleSytem battle)
    {
        this.battle = battle;
    }

    public string BuildCurrentBattleInfoDisplayText(BattleSytem.BattleInfoTab currentTab, BattleSytem.PileDisplayOrderMode currentPileDisplayOrderMode, string cachedRuntimeBattleInfo)
    {
        return currentTab switch
        {
            BattleSytem.BattleInfoTab.DrawPile => BuildAllPlayerPileDetailText("抽牌堆", currentPileDisplayOrderMode, cachedRuntimeBattleInfo),
            BattleSytem.BattleInfoTab.DiscardPile => BuildAllPlayerPileDetailText("弃牌堆", currentPileDisplayOrderMode, cachedRuntimeBattleInfo),
            BattleSytem.BattleInfoTab.ExhaustPile => BuildAllPlayerPileDetailText("消耗牌堆", currentPileDisplayOrderMode, cachedRuntimeBattleInfo),
            _ => cachedRuntimeBattleInfo
        };
    }

    public string BuildRuntimeBattleInfo()
    {
        StringBuilder builder = new StringBuilder();
        string pendingPrompt = battle.GetPendingCardSelectionPrompt();
        if (!string.IsNullOrWhiteSpace(pendingPrompt))
        {
            builder.AppendLine(pendingPrompt);
            builder.AppendLine();
        }

        List<CharacterInstance> alivePlayers = battle.GetAlivePlayers();
        for (int playerIndex = 0; playerIndex < alivePlayers.Count; playerIndex++)
        {
            CharacterInstance player = alivePlayers[playerIndex];
            builder.AppendLine($"角色ID：{player.id} 名称：{player.Name} UniqueInGameID：{FormatUniqueInGameId(player.UniqueInGameId)} HP：{player.HP}/{player.Max_HP}（当前/最大） Atk：{player.Attack} Def：{player.Defend} Costs：{player.costs} Shield：{player.Shield} 本局失去生命值：{battle.GetBattleLostHp(player)} 失去生命次数：{battle.GetBattleHpLossEventCount(player)}");
            builder.AppendLine($"当前状态：{FormatUnitStates(player)}");
            builder.AppendLine("手牌：");

            if (player.handcards == null || player.handcards.Count == 0)
            {
                builder.AppendLine("无");
            }
            else
            {
                List<string> handCardParts = new List<string>();
                for (int index = 0; index < player.handcards.Count; index++)
                {
                    Card card = player.handcards[index];
                    handCardParts.Add($"{index + 1}、{GetCardDisplayName(card)}");
                }

                builder.AppendLine(string.Join(" ", handCardParts));
            }

            if (playerIndex < alivePlayers.Count - 1)
            {
                builder.AppendLine();
            }
        }

        if (battle.Monsters == null || battle.Monsters.Count == 0)
        {
            builder.Append("无怪物");
            return builder.ToString();
        }

        List<int> monsterKeys = new List<int>(battle.Monsters.Keys);
        monsterKeys.Sort();
        foreach (int monsterKey in monsterKeys)
        {
            MonsterInstance monster = battle.Monsters[monsterKey];
            builder.AppendLine($"怪物ID：{monster.id} 名称：{monster.Name} UniqueInGameID：{FormatUniqueInGameId(monster.UniqueInGameId)} HP：{monster.HP}/{monster.Max_HP}（当前/最大） Atk：{monster.Attack} Def：{monster.Defend} Shield：{monster.Shield} 本局失去生命值：{battle.GetBattleLostHp(monster)} 失去生命次数：{battle.GetBattleHpLossEventCount(monster)} 当前意图：{battle.GetMonsterIntentionDisplay(monster)}");
            builder.AppendLine($"当前状态：{FormatUnitStates(monster)}");
        }

        return builder.ToString();
    }

    public string BuildSetupBattleInfo()
    {
        battle.EnsureUnitCachesLoaded();

        StringBuilder builder = new StringBuilder();
        List<int> configuredCharacterIds = battle.SetupData == null ? new List<int>() : battle.SetupData.GetCharacterIdList();
        if (configuredCharacterIds.Count == 0 && battle.SelectedCharacterId > 0)
        {
            configuredCharacterIds.Add(battle.SelectedCharacterId);
        }

        builder.AppendLine("当前选中角色：");
        if (configuredCharacterIds.Count == 0)
        {
            builder.AppendLine("无");
        }
        else
        {
            foreach (int characterId in configuredCharacterIds)
            {
                string characterName = LoadingSystem.CharacterDictionary.TryGetValue(characterId, out Character character)
                    ? character.Name
                    : "未知";
                builder.AppendLine($"{characterId}({characterName})");
            }
        }

        builder.Append("角色已添加卡牌：");
        Dictionary<int, int> cardCounts = GetConfiguredCharacterCardCounts();
        if (cardCounts.Count == 0)
        {
            builder.AppendLine("无");
        }
        else
        {
            List<int> cardIds = new List<int>(cardCounts.Keys);
            cardIds.Sort();
            List<string> cardParts = new List<string>();
            foreach (int cardId in cardIds)
            {
                string cardName = LoadingSystem.CardDictionary.TryGetValue(cardId, out Card cardTemplate)
                    ? GetCardDisplayName(cardTemplate)
                    : $"CardId={cardId}";
                cardParts.Add($"{cardName}x{cardCounts[cardId]}");
            }

            builder.AppendLine(string.Join(" ", cardParts));
        }

        builder.AppendLine("已存在敌人ID：");

        Dictionary<int, int> monsterCounts = GetConfiguredMonsterCounts();
        if (monsterCounts.Count == 0)
        {
            builder.Append("无");
            return builder.ToString();
        }

        List<int> monsterIds = new List<int>(monsterCounts.Keys);
        monsterIds.Sort();
        for (int index = 0; index < monsterIds.Count; index++)
        {
            int monsterId = monsterIds[index];
            string monsterName = LoadingSystem.MonsterDictionary.TryGetValue(monsterId, out Monster monster)
                ? monster.Name
                : "未知";
            builder.Append($"{monsterId}({monsterName}) x{monsterCounts[monsterId]}");
            if (index < monsterIds.Count - 1)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private string BuildAllPlayerPileDetailText(string pileName, BattleSytem.PileDisplayOrderMode currentPileDisplayOrderMode, string cachedRuntimeBattleInfo)
    {
        StringBuilder builder = new StringBuilder();
        string orderDescription = currentPileDisplayOrderMode == BattleSytem.PileDisplayOrderMode.PileOrder
            ? "牌堆顺序"
            : "ID排序（CardId升序，UniqueInGameId升序）";
        builder.AppendLine($"{pileName}（{orderDescription}）：");

        List<CharacterInstance> alivePlayers = battle.GetAlivePlayers();
        if (!battle.IsBattleStarted || alivePlayers.Count == 0)
        {
            builder.Append("当前未开始战斗，暂无牌堆详情。\n\n");
            builder.Append(cachedRuntimeBattleInfo);
            return builder.ToString();
        }

        for (int playerIndex = 0; playerIndex < alivePlayers.Count; playerIndex++)
        {
            CharacterInstance player = alivePlayers[playerIndex];
            List<Card> cards = GetPlayerPileCards(player, pileName);
            builder.AppendLine($"[{playerIndex + 1}] {player.Name} UniqueInGameID={FormatUniqueInGameId(player.UniqueInGameId)}");

            if (cards == null || cards.Count == 0)
            {
                builder.AppendLine("无");
            }
            else
            {
                List<Card> cardsToDisplay = GetCardsForDisplay(cards, currentPileDisplayOrderMode);
                for (int index = 0; index < cardsToDisplay.Count; index++)
                {
                    Card card = cardsToDisplay[index];
                    string uniqueInGameId = string.IsNullOrWhiteSpace(card?.UniqueInGameId)
                        ? "未生成"
                        : card.UniqueInGameId;
                    builder.AppendLine($"{index + 1}、{GetCardDisplayName(card)} CardId={card?.CardId ?? 0} UniqueInGameId={uniqueInGameId}");
                }
            }

            if (playerIndex < alivePlayers.Count - 1)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static List<Card> GetPlayerPileCards(CharacterInstance player, string pileName)
    {
        if (string.Equals(pileName, "抽牌堆", StringComparison.Ordinal))
        {
            return player.drawpile;
        }

        if (string.Equals(pileName, "消耗牌堆", StringComparison.Ordinal))
        {
            return player.ExhaustPile;
        }

        return player.discardpile;
    }

    private static List<Card> GetCardsForDisplay(List<Card> cards, BattleSytem.PileDisplayOrderMode currentPileDisplayOrderMode)
    {
        List<Card> result = cards == null ? new List<Card>() : new List<Card>(cards);
        if (currentPileDisplayOrderMode == BattleSytem.PileDisplayOrderMode.PileOrder)
        {
            return result;
        }

        result.Sort(CompareCardsByIdOrder);
        return result;
    }

    private static int CompareCardsByIdOrder(Card left, Card right)
    {
        int leftCardId = left?.CardId ?? int.MaxValue;
        int rightCardId = right?.CardId ?? int.MaxValue;
        int cardIdCompare = leftCardId.CompareTo(rightCardId);
        if (cardIdCompare != 0)
        {
            return cardIdCompare;
        }

        int leftUniqueNumeric = ParseUniqueInGameIdNumericValue(left?.UniqueInGameId);
        int rightUniqueNumeric = ParseUniqueInGameIdNumericValue(right?.UniqueInGameId);
        int uniqueNumericCompare = leftUniqueNumeric.CompareTo(rightUniqueNumeric);
        if (uniqueNumericCompare != 0)
        {
            return uniqueNumericCompare;
        }

        string leftUnique = left?.UniqueInGameId ?? string.Empty;
        string rightUnique = right?.UniqueInGameId ?? string.Empty;
        return string.Compare(leftUnique, rightUnique, StringComparison.Ordinal);
    }

    private static int ParseUniqueInGameIdNumericValue(string uniqueInGameId)
    {
        if (string.IsNullOrWhiteSpace(uniqueInGameId))
        {
            return int.MaxValue;
        }

        return int.TryParse(uniqueInGameId, out int parsed) ? parsed : int.MaxValue;
    }

    private string FormatUnitStates(IUnitInstance unit)
    {
        if (unit == null || unit.States == null || unit.States.Count == 0)
        {
            return "无";
        }

        battle.EnsureUnitCachesLoaded();

        List<StateType> stateTypes = StateSystem.GetOrderedStateTypes(unit);

        List<string> stateParts = new List<string>();
        for (int index = 0; index < stateTypes.Count; index++)
        {
            StateType stateType = stateTypes[index];
            if (!unit.States.TryGetValue(stateType, out StateRuntimeData stateData) || stateData == null || stateData.Stacks <= 0)
            {
                continue;
            }

            string stateName = LoadingSystem.StateDictionary.TryGetValue(stateType, out StateDefinition definition) && !string.IsNullOrWhiteSpace(definition.Name)
                ? definition.Name
                : stateType.ToString();
            stateParts.Add($"{index + 1}、{stateName}({stateData.Stacks})");
        }

        return stateParts.Count == 0 ? "无" : string.Join(" ", stateParts);
    }

    private static string FormatUniqueInGameId(int uniqueInGameId)
    {
        return uniqueInGameId.ToString("D7");
    }

    private static string GetCardDisplayName(Card card)
    {
        if (card == null)
        {
            return "空卡牌";
        }

        string baseName;
        if (!string.IsNullOrWhiteSpace(card.CardName))
        {
            baseName = card.CardName.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(card.EffectDescription))
        {
            baseName = card.EffectDescription.Trim();
        }
        else
        {
            baseName = $"CardId={card.CardId}";
        }

        if (card.TotalUpgradeLevel <= 0)
        {
            return baseName;
        }

        if (card.HasKeyWord(CardKeyWord.InfiniteUpgrade))
        {
            return $"{baseName}+{card.TotalUpgradeLevel}";
        }

        return $"{baseName}+";
    }

    private Dictionary<int, int> GetConfiguredMonsterCounts()
    {
        Dictionary<int, int> monsterCounts = new Dictionary<int, int>();
        if (battle.SetupData != null)
        {
            battle.SetupData.EnsureMonsterDictionaryInitialized();
            foreach (int monsterId in battle.SetupData.MonsterIds.Keys)
            {
                int count = battle.SetupData.MonsterIds[monsterId];
                if (count <= 0)
                {
                    continue;
                }

                monsterCounts[monsterId] = count;
            }
        }

        if (monsterCounts.Count == 0 && battle.SelectedMonsterIds != null)
        {
            foreach (int monsterId in battle.SelectedMonsterIds)
            {
                if (monsterId <= 0)
                {
                    continue;
                }

                if (!monsterCounts.ContainsKey(monsterId))
                {
                    monsterCounts[monsterId] = 0;
                }

                monsterCounts[monsterId]++;
            }
        }

        return monsterCounts;
    }

    private Dictionary<int, int> GetConfiguredCharacterCardCounts()
    {
        Dictionary<int, int> cardCounts = new Dictionary<int, int>();
        if (battle.SetupData == null)
        {
            return cardCounts;
        }

        battle.SetupData.EnsureCharacterCardDictionaryInitialized();

        foreach (int cardId in battle.SetupData.CharacterCardIds.Keys)
        {
            if (cardId <= 0 || !battle.SetupData.CharacterCardIds.TryGetValue(cardId, out int count) || count <= 0)
            {
                continue;
            }

            cardCounts[cardId] = count;
        }

        return cardCounts;
    }
}