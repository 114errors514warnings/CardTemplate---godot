using Godot;
using System;

public partial class AnimationMaterialTestScene : Control
{
    private const string MainMenuScenePath = "res://Scenes/MainMenu/MainMenuScene.tscn";
    private PixelBattleMap map;
    private PixelHeroActor hero;
    private Label stateLabel;

    public override void _Ready()
    {
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        BuildUi();
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--animation-material-smoke") >= 0) CallDeferred(nameof(RunSmoke));
    }

    private void BuildUi()
    {
        var background = new ColorRect { Color = new Color("101923"), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(background);
        map = new PixelBattleMap(); map.SetAnchorsPreset(LayoutPreset.FullRect); map.OffsetRight = -330; map.MouseFilter = MouseFilterEnum.Ignore; AddChild(map);
        hero = new PixelHeroActor(); map.AddChild(hero); map.Resized += PositionHero; PositionHero();

        var panel = new PanelContainer(); panel.SetAnchorsPreset(LayoutPreset.RightWide);
        panel.OffsetLeft = -318; panel.OffsetTop = 18; panel.OffsetRight = -18; panel.OffsetBottom = -18;
        panel.AddThemeStyleboxOverride("panel", MakePanelStyle()); AddChild(panel);
        var content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 10); panel.AddChild(content);
        content.AddChild(MakeLabel("动画素材测试", 24, Colors.White));
        content.AddChild(MakeLabel("重剑手 · 模块化像素角色", 14, new Color("b8c8d8")));
        stateLabel = MakeLabel("状态：待机 / 双手武器", 14, new Color("f0d99a")); content.AddChild(stateLabel);
        content.AddChild(new HSeparator()); content.AddChild(MakeLabel("动作预览", 17, new Color("dcecff")));
        AddButton(content, "移动", () => { hero.PlayMove(); SetState("移动"); });
        AddButton(content, "攻击", () => { hero.PlayAttack(); SetState("攻击"); });
        AddButton(content, "受击", () => { hero.PlayHit(); SetState("受击"); });
        AddButton(content, "死亡", () => { hero.PlayDeath(); SetState("死亡"); });
        AddButton(content, "重置角色", () => { hero.ResetActor(); SetState("待机"); });
        content.AddChild(new HSeparator()); content.AddChild(MakeLabel("装备美术资源预览", 17, new Color("dcecff")));
        AddButton(content, "右手：剑", () => Select(PixelHeroActor.Loadout.RightSword, "右手剑"));
        AddButton(content, "左手：盾", () => Select(PixelHeroActor.Loadout.LeftShield, "左手盾"));
        AddButton(content, "剑 + 盾", () => Select(PixelHeroActor.Loadout.SwordAndShield, "剑盾"));
        AddButton(content, "双剑", () => Select(PixelHeroActor.Loadout.DualSwords, "双剑"));
        AddButton(content, "双手武器：统一握持", () => Select(PixelHeroActor.Loadout.TwoHandedWeapon, "双手武器"));
        AddButton(content, "弓箭", () => Select(PixelHeroActor.Loadout.Bow, "弓箭"));
        AddButton(content, "法器", () => Select(PixelHeroActor.Loadout.Tome, "法器"));
        AddButton(content, "卸下装备", () => Select(PixelHeroActor.Loadout.None, "空手"));
        content.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        content.AddChild(MakeLabel("单手剑、盾、弓与法器为独立贴图；双手剑使用人物动作图集中的原画。", 12, new Color("8fa4b7")));
        AddButton(content, "返回主界面", () => GetTree().ChangeSceneToFile(MainMenuScenePath));
    }

