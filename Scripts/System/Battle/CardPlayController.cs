// CardPlayController.cs
// 卡牌打出全流程：PlayHandCard 5 重载、抽牌/弃牌/消耗、卡牌操作（升级类）的多步选牌流程。
// 不直接依赖 Godot（仅通过 battle 间接访问 IsBattleStarted / IsPlayerTurn 等 [Export] 字段和 GetTree()）。

using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator;

public sealed class CardOperationRequest
{
    public EffectType EffectType { get; }
    public CardOperationTargetType TargetType { get; }
    public int Count { get; }
    public bool RequireKilledTarget { get; }

    public CardOperationRequest(EffectType effectType, CardOperationTargetType targetType, int count, bool requireKilledTarget)
    {
        EffectType = effectType;
        TargetType = targetType;
        Count = count;
        RequireKilledTarget = requireKilledTarget;
    }
}

public sealed class PendingCardSelectionContext
{
    public CharacterInstance SourcePlayer { get; }
    public Card SourceCard { get; }
    public List<CardOperationRequest> Requests { get; }
    public int RequestIndex { get; set; }
    public List<Card> SelectedCards { get; } = new List<Card>();

    public PendingCardSelectionContext(CharacterInstance sourcePlayer, Card sourceCard, List<CardOperationRequest> requests)
    {
        SourcePlayer = sourcePlayer;
        SourceCard = sourceCard;
        Requests = requests ?? new List<CardOperationRequest>();
        RequestIndex = 0;
    }

    public CardOperationRequest CurrentRequest => RequestIndex >= 0 && RequestIndex < Requests.Count ? Requests[RequestIndex] : null;

    public int RemainingSelectionCount => CurrentRequest == null ? 0 : Math.Max(0, CurrentRequest.Count - SelectedCards.Count);
}

public sealed class CardPlayController
{
    private readonly BattleSytem battle;
    private readonly IBattleConsole console;
    private readonly IBattleUiRefresher uiRefresher;
    private readonly BattleUnitRegistry unitRegistry;
    private readonly BattleStatsTracker stats;
    private readonly StateCardPipeline statePipeline;
    private readonly Random random;

    private PendingCardSelectionContext pendingCardSelectionContext;

