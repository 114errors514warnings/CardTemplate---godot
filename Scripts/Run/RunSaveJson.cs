// RunSaveJson.cs
// 存档 JSON 序列化（纯逻辑，无 Godot 依赖，便于 xUnit 单测）。
// System.Text.Json 默认不序列化 public 字段 → 这里显式 IncludeFields，
// 保证 CurrentNodeId / Seed / HP / 卡牌级数 / GameMode 等字段全部落盘。
using System.Text.Json;

public static class RunSaveJson
{
	public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		IncludeFields = true,
		WriteIndented = true,
	};

	public static string Serialize(RunSaveData data)
	{
		return data == null ? string.Empty : JsonSerializer.Serialize(data, Options);
	}

	public static RunSaveData Deserialize(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		return JsonSerializer.Deserialize<RunSaveData>(json, Options);
	}
}
