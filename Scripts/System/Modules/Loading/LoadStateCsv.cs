using Godot;
using System;
using System.Collections.Generic;
using CardSimulator;

[GlobalClass]
public partial class LoadStateCsv : Node
{
	private const int MinFieldCount = 5;

	public static StateDefinition[] LoadStatesFromCSV(string filePath)
	{
		string[] dataLines = LoadCsv.LoadCSVDataLines(filePath);
		if (dataLines.Length == 0)
		{
			GD.Print($"No state data found in {filePath}");
			return Array.Empty<StateDefinition>();
		}

		List<StateDefinition> definitions = new List<StateDefinition>();
		foreach (string line in dataLines)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			StateDefinition definition = ParseStateFromCSVLine(line);
			if (definition != null)
			{
				definitions.Add(definition);
			}
		}

		GD.Print($"Successfully loaded {definitions.Count} states from {filePath}");
		return definitions.ToArray();
	}

	private static StateDefinition ParseStateFromCSVLine(string line)
	{
		try
		{
			string[] fields = LoadCsv.ParseCSVFields(line);
			if (fields.Length < MinFieldCount)
			{
				GD.PrintErr($"Invalid state CSV format. Expected at least {MinFieldCount} fields, got {fields.Length}");
				return null;
			}

			if (!int.TryParse(fields[0], out int rawStateType))
			{
				GD.PrintErr($"Invalid state type value: {fields[0]}");
				return null;
			}

			StateType stateType = (StateType)rawStateType;
			if (!Enum.IsDefined(typeof(StateType), stateType))
			{
				GD.PrintErr($"Undefined StateType value: {rawStateType}");
				return null;
			}

			if (!TryParseBoolean(fields[2], out bool isStackable))
			{
				GD.PrintErr($"Invalid IsStackable value: {fields[2]}");
				return null;
			}

			if (!TryParseBoolean(fields[3], out bool isPermanent))
			{
				GD.PrintErr($"Invalid IsPermanent value: {fields[3]}");
				return null;
			}

			if (!int.TryParse(fields[4], out int turnStartDecayAmount) || turnStartDecayAmount < 0)
			{
				GD.PrintErr($"Invalid TurnStartDecayAmount value: {fields[4]}");
				return null;
			}

			bool isDebuff = false;
			bool isElite = false;

			if (fields.Length > 5 && !string.IsNullOrWhiteSpace(fields[5]) && !TryParseBoolean(fields[5], out isDebuff))
			{
				GD.PrintErr($"Invalid IsDebuff value: {fields[5]}");
				return null;
			}

			if (fields.Length > 6 && !string.IsNullOrWhiteSpace(fields[6]) && !TryParseBoolean(fields[6], out isElite))
			{
				GD.PrintErr($"Invalid IsElite value: {fields[6]}");
				return null;
			}

			return new StateDefinition(stateType, fields[1], isStackable, isPermanent, turnStartDecayAmount, isDebuff, isElite);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Error parsing state CSV line: {line}\nException: {ex.Message}");
			return null;
		}
	}

	private static bool TryParseBoolean(string rawValue, out bool result)
	{
		result = false;
		if (string.IsNullOrWhiteSpace(rawValue))
		{
			return false;
		}

		string normalized = rawValue.Trim();
		if (bool.TryParse(normalized, out result))
		{
			return true;
		}

		if (string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase))
		{
			result = true;
			return true;
		}

		if (string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase))
		{
			result = false;
			return true;
		}

		return false;
	}
}