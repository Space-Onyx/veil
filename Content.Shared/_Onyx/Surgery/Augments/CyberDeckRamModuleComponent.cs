using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent]
public sealed partial class CyberDeckRamModuleComponent : Component
{
    [DataField]
    public float RamIncrease = 4f;
}
