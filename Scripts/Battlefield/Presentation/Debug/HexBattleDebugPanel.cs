using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator;

namespace CardSimulator.Battlefield;

// 六边形战斗调试面板：顶部类别 Tab + 左侧指令列表 + 右侧动态参数页。
// 指令通过 Command(...) 注册（参数 + 输入方式 + 执行回调），右侧详细参数页由代码动态生成。
public partial class HexBattleDebugPanel : Control
{
    private BattlefieldSession session;
    private BattlefieldView mapView;
    private Action<string> showMessage;

    private Label preview;
    private Func<AxialHex, bool> savedOverride;
    private bool inTargetMode;
    private bool targetNeedsUnit;
    private Action<AxialHex> onMapTarget;

    private HBoxContainer topTabs;
    private VBoxContainer leftList;
    private VBoxContainer detailPanel;
    private PanelContainer window;
    private ColorRect backdrop;
    private readonly List<Category> categories = new();
    private readonly Dictionary<string, Category> categoryByName = new(StringComparer.Ordinal);
    private Category activeCategory;
    private DebugCommand activeCommand;

    // ---------- 指令描述模型 ----------
    private enum ParamKind { Text, Int, Option, TargetCell, TargetUnit }

    private sealed class Param
    {
        public string Label;
        public ParamKind Kind;
        public string[] Options = Array.Empty<string>();
        public string Default = "";
        public Control Widget;
        public Param(string label, ParamKind kind) { Label = label; Kind = kind; }
        public Param Opt(params string[] items) { Options = items; return this; }
        public Param Def(string d) { Default = d; return this; }
    }

    private sealed class DebugCommand
    {
        public string Name;
        public List<Param> Params = new();
        public Action<DebugArgs> OnExecute;
        public DebugArgs Args;
    }

    private sealed class DebugArgs
    {
        private readonly DebugCommand _cmd;
        public AxialHex? Target;
        public bool TargetChosen;
        public DebugArgs(DebugCommand cmd) { _cmd = cmd; }

        public string AsString(string label)
        {
            foreach (var p in _cmd.Params)
                if (p.Label == label)
                {
                    if (p.Widget is LineEdit le) return le.Text;
                    if (p.Widget is OptionButton ob) return ob.ItemCount > 0 ? ob.GetItemText(ob.Selected) : "";
                }
            return "";
        }
        public int AsInt(string label) => int.TryParse(AsString(label).Trim(), out var v) ? v : 0;
        public int OptionIndex(string label)
        {
            foreach (var p in _cmd.Params)
                if (p.Label == label && p.Widget is OptionButton ob) return ob.Selected;
            return 0;
        }
    }

    private sealed class Category
    {
        public string Name;
        public List<DebugCommand> Commands = new();
        public Category(string name) { Name = name; }
    }

    public void Setup(BattlefieldSession session, BattlefieldView mapView, Action<string> showMessage)
    {
        this.session = session;
        this.mapView = mapView;
        this.showMessage = showMessage;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        BuildUi();
        RegisterCommands();
        BuildTopTabs();
    }

    public void ToggleVisible()
    {
        ExitTargetMode();
        Visible = !Visible;
    }

    private void ClosePanel()
    {
        ExitTargetMode();
        Visible = false;
    }

