using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using CardSimulator;

public partial class BattleSytem : Node
{

    private sealed class StateCardApplication
    {
        public IUnitInstance TargetUnit { get; }
        public StateType StateType { get; }
        public int Stacks { get; }

        public StateCardApplication(IUnitInstance targetUnit, StateType stateType, int stacks)
        {
            TargetUnit = targetUnit;
            StateType = stateType;
            Stacks = stacks;
        }
    }

    // 嵌套类 CardOperationRequest / PendingCardSelectionContext 已迁出至 CardPlayController.cs（同名 public 顶级类）

    public static BattleSytem Current { get; private set; }

    // 战斗事件总线（静态，解耦 EffectSystem 与 CardBattleScene）
    public static event System.Action<IUnitInstance, int> OnDamageApplied;   // (target, hpDamage)
    public static void RaiseOnDamageApplied(IUnitInstance target, int hpDamage) => OnDamageApplied?.Invoke(target, hpDamage);
    public static event System.Action OnPlayerTurnStart;   // 触发"己方回合"横幅
    public static event System.Action OnMonsterTurnStart;  // 触发"敌方回合"横幅 + 0.5s 间隔起点
    public static event System.Action<int, bool> OnMonsterIntentionHighlight; // (uniqueInGameId, on) 结算高亮

    internal static readonly Random RandomGenerator = new Random();

    // ============================================================
    // 拆分后的端口（默认指向 Godot 适配器，单测可通过 ConfigureForTesting 替换）
    // ============================================================
    public IBattleConsole Console { get; private set; }
    public IBattleUiRefresher UiRefresher { get; private set; }
    public IBattleScheduler Scheduler { get; private set; }

    // ============================================================
    // 拆分后的模块（按职责划入各 Battle/*.cs）
    // ============================================================
    public BattleStatsTracker Stats { get; private set; }
    public BattleUnitRegistry UnitRegistry { get; private set; }
    public StateCardPipeline StatePipeline { get; private set; }
    public BattleCommandRouter Commands { get; private set; }
    public CardPlayController CardPlay { get; private set; }

    public BattleSytem()
    {
        Console = new GodotConsoleAdapter(this);
        UiRefresher = new GodotUiRefresherAdapter(this);
        Scheduler = new GodotSchedulerAdapter(this);
        Stats = new BattleStatsTracker();
        UnitRegistry = new BattleUnitRegistry(this, RandomGenerator);
        StatePipeline = new StateCardPipeline(this, Console, UiRefresher, UnitRegistry);
        Commands = new BattleCommandRouter(this, Console, UiRefresher, UnitRegistry);
        CardPlay = new CardPlayController(this, Console, UiRefresher, UnitRegistry, Stats, StatePipeline, RandomGenerator);
    }

    /// <summary>
    /// 单测 / 特殊场景下替换默认实现。只能在 _Ready 之前调用。
    /// </summary>
    public void ConfigureForTesting(IBattleConsole console, IBattleUiRefresher uiRefresher, IBattleScheduler scheduler)
    {
        Console = console ?? new GodotConsoleAdapter(this);
        UiRefresher = uiRefresher ?? new GodotUiRefresherAdapter(this);
        Scheduler = scheduler ?? new GodotSchedulerAdapter(this);
        Stats = new BattleStatsTracker();
        UnitRegistry = new BattleUnitRegistry(this, RandomGenerator);
        StatePipeline = new StateCardPipeline(this, Console, UiRefresher, UnitRegistry);
        Commands = new BattleCommandRouter(this, Console, UiRefresher, UnitRegistry);
        CardPlay = new CardPlayController(this, Console, UiRefresher, UnitRegistry, Stats, StatePipeline, RandomGenerator);
    }

    internal enum BattleInfoTab
    {
        Runtime,
        DrawPile,
        DiscardPile,
        ExhaustPile
    }

    internal enum PileDisplayOrderMode
    {
        PileOrder,
        IdOrder
    }

    private const string BattleInfoLabelPath = "局内信息/对局信息滚动/对局信息显示";
    private const string BattleInfoLabelPathInRoot = "UI_Main/局内信息/对局信息滚动/对局信息显示";
    private const string RuntimeTabButtonPath = "局内信息/tab栏/局内";
    private const string RuntimeTabButtonPathInRoot = "UI_Main/局内信息/tab栏/局内";
    private const string DrawPileTabButtonPath = "局内信息/tab栏/抽牌堆详细";
    private const string DrawPileTabButtonPathInRoot = "UI_Main/局内信息/tab栏/抽牌堆详细";
    private const string DiscardPileTabButtonPath = "局内信息/tab栏/弃牌堆详细";
    private const string DiscardPileTabButtonPathInRoot = "UI_Main/局内信息/tab栏/弃牌堆详细";
    private const string ExhaustPileTabButtonPath = "局内信息/tab栏/消耗牌详细";
    private const string ExhaustPileTabButtonPathInRoot = "UI_Main/局内信息/tab栏/消耗牌详细";

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
    private int OrderedCombatLogDepth = 0;
    private readonly Queue<string> DeferredCombatInfoMessages = new Queue<string>();
    private readonly Queue<System.Action> DeferredDeathActions = new Queue<System.Action>();
    private BattleInfoPresenter battleInfoPresenter;
    private BattleInfoUiBinder battleInfoUiBinder;
    private MonsterIntentionService monsterIntentionService;
    private MonsterIntentionFormatter monsterIntentionFormatter;

    private BattleInfoPresenter BattleInfoPresenter => battleInfoPresenter ??= new BattleInfoPresenter(this);
    private BattleInfoUiBinder BattleInfoUiBinder => battleInfoUiBinder ??= new BattleInfoUiBinder();
    private MonsterIntentionService MonsterIntentionService => monsterIntentionService ??= new MonsterIntentionService(this);
    private MonsterIntentionFormatter MonsterIntentionFormatter => monsterIntentionFormatter ??= new MonsterIntentionFormatter(this);

    // 玩家角色实例，Key 为 UniqueInGameId
    public Dictionary<int, CharacterInstance> Players;

    // 兼容旧代码：返回当前第一个存活玩家，没有则返回第一个已实例化玩家。
    public CharacterInstance Player
    {
        get
        {
            List<CharacterInstance> alivePlayers = GetAlivePlayers();
            if (alivePlayers.Count > 0)
            {
                return alivePlayers[0];
            }

            if (Players == null || Players.Count == 0)
            {
                return null;
            }

            return Players.Values.OrderBy(player => player.UniqueInGameId).FirstOrDefault();
        }
    }

    // 怪物实例字典，Key 为 UniqueInGameId
    public Dictionary<int, MonsterInstance> Monsters;

    // 战斗内统计已迁出至 BattleStatsTracker（Stats）。

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

