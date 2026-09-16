using System.Collections.Generic;

/// <summary>Transport-only API request and response contracts.</summary>
public sealed class BattleCommandRequest
{
    public string Type { get; set; }
    public int UnitId { get; set; }
    public int CardId { get; set; }
    public int Q { get; set; }
    public int R { get; set; }
    public bool HasTarget { get; set; }
    public List<ApiHex> Path { get; set; }
    public string InstanceId { get; set; }
    public int Slot { get; set; }
    public string Hand { get; set; }
    public bool FromCurrentCell { get; set; }
    public string Name { get; set; }
    public string ResponseMode { get; set; }
    public string Detail { get; set; }
    public int Radius { get; set; }
    public long AfterEventId { get; set; }
    public int Limit { get; set; }
    public string CaptureMode { get; set; }
}

public sealed class ApiHex { public int Q { get; set; } public int R { get; set; } }

public sealed class BattleCommandResult
{
    public bool Ok { get; set; }
    public string ErrorCode { get; set; }
    public string Message { get; set; }
    public object Data { get; set; }
    public long StateVersion { get; set; }
    public static BattleCommandResult Success(string message, object data) => new() { Ok = true, Message = message, Data = data };
    public static BattleCommandResult Fail(string code, string message, object data = null) => new() { Ok = false, ErrorCode = code, Message = message, Data = data };
}
