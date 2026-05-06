using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using CardSimulator;

public partial class BattleSytem : Node
{
    public static BattleSytem Current { get; private set; }

    private static readonly Random RandomGenerator = new Random();

    private enum BattleInfoTab
    {
        Runtime,
        DrawPile,
        DiscardPile
    }

    private enum PileDisplayOrderMode
    {
        PileOrder,
        IdOrder
    }

    private const string DefaultCharacterCsvPath = "res://DataBase/Unit/Character.csv";
    private const string DefaultMonsterCsvPath = "res://DataBase/Unit/Monster.csv";
    private const string DefaultCardCsvPath = "res://DataBase/Card/通用/通用Card.csv";
    private const string DefaultCharacterDeckCsvPath = "res://DataBase/Unit/Character/CharacterDefaultDeck.csv";

    private const string BattleInfoLabelPath = "局内信息/对局信息滚动/对局信息显示";
    private const string BattleInfoLabelPathInRoot = "UI_Main/局内信息/对局信息滚动/对局信息显示";
    private const string RuntimeTabButtonPath = "局内信息/tab栏/局内";
    private const string RuntimeTabButtonPathInRoot = "UI_Main/局内信息/tab栏/局内";
    private const string DrawPileTabButtonPath = "局内信息/tab栏/抽牌堆详细";
    private const string DrawPileTabButtonPathInRoot = "UI_Main/局内信息/tab栏/抽牌堆详细";
    private const string DiscardPileTabButtonPath = "局内信息/tab栏/弃牌堆详细";
    private const string DiscardPileTabButtonPathInRoot = "UI_Main/局内信息/tab栏/弃牌堆详细";

    [Export]
    public BattleSetupData SetupData;

    [Export]
    public int SelectedCharacterId = 1001;

    [Export]
    public Godot.Collections.Array<int> SelectedMonsterIds = new Godot.Collections.Array<int>();

    [Export]
    public bool IsBattleStarted = false;

    [Export]
    public bool IsPlayerTurn = false;

    private BattleInfoTab CurrentBattleInfoTab = BattleInfoTab.Runtime;
    private PileDisplayOrderMode CurrentPileDisplayOrderMode = PileDisplayOrderMode.PileOrder;
    private string CachedRuntimeBattleInfo = string.Empty;

    // 玩家角色实例（唯一）
    public CharacterInstance Player;

    // 怪物实例字典，Key 为 UniqueInGameId
    public Dictionary<int, MonsterInstance> Monsters;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        Current = this;
        BindBattleInfoTabButtons();

        if (SetupData != null)
        {
            SelectedCharacterId = SetupData.CharacterId;
            SyncSelectedMonsterIdsFromSetupData();
        }
        else
        {
            RefreshBattleInfoDisplay();
        }

