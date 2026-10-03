using System;
using System.Collections.Generic;
using System.Linq;

namespace CardSimulator.Battlefield;

/// <summary>Presentation-only result of one resolved strike. Rules have already applied the result.</summary>
public sealed record BattlefieldAttackEvent(long EventId, int SourceUnitId, int? TargetUnitId,
    AxialHex From, AxialHex To, WeaponAttackMode Mode, int Damage, int ShieldAbsorbed, bool Defeated,
    int TargetHpBefore = -1, int TargetHpAfter = -1, int TargetShieldBefore = -1, int TargetShieldAfter = -1,
    IReadOnlyCollection<AxialHex> AffectedCells = null, AxialHex? Direction = null,
    IReadOnlyCollection<int> AffectedUnitIds = null, bool IsExplosion = false);
public sealed record EnemyIntentDisplay(EnemyIntentPreviewCertainty Certainty, int Damage, int Hits, int ActionBudget, string Tooltip);

/// <summary>Runnable spatial battle session using the existing Card, Effect and State pipelines.</summary>
public sealed partial class BattlefieldSession : IDisposable
{
    public enum BattlePhase { Player, Monsters, Victory, Defeat }
    public BattleMapDefinition Definition { get; }
    public GeneratedBattlefield Generated { get; }
    public BattleBoard Board => Generated.Board;
    public BattleOccupancyService Occupancy { get; }
    public BattleMovementService Movement { get; }
    public List<int> PlayerIds { get; } = new();
    public int SelectedId { get; private set; }
    public int Round { get; private set; } = 1;
    public BattlePhase Phase { get; private set; } = BattlePhase.Player;
    public bool IsFinished => Phase is BattlePhase.Victory or BattlePhase.Defeat;
    /// <summary>当前歼灭战：没有在场敌人即胜利。保护与生存目标由后续目标策略接入。</summary>
    public bool AllEnemiesDefeated => Occupancy != null
        && Occupancy.Placements.Values.All(x => x.Role != BattlefieldRole.Enemy || x.Presence != BattlefieldPresence.Active);
    public event Action Changed;
    public event Action<string> Message;
    public event Action<BattlePhase> Finished;
    public event Action<BattlefieldEntry> UnitEntered;
    public event Action<BattlefieldAttackEvent> AttackResolved;

    /// <summary>怪物攻击命中玩家时触发（攻击者、被命中玩家），每次攻击只触发一次。
    /// 战斗层不认识金币：窃取等依赖局外数据的机制由运行局侧订阅后自行结算。</summary>
    public event Action<BattleUnitPlacement, BattleUnitPlacement> MonsterHitPlayer;

    /// <summary>
    /// 玩家回合开始（参数 = 新回合号）。运行局据此计时间点进程（地图玩法 §5.1：每 10 个战斗中的回合 = 1 时间点）。
    /// 纯运行时事件、不依赖 `RunSession`：本类是可在无存档环境下单跑的战斗规则层。
    /// </summary>
    public event Action<int> PlayerRoundStarted;
    private long attackEventSequence;

    /// <summary>怪物单位 → 实例键（来自关卡 CSV 的 `InstanceId`）：窃取金币等"按怪物实例"记账的稳定标识。</summary>
    private readonly Dictionary<int, string> monsterInstanceKeys = new();

    /// <summary>取某只怪物单位的实例键；不是怪物或未知单位返回空串。</summary>
    public string GetMonsterInstanceKey(int unitId) =>
        monsterInstanceKeys.TryGetValue(unitId, out string key) ? key : string.Empty;

    /// <summary>关卡级战斗规则实例（关卡 CSV 的 `BattleRule` 列解析而来）；为空 = 全部沿用现状。</summary>
    private readonly IReadOnlyList<IBattleRule> battleRules = Array.Empty<IBattleRule>();
    /// <summary>意图批次计数：第几个"怪物回合的意图批次"（从 1 开始；意图在上一怪物回合结束时准备）。</summary>
    private int enemyIntentBatch;
    /// <summary>怪物单位 → 累计命中玩家次数（含被护盾格挡），供战斗规则读取。</summary>
    private readonly Dictionary<int, int> monsterHitPlayerCounts = new();

    /// <summary>当前战斗挂载的规则（只读，供自检/调试）。</summary>
    public IReadOnlyList<IBattleRule> Rules => battleRules;

    // 空间场景持有独立牌堆，但卡牌效果仍统一调用 Card.Apply / EffectSystem / StateSystem。
    private readonly Dictionary<int, List<Card>> drawPiles = new();
    private readonly Dictionary<int, List<Card>> discardPiles = new();
    private readonly Dictionary<int, List<Card>> hands = new();
    private readonly Random random = new();
    private readonly Dictionary<int, int> cardsPlayedThisTurn = new();
    private readonly Dictionary<int, int> hpLostThisBattle = new();
    private readonly Dictionary<int, int> hpLossEvents = new();
    private readonly Dictionary<int, PlayerLoadout> loadouts = new();
    private readonly Dictionary<int, HandSlot> selectedHands = new();
    private PendingHandChoice pendingChoice;
    private readonly Queue<int> monsterTurnQueue = new();
    private MonsterActionState activeMonsterAction;

    private sealed class PendingHandChoice
    {
        public int PlayerId;
        public Card SourceCard;
        public readonly List<(EffectType Effect, int[] Params)> Operations = new();
        /// <summary>该选牌是「消耗 1 张手牌」成本（T6：`EffectType.ConsumeSelectedHandCard`），而非卡牌操作。</summary>
        public bool ConsumeSelectedCard;
        /// <summary>还需消耗几张（默认 1）。</summary>
        public int ConsumeCount = 1;
    }

    /// <summary>One monster's action is deliberately advanced one visual beat at a time.</summary>
    private sealed class MonsterActionState
    {
        public BattleUnitPlacement Enemy;
        public BattleUnitPlacement Target;
        public EnemyIntentSpec Spec;
        public int MoveSteps;
        public int EffectIndex;
        public int[][] Intention;
        public bool NeedsTargetInRange;
        public bool FleeMode;
        public AxialHex LandingCell;
        public IReadOnlyList<AxialHex> PlannedPath;
        public AxialHex? AttackDirection;
    }

    public bool HasPendingHandChoice => pendingChoice != null;
    public enum HandSlot { Left, Right }
    public sealed class PlayerLoadout
    {
        public GroundObject LeftHand;
        public GroundObject RightHand;
        public GroundObject[] Items { get; } = new GroundObject[3];
        public bool AllowEquipmentInItemSlots { get; set; }
    }

    public const int WeaponThrowRange = 3;

    public CardSpatialSpec GetSpatialSpec(int cardId)
    {
        CardSpatialSpec configured = BattleCardSpatialRepository.ForCard(cardId);
        if (configured != null) return configured;
        Card card = hands.Values.SelectMany(x => x).FirstOrDefault(x => x.CardId == cardId)
            ?? LoadingSystem.CardDictionary.GetValueOrDefault(cardId);
        return new CardSpatialSpec
        {
            CardId = cardId,
            Shape = card != null && (card.Category == CardCategory.Attack || card.NeedTarget)
                ? CardSpatialShape.Single : CardSpatialShape.None,
            MaxRange = 1,
        };
    }

    public IReadOnlyCollection<AxialHex> GetCastCandidates(int cardId)
    {
        CardSpatialSpec spec = GetSpatialSpec(cardId); var origin = Selected.Coord;
        if (spec.Shape == CardSpatialShape.SelfMove) return Movement.LegalDestinations(SelectedId);
        if (spec.Shape == CardSpatialShape.Trap)   // 陷阱不是攻击：射程仍用卡牌配置
        {
            // 带落物结算的陷阱卡（如寒霜之地）可以瞄准「站着敌人」的格 —— 落物时按卡的 EffectType/Params 打该格上的敌方单位；
            // 纯落物卡（埋设陷阱 / 古树哨卫）维持「目标格必须为空」的既定规则（2026-10-04，P2-30 前半）。
            bool resolvesOnLanding = CardResolvesOnLanding(cardId);
            return BattleRangeResolver.CellsWithinRange(origin, spec.MaxRange).Where(x => x != origin && Board.Cells.TryGetValue(x, out var c) && c.Walkable
                && c.Trigger == null && c.Items.Count == 0
                && (Occupancy.At(x) == null || (resolvesOnLanding && Occupancy.At(x).Role != Selected.Role))).ToArray();
        }
        if (spec.Shape == CardSpatialShape.None) return new[] { origin };
        // 攻击范围类数值（射程 / 直线长度 / 扇形半径）一律取武器攻击距离；爆炸半径与突刺位移格数保留卡牌值。
        CardSpatialSpec effective = EffectiveSpec(spec);
        int effectiveRange = EffectiveCastRange(effective);
        // 按武器几何打：卡牌没声明特殊攻击方式，或武器类型不满足其条件。
        if (UsesWeaponGeometry(spec))
        {
            // 远程直线（弓 / 弓箭）：只能沿六个方向瞄准，候选 = 六个方向的完整直线（途中目标不截断高亮）。
            if (WeaponIsRay) return RayCandidates(effective);
            // 投掷（魔典 / 法典）：射程内任意落点，中间单位与障碍都不阻挡。
            if (WeaponIsThrown)
                return BattleRangeResolver.CellsWithinRange(origin, effectiveRange)
                    .Where(x => x != origin && Board.Cells.ContainsKey(x)).ToArray();
            // 近战类武器的几何细化未落地（长枪直线/扇形切换等在待接入清单），先沿用卡牌形状 + 武器距离。
        }
        if (spec.Shape == CardSpatialShape.Fan)
            return BattleRangeResolver.Neighbors(origin).Where(Board.Cells.ContainsKey).ToArray();
        if (spec.Shape == CardSpatialShape.Line)
        {
            var cells = new List<AxialHex>();
            foreach (var direction in BattleRangeResolver.SixNeighborOffsets)
            {
                if (spec.AttackMode == WeaponAttackMode.Thrust)
                {
                    // 突刺：位移沿方向走到卡牌规定的格数；这里的“长度”是击退攻击范围，取武器距离。
                    var thrust = new WeaponAttackSpec("card-thrust", Math.Max(1, effective.Length), 0, WeaponAttackMode.Thrust, 0);
                    cells.AddRange(BattleAttackTraceResolver.ResolveDirection(Board, Occupancy, origin, direction, thrust));
                    continue;
                }
                for (int i = 1; i <= effectiveRange; i++)
                {
                    var cell = new AxialHex(origin.Q + direction.Q * i, origin.R + direction.R * i);
                    if (!Board.Cells.ContainsKey(cell)) break;
                    cells.Add(cell);
                    bool blocked = Board.Cells[cell].BlocksSight || Board.Cells[cell].Kind == BattleCellKind.Obstacle || Occupancy.At(cell) != null;
                    if (blocked && !spec.Penetrates) break;
                }
            }
            return cells;
        }
        // 其余形状（单点 / 爆发）：格点目标，按武器距离取射程；被阻挡的目标格不可选。
        return BattleRangeResolver.CellsWithinRange(origin, effectiveRange)
            .Where(x => x != origin && Board.Cells.ContainsKey(x) && HasClearTrace(origin, x, spec.Penetrates)).ToArray();
    }

    public IReadOnlyCollection<AxialHex> GetAffectedCells(int cardId, AxialHex target)
    {
        CardSpatialSpec spec = GetSpatialSpec(cardId);
        CardSpatialSpec effective = EffectiveSpec(spec);
        if (UsesWeaponGeometry(spec) && WeaponIsRay
            && spec.Shape is CardSpatialShape.Single or CardSpatialShape.Burst or CardSpatialShape.Line or CardSpatialShape.Fan)
            return RayAffectedCells(effective, target);
        if (spec.Shape == CardSpatialShape.Line)
        {
            if (spec.AttackMode == WeaponAttackMode.Thrust)
            {
                var thrust = new WeaponAttackSpec("card-thrust", Math.Max(1, effective.Length), 0, WeaponAttackMode.Thrust, 0);
                return BattleAttackTraceResolver.Resolve(Board, Occupancy, Selected.Coord, target, thrust).ToArray();
            }
            AxialHex dir = BattleRangeResolver.PickLineDirection(Selected.Coord, target);
            var line = ResolveLineUntilBlocked(dir, Math.Max(1, effective.Length), spec.Penetrates).ToHashSet();
            if (spec.Explodes && line.Count > 0)
                line.UnionWith(BattleRangeResolver.CellsWithinRange(line.Last(), Math.Max(1, spec.Radius)).Where(Board.Cells.ContainsKey));
            return line;
        }
        return BattleRangeResolver.ResolveAffectedCells(Selected.Coord, target, effective).Where(Board.Cells.ContainsKey).ToArray();
    }
    /// <summary>远程直线武器（弓 / 弓箭）的弹道格，供表现使用：沿瞄准方向走满射程，遇首个阻挡即停。
    /// 非射线几何回落到结算影响格，表现层无需分支。</summary>
    public IReadOnlyCollection<AxialHex> GetAttackTraceCells(int cardId, AxialHex target)
    {
        CardSpatialSpec spec = GetSpatialSpec(cardId);
        if (!UsesWeaponGeometry(spec) || !WeaponIsRay
            || spec.Shape is not (CardSpatialShape.Single or CardSpatialShape.Burst or CardSpatialShape.Line or CardSpatialShape.Fan))
            return GetAffectedCells(cardId, target);
        CardSpatialSpec effective = EffectiveSpec(spec);
        if (!BattleRangeResolver.TryGetExactLineDirection(Selected.Coord, target, out AxialHex direction)) return Array.Empty<AxialHex>();
        return BattleAttackSystem.ResolveAxialRay(Board, Occupancy, Selected.Coord, direction,
            EffectiveCastRange(effective), spec.Penetrates, Selected.UnitId);
    }

    /// <summary>当前手位的装备类型（近战武器 / 远程武器 / 防具）。</summary>
    private EquipmentType CurrentWeaponType => CurrentWeapon.Type;

    private bool WeaponIsRay => CurrentWeapon.Mode == WeaponAttackMode.RangedLine;
    private bool WeaponIsThrown => CurrentWeapon.Mode == WeaponAttackMode.ThrowSingle;

    /// <summary>卡牌声明的特殊攻击方式属于哪类装备；null = 没声明特殊方式（单体 / 无空间列 / 位移 / 陷阱）。</summary>
    private static EquipmentType? CardSpecialModeType(CardSpatialSpec spec) => spec?.Shape switch
    {
        CardSpatialShape.Fan => EquipmentType.Melee,
        CardSpatialShape.Burst => EquipmentType.Ranged,
        CardSpatialShape.Line => spec.AttackMode is WeaponAttackMode.MeleeLine or WeaponAttackMode.Thrust or WeaponAttackMode.AdjacentSingle
            ? EquipmentType.Melee : EquipmentType.Ranged,
        _ => null,
    };

    /// <summary>卡牌声明的特殊攻击方式是否生效：武器类型必须匹配（近战型特殊方式需近战武器，远程型需远程武器）。</summary>
    private bool CardModeApplies(CardSpatialSpec spec) =>
        CardSpecialModeType(spec) is EquipmentType need && CurrentWeaponType == need;

    /// <summary>本次出牌按武器几何结算：卡牌没声明特殊攻击方式，或武器类型不满足其条件。</summary>
    private bool UsesWeaponGeometry(CardSpatialSpec spec) => !CardModeApplies(spec);

    /// <summary>
    /// 生效的空间规格：**攻击范围类数值一律取武器攻击距离**（射程 / 直线长度 / 扇形与环形半径 / 突刺的击退攻击范围）；
    /// **爆炸半径与突刺位移格数**保持卡牌配置（它们与攻击距离无关）。无武器时攻击距离为 1。
    /// </summary>
    private CardSpatialSpec EffectiveSpec(CardSpatialSpec spec)
    {
        if (spec == null) return null;
        int attackRange = Math.Max(1, CurrentAttackRange);
        return new CardSpatialSpec
        {
            CardId = spec.CardId,
            Shape = spec.Shape,
            AttackMode = spec.AttackMode,
            TrapId = spec.TrapId,
            Penetrates = spec.Penetrates,
            Explodes = spec.Explodes,
            Radius = spec.Radius,                                                                        // 爆炸半径：卡牌固定值
            MaxRange = spec.AttackMode == WeaponAttackMode.Thrust ? spec.MaxRange : attackRange,          // 突刺位移格数：卡牌规定
            Length = attackRange,                                                                          // 直线长度 / 突刺攻击范围
        };
    }

    /// <summary>卡牌射程：攻击范围类数值取武器攻击距离（生效规格里已经换算好）。</summary>
    private int EffectiveCastRange(CardSpatialSpec spec) =>
        spec.Shape is CardSpatialShape.Single or CardSpatialShape.Burst or CardSpatialShape.Line
            ? Math.Max(1, spec.MaxRange) : spec.MaxRange;