    private void Select(PixelHeroActor.Loadout loadout, string state) { hero.SetLoadout(loadout); SetState(state); }
    private void PositionHero() { if (map != null && hero != null) hero.SetHomePosition(new Vector2(map.Size.X * .5f, map.Size.Y * .54f)); }
    private void SetState(string state) => stateLabel.Text = $"状态：{state} / {hero.LoadoutLabel}";
    private static Label MakeLabel(string text, int fontSize, Color color) { var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart }; label.AddThemeFontSizeOverride("font_size", fontSize); label.AddThemeColorOverride("font_color", color); return label; }
    private static void AddButton(Container parent, string text, Action action) { var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 36) }; button.AddThemeFontSizeOverride("font_size", 15); button.Pressed += action; parent.AddChild(button); }
    private static StyleBoxFlat MakePanelStyle() { var style = new StyleBoxFlat { BgColor = new Color("162331"), BorderColor = new Color("38566f") }; style.SetBorderWidthAll(1); style.SetCornerRadiusAll(10); style.ContentMarginLeft = 14; style.ContentMarginRight = 14; style.ContentMarginTop = 14; style.ContentMarginBottom = 14; return style; }

    private async void RunSmoke()
    {
        try
        {
            string[] files = { "swordmaster_pixel_action_sheet.png", "swordmaster_pixel_action_equipment_sheet.png", "equipment_sword.png", "equipment_shield.png", "equipment_bow.png", "equipment_tome.png" };
            foreach (string file in files)
                if (!ResourceLoader.Exists($"res://Resources/Images/Characters/Pixel/{file}")) throw new InvalidOperationException($"缺少像素装备资源：{file}");
            foreach (PixelHeroActor.Loadout loadout in Enum.GetValues<PixelHeroActor.Loadout>())
            {
                hero.SetLoadout(loadout);
                SetState("待机");
                if (!hero.IsIdlePlaying) throw new InvalidOperationException($"装备 {loadout} 切换后未播放待机动画。");
                int firstFrame = hero.CurrentFrame;
                await ToSignal(GetTree().CreateTimer(.75), SceneTreeTimer.SignalName.Timeout);
                if (!hero.IsIdlePlaying || hero.CurrentFrame == firstFrame)
                    throw new InvalidOperationException($"装备 {loadout} 的待机动画没有推进帧：{firstFrame} → {hero.CurrentFrame}。");
                if (!CaptureFrame($"res://Tests/animation-material-{loadout}.png", $"装备 {loadout}"))
                    throw new InvalidOperationException($"无法保存装备 {loadout} 截图。");
            }
            hero.SetLoadout(PixelHeroActor.Loadout.TwoHandedWeapon);
            hero.PlayMove(); await ToSignal(GetTree().CreateTimer(.4), SceneTreeTimer.SignalName.Timeout);
            hero.PlayAttack(); await ToSignal(GetTree().CreateTimer(.16), SceneTreeTimer.SignalName.Timeout);
            if (!CaptureFrame("res://Tests/animation-material-attack.png", "攻击动作")) throw new InvalidOperationException("无法保存攻击动作截图。");
            await ToSignal(GetTree().CreateTimer(.44), SceneTreeTimer.SignalName.Timeout);
            hero.SetLoadout(PixelHeroActor.Loadout.SwordAndShield);
            SetState("剑盾攻击");
            hero.PlayAttack();
            await ToSignal(GetTree().CreateTimer(.16), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!CaptureFrame("res://Tests/animation-material-SwordAndShield-attack.png", "剑盾攻击")) throw new InvalidOperationException("无法保存剑盾攻击截图。");
            await ToSignal(GetTree().CreateTimer(.44), SceneTreeTimer.SignalName.Timeout);
            hero.SetLoadout(PixelHeroActor.Loadout.None);
            SetState("空手受击");
            hero.PlayHit(); await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
            SetState("空手死亡");
            hero.PlayDeath(); await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!CaptureFrame("res://Tests/animation-material-death.png", "死亡动作")) throw new InvalidOperationException("无法保存死亡动作截图。");
            hero.SetLoadout(PixelHeroActor.Loadout.TwoHandedWeapon);
            hero.ResetActor();
            SetState("待机");
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            if (!CaptureFrame("res://Tests/animation-material-smoke.png", "烟测收尾")) throw new InvalidOperationException("无法保存动画素材烟测截图。");
            GD.Print("ANIMATION_MATERIAL_SMOKE_PASS: all loadouts advance idle frames; attack captured without whole-character rotation; screenshots="
                + (CanCaptureFrames ? "Tests/animation-material-*.png" : "headless 跳过（无帧缓冲，资源路径已由 BATTLEFIELD_ASSET_PATH_PASS 覆盖）"));
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PrintErr("ANIMATION_MATERIAL_SMOKE_FAIL: " + ex);
            GetTree().Quit(1);
        }
    }

    /// <summary>无窗口（`--headless`）时 `GetViewport().GetTexture().GetImage()` 返回 null，出图只能跳过；
    /// 资源路径本身由 `BattlefieldSceneSmoke.VerifyAssetPaths()` 在无界面下校验。</summary>
    private static bool CanCaptureFrames => DisplayServer.GetName() != "headless";

    private bool CaptureFrame(string resPath, string label)
    {
        if (!CanCaptureFrames)
        {
            GD.Print($"[动画素材] headless：跳过截图（{label}）");
            return true;
        }

        Image image = GetViewport().GetTexture().GetImage();
        if (image == null)
        {
            GD.PrintErr($"[动画素材] 截图失败：拿不到帧缓冲（{label}）");
            return false;
        }

        Error error = image.SavePng(ProjectSettings.GlobalizePath(resPath));
        if (error != Error.Ok)
        {
            GD.PrintErr($"[动画素材] 保存截图失败：{error}（{label} → {resPath}）");
            return false;
        }

        return true;
    }
}

