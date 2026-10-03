// AreaObjectSpecTests.cs
// 区域物 / 格点效果表（`DataBase/Battlefield/AreaObject.csv`，2026-10-04 施工清单 T3 + T8）的纯逻辑 + 文件级校验。
// 用意与 CardTableFileTests 相同：把「配表写了、程序读不到 / 读错」的静默退化变成红灯 —— 进入触发与
// 格点 buff / debuff 现在是查表结算，表结构或列义被改错必须马上暴露。
// 不触碰 Godot I/O（加载器 `LoadAreaObjectCsv` / 仓库 `BattlefieldAreaObjectRepository` 由 `--battlefield-smoke` 覆盖）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CardSimulator;
using CardSimulator.Battlefield;
using Xunit;

public class AreaObjectSpecTests
{
    private static string[] Row(string line) => line.Split(',');

    private static string[] ReadTable()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Battlefield", "AreaObject.csv");
        Assert.True(File.Exists(path), $"缺表：{path}（检查 Tests.csproj 的 None Include 是否拷贝）");
        return File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
    }

    private static List<AreaObjectSpec> DataRows() => ReadTable().Skip(1)
        .Select(line => AreaObjectSpec.ParseFields(Row(line))).Where(spec => spec != null).ToList();

    [Fact]
    public void IsAreaObjectTableHeader_RequiresTrapIdFirstColumn()
    {
        Assert.True(AreaObjectCatalog.IsAreaObjectTableHeader(Row("TrapId,Name,TriggerTiming,SideFilter,MaxTriggers,DecayTiming,EffectType,Params")));
        Assert.True(AreaObjectCatalog.IsAreaObjectTableHeader(Row("trapid,name")));
        Assert.True(AreaObjectCatalog.IsAreaObjectTableHeader(Row("\uFEFFTrapId,Name")));
        Assert.False(AreaObjectCatalog.IsAreaObjectTableHeader(Row("CardId,CardName")));
        Assert.False(AreaObjectCatalog.IsAreaObjectTableHeader(null));
    }

    [Fact]
    public void ParseFields_ReadsTimingSideFilterTriggersDecayEffectsAndParams()
    {
        AreaObjectSpec spec = AreaObjectSpec.ParseFields(Row("vine_sentinel,藤蔓哨卫,OnEnter|OnTurnEnd,Enemy,3,OnTurnEnd,Damage|AddState,2;2|2;2;1"));

        Assert.NotNull(spec);
        Assert.Equal("vine_sentinel", spec.TrapId);
        Assert.True(spec.TriggersOnEnter);
        Assert.True(spec.TriggersOnTurnEnd);
        Assert.Equal(CellEffectSideFilter.Enemy, spec.SideFilter);
        Assert.Equal(3, spec.MaxTriggers);
        Assert.Equal(TerrainDecayTiming.OnTurnEnd, spec.DecayTiming);
        Assert.Equal(new[] { EffectType.Damage, EffectType.AddState }, spec.EffectTypes);
        Assert.Equal(2, spec.Params.Length);
        Assert.Equal(new[] { 2, 2 }, spec.Params[0]);
        Assert.Equal(new[] { 2, 2, 1 }, spec.Params[1]);
        Assert.True(spec.HasEffects);
    }

    [Fact]
    public void ParseFields_DefaultsToEnterTriggererUnlimitedAndNoDecay()
    {
        AreaObjectSpec spec = AreaObjectSpec.ParseFields(Row("plain_trap,普通陷阱"));

        Assert.NotNull(spec);
        Assert.True(spec.TriggersOnEnter);
        Assert.False(spec.TriggersOnTurnEnd);
        Assert.Equal(CellEffectSideFilter.Triggerer, spec.SideFilter);
        Assert.Equal(0, spec.MaxTriggers);
        Assert.Equal(TerrainDecayTiming.Never, spec.DecayTiming);
        Assert.Empty(spec.EffectTypes);
        Assert.False(spec.HasEffects);
    }

    [Fact]
    public void ParseFields_EmptyTrapIdIsRejected()
    {
        Assert.Null(AreaObjectSpec.ParseFields(Array.Empty<string>()));
        Assert.Null(AreaObjectSpec.ParseFields(Row(",无名,OnEnter")));
        Assert.Null(AreaObjectSpec.ParseFields(null));
    }

    [Fact]
    public void ParseTiming_UnknownOrEmptyFallsBackToOnEnter()
    {
        Assert.Equal(AreaTriggerTiming.OnEnter, AreaObjectSpec.ParseTiming(""));
        Assert.Equal(AreaTriggerTiming.OnEnter, AreaObjectSpec.ParseTiming("Foo"));            // 全是未知值 → 回落 OnEnter
        Assert.Equal(AreaTriggerTiming.OnTurnEnd, AreaObjectSpec.ParseTiming("OnTurnEnd,Foo")); // 未知值被忽略，已知值保留
        Assert.Equal(AreaTriggerTiming.OnTurnEnd, AreaObjectSpec.ParseTiming("OnTurnEnd"));
        Assert.Equal(AreaTriggerTiming.OnEnter | AreaTriggerTiming.OnTurnEnd, AreaObjectSpec.ParseTiming("OnEnter；OnTurnEnd"));
    }

    [Fact]
    public void Merge_FirstDefinitionWins_AndHeaderRowsAreSkipped()
    {
        var target = new Dictionary<string, AreaObjectSpec>(StringComparer.Ordinal);
        List<string> conflicts = AreaObjectCatalog.Merge(new[]
        {
            Row("TrapId,Name,TriggerTiming,SideFilter,MaxTriggers,DecayTiming,EffectType,Params"),
            Row("test_trap,测试陷阱,OnEnter,Triggerer,0,Never,Damage,2;3"),
        }, target, "AreaObject.csv");
        conflicts.AddRange(AreaObjectCatalog.Merge(new[]
        {
            Row("test_trap,重复行,OnEnter,Triggerer,0,Never,Damage,2;9"),
            Array.Empty<string>(),
        }, target, "另一来源"));

        Assert.Single(target);
        Assert.Single(conflicts);
        Assert.Contains("test_trap", conflicts[0]);
        Assert.Equal(3, target["test_trap"].Params[0][1]);   // 先到先得
    }

    // ── 文件级：现役 TrapId 与格点效果必须都在表里（T3 / T8 的验收前置）──

    [Fact]
    public void Table_DeclaresEveryTrapUsedByRuntimeCards()
    {
        List<string> trapIds = DataRows().Select(spec => spec.TrapId).ToList();

        Assert.Contains("test_trap", trapIds);       // 勇士 埋设陷阱 21001005 + debug.battle.place_trap
        Assert.Contains("vine_sentinel", trapIds);   // 精灵 古树哨卫 21003003
        Assert.Contains("frost_field", trapIds);     // 法师 寒霜之地 21004003
    }

    [Fact]
    public void TrapRows_MatchTheirCardText()
    {
        AreaObjectSpec vine = DataRows().First(spec => spec.TrapId == "vine_sentinel");
        Assert.True(vine.TriggersOnEnter);
        Assert.Equal(CellEffectSideFilter.Triggerer, vine.SideFilter);
        Assert.Equal(new[] { EffectType.Damage, EffectType.AddState }, vine.EffectTypes);
        Assert.Equal(2, vine.Params[0][1]);                    // 2 点伤害
        Assert.Equal((int)StateType.Weak, vine.Params[1][1]);  // 1 层虚弱
        Assert.Equal(1, vine.Params[1][2]);

        AreaObjectSpec frost = DataRows().First(spec => spec.TrapId == "frost_field");
        Assert.True(frost.TriggersOnEnter);
        Assert.Equal(new[] { EffectType.AddState }, frost.EffectTypes);   // 只给虚弱，不掉血
        Assert.Equal((int)StateType.Weak, frost.Params[0][1]);

        AreaObjectSpec test = DataRows().First(spec => spec.TrapId == "test_trap");
        Assert.Equal(new[] { EffectType.Damage }, test.EffectTypes);      // 与改动前的写死 3 点等价
        Assert.Equal(3, test.Params[0][1]);
    }

    [Fact]
    public void Table_KeepsABuffRowForTheCellEffectChannel()
    {
        // T8 的「格上施加 buff」通道：同一张表里必须能写出增益格（不再只服务陷阱伤害）。
        AreaObjectSpec buff = DataRows().FirstOrDefault(spec => spec.SideFilter == CellEffectSideFilter.Ally);

        Assert.NotNull(buff);
        Assert.True(buff.TriggersOnTurnEnd);
        Assert.Contains(EffectType.AddState, buff.EffectTypes);
    }
}
