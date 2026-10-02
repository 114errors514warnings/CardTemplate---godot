using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// 局内模块化骨骼角色。身体拆件绑定 Bone2D，装备绑定手部 Socket，
/// 所有动作只改变骨骼，不替换人物整图。
/// </summary>
public partial class CharacterRig2D : Node2D
{
    public enum RigLoadout { None, RightSword, LeftShield, SwordAndShield, DualSwords, TwoHandedWeapon, Bow, Tome }

    private const string PartsRoot = "res://Resources/Images/Characters/Rigs/Swordmaster/PartsV2/";
    private const string ElfSkinPath = "res://Resources/Images/Characters/Rigs/Isera/isera_continuous_skin_v1.png";
    private const string PixelRoot = "res://Resources/Images/Characters/Pixel/";

    [Export] public bool IsElf { get; set; }

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
    private Sprite2D deathPose;
    private Polygon2D elfSkin;
    private RigLoadout loadout = RigLoadout.TwoHandedWeapon;
    private bool dead;
    private bool attackImpactSent;

    /// <summary>Raised at the authored contact/release pose, after the animation has advanced.</summary>
    public event Action AttackImpact;
    public float AttackImpactTime => loadout switch
    {
        RigLoadout.Bow => .34f,
        RigLoadout.Tome => .36f,
        RigLoadout.TwoHandedWeapon => .34f,
        _ => .30f
    };

    public bool IsIdlePlaying => animationPlayer != null && animationPlayer.IsPlaying()
        && animationPlayer.CurrentAnimation.ToString().StartsWith("idle", StringComparison.Ordinal);
    public int CurrentFrame => animationPlayer == null ? -1 : (int)Math.Floor(animationPlayer.CurrentAnimationPosition * 20.0);
    public string CurrentAnimationName => animationPlayer?.CurrentAnimation.ToString() ?? "";
    public double CurrentAnimationSeconds => animationPlayer?.CurrentAnimationPosition ?? -1;
    public Vector2 RightHandPosition => rightSocket == null ? Vector2.Zero : ToLocal(rightSocket.GlobalPosition);
    public Vector2 LeftHandPosition => leftSocket == null ? Vector2.Zero : ToLocal(leftSocket.GlobalPosition);
    public Vector2 LeftFootPosition => bones.TryGetValue("FootL", out var foot) ? ToLocal(foot.GlobalPosition) : Vector2.Zero;
    public Vector2 RightFootPosition => bones.TryGetValue("FootR", out var foot) ? ToLocal(foot.GlobalPosition) : Vector2.Zero;
    public Vector2 VisualRootPosition => visualRoot?.Position ?? Vector2.Zero;
    public bool IsDead => dead;
    public float MaxGripOffset => Math.Max(leftEquipment?.Visible == true ? leftEquipment.Position.Length() : 0,
        rightEquipment?.Visible == true ? rightEquipment.Position.Length() : 0);
    public float SecondaryGripError { get; private set; }
    public float SecondaryGripTargetDistance { get; private set; }
    public float SecondaryGripMaxReach { get; private set; }
    public bool IsAttackEffectVisible => effect?.Visible ?? false;
    public bool IsDedicatedDeathVisible => dead && deathPose?.Visible == true;
    public int ContinuousSkinVertexCount => elfSkin?.Polygon.Length ?? 0;
    public int BoneCount => bones.Count;
    public int SocketCount => (leftSocket == null ? 0 : 1) + (rightSocket == null ? 0 : 1);
    public RigLoadout Loadout => loadout;