public partial class PixelBattleMap : Control
{
    private const float Radius = 48f;
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("13202b"));
        Vector2 origin = new(Size.X * .5f, Size.Y * .54f);
        for (int q = -6; q <= 6; q++) for (int r = -5; r <= 5; r++)
        {
            Vector2 center = origin + new Vector2(Radius * 1.5f * q, Radius * Mathf.Sqrt(3f) * (r + q * .5f));
            if (!new Rect2(-Radius, -Radius, Size.X + Radius * 2, Size.Y + Radius * 2).HasPoint(center)) continue;
            var points = new Vector2[6]; for (int i = 0; i < 6; i++) points[i] = center + Vector2.FromAngle(Mathf.DegToRad(60 * i - 30)) * Radius;
            bool heroCell = q == 0 && r == 0; DrawColoredPolygon(points, heroCell ? new Color("315669") : new Color("1d303d"));
            for (int i = 0; i < 6; i++) DrawLine(points[i], points[(i + 1) % 6], heroCell ? new Color("79cce0") : new Color("3c5967"), heroCell ? 2.5f : 1.2f, true);
        }
        DrawString(ThemeDB.FallbackFont, new Vector2(24, 36), "战斗地图 · 像素角色表现预览", HorizontalAlignment.Left, -1, 20, new Color("dcecff"));
    }
}

/// <summary>身体、左右手装备与双手装备均为独立的 Sprite2D 美术资源。</summary>
public partial class PixelHeroActor : Node2D
{
    public enum Loadout { None, RightSword, LeftShield, SwordAndShield, DualSwords, TwoHandedWeapon, Bow, Tome }
    private AnimatedSprite2D actionSprite;
    private SpriteFrames swordFrames;
    private SpriteFrames equipmentFrames;
    private Sprite2D leftHandEquipment;
    private Sprite2D rightHandEquipment;
    private Sprite2D twoHandEquipment;
    private Tween tween;
    private Vector2 home;
    private Loadout loadout;
    public bool IsIdlePlaying => actionSprite != null && actionSprite.Visible && actionSprite.IsPlaying() && actionSprite.Animation == "idle";
    public int CurrentFrame => actionSprite?.Frame ?? -1;
    private Vector2 leftRestPosition;
    private Vector2 rightRestPosition;
    private Vector2 twoHandRestPosition;
    private float leftRestRotation;
    private float rightRestRotation;
    private float twoHandRestRotation;
    public string LoadoutLabel => loadout switch { Loadout.RightSword => "右手剑", Loadout.LeftShield => "左手盾", Loadout.SwordAndShield => "剑盾", Loadout.DualSwords => "双剑", Loadout.TwoHandedWeapon => "双手武器", Loadout.Bow => "弓箭", Loadout.Tome => "法器", _ => "空手" };

    public override void _Ready()
    {
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        swordFrames = CreateActionFrames(Load("swordmaster_pixel_action_sheet.png"));
        equipmentFrames = CreateActionFrames(Load("swordmaster_pixel_action_equipment_sheet.png"));
        actionSprite = new AnimatedSprite2D { SpriteFrames = swordFrames, Scale = Vector2.One * .32f, ZIndex = 1 };
        AddChild(actionSprite);
        actionSprite.FrameChanged += UpdateEquipmentPose;
        leftHandEquipment = CreateEquipmentSprite(2); rightHandEquipment = CreateEquipmentSprite(2); twoHandEquipment = CreateEquipmentSprite(3);
        AddChild(leftHandEquipment); AddChild(rightHandEquipment); AddChild(twoHandEquipment);
        SetLoadout(Loadout.TwoHandedWeapon);
    }

