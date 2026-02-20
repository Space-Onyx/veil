using Robust.Shared.GameStates;

namespace Content.Server._Onyx.Elevator;

[RegisterComponent]
public sealed partial class ElevatorPointComponent : Component
{
    [DataField]
    public string FloorId = "";
}