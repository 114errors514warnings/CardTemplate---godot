using CardSimulator;

public sealed class StateDefinition
{
	public StateType Type { get; }
	public string Name { get; }
	public bool IsStackable { get; }
	public bool IsDebuff { get; }
	public bool IsElite { get; }
	public StateDecayTiming DecayTiming { get; }
	public StateDecayMode DecayMode { get; }
	public int StacksToRemove { get; }
	public string EffectDescription { get; }

	public StateDefinition(
		StateType type,
		string name,
		bool isStackable,
		StateDecayTiming decayTiming = StateDecayTiming.OnTurnStart,
		StateDecayMode decayMode = StateDecayMode.None,
		int stacksToRemove = 0,
		bool isDebuff = false,
		bool isElite = false,
		string effectDescription = null)
	{
		Type = type;
		Name = name ?? string.Empty;
		IsStackable = isStackable;
		IsDebuff = isDebuff;
		IsElite = isElite;
		DecayTiming = decayTiming;
		DecayMode = decayMode;
		StacksToRemove = stacksToRemove < 0 ? 0 : stacksToRemove;
		EffectDescription = effectDescription ?? string.Empty;
	}
}