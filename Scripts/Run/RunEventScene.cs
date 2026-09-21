using Godot;
using System.Linq;

public partial class RunEventScene : Control
{
	public event System.Action ContentFinished;
	public event System.Action<string> LevelRequested;
    public const string MapScenePath = "res://Scenes/Map/MapScene.tscn";
    public const string RunBattleScenePath = "res://Scenes/Run/RunBattleScene.tscn";
    public override void _Ready()
    {
        var run = RunSession.Instance;
        if (run?.Current == null || run.Current.PendingContentType != "Event") { GetTree().ChangeSceneToFile(MapScenePath); return; }
        StoryEventConfig config;
        try { config = StoryEventCatalog.Load(run.Current.PendingContentId); }
        catch { GetTree().ChangeSceneToFile(MapScenePath); return; }
        var packed = GD.Load<PackedScene>("res://Scenes/Battle/HexBattleScene.tscn");
        var scene = packed.Instantiate<HexBattleScene>();
        scene.UseRunSession = true; scene.StoryEventId = config.EventId; scene.StoryMapId = string.IsNullOrWhiteSpace(config.BattleMapId) ? "M-EVENT-EMPTY" : config.BattleMapId;
        scene.EnableCommandApi = false; scene.EnableDebugPanel = true; scene.ShowBuiltInResult = false;
        scene.StoryCompleted += () => OnStoryCompleted(run, scene);
        AddChild(scene);
    }

    /// <summary>事件结束：选项请求进入战斗则转入战斗，否则标记节点并返回地图。</summary>
    private void OnStoryCompleted(RunSession run, HexBattleScene scene)
    {
        string battleLevelId = scene?.PendingStoryBattleLevelId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(battleLevelId))
        {
            run.CompletePendingEventToMap();
			if (ContentFinished != null) { ContentFinished.Invoke(); return; }
            GetTree().ChangeSceneToFile(MapScenePath);
            return;
        }
        if (!TryStartEventBattle(run, battleLevelId, out string failure))
        {
            GD.PrintErr($"事件战斗无法进入：{battleLevelId} / {failure}");
            run.CompletePendingEventToMap();
			if (ContentFinished != null) { ContentFinished.Invoke(); return; }
            GetTree().ChangeSceneToFile(MapScenePath);
            return;
        }
		if (LevelRequested != null) { LevelRequested.Invoke(battleLevelId); return; }
        GetTree().ChangeSceneToFile(RunBattleScenePath);
    }

    /// <summary>按关卡 Id 构造遭遇行并进入战斗；节点类型固定为危险事件，因而不计入普通敌袭分档。</summary>
    private static bool TryStartEventBattle(RunSession run, string levelId, out string failure)
    {
        failure = string.Empty;
        try
        {
            CardSimulator.Battlefield.BattleLevelConfig level = CardSimulator.Battlefield.BattleLevelCatalog.Load(levelId);
            var row = new StageEncounterRow
            {
                Layer = string.Empty,
                NodeType = MapNodeType.DangerousEvent,
                Name = levelId,
                Difficulty = StageDifficulty.Any,
                DropTableId = level.DropTableId,
                LevelId = levelId,
                MonsterIds = level.Objects.Where(x => x.ObjectType == "Monster").Select(x => int.Parse(x.DefinitionId)).ToArray(),
            };
            if (row.MonsterIds.Length == 0) { failure = "关卡没有怪物配置。"; return false; }
            run.BeginRunBattleEncounter(string.Empty, row);
            return true;
        }
        catch (System.Exception ex) { failure = ex.Message; return false; }
    }
}
