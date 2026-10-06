using Xunit;

/// <summary>
/// 卡牌归属与掉落规则（2026-10-03 用户口径）：战斗掉落的牌必须是**该角色的专有牌**，
/// 通用卡表默认不作候选；「特殊装备效果放行」是 <see cref="CardRewardOwnership.IncludeGenericSources"/> 这个开口。
/// </summary>
public class CardRewardOwnershipTests
{
    [Theory]
    [InlineData("通用/通用Card.csv")]
    [InlineData("/通用/通用Card.csv")]
    [InlineData(" 通用/通用Card.csv ")]
    [InlineData("通用\\通用Card.csv")]
    public void IsGenericSource_AcceptsGenericTablePath(string cardSource)
        => Assert.True(CardRewardOwnership.IsGenericSource(cardSource));

    [Theory]
    [InlineData("勇士Card.csv")]
    [InlineData("重剑手Card.csv")]
    [InlineData("精灵Card.csv")]
    [InlineData("法师Card.csv")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsGenericSource_RejectsCharacterOwnTables(string cardSource)
        => Assert.False(CardRewardOwnership.IsGenericSource(cardSource));

    [Fact]
    public void IncludeGenericSources_DefaultsToFalse_SoGenericCardsNeverDrop()
    {
        // 掉落默认不含通用牌；只有特殊装备效果显式打开这个开关才会变（用户口径「留个口」）。
        Assert.False(CardRewardOwnership.IncludeGenericSources);
    }
}