namespace Content.Server._Onyx.Elevator;

[RegisterComponent]
public sealed partial class ElevatorDoorComponent : Component
{
    [DataField]
    public string ElevatorId = "";

    [DataField]
    public string Floor = "";
}