    private static Sprite2D CreateEquipmentSprite(int zIndex) => new() { TextureFilter = CanvasItem.TextureFilterEnum.Nearest, ZIndex = zIndex, Visible = false };
    private static Texture2D Load(string file) => ResourceLoader.Load<Texture2D>($"res://Resources/Images/Characters/Pixel/{file}");
    private static SpriteFrames CreateActionFrames(Texture2D sheet)
    {
        var frames = new SpriteFrames();
        AddAnimation(frames, "idle", sheet, 0, 1, loop: true, speed: 1.5f);
        AddAnimation(frames, "walk", sheet, 2, 3, loop: true, speed: 8f);
        AddAnimation(frames, "attack", sheet, 4, 5, 0, loop: false, speed: 9f);
        AddAnimation(frames, "hurt", sheet, 6, 0, loop: false, speed: 8f);
        AddAnimation(frames, "death", sheet, 6, loop: false, speed: 1f);
        return frames;
    }
    private static void AddAnimation(SpriteFrames frames, string name, Texture2D sheet, int first, int second = -1, int third = -1, bool loop = false, float speed = 6f)
    {
        frames.AddAnimation(name); frames.SetAnimationLoop(name, loop); frames.SetAnimationSpeed(name, speed);
        int width = sheet.GetWidth() / 7;
        foreach (int index in new[] { first, second, third })
        {
            if (index < 0) continue;
            frames.AddFrame(name, new AtlasTexture { Atlas = sheet, Region = new Rect2(index * width, 0, width, sheet.GetHeight()) });
        }
    }
    private static void Attach(Sprite2D sprite, Texture2D texture, Vector2 anchor, float scale, float rotation = 0) { sprite.Texture = texture; sprite.Position = anchor; sprite.Scale = Vector2.One * scale; sprite.Rotation = rotation; sprite.Visible = true; }
    private void ClearEquipment() { foreach (Sprite2D sprite in new[] { leftHandEquipment, rightHandEquipment, twoHandEquipment }) { sprite.Visible = false; sprite.Texture = null; sprite.Rotation = 0; sprite.FlipH = false; } }

    public void SetLoadout(Loadout next)
    {
        loadout = next; ClearEquipment();
        switch (next)
        {
            case Loadout.RightSword: Attach(rightHandEquipment, Load("equipment_sword.png"), new Vector2(24, -7), .050f, Mathf.DegToRad(-14)); break;
            case Loadout.LeftShield: Attach(leftHandEquipment, Load("equipment_shield.png"), new Vector2(-8, 17), .045f); break;
            case Loadout.SwordAndShield:
                Attach(rightHandEquipment, Load("equipment_sword.png"), new Vector2(24, -7), .050f, Mathf.DegToRad(-14)); Attach(leftHandEquipment, Load("equipment_shield.png"), new Vector2(-8, 17), .045f); break;
            case Loadout.DualSwords:
                Attach(leftHandEquipment, Load("equipment_sword.png"), new Vector2(-12, -7), .050f, Mathf.DegToRad(14)); leftHandEquipment.FlipH = true;
                Attach(rightHandEquipment, Load("equipment_sword.png"), new Vector2(24, -7), .050f, Mathf.DegToRad(-14)); break;
            // 大剑、战斧、长枪、双手锤都复用此双手锚点，只替换对应 PNG。
            case Loadout.TwoHandedWeapon: break; // 原动作表已画好双手剑和握持动作。
            // 弓箭和法器是允许拥有专属双手姿势的两类。
            case Loadout.Bow: Attach(twoHandEquipment, Load("equipment_bow.png"), new Vector2(19, 7), .045f, Mathf.DegToRad(-8)); break;
            case Loadout.Tome: Attach(twoHandEquipment, Load("equipment_tome.png"), new Vector2(11, 12), .040f); break;
        }
        leftRestPosition = leftHandEquipment.Position; rightRestPosition = rightHandEquipment.Position; twoHandRestPosition = twoHandEquipment.Position;
        leftRestRotation = leftHandEquipment.Rotation; rightRestRotation = rightHandEquipment.Rotation; twoHandRestRotation = twoHandEquipment.Rotation;
        ResetActor();
    }

