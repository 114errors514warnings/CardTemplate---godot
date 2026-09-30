using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 重剑手局内模块化骨骼角色。身体拆件绑定 Bone2D，装备绑定手部 Socket，
/// 所有动作只改变骨骼，不替换人物整图。
/// </summary>
public partial class CharacterRig2D : Node2D
{
    public enum RigLoadout { None, RightSword, LeftShield, SwordAndShield, DualSwords, TwoHandedWeapon, Bow, Tome }

    private const string PartsRoot = "res://Resources/Images/Characters/Rigs/Swordmaster/PartsV2/";
    private const string PixelRoot = "res://Resources/Images/Characters/Pixel/";

    private readonly Dictionary<string, Bone2D> bones = new();
    private Node2D visualRoot;
    private Skeleton2D skeleton;
    private AnimationPlayer animationPlayer;
    private AnimationTree animationTree;
    private Node2D leftSocket;
    private Node2D rightSocket;
    private Sprite2D leftEquipment;
    private Sprite2D rightEquipment;
    private Sprite2D effect;
    private RigLoadout loadout = RigLoadout.TwoHandedWeapon;
    private bool dead;

    public bool IsIdlePlaying => animationPlayer != null && animationPlayer.IsPlaying()
        && animationPlayer.CurrentAnimation.ToString().StartsWith("idle", StringComparison.Ordinal);
    public int CurrentFrame => animationPlayer == null ? -1 : (int)Math.Floor(animationPlayer.CurrentAnimationPosition * 20.0);
    public Vector2 RightHandPosition => rightSocket == null ? Vector2.Zero : ToLocal(rightSocket.GlobalPosition);
    public bool IsAttackEffectVisible => effect?.Visible ?? false;
    public bool IsDedicatedDeathVisible => dead && animationPlayer?.CurrentAnimation == "death";
    public int BoneCount => bones.Count;
    public int SocketCount => (leftSocket == null ? 0 : 1) + (rightSocket == null ? 0 : 1);
    public RigLoadout Loadout => loadout;

    public override void _Ready()
    {
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        BuildRig();
        BuildAnimationLibrary();
        SetLoadout(loadout);
        PlayIdle();
    }

    public override void _Process(double delta)
    {
        // 像素角色只在显示根节点做整数像素对齐；动作本身不改角色根坐标。
        visualRoot.Position = visualRoot.Position.Round();
        if (effect == null || animationPlayer == null) return;
        string current = animationPlayer.CurrentAnimation;
        double time = animationPlayer.CurrentAnimationPosition;
        effect.Visible = current is "attack_bow" or "attack_tome" && time >= .13 && time <= .36;
    }