    /// <summary>远程直线武器的候选/范围标识：六个方向的完整直线。这里刻意用 <c>penetrates: true</c>
    /// 表示“只受地图边界限制”，途中单位与障碍不截断高亮——首个阻挡只决定弹道终点与命中谁（见 RayAffectedCells）。</summary>
    private IReadOnlyCollection<AxialHex> RayCandidates(CardSpatialSpec spec) =>
        BattleAttackSystem.ResolveRayCandidates(Board, Occupancy, Selected.Coord, EffectiveCastRange(spec), true, Selected.UnitId);

    /// <summary>远程直线武器的“红色”范围（受影响的格 = 弹道段）：先取精确方向与**被首个阻挡截断**的射线，
    /// 再按卡牌自身形状展开——单体/直线=该弹道段（到首个单位或障碍为止，含阻挡格）、爆发=以命中格为爆心、
    /// 扇形=以射手为顶点朝该方向。它与黄色候选（RayCandidates 的六方向完整直线）刻意不同：
    /// 黄色只表示“能瞄哪里”，红色表示“打出去会经过/命中哪里”，因此红会被目标与障碍截断。
    /// 目标不在六个方向之一时返回空：箭头不会拐弯。</summary>
    private IReadOnlyCollection<AxialHex> RayAffectedCells(CardSpatialSpec spec, AxialHex target)
    {
        if (!BattleRangeResolver.TryGetExactLineDirection(Selected.Coord, target, out AxialHex direction)) return Array.Empty<AxialHex>();
        IReadOnlyList<AxialHex> ray = BattleAttackSystem.ResolveAxialRay(Board, Occupancy, Selected.Coord, direction,
            EffectiveCastRange(spec), spec.Penetrates, Selected.UnitId);
        if (ray.Count == 0) return Array.Empty<AxialHex>();
        AxialHex impact = ray[^1];
        switch (spec.Shape)
        {
            case CardSpatialShape.Single:
                // 红色高亮 = 该方向的弹道段：从射手相邻格到首个阻挡为止（含阻挡格），被单位或障碍截断。
                // 段内不会再出现第二个单位（任何单位都会截断射线），所以结算仍是“直线上的第一个阻挡”。
                return ray;
            case CardSpatialShape.Burst:
                return BattleRangeResolver.CellsWithinRange(impact, Math.Max(1, spec.Radius)).Where(Board.Cells.ContainsKey).ToArray();
            case CardSpatialShape.Fan:
                return BattleRangeResolver.ResolveFanCells(Selected.Coord, direction, Math.Max(1, spec.MaxRange)).Where(Board.Cells.ContainsKey).ToArray();
            default:
                return ray;
        }
    }



    private bool HasClearTrace(AxialHex origin, AxialHex target, bool penetrates)
    {
        if (penetrates || AxialHex.Distance(origin, target) <= 1) return true;
        AxialHex direction = BattleRangeResolver.PickLineDirection(origin, target);
        int distance = AxialHex.Distance(origin, target);
        for (int i = 1; i < distance; i++)
        {
            var cell = new AxialHex(origin.Q + direction.Q * i, origin.R + direction.R * i);
            if (!Board.Cells.TryGetValue(cell, out var data) || data.BlocksSight || data.Kind == BattleCellKind.Obstacle || Occupancy.At(cell) != null) return false;
        }
        return true;
    }

    public BattlefieldSession(BattleMapDefinition definition)
    {
        Definition = definition;
        Generated = BattleDeploymentService.Generate(definition);
        Occupancy = new BattleOccupancyService(Board);
        Movement = new BattleMovementService(Board, Occupancy);
        // Validate every template before constructing any live actors.
        foreach (int id in definition.PlayerCharacterIds)
            if (!LoadingSystem.CharacterDictionary.ContainsKey(id)) throw new ArgumentException($"角色 {id} 不存在。");
        foreach (int id in definition.MonsterIds)
            if (!LoadingSystem.MonsterDictionary.ContainsKey(id)) throw new ArgumentException($"怪物 {id} 不存在。");
        for (int i = 0; i < 3; i++)
        {
            var character = new CharacterInstance(LoadingSystem.CharacterDictionary[definition.PlayerCharacterIds[i]]);
            character.Energy = character.Max_costs;
            Register(new BattleUnitPlacement(character, character.Name, BattlefieldRole.Player, character.MovesPerTurn), Generated.PlayerCoords[i]);
            PlayerIds.Add(character.UniqueInGameId);
            loadouts[character.UniqueInGameId] = new PlayerLoadout();
            selectedHands[character.UniqueInGameId] = HandSlot.Left;
        }
        for (int i = 0; i < definition.MonsterIds.Count; i++)
        {
            var monster = new MonsterInstance(LoadingSystem.MonsterDictionary[definition.MonsterIds[i]]);
            // 关卡 CSV 的 InitialValue：开局写入初始生命与初始状态（见 UnitInitialStateConfig）。
            string initialValue = i < definition.MonsterInitialValues.Count ? definition.MonsterInitialValues[i] : string.Empty;
            if (!string.IsNullOrWhiteSpace(initialValue))
            {
                // 报错上下文写关卡 Id（没有关卡来源的纯地图路径退回 MapId）：同一张地图被多个关卡复用，只有 MapId 定位不到出错关卡。
                string source = string.IsNullOrWhiteSpace(definition.LevelId) ? definition.MapId : definition.LevelId;
                UnitInitialStateConfig.Apply(monster, initialValue, $"{monster.Name}（关卡 {source} 第 {i + 1} 只怪物）");
            }
            Register(new BattleUnitPlacement(monster, monster.Name, BattlefieldRole.Enemy, 0), Generated.EnemyCoords[i]);
            // 怪物实例键：窃取金币等"按实例"记账的稳定标识（关卡 CSV 的 InstanceId）。
            string instanceKey = i < definition.MonsterInstanceIds.Count && !string.IsNullOrWhiteSpace(definition.MonsterInstanceIds[i])
                ? definition.MonsterInstanceIds[i]
                : $"unit-{monster.UniqueInGameId}";
            monsterInstanceKeys[monster.UniqueInGameId] = instanceKey;
        }
        SelectedId = PlayerIds[0];
        InitializeDecks();
        battleRules = BattleRuleRegistry.Create(definition.Rules);
        PrepareEnemyIntentions();
        Occupancy.Changed += Notify;
        Board.CellChanged += OnCellChanged;
        Movement.Entered += OnEntered;
    }

    private void Register(BattleUnitPlacement placement, AxialHex coord)
    {
        if (!Occupancy.TryPlace(placement, coord, out string error)) throw new InvalidOperationException(error);
        placement.Unit.OnDead = () =>
        {
            Occupancy.RemoveFromBoard(placement.UnitId, BattlefieldPresence.Defeated);
            EvaluateOutcome();
        };
    }

    public bool Select(int unitId)
    {
        if (!PlayerIds.Contains(unitId) || Occupancy.Placements[unitId].Presence != BattlefieldPresence.Active) return false;
        SelectedId = unitId; Notify(); return true;
    }

    public BattleUnitPlacement Selected => Occupancy.Placements[SelectedId];
    public PlayerLoadout SelectedLoadout => loadouts[SelectedId];
    public PlayerLoadout GetLoadout(int playerId) => loadouts.TryGetValue(playerId, out var loadout) ? loadout : null;
    public HandSlot SelectedHand => selectedHands[SelectedId];
    public int CurrentAttackRange
    {
        get
        {
            GroundObject equipment = SelectedHand == HandSlot.Left ? SelectedLoadout.LeftHand : SelectedLoadout.RightHand;
            return equipment?.Kind == GroundObjectKind.Equipment ? BattleWeaponCatalog.Resolve(equipment).AttackRange : 1;
        }
    }

    public WeaponAttackSpec CurrentWeapon => BattleWeaponCatalog.Resolve(
        SelectedHand == HandSlot.Left ? SelectedLoadout.LeftHand : SelectedLoadout.RightHand);

    public IReadOnlyCollection<AxialHex> GetDefaultAttackCandidates()
    {
        var source = Selected;
        if (CurrentWeapon.Mode == WeaponAttackMode.RangedLine)
            return BattleRangeResolver.Neighbors(source.Coord).Where(Board.Cells.ContainsKey).ToArray();
        return Board.Cells.Keys.Where(cell => BattleAttackTraceResolver.Resolve(Board, Occupancy, source.Coord, cell, CurrentWeapon).Count > 0).ToArray();
    }

    public IReadOnlyCollection<AxialHex> GetDefaultAttackAffectedCells(AxialHex target) =>
        BattleAttackTraceResolver.Resolve(Board, Occupancy, Selected.Coord, target, CurrentWeapon).ToArray();

    /// <summary>Normal attacks are free. The selected hand determines its weapon specification.</summary>
    public bool TryPerformDefaultAttack(AxialHex target, out string error)
    {
        error = "";
        if (Phase != BattlePhase.Player || IsFinished) { error = "当前不是玩家行动阶段。"; return false; }
        var source = Selected;
        var spec = CurrentWeapon;
        var cells = BattleAttackTraceResolver.Resolve(Board, Occupancy, source.Coord, target, spec);
        if (cells.Count == 0) { error = "目标不在该武器的合法攻击范围内。"; return false; }

        int hitCount = 0;
        foreach (AxialHex cell in cells)
        {
            if (!Board.Cells.TryGetValue(cell, out var data)) break;
            var victim = Occupancy.At(cell);
            if (spec.Mode != WeaponAttackMode.ThrowSingle && (data.Kind == BattleCellKind.Obstacle || data.BlocksSight)) break;
            if (victim == null) continue;
            int beforeShield = victim.Unit.Shield;
            int beforeHp = victim.Unit.HP;
            using (new BattlefieldEffectTargetScope(source.Unit, victim.Unit, new[] { victim.Unit }, Array.Empty<IUnitInstance>(),
                hpLostThisBattle.GetValueOrDefault(source.UnitId), playerTurn: true, canAttack: CanDefaultAttack))
            {
                // Weapon attack bonus has already been applied to the equipped user's Attack stat.
                EffectSystem.ApplyAttack(source.Unit, victim.Unit, Array.Empty<int>());
            }
            int damage = Math.Max(0, beforeHp - victim.Unit.HP);
            AttackResolved?.Invoke(new BattlefieldAttackEvent(++attackEventSequence, source.UnitId, victim.UnitId,
                source.Coord, cell, spec.Mode, damage, Math.Max(0, beforeShield - victim.Unit.Shield), victim.Unit.HP <= 0,
                beforeHp, victim.Unit.HP, beforeShield, victim.Unit.Shield, cells,
                BattleRangeResolver.PickLineDirection(source.Coord, target)));
            if (spec.Mode == WeaponAttackMode.Thrust) TryApplyThrustKnockback(source, victim, cell);
            TrackHpLoss(victim.Unit, beforeHp); hitCount++;
            Occupancy.SyncDeaths();
            if (spec.Mode is WeaponAttackMode.AdjacentSingle or WeaponAttackMode.RangedLine or WeaponAttackMode.Thrust) break;
            // Melee line continues through units only when this strike broke their shield.
            if (spec.Mode == WeaponAttackMode.MeleeLine && beforeShield > 0 && victim.Unit.Shield > 0) break;
        }
        if (hitCount == 0 && spec.Mode == WeaponAttackMode.RangedLine)
        {
            AxialHex endpoint = cells.Last();
            AttackResolved?.Invoke(new BattlefieldAttackEvent(++attackEventSequence, source.UnitId, null, source.Coord, endpoint,
                spec.Mode, 0, 0, false, AffectedCells: cells,
                Direction: BattleRangeResolver.PickLineDirection(source.Coord, target)));
            Message?.Invoke($"{source.Name} 向选定方向射击，弹道未命中单位。 ");
            Notify(); return true;
        }
        if (hitCount == 0 && spec.Mode != WeaponAttackMode.ThrowSingle) { error = "攻击路径上没有可命中的单位。"; return false; }
        Message?.Invoke($"{source.Name} 使用 {spec.DefinitionId} 普通攻击，命中 {hitCount} 个单位（不消耗资源）。");
        EvaluateOutcome(); Notify(); return true;
    }

    public void SelectHand(HandSlot hand) { selectedHands[SelectedId] = hand; Notify(); }

    public bool TryEquipFromCurrentCell(string instanceId, HandSlot hand, out string error)
    {
        error = "";
        GroundObject equipment = Board.Cells[Selected.Coord].Items.FirstOrDefault(x => x.InstanceId == instanceId && x.Kind == GroundObjectKind.Equipment);
        if (equipment == null) { error = "当前格没有该装备。"; return false; }
        WeaponAttackSpec equippedWeapon = BattleWeaponCatalog.Resolve(equipment);
        var loadout = SelectedLoadout;
        var replaced = new List<GroundObject>();
        if (equipment.HandsRequired == 2)
        {
            if (loadout.LeftHand != null) replaced.Add(loadout.LeftHand);
            if (loadout.RightHand != null && !ReferenceEquals(loadout.RightHand, loadout.LeftHand)) replaced.Add(loadout.RightHand);
        }
        else
        {
            GroundObject old = hand == HandSlot.Left ? loadout.LeftHand : loadout.RightHand;
            if (old != null) replaced.Add(old);
            if (old?.HandsRequired == 2) { loadout.LeftHand = null; loadout.RightHand = null; }
        }
        if (!Board.TryRemoveObject(Selected.Coord, equipment.InstanceId, out _)) { error = "装备已被移动。"; return false; }
        foreach (var old in replaced)
        {
            Selected.SetEquipmentMoveModifier(old.InstanceId, 0);
            Selected.SetEquipmentCombatModifiers(old.InstanceId, 0, 0);
            if (!Board.TryAddObject(Selected.Coord, old, out error)) throw new InvalidOperationException("换装事务失败：" + error);
        }
        if (equipment.HandsRequired == 2) loadout.LeftHand = loadout.RightHand = equipment;
        else if (hand == HandSlot.Left) loadout.LeftHand = equipment;
        else loadout.RightHand = equipment;
        Selected.SetEquipmentMoveModifier(equipment.InstanceId, equippedWeapon.MoveBonus);
        Selected.SetEquipmentCombatModifiers(equipment.InstanceId, equippedWeapon.DamageBonus, equippedWeapon.DefenseValue);
        selectedHands[SelectedId] = hand; Notify();
        Message?.Invoke($"{Selected.Name} 装备 {equipment.DefinitionId}：攻击距离 {equippedWeapon.AttackRange}，移动额度 {equippedWeapon.MoveBonus:+#;-#;0}。");
        return true;
    }

    public bool TryPickItemFromCurrentCell(string instanceId, int slot, out string error)
    {
        error = "";
        if (slot < 0 || slot >= 3) { error = "道具栏位不可用。"; return false; }
        GroundObject item = Board.Cells[Selected.Coord].Items.FirstOrDefault(x => x.InstanceId == instanceId && x.Kind == GroundObjectKind.Item);
        if (item == null || !Board.TryRemoveObject(Selected.Coord, instanceId, out _)) { error = "当前格没有该道具。"; return false; }
        // 目标栏已有道具时交换：把原栏道具放回当前格。
        if (SelectedLoadout.Items[slot] != null) Board.TryAddObject(Selected.Coord, SelectedLoadout.Items[slot], out _);
        SelectedLoadout.Items[slot] = item; Notify(); return true;
    }

    public IReadOnlyCollection<AxialHex> GetWeaponThrowCandidates()
    {
        var result = new List<AxialHex>(); AxialHex origin = Selected.Coord;
        foreach (var dir in BattleRangeResolver.SixNeighborOffsets)
        {
            for (int i = 1; i <= WeaponThrowRange; i++)
            {
                var cell = new AxialHex(origin.Q + dir.Q * i, origin.R + dir.R * i);
                if (!Board.Cells.TryGetValue(cell, out var data)) break;
                result.Add(cell);
                if (data.Kind == BattleCellKind.Obstacle || data.BlocksSight || Occupancy.At(cell) != null) break;
            }
        }
        return result;
    }

    public bool TryThrowEquippedWeapon(HandSlot hand, AxialHex target, out string error)
    {
        GroundObject weapon = hand == HandSlot.Left ? SelectedLoadout.LeftHand : SelectedLoadout.RightHand;
        if (weapon == null || weapon.Kind != GroundObjectKind.Equipment) { error = "该手位没有可投掷的武器。"; return false; }
        return TryThrowWeapon(weapon, target, () =>
        {
            if (ReferenceEquals(SelectedLoadout.LeftHand, weapon)) SelectedLoadout.LeftHand = null;
            if (ReferenceEquals(SelectedLoadout.RightHand, weapon)) SelectedLoadout.RightHand = null;
            Selected.SetEquipmentMoveModifier(weapon.InstanceId, 0);
            Selected.SetEquipmentCombatModifiers(weapon.InstanceId, 0, 0);
        }, out error);
    }

    public bool TryThrowWeaponFromCurrentCell(string instanceId, AxialHex target, out string error)
    {
        GroundObject weapon = Board.Cells[Selected.Coord].Items.FirstOrDefault(x => x.InstanceId == instanceId && x.Kind == GroundObjectKind.Equipment);
        if (weapon == null) { error = "当前格没有该武器。"; return false; }
        return TryThrowWeapon(weapon, target, () => Board.TryRemoveObject(Selected.Coord, weapon.InstanceId, out _), out error);
    }

