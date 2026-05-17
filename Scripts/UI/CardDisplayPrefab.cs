using Godot;
using CardSimulator;

[Tool]
public partial class CardDisplayPrefab : Control
{
    [Export] public NodePath NameLabelPath = "CardFrame/Content/Margin/Body/NameLabel";
    [Export] public NodePath TypeLabelPath = "CardFrame/Content/Margin/Body/MetaRow/TypeLabel";
    [Export] public NodePath CostLabelPath = "CardFrame/Content/Margin/Body/MetaRow/CostLabel";
    [Export] public NodePath DescriptionLabelPath = "CardFrame/Content/Margin/Body/DescriptionLabel";

    [Export] public string DisplayName = "未命名卡牌";
    [Export(PropertyHint.MultilineText)] public string DisplayDescription = "卡牌描述";
    [Export] public CardCategory DisplayCategory = CardCategory.Attack;
    [Export] public int DisplayEnergyCost = 0;

    private Label nameLabel;
    private Label typeLabel;
    private Label costLabel;
    private RichTextLabel descriptionLabel;

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
    }

    public void SyncCardData(string cardName, string description, CardCategory category, int energyCost)
    {
        DisplayName = string.IsNullOrWhiteSpace(cardName) ? "未命名卡牌" : cardName;
        DisplayDescription = string.IsNullOrWhiteSpace(description) ? "无描述" : description;
        DisplayCategory = category;
        DisplayEnergyCost = energyCost < 0 ? 0 : energyCost;

        RefreshDisplay();
    }

    private void ResolveNodes()
    {
        nameLabel = GetNodeOrNull<Label>(NameLabelPath);
        typeLabel = GetNodeOrNull<Label>(TypeLabelPath);
        costLabel = GetNodeOrNull<Label>(CostLabelPath);
        descriptionLabel = GetNodeOrNull<RichTextLabel>(DescriptionLabelPath);
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
}
