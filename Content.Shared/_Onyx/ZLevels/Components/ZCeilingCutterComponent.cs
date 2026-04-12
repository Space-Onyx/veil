using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.ZLevels.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ZCeilingCutterComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool CeilingMode;
}
