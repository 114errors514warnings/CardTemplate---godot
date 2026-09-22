using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator;
using CardSimulator.Battlefield;

/// <summary>Run in Godot with -- --battlefield-smoke. Never instantiated by the .NET-only tests.</summary>
public static class BattlefieldSceneSmoke
{
    public static async void Run(HexBattleScene scene)
    {
        try
        {
            await scene.ToSignal(scene.GetTree(), SceneTree.SignalName.ProcessFrame);
            await scene.ToSignal(scene.GetTree(), SceneTree.SignalName.ProcessFrame);
            // 弓箭射线几何单独自建一场验证，不依赖本烟测后续的既有流程。
            VerifyBowRayGeometry();
            VerifyCardWeaponModeStacking();
            VerifyNormalCombatPool();
            VerifyAssetPaths();
            VerifyMonsterInitialStates();
            VerifyStateEnumNames();
            VerifyGoldStealLedger();
            VerifyMonsterStealTrigger();
            if (OS.GetCmdlineUserArgs().Contains("--battlefield-bow-capture"))
                await CaptureBowRangePreview(scene, scene.Session);
            var session = scene.Session; var view = scene.MapView;
            if (OS.GetCmdlineUserArgs().Contains("--battlefield-hand-capture"))
            {
                await scene.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Error initialCapture = scene.GetViewport().GetTexture().GetImage().SavePng("res://README/施工文档/2026/2026.09/六边形手牌初始实测.png");
                Check(initialCapture == Error.Ok, "initial hand capture saved");
            }
            Check(session.PlayerIds.Count == 3 && session.Occupancy.Occupants.Count == 9, "deployment");
            Check(session.Selected.BaseMovesPerTurn == 3, "CSV MovesPerTurn reaches runtime");
            int firstId = session.SelectedId;
            int hp = session.Selected.Unit.HP;
            var second = session.Occupancy.Placements[session.PlayerIds[1]];
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = view.CellPosition(second.Coord) });
            Check(session.SelectedId == second.UnitId, "player click");
            var enemy = session.Occupancy.Placements.Values.First(x => x.Role == BattlefieldRole.Enemy);
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = view.CellPosition(enemy.Coord) });
            Check(session.SelectedId == second.UnitId && view.Describe(enemy.Coord).Contains(enemy.Name), "enemy hover-only inspection");
            session.Select(firstId); view.CenterSelected();
            Vector2 panBefore = view.Pan;
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
            Check(view.Pan == panBefore, "wheel does not zoom or pan");
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
            view._GuiInput(new InputEventMouseMotion { ButtonMask = MouseButtonMask.Right, Relative = new Vector2(40, 15) });
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            Check(view.Pan != panBefore, "right drag");
            Check(view.CellAt(view.CellPosition(session.Selected.Coord)) == session.Selected.Coord, "panned hit test");
            var destinations = session.Movement.LegalDestinations(firstId);
            Check(destinations.Count > 0, "available step");
            var movingPlayer = session.Occupancy.Placements[firstId];
            int energy = movingPlayer.Unit.Energy;
            Check(session.Movement.TryMove(firstId, destinations[0], out _), "api-compatible one step");
            Check(movingPlayer.Unit.Energy == energy - 1 && movingPlayer.MovesUsedThisTurn == 1, "one step one energy");
            Check(movingPlayer.Unit.HP == hp, "movement preserves HP");
            Check(!session.Movement.TryMove(firstId, second.Coord, out _), "occupied rejected");
            session.SetTestEquipmentBonus(2);
            Check(session.Selected.RemainingMoves == 4 && session.Selected.MovesUsedThisTurn == 1, "equipment immediate");
            session.SetTestEquipmentBonus(0);
            Check(session.Selected.RemainingMoves == 2, "unequip preserves spent moves");
            session.NextTestRound();
            // 能量上限来自 `DataBase/GameVariables.csv`（Global DefaultEnergyPerTurn），不写死数字。
            int expectedEnergy = session.Selected.Unit is CharacterInstance selectedCharacter
                ? selectedCharacter.Max_costs
                : GameVariables.Load().DefaultEnergyPerTurn;
            Check(session.Selected.Unit.Energy == expectedEnergy && session.Selected.MovesUsedThisTurn == 0,
                $"turn reset (energy {session.Selected.Unit.Energy}/{expectedEnergy}, moves used {session.Selected.MovesUsedThisTurn})");
            var lootCell = new AxialHex(-1, 0);
            Check(session.Occupancy.At(lootCell) == null, "fixture loot cell open");
            session.Occupancy.CommitMove(session.Selected, lootCell);
            GroundObject equipment = session.Board.Cells[lootCell].Items.First(x => x.Kind == GroundObjectKind.Equipment);
            Check(session.TryEquipFromCurrentCell(equipment.InstanceId, BattlefieldSession.HandSlot.Left, out string inventoryError), "equip: " + inventoryError);
            Check(session.CurrentAttackRange == BattleWeaponCatalog.Resolve(equipment).AttackRange && session.Selected.EffectiveMovesPerTurn == 4, "equipment modifiers");
            GroundObject item = session.Board.Cells[lootCell].Items.First(x => x.Kind == GroundObjectKind.Item);
            session.Selected.Unit.HP -= 4;
            Check(session.TryPickItemFromCurrentCell(item.InstanceId, 0, out inventoryError), "pickup: " + inventoryError);
            int woundedHp = session.Selected.Unit.HP;
            Check(session.TryUseItem(0, out inventoryError) && session.Selected.Unit.HP > woundedHp, "use item: " + inventoryError);
            StateSystem.AddOrUpdateState(session.Selected.Unit, StateType.Vulnerable, 1);
            Check(view.Describe(session.Selected.Coord).Contains("易伤"), "state tooltip binding");
            session.DrawCards(session.SelectedId, 99);
            Card selfCard = session.GetHand(session.SelectedId).FirstOrDefault(x => x.CardId == 21001001);
            Check(selfCard != null, "configured hand card loaded");
            int handBefore = session.HandCount(session.SelectedId);
            Check(session.TryCastCard(selfCard.CardId, session.Selected.Coord, out string castError), "existing card pipeline: " + castError);
            Check(session.HandCount(session.SelectedId) >= handBefore - 1, "draw card effect and lifecycle");
            int attackBeforeBow = session.Selected.Unit.Attack;
            var bow = new GroundObject("smoke-bow", "弓箭", GroundObjectKind.Equipment, handsRequired: 2, attackRange: 5);
            Check(session.Board.TryAddObject(session.Selected.Coord, bow, out inventoryError), "place configured bow: " + inventoryError);
            Check(session.TryEquipFromCurrentCell(bow.InstanceId, BattlefieldSession.HandSlot.Left, out inventoryError), "equip configured bow: " + inventoryError);
            Check(session.Selected.Unit.Attack == attackBeforeBow + 2 && session.CurrentWeapon.Mode == WeaponAttackMode.RangedLine,
                "bow applies attack and ranged-line mode");
            Card defenseCard = session.GetHand(session.SelectedId).FirstOrDefault(x => x.CardId == 20000001);
            Check(defenseCard != null && !session.TryCastCard(defenseCard.CardId, session.Selected.Coord, out castError)
                && castError.Contains("无法通过防御牌获得护盾"), "bow blocks defense shield");
            int attackBeforeSword = session.Selected.Unit.Attack, defenseBeforeSword = session.Selected.Unit.Defend;
            var twoHandSword = new GroundObject("smoke-two-hand-sword", "双手剑", GroundObjectKind.Equipment, handsRequired: 2, attackRange: 1);
            Check(session.Board.TryAddObject(session.Selected.Coord, twoHandSword, out inventoryError), "place configured two-hand sword: " + inventoryError);
            Check(session.TryEquipFromCurrentCell(twoHandSword.InstanceId, BattlefieldSession.HandSlot.Left, out inventoryError), "equip configured two-hand sword: " + inventoryError);
            Check(session.Selected.Unit.Attack == attackBeforeSword - 1 && session.Selected.Unit.Defend == defenseBeforeSword + 1,
                "two-hand sword applies attack and defense");
            var tome = new GroundObject("smoke-tome", "法典", GroundObjectKind.Equipment, handsRequired: 2, attackRange: 4);
            Check(session.Board.TryAddObject(session.Selected.Coord, tome, out inventoryError), "place configured tome: " + inventoryError);
            Check(session.TryEquipFromCurrentCell(tome.InstanceId, BattlefieldSession.HandSlot.Left, out inventoryError)
                && session.CurrentWeapon.Mode == WeaponAttackMode.ThrowSingle, "tome applies throw attack mode");
            Card burstCard = session.GetHand(session.SelectedId).FirstOrDefault(x => x.CardId == 11001002);
            Check(burstCard != null && !session.GetCastCandidates(burstCard.CardId).Contains(session.Selected.Coord), "burst cannot target self cell");
            Card thrustCard = session.GetHand(session.SelectedId).FirstOrDefault(x => x.CardId == 11001003);
            Check(thrustCard != null, "thrust card loaded");
            // 突刺要落在近战武器上才有意义：远程武器会把卡牌的 `Thrust` 方式覆盖为武器方式（卡牌×武器叠加规则），
            // 因此先把双手剑换回来（射程 1），再验证"位移 = 卡牌 2 格、目标进射程即停"。
            Check(session.TryEquipFromCurrentCell(twoHandSword.InstanceId, BattlefieldSession.HandSlot.Left, out inventoryError)
                && session.CurrentAttackRange == 1, "melee weapon re-equipped for thrust: " + inventoryError);
            AxialHex thrustUnitCoord = session.Selected.Coord;
            // 候选受武器射程约束（此处射程 1 → 只候选相邻格）；挑一条"整条 2 格位移通道都空"的方向，
            // 让突刺位移断言不受随机障碍影响（位移格数 = 卡牌规定，见 BattlefieldSession.EffectiveSpec）。
            const int thrustDistance = 2;
            AxialHex thrustTarget = session.GetCastCandidates(thrustCard.CardId).First(cell =>
            {
                var step = new AxialHex(cell.Q - thrustUnitCoord.Q, cell.R - thrustUnitCoord.R);
                return Enumerable.Range(1, thrustDistance).All(index =>
                {
                    var path = new AxialHex(thrustUnitCoord.Q + step.Q * index, thrustUnitCoord.R + step.R * index);
                    return session.Board.IsWalkable(path) && session.Occupancy.At(path) == null;
                });
            });
            AxialHex thrustStart = session.Selected.Coord;
            Check(session.TryCastCard(thrustCard.CardId, thrustTarget, out castError), "thrust without target: " + castError);
            int thrustMoved = AxialHex.Distance(thrustStart, session.Selected.Coord);
            Check(session.Selected.Coord != thrustStart && thrustMoved == 2,
                $"thrust moves without enemy (moved {thrustMoved})");
            Card attackCard = session.GetHand(session.SelectedId).FirstOrDefault(x => x.CardId == 11001001);
            Check(attackCard != null, "spatial attack card loaded");
            var attackCell = BattleHexLayout.Neighbors(session.Selected.Coord).First(x => session.Occupancy.CanEnter(x));
            var attackEnemy = session.Occupancy.Placements.Values.First(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active);
            session.Occupancy.CommitMove(attackEnemy, attackCell);
            int targetHp = attackEnemy.Unit.HP;
            Check(session.TryCastCard(attackCard.CardId, attackCell, out castError), "spatial damage pipeline: " + castError);
            Check(attackEnemy.Unit.HP < targetHp, "spatial card damages only validated target");
            VerifyRunBattleInjection();
            VerifyFirstFormalLevel();
            var enemyBefore = session.Occupancy.Placements.Values.Where(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active)
                .ToDictionary(x => x.UnitId, x => x.Coord);
            session.EndCurrentTurn();
            while (session.Phase == BattlefieldSession.BattlePhase.Monsters && session.ExecuteNextMonsterTurnStep()) { }
            Check(session.Round == 3 && session.Phase == BattlefieldSession.BattlePhase.Player, "monster turn then player round");
            Check(enemyBefore.Any(x => session.Occupancy.Placements[x.Key].Presence != BattlefieldPresence.Active ||
                session.Occupancy.Placements[x.Key].Coord != x.Value), "monster spatial action");
            foreach (var remainingEnemy in session.Occupancy.Placements.Values.Where(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active).ToArray())
                remainingEnemy.Unit.HP = 0;
            Check(session.Phase == BattlefieldSession.BattlePhase.Victory, "victory outcome");
            view.CenterSelected(); view.SetMoving(true); view.QueueRedraw();
            await scene.ToSignal(scene.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (OS.GetCmdlineUserArgs().Contains("--battlefield-capture") || OS.GetCmdlineUserArgs().Contains("--battlefield-api-capture"))
            {
                await scene.ToSignal(scene.GetTree(), SceneTree.SignalName.ProcessFrame);
                string path = OS.GetCmdlineUserArgs().Contains("--battlefield-api-capture")
                    ? "res://Tests/api-battlefield-smoke.png"
                    : "res://README/施工文档/2026/2026.09/六边形基础层实测.png";
                Error error = scene.GetViewport().GetTexture().GetImage().SavePng(path);
                Check(error == Error.Ok, "capture saved");
            }
            GD.Print("BATTLEFIELD_SMOKE_PASS: deployment, CSV, click, hover, pan, fixed scale, movement, equipment, items, card pipeline, thrust, burst self exclusion, spatial damage, monster turn, states, victory");
            scene.GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PrintErr("BATTLEFIELD_SMOKE_FAIL: " + ex);
            scene.GetTree().Quit(1);
        }
    }

    private static void VerifyRunBattleInjection()
    {
        string path = BattleLevelCatalog.ResolveMapPath("M-F1-001");
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var definition = BattleMapDefinition.Parse(file.GetAsText());
        definition.PlayerCharacterIds = new List<int> { 1002, 1003, 1004 };
        definition.MonsterIds = new List<int> { 3101 };
        var runBattle = new BattlefieldSession(definition);
        var slots = new List<RunCharacterSlotSave>
        {
            new() { CharacterId = 1002, CurrentHp = 24, MaxHp = 30, EquippedWeaponDefinitionId = "双手剑" },
            new() { CharacterId = 1003, CurrentHp = 25, MaxHp = 30, EquippedWeaponDefinitionId = "弓箭" },
            new() { CharacterId = 1004, CurrentHp = 26, MaxHp = 30, EquippedWeaponDefinitionId = "法典" },
        };
        var decks = new List<List<RunDeckEntry>>
        {
            new() { new() { CardId = 10000001 }, new() { CardId = 20000001 } },
            new() { new() { CardId = 10000001 }, new() { CardId = 20000001 } },
            new() { new() { CardId = 10000001 }, new() { CardId = 20000001 } },
        };
        runBattle.RestoreRunState(slots, decks);
        Check(runBattle.Selected.Name == "重剑手" && runBattle.Selected.Unit.HP == 24 && runBattle.CurrentWeapon.DefinitionId == "双手剑"
            && runBattle.Selected.Unit.Attack == 3 && runBattle.Selected.Unit.Defend == 3, "run inject heavy sword and stats");
        runBattle.Select(runBattle.PlayerIds[1]);
        Check(runBattle.Selected.Name == "精灵" && runBattle.Selected.Unit.HP == 25 && runBattle.CurrentWeapon.DefinitionId == "弓箭"
            && runBattle.CurrentWeapon.Mode == WeaponAttackMode.RangedLine, "run inject elf bow");
        runBattle.Select(runBattle.PlayerIds[2]);
        Check(runBattle.Selected.Name == "法师" && runBattle.Selected.Unit.HP == 26 && runBattle.CurrentWeapon.DefinitionId == "法典"
            && runBattle.CurrentWeapon.Mode == WeaponAttackMode.ThrowSingle, "run inject mage tome");
        runBattle.Dispose();
    }

    /// <summary>弓箭（远程直线武器）：攻击牌只能沿六个方向发射，命中该方向上的第一个阻挡；候选格不再是整片射程六边形。
    /// 自建一场战斗独立验证，不复用主烟测流程。</summary>
    private static void VerifyBowRayGeometry()
    {
        string path = BattleLevelCatalog.ResolveMapPath("M-F1-001");
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var definition = BattleMapDefinition.Parse(file.GetAsText());
        definition.PlayerCharacterIds = new List<int> { 1002, 1003, 1004 };
        definition.MonsterIds = new List<int> { 3101 };
        var bowBattle = new BattlefieldSession(definition);
        var slots = new List<RunCharacterSlotSave>
        {
            new() { CharacterId = 1002, CurrentHp = 30, MaxHp = 30, EquippedWeaponDefinitionId = "双手剑" },
            new() { CharacterId = 1003, CurrentHp = 30, MaxHp = 30, EquippedWeaponDefinitionId = "弓箭" },
            new() { CharacterId = 1004, CurrentHp = 30, MaxHp = 30, EquippedWeaponDefinitionId = "法典" },
        };
        var decks = new List<List<RunDeckEntry>>
        {
            new() { new() { CardId = 10000001 }, new() { CardId = 20000001 } },
            new() { new() { CardId = 10000001 }, new() { CardId = 20000001 } },
            new() { new() { CardId = 10000001 }, new() { CardId = 20000001 } },
        };
        bowBattle.RestoreRunState(slots, decks);
        bowBattle.Select(bowBattle.PlayerIds[1]);
        Check(bowBattle.CurrentWeapon.Mode == WeaponAttackMode.RangedLine, "elf holds the bow");
        const int attackCardId = 10000001;
        AxialHex origin = bowBattle.Selected.Coord;
        var candidates = bowBattle.GetCastCandidates(attackCardId);
        int hexagonCells = BattleRangeResolver.CellsWithinRange(origin, bowBattle.CurrentAttackRange)
            .Count(x => bowBattle.Board.Cells.ContainsKey(x));
        Check(candidates.Count > 0 && candidates.All(cell => BattleRangeResolver.TryGetExactLineDirection(origin, cell, out _)),
            $"bow candidates stay on the six axial rays (origin {origin.Q},{origin.R})");
        Check(candidates.Count < hexagonCells, "bow candidates are not the whole range hexagon");
        List<AxialHex> probeRay = null;
        foreach (AxialHex direction in BattleRangeResolver.SixNeighborOffsets)
        {
            var cells = BattleAttackSystem.ResolveAxialRay(bowBattle.Board, bowBattle.Occupancy, origin, direction, bowBattle.CurrentAttackRange);
            if (cells.Count >= 2 && cells.All(c => bowBattle.Board.IsWalkable(c) && bowBattle.Occupancy.At(c) == null))
            {
                probeRay = cells.ToList(); break;
            }
        }
        Check(probeRay != null, $"bow has a clear ray of two cells (origin {origin.Q},{origin.R})");
        AxialHex blockerCell = probeRay[^2];
        AxialHex behindCell = probeRay[^1];
        Check(candidates.Contains(behindCell), "clear ray cells are selectable before blocking");
        Check(bowBattle.GetAttackTraceCells(attackCardId, behindCell).Count == probeRay.Count, "bow attack trace follows the whole clear ray");
        var blocker = bowBattle.Occupancy.Placements.Values.First(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active);
        AxialHex blockerOrigin = blocker.Coord;
        bowBattle.Occupancy.CommitMove(blocker, blockerCell);
        var blockedCandidates = bowBattle.GetCastCandidates(attackCardId);
        Check(blockedCandidates.Count == candidates.Count && blockedCandidates.Contains(blockerCell) && blockedCandidates.Contains(behindCell),
            "bow range indicator keeps the full six-direction rays even when a unit blocks");
        var blockedAffected = bowBattle.GetAffectedCells(attackCardId, behindCell);
        Check(blockedAffected.Count == probeRay.Count - 1 && blockedAffected.Contains(blockerCell) && !blockedAffected.Contains(behindCell),
            "bow red range (affected) is truncated at the first blocker");
        Check(bowBattle.GetAttackTraceCells(attackCardId, behindCell).Last() == blockerCell, "blocked bow trace stops at the blocker cell");
        // 端到端：瞄准阻挡后方的格点也能出牌，但弹道与伤害都落在“直线上的第一个阻挡”格。
        AxialHex? presentedImpact = null;
        bowBattle.AttackResolved += attack => presentedImpact = attack.To;
        Check(bowBattle.TryCastCard(attackCardId, behindCell, out string hitError), "bow shoots along the blocked ray: " + hitError);
        Check(presentedImpact == blockerCell,
            $"bow hit presentation lands on the first blocker (aim {behindCell.Q},{behindCell.R} -> hit {presentedImpact?.Q},{presentedImpact?.R})");
        bowBattle.Occupancy.CommitMove(blocker, blockerOrigin);
        // 障碍物同样截断红色：地形也计入“首个阻挡”，而黄色范围仍然贯穿到射程末端。
        bowBattle.Board.ChangeTerrain(blockerCell, BattleCellKind.Obstacle, BattleSurface.Ground, false);
        var obstacleAffected = bowBattle.GetAffectedCells(attackCardId, behindCell);
        Check(obstacleAffected.Last() == blockerCell && !obstacleAffected.Contains(behindCell),
            "bow red range stops at an obstacle as well");
        Check(bowBattle.GetCastCandidates(attackCardId).Contains(behindCell),
            "bow yellow range still spans the whole line past an obstacle");
        bowBattle.Board.ChangeTerrain(blockerCell, BattleCellKind.Normal, BattleSurface.Ground, false);
        bowBattle.Dispose();
        GD.Print("BATTLEFIELD_BOW_RAY_PASS: 弓箭攻击牌黄色=六方向完整直线（不被目标/障碍截断），红色=被首个阻挡截断的弹道段，非轴格不可选");
    }

    /// <summary>弓箭范围高亮的可视化验证（仅由 `--battlefield-bow-capture` 触发）：
    /// 黄色 = 六个方向的完整直线（不被途中目标截断）；红色 = 悬停方向的弹道段，到首个单位或障碍为止。
    /// 注意：本方法会给选中角色临时装上弓箭，并保留预览状态，仅供出图使用。</summary>
    private static async System.Threading.Tasks.Task CaptureBowRangePreview(HexBattleScene scene, BattlefieldSession session)
    {
        var bow = new GroundObject("capture-bow", "弓箭", GroundObjectKind.Equipment, handsRequired: 2, attackRange: 5);
        if (session.Board.TryAddObject(session.Selected.Coord, bow, out string equipError)
            && session.TryEquipFromCurrentCell(bow.InstanceId, BattlefieldSession.HandSlot.Left, out equipError))
        {
            const int attackCardId = 10000001;
            AxialHex origin = session.Selected.Coord;
            var candidates = session.GetCastCandidates(attackCardId);
            AxialHex hover = candidates.First(c => AxialHex.Distance(origin, c) == session.CurrentAttackRange);
            scene.MapView.SetCastPreview(candidates, session.GetAffectedCells(attackCardId, hover), origin, hover);
            await scene.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Error error = scene.GetViewport().GetTexture().GetImage().SavePng("res://Tests/battlefield-bow-range.png");
            Check(error == Error.Ok, "bow range capture saved");
            GD.Print("BATTLEFIELD_BOW_CAPTURE: Tests/battlefield-bow-range.png");
        }
        else
        {
            GD.PrintErr("BATTLEFIELD_BOW_CAPTURE_SKIP: " + equipError);
        }
    }

    /// <summary>卡牌特殊攻击方式与武器类型的叠加（2026-09-22 定）：
    /// 卡牌没声明特殊方式 → 按武器方式；声明了且武器类型匹配 → 卡牌方式 + 武器距离；不匹配 → 回落武器方式。
    /// 用 勇士 的两张特殊牌（横扫=扇形、烈闪突=突刺）分别配 双手剑（近战）与 弓箭（远程）验证。</summary>
    private static void VerifyCardWeaponModeStacking()
    {
        const int sweepCardId = 11001002;   // 横扫：Fan（近战型）
        const int thrustCardId = 11001003;  // 烈闪突：Line + AttackMode=Thrust（近战型）
        const int meleeSlot = 0, rangedSlot = 1;

        string path = BattleLevelCatalog.ResolveMapPath("M-F1-001");
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var definition = BattleMapDefinition.Parse(file.GetAsText());
        definition.PlayerCharacterIds = new List<int> { 1002, 1003, 1004 };
        definition.MonsterIds = new List<int> { 3101 };
        var battle = new BattlefieldSession(definition);
        var slots = new List<RunCharacterSlotSave>
        {
            new() { CharacterId = 1002, CurrentHp = 30, MaxHp = 30, EquippedWeaponDefinitionId = "长刀" },   // 近战，距离 2
            new() { CharacterId = 1003, CurrentHp = 30, MaxHp = 30, EquippedWeaponDefinitionId = "弓箭" },
            new() { CharacterId = 1004, CurrentHp = 30, MaxHp = 30, EquippedWeaponDefinitionId = "法典" },
        };
        List<RunDeckEntry> spatialDeck() => new() { new() { CardId = sweepCardId }, new() { CardId = thrustCardId } };
        var decks = new List<List<RunDeckEntry>> { spatialDeck(), spatialDeck(), spatialDeck() };
        battle.RestoreRunState(slots, decks);

        // 1) 近战武器 + 扇形牌：卡牌方式生效 → 候选是六个方向（扇形要选方向）
        battle.Select(battle.PlayerIds[meleeSlot]);
        Check(battle.CurrentWeapon.Type == EquipmentType.Melee, "heavy sword is melee");
        var meleeSweep = battle.GetCastCandidates(sweepCardId);
        Check(meleeSweep.Count == 6 && meleeSweep.All(c => AxialHex.Distance(battle.Selected.Coord, c) == 1),
            "melee weapon keeps the card fan mode (six directions)");
        var meleeFanCells = battle.GetAffectedCells(sweepCardId, meleeSweep.First());
        Check(meleeFanCells.Any(c => !BattleRangeResolver.TryGetExactLineDirection(battle.Selected.Coord, c, out _)),
            $"melee weapon keeps the card fan shape (cells {meleeFanCells.Count}, non-axial included)");

        // 2) 远程武器 + 扇形牌：类型不匹配 → 回落武器的远程直线几何
        battle.Select(battle.PlayerIds[rangedSlot]);
        Check(battle.CurrentWeapon.Type == EquipmentType.Ranged, "bow is ranged");
        var rangedSweep = battle.GetCastCandidates(sweepCardId);
        Check(rangedSweep.Count > 6 && rangedSweep.All(c => BattleRangeResolver.TryGetExactLineDirection(battle.Selected.Coord, c, out _)),
            "ranged weapon overrides the card fan mode with the six axial rays");

        // 3) 远程武器 + 突刺牌：不匹配 → 按远程直线打，且不会位移
        var rangedThrust = battle.GetCastCandidates(thrustCardId);
        Check(rangedThrust.All(c => BattleRangeResolver.TryGetExactLineDirection(battle.Selected.Coord, c, out _)),
            "ranged weapon overrides the card thrust mode with the six axial rays");
        AxialHex rangedThrustStart = battle.Selected.Coord;
        Check(battle.TryCastCard(thrustCardId, rangedThrust.First(), out string rangedThrustError),
            "ranged weapon casts the thrust card as a shot: " + rangedThrustError);
        Check(battle.Selected.Coord == rangedThrustStart, "ranged weapon does not dash");

        // 4) 近战武器 + 突刺牌：卡牌方式生效 → 位移格数取卡牌、攻击范围取武器、不消耗每回合移动次数
        battle.Select(battle.PlayerIds[meleeSlot]);
        var thrustDirections = battle.GetCastCandidates(thrustCardId);
        Check(thrustDirections.Count > 0, "melee thrust has targets");
        int movesBefore = battle.Selected.MovesUsedThisTurn;
        AxialHex meleeThrustStart = battle.Selected.Coord;
        AxialHex clearDirection = default;
        foreach (AxialHex candidate in thrustDirections)
        {
            BattleRangeResolver.TryGetExactLineDirection(meleeThrustStart, candidate, out clearDirection);
            var probe = BattleAttackSystem.ResolveAxialRay(battle.Board, battle.Occupancy, meleeThrustStart, clearDirection, 2);
            if (probe.Count >= 2 && probe.All(c => battle.Board.IsWalkable(c) && battle.Occupancy.At(c) == null)) break;
            clearDirection = default;
        }
        Check(clearDirection != default, "melee thrust has a clear two-cell direction");
        Check(battle.TryCastCard(thrustCardId, thrustDirections.First(c => BattleRangeResolver.TryGetExactLineDirection(meleeThrustStart, c, out AxialHex d) && d == clearDirection), out string meleeThrustError),
            "melee thrust casts: " + meleeThrustError);
        int dashed = AxialHex.Distance(meleeThrustStart, battle.Selected.Coord);
        Check(dashed == 2, $"thrust dash uses the card's own movement count (moved {dashed})");
        Check(battle.Selected.MovesUsedThisTurn == movesBefore, "thrust dash does not consume the per-turn move budget");

        battle.Dispose();
        GD.Print("BATTLEFIELD_CARD_WEAPON_STACK_PASS: 近战武器保留卡牌扇形/突刺方式，远程武器覆盖为六方向直线，突刺位移=卡牌格数且不消耗移动额度");
    }

    /// <summary>第一层普通敌袭池：Low 档必须覆盖 F1-001 / F1-002 / F1-003；
    /// 各难度档实际选到的关卡都必须能加载（防止池行指向不存在的关卡配置）。</summary>
    private static void VerifyNormalCombatPool()
    {
        HexBoardData board = MapGeometry.Generate(HexBoardData.DefaultRadius, 20260922);
        MapBoardNode node = board.Nodes.First(x => x.Type == MapNodeType.NormalCombat);
        var run = new RunSaveData { MapState = new RunMapStateSave { Act = 1, NormalEncounterIndex = 0 } };
        // 次数 → 档位：第 1–3 场 Low、4–5 场 Mid、6 场起 High（NormalCombatRule.csv）。
        var bands = new (string Name, int FoughtCount)[] { ("Low", 0), ("Mid", 4), ("High", 5) };
        var pools = new List<(string Name, SortedSet<string> LevelIds)>();

        foreach ((string name, int foughtCount) in bands)
        {
            var ids = new SortedSet<string>();
            for (int seed = 0; seed < 120; seed++)
            {
                run.MapState.Seed = seed;
                run.MapState.NormalEncounterIndex = foughtCount;
                ResolvedMapContent content = WorldMapContentResolver.Resolve(1, node, board, run);
                if (content != null) ids.Add(content.Id);
            }
            pools.Add((name, ids));
        }

        SortedSet<string> low = pools.First(x => x.Name == "Low").LevelIds;
        Check(low.SetEquals(new[] { "F1-001", "F1-002", "F1-003" }),
            "act1 low pool = F1-001/002/003, actual: " + DescribePools(pools));
        foreach ((string name, SortedSet<string> ids) in pools)
        {
            foreach (string levelId in ids)
            {
                BattleLevelConfig level = BattleLevelCatalog.Load(levelId);
                Check(level.Objects.Count > 0, $"{name} pool level {levelId} loads with objects");
            }
        }
        GD.Print("BATTLEFIELD_NORMAL_POOL_PASS: 第一层普通敌袭池 " + DescribePools(pools));
    }

    private static string DescribePools(List<(string Name, SortedSet<string> LevelIds)> pools) =>
        string.Join("；", pools.Select(x => $"{x.Name}=" + (x.LevelIds.Count == 0 ? "（无池行）" : string.Join("/", x.LevelIds))));

    /// <summary>怪物初始化状态机制：关卡 CSV 的 InitialValue（`HP=` / `State=&lt;类型&gt;:&lt;层数&gt;`）应在开局写入怪物。</summary>
    private static void VerifyMonsterInitialStates()
    {
        string path = BattleLevelCatalog.ResolveMapPath("M-F1-001");
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var definition = BattleMapDefinition.Parse(file.GetAsText());
        definition.PlayerCharacterIds = new List<int> { 1002, 1003, 1004 };
        var level = new BattleLevelConfig
        {
            LevelId = "SMOKE-INITIAL-STATE", MapId = "M-F1-001", DropTableId = 2001,
            LevelType = "NormalCombat", Difficulty = "Low",
            Objects = new List<BattleLevelObject>
            {
                new("SMOKE-M01", "Monster", "3101", -4, 0) { InitialValue = "State=1:2|State=3:2" },
                new("SMOKE-M02", "Monster", "3102", -3, -1) { InitialValue = "State=Steal:1|HP=5" },
            },
        };
        BattleLevelCatalog.ApplyMonstersTo(definition, level);
        Check(definition.MonsterInitialValues.Count == 2, "level rows carry per-monster initial values");
        Check(definition.MonsterInstanceIds.Count == 2 && definition.MonsterInstanceIds[0] == "SMOKE-M01"
            && definition.MonsterInstanceIds[1] == "SMOKE-M02", "level rows carry per-monster instance keys");

        var battle = new BattlefieldSession(definition);
        var burning = battle.Occupancy.Placements.Values.First(x => x.Role == BattlefieldRole.Enemy && x.Coord == new AxialHex(-4, 0));
        var wounded = battle.Occupancy.Placements.Values.First(x => x.Role == BattlefieldRole.Enemy && x.Coord == new AxialHex(-3, -1));
        Check(StateSystem.TryGetStateStacks(burning.Unit, StateType.Vulnerable, out int vulnerable) && vulnerable == 2,
            "stackable initial state applied: Vulnerable=2");
        // 不可叠加状态（燃烧 = IsStackable FALSE）无论配置几层，落地都只按 1 层。
        Check(StateSystem.TryGetStateStacks(burning.Unit, StateType.Ignite, out int ignite) && ignite == 1,
            "non-stackable initial state collapses to one stack: Ignite=1");
        Check(wounded.Unit.HP == 5 && wounded.Unit.Max_HP > 5,
            $"initial HP applied: {wounded.Unit.HP}/{wounded.Unit.Max_HP}");
        // 关卡里可以直接用枚举名写状态：`State=Steal:1`（对应 通用State.csv 的 21 窃取）。
        Check(StateSystem.TryGetStateStacks(wounded.Unit, StateType.Steal, out int steal) && steal == 1,
            "enum-named initial state applied: Steal=1");
        battle.Dispose();
        GD.Print("BATTLEFIELD_INITIAL_STATE_PASS: 关卡 InitialValue 的 HP= 与 State=<类型>:<层数> 已在开局写入怪物");
    }

    /// <summary>图片资源目录自检（2026-09-22 迁移到 `Resources/Images/` 后新增）：
    /// 运行时实际加载的路径必须全部可解析，且旧根目录 `Images/` 不再存在，防止目录再次搬动后静默失效。</summary>
    private static void VerifyAssetPaths()
    {
        string[] required =
        {
            "res://Resources/Images/UI/IntentIcons/intent_move.png",
            "res://Resources/Images/UI/IntentIcons/intent_attack.png",
            "res://Resources/Images/UI/Story/Backgrounds/bg_night_road.png",
            "res://Resources/Images/Characters/Pixel/swordmaster_pixel_base.png",
            "res://Resources/Images/Characters/Pixel/swordmaster_pixel_modular_body.png",
            "res://Resources/Images/Characters/Pixel/swordmaster_pixel_action_sheet.png",
            "res://Resources/Images/Characters/Pixel/swordmaster_pixel_action_equipment_sheet.png",
            "res://Resources/Images/Characters/Pixel/swordmaster_pixel_idle_equipment_sheet.png",
            "res://Resources/Images/Characters/Pixel/equipment_sword.png",
            "res://Resources/Images/Characters/Pixel/equipment_shield.png",
            "res://Resources/Images/Characters/Pixel/equipment_bow.png",
            "res://Resources/Images/Characters/Pixel/equipment_tome.png",
            "res://Resources/Images/Characters/Pixel/equipment_two_hand_sword.png",
        };
        foreach (string path in required) Check(ResourceLoader.Exists(path), $"asset path resolves: {path}");
        Check(!ResourceLoader.Exists("res://Images/UI/IntentIcons/intent_move.png"), "legacy root Images/ folder is gone");
        GD.Print("BATTLEFIELD_ASSET_PATH_PASS: 图片资源统一位于 Resources/Images/（意图图标 + 剧情背景 + 像素角色与装备全部可解析）");
    }

    /// <summary>状态表 `EnumName` 列：每行都要与 `StateType` 枚举逐一对上（含 Steal=21），枚举成员也不能缺行。</summary>
    private static void VerifyStateEnumNames()
    {
        System.Collections.Generic.Dictionary<StateType, StateDefinition> states = LoadingSystem.StateDictionary;
        foreach (StateType type in Enum.GetValues<StateType>())
        {
            Check(states.TryGetValue(type, out StateDefinition definition), $"state CSV has a row for {type}");
            Check(definition.EnumName == type.ToString(), $"state row {type} declares EnumName {definition.EnumName}");
        }
        Check(states[StateType.Steal].Name == "窃取", "steal state keeps its display name");
        GD.Print("BATTLEFIELD_STATE_ENUM_PASS: 通用State.csv 的 EnumName 列与 StateType 枚举逐行一致（含 Steal=21）");
    }

    /// <summary>窃取金币账本（按实例）：攻击扣款（不足不扣）、同 ID 多只各自记账、结算只返还被击杀实例。</summary>
    private static void VerifyGoldStealLedger()
    {
        var run = new RunSaveData { Gold = 5 };
        Check(RunGoldLedger.Steal(run, "F1-006-M01", 3115, 3) == 3 && run.Gold == 2, "steal deducts the configured amount");
        Check(RunGoldLedger.Steal(run, "F1-006-M01", 3115, 3) == 0 && run.Gold == 2, "insufficient gold steals nothing (no partial)");
        Check(RunGoldLedger.Steal(run, "F1-006-M02", 3115, 1) == 1 && run.Gold == 1, "same monster id on another instance records separately");
        Check(RunGoldLedger.Find(run, "F1-006-M01").Amount == 3 && RunGoldLedger.Find(run, "F1-006-M02").Amount == 1,
            "ledger keeps one entry per monster instance");

        int refunded = RunGoldLedger.RefundDefeated(run, new[] { "F1-006-M01" });
        Check(refunded == 3 && run.Gold == 4, $"defeated instance refunds its own loot (gold {run.Gold})");
        Check(RunGoldLedger.Find(run, "F1-006-M01") == null && RunGoldLedger.Find(run, "F1-006-M02") != null,
            "refund clears only the killed instance");

        RunGoldLedger.Clear(run);
        Check(run.StolenGoldFromMonsters.Count == 0 && run.Gold == 4, "clearing keeps already refunded gold");
        GD.Print("BATTLEFIELD_GOLD_LEDGER_PASS: 窃取金币账本（按怪物实例分开记账、不足不扣、只返还被击杀实例）");
    }

    /// <summary>端到端：带 `Steal` 状态的怪物攻击命中玩家 → 触发窃取钩子并按层数记入金币账本（每次攻击一次）。</summary>
    private static void VerifyMonsterStealTrigger()
    {
        string path = BattleLevelCatalog.ResolveMapPath("M-F1-001");
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var definition = BattleMapDefinition.Parse(file.GetAsText());
        definition.PlayerCharacterIds = new List<int> { 1002, 1003, 1004 };
        definition.MonsterIds = new List<int> { 3101 };          // 相邻单体撞击，便于直接命中
        definition.MonsterInitialValues = new List<string> { "State=Steal:2" };
        var battle = new BattlefieldSession(definition);

        // 把怪物搬到 1 号玩家相邻格，保证本次意图能命中。
        BattleUnitPlacement monster = battle.Occupancy.Placements.Values.First(x => x.Role == BattlefieldRole.Enemy);
        AxialHex playerCoord = battle.Occupancy.Placements[battle.PlayerIds[0]].Coord;
        AxialHex adjacent = BattleRangeResolver.Neighbors(playerCoord).First(c => battle.Board.IsWalkable(c) && battle.Occupancy.At(c) == null);
        battle.Occupancy.CommitMove(monster, adjacent);

        // 固定为"相邻撞击"（Intention1 = Damage）：3101 有两个意图，随机可能抽到"加易伤"而本次没有攻击。
        ((MonsterInstance)monster.Unit).SetSelectedIntention(0, new[] { new[] { (int)EffectType.Damage } });

        var run = new RunSaveData { Gold = 5 };
        var stolenPerAttack = new List<int>();
        string instanceKey = battle.GetMonsterInstanceKey(monster.UnitId);
        Check(!string.IsNullOrWhiteSpace(instanceKey), "monster placement exposes a stable instance key");
        Check(instanceKey == "unit-" + monster.UnitId || instanceKey.Length > 0, "instance key is usable for per-instance bookkeeping");
        battle.MonsterHitPlayer += (attacker, _) =>
        {
            int stacks = StateSystem.TryGetStateStacks(attacker.Unit, StateType.Steal, out int perAttack) ? perAttack : 0;
            stolenPerAttack.Add(RunGoldLedger.Steal(run, battle.GetMonsterInstanceKey(attacker.UnitId), (attacker.Unit as MonsterInstance)?.id ?? 0, stacks));
        };

        battle.EndCurrentTurn();
        while (battle.Phase == BattlefieldSession.BattlePhase.Monsters && battle.ExecuteNextMonsterTurnStep()) { }

        Check(stolenPerAttack.Count == 1 && stolenPerAttack[0] == 2,
            $"steal hook fires once per attack with the configured stacks (fired {stolenPerAttack.Count})");
        StolenGoldEntry entry = RunGoldLedger.Find(run, instanceKey);
        Check(run.Gold == 3 && entry != null && entry.Amount == 2 && entry.MonsterId == 3101,
            $"stolen gold recorded under the instance key (gold {run.Gold})");
        battle.Dispose();
        GD.Print("BATTLEFIELD_MONSTER_STEAL_PASS: 带 Steal 的怪物攻击命中玩家 → 按实例键触发窃取并记账");
    }

    private static void VerifyFirstFormalLevel()
    {
        BattleLevelConfig level = BattleLevelCatalog.Load("F1-001");
        // 数量从关卡表读，不写死（F1-001 的怪物编组会随关卡设计调整）。
        int monsterCount = level.Objects.Count(x => x.ObjectType == "Monster");
        Check(level.MapId == "M-F1-001" && monsterCount > 0 && level.Objects.All(x => x.ObjectType == "Monster"),
            $"first formal level parsed ({monsterCount} monsters)");
        string mapPath = BattleLevelCatalog.ResolveMapPath(level.MapId);
        using var file = FileAccess.Open(mapPath, FileAccess.ModeFlags.Read);
        Check(file != null, "first formal map opened");
        BattleMapDefinition map = BattleMapDefinition.Parse(file.GetAsText());
        map.PlayerCharacterIds = new List<int> { 1002, 1003, 1004 };
        map.MonsterIds = level.Objects.Select(x => int.Parse(x.DefinitionId)).ToList();
        map.FixedEnemySpawnCoords = level.Objects.Select(x => new HexCoordinateData { Q = x.Q, R = x.R }).ToList();
        map.ObjectPlacements.Clear(); map.RandomItemCount = 0; map.RandomItemDefinitions.Clear();
        var formal = new BattlefieldSession(map);
        Check(formal.Occupancy.Placements.Values.Count(x => x.Role == BattlefieldRole.Enemy) == monsterCount
            && formal.Generated.EnemyCoords.SequenceEqual(level.Objects.Select(x => new AxialHex(x.Q, x.R))), "first formal level fixed monsters deployed");
        formal.Dispose();
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException(name); }
}