    public bool TrySwapEquippedHands(out string error)
    {
        error = ""; var loadout = SelectedLoadout;
        if (loadout.LeftHand == null || loadout.RightHand == null) { error = "需要左右手都装备单手装备才能交换。"; return false; }
        if (ReferenceEquals(loadout.LeftHand, loadout.RightHand) || loadout.LeftHand.HandsRequired == 2 || loadout.RightHand.HandsRequired == 2)
        { error = "双手装备占满两个手位，不能拆分交换。"; return false; }
        (loadout.LeftHand, loadout.RightHand) = (loadout.RightHand, loadout.LeftHand); Notify(); return true;
    }

    public bool TryDropEquippedWeaponOnCurrentCell(HandSlot hand, out string error)
    {
        error = ""; var weapon = hand == HandSlot.Left ? SelectedLoadout.LeftHand : SelectedLoadout.RightHand;
        if (weapon == null) { error = "该手位为空。"; return false; }
        if (!Board.TryAddObject(Selected.Coord, weapon, out error)) return false;
        if (ReferenceEquals(SelectedLoadout.LeftHand, weapon)) SelectedLoadout.LeftHand = null;
        if (ReferenceEquals(SelectedLoadout.RightHand, weapon)) SelectedLoadout.RightHand = null;
        Selected.SetEquipmentMoveModifier(weapon.InstanceId, 0);
        Selected.SetEquipmentCombatModifiers(weapon.InstanceId, 0, 0);
        Notify(); return true;
    }

    private bool TryThrowWeapon(GroundObject weapon, AxialHex target, Action removeWeapon, out string error)
    {
        error = "";
        if (Phase != BattlePhase.Player || IsFinished) { error = "当前不是玩家行动阶段。"; return false; }
        if (!GetWeaponThrowCandidates().Contains(target)) { error = "武器投掷只能瞄准三格内的直线格。"; return false; }
        var victim = Occupancy.At(target);
        if (victim != null)
        {
            int before = victim.Unit.HP;
            int shieldBefore = victim.Unit.Shield;
            EffectResult result;
            using (new BattlefieldEffectTargetScope(Selected.Unit, victim.Unit, new[] { victim.Unit }, Array.Empty<IUnitInstance>(), 0, true, CanDefaultAttack))
                result = EffectSystem.ApplyAttack(Selected.Unit, victim.Unit, Array.Empty<int>());
            AttackResolved?.Invoke(new BattlefieldAttackEvent(++attackEventSequence, Selected.UnitId, victim.UnitId,
                Selected.Coord, target, WeaponAttackMode.ThrowSingle, Math.Max(0, result.TargetHpBefore - result.TargetHpAfter),
                Math.Max(0, result.TargetShieldBefore - result.TargetShieldAfter), result.TargetHpAfter <= 0,
                before, victim.Unit.HP, shieldBefore, victim.Unit.Shield));
            TrackHpLoss(victim.Unit, before);
            Message?.Invoke($"武器投掷命中 {victim.Name}：{result.TotalValue} 伤害，护盾吸收 {result.ShieldAbsorbed}，HP {result.TargetHpBefore}→{result.TargetHpAfter}。"
                + (victim.Role == Selected.Role ? " 警告：命中友方单位。" : ""));
        }
        removeWeapon();
        AxialHex landing = Board.IsWalkable(target) ? target : Selected.Coord;
        if (!Board.TryAddObject(landing, weapon, out error)) { error = "武器投掷落点无法放置。"; return false; }
        Occupancy.SyncDeaths(); EvaluateOutcome(); Notify();
        Message?.Invoke($"{Selected.Name} 投掷 {weapon.DefinitionId} 至 ({landing.Q},{landing.R})。");
        return true;
    }

    public bool TryUseItem(int slot, out string error)
    {
        error = "";
        if (slot < 0 || slot >= 3 || SelectedLoadout.Items[slot] == null) { error = "该道具栏为空。"; return false; }
        GroundObject item = SelectedLoadout.Items[slot]; int before = Selected.Unit.HP;
        Selected.Unit.HP = Math.Min(Selected.Unit.Max_HP, Selected.Unit.HP + item.HealAmount);
        SelectedLoadout.Items[slot] = null; Notify();
        Message?.Invoke($"{Selected.Name} 使用 {item.DefinitionId}，生命 {before}→{Selected.Unit.HP}。");
        return true;
    }

    // ── 投掷型道具：需要选定目标，从道具栏或当前格直接使用 ──

    public IReadOnlyCollection<AxialHex> GetItemCastCandidates(GroundObject item)
    {
        if (item == null) return Array.Empty<AxialHex>();
        AxialHex origin = Selected.Coord;

        // 直线投掷：仅允许瞄准 6 个方向直线上的格子（沿 ItemLength 延伸，遇单位/障碍即停）。
        if (item.SpatialShape == ItemSpatialShape.Line)
        {
            var lineCells = new List<AxialHex>();
            int length = Math.Max(1, item.ItemLength);
            foreach (var dir in BattleRangeResolver.SixNeighborOffsets)
            {
                for (int i = 1; i <= length; i++)
                {
                    var cell = new AxialHex(origin.Q + dir.Q * i, origin.R + dir.R * i);
                    if (!Board.Cells.ContainsKey(cell)) break;
                    lineCells.Add(cell);
                    bool blocked = Board.Cells[cell].Kind == BattleCellKind.Obstacle || Board.Cells[cell].BlocksSight || Occupancy.At(cell) != null;
                    if (blocked) break; // 首个目标之后不可再瞄准（不能隔墙/隔人瞄准）
                }
            }
            return lineCells.ToArray();
        }

        int range = Math.Max(1, item.ItemMaxRange);
        IEnumerable<AxialHex> cells = BattleRangeResolver.CellsWithinRange(origin, range).Where(Board.Cells.ContainsKey);
        if (item.ItemTrapId.Length > 0)
            cells = cells.Where(x => x != origin && Board.Cells[x].Walkable && Board.Cells[x].Trigger == null
                && Board.Cells[x].Items.Count == 0 && Occupancy.At(x) == null);
        return cells.ToArray();
    }

    public bool IsValidItemTarget(GroundObject item, AxialHex target) =>
        item != null && BattleRangeResolver.Distance(Selected.Coord, target) <= Math.Max(1, item.ItemMaxRange)
        && GetItemCastCandidates(item).Contains(target);

    /// <summary>
    /// 投掷型道具的“实际受影响格”。直线攻击默认会被方向上的第一个单位/障碍挡住，
    /// 只命中该格（后续不再穿透），除非道具被标记为穿透（暂未提供穿透开关，后续可扩展）。
    /// </summary>
    public IReadOnlyCollection<AxialHex> GetItemAffectedCells(GroundObject item, AxialHex target)
    {
        if (item == null) return Array.Empty<AxialHex>();
        if (item.SpatialShape != ItemSpatialShape.Line)
            return BattleRangeResolver.ResolveItemAffectedCells(Selected.Coord, target, item).ToArray();

        // 直线：从施法者 origin 沿投掷方向走 length 格，遇第一个单位或障碍即停（首个目标）。
        AxialHex dir = BattleRangeResolver.PickLineDirection(Selected.Coord, target);
        return ResolveLineUntilBlocked(dir, Math.Max(1, item.ItemLength), penetrates: false);
    }

    /// <summary>直线攻击专用：从施法者沿 dir 走 length 格，命中方向上的第一个单位/障碍即停下（非穿透时）。</summary>
    private IReadOnlyCollection<AxialHex> ResolveLineUntilBlocked(AxialHex dir, int length, bool penetrates)
    {
        var affected = new List<AxialHex>();
        AxialHex origin = Selected.Coord;
        for (int i = 1; i <= length; i++)
        {
            var cell = new AxialHex(origin.Q + dir.Q * i, origin.R + dir.R * i);
            if (!Board.Cells.ContainsKey(cell)) break;
            affected.Add(cell);
            bool blocked = Board.Cells[cell].Kind == BattleCellKind.Obstacle || Board.Cells[cell].BlocksSight || Occupancy.At(cell) != null;
            if (blocked && !penetrates) break;
        }
        return affected;
    }

    public bool TryUseItemAt(int slot, AxialHex? target, out string error)
    {
        error = "";
        if (Phase != BattlePhase.Player || IsFinished) { error = "当前不是玩家出牌阶段。"; return false; }
        if (slot < 0 || slot >= 3 || SelectedLoadout.Items[slot] == null) { error = "该道具栏为空。"; return false; }
        GroundObject item = SelectedLoadout.Items[slot];
        if (item.NeedsTarget)
        {
            if (!target.HasValue) { error = "该道具需要选定目标。"; return false; }
            if (!IsValidItemTarget(item, target.Value)) { error = "超出投掷射程或目标不可用。"; return false; }
        }
        ApplyItemEffect(Selected, item, target, out error);
        if (error.Length > 0) return false;
        SelectedLoadout.Items[slot] = null;
        Notify();
        return true;
    }

    public bool TryUseItemFromCurrentCell(string instanceId, AxialHex? target, out string error)
    {
        error = "";
        if (Phase != BattlePhase.Player || IsFinished) { error = "当前不是玩家出牌阶段。"; return false; }
        if (HasPendingHandChoice) { error = "请先完成当前选牌效果。"; return false; }
        GroundObject item = Board.Cells[Selected.Coord].Items.FirstOrDefault(x => x.InstanceId == instanceId && x.Kind == GroundObjectKind.Item);
        if (item == null) { error = "当前格没有该道具。"; return false; }
        if (item.NeedsTarget)
        {
            if (!target.HasValue) { error = "该道具需要选定目标。"; return false; }
            if (!IsValidItemTarget(item, target.Value)) { error = "超出投掷射程或目标不可用。"; return false; }
        }
        ApplyItemEffect(Selected, item, target, out error);
        if (error.Length > 0) return false;
        Board.TryRemoveObject(Selected.Coord, instanceId, out _);
        Notify();
        return true;
    }

    public bool TryMoveItemBetweenSlots(int from, int to, out string error)
    {
        error = "";
        if (from < 0 || from >= 3 || to < 0 || to >= 3) { error = "道具栏位置无效。"; return false; }
        if (SelectedLoadout.Items[from] == null) { error = "源道具栏为空。"; return false; }
        if (to == from) return true;
        // 目标栏已有道具时直接交换，便于自由挪动。
        if (SelectedLoadout.Items[to] != null)
        {
            (SelectedLoadout.Items[from], SelectedLoadout.Items[to]) = (SelectedLoadout.Items[to], SelectedLoadout.Items[from]);
        }
        else
        {
            SelectedLoadout.Items[to] = SelectedLoadout.Items[from];
            SelectedLoadout.Items[from] = null;
        }
        Notify();
        return true;
    }

    public bool TryDropItemOnCurrentCell(int slot, out string error)
    {
        error = "";
        if (slot < 0 || slot >= 3 || SelectedLoadout.Items[slot] == null) { error = "该道具栏为空。"; return false; }
        if (!Board.TryAddObject(Selected.Coord, SelectedLoadout.Items[slot], out error))
        {
            if (error.Length == 0) error = "当前格无法放下该道具。";
            return false;
        }
        SelectedLoadout.Items[slot] = null;
        Notify();
        return true;
    }

    private void ApplyItemEffect(BattleUnitPlacement source, GroundObject item, AxialHex? target, out string error)
    {
        error = "";
        if (item.NeedsTarget && target.HasValue)
        {
            var affected = GetItemAffectedCells(item, target.Value);
            var tracked = Occupancy.Placements.Values.Where(x => x.Presence == BattlefieldPresence.Active).ToArray();
            var beforeHp = tracked.ToDictionary(x => x.UnitId, x => x.Unit.HP);

            if (item.ItemTrapId.Length > 0)
            {
                var trap = new GroundObject($"trap-{source.UnitId}-{Guid.NewGuid():N}".Substring(0, 20), item.ItemTrapId,
                    GroundObjectKind.Trap, EntryTriggerMode.EveryEntry);
                if (!Board.TryAddObject(target.Value, trap, out error))
                {
                    if (error.Length == 0) error = "陷阱目标必须没有单位或物品。";
                    return;
                }
                Message?.Invoke($"{source.Name} 在 ({target.Value.Q},{target.Value.R}) 投放了陷阱：{trap.DefinitionId}。");
                return;
            }

            var hitLogs = new List<string>();
            foreach (var cell in affected)
            {
                var u = Occupancy.At(cell);
                if (u == null || u.Presence != BattlefieldPresence.Active) continue;
                if (item.DamageAmount > 0)
                {
                    int shieldBefore = u.Unit.Shield, hpBefore = u.Unit.HP;
                    int absorbed = Math.Min(shieldBefore, item.DamageAmount);
                    u.Unit.Shield -= absorbed;
                    int hpDamage = item.DamageAmount - absorbed;
                    u.Unit.HP = Math.Max(0, u.Unit.HP - hpDamage);
                    hitLogs.Add($"命中 {u.Name}：{item.DamageAmount} 伤害，护盾吸收 {absorbed}，HP {hpBefore}→{u.Unit.HP}"
                        + (u.Role == source.Role ? "（警告：友方伤害）" : ""));
                }
                else if (u.Role == BattlefieldRole.Player && item.HealAmount > 0)
                    u.Unit.HP = Math.Min(u.Unit.Max_HP, u.Unit.HP + item.HealAmount);
            }
            foreach (var unit in tracked) TrackHpLoss(unit.Unit, beforeHp[unit.UnitId]);
            Message?.Invoke(hitLogs.Count > 0 ? $"{source.Name} 使用 {item.DefinitionId}：{string.Join("；", hitLogs)}。"
                : $"{source.Name} 使用 {item.DefinitionId}：未命中单位。"
            );
            Occupancy.SyncDeaths(); EvaluateOutcome();
            return;
        }

        int before = source.Unit.HP;
        source.Unit.HP = Math.Min(source.Unit.Max_HP, source.Unit.HP + item.HealAmount);
        Message?.Invoke($"{source.Name} 使用 {item.DefinitionId}，生命 {before}→{source.Unit.HP}。");
    }

    internal void NextTestRound()
    {
        Round++;
        foreach (int id in PlayerIds)
        {
            var p = Occupancy.Placements[id];
            if (p.Presence == BattlefieldPresence.Active && p.Unit is CharacterInstance character)
                character.Energy = character.Max_costs;
        }
        Movement.StartPlayerTurn(); Notify();
        Message?.Invoke("烟测辅助回合：重置能量与移动次数。");
    }

    public void SetTestEquipmentBonus(int bonus)
    {
        Selected.SetEquipmentMoveModifier("foundation-test-equipment", bonus); Notify();
    }

    // ── 手牌、抽牌和完整回合循环 ──

    private void InitializeDecks()
    {
        foreach (int playerId in PlayerIds)
        {
            var placement = Occupancy.Placements[playerId];
            if (placement.Unit is not CharacterInstance character) continue;
            List<int> defaultCardIds = LoadingSystem.GetCharacterDefaultCardIdListByKey(
                character.id, LoadingSystem.CharacterDefaultDeckCsvPathKey, true);
            List<Card> draw = new();
            foreach (int cardId in defaultCardIds)
            {
                if (LoadingSystem.CardDictionary.TryGetValue(cardId, out Card template))
                {
                    Card deckCard = template.CreateDeckInstance();
                    character.DefaultDeck.Add(deckCard);
                    draw.Add(deckCard.CreateBattleInstanceFromDeckCard());
                }
            }
            character.drawpile = draw;
            character.discardpile = new List<Card>();
            character.handcards = new List<Card>();
            drawPiles[playerId] = character.drawpile;
            discardPiles[playerId] = character.discardpile;
            hands[playerId] = character.handcards;
            DrawCards(playerId, drawCardCount(placement));
        }
    }

