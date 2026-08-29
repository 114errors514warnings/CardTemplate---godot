// StateCardPipelineHelpersTests.cs
// 覆盖 StateCardPipeline 的两个静态纯函数：ParseEffectTargetType / GetEffectArguments

using System;
using CardSimulator;
using Xunit;

public class StateCardPipelineHelpersTests
{
    [Fact]
    public void ParseEffectTargetType_NullOrEmpty_ReturnsAuto()
    {
        Assert.Equal(EffectTargetType.Auto, StateCardPipeline.ParseEffectTargetType(null));
        Assert.Equal(EffectTargetType.Auto, StateCardPipeline.ParseEffectTargetType(System.Array.Empty<int>()));
    }

    [Fact]
    public void ParseEffectTargetType_ValidValue_ReturnsParsed()
    {
        Assert.Equal(EffectTargetType.Self, StateCardPipeline.ParseEffectTargetType(new[] { 1 }));
        Assert.Equal(EffectTargetType.SelectedTarget, StateCardPipeline.ParseEffectTargetType(new[] { 2 }));
        Assert.Equal(EffectTargetType.AllEnemies, StateCardPipeline.ParseEffectTargetType(new[] { 3 }));
    }

    [Fact]
    public void ParseEffectTargetType_UndefinedValue_FallsBackToAuto()
    {
        // 100 不在 EffectTargetType 枚举里
        Assert.Equal(EffectTargetType.Auto, StateCardPipeline.ParseEffectTargetType(new[] { 100 }));
        Assert.Equal(EffectTargetType.Auto, StateCardPipeline.ParseEffectTargetType(new[] { -1 }));
    }

    [Fact]
    public void GetEffectArguments_NullOrShort_ReturnsEmpty()
    {
        Assert.Empty(StateCardPipeline.GetEffectArguments(null));
        Assert.Empty(StateCardPipeline.GetEffectArguments(System.Array.Empty<int>()));
        Assert.Empty(StateCardPipeline.GetEffectArguments(new[] { 5 }));
    }

    [Fact]
    public void GetEffectArguments_StripsFirstElement()
    {
        // 第一个元素是 EffectTargetType，剩下的才是 effect args
        var args = StateCardPipeline.GetEffectArguments(new[] { 1, 10, 20, 30 });
        Assert.Equal(3, args.Length);
        Assert.Equal(10, args[0]);
        Assert.Equal(20, args[1]);
        Assert.Equal(30, args[2]);
    }

    [Fact]
    public void GetEffectArguments_TwoElementInput_ReturnsSingleArg()
    {
        var args = StateCardPipeline.GetEffectArguments(new[] { 99, 42 });
        Assert.Single(args);
        Assert.Equal(42, args[0]);
    }
}