    public void SetHomePosition(Vector2 position) { home = position; Position = position; }
    private void PlayActionSheet(string animation)
    {
        actionSprite.SpriteFrames = loadout == Loadout.TwoHandedWeapon ? swordFrames : equipmentFrames;
        actionSprite.Visible = true;
        bool showEquipment = loadout != Loadout.TwoHandedWeapon;
        leftHandEquipment.Visible = showEquipment && leftHandEquipment.Texture != null;
        rightHandEquipment.Visible = showEquipment && rightHandEquipment.Texture != null;
        twoHandEquipment.Visible = showEquipment && twoHandEquipment.Texture != null;
        actionSprite.Stop();
        actionSprite.Frame = 0;
        actionSprite.Play(animation);
        UpdateEquipmentPose();
    }
    private void UpdateEquipmentPose()
    {
        if (leftHandEquipment == null || rightHandEquipment == null || twoHandEquipment == null) return;
        leftHandEquipment.Position = leftRestPosition; rightHandEquipment.Position = rightRestPosition; twoHandEquipment.Position = twoHandRestPosition;
        leftHandEquipment.Rotation = leftRestRotation; rightHandEquipment.Rotation = rightRestRotation; twoHandEquipment.Rotation = twoHandRestRotation;
        if (actionSprite.Animation != "attack" || loadout == Loadout.TwoHandedWeapon) return;
        if (actionSprite.Frame == 0)
        {
            if (rightHandEquipment.Visible) { rightHandEquipment.Position = new Vector2(-10, -35); rightHandEquipment.Rotation = Mathf.DegToRad(-70); }
            if (leftHandEquipment.Visible && loadout == Loadout.DualSwords) { leftHandEquipment.Position = new Vector2(-28, -8); leftHandEquipment.Rotation = Mathf.DegToRad(30); }
            if (twoHandEquipment.Visible) twoHandEquipment.Position = new Vector2(-7, -28);
        }
        else if (actionSprite.Frame == 1)
        {
            if (rightHandEquipment.Visible) { rightHandEquipment.Position = new Vector2(26, 22); rightHandEquipment.Rotation = Mathf.DegToRad(78); }
            if (leftHandEquipment.Visible && loadout == Loadout.DualSwords) { leftHandEquipment.Position = new Vector2(-18, 16); leftHandEquipment.Rotation = Mathf.DegToRad(-22); }
            if (twoHandEquipment.Visible) twoHandEquipment.Position = new Vector2(24, 20);
        }
    }
    private void RestoreIdlePresentation()
    {
        PlayActionSheet("idle");
    }
    public void ResetActor() { tween?.Kill(); Position = home; Rotation = 0; Scale = Vector2.One; Modulate = Colors.White; Visible = true; RestoreIdlePresentation(); }
    public void PlayMove()
    {
        ResetActor(); PlayActionSheet("walk"); tween = CreateTween().SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(this, "position", home + new Vector2(112, -64), .32);
        tween.Parallel().TweenProperty(this, "position:y", home.Y - 84, .16);
        tween.TweenProperty(this, "position:y", home.Y - 64, .16);
        tween.TweenCallback(Callable.From(() => { home = Position; RestoreIdlePresentation(); }));
    }
    public void PlayAttack()
    {
        ResetActor(); PlayActionSheet("attack"); tween = CreateTween();
        tween.TweenInterval(3f / 9f);
        tween.TweenCallback(Callable.From(RestoreIdlePresentation));
    }
    public void PlayHit()
    {
        ResetActor(); PlayActionSheet("hurt"); tween = CreateTween(); tween.TweenProperty(this, "modulate", new Color("ffaaaa"), .07);
        tween.Parallel().TweenProperty(this, "position:x", home.X - 8, .10);
        tween.TweenProperty(this, "modulate", Colors.White, .15);
        tween.Parallel().TweenProperty(this, "position:x", home.X, .15);
        tween.TweenCallback(Callable.From(RestoreIdlePresentation));
    }
    public void PlayDeath()
    {
        ResetActor(); PlayActionSheet("death"); tween = CreateTween().SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(this, "position:y", home.Y + 7, .4);
    }
}
