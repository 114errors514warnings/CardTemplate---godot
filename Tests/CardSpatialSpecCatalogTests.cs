// CardSpatialSpecCatalogTests.cs
// 覆盖「多张卡表 → 同一份空间规格字典」的合并缝（2026-10-04，T1 / 代码需求清单 P2-29）。
// 纯逻辑：不触碰 LoadCsv / DirAccess / FileAccess（测试工程没有 GodotSharp 引用，见 Tests.csproj）。
using System;
using System.Collections.Generic;
using CardSimulator.Battlefield;
using Xunit;

public class CardSpatialSpecCatalogTests
{
    private static string[] Row(string line) => line.Split(',');

    [Fact]
    public void IsCardTableHeader_AcceptsCardIdHeader_ToleratingBomCaseAndPadding()
    {
        Assert.True(CardSpatialSpecCatalog.IsCardTableHeader(Row("CardId,CardName,CardType")));
        Assert.True(CardSpatialSpecCatalog.IsCardTableHeader(Row("\uFEFFCardId,CardName")));
        Assert.True(CardSpatialSpecCatalog.IsCardTableHeader(Row(" cardid ,CardName")));
        Assert.False(CardSpatialSpecCatalog.IsCardTableHeader(Row("CharacterId,CardSource")));
        Assert.False(CardSpatialSpecCatalog.IsCardTableHeader(Array.Empty<string>()));
        Assert.False(CardSpatialSpecCatalog.IsCardTableHeader(null));
    }

    [Fact]
    public void IsSpatialCardTableHeader_RequiresCardIdAndSpatialShapeColumn()
    {
        // 只有「CardId 开头 + 含 SpatialShape 列」的表才进空间规格字典（见 BattleCardSpatialRepository.LoadAll）。
        Assert.True(CardSpatialSpecCatalog.IsSpatialCardTableHeader(Row("CardId,CardName,CardType,EnergyCost,EffectType,EffectDesc,Params,CardKeyWord,ConditionParams,SpatialShape,SpatialArgs,TrapId")));
        // 通用表（8 列）/ 重剑手表（9 列）：是卡表但没声明空间列 → 不进字典，攻击牌保持回落 Single 的既有语义。
        Assert.False(CardSpatialSpecCatalog.IsSpatialCardTableHeader(Row("CardId,CardName,CardType,EnergyCost,EffectType,EffectDesc,Params,CardKeyWord")));
        Assert.False(CardSpatialSpecCatalog.IsSpatialCardTableHeader(Row("CardId,CardName,CardType,EnergyCost,EffectType,EffectDesc,Params,CardKeyWord,ConditionParams")));
        Assert.False(CardSpatialSpecCatalog.IsSpatialCardTableHeader(Row("CharacterId,CardSource")));
        Assert.False(CardSpatialSpecCatalog.IsSpatialCardTableHeader(null));
    }

    [Fact]
    public void Merge_CombinesSeveralTablesIntoOneDictionary()
    {
        var target = new Dictionary<int, CardSpatialSpec>();
        List<string> conflicts = CardSpatialSpecCatalog.Merge(new[]
        {
            Row("11001002,横扫,Attack,2,Damage,扇形,2;1,,,Fan,Range=1,"),
        }, target, "勇士Card.csv");
        conflicts.AddRange(CardSpatialSpecCatalog.Merge(new[]
        {
            Row("11003001,穿林箭,Attack,1,Damage,穿透箭,2;1,,,Line,Range=5;Length=5;Pierce,"),
            Row("21004003,寒霜之地,Skill,1,Damage|AddState,寒霜,2;0|2;2;1,,,Trap,Range=4,frost_field"),
        }, target, "法师Card.csv"));

        Assert.Empty(conflicts);
        Assert.Equal(3, target.Count);
        Assert.Equal(CardSpatialShape.Fan, target[11001002].Shape);

        CardSpatialSpec arrow = target[11003001];
        Assert.Equal(CardSpatialShape.Line, arrow.Shape);
        Assert.Equal(5, arrow.MaxRange);
        Assert.Equal(5, arrow.Length);
        Assert.True(arrow.Penetrates);

        CardSpatialSpec frost = target[21004003];
        Assert.Equal(CardSpatialShape.Trap, frost.Shape);
        Assert.Equal(4, frost.MaxRange);
        Assert.Equal("frost_field", frost.TrapId);
    }