    public CardPlayController(
        BattleSytem battle,
        IBattleConsole console,
        IBattleUiRefresher uiRefresher,
        BattleUnitRegistry unitRegistry,
        BattleStatsTracker stats,
        StateCardPipeline statePipeline,
        Random random)
    {
        this.battle = battle ?? throw new ArgumentNullException(nameof(battle));
        this.console = console ?? throw new ArgumentNullException(nameof(console));
        this.uiRefresher = uiRefresher ?? throw new ArgumentNullException(nameof(uiRefresher));
        this.unitRegistry = unitRegistry ?? throw new ArgumentNullException(nameof(unitRegistry));
        this.stats = stats ?? throw new ArgumentNullException(nameof(stats));
        this.statePipeline = statePipeline ?? throw new ArgumentNullException(nameof(statePipeline));
        this.random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public bool HasPendingCardSelection => pendingCardSelectionContext != null;

    public void ClearPendingCardSelection() => pendingCardSelectionContext = null;

    // ============================================================
    // PlayHandCard 5 个重载
    // ============================================================

    public bool PlayHandCard(int handIndex, IUnitInstance target = null)
    {
        CharacterInstance player = battle.Player;
        if (player == null)
        {
            console.Error("错误：玩家角色尚未初始化。");
            return false;
        }
        if (handIndex < 0 || handIndex >= player.handcards.Count)
        {
            console.Error($"错误：手牌索引 {handIndex} 超出范围，当前手牌数量为 {player.handcards.Count}。");
            return false;
        }
        return PlayHandCard(player, player.handcards[handIndex], target);
    }

    public bool PlayHandCard(int playerUid, int handIndex, IUnitInstance target = null)
    {
        if (!TryResolvePlayerHandCardByIndex(playerUid, handIndex, out CharacterInstance player, out Card card, out string errorMessage))
        {
            console.Error(errorMessage);
            return false;
        }
        return PlayHandCard(player, card, target);
    }

    public bool PlayHandCard(string uniqueInGameId, IUnitInstance target = null)
    {
        if (!TryResolveDefaultPlayerHandCardByUniqueInGameId(uniqueInGameId, out CharacterInstance player, out Card card, out string errorMessage))
        {
            console.Error(errorMessage);
            return false;
        }
        return PlayHandCard(player, card, target);
    }

    public bool PlayHandCard(Card card, IUnitInstance target = null)
    {
        return PlayHandCard(battle.Player, card, target);
    }

    public bool PlayHandCard(CharacterInstance sourcePlayer, Card card, IUnitInstance target = null)
    {
        if (HasPendingCardSelection)
        {
            console.Error("错误：当前有待完成的选牌流程，请先使用“选择卡牌”按钮完成当前卡牌效果。");
            return false;
        }

        if (!battle.IsBattleStarted)
        {
            console.Error("错误：当前不在战斗中，无法出牌。");
            return false;
        }

        if (!battle.IsPlayerTurn)
        {
            console.Error("错误：当前不是玩家回合，无法出牌。");
            return false;
        }

        if (sourcePlayer == null)
        {
            console.Error("错误：玩家角色尚未初始化，无法出牌。");
            return false;
        }

        if (card == null)
        {
            console.Error("错误：出牌失败，卡牌为空。");
            return false;
        }

        int handIndex = sourcePlayer.handcards.IndexOf(card);
        if (handIndex < 0)
        {
            console.Error($"错误：卡牌ID {card.CardId} 不在当前玩家手牌中，无法打出。");
            return false;
        }

        int actualEnergyCost = card.GetCurrentEnergyCost(sourcePlayer);
        if (IsBattleCard(card) && StateSystem.TryGetStateStacks(sourcePlayer, StateType.NextBattleCardFree, out int freeStacks) && freeStacks > 0)
        {
            actualEnergyCost = 0;
            StateSystem.RemoveState(sourcePlayer, StateType.NextBattleCardFree);
            console.Info($"{sourcePlayer.Name} 的 NextBattleCardFree 生效，本张战斗牌免费。");
        }

        if (sourcePlayer.costs < actualEnergyCost)
        {
            console.Error($"错误：费用不足，打出卡牌ID {card.CardId} 需要 {actualEnergyCost} 点费用，当前仅有 {sourcePlayer.costs} 点。");
            return false;
        }

        if (!TryValidateCardPlayConditions(sourcePlayer, card, out string cardConditionError))
        {
            console.Error(cardConditionError);
            return false;
        }

        if (!TryBuildCardOperationRequests(card, out List<CardOperationRequest> cardOperationRequests, out string cardOperationError))
        {
            console.Error(cardOperationError);
            return false;
        }

        if (!ValidateCardOperationRequests(sourcePlayer, cardOperationRequests, out string cardOperationValidationError))
        {
            console.Error(cardOperationValidationError);
            return false;
        }

        Card.CardApplyResult applyResult = card.Apply(sourcePlayer, target);
        if (!applyResult.Success)
        {
            return false;
        }

        List<StateCardApplication> stateCardApplications = null;
        IUnitInstance statePileTarget = null;
        if (card.Category == CardCategory.State)
        {
            if (!statePipeline.TryResolveStateCardApplications(card, sourcePlayer, target, out stateCardApplications, out string stateCardError))
            {
                console.Error(stateCardError);
                return false;
            }

            statePileTarget = stateCardApplications[0].TargetUnit;
        }

        int costToDeduct = actualEnergyCost;
        sourcePlayer.costs -= costToDeduct;
        sourcePlayer.handcards.RemoveAt(handIndex);
        if (statePileTarget != null)
        {
            statePileTarget.StatePile.Add(card);
            statePipeline.RegisterStateCardEndCallbacks(stateCardApplications, card, sourcePlayer);
            console.Info($"玩家 {sourcePlayer.Name} 打出状态牌 CardId={card.CardId}，UniqueInGameId={card.UniqueInGameId}，消耗费用 {costToDeduct}，剩余费用 {sourcePlayer.costs}。卡牌已移入 {unitRegistry.BuildUnitLabel(statePileTarget)} 的状态牌堆。手牌剩余 {sourcePlayer.handcards.Count} 张。目标状态牌堆当前 {statePileTarget.StatePile.Count} 张。");
        }
        else if (card.HasKeyWord(CardKeyWord.Exhaust))
        {
            sourcePlayer.ExhaustPile.Add(card);
            console.Info($"玩家 {sourcePlayer.Name} 打出卡牌 CardId={card.CardId}，UniqueInGameId={card.UniqueInGameId}，消耗费用 {costToDeduct}，剩余费用 {sourcePlayer.costs}。卡牌已移入消耗牌堆。手牌剩余 {sourcePlayer.handcards.Count} 张，消耗牌堆当前 {sourcePlayer.ExhaustPile.Count} 张。");
        }
        else
        {
            sourcePlayer.discardpile.Add(card);
            console.Info($"玩家 {sourcePlayer.Name} 打出卡牌 CardId={card.CardId}，UniqueInGameId={card.UniqueInGameId}，消耗费用 {costToDeduct}，剩余费用 {sourcePlayer.costs}。卡牌已移入弃牌堆。手牌剩余 {sourcePlayer.handcards.Count} 张，弃牌堆当前 {sourcePlayer.discardpile.Count} 张。");
        }

        if (applyResult.EffectResult != null)
        {
            console.Info($"卡牌结算：{applyResult.EffectResult.BuildSummary()}");
        }

        battle.InvokeShowDamageNumbersForCardPlay(applyResult);

        stats.RecordCardPlayedThisTurn(sourcePlayer, card);
        StateSystem.OnCardPlayed(sourcePlayer, card);

        if (!TryExecuteCardOperations(sourcePlayer, card, applyResult, cardOperationRequests, out bool enteredPendingSelection, out string cardOperationMessage))
        {
            console.Error(cardOperationMessage);
            battle.InvokeRefreshBattleInfoDisplay();
            return false;
        }

        if (!string.IsNullOrWhiteSpace(cardOperationMessage))
        {
            console.Info(cardOperationMessage);
        }

        if (enteredPendingSelection)
        {
            battle.InvokeRefreshBattleInfoDisplay();
            return true;
        }

        battle.InvokeRefreshBattleInfoDisplay();
        battle.CheckBattleEndAndHandle();

        return true;
    }

    // ============================================================
    // 选牌流程（多步选牌）
    // ============================================================

    public string GetPendingCardSelectionPrompt()
    {
        if (pendingCardSelectionContext == null)
        {
            return string.Empty;
        }

        CardOperationRequest currentRequest = pendingCardSelectionContext.CurrentRequest;
        if (currentRequest == null)
        {
            return string.Empty;
        }

        string pileName = GetCardOperationPileDisplayName(currentRequest.TargetType);
        return $"当前流程暂停：{pendingCardSelectionContext.SourcePlayer.Name} 正在选择{pileName}中的卡牌，还需选择 {pendingCardSelectionContext.RemainingSelectionCount} 张。来源卡牌={unitRegistry.BuildCardLabel(pendingCardSelectionContext.SourceCard)}，效果={currentRequest.EffectType}。";
    }

    public bool TrySelectPendingHandCard(int handIndex, out string resultMessage)
    {
        resultMessage = string.Empty;
        if (pendingCardSelectionContext == null)
        {
            resultMessage = "错误：当前没有待完成的选牌流程。";
            return false;
        }

        CardOperationRequest currentRequest = pendingCardSelectionContext.CurrentRequest;
        if (currentRequest == null)
        {
            pendingCardSelectionContext = null;
            resultMessage = "错误：待选牌流程已失效。";
            return false;
        }

        CharacterInstance player = pendingCardSelectionContext.SourcePlayer;
        if (player == null)
        {
            pendingCardSelectionContext = null;
            resultMessage = "错误：待选牌流程对应的玩家不存在。";
            return false;
        }

        if (currentRequest.TargetType != CardOperationTargetType.SelectHandCards)
        {
            resultMessage = $"错误：当前待选流程不是手牌选择类型：{currentRequest.TargetType}。";
            return false;
        }

        if (handIndex < 0 || handIndex >= player.handcards.Count)
        {
            resultMessage = $"错误：手牌顺序 {handIndex + 1} 超出范围，当前手牌数量为 {player.handcards.Count}。";
            return false;
        }

        Card selectedCard = player.handcards[handIndex];
        if (selectedCard == null)
        {
            resultMessage = $"错误：手牌顺序 {handIndex + 1} 对应卡牌为空。";
            return false;
        }

        if (pendingCardSelectionContext.SelectedCards.Any(card => string.Equals(card?.UniqueInGameId, selectedCard.UniqueInGameId, StringComparison.Ordinal)))
        {
            resultMessage = $"错误：卡牌 {unitRegistry.BuildCardLabel(selectedCard)} 已被选择，本次效果不能重复选择同一张手牌。";
            return false;
        }

        pendingCardSelectionContext.SelectedCards.Add(selectedCard);
        console.Info($"选牌流程：已选择手牌 {handIndex + 1}，对应卡牌 {unitRegistry.BuildCardLabel(selectedCard)}。还需选择 {pendingCardSelectionContext.RemainingSelectionCount} 张。");

        if (pendingCardSelectionContext.RemainingSelectionCount > 0)
        {
            resultMessage = GetPendingCardSelectionPrompt();
            battle.InvokeRefreshBattleInfoDisplay();
            return true;
        }

        if (!TryAdvancePendingCardOperations(out resultMessage))
        {
            return false;
        }

        battle.InvokeRefreshBattleInfoDisplay();
        battle.CheckBattleEndAndHandle();
        return true;
    }

    // ============================================================
    // AddCardToPlayer（给 Interact 按钮用）
    // ============================================================

    public void AddCardToPlayer(int cardId, string pileType)
    {
        AddCardToPlayer(battle.Player?.UniqueInGameId ?? -1, cardId, pileType);
    }

    public void AddCardToPlayer(int playerUniqueInGameId, int cardId, string pileType)
    {
        if (!unitRegistry.TryGetPlayerByUniqueId(playerUniqueInGameId, out CharacterInstance player))
        {
            console.Error("错误：玩家角色尚未初始化。");
            return;
        }

        var cardTemplate = LoadingSystem.CardDictionary.TryGetValue(cardId, out var card) ? card : null;
        if (cardTemplate == null)
        {
            console.Error($"错误：卡牌ID {cardId} 未在缓存中找到。");
            return;
        }

        Card cardInstance = cardTemplate.CreateRuntimeInstance();

        switch (pileType.ToLower())
        {
            case "hand":
                player.handcards.Add(cardInstance);
                console.Info($"已将卡牌ID {cardId} 加入手牌。UniqueInGameId: {cardInstance.UniqueInGameId}");
                break;
            case "draw":
                player.drawpile.Add(cardInstance);
                console.Info($"已将卡牌ID {cardId} 加入抽牌堆。UniqueInGameId: {cardInstance.UniqueInGameId}");
                break;
            case "discard":
                player.discardpile.Add(cardInstance);
                console.Info($"已将卡牌ID {cardId} 加入弃牌堆。UniqueInGameId: {cardInstance.UniqueInGameId}");
                break;
            default:
                console.Error($"错误：无效牌堆类型 {pileType}，请使用 hand/draw/discard。");
                break;
        }

        uiRefresher.RequestRefresh();
    }

    // ============================================================
    // ApplyExhaustCards
    // ============================================================

    public EffectResult ApplyExhaustCards(IUnitInstance source, params string[] cardUniqueInGameIds)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (cardUniqueInGameIds == null || cardUniqueInGameIds.Length == 0)
        {
            return new EffectResult("ExhaustCards", source, null, summaryOverride: $"来源={unitRegistry.BuildUnitLabel(source)}，消耗卡牌=0（未提供卡牌实例UniqueInGameId）。");
        }

        if (source is not CharacterInstance player)
        {
            return new EffectResult("ExhaustCards", source, null, summaryOverride: $"来源={unitRegistry.BuildUnitLabel(source)}，当前仅支持玩家单位执行消耗卡牌效果。", totalValue: 0);
        }

        List<string> movedCardParts = new List<string>();
        List<string> missingIds = new List<string>();

        for (int index = 0; index < cardUniqueInGameIds.Length; index++)
        {
            string cardUniqueInGameId = cardUniqueInGameIds[index];
            if (string.IsNullOrWhiteSpace(cardUniqueInGameId))
            {
                continue;
            }

            if (!TryRemoveCardFromSupportedPiles(player, cardUniqueInGameId, out Card card, out string fromPileName))
            {
                missingIds.Add(cardUniqueInGameId);
                console.Error($"消耗效果：未在 {player.Name} 的手牌/抽牌堆/弃牌堆中找到 UniqueInGameId={cardUniqueInGameId} 的卡牌，已跳过。");
                continue;
            }

            player.ExhaustPile.Add(card);
            movedCardParts.Add($"{unitRegistry.BuildCardLabel(card)}（来自{fromPileName}）");
            console.Info($"消耗效果：{unitRegistry.BuildCardLabel(card)} 已从 {player.Name} 的{fromPileName}移入消耗牌堆。当前消耗牌堆 {player.ExhaustPile.Count} 张。");
        }

        if (movedCardParts.Count > 0)
        {
            battle.InvokeRefreshBattleInfoDisplay();
        }

        string summary = movedCardParts.Count > 0
            ? $"来源={unitRegistry.BuildUnitLabel(source)}，已消耗 {movedCardParts.Count} 张卡牌：{string.Join("、", movedCardParts)}"
            : $"来源={unitRegistry.BuildUnitLabel(source)}，未消耗任何卡牌。";

        if (missingIds.Count > 0)
        {
            summary += $"；未找到：{string.Join("、", missingIds)}";
        }

        return new EffectResult("ExhaustCards", source, null, summaryOverride: summary, totalValue: movedCardParts.Count);
    }

