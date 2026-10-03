// CardTableFileTests.cs
// 卡牌表的**文件级**校验（2026-10-04，T1 批 / 代码需求清单 P2-29）。
// 直接在纯 .NET 测试里读真实 CSV（Tests.csproj 已把它们拷进输出目录），用 CardSpatialSpecCatalog /
// CardSpatialSpec 的纯逻辑方法解析：表头 / CardId 升序与跨表查重 / 空间列能否解出战场需要的规格。
// 用意：把「配表写了、程序读不到」的**静默退化**变成红灯（P2-29 就是这么漏掉的）。
// 说明：**不**触碰 LoadingSystem / LoadCsv / BattleCardSpatialRepository（Godot 依赖，测试工程没有 GodotSharp 引用），
// 因此运行期「表 → 内存字典」的接线仍由 `--battlefield-smoke` 覆盖。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CardSimulator;
using CardSimulator.Battlefield;
using Xunit;

public class CardTableFileTests
{
    private const string RewardPoolTable = "Card/CharacterRewardPool.csv";
    private const string CardTierColumn = "CardTier";

    private static string[] ReadTable(string relativePath)
    {
        string path = Path.Combine(AppContext.BaseDirectory, relativePath);
        Assert.True(File.Exists(path), $"缺表：{path}（检查 Tests.csproj 的 None Include 是否拷贝）");
        return File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
    }

    private static string[] Cells(string line) => line.Split(',');

    private static List<string[]> DataRows(string relativePath) => ReadTable(relativePath).Skip(1).Select(Cells).ToList();

    private static CardSpatialSpec FindSpec(string relativePath, int cardId) =>
        DataRows(relativePath).Select(CardSpatialSpec.ParseFields).First(x => x.CardId == cardId);

    // ── 表结构：表头 / 升序 / 表内不重复 ──

