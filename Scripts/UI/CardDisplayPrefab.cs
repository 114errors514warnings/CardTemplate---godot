using Godot;
using System.Collections.Generic;
using CardSimulator;

[Tool]
public partial class CardDisplayPrefab : Control
{
    [Export] public NodePath NameLabelPath = "CardFrame/Content/Margin/Body/NameLabel";
    [Export] public NodePath TypeLabelPath = "CardFrame/Content/Margin/Body/MetaRow/TypeLabel";
    [Export] public NodePath CostLabelPath = "CardFrame/Content/Margin/Body/MetaRow/CostLabel";
    [Export] public NodePath DescriptionLabelPath = "CardFrame/Content/Margin/Body/DescriptionLabel";
    [Export] public NodePath AppliedKeywordsContainerPath = "CardFrame/Content/Margin/Body/AppliedKeywordsContainer";

    [Export] public string DisplayName = "未命名卡牌";
    [Export(PropertyHint.MultilineText)] public string DisplayDescription = "卡牌描述";
    [Export] public CardCategory DisplayCategory = CardCategory.Attack;
    [Export] public int DisplayEnergyCost = 0;

    private Label nameLabel;
    private Label typeLabel;
    private Label costLabel;
    private RichTextLabel descriptionLabel;
    private VBoxContainer appliedKeywordsContainer;

    public override void _Ready()
    {
        ResolveNodes();
        RefreshDisplay();
    }

    public void SyncFromCard(Card card)
    {
        if (card == null)
        {
            GD.PushWarning("CardDisplayPrefab.SyncFromCard received null card.");
            return;
        }

        SyncCardData(card.CardName, card.EffectDescription, card.Category, card.EnergyCost);
        RefreshAppliedKeywords(card);
    }

    public void SyncCardData(string cardName, string description, CardCategory category, int energyCost)
    {
        DisplayName = string.IsNullOrWhiteSpace(cardName) ? "未命名卡牌" : cardName;
        DisplayDescription = string.IsNullOrWhiteSpace(description) ? "无描述" : description;
        DisplayCategory = category;
        DisplayEnergyCost = energyCost < 0 ? 0 : energyCost;

        RefreshDisplay();
    }

    public void RefreshAppliedKeywords(Card card)
    {
        if (appliedKeywordsContainer == null)
        {
            return;
        }

        foreach (Node child in appliedKeywordsContainer.GetChildren())
        {
            child.QueueFree();
        }

        if (card == null || card.AppliedKeywords == null)
        {
            return;
        }

        foreach (AppliedKeywordEntry entry in card.AppliedKeywords)
        {
            Label keywordLabel = new Label
            {
                Text = $"[{ToKeywordText(entry.Keyword)}]",
                HorizontalAlignment = HorizontalAlignment.Center,
                Modulate = new Color(0.85f, 0.3f, 0.3f, 1.0f)
            };
            keywordLabel.AddThemeFontSizeOverride("font_size", 16);
            appliedKeywordsContainer.AddChild(keywordLabel);
        }
    }

    private void ResolveNodes()
    {
        nameLabel = GetNodeOrNull<Label>(NameLabelPath);
        typeLabel = GetNodeOrNull<Label>(TypeLabelPath);
        costLabel = GetNodeOrNull<Label>(CostLabelPath);
        descriptionLabel = GetNodeOrNull<RichTextLabel>(DescriptionLabelPath);
        appliedKeywordsContainer = GetNodeOrNull<VBoxContainer>(AppliedKeywordsContainerPath);
    }

    private void RefreshDisplay()
    {
        if (nameLabel != null)
        {
            nameLabel.Text = DisplayName;
        }

        if (typeLabel != null)
        {
            typeLabel.Text = $"类型: {ToCategoryText(DisplayCategory)}";
        }

        if (costLabel != null)
        {
            costLabel.Text = $"费用: {DisplayEnergyCost}";
        }

        if (descriptionLabel != null)
        {
            descriptionLabel.Text = DisplayDescription;
        }
    }

    private static string ToCategoryText(CardCategory category)
    {
        return category switch
        {
            CardCategory.Attack => "攻击",
            CardCategory.Skill => "技能",
            CardCategory.State => "状态",
            _ => category.ToString()
        };
    }

    private static string ToKeywordText(CardKeyWord keyword)
    {
        return keyword switch
        {
            CardKeyWord.Retain => "保留",
            CardKeyWord.Exhaust => "消耗",
            CardKeyWord.InfiniteUpgrade => "可无限升级",
            CardKeyWord.Crit => "暴击",
            _ => keyword.ToString()
        };
    }
}
