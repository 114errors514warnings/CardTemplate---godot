// EventChoiceBattleFlagTests.cs
// 剧情事件选项的「点下即进战」标记（用户口径 2026-10-03）纯逻辑单测：
// `next.type = Battle` 的选项必须被标成 `EntersBattle`（浮层据此跳过「前往地图」、直接让位给战斗内容），
// 其余选项保持「结算后显示前往地图」；标记按配置 Order 跟随选项，不按下标错位。
using System.Collections.Generic;
using Xunit;

public class EventChoiceBattleFlagTests
{
    private static StoryChoiceConfig Choice(string id, int order, string text, string nextType, string levelId = "") => new()
    {
        ChoiceId = id,
        Order = order,
        Text = text,
        EffectPreview = "进入战斗；胜利后结算本次战利品",
        Next = new StoryNextConfig { Type = nextType, ReferenceId = levelId },
    };

    private static StoryEventConfig Config(params StoryChoiceConfig[] choices) => new()
    {
        SchemaVersion = 1,
        EventId = "EVT-TEST",
        Title = "测试事件",
        Summary = "测试摘要",
        Choices = new List<StoryChoiceConfig>(choices),
    };

    [Fact]
    public void BattleChoice_EntersBattleWithoutReturnToMap()
    {
        StoryEventDefinition definition = StoryEventCatalog.ToDefinition(
            Config(
                Choice("C01", 1, "交过路费", StoryNextTypes.Close),
                Choice("C02", 2, "动手夺回被劫的货物", StoryNextTypes.Battle, "F1-D-001")),
            null);

        Assert.Equal(2, definition.Choices.Count);
        Assert.False(definition.Choices[0].EntersBattle, "Close 选项结算后仍应显示「前往地图」。");
        Assert.True(definition.Choices[1].EntersBattle, "next = Battle 的选项点下应立刻进入战斗。");
    }

    [Fact]
    public void FlagsFollowConfiguredOrder_NotDeclarationOrder()
    {
        StoryEventDefinition definition = StoryEventCatalog.ToDefinition(
            Config(
                Choice("C02", 2, "动手夺回被劫的货物", StoryNextTypes.Battle, "F1-D-001"),
                Choice("C01", 1, "交过路费", StoryNextTypes.Close)),
            null);

        Assert.Equal("交过路费", definition.Choices[0].Text);
        Assert.False(definition.Choices[0].EntersBattle);
        Assert.Equal("动手夺回被劫的货物", definition.Choices[1].Text);
        Assert.True(definition.Choices[1].EntersBattle);
    }

    [Fact]
    public void BattleWithoutLevelId_IsNotMarkedAsBattle()
    {
        // 配置校验（`Validate`）会拒绝空 referenceId；这里锁的是标记口径：没有关卡 Id 就没有「进战」可言。
        StoryEventDefinition definition = StoryEventCatalog.ToDefinition(
            Config(Choice("C01", 1, "动手", StoryNextTypes.Battle, "   ")),
            null);

        Assert.False(definition.Choices[0].EntersBattle);
    }

    [Fact]
    public void BattleFlag_KeepsChoiceTextAndPreview()
    {
        StoryEventDefinition definition = StoryEventCatalog.ToDefinition(
            Config(Choice("C01", 1, "动手夺回被劫的货物", StoryNextTypes.Battle, "F1-D-001")),
            null);

        Assert.Equal("动手夺回被劫的货物（进入战斗；胜利后结算本次战利品）", definition.Choices[0].DisplayText);
        Assert.True(definition.Choices[0].EntersBattle);
        Assert.Null(definition.Choices[0].Apply); // 单测不注入回调：标记与回调互不依赖
    }
}