    private void BuildRig()
    {
        visualRoot = new Node2D { Name = "VisualRoot", Scale = Vector2.One * .78f };
        AddChild(visualRoot);
        skeleton = new Skeleton2D { Name = "Skeleton2D" };

        Bone2D pelvis = AddBone("Pelvis", skeleton, Vector2.Zero);
        AddPart(pelvis, "pelvis.png", Vector2.Zero, .155f, 1);

        Bone2D spine = AddBone("Spine", pelvis, new Vector2(0, -24));
        AddPart(spine, "torso.png", new Vector2(0, -4), .155f, 2);
        AddPart(spine, "cape.png", new Vector2(0, 3), .155f, -4);

        Bone2D head = AddBone("Head", spine, new Vector2(0, -29));
        AddPart(head, "head.png", new Vector2(0, -8), .155f, 5);

        Bone2D armLUpper = AddBone("ArmLUpper", spine, new Vector2(-16, -13));
        AddPart(armLUpper, "arm_l_upper.png", new Vector2(-1, 10), .145f, 1);
        Bone2D armLLower = AddBone("ArmLLower", armLUpper, new Vector2(-1, 22));
        AddPart(armLLower, "arm_l_lower.png", new Vector2(0, 10), .135f, 1);
        Bone2D handL = AddBone("HandL", armLLower, new Vector2(0, 21));
        AddPart(handL, "hand_l.png", new Vector2(0, 5), .125f, 3);

        Bone2D armRUpper = AddBone("ArmRUpper", spine, new Vector2(16, -13));
        AddPart(armRUpper, "arm_r_upper.png", new Vector2(1, 10), .145f, 3);
        Bone2D armRLower = AddBone("ArmRLower", armRUpper, new Vector2(1, 22));
        AddPart(armRLower, "arm_r_lower.png", new Vector2(0, 10), .135f, 3);
        Bone2D handR = AddBone("HandR", armRLower, new Vector2(0, 21));
        AddPart(handR, "hand_r.png", new Vector2(0, 5), .125f, 4);

        Bone2D legLUpper = AddBone("LegLUpper", pelvis, new Vector2(-9, 8));
        AddPart(legLUpper, "leg_l_upper.png", new Vector2(0, 12), .14f, 0);
        Bone2D legLLower = AddBone("LegLLower", legLUpper, new Vector2(0, 26));
        AddPart(legLLower, "leg_l_lower.png", new Vector2(0, 12), .13f, 0);
        Bone2D footL = AddBone("FootL", legLLower, new Vector2(0, 25));
        AddPart(footL, "foot_l.png", new Vector2(-3, 5), .13f, 1);

        Bone2D legRUpper = AddBone("LegRUpper", pelvis, new Vector2(9, 8));
        AddPart(legRUpper, "leg_r_upper.png", new Vector2(0, 12), .14f, 1);
        Bone2D legRLower = AddBone("LegRLower", legRUpper, new Vector2(0, 26));
        AddPart(legRLower, "leg_r_lower.png", new Vector2(0, 12), .13f, 1);
        Bone2D footR = AddBone("FootR", legRLower, new Vector2(0, 25));
        AddPart(footR, "foot_r.png", new Vector2(3, 5), .13f, 2);

        leftSocket = new Node2D { Name = "WeaponSocketL", Position = new Vector2(0, 8), ZIndex = 10 };
        handL.AddChild(leftSocket);
        rightSocket = new Node2D { Name = "WeaponSocketR", Position = new Vector2(0, 8), ZIndex = 10 };
        handR.AddChild(rightSocket);
        leftEquipment = NewEquipment("EquipmentL"); leftSocket.AddChild(leftEquipment);
        rightEquipment = NewEquipment("EquipmentR"); rightSocket.AddChild(rightEquipment);
        visualRoot.AddChild(skeleton);

        effect = NewEquipment("AttackEffect");
        effect.ZIndex = 20; effect.Visible = false;
        visualRoot.AddChild(effect);

        animationPlayer = new AnimationPlayer { Name = "AnimationPlayer", RootNode = new NodePath("..") };
        AddChild(animationPlayer);
        animationPlayer.AnimationFinished += OnAnimationFinished;
        // AnimationTree 作为后续状态机/BlendSpace 接口保留；当前动作由同一动画库直接驱动。
        animationTree = new AnimationTree { Name = "AnimationTree", Active = false };
        AddChild(animationTree);
    }

    private Bone2D AddBone(string name, Node parent, Vector2 position)
    {
        var bone = new Bone2D { Name = name, Position = position };
        bone.SetAutocalculateLengthAndAngle(false);
        bone.SetLength(20);
        bone.Rest = bone.Transform;
        parent.AddChild(bone);
        bones[name] = bone;
        return bone;
    }

    private static Sprite2D NewEquipment(string name) => new()
    {
        Name = name,
        Centered = true,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        Visible = false
    };

    private static void AddPart(Node2D parent, string file, Vector2 position, float scale, int zIndex)
    {
        var sprite = new Sprite2D
        {
            Name = file.Replace(".png", ""),
            Texture = ResourceLoader.Load<Texture2D>(PartsRoot + file),
            Position = position,
            Scale = Vector2.One * scale,
            ZIndex = zIndex,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest
        };
        parent.AddChild(sprite);
    }

    private void BuildAnimationLibrary()
    {
        var library = new AnimationLibrary();
        library.AddAnimation("idle", MakeIdle());
        library.AddAnimation("idle_guard", MakeGuardIdle());
        library.AddAnimation("idle_twohand", MakeTwoHandIdle());
        library.AddAnimation("idle_bow", MakeBowIdle());
        library.AddAnimation("idle_tome", MakeTomeIdle());
        library.AddAnimation("move", MakeMove());
        library.AddAnimation("attack_onehand", MakeOneHandAttack());
        library.AddAnimation("attack_twohand", MakeTwoHandAttack());
        library.AddAnimation("attack_bow", MakeBowAttack());
        library.AddAnimation("attack_tome", MakeTomeAttack());
        library.AddAnimation("hurt", MakeHurt());
        library.AddAnimation("death", MakeDeath());
        animationPlayer.AddAnimationLibrary("", library);
    }