    public override void _Input(InputEvent e)
    {
        if (inTargetMode && e is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.Escape)
        {
            ExitTargetMode();
            showMessage?.Invoke("已取消选择目标。");
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        if (inTargetMode && mapView != null && savedOverride != null) mapView.LeftClickOverride = savedOverride;
    }

    // ---------- 目标选择 ----------
    private void EnterTargetMode(bool needsUnit, string prompt, Action<AxialHex> onTarget)
    {
        if (session == null || mapView == null) return;
        ExitTargetMode();
        inTargetMode = true;
        targetNeedsUnit = needsUnit;
        onMapTarget = onTarget;
        savedOverride = mapView.LeftClickOverride;
        mapView.LeftClickOverride = OnDebugMapClick;
        if (window != null) window.Visible = false;
        if (backdrop != null) backdrop.Visible = false;
        preview.Text = prompt;
        preview.Visible = true;
    }

    private void ExitTargetMode()
    {
        if (!inTargetMode) return;
        inTargetMode = false;
        onMapTarget = null;
        if (mapView != null && savedOverride != null) mapView.LeftClickOverride = savedOverride;
        savedOverride = null;
        if (preview != null) preview.Visible = false;
        if (window != null) window.Visible = true;
        if (backdrop != null) backdrop.Visible = true;
    }

    private bool OnDebugMapClick(AxialHex coord)
    {
        var p = session.Occupancy.At(coord);
        if (targetNeedsUnit && (p == null || p.Presence != BattlefieldPresence.Active))
        {
            showMessage?.Invoke("请点击场上单位所在的格。");
            return true;
        }
        onMapTarget?.Invoke(coord);
        ExitTargetMode();
        return true;
    }

    private void ShowMsg(string m) => showMessage?.Invoke(m);
    // ---------- UI 构建 ----------
    private void BuildUi()
    {
        preview = new Label
        {
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0,
            OffsetTop = 8, OffsetBottom = 52,
        };
        preview.AddThemeFontSizeOverride("font_size", 18);
        preview.AddThemeColorOverride("font_color", new Color("ffd27a"));
        AddChild(preview);

        backdrop = new ColorRect { Color = new Color(0, 0, 0, .55f), MouseFilter = MouseFilterEnum.Ignore };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        center.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(center);

        window = new PanelContainer { ZIndex = 60, MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(900, 580) };
        window.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.10f, 0.13f, 0.97f),
            BorderColor = new Color(0.40f, 0.46f, 0.55f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 12, ContentMarginTop = 10, ContentMarginRight = 12, ContentMarginBottom = 12,
        });
        center.AddChild(window);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 8);
        window.AddChild(outer);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        outer.AddChild(header);
        var title = new Label { Text = "六边形战斗调试", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color("e8d9a0"));
        header.AddChild(title);
        var closeBtn = new Button { Text = "关闭" };
        closeBtn.Pressed += () => ClosePanel();
        header.AddChild(closeBtn);

        topTabs = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        topTabs.AddThemeConstantOverride("separation", 4);
        outer.AddChild(topTabs);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        outer.AddChild(body);

        var leftScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(190, 0) };
        body.AddChild(leftScroll);
        leftList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        leftList.AddThemeConstantOverride("separation", 4);
        leftScroll.AddChild(leftList);

        body.AddChild(new VSeparator());

        var detailScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddChild(detailScroll);
        detailPanel = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        detailPanel.AddThemeConstantOverride("separation", 10);
        detailScroll.AddChild(detailPanel);
    }

    private static StyleBoxFlat FlatBox(Color bg, Color border, int bw)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = bw, BorderWidthTop = bw, BorderWidthRight = bw, BorderWidthBottom = bw,
            CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
            ContentMarginLeft = 10, ContentMarginTop = 6, ContentMarginRight = 10, ContentMarginBottom = 6,
        };
    }

    private static void StyleButton(Button b, bool selected)
    {
        var box = FlatBox(selected ? new Color(0.16f, 0.24f, 0.33f) : new Color(0.09f, 0.11f, 0.15f),
            selected ? new Color(0.90f, 0.78f, 0.38f) : new Color(0.26f, 0.30f, 0.38f), selected ? 2 : 1);
        b.AddThemeStyleboxOverride("normal", box);
        b.AddThemeStyleboxOverride("hover", box);
        b.AddThemeStyleboxOverride("pressed", box);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    private static Label FieldLabel(string text)
    {
        var l = new Label { Text = text, CustomMinimumSize = new Vector2(96, 0) };
        l.AddThemeFontSizeOverride("font_size", 13);
        l.AddThemeColorOverride("font_color", new Color("c8d0d8"));
        return l;
    }
    private static void ClearChildren(Node parent)
    {
        foreach (Node c in parent.GetChildren())
        {
            parent.RemoveChild(c);
            c.QueueFree();
        }
    }

    private void BuildTopTabs()
    {
        ClearChildren(topTabs);
        foreach (var cat in categories)
        {
            var btn = new Button { Text = cat.Name };
            btn.CustomMinimumSize = new Vector2(0, 34);
            btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            btn.Pressed += () => SelectCategory(cat);
            StyleButton(btn, false);
            topTabs.AddChild(btn);
        }
        if (categories.Count > 0) SelectCategory(categories[0]);
    }

    private void SelectCategory(Category cat)
    {
        ExitTargetMode();
        activeCategory = cat;
        int idx = categories.IndexOf(cat);
        for (int i = 0; i < topTabs.GetChildCount(); i++)
            if (topTabs.GetChild(i) is Button b) StyleButton(b, i == idx);
        BuildLeftList();
    }

    private void BuildLeftList()
    {
        ClearChildren(leftList);
        foreach (var cmd in activeCategory.Commands)
        {
            var btn = new Button { Text = "  " + cmd.Name, Alignment = HorizontalAlignment.Left };
            btn.CustomMinimumSize = new Vector2(0, 36);
            btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            btn.Pressed += () => SelectCommand(cmd);
            StyleButton(btn, false);
            leftList.AddChild(btn);
        }
        if (activeCategory.Commands.Count > 0) SelectCommand(activeCategory.Commands[0]);
    }

    private void SelectCommand(DebugCommand cmd)
    {
        ExitTargetMode();
        activeCommand = cmd;
        int idx = activeCategory.Commands.IndexOf(cmd);
        for (int i = 0; i < leftList.GetChildCount(); i++)
            if (leftList.GetChild(i) is Button b) StyleButton(b, i == idx);
        BuildDetail(cmd);
    }

    private void BuildDetail(DebugCommand cmd)
    {
        cmd.Args = new DebugArgs(cmd);
        ClearChildren(detailPanel);

        var title = new Label { Text = cmd.Name };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color("e8d9a0"));
        detailPanel.AddChild(title);
        detailPanel.AddChild(new HSeparator());

        bool hasTarget = false, targetNeedsUnit = false;
        foreach (var p in cmd.Params)
        {
            if (p.Kind == ParamKind.TargetCell || p.Kind == ParamKind.TargetUnit)
            {
                hasTarget = true;
                targetNeedsUnit = p.Kind == ParamKind.TargetUnit;
                detailPanel.AddChild(BuildTargetRow(p));
            }
            else
            {
                detailPanel.AddChild(BuildParamRow(p));
            }
        }

        if (hasTarget)
        {
            var tip = new Label { Text = targetNeedsUnit ? "设置好参数后点击“选择目标单位”，面板会收起；点击地图上的目标单位即执行。" : "设置好参数后点击“选择目标格”，面板会收起；点击地图上的目标格即执行。" };
            tip.AddThemeFontSizeOverride("font_size", 12);
            tip.AddThemeColorOverride("font_color", new Color("9fb3bd"));
            detailPanel.AddChild(tip);
        }
        else
        {
            var exec = new Button { Text = "执行", CustomMinimumSize = new Vector2(150, 0) };
            exec.Pressed += () =>
            {
                if (session == null) return;
                cmd.OnExecute?.Invoke(cmd.Args);
            };
            detailPanel.AddChild(exec);
        }
    }

    private Control BuildParamRow(Param p)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(FieldLabel(p.Label + "："));
        if (p.Kind == ParamKind.Option)
        {
            var ob = new OptionButton();
            foreach (var it in p.Options) ob.AddItem(it);
            if (ob.ItemCount > 0) ob.Selected = 0;
            ob.CustomMinimumSize = new Vector2(170, 0);
            p.Widget = ob;
            row.AddChild(ob);
        }
        else
        {
            var le = new LineEdit { CustomMinimumSize = new Vector2(180, 0) };
            le.Text = p.Default;
            p.Widget = le;
            row.AddChild(le);
        }
        return row;
    }

    private Control BuildTargetRow(Param p)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(FieldLabel(p.Label + "："));
        var status = new Label { Text = "（未选择）", CustomMinimumSize = new Vector2(110, 0) };
        status.AddThemeColorOverride("font_color", new Color("9fb3bd"));
        var pick = new Button { Text = p.Kind == ParamKind.TargetUnit ? "选择目标单位" : "选择目标格" };
        pick.Pressed += () =>
        {
            if (session == null) return;
            bool needsUnit = p.Kind == ParamKind.TargetUnit;
            string prompt = BuildTargetPrompt(activeCommand, needsUnit);
            EnterTargetMode(needsUnit, prompt, coord =>
            {
                var cmd = activeCommand;
                cmd.Args.Target = coord;
                cmd.Args.TargetChosen = true;
                cmd.OnExecute?.Invoke(cmd.Args);
                status.Text = $"({coord.Q},{coord.R}) 已执行";
                status.AddThemeColorOverride("font_color", new Color("7fd47a"));
            });
        };
        row.AddChild(pick);
        row.AddChild(status);
        return row;
    }

    private string BuildTargetPrompt(DebugCommand cmd, bool needsUnit)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("执行「").Append(cmd.Name).Append("」");
        foreach (var p in cmd.Params)
            if (p.Kind != ParamKind.TargetCell && p.Kind != ParamKind.TargetUnit)
                sb.Append("  ").Append(p.Label).Append("=").Append(ReadParam(p));
        sb.Append(needsUnit ? " ：点击地图上的目标单位。" : " ：点击地图上的目标格。");
        return sb.ToString();
    }

    private static string ReadParam(Param p)
    {
        if (p.Widget is LineEdit le) return le.Text.Trim();
        if (p.Widget is OptionButton ob) return ob.ItemCount > 0 ? ob.GetItemText(ob.Selected) : "";
        return "";
    }
    // ---------- 通用指令注册：参数 + 输入方式 + 执行回调 ----------
    private void Command(string category, string name, Action<DebugArgs> handler, params Param[] ps)
    {
        if (!categoryByName.TryGetValue(category, out var cat))
        {
            cat = new Category(category);
            categoryByName[category] = cat;
            categories.Add(cat);
        }
        var cmd = new DebugCommand { Name = name, OnExecute = handler };
        cmd.Params.AddRange(ps);
        cat.Commands.Add(cmd);
    }

    private BattleUnitPlacement ResolveTargetUnit(DebugArgs args)
    {
        if (session == null || !args.Target.HasValue) { ShowMsg("请先选择目标单位。"); return null; }
        var p = session.Occupancy.At(args.Target.Value);
        if (p == null || p.Presence != BattlefieldPresence.Active) { ShowMsg("目标格无单位或已离场。"); return null; }
        return p;
    }

    private void RegisterCommands()
    {
        // A 牌堆 / 手牌
        Command("牌堆 / 手牌", "抽牌",
            args => { if (session != null) { int n = Math.Max(0, args.AsInt("数量")); session.DebugDraw(session.SelectedId, n); ShowMsg("已抽 " + n + " 张。"); } },
            new Param("数量", ParamKind.Int).Def("1"));
        Command("牌堆 / 手牌", "添加卡牌",
            args => { if (session == null) return; bool ok = session.DebugAddCard(session.SelectedId, args.AsInt("卡牌ID"), args.OptionIndex("目标堆"), Math.Max(1, args.AsInt("数量")), out string e); ShowMsg(ok ? "已添加卡牌。" : e); },
            new Param("目标堆", ParamKind.Option).Opt("手牌", "抽牌堆", "弃牌堆", "消耗牌堆"),
            new Param("卡牌ID", ParamKind.Int).Def("1001"),
            new Param("数量", ParamKind.Int).Def("1"));
        Command("牌堆 / 手牌", "清空手牌",
            args => { if (session == null) return; int n = session.DebugClearHand(session.SelectedId); ShowMsg("已清空手牌（" + n + " 张入弃牌堆）。"); });

        // B 资源
        Command("资源", "加能量",
            args => { if (session != null) { session.DebugAddEnergy(session.SelectedId, args.AsInt("能量")); ShowMsg("已加能量。"); } },
            new Param("能量", ParamKind.Int).Def("1"));
        Command("资源", "加移动",
            args => { if (session != null) { session.DebugAddMoves(session.SelectedId, args.AsInt("次数")); ShowMsg("已加移动。"); } },
            new Param("次数", ParamKind.Int).Def("1"));
        Command("资源", "重置本回合",
            args => { session?.DebugResetRound(); ShowMsg("已重置本回合。"); });

        // C 单位效果（选目标单位）
        Command("单位效果", "伤害",
            args => { var p = ResolveTargetUnit(args); if (p != null) { bool ok = session.DebugDamage(p.UnitId, args.AsInt("伤害"), out string e); ShowMsg(ok ? "已造成伤害。" : e); } },
            new Param("伤害", ParamKind.Int).Def("5"),
            new Param("目标", ParamKind.TargetUnit));
        Command("单位效果", "回复",
            args => { var p = ResolveTargetUnit(args); if (p != null) { bool ok = session.DebugHeal(p.UnitId, args.AsInt("回复"), out string e); ShowMsg(ok ? "已回复生命。" : e); } },
            new Param("回复", ParamKind.Int).Def("5"),
            new Param("目标", ParamKind.TargetUnit));
        Command("单位效果", "设置生命",
            args => { var p = ResolveTargetUnit(args); if (p != null) { bool ok = session.DebugSetHp(p.UnitId, args.AsInt("生命"), out string e); ShowMsg(ok ? "已设置生命。" : e); } },
            new Param("生命", ParamKind.Int).Def("10"),
            new Param("目标", ParamKind.TargetUnit));
        Command("单位效果", "加护盾",
            args => { var p = ResolveTargetUnit(args); if (p != null) { bool ok = session.DebugAddShield(p.UnitId, args.AsInt("护盾"), out string e); ShowMsg(ok ? "已加护盾。" : e); } },
            new Param("护盾", ParamKind.Int).Def("5"),
            new Param("目标", ParamKind.TargetUnit));
        Command("单位效果", "添加状态",
            args => { var p = ResolveTargetUnit(args); if (p != null) { bool ok = session.DebugAddState(p.UnitId, args.AsInt("状态ID"), args.AsInt("层数"), out string e); ShowMsg(ok ? "已添加状态。" : e); } },
            new Param("状态ID", ParamKind.Int).Def("0"),
            new Param("层数", ParamKind.Int).Def("1"),
            new Param("目标", ParamKind.TargetUnit));
        Command("单位效果", "删除状态",
            args => { var p = ResolveTargetUnit(args); if (p != null) { bool ok = session.DebugRemoveState(p.UnitId, args.AsInt("第几个"), args.AsInt("层数"), out string e); ShowMsg(ok ? "已删除状态。" : e); } },
            new Param("第几个", ParamKind.Int).Def("0"),
            new Param("层数", ParamKind.Int).Def("1"),
            new Param("目标", ParamKind.TargetUnit));
        // D 道具 / 装备
        Command("道具 / 装备", "道具到当前格(普通)",
            args => { if (session == null) return; bool ok = session.DebugSpawnItemAtCell(session.Selected.Coord.Q, session.Selected.Coord.R, args.AsString("道具ID"), false, out string e); ShowMsg(ok ? "已放置道具。" : e); },
            new Param("道具ID", ParamKind.Text).Def("potion"));
        Command("道具 / 装备", "道具到当前格(装备)",
            args => { if (session == null) return; bool ok = session.DebugSpawnItemAtCell(session.Selected.Coord.Q, session.Selected.Coord.R, args.AsString("道具ID"), true, out string e); ShowMsg(ok ? "已装备到当前格。" : e); },
            new Param("道具ID", ParamKind.Text).Def("sword"));
        Command("道具 / 装备", "道具到随身槽",
            args => { if (session == null) return; bool ok = session.DebugSpawnItemToSlot(Math.Clamp(args.AsInt("槽位"), 0, 2), args.AsString("道具ID"), out string e); ShowMsg(ok ? "已放入随身槽。" : e); },
            new Param("道具ID", ParamKind.Text).Def("potion"),
            new Param("槽位", ParamKind.Int).Def("0"));
        Command("道具 / 装备", "清空随身槽",
            args => { if (session == null) return; bool ok = session.DebugClearSlot(Math.Clamp(args.AsInt("槽位"), 0, 2), out string e); ShowMsg(ok ? "已清空。" : e); },
            new Param("槽位", ParamKind.Int).Def("0"));
        Command("道具 / 装备", "装备到手",
            args => { if (session == null) return; bool ok = session.DebugEquipToHand(args.AsString("道具ID"), args.OptionIndex("手"), out string e); ShowMsg(ok ? "已装备。" : e); },
            new Param("道具ID", ParamKind.Text).Def("sword"),
            new Param("手", ParamKind.Option).Opt("左手", "右手"));
        Command("道具 / 装备", "放置陷阱",
            args => { if (session == null || !args.Target.HasValue) return; bool ok = session.DebugPlaceTrap(args.Target.Value.Q, args.Target.Value.R, args.AsString("陷阱ID"), out string e); ShowMsg(ok ? "已放置陷阱。" : e); },
            new Param("陷阱ID", ParamKind.Text).Def("test_trap"),
            new Param("目标格", ParamKind.TargetCell));

        // E 敌方
        Command("敌方", "生成敌人",
            args => { if (session == null || !args.Target.HasValue) return; bool ok = session.DebugSpawnEnemy(args.Target.Value.Q, args.Target.Value.R, args.AsInt("怪物ID"), out string e); ShowMsg(ok ? "已生成敌人。" : e); },
            new Param("怪物ID", ParamKind.Int).Def("0"),
            new Param("目标格", ParamKind.TargetCell));
        Command("敌方", "清空敌人",
            args => { session?.DebugClearEnemies(); ShowMsg("已清空敌人。"); });

        // F 阶段
        Command("阶段", "结束回合",
            args => { session?.DebugEndTurn(); ShowMsg("已结束回合。"); });
    }
}