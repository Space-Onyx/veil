namespace Content.Server._Onyx.Telecommunications.Components;

[RegisterComponent]
public sealed partial class TelecomTrafficConsoleComponent : Component
{
    [DataField]
    public EntityUid? SelectedServer;

    [DataField]
    public float UpdateAccumulator;
}
