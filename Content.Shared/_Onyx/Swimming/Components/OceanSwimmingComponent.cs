using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Swimming.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class OceanSwimmingComponent : Component
{
    [ViewVariables]
    public TimeSpan NextStroke;

    [ViewVariables]
    public TimeSpan StrokeUntil;

    [ViewVariables]
    public TimeSpan NextDrowningDamage;

    [ViewVariables]
    public bool StaminaExhausted;

    [ViewVariables]
    public string? SuspendedSprintDrainKey;

    [ViewVariables]
    public float SuspendedSprintDrainRate;
}
