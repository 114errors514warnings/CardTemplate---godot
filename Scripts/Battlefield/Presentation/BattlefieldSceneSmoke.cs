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
            Check(session.Selected.Unit.Energy == 3 && session.Selected.MovesUsedThisTurn == 0, "turn reset");
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
            AxialHex thrustTarget = session.GetCastCandidates(thrustCard.CardId).First(cell =>
                AxialHex.Distance(session.Selected.Coord, cell) == 2 &&
                session.GetAffectedCells(thrustCard.CardId, cell).All(path => session.Board.IsWalkable(path) && session.Occupancy.At(path) == null));
            AxialHex thrustStart = session.Selected.Coord;
            Check(session.TryCastCard(thrustCard.CardId, thrustTarget, out castError), "thrust without target: " + castError);
            Check(session.Selected.Coord != thrustStart && AxialHex.Distance(thrustStart, session.Selected.Coord) == 2, "thrust moves without enemy");
            Card attackCard = session.GetHand(session.SelectedId).FirstOrDefault(x => x.CardId == 11001001);
            Check(attackCard != null, "spatial attack card loaded");
            var attackCell = BattleHexLayout.Neighbors(session.Selected.Coord).First(x => session.Occupancy.CanEnter(x));
            var attackEnemy = session.Occupancy.Placements.Values.First(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active);
            session.Occupancy.CommitMove(attackEnemy, attackCell);
            int targetHp = attackEnemy.Unit.HP;
            Check(session.TryCastCard(attackCard.CardId, attackCell, out castError), "spatial damage pipeline: " + castError);
            Check(attackEnemy.Unit.HP < targetHp, "spatial card damages only validated target");
            VerifyRunBattleInjection();
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
        string path = LoadingSystem.GetFilePathByKey("Data.Battlefield.Foundation");
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
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException(name); }
}
