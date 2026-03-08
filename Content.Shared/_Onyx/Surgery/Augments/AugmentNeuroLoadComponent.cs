using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent]
public sealed partial class AugmentNeuroLoadComponent : Component
{
    [DataField]
    public float PassiveLoad = 0f;
}
