using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum StorySide { Auto, Left, Right, Narration }
public enum StoryBubbleStyle { Plain, LeftSpeaker, RightSpeaker }
public sealed record StoryLine(string SpeakerId, string SpeakerName, string Text, StorySide Side = StorySide.Auto, StoryBubbleStyle Bubble = StoryBubbleStyle.Plain, float AutoDelay = 1f);
/// <summary>One event outcome. EffectDescription is visible before the player chooses it.</summary>
public sealed record StoryChoice(string Text, string EffectDescription, Action Apply)
{
    public string DisplayText => string.IsNullOrWhiteSpace(EffectDescription) ? Text : $"{Text}（{EffectDescription}）";
}
public sealed record StoryEventDefinition(string Title, string Synopsis, IReadOnlyList<StoryLine> Lines, IReadOnlyList<StoryChoice> Choices = null, bool CanSkip = true, string BackgroundId = "", bool AutoComplete = false);

/// <summary>Modal story overlay. It owns only presentation and calls supplied callbacks for event results.</summary>
public partial class EventStoryOverlay : Control
{
    // 运行局固定层级中的剧情层：高于战斗 UI，低于 RunFlowScene 的世界地图与全局按钮。
    private const int StoryOverlayZIndex = 300;
    private sealed class StoryLogEntry
    {
        public string SpeakerName;
        public readonly List<string> Lines = new();
        public StoryLogEntry(string speakerName, string text) { SpeakerName = speakerName; Lines.Add(text); }
    }
    /// <summary>剧情浮层的功能按钮标识：对话记录 / 隐藏界面 / 自动播放 / 跳过对话。新增功能按钮只需加枚举项并在 Build 里 AddFunctionButton。</summary>
    private enum OverlayFunction { Log, Hide, Auto, Skip, Debug, Pause }

    private readonly StoryEventDefinition definition;
    private readonly Action onClosed;
    private readonly Action onDebug;
    private readonly Action onPause;
    private readonly List<StoryLogEntry> history = new();
    private PanelContainer leftPortrait, rightPortrait, bubble, logPanel, skipPanel, autoMenu;
    private Polygon2D bubbleTail;
    private Label leftName, rightName, speakerLabel, textLabel;
    private VBoxContainer choices;
    private HBoxContainer controls, rightControls;
    private RichTextLabel logBody;
    private readonly Dictionary<OverlayFunction, Button> functionButtons = new();
    private int lineIndex, visibleChars;
    private float charAccumulator, autoDelay;
    private int autoSpeedIndex; // 0 off, 1/2/4 speed
    private bool hidden, waitingChoice;
    private StorySide previousSide = StorySide.Left;
    private string leftSpeakerId = "", rightSpeakerId = "";

    public EventStoryOverlay(StoryEventDefinition definition, Action onClosed, Action onDebug = null, Action onPause = null)
    { this.definition = definition; this.onClosed = onClosed; this.onDebug = onDebug; this.onPause = onPause; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZAsRelative = false;
        ZIndex = StoryOverlayZIndex;
        Build();
        if (definition.AutoComplete) ShowReturnToMap(); else ShowLine(0);
    }

    private static StyleBoxFlat Box(Color color, int radius = 16) => new()
    {
        BgColor = color, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        BorderColor = new Color("d8c38c"), BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        ContentMarginLeft = 22, ContentMarginTop = 16, ContentMarginRight = 22, ContentMarginBottom = 16,
    };
    private static void Place(Control node, float l, float t, float r, float b)
    { node.AnchorLeft = l; node.AnchorTop = t; node.AnchorRight = r; node.AnchorBottom = b; node.OffsetLeft = node.OffsetTop = node.OffsetRight = node.OffsetBottom = 0; }
    private static Label Text(string value, int size) { var label = new Label { Text = value, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore }; label.AddThemeFontSizeOverride("font_size", size); return label; }