    [Theory]
    [InlineData("Card/通用/通用Card.csv", 8)]
    [InlineData("Card/勇士Card.csv", 12)]
    [InlineData("Card/精灵Card.csv", 13)]
    [InlineData("Card/法师Card.csv", 13)]
    [InlineData("Card/重剑手Card.csv", 10)]
    public void CardTable_HeaderStartsWithCardId_AndIdsAreAscendingWithoutDuplicates(string relativePath, int headerColumnCount)
    {
        string[] rows = ReadTable(relativePath);
        Assert.Equal(headerColumnCount, Cells(rows[0]).Length);
        Assert.True(CardSpatialSpecCatalog.IsCardTableHeader(Cells(rows[0])), $"{relativePath} 的表头首列不是 CardId");

        List<int> ids = rows.Skip(1).Select(row => int.Parse(Cells(row)[0])).ToList();
        Assert.NotEmpty(ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(ids.OrderBy(x => x).ToList(), ids);
    }

    [Fact]
    public void CardIds_AreUniqueAcrossAllFiveTables()
    {
        string[] tables =
        {
            "Card/通用/通用Card.csv", "Card/勇士Card.csv", "Card/精灵Card.csv", "Card/法师Card.csv", "Card/重剑手Card.csv",
        };

        List<int> all = tables.SelectMany(table => DataRows(table).Select(row => int.Parse(row[0]))).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    // ── 空间列：两张新角色表必须解得出战场规格（P2-29 的验收）──

    [Fact]
    public void NewCharacterTables_SpatialColumnsParseToBattlefieldSpecs()
    {
        CardSpatialSpec arrow = FindSpec("Card/精灵Card.csv", 11003001);   // 穿林箭
        Assert.Equal(CardSpatialShape.Line, arrow.Shape);
        Assert.Equal(5, arrow.MaxRange);
        Assert.Equal(5, arrow.Length);
        Assert.True(arrow.Penetrates);

        CardSpatialSpec vine = FindSpec("Card/精灵Card.csv", 21003003);    // 古树哨卫
        Assert.Equal(CardSpatialShape.Trap, vine.Shape);
        Assert.Equal(1, vine.MaxRange);
        Assert.Equal("vine_sentinel", vine.TrapId);

        CardSpatialSpec meteor = FindSpec("Card/法师Card.csv", 11004001);  // 陨星投掷
        Assert.Equal(CardSpatialShape.Burst, meteor.Shape);
        Assert.Equal(4, meteor.MaxRange);
        Assert.Equal(1, meteor.Radius);

        CardSpatialSpec frost = FindSpec("Card/法师Card.csv", 21004003);   // 寒霜之地
        Assert.Equal(CardSpatialShape.Trap, frost.Shape);
        Assert.Equal(4, frost.MaxRange);
        Assert.Equal("frost_field", frost.TrapId);
    }

    [Fact]
    public void WarriorSpatialCards_KeepTheirConfiguredShapes()
    {
        Assert.Equal(CardSpatialShape.Fan, FindSpec("Card/勇士Card.csv", 11001002).Shape);   // 横扫

        CardSpatialSpec thrust = FindSpec("Card/勇士Card.csv", 11001003);                    // 烈闪突
        Assert.Equal(CardSpatialShape.Line, thrust.Shape);
        Assert.Equal(2, thrust.Length);
        Assert.Equal(WeaponAttackMode.Thrust, thrust.AttackMode);
        Assert.False(thrust.Penetrates);   // AttackMode=Thrust 强制关掉穿透

        CardSpatialSpec trap = FindSpec("Card/勇士Card.csv", 21001005);                      // 埋设陷阱
        Assert.Equal(CardSpatialShape.Trap, trap.Shape);
        Assert.Equal("test_trap", trap.TrapId);
    }

    [Fact]
    public void LegacyTables_WithoutSpatialColumns_StayShapeNone()
    {
        Assert.All(DataRows("Card/通用/通用Card.csv"), row => Assert.Equal(CardSpatialShape.None, CardSpatialSpec.ParseFields(row).Shape));
        Assert.All(DataRows("Card/重剑手Card.csv"), row => Assert.Equal(CardSpatialShape.None, CardSpatialSpec.ParseFields(row).Shape));
    }

    [Fact]
    public void OnlySpatialDeclaringTables_FeedTheBattlefieldSpatialCatalog()
    {
        // 锁住 BattleCardSpatialRepository.LoadAll 的准入条件：只有声明 SpatialShape 列的表才进空间规格字典。
        // 通用 / 重剑手表若被误当空间表，它们的攻击牌会拿到 Shape=None（而不是回落 Single）→ 出牌退化成「打自己」。
        // （另一半教训：LoadCsv.LoadCSVDataLines 会**跳过表头**，判断表类型必须用 LoadCSVLines。）
        Assert.True(CardSpatialSpecCatalog.IsSpatialCardTableHeader(Cells(ReadTable("Card/勇士Card.csv")[0])));
        Assert.True(CardSpatialSpecCatalog.IsSpatialCardTableHeader(Cells(ReadTable("Card/精灵Card.csv")[0])));
        Assert.True(CardSpatialSpecCatalog.IsSpatialCardTableHeader(Cells(ReadTable("Card/法师Card.csv")[0])));
        Assert.False(CardSpatialSpecCatalog.IsSpatialCardTableHeader(Cells(ReadTable("Card/通用/通用Card.csv")[0])));
        Assert.False(CardSpatialSpecCatalog.IsSpatialCardTableHeader(Cells(ReadTable("Card/重剑手Card.csv")[0])));
        Assert.False(CardSpatialSpecCatalog.IsSpatialCardTableHeader(Cells(ReadTable(RewardPoolTable)[0])));
    }

    // ── T2（P2-30 前半）：寒霜之地的「落物即结算」配置必须在表里 ──

    [Fact]
    public void FrostFieldRow_CarriesLandingEffect()
    {
        string[] row = DataRows("Card/法师Card.csv").First(cells => cells[0] == "21004003");

        string[] effectTypes = row[4].Split('|');
        Assert.Contains("Damage", effectTypes);
        Assert.Contains("AddState", effectTypes);

        int[][] segments = row[6].Split('|').Select(part => part.Split(';').Select(int.Parse).ToArray()).ToArray();
        Assert.Equal(2, segments.Length);
        Assert.Equal((int)EffectTargetType.SelectedTarget, segments[0][0]);   // 受击对象 = 目标格上的敌方单位（战场侧传入）
        Assert.Equal(0, segments[0][1]);                                      // 额外伤害 0 = 只打「一次攻击」
        Assert.Equal((int)EffectTargetType.SelectedTarget, segments[1][0]);
        Assert.Equal((int)StateType.Weak, segments[1][1]);
        Assert.Equal(1, segments[1][2]);
    }

    [Fact]
    public void VineSentinelRow_StaysLandingEffectFree()
    {
        string[] row = DataRows("Card/精灵Card.csv").First(cells => cells[0] == "21003003");
        Assert.Equal("None", row[4].Trim());   // 古树哨卫的「每次进入」触发属 P2-30 后半（区域物定义表），落物时不打
    }

    // ── 掉落池：精灵 / 法师的专有来源必须在表里（守住上一批修掉的「候选为空」）──

    [Fact]
    public void RewardPool_RegistersBothNewCharacterCardTables()
    {
        List<string[]> rows = DataRows(RewardPoolTable);
        List<string> SourcesOf(int characterId) => rows.Where(cells => cells[0].Trim() == characterId.ToString())
            .Select(cells => cells[1].Trim()).ToList();

        Assert.Contains("精灵Card.csv", SourcesOf(1003));
        Assert.Contains("法师Card.csv", SourcesOf(1004));
        Assert.All(rows, cells => Assert.False(string.IsNullOrWhiteSpace(cells[1])));
    }

    // ── T4（D5）：法术共鸣改用「全场同阵营（含自身）」的单一 AllAllies 段 ──

    [Fact]
    public void ArcaneResonanceRow_UsesSingleAllAlliesSegment()
    {
        string[] row = DataRows("Card/法师Card.csv").First(cells => cells[0] == "21004002");

        Assert.Equal("AddState", row[4].Trim());          // 不再写「Self + AllAllies」两段（旧配法让自身叠 2 层）
        int[][] segments = row[6].Split('|').Select(part => part.Split(';').Select(int.Parse).ToArray()).ToArray();
        Assert.Single(segments);
        Assert.Equal((int)EffectTargetType.AllAllies, segments[0][0]);
        Assert.Equal((int)StateType.AddAttack, segments[0][1]);
        Assert.Equal(1, segments[0][2]);
    }

    // ── T6（D3）：林间抚慰的消耗成本必须写进 EffectType / Params ──

    [Fact]
    public void SoothingGroveRow_DeclaresHandCost()
    {
        string[] row = DataRows("Card/精灵Card.csv").First(cells => cells[0] == "21003002");

        Assert.Contains("ConsumeSelectedHandCard", row[4].Split('|'));
        Assert.Contains("Heal", row[4].Split('|'));
        Assert.StartsWith("1;1|", row[6]);   // 第 1 段 = 消耗 1 张手牌（目标类型 1 = 自身范围，张数 1）
    }

    // ── T9（D8）：等级列（CardTier）落表且只含 B–S；勇士表无等级源（拍板项，按不过滤处理）──

    [Theory]
    [InlineData("Card/精灵Card.csv", 7)]
    [InlineData("Card/法师Card.csv", 7)]
    [InlineData("Card/重剑手Card.csv", 30)]
    public void TieredTables_CarryCardTierColumnWithinBandBS(string relativePath, int expectedRows)
    {
        string[] rows = ReadTable(relativePath);
        string[] header = Cells(rows[0]);
        int tierColumn = Array.IndexOf(header.Select(x => x.Trim().TrimStart('\uFEFF')).ToArray(), CardTierColumn);
        Assert.True(tierColumn >= 0, $"{relativePath} 缺 {CardTierColumn} 列");

        List<string[]> data = rows.Skip(1).Select(Cells).ToList();
        Assert.Equal(expectedRows, data.Count);
        Assert.All(data, cells =>
        {
            Assert.True(tierColumn < cells.Length, $"{cells[0]} 没有等级列的值");
            string tier = cells[tierColumn].Trim();
            Assert.Contains(tier, new[] { "B", "A", "S" });   // 角色的掉落候选本来就只有 B–S
        });
    }

    [Fact]
    public void WarriorTable_StillHasNoTierSource()
    {
        // 勇士（1001）没有设计等级数据源（拍板项 12）：缺列 → CardTier.None → 掉落过滤不拦截（并打一次告警）。
        string[] header = Cells(ReadTable("Card/勇士Card.csv")[0]);
        Assert.DoesNotContain(CardTierColumn, header.Select(x => x.Trim()));
    }

    [Fact]
    public void TierEligibility_CutsCAndD_KeepsBSAndUnknown()
    {
        Assert.False(CardRewardOwnership.IsTierEligibleForReward(CardTier.C));
        Assert.False(CardRewardOwnership.IsTierEligibleForReward(CardTier.D));
        Assert.True(CardRewardOwnership.IsTierEligibleForReward(CardTier.B));
        Assert.True(CardRewardOwnership.IsTierEligibleForReward(CardTier.A));
        Assert.True(CardRewardOwnership.IsTierEligibleForReward(CardTier.S));
        Assert.True(CardRewardOwnership.IsTierEligibleForReward(CardTier.None));   // 缺等级数据不静默丢牌
    }

    // ── 落表：精灵 / 法师各 3 张旧设计牌（2026-10-04 补落运行时表）──

    [Theory]
    [InlineData("Card/精灵Card.csv", 21003004, "净化", "B")]
    [InlineData("Card/精灵Card.csv", 21003005, "水源", "B")]
    [InlineData("Card/精灵Card.csv", 21003006, "通灵", "S")]
    [InlineData("Card/法师Card.csv", 21004004, "咒术准备", "B")]
    [InlineData("Card/法师Card.csv", 21004005, "法力涌流", "B")]
    [InlineData("Card/法师Card.csv", 21004006, "法术结界", "S")]
    public void LegacyDesignCards_ArePortedIntoRuntimeTables(string relativePath, int cardId, string cardName, string tier)
    {
        string[] rows = ReadTable(relativePath);
        int tierColumn = Array.IndexOf(Cells(rows[0]).Select(x => x.Trim()).ToArray(), CardTierColumn);
        string[] row = rows.Skip(1).Select(Cells).First(cells => cells[0] == cardId.ToString());

        Assert.Equal(cardName, row[1]);
        Assert.Equal(tier, row[tierColumn].Trim());
        Assert.True(int.TryParse(row[3], out _));   // 费用
    }
}