    private static Animation NewAnimation(double length, bool loop = false) => new()
    {
        Length = (float)length,
        LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None,
        Step = 1.0f / 12.0f
    };

    private static void AddRotation(Animation animation, string nodePath, params (double time, float degrees)[] keys)
    {
        int track = animation.AddTrack(Animation.TrackType.Value);
        animation.TrackSetPath(track, new NodePath(nodePath + ":rotation"));
        foreach ((double time, float degrees) in keys)
            animation.TrackInsertKey(track, time, Mathf.DegToRad(degrees));
    }

    private static void AddPosition(Animation animation, string nodePath, params (double time, Vector2 position)[] keys)
    {
        int track = animation.AddTrack(Animation.TrackType.Value);
        animation.TrackSetPath(track, new NodePath(nodePath + ":position"));
        foreach ((double time, Vector2 position) in keys) animation.TrackInsertKey(track, time, position);
    }

    private const string Rig = "VisualRoot/Skeleton2D/";
    private static Animation MakeIdle()
    {
        Animation a = NewAnimation(.8, true);
        AddRotation(a, Rig + "Pelvis/Spine", (0, 0), (.4, -1), (.8, 0));
        AddRotation(a, Rig + "Pelvis/Spine/Head", (0, 0), (.4, 1), (.8, 0));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper", (0, 2), (.4, 3), (.8, 2));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper", (0, -2), (.4, -3), (.8, -2));
        return a;
    }

    private static Animation MakeMove()
    {
        Animation a = NewAnimation(.48, true);
        AddRotation(a, Rig + "Pelvis", (0, -1), (.24, 1), (.48, -1));
        AddRotation(a, Rig + "Pelvis/LegLUpper", (0, -13), (.24, 15), (.48, -13));
        AddRotation(a, Rig + "Pelvis/LegRUpper", (0, 15), (.24, -13), (.48, 15));
        AddRotation(a, Rig + "Pelvis/LegLUpper/LegLLower", (0, 16), (.24, -5), (.48, 16));
        AddRotation(a, Rig + "Pelvis/LegRUpper/LegRLower", (0, -5), (.24, 16), (.48, -5));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper", (0, 8), (.24, -7), (.48, 8));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper", (0, -7), (.24, 8), (.48, -7));
        return a;
    }

    private static Animation MakeTwoHandIdle()
    {
        Animation a = NewAnimation(.8, true);
        AddRotation(a, Rig + "Pelvis/Spine", (0, 0), (.4, -1), (.8, 0));
        AddRotation(a, Rig + "Pelvis/Spine/Head", (0, 0), (.4, 1), (.8, 0));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper", (0, -28), (.4, -27), (.8, -28));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper/ArmLLower", (0, 20), (.4, 19), (.8, 20));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper", (0, 28), (.4, 27), (.8, 28));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper/ArmRLower", (0, -20), (.4, -19), (.8, -20));
        return a;
    }

    private static Animation MakeGuardIdle()
    {
        Animation a = NewAnimation(.8, true);
        AddRotation(a, Rig + "Pelvis/Spine", (0, 0), (.4, -1), (.8, 0));
        AddRotation(a, Rig + "Pelvis/Spine/Head", (0, 0), (.4, 1), (.8, 0));
        // 副手收向身体中央形成真实盾防；主手略向外，避免剑身与前臂完全重叠。
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper", (0, -42), (.4, -41), (.8, -42));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper/ArmLLower", (0, 24), (.4, 23), (.8, 24));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper", (0, -12), (.4, -11), (.8, -12));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper/ArmRLower", (0, 5), (.4, 4), (.8, 5));
        return a;
    }

    private static Animation MakeBowIdle()
    {
        Animation a = NewAnimation(.8, true);
        AddRotation(a, Rig + "Pelvis/Spine", (0, 0), (.4, -1), (.8, 0));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper", (0, -18), (.4, -17), (.8, -18));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper", (0, 16), (.4, 15), (.8, 16));
        return a;
    }

    private static Animation MakeTomeIdle()
    {
        Animation a = NewAnimation(.8, true);
        AddRotation(a, Rig + "Pelvis/Spine", (0, 0), (.4, -1), (.8, 0));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper", (0, -27), (.4, -26), (.8, -27));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper/ArmLLower", (0, 22), (.4, 21), (.8, 22));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper", (0, 12), (.4, 11), (.8, 12));
        return a;
    }

    private static Animation MakeOneHandAttack()
    {
        Animation a = NewAnimation(.58);
        AddRotation(a, Rig + "Pelvis/Spine", (0, 0), (.16, -8), (.34, 11), (.58, 0));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper", (0, -2), (.16, -68), (.34, 48), (.58, -2));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper/ArmRLower", (0, 0), (.16, -24), (.34, 18), (.58, 0));
        return a;
    }

    private static Animation MakeTwoHandAttack()
    {
        Animation a = NewAnimation(.68);
        AddRotation(a, Rig + "Pelvis/Spine", (0, 0), (.2, -11), (.4, 14), (.68, 0));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper", (0, -14), (.2, -78), (.4, 42), (.68, -14));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper", (0, 14), (.2, -48), (.4, 58), (.68, 14));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper/ArmRLower", (0, 8), (.2, -22), (.4, 16), (.68, 8));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper/ArmLLower", (0, -8), (.2, 18), (.4, -20), (.68, -8));
        return a;
    }

    private static Animation MakeBowAttack()
    {
        Animation a = NewAnimation(.7);
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper", (0, 12), (.18, -72), (.46, -72), (.7, 12));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper/ArmLLower", (0, 0), (.18, 5), (.46, 5), (.7, 0));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper", (0, -12), (.18, -105), (.46, -76), (.7, -12));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper/ArmRLower", (0, 0), (.18, 115), (.46, 42), (.7, 0));
        return a;
    }

    private static Animation MakeTomeAttack()
    {
        Animation a = NewAnimation(.72);
        AddRotation(a, Rig + "Pelvis/Spine", (0, 0), (.22, -4), (.48, 4), (.72, 0));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper", (0, 10), (.22, 72), (.48, 50), (.72, 10));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper", (0, -10), (.22, -62), (.48, -45), (.72, -10));
        return a;
    }

    private static Animation MakeHurt()
    {
        Animation a = NewAnimation(.42);
        AddRotation(a, Rig + "Pelvis", (0, 0), (.09, -7), (.24, 3), (.42, 0));
        AddRotation(a, Rig + "Pelvis/Spine", (0, 0), (.09, -13), (.24, 4), (.42, 0));
        AddRotation(a, Rig + "Pelvis/Spine/Head", (0, 0), (.09, 10), (.24, -3), (.42, 0));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper", (0, 2), (.09, 22), (.42, 2));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper", (0, -2), (.09, -28), (.42, -2));
        return a;
    }

    private static Animation MakeDeath()
    {
        Animation a = NewAnimation(.65);
        AddRotation(a, "VisualRoot", (0, 0), (.12, -8), (.48, 90), (.65, 90));
        AddPosition(a, "VisualRoot", (0, Vector2.Zero), (.12, new Vector2(-2, 1)), (.48, new Vector2(10, 42)), (.65, new Vector2(10, 42)));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper", (0, 2), (.32, 32), (.65, 42));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper", (0, -2), (.32, -35), (.65, -48));
        return a;
    }

    public void SetLoadout(RigLoadout next)
    {
        loadout = next;
        HideEquipment();
        switch (next)
        {
            case RigLoadout.RightSword:
                Equip(rightEquipment, "equipment_sword.png", .064f, new Vector2(280, 820), new Vector2(4, 0), -25); break;
            case RigLoadout.LeftShield:
                Equip(leftEquipment, "equipment_shield.png", .058f, new Vector2(418, 436), new Vector2(-1, 1), 0); break;
            case RigLoadout.SwordAndShield:
                Equip(rightEquipment, "equipment_sword.png", .064f, new Vector2(280, 820), new Vector2(4, 0), -25);
                Equip(leftEquipment, "equipment_shield.png", .058f, new Vector2(418, 436), new Vector2(-1, 1), 0); break;
            case RigLoadout.DualSwords:
                Equip(rightEquipment, "equipment_sword.png", .064f, new Vector2(280, 820), new Vector2(4, 0), -25);
                Equip(leftEquipment, "equipment_sword.png", .064f, new Vector2(280, 820), new Vector2(-4, 0), 25, true); break;
            case RigLoadout.TwoHandedWeapon:
                Equip(rightEquipment, "equipment_two_hand_sword.png", .071f, new Vector2(278, 1370), new Vector2(6, 0), 10); break;
            case RigLoadout.Bow:
                Equip(leftEquipment, "equipment_bow.png", .058f, new Vector2(372, 650), new Vector2(0, 0), 0); break;
            case RigLoadout.Tome:
                Equip(leftEquipment, "equipment_tome.png", .051f, new Vector2(514, 382), new Vector2(0, -2), -6); break;
        }
        ConfigureEffect();
        if (IsInsideTree()) PlayIdle();
    }

    private void HideEquipment()
    {
        foreach (Sprite2D sprite in new[] { leftEquipment, rightEquipment })
        {
            if (sprite == null) continue;
            sprite.Visible = false; sprite.Texture = null; sprite.FlipH = false;
            sprite.Centered = true; sprite.Offset = Vector2.Zero;
            sprite.Position = Vector2.Zero; sprite.Rotation = 0; sprite.Scale = Vector2.One;
        }
    }

    private static void Equip(Sprite2D sprite, string file, float scale, Vector2 gripPixels, Vector2 socketOffset, float degrees, bool flip = false)
    {
        Texture2D texture = ResourceLoader.Load<Texture2D>(PixelRoot + file);
        sprite.Texture = texture;
        sprite.Centered = false;
        Vector2 resolvedGrip = flip ? new Vector2(texture.GetWidth() - gripPixels.X, gripPixels.Y) : gripPixels;
        // Offset 将图片中的实际握点放到 Sprite2D 原点，之后所有旋转都围绕手部 Socket 发生。
        sprite.Offset = -resolvedGrip;
        sprite.Scale = Vector2.One * scale;
        sprite.Position = socketOffset;
        sprite.Rotation = Mathf.DegToRad(degrees);
        sprite.FlipH = flip;
        sprite.Visible = true;
    }

    private void ConfigureEffect()
    {
        bool bow = loadout == RigLoadout.Bow;
        bool tome = loadout == RigLoadout.Tome;
        effect.Texture = bow ? ResourceLoader.Load<Texture2D>(PixelRoot + "fx_bow_arrow_trail.png")
            : tome ? ResourceLoader.Load<Texture2D>(PixelRoot + "fx_tome_cast_rune.png") : null;
        effect.Scale = Vector2.One * (bow ? .25f : .055f);
        effect.Position = bow ? new Vector2(62, -27) : new Vector2(43, -34);
        effect.Visible = false;
    }

    public void PlayIdle()
    {
        dead = false;
        visualRoot.Position = Vector2.Zero; visualRoot.Rotation = 0;
        Modulate = Colors.White;
        string idle = loadout switch
        {
            RigLoadout.LeftShield or RigLoadout.SwordAndShield => "idle_guard",
            RigLoadout.TwoHandedWeapon => "idle_twohand",
            RigLoadout.Bow => "idle_bow",
            RigLoadout.Tome => "idle_tome",
            _ => "idle"
        };
        animationPlayer.Stop();
        animationPlayer.Play(idle, .1);
    }

    public void PlayMove() { dead = false; animationPlayer.Play("move", .08); }
    public void PlayAttack()
    {
        dead = false;
        string animation = loadout switch
        {
            RigLoadout.TwoHandedWeapon => "attack_twohand",
            RigLoadout.Bow => "attack_bow",
            RigLoadout.Tome => "attack_tome",
            _ => "attack_onehand"
        };
        animationPlayer.Play(animation, .06);
    }
    public void PlayHurt() { dead = false; animationPlayer.Play("hurt", .04); }
    public void PlayDeath() { dead = true; animationPlayer.Play("death", .05); }

    private void OnAnimationFinished(StringName animationName)
    {
        if (animationName != "death") PlayIdle();
    }
}
