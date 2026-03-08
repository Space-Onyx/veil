using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentNeuroInterfaceComponent : Component
{
    [DataField, AutoNetworkedField]
    public string InterfaceCode = string.Empty;

    [DataField]
    public float MaxNeuroLoad = 20f;
}

[RegisterComponent]
public sealed partial class AugmentNeuroConfigurableComponent : Component;