    public bool TryRemoveCardFromSupportedPiles(CharacterInstance player, string uniqueInGameId, out Card card, out string fromPileName)
    {
        card = null;
        fromPileName = string.Empty;
        if (player == null || string.IsNullOrWhiteSpace(uniqueInGameId))
        {
            return false;
        }

        if (TryRemoveCardFromPile(player.handcards, uniqueInGameId, out card))
        {
            fromPileName = "手牌";
            return true;
        }
        if (TryRemoveCardFromPile(player.drawpile, uniqueInGameId, out card))
        {
            fromPileName = "抽牌堆";
            return true;
        }
        if (TryRemoveCardFromPile(player.discardpile, uniqueInGameId, out card))
        {
            fromPileName = "弃牌堆";
            return true;
        }
        return false;
    }

    public static bool TryRemoveCardFromPile(List<Card> pile, string uniqueInGameId, out Card card)
    {
        card = null;
        if (pile == null || string.IsNullOrWhiteSpace(uniqueInGameId))
        {
            return false;
        }

        for (int index = 0; index < pile.Count; index++)
        {
            Card current = pile[index];
            if (current == null || !string.Equals(current.UniqueInGameId, uniqueInGameId, StringComparison.Ordinal))
            {
                continue;
            }
            pile.RemoveAt(index);
            card = current;
            return true;
        }
        return false;
    }

