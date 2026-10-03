using Godot;
using System.Collections.Generic;
using CardSimulator.Battlefield;

/// <summary>
/// 区域物 / 格点效果表加载器（2026-10-04，T3 + T8 / P2-30 后半）。
/// 表：`DataBase/Battlefield/AreaObject.csv`，列 `TrapId,Name,TriggerTiming,SideFilter,MaxTriggers,DecayTiming,EffectType,Params`。
///
/// 语义（列口径见 `README/功能说明文档/数据系统/数据配置/卡牌参数.md` §八 与 `AreaObjectSpec`）：
/// - `TriggerTiming`：`OnEnter` / `OnTurnEnd` / `OnEnter|OnTurnEnd`（缺省 = `OnEnter`）；
/// - `SideFilter`：`Triggerer` / `Enemy` / `Ally` / `All`（相对该效果的来源单位；缺省 = `Triggerer`）；
/// - `MaxTriggers`：正整数 = 触发几次后消失，`0` = 无限次；
/// - `DecayTiming`：`Never` / `OnTurnEnd`（回合末结算后层数 −1）；
/// - `EffectType` / `Params`：与卡表同一套语法（`|` 分效果 / 分组，`;` 分参）。
///
/// 只做「文件 → 行数组」的读取；解析与合并交给纯逻辑类 `AreaObjectCatalog`（可被纯 .NET 单测覆盖）。
/// </summary>
[GlobalClass]
public partial class LoadAreaObjectCsv : Node
{
	/// <summary>读取全部数据行（自动跳过表头；表头判定用 <c>LoadCSVLines</c>，<c>LoadCSVDataLines</c> 会跳表头）。</summary>
	public static List<string[]> LoadRowsFromCSV(string filePath)
	{
		List<string[]> rows = new List<string[]>();
		string[] allLines = LoadCsv.LoadCSVLines(filePath);
		if (allLines == null || allLines.Length == 0)
		{
			GD.PrintErr($"[AreaObject] 未读到区域物表（{filePath}）—— 进入触发会回落到旧的「3 点伤害」兜底。");
			return rows;
		}

		bool passedHeader = false;
		foreach (string line in allLines)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			string[] fields = LoadCsv.ParseCSVFields(line);
			if (!passedHeader && AreaObjectCatalog.IsAreaObjectTableHeader(fields))
			{
				passedHeader = true;
				continue;
			}

			rows.Add(fields);
		}

		GD.Print($"[AreaObject] 读到 {rows.Count} 行区域物定义（{filePath}）。");
		return rows;
	}
}