    /// <summary>背景图是否真的加载成功（供烟测/自检确认 `Resources/Images/UI/Story/Backgrounds/<id>.png` 路径有效）。</summary>
    public bool HasBackgroundTexture { get; private set; }
    private void Build()
    {
        HasBackgroundTexture = false;
        if (!string.IsNullOrWhiteSpace(definition.BackgroundId))
        {
            string path = $"res://Resources/Images/UI/Story/Backgrounds/{definition.BackgroundId}.png";
            var texture = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
            if (texture != null)
            {
                var eventBackground = new TextureRect { Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = MouseFilterEnum.Ignore };
                eventBackground.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(eventBackground);
                HasBackgroundTexture = true;
            }
        }
        // A configured background is displayed unmasked. The dark layer is only the no-background fallback.
        if (!HasBackgroundTexture)
        {
            var shade = new ColorRect { Color = new Color(0.02f, 0.03f, 0.05f, .72f), MouseFilter = MouseFilterEnum.Ignore };
            shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(shade);
        }
        // Portrait placeholders reserve the lower corners for future upper-body art.
        // Portraits reserve only the lower corners; future upper-body art stays close to the edges and preserves the background.
        leftPortrait = Portrait("左侧人物", out leftName); Place(leftPortrait, .01f, .64f, .17f, .98f); AddChild(leftPortrait);
        rightPortrait = Portrait("右侧人物", out rightName); Place(rightPortrait, .83f, .64f, .99f, .98f); AddChild(rightPortrait);

        bubbleTail = new Polygon2D { Visible = false, ZIndex = 1 }; AddChild(bubbleTail);
        bubble = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, ZIndex = 2 }; bubble.AddThemeStyleboxOverride("panel", Box(new Color(0.07f, .09f, .13f, .96f))); Place(bubble, .20f, .67f, .80f, .88f); AddChild(bubble);
        var bubbleBox = new VBoxContainer(); bubbleBox.AddThemeConstantOverride("separation", 8); bubble.AddChild(bubbleBox);
        speakerLabel = Text("", 18); speakerLabel.AddThemeColorOverride("font_color", new Color("f0d692")); bubbleBox.AddChild(speakerLabel);
        textLabel = Text("", 20); textLabel.SizeFlagsVertical = SizeFlags.ExpandFill; bubbleBox.AddChild(textLabel);