    /// <summary>Replaces the temporary default decks with the persisted run state after normal battlefield construction.</summary>
    public void RestoreRunState(IReadOnlyList<RunCharacterSlotSave> characterSlots, IReadOnlyList<List<RunDeckEntry>> deckSlots)
    {
        if (characterSlots == null || deckSlots == null || characterSlots.Count != PlayerIds.Count || deckSlots.Count != PlayerIds.Count)
            throw new ArgumentException("运行局角色或卡组槽位数量与六边形战场不一致。");
        for (int i = 0; i < PlayerIds.Count; i++)
        {
            var placement = Occupancy.Placements[PlayerIds[i]];
            if (placement.Unit is not CharacterInstance character) continue;
            RunCharacterSlotSave slot = characterSlots[i];
            character.HP = Math.Max(1, Math.Min(character.Max_HP, slot.CurrentHp));
            RestoreRunWeapon(placement, ResolveRunHandDefinition(slot, RunEquipmentSystem.LeftHand),
                ResolveRunHandDefinition(slot, RunEquipmentSystem.RightHand), i);
            ApplyRunBodyEquipmentModifiers(placement, slot, i);
            character.DefaultDeck.Clear();
            var draw = new List<Card>();
            foreach (RunDeckEntry entry in deckSlots[i] ?? new List<RunDeckEntry>())
            {
                if (entry == null || !LoadingSystem.CardDictionary.TryGetValue(entry.CardId, out Card template)) continue;
                Card deckCard = template.CreateDeckInstance();
                deckCard.PermanentUpgradeLevel = Math.Max(0, entry.PermanentUpgradeLevel);
                character.DefaultDeck.Add(deckCard);
                draw.Add(deckCard.CreateBattleInstanceFromDeckCard());
            }
            character.drawpile = draw;
            character.discardpile = new List<Card>();
            character.handcards = new List<Card>();
            drawPiles[placement.UnitId] = character.drawpile;
            discardPiles[placement.UnitId] = character.discardpile;
            hands[placement.UnitId] = character.handcards;
            DrawCards(placement.UnitId, drawCardCount(placement));
        }
        Notify();
    }
    /// <summary>
    /// 食物效果（[食物系统](../../../README/玩法说明文档/系统规则/物品系统/食物系统.md) §三，2026-10-02 口径 ②）：
    /// 在 `RestoreRunState` 之后、第一回合之前调用 —— 把**仍在寿命轴上**的效果套到每名在场角色身上。
    /// 已接：`Shield`（开局护盾）、`Heal`、`DrawCard`（抽牌）、`AddState`（攻击 +N / 虚弱等）、
    /// `ClearFirstNormalDebuff`（清 1 层普通弱化）、`ShieldOnFirstHit`（首次受伤额外护盾）、
    /// `SurviveFatalOnce`（首次濒死回复 N% 最大生命）。返回逐条说明（控制台 / 烟测断言用）。
    /// </summary>
    public List<string> ApplyBattleStartEffects(IReadOnlyList<RunFoodEffectSave> effects)
    {
        var logs = new List<string>();
        if (effects == null || effects.Count == 0)
        {
            return logs;
        }

        foreach (RunFoodEffectSave effect in effects)
        {
            if (effect == null)
            {
                continue;
            }

            EffectType type = (EffectType)effect.EffectType;
            int first = effect.Params != null && effect.Params.Count > 0 ? effect.Params[0] : 0;
            int second = effect.Params != null && effect.Params.Count > 1 ? effect.Params[1] : 0;

            foreach (int playerId in PlayerIds)
            {
                if (!Occupancy.Placements.TryGetValue(playerId, out BattleUnitPlacement placement)
                    || placement.Unit is not CharacterInstance character
                    || placement.Presence != BattlefieldPresence.Active)
                {
                    continue;
                }

                switch (type)
                {
                    case EffectType.Shield:
                        character.Shield += Math.Max(0, first);
                        break;
                    case EffectType.Heal:
                        character.HP = Math.Min(character.Max_HP, character.HP + Math.Max(0, first));
                        break;
                    case EffectType.DrawCard:
                        DrawCards(playerId, Math.Max(0, first));
                        break;
                    case EffectType.AddState:
                        StateSystem.AddOrUpdateState(character, (StateType)first, Math.Max(1, second));
                        break;
                    case EffectType.ClearFirstNormalDebuff:
                        StateSystem.TryRemoveFirstNormalDebuff(character, out _);
                        break;
                    case EffectType.ShieldOnFirstHit:
                        character.PendingShieldOnFirstHit += Math.Max(0, first);
                        break;
                    case EffectType.SurviveFatalOnce:
                        character.PendingSurviveFatalOncePercent = Math.Max(character.PendingSurviveFatalOncePercent, first);
                        break;
                }
            }

            logs.Add($"{RunFoodSystem.DescribeEffect(effect)}（来源：{effect.SourceFoodId}）");
        }

        if (logs.Count > 0)
        {
            Notify();
        }

        return logs;
    }



    /// <summary>
    /// 局外手位 → 开场的取值（装备系统交互案 §五）：左右手字段是真相；两者都空时回落到旧字段
    /// `EquippedWeaponDefinitionId`（只认左手）—— 旧档与只写旧字段的烟测夹具因此照旧可用。
    /// </summary>
    private static string ResolveRunHandDefinition(RunCharacterSlotSave slot, int hand)
    {
        if (slot == null) return string.Empty;
        string left = slot.LeftHandDefinitionId ?? string.Empty;
        string right = slot.RightHandDefinitionId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
        {
            return hand == RunEquipmentSystem.LeftHand ? (slot.EquippedWeaponDefinitionId ?? string.Empty) : string.Empty;
        }

        return hand == RunEquipmentSystem.LeftHand ? left : right;
    }

