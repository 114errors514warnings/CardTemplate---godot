// GameVariablesTableTests.cs
// 全局数据表（`DataBase/GameVariables.csv`）的**文件级**校验：
//   · 第 6 列 `MoveTimePointCost`（2026-10-05：世界地图「节点 → 相邻节点」的移动时间点进程从代码常量迁到本表）；
//   · 第 7 / 8 列 `VillageOperationTimePointCost` / `ForestForageTimePointCost`（2026-10-05 第四轮：村庄设施
//     「每次操作」与「树林搜寻」的时间点代价进表，两项**分开配**；用户口径「数值配置到全局数据表里」）。
// 纯 .NET 读真实 CSV（Tests.csproj 已把它拷进输出目录），不触碰 LoadingSystem / LoadCsv（Godot 依赖）；
// 运行期接线（`GameVariables.MoveTimePointCost → MapScene` 移动闸门、
// `GameVariables.ApplyFacilityCosts() → RunFacilityCosts → VillageForage / SmithyCrafting /
// RestaurantTrade`）由地图流程（`MapScene._Ready`）覆盖 —— `RunFacilityCostsTests` 覆盖表值落地后的纯逻辑取值。
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

public class GameVariablesTableTests
{
	/// <summary>默认值（表里留空时的兜底，= `RunTimePoints.MoveCost`）。</summary>
	private const double DefaultMoveTimePointCost = 0.3;

	/// <summary>村庄设施「每次操作」的兜底默认值（= 代码侧 `RunFacilityCosts.DefaultOperationCost` = 0.1）。</summary>
	private const double DefaultOperationTimePointCost = RunFacilityCosts.DefaultOperationCost;

	/// <summary>树林「单次搜寻」的兜底默认值（= `RunFacilityCosts.DefaultForestForageCost` = 1.0）。</summary>
	private const double DefaultForestForageTimePointCost = RunFacilityCosts.DefaultForestForageCost;

	/// <summary>时间点的最小计量单位（0.1）：表里任何时间点数值都必须对齐它。</summary>
	private const double TimePointStep = 0.1;

	private static string[] ReadTable()
	{
		string path = Path.Combine(AppContext.BaseDirectory, "GameVariables.csv");
		Assert.True(File.Exists(path), $"缺表：{path}（检查 Tests.csproj 的 None Include 是否拷贝）");
		return File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
	}

	private static string[] Cells(string line) => line.Split(',');

	private static string Cell(string[] cells, int index) => index >= 0 && index < cells.Length ? cells[index].Trim() : string.Empty;

	[Fact]
	public void Header_CarriesTheThreeTimePointCostColumns()
	{
		string[] header = Cells(ReadTable()[0]).Select(x => x.Trim()).ToArray();
		Assert.Equal(
			new[]
			{
				"Scope", "CharacterId", "DefaultEnergyPerTurn", "DefaultDrawCardsPerTurn", "MovesPerTurn",
				"MoveTimePointCost", "VillageOperationTimePointCost", "ForestForageTimePointCost",
			},
			header);
	}

	[Fact]
	public void GlobalRow_CarriesParsableTimePointCosts()
	{
		string[] rows = ReadTable();
		string[] global = rows.Select(Cells).First(cells => Cell(cells, 0) == "Global");
		Assert.Equal("4", Cell(global, 2));
		Assert.Equal("5", Cell(global, 3));

		double move = ReadCostCell(global, 5, "MoveTimePointCost", DefaultMoveTimePointCost);
		double operation = ReadCostCell(global, 6, "VillageOperationTimePointCost", DefaultOperationTimePointCost);
		double forage = ReadCostCell(global, 7, "ForestForageTimePointCost", DefaultForestForageTimePointCost);

		// 当前口径（2026-10-05 第四轮）：移动 0.3 / 村庄每次操作 0.1 / 树林每次搜寻 1.0（独立项）。
		Assert.Equal(0.3, move, 5);
		Assert.Equal(0.1, operation, 5);
		Assert.Equal(1.0, forage, 5);
		Assert.NotEqual(operation, forage, 5);   // 树林与村庄其他操作**分开配**（用户口径：树林耗时不同）
	}

	[Fact]
	public void CharacterRows_LeaveTimePointCostColumnsEmpty()
	{
		string[] rows = ReadTable();
		Assert.Contains(rows, row => Cell(Cells(row), 0) == "Character");
		foreach (string row in rows.Where(row => Cell(Cells(row), 0) == "Character"))
		{
			Assert.True(string.IsNullOrEmpty(Cell(Cells(row), 5)), $"角色行不该填 MoveTimePointCost（只有 Global 行可填）：{row}");
			Assert.True(string.IsNullOrEmpty(Cell(Cells(row), 6)), $"角色行不该填 VillageOperationTimePointCost（只有 Global 行可填）：{row}");
			Assert.True(string.IsNullOrEmpty(Cell(Cells(row), 7)), $"角色行不该填 ForestForageTimePointCost（只有 Global 行可填）：{row}");
		}
	}

	/// <summary>
	/// 读一列时间点代价：不能留空（留空 = 静默回落兜底，表就不再是唯一来源）、必须是 &gt; 0 的有限数、
	/// 必须对齐 0.1 步长、且等于代码侧兜底默认值（口径一致性）。返回解析出的数值。
	/// </summary>
	private static double ReadCostCell(string[] cells, int index, string name, double expected)
	{
		string text = Cell(cells, index);
		Assert.False(string.IsNullOrEmpty(text), $"全局行的 {name} 不能留空。");
		Assert.True(
			float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value),
			$"{name} 不是数：{text}");
		Assert.True(value > 0f && !float.IsNaN(value) && !float.IsInfinity(value), $"{name} 必须是 > 0 的有限数：{text}");

		// 与时间点最小计量单位 0.1 对齐（0.3 = 3 × 0.1、0.1 = 1 × 0.1、1.0 = 10 × 0.1）。
		double steps = (double)value / TimePointStep;
		Assert.Equal(0d, Math.Abs(steps - Math.Round(steps)), 4);
		Assert.Equal(expected, (double)value, 5);
		return value;
	}
}
