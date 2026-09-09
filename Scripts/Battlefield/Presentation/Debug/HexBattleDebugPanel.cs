using Godot;
using System;
using CardSimulator;

namespace CardSimulator.Battlefield;

// 六边形战斗调试面板：只为摆场面/测交互，不进入正式结算。HexBattleScene.EnableDebugPanel 为真时才实例化。
public partial class HexBattleDebugPanel : Control
{
    private BattlefieldSession session;
    private BattlefieldView mapView;
    private Action<string> showMessage;

    private Label preview;
    private Func<AxialHex, bool> savedOverride;
    private Action<AxialHex> onMapTarget;
    private bool targetNeedsUnit;
    private bool inTargetMode;

    private LineEdit drawCount, cardId, cardCount, addEnergyN, addMoveN;
    private LineEdit unitAmount, stateType, stateStacks, stateIndex, stateRemoveStacks;
    private LineEdit itemDefId, slotIdx, equipDefId, trapId, monsterId;
    private OptionButton pileTarget, handOption;

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

    // ---------- UI 构建 ----------
    private void BuildUi()
    {
        preview = new Label
        {
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0,
            OffsetTop = 46, OffsetBottom = 92,
        };
        preview.AddThemeFontSizeOverride("font_size", 18);
        preview.AddThemeColorOverride("font_color", new Color("ffd27a"));
        AddChild(preview);

        var window = new PanelContainer { ZIndex = 60, MouseFilter = MouseFilterEnum.Stop };
        window.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.10f, 0.13f, 0.96f),
            BorderColor = new Color(0.40f, 0.46f, 0.55f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 12, ContentMarginTop = 10, ContentMarginRight = 12, ContentMarginBottom = 12,
        });
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        center.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(center);
        center.AddChild(window);

        var outer = new VBoxContainer(); outer.AddThemeConstantOverride("separation", 8); window.AddChild(outer);
        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 8); outer.AddChild(header);
        var title = new Label { Text = "六边形战斗调试", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color("e8d9a0"));
        header.AddChild(title);
        var closeBtn = new Button { Text = "关闭" };
        closeBtn.Pressed += () => Visible = false;
        header.AddChild(closeBtn);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(540, 620) };
        outer.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(box);

        // A 牌堆/手牌
        Section(box, "牌堆 / 手牌");
        drawCount = Edit(60);
        Row(box, "抽牌", drawCount, Btn("执行", () => DoDraw()));
        pileTarget = Dropdown("手牌", "抽牌堆", "弃牌堆", "消耗牌堆");
        cardId = Edit(70); cardCount = Edit(50);
        Row(box, "添加卡牌", pileTarget, L("ID"), cardId, L("数量"), cardCount, Btn("执行", () => DoAddCard()));
        Row(box, "清空手牌", Btn("执行", () => DoClearHand()));

        // B 资源
        Section(box, "资源（当前角色）");
        addEnergyN = Edit(60); Row(box, "加能量", addEnergyN, Btn("执行", () => DoAddEnergy()));
        addMoveN = Edit(60); Row(box, "加移动", addMoveN, Btn("执行", () => DoAddMove()));
        Row(box, "重置本回合", Btn("执行", () => DoResetRound()));

        // C 单位效果
        Section(box, "单位效果（点地图选目标）");
        unitAmount = Edit(60);
        Row(box, "伤害", unitAmount, Btn("选目标", () => EnterTargetMode(true, "造成 " + Val(unitAmount) + " 点伤害：点击地图上的目标单位", DoDamage)));
        Row(box, "回复", unitAmount, Btn("选目标", () => EnterTargetMode(true, "回复 " + Val(unitAmount) + " 点生命：点击地图上的目标单位", DoHeal)));
        Row(box, "设置生命", unitAmount, Btn("选目标", () => EnterTargetMode(true, "设置生命为 " + Val(unitAmount) + "：点击地图上的目标单位", DoSetHp)));
        Row(box, "加护盾", unitAmount, Btn("选目标", () => EnterTargetMode(true, "护盾 +" + Val(unitAmount) + "：点击地图上的目标单位", DoAddShield)));
        stateType = Edit(60); stateStacks = Edit(50);
        Row(box, "添加状态", L("ID"), stateType, L("层数"), stateStacks, Btn("选目标", () => EnterTargetMode(true, "添加状态 " + Val(stateType) + " x" + Val(stateStacks) + "：点击地图上的目标单位", DoAddState)));
        stateIndex = Edit(50); stateRemoveStacks = Edit(50);
        Row(box, "删除状态", L("第"), stateIndex, L("个，删"), stateRemoveStacks, L("层"), Btn("选目标", () => EnterTargetMode(true, "删除第 " + Val(stateIndex) + " 个状态 " + Val(stateRemoveStacks) + " 层：点击地图上的目标单位", DoRemoveState)));

        // D 道具/装备
        Section(box, "道具 / 装备");
        itemDefId = Edit(90); slotIdx = Edit(50);
        Row(box, "道具到当前格", L("ID"), itemDefId, Btn("执行", () => DoItemAtCell(false)), Btn("到装备(当前格)", () => DoItemAtCell(true)));
        Row(box, "道具到随身槽", L("ID"), itemDefId, L("槽"), slotIdx, Btn("执行", () => DoItemToSlot()));
        Row(box, "清空随身槽", L("槽"), slotIdx, Btn("执行", () => DoClearSlot()));
        equipDefId = Edit(90); handOption = Dropdown("左手", "右手");
        Row(box, "装备到手", L("ID"), equipDefId, handOption, Btn("执行", () => DoEquip()));
        trapId = Edit(90);
        Row(box, "放置陷阱", L("陷阱ID"), trapId, Btn("选格", () => EnterTargetMode(false, "放置陷阱：点击合法格（无单位/道具）", DoTrap)));

        // E 敌方
        Section(box, "敌方");
        monsterId = Edit(70);
        Row(box, "生成敌人", L("怪物ID"), monsterId, Btn("选格", () => EnterTargetMode(false, "生成敌人：点击地图格", DoSpawnEnemy)));
        Row(box, "清空敌人", Btn("执行", () => DoClearEnemies()));

        // F 阶段
        Section(box, "阶段");
        Row(box, "结束回合", Btn("执行", () => session?.DebugEndTurn()));
    }

    // ---------- 命令执行 ----------
    private void DoDraw() { if (session != null) { session.DebugDraw(session.SelectedId, Math.Max(0, Val(drawCount))); ShowMsg("已抽 " + Val(drawCount) + " 张。"); } }
    private void DoAddCard()
    {
        if (session == null) return;
        bool ok = session.DebugAddCard(session.SelectedId, Str(cardId, 0), pileTarget.Selected, Math.Max(1, Val(cardCount)), out string e);
        ShowMsg(ok ? "已添加卡牌。" : e);
    }
    private void DoClearHand() { if (session == null) return; int n = session.DebugClearHand(session.SelectedId); ShowMsg("已清空手牌（" + n + " 张入弃牌堆）。"); }
    private void DoAddEnergy() { if (session != null) { session.DebugAddEnergy(session.SelectedId, Val(addEnergyN)); ShowMsg("已加能量。"); } }
    private void DoAddMove() { if (session != null) { session.DebugAddMoves(session.SelectedId, Val(addMoveN)); ShowMsg("已加移动。"); } }
    private void DoResetRound() { session?.DebugResetRound(); ShowMsg("已重置本回合。"); }

    private void DoDamage(AxialHex c) { var p = session.Occupancy.At(c); if (p != null) { session.DebugDamage(p.UnitId, Val(unitAmount), out string e); ShowMsg(e.Length > 0 ? e : "已造成伤害。"); } }
    private void DoHeal(AxialHex c) { var p = session.Occupancy.At(c); if (p != null) { session.DebugHeal(p.UnitId, Val(unitAmount), out string e); ShowMsg(e.Length > 0 ? e : "已回复生命。"); } }
    private void DoSetHp(AxialHex c) { var p = session.Occupancy.At(c); if (p != null) { session.DebugSetHp(p.UnitId, Val(unitAmount), out string e); ShowMsg(e.Length > 0 ? e : "已设置生命。"); } }
    private void DoAddShield(AxialHex c) { var p = session.Occupancy.At(c); if (p != null) { session.DebugAddShield(p.UnitId, Val(unitAmount), out string e); ShowMsg(e.Length > 0 ? e : "已加护盾。"); } }
    private void DoAddState(AxialHex c) { var p = session.Occupancy.At(c); if (p != null) { session.DebugAddState(p.UnitId, Str(stateType, 0), Val(stateStacks), out string e); ShowMsg(e.Length > 0 ? e : "已添加状态。"); } }
    private void DoRemoveState(AxialHex c) { var p = session.Occupancy.At(c); if (p != null) { session.DebugRemoveState(p.UnitId, Val(stateIndex), Val(stateRemoveStacks), out string e); ShowMsg(e.Length > 0 ? e : "已删除状态。"); } }

    private void DoItemAtCell(bool isEquipment) { if (session == null) return; bool ok = session.DebugSpawnItemAtCell(session.Selected.Coord.Q, session.Selected.Coord.R, itemDefId.Text, isEquipment, out string e); ShowMsg(ok ? "已放置。" : e); }
    private void DoItemToSlot() { if (session == null) return; bool ok = session.DebugSpawnItemToSlot(Math.Clamp(Val(slotIdx), 0, 2), itemDefId.Text, out string e); ShowMsg(ok ? "已放入随身槽。" : e); }
    private void DoClearSlot() { if (session == null) return; bool ok = session.DebugClearSlot(Math.Clamp(Val(slotIdx), 0, 2), out string e); ShowMsg(ok ? "已清空。" : e); }
    private void DoEquip() { if (session == null) return; bool ok = session.DebugEquipToHand(equipDefId.Text, handOption.Selected, out string e); ShowMsg(ok ? "已装备。" : e); }
    private void DoTrap(AxialHex c) { if (session != null) { bool ok = session.DebugPlaceTrap(c.Q, c.R, trapId.Text, out string e); ShowMsg(ok ? "已放置陷阱。" : e); } }
    private void DoSpawnEnemy(AxialHex c) { if (session != null) { bool ok = session.DebugSpawnEnemy(c.Q, c.R, Str(monsterId, 0), out string e); ShowMsg(ok ? "已生成敌人。" : e); } }
    private void DoClearEnemies() { session?.DebugClearEnemies(); ShowMsg("已清空敌人。"); }

    // ---------- helpers ----------
    private void Section(VBoxContainer root, string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 15);
        l.AddThemeColorOverride("font_color", new Color("e8d9a0"));
        root.AddChild(l);
    }
    private void Row(VBoxContainer root, string labelText, params Control[] controls)
    {
        var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 6);
        h.AddChild(L(labelText));
        foreach (var c in controls) h.AddChild(c);
        root.AddChild(h);
    }
    private static Label L(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 13);
        l.AddThemeColorOverride("font_color", new Color("c8d0d8"));
        l.CustomMinimumSize = new Vector2(0, 24);
        return l;
    }
    private static LineEdit Edit(float width)
    {
        return new LineEdit { CustomMinimumSize = new Vector2(width, 0), PlaceholderText = "" };
    }
    private static Button Btn(string text, Action onPressed)
    {
        var b = new Button { Text = text };
        b.Pressed += () => onPressed();
        return b;
    }
    private static OptionButton Dropdown(params string[] items)
    {
        var o = new OptionButton();
        foreach (var it in items) o.AddItem(it);
        o.Selected = 0;
        return o;
    }
    private static int Val(LineEdit e) => int.TryParse(e.Text.Trim(), out int v) ? v : 0;
    private static int Str(LineEdit e, int fallback) => int.TryParse(e.Text.Trim(), out int v) ? v : fallback;
    private void ShowMsg(string m) => showMessage?.Invoke(m);
}