    public bool TryResolvePlayerHandCardByIndex(int playerUniqueInGameId, int handIndex, out CharacterInstance player, out Card card, out string errorMessage)
    {
        card = null;
        if (!battle.Commands.TryResolvePlayerForCommand(playerUniqueInGameId, "错误：玩家角色尚未初始化，无法出牌。", out player, out errorMessage))
        {
            return false;
        }

        if (handIndex < 0 || handIndex >= player.handcards.Count)
        {
            errorMessage = $"错误：手牌索引 {handIndex} 超出范围，当前手牌数量为 {player.handcards.Count}。";
            return false;
        }

        card = player.handcards[handIndex];
        errorMessage = string.Empty;
        return true;
    }

    public bool TryResolveDefaultPlayerHandCardByUniqueInGameId(string uniqueInGameId, out CharacterInstance player, out Card card, out string errorMessage)
    {
        player = battle.Player;
        card = null;
        if (string.IsNullOrWhiteSpace(uniqueInGameId))
        {
            errorMessage = "错误：出牌失败，未提供卡牌 UniqueInGameId。";
            return false;
        }

        if (player == null)
        {
            errorMessage = "错误：玩家角色尚未初始化。";
            return false;
        }

        for (int index = 0; index < player.handcards.Count; index++)
        {
            if (player.handcards[index] != null && string.Equals(player.handcards[index].UniqueInGameId, uniqueInGameId, StringComparison.Ordinal))
            {
                card = player.handcards[index];
                errorMessage = string.Empty;
                return true;
            }
        }

        errorMessage = $"错误：未在默认玩家的手牌中找到 UniqueInGameId={uniqueInGameId} 的卡牌。";
        return false;
    }

