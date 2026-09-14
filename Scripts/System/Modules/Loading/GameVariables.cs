using System;
using System.Collections.Generic;

public sealed class GameVariables
{
    public int DefaultEnergyPerTurn { get; private set; } = 3;
    public int DefaultDrawCardsPerTurn { get; private set; } = 5;
    private readonly Dictionary<int, int> movesPerTurn = new();

    public int GetMovesPerTurn(int characterId) => movesPerTurn.TryGetValue(characterId, out int value) ? value : 0;

    public static GameVariables Load()
    {
        string[] lines = LoadCsv.LoadCSVDataLines(LoadingSystem.GetFilePathByKey("Data.Game.Variables"));
        var result = new GameVariables();
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] fields = LoadCsv.ParseCSVFields(line);
            if (fields.Length == 0 || string.Equals(fields[0], "Scope", StringComparison.OrdinalIgnoreCase)) continue;
            string scope = fields[0].Trim();
            if (string.Equals(scope, "Global", StringComparison.OrdinalIgnoreCase))
            {
                if (fields.Length < 4 || !int.TryParse(fields[2], out int energy) || !int.TryParse(fields[3], out int draw) || energy < 0 || draw < 0)
                    throw new FormatException($"游戏变量全局行无效：{line}");
                result.DefaultEnergyPerTurn = energy; result.DefaultDrawCardsPerTurn = draw;
            }
            else if (string.Equals(scope, "Character", StringComparison.OrdinalIgnoreCase))
            {
                if (fields.Length < 5 || !int.TryParse(fields[1], out int id) || !int.TryParse(fields[4], out int moves) || moves < 0)
                    throw new FormatException($"游戏变量角色行无效：{line}");
                result.movesPerTurn[id] = moves;
            }
            else throw new FormatException($"游戏变量 Scope 无效：{line}");
        }
        return result;
    }
}
