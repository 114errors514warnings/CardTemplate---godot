using Godot;

public partial class RunEventScene : Control
{
    public const string MapScenePath = "res://Scenes/Map/MapScene.tscn";
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
        scene.StoryCompleted += () => { run.CompletePendingEventToMap(); GetTree().ChangeSceneToFile(MapScenePath); };
        AddChild(scene);
    }
}