    // ============================================================
    // 抽牌 / 洗牌
    // ============================================================

    public int DrawCardsToHand(CharacterInstance player, int count)
    {
        if (player == null || count <= 0)
        {
            return 0;
        }

        if (StateSystem.TryGetStateStacks(player, StateType.DrawLock, out int _))
        {
            console.Info($"{player.Name} 受 DrawLock 影响，跳过抽牌。");
            return 0;
        }

        int drawn = 0;
        for (int i = 0; i < count; i++)
        {
            if (player.drawpile.Count == 0)
            {
                if (player.discardpile.Count == 0)
                {
                    break;
                }

                player.drawpile.AddRange(player.discardpile);
                player.discardpile.Clear();
                ShuffleCards(player.drawpile);
                console.Info($"{player.Name} 的抽牌堆为空：已将弃牌堆随机洗牌后放回抽牌堆。");
            }

            Card topCard = player.drawpile[0];
            player.drawpile.RemoveAt(0);
            player.handcards.Add(topCard);
            drawn++;
        }

        return drawn;
    }

    public void ShuffleCards(List<Card> cards)
    {
        if (cards == null || cards.Count <= 1)
        {
            return;
        }

        for (int index = cards.Count - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (cards[index], cards[swapIndex]) = (cards[swapIndex], cards[index]);
        }
    }

    public void InitializePlayerDrawPilesFromCharacterCards()
    {
        List<CharacterInstance> orderedPlayers = unitRegistry.GetOrderedPlayers();
        if (orderedPlayers.Count == 0)
        {
            return;
        }

        foreach (CharacterInstance player in orderedPlayers)
        {
            player.handcards.Clear();
            player.drawpile.Clear();
            player.discardpile.Clear();
            player.ExhaustPile.Clear();
            player.StatePile.Clear();

            battle.EnsurePlayerDefaultDeckInitialized(player);
            int defaultAddedCount = 0;
            foreach (Card deckCard in player.DefaultDeck)
            {
                if (deckCard == null)
                {
                    continue;
                }
                player.drawpile.Add(deckCard.CreateBattleInstanceFromDeckCard());
                defaultAddedCount++;
            }

            if (player.drawpile.Count > 0)
            {
                ShuffleCards(player.drawpile);
                console.Info($"角色 {player.id} 抽牌堆初始化完成：默认卡组实例 {defaultAddedCount} 张，共 {player.drawpile.Count} 张（已洗牌）。");
                continue;
            }

            console.Info($"角色 {player.id} 无默认卡组实例，抽牌堆为空。");
        }
    }

    public List<Card> GetCardsForCardOperation(CharacterInstance player, CardOperationTargetType targetType, int count)
    {
        List<Card> result = new List<Card>();
        if (player == null || count <= 0)
        {
            return result;
        }

        switch (targetType)
        {
            case CardOperationTargetType.SelectHandCards:
            case CardOperationTargetType.RandomHandCards:
                if (player.handcards != null && player.handcards.Count > 0)
                {
                    int take = Math.Min(count, player.handcards.Count);
                    List<Card> shuffled = new List<Card>(player.handcards);
                    ShuffleCards(shuffled);
                    for (int i = 0; i < take; i++)
                    {
                        result.Add(shuffled[i]);
                    }
                }
                break;
            default:
                break;
        }

        return result;
    }

    // ============================================================
    // 出牌前条件验证
    // ============================================================

