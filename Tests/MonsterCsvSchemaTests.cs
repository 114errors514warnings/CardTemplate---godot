// MonsterCsvSchemaTests.cs
// 覆盖「是否为爪牙」的数据载体（9 月施工文档 §35 / 玩法 §7.4、§7.5 / 代码需求清单 10.3、10.5）：
// `Monster.csv` 的 `IsMinion` 列 → `Monster.IsMinion` → `RunBattleScene` 的 `EnemyValueSample(..., IsMinion)`。
// 列结构 / 取值解析在 `MonsterCsvSchema`（不引用 Godot），因此可在纯 .NET 测试里直接喂字段数组。
// CSV 文件与「行 → Monster 标记」的接线由 `--battlefield-smoke` 的 `VerifyMonsterTableColumns()` 覆盖：
// 本测试工程只引用了主 dll（无 GodotSharp 引用），不能触碰 `LoadMonsterCsv`（`Node` 派生类）这类类型。
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class MonsterCsvSchemaTests
{
    private const int IsMinionColumnIndex = 15;

    /// <summary>按真实表头拼接：前 15 列为固定列，其余按需追加（`IsMinion` 追加后落在第 16 列）。</summary>
    private static string Header(params string[] extraColumns)
    {
        string[] fixedColumns =
        {
            "id", "Name", "MAX_HP", "Ini_Attack", "Ini_Defend",
            "Intention1", "Intention2", "Intention3", "Intention4", "Intention5",
            "Intention6", "Intention7", "Intention8", "Intention9", "Intention10",
        };

        return string.Join(",", fixedColumns.Concat(extraColumns));
    }

    /// <summary>按真实行形状拼一行：前 5 列为属性，意图依次落到 `Intention1..N`，其余意图列留空，末尾补 `IsMinion` 值。</summary>
    private static string Row(int id, string name, int maxHp, int attack, int defend, string[] intentions, string isMinionValue)
    {
        List<string> fields = new List<string> { id.ToString(), name, maxHp.ToString(), attack.ToString(), defend.ToString() };
        fields.AddRange(intentions);
        while (fields.Count < 15)
        {
            fields.Add(string.Empty);
        }

        fields.Add(isMinionValue);
        return string.Join(",", fields);
    }

    private static string[] Split(string line) => line.Split(',');

    [Fact]
    public void ResolveIsMinionColumnIndex_LocatesColumnByHeaderName()
    {
        Assert.Equal(IsMinionColumnIndex, MonsterCsvSchema.ResolveIsMinionColumnIndex(Split(Header("IsMinion"))));
        Assert.Equal(IsMinionColumnIndex, MonsterCsvSchema.ResolveIsMinionColumnIndex(Split(Header("是否爪牙"))));
        Assert.Equal(IsMinionColumnIndex, MonsterCsvSchema.ResolveIsMinionColumnIndex(Split(Header(" isminion "))));

        // 旧表（追加本列之前）没有这一列 → 全部按非爪牙。
        Assert.Equal(-1, MonsterCsvSchema.ResolveIsMinionColumnIndex(Split(Header())));
        Assert.Equal(-1, MonsterCsvSchema.ResolveIsMinionColumnIndex(null));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("是", true)]
    [InlineData("爪牙", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("no", false)]
    [InlineData("否", false)]
    [InlineData("-", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void TryParseIsMinion_RecognizesToggleTokens(string raw, bool expected)
    {
        string[] fields = Split(Row(9001, "爪牙探针", 8, 2, 1, new[] { "1", "2" }, raw));

        Assert.True(MonsterCsvSchema.TryParseIsMinion(fields, IsMinionColumnIndex, out bool isMinion, out string error));
        Assert.Equal(expected, isMinion);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryParseIsMinion_UnrecognizedValue_FallsBackToFalseWithReason()
    {
        string[] fields = Split(Row(9001, "爪牙探针", 8, 2, 1, new[] { "1" }, "也许"));

        Assert.False(MonsterCsvSchema.TryParseIsMinion(fields, IsMinionColumnIndex, out bool isMinion, out string error));
        Assert.False(isMinion);
        Assert.Contains(MonsterCsvSchema.IsMinionColumnName, error);
    }

    [Fact]
    public void TryParseIsMinion_MissingColumnOrMissingField_DefaultsToFalse()
    {
        string[] row = Split(Row(9001, "爪牙探针", 8, 2, 1, new[] { "1" }, "1"));

        // 列不存在（旧表）＝ 该表不记爪牙。
        Assert.True(MonsterCsvSchema.TryParseIsMinion(row, -1, out bool noColumn, out _));
        Assert.False(noColumn);

        // 行末没写到这一列（尾列省略）＝ 非爪牙，不抛异常。
        string[] fullRow = Split(Row(9004, "缺列探针", 9, 1, 1, new[] { "1" }, "1"));
        string[] shortRow = fullRow.Take(fullRow.Length - 1).ToArray();
        Assert.Equal(IsMinionColumnIndex, shortRow.Length); // 15 列：正好少了 `IsMinion`
        Assert.True(MonsterCsvSchema.TryParseIsMinion(shortRow, IsMinionColumnIndex, out bool missingField, out _));
        Assert.False(missingField);

        Assert.True(MonsterCsvSchema.TryParseIsMinion(null, IsMinionColumnIndex, out bool nullFields, out _));
        Assert.False(nullFields);
    }

    [Fact]
    public void Monster_TemplateCarriesIsMinionThroughEveryCopyPath()
    {
        // 表模板（战利品侧 `GetValuePointsForMonster` 按 id 取的是它）与战场实例
        // （`BattlefieldSession` 用的 `new MonsterInstance(模板)`）都必须带标记。
        Monster template = new Monster(9001, "爪牙探针", 8, 2, 1, null, isMinion: true);
        Assert.True(template.IsMinion);

        Monster copy = new Monster(template);
        Assert.True(copy.IsMinion);

        MonsterInstance instance = new MonsterInstance(template);
        Assert.True(instance.IsMinion);
        Assert.Equal(9001, instance.id);
    }

    [Fact]
    public void Monster_DefaultsToNotMinion()
    {
        // 旧数据（无 `IsMinion` 列）走的就是这些路径 → 一律非爪牙，不改变现有折损结果。
        Assert.False(new Monster(9002, "普通探针", 12, 3, 1).IsMinion);
        Assert.False(new Monster(9002, "普通探针", 12, 3, 1, null, false).IsMinion);
        Assert.False(new MonsterInstance(new Monster(9002, "普通探针", 12, 3, 1)).IsMinion);
    }

    [Fact]
    public void Monster_IntentTableSurvivesCopyConstruction()
    {
        int[][][] table =
        {
            new[] { new[] { 1, 2 }, new[] { 1, 2 } },
            new[] { new[] { 2 } },
        };

        Monster copy = new Monster(new Monster(9001, "爪牙探针", 8, 2, 1, table, true));

        Assert.Equal(2, copy.Table.Length);
        Assert.Equal(new[] { 1, 2 }, copy.Table[0][0]);
        Assert.Equal(new[] { 1, 2 }, copy.Table[0][1]);
        Assert.Equal(new[] { 2 }, copy.Table[1][0]);
        Assert.True(copy.IsMinion);
    }

    [Fact]
    public void GetDefeatRatio_DefeatedMinionStillExcludedFromBothSides()
    {
        // 存活非爪牙 1 只（分母 38.33）+ 被击败的爪牙 1 只：爪牙既不进分子也不进分母 → 比例 0。
        List<MonsterValuePoints.EnemyValueSample> samples = new List<MonsterValuePoints.EnemyValueSample>
        {
            new MonsterValuePoints.EnemyValueSample(3115, false, false),
            new MonsterValuePoints.EnemyValueSample(3116, true, true),
        };

        double ratio = MonsterValuePoints.GetDefeatRatio(samples, monsterId => monsterId == 3115 ? 38.3333 : 18.3333);

        Assert.Equal(0.0, ratio, 4);
        Assert.Equal(1, MonsterValuePoints.GetCardRewardCount(ratio));
    }
}
