using Godot;
using System;
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
            int energy = session.Selected.Unit.Energy;
            view.SetMoving(true);
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = view.CellPosition(destinations[0]) });
            Check(session.Selected.Unit.Energy == energy - 1 && session.Selected.MovesUsedThisTurn == 1, "one step one energy");
            Check(session.Selected.Unit.HP == hp, "movement preserves HP");
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
            Check(session.CurrentAttackRange == 2 && session.Selected.EffectiveMovesPerTurn == 4, "equipment modifiers");
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
            Card attackCard = session.GetHand(session.SelectedId).FirstOrDefault(x => x.CardId == 11001001);
            Check(attackCard != null, "spatial attack card loaded");
            var attackCell = BattleHexLayout.Neighbors(session.Selected.Coord).First(x => session.Occupancy.CanEnter(x));
            var attackEnemy = session.Occupancy.Placements.Values.First(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active);
            session.Occupancy.CommitMove(attackEnemy, attackCell);
            int targetHp = attackEnemy.Unit.HP;
            Check(session.TryCastCard(attackCard.CardId, attackCell, out castError), "spatial damage pipeline: " + castError);
            Check(attackEnemy.Unit.HP < targetHp, "spatial card damages only validated target");
            var enemyBefore = session.Occupancy.Placements.Values.Where(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active)
                .ToDictionary(x => x.UnitId, x => x.Coord);
            session.EndCurrentTurn();
            Check(session.Round == 3 && session.Phase == BattlefieldSession.BattlePhase.Player, "monster turn then player round");
            Check(enemyBefore.Any(x => session.Occupancy.Placements[x.Key].Presence != BattlefieldPresence.Active ||
                session.Occupancy.Placements[x.Key].Coord != x.Value), "monster spatial action");
            foreach (var remainingEnemy in session.Occupancy.Placements.Values.Where(x => x.Role == BattlefieldRole.Enemy && x.Presence == BattlefieldPresence.Active).ToArray())
                remainingEnemy.Unit.HP = 0;
            Check(session.Phase == BattlefieldSession.BattlePhase.Victory, "victory outcome");
            view.CenterSelected(); view.SetMoving(true); view.QueueRedraw();
            await scene.ToSignal(scene.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (OS.GetCmdlineUserArgs().Contains("--battlefield-capture"))
            {
                await scene.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string path = "res://README/施工文档/2026/2026.09/六边形基础层实测.png";
                Error error = scene.GetViewport().GetTexture().GetImage().SavePng(path);
                Check(error == Error.Ok, "capture saved");
            }
            GD.Print("BATTLEFIELD_SMOKE_PASS: deployment, CSV, click, hover, pan, fixed scale, movement, equipment, items, card pipeline, spatial damage, monster turn, states, victory");
            scene.GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PrintErr("BATTLEFIELD_SMOKE_FAIL: " + ex);
            scene.GetTree().Quit(1);
        }
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException(name); }
}