    public override void _Ready()
    {
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        if (IsElf) loadout = RigLoadout.Bow;
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
        if (loadout == RigLoadout.TwoHandedWeapon && rightEquipment.Visible && !dead)
            SecondaryGripError = ApplyHandIk(true, rightEquipment.ToGlobal(new Vector2(0, -120)));
        else if (loadout == RigLoadout.Bow && leftEquipment.Visible && current == "attack_bow" && time >= .10 && time <= .48 && !dead)
        {
            float weight = time < .16 ? Mathf.Clamp((float)(time - .10) / .06f, 0, 1)
                : time > .34 ? Mathf.Clamp((float)(.48 - time) / .14f, 0, 1) : 1;
            // The visible nock is a few display pixels inside the source bow's string edge.
            SecondaryGripError = ApplyHandIk(false, leftEquipment.ToGlobal(new Vector2(-225, 0)), weight);
        }
        else SecondaryGripError = 0;
        bool attacking = current.StartsWith("attack_", StringComparison.Ordinal);
        if (attacking && !attackImpactSent && time >= AttackImpactTime)
        { attackImpactSent = true; AttackImpact?.Invoke(); }
        effect.Visible = current is "attack_bow" or "attack_tome" && time >= AttackImpactTime && time <= AttackImpactTime + .26f;
        if (effect.Visible)
        {
            Node2D socket = current == "attack_bow" ? leftSocket : rightSocket;
            effect.Position = visualRoot.ToLocal(socket.GlobalPosition) + (current == "attack_bow" ? new Vector2(20, -7) : new Vector2(22, -16));
        }
        if (dead && current == "death" && time >= .40)
        {
            visualRoot.Visible = false;
            deathPose.Visible = true;
        }
    }

    private float ApplyHandIk(bool left, Vector2 targetGlobal, float weight = 1)
    {
        Bone2D upper = bones[left ? "ArmLUpper" : "ArmRUpper"];
        Bone2D lower = bones[left ? "ArmLLower" : "ArmRLower"];
        Node2D socket = left ? leftSocket : rightSocket;
        Node2D parent = upper.GetParent<Node2D>();
        Vector2 target = parent.ToLocal(targetGlobal) - upper.Position;
        float upperLength = lower.Position.Length();
        float lowerLength = (socket.Position + bones[left ? "HandL" : "HandR"].Position).Length();
        SecondaryGripTargetDistance = target.Length();
        SecondaryGripMaxReach = upperLength + lowerLength;
        float distance = Mathf.Clamp(target.Length(), Mathf.Abs(upperLength - lowerLength) + .01f, upperLength + lowerLength - .01f);
        float bearing = target.Angle();
        float shoulderAngle = Mathf.Acos(Mathf.Clamp((distance * distance + upperLength * upperLength - lowerLength * lowerLength)
            / (2f * distance * upperLength), -1f, 1f));
        float elbowAngle = Mathf.Acos(Mathf.Clamp((distance * distance - upperLength * upperLength - lowerLength * lowerLength)
            / (2f * upperLength * lowerLength), -1f, 1f));
        float bend = left ? -1f : 1f;
        float upperRest = lower.Position.Angle();
        float lowerRest = (socket.Position + bones[left ? "HandL" : "HandR"].Position).Angle();
        upper.Rotation = Mathf.LerpAngle(upper.Rotation, bearing - bend * shoulderAngle - upperRest, weight);
        lower.Rotation = Mathf.LerpAngle(lower.Rotation, bend * elbowAngle - (lowerRest - upperRest), weight);
        return socket.GlobalPosition.DistanceTo(targetGlobal);
    }