    public bool TryValidateCardPlayConditions(CharacterInstance sourcePlayer, Card card, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (card == null) return true;
        if (card.ConditionParams == null || card.ConditionParams.Length == 0) return true;

        for (int index = 0; index < card.ConditionParams.Length; index++)
        {
            CardConditionType conditionType = card.ConditionParams[index];
            switch (conditionType)
            {
                case CardConditionType.NoBattleCardPlayedThisTurn:
                    if (stats.GetBattleCardsPlayedThisTurnCount(sourcePlayer) > 0)
                    {
                        errorMessage = $"错误：卡牌 {unitRegistry.BuildCardLabel(card)} 需要满足“本回合内该角色未打出过战斗牌”才能打出。";
                        return false;
                    }
                    break;
                case CardConditionType.None:
                    break;
                default:
                    errorMessage = $"错误：卡牌ID {card.CardId} 配置了未支持的条件枚举值 {conditionType}。";
                    return false;
            }
        }
        return true;
    }

    private static bool IsBattleCard(Card card) => card != null && card.Category != CardCategory.State;

    // ============================================================
    // 卡牌操作（升级类）—— 多步选牌的状态机
    // ============================================================

    public bool TryBuildCardOperationRequests(Card card, out List<CardOperationRequest> requests, out string errorMessage)
    {
        requests = new List<CardOperationRequest>();
        errorMessage = string.Empty;
        if (card == null || card.EffectTypes == null || card.EffectTypes.Length == 0)
        {
            return true;
        }

        for (int index = 0; index < card.EffectTypes.Length; index++)
        {
            EffectType effectType = card.EffectTypes[index];
            if (effectType != EffectType.UpgradeBattleCard && effectType != EffectType.UpgradePermanentCard)
            {
                continue;
            }

            int[] rawEffectParams = card.Params != null && index < card.Params.Length ? card.Params[index] : Array.Empty<int>();
            if (rawEffectParams.Length <= 0)
            {
                errorMessage = $"卡牌 {unitRegistry.BuildCardLabel(card)} 的卡牌操作效果 {effectType} 缺少目标类型参数。";
                return false;
            }

            CardOperationTargetType targetType = (CardOperationTargetType)rawEffectParams[0];
            if (!Enum.IsDefined(typeof(CardOperationTargetType), targetType) || targetType == CardOperationTargetType.None)
            {
                errorMessage = $"卡牌 {unitRegistry.BuildCardLabel(card)} 的卡牌操作效果 {effectType} 目标类型非法：{rawEffectParams[0]}。";
                return false;
            }

            int count = rawEffectParams.Length > 1 ? rawEffectParams[1] : 1;
            if (count <= 0)
            {
                errorMessage = $"卡牌 {unitRegistry.BuildCardLabel(card)} 的卡牌操作效果 {effectType} 目标数量必须大于 0。";
                return false;
            }

            bool requireKilledTarget = rawEffectParams.Length > 2 && rawEffectParams[2] > 0;
            requests.Add(new CardOperationRequest(effectType, targetType, count, requireKilledTarget));
        }

        return true;
    }

    public bool ValidateCardOperationRequests(CharacterInstance sourcePlayer, List<CardOperationRequest> requests, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (sourcePlayer == null || requests == null || requests.Count == 0)
        {
            return true;
        }

        int availableHandCountAfterPlay = Math.Max(0, sourcePlayer.handcards.Count - 1);
        foreach (CardOperationRequest request in requests)
        {
            if (request.TargetType == CardOperationTargetType.SelectHandCards || request.TargetType == CardOperationTargetType.RandomHandCards)
            {
                if (availableHandCountAfterPlay < request.Count)
                {
                    errorMessage = $"错误：{sourcePlayer.Name} 当前可供选择的手牌不足。打出当前卡后剩余 {availableHandCountAfterPlay} 张手牌，但效果 {request.EffectType} 需要选择 {request.Count} 张。";
                    return false;
                }
            }
            if (request.TargetType == CardOperationTargetType.RandomDefaultDeckCards && sourcePlayer.DefaultDeck.Count < request.Count)
            {
                errorMessage = $"错误：{sourcePlayer.Name} 的默认卡组实例不足 {request.Count} 张，无法执行 {request.EffectType}。";
                return false;
            }
        }
        return true;
    }

