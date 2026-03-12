using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent]
public sealed partial class NeuroLoadLimitModifierComponent : Component
{
    [DataField]
    public float MaxNeuroLoad = 0f;
}