    /// <summary>
    /// 开场的局外装备还原（装备系统交互案 §四，SchemaVersion 5）：单手各占一槽（两条 `GroundObject`、
    /// 各自的移动 / 攻防修正分别记账）；双手装备同名占满两槽（**同一条** `GroundObject`，修正只记一次，
    /// 与既有口径一致）。未知定义照旧抛错（不静默吞掉「装备名写错」）。
    /// </summary>
    private void RestoreRunWeapon(BattleUnitPlacement placement, string leftDefinitionId, string rightDefinitionId, int slotIndex)
    {
        string left = leftDefinitionId ?? string.Empty;
        string right = rightDefinitionId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right)) return;

        WeaponAttackSpec leftWeapon = string.IsNullOrWhiteSpace(left) ? null : RequireRunWeapon(left);
        WeaponAttackSpec rightWeapon = string.IsNullOrWhiteSpace(right) ? null : RequireRunWeapon(right);
        PlayerLoadout loadout = loadouts[placement.UnitId];

        // 双手装备独占两槽（玩法 §5.2）：任意一手是双手武器都按占满两槽处理；档里另一手还留着别的装备
        // （写坏的档）时以双手装备为准 —— 界面侧（RunEquipmentSystem）不允许产生这种组合。
        WeaponAttackSpec twoHanded = leftWeapon?.HandsRequired == 2 ? leftWeapon
            : rightWeapon?.HandsRequired == 2 ? rightWeapon
            : null;
        if (twoHanded != null)
        {
            GroundObject twoHandedItem = CreateRunEquipment($"run-equipped-{slotIndex + 1}", twoHanded);
            loadout.LeftHand = loadout.RightHand = twoHandedItem;
            ApplyRunEquipmentModifiers(placement, twoHandedItem, twoHanded);
            return;
        }

        if (leftWeapon != null)
        {
            GroundObject item = CreateRunEquipment($"run-equipped-{slotIndex + 1}", leftWeapon);
            loadout.LeftHand = item;
            ApplyRunEquipmentModifiers(placement, item, leftWeapon);
        }

        if (rightWeapon != null)
        {
            GroundObject item = CreateRunEquipment($"run-equipped-{slotIndex + 1}R", rightWeapon);
            loadout.RightHand = item;
            ApplyRunEquipmentModifiers(placement, item, rightWeapon);
        }
    }

    private static WeaponAttackSpec RequireRunWeapon(string definitionId) =>
        BattleWeaponCatalog.ForDefinition(definitionId)
            ?? throw new ArgumentException($"运行局装备不存在：{definitionId}");

    private static GroundObject CreateRunEquipment(string instanceId, WeaponAttackSpec weapon) =>
        new GroundObject(instanceId, weapon.DefinitionId, GroundObjectKind.Equipment,
            handsRequired: weapon.HandsRequired, attackRange: weapon.AttackRange, moveBonus: weapon.MoveBonus);

    private static void ApplyRunEquipmentModifiers(BattleUnitPlacement placement, GroundObject item, WeaponAttackSpec weapon)
    {
        placement.SetEquipmentMoveModifier(item.InstanceId, weapon.MoveBonus);
        placement.SetEquipmentCombatModifiers(item.InstanceId, weapon.DamageBonus, weapon.DefenseValue);
    }

    /// <summary>
    /// 局外**部位装备**在战斗开场生效（装备系统交互案 §四：属性修正「在下一场战斗开场生效」）：
    /// 防御值 → 防御、伤害修正 → 攻击、移动修正 → 每回合移动额度（与手位装备走同一套 `BattleUnitPlacement` 修正口）。
    /// **不**生成 `GroundObject`：部位装备不进战场物品堆，战斗中也不可换（案 §九 第 3 条）。
    /// 未注册的装备名一律忽略 —— 读档清洗（`RunEquipmentSystem.SanitizeEquipment`）已把关，这里只兜底、不抛错。
    /// 被动 / 状态类效果（玩法 §5.2 末）暂无配表列，未接。
    /// </summary>
    private static void ApplyRunBodyEquipmentModifiers(BattleUnitPlacement placement, RunCharacterSlotSave slot, int slotIndex)
    {
        for (int kind = 0; kind < RunEquipmentSystem.BodySlotKindCount; kind++)
        {
            for (int index = 0; index < RunEquipmentSystem.SlotCountOf(kind); index++)
            {
                string definitionId = RunEquipmentSystem.BodySlotDefinitionOf(slot, kind, index);
                if (string.IsNullOrWhiteSpace(definitionId)
                    || !ItemNameResolver.TryGetArmorDefinition(definitionId, out ArmorDefinition armor)
                    || armor == null)
                {
                    continue;
                }

                // 实例 ID 按「槽 + 部位 + 格序」唯一：同一角色多件部位装备各自记账（卸下 / 换装才会撤掉对应的修正）。
                string instanceId = $"run-armor-{slotIndex + 1}-{kind}{index}";
                placement.SetEquipmentMoveModifier(instanceId, armor.MoveBonus);
                placement.SetEquipmentCombatModifiers(instanceId, armor.DamageBonus, armor.DefenseValue);
            }
        }
    }

    private static int drawCardCount(BattleUnitPlacement placement)
        => placement.Unit is CharacterInstance c ? Math.Max(1, c.drawCardNum) : 5;

    public IReadOnlyList<Card> GetHand(int playerId)
        => hands.TryGetValue(playerId, out var hand) ? hand : Array.Empty<Card>();
    public IReadOnlyList<Card> GetDrawPile(int playerId)
        => drawPiles.TryGetValue(playerId, out var pile) ? pile : Array.Empty<Card>();
    public IReadOnlyList<Card> GetDiscardPile(int playerId)
        => discardPiles.TryGetValue(playerId, out var pile) ? pile : Array.Empty<Card>();

    public int HandCount(int playerId) => hands.TryGetValue(playerId, out var hand) ? hand.Count : 0;
    public int DrawPileCount(int playerId) => drawPiles.TryGetValue(playerId, out var pile) ? pile.Count : 0;
    public int DiscardPileCount(int playerId) => discardPiles.TryGetValue(playerId, out var pile) ? pile.Count : 0;

    public void DrawCards(int playerId, int count)
    {
        if (!drawPiles.ContainsKey(playerId)) return;
        var draw = drawPiles[playerId];
        var hand = hands[playerId];
        for (int i = 0; i < count; i++)
        {
            if (draw.Count == 0) RecycleDiscardIntoDraw(playerId);
            if (draw.Count == 0) return;
            int index = random.Next(draw.Count);
            hand.Add(draw[index]);
            draw.RemoveAt(index);
        }
        Notify();
    }

    private void RecycleDiscardIntoDraw(int playerId)
    {
        var draw = drawPiles[playerId];
        var discard = discardPiles[playerId];
        if (discard.Count == 0) return;
        draw.AddRange(discard);
        discard.Clear();
        for (int i = draw.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (draw[i], draw[j]) = (draw[j], draw[i]);
        }
    }

    public void EndCurrentTurn()
    {
        if (Phase != BattlePhase.Player || HasPendingHandChoice || IsFinished) return;
        Phase = BattlePhase.Monsters;
        foreach (int playerId in PlayerIds)
        {
            var p = Occupancy.Placements[playerId];
            if (p.Presence != BattlefieldPresence.Active || p.Unit is not CharacterInstance character) continue;
            var hand = hands[playerId];
            var discard = discardPiles[playerId];
            for (int i = hand.Count - 1; i >= 0; i--)
            {
                Card card = hand[i];
                card.AppliedKeywords.RemoveAll(x => x.Flags.HasFlag(KeywordFlag.RemoveAtTurnEnd));
                if (card.HasKeyWord(CardKeyWord.Retain)) continue;
                hand.RemoveAt(i); discard.Add(card);
            }
            StateDecayProcessor.ProcessDecayAtTiming(character, DecayTrigger.OnTurnEnd);
        }

        // 格点地形状态的「停留 / 回合末」结算（T8，2026-10-04）：一回合一次，放在怪物行动之前。
        ResolveTerrainTurnEnd();
        monsterTurnQueue.Clear();
        activeMonsterAction = null;
        foreach (var enemy in Occupancy.Placements.Values.Where(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active).OrderBy(x => x.UnitId))
            monsterTurnQueue.Enqueue(enemy.UnitId);
        Message?.Invoke("怪物回合开始。"); Notify();
    }

    /// <summary>
    /// 战后战场操作态的自由移动开关（新案 §四 / §五）：宿主在关闭结算面板后开启。
    /// `Phase` 保持 `Victory`（`IsFinished` 仍为 true → 出牌 / 攻击 / `EndCurrentTurn` / 常规 `TryMove` 继续拒绝），
    /// 只有移动服务的「非玩家阶段」校验被放开；实际移动走 <see cref="BattleMovementService.TryMoveWithoutPlayerCost"/>
    /// （本就不扣能量、不扣回合移动次数）。
    /// </summary>
    public bool IsPostBattleFreeMove { get; private set; }

    /// <summary>进入战后自由移动：只在胜负已定（结算已存在）后成立，未结束的战斗一律拒绝。</summary>
    public bool EnterPostBattleFreeMove()
    {
        if (!IsFinished) return false;
        IsPostBattleFreeMove = true;
        Movement.PlayerTurn = true;
        Notify();
        return true;
    }

    /// <summary>退出战后自由移动（战场即将销毁 / 内容收起时收敛，恢复胜负已定的常规校验）。</summary>
    public void ExitPostBattleFreeMove()
    {
        if (!IsPostBattleFreeMove) return;
        IsPostBattleFreeMove = false;
        Movement.PlayerTurn = false;
        Notify();
    }

    /// <summary>
    /// 战后自由移动（新案 §五）：沿最短合法路径走到目标格，**不消耗能量与移动次数**，可反复调用。
    /// 不可达 / 非法目标一律返回 false 并把原因写入 <paramref name="error"/>（调用方只打控制台日志，不弹提示框）。
    /// 目标格已有单位（含自己人）时不可达 —— 与逐格移动同规则。
    /// </summary>
    public bool TryPostBattleMove(int unitId, AxialHex destination, out string error)
    {
        error = "";
        if (!IsPostBattleFreeMove) { error = "当前不是战后自由移动状态。"; return false; }
        if (!Occupancy.Placements.TryGetValue(unitId, out var mover) || mover.Role != BattlefieldRole.Player ||
            mover.Presence != BattlefieldPresence.Active || mover.Unit.HP <= 0) { error = "当前角色无法移动。"; return false; }
        if (destination == mover.Coord) return true;

        IReadOnlyList<AxialHex> path = Movement.FindPathIgnoringBudget(unitId, destination);
        if (path.Count == 0) { error = "目标不可达。"; return false; }
        // 逐格走：每一步都复用「不扣玩家成本」的现成移动（含可走 / 可进入 / 相邻校验与移动表现）。
        foreach (AxialHex step in path)
        {
            if (!Movement.TryMoveWithoutPlayerCost(unitId, step, out string stepError)) { error = stepError; return false; }
        }
        return true;
    }

    /// <summary>
    /// 导出战后战场快照（交互案 §七 4 改口径：**位置落档**）：单位位置 / 存活 / 生命 + 地面物件 +
    /// 每个角色槽的随身道具与左右手装备。由宿主在进入战后操作态与每次战后操作（移动 / 拾取）后调用并落档；
    /// 读档重进结算界面时用 <see cref="RestorePostBattleState"/> 按它重建同一张战场。
    /// </summary>
    public RunPostBattleSave ExportPostBattleState(string levelId)
    {
        var save = new RunPostBattleSave
        {
            LevelId = levelId ?? string.Empty,
            MapId = Definition?.MapId ?? string.Empty,
            Round = Round,
            SelectedSlotIndex = Math.Max(0, PlayerIds.IndexOf(SelectedId)),
        };

        int orderIndex = 0;
        foreach (BattleUnitPlacement placement in Occupancy.Placements.Values)
        {
            save.Units.Add(new RunUnitPlacementSave
            {
                OrderIndex = orderIndex++,
                UnitId = placement.UnitId,
                Name = placement.Name ?? string.Empty,
                Role = (int)placement.Role,
                InstanceKey = GetMonsterInstanceKey(placement.UnitId) ?? string.Empty,
                SlotIndex = placement.Role == BattlefieldRole.Player ? PlayerIds.IndexOf(placement.UnitId) : -1,
                Q = placement.Coord.Q,
                R = placement.Coord.R,
                Presence = (int)placement.Presence,
                Hp = placement.Unit.HP,
            });
        }

        foreach (BattleCell cell in Board.Cells.Values)
        {
            if (cell.Trigger != null) save.GroundObjects.Add(PostBattleSnapshotCodec.ToSave(cell.Trigger, cell.Coord));
            foreach (GroundObject item in cell.Items) save.GroundObjects.Add(PostBattleSnapshotCodec.ToSave(item, cell.Coord));
        }

        for (int i = 0; i < PlayerIds.Count; i++) save.Loadouts.Add(ExportLoadout(loadouts[PlayerIds[i]]));
        return save;
    }

    private static RunLoadoutSave ExportLoadout(PlayerLoadout loadout) => new RunLoadoutSave
    {
        LeftHand = PostBattleSnapshotCodec.ToSave(loadout?.LeftHand),
        RightHand = PostBattleSnapshotCodec.ToSave(loadout?.RightHand),
        Items = loadout == null ? new List<RunGroundObjectSave>() : loadout.Items.Select(item => PostBattleSnapshotCodec.ToSave(item)).ToList(),
    };

    /// <summary>
    /// 按快照重建战后战场（位置落档）：单位位置 / 在场 / 生命、地面物件、随身与手位，并把阶段置为胜利。
    /// 只在结算态使用（宿主重建「胜利未领奖」的战场）。先把阶段置胜利，重放「已阵亡」单位的死亡记录时
    /// 不会再判一次胜负、也不会再触发结算；快照与重建战场无法匹配（例如此前是事件结算）直接失败，由宿主退回原表现。
    /// </summary>
    public bool RestorePostBattleState(RunPostBattleSave snapshot, out string error)
    {
        error = "";
        if (snapshot == null) { error = "战后快照为空。"; return false; }
        if (snapshot.Loadouts == null || snapshot.Loadouts.Count != PlayerIds.Count)
        { error = "战后快照的携带状态与本场角色数量不一致。"; return false; }
        if (!string.IsNullOrEmpty(snapshot.MapId) && !string.Equals(snapshot.MapId, Definition?.MapId, StringComparison.Ordinal))
        { error = $"战后快照的地图 {snapshot.MapId} 与本场 {Definition?.MapId} 不一致。"; return false; }

        if (!PostBattleSnapshotCodec.ResolveUnitLayout(snapshot, PlayerIds, GetMonsterInstanceKey, Occupancy.Placements,
            out Dictionary<int, (AxialHex Coord, BattlefieldPresence Presence)> layout, out Dictionary<int, int> hp, out string layoutError))
        { error = layoutError; return false; }

        // 先置胜利：还原过程中重放「已阵亡」单位不会再次判胜负、也不再触发 Finished / 结算。
        Phase = BattlePhase.Victory;
        activeMonsterAction = null;
        monsterTurnQueue.Clear();

        Occupancy.RestoreLayout(layout);
        foreach (var pair in hp)
        {
            if (!Occupancy.Placements.TryGetValue(pair.Key, out BattleUnitPlacement placement)) continue;
            if (pair.Value <= 0 && placement.Presence != BattlefieldPresence.Active) continue; // 已退场：不重放死亡
            int hpValue = placement.Role == BattlefieldRole.Player ? Math.Max(1, pair.Value) : Math.Max(0, pair.Value);
            if (placement.Unit.HP != hpValue) placement.Unit.HP = hpValue;
        }

        RestoreLoadouts(snapshot.Loadouts);
        PostBattleSnapshotCodec.RestoreGroundObjects(Board, snapshot.GroundObjects, out string groundWarning);
        if (groundWarning.Length > 0) Message?.Invoke("战后战场还原警告：" + groundWarning);

        Round = Math.Max(1, snapshot.Round);
        SelectedId = PlayerIds[Math.Clamp(snapshot.SelectedSlotIndex, 0, PlayerIds.Count - 1)];
        Notify();
        return true;
    }

    private void RestoreLoadouts(IReadOnlyList<RunLoadoutSave> saves)
    {
        for (int i = 0; i < PlayerIds.Count; i++)
        {
            int playerId = PlayerIds[i];
            BattleUnitPlacement placement = Occupancy.Placements[playerId];
            PlayerLoadout loadout = loadouts[playerId];
            // 先摘掉旧物件带来的额度 / 攻防加成，再按快照重建（与战斗内换装同一套加成口径）。
            foreach (GroundObject old in new[] { loadout.LeftHand, loadout.RightHand }.Concat(loadout.Items).Where(x => x != null))
            {
                placement.SetEquipmentMoveModifier(old.InstanceId, 0);
                placement.SetEquipmentCombatModifiers(old.InstanceId, 0, 0);
            }

            RunLoadoutSave save = saves[i] ?? new RunLoadoutSave();
            loadout.LeftHand = PostBattleSnapshotCodec.ToGroundObject(save.LeftHand);
            loadout.RightHand = PostBattleSnapshotCodec.ToGroundObject(save.RightHand);
            for (int slot = 0; slot < loadout.Items.Length; slot++)
                loadout.Items[slot] = save.Items != null && slot < save.Items.Count
                    ? PostBattleSnapshotCodec.ToGroundObject(save.Items[slot]) : null;

            foreach (GroundObject equipment in new[] { loadout.LeftHand, loadout.RightHand }.Where(x => x != null))
            {
                WeaponAttackSpec spec = BattleWeaponCatalog.ForDefinition(equipment.DefinitionId);
                placement.SetEquipmentMoveModifier(equipment.InstanceId, spec?.MoveBonus ?? equipment.MoveBonus);
                placement.SetEquipmentCombatModifiers(equipment.InstanceId, spec?.DamageBonus ?? 0, spec?.DefenseValue ?? 0);
            }

            selectedHands[playerId] = HandSlot.Left;
        }
    }

    public bool ExecuteNextMonsterTurnStep()
    {
        while (true)
        {
            if (IsFinished) return false;
            if (activeMonsterAction == null)
            {
                if (!TryBeginNextMonsterAction())
                {
                    if (!IsFinished) { PrepareEnemyIntentions(); StartNextPlayerRound(); }
                    return false;
                }
            }

            MonsterActionState action = activeMonsterAction;
            if (action.Enemy.Presence != BattlefieldPresence.Active || action.Target.Presence != BattlefieldPresence.Active)
            {
                FinishActiveMonsterAction();
                continue;
            }

            // 逃跑行为：不索敌、不攻击，朝最近边界逐步移动；抵达边界格即离场。
            if (action.FleeMode)
            {
                if (action.MoveSteps < Math.Max(0, action.Spec.MoveBudget) && action.MoveSteps < action.PlannedPath.Count)
                {
                    AxialHex step = action.PlannedPath[action.MoveSteps];
                    action.MoveSteps++;
                    if (Movement.TryMoveWithoutPlayerCost(action.Enemy.UnitId, step, out _))
                    {
                        if (IsOnMapBoundary(action.Enemy.Coord))
                        {
                            DepartEnemy(action.Enemy);
                            FinishActiveMonsterAction();
                            return !IsFinished;
                        }
                        // 一步一帧推进，交给表现层播放移动。
                        return true;
                    }
                }
                FinishActiveMonsterAction();
                continue;
            }

            int plannedHits = action.Intention.Count(effect => effect != null && effect.Length > 0 && (EffectType)effect[0] == EffectType.Damage);
            int moveLimit = action.Spec.ActionBudget > 0 ? Math.Min(action.Spec.MoveBudget, Math.Max(0, action.Spec.ActionBudget - plannedHits)) : action.Spec.MoveBudget;
            if (action.NeedsTargetInRange && action.MoveSteps < moveLimit && !CanEnemyHit(action.Enemy, action.Target, action.Spec))
            {
                AxialHex? step = action.MoveSteps < action.PlannedPath.Count ? action.PlannedPath[action.MoveSteps] : BestStepToward(action.Enemy, action.Target);
                action.MoveSteps++;
                if (step.HasValue && Movement.TryMoveWithoutPlayerCost(action.Enemy.UnitId, step.Value, out _)) return true;
            }

            if (action.NeedsTargetInRange && !CanEnemyHit(action.Enemy, action.Target, action.Spec))
            {
                Message?.Invoke($"{action.Enemy.Name} 无法进入攻击范围，本次行动结束。 ");
                FinishActiveMonsterAction();
                continue;
            }

            if (action.EffectIndex < action.Intention.Length)
            {
                int[] effect = action.Intention[action.EffectIndex++];
                ExecuteEnemyIntentionEffect(action.Enemy, action.Target, action.Spec, action.LandingCell, action.AttackDirection, effect);
                Occupancy.SyncDeaths(); EvaluateOutcome();
                return !IsFinished;
            }

            FinishActiveMonsterAction();
        }
    }

    private bool TryBeginNextMonsterAction()
    {
        while (monsterTurnQueue.Count > 0)
        {
            int nextId = monsterTurnQueue.Dequeue();
            if (!Occupancy.Placements.TryGetValue(nextId, out var enemy) || enemy.Presence != BattlefieldPresence.Active) continue;
            StateSystem.OnTurnStart(enemy.Unit);
            StateDecayProcessor.ProcessDecayAtTiming(enemy.Unit, DecayTrigger.OnTurnStart);
            // 燃烧回合结算（T7，2026-10-04）：与角色回合同一口径；烧死则跳过本次行动（胜负在下次评估收敛）。
            if (StateSystem.ProcessIgniteTick(enemy.Unit) > 0)
            {
                Message?.Invoke($"{enemy.Name} 的燃烧结算完成。");
                Occupancy.SyncDeaths(); EvaluateOutcome();
                if (enemy.Presence != BattlefieldPresence.Active || enemy.Unit.HP <= 0) continue;
            }

            MonsterInstance monster = enemy.Unit as MonsterInstance;
            EnemyIntentSpec spec = BattleEnemyIntentCatalog.Resolve(monster);
            // 逃跑行为不索敌：直接按"朝最近边界"规划，不走攻击管线。
            if (spec.Behavior == EnemyIntentBehavior.Flee)
            {
                if (BeginFleeAction(enemy, spec, monster)) return true;
                continue;
            }
            EnemyIntentPlan plan = EnemyIntentPlanner.Plan(Board, Occupancy, enemy, PlayerIds.Select(id => Occupancy.Placements[id]),
                Occupancy.Placements.Values.FirstOrDefault(x => x.Role == BattlefieldRole.Protected && x.Presence == BattlefieldPresence.Active), spec, random);
            if (plan?.Target == null)
            {
                // 索敌失败不能等同于失败：可能只是占位刚变化或该意图无合法目标。
                // 只有所有玩家的真实 HP 都归零时才允许进入失败。
                if (PlayerIds.All(id => Occupancy.Placements[id].Unit.HP <= 0))
                {
                    SetOutcome(BattlePhase.Defeat, BuildPlayerFailureReason("索敌时未找到存活玩家"));
                    return false;
                }
                Message?.Invoke($"{enemy.Name} 本次没有合法索敌目标，跳过行动。 ");
                continue;
            }
            int[][] intention = monster?.SelectedIntention;
            if (intention == null || intention.Length == 0) intention = new[] { new[] { (int)EffectType.Damage, 1, 0 } };
            activeMonsterAction = new MonsterActionState
            {
                Enemy = enemy, Target = plan.Target, Spec = spec, Intention = intention, LandingCell = plan.LandingCell ?? plan.Target.Coord,
                PlannedPath = plan.Path ?? Array.Empty<AxialHex>(),
                AttackDirection = plan.AttackDirection,
                NeedsTargetInRange = intention.Any(effect => effect != null && effect.Length > 0 && (EffectType)effect[0] == EffectType.Damage),
            };
            Message?.Invoke($"{enemy.Name} 开始行动。"); Notify();
            return true;
        }
        return false;
    }

    private void FinishActiveMonsterAction()
    {
        if (activeMonsterAction == null) return;
        StateDecayProcessor.ProcessDecayAtTiming(activeMonsterAction.Enemy.Unit, DecayTrigger.OnTurnEnd);
        activeMonsterAction = null;
        Occupancy.SyncDeaths(); EvaluateOutcome(); Notify();
    }

    /// <summary>逃跑行动的准备：不索敌、不攻击；已站在边界格则直接离场，否则按预算规划逃向最近边界。</summary>
    private bool BeginFleeAction(BattleUnitPlacement enemy, EnemyIntentSpec spec, MonsterInstance monster)
    {
        if (IsOnMapBoundary(enemy.Coord))
        {
            Message?.Invoke($"{enemy.Name} 已在战场边缘，直接逃离。");
            DepartEnemy(enemy);
            return false;
        }

        IReadOnlyList<AxialHex> path = EnemyIntentPlanner.PlanFleePath(Board, Occupancy, enemy, MapBoundaryRadius,
            Math.Max(0, spec.MoveBudget), out _);
        int[][] intention = monster?.SelectedIntention;
        activeMonsterAction = new MonsterActionState
        {
            Enemy = enemy, Target = enemy, Spec = spec,
            Intention = intention ?? Array.Empty<int[]>(),
            LandingCell = enemy.Coord, PlannedPath = path,
            NeedsTargetInRange = false, FleeMode = true,
        };
        Message?.Invoke(path.Count == 0 ? $"{enemy.Name} 想逃跑但无路可走。" : $"{enemy.Name} 开始逃跑。");
        Notify();
        return true;
    }

    /// <summary>离场：走 `Departed`（不是 `Defeated`），因此结算不会把逃跑实例算作被击杀。</summary>
    private void DepartEnemy(BattleUnitPlacement enemy)
    {
        Occupancy.RemoveFromBoard(enemy.UnitId, BattlefieldPresence.Departed);
        Message?.Invoke($"{enemy.Name} 逃出了战场。");
        Occupancy.SyncDeaths(); EvaluateOutcome(); Notify();
    }

    /// <summary>地图最外圈格点的判定半径（正式地图按 `Radius`）。</summary>
    private int MapBoundaryRadius => Math.Max(1, Definition.Radius);
    private bool IsOnMapBoundary(AxialHex coord) => EnemyIntentPlanner.IsOnMapBoundary(coord, MapBoundaryRadius);

    private void PrepareEnemyIntentions()
    {
        enemyIntentBatch++;
        foreach (var enemy in Occupancy.Placements.Values.Where(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active))
        {
            if (enemy.Unit is not MonsterInstance monster || monster.Table == null || monster.Table.Length == 0) continue;
            // 候选池只含非空意图列（空列既不选也不展示），避免抽中"什么都不做"的空列。
            int[] candidates = Enumerable.Range(0, monster.Table.Length)
                .Where(index => monster.Table[index] != null && monster.Table[index].Length > 0).ToArray();
            if (candidates.Length == 0) continue;

            int index = -1;
            if (battleRules.Count > 0)
            {
                var context = new IntentSelectionContext
                {
                    Round = Round,
                    IntentBatch = enemyIntentBatch,
                    EnemyId = enemy.UnitId,
                    Monster = monster,
                    Candidates = candidates,
                    HitPlayerCount = monsterHitPlayerCounts.TryGetValue(enemy.UnitId, out int hits) ? hits : 0,
                    Random = random,
                };
                foreach (IBattleRule rule in battleRules)
                {
                    if (!rule.TrySelectIntention(context)) continue;
                    index = candidates.Contains(context.SelectedIndex) ? context.SelectedIndex : -1;
                    break;
                }
            }

            if (index < 0) index = candidates[random.Next(candidates.Length)];
            monster.SetSelectedIntention(index, monster.Table[index]);
        }
        Notify();
    }

    public string GetEnemyIntentionText(int unitId)
    {
        if (!Occupancy.Placements.TryGetValue(unitId, out var placement) || placement.Unit is not MonsterInstance monster || monster.SelectedIntention == null)
            return "无意图";
        EnemyIntentSpec spec = BattleEnemyIntentCatalog.Resolve(monster);
        if (spec.Behavior != EnemyIntentBehavior.Attack) return "逃跑";
        int hitCount = monster.SelectedIntention.Count(effect => effect != null && effect.Length > 0 && (EffectType)effect[0] == EffectType.Damage);
        // 伤害修正值取参与执行侧同源（EnemyIntentDamageArgs）：两元素写法 `1;<修正值>` 必须计入，否则预览与实伤不一致。
        int bonus = monster.SelectedIntention.Where(effect => effect != null).Select(EnemyIntentDamageArgs.GetDamageModifier).DefaultIfEmpty(0).Max();
        if (spec.PreviewCertainty == EnemyIntentPreviewCertainty.UnknownNumbers) return "移动 / 攻击";
        int hits = Math.Max(1, hitCount);
        int damage = placement.Unit.Attack + bonus;
        return hits == 1 ? damage.ToString() : $"{damage}×{hits}";
    }

    public EnemyIntentDisplay GetEnemyIntentDisplay(int unitId)
    {
        if (!Occupancy.Placements.TryGetValue(unitId, out var placement) || placement.Unit is not MonsterInstance monster || monster.SelectedIntention == null)
            return new EnemyIntentDisplay(EnemyIntentPreviewCertainty.UnknownNumbers, 0, 0, 0, "无可用意图。");
        EnemyIntentSpec spec = BattleEnemyIntentCatalog.Resolve(monster);
        if (spec.Behavior != EnemyIntentBehavior.Attack)
            return new EnemyIntentDisplay(spec.PreviewCertainty, 0, 0, spec.ActionBudget, "逃跑：不攻击，朝最近的战场边缘移动，抵达边界即离场。");
        int hits = monster.SelectedIntention.Count(effect => effect != null && effect.Length > 0 && (EffectType)effect[0] == EffectType.Damage);
        int bonus = monster.SelectedIntention.Where(effect => effect != null).Select(EnemyIntentDamageArgs.GetDamageModifier).DefaultIfEmpty(0).Max();
        int damage = placement.Unit.Attack + bonus;
        string detail = spec.PreviewCertainty switch
        {
            EnemyIntentPreviewCertainty.UnknownNumbers => $"行动总额 {spec.ActionBudget}：移动与攻击次数将在敌方回合按索敌结果分配。",
            EnemyIntentPreviewCertainty.KnownDamageUnknownRange => $"伤害 {damage}，攻击 {Math.Max(1, hits)} 次；{spec.AttackMode}，最多移动 {spec.MoveBudget} 格后索敌，范围将在敌方回合确定。",
            _ => $"伤害 {damage}，攻击 {Math.Max(1, hits)} 次；固定 {spec.AttackMode}，距离 {spec.AttackRange}，悬停时显示范围。",
        };
        return new EnemyIntentDisplay(spec.PreviewCertainty, damage, Math.Max(1, hits), spec.ActionBudget, detail);
    }

    public IReadOnlyCollection<AxialHex> GetKnownEnemyIntentPreviewCells(int unitId)
    {
        if (!Occupancy.Placements.TryGetValue(unitId, out var p) || p.Unit is not MonsterInstance monster) return Array.Empty<AxialHex>();
        EnemyIntentSpec spec = BattleEnemyIntentCatalog.Resolve(monster);
        if (spec.PreviewCertainty != EnemyIntentPreviewCertainty.KnownDamageKnownRange) return Array.Empty<AxialHex>();
        AxialHex dir = spec.PreviewDirection ?? BattleRangeResolver.SixNeighborOffsets[0];
        AxialHex landing = new(p.Coord.Q + dir.Q * spec.AttackRange, p.Coord.R + dir.R * spec.AttackRange);
        if (spec.AttackMode == WeaponAttackMode.ThrowSingle && spec.AreaRadius > 0)
            return BattleRangeResolver.CellsWithinRange(landing, spec.AreaRadius).Where(Board.Cells.ContainsKey).ToArray();
        return BattleAttackTraceResolver.Resolve(Board, Occupancy, p.Coord, landing, new WeaponAttackSpec("preview", spec.AttackRange, 0, spec.AttackMode, 0));
    }

    private string GetLegacyEnemyIntentionText(int unitId)
    {
        if (!Occupancy.Placements.TryGetValue(unitId, out var placement) || placement.Unit is not MonsterInstance monster || monster.SelectedIntention == null)
            return "无意图";
        var parts = new List<string>();
        foreach (int[] effect in monster.SelectedIntention)
        {
            if (effect == null || effect.Length == 0) continue;
            EffectType type = (EffectType)effect[0];
            parts.Add(type switch
            {
                EffectType.Damage => $"攻击 +{EnemyIntentDamageArgs.GetDamageModifier(effect)}",
                EffectType.Shield => $"防御 +{(effect.Length > 1 ? effect[1] : 0)}",
                EffectType.AddState => "施加状态",
                _ => type.ToString(),
            });
        }
        return parts.Count == 0 ? "无意图" : string.Join(" / ", parts);
    }

    private void ExecuteEnemyIntentionEffect(BattleUnitPlacement enemy, BattleUnitPlacement target, EnemyIntentSpec spec, AxialHex landing, AxialHex? attackDirection, int[] effect)
    {
        if (effect == null || effect.Length == 0 || target.Presence != BattlefieldPresence.Active) return;
        EffectType type = (EffectType)effect[0];
        if (type == EffectType.Damage)
        {
            // 伤害段取参：`1;<修正值>`（两元素）与 `1;<模式>;<修正值>`（三元素起）都按 EnemyIntentDamageArgs 解析。
            // 此前统一 Skip(2) 会把两元素写法的修正值整段丢掉（P2-10.4）。
            int[] args = EnemyIntentDamageArgs.ResolveDamageParams(effect);
            IReadOnlyList<BattleUnitPlacement> victims = ResolveEnemyAffectedTargets(enemy, target, spec, landing, attackDirection,
                out IReadOnlyCollection<AxialHex> affectedCells);
            if (victims.Count == 0 && spec.AttackMode == WeaponAttackMode.RangedLine && attackDirection.HasValue)
            {
                var trace = BattleAttackSystem.ResolveFromDirection(Board, Occupancy, enemy.Coord, attackDirection.Value,
                    new WeaponAttackSpec("intent", spec.AttackRange, 0, WeaponAttackMode.RangedLine, 0), enemy.UnitId);
                AxialHex endpoint = trace.Count > 0 ? trace[^1] : enemy.Coord;
                AttackResolved?.Invoke(new BattlefieldAttackEvent(++attackEventSequence, enemy.UnitId, null,
                    enemy.Coord, endpoint, WeaponAttackMode.RangedLine, 0, 0, false, AffectedCells: affectedCells,
                    Direction: attackDirection));
                Message?.Invoke($"{enemy.Name} 沿选定方向射击，未命中单位。 ");
            }
            foreach (BattleUnitPlacement victim in victims)
            {
                int before = victim.Unit.HP;
                using (new BattlefieldEffectTargetScope(enemy.Unit, victim.Unit, new[] { victim.Unit }, Array.Empty<IUnitInstance>(), playerTurn: false, canAttack: CanDefaultAttack))
                {
                    EffectResult result = EffectSystem.ApplyAttack(enemy.Unit, victim.Unit, args);
                    AttackResolved?.Invoke(new BattlefieldAttackEvent(++attackEventSequence, enemy.UnitId, victim.UnitId,
                        enemy.Coord, spec.AttackMode == WeaponAttackMode.ThrowSingle ? landing : victim.Coord,
                        spec.AttackMode, Math.Max(0, result.TargetHpBefore - result.TargetHpAfter),
                        Math.Max(0, result.TargetShieldBefore - result.TargetShieldAfter), result.TargetHpAfter <= 0,
                        result.TargetHpBefore, result.TargetHpAfter, result.TargetShieldBefore, result.TargetShieldAfter,
                        affectedCells, attackDirection ?? BattleRangeResolver.PickLineDirection(enemy.Coord, landing)));
                    if (spec.AttackMode == WeaponAttackMode.Thrust) TryApplyThrustKnockback(enemy, victim, victim.Coord);
                }
                TrackHpLoss(victim.Unit, before);
            }
            // 每次攻击只触发一次窃取类机制：命中任意玩家即通知一次（伤害已结算）。
            BattleUnitPlacement hitPlayer = victims.FirstOrDefault(v => v.Role == BattlefieldRole.Player);
            if (hitPlayer != null)
            {
                // 命中计数（含被护盾格挡）供战斗规则读取；本局内按怪物单位累计。
                monsterHitPlayerCounts[enemy.UnitId] = monsterHitPlayerCounts.TryGetValue(enemy.UnitId, out int hits) ? hits + 1 : 1;
                MonsterHitPlayer?.Invoke(enemy, hitPlayer);
            }
        }
        else if (type == EffectType.Shield)
        {
            if (spec.TargetPolicy == EnemyTargetPolicy.AllyRange)
            {
                foreach (BattleUnitPlacement ally in Occupancy.Placements.Values.Where(p => p.Role == BattlefieldRole.Enemy &&
                    p.Presence == BattlefieldPresence.Active && BattleRangeResolver.Distance(enemy.Coord, p.Coord) <= spec.AreaRadius))
                    EffectSystem.ApplyShield(ally.Unit, effect.Skip(1).ToArray());
            }
            else EffectSystem.ApplyShield(enemy.Unit, effect.Skip(1).ToArray());
        }
        else if (type == EffectType.AddState && effect.Length > 2 && Enum.IsDefined(typeof(StateType), effect[2]))
        {
            int stacks = effect.Length > 3 ? effect[3] : 1;
            StateSystem.AddOrUpdateState(target.Unit, (StateType)effect[2], stacks, ownerUnit: enemy.Unit);
        }
    }

    private IReadOnlyList<BattleUnitPlacement> ResolveEnemyAffectedTargets(BattleUnitPlacement enemy, BattleUnitPlacement target,
        EnemyIntentSpec spec, AxialHex landing, AxialHex? attackDirection, out IReadOnlyCollection<AxialHex> affectedCells)
    {
        IEnumerable<AxialHex> cells = spec.AttackMode == WeaponAttackMode.ThrowSingle && spec.AreaRadius > 0
            ? BattleRangeResolver.CellsWithinRange(landing, spec.AreaRadius)
            : spec.AttackMode == WeaponAttackMode.RangedLine && attackDirection.HasValue
                ? BattleAttackSystem.ResolveFromDirection(Board, Occupancy, enemy.Coord, attackDirection.Value,
                    new WeaponAttackSpec("intent", spec.AttackRange, 0, WeaponAttackMode.RangedLine, 0), enemy.UnitId)
            : BattleAttackTraceResolver.Resolve(Board, Occupancy, enemy.Coord, landing, new WeaponAttackSpec("intent", spec.AttackRange, 0, spec.AttackMode, 0));
        affectedCells = cells.ToArray();
        BattleUnitPlacement[] hits = Occupancy.Placements.Values.Where(p => p.UnitId != enemy.UnitId &&
            p.Presence == BattlefieldPresence.Active && cells.Contains(p.Coord)).ToArray();
        // Directional projectiles never fall back to their planning target: only the actual trace may hit.
        if (spec.AttackMode == WeaponAttackMode.RangedLine) return hits;
        return hits.Length > 0 ? hits : new[] { target };
    }

    private WeaponAttackMode intentSpecFor(BattleUnitPlacement enemy) =>
        enemy.Unit is MonsterInstance monster ? BattleEnemyIntentCatalog.Resolve(monster).AttackMode : WeaponAttackMode.AdjacentSingle;

    private void StartNextPlayerRound()
    {
        Round++; cardsPlayedThisTurn.Clear();
        // 回合计点（地图玩法 §5.1）：一个战斗回合 = 0.1 时间点；订阅方（RunBattleScene）落档，本类不认识存档。
        PlayerRoundStarted?.Invoke(Round);
        foreach (int playerId in PlayerIds)
        {
            var p = Occupancy.Placements[playerId];
            if (p.Presence != BattlefieldPresence.Active || p.Unit is not CharacterInstance character) continue;
            if (character.Shield > 0 && !StateSystem.TryGetStateStacks(character, StateType.ShieldCapEqualsHP, out _)) character.Shield = 0;
            StateSystem.OnTurnStart(character);
            StateDecayProcessor.ProcessDecayAtTiming(character, DecayTrigger.OnTurnStart);
            // 燃烧回合结算（T7，2026-10-04）：挂在既有「OnTurnStart → ProcessDecay」之后，不改动既有顺序。
            if (StateSystem.ProcessIgniteTick(character) > 0)
            {
                Message?.Invoke($"{p.Name} 的燃烧结算完成。");
            }

            character.Energy = character.Max_costs;
            DrawCards(playerId, drawCardCount(p));
        }
        Movement.StartPlayerTurn(); Phase = BattlePhase.Player; Notify();
        Message?.Invoke($"玩家回合 {Round}：状态已结算，能量、移动次数与手牌已刷新。");
    }

    private BattleUnitPlacement NearestAlivePlayer(AxialHex from) => PlayerIds.Select(x => Occupancy.Placements[x])
        .Where(x => x.Presence == BattlefieldPresence.Active && x.Unit.HP > 0)
        .OrderBy(x => AxialHex.Distance(from, x.Coord)).ThenBy(x => x.UnitId).FirstOrDefault();

    private bool CanEnemyHit(BattleUnitPlacement enemy, BattleUnitPlacement target, EnemyIntentSpec spec) =>
        BattleAttackTraceResolver.Resolve(Board, Occupancy, enemy.Coord, target.Coord,
            new WeaponAttackSpec("enemy", spec.AttackRange, 0, spec.AttackMode, 0)).Contains(target.Coord);

    private void TryApplyThrustKnockback(BattleUnitPlacement source, BattleUnitPlacement victim, AxialHex victimCoord)
    {
        if (victim.Presence != BattlefieldPresence.Active || victim.Unit.HP <= 0 ||
            !BattleAttackSystem.TrySelectDirection(source.Coord, victimCoord, out AxialHex direction)) return;
        AxialHex destination = new(victimCoord.Q + direction.Q, victimCoord.R + direction.R);
        Movement.TryMoveWithoutPlayerCost(victim.UnitId, destination, out _);
    }

    private AxialHex? BestStepToward(BattleUnitPlacement enemy, BattleUnitPlacement target)
    {
        var queue = new Queue<AxialHex>();
        var firstStep = new Dictionary<AxialHex, AxialHex>();
        foreach (var next in BattleHexLayout.Neighbors(enemy.Coord).Where(Occupancy.CanEnter).OrderBy(x => x.Q).ThenBy(x => x.R))
        { queue.Enqueue(next); firstStep[next] = next; }
        while (queue.Count > 0)
        {
            AxialHex current = queue.Dequeue();
            if (AxialHex.Distance(current, target.Coord) == 1) return firstStep[current];
            foreach (var next in BattleHexLayout.Neighbors(current).Where(Occupancy.CanEnter).OrderBy(x => x.Q).ThenBy(x => x.R))
            {
                if (firstStep.ContainsKey(next)) continue;
                firstStep[next] = firstStep[current]; queue.Enqueue(next);
            }
        }
        return null;
    }

    // ── 空间出牌路由：空间层筛目标，既有 Card/Effect/State 管线结算 ──

    public IReadOnlyList<BattleUnitPlacement> GetFriendlyAffectedTargets(int cardId, AxialHex target)
    {
        CardSpatialSpec spec = GetSpatialSpec(cardId);
        if (spec.Shape is not (CardSpatialShape.Single or CardSpatialShape.Burst or CardSpatialShape.Line or CardSpatialShape.Fan)) return Array.Empty<BattleUnitPlacement>();
        IReadOnlyCollection<AxialHex> affected = spec.Shape == CardSpatialShape.Line || UsesWeaponGeometry(spec) && WeaponIsRay
            ? GetAffectedCells(cardId, target)
            : BattleRangeResolver.ResolveAffectedCells(Selected.Coord, target, EffectiveSpec(spec)).Where(Board.Cells.ContainsKey).ToArray();
        return affected.Select(Occupancy.At).Where(x => x != null && x.Presence == BattlefieldPresence.Active && x.Role == Selected.Role).ToArray();
    }

    public bool CardCanCauseNegativeEffect(int cardId)
    {
        Card card = GetHand(SelectedId).FirstOrDefault(x => x?.CardId == cardId);
        if (card == null) return false;
        for (int i = 0; i < card.EffectTypes.Length; i++)
        {
            EffectType type = card.EffectTypes[i];
            int[] args = card.Params != null && i < card.Params.Length ? card.Params[i] : Array.Empty<int>();
            if (type is EffectType.Damage or EffectType.DamageByBattleLostHp or EffectType.ShieldSlam or EffectType.HpLoss or EffectType.ClearState or EffectType.ClearAllStates) return true;
            if (type == EffectType.AddCost && args.Length > 1 && args[1] < 0) return true;
            if (type == EffectType.AddState && args.Length > 1 && Enum.IsDefined(typeof(StateType), args[1]) && StateSystem.IsDebuff((StateType)args[1])) return true;
        }
        return false;
    }

    /// <summary>陷阱 / 场地卡是否有「落物即结算」的效果（EffectType 里存在非 None 的项）。</summary>
    private bool CardResolvesOnLanding(int cardId) => CardResolvesOnLanding(ResolveCardTemplate(cardId));

    private static bool CardResolvesOnLanding(Card card) =>
        card != null && card.EffectTypes != null && card.EffectTypes.Any(x => x != EffectType.None);

    /// <summary>按 CardId 取卡模板：先手牌实例（带上限升级等运行时状态），再回落全局卡表。</summary>
    private Card ResolveCardTemplate(int cardId) =>
        hands.Values.SelectMany(x => x).FirstOrDefault(x => x != null && x.CardId == cardId)
        ?? LoadingSystem.CardDictionary.GetValueOrDefault(cardId);

    public bool TryCastCard(int cardId, AxialHex target, out string error)
    {
        error = string.Empty;
        var p = Selected;
        if (Phase != BattlePhase.Player || IsFinished) { error = "当前不是玩家出牌阶段。"; return false; }
        if (HasPendingHandChoice) { error = "请先完成当前选牌效果。"; return false; }
        if (p.Presence != BattlefieldPresence.Active || p.Unit.HP <= 0) { error = "当前角色无法出牌。"; return false; }

        Card handCard = null;
        foreach (Card c in GetHand(p.UnitId)) if (c != null && c.CardId == cardId) { handCard = c; break; }
        if (handCard == null) { error = "手牌中没有这张卡。"; return false; }
        if (CurrentWeapon.BlocksDefenseShield && handCard.EffectTypes.Contains(EffectType.Shield))
        {
            error = $"装备 {CurrentWeapon.DefinitionId} 时无法通过防御牌获得护盾。";
            return false;
        }

        var spec = GetSpatialSpec(cardId);

        int actualCost = handCard.GetCurrentEnergyCost(p.Unit);
        if (handCard.CardId == 11002010) actualCost = Math.Max(0, handCard.EnergyCost - hpLossEvents.GetValueOrDefault(p.UnitId));
        if (StateSystem.TryGetStateStacks(p.Unit, StateType.NextBattleCardFree, out _) && handCard.Category != CardCategory.State) actualCost = 0;
        if (p.Unit.Energy < actualCost) { error = $"能量不足，需要 {actualCost} 点能量。"; return false; }
        if (handCard.Category != CardCategory.State && StateSystem.TryGetStateStacks(p.Unit, StateType.BattleCardBlocked, out _))
        { error = "当前状态禁止打出战斗牌。"; return false; }
        if (handCard.ConditionParams.Contains(CardConditionType.NoBattleCardPlayedThisTurn) && cardsPlayedThisTurn.GetValueOrDefault(p.UnitId) > 0)
        { error = "本回合已经打出过战斗牌。"; return false; }

        switch (spec.Shape)
        {
            case CardSpatialShape.SelfMove:
                if (actualCost != 1) { error = "空间移动牌当前要求费用为 1，以免重复扣费。"; return false; }
                if (!Movement.TryMove(p.UnitId, target, out error)) return false;
                CompleteCardLifecycle(p, handCard);
                Notify();
                return true;

            case CardSpatialShape.Trap:
            {
                if (BattleRangeResolver.Distance(p.Coord, target) > spec.MaxRange || !Board.Cells.ContainsKey(target)) { error = "超出陷阱射程或目标不存在。"; return false; }
                if (!Board.IsWalkable(target)) { error = "陷阱只能放在可通行的格子上。"; return false; }
                bool resolvesOnLanding = CardResolvesOnLanding(handCard);
                BattleUnitPlacement standing = Occupancy.At(target);
                if (Board.Cells[target].Items.Count > 0) { error = "陷阱目标必须没有单位或物品。"; return false; }
                if (standing != null && !resolvesOnLanding) { error = "陷阱只能放在空格上（这张牌没有落物结算效果）。"; return false; }
                if (standing != null && standing.Role == p.Role) { error = "不能对友方单位施放场地效果。"; return false; }
                var trap = new GroundObject($"trap-{p.UnitId}-{Guid.NewGuid():N}".Substring(0, 20),
                    string.IsNullOrEmpty(spec.TrapId) ? "test_trap" : spec.TrapId, GroundObjectKind.Trap, EntryTriggerMode.EveryEntry);
                if (resolvesOnLanding)
                {
                    // 落物即结算：受影响格 = 目标格，结算目标 = 该格上的敌方单位。
                    // 走既有出牌管线（费用 / 卡牌去向 / 打出记账 / 生命账本 / 胜负判定 / 消息都与其他卡同源），失败时不落物。
                    var landingTargets = standing != null && standing.Presence == BattlefieldPresence.Active
                        ? new[] { standing } : Array.Empty<BattleUnitPlacement>();
                    if (!ApplyCardThroughExistingPipeline(p, handCard, standing, landingTargets, actualCost, out error)) return false;
                    if (!Board.TryAddObject(target, trap, out string placeError))
                    {
                        error = string.IsNullOrEmpty(placeError) ? "陷阱落物失败。" : "陷阱落物失败：" + placeError;
                        return false;
                    }

                    // 持续地形（T8，2026-10-04）：TriggerTiming 含 OnTurnEnd 的定义同时写成格点地形状态。
                    WriteLandingTerrainState(trap, target, p);
                    Message?.Invoke($"在 ({target.Q},{target.R}) 布下 {trap.DefinitionId}" +
                        (landingTargets.Length > 0 ? $"（{standing.Name} 触发落物结算）。" : "。"));
                    return true;
                }

                // 纯落物卡（埋设陷阱 / 古树哨卫）：没有可结算效果 → 沿用原有「落物 + 扣费 + 卡牌去向 + 提示」路径（进入触发内容待 P2-30 后半）。
                if (!Board.TryAddObject(target, trap, out error)) { if (error.Length == 0) error = "陷阱目标必须没有单位或物品。"; return false; }
                WriteLandingTerrainState(trap, target, p);
                p.Unit.Energy -= actualCost;
                CompleteCardLifecycle(p, handCard);
                Notify();
                Message?.Invoke($"在 ({target.Q},{target.R}) 埋设了陷阱：{trap.DefinitionId}（进入触发按 AreaObject.csv 结算）。");
                return true;
            }

            case CardSpatialShape.Single:
            case CardSpatialShape.Burst:
            case CardSpatialShape.Line:
            case CardSpatialShape.Fan:
            {
                if (!GetCastCandidates(cardId).Contains(target)) { error = "超出卡牌射程或被单位/障碍阻挡。"; return false; }
                bool rayAimed = UsesWeaponGeometry(spec) && WeaponIsRay;
                bool thrustApplies = spec.AttackMode == WeaponAttackMode.Thrust && CardModeApplies(spec);
                var affected = spec.Shape == CardSpatialShape.Line || rayAimed
                    ? GetAffectedCells(cardId, target)
                    : BattleRangeResolver.ResolveAffectedCells(p.Coord, target, spec).Where(Board.Cells.ContainsKey).ToArray();
                // 远程直线武器：弹道表现走满整条射线（打空也飞到射程末端或被障碍挡住处），命中处就是射线末端。
                IReadOnlyList<AxialHex> rayTrace = rayAimed ? GetAttackTraceCells(cardId, target).ToArray() : null;
                AxialHex presentationCenter = rayTrace is { Count: > 0 } ? rayTrace[^1] : target;
                var affectedTargets = new List<BattleUnitPlacement>();
                var seen = new HashSet<int>();
                foreach (var cell in affected)
                {
                    var unit = Occupancy.At(cell);
                    // 敌我过滤（T5，2026-10-04）：空间受影响集合只留**敌方** —— 此前横扫 / 烈闪突 / 陨星投掷 / 穿林箭
                    // 会把友军一起打进真实伤害（D6）。与六边形交互案 §八「会命中友军的行动要求再次确认」的冲突
                    // 按「误伤是 bug」处理，记录见施工文档 §26；打空语义不变（集合为空照常消耗卡牌）。
                    if (unit != null && unit.Presence == BattlefieldPresence.Active && unit.Role != p.Role && seen.Add(unit.UnitId))
                    {
                        affectedTargets.Add(unit);
                    }
                }
                // 允许“对空格/无敌人区域”出牌（打空）：卡牌照常消耗，伤害落空，其余效果仍结算。
                // 目标格只需在射程内即可（无论其中是否存在单位）；无敌人时传入 null 目标并让效果层跳过空目标。
                BattleUnitPlacement selected = Occupancy.At(target);
                if (selected == null || !affectedTargets.Contains(selected)) selected = affectedTargets.FirstOrDefault();
                if (affectedTargets.Count == 0 && thrustApplies)
                {
                    AxialHex endpoint = affected.LastOrDefault();
                    if (endpoint == default) endpoint = target;
                    AttackResolved?.Invoke(new BattlefieldAttackEvent(++attackEventSequence, p.UnitId, null, p.Coord,
                        endpoint, WeaponAttackMode.Thrust, 0, 0, false, AffectedCells: affected,
                        Direction: BattleRangeResolver.PickLineDirection(p.Coord, target)));
                    FinalizeNoTargetSpatialCard(p, handCard, actualCost);
                    ExecuteDash(p, target, spec, Math.Max(1, CurrentAttackRange));
                    return true;
                }

                bool applied = ApplyCardThroughExistingPipeline(p, handCard, selected, affectedTargets, actualCost, out error,
                    affectedCells: rayTrace ?? affected, presentationCenter: presentationCenter);
                if (applied && thrustApplies && !IsFinished)
                {
                    if (selected != null) TryApplyThrustKnockback(p, selected, selected.Coord);
                    ExecuteDash(p, target, spec, Math.Max(1, CurrentAttackRange), selected);
                }
                return applied;
            }

            default:
            {
                // 无空间形状：自身目标卡（抽牌 / 护盾 / 自身状态）
                var allies = ActiveAlliesOnField(p);
                return ApplyCardThroughExistingPipeline(p, handCard, p, Array.Empty<BattleUnitPlacement>(), actualCost, out error, allies);
            }
        }
    }

    private bool ApplyCardThroughExistingPipeline(BattleUnitPlacement source, Card card, BattleUnitPlacement selected,
        IReadOnlyList<BattleUnitPlacement> enemies, int cost, out string error, IReadOnlyList<BattleUnitPlacement> allies = null,
        IReadOnlyCollection<AxialHex> affectedCells = null, AxialHex? presentationCenter = null)
    {
        error = "";
        var allyList = allies ?? ActiveAlliesOnField(source);
        var tracked = Occupancy.Placements.Values.Where(x => x.Presence == BattlefieldPresence.Active).ToArray();
        var beforeHp = tracked.ToDictionary(x => x.UnitId, x => x.Unit.HP);
        var beforeShield = tracked.ToDictionary(x => x.UnitId, x => x.Unit.Shield);
        Card.CardApplyResult result;
        using (new BattlefieldEffectTargetScope(source.Unit, selected?.Unit,
            enemies.Select(x => x.Unit).ToArray(), allyList.Select(x => x.Unit).ToArray(), hpLostThisBattle.GetValueOrDefault(source.UnitId),
            playerTurn: true, canAttack: CanDefaultAttack, spatialAttackTargets: card.Category == CardCategory.Attack && affectedCells != null))
        {
            result = card.Apply(source.Unit, selected?.Unit);
        }
        if (!result.Success) { error = result.ErrorMessage; return false; }
        bool emittedAttackPresentation = false;
        foreach (var unit in tracked)
        {
            int hpLoss = Math.Max(0, beforeHp[unit.UnitId] - unit.Unit.HP);
            int shieldLoss = Math.Max(0, beforeShield[unit.UnitId] - unit.Unit.Shield);
            TrackHpLoss(unit.Unit, beforeHp[unit.UnitId]);
            if (card.Category == CardCategory.Attack && (hpLoss > 0 || shieldLoss > 0))
            {
                AttackResolved?.Invoke(new BattlefieldAttackEvent(++attackEventSequence, source.UnitId, unit.UnitId, source.Coord,
                    presentationCenter ?? unit.Coord, VisualModeForCard(card), hpLoss, shieldLoss, unit.Unit.HP <= 0,
                    beforeHp[unit.UnitId], unit.Unit.HP, beforeShield[unit.UnitId], unit.Unit.Shield, affectedCells,
                    BattleRangeResolver.PickLineDirection(source.Coord, selected?.Coord ?? unit.Coord),
                    IsExplosion: GetSpatialSpec(card.CardId).Explodes));
                emittedAttackPresentation = true;
            }
        }
        if (card.Category == CardCategory.Attack && !emittedAttackPresentation)
        {
            AxialHex endpoint = presentationCenter ?? selected?.Coord ?? source.Coord;
            AttackResolved?.Invoke(new BattlefieldAttackEvent(++attackEventSequence, source.UnitId, null, source.Coord,
                endpoint, VisualModeForCard(card), 0, 0, false, AffectedCells: affectedCells,
                Direction: BattleRangeResolver.PickLineDirection(source.Coord, endpoint),
                IsExplosion: GetSpatialSpec(card.CardId).Explodes));
        }
        source.Unit.Energy -= cost;
        cardsPlayedThisTurn[source.UnitId] = cardsPlayedThisTurn.GetValueOrDefault(source.UnitId) + (card.Category == CardCategory.State ? 0 : 1);
        StateSystem.OnCardPlayed(source.Unit, card);
        if (card.Category == CardCategory.Attack) StateDecayProcessor.ProcessDecayAtTiming(source.Unit, DecayTrigger.OnAttackPlayed);
        BuildPendingOperations(source, card, result);
        CompleteCardLifecycle(source, card);
        if (cost == 0 && card.Category != CardCategory.State && StateSystem.TryGetStateStacks(source.Unit, StateType.NextBattleCardFree, out _))
            StateSystem.RemoveState(source.Unit, StateType.NextBattleCardFree);
        Occupancy.SyncDeaths(); EvaluateOutcome(); Notify();
        Message?.Invoke($"{source.Name} 打出 {card.CardName}，消耗 {cost} 能量。" + (result.EffectResult == null ? "" : " " + result.EffectResult.BuildSummary()));
        return true;
    }

    private WeaponAttackMode VisualModeForCard(Card card)
    {
        CardSpatialSpec spec = GetSpatialSpec(card.CardId);
        return spec.Shape switch
        {
            CardSpatialShape.Fan => WeaponAttackMode.Fan,
            _ when spec.AttackMode.HasValue => spec.AttackMode.Value,
            CardSpatialShape.Line => WeaponAttackMode.RangedLine,
            CardSpatialShape.Burst => WeaponAttackMode.ThrowSingle,
            _ => CurrentWeapon.Mode,
        };
    }

    private void FinalizeNoTargetSpatialCard(BattleUnitPlacement source, Card card, int cost)
    {
        source.Unit.Energy -= cost;
        cardsPlayedThisTurn[source.UnitId] = cardsPlayedThisTurn.GetValueOrDefault(source.UnitId) + (card.Category == CardCategory.State ? 0 : 1);
        StateSystem.OnCardPlayed(source.Unit, card);
        if (card.Category == CardCategory.Attack) StateDecayProcessor.ProcessDecayAtTiming(source.Unit, DecayTrigger.OnAttackPlayed);
        CompleteCardLifecycle(source, card);
        Notify();
        Message?.Invoke($"{source.Name} 打出 {card.CardName}，该方向没有敌人，伤害落空。消耗 {cost} 能量。");
    }

    /// <summary>突刺的位移部分：沿选定方向直线前进，直到用完**卡牌规定的位移格数**
    /// （与每回合移动次数无关，也不消耗移动额度），或前方目标已经落在**攻击范围**内。
    /// 击退攻击由调用方单独结算：角色在位移结束后保持不动，把目标击退一格。</summary>
    private void ExecuteDash(BattleUnitPlacement source, AxialHex target, CardSpatialSpec spec,
        int attackRange, BattleUnitPlacement victim = null)
    {
        AxialHex direction = BattleRangeResolver.PickLineDirection(source.Coord, target);
        int requested = Math.Max(1, spec.MaxRange);   // 位移格数 = 卡牌规定，不由点击距离或每回合移动额度决定
        int moved = 0;
        for (int i = 0; i < requested; i++)
        {
            if (victim != null && AxialHex.Distance(source.Coord, victim.Coord) <= attackRange) break;
            AxialHex next = new AxialHex(source.Coord.Q + direction.Q, source.Coord.R + direction.R);
            if (!Movement.TryMoveWithoutPlayerCost(source.UnitId, next, out _)) break;
            moved++;
            if (source.Presence != BattlefieldPresence.Active || source.Unit.HP <= 0) break;
        }
        Message?.Invoke(moved > 0
            ? $"{source.Name} 沿选定方向突进 {moved} 格。"
            : $"{source.Name} 的突进被单位、障碍或边界阻挡。" );
    }

    private IReadOnlyList<BattleUnitPlacement> ActiveAlliesOnField(BattleUnitPlacement source) =>
        Occupancy.Placements.Values.Where(x => x.Role == source.Role && x.Presence == BattlefieldPresence.Active).ToArray();

    private void BuildPendingOperations(BattleUnitPlacement source, Card card, Card.CardApplyResult result)
    {
        PendingHandChoice choice = null;
        for (int i = 0; i < card.EffectTypes.Length; i++)
        {
            EffectType type = card.EffectTypes[i];
            int[] raw = card.Params != null && i < card.Params.Length ? card.Params[i] : Array.Empty<int>();
            if (type == EffectType.UpgradePermanentCard)
            {
                bool requireKill = raw.Length > 2 && raw[2] > 0;
                if (!requireKill || CardPlayController.DidApplyResultKillTarget(result))
                {
                    var deck = (source.Unit as CharacterInstance)?.DefaultDeck;
                    if (deck != null && deck.Count > 0) deck[random.Next(deck.Count)].PermanentUpgradeLevel++;
                }
            }
            else if (type is EffectType.UpgradeBattleCard or EffectType.AddKeyword)
            {
                choice ??= new PendingHandChoice { PlayerId = source.UnitId, SourceCard = card };
                choice.Operations.Add((type, raw));
            }
            else if (type == EffectType.ConsumeSelectedHandCard)
            {
                // 消耗成本（T6，2026-10-04）：林间抚慰 / 净化 的「消耗任意友军 1 张手牌」。
                // 口径 = **仅施法者自己的手牌**（跨角色选牌要改 PendingHandChoice 结构与选牌 UI，本轮不做，见施工文档 §26）；
                // 选中牌进消耗堆（与卡面关键词 Exhaust 同源）；没有别的牌可选时跳过成本、不阻塞回合。
                choice ??= new PendingHandChoice { PlayerId = source.UnitId, SourceCard = card };
                choice.ConsumeSelectedCard = true;
                choice.ConsumeCount = raw.Length > 1 && raw[1] > 0 ? raw[1] : 1;
            }
        }
        // 候选牌要排除本牌自身（选牌发生在卡牌离手之前）：只剩本牌时视为「无人可选」→ 直接跳过成本。
        if (choice != null && hands[choice.PlayerId].Any(x => x != null && !ReferenceEquals(x, choice.SourceCard)))
        {
            pendingChoice = choice;
            Message?.Invoke(choice.ConsumeSelectedCard ? "请选择一张手牌作为消耗成本。" : "请选择一张手牌完成卡牌效果。");
        }
    }

    public bool TryChooseHandCard(Card target, out string message)
    {
        message = "";
        if (pendingChoice == null || target == null || !hands[pendingChoice.PlayerId].Contains(target)
            || (pendingChoice.ConsumeSelectedCard && ReferenceEquals(target, pendingChoice.SourceCard)))
        {
            message = "该牌不能用于当前选择。";
            return false;
        }

        int operationCount = pendingChoice.Operations.Count;
        foreach (var operation in pendingChoice.Operations)
        {
            if (operation.Effect == EffectType.UpgradeBattleCard) target.BattleUpgradeLevel++;
            else if (operation.Effect == EffectType.AddKeyword && operation.Params.Length >= 2)
            {
                var keyword = Enum.IsDefined(typeof(CardKeyWord), operation.Params[1]) ? (CardKeyWord)operation.Params[1] : CardKeyWord.None;
                var flags = operation.Params.Length > 2 ? (KeywordFlag)operation.Params[2] : KeywordFlag.None;
                if (keyword != CardKeyWord.None) target.AppliedKeywords.Add(new AppliedKeywordEntry { Keyword = keyword, Flags = flags });
            }
        }

        if (!pendingChoice.ConsumeSelectedCard)
        {
            message = $"已对 {target.CardName} 完成 {operationCount} 项卡牌操作。";
            pendingChoice = null; Notify(); return true;
        }

        // 消耗：从手牌移出并放进消耗堆。
        hands[pendingChoice.PlayerId].Remove(target);
        Occupancy.Placements[pendingChoice.PlayerId].Unit.ExhaustPile.Add(target);
        int remainingNeeded = pendingChoice.ConsumeCount - 1;
        bool anotherAvailable = remainingNeeded > 0 && hands[pendingChoice.PlayerId]
            .Any(x => x != null && !ReferenceEquals(x, pendingChoice.SourceCard));
        if (anotherAvailable)
        {
            pendingChoice.ConsumeCount = remainingNeeded;
            message = $"已消耗 {target.CardName}，还需消耗 {remainingNeeded} 张。";
            Message?.Invoke(message);
            Notify();
            return true;
        }

        message = $"已消耗 {target.CardName}（成本已支付）。";
        pendingChoice = null; Notify(); return true;
    }

    private void CompleteCardLifecycle(BattleUnitPlacement source, Card card)
    {
        if (card == null || !hands.TryGetValue(source.UnitId, out var hand)) return;
        hand.Remove(card);
        if (card.Category == CardCategory.State) source.Unit.StatePile.Add(card);
        else if (card.HasKeyWord(CardKeyWord.Exhaust)) source.Unit.ExhaustPile.Add(card);
        else discardPiles[source.UnitId].Add(card);
    }

    private void TrackHpLoss(IUnitInstance unit, int before)
    {
        if (unit == null || unit.HP >= before) return;
        int loss = before - unit.HP;
        hpLostThisBattle[unit.UniqueInGameId] = hpLostThisBattle.GetValueOrDefault(unit.UniqueInGameId) + loss;
        hpLossEvents[unit.UniqueInGameId] = hpLossEvents.GetValueOrDefault(unit.UniqueInGameId) + 1;
        StateSystem.OnHpLost(unit, loss);
    }

    private bool CanDefaultAttack(IUnitInstance attacker, IUnitInstance target)
    {
        if (attacker == null || target == null || !Occupancy.Placements.TryGetValue(attacker.UniqueInGameId, out var a) ||
            !Occupancy.Placements.TryGetValue(target.UniqueInGameId, out var b)) return false;
        return a.Presence == BattlefieldPresence.Active && b.Presence == BattlefieldPresence.Active && AxialHex.Distance(a.Coord, b.Coord) == 1;
    }

    private void EvaluateOutcome()
    {
        Occupancy.SyncDeaths();
        // 失败只由真实 HP 决定。Presence 是战场占位状态，不能把短暂/错误的离场标记当作死亡。
        if (PlayerIds.All(id => Occupancy.Placements[id].Unit.HP <= 0))
            SetOutcome(BattlePhase.Defeat, BuildPlayerFailureReason("全体玩家均已死亡"));
        else if (AllEnemiesDefeated) SetOutcome(BattlePhase.Victory, "所有敌人已被击败。");
        else if (Occupancy.Placements[SelectedId].Presence != BattlefieldPresence.Active)
        {
            int replacement = PlayerIds.FirstOrDefault(id => Occupancy.Placements[id].Presence == BattlefieldPresence.Active && Occupancy.Placements[id].Unit.HP > 0);
            if (replacement != 0) SelectedId = replacement;
        }
    }

    private string BuildPlayerFailureReason(string prefix) => prefix + "。玩家状态：" + string.Join("；", PlayerIds.Select(id =>
    {
        BattleUnitPlacement p = Occupancy.Placements[id];
        return $"{p.Name}(HP={p.Unit.HP}, Presence={p.Presence})";
    }));

    private void SetOutcome(BattlePhase outcome, string reason)
    {
        if (IsFinished) return;
        Phase = outcome; Movement.PlayerTurn = false; Notify(); Message?.Invoke(reason); Finished?.Invoke(outcome);
    }

    /// <summary>未登记在 `AreaObject.csv` 的 TrapId 的兜底伤害（= 改动前写死的 3 点）。</summary>
    private const int LegacyTrapFallbackDamage = 3;

    private void OnEntered(BattlefieldEntry entry)
    {
        UnitEntered?.Invoke(entry);
        var p = Occupancy.Placements[entry.UnitId];
        string text = entry.ConsumesPlayerMove
            ? $"{p.Name} 移动到 ({entry.To.Q},{entry.To.R})，消耗 1 能量、1 次移动。"
            : $"{p.Name} 进入 ({entry.To.Q},{entry.To.R})。";
        if (entry.Trigger != null)
        {
            if (entry.Trigger.Kind is GroundObjectKind.Trap or GroundObjectKind.Mechanism)
            {
                text += ResolveAreaObjectOnEnter(entry.Trigger, entry.To);
            }
            else
            {
                text += $" 进入物件：{entry.Trigger.DefinitionId}（效果待接入）。";
            }
        }

        // 格点地形状态（T8）：与物件互斥无关，进入该格时按表结算 OnEnter 效果（触发次数由地形状态自身扣减）。
        text += ResolveTerrainStates(entry.To, AreaTriggerTiming.OnEnter);
        Message?.Invoke(text);
        Occupancy.SyncDeaths(); EvaluateOutcome(); Notify();
    }

    /// <summary>
    /// 区域物（陷阱 / 机关）进入触发：按 `TrapId` 查 `DataBase/Battlefield/AreaObject.csv` 结算
    /// （T3 = D2 后半 / P2-30 后半，2026-10-04）。未登记的定义保留「3 点伤害」兜底，保住
    /// `debug.battle.place_trap` 等自定义 TrapId 的既有手感。
    /// </summary>
    private string ResolveAreaObjectOnEnter(GroundObject trigger, AxialHex cell)
    {
        AreaObjectSpec spec = BattlefieldAreaObjectRepository.ForId(trigger.DefinitionId);
        if (spec == null)
        {
            int damage = ApplyFlatAreaDamage(Occupancy.At(cell), LegacyTrapFallbackDamage);
            return $" 触发陷阱 {trigger.DefinitionId}，受到 {damage} 点伤害（该 TrapId 未登记 AreaObject.csv，按旧 3 点伤害兜底）。";
        }

        if (!spec.TriggersOnEnter)
        {
            return $" 进入 {spec.Name}（{spec.TrapId}）：触发时机不含「进入」，本次无效果。";
        }

        List<string> applied = ApplyAreaObjectEffects(spec, cell, sourcePlacement: null, out string targetName);
        return applied.Count == 0
            ? $" 进入 {spec.Name}（{spec.TrapId}）：对 {targetName} 未生效（SideFilter={spec.SideFilter}）。"
            : $" 进入 {spec.Name}（{spec.TrapId}）：{targetName} {string.Join("、", applied)}。";
    }

    /// <summary>
    /// 格点地形状态触发（T8）：`OnEnter` 挂在进入事件、`OnTurnEnd` 挂在回合末（<see cref="EndCurrentTurn"/>，一回合一次）。
    /// 与陷阱物件共用同一张表与同一套结算。
    /// </summary>
    private string ResolveTerrainStates(AxialHex cell, AreaTriggerTiming timing)
    {
        if (!Board.Cells.TryGetValue(cell, out BattleCell battleCell) || battleCell.TerrainStates.Count == 0)
        {
            return string.Empty;
        }

        List<string> notes = new List<string>();
        foreach (BattleTerrainState state in battleCell.TerrainStates.ToArray())
        {
            AreaObjectSpec spec = BattlefieldAreaObjectRepository.ForId(state.DefinitionId);
            if (spec == null)
            {
                notes.Add($"地形 {state.DefinitionId} 未登记 AreaObject.csv");
                continue;
            }

            if ((spec.Timing & timing) == 0)
            {
                continue;
            }

            BattleUnitPlacement source = state.SourceUnitId != 0 && Occupancy.Placements.TryGetValue(state.SourceUnitId, out BattleUnitPlacement origin)
                ? origin : null;
            List<string> applied = ApplyAreaObjectEffects(spec, cell, source, out string targetName);
            notes.Add(applied.Count == 0
                ? $"{spec.Name}（{spec.TrapId}）对 {targetName} 未生效"
                : $"{spec.Name}（{spec.TrapId}）对 {targetName} {string.Join("、", applied)}");
            Board.TryConsumeTerrainTrigger(cell, state.InstanceId, out _, out _);
        }

        return notes.Count == 0 ? string.Empty : " 格点效果：" + string.Join("；", notes) + "。";
    }

    /// <summary>
    /// 按表结算一次格点效果（进入 / 回合末共用）：目标 = 该格上的单位，按 <see cref="AreaObjectSpec.SideFilter"/> 过滤敌我。
    /// 结算复用现成层 —— 伤害为**表值平伤**（先吃护盾，与改动前的陷阱口径一致，不吃攻击力 / 虚弱），
    /// 状态走 `StateSystem.AddOrUpdateState`（**buff 与 debuff 同一条通道**，不为增益另开枚举，T8 口径）。
    /// </summary>
    private List<string> ApplyAreaObjectEffects(AreaObjectSpec spec, AxialHex cell, BattleUnitPlacement sourcePlacement, out string targetName)
    {
        List<string> applied = new List<string>();
        BattleUnitPlacement target = Occupancy.At(cell);
        targetName = target?.Name ?? "该格";
        if (target == null || target.Presence != BattlefieldPresence.Active || target.Unit.HP <= 0)
        {
            targetName = "格上无单位";
            return applied;
        }

        if (!PassesSideFilter(spec.SideFilter, sourcePlacement, target))
        {
            return applied;
        }

        for (int i = 0; i < spec.EffectTypes.Length; i++)
        {
            int[] args = spec.Params != null && i < spec.Params.Length ? spec.Params[i] : Array.Empty<int>();
            int value = args.Length > 1 ? Math.Max(0, args[1]) : 1;
            switch (spec.EffectTypes[i])
            {
                case EffectType.Damage:
                    applied.Add($"受到 {ApplyFlatAreaDamage(target, value)} 点伤害");
                    break;
                case EffectType.HpLoss:
                    EffectSystem.ApplyHpLoss(target.Unit, value);
                    applied.Add($"失去 {value} 点生命（无视护盾）");
                    break;
                case EffectType.Heal:
                {
                    int healed = Math.Min(value, Math.Max(0, target.Unit.Max_HP - target.Unit.HP));
                    target.Unit.HP += healed;
                    applied.Add($"恢复 {healed} 点生命");
                    break;
                }
                case EffectType.Shield:
                    EffectSystem.ApplyShield(target.Unit, new[] { value });
                    applied.Add($"获得 {value} 点护盾");
                    break;
                case EffectType.AddState:
                {
                    if (args.Length < 2 || !Enum.IsDefined(typeof(StateType), args[1]))
                    {
                        applied.Add("状态参数无效（已跳过）");
                        break;
                    }

                    StateType stateType = (StateType)args[1];
                    int stacks = args.Length > 2 && args[2] > 0 ? args[2] : 1;
                    StateSystem.AddOrUpdateState(target.Unit, stateType, stacks, ownerUnit: sourcePlacement?.Unit);
                    applied.Add($"获得 {stacks} 层 {stateType}");
                    break;
                }
                default:
                    applied.Add($"未支持的效果 {spec.EffectTypes[i]}（已跳过）");
                    break;
            }
        }

        if (applied.Count > 0)
        {
            Occupancy.SyncDeaths();
        }

        return applied;
    }

    /// <summary>格点效果的作用对象过滤：`Triggerer` = 格上单位本人；`Enemy` / `Ally` 相对来源单位（无来源时按「触发者」处理，即不过滤）。</summary>
    private static bool PassesSideFilter(CellEffectSideFilter filter, BattleUnitPlacement source, BattleUnitPlacement target)
    {
        if (target == null)
        {
            return false;
        }

        return filter switch
        {
            CellEffectSideFilter.Enemy => source == null || target.Role != source.Role,
            CellEffectSideFilter.Ally => source == null || target.Role == source.Role,
            CellEffectSideFilter.All => true,
            _ => true,   // Triggerer：结算目标本就是该格上的单位
        };
    }

    /// <summary>表值平伤：先吃护盾再扣生命（与改动前 `OnEntered` 的陷阱伤害同口径），并登记生命账本。</summary>
    private int ApplyFlatAreaDamage(BattleUnitPlacement target, int amount)
    {
        if (target == null || amount <= 0 || target.Unit.HP <= 0)
        {
            return 0;
        }

        int before = target.Unit.HP;
        int absorbed = Math.Min(target.Unit.Shield, amount);
        target.Unit.Shield -= absorbed;
        int hpLoss = amount - absorbed;
        if (hpLoss > 0)
        {
            target.Unit.HP = Math.Max(0, target.Unit.HP - hpLoss);
        }

        TrackHpLoss(target.Unit, before);
        return absorbed + hpLoss;
    }

    /// <summary>
    /// 回合末的格点效果结算（T8）：**每次玩家结束回合只跑一遍**（不按角色各扫一次，执行方案 §2.7 明确
    /// 「避免三角色每人调用一次导致衰减三次」）；结算 `TriggerTiming` 含 `OnTurnEnd` 的地形状态，
    /// 再按 `DecayTiming` 衰减层数（归零即移除）。
    /// </summary>
    private void ResolveTerrainTurnEnd()
    {
        foreach (BattleCell cell in Board.Cells.Values.ToArray())
        {
            if (cell.TerrainStates.Count == 0)
            {
                continue;
            }

            string note = ResolveTerrainStates(cell.Coord, AreaTriggerTiming.OnTurnEnd);
            foreach (BattleTerrainState state in cell.TerrainStates.ToArray())
            {
                AreaObjectSpec spec = BattlefieldAreaObjectRepository.ForId(state.DefinitionId);
                if (spec == null || spec.DecayTiming != TerrainDecayTiming.OnTurnEnd)
                {
                    continue;
                }

                int stacks = state.Stacks - 1;
                if (stacks <= 0) Board.TryRemoveTerrainState(cell.Coord, state.InstanceId, out _);
                else Board.TryReplaceTerrainState(cell.Coord, state.InstanceId, state with { Stacks = stacks });
            }

            if (note.Length > 0)
            {
                Message?.Invoke(note.Trim());
            }
        }
    }

    /// <summary>
    /// 落物时写入「持续地形」状态（T8）：只有 `TriggerTiming` 含 `OnTurnEnd` 的定义才写 ——
    /// `OnEnter` 由物件本体承载，避免同一次进入被结算两次。
    /// </summary>
    private void WriteLandingTerrainState(GroundObject trap, AxialHex cell, BattleUnitPlacement source)
    {
        AreaObjectSpec spec = BattlefieldAreaObjectRepository.ForId(trap.DefinitionId);
        if (spec == null || !spec.TriggersOnTurnEnd)
        {
            return;
        }

        string instanceId = $"terrain-{source.UnitId}-{Guid.NewGuid():N}";
        var state = new BattleTerrainState(instanceId.Substring(0, Math.Min(24, instanceId.Length)), spec.TrapId,
            source.UnitId, 1, spec.MaxTriggers);
        if (!Board.TryAddTerrainState(cell, state, out string error))
        {
            Message?.Invoke($"地形状态写入失败：{error}");
        }
    }

    private void OnCellChanged(AxialHex coord) => Notify();
    private void Notify() => Changed?.Invoke();

    public void Dispose()
    {
        Occupancy.Changed -= Notify; Board.CellChanged -= OnCellChanged; Movement.Entered -= OnEntered;
        foreach (var p in Occupancy.Placements.Values) p.Unit.OnDead = null;
        Changed = null; Message = null;
        Finished = null;
    }
}
