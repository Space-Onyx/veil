using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CyberDeckComponent : Component
{
    [DataField]
    public float BaseMaxRam = 8f;

    [DataField, AutoNetworkedField]
    public float MaxRam = 8f;

    [DataField, AutoNetworkedField]
    public float CurrentRam = 8f;

    [DataField]
    public float RamRegenTime = 3f;

    [DataField]
    public float RegenAccumulator;
}
