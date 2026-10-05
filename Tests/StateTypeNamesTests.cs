using System;
using CardSimulator;
using Xunit;

/// <summary>状态表 EnumName 列与 StateType 枚举的映射规则。</summary>
public class StateTypeNamesTests
{
    [Theory]
    [InlineData("Steal", StateType.Steal)]
    [InlineData("steal", StateType.Steal)]
    [InlineData("Vulnerable", StateType.Vulnerable)]
    [InlineData(" counterattack ", StateType.CounterAttack)]
    public void TryParse_AcceptsDefinedEnumNames(string raw, StateType expected)
    {
        Assert.True(StateTypeNames.TryParse(raw, out StateType parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("21")]
    [InlineData("NotAState")]
    [InlineData("Steal 窃取")]
    public void TryParse_RejectsNumbersUnknownAndBlank(string raw) => Assert.False(StateTypeNames.TryParse(raw, out _));

    [Fact]
    public void Matches_OnlyFlagsRealMismatches()
    {
        Assert.True(StateTypeNames.Matches(StateType.Steal, "Steal"));
        Assert.True(StateTypeNames.Matches(StateType.Steal, ""));
        Assert.True(StateTypeNames.Matches(StateType.Steal, null));
        Assert.False(StateTypeNames.Matches(StateType.Steal, "Vulnerable"));
        Assert.False(StateTypeNames.Matches(StateType.Steal, "NotAState"));
        Assert.False(StateTypeNames.Matches(StateType.Steal, "21"));
    }

    [Fact]
    public void StateDefinition_DefaultsEnumNameFromType()
    {
        var named = new StateDefinition(StateType.Steal, "窃取", true, enumName: "Steal");
        var unnamed = new StateDefinition(StateType.Steal, "窃取", true);

        Assert.Equal("Steal", named.EnumName);
        Assert.Equal(nameof(StateType.Steal), unnamed.EnumName);
    }

    [Fact]
    public void StateType_KeepsCsvNumbering()
    {
        // 与 通用State.csv 的 StateType 列保持一致：改枚举必须同步改表（加载器会校验 EnumName 列）。
        Assert.Equal(20, (int)StateType.NextBattleCardFree);
        Assert.Equal(21, (int)StateType.Steal);
        Assert.Equal(22, (int)StateType.Barrier);
    }
}