        RefreshBattleInfoDisplay();
    }

    public override void _ExitTree()
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }
    }

    public List<IUnitInstance> GetEnemyUnits(IUnitInstance source)
    {
        List<IUnitInstance> result = new List<IUnitInstance>();
        if (source == null)
        {
            return result;
        }

        if (source is CharacterInstance)
        {
            if (Monsters != null)
            {
                foreach (MonsterInstance monster in Monsters.Values)
                {
                    if (monster != null && monster.HP > 0)
                    {
                        result.Add(monster);
                    }
                }
            }
            return result;
        }

        if (source is MonsterInstance)
        {
            if (Player != null && Player.HP > 0)
            {
                result.Add(Player);
            }
        }

        return result;
    }

    public List<IUnitInstance> GetAllUnits()
    {
        List<IUnitInstance> result = new List<IUnitInstance>();
        if (Player != null && Player.HP > 0)
        {
            result.Add(Player);
        }

        if (Monsters != null)
        {
            foreach (MonsterInstance monster in Monsters.Values)
            {
                if (monster != null && monster.HP > 0)
                {
                    result.Add(monster);
                }
            }
        }

        return result;
    }

    public bool StartGameFromSetupData()
    {
        if (IsBattleStarted)
        {
            AppendPanelConsoleError("错误：当前战斗已经开始，不能重复开始游戏。");
            return false;
        }

        if (SetupData == null)
        {
            AppendPanelConsoleError("错误：BattleSetupData 为空，无法开始游戏。");
            return false;
        }

        SetupData.EnsureMonsterDictionaryInitialized();
        EnsureUnitCachesLoaded();

        if (SetupData.CharacterId <= 0)
        {
            AppendPanelConsoleError("错误：BattleSetupData 缺少有效的 CharacterID，无法开始游戏。");
            return false;
        }

        if (!LoadingSystem.CharacterDictionary.ContainsKey(SetupData.CharacterId))
        {
            AppendPanelConsoleError($"错误：CharacterID {SetupData.CharacterId} 未在角色配置中找到，无法开始游戏。");
            return false;
        }

        if (SetupData.GetTotalMonsterCount() <= 0)
        {
            AppendPanelConsoleError("错误：BattleSetupData 缺少 MonsterID，无法开始游戏。");
            return false;
        }

        List<int> monsterIds = SetupData.GetMonsterIdList();
        for (int index = 0; index < monsterIds.Count; index++)
        {
            int monsterId = monsterIds[index];
            if (!LoadingSystem.MonsterDictionary.ContainsKey(monsterId))
            {
                AppendPanelConsoleError($"错误：MonsterID {monsterId} 未在怪物配置中找到，无法开始游戏。");
                return false;
            }
        }

        SelectedCharacterId = SetupData.CharacterId;
        SyncSelectedMonsterIdsFromSetupData();
        AppendPanelConsoleInfo($"开始游戏：角色ID={SetupData.CharacterId}，怪物数量={monsterIds.Count}。");
        OnInit(SetupData.CharacterId, monsterIds);
        return true;
    }

    /// <summary>
    /// 初始化战斗系统，根据指定的角色ID和怪物ID列表
    /// </summary>
    /// <param name="characterId">玩家角色ID</param>
    /// <param name="monsterIds">怪物ID列表</param>
    public void OnInit(int characterId, List<int> monsterIds)
    {
        EnsureUnitCachesLoaded();
        InitializePlayer(characterId);
        InitializeMonsters(monsterIds);

        CurrentPileDisplayOrderMode = PileDisplayOrderMode.PileOrder;

        InitializePlayerDrawPileFromCharacterCards();
        SelectIntentionsForAllMonsters();

        IsBattleStarted = true;
        IsPlayerTurn = false;
        StartPlayerTurn();
        RefreshBattleInfoDisplay();
    }

    /// <summary>
    /// 初始化战斗系统，根据指定的角色ID和单个怪物ID
    /// </summary>
    /// <param name="characterId">玩家角色ID</param>
    /// <param name="monsterId">怪物ID</param>
    public void OnInit(int characterId, int monsterId)
    {
        OnInit(characterId, new List<int> { monsterId });
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
    }

    public BattleSetupData EnsureSetupData()
    {
        if (SetupData == null)
        {
            SetupData = new BattleSetupData
            {
                CharacterId = SelectedCharacterId
            };
        }

        SetupData.EnsureMonsterDictionaryInitialized();
        RefreshBattleInfoDisplay();
        return SetupData;
    }

    public void SyncSelectedMonsterIdsFromSetupData()
    {
        SelectedMonsterIds.Clear();
        if (SetupData == null)
        {
            RefreshBattleInfoDisplay();
            return;
        }

        foreach (int monsterId in SetupData.GetMonsterIdList())
        {
            SelectedMonsterIds.Add(monsterId);
        }

        RefreshBattleInfoDisplay();
    }

    public void RefreshBattleInfoDisplay()
    {
        Node scene = GetTree().CurrentScene;
        if (scene == null)
        {
            return;
        }

        RichTextLabel battleInfoLabel = FindBattleInfoLabel(scene);

        if (battleInfoLabel == null)
        {
            return;
        }

        StringBuilder builder = new StringBuilder();
        if (IsBattleStarted && Player != null)
        {
            BuildRuntimeBattleInfo(builder);
        }
        else
        {
            BuildSetupBattleInfo(builder);
        }

        CachedRuntimeBattleInfo = builder.ToString();
        battleInfoLabel.Text = BuildCurrentBattleInfoDisplayText();
        UpdateBattleInfoTabVisualState(scene);
    }

    private void BindBattleInfoTabButtons()
    {
        Node scene = GetTree().CurrentScene;
        if (scene == null)
        {
            return;
        }

        Button runtimeButton = FindBattleInfoButton(scene, RuntimeTabButtonPath, RuntimeTabButtonPathInRoot);
        if (runtimeButton != null)
        {
            runtimeButton.Pressed += OnRuntimeBattleInfoTabPressed;
        }

        Button drawPileButton = FindBattleInfoButton(scene, DrawPileTabButtonPath, DrawPileTabButtonPathInRoot);
        if (drawPileButton != null)
        {
            drawPileButton.Pressed += OnDrawPileBattleInfoTabPressed;
        }

        Button discardPileButton = FindBattleInfoButton(scene, DiscardPileTabButtonPath, DiscardPileTabButtonPathInRoot);
        if (discardPileButton != null)
        {
            discardPileButton.Pressed += OnDiscardPileBattleInfoTabPressed;
        }

        UpdateBattleInfoTabVisualState(scene);
    }

    private void OnRuntimeBattleInfoTabPressed()
    {
        SwitchBattleInfoTab(BattleInfoTab.Runtime);
    }

    private void OnDrawPileBattleInfoTabPressed()
    {
        SwitchBattleInfoTab(BattleInfoTab.DrawPile);
    }

    private void OnDiscardPileBattleInfoTabPressed()
    {
        SwitchBattleInfoTab(BattleInfoTab.DiscardPile);
    }

    private void SwitchBattleInfoTab(BattleInfoTab tab)
    {
        CurrentBattleInfoTab = tab;

        Node scene = GetTree().CurrentScene;
        if (scene == null)
        {
            return;
        }

        RichTextLabel battleInfoLabel = FindBattleInfoLabel(scene);
        if (battleInfoLabel != null)
        {
            battleInfoLabel.Text = BuildCurrentBattleInfoDisplayText();
        }

        UpdateBattleInfoTabVisualState(scene);
    }

    private RichTextLabel FindBattleInfoLabel(Node scene)
    {
        RichTextLabel battleInfoLabel = scene.GetNodeOrNull<RichTextLabel>(BattleInfoLabelPath);
        if (battleInfoLabel != null)
        {
            return battleInfoLabel;
        }

        return scene.GetNodeOrNull<RichTextLabel>(BattleInfoLabelPathInRoot);
    }

    private Button FindBattleInfoButton(Node scene, string primaryPath, string fallbackPath)
    {
        Button button = scene.GetNodeOrNull<Button>(primaryPath);
        if (button != null)
        {
            return button;
        }

        return scene.GetNodeOrNull<Button>(fallbackPath);
    }

    private string BuildCurrentBattleInfoDisplayText()
    {
        return CurrentBattleInfoTab switch
        {
            BattleInfoTab.DrawPile => BuildPileDetailText("抽牌堆", Player?.drawpile),
            BattleInfoTab.DiscardPile => BuildPileDetailText("弃牌堆", Player?.discardpile),
            _ => CachedRuntimeBattleInfo
        };
    }

    private string BuildPileDetailText(string pileName, List<Card> cards)
    {
        StringBuilder builder = new StringBuilder();
        string orderDescription = CurrentPileDisplayOrderMode == PileDisplayOrderMode.PileOrder
            ? "牌堆顺序"
            : "ID排序（CardId升序，UniqueInGameId升序）";
        builder.AppendLine($"{pileName}（{orderDescription}）：");

        if (!IsBattleStarted || Player == null)
        {
            builder.Append("当前未开始战斗，暂无牌堆详情。\n\n");
            builder.Append(CachedRuntimeBattleInfo);
            return builder.ToString();
        }

        if (cards == null || cards.Count == 0)
        {
            builder.Append("无");
            return builder.ToString();
        }

        List<Card> cardsToDisplay = GetCardsForDisplay(cards);
        for (int index = 0; index < cardsToDisplay.Count; index++)
        {
            Card card = cardsToDisplay[index];
            string uniqueInGameId = string.IsNullOrWhiteSpace(card?.UniqueInGameId)
                ? "未生成"
                : card.UniqueInGameId;
            builder.AppendLine($"{index + 1}、{GetCardDisplayName(card)} CardId={card?.CardId ?? 0} UniqueInGameId={uniqueInGameId}");
        }

        return builder.ToString().TrimEnd();
    }

    private List<Card> GetCardsForDisplay(List<Card> cards)
    {
        List<Card> result = cards == null ? new List<Card>() : new List<Card>(cards);
        if (CurrentPileDisplayOrderMode == PileDisplayOrderMode.PileOrder)
        {
            return result;
        }

        result.Sort(CompareCardsByIdOrder);
        return result;
    }

    private int CompareCardsByIdOrder(Card left, Card right)
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

    private int ParseUniqueInGameIdNumericValue(string uniqueInGameId)
    {
        if (string.IsNullOrWhiteSpace(uniqueInGameId))
        {
            return int.MaxValue;
        }

        return int.TryParse(uniqueInGameId, out int parsed) ? parsed : int.MaxValue;
    }

    public bool TogglePileDisplayOrderMode()
    {
        CurrentPileDisplayOrderMode = CurrentPileDisplayOrderMode == PileDisplayOrderMode.PileOrder
            ? PileDisplayOrderMode.IdOrder
            : PileDisplayOrderMode.PileOrder;

        if (CurrentBattleInfoTab == BattleInfoTab.DrawPile || CurrentBattleInfoTab == BattleInfoTab.DiscardPile)
        {
            SwitchBattleInfoTab(CurrentBattleInfoTab);
        }

        return CurrentPileDisplayOrderMode == PileDisplayOrderMode.PileOrder;
    }

    private void UpdateBattleInfoTabVisualState(Node scene)
    {
        Button runtimeButton = FindBattleInfoButton(scene, RuntimeTabButtonPath, RuntimeTabButtonPathInRoot);
        Button drawPileButton = FindBattleInfoButton(scene, DrawPileTabButtonPath, DrawPileTabButtonPathInRoot);
        Button discardPileButton = FindBattleInfoButton(scene, DiscardPileTabButtonPath, DiscardPileTabButtonPathInRoot);

        UpdateBattleInfoButtonState(runtimeButton, CurrentBattleInfoTab == BattleInfoTab.Runtime);
        UpdateBattleInfoButtonState(drawPileButton, CurrentBattleInfoTab == BattleInfoTab.DrawPile);
        UpdateBattleInfoButtonState(discardPileButton, CurrentBattleInfoTab == BattleInfoTab.DiscardPile);
    }

    private void UpdateBattleInfoButtonState(Button button, bool isActive)
    {
        if (button == null)
        {
            return;
        }

        button.Disabled = isActive;
    }

    private void BuildRuntimeBattleInfo(StringBuilder builder)
    {
           builder.AppendLine($"角色ID：{Player.id} 名称：{Player.Name} UniqueInGameID：{FormatUniqueInGameId(Player.UniqueInGameId)} HP：{Player.HP}/{Player.Max_HP}（当前/最大） Atk：{Player.Attack} Def：{Player.Defend} Costs：{Player.costs} Shield：{Player.Shield}");
        builder.AppendLine("手牌：");

        if (Player.handcards == null || Player.handcards.Count == 0)
        {
            builder.AppendLine("无");
        }
        else
        {
            List<string> handCardParts = new List<string>();
            for (int index = 0; index < Player.handcards.Count; index++)
            {
                Card card = Player.handcards[index];
                handCardParts.Add($"{index + 1}、{GetCardDisplayName(card)}");
            }

            builder.AppendLine(string.Join(" ", handCardParts));
        }

        if (Monsters == null || Monsters.Count == 0)
        {
            builder.Append("无怪物");
            return;
        }

        List<int> monsterKeys = new List<int>(Monsters.Keys);
        monsterKeys.Sort();
        foreach (int monsterKey in monsterKeys)
        {
            MonsterInstance monster = Monsters[monsterKey];
            builder.AppendLine($"怪物ID：{monster.id} 名称：{monster.Name} UniqueInGameID：{FormatUniqueInGameId(monster.UniqueInGameId)} HP：{monster.HP}/{monster.Max_HP}（当前/最大） Atk：{monster.Attack} Def：{monster.Defend} Shield：{monster.Shield} 当前意图：{FormatSelectedMonsterIntention(monster)}");
        }
    }

    private string FormatUniqueInGameId(int uniqueInGameId)
    {
        return uniqueInGameId.ToString("D7");
    }

    private void BuildSetupBattleInfo(StringBuilder builder)
    {
        EnsureUnitCachesLoaded();

        string characterName = LoadingSystem.CharacterDictionary.TryGetValue(SelectedCharacterId, out Character character)
            ? character.Name
            : "未知";
        builder.AppendLine($"当前选中角色ID：{SelectedCharacterId} 名称：{characterName}");

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
            return;
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
    }

    private string GetCardDisplayName(Card card)
    {
        if (card == null)
        {
            return "空卡牌";
        }

        if (!string.IsNullOrWhiteSpace(card.CardName))
        {
            return card.CardName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(card.EffectDescription))
        {
            return card.EffectDescription.Trim();
        }

        return $"CardId={card.CardId}";
    }

    private Dictionary<int, int> GetConfiguredMonsterCounts()
    {
        Dictionary<int, int> monsterCounts = new Dictionary<int, int>();

        if (SetupData != null)
        {
            SetupData.EnsureMonsterDictionaryInitialized();
            foreach (int monsterId in SetupData.MonsterIds.Keys)
            {
                monsterCounts[monsterId] = SetupData.MonsterIds[monsterId];
            }

            return monsterCounts;
        }

        foreach (int monsterId in SelectedMonsterIds)
        {
            if (monsterCounts.ContainsKey(monsterId))
            {
                monsterCounts[monsterId]++;
            }
            else
            {
                monsterCounts[monsterId] = 1;
            }
        }

        return monsterCounts;
    }

    private Dictionary<int, int> GetConfiguredCharacterCardCounts()
    {
        Dictionary<int, int> cardCounts = new Dictionary<int, int>();

        if (SetupData == null)
        {
            return cardCounts;
        }

        SetupData.EnsureCharacterCardDictionaryInitialized();
        foreach (int cardId in SetupData.CharacterCardIds.Keys)
        {
            cardCounts[cardId] = SetupData.CharacterCardIds[cardId];
        }

        return cardCounts;
    }

    /// <summary>
    /// 初始化玩家角色实例，根据指定的角色ID
    /// </summary>
    /// <param name="characterId">角色ID</param>
    private void InitializePlayer(int characterId)
    {
        var characters = LoadingSystem.CharacterDictionary;
        if (characters.TryGetValue(characterId, out var character))
        {
            Player = new CharacterInstance(character);
            Player.OnDead = () =>
            {
                AppendPanelConsoleInfo("战斗结束：玩家已死亡。游戏结束。");
                EndGame();
            };
            AppendPanelConsoleInfo($"已创建角色 {Player.Name}（ID: {characterId}, UniqueInGameId: {Player.UniqueInGameId}）。");
        }
        else
        {
            AppendPanelConsoleError($"错误：角色ID {characterId} 未在缓存中找到。");
        }
    }

    /// <summary>
    /// 初始化怪物实例字典，根据指定的怪物ID列表
    /// </summary>
    /// <param name="monsterIds">怪物ID列表</param>
    private void InitializeMonsters(List<int> monsterIds)
    {
        var monsters = LoadingSystem.MonsterDictionary;
        Monsters = new Dictionary<int, MonsterInstance>();
        foreach (int id in monsterIds)
        {
            if (monsters.TryGetValue(id, out var monster))
            {
                var monsterInstance = new MonsterInstance(monster);
                monsterInstance.OnDead = CreateMonsterOnDeadCallback(monsterInstance);
                Monsters[monsterInstance.UniqueInGameId] = monsterInstance;
                AppendPanelConsoleInfo($"已创建怪物 {monsterInstance.Name}（模板ID: {id}, UniqueInGameId: {monsterInstance.UniqueInGameId}，字典key: {monsterInstance.UniqueInGameId}）。");
            }
            else
            {
                AppendPanelConsoleError($"错误：怪物ID {id} 未在缓存中找到。");
            }
        }
        AppendPanelConsoleInfo($"已初始化怪物数量：{Monsters.Count}。");
    }

    public int GetCurrentMonsterInstanceCount()
    {
        return Monsters == null ? 0 : Monsters.Count;
    }

    public int AddMonsterInstancesByTemplateId(int monsterId, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        EnsureUnitCachesLoaded();
        if (!LoadingSystem.MonsterDictionary.TryGetValue(monsterId, out Monster template))
        {
            return 0;
        }

        Monsters ??= new Dictionary<int, MonsterInstance>();

        int currentCount = Monsters.Count;
        int remainingCapacity = BattleSetupData.MaxMonsterCapacity - currentCount;
        if (remainingCapacity <= 0)
        {
            return 0;
        }

        int addCount = count < remainingCapacity ? count : remainingCapacity;
        int added = 0;
        for (int index = 0; index < addCount; index++)
        {
            MonsterInstance instance = new MonsterInstance(template);

            // 极低概率下若随机冲突，继续生成直到拿到可用UniqueInGameId。
            while (Monsters.ContainsKey(instance.UniqueInGameId))
            {
                instance = new MonsterInstance(template);
            }

            instance.OnDead = CreateMonsterOnDeadCallback(instance);
            Monsters[instance.UniqueInGameId] = instance;
            added++;

            AppendPanelConsoleInfo($"战斗中新增怪物 {instance.Name}（模板ID: {monsterId}, UniqueInGameId: {instance.UniqueInGameId}）。");
        }

        if (added > 0)
        {
            if (IsBattleStarted && IsPlayerTurn)
            {
                SelectIntentionsForAllMonsters();
            }

            AppendPanelConsoleInfo($"战斗中怪物总数：{Monsters.Count}/{BattleSetupData.MaxMonsterCapacity}。");
            RefreshBattleInfoDisplay();
        }

        return added;
    }

    /// <summary>
    /// 确保角色与怪物缓存已加载
    /// </summary>
    private void EnsureUnitCachesLoaded()
    {
        if (LoadingSystem.CardDictionary.Count == 0)
        {
            LoadingSystem.LoadCards(DefaultCardCsvPath, true);
        }

        if (LoadingSystem.CharacterDictionary.Count == 0)
        {
            LoadingSystem.LoadCharacters(DefaultCharacterCsvPath, true);
        }

        if (LoadingSystem.MonsterDictionary.Count == 0)
        {
            LoadingSystem.LoadMonsters(DefaultMonsterCsvPath, true);
        }
    }

    /// <summary>
    /// 根据卡牌 ID 向玩家的指定牌堆添加卡牌实例
    /// </summary>
    /// <param name="cardId">卡牌 ID</param>
    /// <param name="pileType">牌堆类型："hand"（手牌）、"draw"（抽牌堆）、"discard"（弃牌堆）</param>
    public void AddCardToPlayer(int cardId, string pileType)
    {
        if (Player == null)
        {
            AppendPanelConsoleError("错误：玩家角色尚未初始化。");
            return;
        }

        var cardTemplate = LoadingSystem.CardDictionary.TryGetValue(cardId, out var card) ? card : null;
        if (cardTemplate == null)
        {
            AppendPanelConsoleError($"错误：卡牌ID {cardId} 未在缓存中找到。");
            return;
        }

        Card cardInstance = cardTemplate.CreateRuntimeInstance();

        switch (pileType.ToLower())
        {
            case "hand":
                Player.handcards.Add(cardInstance);
                AppendPanelConsoleInfo($"已将卡牌ID {cardId} 加入手牌。UniqueInGameId: {cardInstance.UniqueInGameId}");
                break;
            case "draw":
                Player.drawpile.Add(cardInstance);
                AppendPanelConsoleInfo($"已将卡牌ID {cardId} 加入抽牌堆。UniqueInGameId: {cardInstance.UniqueInGameId}");
                break;
            case "discard":
                Player.discardpile.Add(cardInstance);
                AppendPanelConsoleInfo($"已将卡牌ID {cardId} 加入弃牌堆。UniqueInGameId: {cardInstance.UniqueInGameId}");
                break;
            default:
                AppendPanelConsoleError($"错误：无效牌堆类型 {pileType}，请使用 hand/draw/discard。");
                break;
        }

        RefreshBattleInfoDisplay();
    }

    public bool PlayHandCard(int handIndex, IUnitInstance target = null)
    {
        if (Player == null)
        {
            AppendPanelConsoleError("错误：玩家角色尚未初始化，无法出牌。");
            return false;
        }

        if (handIndex < 0 || handIndex >= Player.handcards.Count)
        {
            AppendPanelConsoleError($"错误：手牌索引 {handIndex} 超出范围，当前手牌数量为 {Player.handcards.Count}。");
            return false;
        }

        Card card = Player.handcards[handIndex];
        return PlayHandCard(card, target);
    }

    public bool PlayHandCard(string uniqueInGameId, IUnitInstance target = null)
    {
        if (string.IsNullOrWhiteSpace(uniqueInGameId))
        {
            AppendPanelConsoleError("错误：出牌失败，未提供卡牌 UniqueInGameId。");
            return false;
        }

        if (Player == null)
        {
            AppendPanelConsoleError("错误：玩家角色尚未初始化，无法出牌。");
            return false;
        }

        Card card = Player.handcards.Find(current => current.UniqueInGameId == uniqueInGameId);
        if (card == null)
        {
            AppendPanelConsoleError($"错误：手牌中不存在 UniqueInGameId={uniqueInGameId} 的卡牌。");
            return false;
        }

        return PlayHandCard(card, target);
    }

    public bool PlayHandCard(Card card, IUnitInstance target = null)
    {
        if (!IsBattleStarted)
        {
            AppendPanelConsoleError("错误：当前不在战斗中，无法出牌。");
            return false;
        }

        if (!IsPlayerTurn)
        {
            AppendPanelConsoleError("错误：当前不是玩家回合，无法出牌。");
            return false;
        }

        if (Player == null)
        {
            AppendPanelConsoleError("错误：玩家角色尚未初始化，无法出牌。");
            return false;
        }

        if (card == null)
        {
            AppendPanelConsoleError("错误：出牌失败，卡牌为空。");
            return false;
        }

        int handIndex = Player.handcards.IndexOf(card);
        if (handIndex < 0)
        {
            AppendPanelConsoleError($"错误：卡牌ID {card.CardId} 不在当前玩家手牌中，无法打出。");
            return false;
        }

        if (Player.costs < card.EnergyCost)
        {
            AppendPanelConsoleError($"错误：费用不足，打出卡牌ID {card.CardId} 需要 {card.EnergyCost} 点费用，当前仅有 {Player.costs} 点。");
            return false;
        }

        Card.CardApplyResult applyResult = card.Apply(Player, target);
        if (!applyResult.Success)
        {
            return false;
        }

        Player.costs -= card.EnergyCost;
        Player.handcards.RemoveAt(handIndex);
        Player.discardpile.Add(card);

        AppendPanelConsoleInfo($"玩家打出卡牌 CardId={card.CardId}，UniqueInGameId={card.UniqueInGameId}，消耗费用 {card.EnergyCost}，剩余费用 {Player.costs}。卡牌已移入弃牌堆。手牌剩余 {Player.handcards.Count} 张，弃牌堆当前 {Player.discardpile.Count} 张。");

        if (applyResult.EffectResult != null)
        {
            AppendPanelConsoleInfo($"卡牌结算：{applyResult.EffectResult.BuildSummary()}");
        }

        RefreshBattleInfoDisplay();
        CheckBattleEndAndHandle();

        return true;
    }

    public void EndPlayerTurn()
    {
        if (!IsBattleStarted)
        {
            AppendPanelConsoleError("错误：当前不在战斗中，无法结束回合。");
            return;
        }

        if (!IsPlayerTurn)
        {
            AppendPanelConsoleError("错误：当前不是玩家回合，无需重复结束回合。");
            return;
        }

        IsPlayerTurn = false;
        AppendPanelConsoleInfo("玩家回合结束。进入怪物回合。");
        StartMonsterTurn();
    }

    public void StartPlayerTurn()
    {
        if (!IsBattleStarted || Player == null)
        {
            return;
        }

        if (CheckBattleEndAndHandle())
        {
            return;
        }

        IsPlayerTurn = true;
        StateSystem.OnTurnStart(Player);
        if (Player.Shield > 0)
        {
            AppendPanelConsoleInfo($"玩家回合开始：角色护盾清零（{Player.Shield}->0）。");
            Player.Shield = 0;
        }

        int drawCount = Player.drawCardNum > 0 ? Player.drawCardNum : 0;
        int drawn = DrawCardsToHand(drawCount);
        Player.costs = Player.Max_costs;

        AppendPanelConsoleInfo($"玩家回合开始：抽牌 {drawn}/{drawCount}，费用重置为 {Player.costs}。");
        RefreshBattleInfoDisplay();
    }

    private void StartMonsterTurn()
    {
        if (!IsBattleStarted || Player == null)
        {
            return;
        }

        if (CheckBattleEndAndHandle())
        {
            return;
        }

        List<int> orderedKeys = Monsters == null ? new List<int>() : new List<int>(Monsters.Keys);
        orderedKeys.Sort();

        for (int index = 0; index < orderedKeys.Count; index++)
        {
            int uniqueInGameId = orderedKeys[index];
            if (Monsters == null || !Monsters.TryGetValue(uniqueInGameId, out MonsterInstance monster))
            {
                continue;
            }

            StateSystem.OnTurnStart(monster);

            if (monster.Shield > 0)
            {
                AppendPanelConsoleInfo($"怪物回合开始：{monster.Name}#{monster.UniqueInGameId} 护盾清零（{monster.Shield}->0）。");
                monster.Shield = 0;
            }
        }

        AppendPanelConsoleInfo($"怪物回合开始：本轮行动怪物数量 {orderedKeys.Count}。");

        foreach (int uniqueInGameId in orderedKeys)
        {
            if (Monsters == null || !Monsters.TryGetValue(uniqueInGameId, out MonsterInstance monster))
            {
                continue;
            }

            ExecuteMonsterIntention(monster);

            if (CheckBattleEndAndHandle())
            {
                return;
            }
        }

        SelectIntentionsForAllMonsters();
        RefreshBattleInfoDisplay();
        if (CheckBattleEndAndHandle())
        {
            return;
        }

        StartPlayerTurn();
    }

    private void SelectIntentionsForAllMonsters()
    {
        if (Monsters == null || Monsters.Count == 0)
        {
            return;
        }

        foreach (MonsterInstance monster in Monsters.Values)
        {
            SelectIntentionForMonster(monster);
        }
    }

    private void SelectIntentionForMonster(MonsterInstance monster)
    {
        if (monster == null || monster.HP <= 0)
        {
            return;
        }

        if (monster.Table == null || monster.Table.Length == 0)
        {
            monster.ClearSelectedIntention();
            return;
        }

        List<int> availableIndices = new List<int>();
        for (int index = 0; index < monster.Table.Length; index++)
        {
            int[][] intention = monster.Table[index];
            if (intention != null && intention.Length > 0)
            {
                availableIndices.Add(index);
            }
        }

        if (availableIndices.Count == 0)
        {
            monster.ClearSelectedIntention();
            return;
        }

        int randomPosition = RandomGenerator.Next(availableIndices.Count);
        int selectedIndex = availableIndices[randomPosition];
        monster.SetSelectedIntention(selectedIndex, monster.Table[selectedIndex]);
    }

    public bool TrySwitchMonsterIntention(int monsterUniqueInGameId, int intentionIndex, out string resultMessage)
    {
        resultMessage = string.Empty;

        if (!IsBattleStarted)
        {
            resultMessage = "当前不在战斗中，无法修改怪物意图。";
            return false;
        }

        if (Monsters == null || Monsters.Count == 0)
        {
            resultMessage = "当前没有已实例化怪物，无法修改意图。";
            return false;
        }

        if (!Monsters.TryGetValue(monsterUniqueInGameId, out MonsterInstance monster) || monster == null)
        {
            resultMessage = $"未找到怪物UniqueInGameID={monsterUniqueInGameId}。";
            return false;
        }

        if (monster.HP <= 0)
        {
            resultMessage = $"怪物UniqueInGameID={monsterUniqueInGameId} 已死亡，无法修改意图。";
            return false;
        }

        if (intentionIndex <= 0)
        {
            resultMessage = $"意图index={intentionIndex} 非法，意图序号从1开始。";
            return false;
        }

        if (monster.Table == null || monster.Table.Length == 0)
        {
            resultMessage = $"怪物 {monster.Name} 未配置任何意图。";
            return false;
        }

        int targetIndex = intentionIndex - 1;
        if (targetIndex >= monster.Table.Length)
        {
            resultMessage = $"怪物 {monster.Name} 的意图index={intentionIndex} 超出范围，当前共 {monster.Table.Length} 种意图。";
            return false;
        }

        int[][] targetIntention = monster.Table[targetIndex];
        if (targetIntention == null || targetIntention.Length == 0)
        {
            resultMessage = $"怪物 {monster.Name} 的第 {intentionIndex} 种意图为空，无法切换。";
            return false;
        }

        monster.SetSelectedIntention(targetIndex, targetIntention);
        RefreshBattleInfoDisplay();
        resultMessage = $"已将怪物 {monster.Name}#{monster.UniqueInGameId} 切换到第 {intentionIndex} 种意图：{FormatSelectedMonsterIntention(monster)}";
        return true;
    }

    private void ExecuteMonsterIntention(MonsterInstance monster)
    {
        if (monster == null || monster.HP <= 0)
        {
            return;
        }

        if (monster.SelectedIntention == null || monster.SelectedIntention.Length == 0)
        {
            SelectIntentionForMonster(monster);
        }

        if (monster.SelectedIntention == null || monster.SelectedIntention.Length == 0)
        {
            AppendPanelConsoleInfo($"怪物行动（{monster.Name}#{monster.UniqueInGameId}）跳过：未配置可执行意图。");
            return;
        }

        AppendPanelConsoleInfo($"怪物行动（{monster.Name}#{monster.UniqueInGameId}）执行意图：{FormatSelectedMonsterIntention(monster)}");

        foreach (int[] effectConfig in monster.SelectedIntention)
        {
            if (!TryExecuteMonsterEffect(monster, effectConfig, out string resultSummary))
            {
                continue;
            }

            AppendPanelConsoleInfo($"怪物行动（{monster.Name}#{monster.UniqueInGameId}）{resultSummary}");
        }
    }

    private bool TryExecuteMonsterEffect(MonsterInstance monster, int[] effectConfig, out string resultSummary)
    {
        resultSummary = string.Empty;

        if (monster == null || effectConfig == null || effectConfig.Length == 0)
        {
            return false;
        }

        EffectType effectType = (EffectType)effectConfig[0];
        int[] effectArgs = GetMonsterEffectArguments(effectConfig);

        switch (effectType)
        {
            case EffectType.Damage:
                if (Player == null || Player.HP <= 0)
                {
                    resultSummary = "伤害效果跳过：玩家目标不存在。";
                    return true;
                }

                EffectResult attackResult = EffectSystem.ApplyAttack(monster, Player, effectArgs);
                resultSummary = $"Damage：{attackResult.BuildSummary()}";
                return true;

            case EffectType.Shield:
                EffectResult shieldResult = EffectSystem.ApplyShield(monster, effectArgs);
                resultSummary = $"Shield：{shieldResult.BuildSummary()}";
                return true;

            default:
                AppendPanelConsoleError($"错误：怪物意图暂不支持效果类型 {effectType}。当前仅支持 Damage 与 Shield。");
                return false;
        }
    }

    private static int[] GetMonsterEffectArguments(int[] effectConfig)
    {
        if (effectConfig == null || effectConfig.Length <= 1)
        {
            return Array.Empty<int>();
        }

        int[] args = new int[effectConfig.Length - 1];
        Array.Copy(effectConfig, 1, args, 0, args.Length);
        return args;
    }

    private string FormatSelectedMonsterIntention(MonsterInstance monster)
    {
        if (monster == null || monster.SelectedIntention == null || monster.SelectedIntention.Length == 0)
        {
            return "无";
        }

        List<string> effectParts = new List<string>();
        foreach (int[] effectConfig in monster.SelectedIntention)
        {
            string effectText = FormatMonsterEffectPreview(monster, effectConfig);
            if (!string.IsNullOrWhiteSpace(effectText))
            {
                effectParts.Add(effectText);
            }
        }

        return effectParts.Count == 0 ? "无" : string.Join(" | ", effectParts);
    }

    private string FormatMonsterEffectPreview(MonsterInstance monster, int[] effectConfig)
    {
        if (monster == null || effectConfig == null || effectConfig.Length == 0)
        {
            return string.Empty;
        }

        EffectType effectType = (EffectType)effectConfig[0];
        int[] effectArgs = GetMonsterEffectArguments(effectConfig);

        switch (effectType)
        {
            case EffectType.Damage:
                return $"{effectType}+{Math.Max(0, monster.Attack + GetEffectArgument(effectArgs, 0))}";

            case EffectType.Shield:
                return $"{effectType}+{Math.Max(0, monster.Defend + GetEffectArgument(effectArgs, 0))}";

            case EffectType.AddState:
                return effectArgs.Length == 0
                    ? effectType.ToString()
                    : $"{effectType}+{GetEffectArgument(effectArgs, 1, 1)}({(StateType)GetEffectArgument(effectArgs, 0)})";

            case EffectType.ClearState:
                return effectArgs.Length == 0
                    ? effectType.ToString()
                    : $"{effectType}({(StateType)GetEffectArgument(effectArgs, 0)})";

            default:
                return effectType.ToString();
        }
    }

    private static int GetEffectArgument(int[] effectArgs, int index, int defaultValue = 0)
    {
        return effectArgs != null && index >= 0 && index < effectArgs.Length
            ? effectArgs[index]
            : defaultValue;
    }

    private int DrawCardsToHand(int count)
    {
        if (Player == null || count <= 0)
        {
            return 0;
        }

        int drawn = 0;
        for (int i = 0; i < count; i++)
        {
            if (Player.drawpile.Count == 0)
            {
                if (Player.discardpile.Count == 0)
                {
                    break;
                }

                Player.drawpile.AddRange(Player.discardpile);
                Player.discardpile.Clear();
                ShuffleCards(Player.drawpile);
                AppendPanelConsoleInfo("抽牌堆为空：已将弃牌堆随机洗牌后放回抽牌堆。");
            }

            Card topCard = Player.drawpile[0];
            Player.drawpile.RemoveAt(0);
            Player.handcards.Add(topCard);
            drawn++;
        }

        return drawn;
    }

    private void ShuffleCards(List<Card> cards)
    {
        if (cards == null || cards.Count <= 1)
        {
            return;
        }

        for (int index = cards.Count - 1; index > 0; index--)
        {
            int swapIndex = RandomGenerator.Next(index + 1);
            (cards[index], cards[swapIndex]) = (cards[swapIndex], cards[index]);
        }
    }

    private void InitializePlayerDrawPileFromCharacterCards()
    {
        if (Player == null)
        {
            return;
        }

        Player.handcards.Clear();
        Player.drawpile.Clear();
        Player.discardpile.Clear();

        List<int> configuredCardIds = SetupData == null ? new List<int>() : SetupData.GetCharacterCardIdList();
        if (configuredCardIds.Count > 0)
        {
            for (int index = 0; index < configuredCardIds.Count; index++)
            {
                int cardId = configuredCardIds[index];
                if (!LoadingSystem.CardDictionary.TryGetValue(cardId, out Card template))
                {
                    AppendPanelConsoleError($"错误：配置中的卡牌ID {cardId} 未在缓存中找到，已跳过。");
                    continue;
                }

                Player.drawpile.Add(template.CreateRuntimeInstance());
            }

            ShuffleCards(Player.drawpile);

            AppendPanelConsoleInfo($"角色配置卡组已实例化并洗牌后加入抽牌堆，共 {Player.drawpile.Count} 张。");
            return;
        }

        List<int> defaultCardIds = LoadingSystem.GetCharacterDefaultCardIdList(Player.id, DefaultCharacterDeckCsvPath, true);
        if (defaultCardIds.Count > 0)
        {
            foreach (int cardId in defaultCardIds)
            {
                if (!LoadingSystem.CardDictionary.TryGetValue(cardId, out Card template))
                {
                    AppendPanelConsoleError($"错误：角色 {Player.id} 默认卡组中的卡牌ID {cardId} 未在缓存中找到，已跳过。");
                    continue;
                }

                Player.drawpile.Add(template.CreateRuntimeInstance());
            }

            ShuffleCards(Player.drawpile);

            AppendPanelConsoleInfo($"已从角色 {Player.id} 默认卡组配置初始化并洗牌抽牌堆，共 {Player.drawpile.Count} 张。");
            return;
        }

        AppendPanelConsoleInfo($"角色 {Player.id} 无配置卡组也无默认卡组，抽牌堆为空。");
    }

    private System.Action CreateMonsterOnDeadCallback(MonsterInstance instance)
    {
        return () =>
        {
            int id = instance.UniqueInGameId;
            AppendPanelConsoleInfo($"怪物死亡：{instance.Name}（UniqueInGameId: {id}）。");
            Monsters?.Remove(id);
            RefreshBattleInfoDisplay();
            CheckBattleEndAndHandle();
        };
    }

    private bool CheckBattleEndAndHandle()
    {
        if (!IsBattleStarted)
        {
            return true;
        }

        if (Player == null || Player.HP <= 0)
        {
            return true;
        }

        if (Monsters == null || Monsters.Count == 0)
        {
            EndBattle();
            return true;
        }

        return false;
    }

    public void EndBattle()
    {
        int monsterCount = Monsters == null ? 0 : Monsters.Count;
        if (Monsters != null)
        {
            Monsters.Clear();
            Monsters = null;
        }

        IsBattleStarted = false;
        IsPlayerTurn = false;

        AppendPanelConsoleInfo($"战斗结束：已销毁怪物实例 {monsterCount} 个。");
        RefreshBattleInfoDisplay();
    }

    public void EndGame()
    {
        int monsterCount = Monsters == null ? 0 : Monsters.Count;
        bool hadPlayer = Player != null;

        if (Player != null)
        {
            Player.handcards?.Clear();
            Player.drawpile?.Clear();
            Player.discardpile?.Clear();
            Player = null;
        }

        if (Monsters != null)
        {
            Monsters.Clear();
            Monsters = null;
        }

        IsBattleStarted = false;
        IsPlayerTurn = false;

        AppendPanelConsoleInfo($"战斗结束：已销毁角色实例 {(hadPlayer ? 1 : 0)} 个，怪物实例 {monsterCount} 个。");
        RefreshBattleInfoDisplay();
    }

    public void AppendPanelConsoleInfo(string message)
    {
        AppendPanelConsole("[信息] " + message);
    }

    private void AppendPanelConsoleError(string message)
    {
        AppendPanelConsole("[错误] " + message);
    }

    private void AppendPanelConsole(string message)
    {
        Node scene = GetTree().CurrentScene;
        if (scene == null)
        {
            return;
        }

        RichTextLabel console = scene.GetNodeOrNull<RichTextLabel>("ConsoleContainer/Console");
        if (console == null)
        {
            console = scene.GetNodeOrNull<RichTextLabel>("UI_Main/ConsoleContainer/Console");
        }

        if (console == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(console.Text))
        {
            console.Text += "\n";
        }

        console.Text += message;
    }
}