    public bool TryExecuteCardOperations(CharacterInstance sourcePlayer, Card sourceCard, Card.CardApplyResult applyResult, List<CardOperationRequest> requests, out bool enteredPendingSelection, out string resultMessage)
    {
        enteredPendingSelection = false;
        resultMessage = string.Empty;
        if (requests == null || requests.Count == 0)
        {
            return true;
        }

        List<string> messageParts = new List<string>();
        for (int index = 0; index < requests.Count; index++)
        {
            CardOperationRequest request = requests[index];
            if (request.RequireKilledTarget && !DidApplyResultKillTarget(applyResult))
            {
                messageParts.Add($"卡牌操作跳过：来源={unitRegistry.BuildCardLabel(sourceCard)}，效果={request.EffectType} 需要先击杀目标，本次未满足条件。");
                continue;
            }

            if (request.TargetType == CardOperationTargetType.RandomHandCards)
            {
                if (!TryApplyCardOperationToRandomHandCards(sourcePlayer, sourceCard, request, out string randomMessage))
                {
                    resultMessage = randomMessage;
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(randomMessage)) messageParts.Add(randomMessage);
                continue;
            }

            if (request.TargetType == CardOperationTargetType.RandomDefaultDeckCards)
            {
                if (!TryApplyCardOperationToRandomDefaultDeckCards(sourcePlayer, sourceCard, request, out string randomDeckMessage))
                {
                    resultMessage = randomDeckMessage;
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(randomDeckMessage)) messageParts.Add(randomDeckMessage);
                continue;
            }

            if (request.TargetType == CardOperationTargetType.SelectHandCards)
            {
                pendingCardSelectionContext = new PendingCardSelectionContext(sourcePlayer, sourceCard, requests.Skip(index).ToList());
                enteredPendingSelection = true;
                string prompt = GetPendingCardSelectionPrompt();
                if (!string.IsNullOrWhiteSpace(prompt)) messageParts.Add(prompt);
                resultMessage = string.Join("\n", messageParts.Where(part => !string.IsNullOrWhiteSpace(part)));
                return true;
            }

            resultMessage = $"错误：暂不支持的卡牌目标类型：{request.TargetType}。";
            return false;
        }

        resultMessage = string.Join("\n", messageParts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return true;
    }

    public bool TryAdvancePendingCardOperations(out string resultMessage)
    {
        resultMessage = string.Empty;
        if (pendingCardSelectionContext == null) return true;

        List<string> messageParts = new List<string>();
        while (pendingCardSelectionContext != null)
        {
            CardOperationRequest currentRequest = pendingCardSelectionContext.CurrentRequest;
            if (currentRequest == null)
            {
                pendingCardSelectionContext = null;
                break;
            }

            if (currentRequest.TargetType == CardOperationTargetType.SelectHandCards)
            {
                if (pendingCardSelectionContext.SelectedCards.Count < currentRequest.Count)
                {
                    string prompt = GetPendingCardSelectionPrompt();
                    if (!string.IsNullOrWhiteSpace(prompt)) messageParts.Add(prompt);
                    resultMessage = string.Join("\n", messageParts.Where(part => !string.IsNullOrWhiteSpace(part)));
                    return true;
                }
                if (!TryApplyCardOperationToCards(pendingCardSelectionContext.SourcePlayer, pendingCardSelectionContext.SourceCard, currentRequest, pendingCardSelectionContext.SelectedCards, out string applyMessage))
                {
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(applyMessage)) messageParts.Add(applyMessage);
                pendingCardSelectionContext.RequestIndex++;
                pendingCardSelectionContext.SelectedCards.Clear();
                continue;
            }

            if (currentRequest.TargetType == CardOperationTargetType.RandomHandCards)
            {
                if (!TryApplyCardOperationToRandomHandCards(pendingCardSelectionContext.SourcePlayer, pendingCardSelectionContext.SourceCard, currentRequest, out string randomMessage))
                {
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(randomMessage)) messageParts.Add(randomMessage);
                pendingCardSelectionContext.RequestIndex++;
                continue;
            }

            if (currentRequest.TargetType == CardOperationTargetType.RandomDefaultDeckCards)
            {
                if (!TryApplyCardOperationToRandomDefaultDeckCards(pendingCardSelectionContext.SourcePlayer, pendingCardSelectionContext.SourceCard, currentRequest, out string randomDeckMessage))
                {
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(randomDeckMessage)) messageParts.Add(randomDeckMessage);
                pendingCardSelectionContext.RequestIndex++;
                continue;
            }

            resultMessage = $"错误：暂不支持的卡牌目标类型：{currentRequest.TargetType}。";
            return false;
        }

        resultMessage = string.Join("\n", messageParts.Where(part => !string.IsNullOrWhiteSpace(part)));
        return true;
    }

    public bool TryApplyCardOperationToRandomHandCards(CharacterInstance sourcePlayer, Card sourceCard, CardOperationRequest request, out string resultMessage)
    {
        resultMessage = string.Empty;
        if (sourcePlayer == null)
        {
            resultMessage = "错误：随机选牌时来源玩家不存在。";
            return false;
        }
        if (sourcePlayer.handcards.Count < request.Count)
        {
            resultMessage = $"错误：{sourcePlayer.Name} 的手牌数量不足，无法随机选择 {request.Count} 张。";
            return false;
        }
        List<Card> candidates = new List<Card>(sourcePlayer.handcards);
        ShuffleCards(candidates);
        List<Card> selectedCards = candidates.Take(request.Count).ToList();
        return TryApplyCardOperationToCards(sourcePlayer, sourceCard, request, selectedCards, out resultMessage);
    }

    public bool TryApplyCardOperationToRandomDefaultDeckCards(CharacterInstance sourcePlayer, Card sourceCard, CardOperationRequest request, out string resultMessage)
    {
        resultMessage = string.Empty;
        if (sourcePlayer == null)
        {
            resultMessage = "错误：随机默认卡组选牌时来源玩家不存在。";
            return false;
        }
        if (sourcePlayer.DefaultDeck.Count < request.Count)
        {
            resultMessage = $"错误：{sourcePlayer.Name} 的默认卡组实例数量不足，无法随机选择 {request.Count} 张。";
            return false;
        }
        List<Card> candidates = new List<Card>(sourcePlayer.DefaultDeck);
        ShuffleCards(candidates);
        List<Card> selectedCards = candidates.Take(request.Count).ToList();
        return TryApplyCardOperationToCards(sourcePlayer, sourceCard, request, selectedCards, out resultMessage);
    }

    public bool TryApplyCardOperationToCards(CharacterInstance sourcePlayer, Card sourceCard, CardOperationRequest request, List<Card> targetCards, out string resultMessage)
    {
        resultMessage = string.Empty;
        if (sourcePlayer == null)
        {
            resultMessage = "错误：卡牌操作缺少来源玩家。";
            return false;
        }
        if (request == null || targetCards == null || targetCards.Count == 0) return true;

        List<string> upgradedParts = new List<string>();
        foreach (Card targetCard in targetCards)
        {
            if (targetCard == null) continue;
            switch (request.EffectType)
            {
                case EffectType.UpgradeBattleCard:
                    targetCard.BattleUpgradeLevel++;
                    upgradedParts.Add($"{unitRegistry.BuildCardLabel(targetCard)} 战斗内升级至 {targetCard.BattleUpgradeLevel}");
                    break;
                case EffectType.UpgradePermanentCard:
                    if (!TryApplyPermanentUpgrade(sourcePlayer, targetCard, out string permanentUpgradePart))
                    {
                        resultMessage = permanentUpgradePart;
                        return false;
                    }
                    upgradedParts.Add(permanentUpgradePart);
                    break;
                default:
                    resultMessage = $"错误：暂不支持的卡牌操作效果：{request.EffectType}。";
                    return false;
            }
        }

        string sourceLabel = sourceCard == null ? "无" : unitRegistry.BuildCardLabel(sourceCard);
        resultMessage = $"卡牌操作结算：来源={sourceLabel}，效果={request.EffectType}，目标={string.Join("、", upgradedParts)}。";
        return true;
    }

    public bool TryApplyPermanentUpgrade(CharacterInstance sourcePlayer, Card battleCard, out string resultMessage)
    {
        resultMessage = string.Empty;
        if (sourcePlayer == null || battleCard == null)
        {
            resultMessage = "错误：永久升级缺少必要的玩家或卡牌信息。";
            return false;
        }

        if (sourcePlayer.DefaultDeck.Any(card => ReferenceEquals(card, battleCard)))
        {
            battleCard.PermanentUpgradeLevel++;
            resultMessage = $"{unitRegistry.BuildCardLabel(battleCard)} 永久升级至 {battleCard.PermanentUpgradeLevel}";
            return true;
        }

        string sourceDeckCardUniqueInGameId = string.IsNullOrWhiteSpace(battleCard.SourceDeckCardUniqueInGameId)
            ? battleCard.UniqueInGameId
            : battleCard.SourceDeckCardUniqueInGameId;
        Card deckCard = sourcePlayer.DefaultDeck.FirstOrDefault(card => string.Equals(card?.UniqueInGameId, sourceDeckCardUniqueInGameId, StringComparison.Ordinal));
        if (deckCard == null)
        {
            resultMessage = $"错误：未在 {sourcePlayer.Name} 的默认卡组实例中找到来源卡牌 UniqueInGameId={sourceDeckCardUniqueInGameId}，无法执行永久升级。";
            return false;
        }

        deckCard.PermanentUpgradeLevel++;
        battleCard.PermanentUpgradeLevel = deckCard.PermanentUpgradeLevel;
        resultMessage = $"{unitRegistry.BuildCardLabel(battleCard)} 永久升级至 {deckCard.PermanentUpgradeLevel}（默认卡组来源={unitRegistry.BuildCardLabel(deckCard)}）";
        return true;
    }

    public static string GetCardOperationPileDisplayName(CardOperationTargetType targetType) => targetType switch
    {
        CardOperationTargetType.SelectHandCards => "手牌",
        CardOperationTargetType.RandomHandCards => "手牌",
        CardOperationTargetType.RandomDefaultDeckCards => "默认卡组",
        _ => "未知牌堆"
    };

    public static bool DidApplyResultKillTarget(Card.CardApplyResult applyResult)
    {
        if (applyResult?.EffectResult == null) return false;
        EffectResult effectResult = applyResult.EffectResult;
        return effectResult.Target != null
            && effectResult.TargetHpBefore > 0
            && effectResult.TargetHpAfter <= 0
            && effectResult.HpDamage > 0;
    }
}
