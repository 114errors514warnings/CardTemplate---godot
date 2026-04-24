using Godot;
using System.Text;

/// <summary>
/// 游戏初始化脚本
/// </summary>
public partial class Init : Control
{
    public override void _Ready()
    {
        // 获取Console RichTextLabel
        var console = GetNode<RichTextLabel>("ConsoleContainer/Console");

        // 构建输出字符串
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Loading cards...");

        // 直接调用LoadCards
        var cards = LoadingSystem.LoadCards("res://DataBase/Card/通用/通用Card.csv", true);

        sb.AppendLine($"Loaded {cards.Count} cards");

        // 遍历并打印每张卡牌信息
        foreach (var kvp in cards)
        {
            sb.AppendLine($"id:{kvp.Key}, type:{kvp.Value.Category}, name:{kvp.Value.EffectDescription}");
        }

        sb.AppendLine("Loading characters...");

        // 调用LoadCharacters
        var characters = LoadingSystem.LoadCharacters("res://DataBase/Unit/Character.csv", true);

        sb.AppendLine($"Loaded {characters.Count} characters");

        // 遍历并打印每个角色信息
        foreach (var kvp in characters)
        {
            sb.AppendLine($"id:{kvp.Key}, name:{kvp.Value.Name}, MAX_HP:{kvp.Value.MAX_HP}, drawCardNum:{kvp.Value.drawCardNum}");
        }

        sb.AppendLine("Loading monsters...");

        // 调用LoadMonsters
        var monsters = LoadingSystem.LoadMonsters("res://DataBase/Unit/Monster.csv", true);

        sb.AppendLine($"Loaded {monsters.Count} monsters");

        // 遍历并打印每个怪物信息
        foreach (var kvp in monsters)
        {
            sb.AppendLine($"id:{kvp.Key}, name:{kvp.Value.Name}, MAX_HP:{kvp.Value.MAX_HP}, MaxActionTime:{kvp.Value.MaxActionTime}");
        }

        // 设置Console的文本
        console.Text = sb.ToString();

        // 滚动到底部
        CallDeferred("ScrollToBottom");
    }

    private void ScrollToBottom()
    {
        var scrollContainer = GetNode<ScrollContainer>("ConsoleContainer");
        scrollContainer.ScrollVertical = (int)scrollContainer.GetVScrollBar().MaxValue;
    }
}