        UiRefresher.RequestRefresh();
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
            foreach (CharacterInstance player in GetAlivePlayers())
            {
                result.Add(player);
            }
        }

        return result;
    }

    public List<IUnitInstance> GetAllUnits()
    {
        List<IUnitInstance> result = new List<IUnitInstance>();

        foreach (CharacterInstance player in GetAlivePlayers())
        {
            result.Add(player);
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

    public List<IUnitInstance> GetAllyUnits(IUnitInstance source)
    {
        List<IUnitInstance> allies = new List<IUnitInstance>();
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
        }
        else
        {
            if (Monsters != null)
            {
                foreach (MonsterInstance monster in Monsters.Values)
                {
                    if (monster != null && monster.HP > 0 && monster.UniqueInGameId != source.UniqueInGameId)
                    {
                        allies.Add(monster);
                    }
                }
            }
        }

        return allies;
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
                    ShuffleCardList(shuffled);
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

    private static void ShuffleCardList(List<Card> cards)
    {
        if (cards == null || cards.Count <= 1)
        {
            return;
        }

        System.Random rng = new System.Random();
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
    }

    public int GetBattleLostHp(IUnitInstance unit) => Stats.GetBattleLostHp(unit);

    public int GetBattleHpLossEventCount(IUnitInstance unit) => Stats.GetBattleHpLossEventCount(unit);

    public void RecordHpLossEvent(IUnitInstance target, int hpLoss) => Stats.RecordHpLossEvent(target, hpLoss);

    public void OnUnitHpChanged(IUnitInstance unit, int oldHp, int newHp)
    {
        if (!IsBattleStarted || unit == null)
        {
            return;
        }

        if (newHp < oldHp)
        {
            int loss = oldHp - newHp;
            Stats.RecordHpLossEvent(unit, loss);
            StateSystem.OnHpLost(unit, loss);
        }
    }

    public int GetBattleCardsPlayedThisTurnCount(CharacterInstance player) => Stats.GetBattleCardsPlayedThisTurnCount(player);

    private static bool IsBattleCard(Card card)
    {
        return card != null && card.Category != CardCategory.State;
    }

    private void RecordCardPlayedThisTurn(CharacterInstance player, Card card) => Stats.RecordCardPlayedThisTurn(player, card);

    private void ResetBattleCardsPlayedThisTurnCounts(IEnumerable<CharacterInstance> players) => Stats.ResetBattleCardsPlayedThisTurnCounts(players);

    private bool TryValidateCardPlayConditions(CharacterInstance sourcePlayer, Card card, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (card == null)
        {
            return true;
        }

        if (card.ConditionParams == null || card.ConditionParams.Length == 0)
        {
            return true;
        }

        for (int index = 0; index < card.ConditionParams.Length; index++)
        {
	        CardConditionType conditionType = card.ConditionParams[index];
            switch (conditionType)
            {
                case CardConditionType.NoBattleCardPlayedThisTurn:
                    if (GetBattleCardsPlayedThisTurnCount(sourcePlayer) > 0)
                    {
                        errorMessage = $"错误：卡牌 {BuildCardLabel(card)} 需要满足“本回合内该角色未打出过战斗牌”才能打出。";
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

    public bool HasPendingCardSelection => CardPlay.HasPendingCardSelection;
    public string GetPendingCardSelectionPrompt() => CardPlay.GetPendingCardSelectionPrompt();
    public bool TrySelectPendingHandCard(int handIndex, out string resultMessage) => CardPlay.TrySelectPendingHandCard(handIndex, out resultMessage);

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

        List<int> characterIds = SetupData.GetCharacterIdList();
        if (characterIds.Count == 0)
        {
            AppendPanelConsoleError("错误：BattleSetupData 缺少有效的 CharacterID，无法开始游戏。");
            return false;
        }

        for (int index = 0; index < characterIds.Count; index++)
        {
            int characterId = characterIds[index];
            if (!LoadingSystem.CharacterDictionary.ContainsKey(characterId))
            {
                AppendPanelConsoleError($"错误：CharacterID {characterId} 未在角色配置中找到，无法开始游戏。");
                return false;
            }
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

        SelectedCharacterId = characterIds[0];
        SyncSelectedMonsterIdsFromSetupData();
        AppendPanelConsoleInfo($"开始游戏：角色数量={characterIds.Count}，怪物数量={monsterIds.Count}。");
        OnInit(characterIds, monsterIds);
        return true;
    }

    /// <summary>
    /// 初始化战斗系统，根据指定的角色ID和怪物ID列表
    /// </summary>
    /// <param name="characterId">玩家角色ID</param>
    /// <param name="monsterIds">怪物ID列表</param>
    public void OnInit(List<int> characterIds, List<int> monsterIds)
    {
        EnsureUnitCachesLoaded();
        CardPlay.ClearPendingCardSelection();
        InitializePlayers(characterIds);
        InitializeMonsters(monsterIds);
        SnapshotBattleInitialHp();

        CurrentPileDisplayOrderMode = PileDisplayOrderMode.PileOrder;

        CardPlay.InitializePlayerDrawPilesFromCharacterCards();
        SelectIntentionsForAllMonsters();

        IsBattleStarted = true;
        IsPlayerTurn = false;
        StartPlayerTurn();
        UiRefresher.RequestRefresh();
    }

    /// <summary>
    /// 初始化战斗系统，根据指定的角色ID和单个怪物ID
    /// </summary>
    /// <param name="characterId">玩家角色ID</param>
    /// <param name="monsterId">怪物ID</param>
    public void OnInit(int characterId, List<int> monsterIds)
    {
        OnInit(new List<int> { characterId }, monsterIds);
    }

    public void OnInit(int characterId, int monsterId)
    {
        OnInit(characterId, new List<int> { monsterId });
    }

    private void SnapshotBattleInitialHp()
    {
        Stats.Reset();
        Stats.SnapshotInitialHp(GetAllUnits());
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

        SetupData.EnsureCharacterDictionaryInitialized();
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

        UiRefresher.RequestRefresh();
    }

    public void RefreshBattleInfoDisplay()
    {
        Node scene = GetTree().CurrentScene;
        if (scene == null)
        {
            return;
        }

        RichTextLabel battleInfoLabel = BattleInfoUiBinder.FindBattleInfoLabel(scene, BattleInfoLabelPath, BattleInfoLabelPathInRoot);

        if (battleInfoLabel == null)
        {
            return;
        }

        if (IsBattleStarted && GetAlivePlayers().Count > 0)
        {
            CachedRuntimeBattleInfo = BattleInfoPresenter.BuildRuntimeBattleInfo();
        }
        else
        {
            CachedRuntimeBattleInfo = BattleInfoPresenter.BuildSetupBattleInfo();
        }

        battleInfoLabel.Text = BattleInfoPresenter.BuildCurrentBattleInfoDisplayText(CurrentBattleInfoTab, CurrentPileDisplayOrderMode, CachedRuntimeBattleInfo);
        BattleInfoUiBinder.UpdateTabVisualState(scene, RuntimeTabButtonPath, RuntimeTabButtonPathInRoot, DrawPileTabButtonPath, DrawPileTabButtonPathInRoot, DiscardPileTabButtonPath, DiscardPileTabButtonPathInRoot, ExhaustPileTabButtonPath, ExhaustPileTabButtonPathInRoot, CurrentBattleInfoTab);
    }

    private void BindBattleInfoTabButtons()
    {
        Node scene = GetTree().CurrentScene;
        if (scene == null)
        {
            return;
        }

        BattleInfoUiBinder.BindTabButtons(
            scene,
            RuntimeTabButtonPath,
            RuntimeTabButtonPathInRoot,
            DrawPileTabButtonPath,
            DrawPileTabButtonPathInRoot,
            DiscardPileTabButtonPath,
            DiscardPileTabButtonPathInRoot,
            ExhaustPileTabButtonPath,
            ExhaustPileTabButtonPathInRoot,
            OnRuntimeBattleInfoTabPressed,
            OnDrawPileBattleInfoTabPressed,
            OnDiscardPileBattleInfoTabPressed,
            OnExhaustPileBattleInfoTabPressed,
            CurrentBattleInfoTab);
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

    private void OnExhaustPileBattleInfoTabPressed()
    {
        SwitchBattleInfoTab(BattleInfoTab.ExhaustPile);
    }

    private void SwitchBattleInfoTab(BattleInfoTab tab)
    {
        CurrentBattleInfoTab = tab;

        Node scene = GetTree().CurrentScene;
        if (scene == null)
        {
            return;
        }

        RichTextLabel battleInfoLabel = BattleInfoUiBinder.FindBattleInfoLabel(scene, BattleInfoLabelPath, BattleInfoLabelPathInRoot);
        if (battleInfoLabel != null)
        {
            battleInfoLabel.Text = BattleInfoPresenter.BuildCurrentBattleInfoDisplayText(CurrentBattleInfoTab, CurrentPileDisplayOrderMode, CachedRuntimeBattleInfo);
        }

        BattleInfoUiBinder.UpdateTabVisualState(scene, RuntimeTabButtonPath, RuntimeTabButtonPathInRoot, DrawPileTabButtonPath, DrawPileTabButtonPathInRoot, DiscardPileTabButtonPath, DiscardPileTabButtonPathInRoot, ExhaustPileTabButtonPath, ExhaustPileTabButtonPathInRoot, CurrentBattleInfoTab);
    }

    public bool TogglePileDisplayOrderMode()
    {
        CurrentPileDisplayOrderMode = CurrentPileDisplayOrderMode == PileDisplayOrderMode.PileOrder
            ? PileDisplayOrderMode.IdOrder
            : PileDisplayOrderMode.PileOrder;

        if (CurrentBattleInfoTab == BattleInfoTab.DrawPile || CurrentBattleInfoTab == BattleInfoTab.DiscardPile || CurrentBattleInfoTab == BattleInfoTab.ExhaustPile)
        {
            SwitchBattleInfoTab(CurrentBattleInfoTab);
        }

        return CurrentPileDisplayOrderMode == PileDisplayOrderMode.PileOrder;
    }

    internal string FormatUniqueInGameId(int uniqueInGameId)
    {
        return uniqueInGameId.ToString("D7");
    }

    internal string GetMonsterIntentionDisplay(MonsterInstance monster)
    {
        return MonsterIntentionFormatter.FormatSelectedMonsterIntention(monster);
    }

    /// <summary>
    /// 初始化玩家角色实例，根据指定的角色ID
    /// </summary>
    /// <param name="characterId">角色ID</param>
    private List<CharacterInstance> GetOrderedPlayers()
    {
        if (Players == null || Players.Count == 0)
        {
            return new List<CharacterInstance>();
        }

        return Players.Values.OrderBy(player => player.UniqueInGameId).ToList();
    }

    public List<CharacterInstance> GetAlivePlayers()
    {
        if (Players == null || Players.Count == 0)
        {
            return new List<CharacterInstance>();
        }

        return Players.Values
            .Where(player => player != null && player.HP > 0)
            .OrderBy(player => player.UniqueInGameId)
            .ToList();
    }

    public bool TryGetPlayerByUniqueId(int uniqueInGameId, out CharacterInstance player)
    {
        player = null;
        return Players != null && Players.TryGetValue(uniqueInGameId, out player) && player != null;
    }

    public bool TryGetUnitByUniqueId(int uniqueInGameId, out IUnitInstance unit)
    {
        unit = null;
        if (TryGetPlayerByUniqueId(uniqueInGameId, out CharacterInstance player))
        {
            unit = player;
            return true;
        }

        if (Monsters != null && Monsters.TryGetValue(uniqueInGameId, out MonsterInstance monster) && monster != null)
        {
            unit = monster;
            return true;
        }

        return false;
    }

    private void InitializePlayers(List<int> characterIds)
    {
        var characters = LoadingSystem.CharacterDictionary;

        List<CharacterInstance> existingPlayers = GetOrderedPlayers();
        bool canReuseExistingPlayers = existingPlayers.Count == characterIds.Count;
        if (canReuseExistingPlayers)
        {
            for (int index = 0; index < characterIds.Count; index++)
            {
                if (existingPlayers[index].id != characterIds[index])
                {
                    canReuseExistingPlayers = false;
                    break;
                }
            }
        }

        if (canReuseExistingPlayers)
        {
            Players = existingPlayers.ToDictionary(player => player.UniqueInGameId, player => player);
            for (int idx = 0; idx < existingPlayers.Count; idx++)
            {
                CharacterInstance player = existingPlayers[idx];
                if (!characters.TryGetValue(player.id, out Character template))
                {
                    AppendPanelConsoleError($"错误：角色ID {player.id} 未在缓存中找到，无法复用角色实例。");
                    continue;
                }

                ResetPlayerForNewBattle(player, template);
                BindPlayerCallbacks(player);
                EnsurePlayerDefaultDeckInitialized(player, idx);
                AppendPanelConsoleInfo($"已复用角色 {player.Name}（ID: {player.id}, UniqueInGameId: {player.UniqueInGameId}），保留默认卡组实例 {player.DefaultDeck.Count} 张。" );
            }
            return;
        }

        Players = new Dictionary<int, CharacterInstance>();

        for (int idx = 0; idx < characterIds.Count; idx++)
        {
            int characterId = characterIds[idx];
            if (!characters.TryGetValue(characterId, out var character))
            {
                AppendPanelConsoleError($"错误：角色ID {characterId} 未在缓存中找到。");
                continue;
            }

            CharacterInstance player = new CharacterInstance(character);
            BindPlayerCallbacks(player);
            EnsurePlayerDefaultDeckInitialized(player, idx);
            Players[player.UniqueInGameId] = player;
            AppendPanelConsoleInfo($"已创建角色 {player.Name}（ID: {characterId}, UniqueInGameId: {player.UniqueInGameId}）。");
        }
    }

    private void BindPlayerCallbacks(CharacterInstance player)
    {
        if (player == null)
        {
            return;
        }

        player.OnStateEnded = CreateStateEndedCallback(player);
        player.OnDead = () =>
        {
            AppendPanelConsoleInfo($"玩家死亡：{player.Name}（UniqueInGameId: {player.UniqueInGameId}）。");
            if (GetAlivePlayers().Count == 0)
            {
                AppendPanelConsoleInfo("战斗结束：所有玩家已死亡。游戏结束。");
                EndGame();
            }
            else
            {
                RefreshBattleInfoDisplay();
            }
        };
    }

    private void ResetPlayerForNewBattle(CharacterInstance player, Character template)
    {
        if (player == null || template == null)
        {
            return;
        }

        player.id = template.id;
        player.Name = template.Name;
        player.MAX_HP = template.MAX_HP;
        player.Max_HP = template.MAX_HP;
        player.HP = template.MAX_HP;
        player.Attack = template.Ini_Attack;
        player.Defend = template.Ini_Defend;
        player.drawCardNum = template.drawCardNum;
        player.Shield = 0;
        player.Max_costs = 3;
        player.costs = 0;
        player.posx = 0;
        player.posy = 0;
        player.cards?.Clear();
        player.handcards?.Clear();
        player.drawpile?.Clear();
        player.discardpile?.Clear();
        player.ExhaustPile?.Clear();
        player.StatePile?.Clear();
        player.States?.Clear();
    }

    internal void EnsurePlayerDefaultDeckInitialized(CharacterInstance player, int playerIndex = -1)
    {
        if (player == null || player.DefaultDeck.Count > 0)
        {
            return;
        }

        List<int> defaultCardIds = LoadingSystem.GetCharacterDefaultCardIdListByKey(player.id, LoadingSystem.CharacterDefaultDeckCsvPathKey, true);
        List<int> configuredCardIds;
        if (playerIndex >= 0 && SetupData != null)
        {
            configuredCardIds = SetupData.GetCharacterCardIdListForPlayer(playerIndex);
        }
        else
        {
            configuredCardIds = SetupData == null ? new List<int>() : SetupData.GetCharacterCardIdList();
        }

        foreach (int cardId in defaultCardIds)
        {
            if (!LoadingSystem.CardDictionary.TryGetValue(cardId, out Card template))
            {
                AppendPanelConsoleError($"错误：角色 {player.id} 默认卡组中的卡牌ID {cardId} 未在缓存中找到，已跳过。" );
                continue;
            }

            player.DefaultDeck.Add(template.CreateDeckInstance());
        }

        foreach (int cardId in configuredCardIds)
        {
            if (!LoadingSystem.CardDictionary.TryGetValue(cardId, out Card template))
            {
                AppendPanelConsoleError($"错误：新增配置中的卡牌ID {cardId} 未在缓存中找到，已跳过。" );
                continue;
            }

            player.DefaultDeck.Add(template.CreateDeckInstance());
        }

        player.cards?.Clear();
        player.cards?.AddRange(player.DefaultDeck);
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
                monsterInstance.OnStateEnded = CreateStateEndedCallback(monsterInstance);
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

            instance.OnStateEnded = CreateStateEndedCallback(instance);
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
    internal void EnsureUnitCachesLoaded()
    {
        if (LoadingSystem.CardDictionary.Count == 0)
        {
            LoadingSystem.LoadCardsByKey(LoadingSystem.CardCsvPathKey, true);
        }

        if (LoadingSystem.CharacterDictionary.Count == 0)
        {
            LoadingSystem.LoadCharactersByKey(LoadingSystem.CharacterCsvPathKey, true);
        }

        if (LoadingSystem.MonsterDictionary.Count == 0)
        {
            LoadingSystem.LoadMonstersByKey(LoadingSystem.MonsterCsvPathKey, true);
        }

		if (LoadingSystem.StateDictionary.Count == 0)
		{
			LoadingSystem.LoadStatesByKey(LoadingSystem.StateCsvPathKey, true);
		}
    }

    /// <summary>
    /// 根据卡牌 ID 向玩家的指定牌堆添加卡牌实例
    /// </summary>
    /// <param name="cardId">卡牌 ID</param>
    /// <param name="pileType">牌堆类型："hand"（手牌）、"draw"（抽牌堆）、"discard"（弃牌堆）</param>
    public void AddCardToPlayer(int cardId, string pileType)
    {
        AddCardToPlayer(Player?.UniqueInGameId ?? -1, cardId, pileType);
    }

    public void AddCardToPlayer(int playerUniqueInGameId, int cardId, string pileType)
    {
        if (!TryGetPlayerByUniqueId(playerUniqueInGameId, out CharacterInstance player))
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
                player.handcards.Add(cardInstance);
                AppendPanelConsoleInfo($"已将卡牌ID {cardId} 加入手牌。UniqueInGameId: {cardInstance.UniqueInGameId}");
                break;
            case "draw":
                player.drawpile.Add(cardInstance);
                AppendPanelConsoleInfo($"已将卡牌ID {cardId} 加入抽牌堆。UniqueInGameId: {cardInstance.UniqueInGameId}");
                break;
            case "discard":
                player.discardpile.Add(cardInstance);
                AppendPanelConsoleInfo($"已将卡牌ID {cardId} 加入弃牌堆。UniqueInGameId: {cardInstance.UniqueInGameId}");
                break;
            default:
                AppendPanelConsoleError($"错误：无效牌堆类型 {pileType}，请使用 hand/draw/discard。");
                break;
        }

        UiRefresher.RequestRefresh();
    }

    public EffectResult ApplyExhaustCards(IUnitInstance source, params string[] cardUniqueInGameIds)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (cardUniqueInGameIds == null || cardUniqueInGameIds.Length == 0)
        {
            return new EffectResult("ExhaustCards", source, null, summaryOverride: $"来源={BuildUnitLabel(source)}，消耗卡牌=0（未提供卡牌实例UniqueInGameId）。");
        }

        if (source is not CharacterInstance player)
        {
            return new EffectResult("ExhaustCards", source, null, summaryOverride: $"来源={BuildUnitLabel(source)}，当前仅支持玩家单位执行消耗卡牌效果。", totalValue: 0);
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
                AppendPanelConsoleError($"消耗效果：未在 {player.Name} 的手牌/抽牌堆/弃牌堆中找到 UniqueInGameId={cardUniqueInGameId} 的卡牌，已跳过。");
                continue;
            }

            player.ExhaustPile.Add(card);
            movedCardParts.Add($"{BuildCardLabel(card)}（来自{fromPileName}）");
            AppendPanelConsoleInfo($"消耗效果：{BuildCardLabel(card)} 已从 {player.Name} 的{fromPileName}移入消耗牌堆。当前消耗牌堆 {player.ExhaustPile.Count} 张。");
        }

        if (movedCardParts.Count > 0)
        {
            RefreshBattleInfoDisplay();
        }

        string summary = movedCardParts.Count > 0
            ? $"来源={BuildUnitLabel(source)}，已消耗 {movedCardParts.Count} 张卡牌：{string.Join("、", movedCardParts)}"
            : $"来源={BuildUnitLabel(source)}，未消耗任何卡牌。";

        if (missingIds.Count > 0)
        {
            summary += $"；未找到：{string.Join("、", missingIds)}";
        }

        return new EffectResult("ExhaustCards", source, null, summaryOverride: summary, totalValue: movedCardParts.Count);
    }

    private bool TryRemoveCardFromSupportedPiles(CharacterInstance player, string uniqueInGameId, out Card card, out string fromPileName)
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

    private static bool TryRemoveCardFromPile(List<Card> pile, string uniqueInGameId, out Card card)
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

    private bool TryResolvePlayerHandCardByIndex(int playerUniqueInGameId, int handIndex, out CharacterInstance player, out Card card, out string errorMessage)
    {
        card = null;
        if (!TryResolvePlayerForCommand(playerUniqueInGameId, "错误：玩家角色尚未初始化，无法出牌。", out player, out errorMessage))
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

    private bool TryResolveDefaultPlayerHandCardByUniqueInGameId(string uniqueInGameId, out CharacterInstance player, out Card card, out string errorMessage)
    {
        player = Player;
        card = null;
        if (string.IsNullOrWhiteSpace(uniqueInGameId))
        {
            errorMessage = "错误：出牌失败，未提供卡牌 UniqueInGameId。";
            return false;
        }

        if (player == null)
        {
            errorMessage = "错误：玩家角色尚未初始化，无法出牌。";
            return false;
        }

        card = player.handcards.Find(current => current.UniqueInGameId == uniqueInGameId);
        if (card == null)
        {
            errorMessage = $"错误：手牌中不存在 UniqueInGameId={uniqueInGameId} 的卡牌。";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    public bool PlayHandCard(int handIndex, IUnitInstance target = null) => CardPlay.PlayHandCard(handIndex, target);
    public bool PlayHandCard(int playerUniqueInGameId, int handIndex, IUnitInstance target = null) => CardPlay.PlayHandCard(playerUniqueInGameId, handIndex, target);
    public bool PlayHandCard(string uniqueInGameId, IUnitInstance target = null) => CardPlay.PlayHandCard(uniqueInGameId, target);
    public bool PlayHandCard(Card card, IUnitInstance target = null) => CardPlay.PlayHandCard(card, target);
    public bool PlayHandCard(CharacterInstance sourcePlayer, Card card, IUnitInstance target = null) => CardPlay.PlayHandCard(sourcePlayer, card, target);

    public void EndPlayerTurn()
    {
        if (HasPendingCardSelection)
        {
            AppendPanelConsoleError("错误：当前有待完成的选牌流程，无法结束回合。");
            return;
        }

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

        // 弃置手牌
        foreach (CharacterInstance player in GetAlivePlayers())
        {
            var toDiscard = player.handcards.FindAll(c => !c.HasKeyWord(CardKeyWord.Retain));
            var toKeep = player.handcards.FindAll(c => c.HasKeyWord(CardKeyWord.Retain));
            player.discardpile.AddRange(toDiscard);
            player.handcards.Clear();
            player.handcards.AddRange(toKeep);

            // 移除运行时关键词中标记为回合结束移除的条目
            foreach (Card keptCard in toKeep)
            {
                keptCard.AppliedKeywords?.RemoveAll(e => e.Flags.HasFlag(KeywordFlag.RemoveAtTurnEnd));
            }

            if (toDiscard.Count > 0)
                AppendPanelConsoleInfo($"玩家 {player.Name} 回合结束：弃置手牌 {toDiscard.Count} 张{(toKeep.Count > 0 ? $"，保留 {toKeep.Count} 张" : string.Empty)}。");
            else if (toKeep.Count > 0)
                AppendPanelConsoleInfo($"玩家 {player.Name} 回合结束：所有手牌（{toKeep.Count} 张）均为保留牌，不弃置。");
        }

        AppendPanelConsoleInfo("玩家回合结束。进入怪物回合。");

        // 玩家回合结束时按 DecayTiming 衰减（如勇气铠甲 OnTurnEnd）
        foreach (CharacterInstance player in GetAlivePlayers())
        {
            StateDecayProcessor.ProcessDecayAtTiming(player, DecayTrigger.OnTurnEnd);
        }

        NotifyBattleSceneRefresh();
        StartMonsterTurn();
    }

    public void StartPlayerTurn()
    {
        List<CharacterInstance> alivePlayers = GetAlivePlayers();
        if (!IsBattleStarted || alivePlayers.Count == 0)
        {
            return;
        }

        if (CheckBattleEndAndHandle())
        {
            return;
        }

        OnPlayerTurnStart?.Invoke();
        IsPlayerTurn = true;
        ResetBattleCardsPlayedThisTurnCounts(alivePlayers);
        foreach (CharacterInstance player in alivePlayers)
        {
            StateSystem.OnTurnStart(player);
            // 先 OnTurnStart（给资源/护盾/cap）→ 后 ProcessDecayAtTiming（按 DecayMode=ClearAll 移除）
            StateDecayProcessor.ProcessDecayAtTiming(player, DecayTrigger.OnTurnStart);
            if (player.Shield > 0)
            {
                // 阵地 (ShieldCapEqualsHP) 例外：保留护盾但 cap 在当前 HP。
                bool hasShieldCapState = StateSystem.TryGetStateStacks(player, StateType.ShieldCapEqualsHP, out int _);
                if (hasShieldCapState)
                {
                    if (player.Shield > player.HP) player.Shield = player.HP;
                    AppendPanelConsoleInfo($"玩家回合开始：{player.Name} 阵地生效，护盾保留 {player.Shield}（cap=HP {player.HP}）。");
                }
                else
                {
                    AppendPanelConsoleInfo($"玩家回合开始：{player.Name} 护盾清零（{player.Shield}->0）。");
                    player.Shield = 0;
                }
            }

            int drawCount = player.drawCardNum > 0 ? player.drawCardNum : 0;
            int drawn = DrawCardsToHand(player, drawCount);
            player.costs = player.Max_costs;
            AppendPanelConsoleInfo($"玩家回合开始：{player.Name} 抽牌 {drawn}/{drawCount}，费用重置为 {player.costs}。");
        }
        UiRefresher.RequestRefresh();
    }

    private async void StartMonsterTurn()
    {
        if (!IsBattleStarted || GetAlivePlayers().Count == 0)
        {
            return;
        }

        if (CheckBattleEndAndHandle())
        {
            return;
        }

        OnMonsterTurnStart?.Invoke();

        List<int> orderedKeys = Monsters == null ? new List<int>() : new List<int>(Monsters.Keys);
        orderedKeys.Sort();

        for (int index = 0; index < orderedKeys.Count; index++)
        {
            int uniqueInGameId = orderedKeys[index];
            if (Monsters == null || !Monsters.TryGetValue(uniqueInGameId, out MonsterInstance monster) || monster.HP <= 0)
            {
                continue;
            }

            StateSystem.OnTurnStart(monster);
            // 先 OnTurnStart（给资源/护盾/cap）→ 后 ProcessDecayAtTiming（按 DecayMode=ClearAll 移除）
            StateDecayProcessor.ProcessDecayAtTiming(monster, DecayTrigger.OnTurnStart);

            if (monster.Shield > 0)
            {
                // 阵地 (ShieldCapEqualsHP) 例外：保留护盾但 cap 在当前 HP。
                bool hasShieldCapState = StateSystem.TryGetStateStacks(monster, StateType.ShieldCapEqualsHP, out int _);
                if (hasShieldCapState)
                {
                    if (monster.Shield > monster.HP) monster.Shield = monster.HP;
                    AppendPanelConsoleInfo($"怪物回合开始：{monster.Name}#{monster.UniqueInGameId} 阵地生效，护盾保留 {monster.Shield}（cap=HP {monster.HP}）。");
                }
                else
                {
                    AppendPanelConsoleInfo($"怪物回合开始：{monster.Name}#{monster.UniqueInGameId} 护盾清零（{monster.Shield}->0）。");
                    monster.Shield = 0;
                }
            }
        }

        NotifyBattleSceneRefresh();
        AppendPanelConsoleInfo($"怪物回合开始：本轮行动怪物数量 {orderedKeys.Count}。");

        // 横幅（1.0s）结束后等 0.5s 才开始结算
        await ToSignal(GetTree().CreateTimer(1.5f), SceneTreeTimer.SignalName.Timeout);

        int highlightedId = -1;
        for (int i = 0; i < orderedKeys.Count; i++)
        {
            int uniqueInGameId = orderedKeys[i];
            if (Monsters == null || !Monsters.TryGetValue(uniqueInGameId, out MonsterInstance monster) || monster.HP <= 0)
            {
                continue;
            }

            // 上一只恢复 + 当前只放大：高亮持续到下一只开始结算
            if (highlightedId != -1) OnMonsterIntentionHighlight?.Invoke(highlightedId, false);
            OnMonsterIntentionHighlight?.Invoke(uniqueInGameId, true);
            highlightedId = uniqueInGameId;

            ExecuteMonsterIntention(monster);
            NotifyBattleSceneRefresh();

            if (CheckBattleEndAndHandle())
            {
                if (highlightedId != -1) OnMonsterIntentionHighlight?.Invoke(highlightedId, false);
                return;
            }

            // 0.5s delay between monsters, except after the last one
            if (i < orderedKeys.Count - 1)
            {
                await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
            }
        }

        // 最后一只结算后恢复
        if (highlightedId != -1) OnMonsterIntentionHighlight?.Invoke(highlightedId, false);

        NotifyBattleSceneRefresh();
        SelectIntentionsForAllMonsters();
        UiRefresher.RequestRefresh();
        if (CheckBattleEndAndHandle())
        {
            return;
        }

        // 怪物回合结束时按 DecayTiming 衰减
        foreach (var kvp in Monsters)
        {
            if (kvp.Value != null && kvp.Value.HP > 0)
            {
                StateDecayProcessor.ProcessDecayAtTiming(kvp.Value, DecayTrigger.OnTurnEnd);
            }
        }

        StartPlayerTurn();
    }

    private void SelectIntentionsForAllMonsters() => MonsterIntentionService.SelectIntentionsForAllMonsters();

    private void SelectIntentionForMonster(MonsterInstance monster) => MonsterIntentionService.SelectIntentionForMonster(monster);

    public bool TrySwitchMonsterIntention(int monsterUniqueInGameId, int intentionIndex, out string resultMessage) => MonsterIntentionService.TrySwitchMonsterIntention(monsterUniqueInGameId, intentionIndex, out resultMessage);

    public bool TryDrawCardsByCommand(int count, out string resultMessage)
    {
        resultMessage = string.Empty;

        if (Player == null)
        {
            resultMessage = "玩家角色尚未初始化，无法抽牌。";
            return false;
        }

        if (count <= 0)
        {
            resultMessage = $"抽牌数={count} 非法，需大于0。";
            return false;
        }

        int drawn = DrawCardsToHand(Player, count);
        RefreshBattleInfoDisplay();
        resultMessage = $"抽牌完成：{drawn}/{count}。当前手牌 {Player.handcards.Count} 张，抽牌堆 {Player.drawpile.Count} 张，弃牌堆 {Player.discardpile.Count} 张。";
        return true;
    }

    private bool TryResolvePlayerForCommand(int playerUniqueInGameId, string missingPlayerMessage, out CharacterInstance player, out string resultMessage)
    {
        resultMessage = string.Empty;
        if (!TryGetPlayerByUniqueId(playerUniqueInGameId, out player))
        {
            resultMessage = missingPlayerMessage;
            return false;
        }

        return true;
    }

    private bool TryApplyPlayerMutation(int playerUniqueInGameId, string missingPlayerMessage, Func<CharacterInstance, string> validate, Action<CharacterInstance> apply, Func<CharacterInstance, string> buildSuccessMessage, out string resultMessage)
    {
        resultMessage = string.Empty;

        if (!TryResolvePlayerForCommand(playerUniqueInGameId, missingPlayerMessage, out CharacterInstance player, out resultMessage))
        {
            return false;
        }

        string validationMessage = validate?.Invoke(player);
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            resultMessage = validationMessage;
            return false;
        }

        apply?.Invoke(player);
        RefreshBattleInfoDisplay();
        resultMessage = buildSuccessMessage?.Invoke(player) ?? string.Empty;
        return true;
    }

    public bool TrySetPlayerHealth(int hp, int maxHp, out string resultMessage)
    {
        return TrySetPlayerHealth(Player?.UniqueInGameId ?? -1, hp, maxHp, out resultMessage);
    }

    public bool TrySetPlayerHealth(int playerUniqueInGameId, int hp, int maxHp, out string resultMessage)
    {
        int oldHp = 0;
        int oldMaxHp = 0;
        return TryApplyPlayerMutation(
            playerUniqueInGameId,
            "玩家角色尚未初始化，无法设置生命。",
            _ =>
            {
                if (maxHp <= 0)
                {
                    return $"最大生命值={maxHp} 非法，必须大于0。";
                }

                if (hp < 0)
                {
                    return $"当前生命值={hp} 非法，不能小于0。";
                }

                return null;
            },
            player =>
            {
                oldHp = player.HP;
                oldMaxHp = player.Max_HP;
                player.Max_HP = maxHp;
                player.HP = hp > maxHp ? maxHp : hp;
            },
            player => $"玩家 {player.Name} 生命已设置：HP {oldHp}->{player.HP}，MaxHP {oldMaxHp}->{player.Max_HP}。",
            out resultMessage);
    }

    public bool TrySetPlayerAttack(int attack, out string resultMessage)
    {
        return TrySetPlayerAttack(Player?.UniqueInGameId ?? -1, attack, out resultMessage);
    }

    public bool TrySetPlayerAttack(int playerUniqueInGameId, int attack, out string resultMessage)
    {
        int oldAttack = 0;
        return TryApplyPlayerMutation(
            playerUniqueInGameId,
            "玩家角色尚未初始化，无法设置攻击。",
            null,
            player =>
            {
                oldAttack = player.Attack;
                player.Attack = attack;
            },
            player => $"玩家 {player.Name} 攻击已设置：{oldAttack}->{player.Attack}。",
            out resultMessage);
    }

    public bool TrySetPlayerDefend(int defend, out string resultMessage)
    {
        return TrySetPlayerDefend(Player?.UniqueInGameId ?? -1, defend, out resultMessage);
    }

    public bool TrySetPlayerDefend(int playerUniqueInGameId, int defend, out string resultMessage)
    {
        int oldDefend = 0;
        return TryApplyPlayerMutation(
            playerUniqueInGameId,
            "玩家角色尚未初始化，无法设置防御。",
            null,
            player =>
            {
                oldDefend = player.Defend;
                player.Defend = defend;
            },
            player => $"玩家 {player.Name} 防御已设置：{oldDefend}->{player.Defend}。",
            out resultMessage);
    }

    public bool TrySetPlayerMaxEnergy(int maxEnergy, out string resultMessage)
    {
        return TrySetPlayerMaxEnergy(Player?.UniqueInGameId ?? -1, maxEnergy, out resultMessage);
    }

    public bool TrySetPlayerMaxEnergy(int playerUniqueInGameId, int maxEnergy, out string resultMessage)
    {
        int oldMaxEnergy = 0;
        int oldEnergy = 0;
        return TryApplyPlayerMutation(
            playerUniqueInGameId,
            "玩家角色尚未初始化，无法设置能量上限。",
            _ => maxEnergy < 1 ? $"能量上限={maxEnergy} 非法，不能小于1。" : null,
            player =>
            {
                oldMaxEnergy = player.Max_costs;
                oldEnergy = player.costs;
                player.Max_costs = maxEnergy;
                if (player.costs > player.Max_costs)
                {
                    player.costs = player.Max_costs;
                }
            },
            player => $"玩家 {player.Name} 能量上限已设置：{oldMaxEnergy}->{player.Max_costs}，当前能量 {oldEnergy}->{player.costs}。",
            out resultMessage);
    }

    public bool TryAddPlayerEnergyRaw(int addEnergy, out string resultMessage)
    {
        return TryAddPlayerEnergyRaw(Player?.UniqueInGameId ?? -1, addEnergy, out resultMessage);
    }

    public bool TryAddPlayerEnergyRaw(int playerUniqueInGameId, int addEnergy, out string resultMessage)
    {
        int oldEnergy = 0;
        return TryApplyPlayerMutation(
            playerUniqueInGameId,
            "玩家角色尚未初始化，无法增加能量。",
            _ => addEnergy <= 0 ? $"增加能量值={addEnergy} 非法，需大于0。" : null,
            player =>
            {
                oldEnergy = player.costs;
                player.costs += addEnergy;
            },
            player => $"玩家 {player.Name} 增加能量（跳过状态修正）：{oldEnergy}->{player.costs}（+{addEnergy}）。",
            out resultMessage);
    }

    public bool TryAddStateToUnit(int targetUniqueInGameId, int rawStateType, int stacks, out string resultMessage)
    {
        resultMessage = string.Empty;

        if (stacks <= 0)
        {
            resultMessage = $"层数={stacks} 非法，需大于0。";
            return false;
        }

        if (!Enum.IsDefined(typeof(StateType), rawStateType))
        {
            resultMessage = $"状态ID={rawStateType} 非法，未定义对应 StateType。";
            return false;
        }

        StateType stateType = (StateType)rawStateType;
        if (stateType == StateType.None)
        {
            resultMessage = "状态ID=0 对应 None，不能添加。";
            return false;
        }

        EnsureUnitCachesLoaded();
        if (!LoadingSystem.StateDictionary.ContainsKey(stateType))
        {
            resultMessage = $"状态ID={rawStateType} 未在状态配置中找到。";
            return false;
        }

        if (!TryGetUnitByUniqueId(targetUniqueInGameId, out IUnitInstance targetUnit))
        {
            resultMessage = $"未找到目标UniqueInGameId={targetUniqueInGameId} 对应的单位。";
            return false;
        }

        StateSystem.AddOrUpdateState(targetUnit, stateType, stacks);
        RefreshBattleInfoDisplay();

        string targetLabel = BuildUnitLabel(targetUnit);
        string stateName = LoadingSystem.StateDictionary.TryGetValue(stateType, out StateDefinition definition) && !string.IsNullOrWhiteSpace(definition.Name)
            ? definition.Name
            : stateType.ToString();
        resultMessage = $"已为 {targetLabel} 添加状态 {stateName}（StateId={(int)stateType}）x{stacks}。";
        return true;
    }

    public bool TryRemoveStateFromUnit(int targetUniqueInGameId, int rawStateType, int? stacks, out string resultMessage)
    {
        resultMessage = string.Empty;

        if (!Enum.IsDefined(typeof(StateType), rawStateType))
        {
            resultMessage = $"状态ID={rawStateType} 非法，未定义对应 StateType。";
            return false;
        }

        StateType stateType = (StateType)rawStateType;
        if (stateType == StateType.None)
        {
            resultMessage = "状态ID=0 对应 None，不能删除。";
            return false;
        }

        if (stacks.HasValue && stacks.Value <= 0)
        {
            resultMessage = $"层数={stacks.Value} 非法，需大于0。";
            return false;
        }

        EnsureUnitCachesLoaded();
        if (!LoadingSystem.StateDictionary.ContainsKey(stateType))
        {
            resultMessage = $"状态ID={rawStateType} 未在状态配置中找到。";
            return false;
        }

        if (!TryGetUnitByUniqueId(targetUniqueInGameId, out IUnitInstance targetUnit))
        {
            resultMessage = $"未找到目标UniqueInGameId={targetUniqueInGameId} 对应的单位。";
            return false;
        }

        if (!StateSystem.TryGetStateStacks(targetUnit, stateType, out int currentStacks) || currentStacks <= 0)
        {
            resultMessage = $"目标 {BuildUnitLabel(targetUnit)} 当前不存在状态 {(int)stateType}。";
            return false;
        }

        int removedStacks;
        if (!stacks.HasValue)
        {
            removedStacks = currentStacks;
            StateSystem.RemoveState(targetUnit, stateType);
        }
        else
        {
            removedStacks = StateSystem.RemoveStateStacks(targetUnit, stateType, stacks.Value);
        }

        RefreshBattleInfoDisplay();

        string targetLabel = BuildUnitLabel(targetUnit);
        string stateName = LoadingSystem.StateDictionary.TryGetValue(stateType, out StateDefinition definition) && !string.IsNullOrWhiteSpace(definition.Name)
            ? definition.Name
            : stateType.ToString();
        int remainingStacks = StateSystem.TryGetStateStacks(targetUnit, stateType, out int leftStacks) ? leftStacks : 0;
        resultMessage = remainingStacks > 0
            ? $"已为 {targetLabel} 删除状态 {stateName}（StateId={(int)stateType}）x{removedStacks}，剩余 {remainingStacks} 层。"
            : $"已为 {targetLabel} 删除状态 {stateName}（StateId={(int)stateType}）全部 {removedStacks} 层。";
        return true;
    }

    private void NotifyBattleSceneRefresh()
    {
        Node scene = GetTree().CurrentScene;
        if (scene != null && scene is CardBattleScene cardBattleScene)
        {
            cardBattleScene.RequestExternalUiRefresh();
        }
    }

    private void ExecuteMonsterIntention(MonsterInstance monster)
    {
        MonsterIntentionService.ExecuteMonsterIntention(monster);
        // 怪物攻击意图执行后：按 DecayTiming=OnAttackPlayed 处理状态（如旋风斩状态触发的群攻由 MonsterIntentionService 内部完成）
        StateDecayProcessor.ProcessDecayAtTiming(monster, DecayTrigger.OnAttackPlayed);
    }

    private void ShowDamageNumbersForCardPlay(Card.CardApplyResult applyResult)
    {
        if (applyResult == null || !applyResult.Success || applyResult.IndividualEffectResults == null)
        {
            return;
        }

        CardBattleScene scene = GetTree().CurrentScene as CardBattleScene;
        if (scene == null) return;

        foreach (EffectResult singleResult in applyResult.IndividualEffectResults)
        {
            if (singleResult == null || singleResult.HpDamage <= 0 || singleResult.Target == null)
            {
                continue;
            }

            scene.ShowDamageNumberOnUnit(singleResult.Target, singleResult.HpDamage);
        }
    }

    public void BeginOrderedCombatLog()
    {
        OrderedCombatLogDepth++;
    }

    public void EndOrderedCombatLog()
    {
        if (OrderedCombatLogDepth > 0)
        {
            OrderedCombatLogDepth--;
        }
    }

    public void EnqueueDeferredCombatInfo(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (OrderedCombatLogDepth > 0)
        {
            DeferredCombatInfoMessages.Enqueue(message);
            return;
        }

        AppendPanelConsoleInfo(message);
    }

    public void HandleUnitDeath(System.Action onDead)
    {
        if (onDead == null)
        {
            return;
        }

        if (OrderedCombatLogDepth > 0)
        {
            DeferredDeathActions.Enqueue(onDead);
            return;
        }

        onDead.Invoke();
    }

    internal void FlushDeferredCombatResolution()
    {
        while (DeferredCombatInfoMessages.Count > 0)
        {
            AppendPanelConsoleInfo(DeferredCombatInfoMessages.Dequeue());
        }

        while (DeferredDeathActions.Count > 0)
        {
            System.Action onDead = DeferredDeathActions.Dequeue();
            onDead?.Invoke();
        }
    }

    private IUnitInstance ResolveRandomAlivePlayerTarget()
    {
        List<CharacterInstance> alivePlayers = GetAlivePlayers();
        if (alivePlayers.Count == 0)
        {
            return null;
        }

        return alivePlayers[RandomGenerator.Next(alivePlayers.Count)];
    }

    internal int DrawCardsToHand(CharacterInstance player, int count)
    {
        if (player == null || count <= 0)
        {
            return 0;
        }

        if (StateSystem.TryGetStateStacks(player, StateType.DrawLock, out int _))
        {
            AppendPanelConsoleInfo($"{player.Name} 受 DrawLock 影响，跳过抽牌。");
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
                AppendPanelConsoleInfo($"{player.Name} 的抽牌堆为空：已将弃牌堆随机洗牌后放回抽牌堆。");
            }

            Card topCard = player.drawpile[0];
            player.drawpile.RemoveAt(0);
            player.handcards.Add(topCard);
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


    private System.Action CreateMonsterOnDeadCallback(MonsterInstance instance)
    {
        return () =>
        {
            int id = instance.UniqueInGameId;
            AppendPanelConsoleInfo($"怪物死亡：{instance.Name}（UniqueInGameId: {id}）。");
            // Keep in dictionary to preserve position; HP=0 marks as dead
            RefreshBattleInfoDisplay();
            CheckBattleEndAndHandle();
        };
    }

    private System.Action<StateEndedContext> CreateStateEndedCallback(IUnitInstance targetUnit)
    {
        return context =>
        {
            if (context == null || !context.NeedCallback)
            {
                return;
            }

            IUnitInstance actualTarget = context.TargetUnit ?? targetUnit;
            string targetLabel = BuildUnitLabel(actualTarget);
            string ownerLabel = BuildUnitLabel(context.OwnerUnit);
            string cardUniqueInGameId = string.IsNullOrWhiteSpace(context.StateCardUniqueInGameId) ? "无" : context.StateCardUniqueInGameId;

            Card stateCard = FindAndRemoveCardFromStatePile(actualTarget, context.StateCardUniqueInGameId);
            if (stateCard == null)
            {
                AppendPanelConsoleError($"错误：状态结束回调未在 {targetLabel} 的状态牌堆中找到 UniqueInGameId={cardUniqueInGameId} 的状态牌。状态={context.StateType}。");
                return;
            }

            if (context.OwnerUnit == null || context.OwnerUnit.HP <= 0)
            {
                AppendPanelConsoleInfo($"状态结束：{targetLabel} 的状态 {context.StateType} 已结束，对应状态牌 {BuildCardLabel(stateCard)} 未回收，因为所属单位已不存在或已死亡。原因={context.EndReason}。");
                RefreshBattleInfoDisplay();
                return;
            }

            if (stateCard.HasKeyWord(CardKeyWord.Exhaust))
            {
                context.OwnerUnit.ExhaustPile.Add(stateCard);
                AppendPanelConsoleInfo($"状态结束：{targetLabel} 的状态 {context.StateType} 已结束，对应状态牌 {BuildCardLabel(stateCard)} 已移入 {ownerLabel} 的消耗牌堆。原因={context.EndReason}。");
            }
            else
            {
                context.OwnerUnit.DiscardPile.Add(stateCard);
                AppendPanelConsoleInfo($"状态结束：{targetLabel} 的状态 {context.StateType} 已结束，对应状态牌 {BuildCardLabel(stateCard)} 已移入 {ownerLabel} 的弃牌堆。原因={context.EndReason}。");
            }
            RefreshBattleInfoDisplay();
        };
    }

    private string BuildUnitLabel(IUnitInstance unit)
    {
        if (unit == null)
        {
            return "无";
        }

        Unit typedUnit = unit as Unit;
        string name = typedUnit?.Name ?? unit.GetType().Name;
        return $"{name}(UniqueInGameId={unit.UniqueInGameId})";
    }

    private string BuildCardLabel(Card card)
    {
        if (card == null)
        {
            return "无";
        }

        string cardName = string.IsNullOrWhiteSpace(card.CardName) ? $"CardId={card.CardId}" : card.CardName;
        string uniqueInGameId = string.IsNullOrWhiteSpace(card.UniqueInGameId) ? "未生成" : card.UniqueInGameId;
        return $"{cardName}(CardId={card.CardId}, UniqueInGameId={uniqueInGameId})";
    }

    internal bool CheckBattleEndAndHandle()
    {
        if (!IsBattleStarted)
        {
            return true;
        }

        if (GetAlivePlayers().Count == 0)
        {
            return true;
        }

        if (Monsters == null || Monsters.Values.All(m => m.HP <= 0))
        {
            EndBattle();
            return true;
        }

        return false;
    }

    public void EndBattle()
    {
        CardPlay.ClearPendingCardSelection();
        Stats.Reset();
        int monsterCount = Monsters == null ? 0 : Monsters.Count;
        if (Monsters != null)
        {
            Monsters.Clear();
            Monsters = null;
        }

        IsBattleStarted = false;
        IsPlayerTurn = false;

        AppendPanelConsoleInfo($"战斗结束：已销毁怪物实例 {monsterCount} 个。");
        UiRefresher.RequestRefresh();
    }

    public void EndGame()
    {
        CardPlay.ClearPendingCardSelection();
        Stats.Reset();
        int monsterCount = Monsters == null ? 0 : Monsters.Count;
        int playerCount = Players == null ? 0 : Players.Count;

        if (Players != null)
        {
            foreach (CharacterInstance player in Players.Values)
            {
                player?.handcards?.Clear();
                player?.drawpile?.Clear();
                player?.discardpile?.Clear();
                player?.ExhaustPile?.Clear();
                player?.StatePile?.Clear();
            }

            Players.Clear();
            Players = null;
        }

        if (Monsters != null)
        {
            Monsters.Clear();
            Monsters = null;
        }

        IsBattleStarted = false;
        IsPlayerTurn = false;

        AppendPanelConsoleInfo($"战斗结束：已销毁角色实例 {playerCount} 个，怪物实例 {monsterCount} 个。");
        UiRefresher.RequestRefresh();
    }

    public void AppendPanelConsoleInfo(string message) => Console.Info(message);

    internal void AppendPanelConsoleError(string message) => Console.Error(message);

    /// <summary>
    /// 供 GodotUiRefresherAdapter 在没有 public 访问权限的情况下调用的"重绘信息标签"入口。
    /// </summary>
    internal void InvokeRefreshBattleInfoDisplay() => RefreshBattleInfoDisplay();

    /// <summary>
    /// 供 CardPlayController 在 CardPlay 出牌后回调：在 CardBattleScene 上飘伤害数字。
    /// </summary>
    internal void InvokeShowDamageNumbersForCardPlay(Card.CardApplyResult applyResult) => ShowDamageNumbersForCardPlay(applyResult);

    private void RegisterStateCardEndCallbacks(List<StateCardApplication> applications, Card sourceCard, IUnitInstance ownerUnit)
    {
        if (applications == null || applications.Count == 0)
        {
            return;
        }

        foreach (StateCardApplication application in applications)
        {
            if (application?.TargetUnit == null)
            {
                continue;
            }

            StateSystem.RegisterStateEndCallback(application.TargetUnit, application.StateType, application.Stacks, sourceCard?.UniqueInGameId, ownerUnit);
        }
    }

    private Card FindAndRemoveCardFromStatePile(IUnitInstance targetUnit, string stateCardUniqueInGameId)
    {
        if (targetUnit?.StatePile == null || string.IsNullOrWhiteSpace(stateCardUniqueInGameId))
        {
            return null;
        }

        for (int index = 0; index < targetUnit.StatePile.Count; index++)
        {
            Card stateCard = targetUnit.StatePile[index];
            if (stateCard == null || !string.Equals(stateCard.UniqueInGameId, stateCardUniqueInGameId, StringComparison.Ordinal))
            {
                continue;
            }

            targetUnit.StatePile.RemoveAt(index);
            return stateCard;
        }

        return null;
    }

    private bool TryResolveStateCardApplications(Card card, IUnitInstance source, IUnitInstance selectedTarget, out List<StateCardApplication> applications, out string errorMessage)
    {
        applications = new List<StateCardApplication>();
        errorMessage = string.Empty;

        if (card == null)
        {
            errorMessage = "错误：状态牌为空，无法解析状态牌堆目标。";
            return false;
        }

        for (int effectIndex = 0; effectIndex < card.EffectTypes.Length; effectIndex++)
        {
            if (card.EffectTypes[effectIndex] != EffectType.AddState)
            {
                continue;
            }

            int[] rawEffectParams = (card.Params != null && effectIndex < card.Params.Length) ? card.Params[effectIndex] : Array.Empty<int>();
            EffectTargetType effectTargetType = ParseEffectTargetType(rawEffectParams);
            int[] effectArgs = GetEffectArguments(rawEffectParams);
            List<IUnitInstance> resolvedTargets = ResolveEffectTargets(source, selectedTarget, effectTargetType);

            if (effectArgs.Length <= 0)
            {
                errorMessage = $"错误：状态牌 CardId={card.CardId} 的 AddState 缺少 stateType 参数。";
                return false;
            }

            StateType stateType = (StateType)effectArgs[0];
            if (!Enum.IsDefined(typeof(StateType), stateType) || stateType == StateType.None)
            {
                errorMessage = $"错误：状态牌 CardId={card.CardId} 的 AddState 参数非法，stateType={effectArgs[0]}。";
                return false;
            }

            int stacks = effectArgs.Length > 1 ? effectArgs[1] : 1;
            foreach (IUnitInstance resolvedTarget in resolvedTargets)
            {
                applications.Add(new StateCardApplication(resolvedTarget, stateType, stacks));
            }
        }

        if (applications.Count == 0)
        {
            errorMessage = $"错误：状态牌 CardId={card.CardId} 未配置 AddState，无法放入目标状态牌堆。";
            return false;
        }

        IUnitInstance uniqueTarget = applications[0].TargetUnit;
        if (uniqueTarget == null)
        {
            errorMessage = $"错误：状态牌 CardId={card.CardId} 未解析出有效状态目标。";
            return false;
        }

        if (applications.Any(current => current.TargetUnit == null || current.TargetUnit.UniqueInGameId != uniqueTarget.UniqueInGameId))
        {
            errorMessage = $"错误：状态牌 CardId={card.CardId} 当前不支持同时作用于多个不同单位，因为同一张牌无法同时进入多个状态牌堆。";
            return false;
        }

        return true;
    }

    private static EffectTargetType ParseEffectTargetType(int[] rawEffectParams)
    {
        if (rawEffectParams == null || rawEffectParams.Length == 0)
        {
            return EffectTargetType.Auto;
        }

        EffectTargetType parsed = (EffectTargetType)rawEffectParams[0];
        return Enum.IsDefined(typeof(EffectTargetType), parsed) ? parsed : EffectTargetType.Auto;
    }

    private static int[] GetEffectArguments(int[] rawEffectParams)
    {
        if (rawEffectParams == null || rawEffectParams.Length <= 1)
        {
            return Array.Empty<int>();
        }

        int[] args = new int[rawEffectParams.Length - 1];
        Array.Copy(rawEffectParams, 1, args, 0, args.Length);
        return args;
    }

    private List<IUnitInstance> ResolveEffectTargets(IUnitInstance source, IUnitInstance selectedTarget, EffectTargetType effectTargetType)
    {
        List<IUnitInstance> targets = new List<IUnitInstance>();
        switch (effectTargetType)
        {
            case EffectTargetType.Self:
                if (source != null)
                {
                    targets.Add(source);
                }
                break;
            case EffectTargetType.SelectedTarget:
                if (selectedTarget != null)
                {
                    targets.Add(selectedTarget);
                }
                break;
            case EffectTargetType.AllEnemies:
                targets.AddRange(GetEnemyUnits(source) ?? new List<IUnitInstance>());
                break;
            case EffectTargetType.AllUnits:
                targets.AddRange(GetAllUnits() ?? new List<IUnitInstance>());
                break;
            case EffectTargetType.AllAllies:
                targets.AddRange(GetAllyUnits(source) ?? new List<IUnitInstance>());
                break;
            case EffectTargetType.Auto:
            default:
                if (selectedTarget != null)
                {
                    targets.Add(selectedTarget);
                }
                else if (source != null)
                {
                    targets.Add(source);
                }
                break;
        }

        return targets;
    }
}
