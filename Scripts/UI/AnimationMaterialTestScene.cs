using Godot;
using System;
using System.Collections.Generic;

public partial class AnimationMaterialTestScene : Control
{
    private const string MainMenuScenePath = "res://Scenes/MainMenu/MainMenuScene.tscn";
    private PixelBattleMap map;
    private PixelHeroActor hero;
    private Label stateLabel;
    private Label characterLabel;
    private ScrollContainer testScroll;
    private readonly List<Button> equipmentButtons = new();

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
        testScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        panel.AddChild(testScroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 1040) };
        content.AddThemeConstantOverride("separation", 10); testScroll.AddChild(content);
        content.AddChild(MakeLabel("动画素材测试", 24, Colors.White));
        characterLabel = MakeLabel("重剑手 · 骨骼迭代版", 14, new Color("b8c8d8")); content.AddChild(characterLabel);
        content.AddChild(MakeLabel("测试角色", 17, new Color("dcecff")));
        var characterRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        characterRow.AddThemeConstantOverride("separation", 8); content.AddChild(characterRow);
        AddButton(characterRow, "重剑手", () => SelectCharacter(PixelHeroActor.CharacterKind.Greatsword));
        AddButton(characterRow, "精灵", () => SelectCharacter(PixelHeroActor.CharacterKind.Elf));
        stateLabel = MakeLabel("状态：待机 / 双手武器", 14, new Color("f0d99a")); content.AddChild(stateLabel);
        content.AddChild(new HSeparator()); content.AddChild(MakeLabel("动作预览", 17, new Color("dcecff")));
        AddButton(content, "移动", () => { hero.PlayMove(); SetState("移动"); });
        AddButton(content, "攻击", () => { hero.PlayAttack(); SetState("攻击"); });
        AddButton(content, "受击", () => { hero.PlayHit(); SetState("受击"); });
        AddButton(content, "死亡", () => { hero.PlayDeath(); SetState("死亡"); });
        AddButton(content, "重置角色", () => { hero.ResetActor(); SetState("待机"); });
        content.AddChild(new HSeparator()); content.AddChild(MakeLabel("装备美术资源预览", 17, new Color("dcecff")));
        equipmentButtons.Add(AddButton(content, "右手：剑", () => Select(PixelHeroActor.Loadout.RightSword, "右手剑")));
        equipmentButtons.Add(AddButton(content, "左手：盾", () => Select(PixelHeroActor.Loadout.LeftShield, "左手盾")));
        equipmentButtons.Add(AddButton(content, "剑 + 盾", () => Select(PixelHeroActor.Loadout.SwordAndShield, "剑盾")));
        equipmentButtons.Add(AddButton(content, "双剑", () => Select(PixelHeroActor.Loadout.DualSwords, "双剑")));
        equipmentButtons.Add(AddButton(content, "双手武器：统一握持", () => Select(PixelHeroActor.Loadout.TwoHandedWeapon, "双手武器")));
        equipmentButtons.Add(AddButton(content, "弓箭", () => Select(PixelHeroActor.Loadout.Bow, "弓箭")));
        equipmentButtons.Add(AddButton(content, "法器", () => Select(PixelHeroActor.Loadout.Tome, "法器")));
        equipmentButtons.Add(AddButton(content, "卸下装备", () => Select(PixelHeroActor.Loadout.None, "空手")));
        content.AddChild(MakeLabel("单手剑、盾、弓与法器为独立贴图；双手剑使用人物动作图集中的原画。", 12, new Color("8fa4b7")));
        AddButton(content, "返回主界面", () => GetTree().ChangeSceneToFile(MainMenuScenePath));
    }

    private void Select(PixelHeroActor.Loadout loadout, string state) { hero.SetLoadout(loadout); SetState(state); }
    private void SelectCharacter(PixelHeroActor.CharacterKind kind)
    {
        hero.SetCharacter(kind);
        bool isElf = kind == PixelHeroActor.CharacterKind.Elf;
        characterLabel.Text = isElf ? "精灵 · 伊瑟拉 · 默认弓箭" : "重剑手 · 骨骼迭代版";
        foreach (Button button in equipmentButtons) button.Disabled = isElf;
        SetState("待机");
    }
    private void PositionHero() { if (map != null && hero != null) hero.SetHomePosition(new Vector2(map.Size.X * .5f, map.Size.Y * .54f)); }
    private void SetState(string state) => stateLabel.Text = $"状态：{state} / {hero.LoadoutLabel}";
    private static Label MakeLabel(string text, int fontSize, Color color) { var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart }; label.AddThemeFontSizeOverride("font_size", fontSize); label.AddThemeColorOverride("font_color", color); return label; }
    private static Button AddButton(Container parent, string text, Action action) { var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 36) }; button.AddThemeFontSizeOverride("font_size", 15); button.Pressed += action; parent.AddChild(button); return button; }
    private static StyleBoxFlat MakePanelStyle() { var style = new StyleBoxFlat { BgColor = new Color("162331"), BorderColor = new Color("38566f") }; style.SetBorderWidthAll(1); style.SetCornerRadiusAll(10); style.ContentMarginLeft = 14; style.ContentMarginRight = 14; style.ContentMarginTop = 14; style.ContentMarginBottom = 14; return style; }

    private async void RunSmoke()
    {
        try
        {
            string[] files = { "swordmaster_pixel_action_sheet.png", "swordmaster_pixel_action_equipment_sheet.png", "swordmaster_bow_action_sheet_v2.png", "swordmaster_tome_action_sheet_v2.png", "swordmaster_death_pose_v2.png", "isera_pixel_action_sheet_v3.png", "equipment_sword.png", "equipment_shield.png", "fx_bow_arrow_trail.png", "fx_tome_cast_rune.png" };
            foreach (string file in files)
                if (!ResourceLoader.Exists($"res://Resources/Images/Characters/Pixel/{file}")) throw new InvalidOperationException($"缺少像素装备资源：{file}");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree().CreateTimer(.05), SceneTreeTimer.SignalName.Timeout);
            if (CanCaptureFrames)
            {
            VScrollBar scrollBar = testScroll.GetVScrollBar();
            if (scrollBar.MaxValue <= scrollBar.Page) throw new InvalidOperationException("右侧测试栏内容未形成可滚动区域。");
            testScroll.ScrollVertical = (int)scrollBar.MaxValue;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (testScroll.ScrollVertical <= 0) throw new InvalidOperationException("右侧测试栏无法滚动到底部。");
            if (!CaptureFrame("res://Tests/animation-material-panel-bottom.png", "测试栏底部")) throw new InvalidOperationException("无法保存测试栏底部截图。");
            testScroll.ScrollVertical = 0;
            }
            SelectCharacter(PixelHeroActor.CharacterKind.Greatsword);
            if (hero.RigBoneCount < 15) throw new InvalidOperationException($"重剑手骨骼数量不足：{hero.RigBoneCount}。");
            if (hero.RigSocketCount != 2) throw new InvalidOperationException($"重剑手左右手装备插槽数量错误：{hero.RigSocketCount}。");
            if (!ResourceLoader.Exists("res://Scenes/Characters/SwordmasterRig.tscn")) throw new InvalidOperationException("缺少重剑手骨骼场景资源。");
            foreach (string part in new[] { "head", "torso", "pelvis", "cape", "arm_l_upper", "arm_l_lower", "hand_l", "arm_r_upper", "arm_r_lower", "hand_r", "leg_l_upper", "leg_l_lower", "foot_l", "leg_r_upper", "leg_r_lower", "foot_r" })
                if (!ResourceLoader.Exists($"res://Resources/Images/Characters/Rigs/Swordmaster/PartsV2/{part}.png"))
                    throw new InvalidOperationException($"缺少重剑手骨骼拆件：{part}.png");
            foreach (PixelHeroActor.Loadout loadout in Enum.GetValues<PixelHeroActor.Loadout>())
            {
                hero.SetLoadout(loadout);
                SetState("待机");
                if (!hero.IsIdlePlaying) throw new InvalidOperationException($"装备 {loadout} 切换后未播放待机动画。");
                int firstFrame = hero.CurrentFrame;
                Vector2 idlePosition = hero.Position;
                await ToSignal(GetTree().CreateTimer(.75), SceneTreeTimer.SignalName.Timeout);
                if (!hero.IsIdlePlaying || hero.CurrentFrame == firstFrame)
                    throw new InvalidOperationException($"装备 {loadout} 的待机动画没有推进帧：{firstFrame} → {hero.CurrentFrame}。");
                if (hero.Position != idlePosition) throw new InvalidOperationException($"装备 {loadout} 的待机动画移动了人物位置：{idlePosition} → {hero.Position}。");
                if (!CaptureFrame($"res://Tests/animation-material-{loadout}.png", $"装备 {loadout}"))
                    throw new InvalidOperationException($"无法保存装备 {loadout} 截图。");
            }
            hero.SetLoadout(PixelHeroActor.Loadout.SwordAndShield);
            SetState("剑盾移动");
            Vector2 moveRestWeapon = hero.RightHandPosition;
            hero.PlayMove(); await ToSignal(GetTree().CreateTimer(.08), SceneTreeTimer.SignalName.Timeout);
            if (hero.RightHandPosition == moveRestWeapon) throw new InvalidOperationException("移动动画中右手武器没有跟随帧移动。");
            if (!CaptureFrame("res://Tests/animation-material-SwordAndShield-move.png", "剑盾移动")) throw new InvalidOperationException("无法保存剑盾移动截图。");
            await ToSignal(GetTree().CreateTimer(.36), SceneTreeTimer.SignalName.Timeout);
            hero.SetLoadout(PixelHeroActor.Loadout.TwoHandedWeapon);
            hero.PlayAttack(); await ToSignal(GetTree().CreateTimer(.16), SceneTreeTimer.SignalName.Timeout);
            if (!CaptureFrame("res://Tests/animation-material-attack.png", "攻击动作")) throw new InvalidOperationException("无法保存攻击动作截图。");
            await ToSignal(GetTree().CreateTimer(.44), SceneTreeTimer.SignalName.Timeout);
            hero.SetLoadout(PixelHeroActor.Loadout.Bow);
            SetState("弓箭攻击"); hero.PlayAttack(); await ToSignal(GetTree().CreateTimer(.16), SceneTreeTimer.SignalName.Timeout);
            if (!hero.IsAttackEffectVisible) throw new InvalidOperationException("弓箭释放帧没有显示独立箭迹特效。");
            if (!CaptureFrame("res://Tests/animation-material-Bow-attack.png", "弓箭攻击")) throw new InvalidOperationException("无法保存弓箭攻击截图。");
            await ToSignal(GetTree().CreateTimer(.44), SceneTreeTimer.SignalName.Timeout);
            hero.SetLoadout(PixelHeroActor.Loadout.Tome);
            SetState("法典攻击"); hero.PlayAttack(); await ToSignal(GetTree().CreateTimer(.16), SceneTreeTimer.SignalName.Timeout);
            if (!hero.IsAttackEffectVisible) throw new InvalidOperationException("法典释放帧没有显示独立符文特效。");
            if (!CaptureFrame("res://Tests/animation-material-Tome-attack.png", "法典攻击")) throw new InvalidOperationException("无法保存法典攻击截图。");
            await ToSignal(GetTree().CreateTimer(.44), SceneTreeTimer.SignalName.Timeout);
            hero.SetLoadout(PixelHeroActor.Loadout.SwordAndShield);
            SetState("剑盾攻击");
            hero.PlayAttack();
            await ToSignal(GetTree().CreateTimer(.16), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!CaptureFrame("res://Tests/animation-material-SwordAndShield-attack.png", "剑盾攻击")) throw new InvalidOperationException("无法保存剑盾攻击截图。");
            await ToSignal(GetTree().CreateTimer(.44), SceneTreeTimer.SignalName.Timeout);
            hero.SetLoadout(PixelHeroActor.Loadout.RightSword);
            SetState("持剑受击");
            Vector2 hurtRestWeapon = hero.RightHandPosition;
            hero.PlayHit(); await ToSignal(GetTree().CreateTimer(.06), SceneTreeTimer.SignalName.Timeout);
            if (hero.RightHandPosition == hurtRestWeapon) throw new InvalidOperationException("受击动画中右手武器没有跟随帧移动。");
            if (!CaptureFrame("res://Tests/animation-material-hurt.png", "持剑受击")) throw new InvalidOperationException("无法保存受击截图。");
            await ToSignal(GetTree().CreateTimer(.28), SceneTreeTimer.SignalName.Timeout);
            SetState("持剑死亡");
            hero.PlayDeath(); await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
            if (!hero.IsDedicatedDeathVisible) throw new InvalidOperationException("重剑手死亡没有切换到独立倒地精灵。");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!CaptureFrame("res://Tests/animation-material-death.png", "死亡动作")) throw new InvalidOperationException("无法保存死亡动作截图。");
            hero.SetLoadout(PixelHeroActor.Loadout.None);
            SetState("空手受击");
            hero.PlayHit(); await ToSignal(GetTree().CreateTimer(.06), SceneTreeTimer.SignalName.Timeout);
            if (!CaptureFrame("res://Tests/animation-material-None-hurt.png", "空手受击")) throw new InvalidOperationException("无法保存空手受击截图。");
            await ToSignal(GetTree().CreateTimer(.28), SceneTreeTimer.SignalName.Timeout);
            SetState("空手死亡");
            hero.PlayDeath(); await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!CaptureFrame("res://Tests/animation-material-None-death.png", "空手死亡")) throw new InvalidOperationException("无法保存空手死亡截图。");
            hero.SetLoadout(PixelHeroActor.Loadout.TwoHandedWeapon);
            hero.ResetActor();
            SetState("待机");
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            if (!CaptureFrame("res://Tests/animation-material-smoke.png", "烟测收尾")) throw new InvalidOperationException("无法保存动画素材烟测截图。");
            SelectCharacter(PixelHeroActor.CharacterKind.Elf);
            if (!hero.IsIdlePlaying) throw new InvalidOperationException("切换精灵后没有播放待机动画。");
            int elfIdleFrame = hero.CurrentFrame;
            Vector2 elfIdlePosition = hero.Position;
            await ToSignal(GetTree().CreateTimer(.75), SceneTreeTimer.SignalName.Timeout);
            if (!hero.IsIdlePlaying || hero.CurrentFrame == elfIdleFrame) throw new InvalidOperationException("精灵待机动画没有推进帧。");
            if (hero.Position != elfIdlePosition) throw new InvalidOperationException($"精灵待机动画移动了人物位置：{elfIdlePosition} → {hero.Position}。");
            if (!CaptureFrame("res://Tests/animation-material-elf-idle.png", "精灵待机")) throw new InvalidOperationException("无法保存精灵待机截图。");
            SetState("移动"); hero.PlayMove(); await ToSignal(GetTree().CreateTimer(.08), SceneTreeTimer.SignalName.Timeout);
            if (!CaptureFrame("res://Tests/animation-material-elf-move.png", "精灵移动")) throw new InvalidOperationException("无法保存精灵移动截图。");
            await ToSignal(GetTree().CreateTimer(.36), SceneTreeTimer.SignalName.Timeout);
            SetState("攻击"); hero.PlayAttack(); await ToSignal(GetTree().CreateTimer(.16), SceneTreeTimer.SignalName.Timeout);
            if (!CaptureFrame("res://Tests/animation-material-elf-attack.png", "精灵攻击")) throw new InvalidOperationException("无法保存精灵攻击截图。");
            await ToSignal(GetTree().CreateTimer(.44), SceneTreeTimer.SignalName.Timeout);
            SetState("受击"); hero.PlayHit(); await ToSignal(GetTree().CreateTimer(.06), SceneTreeTimer.SignalName.Timeout);
            if (!CaptureFrame("res://Tests/animation-material-elf-hurt.png", "精灵受击")) throw new InvalidOperationException("无法保存精灵受击截图。");
            await ToSignal(GetTree().CreateTimer(.28), SceneTreeTimer.SignalName.Timeout);
            SetState("死亡"); hero.PlayDeath(); await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
            if (hero.IsDedicatedDeathVisible) throw new InvalidOperationException("精灵死亡错误使用了重剑手倒地精灵。");
            if (!CaptureFrame("res://Tests/animation-material-elf-death.png", "精灵死亡")) throw new InvalidOperationException("无法保存精灵死亡截图。");
            SelectCharacter(PixelHeroActor.CharacterKind.Greatsword);
            GD.Print("ANIMATION_MATERIAL_SMOKE_PASS: fixed idle anchors + dedicated bow/tome actions/effects + distinct greatsword/elf hurt/death; screenshots="
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
    public enum CharacterKind { Greatsword, Elf }
    public enum Loadout { None, RightSword, LeftShield, SwordAndShield, DualSwords, TwoHandedWeapon, Bow, Tome }
    private AnimatedSprite2D actionSprite;
    private SpriteFrames swordFrames;
    private SpriteFrames equipmentFrames;
    private SpriteFrames elfFrames;
    private SpriteFrames bowFrames;
    private SpriteFrames tomeFrames;
    private Sprite2D leftHandEquipment;
    private Sprite2D rightHandEquipment;
    private Sprite2D twoHandEquipment;
    private Sprite2D actionEffect;
    private Sprite2D deathSprite;
    private CharacterRig2D greatswordRig;
    // 当前拆件未达到原角色外观质量，默认继续使用已验收的完整像素动作表。
    // 骨骼场景保留为开发资源，但不得在通过外观对比验收前替换默认人物。
    private bool rigPreviewEnabled = true;
    private Tween tween;
    private Vector2 home;
    private Loadout loadout;
    private CharacterKind characterKind;
    public bool IsIdlePlaying => UsesSkeletalRig
        ? greatswordRig?.IsIdlePlaying ?? false
        : actionSprite != null && actionSprite.Visible && actionSprite.IsPlaying() && actionSprite.Animation == "idle";
    public int CurrentFrame => UsesSkeletalRig ? greatswordRig?.CurrentFrame ?? -1 : actionSprite?.Frame ?? -1;
    public Vector2 RightHandPosition => UsesSkeletalRig ? greatswordRig?.RightHandPosition ?? Vector2.Zero : rightHandEquipment?.Position ?? Vector2.Zero;
    public bool IsAttackEffectVisible => UsesSkeletalRig ? greatswordRig?.IsAttackEffectVisible ?? false : actionEffect?.Visible ?? false;
    public bool IsDedicatedDeathVisible => UsesSkeletalRig ? greatswordRig?.IsDedicatedDeathVisible ?? false : deathSprite?.Visible ?? false;
    public bool UsesSkeletalRig => rigPreviewEnabled && characterKind == CharacterKind.Greatsword && greatswordRig != null && greatswordRig.Visible;
    public int RigBoneCount => greatswordRig?.BoneCount ?? 0;
    public int RigSocketCount => greatswordRig?.SocketCount ?? 0;
    private Vector2 leftRestPosition;
    private Vector2 rightRestPosition;
    private Vector2 twoHandRestPosition;
    private float leftRestRotation;
    private float rightRestRotation;
    private float twoHandRestRotation;
    public string LoadoutLabel => characterKind == CharacterKind.Elf ? "默认弓箭" : loadout switch { Loadout.RightSword => "右手剑", Loadout.LeftShield => "左手盾", Loadout.SwordAndShield => "剑盾", Loadout.DualSwords => "双剑", Loadout.TwoHandedWeapon => "双手武器", Loadout.Bow => "弓箭", Loadout.Tome => "法器", _ => "空手" };

    public override void _Ready()
    {
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        swordFrames = CreateActionFrames(Load("swordmaster_pixel_action_sheet.png"));
        equipmentFrames = CreateActionFrames(Load("swordmaster_pixel_action_equipment_sheet.png"));
        elfFrames = CreateActionFrames(Load("isera_pixel_action_sheet_v3.png"), hurtFrame: 3, deathFrame: 6);
        bowFrames = CreateActionFrames(Load("swordmaster_bow_action_sheet_v2.png"));
        tomeFrames = CreateActionFrames(Load("swordmaster_tome_action_sheet_v2.png"));
        actionSprite = new AnimatedSprite2D { SpriteFrames = swordFrames, Scale = Vector2.One * .32f, ZIndex = 1 };
        AddChild(actionSprite);
        actionSprite.FrameChanged += UpdateEquipmentPose;
        leftHandEquipment = CreateEquipmentSprite(2); rightHandEquipment = CreateEquipmentSprite(2); twoHandEquipment = CreateEquipmentSprite(3);
        AddChild(leftHandEquipment); AddChild(rightHandEquipment); AddChild(twoHandEquipment);
        actionEffect = CreateEquipmentSprite(5); AddChild(actionEffect);
        deathSprite = new Sprite2D { Texture = Load("swordmaster_death_pose_v2.png"), Scale = Vector2.One * .10f, Position = new Vector2(0, 38), ZIndex = 1, Visible = false };
        AddChild(deathSprite);
        greatswordRig = ResourceLoader.Load<PackedScene>("res://Scenes/Characters/SwordmasterRig.tscn").Instantiate<CharacterRig2D>();
        greatswordRig.ZIndex = 2;
        greatswordRig.Visible = false;
        AddChild(greatswordRig);
        SetCharacter(CharacterKind.Greatsword);
    }

    private static Sprite2D CreateEquipmentSprite(int zIndex) => new() { TextureFilter = CanvasItem.TextureFilterEnum.Nearest, ZIndex = zIndex, Visible = false };
    private static Texture2D Load(string file) => ResourceLoader.Load<Texture2D>($"res://Resources/Images/Characters/Pixel/{file}");
    private static SpriteFrames CreateActionFrames(Texture2D sheet, int hurtFrame = 6, int deathFrame = 6)
    {
        var frames = new SpriteFrames();
        AddAnimation(frames, "idle", sheet, 0, 1, loop: true, speed: 1.5f);
        AddAnimation(frames, "walk", sheet, 2, 3, loop: true, speed: 8f);
        AddAnimation(frames, "attack", sheet, 4, 5, 0, loop: false, speed: 9f);
        AddAnimation(frames, "hurt", sheet, hurtFrame, 0, loop: false, speed: 8f);
        AddAnimation(frames, "death", sheet, deathFrame, loop: false, speed: 1f);
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
        if (characterKind == CharacterKind.Elf)
        {
            loadout = Loadout.Bow;
            ClearEquipment();
            ResetActor();
            return;
        }
        loadout = next;
        if (UsesSkeletalRig)
        {
            greatswordRig.SetLoadout((CharacterRig2D.RigLoadout)(int)next);
            ClearEquipment();
            ResetActor();
            return;
        }
        ClearEquipment();
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
            case Loadout.Bow: break; // 使用完整弓箭动作图集。
            case Loadout.Tome: break; // 使用完整法典动作图集。
        }
        leftRestPosition = leftHandEquipment.Position; rightRestPosition = rightHandEquipment.Position; twoHandRestPosition = twoHandEquipment.Position;
        leftRestRotation = leftHandEquipment.Rotation; rightRestRotation = rightHandEquipment.Rotation; twoHandRestRotation = twoHandEquipment.Rotation;
        ResetActor();
    }

    public void SetCharacter(CharacterKind kind)
    {
        characterKind = kind;
        ClearEquipment();
        if (greatswordRig != null) greatswordRig.Visible = rigPreviewEnabled && kind == CharacterKind.Greatsword;
        if (kind == CharacterKind.Elf)
        {
            loadout = Loadout.Bow;
            leftRestPosition = rightRestPosition = twoHandRestPosition = Vector2.Zero;
            leftRestRotation = rightRestRotation = twoHandRestRotation = 0;
            ResetActor();
            return;
        }
        SetLoadout(Loadout.TwoHandedWeapon);
    }

    public void SetHomePosition(Vector2 position) { home = position; Position = position; }
    private void PlayActionSheet(string animation)
    {
        deathSprite.Visible = false;
        actionSprite.SpriteFrames = characterKind == CharacterKind.Elf ? elfFrames : loadout switch
        {
            Loadout.TwoHandedWeapon => swordFrames,
            Loadout.Bow => bowFrames,
            Loadout.Tome => tomeFrames,
            _ => equipmentFrames,
        };
        actionSprite.Visible = true;
        bool showEquipment = characterKind == CharacterKind.Greatsword
            && loadout is not (Loadout.TwoHandedWeapon or Loadout.Bow or Loadout.Tome);
        leftHandEquipment.Visible = showEquipment && leftHandEquipment.Texture != null;
        rightHandEquipment.Visible = showEquipment && rightHandEquipment.Texture != null;
        twoHandEquipment.Visible = showEquipment && twoHandEquipment.Texture != null;
        actionEffect.Visible = false;
        actionSprite.Stop();
        actionSprite.Frame = 0;
        actionSprite.Play(animation);
        UpdateEquipmentPose();
    }
    private void UpdateEquipmentPose()
    {
        if (leftHandEquipment == null || rightHandEquipment == null || twoHandEquipment == null || actionEffect == null) return;
        actionEffect.Visible = false;
        int sourceFrame = ResolveSourceFrame(actionSprite.Animation, actionSprite.Frame);
        Vector2 sourceCorrection = GetSourceFrameCorrection(sourceFrame);
        actionSprite.Offset = sourceCorrection;
        Vector2 renderedCorrection = sourceCorrection * actionSprite.Scale;
        if (characterKind == CharacterKind.Elf) return;
        leftHandEquipment.Position = leftRestPosition + renderedCorrection;
        rightHandEquipment.Position = rightRestPosition + renderedCorrection;
        twoHandEquipment.Position = twoHandRestPosition + renderedCorrection;
        leftHandEquipment.Rotation = leftRestRotation; rightHandEquipment.Rotation = rightRestRotation; twoHandEquipment.Rotation = twoHandRestRotation;

        string animation = actionSprite.Animation;
        int frame = actionSprite.Frame;
        if (animation == "idle")
        {
            if (frame == 1) OffsetVisibleEquipment(new Vector2(0, 1), 0);
            return;
        }
        if (animation == "walk")
        {
            OffsetVisibleEquipment(frame == 0 ? new Vector2(-2, 1) : new Vector2(3, -1),
                frame == 0 ? Mathf.DegToRad(-3) : Mathf.DegToRad(3));
            return;
        }
        if (animation is "hurt" or "death")
        {
            if (frame == 0) OffsetVisibleEquipment(new Vector2(10, 8), Mathf.DegToRad(12));
            return;
        }
        if (animation == "attack" && frame == 1 && loadout is Loadout.Bow or Loadout.Tome)
        {
            bool isBow = loadout == Loadout.Bow;
            actionEffect.Texture = Load(isBow ? "fx_bow_arrow_trail.png" : "fx_tome_cast_rune.png");
            actionEffect.Position = (isBow ? new Vector2(72, -13) : new Vector2(58, -19)) + renderedCorrection;
            actionEffect.Scale = Vector2.One * (isBow ? .32f : .075f);
            actionEffect.Rotation = 0;
            actionEffect.Visible = true;
            return;
        }
        if (animation != "attack" || loadout is Loadout.TwoHandedWeapon or Loadout.Bow or Loadout.Tome) return;
        if (frame == 0)
        {
            if (rightHandEquipment.Visible) { rightHandEquipment.Position = new Vector2(-10, -35); rightHandEquipment.Rotation = Mathf.DegToRad(-70); }
            if (leftHandEquipment.Visible && loadout == Loadout.DualSwords) { leftHandEquipment.Position = new Vector2(-28, -8); leftHandEquipment.Rotation = Mathf.DegToRad(30); }
            if (twoHandEquipment.Visible) twoHandEquipment.Position = new Vector2(-7, -28);
        }
        else if (frame == 1)
        {
            if (rightHandEquipment.Visible) { rightHandEquipment.Position = new Vector2(26, 22); rightHandEquipment.Rotation = Mathf.DegToRad(78); }
            if (leftHandEquipment.Visible && loadout == Loadout.DualSwords) { leftHandEquipment.Position = new Vector2(-18, 16); leftHandEquipment.Rotation = Mathf.DegToRad(-22); }
            if (twoHandEquipment.Visible) twoHandEquipment.Position = new Vector2(24, 20);
        }
    }
    private int ResolveSourceFrame(string animation, int animationFrame)
    {
        int[] sequence = animation switch
        {
            "idle" => new[] { 0, 1 },
            "walk" => new[] { 2, 3 },
            "attack" => new[] { 4, 5, 0 },
            "hurt" => characterKind == CharacterKind.Elf ? new[] { 3, 0 } : new[] { 6, 0 },
            "death" => new[] { 6 },
            _ => new[] { 0 },
        };
        return sequence[Math.Clamp(animationFrame, 0, sequence.Length - 1)];
    }
    private Vector2 GetSourceFrameCorrection(int sourceFrame)
    {
        float[] centers;
        float[] bottoms;
        if (ReferenceEquals(actionSprite.SpriteFrames, equipmentFrames))
        {
            centers = new[] { 144f, 142f, 134f, 146f, 144f, 102f, 158f };
            bottoms = new[] { 616f, 616f, 616f, 616f, 616f, 612f, 614f };
        }
        else if (ReferenceEquals(actionSprite.SpriteFrames, bowFrames))
        {
            centers = new[] { 144f, 145f, 144f, 145f, 146f, 145f, 145f };
            bottoms = new[] { 616f, 616f, 616f, 616f, 616f, 616f, 616f };
        }
        else if (ReferenceEquals(actionSprite.SpriteFrames, tomeFrames))
        {
            centers = new[] { 143f, 144f, 144f, 145f, 145f, 144f, 143f };
            bottoms = new[] { 616f, 616f, 616f, 616f, 614f, 616f, 616f };
        }
        else if (ReferenceEquals(actionSprite.SpriteFrames, elfFrames))
        {
            centers = new[] { 144f, 144f, 145f, 145f, 144f, 145f, 145f };
            bottoms = new[] { 616f, 614f, 614f, 614f, 616f, 614f, 616f };
        }
        else
        {
            centers = new[] { 144f, 142f, 134f, 146f, 144f, 144f, 139f };
            bottoms = new[] { 616f, 616f, 616f, 616f, 616f, 616f, 616f };
        }
        int index = Math.Clamp(sourceFrame, 0, 6);
        return new Vector2(144f - centers[index], 616f - bottoms[index]);
    }
    private void OffsetVisibleEquipment(Vector2 offset, float rotationOffset)
    {
        foreach (Sprite2D equipment in new[] { leftHandEquipment, rightHandEquipment, twoHandEquipment })
        {
            if (!equipment.Visible) continue;
            equipment.Position += offset;
            equipment.Rotation += rotationOffset;
        }
    }
    private void RestoreIdlePresentation()
    {
        if (UsesSkeletalRig)
        {
            actionSprite.Visible = false;
            deathSprite.Visible = false;
            actionEffect.Visible = false;
            ClearEquipment();
            greatswordRig.PlayIdle();
            return;
        }
        PlayActionSheet("idle");
    }
    public void ResetActor() { tween?.Kill(); Position = home; Rotation = 0; Scale = Vector2.One; Modulate = Colors.White; Visible = true; deathSprite.Visible = false; RestoreIdlePresentation(); }
    public void PlayMove()
    {
        ResetActor();
        if (UsesSkeletalRig) greatswordRig.PlayMove(); else PlayActionSheet("walk");
        tween = CreateTween().SetTrans(Tween.TransitionType.Sine);
        Vector2 destination = home + new Vector2(112, -64);
        Vector2 midpoint = home.Lerp(destination, .5f) + Vector2.Up * 12f;
        tween.TweenProperty(this, "position", midpoint, .16);
        tween.TweenProperty(this, "position", destination, .16);
        tween.TweenCallback(Callable.From(() => { home = Position; RestoreIdlePresentation(); }));
    }
    public void PlayAttack()
    {
        ResetActor();
        if (UsesSkeletalRig) greatswordRig.PlayAttack(); else PlayActionSheet("attack");
        tween = CreateTween();
        tween.TweenInterval(UsesSkeletalRig ? .72f : 3f / 9f);
        tween.TweenCallback(Callable.From(RestoreIdlePresentation));
    }
    public void PlayHit()
    {
        ResetActor();
        if (UsesSkeletalRig) greatswordRig.PlayHurt(); else PlayActionSheet("hurt");
        tween = CreateTween(); tween.TweenProperty(this, "modulate", new Color("ffaaaa"), .07);
        tween.Parallel().TweenProperty(this, "position:x", home.X - 8, .10);
        tween.TweenProperty(this, "modulate", Colors.White, .15);
        tween.Parallel().TweenProperty(this, "position:x", home.X, .15);
        tween.TweenCallback(Callable.From(RestoreIdlePresentation));
    }
    public void PlayDeath()
    {
        ResetActor();
        if (characterKind == CharacterKind.Greatsword)
        {
            if (UsesSkeletalRig)
            {
                actionSprite.Stop(); actionSprite.Visible = false; actionEffect.Visible = false; deathSprite.Visible = false;
                ClearEquipment();
                greatswordRig.PlayDeath();
                return;
            }
            actionSprite.Stop(); actionSprite.Visible = false; actionEffect.Visible = false;
            deathSprite.Visible = true;
            ConfigureFallenEquipment();
            tween = CreateTween().SetTrans(Tween.TransitionType.Quad);
            tween.TweenProperty(this, "position:y", home.Y + 5, .28);
            tween.Parallel().TweenProperty(this, "modulate", new Color(.72f, .75f, .80f, 1f), .28);
            return;
        }
        PlayActionSheet("death"); tween = CreateTween().SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(this, "position:y", home.Y + 7, .36);
        tween.Parallel().TweenProperty(this, "modulate", new Color(.86f, .88f, .90f, 1f), .36);
    }

    private void ConfigureFallenEquipment()
    {
        ClearEquipment();
        switch (loadout)
        {
            case Loadout.RightSword:
                Attach(rightHandEquipment, Load("equipment_sword.png"), new Vector2(40, 42), .050f, Mathf.DegToRad(78)); break;
            case Loadout.LeftShield:
                Attach(leftHandEquipment, Load("equipment_shield.png"), new Vector2(-42, 39), .045f, Mathf.DegToRad(-18)); break;
            case Loadout.SwordAndShield:
                Attach(rightHandEquipment, Load("equipment_sword.png"), new Vector2(42, 42), .050f, Mathf.DegToRad(78));
                Attach(leftHandEquipment, Load("equipment_shield.png"), new Vector2(-42, 39), .045f, Mathf.DegToRad(-18)); break;
            case Loadout.DualSwords:
                Attach(leftHandEquipment, Load("equipment_sword.png"), new Vector2(-42, 43), .050f, Mathf.DegToRad(-75));
                Attach(rightHandEquipment, Load("equipment_sword.png"), new Vector2(42, 42), .050f, Mathf.DegToRad(78)); break;
            case Loadout.TwoHandedWeapon:
                Attach(twoHandEquipment, Load("equipment_two_hand_sword.png"), new Vector2(48, 39), .055f, Mathf.DegToRad(82)); break;
            case Loadout.Bow:
                Attach(twoHandEquipment, Load("equipment_bow.png"), new Vector2(48, 39), .045f, Mathf.DegToRad(78)); break;
            case Loadout.Tome:
                Attach(twoHandEquipment, Load("equipment_tome.png"), new Vector2(42, 39), .040f, Mathf.DegToRad(18)); break;
        }
    }
}
