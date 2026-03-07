using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentNeuroInterfaceComponent : Component
{
    [DataField, AutoNetworkedField]
    public string InterfaceCode = string.Empty;
}

[RegisterComponent]
public sealed partial class AugmentNeuroConfigurableComponent : Component;

