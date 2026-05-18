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

    public static BattleSytem Current { get; private set; }

    internal static readonly Random RandomGenerator = new Random();

    internal enum BattleInfoTab
    {
        Runtime,
        DrawPile,
        DiscardPile
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

    // 本局战斗中各单位的初始生命值快照，Key 为 UniqueInGameId
    private readonly Dictionary<int, int> BattleInitialHpSnapshots = new Dictionary<int, int>();

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

    public int GetBattleLostHp(IUnitInstance unit)
    {
        if (unit == null)
        {
            return 0;
        }

        if (!BattleInitialHpSnapshots.TryGetValue(unit.UniqueInGameId, out int initialHp))
        {
            return 0;
        }

        return Math.Max(0, initialHp - unit.HP);
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
        InitializePlayers(characterIds);
        InitializeMonsters(monsterIds);
        SnapshotBattleInitialHp();

        CurrentPileDisplayOrderMode = PileDisplayOrderMode.PileOrder;

        InitializePlayerDrawPilesFromCharacterCards();
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
        BattleInitialHpSnapshots.Clear();

        if (Players != null)
        {
            foreach (CharacterInstance player in Players.Values)
            {
                if (player != null)
                {
                    BattleInitialHpSnapshots[player.UniqueInGameId] = player.HP;
                }
            }
        }

        if (Monsters != null)
        {
            foreach (MonsterInstance monster in Monsters.Values)
            {
                if (monster != null)
                {
                    BattleInitialHpSnapshots[monster.UniqueInGameId] = monster.HP;
                }
            }
        }
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

        RefreshBattleInfoDisplay();
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
        BattleInfoUiBinder.UpdateTabVisualState(scene, RuntimeTabButtonPath, RuntimeTabButtonPathInRoot, DrawPileTabButtonPath, DrawPileTabButtonPathInRoot, DiscardPileTabButtonPath, DiscardPileTabButtonPathInRoot, CurrentBattleInfoTab);
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
            OnRuntimeBattleInfoTabPressed,
            OnDrawPileBattleInfoTabPressed,
            OnDiscardPileBattleInfoTabPressed,
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

        BattleInfoUiBinder.UpdateTabVisualState(scene, RuntimeTabButtonPath, RuntimeTabButtonPathInRoot, DrawPileTabButtonPath, DrawPileTabButtonPathInRoot, DiscardPileTabButtonPath, DiscardPileTabButtonPathInRoot, CurrentBattleInfoTab);
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
        Players = new Dictionary<int, CharacterInstance>();

        foreach (int characterId in characterIds)
        {
            if (!characters.TryGetValue(characterId, out var character))
            {
                AppendPanelConsoleError($"错误：角色ID {characterId} 未在缓存中找到。");
                continue;
            }

            CharacterInstance player = new CharacterInstance(character);
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
            Players[player.UniqueInGameId] = player;
            AppendPanelConsoleInfo($"已创建角色 {player.Name}（ID: {characterId}, UniqueInGameId: {player.UniqueInGameId}）。");
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

        RefreshBattleInfoDisplay();
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

    public bool PlayHandCard(int handIndex, IUnitInstance target = null)
    {
        if (!TryResolvePlayerHandCardByIndex(Player?.UniqueInGameId ?? -1, handIndex, out CharacterInstance player, out Card card, out string errorMessage))
        {
            AppendPanelConsoleError(errorMessage);
            return false;
        }

        return PlayHandCard(player, card, target);
    }

    public bool PlayHandCard(int playerUniqueInGameId, int handIndex, IUnitInstance target = null)
    {
        if (!TryResolvePlayerHandCardByIndex(playerUniqueInGameId, handIndex, out CharacterInstance player, out Card card, out string errorMessage))
        {
            AppendPanelConsoleError(errorMessage);
            return false;
        }

        return PlayHandCard(player, card, target);
    }

    public bool PlayHandCard(string uniqueInGameId, IUnitInstance target = null)
    {
        if (!TryResolveDefaultPlayerHandCardByUniqueInGameId(uniqueInGameId, out CharacterInstance player, out Card card, out string errorMessage))
        {
            AppendPanelConsoleError(errorMessage);
            return false;
        }

        return PlayHandCard(player, card, target);
    }

    public bool PlayHandCard(Card card, IUnitInstance target = null)
    {
        return PlayHandCard(Player, card, target);
    }

    public bool PlayHandCard(CharacterInstance sourcePlayer, Card card, IUnitInstance target = null)
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

        if (sourcePlayer == null)
        {
            AppendPanelConsoleError("错误：玩家角色尚未初始化，无法出牌。");
            return false;
        }

        if (card == null)
        {
            AppendPanelConsoleError("错误：出牌失败，卡牌为空。");
            return false;
        }

        int handIndex = sourcePlayer.handcards.IndexOf(card);
        if (handIndex < 0)
        {
            AppendPanelConsoleError($"错误：卡牌ID {card.CardId} 不在当前玩家手牌中，无法打出。");
            return false;
        }

        if (sourcePlayer.costs < card.EnergyCost)
        {
            AppendPanelConsoleError($"错误：费用不足，打出卡牌ID {card.CardId} 需要 {card.EnergyCost} 点费用，当前仅有 {sourcePlayer.costs} 点。");
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
            if (!TryResolveStateCardApplications(card, sourcePlayer, target, out stateCardApplications, out string stateCardError))
            {
                AppendPanelConsoleError(stateCardError);
                return false;
            }

            statePileTarget = stateCardApplications[0].TargetUnit;
        }

        sourcePlayer.costs -= card.EnergyCost;
        sourcePlayer.handcards.RemoveAt(handIndex);
        if (statePileTarget != null)
        {
            statePileTarget.StatePile.Add(card);
            RegisterStateCardEndCallbacks(stateCardApplications, card, sourcePlayer);
            AppendPanelConsoleInfo($"玩家 {sourcePlayer.Name} 打出状态牌 CardId={card.CardId}，UniqueInGameId={card.UniqueInGameId}，消耗费用 {card.EnergyCost}，剩余费用 {sourcePlayer.costs}。卡牌已移入 {BuildUnitLabel(statePileTarget)} 的状态牌堆。手牌剩余 {sourcePlayer.handcards.Count} 张。目标状态牌堆当前 {statePileTarget.StatePile.Count} 张。");
        }
        else
        {
            sourcePlayer.discardpile.Add(card);
            AppendPanelConsoleInfo($"玩家 {sourcePlayer.Name} 打出卡牌 CardId={card.CardId}，UniqueInGameId={card.UniqueInGameId}，消耗费用 {card.EnergyCost}，剩余费用 {sourcePlayer.costs}。卡牌已移入弃牌堆。手牌剩余 {sourcePlayer.handcards.Count} 张，弃牌堆当前 {sourcePlayer.discardpile.Count} 张。");
        }

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

        // 弃置手牌
        foreach (CharacterInstance player in GetAlivePlayers())
        {
            var toDiscard = player.handcards.FindAll(c => !c.HasKeyWord(CardKeyWord.Retain));
            var toKeep = player.handcards.FindAll(c => c.HasKeyWord(CardKeyWord.Retain));
            player.discardpile.AddRange(toDiscard);
            player.handcards.Clear();
            player.handcards.AddRange(toKeep);
            if (toDiscard.Count > 0)
                AppendPanelConsoleInfo($"玩家 {player.Name} 回合结束：弃置手牌 {toDiscard.Count} 张{(toKeep.Count > 0 ? $"，保留 {toKeep.Count} 张" : string.Empty)}。");
            else if (toKeep.Count > 0)
                AppendPanelConsoleInfo($"玩家 {player.Name} 回合结束：所有手牌（{toKeep.Count} 张）均为保留牌，不弃置。");
        }

        AppendPanelConsoleInfo("玩家回合结束。进入怪物回合。");
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

        IsPlayerTurn = true;
        foreach (CharacterInstance player in alivePlayers)
        {
            StateSystem.OnTurnStart(player);
            if (player.Shield > 0)
            {
                AppendPanelConsoleInfo($"玩家回合开始：{player.Name} 护盾清零（{player.Shield}->0）。");
                player.Shield = 0;
            }

            int drawCount = player.drawCardNum > 0 ? player.drawCardNum : 0;
            int drawn = DrawCardsToHand(player, drawCount);
            player.costs = player.Max_costs;
            AppendPanelConsoleInfo($"玩家回合开始：{player.Name} 抽牌 {drawn}/{drawCount}，费用重置为 {player.costs}。");
        }
        RefreshBattleInfoDisplay();
    }

    private void StartMonsterTurn()
    {
        if (!IsBattleStarted || GetAlivePlayers().Count == 0)
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

    private void ExecuteMonsterIntention(MonsterInstance monster) => MonsterIntentionService.ExecuteMonsterIntention(monster);

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

    private int DrawCardsToHand(CharacterInstance player, int count)
    {
        if (player == null || count <= 0)
        {
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

    private void InitializePlayerDrawPilesFromCharacterCards()
    {
        List<CharacterInstance> orderedPlayers = GetOrderedPlayers();
        if (orderedPlayers.Count == 0)
        {
            return;
        }

        foreach (CharacterInstance player in orderedPlayers)
        {
            player.handcards.Clear();
            player.drawpile.Clear();
            player.discardpile.Clear();

            List<int> defaultCardIds = LoadingSystem.GetCharacterDefaultCardIdListByKey(player.id, LoadingSystem.CharacterDefaultDeckCsvPathKey, true);
            int defaultAddedCount = 0;
            foreach (int cardId in defaultCardIds)
            {
                if (!LoadingSystem.CardDictionary.TryGetValue(cardId, out Card template))
                {
                    AppendPanelConsoleError($"错误：角色 {player.id} 默认卡组中的卡牌ID {cardId} 未在缓存中找到，已跳过。");
                    continue;
                }

                player.drawpile.Add(template.CreateRuntimeInstance());
                defaultAddedCount++;
            }

            List<int> configuredCardIds = SetupData == null ? new List<int>() : SetupData.GetCharacterCardIdList();
            int configuredAddedCount = 0;
            for (int index = 0; index < configuredCardIds.Count; index++)
            {
                int cardId = configuredCardIds[index];
                if (!LoadingSystem.CardDictionary.TryGetValue(cardId, out Card template))
                {
                    AppendPanelConsoleError($"错误：新增配置中的卡牌ID {cardId} 未在缓存中找到，已跳过。");
                    continue;
                }

                player.drawpile.Add(template.CreateRuntimeInstance());
                configuredAddedCount++;
            }

            if (player.drawpile.Count > 0)
            {
                ShuffleCards(player.drawpile);
                AppendPanelConsoleInfo($"角色 {player.id} 抽牌堆初始化完成：默认卡组 {defaultAddedCount} 张 + 新增卡牌 {configuredAddedCount} 张，共 {player.drawpile.Count} 张（已洗牌）。");
                continue;
            }

            AppendPanelConsoleInfo($"角色 {player.id} 无默认卡组且未配置新增卡牌，抽牌堆为空。");
        }
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

            context.OwnerUnit.DiscardPile.Add(stateCard);
            AppendPanelConsoleInfo($"状态结束：{targetLabel} 的状态 {context.StateType} 已结束，对应状态牌 {BuildCardLabel(stateCard)} 已移入 {ownerLabel} 的弃牌堆。原因={context.EndReason}。");
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

    private bool CheckBattleEndAndHandle()
    {
        if (!IsBattleStarted)
        {
            return true;
        }

        if (GetAlivePlayers().Count == 0)
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
        int playerCount = Players == null ? 0 : Players.Count;

        if (Players != null)
        {
            foreach (CharacterInstance player in Players.Values)
            {
                player?.handcards?.Clear();
                player?.drawpile?.Clear();
                player?.discardpile?.Clear();
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
        RefreshBattleInfoDisplay();
    }

    public void AppendPanelConsoleInfo(string message)
    {
        AppendPanelConsole("[信息] " + message);
    }

    internal void AppendPanelConsoleError(string message)
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
