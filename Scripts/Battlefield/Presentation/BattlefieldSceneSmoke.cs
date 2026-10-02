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
            VerifyHealthBarPresentation();
            VerifyMonsterInitialStates();
            VerifyLevelConfigs();
            VerifyMonsterTableColumns();
            VerifyItemTables();
            VerifyBattleRules();
            VerifyStateEnumNames();
            VerifyGoldStealLedger();
            VerifyMonsterStealTrigger();
            VerifyMonsterStateIntentTargets();
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
            Check(session.PlayerIds.Skip(1).All(view.HasCharacterRig), "swordmaster and elf rigs bound to battlefield units");
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
            if (OS.GetCmdlineUserArgs().Contains("--battlefield-health-capture"))
            {
                // 只截图用：把一只怪打到 60% 上下，让「当前/上限 + 部分填充 + 深色底条」同框；
                // 截完立刻还原（不影响后续任何断言），产物 Tests/battlefield-health-bars.png。
                var showcase = session.Occupancy.Placements.Values.First(x => x.Role == BattlefieldRole.Enemy);
                int showcaseHp = showcase.Unit.HP;
                showcase.Unit.HP = Math.Max(1, showcaseHp * 3 / 5);
                view.CenterSelected(); view.QueueRedraw();
                await scene.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Error healthCapture = scene.GetViewport().GetTexture().GetImage().SavePng("res://Tests/battlefield-health-bars.png");
                Check(healthCapture == Error.Ok, "health bar capture saved");
                showcase.Unit.HP = showcaseHp;
                view.QueueRedraw();
            }

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
            GD.Print("BATTLEFIELD_SMOKE_PASS: deployment, CSV, click, hover, pan, fixed scale, movement, equipment, items, card pipeline, thrust, burst self exclusion, spatial damage, monster minion column, item tables, monster state target, monster turn, states, health text, victory");
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
        var run = new RunSaveData { MapState = new RunMapStateSave { Act = 1 } };
        // 次数 → 档位：第 1–3 场 Low、4–5 场 Mid、6 场起 High（NormalCombatRule.csv）。
        var bands = new (string Name, int FoughtCount)[] { ("Low", 0), ("Mid", 4), ("High", 5) };
        var pools = new List<(string Name, SortedSet<string> LevelIds)>();

        foreach ((string name, int foughtCount) in bands)
        {
            var ids = new SortedSet<string>();
            for (int seed = 0; seed < 120; seed++)
            {
                run.MapState.Seed = seed;
                // 分档计数按层独立（P2-11）：写 `NormalEncounterCounts[Act]`。
                // 旧字段 `NormalEncounterIndex` 要经 `MigrateLegacyNormalEncounterCount()` 才会生效，
                // 直接写它会让三个档位全部解析成 Low（本烟测此前就是这样静默通过的）。
                run.MapState.NormalEncounterCounts[1] = foughtCount;
                ResolvedMapContent content = WorldMapContentResolver.Resolve(1, node, board, run);
                if (content != null) ids.Add(content.Id);
            }
            pools.Add((name, ids));
        }

        SortedSet<string> low = pools.First(x => x.Name == "Low").LevelIds;
        Check(low.SetEquals(new[] { "F1-001", "F1-002", "F1-003" }),
            "act1 low pool = F1-001/002/003, actual: " + DescribePools(pools));
        SortedSet<string> mid = pools.First(x => x.Name == "Mid").LevelIds;
        Check(mid.SetEquals(new[] { "F1-004", "F1-005", "F1-006" }),
            "act1 mid pool = F1-004/005/006, actual: " + DescribePools(pools));
        SortedSet<string> high = pools.First(x => x.Name == "High").LevelIds;
        Check(high.SetEquals(new[] { "F1-007", "F1-008", "F1-009" }),
            "act1 high pool = F1-007/008/009, actual: " + DescribePools(pools));
        // 档位池行必须存在：2026-10 之前 `LevelPool.csv` 没有 `NormalCombat/High` 行，
        // 第 6 场起的每个普通敌袭格都会解析成 null 并静默通过（高等档普通敌袭是死内容）。
        foreach ((string name, SortedSet<string> ids) in pools)
        {
            Check(ids.Count > 0, $"act1 {name} normal-combat pool has at least one level row: " + DescribePools(pools));
        }

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

    /// <summary>
    /// 全关卡配置自检（2026-09-28 新增）：`LevelIndex.csv` 里的**每个**关卡都按运行局真实路径走一遍
    /// （`BattleLevelCatalog.Load` → `ApplyMonstersTo` → `BattlefieldSession`），保证「地图能开、怪能建、`InitialValue` 能解析」。
    /// 起因：`F1-H-001`（「危」节点）的 `InitialValue` 列误写成字面量 `None`（表格导出的空值），
    /// 建怪时抛错 → 战场初始化失败 → 玩家看到的是「只剩 UI 框、没有格子和单位」的空白战场；
    /// 原烟测只建 `F1-001` 且不走 `ApplyMonstersTo`，所以这类配错漏到了实机。
    /// </summary>
    private static void VerifyLevelConfigs()
    {
        var indexRows = LoadCsv.LoadCSVDataLines("res://DataBase/Level/LevelIndex.csv")
            .Select(LoadCsv.ParseCSVFields)
            .Where(x => x.Length >= 2 && x[1].StartsWith("res://", StringComparison.Ordinal))
            .ToList();
        Check(indexRows.Count > 0, "level index lists levels with config paths");

        var builtLevelIds = new List<string>();
        int monsterTotal = 0;
        foreach (string[] row in indexRows)
        {
            string levelId = row[0];
            BattleLevelConfig level = BattleLevelCatalog.Load(levelId);
            Check(level.Objects.Count > 0, $"level {levelId} declares objects");

            // ① 关卡 CSV 的任何字段都不允许出现字面量 `None`：空值必须留空。
            //    （该字面量来自表格导出；`InitialValue=None` 会在建怪时抛「条目必须是 Key=Value」。）
            foreach (string raw in LoadCsv.LoadCSVDataLines(row[1]))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                foreach (string field in LoadCsv.ParseCSVFields(raw))
                    Check(!string.Equals(field.Trim(), "None", StringComparison.OrdinalIgnoreCase),
                        $"{levelId} 的字段写成了字面量 None（空值请留空）：{raw}");
            }

            // ② 按运行局路径建场：怪物 ID / 出生点 / 初始值 / 实例键都走 `ApplyMonstersTo`，
            //    建会话这一步会真正解析 `InitialValue`（= 玩家点进节点时的同一入口）。
            string mapPath = BattleLevelCatalog.ResolveMapPath(level.MapId);
            using (var file = FileAccess.Open(mapPath, FileAccess.ModeFlags.Read))
            {
                Check(file != null, $"level {levelId} map opened ({level.MapId})");
                BattleMapDefinition definition = BattleMapDefinition.Parse(file.GetAsText());
                definition.PlayerCharacterIds = new List<int> { 1002, 1003, 1004 };
                BattleLevelCatalog.ApplyMonstersTo(definition, level);
                int monsters = definition.MonsterIds.Count;
                Check(monsters > 0 && definition.MonsterInitialValues.Count == monsters
                    && definition.MonsterInstanceIds.Count == monsters,
                    $"level {levelId} carries one initial value / instance key per monster");
                using var battle = new BattlefieldSession(definition);
                Check(battle.Occupancy.Placements.Values.Count(x => x.Role == BattlefieldRole.Enemy) == monsters,
                    $"level {levelId} deploys {monsters} monsters");
                monsterTotal += monsters;
            }
            builtLevelIds.Add(levelId);
        }
        Check(builtLevelIds.Count == indexRows.Count, $"every indexed level builds a battlefield ({builtLevelIds.Count}/{indexRows.Count})");
        GD.Print($"BATTLEFIELD_LEVEL_CONFIG_PASS: LevelIndex 的 {builtLevelIds.Count} 个关卡按运行局路径建场成功（含 InitialValue 解析），共 {monsterTotal} 只怪物");
    }
    /// <summary>
    /// 物品四表（2026-10-02 批 A / 代码需求清单 P1-4）：材料 / 道具 / 食物 / 配方的**真实加载**接线 ——
    /// 表头校验、ID 去重、效果文本与寿命轴解析、跨表引用（配方结果与输入）、掉落表引用，
    /// 以及 2026-10-02 的两条口径：① 材料不参与烹饪（放行配方只允许 Food 输入）；② 寿命不填默认 1（下一场战斗）。
    /// </summary>
    private static void VerifyItemTables()
    {
        LoadingSystem.LoadItemTablesByKey();

        int materials = LoadingSystem.MaterialDictionary.Count;
        int items = LoadingSystem.ItemDictionary.Count;
        int foods = LoadingSystem.FoodDictionary.Count;
        int recipes = LoadingSystem.FoodRecipeDictionary.Count;
        Check(materials == 16, $"material table rows (实际 {materials})");
        Check(items == 8, $"item table rows (实际 {items})");
        Check(foods == 8, $"food table rows (实际 {foods})");
        Check(recipes == 12, $"recipe rows (实际 {recipes})");

        int enabled = LoadingSystem.FoodRecipeDictionary.Values.Count(x => x.Enabled);
        int disabled = recipes - enabled;
        Check(enabled == 5 && disabled == 7, $"放行 5 条食物升级配方 / 禁用 7 条材料配方（实际 {enabled} / {disabled}）");
        Check(LoadingSystem.FoodRecipeDictionary.Values.Where(x => x.Enabled)
                .All(x => x.Inputs.All(i => i.Kind == RecipeInputKind.Food)),
            "2026-10-02 口径：放行配方只允许 Food 输入（材料暂不参与烹饪）");

        Check(LoadingSystem.FoodDictionary.Values.SelectMany(x => x.Effects)
                .All(e => e.DurationKind != FoodEffectDurationKind.None && e.DurationValue >= 1),
            "每条食物效果都带寿命轴且数量 ≥ 1");
        ItemEffectSpecParser.TryParseDuration(string.Empty, out FoodEffectDurationKind blankKind, out int blankValue, "smoke");
        Check(blankKind == FoodEffectDurationKind.BattleCount && blankValue == 1,
            "寿命列留空 = BattleCount:1（下一场战斗）");

        Check(ItemNameResolver.DisplayMaterial(101) == "药草", $"掉落表 Material 101 → 药草（实际 {ItemNameResolver.DisplayMaterial(101)}）");
        Check(ItemNameResolver.DisplayFood(402) == "香草炖菜", "食物 402 → 香草炖菜");

        GD.Print($"BATTLEFIELD_ITEM_TABLES_PASS: 材料 {materials} / 道具 {items} / 食物 {foods} / 配方 {recipes}" +
            $"（放行 {enabled} · 材料通道禁用 {disabled}）；掉落表 Material 101 → {ItemNameResolver.DisplayMaterial(101)}");
    }



    /// <summary>
    /// `Monster.csv` 的 `IsMinion`（是否为爪牙）列：真实表按表头定位并整表可解析（意图列不受新列影响），
    /// 且 `1` 会被解析成 `Monster.IsMinion` 并能被 `MonsterInstance` 继承（折损分流的数据载体，玩法 §7.4 / §7.5）。
    /// </summary>
    private static void VerifyMonsterTableColumns()
    {
        string path = LoadingSystem.GetFilePathByKey(LoadingSystem.MonsterCsvPathKey);
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        Check(file != null, "monster CSV reachable");
        string[] lines = file.GetAsText().Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Check(lines.Length > 1, "monster CSV has data rows");

        int isMinionColumn = MonsterCsvSchema.ResolveIsMinionColumnIndex(LoadCsv.ParseCSVFields(lines[0]));
        Check(isMinionColumn >= 0, "Monster.csv declares the IsMinion column");

        bool everyValueRecognized = true;
        int minionRows = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            if (!MonsterCsvSchema.TryParseIsMinion(LoadCsv.ParseCSVFields(lines[i]), isMinionColumn, out bool isMinion, out _))
                everyValueRecognized = false;
            if (isMinion) minionRows++;
        }
        Check(everyValueRecognized, "every Monster.csv IsMinion value is a recognized token");

        Monster[] parsed = LoadMonsterCsv.ParseMonstersFromLines(lines);
        Check(parsed.Length == lines.Length - 1 && parsed.Length == LoadingSystem.MonsterDictionary.Count,
            "real monster table parses through the same entry as the loader");
        Check(parsed.First(x => x.id == 3101).Table.Length == 2 && parsed.First(x => x.id == 3003).Table[0].Length == 3,
            "appended IsMinion column does not disturb intention parsing");

        Monster[] probe = LoadMonsterCsv.ParseMonstersFromLines(new[]
        {
            "id,Name,MAX_HP,Ini_Attack,Ini_Defend,Intention1,Intention2,Intention3,Intention4,Intention5,Intention6,Intention7,Intention8,Intention9,Intention10,IsMinion",
            "9001,爪牙探针,8,2,1,1,2,,,,,,,,,1",
        });
        Check(probe.Length == 1 && probe[0].IsMinion && probe[0].MAX_HP == 8 && probe[0].Table.Length == 2,
            "IsMinion=1 parses into Monster.IsMinion");
        Check(new MonsterInstance(probe[0]).IsMinion, "MonsterInstance inherits IsMinion");
        GD.Print($"BATTLEFIELD_MONSTER_TABLE_PASS: Monster.csv 的 IsMinion 列 = 第 {isMinionColumn + 1} 列，当前标记为爪牙的怪物 {minionRows} 只");
    }

    /// <summary>关卡级战斗规则（`BattleRule` 列）：枚举解析、规则挂载、意图批次节奏，以及逃跑行为的离场。</summary>
    private static void VerifyBattleRules()
    {
        // ① 关卡 CSV 第 13 列 → 关卡级变量。
        string[] rows =
        {
            "M-F1-001,2001,NormalCombat,Mid,SRB-M01,Monster,3115,-3,0,1;0,State=Steal:1,,BanditEscalation",
            "M-F1-001,2001,NormalCombat,Mid,SRB-M02,Monster,3115,-3,-1,1;0,State=Steal:1,,BanditEscalation",
        };
        BattleLevelConfig ruleLevel = BattleLevelCatalog.Parse("SMOKE-RULE", rows);
        Check(ruleLevel.Rules.Count == 1 && ruleLevel.Rules[0] == BattleRuleKind.BanditEscalation,
            "level BattleRule column becomes a level variable");
        // 真实关卡文件（F1-006 劫匪团伙）走的是同一列，必须能被读到。
        BattleLevelConfig banditLevel = BattleLevelCatalog.Load("F1-006");
        Check(banditLevel.Rules.Count == 1 && banditLevel.Rules[0] == BattleRuleKind.BanditEscalation,
            "F1-006 declares the bandit rule in its own CSV");
        Check(BattleLevelCatalog.Parse("SMOKE-LEGACY", new[]
        {
            "M-F1-001,2001,NormalCombat,Low,SL-M01,Monster,3101,-4,0,,,",
        }).Rules.Count == 0, "level without BattleRule column stays rule-free");

        // ② 意图批次节奏：第 1–2 个怪物回合低攻列，第 3 个必定高攻列。
        string path = BattleLevelCatalog.ResolveMapPath("M-F1-001");
        using (var file = FileAccess.Open(path, FileAccess.ModeFlags.Read))
        {
            var definition = BattleMapDefinition.Parse(file.GetAsText());
            definition.PlayerCharacterIds = new List<int> { 1002, 1003, 1004 };
            BattleLevelCatalog.ApplyMonstersTo(definition, ruleLevel);
            Check(definition.Rules.Count == 1, "battle definition carries level rules");

            using var battle = new BattlefieldSession(definition);
            Check(battle.Rules.Count == 1, "session builds the rule implementation");
            var bandits = battle.Occupancy.Placements.Values
                .Where(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active).ToArray();
            Check(bandits.Length == 2, "fixture deploys the two bandits");
            Check(bandits.All(x => x.Unit is MonsterInstance m && m.SelectedIntentionIndex == BanditEscalationRule.LowIntentIndex),
                "batch 1 uses the low-attack intention");

            RunMonsterPhase(battle);
            Check(bandits.All(x => x.Presence != BattlefieldPresence.Active ||
                x.Unit is MonsterInstance m && m.SelectedIntentionIndex == BanditEscalationRule.LowIntentIndex),
                "batch 2 uses the low-attack intention");
            RunMonsterPhase(battle);
            var alive = bandits.Where(x => x.Presence == BattlefieldPresence.Active).ToArray();
            // 第 3 批次不再出低攻：逃跑优先（规格："若非逃跑，必定高攻"），因此允许 High 或 Flee。
            Check(alive.Length > 0 && alive.All(x => x.Unit is MonsterInstance m &&
                    m.SelectedIntentionIndex != BanditEscalationRule.LowIntentIndex),
                "batch 3 leaves the low-attack intention (high attack, or flee when the trigger already fired)");
            Check(alive.All(x => x.Unit is MonsterInstance m &&
                    (m.SelectedIntentionIndex == BanditEscalationRule.HighIntentIndex ||
                     m.SelectedIntentionIndex == BanditEscalationRule.FleeIntentIndex)),
                "batch 3 uses the high-attack or flee intention only");
        }

        // ③ 逃跑行为：选中逃跑意图的劫匪站在边界格 → 离场，而不是攻击。
        using (var file = FileAccess.Open(path, FileAccess.ModeFlags.Read))
        {
            var definition = BattleMapDefinition.Parse(file.GetAsText());
            definition.PlayerCharacterIds = new List<int> { 1002, 1003, 1004 };
            BattleLevelCatalog.ApplyMonstersTo(definition, ruleLevel);

            using var battle = new BattlefieldSession(definition);
            var runner = battle.Occupancy.Placements.Values.First(x => x.Role == BattlefieldRole.Enemy);
            int radius = Math.Max(1, definition.Radius);
            AxialHex boundary = battle.Board.Cells.Keys
                .Where(cell => EnemyIntentPlanner.IsOnMapBoundary(cell, radius) && battle.Occupancy.CanEnter(cell))
                .OrderBy(cell => cell.Q).ThenBy(cell => cell.R).First();
            battle.Occupancy.CommitMove(runner, boundary);
            Check(EnemyIntentPlanner.IsOnMapBoundary(runner.Coord, radius), "runner stands on the map boundary");

            var runnerMonster = (MonsterInstance)runner.Unit;
            runnerMonster.SetSelectedIntention(BanditEscalationRule.FleeIntentIndex,
                runnerMonster.Table[BanditEscalationRule.FleeIntentIndex]);
            Check(BattleEnemyIntentCatalog.Resolve(runnerMonster).Behavior == EnemyIntentBehavior.Flee,
                "flee intention resolves as the Flee behavior");

            RunMonsterPhase(battle);
            Check(runner.Presence == BattlefieldPresence.Departed,
                $"fleeing bandit leaves via Departed (presence {runner.Presence})");
        }

        GD.Print("BATTLEFIELD_BATTLE_RULE_PASS: 关卡 BattleRule 列挂载规则 → 低攻/门槛高攻节奏 + 逃跑离场（Departed）");
    }

    /// <summary>跑到怪物阶段结束（含下一轮意图准备）。</summary>
    private static void RunMonsterPhase(BattlefieldSession battle)
    {
        battle.EndCurrentTurn();
        while (battle.Phase == BattlefieldSession.BattlePhase.Monsters && battle.ExecuteNextMonsterTurnStep()) { }
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
            "res://Resources/Images/Characters/Pixel/isera_pixel_action_sheet_v3.png",
            "res://Resources/Images/Characters/Portraits/isera_portrait_v3.png",
            "res://Resources/Images/Characters/Pixel/swordmaster_bow_action_sheet_v2.png",
            "res://Resources/Images/Characters/Pixel/swordmaster_tome_action_sheet_v2.png",
            "res://Resources/Images/Characters/Pixel/swordmaster_death_pose_v2.png",
            "res://Resources/Images/Characters/Pixel/fx_bow_arrow_trail.png",
            "res://Resources/Images/Characters/Pixel/fx_tome_cast_rune.png",
            "res://Resources/Images/Characters/Rigs/Isera/isera_parts_atlas_v1.png",
            "res://Resources/Images/Characters/Rigs/Isera/isera_continuous_skin_v1.png",
        };
        foreach (string path in required) Check(ResourceLoader.Exists(path), $"asset path resolves: {path}");
        Check(!ResourceLoader.Exists("res://Images/UI/IntentIcons/intent_move.png"), "legacy root Images/ folder is gone");
        GD.Print("BATTLEFIELD_ASSET_PATH_PASS: 图片资源统一位于 Resources/Images/（意图图标 + 剧情背景 + 像素角色与装备全部可解析）");
    }

    /// <summary>
    /// 血条生命数值文本口径（血条案 §二 / §四，2026-10-01 用户口径）：玩家与怪物**同格式** `当前/上限`，颜色**纯黑**。
    /// 断言的是口径本身（表现层不跑渲染断言；黑字 + 描边好不好看由出图人工看）。
    /// </summary>
    private static void VerifyHealthBarPresentation()
    {
        Check(BattlefieldView.FormatHealthText(12, 40) == "12/40", "health text shows current/max");
        Check(BattlefieldView.FormatHealthText(40, 40) == "40/40", "full-health text shows current/max");
        Check(BattlefieldView.FormatHealthText(0, 30) == "0/30", "defeated text shows current/max");
        Check(BattlefieldView.HealthTextColor == Colors.Black, "health text is pure black");
        GD.Print("BATTLEFIELD_HEALTH_TEXT_PASS: 血条生命数值 = `当前/上限`（玩家与怪物同格式）、纯黑字 + 浅色细描边");
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

    /// <summary>窃取金币账本（按实例）：攻击扣款（余额不足扣到归零）、同 ID 多只各自记账、结算只返还被击杀实例。</summary>
    private static void VerifyGoldStealLedger()
    {
        var run = new RunSaveData { Gold = 5 };
        Check(RunGoldLedger.Steal(run, "F1-006-M01", 3115, 3) == 3 && run.Gold == 2, "steal deducts the configured amount");
        // 口径（2026-09-26）：余额不足 → 扣到归零（可部分扣除），记账 = 实扣额。
        Check(RunGoldLedger.Steal(run, "F1-006-M01", 3115, 3) == 2 && run.Gold == 0, "insufficient gold is deducted down to zero");
        Check(RunGoldLedger.Steal(run, "F1-006-M02", 3115, 1) == 0 && run.Gold == 0, "an empty purse gives another instance nothing");
        Check(RunGoldLedger.Find(run, "F1-006-M01").Amount == 5 && RunGoldLedger.Find(run, "F1-006-M02") == null,
            "ledger keeps one entry per monster instance (partial included)");

        int refunded = RunGoldLedger.RefundDefeated(run, new[] { "F1-006-M01" });
        Check(refunded == 5 && run.Gold == 5, $"defeated instance refunds its own loot (gold {run.Gold})");
        Check(RunGoldLedger.Find(run, "F1-006-M01") == null, "refund clears the killed instance");

        RunGoldLedger.Clear(run);
        Check(run.StolenGoldFromMonsters.Count == 0 && run.Gold == 5, "clearing keeps already refunded gold");
        GD.Print("BATTLEFIELD_GOLD_LEDGER_PASS: 窃取金币账本（按怪物实例分开记账、余额不足扣到归零、只返还被击杀实例）");
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

    /// <summary>怪物状态意图的目标：`3;&lt;EffectTargetType&gt;;&lt;状态&gt;;&lt;层数&gt;` 必须落在**角色**身上。
    /// 六边形战场按索敌目标结算（最近角色），旧卡牌战斗（`MonsterIntentionService`）按 `EffectTargetType`
    /// 结算（`0` = Auto = 随机一名角色）；两条路径都不允许把弱化加回怪物自己。</summary>
    private static void VerifyMonsterStateIntentTargets()
    {
        string path = BattleLevelCatalog.ResolveMapPath("M-F1-001");
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var definition = BattleMapDefinition.Parse(file.GetAsText());
        definition.PlayerCharacterIds = new List<int> { 1002, 1003, 1004 };
        definition.MonsterIds = new List<int> { 3118 };   // 意图① `3;0;2;2` = 对角色施加 2 层虚弱（狂暴压制）
        var battle = new BattlefieldSession(definition);

        BattleUnitPlacement monster = battle.Occupancy.Placements.Values.First(x => x.Role == BattlefieldRole.Enemy);
        AxialHex playerCoord = battle.Occupancy.Placements[battle.PlayerIds[0]].Coord;
        AxialHex adjacent = BattleRangeResolver.Neighbors(playerCoord).First(c => battle.Board.IsWalkable(c) && battle.Occupancy.At(c) == null);
        battle.Occupancy.CommitMove(monster, adjacent);

        var instance = (MonsterInstance)monster.Unit;
        Check(instance.Table.Length == 2 && instance.Table[0][0][0] == (int)EffectType.AddState,
            "3118 declares a state-only intention first");
        instance.SetSelectedIntention(0, instance.Table[0]);
        RunMonsterPhase(battle);

        int monsterWeak = StateSystem.TryGetStateStacks(instance, StateType.Weak, out int ownWeak) ? ownWeak : 0;
        int playerWeak = battle.PlayerIds.Sum(id =>
            StateSystem.TryGetStateStacks(battle.Occupancy.Placements[id].Unit, StateType.Weak, out int stacks) ? stacks : 0);
        Check(monsterWeak == 0, $"state intention must not debuff the monster itself (weak {monsterWeak})");
        // `3;0;2;2` 施加 2 层虚弱；本烟测跑到怪物阶段结束（`RunMonsterPhase` 末尾会开下一轮玩家回合），
        // 玩家回合开始时虚弱按 OnTurnStart/Flat 1 衰减 1 层 → 剩 1 层即证明「2 层确实加到了角色身上」。
        Check(playerWeak == 1, $"state intention lands on a player (total weak {playerWeak})");
        battle.Dispose();
        GD.Print("BATTLEFIELD_MONSTER_STATE_TARGET_PASS: `3;0;<状态>;<层数>` 的弱化落在角色身上（怪物自身不加状态）");
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
