using System;
using System.Collections.Generic;
using System.Linq;
using CardSimulator;
using CardSimulator.Battlefield;
using Xunit;

/// <summary>关卡对象的初始值（InitialValue）解析与应用规则。</summary>
public class UnitInitialStateConfigTests
{
    [Fact]
    public void Parse_EmptyText_ReturnsEmpty()
    {
        Assert.Null(UnitInitialStateConfig.Parse("").Hp);
        Assert.Empty(UnitInitialStateConfig.Parse("   ").States);
    }

    [Fact]
    public void Parse_HpAndStates_KeepConfiguredOrder()
    {
        UnitInitialState state = UnitInitialStateConfig.Parse("HP=18|State=3:2|State=Vulnerable:1");

        Assert.Equal(18, state.Hp);
        Assert.Equal(new[] { (StateType.Ignite, 2), (StateType.Vulnerable, 1) }, state.States);
    }

    [Fact]
    public void Parse_UnknownKeys_ArePreservedForLaterMechanics()
    {
        UnitInitialState state = UnitInitialStateConfig.Parse("窃取=1|HP=7|Custom=abc");

        Assert.Equal(7, state.Hp);
        Assert.Equal("1", state.Extras["窃取"]);
        Assert.Equal("abc", state.Extras["custom"]);
    }

    [Fact]
    public void Parse_AcceptsEnumNamesAndNumbers_AndDefaultsSingleStack()
    {
        UnitInitialState numeric = UnitInitialStateConfig.Parse("State=3");
        Assert.Equal(StateType.Ignite, numeric.States[0].State);
        Assert.Equal(1, numeric.States[0].Stacks);
        Assert.Equal(StateType.CounterAttack, UnitInitialStateConfig.Parse("State=counterattack").States[0].State);
        UnitInitialState named = UnitInitialStateConfig.Parse("state=2:4");
        Assert.Equal(StateType.Weak, named.States[0].State);
        Assert.Equal(4, named.States[0].Stacks);
    }

    [Theory]
    [InlineData("HP=0")]
    [InlineData("HP=-3")]
    [InlineData("HP=abc")]
    [InlineData("State=3:0")]
    [InlineData("State=3:-1")]
    [InlineData("State=999:1")]
    [InlineData("State=None:1")]
    [InlineData("State=3:2:5")]
    [InlineData("State=")]
    [InlineData("HP")]
    [InlineData("=1")]
    public void Parse_InvalidKnownValue_ThrowsWithRawText(string raw)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => UnitInitialStateConfig.Parse(raw));
        Assert.Contains("单位初始值无效", error.Message);
        Assert.Contains(raw, error.Message);
    }

    [Fact]
    public void LevelCatalog_Parse_ReadsInitialValueColumn()
    {
        // Parse 接收的已经是数据行（表头由 LoadCsv 读文件时剥离），这里与 Load 的调用方式保持一致。
        string[] lines =
        {
            "M-F1-001,2001,NormalCombat,Low,F1-X-M01,Monster,3101,-4,0,1;0,State=3:2|HP=5,",
            "M-F1-001,2001,NormalCombat,Low,F1-X-M02,Monster,3102,-3,-1,,,",
        };

        BattleLevelConfig config = BattleLevelCatalog.Parse("F1-X", lines);

        Assert.Equal("M-F1-001", config.MapId);
        Assert.Equal(2, config.Objects.Count);
        Assert.Equal("State=3:2|HP=5", config.Objects[0].InitialValue);
        Assert.Equal(string.Empty, config.Objects[1].InitialValue);

        // 关卡内的地图/掉落/类型/难度必须一致，错一行就抛错。
        string[] mismatch = lines.Concat(new[]
        {
            "M-OTHER,2001,NormalCombat,Low,F1-X-M03,Monster,3101,0,0,,,",
        }).ToArray();
        Assert.Throws<ArgumentException>(() => BattleLevelCatalog.Parse("F1-X", mismatch));
    }

    [Fact]
    public void LevelCatalog_ApplyMonstersTo_CarriesPerMonsterInitialValues()
    {
        var definition = new BattleMapDefinition();
        var level = new BattleLevelConfig
        {
            LevelId = "F1-X",
            Objects = new List<BattleLevelObject>
            {
                new("F1-X-M01", "Monster", "3101", -4, 0) { InitialValue = "State=3:2" },
                new("F1-X-T01", "Trap", "毒雾陷阱", 1, -2),
                new("F1-X-M02", "Monster", "3102", -3, -1) { InitialValue = "HP=5" },
            },
        };

        BattleLevelCatalog.ApplyMonstersTo(definition, level);

        Assert.Equal(new[] { 3101, 3102 }, definition.MonsterIds);
        Assert.Equal(new[] { "State=3:2", "HP=5" }, definition.MonsterInitialValues);
        Assert.Equal(2, definition.FixedEnemySpawnCoords.Count);

        var badLevel = new BattleLevelConfig
        {
            LevelId = "F1-Y",
            Objects = new List<BattleLevelObject> { new("F1-Y-M01", "Monster", "史莱姆", 0, 0) },
        };
        Assert.Throws<ArgumentException>(() => BattleLevelCatalog.ApplyMonstersTo(new BattleMapDefinition(), badLevel));
    }
}