    private void BuildRig()
    {
        visualRoot = new Node2D { Name = "VisualRoot", Scale = Vector2.One * (IsElf ? .70f : .78f), Position = FootAnchor };
        AddChild(visualRoot);
        skeleton = new Skeleton2D { Name = "Skeleton2D" };

        Bone2D pelvis = AddBone("Pelvis", skeleton, Vector2.Zero);
        AddPart(pelvis, "pelvis.png", Vector2.Zero, .155f, 1);

        Bone2D spine = AddBone("Spine", pelvis, IsElf ? new Vector2(0, -26) : new Vector2(0, -24));
        AddPart(spine, "torso.png", new Vector2(0, -4), .155f, 2);
        AddPart(spine, "cape.png", new Vector2(0, 3), .155f, -4);

        Bone2D head = AddBone("Head", spine, new Vector2(0, IsElf ? -27 : -29));
        AddPart(head, "head.png", new Vector2(0, -8), .155f, 5);

        Bone2D armLUpper = AddBone("ArmLUpper", spine, IsElf ? new Vector2(-10, -14) : new Vector2(-16, -13));
        AddPart(armLUpper, "arm_l_upper.png", new Vector2(-1, 10), .145f, 1);
        Bone2D armLLower = AddBone("ArmLLower", armLUpper, IsElf ? new Vector2(-5, 22) : new Vector2(-1, 22));
        AddPart(armLLower, "arm_l_lower.png", new Vector2(0, 10), .135f, 1);
        Bone2D handL = AddBone("HandL", armLLower, IsElf ? new Vector2(-8, 18) : new Vector2(0, 21));
        AddPart(handL, "hand_l.png", new Vector2(0, 5), .125f, 3);

        Bone2D armRUpper = AddBone("ArmRUpper", spine, IsElf ? new Vector2(10, -14) : new Vector2(16, -13));
        AddPart(armRUpper, "arm_r_upper.png", new Vector2(1, 10), .145f, 3);
        Bone2D armRLower = AddBone("ArmRLower", armRUpper, IsElf ? new Vector2(6, 22) : new Vector2(1, 22));
        AddPart(armRLower, "arm_r_lower.png", new Vector2(0, 10), .135f, 3);
        Bone2D handR = AddBone("HandR", armRLower, IsElf ? new Vector2(9, 18) : new Vector2(0, 21));
        AddPart(handR, "hand_r.png", new Vector2(0, 5), .125f, 4);

        Bone2D legLUpper = AddBone("LegLUpper", pelvis, IsElf ? new Vector2(-6, 5) : new Vector2(-9, 8));
        AddPart(legLUpper, "leg_l_upper.png", new Vector2(0, 12), .14f, 0);
        Bone2D legLLower = AddBone("LegLLower", legLUpper, IsElf ? new Vector2(-1, 30) : new Vector2(0, 26));
        AddPart(legLLower, "leg_l_lower.png", new Vector2(0, 12), .13f, 0);
        Bone2D footL = AddBone("FootL", legLLower, IsElf ? new Vector2(-2, 28) : new Vector2(0, 25));
        AddPart(footL, "foot_l.png", new Vector2(-3, 5), .13f, 1);

        Bone2D legRUpper = AddBone("LegRUpper", pelvis, IsElf ? new Vector2(6, 5) : new Vector2(9, 8));
        AddPart(legRUpper, "leg_r_upper.png", new Vector2(0, 12), .14f, 1);
        Bone2D legRLower = AddBone("LegRLower", legRUpper, IsElf ? new Vector2(1, 30) : new Vector2(0, 26));
        AddPart(legRLower, "leg_r_lower.png", new Vector2(0, 12), .13f, 1);
        Bone2D footR = AddBone("FootR", legRLower, IsElf ? new Vector2(2, 28) : new Vector2(0, 25));
        AddPart(footR, "foot_r.png", new Vector2(3, 5), .13f, 2);

        leftSocket = new Node2D { Name = "WeaponSocketL", Position = new Vector2(0, 8), ZIndex = 10 };
        handL.AddChild(leftSocket);
        rightSocket = new Node2D { Name = "WeaponSocketR", Position = new Vector2(0, 8), ZIndex = 10 };
        handR.AddChild(rightSocket);
        leftEquipment = NewEquipment("EquipmentL"); leftSocket.AddChild(leftEquipment);
        rightEquipment = NewEquipment("EquipmentR"); rightSocket.AddChild(rightEquipment);
        visualRoot.AddChild(skeleton);
        if (IsElf) BuildElfSkin();

        effect = NewEquipment("AttackEffect");
        effect.ZIndex = 20; effect.Visible = false;
        visualRoot.AddChild(effect);

        Texture2D deathTexture;
        if (IsElf)
        {
            Texture2D sheet = ResourceLoader.Load<Texture2D>(PixelRoot + "isera_pixel_action_sheet_v3.png");
            int width = sheet.GetWidth() / 7;
            deathTexture = new AtlasTexture { Atlas = sheet, Region = new Rect2(width * 6, 0, width, sheet.GetHeight()) };
        }
        else deathTexture = ResourceLoader.Load<Texture2D>(PixelRoot + "swordmaster_death_pose_v2.png");
        deathPose = new Sprite2D
        {
            Name = "FinalDeathPose", Texture = deathTexture, Visible = false,
            Position = IsElf ? new Vector2(0, -75) : new Vector2(0, -21),
            Scale = Vector2.One * (IsElf ? .32f : .10f),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest, ZIndex = 3
        };
        AddChild(deathPose);

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

    private Vector2 FootAnchor => new(0, IsElf ? -56 : -62);

    private void AddPart(Node2D parent, string file, Vector2 position, float scale, int zIndex)
    {
        if (IsElf) return;
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

    private void BuildElfSkin()
    {
        // One textured surface: adjacent triangles share vertices, so animated joints cannot split apart.
        Texture2D texture = ResourceLoader.Load<Texture2D>(ElfSkinPath);
        const int columns = 25, rows = 37;
        var vertices = new Vector2[columns * rows];
        var uv = new Vector2[vertices.Length];
        var polygons = new Godot.Collections.Array();
        var weights = new Dictionary<string, float[]>();
        foreach (string bone in bones.Keys) weights[bone] = new float[vertices.Length];
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            int index = y * columns + x;
            Vector2 source = new(texture.GetWidth() * x / (float)(columns - 1),
                texture.GetHeight() * y / (float)(rows - 1));
            uv[index] = source;
            vertices[index] = (source - new Vector2(texture.GetWidth() * .5f, 760)) * .10f;
            foreach ((string bone, float weight) in ElfSkinWeights(vertices[index]))
                weights[bone][index] = weight;
            if (x == columns - 1 || y == rows - 1) continue;
            int next = index + columns;
            polygons.Add(new[] { index, index + 1, next + 1, next });
        }
        var skin = new Polygon2D
        {
            Name = "ContinuousElfSkin", Texture = texture, Polygon = vertices,
            UV = uv, Polygons = polygons, TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            ZIndex = 1
        };
        visualRoot.AddChild(skin);
        elfSkin = skin;
        skin.Skeleton = skin.GetPathTo(skeleton);
        foreach ((string bone, float[] boneWeights) in weights)
            skin.AddBone(skin.GetPathTo(bones[bone]), boneWeights);
    }

    private static Dictionary<string, float> ElfSkinWeights(Vector2 p)
    {
        var result = new Dictionary<string, float>();
        static void Put(Dictionary<string, float> output, string bone, float value)
        { if (value > 0) output[bone] = output.GetValueOrDefault(bone) + value; }
        if (p.Y < -42)
        {
            float head = Mathf.Clamp((-p.Y - 42) / 10f, 0, 1);
            Put(result, "Head", head); Put(result, "Spine", 1 - head);
        }
        else if (p.Y < 10 && p.X is < -12 and > -38 or > 12 and < 38)
        {
            bool left = p.X < 0;
            string side = left ? "L" : "R";
            float arm = Mathf.Clamp((Mathf.Abs(p.X) - 12) / 8f, 0, 1);
            Put(result, "Spine", 1 - arm);
            float upper = Mathf.Clamp((-p.Y - 14) / 12f, 0, 1);
            float hand = Mathf.Clamp((p.Y + 5) / 12f, 0, 1);
            Put(result, $"Arm{side}Upper", arm * upper);
            Put(result, $"Arm{side}Lower", arm * (1 - upper) * (1 - hand));
            Put(result, $"Hand{side}", arm * (1 - upper) * hand);
        }
        else if (p.Y > 4 && Mathf.Abs(p.X) < 22)
        {
            float leg = Mathf.Clamp((p.Y - 4) / 12f, 0, 1) * Mathf.Clamp((22 - Mathf.Abs(p.X)) / 6f, 0, 1);
            Put(result, "Pelvis", 1 - leg);
            float left = Mathf.Clamp((-p.X + 3) / 6f, 0, 1);
            float upper = Mathf.Clamp((38 - p.Y) / 10f, 0, 1);
            float foot = Mathf.Clamp((p.Y - 58) / 10f, 0, 1);
            foreach ((string side, float sideWeight) in new[] { ("L", left), ("R", 1 - left) })
            {
                Put(result, $"Leg{side}Upper", leg * sideWeight * upper);
                Put(result, $"Leg{side}Lower", leg * sideWeight * (1 - upper) * (1 - foot));
                Put(result, $"Foot{side}", leg * sideWeight * (1 - upper) * foot);
            }
        }
        else
        {
            float spine = Mathf.Clamp((-p.Y) / 14f, 0, 1);
            Put(result, "Spine", spine); Put(result, "Pelvis", 1 - spine);
        }
        float total = 0;
        foreach (float weight in result.Values) total += weight;
        if (total <= .0001f) { result.Clear(); result["Pelvis"] = 1; }
        else foreach (string bone in new List<string>(result.Keys)) result[bone] /= total;
        return result;
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
        Animation a = NewAnimation(.64);
        AddRotation(a, Rig + "Pelvis", (0, 0), (.18, -5), (.34, 4), (.64, 0));
        AddRotation(a, Rig + "Pelvis/Spine", (0, 0), (.18, -7), (.34, 11), (.64, 0));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper", (0, 28), (.18, -48), (.34, 48), (.48, 34), (.64, 28));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper/ArmRLower", (0, -20), (.18, -28), (.34, 5), (.64, -20));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper/ArmRLower/HandR/WeaponSocketR/EquipmentR",
            (0, 10), (.18, 45), (.34, -20), (.64, 10));
        return a;
    }

    private static Animation MakeBowAttack()
    {
        Animation a = NewAnimation(.64);
        AddRotation(a, Rig + "Pelvis/Spine", (0, 0), (.22, -4), (.34, -2), (.64, 0));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper", (0, -18), (.16, -48), (.34, -60), (.48, -40), (.64, -18));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper/ArmLLower", (0, 0), (.16, 8), (.34, 8), (.64, 0));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper/ArmLLower/HandL/WeaponSocketL/EquipmentL",
            (0, 18), (.16, 50), (.34, 60), (.48, 40), (.64, 18));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper", (0, 16), (.16, -48), (.34, -65), (.48, 12), (.64, 16));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper/ArmRLower", (0, 0), (.16, 75), (.34, 105), (.48, 18), (.64, 0));
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

    private Animation MakeDeath()
    {
        Animation a = NewAnimation(.7);
        // Fold at the hips and knees into a grounded side pose. The display root stays upright.
        Vector2 anchor = FootAnchor;
        AddPosition(a, "VisualRoot", (0, anchor), (.18, anchor + new Vector2(-2, 10)), (.50, anchor + new Vector2(10, 38)), (.7, anchor + new Vector2(10, 38)));
        AddRotation(a, Rig + "Pelvis", (0, 0), (.18, 12), (.50, 26), (.7, 26));
        AddRotation(a, Rig + "Pelvis/Spine", (0, 0), (.18, 15), (.50, 62), (.7, 62));
        AddRotation(a, Rig + "Pelvis/Spine/Head", (0, 0), (.50, 24), (.7, 24));
        AddRotation(a, Rig + "Pelvis/LegLUpper", (0, 0), (.50, -58), (.7, -58));
        AddRotation(a, Rig + "Pelvis/LegRUpper", (0, 0), (.50, 45), (.7, 45));
        AddRotation(a, Rig + "Pelvis/LegLUpper/LegLLower", (0, 0), (.50, 68), (.7, 68));
        AddRotation(a, Rig + "Pelvis/LegRUpper/LegRLower", (0, 0), (.50, -62), (.7, -62));
        AddRotation(a, Rig + "Pelvis/Spine/ArmLUpper", (0, 2), (.50, 52), (.7, 52));
        AddRotation(a, Rig + "Pelvis/Spine/ArmRUpper", (0, -2), (.50, -55), (.7, -55));
        return a;
    }

    public void SetLoadout(RigLoadout next)
    {
        loadout = next;
        HideEquipment();
        switch (next)
        {
            case RigLoadout.RightSword:
                Equip(rightEquipment, "equipment_sword.png", .064f, new Vector2(280, 820), Vector2.Zero, -25); break;
            case RigLoadout.LeftShield:
                Equip(leftEquipment, "equipment_shield.png", .058f, new Vector2(418, 436), new Vector2(-1, 1), 0); break;
            case RigLoadout.SwordAndShield:
                Equip(rightEquipment, "equipment_sword.png", .064f, new Vector2(280, 820), Vector2.Zero, -25);
                Equip(leftEquipment, "equipment_shield.png", .058f, new Vector2(418, 436), new Vector2(-1, 1), 0); break;
            case RigLoadout.DualSwords:
                Equip(rightEquipment, "equipment_sword.png", .064f, new Vector2(280, 820), Vector2.Zero, -25);
                Equip(leftEquipment, "equipment_sword.png", .064f, new Vector2(280, 820), Vector2.Zero, 25, true); break;
            case RigLoadout.TwoHandedWeapon:
                Equip(rightEquipment, "equipment_two_hand_sword.png", .071f, new Vector2(278, 1370), Vector2.Zero, 10); break;
            case RigLoadout.Bow:
                Equip(leftEquipment, "equipment_bow.png", .058f, new Vector2(372, 650), new Vector2(0, 0), 0); break;
            case RigLoadout.Tome:
                Equip(leftEquipment, "equipment_tome.png", .051f, new Vector2(514, 382), Vector2.Zero, -6); break;
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
        deathPose.Visible = false;
        visualRoot.Visible = true;
        animationPlayer.SpeedScale = 1;
        visualRoot.Position = FootAnchor; visualRoot.Rotation = 0;
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

    public void PlayMove() { dead = false; animationPlayer.SpeedScale = 1; animationPlayer.Play("move", .08); }
    public void PlayAttack()
    {
        dead = false;
        animationPlayer.SpeedScale = 1;
        attackImpactSent = false;
        animationPlayer.Play(AttackAnimationName, .06);
    }
    private string AttackAnimationName => loadout switch
    {
        RigLoadout.TwoHandedWeapon => "attack_twohand",
        RigLoadout.Bow => "attack_bow",
        RigLoadout.Tome => "attack_tome",
        _ => "attack_onehand"
    };
    /// <summary>Freeze an authored attack pose for deterministic visual review.</summary>
    public void PreviewAttackAt(double time)
    {
        // Start without crossfade so seeking writes the authored bone tracks immediately.
        animationPlayer.Stop();
        animationPlayer.SpeedScale = 1;
        animationPlayer.Play(AttackAnimationName, 0);
        animationPlayer.Advance(0);
        animationPlayer.Seek(time, true);
        animationPlayer.Advance(0);
        animationPlayer.SpeedScale = 0;
        _Process(0);
    }
    public void PlayHurt() { dead = false; animationPlayer.SpeedScale = 1; animationPlayer.Play("hurt", .04); }
    public void PlayDeath()
    {
        dead = true;
        deathPose.Visible = false;
        visualRoot.Visible = true;
        animationPlayer.SpeedScale = 1;
        animationPlayer.Play("death", .05);
    }

    private void OnAnimationFinished(StringName animationName)
    {
        if (animationName != "death") PlayIdle();
    }
}
