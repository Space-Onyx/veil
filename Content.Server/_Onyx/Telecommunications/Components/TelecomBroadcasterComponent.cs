namespace Content.Server._Onyx.Telecommunications.Components;

/// <summary>
/// Broadcasts radio traffic that has passed through the telecommunications chain.
/// </summary>
[RegisterComponent]
public sealed partial class TelecomBroadcasterComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int MaxServers = 2;

    [ViewVariables(VVAccess.ReadOnly)]
    public bool Sabotaged;
}
