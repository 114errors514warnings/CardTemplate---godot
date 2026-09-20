using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public sealed class StoryEventConfig
{
    public int SchemaVersion { get; set; }
    public string EventId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string BattleMapId { get; set; } = "M-EVENT-EMPTY";
    public bool CanSkip { get; set; } = true;
    public StoryBackgroundConfig Background { get; set; } = new();
    public List<StoryActorConfig> Actors { get; set; } = new();
    public List<StoryTimelineConfig> Timeline { get; set; } = new();
    public List<StoryChoiceConfig> Choices { get; set; } = new();
}
public sealed class StoryBackgroundConfig { public string BackgroundId { get; set; } = ""; public string InitialEffect { get; set; } = ""; }
public sealed class StoryActorConfig { public string ActorId { get; set; } = ""; public string DisplayName { get; set; } = ""; public string PortraitId { get; set; } = ""; public string DefaultSide { get; set; } = "Auto"; public Dictionary<string, string> Variants { get; set; } = new(); }
public sealed class StoryTimelineConfig { public int Order { get; set; } public string Type { get; set; } = "Dialogue"; public string ActorId { get; set; } = ""; public string Side { get; set; } = "Auto"; public string PortraitVariant { get; set; } = ""; public string BubbleStyle { get; set; } = "Plain"; public string Text { get; set; } = ""; public float AutoDelay { get; set; } = 1f; }
public sealed class StoryChoiceConfig { public string ChoiceId { get; set; } = ""; public int Order { get; set; } public string Text { get; set; } = ""; public string EffectPreview { get; set; } = ""; public List<StoryEffectConfig> Effects { get; set; } = new(); public StoryNextConfig Next { get; set; } = new(); }
public sealed class StoryEffectConfig { public string Type { get; set; } = ""; public string Target { get; set; } = ""; public int Value { get; set; } public string ReferenceId { get; set; } = ""; }
public sealed class StoryNextConfig { public string Type { get; set; } = "Close"; public string ReferenceId { get; set; } = ""; }

public static class StoryEventCatalog
{
    private const string IndexPath = "res://DataBase/Story/EventIndex.csv";
    public static StoryEventConfig Load(string eventId)
    {
        string path = "";
        foreach (string line in LoadCsv.LoadCSVDataLines(IndexPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] fields = LoadCsv.ParseCSVFields(line);
            if (fields.Length >= 2 && string.Equals(fields[0], eventId, StringComparison.OrdinalIgnoreCase)) { path = fields[1]; break; }
        }
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException($"剧情事件不存在：{eventId}");
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null) throw new ArgumentException($"无法打开剧情事件：{path}");
        var config = JsonSerializer.Deserialize<StoryEventConfig>(file.GetAsText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new ArgumentException($"剧情事件 JSON 为空：{eventId}");
        Validate(config, eventId); return config;
    }
    public static StoryEventDefinition ToDefinition(StoryEventConfig config, Func<StoryChoiceConfig, Action> actionFactory)
    {
        var actors = config.Actors.ToDictionary(x => x.ActorId, x => x, StringComparer.Ordinal);
        var lines = config.Timeline.Where(x => x.Type is "Dialogue" or "Narration").OrderBy(x => x.Order).Select(x =>
        {
            actors.TryGetValue(x.ActorId, out StoryActorConfig actor);
            string name = actor?.DisplayName ?? (x.Type == "Narration" ? "旁白" : x.ActorId);
            Enum.TryParse(x.Side, true, out StorySide side);
            Enum.TryParse(x.BubbleStyle, true, out StoryBubbleStyle bubble);
            return new StoryLine(x.ActorId, name, x.Text, side, bubble, Math.Max(0, x.AutoDelay));
        }).ToList();
        var choices = config.Choices.OrderBy(x => x.Order).Select(x => new StoryChoice(x.Text, x.EffectPreview, actionFactory?.Invoke(x))).ToList();
        return new StoryEventDefinition(config.Title, config.Summary, lines, choices, config.CanSkip, config.Background?.BackgroundId ?? "");
    }
    private static void Validate(StoryEventConfig config, string requestedId)
    {
        if (config.SchemaVersion != 1 || config.EventId != requestedId || string.IsNullOrWhiteSpace(config.Summary)) throw new ArgumentException("剧情事件基础字段无效。");
        if (config.Actors.Any(x => string.IsNullOrWhiteSpace(x.ActorId) || string.IsNullOrWhiteSpace(x.DisplayName)) || config.Actors.Select(x => x.ActorId).Distinct().Count() != config.Actors.Count) throw new ArgumentException("剧情角色配置无效或重复。");
        if (config.Timeline.Count == 0 || config.Timeline.Select(x => x.Order).Distinct().Count() != config.Timeline.Count || config.Timeline.Any(x => string.IsNullOrWhiteSpace(x.Text))) throw new ArgumentException("剧情时间线无效。");
        if (config.Choices.Count == 0 || config.Choices.Any(x => string.IsNullOrWhiteSpace(x.ChoiceId) || string.IsNullOrWhiteSpace(x.Text) || string.IsNullOrWhiteSpace(x.EffectPreview))) throw new ArgumentException("剧情选项配置无效。");
    }
}