    [Fact]
    public void Merge_DuplicateCardId_KeepsFirstTableAndReportsConflict()
    {
        var target = new Dictionary<int, CardSpatialSpec>();
        CardSpatialSpecCatalog.Merge(new[]
        {
            Row("11003001,穿林箭,Attack,1,Damage,穿透箭,2;1,,,Line,Range=5;Length=5;Pierce,"),
        }, target, "精灵Card.csv");

        List<string> conflicts = CardSpatialSpecCatalog.Merge(new[]
        {
            Row("11003001,穿林箭,Attack,1,Damage,重复条目,2;1,,,Single,Range=1,"),
        }, target, "另一个Card.csv");

        Assert.Single(conflicts);
        Assert.Contains("11003001", conflicts[0]);
        Assert.Contains("另一个Card.csv", conflicts[0]);
        Assert.Equal(CardSpatialShape.Line, target[11003001].Shape);   // 先到先得：文件顺序（调用方先排序）决定优先级
    }

    [Fact]
    public void Merge_ShortRows_FallBackToNoSpatialShape()
    {
        var target = new Dictionary<int, CardSpatialSpec>();
        // 通用表表头 8 列 / 重剑手表 9 列 → 索引 9 / 10 / 11 越界取值返回空串 → Shape=None（现役两表零影响）
        CardSpatialSpecCatalog.Merge(new[]
        {
            Row("10000001,攻击,Attack,1,Damage,攻击,2"),
            Row("11002001,盾牌冲撞,Attack,1,ShieldSlam,额外伤害,2,Retain,"),
        }, target, "通用与重剑手");

        Assert.Equal(2, target.Count);
        Assert.Equal(CardSpatialShape.None, target[10000001].Shape);
        Assert.False(target[10000001].HasSpatial);
        Assert.Equal(CardSpatialShape.None, target[11002001].Shape);
    }

    [Fact]
    public void Merge_SkipsNullEmptyHeaderAndBlankRows()
    {
        var target = new Dictionary<int, CardSpatialSpec>();
        CardSpatialSpecCatalog.Merge(new[]
        {
            null,
            Array.Empty<string>(),
            Row("CardId,CardName,CardType,EnergyCost,EffectType,EffectDesc,Params,CardKeyWord,ConditionParams,SpatialShape,SpatialArgs,TrapId"),
            Row("21001005,埋设陷阱,Skill,1,None,布置一次性的测试陷阱, ,,,Trap,Range=1,test_trap"),
        }, target, "混合来源");

        Assert.Single(target);
        Assert.Equal("test_trap", target[21001005].TrapId);
        Assert.Equal(CardSpatialShape.Trap, target[21001005].Shape);
    }

    [Fact]
    public void Merge_ThrustRow_DisablesPierce()
    {
        var target = new Dictionary<int, CardSpatialSpec>();
        CardSpatialSpecCatalog.Merge(new[]
        {
            Row("11001003,烈闪突,Attack,1,Damage,突刺,2;3,,,Line,Range=2;Length=2;AttackMode=Thrust,"),
        }, target, "勇士Card.csv");

        CardSpatialSpec thrust = target[11001003];
        Assert.Equal(CardSpatialShape.Line, thrust.Shape);
        Assert.Equal(2, thrust.MaxRange);
        Assert.Equal(2, thrust.Length);
        Assert.False(thrust.Penetrates);
    }
}
