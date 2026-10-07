// InteractPointTableTests.cs
// 交互点定义表（`DataBase/Level/InteractPoint.csv`）的文件级 + 解析级校验。
// 口径出处：README/功能说明文档/数据系统/数据配置/配表规范.md §「交互点定义表」（2026-10-06）、
//   README/施工文档/2026/2026.10/10月施工文档.md §33.2.1（地点内一切可交互单位都是一个类型）。
// 这一份测试锁两件事：① 首个落地版的 8 行取值与村庄 / 商人案一致；
//   ② 坏表（表头错 / 列数错 / DefinitionId 重复或带空格 / Trigger 写错值）**拦得住、不静默降级**。
using System;
using System.IO;
using System.Linq;
using CardSimulator.Battlefield;
using Xunit;

public sealed class InteractPointTableTests
{
    private static string[] TableLines()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Level", "InteractPoint.csv");
        return File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
    }

    [Fact]
    public void First_landing_table_has_the_eight_definitions()
    {
        var table = InteractPointCatalog.Parse(TableLines());

        Assert.Equal(
            new[] { "Entrance", "Exit", "Inn", "Guesthouse", "Smithy", "Restaurant", "Forest", "Merchant" },
            table.Select(x => x.DefinitionId).ToArray());
        Assert.All(table, row => Assert.False(string.IsNullOrWhiteSpace(row.Name)));
    }

    [Fact]
    public void Trigger_and_ui_values_match_the_design()
    {
        var table = InteractPointCatalog.Parse(TableLines());

        // 入口格 / 离开格：可通行、踏入触发，但自己不开界面（离开 = 关卡完成，由视图判定）。
        Assert.Equal(InteractTriggerKind.Door, InteractPointCatalog.Find(table, "Entrance").Trigger);
        Assert.Equal(InteractPointCatalog.UiNone, InteractPointCatalog.Find(table, "Entrance").Ui);
        Assert.Equal(InteractTriggerKind.Door, InteractPointCatalog.Find(table, "Exit").Trigger);
        Assert.Equal(InteractPointCatalog.UiNone, InteractPointCatalog.Find(table, "Exit").Ui);

        // 5 个村庄设施：门口格触发，各自开 tips 或专用界面。
        foreach (string id in new[] { "Inn", "Guesthouse", "Forest" })
        {
            Assert.Equal(InteractTriggerKind.Door, InteractPointCatalog.Find(table, id).Trigger);
            Assert.Equal(id, InteractPointCatalog.Find(table, id).Ui);
        }

        Assert.Equal("Smithy", InteractPointCatalog.Find(table, "Smithy").Ui);
        Assert.Equal("Restaurant", InteractPointCatalog.Find(table, "Restaurant").Ui);

        // 商人：走到相邻格自动触发（没有门口格）。
        Assert.Equal(InteractTriggerKind.Adjacent, InteractPointCatalog.Find(table, "Merchant").Trigger);
        Assert.Equal("Merchant", InteractPointCatalog.Find(table, "Merchant").Ui);
        Assert.Equal("商人", InteractPointCatalog.Find(table, "Merchant").Name);
    }

    [Fact]
    public void Find_is_case_insensitive_and_missing_id_returns_null()
    {
        var table = InteractPointCatalog.Parse(TableLines());
        Assert.NotNull(InteractPointCatalog.Find(table, "inn"));
        Assert.Null(InteractPointCatalog.Find(table, "NoSuchPoint"));
    }

    [Fact]
    public void Bad_header_is_rejected()
    {
        var lines = new[]
        {
            "DefinitionId,Name,Trigger,Ui,TimePointCost,GoldCost",
            "Inn,旅馆,Door,Inn,,,",
        };
        Assert.Throws<FormatException>(() => InteractPointCatalog.Parse(lines));
    }

    [Fact]
    public void Duplicate_definition_id_is_rejected()
    {
        var lines = new[]
        {
            "DefinitionId,Name,Trigger,Ui,TimePointCost,GoldCost,Params",
            "Inn,旅馆,Door,Inn,,,",
            "Inn,旅馆2,Door,Inn,,,",
        };
        Assert.Throws<FormatException>(() => InteractPointCatalog.Parse(lines));
    }

    [Fact]
    public void Definition_id_with_space_or_blank_is_rejected()
    {
        Assert.Throws<FormatException>(() => InteractPointCatalog.Parse(new[]
        {
            "DefinitionId,Name,Trigger,Ui,TimePointCost,GoldCost,Params",
            "Inn House,旅馆,Door,Inn,,,",
        }));
        Assert.Throws<FormatException>(() => InteractPointCatalog.Parse(new[]
        {
            "DefinitionId,Name,Trigger,Ui,TimePointCost,GoldCost,Params",
            ",旅馆,Door,Inn,,,",
        }));
    }

    [Fact]
    public void Unknown_trigger_is_rejected()
    {
        Assert.Throws<FormatException>(() => InteractPointCatalog.Parse(new[]
        {
            "DefinitionId,Name,Trigger,Ui,TimePointCost,GoldCost,Params",
            "Inn,旅馆,Walk,Inn,,,",
        }));
    }

    [Fact]
    public void Wrong_column_count_is_rejected()
    {
        Assert.Throws<FormatException>(() => InteractPointCatalog.Parse(new[]
        {
            "DefinitionId,Name,Trigger,Ui,TimePointCost,GoldCost,Params",
            "Inn,旅馆,Door,Inn",
        }));
    }

    [Fact]
    public void Missing_header_is_rejected()
    {
        Assert.Throws<FormatException>(() => InteractPointCatalog.Parse(Array.Empty<string>()));
    }

    [Fact]
    public void Blank_trigger_cell_means_none()
    {
        var table = InteractPointCatalog.Parse(new[]
        {
            "DefinitionId,Name,Trigger,Ui,TimePointCost,GoldCost,Params",
            "Marker,标记,,None,,,",
        });
        Assert.Equal(InteractTriggerKind.None, table[0].Trigger);
    }
}
