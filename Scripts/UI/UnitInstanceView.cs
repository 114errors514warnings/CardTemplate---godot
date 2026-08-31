using Godot;
using CardSimulator;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class UnitInstanceView : PanelContainer
{
    private ColorRect portrait;
    private Label nameLabel;
    private ProgressBar healthBar;
    private Label healthLabel;
    private Label shieldLabel;
    private RichTextLabel intentionLabel;
    private HFlowContainer stateFlow;

    public IUnitInstance BoundUnit { get; private set; }

    public bool IsPlayer { get; private set; }

    public override void _Ready()
    {
        EnsureNodes();
    }

    public void Bind(IUnitInstance unit, bool isPlayer, Action<StateType, int> onStateHovered, Action onStateHoverExited)
    {
        EnsureNodes();
        BoundUnit = unit;
        IsPlayer = isPlayer;
        CustomMinimumSize = new Vector2(160, 280);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;

        if (portrait != null)
        {
            portrait.Color = isPlayer ? new Color(0.72f, 0.84f, 0.97f) : new Color(0.96f, 0.75f, 0.72f);
        }

        if (nameLabel != null)
        {
            nameLabel.Text = BuildUnitTitle(unit);
            nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        }

        if (healthBar != null)
        {
            healthBar.MinValue = 0;
            healthBar.MaxValue = Math.Max(1, unit.Max_HP);
            healthBar.Value = Mathf.Clamp(unit.HP, 0, unit.Max_HP);
            healthBar.ShowPercentage = false;
            healthBar.AddThemeStyleboxOverride("background", BuildFlatStyle(new Color(0.15f, 0.15f, 0.15f), new Color(0.15f, 0.15f, 0.15f), 0, 8));
            healthBar.AddThemeStyleboxOverride("fill", BuildFlatStyle(new Color(0.82f, 0.15f, 0.16f), new Color(0.82f, 0.15f, 0.16f), 0, 8));
        }

        if (healthLabel != null)
        {
            healthLabel.Text = $"{Math.Max(0, unit.HP)}/{Math.Max(0, unit.Max_HP)}";
            healthLabel.HorizontalAlignment = HorizontalAlignment.Center;
            healthLabel.VerticalAlignment = VerticalAlignment.Center;
        }

        if (shieldLabel != null)
        {
            if (unit.Shield > 0)
            {
                shieldLabel.Text = $"▦{unit.Shield}";
                shieldLabel.Visible = true;
            }
            else
            {
                shieldLabel.Visible = false;
            }
        }

        PopulateStates(unit, onStateHovered, onStateHoverExited);
        RefreshIntentionDisplay();
        intentionLabel?.AddThemeFontSizeOverride("font_size", 24);
        // ApplyHighlight removed
    }

    public void BindPlaceholder(string title)
    {
        EnsureNodes();
        BoundUnit = null;
        IsPlayer = true;
        CustomMinimumSize = new Vector2(160, 280);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;

        if (portrait != null)
        {
            portrait.Color = new Color(0.28f, 0.28f, 0.28f, 0.8f);
        }

        if (nameLabel != null)
        {
            nameLabel.Text = title;
            nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
            nameLabel.Modulate = new Color(0.85f, 0.85f, 0.85f);
        }

        if (healthBar != null)
        {
            healthBar.MinValue = 0;
            healthBar.MaxValue = 1;
            healthBar.Value = 0;
            healthBar.ShowPercentage = false;
            healthBar.AddThemeStyleboxOverride("background", BuildFlatStyle(new Color(0.2f, 0.2f, 0.2f), new Color(0.2f, 0.2f, 0.2f), 0, 8));
            healthBar.AddThemeStyleboxOverride("fill", BuildFlatStyle(new Color(0.35f, 0.35f, 0.35f), new Color(0.35f, 0.35f, 0.35f), 0, 8));
        }

        if (healthLabel != null)
        {
            healthLabel.Text = "";
            healthLabel.HorizontalAlignment = HorizontalAlignment.Center;
            healthLabel.VerticalAlignment = VerticalAlignment.Center;
            healthLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.92f));
        }

        if (shieldLabel != null)
        {
            shieldLabel.Visible = false;
        }
        if (stateFlow != null)
        {
            ClearChildren(stateFlow);
        }

        if (intentionLabel != null)
        {
            intentionLabel.Visible = false;
        }

        AddThemeStyleboxOverride("panel", BuildUnitPanelStyle(isPlayer: true, isSelected: false, isValidTarget: false, isEmpty: true));
    }

    public void RefreshIntentionDisplay()
    {
        if (intentionLabel == null) return;
        if (BoundUnit == null || IsPlayer || BoundUnit.HP <= 0) { intentionLabel.Visible = false; return; }
        if (BoundUnit is MonsterInstance monster)
        {
            if (monster.SelectedIntention == null || monster.SelectedIntention.Length == 0)
            {
                intentionLabel.Visible = false;
                return;
            }

            var dmgTotal = 0;
            var dmgCount = 0;
            var shieldTotal = 0;
            var shieldCount = 0;
            var debuffStates = new System.Collections.Generic.List<string>();
            var buffStates = new System.Collections.Generic.List<string>();

            foreach (int[] effectConfig in monster.SelectedIntention)
            {
                if (effectConfig == null || effectConfig.Length == 0) continue;
                int et = effectConfig[0];
                int[] args = effectConfig.Length > 1
                    ? new ArraySegment<int>(effectConfig, 1, effectConfig.Length - 1).ToArray()
                    : System.Array.Empty<int>();
                CardSimulator.EffectType effectType = (CardSimulator.EffectType)et;

                switch (effectType)
                {
                    case CardSimulator.EffectType.Damage:
                        dmgTotal += monster.Attack + (args.Length > 0 ? args[0] : 0);
                        dmgCount++;
                        break;
                    case CardSimulator.EffectType.Shield:
                        shieldTotal += monster.Defend + (args.Length > 0 ? args[0] : 0);
                        shieldCount++;
                        break;
                    case CardSimulator.EffectType.AddState:
                        bool isDebuff = args.Length > 0 && (CardSimulator.EffectTargetType)args[0] == CardSimulator.EffectTargetType.SelectedTarget;
                        int stateTypeVal = args.Length >= 2 ? args[1] : 0;
                        int stacks = args.Length >= 3 ? args[2] : 1;
                        string stateName = "";
                        if (stateTypeVal > 0 && System.Enum.IsDefined(typeof(CardSimulator.StateType), stateTypeVal))
                        {
                            var st = (CardSimulator.StateType)stateTypeVal;
                            if (LoadingSystem.StateDictionary.TryGetValue(st, out var def) && !string.IsNullOrWhiteSpace(def.Name))
                                stateName = def.Name;
                            else
                                stateName = st.ToString();
                        }
                        stateName += (stacks > 1 ? "x" + stacks : "");
                        if (isDebuff)
                            debuffStates.Add(stateName);
                        else
                            buffStates.Add(stateName);
                        break;
                }
            }

            var sb = new System.Text.StringBuilder();
            if (dmgCount > 0)
            {
                if (sb.Length > 0) sb.Append("\n");
                sb.Append("\u653b\u51fb\uff1a").Append(dmgTotal / dmgCount).Append("*").Append(dmgCount);
            }
            if (shieldCount > 0)
            {
                if (sb.Length > 0) sb.Append("\n");
                sb.Append("\u9632\u5fa1\uff1a").Append(shieldTotal / shieldCount).Append("*").Append(shieldCount);
            }
            if (debuffStates.Count > 0)
            {
                if (sb.Length > 0) sb.Append("\n");
                sb.Append("\u5f31\u5316\uff1a").Append(string.Join(" ", debuffStates));
            }
            if (buffStates.Count > 0)
            {
                if (sb.Length > 0) sb.Append("\n");
                sb.Append("\u5f3a\u5316\uff1a").Append(string.Join(" ", buffStates));
            }

            intentionLabel.Text = sb.ToString();
            intentionLabel.Visible = sb.Length > 0;
        }
    }

    public void HideIntentionDisplay()
    {
        if (intentionLabel != null) intentionLabel.Visible = false;
    }

	public void ShowDeadOverlay()
	{
		EnsureNodes();
		if (healthBar == null) return;

		// 移除旧的死亡覆盖层（如果存在）
		for (int i = healthBar.GetChildCount() - 1; i >= 0; i--)
		{
			Node child = healthBar.GetChild(i);
			if (child is Label label && label.Name == "DeadOverlay")
			{
				label.QueueFree();
			}
		}

		Label deadLabel = new Label();
		deadLabel.Name = "DeadOverlay";
		deadLabel.Text = "已死亡";
		deadLabel.HorizontalAlignment = HorizontalAlignment.Center;
		deadLabel.VerticalAlignment = VerticalAlignment.Center;
		deadLabel.AddThemeFontSizeOverride("font_size", 20);
		deadLabel.Modulate = new Color(0.85f, 0.85f, 0.85f);
		deadLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.7f));
		deadLabel.AddThemeConstantOverride("outline_size", 2);
		deadLabel.SetAnchorsPreset(LayoutPreset.FullRect);
		deadLabel.MouseFilter = MouseFilterEnum.Ignore;

		healthBar.AddChild(deadLabel);
	}


    public void SetHealthLabelText(string text)
    {
        if (healthLabel != null)
        {
            healthLabel.Text = text;
            healthLabel.HorizontalAlignment = HorizontalAlignment.Center;
            healthLabel.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    /// <summary>
    /// 结算时高亮放大：on=true 时 Scale=1.15，false 时恢复 1.0；走 tween 平滑过渡。
    /// </summary>
    public void SetHighlighted(bool on)
    {
        float targetScale = on ? 1.15f : 1.0f;
        Tween tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Sine);
        tween.SetEase(Tween.EaseType.Out);
        tween.TweenProperty(this, "scale", new Vector2(targetScale, targetScale), 0.4);
    }

    private void EnsureNodes()
    {
        portrait ??= GetNodeOrNull<ColorRect>("Margin/Body/PortraitCenter/Portrait");
        nameLabel ??= GetNodeOrNull<Label>("Margin/Body/NameLabel");
        healthBar ??= GetNodeOrNull<ProgressBar>("Margin/Body/HealthBarRoot/HealthBar");
        healthLabel ??= GetNodeOrNull<Label>("Margin/Body/HealthBarRoot/HealthLabel");
        shieldLabel ??= GetNodeOrNull<Label>("Margin/Body/HealthBarRoot/ShieldLabel");
        intentionLabel ??= GetNodeOrNull<RichTextLabel>("Margin/Body/IntentionLabel");
        stateFlow ??= GetNodeOrNull<HFlowContainer>("Margin/Body/StateCenter/StateFlow");
    }

    private void PopulateStates(IUnitInstance unit, Action<StateType, int> onStateHovered, Action onStateHoverExited)
    {
        if (stateFlow == null)
        {
            return;
        }

        ClearChildren(stateFlow);

        if (unit == null || unit.States.Count == 0)
        {
            Label emptyLabel = new Label();
            emptyLabel.Text = "\u65e0\u72b6\u6001";
            emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
            emptyLabel.Modulate = new Color(0.82f, 0.82f, 0.82f);
            stateFlow.AddChild(emptyLabel);
            return;
        }

        foreach (KeyValuePair<StateType, StateRuntimeData> pair in unit.States.OrderBy(current => current.Key))
        {
            Button chip = CreateStateChip(pair.Key, pair.Value.Stacks, onStateHovered, onStateHoverExited);
            stateFlow.AddChild(chip);
        }
    }

    private static Button CreateStateChip(StateType stateType, int stacks, Action<StateType, int> onStateHovered, Action onStateHoverExited)
    {
        Button chip = new Button();
        chip.Flat = true;
        chip.CustomMinimumSize = new Vector2(60, 22);
        chip.Text = BuildStateChipText(stateType, stacks);
        chip.AddThemeFontSizeOverride("font_size", 11);
        chip.AddThemeColorOverride("font_color", Colors.White);
        Color background = GetStateChipBackground(stateType);
        chip.AddThemeStyleboxOverride("normal", BuildFlatStyle(background, background, 0, 6));
        chip.AddThemeStyleboxOverride("hover", BuildFlatStyle(background.Lightened(0.15f), background.Lightened(0.15f), 0, 6));
        chip.MouseEntered += () => onStateHovered?.Invoke(stateType, stacks);
        chip.MouseExited += () => onStateHoverExited?.Invoke();
        return chip;
    }

    private static string BuildUnitTitle(IUnitInstance unit)
    {
        if (unit is Unit typedUnit && !string.IsNullOrWhiteSpace(typedUnit.Name))
        {
            return typedUnit.Name;
        }
        return unit == null ? string.Empty : $"\u5355\u4f4d {unit.UniqueInGameId}";
    }

    private static string BuildStateChipText(StateType stateType, int stacks)
    {
        string name = GetStateDisplayName(stateType);
        return stacks > 1 ? $"{name}x{stacks}" : name;
    }

    private static string GetStateDisplayName(StateType stateType)
    {
        if (LoadingSystem.StateDictionary.TryGetValue(stateType, out StateDefinition definition) && !string.IsNullOrWhiteSpace(definition.Name))
        {
            return definition.Name;
        }
        return stateType.ToString();
    }

    private static Color GetStateChipBackground(StateType stateType)
    {
        return stateType switch
        {
            StateType.Vulnerable => new Color(0.78f, 0.3f, 0.22f),
            StateType.Weak => new Color(0.58f, 0.39f, 0.14f),
            StateType.Void => new Color(0.27f, 0.12f, 0.33f),
            StateType.CounterAttack => new Color(0.12f, 0.44f, 0.54f),
            StateType.WhirlwindSlash => new Color(0.13f, 0.51f, 0.45f),
            StateType.AddAttack => new Color(0.63f, 0.12f, 0.17f),
            StateType.ExtraEnergy => new Color(0.19f, 0.49f, 0.16f),
            StateType.CourageArmor => new Color(0.18f, 0.31f, 0.63f),
            _ => new Color(0.33f, 0.33f, 0.33f),
        };
    }

    private static StyleBoxFlat BuildUnitPanelStyle(bool isPlayer, bool isSelected, bool isValidTarget, bool isEmpty = false)
    {
        Color background = isEmpty ? new Color(0.11f, 0.11f, 0.11f, 0.25f) : Colors.Transparent;
        Color border = isValidTarget
            ? new Color(0.88f, 0.18f, 0.18f)
            : isSelected ? new Color(0.18f, 0.43f, 0.84f) : Colors.Transparent;
        int borderWidth = isSelected || isValidTarget ? 2 : 0;
        return BuildFlatStyle(background, border, borderWidth, 10);
    }

    private static StyleBoxFlat BuildFlatStyle(Color background, Color border, int borderWidth, int radius)
    {
        StyleBoxFlat styleBox = new StyleBoxFlat();
        styleBox.BgColor = background;
        styleBox.BorderColor = border;
        styleBox.SetBorderWidthAll(borderWidth);
        styleBox.SetCornerRadiusAll(radius);
        styleBox.ContentMarginLeft = 6;
        styleBox.ContentMarginTop = 6;
        styleBox.ContentMarginRight = 6;
        styleBox.ContentMarginBottom = 6;
        return styleBox;
    }

    private static void ClearChildren(Node node)
    {
        if (node == null)
        {
            return;
        }
        foreach (Node child in node.GetChildren())
        {
            child.QueueFree();
        }
    }
}