        // All dialogue controls stay in upper corners, never in the lower dialogue/portrait area.
        controls = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin }; controls.AddThemeConstantOverride("separation", 10); Place(controls, .02f, .025f, .37f, .08f); AddChild(controls);
        AddFunctionButton(OverlayFunction.Log, "Log", controls, ToggleLog).CustomMinimumSize = new Vector2(90, 38);
        AddFunctionButton(OverlayFunction.Hide, "隐藏", controls, ToggleHidden).CustomMinimumSize = new Vector2(90, 38);
        AddFunctionButton(OverlayFunction.Auto, "Auto: 关闭", controls, ToggleAutoMenu).CustomMinimumSize = new Vector2(90, 38);
        rightControls = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, ZIndex = 20 }; rightControls.AddThemeConstantOverride("separation", 8); Place(rightControls, .64f, .025f, .98f, .08f); AddChild(rightControls);
        if (onDebug != null) AddFunctionButton(OverlayFunction.Debug, "调试", rightControls, onDebug).CustomMinimumSize = new Vector2(88, 38);
        if (onPause != null) AddFunctionButton(OverlayFunction.Pause, "暂停", rightControls, onPause).CustomMinimumSize = new Vector2(88, 38);
        AddFunctionButton(OverlayFunction.Skip, "跳过", rightControls, ToggleSkip).CustomMinimumSize = new Vector2(88, 38);

        choices = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, Visible = false }; choices.AddThemeConstantOverride("separation", 14); Place(choices, .30f, .30f, .70f, .66f); AddChild(choices);
        BuildLog(); BuildSkip(); BuildAutoMenu();
    }

    private PanelContainer Portrait(string placeholder, out Label name)
    {
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore }; panel.AddThemeStyleboxOverride("panel", Box(new Color(.12f, .15f, .21f, .90f), 20));
        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; panel.AddChild(box);
        name = Text(placeholder, 18); name.HorizontalAlignment = HorizontalAlignment.Center; box.AddChild(name);
        var hint = Text("立绘占位", 14); hint.HorizontalAlignment = HorizontalAlignment.Center; hint.AddThemeColorOverride("font_color", new Color("aab6c5")); box.AddChild(hint);
        return panel;
    }

    private Button AddFunctionButton(OverlayFunction key, string caption, Control parent, Action action)
    {
        var button = new Button { Name = key.ToString(), Text = caption };
        button.Pressed += action;
        functionButtons[key] = button;
        parent.AddChild(button);
        return button;
    }

    /// <summary>运行局常驻顶部栏启用时，隐藏剧情自身的重复按钮，但仍由此对象执行按钮行为。</summary>
    public void SetBuiltInTopControlsVisible(bool visible)
    {
        if (controls != null) controls.Visible = visible;
        if (rightControls != null) rightControls.Visible = visible;
    }

    /// <summary>世界地图覆盖层开合：地图打开时剧情 UI 整体让位，否则会透在地图下方。</summary>
    public void SetWorldMapOpen(bool open) => Visible = !open;

    /// <summary>当前是否还允许跳过剧情：进入结束面板（等待“前往地图”）后不再提供跳过。</summary>
    public bool CanSkipNow => definition.CanSkip && !waitingChoice;

    public void ToggleLogFromGlobalTopBar() => ToggleLog();
    public void ToggleHiddenFromGlobalTopBar() => ToggleHidden();
    public void ToggleAutoFromGlobalTopBar() => ToggleAutoMenu();
    public void ToggleSkipFromGlobalTopBar() => ToggleSkip();

    private void BuildLog()
    {
        // Log is a modal above bubble, portraits, choices and all other story UI.
        logPanel = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Stop, ZIndex = 30 }; logPanel.AddThemeStyleboxOverride("panel", Box(new Color(.04f, .05f, .08f, 1f))); Place(logPanel, .16f, .10f, .84f, .78f); AddChild(logPanel);
        var box = new VBoxContainer(); logPanel.AddChild(box);
        var header = new HBoxContainer(); box.AddChild(header);
        var title = Text("对话记录", 24); title.SizeFlagsHorizontal = SizeFlags.ExpandFill; header.AddChild(title);
        var close = new Button { Text = "关闭", CustomMinimumSize = new Vector2(84, 34) }; close.Pressed += ToggleLog; header.AddChild(close);
        logBody = new RichTextLabel { BbcodeEnabled = false, ScrollActive = true, SizeFlagsVertical = SizeFlags.ExpandFill }; logBody.Name = "Body"; box.AddChild(logBody);
    }
    private void BuildSkip()
    {
        skipPanel = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Stop }; skipPanel.AddThemeStyleboxOverride("panel", Box(new Color(.08f, .06f, .07f, .99f))); Place(skipPanel, .31f, .31f, .69f, .62f); AddChild(skipPanel);
        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; box.AddThemeConstantOverride("separation", 14); skipPanel.AddChild(box);
        box.AddChild(Text("跳过剧情？", 24)); box.AddChild(Text(definition.Synopsis, 16));
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; row.AddThemeConstantOverride("separation", 16); box.AddChild(row);
        var confirm = new Button { Text = "确认跳过" }; confirm.Pressed += Skip; row.AddChild(confirm);
        var cancel = new Button { Text = "返回剧情" }; cancel.Pressed += ToggleSkip; row.AddChild(cancel);
    }
    private void BuildAutoMenu()
    {
        autoMenu = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Stop, ZIndex = 10 };
        autoMenu.AddThemeStyleboxOverride("panel", Box(new Color(.04f, .06f, .09f, .98f), 8));
        // Directly below the third control (Auto); fixed anchors keep the menu stable at every resolution.
        Place(autoMenu, .145f, .085f, .255f, .285f); AddChild(autoMenu);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 4); autoMenu.AddChild(box);
        AddAutoOption(box, "关闭", 0);
        AddAutoOption(box, "1×", 1);
        AddAutoOption(box, "2×", 2);
        AddAutoOption(box, "4×", 3);
    }
    private void AddAutoOption(Control parent, string text, int speedIndex)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(92, 30) };
        button.Pressed += () => SetAutoSpeed(speedIndex);
        parent.AddChild(button);
    }

    private void ShowLine(int index)
    {
        if (index >= definition.Lines.Count) { ShowChoicesOrClose(); return; }
        lineIndex = index; visibleChars = 0; charAccumulator = 0; autoDelay = 0;
        var line = definition.Lines[index]; StorySide side = ResolveSide(line);
        UpdatePortraits(line, side); ApplyBubble(line, side);
        speakerLabel.Text = line.SpeakerName; textLabel.Text = "";
    }
    private StorySide ResolveSide(StoryLine line)
    {
        if (line.Side != StorySide.Auto) return line.Side;
        if (line.SpeakerId == leftSpeakerId) return StorySide.Left;
        if (line.SpeakerId == rightSpeakerId) return StorySide.Right;
        return previousSide == StorySide.Left ? StorySide.Right : StorySide.Left;
    }
    private void UpdatePortraits(StoryLine line, StorySide side)
    {
        if (side == StorySide.Left) { leftSpeakerId = line.SpeakerId; leftName.Text = line.SpeakerName; }
        else if (side == StorySide.Right) { rightSpeakerId = line.SpeakerId; rightName.Text = line.SpeakerName; }
        leftPortrait.Modulate = side == StorySide.Left ? Colors.White : new Color(.55f, .55f, .60f);
        rightPortrait.Modulate = side == StorySide.Right ? Colors.White : new Color(.55f, .55f, .60f);
        previousSide = side;
    }
    private void ApplyBubble(StoryLine line, StorySide side)
    {
        var style = line.Bubble == StoryBubbleStyle.Plain && side != StorySide.Narration ? (side == StorySide.Left ? StoryBubbleStyle.LeftSpeaker : StoryBubbleStyle.RightSpeaker) : line.Bubble;
        Color color = style == StoryBubbleStyle.Plain ? new Color(.08f, .10f, .14f, .96f) : style == StoryBubbleStyle.LeftSpeaker ? new Color(.10f, .16f, .22f, .97f) : new Color(.18f, .12f, .16f, .97f);
        bubble.AddThemeStyleboxOverride("panel", Box(color));
        CallDeferred(nameof(UpdateBubbleTail), (int)style, color);
    }

    private void UpdateBubbleTail(int styleValue, Color color)
    {
        var style = (StoryBubbleStyle)styleValue;
        if (style == StoryBubbleStyle.Plain) { bubbleTail.Visible = false; return; }
        Rect2 rect = bubble.GetGlobalRect();
        float x = style == StoryBubbleStyle.LeftSpeaker ? rect.Position.X + 52f : rect.End.X - 52f;
        float y = rect.End.Y;
        bubbleTail.Polygon = new[] { new Vector2(x - 18f, y - 1f), new Vector2(x + 18f, y - 1f), new Vector2(x, y + 24f) };
        bubbleTail.Color = color;
        bubbleTail.Visible = !hidden;
    }

    public override void _Process(double delta)
    {
        // 地图覆盖层打开时浮层整体隐藏（Visible=false）：此时不应在幕后继续推进台词/自动播放。
        if (!Visible || hidden || logPanel.Visible || skipPanel.Visible || autoMenu.Visible || waitingChoice || lineIndex >= definition.Lines.Count) return;
        var line = definition.Lines[lineIndex]; float speed = autoSpeedIndex switch { 1 => 45f, 2 => 90f, 3 => 180f, _ => 45f };
        if (visibleChars < line.Text.Length)
        {
            charAccumulator += (float)delta * speed;
            int count = Math.Min(line.Text.Length, (int)charAccumulator);
            if (count != visibleChars) { visibleChars = count; textLabel.Text = line.Text[..visibleChars]; }
            return;
        }
        if (autoSpeedIndex > 0)
        {
            autoDelay += (float)delta;
            if (autoDelay >= line.AutoDelay / (1 << (autoSpeedIndex - 1))) Advance();
        }
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is not InputEventMouseButton mouse || mouse.ButtonIndex != MouseButton.Left || !mouse.Pressed) return;
        if (hidden) { ToggleHidden(); AcceptEvent(); return; }
        if (autoMenu.Visible) { autoMenu.Visible = false; AcceptEvent(); return; }
        if (!logPanel.Visible && !skipPanel.Visible && !waitingChoice) Advance();
        AcceptEvent();
    }
    private void Advance()
    {
        var line = definition.Lines[lineIndex];
        if (visibleChars < line.Text.Length) { visibleChars = line.Text.Length; textLabel.Text = line.Text; return; }
        // The active line is deliberately not written until it is advanced; Log therefore contains every prior line only.
        if (history.Count > 0 && history[^1].SpeakerName == line.SpeakerName) history[^1].Lines.Add(line.Text);
        else history.Add(new StoryLogEntry(line.SpeakerName, line.Text));
        ShowLine(lineIndex + 1);
    }
    private void ShowChoicesOrClose(bool showSkipSynopsis = false)
    {
        if (definition.Choices == null || definition.Choices.Count == 0) { Close(); return; }
        waitingChoice = true; choices.Visible = true;
        if (functionButtons.TryGetValue(OverlayFunction.Skip, out var skipButton)) skipButton.Visible = false;
        if (showSkipSynopsis)
        {
            var synopsis = Text("剧情梗概\n" + definition.Synopsis, 17);
            synopsis.HorizontalAlignment = HorizontalAlignment.Left;
            synopsis.AddThemeColorOverride("font_color", new Color("d7dce6"));
            synopsis.CustomMinimumSize = new Vector2(420, 80);
            choices.AddChild(synopsis);
        }
        foreach (var choice in definition.Choices)
        {
            var button = new Button { Text = choice.DisplayText, CustomMinimumSize = new Vector2(420, 52) };
            button.TooltipText = choice.EffectDescription;
            button.Pressed += () => { choice.Apply?.Invoke(); ShowReturnToMap(); };
            choices.AddChild(button);
        }
    }
    private void ShowReturnToMap()
    {
        waitingChoice = true;
        choices.Visible = true;
        if (functionButtons.TryGetValue(OverlayFunction.Skip, out var skipButton)) skipButton.Visible = false;
        foreach (Node child in choices.GetChildren()) { choices.RemoveChild(child); child.QueueFree(); }
        var button = new Button { Text = "前往地图", CustomMinimumSize = new Vector2(420, 52) };
        button.Pressed += Close;
        choices.AddChild(button);
    }
    private void ToggleLog()
    {
        logPanel.Visible = !logPanel.Visible;
        if (logPanel.Visible && logBody != null)
        {
            logBody.Text = string.Join("\n\n", history.Select(entry => entry.SpeakerName + "\n" + string.Join("\n", entry.Lines)));
            CallDeferred(nameof(ScrollLogToLatest));
        }
    }
    private void ScrollLogToLatest()
    {
        if (logBody != null) logBody.ScrollToLine(Math.Max(0, logBody.GetLineCount() - 1));
    }
    private void ToggleHidden() { hidden = !hidden; bubble.Visible = !hidden; bubbleTail.Visible = !hidden && bubbleTail.Polygon.Length > 0; controls.Visible = !hidden; autoMenu.Visible = false; choices.Visible = !hidden && waitingChoice; }
    private void ToggleAutoMenu()
    {
        if (hidden || waitingChoice) return;
        autoMenu.Visible = !autoMenu.Visible;
    }
    private void SetAutoSpeed(int speedIndex)
    {
        autoSpeedIndex = Math.Clamp(speedIndex, 0, 3);
        autoMenu.Visible = false;
        if (functionButtons.TryGetValue(OverlayFunction.Auto, out var button))
            button.Text = autoSpeedIndex == 0 ? "Auto: 关闭" : $"Auto: {1 << (autoSpeedIndex - 1)}×";
    }
    private void ToggleSkip() { if (definition.CanSkip) skipPanel.Visible = !skipPanel.Visible; }
    private void Skip()
    {
        if (waitingChoice) return;
        history.Add(new StoryLogEntry("剧情梗概", "（已跳过：" + definition.Synopsis + "）"));
        skipPanel.Visible = false;
        // Skipping condenses the presentation only; it never selects an outcome for the player.
        ShowChoicesOrClose(showSkipSynopsis: true);
    }
    /// <summary>事件结束后浮层不再自毁：完成后的剧情 UI 继续存在（世界地图作为覆盖层叠加其上），
    /// 由持有者在换剧情或销毁内容时回收。</summary>
    private void Close() { onClosed?.Invoke(); Visible = false; }
}
